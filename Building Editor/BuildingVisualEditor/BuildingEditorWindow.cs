using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CageFightBuildingTool
{
    /// <summary>
    /// 建筑可视化编辑器（主窗口）。
    ///
    ///   左栏 —— 建筑列表：按种族或按类型分组，行内带一级外观缩略图与归属角标；
    ///   中栏 —— 三级外观对比 + 三级数值编辑表 + 占地/施工/升级拆除/兵营/哨塔各分区；
    ///   右栏 —— 数据体检 / 设置；
    ///   工具栏 —— 保存全部、撤销、一键修复、备份恢复。
    ///
    /// 写入范围：建筑预制体（.prefab）。改的是**全局**资产 —— 所有关卡、所有种族共用，
    /// 每一处都会写明这一点；保存前自动备份到工具目录的 BuildingBackups。
    /// </summary>
    public sealed class BuildingEditorWindow : EditorWindow
    {
        private const string MenuPath = "工具/建筑可视化编辑器 %&g";
        private const float MinListWidth = 240f;
        private const float MaxListWidth = 460f;
        private const float MinRightWidth = 300f;
        private const float MaxRightWidth = 700f;

        #region 状态
        private readonly BuildingEditorData _data = new BuildingEditorData();

        private List<BuildingIssue> _issues = new List<BuildingIssue>();
        private bool _issuesDirty = true;

        private string _search = string.Empty;
        private int _filter;                 // 0=全部 1=只看有问题的 2=只看能建造的 3=只看没有 BuildUP 的
        private static readonly string[] FilterLabels = { "全部", "只看有问题的", "只看能建造的", "只看中立/没等级表的" };

        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private Vector2 _issueScroll;
        private Vector2 _welcomeScroll;

        private float _listWidth = 300f;
        private float _rightWidth = 420f;
        private int _rightTab;

        /// <summary>右栏（体检 / 设置）是否收起。收起后中栏拿到全部宽度 —— 体检面板很占地方。</summary>
        private bool _rightCollapsed;

        private string _status = string.Empty;

        private double _lastValidateTime;

        /// <summary>上次全量重扫的时间（用于 OnFocus 防抖）。</summary>
        private double _lastRescanTime;

        /// <summary>分组方式。缓存在字段里：`BuildingEditorPrefs.GroupByRace` 是 EditorPrefs（注册表）读取，
        /// 而左栏每帧都要问一次。真正的写回在 OnDisable。</summary>
        private bool _groupByRace = true;

        /// <summary>
        /// 体检节流间隔（秒）。编辑时每个按键都会把体检标脏，而体检要遍历全部对象 ——
        /// 不节流的话打字 / 拖数值时每一帧都在重算，界面就会一顿一顿的。
        /// </summary>
        private const double ValidateInterval = 0.2;
        private MessageType _statusType = MessageType.None;

        private static readonly string[] RightTabLabels = { "体检", "设置" };
        #endregion

        #region 折叠状态
        private const string FoldPrefPrefix = "CageFight.BuildingVisualEditor.Fold.";

        private static readonly string[] FoldKeys =
        {
            "levels", "occupancy", "upgrade", "barracks", "sentry", "owners",
            "tier.0", "tier.1", "tier.2",
        };

        /// <summary>折叠状态的内存缓存。</summary>
        private static readonly Dictionary<string, bool> FoldCache = new Dictionary<string, bool>();

        /// <summary>
        /// 读折叠状态。**走内存缓存**：`EditorPrefs` 背后是注册表读取，
        /// 而每个折叠头每帧都要问一次，折叠头一多就成了每帧十几次注册表访问。
        /// 只有真正切换时才写回 EditorPrefs（见 SetCollapsed）。
        /// </summary>
        private static bool IsCollapsed(string key)
        {
            bool value;
            if (FoldCache.TryGetValue(key, out value))
                return value;

            value = EditorPrefs.GetBool(FoldPrefPrefix + key, false);
            FoldCache[key] = value;
            return value;
        }

        private static void SetCollapsed(string key, bool collapsed)
        {
            // 缓存必须同步写回：IsCollapsed 只认 FoldCache，不写这里的话
            // 下一次读到的还是旧值，折叠开关会立刻弹回原状（"点了没反应"）。
            FoldCache[key] = collapsed;
            EditorPrefs.SetBool(FoldPrefPrefix + key, collapsed);
        }

        /// <summary>可折叠分区。返回 true = 已收起，调用方直接 return 即可。</summary>
        private bool Fold(string key, string title, string hint)
        {
            bool expanded = !IsCollapsed(key);
            bool now = BuildingEditorUI.FoldHeader(expanded, title, hint, BuildingEditorUI.Accent);
            if (now != expanded)
                SetCollapsed(key, !now);
            return !now;
        }
        #endregion

        #region 生命周期
        [MenuItem(MenuPath, false, 100)]
        public static void OpenWindow()
        {
            var window = GetWindow<BuildingEditorWindow>(false, "建筑编辑器", true);
            window.minSize = new Vector2(1120f, 620f);
            window.Show();
        }

        [MenuItem("Assets/打开建筑可视化编辑器", false, 2000)]
        private static void OpenFromContext()
        {
            GameObject prefab = Selection.activeObject as GameObject;
            BuildingEditorWindow window = GetWindow<BuildingEditorWindow>(false, "建筑编辑器", true);
            window.minSize = new Vector2(1120f, 620f);
            window.Show();
            if (prefab != null)
                window.LoadBuilding(AssetDatabase.GetAssetPath(prefab));
        }

        [MenuItem("Assets/打开建筑可视化编辑器", true)]
        private static bool OpenFromContextValidate()
        {
            GameObject prefab = Selection.activeObject as GameObject;
            if (prefab == null)
                return false;

            // 只有建筑预制体才显示这一项
            return prefab.GetComponent<BuildUP>() != null || prefab.GetComponent<BuildingBase>() != null;
        }

        private void OnEnable()
        {
            _listWidth = BuildingEditorPrefs.ListWidth;
            _rightWidth = BuildingEditorPrefs.RightWidth;
            _rightTab = Mathf.Clamp(BuildingEditorPrefs.RightTab, 0, RightTabLabels.Length - 1);
            _rightCollapsed = BuildingEditorPrefs.RightCollapsed;
            _groupByRace = BuildingEditorPrefs.GroupByRace;
            _data.Changed += OnDataChanged;
            Rescan();
            TryAutoLoad();
        }

        private void OnDisable()
        {
            _data.Changed -= OnDataChanged;
            BuildingEditorPrefs.ListWidth = _listWidth;
            BuildingEditorPrefs.RightWidth = _rightWidth;
            BuildingEditorPrefs.RightTab = _rightTab;
            BuildingEditorPrefs.GroupByRace = _groupByRace;
            BuildingEditorPrefs.RightCollapsed = _rightCollapsed;
        }

        /// <summary>
        /// 窗口重新获得焦点时**不做全量重扫**。
        ///
        /// `Rescan()` 要遍历 161 个预制体逐个取组件，`BuildingResourceIndex.Invalidate()` 还要
        /// 重建 2500+ 条 Sprite 映射 —— 以前每次从场景切回来都做一遍，就是那个"切回来卡一下"。
        /// 这里只清缓存里的实例引用；真正的重扫交给工具栏的「重新扫描建筑」和设置页的「重新读取全部注册表」。
        /// </summary>
        private void OnFocus()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - _lastRescanTime < 3.0)
                return;   // 连着切焦点时别反复清

            _lastRescanTime = now;
            BuildingFields.InvalidateCache();
            BuildingEditorData.InvalidateSourceStamp();
            BuildingEditorUI.ClearThumbnailCache();
        }

        private void OnSelectionChange()
        {
            GameObject prefab = Selection.activeObject as GameObject;
            if (prefab == null || _data.IsDirty)
                return;

            string path = AssetDatabase.GetAssetPath(prefab);
            if (!string.IsNullOrEmpty(path) && path != _data.AssetPath)
                LoadBuilding(path);
        }

        private void OnDataChanged()
        {
            _issuesDirty = true;
            Repaint();
        }

        private void Rescan()
        {
            _data.Rescan();
            BuildingFields.InvalidateCache();
            BuildingEditorData.InvalidateSourceStamp();
            _issuesDirty = true;
        }

        private void TryAutoLoad()
        {
            if (_data.IsLoaded)
                return;

            string last = BuildingEditorPrefs.LastBuildingPath;
            if (!string.IsNullOrEmpty(last) && LoadBuilding(last))
                return;

            if (_data.All.Count > 0)
                LoadBuilding(_data.All[0].Path);
        }

        public bool LoadBuilding(string assetPath)
        {
            if (_data.IsLoaded && _data.IsDirty && !string.Equals(_data.AssetPath, assetPath, StringComparison.OrdinalIgnoreCase))
            {
                bool save = EditorUtility.DisplayDialog("有未保存的修改",
                    "当前建筑「" + _data.Current.Name + "」还有未保存的修改，切换会丢掉它们。\n\n要先保存吗？",
                    "保存并切换", "直接切换");
                if (save)
                    SaveNow(BuildingEditorPrefs.AutoBackupOnSave);
            }

            if (!_data.Load(assetPath))
            {
                SetStatus("加载失败：" + assetPath, MessageType.Error);
                return false;
            }

            // 选键窗口的回调绑在"当时那个字段"上，切建筑后它就不该再能写值了
            BuildingKeyPickerWindow.CloseIfOpen();

            ClearEditorFocus();
            _issuesDirty = true;
            SetStatus("已加载：" + assetPath, MessageType.Info);
            Repaint();
            return true;
        }

        private static void ClearEditorFocus()
        {
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
        }

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
        }
        #endregion

        #region 写入封装
        /// <summary>
        /// 改一个组件的字段。
        ///
        /// 全程走 SerializedObject + ApplyModifiedProperties —— 与 Inspector 完全一致，
        /// `Ctrl+Z` 原生可用，而且**不受私有 [SerializeField] 限制**
        /// （建筑的绝大多数参数都是私有的：removable、6 个拆除表现参数、panelPrefabKey、avatarRenderer…）。
        /// </summary>
        /// <summary>
        /// 改一个组件的字段（复用 `BuildingFields` 缓存里的那一份 SerializedObject）。
        ///
        /// 原来这里是每帧 `new SerializedObject(...)` —— 本窗口一帧要画 4~5 个组件，
        /// 每次构造都要重建整棵属性树。改动后 `ApplyModifiedProperties()`
        /// 就是 Unity 自己写 Inspector 的标准做法。
        ///
        /// 走 `GetSerializedForWrite`：**写路径每次都要先 Update()**，否则
        /// `ApplyModifiedProperties()` 会拿旧属性树把别处刚做的改动一起覆盖掉。
        /// （这里原本自己又 Update() 了一次，而缓存命中时 `GetSerialized` 也会 Update ——
        /// 一帧白刷十几遍，是第三次卡顿排查里找到的主因之一。）
        /// </summary>
        private void Edit(UnityEngine.Object target, Action<SerializedObject> body)
        {
            if (target == null || body == null)
                return;

            SerializedObject serialized = BuildingFields.GetSerializedForWrite(target);
            if (serialized == null)
                return;

            EditorGUI.BeginChangeCheck();
            body(serialized);
            if (EditorGUI.EndChangeCheck())
            {
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                _data.MarkDirty();
            }
        }

        private static void Field(SerializedObject serialized, string name, string label, string tooltip = null)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null)
            {
                EditorGUILayout.LabelField(label, "（组件上没有这个字段）", BuildingEditorUI.MiniLabel);
                return;
            }
            EditorGUILayout.PropertyField(property, new GUIContent(label, tooltip));
        }

        #endregion

        #region 主循环
        private void OnGUI()
        {
            // 本趟 UI 的第一件事：告诉字段访问层"新的一趟开始了"，让下面这一批读取
            // 每个对象各自最多刷新一次 SerializedObject。
            // 必须排在 DrawToolbar / EnsureIssues / 三栏绘制之前 —— 它们全都要读字段。
            BuildingFields.BeginPass();

            if (Event.current.type == EventType.KeyDown && Event.current.control && Event.current.keyCode == KeyCode.S)
            {
                SaveNow(BuildingEditorPrefs.AutoBackupOnSave);
                Event.current.Use();
            }

            DrawToolbar();

            if (_data.All.Count == 0)
            {
                DrawWelcome("工程里没有找到任何建筑预制体。\n\n" +
                            "判定方式：预制体上挂了 `BuildUP` 或 `BuildingBase` 就算建筑（实测工程里有 9 个）。\n" +
                            "点工具栏「重新扫描」再试一次。");
                DrawStatusBar();
                return;
            }

            EnsureIssues();

            EditorGUILayout.BeginHorizontal();

            float listWidth, rightWidth;
            ComputeColumnWidths(out listWidth, out rightWidth);

            EditorGUILayout.BeginVertical(GUILayout.Width(listWidth), GUILayout.ExpandHeight(true));
            DrawLeftColumn();
            EditorGUILayout.EndVertical();

            Rect leftSplitter = GUILayoutUtility.GetRect(5f, 5f, GUILayout.Width(5f), GUILayout.ExpandHeight(true));
            float maxListWidth = Mathf.Max(MinListWidth, Mathf.Min(MaxListWidth, position.width - 320f - rightWidth));
            _listWidth = BuildingEditorUI.Splitter(leftSplitter, listWidth, MinListWidth, maxListWidth);

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawMiddleColumn();
            EditorGUILayout.EndVertical();

            // 收起时整条右栏不画（连分割条也不画），中栏自动拿到全部宽度
            if (!_rightCollapsed)
            {
                Rect rightSplitter = GUILayoutUtility.GetRect(5f, 5f, GUILayout.Width(5f), GUILayout.ExpandHeight(true));
                float maxRightWidth = Mathf.Max(MinRightWidth, Mathf.Min(MaxRightWidth, position.width - 320f - _listWidth));
                _rightWidth = BuildingEditorUI.Splitter(rightSplitter, rightWidth, MinRightWidth, maxRightWidth);
    
                EditorGUILayout.BeginVertical(GUILayout.Width(rightWidth), GUILayout.ExpandHeight(true));
                DrawRightColumn();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndHorizontal();

            DrawStatusBar();
        }

        /// <summary>中栏至少要留这么宽，不然三级卡片 / 兵种卡会被压得没法看。</summary>
        private const float MiddleMinWidth = 360f;

        /// <summary>
        /// 两条固定栏宽的实际取值：既尊重用户拖出来的宽度，又保证中栏不会被挤没。
        ///
        /// 原来两条各自都能拖到最大值（合起来会超过窗口宽度），中栏就被压成一条缝 ——
        /// "拉伸起来很别扭"就是这个。现在超了就按各自的余量按比例压缩。
        /// </summary>
        private void ComputeColumnWidths(out float listWidth, out float rightWidth)
        {
            float window = Mathf.Max(480f, position.width);
            float budget = window - MiddleMinWidth;

            listWidth = Mathf.Clamp(_listWidth, MinListWidth, MaxListWidth);
            rightWidth = Mathf.Clamp(_rightWidth, MinRightWidth, MaxRightWidth);

            float overflow = listWidth + rightWidth - budget;
            if (overflow <= 0f)
                return;

            float listSlack = Mathf.Max(0f, listWidth - MinListWidth);
            float rightSlack = Mathf.Max(0f, rightWidth - MinRightWidth);
            float totalSlack = listSlack + rightSlack;
            if (totalSlack <= 0f)
                return;

            float takeList = Mathf.Min(listSlack, overflow * (listSlack / totalSlack));
            listWidth -= takeList;
            rightWidth -= Mathf.Min(rightSlack, overflow - takeList);
        }

        private void EnsureIssues()
        {
            if (!_issuesDirty)
                return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastValidateTime < ValidateInterval)
            {
                Repaint();   // 约好稍后再算一次
                return;
            }
            _lastValidateTime = now;

            _issuesDirty = false;
            BuildingValidator.ValidateAll(new List<BuildingInfo>(_data.All), _issues);
        }

        private int ErrorCount { get { int e, w, i; BuildingValidator.Count(_issues, out e, out w, out i); return e; } }
        private int WarningCount { get { int e, w, i; BuildingValidator.Count(_issues, out e, out w, out i); return w; } }
        private int InfoCount { get { int e, w, i; BuildingValidator.Count(_issues, out e, out w, out i); return i; } }
        #endregion

        #region 工具栏
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("重新扫描建筑", EditorStyles.toolbarButton, GUILayout.Width(86f)))
            {
                BuildingResourceIndex.Invalidate();
                BuildingEditorUI.ClearThumbnailCache();
                Rescan();
                EnsureIssues();
                SetStatus("已重新扫描：" + _data.All.Count + " 个建筑预制体。", MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!_data.IsLoaded))
            {
                GUI.backgroundColor = _data.IsDirty ? new Color(1f, 0.85f, 0.5f) : Color.white;
                if (GUILayout.Button(_data.IsDirty ? "● 保存 (Ctrl+S)" : "保存 (Ctrl+S)", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                    SaveNow(BuildingEditorPrefs.AutoBackupOnSave);
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("撤销", EditorStyles.toolbarButton, GUILayout.Width(46f)))
                    Undo.PerformUndo();
            }

            GUILayout.FlexibleSpace();

            int errors = ErrorCount;
            int warnings = WarningCount;
            int infos = InfoCount;

            if (GUILayout.Button("体检 " + errors + " 错", EditorStyles.toolbarButton, GUILayout.Width(74f)))
                SwitchToIssues(2);
            if (GUILayout.Button(warnings + " 警", EditorStyles.toolbarButton, GUILayout.Width(50f)))
                SwitchToIssues(1);
            if (GUILayout.Button(infos + " 提示", EditorStyles.toolbarButton, GUILayout.Width(58f)))
                SwitchToIssues(0);

            if (_rightCollapsed &&
                GUILayout.Button("▸ 展开右栏", EditorStyles.toolbarButton, GUILayout.Width(84f)))
            {
                SetRightCollapsed(false);
            }

            using (new EditorGUI.DisabledScope(!_data.IsLoaded))
            {
                if (GUILayout.Button("一键修复", EditorStyles.toolbarButton, GUILayout.Width(66f)))
                    RunAutoFix();
            }

            if (GUILayout.Button("备份 ▾", EditorStyles.toolbarButton, GUILayout.Width(58f)))
                ShowBackupMenu();

            if (GUILayout.Button("设置", EditorStyles.toolbarButton, GUILayout.Width(40f)))
            {
                SetRightCollapsed(false);
                _rightTab = 1;
                BuildingEditorPrefs.RightTab = _rightTab;
                Repaint();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void SwitchToIssues(int levelFilter)
        {
            SetRightCollapsed(false);
            _rightTab = 0;
            BuildingEditorPrefs.RightTab = 0;
            _issueFilter = levelFilter;
            Repaint();
        }

        private int _issueFilter = -1;   // -1=全部 0=提示 1=警告 2=错误

        private void SaveNow(bool backup)
        {
            if (!_data.IsLoaded)
                return;

            string backupFile = backup ? _data.BackupNow("保存前") : null;
            _data.SaveToDisk(false);
            _issuesDirty = true;

            SetStatus("已保存：" + _data.AssetPath +
                      (string.IsNullOrEmpty(backupFile) ? string.Empty : "（旧版本已备份到 " + Path.GetFileName(backupFile) + "）"),
                      MessageType.Info);
            Repaint();
        }

        private void RunAutoFix()
        {
            if (!_data.IsLoaded)
                return;

            int fixable = 0;
            for (int i = 0; i < _issues.Count; i++)
            {
                BuildingIssue issue = _issues[i];
                if (issue != null && issue.Fixable && string.Equals(issue.BuildingPath, _data.AssetPath, StringComparison.Ordinal))
                    fixable++;
            }

            if (fixable == 0)
            {
                SetStatus("本建筑没有可以安全自动修复的问题。", MessageType.Info);
                return;
            }

            if (!EditorUtility.DisplayDialog("一键修复",
                    "将对「" + _data.Current.Name + "」修复 " + fixable + " 处问题：\n\n" +
                    "· 等级表补到 3 级 / 截断到 3 级（用最后一级的值补齐）\n" +
                    "· 负数造价、负数时长、负数维护费 归零\n" +
                    "· 占地尺寸夹到至少 1×1、side 夹回 1（玩家）\n" +
                    "· 删掉兵种表里的空条目；isDarkBarracks 按名字纠正\n\n" +
                    "三级数值（生命/攻击/射程/造价）一律不动 —— 那是平衡决策。" +
                    "种族建筑表也不动。可 Ctrl+Z 撤销。",
                    "开始修复", "取消"))
                return;

            _data.BackupNow("一键修复前");
            int fixedCount = BuildingValidator.ApplySafeFixes(_data.Current);
            _data.MarkDirty();
            _issuesDirty = true;

            SetStatus("已修复 " + fixedCount + " 处。", fixedCount > 0 ? MessageType.Info : MessageType.Warning);
            Repaint();
        }
        #endregion

        #region 左栏：建筑列表
        private void DrawLeftColumn()
        {
            BuildingEditorUI.Section("建筑", _data.All.Count + " 个预制体");

            _search = BuildingEditorUI.SearchField(_search, "搜索：名字 / 种族 / 类型 / 资源键");

            EditorGUILayout.BeginHorizontal();
            _filter = EditorGUILayout.Popup(_filter, FilterLabels, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();

            _groupByRace = EditorGUILayout.ToggleLeft("按种族分组（关掉 = 按建筑类型分组）", _groupByRace);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
            _listScroll.x = 0f;

            int shown = 0;
            if (_groupByRace)
            {
                var groups = new List<string>();
                for (int i = 0; i < _data.All.Count; i++)
                {
                    string key = _data.All[i].RaceLabel;
                    if (!groups.Contains(key))
                        groups.Add(key);
                }

                for (int g = 0; g < groups.Count; g++)
                {
                    int count = 0;
                    for (int i = 0; i < _data.All.Count; i++)
                    {
                        if (_data.All[i].RaceLabel == groups[g] && MatchesFilter(_data.All[i]))
                            count++;
                    }

                    BuildingEditorUI.FoldHeader(true, groups[g], null, BuildingEditorUI.Friendly, count + " 个");
                    for (int i = 0; i < _data.All.Count; i++)
                    {
                        if (_data.All[i].RaceLabel != groups[g] || !MatchesFilter(_data.All[i]))
                            continue;
                        DrawBuildingRow(_data.All[i]);
                        shown++;
                    }
                }
            }
            else
            {
                var order = new List<BuildingType>
                {
                    BuildingType.Barracks, BuildingType.DarkBarracks, BuildingType.Sentry, BuildingType.MainBase, BuildingType.None
                };

                for (int t = 0; t < order.Count; t++)
                {
                    int count = 0;
                    for (int i = 0; i < _data.All.Count; i++)
                    {
                        if (_data.All[i].PrimaryType == order[t] && MatchesFilter(_data.All[i]))
                            count++;
                    }
                    if (count == 0)
                        continue;

                    BuildingEditorUI.FoldHeader(true, BuildingInfo.TypeLabelOf(order[t]), null, BuildingEditorUI.Accent, count + " 个");
                    for (int i = 0; i < _data.All.Count; i++)
                    {
                        if (_data.All[i].PrimaryType != order[t] || !MatchesFilter(_data.All[i]))
                            continue;
                        DrawBuildingRow(_data.All[i]);
                        shown++;
                    }
                }
            }

            if (shown == 0)
                EditorGUILayout.LabelField("没有符合条件的建筑。", BuildingEditorUI.MiniLabel);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField("显示 " + shown + " / " + _data.All.Count + " 个", BuildingEditorUI.MiniLabel);
        }

        private bool MatchesFilter(BuildingInfo info)
        {
            if (info == null)
                return false;

            string q = (_search ?? string.Empty).Trim();
            if (q.Length > 0)
            {
                bool hit = (info.Name ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                           || (info.RaceLabel ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                           || (info.TypeLabel ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                           || (info.Path ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hit)
                    return false;
            }

            switch (_filter)
            {
                case 1:
                    return BuildingValidator.WorstLevelOf(_issues, info.Path) >= (int)BuildingIssueLevel.Warning;
                case 2:
                    return info.BuildUp != null;
                case 3:
                    return info.BuildUp == null;
                default:
                    return true;
            }
        }

        private void DrawBuildingRow(BuildingInfo info)
        {
            bool selected = _data.AssetPath == info.Path;

            Rect row = GUILayoutUtility.GetRect(0f, 40f, GUILayout.ExpandWidth(true));
            if (selected)
                BuildingEditorUI.FillRect(row, BuildingEditorUI.Selected);
            else if (row.Contains(Event.current.mousePosition))
                BuildingEditorUI.FillRect(row, new Color(1f, 1f, 1f, 0.06f));

            int worst = BuildingValidator.WorstLevelOf(_issues, info.Path);
            Color barColor = worst == (int)BuildingIssueLevel.Error ? BuildingEditorUI.Error
                : worst == (int)BuildingIssueLevel.Warning ? BuildingEditorUI.Warn
                : worst == (int)BuildingIssueLevel.Info ? BuildingEditorUI.Muted
                : BuildingEditorUI.Ok;
            BuildingEditorUI.FillRect(new Rect(row.x, row.y + 2f, 3f, row.height - 4f), barColor);

            // 一级外观缩略图（强引用，直接画）
            var thumbRect = new Rect(row.x + 8f, row.y + 4f, 32f, 32f);
            BuildingEditorUI.FillRect(thumbRect, new Color(0f, 0f, 0f, 0.22f));
            Sprite sprite = info.HasLevels ? info.BuildUp.levels[0].sprite : null;
            BuildingEditorUI.DrawSpriteFit(thumbRect, sprite, Color.white, 2f);

            float textWidth = Mathf.Max(60f, row.width - 120f);

            GUI.Label(new Rect(row.x + 46f, row.y + 4f, textWidth, 16f), info.Name, EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(row.x + 46f, row.y + 20f, textWidth, 14f),
                info.TypeLabel + "　" + info.RaceLabel, BuildingEditorUI.MiniLabel);

            // 右上角：有没有等级表
            var badge = new Rect(row.xMax - 62f, row.y + 3f, 56f, 15f);
            if (info.HasLevels)
                BuildingEditorUI.TagAt(badge, info.BuildUp.levels.Length + " 级", BuildingEditorUI.Accent);
            else
                BuildingEditorUI.TagAt(badge, "无等级表", BuildingEditorUI.Warn);

            if (worst >= 0)
            {
                int count = 0;
                for (int i = 0; i < _issues.Count; i++)
                {
                    BuildingIssue issue = _issues[i];
                    if (issue != null && string.Equals(issue.BuildingPath, info.Path, StringComparison.Ordinal))
                        count++;
                }
                GUI.Label(new Rect(row.xMax - 62f, row.y + 21f, 56f, 14f), count + " 个问题", BuildingEditorUI.MiniLabel);
            }

            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                if (!selected)
                    LoadBuilding(info.Path);
                Event.current.Use();
            }
        }
        #endregion

        #region 中栏：三级 + 各分区
        private void DrawMiddleColumn()
        {
            if (!_data.IsLoaded)
            {
                EditorGUILayout.Space(20f);
                EditorGUILayout.HelpBox("左边点一个建筑开始。", MessageType.Info);
                return;
            }

            BuildingInfo info = _data.Current;

            // 这里**不能**锁 x：列表锁横向是为了不让卡片左边被滚走，
            // 但详情区一旦内容比栏宽（窄栏下很容易），锁住 x 就等于"数值永远看不见"。
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.ExpandHeight(true));

            DrawBuildingHeader(info);
            DrawLevelsBlock(info);
            DrawOccupancyBlock(info);

            if (info.Training != null)
                DrawBarracksBlock(info);

            if (info.Tower != null)
                DrawSentryBlock(info);

            DrawUpgradeBlock(info);
            DrawOwnersBlock(info);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "提醒：建筑预制体是<b>全局资产</b> —— 所有关卡、所有种族共用同一份；" +
                "改完记得让程序知道（资源键变了可能要重新构建 Addressables）。",
                BuildingEditorUI.RichLabel);

            EditorGUILayout.EndScrollView();
        }

        private void DrawBuildingHeader(BuildingInfo info)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            var thumbRect = GUILayoutUtility.GetRect(56f, 56f, GUILayout.Width(56f), GUILayout.Height(56f));
            BuildingEditorUI.FillRect(thumbRect, new Color(0f, 0f, 0f, 0.22f));
            BuildingEditorUI.DrawSpriteFit(thumbRect, info.HasLevels ? info.BuildUp.levels[0].sprite : null, Color.white, 2f);

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(info.Name, EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            BuildingEditorUI.Tag(info.TypeLabel, BuildingEditorUI.Accent);
            BuildingEditorUI.Tag(info.RaceLabel, BuildingEditorUI.Friendly);
            if (info.BuildUp != null) BuildingEditorUI.Tag("BuildUP", BuildingEditorUI.Muted);
            if (info.Base != null) BuildingEditorUI.Tag("BuildingBase", BuildingEditorUI.Muted);
            if (info.Training != null) BuildingEditorUI.Tag("兵营", BuildingEditorUI.Boss);
            if (info.Tower != null) BuildingEditorUI.Tag("哨塔", BuildingEditorUI.Hostile);
            if (info.Upkeep != null) BuildingEditorUI.Tag("维护费", BuildingEditorUI.Warn);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(info.Path, BuildingEditorUI.MiniLabel);
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>三级：上面是外观对比条，下面是可编辑的数值表。</summary>
        private void DrawLevelsBlock(BuildingInfo info)
        {
            if (info.BuildUp == null)
            {
                EditorGUILayout.HelpBox(
                    "这个建筑没有 BuildUP 组件 —— 它没有等级表，既不能建造也不能升级。\n" +
                    "（中立的场景营地属于正常情况。如果它是玩家能造的建筑，说明预制体配错了。）",
                    MessageType.Info);
                return;
            }

            BuildUP buildUp = info.BuildUp;
            int count = buildUp.levels != null ? buildUp.levels.Length : 0;

            if (Fold("levels", "三级外观与数值", "游戏里升级换的就是这张表里的图与数值"))
                return;

            // ---------- 外观对比条 ----------
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < count; i++)
            {
                DrawLevelPreviewCard(info, i);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2f);

            // ---------- 数值编辑表 ----------
            EditorGUILayout.LabelField("三级数值（改这里才有效）", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "这一屏就是建筑的真值 —— 游戏里升级用的就是这张表。\n" +
                "· 改这里的「生命 / 攻击 / 范围 / 造价」才有效；预制体上同名的那几个字段，运行时会被这张表覆盖。\n" +
                "· 生命与攻击在游戏里按 ×1.1^(等级-1) 放大；攻击范围不放大，所以三级要分别填。",
                MessageType.None);

            if (info.BuildUp != null)
            {
                Edit(info.BuildUp, serialized =>
                {
                    SerializedProperty levels = serialized.FindProperty("levels");
                    if (levels == null || !levels.isArray)
                    {
                        EditorGUILayout.LabelField("读不到 levels 数组（字段名变了？）", BuildingEditorUI.MiniLabel);
                        return;
                    }

                    // 每级一个三行小组，**窄栏下也放得下**。
                    // （原来一行塞 级号 + 外观图 + 生命 + 攻击 + 攻击范围 + 造价 ≈ 800px，
                    //   中栏一窄右边就被裁掉，数值整个看不见 —— 这就是用户报的那个问题。）
                    // ★ 必须把 labelWidth 压小。
                    //   `PropertyField(..., GUILayout.Width(80))` 里的 80 是「标签 + 输入框」的总宽，
                    //   而标签宽度取 EditorGUIUtility.labelWidth（默认约 150）——
                    //   不压小的话，窄栏里标签会把宽度全吃掉，**输入框宽度变成 0，数值看起来是空的**。
                    float oldLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 40f;

                    for (int i = 0; i < levels.arraySize; i++)
                    {
                        SerializedProperty level = levels.GetArrayElementAtIndex(i);

                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("第 " + (i + 1) + " 级", EditorStyles.boldLabel, GUILayout.Width(48f));
                        EditorGUILayout.PropertyField(level.FindPropertyRelative("sprite"), new GUIContent("外观图"), GUILayout.Width(132f));
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.EndHorizontal();

                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(48f);
                        EditorGUILayout.PropertyField(level.FindPropertyRelative("maxHp"), new GUIContent("生命"), GUILayout.Width(92f));
                        EditorGUILayout.PropertyField(level.FindPropertyRelative("attack"), new GUIContent("攻击"), GUILayout.Width(92f));
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.EndHorizontal();

                        // 「范围」单独一行：Vector2Int 有自己的最小宽度，挤在同一行时
                        // Unity 会把 X / Y 两个子框折到下一行，看起来就像排版坏了。
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(48f);
                        EditorGUILayout.PropertyField(level.FindPropertyRelative("attackRange"), new GUIContent("攻击范围"), GUILayout.Width(200f));
                        GUILayout.FlexibleSpace();
                        EditorGUILayout.EndHorizontal();

                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Space(48f);
                        EditorGUILayout.PropertyField(level.FindPropertyRelative("cost"), new GUIContent(i == 0 ? "建造费" : "升级费"), GUILayout.Width(96f));
                        int spentHere = BuildingFacts.SpentToReachLevel(info.BuildUp, i);
                        // 这一行**不要**再放 FlexibleSpace：它和 LabelField 抢同一份剩余宽度，
                        // 标签只拿到一半 —— 「拆退 600」会被裁成「拆退 60」。
                        EditorGUILayout.LabelField("累计 " + spentHere + "　拆退 " + BuildingFacts.RefundOf(spentHere),
                            BuildingEditorUI.MiniLabel);
                        EditorGUILayout.EndHorizontal();

                        EditorGUILayout.EndVertical();
                    }

                    EditorGUIUtility.labelWidth = oldLabelWidth;
                });
            }

            EditorGUILayout.LabelField(
                "建造费 = 第 1 级的造价；升级费 = 下一级的造价；拆除返还 = （建造费 + 已付升级费）/ 2 向下取整。",
                BuildingEditorUI.RichLabel);

            // 补齐/截断
            if (count != BuildingValidator.ExpectedLevelCount)
            {
                EditorGUILayout.BeginHorizontal();
                GUI.contentColor = BuildingEditorUI.Warn;
                EditorGUILayout.LabelField("等级表是 " + count + " 级，而语义是三级。", BuildingEditorUI.MiniLabel, GUILayout.Width(260f));
                GUI.contentColor = Color.white;
                if (GUILayout.Button(count < 3 ? "补齐到 3 级" : "截断到 3 级", EditorStyles.miniButton, GUILayout.Width(110f)))
                {
                    int fixedCount = BuildingValidator.ApplySafeFixes(info);
                    _data.MarkDirty();
                    _issuesDirty = true;
                    SetStatus("已修正等级表长度（共 " + fixedCount + " 处改动）。", MessageType.Info);
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawLevelPreviewCard(BuildingInfo info, int index)
        {
            BuildUP.LevelData level = info.BuildUp.levels[index];

            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));

            EditorGUILayout.LabelField("第 " + (index + 1) + " 级", EditorStyles.miniBoldLabel);

            // 外观（强引用；空图时升级不会换图）
            var rect = GUILayoutUtility.GetRect(0f, 96f, GUILayout.ExpandWidth(true), GUILayout.Height(96f));
            BuildingEditorUI.FillRect(rect, new Color(0f, 0f, 0f, 0.22f));
            BuildingEditorUI.DrawSpriteFit(rect, level.sprite, Color.white, 4f);
            if (level.sprite == null)
                BuildingEditorUI.Frame(rect, BuildingEditorUI.Error, 1f);

            CardLine(level.sprite != null ? level.sprite.name : "（没有外观图）");

            // 关键数值 + 换算
            float scale = BuildingFacts.LevelScale(CurrentInjectedLevel(info));
            bool scales = info.IsSentry || info.IsBarracks;

            // 卡片只有三分之一栏宽，文字要压短（换行/裁掉都难看）。
            // 累计花费与拆退放在下面的编辑表里，那里宽度够。
            // ×1 的时候不画箭头："1423→1423" 只会让人以为哪里出错了
            bool scaled = scales && !Mathf.Approximately(scale, 1f);
            CardLine("生命 " + level.maxHp + (scaled ? "→" + Mathf.Max(1, Mathf.RoundToInt(level.maxHp * scale)) : string.Empty));
            CardLine("攻击 " + level.attack + (scaled ? "→" + Mathf.Max(1, Mathf.RoundToInt(level.attack * scale)) : string.Empty));
            if (info.Tower != null)
                CardLine("范围 " + level.attackRange.x + "×" + level.attackRange.y);
            CardLine((index == 0 ? "建造 " : "升级 ") + level.cost);

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 预览用的"注入等级"。游戏的缩放取的是关卡注入到 prop 上的等级字段
        /// （哨塔 sentryTowerLevel / 兵营 barracksLevel，其它恒 1）——
        /// 这里取预制体上的当前值当参考（编辑器里没有关卡上下文）。
        /// </summary>
        /// <summary>
        /// 三级预览卡里的一行文字。
        ///
        /// 用 `GUI.Label` + `GetRect` 而不是 `LabelField`：`LabelField` 会把文字宽度当成**最小宽度**，
        /// 三张卡的文字加起来能撑破中栏 —— 表现就是第三张卡被裁掉一半。
        /// </summary>
        private static void CardLine(string text)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 14f, GUILayout.ExpandWidth(true));
            GUI.Label(rect, text, BuildingEditorUI.MiniLabel);
        }


        private static int CurrentInjectedLevel(BuildingInfo info)
        {
            if (info.Property == null)
                return 1;

            if (info.IsSentry)
                return Mathf.Max(1, BuildingFields.GetInt(info.Property, "sentryTowerLevel", 1));
            if (info.IsBarracks)
            {
                bool dark = info.Training != null && info.Training.IsDarkBarracks;
                return Mathf.Max(1, BuildingFields.GetInt(info.Property, dark ? "darkBarracksLevel" : "barracksLevel", 1));
            }
            return 1;
        }

        private void DrawOccupancyBlock(BuildingInfo info)
        {
            if (info.Property == null)
            {
                EditorGUILayout.HelpBox("这个建筑没有 GameObjectProperty —— 占地、施工时长、血量都取不到。", MessageType.Warning);
                return;
            }

            if (Fold("occupancy", "占地与施工", "决定网格占用、合法位置与建造过程"))
                return;

            Edit(info.Property, serialized =>
            {
                Field(serialized, "occupySpace", "占地（格）", "左下基准格由 (位置 - 占地/2 + 0.5) 推出（BuildingBase.GetBasePos）");
                Field(serialized, "buildTime", "施工时长（秒）", "施工期间 spr.enabled=false、血量按进度涨");
                Field(serialized, "buildAnime", "施工特效键", "★ 为空则完全不进施工流程（BuildingBase.StartBuild 直接 return）→ isCompleted 恒为 false");
                EditorGUILayout.Space(2f);
                Field(serialized, "side", "队伍（1 = 玩家）", "玩家的建造/升级/拆除只对 side == 1 开放；敌方建筑在运行时由生成方改");
                Field(serialized, "objectType", "对象类型", "必须含 Building，否则放置系统不把它当建筑");
                Field(serialized, "maxHp", "预制体上的生命（会被覆盖）", "★ 运行时会被 BuildUP.levels[].maxHp 覆盖，改这里没有用");
                Field(serialized, "atk", "预制体上的攻击（会被覆盖）", "★ 同样会被 levels[].attack 覆盖");
                Field(serialized, "atkRange", "预制体上的攻击范围（会被覆盖）", "★ 会被 levels[].attackRange 覆盖");
            });

            int side = BuildingFields.GetInt(info.Property, "side", 1);
            string buildAnime = BuildingFields.GetString(info.Property, "buildAnime");
            if (string.IsNullOrEmpty(buildAnime))
            {
                EditorGUILayout.HelpBox(
                    "施工特效键是空的 → BuildingBase.StartBuild() 会直接 return，建筑瞬间完工，" +
                    "而且 `isCompleted` 永远是 false（兵营面板打不开、哨塔不开火、拆除表现不显示）。\n" +
                    (side == 1 ? "" : "（当前 side = " + side + "，敌方建筑在运行时会被生成方改。）"),
                    MessageType.Warning);
            }
        }

        private void DrawBarracksBlock(BuildingInfo info)
        {
            if (Fold("barracks", "兵营：兵种表与表现", "这块数据在建筑预制体上（兵种编辑器只读它）"))
                return;

            BuildingTraining training = info.Training;
            TroopDefinition[] troops = training.Troops;

            EditorGUILayout.BeginHorizontal();
            BuildingEditorUI.Tag(training.IsDarkBarracks ? "黑暗兵营" : "普通兵营",
                training.IsDarkBarracks ? BuildingEditorUI.Boss : BuildingEditorUI.Friendly);
            EditorGUILayout.LabelField("挂了 " + (troops != null ? troops.Length : 0) + " 个兵种", BuildingEditorUI.MiniLabel, GUILayout.Width(140f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("等级结算：" + (training.IsDarkBarracks ? "darkBarracksLevel" : "barracksLevel"), BuildingEditorUI.MiniLabel);
            EditorGUILayout.EndHorizontal();

            Edit(training, serialized =>
            {
                SerializedProperty troopArray = serialized.FindProperty("troops");
                if (troopArray != null && troopArray.isArray)
                {
                    // 同上：不压小 labelWidth，资产名输入框会被标签吃成 0 宽（看不见兵种）
                    float oldTroopLabelWidth = EditorGUIUtility.labelWidth;
                    EditorGUIUtility.labelWidth = 22f;

                    for (int i = 0; i < troopArray.arraySize; i++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        SerializedProperty element = troopArray.GetArrayElementAtIndex(i);
                        TroopDefinition troop = element.objectReferenceValue as TroopDefinition;

                        if (troop != null)
                        {
                            var iconRect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.Width(18f), GUILayout.Height(18f));
                            BuildingEditorUI.DrawSpriteFit(iconRect, BuildingResourceIndex.Instance.GetSprite(troop.IconKey), Color.white, 1f);
                        }

                        EditorGUILayout.PropertyField(element, new GUIContent((i + 1).ToString()), GUILayout.Width(142f));

                        if (troop != null)
                        {
                            bool rangeOk = troop.UnlockLevel >= 1 && troop.UnlockLevel <= 3;
                            GUI.contentColor = rangeOk ? Color.white : BuildingEditorUI.Error;
                            EditorGUILayout.LabelField("Lv" + troop.UnlockLevel + " · 维护 " + troop.Upkeep,
                                BuildingEditorUI.MiniLabel, GUILayout.Width(102f));
                            GUI.contentColor = Color.white;
                            if (!rangeOk)
                                BuildingEditorUI.Tag("面板不显示", BuildingEditorUI.Error);
                        }

                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("删", EditorStyles.miniButton, GUILayout.Width(24f)))
                        {
                            // 同 ApplySafeFixes：troops 是**对象引用数组**，第一次 Delete 只把元素置空、
                            // 不缩短数组 —— 只删一次的后果是"点了删，那一行不消失、变成 None"。
                            int before = troopArray.arraySize;
                            troopArray.DeleteArrayElementAtIndex(i);
                            if (troopArray.arraySize == before)
                                troopArray.DeleteArrayElementAtIndex(i);

                            // 这里**不能**用 GUIUtility.ExitGUI()：它会把本帧直接抛出去，
                            // 连带跳过外面 Edit() 里的 ApplyModifiedProperties()，删除就白做了。
                            // 改成把这一行的水平组配平后 break —— 少画后面几行在 IMGUI 里是安全的
                            //（只有"多画"才会报 Getting control N's position）。
                            EditorGUILayout.EndHorizontal();
                            break;
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUIUtility.labelWidth = oldTroopLabelWidth;
                }

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("＋ 加一个兵种", EditorStyles.miniButton, GUILayout.Width(100f)))
                {
                    SerializedProperty arr = serialized.FindProperty("troops");
                    if (arr != null && arr.isArray)
                        arr.InsertArrayElementAtIndex(arr.arraySize);
                }
                EditorGUILayout.LabelField("面板按兵种解锁等级（1/2/3）分三排 —— 要改阶级去兵种编辑器", BuildingEditorUI.MiniLabel);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2f);
                Field(serialized, "isDarkBarracks", "按黑暗兵营结算等级",
                    "勾上走 darkBarracksLevel，否则走 barracksLevel —— 配错会让敌方黑暗兵营等级作用在普通兵营上");
                Field(serialized, "panelPrefabKey", "训练面板预制体键");
                Field(serialized, "avatarSize", "头像角标尺寸");
                Field(serialized, "avatarOffset", "头像偏移");
                Field(serialized, "cooldownMaskAlpha", "冷却遮罩透明度");
                Field(serialized, "selectedTint", "选中颜色");
                Field(serialized, "avatarRenderer", "头像渲染器（预制体内部引用）");
                Field(serialized, "cooldownMaskRenderer", "冷却遮罩渲染器（预制体内部引用）");
                Field(serialized, "upgradeCoinMark", "升级标记（可见时点击只升级、不开面板）");
            });

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("选训练面板键", EditorStyles.miniButton, GUILayout.Width(120f)))
            {
                BuildingTraining target = training;
                BuildingKeyPickerWindow.Open("训练面板预制体键", info.Name, BuildingKeyPickerWindow.Kind.Prefab,
                    key => { WriteString(target, "panelPrefabKey", key); Repaint(); },
                    () => { WriteString(target, "panelPrefabKey", string.Empty); Repaint(); });
            }
            EditorGUILayout.LabelField("兵种表是强引用（不是键），直接从 Project 拖进来即可。", BuildingEditorUI.MiniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void WriteString(UnityEngine.Object target, string field, string value)
        {
            Edit(target, serialized =>
            {
                SerializedProperty property = serialized.FindProperty(field);
                if (property != null && property.propertyType == SerializedPropertyType.String)
                    property.stringValue = value ?? string.Empty;
            });
        }

        private void DrawSentryBlock(BuildingInfo info)
        {
            if (Fold("sentry", "哨塔：射击与维护费", "索敌是矩形、伤害类型与维护费都在这里"))
                return;

            Edit(info.Tower, serialized =>
            {
                Field(serialized, "shootPoint", "射击点（留空按名字找）");
                Field(serialized, "projectilePrefabKey", "弹幕预制体键", "留空回退 GameObjectProperty.atkObj");
                Field(serialized, "attackInterval", "射击间隔（秒）", "0 = 用 prop.atkRate（每秒次数）");
                Field(serialized, "damageType", "伤害类型");
                Field(serialized, "attackSoundKey", "开火音效键");
                Field(serialized, "attackAudio", "AudioSource（留空自动创建）");
                Field(serialized, "shakeAngle", "摇头最大偏角");
                Field(serialized, "shakeDuration", "震动时长");
                Field(serialized, "shakeWaves", "震动波数");
            });

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("选弹幕键", EditorStyles.miniButton, GUILayout.Width(90f)))
            {
                BuildingTowerAI tower = info.Tower;
                BuildingKeyPickerWindow.Open("弹幕预制体键", info.Name, BuildingKeyPickerWindow.Kind.Prefab,
                    key => { WriteString(tower, "projectilePrefabKey", key); Repaint(); },
                    () => { WriteString(tower, "projectilePrefabKey", string.Empty); Repaint(); });
            }
            if (GUILayout.Button("选开火音效键", EditorStyles.miniButton, GUILayout.Width(110f)))
            {
                BuildingTowerAI tower = info.Tower;
                BuildingKeyPickerWindow.Open("开火音效键", info.Name, BuildingKeyPickerWindow.Kind.Audio,
                    key => { WriteString(tower, "attackSoundKey", key); Repaint(); },
                    () => { WriteString(tower, "attackSoundKey", string.Empty); Repaint(); });
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (info.Upkeep != null)
            {
                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField("哨塔维护费（按 0 起等级索引）", EditorStyles.boldLabel);
                Edit(info.Upkeep, serialized =>
                {
                    Field(serialized, "upkeepPerLevel", "每级维护费", "按级别登记进队伍账本（isDefense: true）");
                });
            }
        }

        private void DrawUpgradeBlock(BuildingInfo info)
        {
            if (info.BuildUp == null)
                return;

            if (Fold("upgrade", "升级与拆除表现", "升级特效/果冻动画/拆除掉落那一套"))
                return;

            Edit(info.BuildUp, serialized =>
            {
                Field(serialized, "upgradeEffectPrefabKey", "升级特效键");
                Field(serialized, "effectTime", "升级动画时长");
                Field(serialized, "jellyTime", "果冻动画时长");
                Field(serialized, "jellyAmount", "果冻幅度");
                Field(serialized, "upgradeSoundKey", "升级音效键");
                EditorGUILayout.Space(2f);
                Field(serialized, "removable", "可拆除", "大本营设 false：不显示红/金币、TryRemove 直接返回 false");
                Field(serialized, "demolishSoundKey", "拆除音效键");
                Field(serialized, "demolishAnimWait", "拆除动画等待");
                Field(serialized, "demolishFallTime", "掉落时长");
                Field(serialized, "demolishFallDistance", "掉落距离");
                Field(serialized, "demolishSway", "掉落摆动");
                Field(serialized, "demolishAlpha", "掉落透明度");
            });

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("选升级特效键", EditorStyles.miniButton, GUILayout.Width(110f)))
            {
                BuildUP buildUp = info.BuildUp;
                BuildingKeyPickerWindow.Open("升级特效键", info.Name, BuildingKeyPickerWindow.Kind.Prefab,
                    key => { WriteString(buildUp, "upgradeEffectPrefabKey", key); Repaint(); },
                    () => { WriteString(buildUp, "upgradeEffectPrefabKey", string.Empty); Repaint(); });
            }
            if (GUILayout.Button("选升级音效键", EditorStyles.miniButton, GUILayout.Width(110f)))
            {
                BuildUP buildUp = info.BuildUp;
                BuildingKeyPickerWindow.Open("升级音效键", info.Name, BuildingKeyPickerWindow.Kind.Audio,
                    key => { WriteString(buildUp, "upgradeSoundKey", key); Repaint(); },
                    () => { WriteString(buildUp, "upgradeSoundKey", string.Empty); Repaint(); });
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawOwnersBlock(BuildingInfo info)
        {
            if (Fold("owners", "谁在用这个建筑", "种族建筑表里的引用（改动要在种族资产里做）"))
                return;

            if (info.Owners.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "没有任何种族在 RaceDefinition.buildings 里引用这个预制体 → 玩家永远造不出它。\n" +
                    "（中立的场景营地属于正常情况。）",
                    info.BuildUp == null ? MessageType.Info : MessageType.Warning);
                return;
            }

            for (int i = 0; i < info.Owners.Count; i++)
            {
                BuildingOwnership owner = info.Owners[i];
                EditorGUILayout.BeginHorizontal();
                BuildingEditorUI.Tag(owner.TypeLabel, BuildingEditorUI.Accent);
                EditorGUILayout.LabelField("种族 " + owner.RaceId, BuildingEditorUI.MiniLabel, GUILayout.Width(160f));
                EditorGUILayout.LabelField("键 " + owner.PrefabKey, BuildingEditorUI.MiniLabel);
                GUILayout.FlexibleSpace();

                RaceDefinition race = BuildingResourceIndex.Instance.GetRace(owner.RaceId);
                if (race != null && GUILayout.Button("定位种族资产", EditorStyles.miniButton, GUILayout.Width(96f)))
                    EditorGUIUtility.PingObject(race);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.LabelField(
                "种族建筑表是「种族 → 类型 → 预制体键」的唯一来源；查不到会回落到建造按钮上的默认预制体（通常是别的种族的建筑）。" +
                "本工具目前<b>只读</b>这张表 —— 改动请在种族资产里做。",
                BuildingEditorUI.RichLabel);
        }
        #endregion

        #region 右栏
        /// <summary>收起 / 展开右栏。收起后中栏自动拿到整条宽度。</summary>
        private void SetRightCollapsed(bool collapsed)
        {
            _rightCollapsed = collapsed;
            BuildingEditorPrefs.RightCollapsed = collapsed;
            Repaint();
        }

        private void DrawRightColumn()
        {
            // 标题栏右侧的 ✕：收起整条右栏，把宽度让给中栏（体检面板很占地方）
            EditorGUILayout.BeginHorizontal();
            int tab = GUILayout.Toolbar(_rightTab, RightTabLabels, EditorStyles.toolbarButton, GUILayout.Height(22f));
            if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(24f)))
            {
                SetRightCollapsed(true);
                EditorGUILayout.EndHorizontal();
                return;
            }
            EditorGUILayout.EndHorizontal();
            if (tab != _rightTab)
            {
                _rightTab = tab;
                BuildingEditorPrefs.RightTab = tab;
            }

            if (_rightTab == 1)
                DrawSettingsTab();
            else
                DrawIssuesTab();
        }

        private void DrawIssuesTab()
        {
            int errors = ErrorCount;
            int warnings = WarningCount;
            int infos = InfoCount;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("错误 " + errors, EditorStyles.miniButton, GUILayout.Width(70f)))
                _issueFilter = _issueFilter == 2 ? -1 : 2;
            if (GUILayout.Button("警告 " + warnings, EditorStyles.miniButton, GUILayout.Width(70f)))
                _issueFilter = _issueFilter == 1 ? -1 : 1;
            if (GUILayout.Button("提示 " + infos, EditorStyles.miniButton, GUILayout.Width(70f)))
                _issueFilter = _issueFilter == 0 ? -1 : 0;
            if (GUILayout.Button("全部", EditorStyles.miniButton, GUILayout.Width(48f)))
                _issueFilter = -1;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("重新体检", EditorStyles.miniButton, GUILayout.Width(70f)))
            {
                BuildingResourceIndex.Invalidate();
                BuildingEditorUI.ClearThumbnailCache();
                _issuesDirty = true;
                EnsureIssues();
            }
            EditorGUILayout.EndHorizontal();

            if (_issueFilter >= 0)
                EditorGUILayout.LabelField("（正在按级别过滤，点标题按钮取消）", BuildingEditorUI.MiniLabel);

            _issueScroll = EditorGUILayout.BeginScrollView(_issueScroll, GUILayout.ExpandHeight(true));

            if (_issues.Count == 0)
                EditorGUILayout.HelpBox("所有建筑都没有发现问题。", MessageType.Info);

            for (int i = 0; i < _issues.Count; i++)
            {
                BuildingIssue issue = _issues[i];
                if (issue == null)
                    continue;

                if (_issueFilter >= 0 && (int)issue.Level != _issueFilter)
                    continue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                GUI.contentColor = issue.LevelColor;
                EditorGUILayout.LabelField(issue.LevelLabel + " " + issue.Title, EditorStyles.boldLabel);
                GUI.contentColor = Color.white;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(issue.Detail, BuildingEditorUI.RichLabel);

                EditorGUILayout.BeginHorizontal();
                if (string.IsNullOrEmpty(issue.BuildingPath))
                {
                    EditorGUILayout.LabelField("全局", BuildingEditorUI.MiniLabel, GUILayout.Width(180f));
                }
                else
                {
                    bool isCurrent = issue.BuildingPath == _data.AssetPath;
                    EditorGUILayout.LabelField(issue.BuildingName + (isCurrent ? "（当前）" : string.Empty),
                        BuildingEditorUI.MiniLabel, GUILayout.Width(180f));

                    if (!isCurrent && GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(44f)))
                    {
                        LoadBuilding(issue.BuildingPath);
                        Repaint();
                    }
                }

                if (issue.Fixable)
                {
                    GUI.contentColor = BuildingEditorUI.Ok;
                    EditorGUILayout.LabelField("→ " + issue.SuggestedValue, BuildingEditorUI.MiniLabel);
                    GUI.contentColor = Color.white;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSettingsTab()
        {
            _welcomeScroll = EditorGUILayout.BeginScrollView(_welcomeScroll, GUILayout.ExpandHeight(true));

            BuildingEditorUI.Section("资源注册表");
            BuildingResourceIndex index = BuildingResourceIndex.Instance;
            EditorGUILayout.LabelField("PrefabRegistry：" + (index.PrefabRegistryFound ? index.PrefabKeys.Count + " 个键" : "未找到"), BuildingEditorUI.MiniLabel);
            EditorGUILayout.LabelField("SpriteRegistry：" + (index.SpriteRegistryFound ? index.SpriteKeyCount + " 个键" : "未找到"), BuildingEditorUI.MiniLabel);
            EditorGUILayout.LabelField("AudioRegistry：" + (index.AudioRegistryFound ? index.AudioKeyCount + " 个键" : "未找到"), BuildingEditorUI.MiniLabel);
            EditorGUILayout.LabelField("种族定义：" + index.Races.Count + " 个　兵种定义：" + index.Troops.Count + " 个", BuildingEditorUI.MiniLabel);

            if (GUILayout.Button("重新读取全部注册表"))
            {
                BuildingResourceIndex.Invalidate();
                BuildingEditorUI.ClearThumbnailCache();
                _issuesDirty = true;
                SetStatus("已重新读取注册表。", MessageType.Info);
                Repaint();
            }

            BuildingEditorUI.Section("备份");
            EditorGUILayout.LabelField("目录：" + BuildingEditorPrefs.BackupFolder, BuildingEditorUI.MiniLabel);
            EditorGUILayout.LabelField("（默认跟随工具目录，改过文件夹名也会自动找到；在工程之外，不会进包、不会进版本库）", BuildingEditorUI.MiniLabel);

            BuildingEditorPrefs.AutoBackupOnSave = EditorGUILayout.ToggleLeft("保存前自动备份", BuildingEditorPrefs.AutoBackupOnSave);
            BuildingEditorPrefs.KeepBackupCount = EditorGUILayout.IntSlider("每个资产保留份数", BuildingEditorPrefs.KeepBackupCount, 5, 200);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("立即备份当前建筑"))
            {
                string file = _data.BackupNow("手动");
                SetStatus(string.IsNullOrEmpty(file) ? "备份失败。" : "已备份：" + Path.GetFileName(file),
                    string.IsNullOrEmpty(file) ? MessageType.Error : MessageType.Info);
            }
            if (GUILayout.Button("打开备份目录"))
            {
                Directory.CreateDirectory(BuildingEditorPrefs.BackupFolder);
                EditorUtility.RevealInFinder(BuildingEditorPrefs.BackupFolder);
            }
            EditorGUILayout.EndHorizontal();

            BuildingEditorUI.Section("当前建筑");
            EditorGUILayout.LabelField(string.IsNullOrEmpty(_data.AssetPath) ? "（未加载）" : _data.AssetPath, BuildingEditorUI.MiniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("从磁盘重新加载"))
            {
                if (EditorUtility.DisplayDialog("重新加载", "将丢弃未保存的修改，从磁盘重新读取。", "重新加载", "取消"))
                {
                    _data.RevertToDisk();
                    _issuesDirty = true;
                    SetStatus("已从磁盘重新加载。", MessageType.Info);
                }
            }
            if (GUILayout.Button("在 Project 里定位"))
                EditorGUIUtility.PingObject(_data.Prefab);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("（本工具源码位于 " + BuildingEditorData.ToolRoot +
                "\\BuildingVisualEditor，通过目录联接接入工程；只写建筑预制体，不改运行时代码。）",
                BuildingEditorUI.RichLabel);

            EditorGUILayout.EndScrollView();
        }
        #endregion

        #region 备份菜单 / 欢迎页 / 状态栏
        private void ShowBackupMenu()
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("立即备份当前建筑"), false, () =>
            {
                string file = _data.BackupNow("手动");
                SetStatus(string.IsNullOrEmpty(file) ? "备份失败。" : "已备份：" + Path.GetFileName(file),
                    string.IsNullOrEmpty(file) ? MessageType.Error : MessageType.Info);
            });

            menu.AddItem(new GUIContent("打开备份目录"), false, () =>
            {
                Directory.CreateDirectory(BuildingEditorPrefs.BackupFolder);
                EditorUtility.RevealInFinder(BuildingEditorPrefs.BackupFolder);
            });

            List<string> backups = BuildingEditorData.ListBackups();
            if (backups.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("（还没有任何备份）"));
            }
            else
            {
                menu.AddSeparator(string.Empty);
                for (int i = 0; i < backups.Count && i < 25; i++)
                {
                    string file = backups[i];
                    menu.AddItem(new GUIContent("恢复：" + Path.GetFileName(file)), false, () => RestoreBackup(file));
                }
            }

            menu.ShowAsContext();
        }

        private void RestoreBackup(string file)
        {
            string target = _data.AssetPath;
            if (string.IsNullOrEmpty(target))
            {
                SetStatus("先选一个建筑，再恢复备份。", MessageType.Warning);
                return;
            }

            if (!EditorUtility.DisplayDialog("恢复备份",
                    "将用这个备份覆盖当前建筑预制体：\n" + Path.GetFileName(file) + "\n\n目标：" + target +
                    "\n\n当前内容会先自动备份一份（文件级覆盖，Ctrl+Z 无效）。",
                    "确认恢复", "取消"))
                return;

            if (BuildingEditorData.RestoreBackup(file, target))
            {
                LoadBuilding(target);
                _issuesDirty = true;
                SetStatus("已恢复：" + Path.GetFileName(file), MessageType.Info);
            }
            else
            {
                SetStatus("恢复失败，详见 Console。", MessageType.Error);
            }
        }

        private void DrawWelcome(string message)
        {
            _welcomeScroll = EditorGUILayout.BeginScrollView(_welcomeScroll);
            EditorGUILayout.Space(16f);
            EditorGUILayout.LabelField("建筑可视化编辑器", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("给策划用的建筑编辑器：三级数值与外观、造价、占地、兵营兵种表、哨塔参数，一屏看完。",
                BuildingEditorUI.RichLabel);
            EditorGUILayout.Space(10f);

            EditorGUILayout.HelpBox(message, MessageType.Info);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("全部功能", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "· 左栏：建筑列表（按种族或按类型分组），行内带一级外观缩略图与「N 级 / 无等级表」角标\n" +
                "· 中栏：三级外观对比 + 三级数值编辑表（生命 / 攻击 / 攻击范围 / 造价），带缩放换算与累计花费、拆除返还\n" +
                "· 中栏：占地与施工、兵营兵种表（可增删）、哨塔射击与维护费、升级与拆除表现、谁在用这个建筑\n" +
                "· 右栏：数据体检（种族建筑表缺条目、资源键失效、造价/血量倒挂、兵种表异常…）+ 一键修复 + 设置\n" +
                "· 安全：所有改动走 SerializedObject（与 Inspector 一致，可 Ctrl+Z）；保存前自动备份到工具目录的 BuildingBackups",
                BuildingEditorUI.RichLabel);

            EditorGUILayout.EndScrollView();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            if (_data.IsLoaded)
            {
                string dirty = _data.IsDirty ? "　● 有未保存的修改" : "　已保存";
                EditorGUILayout.LabelField("当前 " + _data.Current.Name + "　" + _data.Current.TypeLabel + dirty,
                    BuildingEditorUI.MiniLabel, GUILayout.Width(300f));

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("工具 " + BuildingEditorData.SourceStamp(), BuildingEditorUI.MiniLabel, GUILayout.Width(140f));
            }
            else
            {
                GUILayout.FlexibleSpace();
            }

            GUI.contentColor = _statusType == MessageType.Error ? BuildingEditorUI.Error
                : _statusType == MessageType.Warning ? BuildingEditorUI.Warn
                : BuildingEditorUI.Muted;
            EditorGUILayout.LabelField(_status, BuildingEditorUI.MiniLabel);
            GUI.contentColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }
        #endregion
    }
}