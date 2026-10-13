using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CageFightBookTool
{
    /// <summary>预览的解锁模拟模式：让策划看到「玩家没解锁时」和「解锁但没拥有（灰白）」的样子。</summary>
    public enum BookPreviewUnlockMode
    {
        AllUnlockedOwned = 0,
        AllUnlockedUnowned = 1,
        AllLocked = 2,
        HalfProgress = 3
    }

    /// <summary>
    /// 游戏内预览：
    /// 1) 图鉴页 —— 复刻 BookController 的 12 格 + 页签 + L/R 翻页 + 未解锁盖锁 + 灰白未拥有；
    /// 2) Card —— 用 IMGUI 按数据画一张卡（稳定可靠）；
    /// 3) 真实 Card 渲染 —— 把 CDprofab/Card.prefab 实例化到预览场景，用相机渲进 RenderTexture（实验性）。
    /// </summary>
    public static class BookEditorPreview
    {
        public const int PageSize = 12;

        /// <summary>绘制图鉴页。返回被点击的条目下标（-1 表示没点）。</summary>
        public static int DrawGridPage(
            Rect area,
            BookEditorData data,
            List<string> tabLabels,          // 页签文案（已含条目数），最后一项可以是「未分类」
            ref int tabIndex,
            List<int> entryIndices,          // 当前页签下的条目下标（已按 star 排序）
            ref int pageIndex,
            int selectedIndex,
            Dictionary<int, List<BookIssue>> issueMap,
            BookPreviewUnlockMode unlockMode)
        {
            int clicked = -1;
            if (area.width < 40f || area.height < 40f)
                return -1;

            BookEditorUI.FillRect(area, BookEditorUI.PreviewBg);

            // ---------- 页签 ----------
            var tabRow = new Rect(area.x + 6f, area.y + 6f, area.width - 12f, 22f);
            if (tabLabels != null && tabLabels.Count > 0)
            {
                tabIndex = Mathf.Clamp(tabIndex, 0, tabLabels.Count - 1);
                float tabWidth = Mathf.Min(104f, (tabRow.width - (tabLabels.Count - 1) * 4f) / tabLabels.Count);
                for (int i = 0; i < tabLabels.Count; i++)
                {
                    var tabRect = new Rect(tabRow.x + i * (tabWidth + 4f), tabRow.y, tabWidth, tabRow.height);
                    bool active = i == tabIndex;
                    BookEditorUI.FillRect(tabRect, active ? BookEditorUI.Accent : new Color(1f, 1f, 1f, 0.10f));
                    GUI.Label(tabRect, tabLabels[i], EditorStyles.centeredGreyMiniLabel);

                    if (Event.current.type == EventType.MouseDown && tabRect.Contains(Event.current.mousePosition) && !active)
                    {
                        tabIndex = i;
                        pageIndex = 0;
                        Event.current.Use();
                    }
                }
            }

            // ---------- 格子 ----------
            var gridArea = new Rect(area.x + 6f, tabRow.yMax + 6f, area.width - 12f, area.height - tabRow.height - 44f);
            const int columns = 4;
            const int rows = 3;
            const float gap = 5f;
            float slotW = (gridArea.width - gap * (columns - 1)) / columns;
            float slotH = (gridArea.height - gap * (rows - 1)) / rows;
            slotH = Mathf.Min(slotH, slotW * 1.35f);

            int pageStart = pageIndex * PageSize;
            int pageCount = Mathf.Max(1, (entryIndices.Count + PageSize - 1) / PageSize);

            for (int i = 0; i < PageSize; i++)
            {
                int col = i % columns;
                int row = i / columns;
                var slotRect = new Rect(
                    gridArea.x + col * (slotW + gap),
                    gridArea.y + row * (slotH + gap),
                    slotW,
                    slotH);

                int listIndex = pageStart + i;
                bool hasEntry = listIndex < entryIndices.Count;
                int entryIndex = hasEntry ? entryIndices[listIndex] : -1;
                BookEntry entry = hasEntry ? data.EntryAt(entryIndex) : null;

                bool unlocked = hasEntry && entry != null && IsUnlockedPreview(unlockMode, i, listIndex);
                bool owned = unlockMode != BookPreviewUnlockMode.AllUnlockedUnowned;

                DrawSlot(slotRect, entry, unlocked, owned, entryIndex == selectedIndex, entryIndex, issueMap, unlocked && owned);

                if (hasEntry && entry != null && Event.current.type == EventType.MouseDown && slotRect.Contains(Event.current.mousePosition))
                {
                    clicked = entryIndex;
                    Event.current.Use();
                }
            }

            // ---------- 翻页 ----------
            var footer = new Rect(area.x + 6f, area.yMax - 30f, area.width - 12f, 24f);
            var leftRect = new Rect(footer.x, footer.y, 54f, footer.height);
            var rightRect = new Rect(footer.xMax - 54f, footer.y, 54f, footer.height);
            if (GUI.Button(leftRect, "◀ 上一页") && pageIndex > 0)
                pageIndex--;
            if (GUI.Button(rightRect, "下一页 ▶") && pageIndex < pageCount - 1)
                pageIndex++;

            pageIndex = Mathf.Clamp(pageIndex, 0, pageCount - 1);
            GUI.Label(new Rect(footer.x + 60f, footer.y + 3f, footer.width - 120f, 20f),
                "第 " + (pageIndex + 1) + " / " + pageCount + " 页　共 " + entryIndices.Count + " 条",
                EditorStyles.centeredGreyMiniLabel);

            return clicked;
        }

        private static bool IsUnlockedPreview(BookPreviewUnlockMode mode, int slotIndex, int listIndex)
        {
            switch (mode)
            {
                case BookPreviewUnlockMode.AllLocked:
                    return false;
                case BookPreviewUnlockMode.HalfProgress:
                    return listIndex < 6;
                default:
                    return true;
            }
        }

        private static void DrawSlot(
            Rect rect,
            BookEntry entry,
            bool unlocked,
            bool owned,
            bool selected,
            int entryIndex,
            Dictionary<int, List<BookIssue>> issueMap,
            bool fullColor)
        {
            BookEditorUI.FillRect(rect, new Color(1f, 1f, 1f, 0.06f));

            if (selected)
                BookEditorUI.Frame(rect, BookEditorUI.Accent, 2f);
            else
                BookEditorUI.Frame(rect, new Color(1f, 1f, 1f, 0.14f), 1f);

            // 图标区
            var iconRect = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 26f);
            if (entry == null)
            {
                BookEditorUI.FillRect(iconRect, new Color(0f, 0f, 0f, 0.18f));
            }
            else
            {
                // 与游戏一致：按资源键取图（游戏只认资源键）
                Sprite sprite = BookEditorData.ResolveAvatarSprite(entry);

                Color tint = fullColor ? Color.white : new Color(0.62f, 0.62f, 0.62f, 1f);   // 与 BookController.unownedColor 一致
                BookEditorUI.DrawSpriteFit(iconRect, sprite, tint, 3f);

                if (!unlocked)
                {
                    BookEditorUI.FillRect(iconRect, new Color(0f, 0f, 0f, 0.66f));
                    GUI.Label(iconRect, "未解锁", EditorStyles.centeredGreyMiniLabel);
                }
            }

            // 名字 + 星级
            string label = entry == null ? "（空）" : (string.IsNullOrEmpty(entry.displayName) ? entry.id : entry.displayName);
            GUI.Label(new Rect(rect.x + 3f, rect.yMax - 24f, rect.width - 6f, 14f), label, EditorStyles.centeredGreyMiniLabel);
            BookEditorUI.DrawStars(new Rect(rect.x + 14f, rect.yMax - 9f, rect.width - 28f, 5f), entry != null ? entry.star : 0);

            // 问题角标
            if (entry != null && issueMap != null && entryIndex >= 0)
            {
                List<BookIssue> issues;
                if (issueMap.TryGetValue(entryIndex, out issues) && issues != null && issues.Count > 0)
                    BookEditorUI.FillRect(new Rect(rect.xMax - 9f, rect.y + 2f, 7f, 7f), WorstColor(issues));
            }
        }

        private static Color WorstColor(List<BookIssue> issues)
        {
            Color result = BookEditorUI.Muted;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i] == null)
                    continue;
                if (issues[i].Level == BookIssueLevel.Error)
                    return BookEditorUI.Error;
                if (issues[i].Level == BookIssueLevel.Warning)
                    result = BookEditorUI.Warn;
            }
            return result;
        }

        /// <summary>IMGUI 卡面预览：用条目数据按游戏内 Card 的信息层次画一张卡（不依赖预制体）。</summary>
        public static void DrawCardImGui(Rect area, BookEntry entry)
        {
            BookEditorUI.FillRect(area, BookEditorUI.PreviewBg);
            if (entry == null)
            {
                GUI.Label(area, "未选中条目", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            float padding = 10f;
            var card = new Rect(area.x + padding, area.y + padding, area.width - padding * 2f, area.height - padding * 2f);
            BookEditorUI.FillRect(card, new Color(0.96f, 0.93f, 0.85f, 1f));
            BookEditorUI.Frame(card, new Color(0.35f, 0.28f, 0.18f, 1f), 2f);

            float y = card.y + 8f;

            // 立绘
            float roleHeight = Mathf.Min(card.height * 0.42f, 190f);
            var roleRect = new Rect(card.x + 8f, y, card.width - 16f, roleHeight);
            Sprite role = BookEditorData.ResolveRoleSprite(entry);   // 与游戏一致：按资源键取图
            BookEditorUI.FillRect(roleRect, new Color(0f, 0f, 0f, 0.06f));
            BookEditorUI.DrawSpriteFit(roleRect, role, Color.white, 4f);
            y = roleRect.yMax + 6f;

            // 名字
            var nameRect = new Rect(card.x + 8f, y, card.width - 16f, 22f);
            BookEditorUI.FillRect(nameRect, new Color(0.30f, 0.22f, 0.14f, 0.85f));
            GUI.Label(nameRect, string.IsNullOrEmpty(entry.displayName) ? "（无名）" : entry.displayName, CenteredBoldStyle());
            y = nameRect.yMax + 6f;

            // 星级 + 种族
            var starRect = new Rect(card.x + 8f, y, card.width - 16f, 10f);
            BookEditorUI.DrawStars(starRect, entry.star);
            y = starRect.yMax + 4f;
            GUI.Label(new Rect(card.x + 8f, y, card.width - 16f, 16f),
                "种族图标：" + (string.IsNullOrEmpty(entry.raceId) ? "（未设置）" : entry.raceId),
                EditorStyles.miniLabel);
            y += 18f;

            // 六行数值
            string[,] rows =
            {
                { "Repel", entry.repelText },
                { "Quiantity", entry.quantityText },
                { "Scope", entry.scopeText },
                { "time", entry.timeText },
                { "weight", entry.weightText },
                { "coin", entry.coinText }
            };

            float rowHeight = 17f;
            for (int i = 0; i < 6; i++)
            {
                var rowRect = new Rect(card.x + 8f, y + i * rowHeight, card.width - 16f, rowHeight - 2f);
                BookEditorUI.FillRect(rowRect, i % 2 == 0 ? new Color(0f, 0f, 0f, 0.05f) : new Color(0f, 0f, 0f, 0.02f));
                GUI.Label(new Rect(rowRect.x + 4f, rowRect.y, rowRect.width * 0.45f, rowRect.height), rows[i, 0], EditorStyles.miniLabel);
                GUI.Label(new Rect(rowRect.x + rowRect.width * 0.45f, rowRect.y, rowRect.width * 0.55f - 4f, rowRect.height),
                    string.IsNullOrEmpty(rows[i, 1]) ? "（空）" : rows[i, 1], EditorStyles.miniLabel);
            }

            y += rowHeight * 6f + 6f;

            // 简介
            float explainHeight = Mathf.Max(40f, card.yMax - y - 10f);
            var explainRect = new Rect(card.x + 8f, y, card.width - 16f, explainHeight);
            BookEditorUI.FillRect(explainRect, new Color(0f, 0f, 0f, 0.04f));
            GUI.Label(new Rect(explainRect.x + 4f, explainRect.y + 2f, explainRect.width - 8f, explainRect.height - 4f),
                string.IsNullOrEmpty(entry.explainText) ? "（无简介）" : entry.explainText, BookEditorUI.RichLabel);
        }

        private static GUIStyle _centeredBold;

        private static GUIStyle CenteredBoldStyle()
        {
            if (_centeredBold == null)
            {
                _centeredBold = new GUIStyle(EditorStyles.boldLabel);
                _centeredBold.alignment = TextAnchor.MiddleCenter;
                _centeredBold.normal.textColor = new Color(1f, 0.94f, 0.78f, 1f);
            }
            return _centeredBold;
        }
    }

    /// <summary>
    /// 真实 Card 预制体渲染器（实验性）：
    /// 把 CDprofab/Card.prefab 实例化到 Unity 预览场景，套一层 ScreenSpaceCamera 画布，
    /// 用一台正交相机渲进 RenderTexture，再按卡面真实位置裁切显示 —— 所见即所得。
    /// 任何一步失败都会返回 false，调用方回退到 IMGUI 卡面，不会影响编辑器可用性。
    /// </summary>
    public sealed class BookCardRenderer : IDisposable
    {
        private Scene _scene;
        private GameObject _instance;
        private GameObject _canvasObject;
        private Canvas _canvas;
        private Camera _camera;
        private RenderTexture _renderTexture;
        private CardView _cardView;
        private bool _created;
        private int _lastHash;
        private Rect _lastCrop = new Rect(0f, 0f, 1f, 1f);

        public string LastError { get; private set; }
        public bool IsAvailable { get { return _created && _cardView != null && _renderTexture != null; } }

        /// <summary>关键数字诊断（画布尺寸 / 卡面尺寸 / 是否激活），出问题时方便定位。</summary>
        public string Diagnostics { get; private set; }

        /// <summary>确保预览场景与实例已创建。失败时返回 false 并写入 LastError。</summary>
        public bool EnsureCreated()
        {
            if (_created)
                return IsAvailable;

            _created = true;
            LastError = null;

            try
            {
                string prefabPath = BookEditorPrefs.CardPrefabPath;
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    LastError = "找不到 Card 预制体：" + prefabPath;
                    return false;
                }

                _scene = EditorSceneManager.NewPreviewScene();

                UnityEngine.Object spawned = PrefabUtility.InstantiatePrefab(prefab, _scene);
                _instance = spawned as GameObject;
                if (_instance == null)
                {
                    LastError = "实例化 Card 预制体失败。";
                    return false;
                }

                _cardView = _instance.GetComponentInChildren<CardView>(true);
                if (_cardView == null)
                {
                    LastError = "Card 预制体上找不到 CardView 组件。";
                    return false;
                }

                Canvas existing = _instance.GetComponentInChildren<Canvas>(true);
                if (existing != null)
                {
                    // 预制体自带 Canvas（游戏里它是 Book Canvas 下的嵌套画布）。
                    // 这里**不能**直接拿它当根画布用：它自己带 RectTransform，尺寸由预制体写着、
                    // 不一定会被 ScreenSpaceCamera 驱动，量出来的范围和实际渲染都可能不对。
                    // 所以按游戏里的层级重建：自建根画布 → 预制体整体挂进去（它自己的 Canvas 变成嵌套画布）。
                    _canvasObject = new GameObject("BookPreviewCanvas", typeof(Canvas), typeof(CanvasScaler));
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_canvasObject, _scene);
                    _canvas = _canvasObject.GetComponent<Canvas>();
                }
                else
                {
                    _canvasObject = new GameObject("BookPreviewCanvas", typeof(Canvas), typeof(CanvasScaler));
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_canvasObject, _scene);
                    _canvas = _canvasObject.GetComponent<Canvas>();
                }

                // 用 WorldSpace 画布 + 正交相机来渲染，而不是 ScreenSpaceCamera：
                // ScreenSpace 画布的尺寸/渲染时机由引擎在画布更新阶段驱动，手动 Camera.Render() 很容易
                // 出现"画布还没定尺寸就渲染"→ 一片空白的现象。WorldSpace 画布就是普通几何体，
                // 相机 1 单位 = 1 像素（正交高度 = 渲染高度的一半），效果与游戏里的 1:1 像素一致。
                _canvas.renderMode = RenderMode.WorldSpace;
                _canvas.worldCamera = null;
                _canvas.overrideSorting = false;

                var canvasRect = _canvas.transform as RectTransform;
                if (canvasRect != null)
                {
                    canvasRect.localScale = Vector3.one;
                    canvasRect.localRotation = Quaternion.identity;
                    canvasRect.localPosition = new Vector3(0f, 0f, 10f);
                    canvasRect.sizeDelta = new Vector2(RenderWidth, RenderHeight);
                    canvasRect.anchoredPosition = Vector2.zero;
                }

                // 世界空间画布不使用 CanvasScaler，尺寸由上面的 sizeDelta 直接决定
                CanvasScaler scaler = _canvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                    UnityEngine.Object.DestroyImmediate(scaler);

                // 预制体整体挂到自建画布下（保持内部结构不变）
                _instance.transform.SetParent(_canvas.transform, false);

                // 强制激活（预制体可能存成未激活状态，未激活的层级不参与布局也不渲染）
                _instance.SetActive(true);
                if (_cardView != null)
                    _cardView.gameObject.SetActive(true);

                // 预览时把卡面居中，保证整张卡都在渲染范围内（不影响游戏里的摆放）
                var rootRect = _instance.transform as RectTransform;
                if (rootRect != null)
                {
                    rootRect.anchorMin = new Vector2(0.5f, 0.5f);
                    rootRect.anchorMax = new Vector2(0.5f, 0.5f);
                    rootRect.pivot = new Vector2(0.5f, 0.5f);
                    rootRect.anchoredPosition = Vector2.zero;
                    rootRect.localScale = rootRect.localScale == Vector3.zero ? Vector3.one : rootRect.localScale;
                }

                var cameraObject = new GameObject("BookPreviewCamera", typeof(Camera));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, _scene);
                _camera = cameraObject.GetComponent<Camera>();
                _camera.orthographic = true;
                _camera.nearClipPlane = 0.3f;
                _camera.farClipPlane = 1000f;
                _camera.clearFlags = CameraClearFlags.SolidColor;
                _camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _camera.cullingMask = ~0;
                _camera.transform.rotation = Quaternion.identity;

                // 关键：**先量卡面真实范围，再据此定渲染尺寸与相机**。
                // 反过来（先定画布、再让卡面去适应画布）时，卡面往往比画布大、甚至不在相机视野里，
                // 结果就是"渲染结果全透明"—— 之前就是这么空白的。
                ForceCanvasLayout();
                FitToCard();

                // 刚建好时布局还没算完，先连续渲染几帧把画布"捂热"
                _warmupFrames = 4;
                _lastHash = 0;

                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Dispose();
                return false;
            }
        }

        /// <summary>渲染分辨率（宽 x 高）。量不到卡面范围时用它兜底。</summary>
        public int RenderWidth { get { return Mathf.Max(64, _renderWidth); } }
        public int RenderHeight { get { return Mathf.Max(64, _renderHeight); } }

        private Bounds _cardBounds;
        private bool _boundsClamped;

        /// <summary>
        /// 两遍渲染自动贴合卡面（完全不依赖层级包围盒的准确度）：
        /// 第一遍用一个足够大的窗口渲染，从**像素内容**里量出卡面在场景里的真实位置与大小；
        /// 第二遍按内容尺寸重建渲染目标、相机正对内容，于是得到一张**紧密裁好的 1:1 卡面图**。
        /// </summary>
        private void FitToCard()
        {
            const int probeBase = 1024;      // 第一遍探测窗口的基准边长（像素）
            const int maxSize = 2048;        // 最终渲染尺寸上限

            Vector3 center = GetCardCenterWorld();
            float probeWorldHeight = GetProbeWorldHeight();

            // 探测窗口按层级包围盒的长宽比开，保证内容不会被窗口裁掉
            int probeHeight = probeBase;
            int probeWidth = probeBase;
            if (UpdateCardBounds() && _cardBounds.size.y > 1f)
            {
                float aspect = _cardBounds.size.x / _cardBounds.size.y;
                probeWidth = Mathf.Clamp(Mathf.RoundToInt(probeBase * aspect), 64, 2048);
            }

            RenderAt(center, probeWorldHeight, probeWidth, probeHeight);
            if (_camera != null)
                _camera.Render();
            MeasureContent();

            if (!_contentMeasured)
            {
                // 第一遍就没渲到东西：保持探测窗口，交给自检/诊断说明原因
                _boundsClamped = false;
                return;
            }

            float worldPerPixel = probeWorldHeight / (float)probeHeight;
            float contentWidth = _contentCrop.width * probeWidth * worldPerPixel;
            float contentHeight = _contentCrop.height * probeHeight * worldPerPixel;

            Vector2 offset = new Vector2(
                (_contentCrop.center.x - 0.5f) * probeWidth * worldPerPixel,
                (_contentCrop.center.y - 0.5f) * probeHeight * worldPerPixel);
            Vector3 contentCenter = center + new Vector3(offset.x, offset.y, 0f);

            int width = Mathf.Clamp(Mathf.CeilToInt(contentWidth) + 8, 64, maxSize);
            int height = Mathf.Clamp(Mathf.CeilToInt(contentHeight) + 8, 64, maxSize);
            _boundsClamped = contentWidth + 8f > maxSize || contentHeight + 8f > maxSize;

            RenderAt(contentCenter, height, width, height);
            if (_camera != null)
                _camera.Render();
            MeasureContent();
        }

        /// <summary>卡面对象的世界坐标（画布在原点、缩放 1，所以等于画布局部坐标）。</summary>
        private Vector3 GetCardCenterWorld()
        {
            if (_cardView == null)
                return Vector3.zero;

            Vector3 position = _cardView.transform.position;
            return new Vector3(position.x, position.y, 0f);
        }

        /// <summary>第一遍探测窗口的世界高度：按层级包围盒粗估（偏大无妨），兜底 1200。</summary>
        private float GetProbeWorldHeight()
        {
            if (UpdateCardBounds())
            {
                float size = Mathf.Max(_cardBounds.size.y, _cardBounds.size.x) * 1.3f;
                return Mathf.Clamp(size, 256f, 4096f);
            }
            return 1200f;
        }

        /// <summary>重建/更新渲染目标，并把相机对准指定中心（渲染高度 = orthoHeight，1 单位 = 1 像素）。</summary>
        private void RenderAt(Vector3 worldCenter, float orthoHeight, int width, int height)
        {
            width = Mathf.Clamp(width, 64, 4096);
            height = Mathf.Clamp(height, 64, 4096);

            if (_renderTexture == null || _renderTexture.width != width || _renderTexture.height != height)
            {
                if (_renderTexture != null)
                {
                    if (_camera != null)
                        _camera.targetTexture = null;
                    _renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(_renderTexture);
                    _renderTexture = null;
                }

                _renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                _renderTexture.antiAliasing = 1;
                _renderTexture.Create();
            }

            if (_camera == null)
                return;

            _camera.targetTexture = _renderTexture;
            _camera.orthographicSize = orthoHeight * 0.5f;
            _camera.transform.position = new Vector3(worldCenter.x, worldCenter.y, -10f);
            _camera.transform.rotation = Quaternion.identity;
        }

        /// <summary>量卡面在画布坐标系里的包围盒；量不到返回 false。</summary>
        private bool UpdateCardBounds()
        {
            try
            {
                var canvasRect = _canvas != null ? _canvas.transform as RectTransform : null;
                if (canvasRect == null || _cardView == null)
                    return false;

                Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvasRect, _cardView.transform);
                if (bounds.size.x <= 1f || bounds.size.y <= 1f)
                    return false;

                _cardBounds = bounds;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private int _renderWidth = 1080;
        private int _renderHeight = 1920;

        /// <summary>刚创建/刚改分辨率时，需要连续渲染几帧才能拿到稳定布局（否则量不到卡面范围）。</summary>
        private int _warmupFrames;

        /// <summary>还在"捂热"阶段：调用方应主动 Repaint，让这几帧尽快跑完。</summary>
        public bool NeedsWarmup { get { return _warmupFrames > 0; } }

        private const int ContentAlphaThreshold = 8;

        /// <summary>按渲染结果的**像素内容**算出来的裁切 UV（左下原点，与 DrawTextureWithTexCoords 一致）。</summary>
        private Rect _contentCrop = new Rect(0f, 0f, 1f, 1f);
        private bool _contentMeasured;
        private string _contentNote = string.Empty;

        public BookCardRenderer()
        {
        }

        public BookCardRenderer(int width, int height)
        {
            _renderWidth = Mathf.Max(64, width);
            _renderHeight = Mathf.Max(64, height);
        }

        /// <summary>切换渲染分辨率（会重建画布与 RenderTexture）。</summary>
        public void SetResolution(int width, int height)
        {
            width = Mathf.Max(64, width);
            height = Mathf.Max(64, height);
            if (width == _renderWidth && height == _renderHeight)
                return;

            _renderWidth = width;
            _renderHeight = height;
            Dispose();
        }

        /// <summary>强制画布完成布局：不重建的话，刚创建的画布量出来的卡面尺寸是 0。</summary>
        private void ForceCanvasLayout()
        {
            Canvas.ForceUpdateCanvases();

            if (_canvas == null)
                return;

            var canvasRect = _canvas.transform as RectTransform;
            if (canvasRect != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRect);
        }

        private GameObject _probeObject;
        private string _probeNote = string.Empty;

        /// <summary>
        /// 自检：往画布正中放一块纯色块渲染一次。
        /// 若色块能渲出来 → 渲染管线正常，只是卡面内容不在视野/未激活；
        /// 若色块也渲不出来 → 是渲染管线层面没工作（URP / 预览场景相机问题）。
        /// </summary>
        private void ProbePipeline()
        {
            try
            {
                if (_probeObject == null || _probeObject.Equals(null))
                {
                    _probeObject = new GameObject("PipelineProbe", typeof(RawImage));
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_probeObject, _scene);

                    var probeRect = _probeObject.GetComponent<RectTransform>();
                    probeRect.SetParent(_canvas.transform, false);
                    probeRect.anchorMin = new Vector2(0.5f, 0.5f);
                    probeRect.anchorMax = new Vector2(0.5f, 0.5f);
                    probeRect.pivot = new Vector2(0.5f, 0.5f);
                    probeRect.anchoredPosition = Vector2.zero;
                    probeRect.sizeDelta = new Vector2(64f, 64f);

                    var raw = _probeObject.GetComponent<RawImage>();
                    raw.texture = Texture2D.whiteTexture;
                    raw.color = Color.magenta;
                }

                _probeObject.SetActive(true);
                ForceCanvasLayout();
                _camera.Render();
                MeasureContent();

                _probeNote = _contentMeasured
                    ? "渲染管线正常（色块渲得出来），但卡面内容不在视野/未激活"
                    : "渲染管线未工作：连自检色块都渲不出来（URP / 预览场景相机问题）";

                _probeObject.SetActive(false);
                ForceCanvasLayout();
                _camera.Render();
                MeasureContent();
            }
            catch (Exception e)
            {
                _probeNote = "自检失败：" + e.Message;
            }
        }

        /// <summary>渲染指定条目，返回可直接绘制的纹理（失败返回 null）。</summary>
        public Texture Render(BookEntry entry, out Rect cropUv)
        {
            cropUv = new Rect(0f, 0f, 1f, 1f);
            if (entry == null || !EnsureCreated())
                return null;

            try
            {
                int hash = ComputeHash(entry);
                bool needRender = hash != _lastHash || _warmupFrames > 0;

                if (needRender)
                {
                    _cardView.Fill(entry);
                    ForceCanvasLayout();
                    _camera.Render();
                    MeasureContent();      // 渲染完直接从像素量真实内容范围

                    // 一个像素都没渲到：放一块纯色色块自检，区分"渲染管线没工作"和"卡面内容不可见"
                    if (!_contentMeasured)
                        ProbePipeline();
                    else
                        _probeNote = string.Empty;

                    _lastHash = hash;
                    if (_warmupFrames > 0)
                        _warmupFrames--;
                }

                cropUv = ComputeCropUv();
                UpdateDiagnostics();
                return _renderTexture;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                return null;
            }
        }

        /// <summary>
        /// 直接从渲染结果读像素，算出**真实内容的包围盒**当裁切范围。
        ///
        /// 为什么不用层级里的 RectTransform 尺寸：卡面预制体内部可能有大片透明区域、
        /// 或尺寸由嵌套画布/父级决定，量出来经常是"整个画布"这种没用的值。
        /// 按像素量是所见即所得，也顺带能判断"到底渲染出东西没有"。
        /// </summary>
        private void MeasureContent()
        {
            _contentMeasured = false;
            _contentNote = string.Empty;

            RenderTexture previous = RenderTexture.active;
            Texture2D tex = null;
            try
            {
                if (_renderTexture == null)
                    return;

                int w = _renderTexture.width;
                int h = _renderTexture.height;
                RenderTexture.active = _renderTexture;

                tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0f, 0f, w, h), 0, 0, false);
                tex.Apply(false, false);

                Color32[] pixels = tex.GetPixels32();
                if (pixels == null || pixels.Length < w * h)
                {
                    _contentNote = "读回像素失败";
                    return;
                }

                int minX = w, minY = h, maxX = -1, maxY = -1;
                const int stride = 2;   // 隔点采样：够精确、也不拖慢编辑器
                for (int y = 0; y < h; y += stride)
                {
                    int row = y * w;
                    for (int x = 0; x < w; x += stride)
                    {
                        if (pixels[row + x].a < ContentAlphaThreshold)
                            continue;

                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }

                if (maxX < 0 || maxY < 0)
                {
                    _contentNote = "渲染结果全透明：相机没渲染到 UI（画布/相机设置或层级问题）";
                    return;
                }

                // Texture2D 的 (0,0) 在左下，与 GUI.DrawTextureWithTexCoords 的纹理坐标一致，不需要翻转 y
                const int pad = 2;
                minX = Mathf.Max(0, minX - pad);
                minY = Mathf.Max(0, minY - pad);
                maxX = Mathf.Min(w - 1, maxX + pad);
                maxY = Mathf.Min(h - 1, maxY + pad);

                _contentCrop = new Rect(
                    (float)minX / w,
                    (float)minY / h,
                    (float)(maxX - minX + 1) / w,
                    (float)(maxY - minY + 1) / h);
                _contentMeasured = true;
            }
            catch (Exception e)
            {
                _contentNote = "读取渲染结果失败：" + e.Message;
            }
            finally
            {
                RenderTexture.active = previous;
                if (tex != null)
                    UnityEngine.Object.DestroyImmediate(tex);
            }
        }

        /// <summary>收集关键数字，出问题时界面会直接显示出来。</summary>
        private void UpdateDiagnostics()
        {
            try
            {
                var canvasRect = _canvas != null ? _canvas.transform as RectTransform : null;
                string canvasText = canvasRect != null
                    ? Mathf.RoundToInt(canvasRect.rect.width) + "×" + Mathf.RoundToInt(canvasRect.rect.height)
                    : "无画布";

                string cardText = "量不到";
                string activeText = "?";
                bool overflow = false;

                if (_cardView != null)
                {
                    activeText = _cardView.gameObject.activeInHierarchy ? "激活" : "未激活";
                    if (canvasRect != null && canvasRect.rect.width > 0f && canvasRect.rect.height > 0f)
                    {
                        Bounds b = RectTransformUtility.CalculateRelativeRectTransformBounds(canvasRect, _cardView.transform);
                        cardText = Mathf.RoundToInt(b.size.x) + "×" + Mathf.RoundToInt(b.size.y);
                        overflow = b.size.x > canvasRect.rect.width + 1f || b.size.y > canvasRect.rect.height + 1f;
                    }
                }

                Diagnostics = "渲染 " + RenderWidth + "×" + RenderHeight + "（卡面 " + cardText + "），画布 " + canvasText +
                              "，卡面对象 " + activeText +
                              (_contentMeasured
                                  ? "，内容 " + Mathf.RoundToInt(_contentCrop.width * RenderWidth) + "×" +
                                    Mathf.RoundToInt(_contentCrop.height * RenderHeight) + " 像素"
                                  : "，内容：" + (string.IsNullOrEmpty(_contentNote) ? "未测到" : _contentNote)) +
                              (string.IsNullOrEmpty(_probeNote) ? string.Empty : "；自检：" + _probeNote) +
                              (_boundsClamped ? "；卡面超过 2048 上限，已截断" : string.Empty);
            }
            catch (Exception e)
            {
                Diagnostics = "诊断失败：" + e.Message;
            }
        }

        private int ComputeHash(BookEntry entry)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (entry.displayName != null ? entry.displayName.GetHashCode() : 0);
                hash = hash * 31 + (entry.roleSpriteKey != null ? entry.roleSpriteKey.GetHashCode() : 0);
                hash = hash * 31 + (entry.avatarSpriteKey != null ? entry.avatarSpriteKey.GetHashCode() : 0);
                hash = hash * 31 + entry.star;
                hash = hash * 31 + (entry.raceId != null ? entry.raceId.GetHashCode() : 0);
                hash = hash * 31 + (entry.repelText != null ? entry.repelText.GetHashCode() : 0);
                hash = hash * 31 + (entry.quantityText != null ? entry.quantityText.GetHashCode() : 0);
                hash = hash * 31 + (entry.scopeText != null ? entry.scopeText.GetHashCode() : 0);
                hash = hash * 31 + (entry.timeText != null ? entry.timeText.GetHashCode() : 0);
                hash = hash * 31 + (entry.weightText != null ? entry.weightText.GetHashCode() : 0);
                hash = hash * 31 + (entry.coinText != null ? entry.coinText.GetHashCode() : 0);
                hash = hash * 31 + (entry.explainText != null ? entry.explainText.GetHashCode() : 0);
                return hash;
            }
        }

        /// <summary>
        /// 计算卡面在画布中的裁切 UV。
        /// 注意：GUI.DrawTextureWithTexCoords 的 texCoords 用**左下角原点**（与 sprite.textureRect 一致），
        /// 所以这里 v 不要翻转 —— 翻转的话上下会错位（曾经就是这么错的）。
        /// </summary>
        public Rect ComputeCropUv()
        {
            // 优先用"渲染结果里的真实内容范围"（所见即所得，最可靠）
            if (_contentMeasured)
            {
                _lastCrop = _contentCrop;
                return _contentCrop;
            }

            try
            {
                if (_canvas == null || _cardView == null)
                    return new Rect(0f, 0f, 1f, 1f);

                Rect crop;
                if (TryMeasureCard(out crop))
                {
                    _lastCrop = crop;
                    return crop;
                }

                // 量不到（刚重建画布、布局未完成）：强推一次布局再量
                ForceCanvasLayout();
                if (TryMeasureCard(out crop))
                {
                    _lastCrop = crop;
                    return crop;
                }

                return new Rect(0f, 0f, 1f, 1f);
            }
            catch (Exception)
            {
                return new Rect(0f, 0f, 1f, 1f);
            }
        }

        /// <summary>量卡面在画布中的 UV 范围。量不到（宽高为 0）时返回 false。</summary>
        private bool TryMeasureCard(out Rect crop)
        {
            crop = new Rect(0f, 0f, 1f, 1f);

            var canvasRect = _canvas.transform as RectTransform;
            if (canvasRect == null || canvasRect.rect.width <= 0f || canvasRect.rect.height <= 0f)
                return false;

            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(canvasRect, _cardView.transform);
            if (bounds.size.x <= 1f || bounds.size.y <= 1f)
                return false;

            float u0 = (bounds.min.x - canvasRect.rect.xMin) / canvasRect.rect.width;
            float u1 = (bounds.max.x - canvasRect.rect.xMin) / canvasRect.rect.width;
            float v0 = (bounds.min.y - canvasRect.rect.yMin) / canvasRect.rect.height;
            float v1 = (bounds.max.y - canvasRect.rect.yMin) / canvasRect.rect.height;

            const float margin = 0.01f;
            u0 = Mathf.Clamp01(u0 - margin);
            u1 = Mathf.Clamp01(u1 + margin);
            v0 = Mathf.Clamp01(v0 - margin);
            v1 = Mathf.Clamp01(v1 + margin);

            if (u1 - u0 <= 0.001f || v1 - v0 <= 0.001f)
                return false;

            // 注意：GUI.DrawTextureWithTexCoords 的纹理坐标是左下原点，这里不要翻转 y
            crop = new Rect(u0, v0, u1 - u0, v1 - v0);
            return true;
        }

        /// <summary>裁切出来的卡面像素尺寸（用于界面提示）。</summary>
        public Vector2 CropPixelSize(Rect cropUv)
        {
            return new Vector2(
                Mathf.Max(1f, cropUv.width * RenderWidth),
                Mathf.Max(1f, cropUv.height * RenderHeight));
        }

        public void Dispose()
        {
            try
            {
                if (_camera != null)
                    _camera.targetTexture = null;

                if (_renderTexture != null)
                {
                    _renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(_renderTexture);
                    _renderTexture = null;
                }

                if (_instance != null)
                {
                    UnityEngine.Object.DestroyImmediate(_instance);
                    _instance = null;
                }

                if (_canvasObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(_canvasObject);
                    _canvasObject = null;
                }

                if (_scene.IsValid())
                    EditorSceneManager.ClosePreviewScene(_scene);
            }
            catch (Exception)
            {
                // 关闭时的清理失败不影响编辑器使用。
            }

            _created = false;
            _canvas = null;
            _camera = null;
            _cardView = null;
        }
    }
}
