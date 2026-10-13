using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightStageTool
{
    /// <summary>
    /// 关卡编辑器的绘制工具箱：配色、Sprite/缩略图、分区标题、分割条、标签、进度条、标记点、网格。
    ///
    /// 视觉语言与「图鉴可视化编辑器」保持一致（同一套 IMGUI 自绘做法），
    /// 只做绘制，不持有任何游戏数据。
    /// </summary>
    public static class LevelEditorUI
    {
        #region 配色
        public static readonly Color Accent = new Color(0.29f, 0.62f, 0.96f, 1f);
        public static readonly Color Ok = new Color(0.35f, 0.78f, 0.44f, 1f);
        public static readonly Color Warn = new Color(0.96f, 0.72f, 0.20f, 1f);
        public static readonly Color Error = new Color(0.94f, 0.36f, 0.34f, 1f);
        public static readonly Color Muted = new Color(0.62f, 0.62f, 0.62f, 1f);
        public static readonly Color Selected = new Color(0.24f, 0.45f, 0.75f, 0.55f);
        public static readonly Color RowEven = new Color(1f, 1f, 1f, 0.03f);
        public static readonly Color SectionBg = new Color(0f, 0f, 0f, 0.16f);
        public static readonly Color PreviewBg = new Color(0.13f, 0.14f, 0.16f, 1f);

        /// <summary>战场画布底色（比预览底稍暖一点，和"地图"的感觉区分开）。</summary>
        public static readonly Color FieldBg = new Color(0.16f, 0.17f, 0.15f, 1f);
        /// <summary>网格线。</summary>
        public static readonly Color GridLine = new Color(1f, 1f, 1f, 0.07f);
        /// <summary>网格主线（每 5 格）。</summary>
        public static readonly Color GridLineMajor = new Color(1f, 1f, 1f, 0.15f);

        // 阵营/角色配色（战场画布上区分不同性质的东西）
        public static readonly Color Friendly = new Color(0.40f, 0.75f, 0.98f, 1f);
        public static readonly Color Hostile = new Color(0.95f, 0.45f, 0.40f, 1f);
        public static readonly Color Neutral = new Color(0.80f, 0.78f, 0.55f, 1f);
        public static readonly Color Boss = new Color(0.78f, 0.45f, 0.92f, 1f);
        #endregion

        #region 缓存的贴图与样式
        private static Texture2D _white;
        private static GUIStyle _sectionStyle;
        private static GUIStyle _miniLabel;
        private static GUIStyle _richLabel;
        private static GUIStyle _centeredMini;
        private static GUIStyle _searchStyle;
        private static GUIStyle _searchCancelStyle;

        public static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1);
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply();
                    _white.hideFlags = HideFlags.HideAndDontSave;
                }
                return _white;
            }
        }

        public static GUIStyle SectionStyle
        {
            get
            {
                if (_sectionStyle == null)
                {
                    _sectionStyle = new GUIStyle(EditorStyles.boldLabel);
                    _sectionStyle.fontSize = 12;
                }
                return _sectionStyle;
            }
        }

        public static GUIStyle MiniLabel
        {
            get
            {
                if (_miniLabel == null)
                {
                    _miniLabel = new GUIStyle(EditorStyles.miniLabel);
                    _miniLabel.wordWrap = false;
                }
                return _miniLabel;
            }
        }

        public static GUIStyle RichLabel
        {
            get
            {
                if (_richLabel == null)
                {
                    _richLabel = new GUIStyle(EditorStyles.label);
                    _richLabel.richText = true;
                    _richLabel.wordWrap = true;
                }
                return _richLabel;
            }
        }

        public static GUIStyle CenteredMini
        {
            get
            {
                if (_centeredMini == null)
                {
                    _centeredMini = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                    _centeredMini.clipping = TextClipping.Clip;
                }
                return _centeredMini;
            }
        }
        #endregion

        #region 基础绘制
        public static void FillRect(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, White);
            GUI.color = old;
        }

        public static void Frame(Rect rect, Color color, float thickness = 1f)
        {
            FillRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            FillRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            FillRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            FillRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        /// <summary>分区标题（带背景条 + 副标题）。</summary>
        public static void Section(string title, string hint = null)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            FillRect(rect, SectionBg);
            FillRect(new Rect(rect.x, rect.y, 3f, rect.height), Accent);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 3f, rect.width - 12f, 18f), title, SectionStyle);
            if (!string.IsNullOrEmpty(hint))
                GUI.Label(new Rect(rect.x + 8f + SectionStyle.CalcSize(new GUIContent(title)).x + 10f, rect.y + 4f, rect.width, 16f), hint, MiniLabel);
            GUILayout.Space(4f);
        }

        public static void Hint(string text)
        {
            EditorGUILayout.LabelField(text, MiniLabel);
        }

        /// <summary>
        /// 可点击折叠的分组头（整行可点）。返回点击后的展开状态（true = 展开）。
        /// 用 ▼ / ▶ 表示状态，和 Unity 自带的 Foldout 视觉一致但能整行点、也能带右侧计数。
        /// </summary>
        public static bool FoldHeader(bool expanded, string title, string hint, Color accent,
            string rightText = null, int indent = 0)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 21f, GUILayout.ExpandWidth(true));
            bool hover = rect.Contains(Event.current.mousePosition);

            FillRect(rect, hover ? new Color(1f, 1f, 1f, 0.10f) : new Color(1f, 1f, 1f, 0.05f));
            FillRect(new Rect(rect.x + indent, rect.y, 3f, rect.height), accent);

            float x = rect.x + indent + 6f;
            GUI.Label(new Rect(x, rect.y + 2f, 14f, 17f), expanded ? "▼" : "▶", CenteredMini);
            x += 16f;

            GUI.Label(new Rect(x, rect.y + 2f, rect.width - (x - rect.x) - 8f, 17f), title, EditorStyles.boldLabel);
            x += EditorStyles.boldLabel.CalcSize(new GUIContent(title)).x + 8f;

            // 计数紧跟在标题后面（左对齐流动），**不做右对齐**：
            // 右对齐是靠 rect.xMax 定位的，而滚动视图的内容一旦比视口宽，
            // rect.xMax 就会跑到可视区外，计数会被裁得只剩一条 —— 看上去就是"个数看不清"。
            if (!string.IsNullOrEmpty(rightText))
            {
                float tagWidth = EditorStyles.miniLabel.CalcSize(new GUIContent(rightText)).x + 12f;
                if (x + tagWidth <= rect.xMax - 4f)
                {
                    TagAt(new Rect(x, rect.y + 3f, tagWidth, 15f), rightText, accent);
                    x += tagWidth + 6f;
                }
            }

            if (!string.IsNullOrEmpty(hint) && x < rect.xMax - 40f)
                GUI.Label(new Rect(x, rect.y + 3f, rect.xMax - x - 6f, 16f), hint, MiniLabel);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition) && Event.current.button == 0)
            {
                expanded = !expanded;
                Event.current.Use();
            }

            return expanded;
        }

        /// <summary>一个小的 ▼/▶ 折叠开关按钮，返回点击后的展开状态。</summary>
        public static bool FoldToggle(bool expanded, float width = 20f)
        {
            if (GUILayout.Button(expanded ? "▼" : "▶", EditorStyles.miniButton, GUILayout.Width(width)))
                expanded = !expanded;
            return expanded;
        }

        public static void Space(float px = 6f)
        {
            GUILayout.Space(px);
        }

        /// <summary>
        /// 标签底色：把分类色提亮成浅色。
        ///
        /// 原来是「分类色 0.22 透明 + 灰字」，在 Unity 深色皮肤下底色是暗的（实测 RGB 80,100,113）、
        /// 文字是 Unity 的 centeredGreyMiniLabel（中灰），对比度极低 —— 计数根本看不清。
        /// 现在改成浅色底 + 深黑字，分类色只用于底色淡色与描边。
        /// </summary>
        public static Color TagFill(Color accent)
        {
            return Color.Lerp(Color.white, new Color(accent.r, accent.g, accent.b, 1f), 0.32f);
        }

        private static GUIStyle _tagTextStyle;

        /// <summary>标签文字样式：深黑色、加粗、居中。</summary>
        public static GUIStyle TagTextStyle
        {
            get
            {
                if (_tagTextStyle == null)
                {
                    _tagTextStyle = new GUIStyle(EditorStyles.miniLabel);
                    _tagTextStyle.alignment = TextAnchor.MiddleCenter;
                    _tagTextStyle.fontStyle = FontStyle.Bold;
                    _tagTextStyle.clipping = TextClipping.Clip;
                    _tagTextStyle.normal.textColor = new Color(0.05f, 0.06f, 0.08f, 1f);
                }
                return _tagTextStyle;
            }
        }

        /// <summary>一行小色块 + 文本，用于状态标签（浅底 + 深黑字）。</summary>
        public static void Tag(string text, Color color)
        {
            float w = Mathf.Max(TagTextStyle.CalcSize(new GUIContent(text)).x + 12f, EditorStyles.miniLabel.CalcSize(new GUIContent(text)).x + 10f);
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), EditorStyles.miniLabel,
                GUILayout.Width(w), GUILayout.Height(15f));
            FillRect(rect, TagFill(color));
            Frame(rect, color, 1f);
            GUI.Label(rect, text, TagTextStyle);
        }

        /// <summary>在指定矩形里画一个小标签（不参与布局）。</summary>
        public static void TagAt(Rect rect, string text, Color color)
        {
            FillRect(rect, TagFill(color));
            Frame(rect, color, 1f);
            GUI.Label(rect, text, TagTextStyle);
        }

        /// <summary>
        /// 可拖拽的分割条，返回新的宽度/高度。
        ///
        /// 手感相关的三件事（原来都缺，所以"拉伸很别扭"）：
        ///   1) 真正只有 5px 的线很难瞄准 → 命中区域左右各外扩 3px（画出来还是细线，但好抓）；
        ///   2) 原来光标只在 5px 里才变 → 现在外扩区里就变，并且**悬停/拖动时把线画亮加粗**，
        ///      一眼就知道"这里能拖"；
        ///   3) 拖动时置 GUI.changed，让窗口跟着重绘，鼠标走了画面不会落后。
        /// </summary>
        public static float Splitter(Rect rect, float currentWidth, float min, float max, bool vertical = true)
        {
            int control = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;

            Rect hit = vertical
                ? new Rect(rect.x - 3f, rect.y, rect.width + 6f, rect.height)
                : new Rect(rect.x, rect.y - 3f, rect.width, rect.height + 6f);

            bool hovering = hit.Contains(e.mousePosition);
            bool dragging = GUIUtility.hotControl == control;

            EditorGUIUtility.AddCursorRect(hit, vertical ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);

            if (e.type == EventType.MouseDown && e.button == 0 && hovering)
            {
                GUIUtility.hotControl = control;
                dragging = true;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == control)
            {
                float delta = vertical ? e.delta.x : e.delta.y;
                float safeMin = Mathf.Min(min, max);
                currentWidth = Mathf.Clamp(currentWidth + delta, safeMin, Mathf.Max(min, max));
                dragging = true;
                e.Use();
                GUI.changed = true;
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == control)
            {
                GUIUtility.hotControl = 0;
                dragging = false;
                e.Use();
            }

            bool active = hovering || dragging;
            Color barColor = active
                ? new Color(Accent.r, Accent.g, Accent.b, 0.85f)
                : new Color(1f, 1f, 1f, 0.10f);

            if (vertical)
            {
                float w = active ? 3f : 1f;
                FillRect(new Rect(rect.x + rect.width * 0.5f - w * 0.5f, rect.y, w, rect.height), barColor);
            }
            else
            {
                float h = active ? 3f : 1f;
                FillRect(new Rect(rect.x, rect.y + rect.height * 0.5f - h * 0.5f, rect.width, h), barColor);
            }

            return currentWidth;
        }

        /// <summary>模拟游戏内搜索框样式的输入框（带清除按钮）。</summary>
        public static string SearchField(string text, string placeholder, params GUILayoutOption[] options)
        {
            if (_searchStyle == null)
                _searchStyle = GUI.skin.FindStyle("ToolbarSeachTextField") ?? GUI.skin.FindStyle("ToolbarSearchTextField") ?? GUI.skin.textField;
            if (_searchCancelStyle == null)
                _searchCancelStyle = GUI.skin.FindStyle("ToolbarSeachCancelButton") ?? GUI.skin.FindStyle("ToolbarSearchCancelButton");

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, options);
            text = GUILayout.TextField(text ?? string.Empty, _searchStyle, GUILayout.ExpandWidth(true));
            if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(placeholder))
            {
                Rect last = GUILayoutUtility.GetLastRect();
                GUI.Label(new Rect(last.x + 4f, last.y, last.width, last.height), placeholder, EditorStyles.miniLabel);
            }
            if (_searchCancelStyle != null && GUILayout.Button(string.Empty, _searchCancelStyle, GUILayout.Width(16f)))
                text = string.Empty;
            EditorGUILayout.EndHorizontal();
            return text;
        }

        /// <summary>一条横向进度/占比条：底槽 + 填充 + 居中文本。</summary>
        public static void Bar(Rect rect, float ratio, Color color, string label = null)
        {
            FillRect(rect, new Color(1f, 1f, 1f, 0.07f));
            float w = Mathf.Clamp01(ratio) * rect.width;
            if (w > 0f)
                FillRect(new Rect(rect.x, rect.y, w, rect.height), new Color(color.r, color.g, color.b, 0.75f));
            Frame(rect, new Color(color.r, color.g, color.b, 0.5f), 1f);
            if (!string.IsNullOrEmpty(label))
                GUI.Label(rect, label, CenteredMini);
        }

        /// <summary>一行「键 -> 值」的只读展示。</summary>
        public static void KeyValue(string key, string value, float keyWidth = 96f)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(key, MiniLabel, GUILayout.Width(keyWidth));
            EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(value) ? "（空）" : value, EditorStyles.miniLabel, GUILayout.Height(15f));
            EditorGUILayout.EndHorizontal();
        }
        #endregion

        #region Sprite / 纹理
        private static readonly Dictionary<int, Texture> _prefabThumbs = new Dictionary<int, Texture>();

        /// <summary>按贴图子区域绘制 Sprite（支持图集切片，比 AssetPreview 更可靠）。</summary>
        public static void DrawSprite(Rect rect, Sprite sprite, Color tint)
        {
            if (sprite == null || sprite.texture == null)
            {
                DrawEmptySlot(rect, tint);
                return;
            }

            Texture texture = sprite.texture;
            Rect tr = sprite.textureRect;
            if (tr.width <= 0f || tr.height <= 0f)
            {
                DrawEmptySlot(rect, tint);
                return;
            }

            var uv = new Rect(tr.x / texture.width, tr.y / texture.height, tr.width / texture.width, tr.height / texture.height);
            Color old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
            GUI.color = old;
        }

        /// <summary>等比缩放居中绘制 Sprite。</summary>
        public static void DrawSpriteFit(Rect rect, Sprite sprite, Color tint, float padding = 2f)
        {
            if (sprite == null)
            {
                DrawEmptySlot(rect, tint);
                return;
            }

            float sw = sprite.rect.width;
            float sh = sprite.rect.height;
            if (sw <= 0f || sh <= 0f)
            {
                DrawSprite(rect, sprite, tint);
                return;
            }

            float availW = Mathf.Max(1f, rect.width - padding * 2f);
            float availH = Mathf.Max(1f, rect.height - padding * 2f);
            float scale = Mathf.Min(availW / sw, availH / sh);
            float w = sw * scale;
            float h = sh * scale;
            var target = new Rect(rect.center.x - w * 0.5f, rect.center.y - h * 0.5f, w, h);
            DrawSprite(target, sprite, tint);
        }

        /// <summary>空位绘制：灰框 + 提示字。</summary>
        public static void DrawEmptySlot(Rect rect, Color tint = default(Color), string text = "无图")
        {
            Color c = tint == default(Color) ? Muted : tint;
            FillRect(rect, new Color(c.r, c.g, c.b, 0.10f));
            Frame(rect, new Color(c.r, c.g, c.b, 0.45f), 1f);
            if (rect.width > 34f && rect.height > 16f)
                GUI.Label(rect, text, CenteredMini);
        }

        /// <summary>预制体缩略图（AssetPreview 缓存）。</summary>
        public static Texture GetPrefabThumbnail(GameObject prefab)
        {
            if (prefab == null)
                return null;

            int id = prefab.GetInstanceID();
            Texture cached;
            if (_prefabThumbs.TryGetValue(id, out cached) && cached != null)
                return cached;

            Texture thumb = AssetPreview.GetAssetPreview(prefab);
            if (thumb != null)
                _prefabThumbs[id] = thumb;
            return thumb;
        }

        public static void ClearThumbnailCache()
        {
            _prefabThumbs.Clear();
        }

        /// <summary>绘制星级（1-7，只亮所选那一颗）。</summary>
        public static void DrawStars(Rect rect, int star, int max = 7)
        {
            float slot = rect.width / max;
            for (int i = 0; i < max; i++)
            {
                var cell = new Rect(rect.x + i * slot, rect.y, Mathf.Max(1f, slot - 1f), rect.height);
                bool lit = (i + 1) == star;
                FillRect(cell, lit ? new Color(Warn.r, Warn.g, Warn.b, 0.85f) : new Color(1f, 1f, 1f, 0.08f));
                if (lit)
                    Frame(cell, Color.white, 1f);
            }
        }
        #endregion

        #region 战场画布专用绘制
        /// <summary>画一层网格（每 step 一条细线，每 majorEvery 条一条主线）。</summary>
        public static void DrawGrid(Rect area, float cellPixels, int step = 1, int majorEvery = 5)
        {
            if (cellPixels <= 0.5f)
                return;

            int index = 0;
            for (float x = area.x; x <= area.xMax; x += cellPixels)
            {
                bool major = (index % (step * majorEvery)) == 0;
                FillRect(new Rect(x, area.y, 1f, area.height), major ? GridLineMajor : GridLine);
                index += step;
            }

            index = 0;
            for (float y = area.y; y <= area.yMax; y += cellPixels)
            {
                bool major = (index % (step * majorEvery)) == 0;
                FillRect(new Rect(area.x, y, area.width, 1f), major ? GridLineMajor : GridLine);
                index += step;
            }
        }

        /// <summary>
        /// 在地图上画一个标记：方块/菱形/圆点由 shape 决定，返回它的矩形（供命中测试）。
        /// 菱形和圆点用逐行 FillRect 近似，避免依赖任何贴图资源。
        /// </summary>
        public static Rect DrawMarker(Vector2 center, float size, Color color, MarkerShape shape, bool selected = false)
        {
            var rect = new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size);

            switch (shape)
            {
                case MarkerShape.Diamond:
                    {
                        int steps = Mathf.Max(3, Mathf.RoundToInt(size));
                        for (int i = 0; i < steps; i++)
                        {
                            float t = i / (float)(steps - 1);
                            float w = size * (1f - Mathf.Abs(t * 2f - 1f));
                            FillRect(new Rect(center.x - w * 0.5f, rect.y + i * (size / steps), w, size / steps + 0.5f), color);
                        }
                        break;
                    }
                case MarkerShape.Circle:
                    {
                        int steps = Mathf.Max(3, Mathf.RoundToInt(size));
                        for (int i = 0; i < steps; i++)
                        {
                            float t = i / (float)(steps - 1);
                            float w = size * Mathf.Sqrt(Mathf.Max(0f, 1f - (t * 2f - 1f) * (t * 2f - 1f)));
                            FillRect(new Rect(center.x - w * 0.5f, rect.y + i * (size / steps), w, size / steps + 0.5f), color);
                        }
                        break;
                    }
                default:
                    FillRect(rect, color);
                    break;
            }

            if (selected)
            {
                var ring = new Rect(rect.x - 3f, rect.y - 3f, rect.width + 6f, rect.height + 6f);
                Frame(ring, Color.white, 1.5f);
            }
            else
            {
                Frame(rect, new Color(0f, 0f, 0f, 0.55f), 1f);
            }
            return rect;
        }

        /// <summary>带竖线的小旗子（用于大本营/锚点这类"位置"标记）。</summary>
        public static Rect DrawFlag(Rect field, float x, Color color, string label)
        {
            FillRect(new Rect(x - 1f, field.y, 2f, field.height), new Color(color.r, color.g, color.b, 0.55f));

            float w = string.IsNullOrEmpty(label) ? 14f : Mathf.Min(120f, EditorStyles.miniLabel.CalcSize(new GUIContent(label)).x + 8f);
            var head = new Rect(x + 2f, field.y + 2f, w, 15f);
            head.x = Mathf.Min(head.x, field.xMax - w - 1f);
            FillRect(head, new Color(color.r, color.g, color.b, 0.85f));
            if (!string.IsNullOrEmpty(label))
                GUI.Label(head, label, CenteredMini);
            return head;
        }

        /// <summary>双箭头标注两个 x 之间的距离。</summary>
        public static void DrawSpan(Rect field, float x1, float x2, float y, Color color, string label)
        {
            float a = Mathf.Min(x1, x2);
            float b = Mathf.Max(x1, x2);
            FillRect(new Rect(a, y, b - a, 1f), color);
            FillRect(new Rect(a, y - 3f, 1f, 7f), color);
            FillRect(new Rect(b - 1f, y - 3f, 1f, 7f), color);

            if (!string.IsNullOrEmpty(label))
            {
                var size = EditorStyles.miniLabel.CalcSize(new GUIContent(label));
                var rect = new Rect((a + b) * 0.5f - size.x * 0.5f - 3f, y - 16f, size.x + 6f, 14f);
                FillRect(rect, new Color(0f, 0f, 0f, 0.6f));
                GUI.Label(rect, label, CenteredMini);
            }
        }

        public enum MarkerShape
        {
            Square,
            Diamond,
            Circle
        }
        #endregion
    }
}
