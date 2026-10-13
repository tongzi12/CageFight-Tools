using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightEngineerTool
{
    /// <summary>
    /// 工程师可视化编辑器（Engineer Visual Editor）—— 管玩家可选的工程师：
    /// `Assets\RemoteResource\LoadoutDefinitions\*.asset`（EngineerDefinition）
    /// + 它在选装注册表里的一条登记 + 它指到的**单位预制体**上的局内数值。
    ///
    /// 为什么值得单独做一个：工程师这条链路有 4 个容易静默失效的环节，配置错了游戏里只表现为
    /// 「进关了但没有工程师」「列表里没有这个人」「图标/立绘是空的」，光看资产看不出来 ——
    /// 体检把这 4 条逐条查出来，并给出出处。
    ///
    /// 设计原则与另外六个编辑器一致：
    ///   · **只读地**引用运行时类型，游戏源码一行不改；写数据只写工程师定义资产、选装注册表与预制体数据；
    ///   · 每次写盘前自动备份到工具目录的 EngineerBackups；
    ///   · 工具源码放在工程之外，靠目录联接挂进来（tools\junction.ps1）。
    /// </summary>
    public sealed class EngineerEditorWindow : EditorWindow
    {
        private const string MenuPath = "工具/工程师可视化编辑器 %&e";
        private const string WindowTitle = "工程师编辑器";
        private const float ToolbarHeight = 24f;
        private const float StatusHeight = 22f;

        private static readonly string[] TabNames = { "标识与资源键", "立绘与待机帧", "局内数值（预制体）", "选装注册表", "体检" };

        private EngineerCatalog _catalog = new EngineerCatalog();
        private string _selectedPath = string.Empty;
        private string _search = string.Empty;
        private float _leftWidth = 320f;
        private int _tab;

        private Vector2 _leftScroll;
        private Vector2 _rightScroll;
        private Vector2 _issueScroll;

        private string _status = string.Empty;
        private MessageType _statusType = MessageType.None;
        private bool _dirty;          // 定义资产有未保存改动
        private bool _prefabDirty;    // 预制体有未保存改动

        private readonly List<EngineerIssue> _issues = new List<EngineerIssue>();
        private bool _issuesDirty = true;

        private SerializedObject _so;                 // 选中定义的编辑会话
        private GameObject _prefabAsset;
        private EngineerController _prefabController;
        private GameObjectProperty _prefabProperty;

        #region 生命周期
        [MenuItem(MenuPath, false, 100)]
        public static void OpenWindow()
        {
            var window = GetWindow<EngineerEditorWindow>(false, WindowTitle, true);
            window.minSize = new Vector2(1000f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            _leftWidth = EngineerEditorPrefs.LeftWidth;
            _tab = Mathf.Clamp(EngineerEditorPrefs.RightTab, 0, TabNames.Length - 1);
            _search = EngineerEditorPrefs.Search;
            titleContent = new GUIContent(WindowTitle);
            _selectedPath = EngineerEditorPrefs.LastEngineerPath;
            Rescan(true);
        }

        private void OnDisable()
        {
            EngineerEditorPrefs.LeftWidth = _leftWidth;
            EngineerEditorPrefs.RightTab = _tab;
            EngineerEditorPrefs.Search = _search;
            if (Selected != null)
                EngineerEditorPrefs.LastEngineerPath = Selected.AssetPath;
        }

        private void OnFocus()
        {
            EngineerResourceIndex.Invalidate();
        }
        #endregion

        #region 数据与选中
        private EngineerRecord Selected
        {
            get { return _catalog.Find(_selectedPath); }
        }

        private void Rescan(bool keepSelection)
        {
            CloseSession();
            _catalog = EngineerCatalog.Build();
            _issuesDirty = true;

            if (!keepSelection || _catalog.Find(_selectedPath) == null)
                _selectedPath = _catalog.Records.Count > 0 ? _catalog.Records[0].AssetPath : string.Empty;

            OpenSession();
            Repaint();
        }

        private void SelectRecord(EngineerRecord record)
        {
            if (!CanLeaveCurrent())
                return;

            _selectedPath = record != null ? record.AssetPath : string.Empty;
            if (record != null)
                EngineerEditorPrefs.LastEngineerPath = record.AssetPath;
            OpenSession();
            _issuesDirty = true;
            Repaint();
        }

        private bool CanLeaveCurrent()
        {
            if (!_dirty && !_prefabDirty)
                return true;

            return EditorUtility.DisplayDialog("有未保存的改动",
                "当前工程师（" + (Selected != null ? Selected.Id : "?") + "）还有未保存的改动，切走会丢掉它们。",
                "丢弃并切走", "留在这里");
        }

        private void OpenSession()
        {
            CloseSession();
            EngineerRecord record = Selected;
            if (record == null || record.Definition == null)
                return;

            _so = new SerializedObject(record.Definition);
            _prefabAsset = record.PrefabAsset;
            if (_prefabAsset != null)
            {
                _prefabController = EngineerPrefabAccess.GetController(_prefabAsset);
                _prefabProperty = EngineerPrefabAccess.GetProperty(_prefabAsset);
            }
        }

        private void CloseSession()
        {
            _so = null;
            _prefabAsset = null;
            _prefabController = null;
            _prefabProperty = null;
            _dirty = false;
            _prefabDirty = false;
        }

        private void SaveAll()
        {
            EngineerRecord record = Selected;
            if (record == null)
                return;

            var backedUp = new List<string>();

            if (_dirty && _so != null)
            {
                string backup = EngineerWriter.Backup(record.AssetPath, "保存前");
                if (!string.IsNullOrEmpty(backup))
                    backedUp.Add(System.IO.Path.GetFileName(backup));

                _so.ApplyModifiedProperties();
                EditorUtility.SetDirty(record.Definition);
                _dirty = false;
            }

            if (_prefabDirty && _prefabAsset != null)
            {
                string backup = EngineerWriter.Backup(record.PrefabPath, "保存前");
                if (!string.IsNullOrEmpty(backup))
                    backedUp.Add(System.IO.Path.GetFileName(backup));

                if (_prefabController != null)
                    EditorUtility.SetDirty(_prefabController);
                if (_prefabProperty != null)
                    EditorUtility.SetDirty(_prefabProperty);
                _prefabDirty = false;
            }

            AssetDatabase.SaveAssets();
            Rescan(true);
            SetStatus(backedUp.Count > 0
                ? "已保存。" + backedUp.Count + " 份备份：" + string.Join("、", backedUp.ToArray())
                : "没有需要保存的改动。", MessageType.Info);
        }

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
        }

        private void MarkDirty()
        {
            _dirty = true;
            Repaint();
        }

        private void MarkPrefabDirty()
        {
            _prefabDirty = true;
            Repaint();
        }

        private void RefreshIssues()
        {
            _issues.Clear();
            EngineerRecord record = Selected;
            if (record != null)
                EngineerValidator.Validate(record, _catalog.Records, _issues);
            _issuesDirty = false;
        }
        #endregion

        #region 主循环
        private void OnGUI()
        {
            if (Event.current.type == EventType.KeyDown && Event.current.control && Event.current.keyCode == KeyCode.S)
            {
                SaveAll();
                Event.current.Use();
            }

            if (_catalog == null)
                Rescan(true);

            if (_issuesDirty)
                RefreshIssues();

            DrawToolbar();

            Rect body = new Rect(0f, ToolbarHeight, position.width, position.height - ToolbarHeight - StatusHeight);
            float leftWidth = Mathf.Clamp(_leftWidth, 220f, Mathf.Max(240f, body.width - 420f));

            var leftRect = new Rect(body.x, body.y, leftWidth, body.height);
            var splitterRect = new Rect(leftRect.xMax, body.y, 5f, body.height);
            var rightRect = new Rect(splitterRect.xMax, body.y, body.width - leftWidth - 5f, body.height);

            DrawLeftColumn(leftRect);

            float updated = EngineerEditorUI.Splitter(splitterRect, leftWidth, 220f, Mathf.Max(240f, body.width - 420f));
            if (!Mathf.Approximately(updated, leftWidth))
                _leftWidth = updated;

            DrawRightColumn(rightRect);
            DrawStatusBar();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(ToolbarHeight));

            if (GUILayout.Button("重新扫描", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                if (CanLeaveCurrent())
                {
                    EngineerResourceIndex.Invalidate();
                    Rescan(true);
                    SetStatus("已重新扫描工程里的工程师与注册表。", MessageType.Info);
                }
            }

            if (GUILayout.Button(_dirty || _prefabDirty ? "● 保存 (Ctrl+S)" : "保存 (Ctrl+S)",
                    EditorStyles.toolbarButton, GUILayout.Width(104f)))
                SaveAll();

            if (GUILayout.Button("备份一份", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                EngineerRecord record = Selected;
                string backup = record != null ? EngineerWriter.Backup(record.AssetPath, "手动备份") : null;
                SetStatus(string.IsNullOrEmpty(backup)
                    ? "备份失败（没有选中工程师？）"
                    : "已备份：" + System.IO.Path.GetFileName(backup), MessageType.Info);
            }

            if (GUILayout.Button("定位资产", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                PingRecord(Selected);

            GUILayout.Space(8f);
            if (_dirty || _prefabDirty)
            {
                Color old = GUI.contentColor;
                GUI.contentColor = EngineerEditorUI.Growth;
                GUILayout.Label("● 有未保存的改动", EditorStyles.miniLabel);
                GUI.contentColor = old;
            }

            GUILayout.FlexibleSpace();

            int errors = 0, warnings = 0;
            for (int i = 0; i < _issues.Count; i++)
            {
                if (_issues[i].Level == EngineerIssueLevel.Error) errors++;
                else if (_issues[i].Level == EngineerIssueLevel.Warning) warnings++;
            }

            EngineerEditorUI.Tag(errors + " 错误", errors > 0 ? EngineerEditorUI.Error : EngineerEditorUI.Muted);
            EngineerEditorUI.Tag(warnings + " 警告", warnings > 0 ? EngineerEditorUI.Warn : EngineerEditorUI.Muted);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatusBar()
        {
            Rect rect = new Rect(0f, position.height - StatusHeight, position.width, StatusHeight);
            EngineerEditorUI.FillRect(rect, new Color(0f, 0f, 0f, 0.18f));
            EngineerEditorUI.FillRect(new Rect(rect.x, rect.y, 3f, rect.height),
                _statusType == MessageType.Error ? EngineerEditorUI.Error :
                _statusType == MessageType.Warning ? EngineerEditorUI.Warn : EngineerEditorUI.Accent);

            Color old = GUI.contentColor;
            GUI.contentColor = _statusType == MessageType.Error ? EngineerEditorUI.Error :
                               _statusType == MessageType.Warning ? EngineerEditorUI.Warn : Color.white;
            GUI.Label(new Rect(rect.x + 10f, rect.y + 3f, rect.width - 20f, rect.height - 4f),
                _status ?? string.Empty, EditorStyles.miniLabel);
            GUI.contentColor = old;
        }
        #endregion

        #region 左栏：工程师列表
        private void DrawLeftColumn(Rect rect)
        {
            GUILayout.BeginArea(rect);

            EngineerEditorUI.Section("工程师", _catalog.Records.Count + " 个资产 / 已登记 " + _catalog.RegisteredCount);
            _search = EngineerEditorUI.SearchField(_search, "搜索：id / 名字");

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);
            for (int i = 0; i < _catalog.Records.Count; i++)
            {
                EngineerRecord record = _catalog.Records[i];
                if (!Matches(record, _search))
                    continue;

                bool selected = string.Equals(record.AssetPath, _selectedPath, StringComparison.OrdinalIgnoreCase);
                Rect row = GUILayoutUtility.GetRect(0f, 38f, GUILayout.ExpandWidth(true));
                if (selected)
                    EngineerEditorUI.FillRect(row, EngineerEditorUI.Selected);
                else if (i % 2 == 0)
                    EngineerEditorUI.FillRect(row, EngineerEditorUI.RowEven);

                if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                    SelectRecord(record);

                var iconRect = new Rect(row.x + 4f, row.y + 3f, 30f, 30f);
                Sprite icon = EngineerResourceIndex.Instance.AssetOf(EngineerResKind.Sprite, record.IconKey) as Sprite;
                EngineerEditorUI.DrawSpriteFit(iconRect, icon, Color.white);

                GUI.Label(new Rect(row.x + 40f, row.y + 3f, row.width - 90f, 16f),
                    string.IsNullOrEmpty(record.DisplayName) ? record.Id : record.DisplayName, EditorStyles.boldLabel);
                GUI.Label(new Rect(row.x + 40f, row.y + 19f, row.width - 90f, 16f),
                    "id: " + (string.IsNullOrEmpty(record.Id) ? "（空）" : record.Id), EditorStyles.miniLabel);

                var tagRect = new Rect(row.xMax - 66f, row.y + 11f, 62f, 15f);
                EngineerEditorUI.TagAt(tagRect, record.IsRegistered ? "已登记" : "未登记",
                    record.IsRegistered ? EngineerEditorUI.Ok : EngineerEditorUI.Error);
            }
            EditorGUILayout.EndScrollView();

            GUILayout.EndArea();
        }

        private static bool Matches(EngineerRecord record, string search)
        {
            if (string.IsNullOrEmpty(search))
                return true;

            return (record.Id ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (record.DisplayName ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void PingRecord(EngineerRecord record)
        {
            if (record == null || record.Definition == null)
                return;

            EditorGUIUtility.PingObject(record.Definition);
            Selection.activeObject = record.Definition;
        }
        #endregion

        #region 右栏：页签
        private void DrawRightColumn(Rect rect)
        {
            GUILayout.BeginArea(rect);

            _tab = GUILayout.Toolbar(_tab, TabNames, EditorStyles.toolbarButton, GUILayout.Height(22f));
            EngineerEditorUI.Space(4f);

            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

            EngineerRecord record = Selected;
            if (record == null)
            {
                EngineerEditorUI.Hint("工程里没有 EngineerDefinition 资产。");
            }
            else
            {
                switch (TabNames[Mathf.Clamp(_tab, 0, TabNames.Length - 1)])
                {
                    case "标识与资源键": DrawIdentityTab(record); break;
                    case "立绘与待机帧": DrawPortraitTab(record); break;
                    case "局内数值（预制体）": DrawPrefabTab(record); break;
                    case "选装注册表": DrawRegistryTab(record); break;
                    default: DrawIssuesTab(record); break;
                }
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        #region ① 标识与资源键
        private void DrawIdentityTab(EngineerRecord record)
        {
            EngineerEditorUI.Section(EngineerValidator.SectionIdentity, "存档按 id 记录玩家选择");
            DrawSaveHint();

            if (_so == null)
                return;

            StringField("id", "id", "选装注册表按它建索引、存档也按它记录（UserGlobalInfo.selectedEngineerId）。改名 = 老存档选不中这个人。");
            StringField("displayName", "displayName", "选装界面/图鉴上显示的名字。");

            EngineerEditorUI.Space(6f);
            EngineerEditorUI.Section(EngineerValidator.SectionKeys, "资源键（写错只是静默失效，不会报错）");

            KeyField("iconKey", "小图标", EngineerResKind.Sprite);
            KeyField("prefabKey", "单位预制体", EngineerResKind.Prefab,
                "为空或不在 PrefabRegistry → 进游戏时工程师预制体加载不到 → 这一局没有工程师。");

            if (!string.IsNullOrEmpty(record.PrefabKey))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("预制体资产", GUILayout.Width(110f));
                EditorGUILayout.LabelField(string.IsNullOrEmpty(record.PrefabPath) ? "（按注册表反查不到）" : record.PrefabPath,
                    EditorStyles.miniLabel);
                if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(48f)) && !string.IsNullOrEmpty(record.PrefabPath))
                {
                    UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(record.PrefabPath);
                    if (asset != null)
                    {
                        EditorGUIUtility.PingObject(asset);
                        Selection.activeObject = asset;
                    }
                }
                EditorGUILayout.EndHorizontal();
            }

            EngineerEditorUI.Space(6f);
            EngineerEditorUI.Section(EngineerValidator.SectionSpell, "自带法术（局内槽位 0）");
            SpellField(record);
        }

        private void DrawSaveHint()
        {
            if (_dirty)
                EngineerEditorUI.Note("改动还没落盘：点工具栏「保存 (Ctrl+S)」（写盘前会自动备份一份）。", EngineerEditorUI.Growth);
        }
        #endregion

        #region ② 立绘与待机帧
        private void DrawPortraitTab(EngineerRecord record)
        {
            EngineerEditorUI.Section(EngineerValidator.SectionPortrait, "选装菜单里的立绘与待机动画");

            EngineerEditorUI.Note("菜单立绘的显示规则：配了待机帧就播帧动画；没配就用静态立绘；静态立绘也没有就回退小图标" +
                "（EngineerMenuPortrait.cs:84-140）。", EngineerEditorUI.FileInfo);

            KeyField("portraitFrameKey", "静态立绘", EngineerResKind.Sprite);

            EngineerEditorUI.Space(4f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("待机帧率", GUILayout.Width(110f));
            SerializedProperty rate = _so != null ? _so.FindProperty("idlePortraitFrameRate") : null;
            if (rate != null)
            {
                EditorGUI.BeginChangeCheck();
                float value = EditorGUILayout.Slider(rate.floatValue, 1f, 20f);
                if (EditorGUI.EndChangeCheck())
                {
                    rate.floatValue = value;
                    MarkDirty();
                }
                EditorGUILayout.LabelField("帧/秒", EditorStyles.miniLabel, GUILayout.Width(40f));
            }
            EditorGUILayout.EndHorizontal();

            DrawIdleFrames(record);

            EngineerEditorUI.Space(6f);
            EngineerEditorUI.Section("预览", null);
            Rect preview = GUILayoutUtility.GetRect(0f, 160f, GUILayout.ExpandWidth(true));
            EngineerEditorUI.FillRect(preview, EngineerEditorUI.PreviewBg);
            Sprite portrait = EngineerResourceIndex.Instance.AssetOf(EngineerResKind.Sprite, record.PortraitFrameKey) as Sprite;
            if (portrait == null && !string.IsNullOrEmpty(record.IconKey))
                portrait = EngineerResourceIndex.Instance.AssetOf(EngineerResKind.Sprite, record.IconKey) as Sprite;
            EngineerEditorUI.DrawSpriteFit(preview, portrait, Color.white, 6f);
        }

        private void DrawIdleFrames(EngineerRecord record)
        {
            SerializedProperty frames = _so != null ? _so.FindProperty("idlePortraitFrameKeys") : null;
            int count = frames != null && frames.isArray ? frames.arraySize : 0;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("待机帧", EditorStyles.boldLabel, GUILayout.Width(80f));
            EngineerEditorUI.Tag(count + " 帧", count > 0 ? EngineerEditorUI.Ok : EngineerEditorUI.Muted);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("＋ 加一帧", EditorStyles.miniButton, GUILayout.Width(72f)) && frames != null)
            {
                frames.arraySize = count + 1;
                frames.GetArrayElementAtIndex(count).stringValue = string.Empty;
                MarkDirty();
            }
            if (GUILayout.Button("清空", EditorStyles.miniButton, GUILayout.Width(48f)) && frames != null && count > 0)
            {
                if (EditorUtility.DisplayDialog("清空待机帧", "删掉这 " + count + " 帧？（保存后才会落盘）", "清空", "取消"))
                {
                    frames.arraySize = 0;
                    MarkDirty();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (frames == null || count == 0)
            {
                EngineerEditorUI.Hint("（没有待机帧：菜单里显示静态立绘）");
                return;
            }

            EngineerResourceIndex index = EngineerResourceIndex.Instance;
            int removeAt = -1;
            for (int i = 0; i < count; i++)
            {
                SerializedProperty element = frames.GetArrayElementAtIndex(i);
                bool valid = index.HasKey(EngineerResKind.Sprite, element.stringValue);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("#" + i, EditorStyles.miniLabel, GUILayout.Width(24f));

                EditorGUI.BeginChangeCheck();
                string value = EditorGUILayout.TextField(element.stringValue ?? string.Empty, GUILayout.ExpandWidth(true));
                if (EditorGUI.EndChangeCheck())
                {
                    element.stringValue = value;
                    MarkDirty();
                }

                if (!valid)
                    EngineerEditorUI.Tag("键无效", EngineerEditorUI.Warn);

                if (GUILayout.Button("选键", EditorStyles.miniButton, GUILayout.Width(44f)))
                {
                    int slot = i;
                    EngineerKeyPickerWindow.Open(EngineerResKind.Sprite, element.stringValue, picked =>
                    {
                        if (_so == null)
                            return;
                        SerializedProperty list = _so.FindProperty("idlePortraitFrameKeys");
                        if (list != null && slot < list.arraySize)
                        {
                            list.GetArrayElementAtIndex(slot).stringValue = picked;
                            _dirty = true;
                        }
                    });
                }

                if (GUILayout.Button("↑", EditorStyles.miniButton, GUILayout.Width(22f)) && i > 0)
                {
                    frames.MoveArrayElement(i, i - 1);
                    MarkDirty();
                }
                if (GUILayout.Button("↓", EditorStyles.miniButton, GUILayout.Width(22f)) && i < count - 1)
                {
                    frames.MoveArrayElement(i, i + 1);
                    MarkDirty();
                }
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22f)))
                    removeAt = i;

                EditorGUILayout.EndHorizontal();
            }

            if (removeAt >= 0)
            {
                frames.DeleteArrayElementAtIndex(removeAt);
                MarkDirty();
            }
        }
        #endregion

        #region ③ 局内数值（预制体）
        private void DrawPrefabTab(EngineerRecord record)
        {
            EngineerEditorUI.Section(EngineerValidator.SectionPrefab,
                string.IsNullOrEmpty(record.PrefabKey) ? "没配预制体" : record.PrefabKey);

            if (_prefabAsset == null)
            {
                EngineerEditorUI.Note("按 prefabKey 取不到预制体资产 —— 先在「标识与资源键」里把键配对。", EngineerEditorUI.Warn);
                return;
            }

            EngineerEditorUI.Note("这里改的是**预制体资产上的数据**（EngineerController / GameObjectProperty 的字段），" +
                "不碰任何脚本。保存时整个 .prefab 会先备份一份。", EngineerEditorUI.FileInfo);

            if (_prefabController == null)
            {
                EngineerEditorUI.Note("预制体上没有 EngineerController —— 进游戏时工程师不会生成。", EngineerEditorUI.Error);
                return;
            }

            EngineerEditorUI.Section("生命与移动", "EngineerController");
            PrefabFloat("最大生命", _prefabController.maxHp, v => _prefabController.maxHp = Mathf.Max(1, Mathf.RoundToInt(v)),
                "工程师自己的血量池就是它（EngineerController.cs:117/125/183），跟 GameObjectProperty.maxHp 不是同一处。");
            PrefabFloat("移动速度", _prefabController.moveSpeed, v => _prefabController.moveSpeed = Mathf.Max(0.01f, v));
            PrefabFloat("击退抗性", _prefabController.antiRepel, v => _prefabController.antiRepel = Mathf.Max(0.1f, v),
                "越大被击退得越少（位移 = 击退/抗性）。");
            PrefabFloat("相机视野", _prefabController.cameraViewSize, v => _prefabController.cameraViewSize = Mathf.Max(0.1f, v));

            EngineerEditorUI.Section("濒死击飞", null);
            PrefabFloat("击飞距离", _prefabController.defeatFlyDistance, v => _prefabController.defeatFlyDistance = Mathf.Max(0f, v));
            PrefabFloat("击飞时长", _prefabController.defeatFlyTime, v => _prefabController.defeatFlyTime = Mathf.Max(0.01f, v));
            PrefabFloat("眩晕时长", _prefabController.defeatStunTime, v => _prefabController.defeatStunTime = Mathf.Max(0f, v));

            EngineerEditorUI.Section("修复建筑", "工程师靠近友方建筑时按每秒百分比回血");
            PrefabFloat("修复半径", _prefabController.buildingHealRadius, v => _prefabController.buildingHealRadius = Mathf.Max(0f, v));
            PrefabFloat("每秒修复占比", _prefabController.buildingHealPercentPerSecond,
                v => _prefabController.buildingHealPercentPerSecond = Mathf.Max(0f, v), "0.02 = 每秒回 2% 最大生命。");
            PrefabFloat("结算间隔", _prefabController.buildingHealTick, v => _prefabController.buildingHealTick = Mathf.Max(0.05f, v));
            PrefabBool("修复粒子", _prefabController.buildingHealParticles, v => _prefabController.buildingHealParticles = v);

            EngineerEditorUI.Section("动画与法术锚点", null);
            PrefabString("移动参数名", _prefabController.moveParameter, v => _prefabController.moveParameter = v,
                "动画控制器里必须有一个同名 bool（EngineerController.cs:113/174）。");
            PrefabObject("施法锚点", _prefabController.spellAnchor,
                v => _prefabController.spellAnchor = v as Transform, "留空 = 用自身 transform（法术从脚底发出）。");

            if (_prefabProperty != null)
            {
                EngineerEditorUI.Section("GameObjectProperty", "对工程师而言它主要提供阵营与等级上下文");
                PrefabFloat("等级上下文：防御魔法等级", _prefabProperty.defenseMagicLevel,
                    v => _prefabProperty.defenseMagicLevel = Mathf.Max(1, Mathf.RoundToInt(v)), null, true);
                PrefabFloat("等级上下文：攻击魔法等级", _prefabProperty.attackMagicLevel,
                    v => _prefabProperty.attackMagicLevel = Mathf.Max(1, Mathf.RoundToInt(v)), null, true);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("两处血量", GUILayout.Width(110f));
                EditorGUILayout.LabelField("EngineerController " + (_prefabController != null ? _prefabController.maxHp : 0) +
                    " / GameObjectProperty " + _prefabProperty.maxHp, EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
            }

            if (_prefabDirty)
            {
                EngineerEditorUI.Space(4f);
                EngineerEditorUI.Note("预制体改动还没落盘：点工具栏「保存 (Ctrl+S)」。", EngineerEditorUI.Growth);
            }
        }
        #endregion

        #region ④ 选装注册表
        private void DrawRegistryTab(EngineerRecord record)
        {
            EngineerResourceIndex index = EngineerResourceIndex.Instance;

            EngineerEditorUI.Section(EngineerValidator.SectionRegistry, "LoadoutDefinitionRegistry.asset");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("登记状态", GUILayout.Width(110f));
            EngineerEditorUI.Tag(record.IsRegistered ? "已登记" : "未登记",
                record.IsRegistered ? EngineerEditorUI.Ok : EngineerEditorUI.Error);
            GUILayout.FlexibleSpace();
            if (!record.IsRegistered)
            {
                if (GUILayout.Button("加入注册表", EditorStyles.miniButton, GUILayout.Width(88f)))
                {
                    EngineerRegistryWriter.Add(record);
                    Rescan(true);
                    SetStatus("已把「" + record.Id + "」加入选装注册表。", MessageType.Info);
                    GUIUtility.ExitGUI();
                }
            }
            else if (GUILayout.Button("移出注册表", EditorStyles.miniButton, GUILayout.Width(88f)))
            {
                if (EditorUtility.DisplayDialog("移出选装注册表",
                        "把「" + record.Id + "」从注册表移除？玩家存档里如果正选着它，进关就没有工程师了。", "移出", "取消"))
                {
                    EngineerRegistryWriter.Remove(record);
                    Rescan(true);
                    SetStatus("已把「" + record.Id + "」移出选装注册表。", MessageType.Warning);
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();

            DrawSaveHint();

            EngineerEditorUI.Space(6f);
            EngineerEditorUI.KeyValue("注册表资产", EngineerResourceIndex.LoadoutRegistryPath, 78f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("已登记工程师", GUILayout.Width(110f));
            EditorGUILayout.LabelField(index.RegisteredEngineerPaths.Count + " 个（工程里共 " + _catalog.Records.Count + " 个定义资产）",
                EditorStyles.miniLabel);
            if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(48f)))
            {
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(EngineerResourceIndex.LoadoutRegistryPath);
                if (asset != null)
                {
                    EditorGUIUtility.PingObject(asset);
                    Selection.activeObject = asset;
                }
            }
            EditorGUILayout.EndHorizontal();

            EngineerEditorUI.Space(6f);
            EngineerEditorUI.Section("默认选择", "新存档开局用的那一套");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("默认工程师", GUILayout.Width(110f));
            EditorGUILayout.LabelField(string.IsNullOrEmpty(index.DefaultEngineerId) ? "（空）" : index.DefaultEngineerId,
                EditorStyles.miniLabel);
            bool isThis = string.Equals(index.DefaultEngineerId, record.Id, StringComparison.Ordinal);
            using (new EditorGUI.DisabledScope(isThis || string.IsNullOrEmpty(record.Id)))
            {
                if (GUILayout.Button(isThis ? "就是它" : "设为默认", EditorStyles.miniButton, GUILayout.Width(72f)))
                {
                    EngineerRegistryWriter.SetDefaultEngineer(record.Id);
                    EngineerResourceIndex.Invalidate();
                    Rescan(true);
                    SetStatus("默认工程师已设为「" + record.Id + "」。", MessageType.Info);
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("默认种族", GUILayout.Width(110f));
            EditorGUILayout.LabelField(string.IsNullOrEmpty(index.DefaultRaceId) ? "（空）" : index.DefaultRaceId, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("默认法术槽 1 / 2", GUILayout.Width(110f));
            EditorGUILayout.LabelField((string.IsNullOrEmpty(index.DefaultSpellSlot1Id) ? "（空）" : index.DefaultSpellSlot1Id) +
                "  /  " + (string.IsNullOrEmpty(index.DefaultSpellSlot2Id) ? "（空）" : index.DefaultSpellSlot2Id),
                EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EngineerEditorUI.Note("默认选择的两个槽位是**玩家可选法术**（局内第二、第三格）；工程师自带法术在「标识与资源键」页里配，" +
                "不走注册表默认值。", EngineerEditorUI.FileInfo);
        }
        #endregion

        #region ⑤ 体检
        private void DrawIssuesTab(EngineerRecord record)
        {
            int errors = 0, warnings = 0, fixable = 0;
            for (int i = 0; i < _issues.Count; i++)
            {
                if (_issues[i].Level == EngineerIssueLevel.Error) errors++;
                else if (_issues[i].Level == EngineerIssueLevel.Warning) warnings++;
                if (_issues[i].Fixable) fixable++;
            }

            EngineerEditorUI.Section("体检", errors + " 错误 / " + warnings + " 警告 / " + _issues.Count + " 条结论");

            if (GUILayout.Button(fixable > 0 ? ("一键修复（" + fixable + " 处）") : "没有可自动修复的问题"))
            {
                if (fixable > 0 && EditorUtility.DisplayDialog("一键修复",
                        "修 " + fixable + " 处：补登记进注册表、清待机帧空项、夹帧率、补默认选择。\n" +
                        "不会删资源键、不会动没报错的字段。每一步写盘前都会备份。", "开始修复", "取消"))
                {
                    int count = EngineerValidator.ApplySafeFixes(_issues);
                    EngineerResourceIndex.Invalidate();
                    Rescan(true);
                    SetStatus("已修复 " + count + " 处。", MessageType.Info);
                    GUIUtility.ExitGUI();
                }
            }

            if (_issues.Count == 0)
            {
                EngineerEditorUI.Hint("体检通过：这个工程师没有发现问题。");
                return;
            }

            _issueScroll = EditorGUILayout.BeginScrollView(_issueScroll);
            for (int i = 0; i < _issues.Count; i++)
            {
                EngineerIssue issue = _issues[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                Color old = GUI.contentColor;
                GUI.contentColor = issue.LevelColor;
                EditorGUILayout.LabelField(issue.LevelLabel + " " + issue.Title, EditorStyles.boldLabel);
                GUI.contentColor = old;

                EditorGUILayout.LabelField(issue.Detail, EngineerEditorUI.RichLabel);

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(48f)))
                    _tab = Mathf.Max(0, Array.IndexOf(TabNames, issue.Section));

                if (issue.Fixable)
                {
                    if (GUILayout.Button("修复：" + issue.SuggestedValue, EditorStyles.miniButton))
                    {
                        if (issue.Fix != null)
                            issue.Fix();
                        AssetDatabase.SaveAssets();
                        EngineerResourceIndex.Invalidate();
                        Rescan(true);
                        SetStatus("已按建议修复。", MessageType.Info);
                        GUIUtility.ExitGUI();
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("（只报告，工具不擅自改）", EditorStyles.miniLabel, GUILayout.Width(180f));
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }
        #endregion
        #endregion

        #region 字段控件
        private void StringField(string label, string propertyName, string hint = null)
        {
            if (_so == null)
                return;

            SerializedProperty property = _so.FindProperty(propertyName);
            if (property == null)
            {
                EditorGUILayout.LabelField(label, "字段缺失：" + propertyName);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110f));
            EditorGUI.BeginChangeCheck();
            string value = EditorGUILayout.TextField(property.stringValue ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
            {
                property.stringValue = value;
                MarkDirty();
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(hint))
                EditorGUILayout.LabelField(hint, EngineerEditorUI.RichLabel);
        }

        /// <summary>资源键字段：输入框 + 校验角标 + 键选择器 + 定位。</summary>
        private void KeyField(string propertyName, string label, EngineerResKind kind, string hint = null)
        {
            if (_so == null)
                return;

            SerializedProperty property = _so.FindProperty(propertyName);
            if (property == null)
                return;

            EngineerResourceIndex index = EngineerResourceIndex.Instance;
            string current = property.stringValue ?? string.Empty;
            bool hasValue = !string.IsNullOrWhiteSpace(current);
            bool valid = hasValue && index.HasKey(kind, current);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110f));

            EditorGUI.BeginChangeCheck();
            string value = EditorGUILayout.TextField(current);
            if (EditorGUI.EndChangeCheck())
            {
                property.stringValue = value;
                MarkDirty();
            }

            if (hasValue)
                EngineerEditorUI.Tag(valid ? "键有效" : "键无效", valid ? EngineerEditorUI.Ok : EngineerEditorUI.Error);
            else
                EngineerEditorUI.Tag("未配", EngineerEditorUI.Muted);

            if (GUILayout.Button("选键", EditorStyles.miniButton, GUILayout.Width(44f)))
            {
                string fieldName = propertyName;
                EngineerKeyPickerWindow.Open(kind, current, picked =>
                {
                    if (_so == null)
                        return;
                    SerializedProperty target = _so.FindProperty(fieldName);
                    if (target != null)
                    {
                        target.stringValue = picked;
                        _dirty = true;
                    }
                });
            }

            if (valid && GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(44f)))
            {
                string path = index.AssetPathOf(kind, current);
                UnityEngine.Object asset = string.IsNullOrEmpty(path)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                if (asset != null)
                {
                    EditorGUIUtility.PingObject(asset);
                    Selection.activeObject = asset;
                }
            }

            EditorGUILayout.EndHorizontal();

            EngineerEditorUI.Hint("（" + EngineerResourceIndex.KindName(kind) + " 注册表：" +
                EngineerResourceIndex.RegistryPathOf(kind) + "）");

            if (!string.IsNullOrEmpty(hint))
                EditorGUILayout.LabelField(hint, EngineerEditorUI.RichLabel);
        }

        /// <summary>自带法术：从选装注册表的法术列表里挑。</summary>
        private void SpellField(EngineerRecord record)
        {
            if (_so == null)
                return;

            SerializedProperty property = _so.FindProperty("innateSpellId");
            if (property == null)
                return;

            List<string> ids = EngineerResourceIndex.Instance.SpellIds;

            var labels = new List<string> { "（无）" };
            var values = new List<string> { string.Empty };
            for (int i = 0; i < ids.Count; i++)
            {
                labels.Add(ids[i]);
                values.Add(ids[i]);
            }

            string current = property.stringValue ?? string.Empty;
            int currentIndex = values.IndexOf(current);
            if (currentIndex < 0)
            {
                labels.Add(current + "  ← 不在注册表法术列表里");
                values.Add(current);
                currentIndex = values.Count - 1;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("innateSpellId", GUILayout.Width(110f));
            int picked = EditorGUILayout.Popup(currentIndex, labels.ToArray());
            if (picked != currentIndex && picked >= 0 && picked < values.Count)
            {
                property.stringValue = values[picked];
                MarkDirty();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EngineerEditorUI.Hint("可选 " + ids.Count + " 个法术（来自选装注册表 spellDefinitions）。" +
                (string.IsNullOrEmpty(current) ? " 现在没配：局内第一格是空的。" : string.Empty));
        }

        private void PrefabFloat(string label, float value, Action<float> setter, string hint = null, bool asInt = false)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110f));

            EditorGUI.BeginChangeCheck();
            float next = asInt
                ? EditorGUILayout.IntField(Mathf.RoundToInt(value), GUILayout.Width(80f))
                : EditorGUILayout.FloatField(value, GUILayout.Width(80f));
            if (EditorGUI.EndChangeCheck())
            {
                setter(next);
                MarkPrefabDirty();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(hint))
                EditorGUILayout.LabelField(hint, EngineerEditorUI.RichLabel);
        }

        private void PrefabBool(string label, bool value, Action<bool> setter)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110f));
            EditorGUI.BeginChangeCheck();
            bool next = EditorGUILayout.Toggle(value, GUILayout.Width(20f));
            if (EditorGUI.EndChangeCheck())
            {
                setter(next);
                MarkPrefabDirty();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void PrefabString(string label, string value, Action<string> setter, string hint = null)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110f));
            EditorGUI.BeginChangeCheck();
            string next = EditorGUILayout.TextField(value ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
            {
                setter(next);
                MarkPrefabDirty();
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(hint))
                EditorGUILayout.LabelField(hint, EngineerEditorUI.RichLabel);
        }

        private void PrefabObject(string label, UnityEngine.Object value, Action<UnityEngine.Object> setter, string hint = null)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110f));
            EditorGUI.BeginChangeCheck();
            UnityEngine.Object next = EditorGUILayout.ObjectField(value, typeof(Transform), true);
            if (EditorGUI.EndChangeCheck())
            {
                setter(next);
                MarkPrefabDirty();
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(hint))
                EditorGUILayout.LabelField(hint, EngineerEditorUI.RichLabel);
        }
        #endregion
    }
}
