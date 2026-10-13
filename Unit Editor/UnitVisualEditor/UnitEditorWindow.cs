using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CageFightUnitTool
{
    /// <summary>
    /// 兵种可视化编辑器（主窗口）。
    ///
    /// 左栏：按种族分组的兵种列表 + 搜索/筛选 + 勾选批量；
    /// 中栏：训练配置（TroopDefinition）+ 战斗数值（预制体上的 GameObjectProperty）+ 精灵图/动画/音效；
    /// 右栏：全兵种数值对比表 / 数据体检 / 设置（批量倍率、CSV、备份）。
    ///
    /// 数值改的是「单位预制体」里的组件字段，训练规则改的是 TroopDefinition 资产；
    /// 两类写入都走 SerializedObject（与 Inspector 一致，可 Ctrl+Z），并且每次批量/导入前自动备份。
    /// </summary>
    public sealed class UnitEditorWindow : EditorWindow
    {
        private const string MenuPath = "工具/兵种可视化编辑器 %&u";
        private const float MinListWidth = 240f;
        private const float MaxListWidth = 520f;
        private const float MinRightWidth = 320f;
        // 对比表 18 列宽度合计约 1100px；上限以前是 900，拉到最宽也看不到「冷却/产出/维护」这几列，
        // 只能靠横向滚动条去找。放宽到能一屏放下整张表。
        private const float MaxRightWidth = 1140f;

        #region 状态
        private readonly UnitCatalog _catalog = new UnitCatalog();

        private int _selectedIndex = -1;

        /// <summary>上一次绘制表单时对应的兵种下标：用于在「切换兵种」时清掉 IMGUI 的文本编辑焦点。</summary>
        private int _focusOwnerIndex = -2;
        private string _search = string.Empty;
        private string _raceFilter = string.Empty;      // 空 = 全部
        private int _barracksTierFilter;                // 兵营阶级筛选（= UnlockLevel），0 = 全部
        private int _barracksFilter;                    // 兵营归属筛选：0 全部 / 1 普通 / 2 黑暗 / 3 两者 / 4 未挂
        private int _groupMode;                         // 左栏分组方式：0 按种族 / 1 按兵营归属
        private readonly List<bool> _barracksFold = new List<bool>();

        private static readonly string[] BarracksFilterOptions = { "全部兵营", "普通兵营", "黑暗兵营", "未挂兵营" };
        private static readonly string[] GroupModeOptions = { "按种族分组", "按兵营分组" };

        // 兵营归属是互斥的：实测 4 个兵营预制体（Cat/Mouse × 普通/黑暗）共 44 个兵种，
        // 只挂普通 24 个、只挂黑暗 20 个、两边都挂 0 个 —— 所以这里只分三类，不做"两者都挂"。
        // （万一以后真有人把同一个兵种拖进两种兵营，行内角标会显示"普+暗"、体检也会提示。）
        private static readonly string[] BarracksSectionTitles = { "【普通兵营】", "【黑暗兵营】", "【未挂任何兵营】" };
        private bool _onlyWithIssues;

        /// <summary>列表行内是否显示训练 ID。</summary>
        private bool _showTrainingId = true;
        private readonly HashSet<int> _checked = new HashSet<int>();

        private Vector2 _listScroll;
        private Vector2 _formScroll;
        private Vector2 _issueScroll;
        private Vector2 _tableScroll;
        private Vector2 _settingsScroll;

        private List<UnitIssue> _issues = new List<UnitIssue>();
        private Dictionary<int, List<UnitIssue>> _issueMap = new Dictionary<int, List<UnitIssue>>();
        private bool _issuesDirty = true;

        private readonly List<bool> _raceFold = new List<bool>();

        private UnitStatSession _session;
        private int _sessionIndex = -1;

        private int _rightTab;                          // 0=对比表 1=体检 2=设置

        /// <summary>右栏是否收起。收起后中栏拿到全部宽度 —— 体检面板很占地方。</summary>
        private bool _rightCollapsed;
        private float _listWidth = 300f;
        private float _rightWidth = 560f;

        private string _status = string.Empty;
        private MessageType _statusType = MessageType.None;
        private bool _scanned;
        private bool _showRuntimeFields;
        private int _issueLevelFilter;      // 体检页的级别过滤：0=全部 1=错误 2=警告 3=提示
        private bool _dirtySinceSave;       // 本次打开窗口后是否改过东西（状态栏提示用）
        #endregion

        #region 生命周期
        [MenuItem(MenuPath, false, 101)]
        public static void OpenWindow()
        {
            var window = GetWindow<UnitEditorWindow>(false, "兵种编辑器", true);
            window.minSize = new Vector2(1120f, 580f);
            window.Show();
        }

        private void OnEnable()
        {
            _listWidth = UnitEditorPrefs.ListWidth;
            _rightCollapsed = UnitEditorPrefs.RightCollapsed;
            _showTrainingId = UnitEditorPrefs.ShowTrainingIdInList;
            _rightWidth = UnitEditorPrefs.RightWidth;
            if (!_scanned)
                Rescan(true);
        }

        private void OnDisable()
        {
            UnitEditorPrefs.ListWidth = _listWidth;
            UnitEditorPrefs.RightCollapsed = _rightCollapsed;
            UnitEditorPrefs.RightWidth = _rightWidth;
        }

        private void Rescan(bool silent)
        {
            UnitResourceIndex.Invalidate();
            UnitBarracksIndex.Invalidate();
            RefreshCatalogPreservingSelection();
            _scanned = true;

            if (!silent)
                SetStatus("已重新扫描：" + _catalog.Count + " 个兵种（" + _catalog.Races.Count + " 个种族）。", MessageType.Info);
        }

        /// <summary>
        /// 重建兵种目录，并按"资产路径"把当前选中的兵种重新定位回去。
        ///
        /// 为什么必须这么做：Rebuild 会按 (种族, 名称) 重新排序、数量也可能变化，
        /// 若继续沿用旧的 _selectedIndex / _checked，它们会指向**别的兵种** ——
        /// 紧接着点「批量操作」就会改错数据（这类操作会先备份、可撤销，但很容易没注意到）。
        /// 勾选一律清空（下标全部失效，宁可让策划重勾，也不能改错）。
        /// </summary>
        private void RefreshCatalogPreservingSelection()
        {
            string selectedKey = null;
            if (_selectedIndex >= 0 && _selectedIndex < _catalog.Count)
            {
                UnitRecord current = _catalog.Records[_selectedIndex];
                selectedKey = RecordKey(current);
            }

            _catalog.Rebuild();

            _checked.Clear();
            _focusOwnerIndex = -2;   // 让 OnGUI 的兜底逻辑清掉文本编辑焦点
            CloseSession();
            _issuesDirty = true;

            int newIndex = -1;
            if (!string.IsNullOrEmpty(selectedKey))
            {
                for (int i = 0; i < _catalog.Count; i++)
                {
                    if (string.Equals(RecordKey(_catalog.Records[i]), selectedKey, StringComparison.OrdinalIgnoreCase))
                    {
                        newIndex = i;
                        break;
                    }
                }
            }

            _selectedIndex = newIndex;
        }

        /// <summary>重定位选中项用的稳定键：优先训练配置资产路径，其次预制体路径。</summary>
        private static string RecordKey(UnitRecord record)
        {
            if (record == null)
                return null;

            return !string.IsNullOrEmpty(record.DefinitionPath) ? record.DefinitionPath : record.PrefabPath;
        }

        private void CloseSession()
        {
            _session = null;
            _sessionIndex = -1;
        }

        private UnitStatSession Session
        {
            get
            {
                if (_selectedIndex < 0 || _selectedIndex >= _catalog.Count)
                    return null;

                if (_session == null || _sessionIndex != _selectedIndex)
                {
                    _session = UnitStatSession.Open(_catalog.Records[_selectedIndex]);
                    _sessionIndex = _selectedIndex;
                }
                return _session;
            }
        }

        private void EnsureIssues()
        {
            if (!_issuesDirty)
                return;

            _issuesDirty = false;
            _issues = UnitValidator.Validate(_catalog);
            _issueMap = UnitValidator.GroupByRecord(_issues);
        }

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
        }

        /// <summary>当前选中的兵种（没有就 null）——「新建兵种」拿它当默认模板。</summary>
        private UnitRecord SelectedRecordOrNull()
        {
            return _selectedIndex >= 0 && _selectedIndex < _catalog.Count
                ? _catalog.Records[_selectedIndex]
                : null;
        }

        /// <summary>
        /// 「新建兵种」完成后：重扫目录并把新兵种选中，省得再自己找一遍。
        /// 新预制体是本工具刚写出来的，重扫后必然在列表里。
        /// </summary>
        private void OnUnitCreated(string prefabPath)
        {
            Rescan(false);

            for (int i = 0; i < _catalog.Count; i++)
            {
                UnitRecord record = _catalog.Records[i];
                if (record != null && string.Equals(record.PrefabPath, prefabPath, StringComparison.OrdinalIgnoreCase))
                {
                    SelectUnit(i);
                    SetStatus("已新建兵种并选中它：" + record.PrefabName +
                              "。战斗数值在「战斗数值」页调，资源键与训练规则已按向导填好。", MessageType.Info);
                    Repaint();
                    return;
                }
            }

            SetStatus("兵种已新建（" + prefabPath + "），但重扫后没在列表里找到它 —— 点「重新扫描」看看。",
                MessageType.Warning);
        }
        /// <summary>
        /// 切换当前编辑的兵种。必须清掉 IMGUI 的文本编辑焦点：
        /// 否则在数值输入框里还带着光标时点另一个兵种，那个输入框会沿用上一个兵种的编辑缓冲，
        /// 既显示错内容，还可能把旧数值写进新选中的兵种。
        /// </summary>
        private void SelectUnit(int index)
        {
            if (_selectedIndex == index)
                return;

            ClearEditorFocus();
            _selectedIndex = index;
            _focusOwnerIndex = index;
            _session = null;
            _sessionIndex = -1;

            // 选键窗口是常驻的独立窗口，标题里写着"正在填：兵营图标 → Miner rat"。
            // 换了兵种就该关掉它 —— 否则窗口标题还指着上一个兵种，看起来像还在改那一个
            // （真正的保护是 IsStillCurrentUnit，但让窗口跟着一起收掉更不容易误会）。
            UnitKeyPickerWindow.CloseIfOpen();
        }

        /// <summary>丢弃当前文本框的编辑状态（光标 / 选区 / 编辑缓冲）。</summary>
        private static void ClearEditorFocus()
        {
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
        }
        #endregion

        #region 主循环
        private void OnGUI()
        {
            // 兜底：选中项若是别处改的（重新扫描后下标变化等），也清一次编辑焦点
            if (_focusOwnerIndex != _selectedIndex)
            {
                ClearEditorFocus();
                _focusOwnerIndex = _selectedIndex;
                _session = null;
                _sessionIndex = -1;
            }

            if (Event.current.type == EventType.KeyDown && Event.current.control && Event.current.keyCode == KeyCode.S)
            {
                SaveAll();
                Event.current.Use();
            }

            DrawToolbar();

            if (_catalog.Count == 0)
            {
                EditorGUILayout.HelpBox("还没有扫描到兵种。\n\n" +
                                        "本工具从两处取数据：\n" +
                                        "· " + UnitCatalog.PrefabRoot + "（单位预制体，数值在这里）\n" +
                                        "· " + UnitCatalog.DefinitionRoot + "（TroopDefinition 训练配置）\n\n" +
                                        "如果这里显示 0，说明资源还没导入完或注册表未生成。", MessageType.Warning);
                if (GUILayout.Button("重新扫描兵种", GUILayout.Height(26f)))
                    Rescan(false);
                return;
            }

            EnsureIssues();

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.Width(_listWidth), GUILayout.ExpandHeight(true));
            DrawLeftColumn();
            EditorGUILayout.EndVertical();

            Rect splitter1 = GUILayoutUtility.GetRect(5f, 5f, GUILayout.Width(5f), GUILayout.ExpandHeight(true));
            _listWidth = UnitEditorUI.Splitter(splitter1, _listWidth, MinListWidth, MaxListWidth);

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawMiddleColumn();
            EditorGUILayout.EndVertical();

            // 收起时整条右栏不画（连分割条也不画），中栏自动拿到全部宽度
            if (!_rightCollapsed)
            {
                Rect splitter2 = GUILayoutUtility.GetRect(5f, 5f, GUILayout.Width(5f), GUILayout.ExpandHeight(true));
                _rightWidth = UnitEditorUI.Splitter(splitter2, _rightWidth, MinRightWidth, MaxRightWidth);
    
                EditorGUILayout.BeginVertical(GUILayout.Width(_rightWidth), GUILayout.ExpandHeight(true));
                DrawRightColumn();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndHorizontal();

            DrawStatusBar();
        }
        #endregion

        #region 工具栏
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("重新扫描", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                Rescan(false);

            // 新建兵种（一条龙）：从现有兵种复制出预制体 + 建训练配置 + 登记资源键 + 挂进兵营。
            // 这是本工具唯一会**创建预制体**的入口，所以单独摆一个按钮、并用「试运行」先给计划。
            if (GUILayout.Button(new GUIContent("新建兵种…",
                    "从现有兵种复制出一个新的单位预制体，并把训练配置、PrefabRegistry、兵营挂载一次接好\n" +
                    "（会先给一份计划让你确认：写哪些文件、动哪些已有资产、建完还差什么）"),
                    EditorStyles.toolbarButton, GUILayout.Width(78f)))
            {
                UnitEditorNewUnitWindow.Open(_catalog, SelectedRecordOrNull(), OnUnitCreated);
            }

            if (GUILayout.Button("保存全部" + (_dirtySinceSave ? " *" : ""), EditorStyles.toolbarButton, GUILayout.Width(66f)))
                SaveAll();

            if (GUILayout.Button("撤销", EditorStyles.toolbarButton, GUILayout.Width(44f)))
                Undo.PerformUndo();

            if (GUILayout.Button("全选显示项", EditorStyles.toolbarButton, GUILayout.Width(78f)))
                CheckAllVisible(true);

            if (GUILayout.Button("取消勾选", EditorStyles.toolbarButton, GUILayout.Width(62f)))
                _checked.Clear();

            GUILayout.FlexibleSpace();

            int errors, warnings, infos;
            UnitValidator.Count(_issues, out errors, out warnings, out infos);

            GUILayout.Label("体检", EditorStyles.miniLabel, GUILayout.Width(28f));

            // 这三块以前是用 Label 画成"看起来像按钮但点不动"的样子 —— 几乎每个人都会去点它。
            // 现在是真的按钮：点一下切到体检页并按级别过滤。
            GUI.contentColor = errors > 0 ? UnitEditorUI.Error : UnitEditorUI.Ok;
            if (GUILayout.Button("错误 " + errors, EditorStyles.toolbarButton, GUILayout.Width(58f)))
                FocusIssues(1);
            GUI.contentColor = warnings > 0 ? UnitEditorUI.Warn : UnitEditorUI.Ok;
            if (GUILayout.Button("警告 " + warnings, EditorStyles.toolbarButton, GUILayout.Width(58f)))
                FocusIssues(2);
            GUI.contentColor = UnitEditorUI.Ok;
            if (GUILayout.Button("提示 " + infos, EditorStyles.toolbarButton, GUILayout.Width(58f)))
                FocusIssues(3);
            GUI.contentColor = Color.white;

            if (_rightCollapsed &&
                GUILayout.Button("▸ 展开右栏", EditorStyles.toolbarButton, GUILayout.Width(84f)))
            {
                SetRightCollapsed(false);
            }

            if (GUILayout.Button("一键修复", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                RunAutoFix();

            if (GUILayout.Button("表格", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                ShowCsvMenu();

            if (GUILayout.Button("备份", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                ShowBackupMenu();

            if (GUILayout.Button("设置", EditorStyles.toolbarButton, GUILayout.Width(40f)))
            {
                SetRightCollapsed(false);
                _rightTab = 2;
                Repaint();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void SaveAll()
        {
            if (UnitEditorPrefs.AutoBackupOnSave)
            {
                var paths = new List<string>();
                for (int i = 0; i < _catalog.Count; i++)
                {
                    UnitRecord record = _catalog.Records[i];
                    if (record.HasDefinition)
                        paths.Add(record.DefinitionPath);
                    if (record.HasPrefab)
                        paths.Add(record.PrefabPath);
                }

                if (paths.Count > 0 && EditorUtility.DisplayDialog("保存并备份",
                        "将保存全部改动，并把 " + _catalog.Count + " 个兵种的预制体与训练配置备份到：\n" + UnitEditorPrefs.BackupFolder +
                        "\n\n（每个资产保留最新 " + UnitEditorPrefs.KeepBackupCount + " 份，更早的自动清理）",
                        "保存并备份", "只保存，不备份"))
                {
                    int n = UnitBackup.BackupAssets(paths, "手动保存");
                    Debug.Log("[兵种编辑器] 已备份 " + n + " 个资产到 " + UnitEditorPrefs.BackupFolder);
                }
            }

            AssetDatabase.SaveAssets();
            _catalog.InvalidateStats();
            _dirtySinceSave = false;
            SetStatus("已保存（AssetDatabase.SaveAssets）。", MessageType.Info);
        }

        private void RunAutoFix()
        {
            EnsureIssues();
            int fixable = 0;
            for (int i = 0; i < _issues.Count; i++)
            {
                if (_issues[i] != null && _issues[i].Fixable)
                    fixable++;
            }

            if (fixable == 0)
            {
                SetStatus("没有可自动修复的问题（数值类问题需要你自己判断，工具不会替你改平衡）。", MessageType.Info);
                return;
            }

            if (!EditorUtility.DisplayDialog("一键修复",
                    "将自动修复 " + fixable + " 处训练配置问题（补 displayName / id / prefabKey）。\n\n" +
                    "数值（HP、攻击、射程、移速等）一律不动，可 Ctrl+Z 撤销。",
                    "开始修复", "取消"))
                return;

            int fixedCount = UnitValidator.ApplySafeFixes(_catalog, _issues);

            // 一键修复里有一条会**改预制体**（无效投射物键 → 写 GameObjectProperty.atkObj，
            // 见 UnitEditorValidation.FixProjectileKey），而体检读的是 `UnitStats` 数值快照。
            // 不失效缓存的话，下一次体检拿到的还是修复前的值 → **同一条错误永久复报**，
            // 反复点一键修复都"修不掉"（状态栏说已修复、错误却还在）。
            // 其他所有写预制体的路径都调了 InvalidateStats（本文件 :395 / 892 / 1341 / 2249 / 2327
            // 以及 UnitEditorCsv.cs:422），唯独一键修复这条漏了。
            _catalog.InvalidateStats();

            _issuesDirty = true;
            if (fixedCount > 0)
                MarkDirty();
            CloseSession();
            SetStatus("已修复 " + fixedCount + " 处训练配置。", MessageType.Info);
            Repaint();
        }
        #endregion

        #region 左栏
        private void DrawLeftColumn()
        {
            UnitEditorUI.Section("兵种列表", "共 " + _catalog.Count + " 个");

            EditorGUILayout.BeginHorizontal();
            _search = UnitEditorUI.SearchField(_search, "搜索：训练ID / 名称 / 预制体名 / 资源键（回车定位）");
            bool locate = GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(38f));
            EditorGUILayout.EndHorizontal();

            // 不要在打字过程中自动跳选：搜 "miner_rat" 时，打到某个前缀如果恰好等于另一个兵种的 id，
            // 选中会被抢走、搜索框同时失焦，后面的按键就落到窗口上了（可能撞到 Ctrl+S 之类的快捷键）。
            // 只有按回车、或点「定位」才跳。
            bool locateByEnter = Event.current.type == EventType.KeyDown &&
                                 (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter) &&
                                 !string.IsNullOrEmpty(_search);
            if (locate || locateByEnter)
            {
                TrySelectByExactTrainingId(_search);
                Event.current.Use();
            }

            EditorGUILayout.BeginHorizontal();
            var raceOptions = new List<string> { "全部种族" };
            raceOptions.AddRange(_catalog.Races);
            int raceIndex = Mathf.Max(0, raceOptions.IndexOf(_raceFilter));
            raceIndex = EditorGUILayout.Popup(raceIndex, raceOptions.ToArray(), GUILayout.Width(112f));
            _raceFilter = raceIndex <= 0 ? string.Empty : raceOptions[raceIndex];

            // 「阶级」一律指**兵营阶级**：兵营训练面板就是按 UnlockLevel 分成 1/2/3 三排的
            // （BuildingTraining.GetTroop(row) 里是 troop.UnlockLevel != row）。
            // 这里以前筛的是 TroopDefinition.tier —— 那是敌方 AI 的选兵权重（max(0,tier)*10），
            // 和玩家在兵营里看到的排数没有关系，按它筛会把两个概念混在一起。
            var tierOptions = new[] { "全部兵营阶级", "兵营 1 阶", "兵营 2 阶", "兵营 3 阶", "无训练配置" };
            _barracksTierFilter = EditorGUILayout.Popup(_barracksTierFilter, tierOptions, GUILayout.Width(112f));
            EditorGUILayout.EndHorizontal();

            // 普通 / 黑暗兵营的归属不在兵种上，而在兵营预制体上（BuildingTraining.isDarkBarracks + troops），
            // 所以这里按 UnitBarracksIndex 扫出来的归属来筛，并支持把左栏按归属分节查看。
            EditorGUILayout.BeginHorizontal();
            _barracksFilter = EditorGUILayout.Popup(_barracksFilter, BarracksFilterOptions, GUILayout.Width(112f));
            _groupMode = EditorGUILayout.Popup(_groupMode, GroupModeOptions, GUILayout.Width(112f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(BarracksSummary(), EditorStyles.miniLabel);

            _onlyWithIssues = EditorGUILayout.ToggleLeft("只看有问题的兵种（" + _issueMap.Count + " 个）", _onlyWithIssues);
            bool showTrainingId = EditorGUILayout.ToggleLeft("行内显示训练 ID（拿 id 找人时打开）", _showTrainingId);
            if (showTrainingId != _showTrainingId)
            {
                _showTrainingId = showTrainingId;
                UnitEditorPrefs.ShowTrainingIdInList = showTrainingId;   // 只在变化时写 EditorPrefs
            }

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
            // 搜索时给平铺结果（找东西时不需要分节），否则按选的分组方式分节；
            // 分节视图内部也会应用全部筛选条件（见 DrawGroupedList / DrawGroupedListByBarracks）。
            if (!string.IsNullOrEmpty(_search))
                DrawFlatList();
            else if (_groupMode == 1)
                DrawGroupedListByBarracks();
            else
                DrawGroupedList();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("批量操作 ▾", GUILayout.Height(22f)))
                ShowBatchMenu();
            if (GUILayout.Button("选中项对比", GUILayout.Height(22f)))
            {
                _rightTab = 0;
                SetRightCollapsed(false);
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("批量补全训练 ID（空的 + 重复的）", GUILayout.Height(20f)))
                FillTrainingIds();

            if (GUILayout.Button("为缺失的兵种新建训练配置", GUILayout.Height(20f)))
                CreateMissingDefinitions();

            EditorGUILayout.LabelField("勾选 = 批量操作对象（当前 " + _checked.Count + " 个）", EditorStyles.miniLabel);
        }

        /// <summary>按种族分节（节内按兵营阶级排）。筛选条件同样生效，被筛空的种族不再显示空节。</summary>
        private void DrawGroupedList()
        {
            List<int> visible = CollectVisibleIndices();

            while (_raceFold.Count < _catalog.Races.Count)
                _raceFold.Add(true);

            for (int r = 0; r < _catalog.Races.Count; r++)
            {
                string race = _catalog.Races[r];
                var indices = new List<int>();
                for (int i = 0; i < visible.Count; i++)
                {
                    if (string.Equals(_catalog.Records[visible[i]].Race, race, StringComparison.OrdinalIgnoreCase))
                        indices.Add(visible[i]);
                }

                if (indices.Count == 0)
                    continue;

                DrawListSection("【" + race + "】 " + indices.Count + " 个", indices, _raceFold, r);
            }
        }

        /// <summary>按兵营归属分节：普通 / 黑暗 / 两者都挂 / 没挂兵营（四类互不重叠）。</summary>
        private void DrawGroupedListByBarracks()
        {
            List<int> visible = CollectVisibleIndices();

            while (_barracksFold.Count < BarracksSectionTitles.Length)
                _barracksFold.Add(true);

            for (int s = 0; s < BarracksSectionTitles.Length; s++)
            {
                var indices = new List<int>();
                for (int i = 0; i < visible.Count; i++)
                {
                    if (BarracksSectionOf(_catalog.Records[visible[i]]) == s)
                        indices.Add(visible[i]);
                }

                DrawListSection(BarracksSectionTitles[s] + "（" + indices.Count + " 个）", indices, _barracksFold, s);
            }
        }

        /// <summary>分节标题 + 折叠 + 行；空节也会显示（0 个），方便一眼看出"这类一个都没有"。</summary>
        private void DrawListSection(string title, List<int> indices, List<bool> foldStates, int foldIndex)
        {
            bool fold = foldStates[foldIndex];
            EditorGUILayout.BeginHorizontal();
            fold = EditorGUILayout.Foldout(fold, title, true, EditorStyles.foldoutHeader);
            EditorGUILayout.EndHorizontal();
            foldStates[foldIndex] = fold;

            if (!fold)
                return;

            EditorGUI.indentLevel++;
            for (int i = 0; i < indices.Count; i++)
                DrawUnitRow(indices[i]);
            EditorGUI.indentLevel--;
        }

        /// <summary>兵营归属分节编号：0 挂普通、1 只挂黑暗、2 没挂任何兵营（归属互斥，没有"两者"这一类）。</summary>
        private static int BarracksSectionOf(UnitRecord record)
        {
            BarracksInfo info = UnitBarracksIndex.Get(record);
            if (info.HasNormal)
                return 0;
            if (info.HasDark)
                return 1;

            return 2;
        }

        /// <summary>兵营归属筛选（0=全部，其余与 BarracksFilterOptions 下标一致）。</summary>
        private bool MatchesBarracksFilter(UnitRecord record)
        {
            if (_barracksFilter == 0)
                return true;

            BarracksInfo info = UnitBarracksIndex.Get(record);
            switch (_barracksFilter)
            {
                case 1: return info.HasNormal;
                case 2: return info.HasDark;
                default: return !info.IsWired;
            }
        }

        /// <summary>三类兵营归属的数量概览（按整个目录统计，不受当前筛选影响）。</summary>
        private string BarracksSummary()
        {
            var counts = new int[BarracksSectionTitles.Length];
            for (int i = 0; i < _catalog.Count; i++)
                counts[BarracksSectionOf(_catalog.Records[i])]++;

            return "归属分布（全部 " + _catalog.Count + " 个）：普通 " + counts[0] + "　黑暗 " + counts[1] +
                   "　未挂 " + counts[2] + "（未挂 = 玩家在兵营里看不到）";
        }

        private void DrawFlatList()
        {
            List<int> indices = CollectVisibleIndices();
            EditorGUILayout.LabelField("筛选结果 " + indices.Count + " 个", EditorStyles.miniLabel);

            for (int i = 0; i < indices.Count; i++)
                DrawUnitRow(indices[i]);
        }

        private List<int> CollectVisibleIndices()
        {
            var result = new List<int>();
            string query = (_search ?? string.Empty).Trim();

            for (int i = 0; i < _catalog.Count; i++)
            {
                UnitRecord record = _catalog.Records[i];

                if (!string.IsNullOrEmpty(_raceFilter) && !string.Equals(record.Race, _raceFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_barracksTierFilter != 0)
                {
                    if (_barracksTierFilter == 4)
                    {
                        if (record.HasDefinition)
                            continue;
                    }
                    else
                    {
                        TroopDefinition definition = record.Definition;
                        // 按兵营阶级（UnlockLevel）严格相等筛：兵营面板只有 1/2/3 三排，
                        // 越界的话（0、4…）在面板上根本不会出现，体检里会单独报警。
                        if (definition == null || definition.UnlockLevel != _barracksTierFilter)
                            continue;
                    }
                }

                if (_onlyWithIssues && !_issueMap.ContainsKey(i))
                    continue;

                if (!MatchesBarracksFilter(record))
                    continue;

                if (!string.IsNullOrEmpty(query) && !Matches(record, query))
                    continue;

                result.Add(i);
            }

            result.Sort((a, b) => CompareByBarracksTier(a, b));
            return result;
        }

        /// <summary>
        /// 列表里的"阶级"顺序一律按**兵营阶级**（UnlockLevel，也就是兵营面板的 1/2/3 三排）排，
        /// 不再按 AI 权重阶级（tier）—— 策划是按兵营里的排数找兵的。
        /// 同级内先按 AI 阶级、再按名称，保证顺序稳定可预测。
        /// </summary>
        private int CompareByBarracksTier(int a, int b)
        {
            TroopDefinition da = _catalog.Records[a].Definition;
            TroopDefinition db = _catalog.Records[b].Definition;

            int ua = da != null ? da.UnlockLevel : int.MaxValue;   // 没有训练配置的排到最后
            int ub = db != null ? db.UnlockLevel : int.MaxValue;
            if (ua != ub)
                return ua.CompareTo(ub);

            int ta = da != null ? da.Tier : 0;
            int tb = db != null ? db.Tier : 0;
            if (ta != tb)
                return ta.CompareTo(tb);

            return string.Compare(_catalog.Records[a].Name, _catalog.Records[b].Name, StringComparison.CurrentCulture);
        }

        /// <summary>
        /// 搜索词与某个兵种的训练 ID（TroopDefinition.id）或名称完全相同时直接选中它 ——
        /// "拿着一个 id 来找兵种"时不用再在两栏里点来点去。
        /// </summary>
        private void TrySelectByExactTrainingId(string query)
        {
            if (string.IsNullOrEmpty(query))
                return;

            string trimmed = query.Trim();
            if (trimmed.Length < 3)
                return;

            for (int i = 0; i < _catalog.Count; i++)
            {
                UnitRecord record = _catalog.Records[i];
                if (string.Equals(record.DefinitionId, trimmed, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(record.Id, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    SelectUnit(i);
                    SetStatus("已按训练 ID 定位到：" + (string.IsNullOrEmpty(record.Name) ? record.PrefabName : record.Name) +
                              "（" + trimmed + "）", MessageType.Info);
                    return;
                }
            }
        }

        private static bool Matches(UnitRecord record, string query)
        {
            return Contains(record.Name, query) || Contains(record.Id, query) || Contains(record.PrefabName, query) ||
                   Contains(record.Race, query) || Contains(record.PrefabPath, query) || Contains(record.DefinitionPath, query) ||
                   Contains(record.DefinitionId, query);
        }

        private static bool Contains(string source, string query)
        {
            return !string.IsNullOrEmpty(source) && source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawUnitRow(int index)
        {
            UnitRecord record = _catalog.Records[index];
            UnitStats stats = _catalog.GetStats(record);
            TroopDefinition definition = record.Definition;

            Rect row = GUILayoutUtility.GetRect(0f, _showTrainingId ? 40f : 28f, GUILayout.ExpandWidth(true));
            bool selected = index == _selectedIndex;
            List<UnitIssue> recordIssues;
            bool hasIssues = _issueMap.TryGetValue(index, out recordIssues) && recordIssues != null && recordIssues.Count > 0;

            if (selected)
                UnitEditorUI.FillRect(row, UnitEditorUI.Selected);
            else if (row.Contains(Event.current.mousePosition))
                UnitEditorUI.FillRect(row, new Color(1f, 1f, 1f, 0.06f));

            var checkRect = new Rect(row.x + 2f, row.y + 6f, 15f, 15f);
            bool wasChecked = _checked.Contains(index);
            bool nowChecked = EditorGUI.Toggle(checkRect, wasChecked);
            if (nowChecked != wasChecked)
            {
                if (nowChecked) _checked.Add(index);
                else _checked.Remove(index);
            }

            var thumbRect = new Rect(row.x + 21f, row.y + 3f, 22f, 22f);
            Sprite icon = definition != null && !string.IsNullOrEmpty(definition.IconKey)
                ? UnitResourceIndex.Instance.GetSprite(definition.IconKey)
                : null;
            UnitEditorUI.DrawSpriteFit(thumbRect, icon, Color.white, 1f);

            string name = string.IsNullOrEmpty(record.Name) ? record.PrefabName : record.Name;
            // 右侧留 100px：48px 给兵营归属角标，其余给问题圆点，避免长名字和角标叠在一起
            GUI.Label(new Rect(row.x + 47f, row.y + 2f, row.width - 100f, 15f), name, EditorStyles.miniBoldLabel);

            // 行内的「阶级」显示兵营阶级（= 解锁兵营等级，兵营面板的第几排）。
            // AI 权重阶级只在两者不一致时补一个短标记，避免把两个概念看成一个。
            string tier;
            if (definition == null)
            {
                tier = "无配置";
            }
            else
            {
                tier = "兵营" + definition.UnlockLevel + "阶";
                if (definition.Tier != definition.UnlockLevel)
                    tier += "·AI" + definition.Tier;
            }
            string sub = tier + "　HP " + (stats.Valid ? stats.MaxHp.ToString() : "?") + "　ATK " + (stats.Valid ? stats.Atk.ToString() : "?");
            if (_showTrainingId)
            {
                string trainingId = string.IsNullOrEmpty(record.DefinitionId)
                    ? (definition != null ? "（训练 ID 未填）" : "（无训练配置）")
                    : record.DefinitionId;
                GUI.Label(new Rect(row.x + 47f, row.y + 15f, row.width - 92f, 12f), "ID: " + trainingId, EditorStyles.miniLabel);
                GUI.Label(new Rect(row.x + 47f, row.y + 26f, row.width - 92f, 12f), sub, EditorStyles.miniLabel);
            }
            else
            {
                GUI.Label(new Rect(row.x + 47f, row.y + 15f, row.width - 92f, 12f), sub, EditorStyles.miniLabel);
            }

            if (hasIssues)
            {
                Color c = UnitEditorUI.Muted;
                for (int i = 0; i < recordIssues.Count; i++)
                {
                    if (recordIssues[i].Level == UnitIssueLevel.Error) { c = UnitEditorUI.Error; break; }
                    if (recordIssues[i].Level == UnitIssueLevel.Warning) c = UnitEditorUI.Warn;
                }
                UnitEditorUI.FillRect(new Rect(row.xMax - 10f, row.y + 10f, 8f, 8f), c);
            }

            // 兵营归属角标：归属不在兵种资产上（在兵营预制体上），列表里不标就只能选中后去中栏看。
            // 位置在名称行右侧空出来的 45px 里，不会和名称/问题点重叠。
            BarracksInfo barracks = UnitBarracksIndex.Get(record);
            if (barracks.IsWired)
            {
                bool both = barracks.HasNormal && barracks.HasDark;
                string tag = both ? "普+暗" : (barracks.HasNormal ? "普通" : "黑暗");
                GUI.contentColor = barracks.HasDark && !barracks.HasNormal
                    ? new Color(0.75f, 0.7f, 0.95f, 1f)      // 只挂黑暗
                    : new Color(0.7f, 0.9f, 0.75f, 1f);      // 普通 / 两者
                GUI.Label(new Rect(row.xMax - 48f, row.y + 2f, 36f, 13f), tag, EditorStyles.miniLabel);
                GUI.contentColor = Color.white;
            }
            else
            {
                GUI.contentColor = UnitEditorUI.Warn;
                GUI.Label(new Rect(row.xMax - 48f, row.y + 2f, 36f, 13f), "未挂", EditorStyles.miniLabel);
                GUI.contentColor = Color.white;
            }

            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition) && !checkRect.Contains(Event.current.mousePosition))
            {
                SelectUnit(index);
                Event.current.Use();
                Repaint();
            }
        }

        private void CheckAllVisible(bool value)
        {
            List<int> indices = CollectVisibleIndices();
            for (int i = 0; i < indices.Count; i++)
            {
                if (value) _checked.Add(indices[i]);
                else _checked.Remove(indices[i]);
            }
        }
        #endregion

        #region 中栏：表单
        private void DrawMiddleColumn()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _catalog.Count)
            {
                EditorGUILayout.Space(20f);
                EditorGUILayout.HelpBox(
                    "左侧点选一个兵种开始调值。\n\n" +
                    "· 「战斗数值」改的是单位预制体上的 GameObjectProperty（生命/攻击/射程/移速…）\n" +
                    "· 「训练配置」改的是 TroopDefinition 资产（兵营阶级/冷却/图标…）\n" +
                    "· 「精灵图 / 动画 / 音效」是各类资源键，点「搜索」按键名选\n" +
                    "· 所有改动可 Ctrl+Z；工具栏「保存全部」落盘前会提示备份\n\n" +
                    "注意：角色的实际出手节奏由攻击动画事件驱动（CharacterAI），atkRate 目前只影响哨塔与 Buff 计算。",
                    MessageType.Info);
                return;
            }

            UnitRecord record = _catalog.Records[_selectedIndex];
            UnitStatSession session = Session;

            _formScroll = EditorGUILayout.BeginScrollView(_formScroll);
            DrawHeader(record, session);

            if (session != null && !session.Valid)
            {
                EditorGUILayout.HelpBox(session.Error, MessageType.Error);
                DrawDefinitionSection(record);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawDefinitionSection(record);
            DrawStatsSection(session);
            DrawVisualSection(session);
            DrawExtraSection(session);

            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("在资源里定位预制体", GUILayout.Height(24f)) && record.HasPrefab)
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(record.PrefabPath));
            if (GUILayout.Button("定位训练配置", GUILayout.Height(24f)) && record.HasDefinition)
                EditorGUIUtility.PingObject(record.Definition);
            if (GUILayout.Button("重新读取", GUILayout.Height(24f)))
            {
                _catalog.InvalidateStats();
                if (session != null)
                    session.Reload();
                _issuesDirty = true;
                SetStatus("已重新读取该兵种数据。", MessageType.Info);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader(UnitRecord record, UnitStatSession session)
        {
            EditorGUILayout.BeginHorizontal();

            TroopDefinition definition = record.Definition;
            Sprite icon = definition != null && !string.IsNullOrEmpty(definition.IconKey)
                ? UnitResourceIndex.Instance.GetSprite(definition.IconKey)
                : null;

            var rect = GUILayoutUtility.GetRect(72f, 72f, GUILayout.Width(72f), GUILayout.Height(72f));
            UnitEditorUI.FillRect(rect, new Color(0f, 0f, 0f, 0.2f));
            UnitEditorUI.DrawSpriteFit(rect, icon, Color.white, 2f);

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(string.IsNullOrEmpty(record.Name) ? record.PrefabName : record.Name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("种族：" + record.Race + "　预制体：" + (record.HasPrefab ? record.PrefabName : "（找不到）"), EditorStyles.miniLabel);
            EditorGUILayout.LabelField("训练配置：" + (record.HasDefinition ? Path.GetFileName(record.DefinitionPath) : "（没有，玩家训练不到）"), EditorStyles.miniLabel);

            // 训练 ID 单独一行、可选中复制（关卡/敌方队伍配置按它引用兵种，常要拿去对）
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("训练 ID：", EditorStyles.miniLabel, GUILayout.Width(56f));
            EditorGUILayout.SelectableLabel(
                string.IsNullOrEmpty(record.DefinitionId) ? "（未填）" : record.DefinitionId,
                EditorStyles.miniLabel, GUILayout.Height(15f));
            EditorGUILayout.EndHorizontal();

            List<UnitIssue> recordIssues;
            if (_issueMap.TryGetValue(_selectedIndex, out recordIssues) && recordIssues.Count > 0)
            {
                for (int i = 0; i < recordIssues.Count && i < 3; i++)
                {
                    GUI.contentColor = recordIssues[i].LevelColor;
                    EditorGUILayout.LabelField("· " + recordIssues[i].Title, EditorStyles.miniLabel);
                    GUI.contentColor = Color.white;
                }
                if (recordIssues.Count > 3)
                    EditorGUILayout.LabelField("· …还有 " + (recordIssues.Count - 3) + " 条，见右栏「数据体检」", EditorStyles.miniLabel);
            }
            else
            {
                GUI.contentColor = UnitEditorUI.Ok;
                EditorGUILayout.LabelField("· 这个兵种没有发现问题", EditorStyles.miniLabel);
                GUI.contentColor = Color.white;
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            if (session != null && session.Stats != null && session.Stats.Valid)
            {
                UnitStats s = session.Stats;
                UnitEditorUI.KeyValue("DPS 粗算", s.RoughDps.ToString("0.#") + "（攻击 × 攻速，仅供参考）", 60f);
            }
            EditorGUILayout.Space(4f);
        }

        #region 训练配置
        private void DrawDefinitionSection(UnitRecord record)
        {
            UnitEditorUI.Section("训练配置（TroopDefinition）", "决定兵营里怎么训练、什么时候解锁");

            TroopDefinition definition = record.Definition;
            if (definition == null)
            {
                EditorGUILayout.HelpBox("这个兵种没有训练配置：玩家在兵营里训练不到它，图鉴也无法用它的键解锁。\n" +
                                        "点左下角「为缺失的兵种新建训练配置」可以一键补齐。", MessageType.Warning);
                return;
            }

            var so = new SerializedObject(definition);

            DefString(so, definition, "displayName", "显示名");
            DefString(so, definition, "id", "训练 ID", "存档与关卡配置用的稳定标识，改了会影响已有引用。");
            DefEnum(so, definition, "tag", "定位标签", "敌方 AI 判断兵种作用与转产克制用。");
            DefFloat(so, definition, "threatScore", "威胁权重", "对建筑的威胁评估：攻击力 × 该权重计入威胁值。");
            DefInt(so, definition, "unlockLevel", "兵营阶级（解锁兵营等级 1-3）",
                "兵营训练面板就是按这个分排的：BuildingTraining.GetTroop(row) 取的是 UnlockLevel == row 的兵种，\n" +
                "所以这里填几，玩家就在兵营面板的第几排看到它；兵营等级不足时显示为未解锁。\n" +
                "（兵营显示等级 = BuildUP 等级 + 1）");
            DefInt(so, definition, "tier", "AI 威胁阶级（1-3，敌方 AI 选兵用）",
                "只影响敌方 AI 的选兵评分（max(0,tier) × 10，越高 AI 越愿意生产它），\n" +
                "和玩家在兵营里看到的排数没有关系 —— 那个由上面的「兵营阶级」决定。");
            DefInt(so, definition, "trainCount", "单次产出数量");
            DefFloat(so, definition, "cooldown", "训练冷却（秒）");
            DefInt(so, definition, "upkeep", "维护费", "选定该兵种后每秒扣除的资源获取量。");
            DefBool(so, definition, "requiresOwnership", "需要玩家拥有后才能训练");

            EditorGUILayout.Space(4f);
            DrawBarracksRow(record);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("训练配置里的资源键", EditorStyles.miniBoldLabel);
            DefKey(so, definition, "iconKey", UnitResKind.Sprite, "兵营图标");
            DefKey(so, definition, "animationSpriteKey", UnitResKind.Sprite, "基础动画贴图");
            DefKey(so, definition, "animatorControllerKey", UnitResKind.AnimatorController, "动画控制器");
            DefKey(so, definition, "prefabKey", UnitResKind.Prefab, "单位预制体");
        }

        /// <summary>显示该兵种挂在哪个兵营（普通/黑暗），没挂的标红提醒。</summary>
        private void DrawBarracksRow(UnitRecord record)
        {
            BarracksInfo info = UnitBarracksIndex.Get(record);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("兵营归属", GUILayout.Width(150f));
            GUI.contentColor = info.IsWired ? UnitEditorUI.Ok : UnitEditorUI.Warn;
            EditorGUILayout.LabelField(info.Detail, UnitEditorUI.MiniLabel);
            GUI.contentColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private void DefString(SerializedObject so, UnityEngine.Object target, string field, string label, string tooltip = null)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                return;

            EditorGUI.BeginChangeCheck();
            string value = EditorGUILayout.TextField(new GUIContent(label, tooltip), prop.stringValue);
            if (EditorGUI.EndChangeCheck())
            {
                prop.stringValue = value.Trim();
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                _issuesDirty = true;
                MarkDirty();

                // id / displayName 是左栏列表、标题、对比表「训练ID」列和"按训练 ID 定位"直接读的缓存字段，
                // 不同步的话改完界面还显示旧值（看起来像没保存/修复没生效）。
                if (field == "id" || field == "displayName")
                    SyncSelectedRecordCache();
            }
        }

        /// <summary>把当前选中兵种的目录缓存与资产对齐（改完 id / displayName 后调用）。</summary>
        private void SyncSelectedRecordCache()
        {
            if (_selectedIndex < 0 || _selectedIndex >= _catalog.Count)
                return;

            _catalog.Records[_selectedIndex].SyncDefinitionCache();
        }

        private void DefInt(SerializedObject so, UnityEngine.Object target, string field, string label, string tooltip = null, int min = int.MinValue, int max = int.MaxValue)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                return;

            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.IntField(new GUIContent(label, tooltip), prop.intValue);
            if (EditorGUI.EndChangeCheck())
            {
                prop.intValue = Mathf.Clamp(value, min, max);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                _issuesDirty = true;
                MarkDirty();
            }
        }

        private void DefFloat(SerializedObject so, UnityEngine.Object target, string field, string label, string tooltip = null)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                return;

            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.FloatField(new GUIContent(label, tooltip), prop.floatValue);
            if (EditorGUI.EndChangeCheck())
            {
                prop.floatValue = value;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                _issuesDirty = true;
                MarkDirty();
            }
        }

        private void DefBool(SerializedObject so, UnityEngine.Object target, string field, string label, string tooltip = null)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                return;

            EditorGUI.BeginChangeCheck();
            bool value = EditorGUILayout.Toggle(new GUIContent(label, tooltip), prop.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                prop.boolValue = value;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                _issuesDirty = true;
                MarkDirty();
            }
        }

        private void DefEnum(SerializedObject so, UnityEngine.Object target, string field, string label, string tooltip = null)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                return;

            EditorGUI.BeginChangeCheck();

            // 用 intValue 读写枚举的"数值"，不用 enumValueIndex —— 后者是"枚举名单里的下标"，
            // 只有当枚举值从 0 连续排列时两者才相等；用 intValue 无论枚举怎么定义都正确。
            var current = (TroopTag)prop.intValue;
            var value = (TroopTag)EditorGUILayout.EnumPopup(new GUIContent(label, tooltip), current);
            if (EditorGUI.EndChangeCheck())
            {
                prop.intValue = (int)value;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                _issuesDirty = true;
                MarkDirty();
            }
        }

        private void DefKey(SerializedObject so, UnityEngine.Object target, string field, UnitResKind kind, string label)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                return;

            int openedFor = _selectedIndex;
            KeyRow(label, prop.stringValue, kind,
                key =>
                {
                    if (!IsStillCurrentUnit(openedFor)) return;
                    // 回调是在选键窗口的后续帧才执行的，闭包里的 so 是点「搜索」那一帧的实例。
                    // 期间同一资产可能已被别的 SerializedObject 改过，继续拿旧实例写会覆盖掉中间的改动，
                    // 所以在这里重新开一个。
                    SerializedProperty p = new SerializedObject(target).FindProperty(field);
                    if (p == null)
                        return;
                    p.stringValue = key;
                    p.serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(target);
                    _issuesDirty = true;
                    Repaint();
                },
                () =>
                {
                    if (!IsStillCurrentUnit(openedFor)) return;
                    SerializedProperty p = new SerializedObject(target).FindProperty(field);
                    if (p == null)
                        return;
                    p.stringValue = string.Empty;
                    p.serializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(target);
                    _issuesDirty = true;
                    Repaint();
                },
                null);
        }

        /// <summary>
        /// 选键窗口是独立窗口，可能一直开着；用户中途切换了兵种时，
        /// 旧的选键回调不能往已经不相关的兵种上写值。
        /// </summary>
        private bool IsStillCurrentUnit(int openedFor)
        {
            if (openedFor == _selectedIndex && _session != null && _session.Valid)
                return true;

            SetStatus("当前选中的兵种已经变了，请在目标字段上重新点「搜索」。", MessageType.Warning);
            return false;
        }
        #endregion

        #region 战斗数值
        private void DrawStatsSection(UnitStatSession session)
        {
            GameObjectProperty prop = session.Prop;
            var so = new SerializedObject(prop);

            UnitEditorUI.Section("战斗数值（单位预制体）", "改的是预制体上的 GameObjectProperty");

            EditorGUILayout.LabelField("生存", EditorStyles.miniBoldLabel);
            StatInt(so, prop, "maxHp", "生命值 maxHp", "等级缩放前的基础值：实际 = 该值 × 1.1^(兵营等级-1)。");
            StatInt(so, prop, "defense", "物理防御 defense（当前未生效）",
                "运行时没有任何地方读取 defense —— 伤害结算（DamageComputor）用的是 armor 与 damageReduction，" +
                "而这两个字段在出场时又会被 ResetRuntimeState 归位成常量。改这里在游戏里不会有任何变化。");
            StatInt(so, prop, "magicDefense", "魔法防御 magicDefense（当前未生效）",
                "同上：运行时没有任何读取点，改它不会影响战斗结果。");

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("输出", EditorStyles.miniBoldLabel);
            StatInt(so, prop, "atk", "物理攻击 atk", "同样受兵营等级缩放（1.1^(等级-1)）。");
            StatInt(so, prop, "magicAtk", "魔法攻击 magicAtk");
            StatFloat(so, prop, "atkRate", "攻速系数 atkRate", "哨塔按「每秒射击次数」使用；角色按攻击动画事件出手，该值主要影响 Buff 计算。");
            StatVector2Int(so, prop, "atkRange", "攻击范围（网格 X/Y）",
                "不是以自己为中心的半径，而是朝正面伸出去的判定矩形：\n" +
                "· X = 从身前第 1 格起算往前打几格（X = 1 时判定框含自己所在格，近战就是这种）\n" +
                "· Y = 矩形高几格，纵向按 CeilToInt((占地高 - Y) / 2) 居中，占地的奇偶不同会整体偏 1 格\n" +
                "· 朝向由运行时的 isFacingLeft 决定，预制体上的值不影响");
            StatBool(so, prop, "isRemoteAtk", "远程攻击（仅作标注，运行时未读取）",
                "运行时没有任何地方读取这个勾 —— 能不能发射投射物只看 atkObj 是否为空（CharacterAI 里 atkObj 为空就直接 return）。\n" +
                "它只用来在编辑器和本工具里标注兵种定位。");
            StatKey(so, prop, "atkObj", UnitResKind.Prefab, "投射物资源键 atkObj（填了就会发射）");
            StatFloat(so, prop, "baseMissChance", "基础未命中率（0-1）", "运行时会被重置回该值，目盲 Buff 在其上叠加。");

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("机动与体型", EditorStyles.miniBoldLabel);
            StatFloat(so, prop, "moveSpeed", "移动速度（格/秒）");
            StatFloat(so, prop, "repel", "击退强度 repel");
            StatFloat(so, prop, "antiRepel", "抗击退 antiRepel", "抵抗击退的除数，越大实际位移越小。");
            StatFloat(so, prop, "suckBlood", "吸血比例 suckBlood");
            StatVector2Int(so, prop, "occupySpace", "占地尺寸（网格 X/Y）",
                "真正挡路的尺寸：地图占位按这个格数占用（和美术图大小无关，图比格子大是正常的）。");
            StatFloat(so, prop, "barSustainTime", "血条保持时间（秒）");

            EditorGUILayout.Space(6f);
            _showRuntimeFields = EditorGUILayout.Foldout(_showRuntimeFields, "运行时修正字段（改预制体不生效，仅供查看）", true);
            if (_showRuntimeFields)
            {
                EditorGUI.indentLevel++;
                StatFloat(so, prop, "damageMultiplier", "造成伤害倍率（运行时=1）");
                StatFloat(so, prop, "damageTakenMultiplier", "受到伤害倍率（运行时=1）");
                StatFloat(so, prop, "critChance", "暴击率（运行时=0）");
                StatInt(so, prop, "armor", "免伤值 armor（运行时=0）");
                StatFloat(so, prop, "damageReduction", "最终伤害减免（运行时=0）");
                StatInt(so, prop, "blockHits", "格挡次数（运行时=0）");
                StatFloat(so, prop, "repelTakenMultiplier", "受到击退倍率（运行时=1）");
                StatFloat(so, prop, "healTakenMultiplier", "受到治疗倍率（运行时=1）");
                EditorGUILayout.LabelField(
                    "这些字段每次出场（以及对象池复用时）都会被 ResetRuntimeState 归位成固定常量，\n" +
                    "预制体上填的值不会生效 —— 它们由 Buff/技能在运行时改写。\n" +
                    "本区只有 baseMissChance 会被读取（missChance = baseMissChance）。",
                    EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }
        }

        private void StatInt(SerializedObject so, UnityEngine.Object target, string field, string label, string tooltip = null)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                return;

            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.IntField(new GUIContent(label, tooltip), prop.intValue);
            if (EditorGUI.EndChangeCheck())
            {
                prop.intValue = value;
                so.ApplyModifiedProperties();
                AfterStatChange(target);
            }
        }

        private void StatFloat(SerializedObject so, UnityEngine.Object target, string field, string label, string tooltip = null)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                return;

            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.FloatField(new GUIContent(label, tooltip), prop.floatValue);
            if (EditorGUI.EndChangeCheck())
            {
                prop.floatValue = value;
                so.ApplyModifiedProperties();
                AfterStatChange(target);
            }
        }

        private void StatBool(SerializedObject so, UnityEngine.Object target, string field, string label, string tooltip = null)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                return;

            EditorGUI.BeginChangeCheck();
            bool value = EditorGUILayout.Toggle(new GUIContent(label, tooltip), prop.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                prop.boolValue = value;
                so.ApplyModifiedProperties();
                AfterStatChange(target);
            }
        }

        private void StatVector2Int(SerializedObject so, UnityEngine.Object target, string field, string label, string tooltip = null)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                return;

            EditorGUI.BeginChangeCheck();
            Vector2Int value = EditorGUILayout.Vector2IntField(new GUIContent(label, tooltip), prop.vector2IntValue);
            if (EditorGUI.EndChangeCheck())
            {
                prop.vector2IntValue = value;
                so.ApplyModifiedProperties();
                AfterStatChange(target);
            }
        }

        private void StatKey(SerializedObject so, UnityEngine.Object target, string field, UnitResKind kind, string label)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                return;

            int openedFor = _selectedIndex;
            KeyRow(label, prop.stringValue, kind,
                key =>
                {
                    if (!IsStillCurrentUnit(openedFor)) return;
                    // 同 DefKey：回调是跨帧执行的，必须当场重开 SerializedObject，别用闭包里的旧实例。
                    SerializedProperty p = new SerializedObject(target).FindProperty(field);
                    if (p == null)
                        return;
                    p.stringValue = key;
                    p.serializedObject.ApplyModifiedProperties();
                    AfterStatChange(target);
                    Repaint();
                },
                () =>
                {
                    if (!IsStillCurrentUnit(openedFor)) return;
                    SerializedProperty p = new SerializedObject(target).FindProperty(field);
                    if (p == null)
                        return;
                    p.stringValue = string.Empty;
                    p.serializedObject.ApplyModifiedProperties();
                    AfterStatChange(target);
                    Repaint();
                },
                null);
        }

        private void AfterStatChange(UnityEngine.Object target)
        {
            EditorUtility.SetDirty(target);
            if (_session != null)
                _session.RefreshStats();
            _catalog.InvalidateStats();
            _issuesDirty = true;
            MarkDirty();
            Repaint();
        }

        /// <summary>
        /// 记下"这次打开窗口后改过东西了"。
        /// 状态栏会显示"未保存"，关窗口/交给程序前一眼就能看出还没落盘
        /// （改的是资产文件，Unity 自己的 "已修改" 标记不在这个窗口里显示）。
        /// </summary>
        private void MarkDirty()
        {
            _dirtySinceSave = true;
        }
        #endregion

        #region 精灵图 / 动画 / 音效
        private void DrawVisualSection(UnitStatSession session)
        {
            UnitEditorUI.Section("精灵图 / 动画 / 音效", "出包后按资源键加载，键必须存在于对应注册表");

            if (session.Texture == null && session.AnimatorController == null && session.Audio == null)
            {
                EditorGUILayout.HelpBox("这个预制体上没有 StageTexture / StageAnimatorController / StageAudio 组件。", MessageType.Warning);
                return;
            }

            if (session.Texture != null)
            {
                var so = new SerializedObject(session.Texture);
                EditorGUILayout.LabelField("动画贴图（StageTexture）", EditorStyles.miniBoldLabel);
                StatKey(so, session.Texture, "spriteKey", UnitResKind.Sprite, "基础动画贴图 spriteKey");

                Sprite current = UnitResourceIndex.Instance.GetSprite(session.Stats != null ? session.Stats.SpriteKey : null);
                var rect = GUILayoutUtility.GetRect(0f, 56f, GUILayout.ExpandWidth(true));
                UnitEditorUI.FillRect(rect, new Color(0f, 0f, 0f, 0.18f));
                UnitEditorUI.DrawSpriteFit(rect, current, Color.white, 4f);
            }

            if (session.AnimatorController != null)
            {
                var so = new SerializedObject(session.AnimatorController);
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("动画控制器（StageAnimatorController）", EditorStyles.miniBoldLabel);
                StatKey(so, session.AnimatorController, "animatorControllerKey", UnitResKind.AnimatorController, "动画控制器键");
            }

            if (session.Audio != null)
            {
                var so = new SerializedObject(session.Audio);
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("音效键（StageAudio，最多 8 个）", EditorStyles.miniBoldLabel);
                for (int i = 1; i <= 8; i++)
                    StatKey(so, session.Audio, "audioKey" + i, UnitResKind.Audio, "音效 " + i);
            }
        }

        private void DrawExtraSection(UnitStatSession session)
        {
            if (session.Health == null && session.Overlap == null)
                return;

            UnitEditorUI.Section("其它组件", "死亡表现与防重叠");

            if (session.Health != null)
            {
                var so = new SerializedObject(session.Health);
                EditorGUILayout.LabelField("死亡表现（CharacterHealth）", EditorStyles.miniBoldLabel);
                StatFloat(so, session.Health, "deathAngularSpeed", "死亡旋转角速度（度/秒）");
                StatFloat(so, session.Health, "deathFadeAlpha", "死亡结束透明度");
                StatFloat(so, session.Health, "deathDarkFactor", "死亡结束变暗系数");
            }

            if (session.Overlap != null)
            {
                var so = new SerializedObject(session.Overlap);
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("防重叠（UnitAntiOverlap）", EditorStyles.miniBoldLabel);
                StatFloat(so, session.Overlap, "radius", "防重叠半径");
            }
        }
        #endregion

        /// <summary>一行「标签 + 缩略图 + 键输入框 + 搜索/清空」。</summary>
        private void KeyRow(string label, string currentKey, UnitResKind kind, Action<string> onPick, Action onClear, HashSet<string> usedKeys)
        {
            UnitResourceIndex index = UnitResourceIndex.Instance;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(150f));

            var thumbRect = GUILayoutUtility.GetRect(22f, 22f, GUILayout.Width(22f), GUILayout.Height(22f));
            if (kind == UnitResKind.Sprite)
            {
                UnitEditorUI.DrawSpriteFit(thumbRect, index.GetSprite(currentKey), Color.white, 1f);
            }
            else
            {
                UnitEditorUI.FillRect(thumbRect, new Color(1f, 1f, 1f, 0.10f));
            }

            // DelayedTextField：回车或失焦时才提交。以前是 TextField，每敲一个字符就写一次资产 ——
            // 缩略图总比输入框慢一拍，Ctrl+Z 也变成"一个字符一步"。
            string typed = EditorGUILayout.DelayedTextField(currentKey ?? string.Empty);

            bool valid = string.IsNullOrEmpty(currentKey) || index.Has(kind, currentKey);
            GUI.contentColor = valid ? Color.white : UnitEditorUI.Error;
            if (GUILayout.Button("搜索", EditorStyles.miniButton, GUILayout.Width(42f)))
            {
                string unitName = _selectedIndex >= 0 && _selectedIndex < _catalog.Count ? _catalog.Records[_selectedIndex].Name : string.Empty;
                UnitKeyPickerWindow.Open(label, unitName, kind, onPick, onClear, BuildUsedKeys(kind), currentKey);
            }
            GUI.contentColor = Color.white;

            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(currentKey) && !valid)
                EditorGUILayout.LabelField("　键不存在于 " + UnitResourceIndex.KindName(kind) + " 注册表，出包会加载失败。", EditorStyles.miniLabel);

            if (typed != (currentKey ?? string.Empty))
                onPick(typed.Trim());
        }

        /// <summary>
        /// 收集"其它兵种已经在用的键"，交给选键窗口的「只看已被用过的键」过滤。
        /// 只在点「搜索」时算一次 —— 不能放进 OnGUI 的每帧路径里（要遍历全部兵种的数值快照）。
        /// </summary>
        private HashSet<string> BuildUsedKeys(UnitResKind kind)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < _catalog.Count; i++)
            {
                if (i == _selectedIndex)
                    continue;

                UnitRecord record = _catalog.Records[i];
                UnitStats stats = _catalog.GetStats(record);
                TroopDefinition definition = record.Definition;

                switch (kind)
                {
                    case UnitResKind.Sprite:
                        if (!string.IsNullOrEmpty(stats.SpriteKey)) used.Add(stats.SpriteKey);
                        if (definition != null)
                        {
                            if (!string.IsNullOrEmpty(definition.IconKey)) used.Add(definition.IconKey);
                            if (!string.IsNullOrEmpty(definition.AnimationSpriteKey)) used.Add(definition.AnimationSpriteKey);
                        }
                        break;
                    case UnitResKind.Prefab:
                        if (!string.IsNullOrEmpty(stats.AtkObjKey)) used.Add(stats.AtkObjKey);
                        if (definition != null && !string.IsNullOrEmpty(definition.PrefabKey)) used.Add(definition.PrefabKey);
                        break;
                    case UnitResKind.AnimatorController:
                        if (!string.IsNullOrEmpty(stats.AnimatorControllerKey)) used.Add(stats.AnimatorControllerKey);
                        if (definition != null && !string.IsNullOrEmpty(definition.AnimatorControllerKey)) used.Add(definition.AnimatorControllerKey);
                        break;
                }
            }

            return used;
        }
        #endregion
        #region 右栏
        /// <summary>收起 / 展开右栏。收起后中栏自动拿到整条宽度。</summary>
        private void SetRightCollapsed(bool collapsed)
        {
            _rightCollapsed = collapsed;
            UnitEditorPrefs.RightCollapsed = collapsed;
            Repaint();
        }

        private void DrawRightColumn()
        {
            EditorGUILayout.BeginHorizontal();
            // 标题栏右侧的 ✕：收起整条右栏，把宽度让给中栏（体检面板很占地方）
            string[] tabs = { "数值对比表", "数据体检", "设置" };
            _rightTab = GUILayout.Toolbar(_rightTab, tabs, EditorStyles.toolbarButton, GUILayout.Height(22f));
            if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(24f)))
            {
                SetRightCollapsed(true);
                EditorGUILayout.EndHorizontal();
                return;
            }
            EditorGUILayout.EndHorizontal();

            switch (_rightTab)
            {
                case 0: DrawTableTab(); break;
                case 1: DrawIssuesTab(); break;
                default: DrawSettingsTab(); break;
            }
        }

        #region 对比表
        private sealed class TableColumn
        {
            public string Title;
            public string Tooltip;
            public float Width;
            public Func<UnitRecord, UnitStats, float> Value;
            public bool IsText;
        }

        private static readonly TableColumn[] TableColumns =
        {
            new TableColumn { Title = "名称", Width = 150f, IsText = true, Value = (r, s) => 0f, Tooltip = "兵种名称" },
            new TableColumn { Title = "种族", Width = 58f, IsText = true, Value = (r, s) => 0f },
            new TableColumn { Title = "训练ID", Width = 132f, IsText = true, Value = (r, s) => 0f, Tooltip = "TroopDefinition.id：关卡/敌方队伍配置按它引用兵种" },
            new TableColumn { Title = "兵营", Width = 46f, IsText = true, Value = (r, s) => 0f, Tooltip = "挂在普通兵营 / 黑暗兵营（由兵营预制体的 BuildingTraining 决定）" },
            new TableColumn { Title = "兵营阶", Width = 52f, Value = (r, s) => UnlockOf(r), Tooltip = "兵营阶级 = UnlockLevel：兵营训练面板按它分 1/2/3 三排" },
            new TableColumn { Title = "AI阶", Width = 42f, Value = (r, s) => TierOf(r), Tooltip = "AI 权重阶级 = TroopDefinition.tier：只用于敌方 AI 选兵评分（max(0,tier)×10），不是兵营排数" },
            new TableColumn { Title = "HP", Width = 62f, Value = (r, s) => s.MaxHp, Tooltip = "maxHp（等级缩放的基准值）" },
            new TableColumn { Title = "物防", Width = 50f, Value = (r, s) => s.Defense },
            new TableColumn { Title = "魔防", Width = 50f, Value = (r, s) => s.MagicDefense },
            new TableColumn { Title = "攻击", Width = 56f, Value = (r, s) => s.Atk },
            new TableColumn { Title = "魔攻", Width = 56f, Value = (r, s) => s.MagicAtk },
            new TableColumn { Title = "攻速", Width = 52f, Value = (r, s) => s.AtkRate },
            new TableColumn { Title = "射程", Width = 62f, IsText = true, Value = (r, s) => 0f, Tooltip = "攻击范围 网格X×Y" },
            new TableColumn { Title = "移速", Width = 52f, Value = (r, s) => s.MoveSpeed },
            new TableColumn { Title = "冷却", Width = 52f, Value = (r, s) => CooldownOf(r), Tooltip = "训练冷却（秒）" },
            new TableColumn { Title = "产出", Width = 44f, Value = (r, s) => TrainCountOf(r), Tooltip = "单次训练产出数量" },
            new TableColumn { Title = "维护", Width = 44f, Value = (r, s) => UpkeepOf(r), Tooltip = "每秒维护费" },
            new TableColumn { Title = "远程", Width = 40f, IsText = true, Value = (r, s) => 0f }
        };

        private static float TierOf(UnitRecord r)
        {
            TroopDefinition d = r.Definition;
            return d != null ? d.Tier : 0f;
        }

        private static float UnlockOf(UnitRecord r)
        {
            TroopDefinition d = r.Definition;
            return d != null ? d.UnlockLevel : 0f;
        }

        private static float CooldownOf(UnitRecord r)
        {
            TroopDefinition d = r.Definition;
            return d != null ? d.Cooldown : 0f;
        }

        private static float TrainCountOf(UnitRecord r)
        {
            TroopDefinition d = r.Definition;
            return d != null ? d.TrainCount : 0f;
        }

        private static float UpkeepOf(UnitRecord r)
        {
            TroopDefinition d = r.Definition;
            return d != null ? d.Upkeep : 0f;
        }

        private void DrawTableTab()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("全部 " + _catalog.Count + " 个兵种，点表头排序，点行选中；勾选后可批量调值。", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            float totalWidth = 0f;
            for (int i = 0; i < TableColumns.Length; i++)
                totalWidth += TableColumns[i].Width;

            _tableScroll = EditorGUILayout.BeginScrollView(_tableScroll, GUILayout.ExpandHeight(true));

            // 表头
            Rect headerRow = GUILayoutUtility.GetRect(totalWidth, 20f);
            float x = headerRow.x;
            int sortColumn = UnitEditorPrefs.TableSortColumn;
            bool ascending = UnitEditorPrefs.TableSortAscending;

            for (int i = 0; i < TableColumns.Length; i++)
            {
                var cellRect = new Rect(x, headerRow.y, TableColumns[i].Width, headerRow.height);
                if (TableColumns[i].IsText && i > 0)
                {
                    UnitEditorUI.FillRect(cellRect, i == sortColumn ? new Color(UnitEditorUI.Accent.r, UnitEditorUI.Accent.g, UnitEditorUI.Accent.b, 0.30f) : new Color(1f, 1f, 1f, 0.06f));
                    GUI.Label(cellRect, TableColumns[i].Title, EditorStyles.centeredGreyMiniLabel);
                    if (Event.current.type == EventType.MouseDown && cellRect.Contains(Event.current.mousePosition))
                    {
                        UnitEditorPrefs.TableSortColumn = i;
                        UnitEditorPrefs.TableSortAscending = i == sortColumn ? !ascending : true;
                        Event.current.Use();
                    }
                }
                else
                {
                    if (UnitEditorUI.HeaderCell(cellRect, TableColumns[i].Title, i, sortColumn, ascending, TableColumns[i].Tooltip))
                    {
                        UnitEditorPrefs.TableSortColumn = i;
                        UnitEditorPrefs.TableSortAscending = i == sortColumn ? !ascending : true;
                    }
                }
                x += TableColumns[i].Width;
            }

            // 排序（对下标排序，方便反查原始记录）
            var order = new List<int>();
            for (int i = 0; i < _catalog.Count; i++)
                order.Add(i);

            sortColumn = Mathf.Clamp(UnitEditorPrefs.TableSortColumn, 0, TableColumns.Length - 1);
            ascending = UnitEditorPrefs.TableSortAscending;
            TableColumn sortBy = TableColumns[sortColumn];
            int direction = ascending ? 1 : -1;

            order.Sort((ia, ib) =>
            {
                UnitRecord a = _catalog.Records[ia];
                UnitRecord b = _catalog.Records[ib];

                if (sortColumn == 0)
                    return string.Compare(a.Name, b.Name, StringComparison.CurrentCulture) * direction;

                if (sortColumn == 1)
                    return string.Compare(a.Race, b.Race, StringComparison.OrdinalIgnoreCase) * direction;

                if (sortBy.IsText && sortBy.Title == "兵营")
                    return string.Compare(UnitBarracksIndex.Get(a).ShortLabel, UnitBarracksIndex.Get(b).ShortLabel,
                        StringComparison.OrdinalIgnoreCase) * direction;

                if (sortBy.IsText && sortBy.Title == "训练ID")
                    return string.Compare(a.DefinitionId, b.DefinitionId, StringComparison.OrdinalIgnoreCase) * direction;

                UnitStats sa = _catalog.GetStats(a);
                UnitStats sb = _catalog.GetStats(b);

                if (sortBy.IsText && sortBy.Title == "射程")
                {
                    float va = sa.Valid ? sa.AtkRange.x * sa.AtkRange.y : -1f;
                    float vb = sb.Valid ? sb.AtkRange.x * sb.AtkRange.y : -1f;
                    return va.CompareTo(vb) * direction;
                }

                if (sortBy.IsText && sortBy.Title == "远程")
                {
                    float va = sa.IsRemoteAtk ? 1f : 0f;
                    float vb = sb.IsRemoteAtk ? 1f : 0f;
                    return va.CompareTo(vb) * direction;
                }

                return sortBy.Value(a, sa).CompareTo(sortBy.Value(b, sb)) * direction;
            });

            // 各列最大值（画迷你条）
            var maxValues = new float[TableColumns.Length];
            for (int i = 0; i < order.Count; i++)
            {
                UnitRecord record = _catalog.Records[order[i]];
                UnitStats s = _catalog.GetStats(record);
                for (int c = 0; c < TableColumns.Length; c++)
                {
                    if (TableColumns[c].IsText)
                        continue;
                    maxValues[c] = Mathf.Max(maxValues[c], TableColumns[c].Value(record, s));
                }
            }

            for (int r = 0; r < order.Count; r++)
            {
                int recordIndex = order[r];
                UnitRecord record = _catalog.Records[recordIndex];
                UnitStats stats = _catalog.GetStats(record);

                Rect rowRect = GUILayoutUtility.GetRect(totalWidth, 18f);
                bool selected = recordIndex == _selectedIndex;
                bool isChecked = _checked.Contains(recordIndex);

                if (selected)
                    UnitEditorUI.FillRect(rowRect, UnitEditorUI.Selected);
                else if (r % 2 == 1)
                    UnitEditorUI.FillRect(rowRect, UnitEditorUI.RowAlt);

                x = rowRect.x;
                for (int c = 0; c < TableColumns.Length; c++)
                {
                    var cellRect = new Rect(x, rowRect.y, TableColumns[c].Width, rowRect.height);
                    TableColumn column = TableColumns[c];

                    if (c == 0)
                    {
                        // 这里以前只是用字符画 "☑ / ☐"，点上去没有任何反应 —— 但看起来能点，
                        // 策划点完以为勾上了，接着去批量调值，作用对象其实是"全部兵种"。改成真勾选框。
                        var toggleRect = new Rect(cellRect.x + 2f, cellRect.y + 2f, 14f, 14f);
                        bool nowChecked = EditorGUI.Toggle(toggleRect, isChecked);
                        if (nowChecked != isChecked)
                        {
                            if (nowChecked)
                                _checked.Add(recordIndex);
                            else
                                _checked.Remove(recordIndex);
                        }

                        var nameRect = new Rect(cellRect.x + 18f, cellRect.y, cellRect.width - 18f, cellRect.height);
                        string name = string.IsNullOrEmpty(record.Name) ? record.PrefabName : record.Name;
                        UnitEditorUI.CellLeft(nameRect, name, record.HasPrefab ? Color.white : UnitEditorUI.Error);

                        // 点勾选框时不要顺带切换选中的兵种（Use() 之后下面的行点击判断就不会命中）
                        if (Event.current.type == EventType.MouseDown && toggleRect.Contains(Event.current.mousePosition))
                            Event.current.Use();
                    }
                    else if (c == 1)
                    {
                        UnitEditorUI.Cell(cellRect, record.Race, new Color(0.75f, 0.8f, 0.9f, 1f));
                    }
                    else if (column.Title == "兵营")
                    {
                        BarracksInfo info = UnitBarracksIndex.Get(record);
                        UnitEditorUI.Cell(cellRect, info.ShortLabel,
                            info.IsWired ? new Color(0.75f, 0.92f, 0.75f, 1f) : UnitEditorUI.Warn);
                    }
                    else if (column.Title == "训练ID")
                    {
                        string trainingId = record.DefinitionId ?? string.Empty;
                        UnitEditorUI.CellLeft(cellRect, string.IsNullOrEmpty(trainingId) ? "—（未填）" : trainingId,
                            string.IsNullOrEmpty(trainingId) ? UnitEditorUI.Warn : new Color(0.9f, 0.88f, 0.72f, 1f));
                    }
                    else if (column.Title == "射程")
                    {
                        UnitEditorUI.Cell(cellRect, stats.Valid ? stats.AtkRange.x + "×" + stats.AtkRange.y : "-", CellColor(stats, stats.AtkRange.x > 0 && stats.AtkRange.y > 0));
                    }
                    else if (column.Title == "远程")
                    {
                        UnitEditorUI.Cell(cellRect, stats.Valid ? (stats.IsRemoteAtk ? "是" : "否") : "-", CellColor(stats, true));
                    }
                    else if (column.IsText)
                    {
                        UnitEditorUI.Cell(cellRect, "-", Color.white);
                    }
                    else
                    {
                        float value = column.Value(record, stats);
                        bool bad = IsBadValue(column.Title, record, stats, value);
                        Color color = CellColor(stats, !bad);
                        string text = column.Title == "攻速" || column.Title == "移速" || column.Title == "冷却"
                            ? value.ToString("0.##")
                            : Mathf.RoundToInt(value).ToString();

                        if (!record.HasDefinition && (column.Title == "兵营阶" || column.Title == "AI阶" || column.Title == "冷却" || column.Title == "产出" || column.Title == "维护"))
                            text = "—";

                        UnitEditorUI.Cell(cellRect, text, color);
                        if (!column.IsText && maxValues[c] > 0f && IsBarColumn(column.Title))
                            UnitEditorUI.CellBar(cellRect, value, maxValues[c], bad ? UnitEditorUI.Error : UnitEditorUI.Accent);
                    }

                    x += column.Width;
                }

                if (Event.current.type == EventType.MouseDown && rowRect.Contains(Event.current.mousePosition))
                {
                    SelectUnit(recordIndex);
                    Event.current.Use();
                    Repaint();
                }
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField("提示：角色的实际出手节奏由攻击动画驱动，atkRate 只影响哨塔与 Buff；「攻速」列不代表真实 DPS。", EditorStyles.miniLabel);
        }

        private static bool IsBarColumn(string title)
        {
            return title == "HP" || title == "攻击" || title == "魔攻" || title == "移速";
        }

        private static Color CellColor(UnitStats stats, bool normal)
        {
            if (!stats.Valid)
                return UnitEditorUI.Error;
            return normal ? Color.white : UnitEditorUI.Error;
        }

        private static bool IsBadValue(string title, UnitRecord record, UnitStats stats, float value)
        {
            if (!stats.Valid)
                return true;

            switch (title)
            {
                case "HP": return value <= 0f;
                case "攻速": return value <= 0f;
                case "移速": return value <= 0f;
                default: return false;
            }
        }
        #endregion

        #region 体检
        /// <summary>切到体检页并按级别过滤（0=全部，1=错误，2=警告，3=提示）。</summary>
        private void FocusIssues(int level)
        {
            _issueLevelFilter = level;
            _rightTab = 1;
            SetRightCollapsed(false);
            _issuesDirty = true;
            EnsureIssues();
            Repaint();
        }

        private bool MatchesIssueFilter(UnitIssue issue)
        {
            if (_issueLevelFilter == 0)
                return true;

            if (_issueLevelFilter == 1)
                return issue.Level == UnitIssueLevel.Error;
            if (_issueLevelFilter == 2)
                return issue.Level == UnitIssueLevel.Warning;

            return issue.Level == UnitIssueLevel.Info;
        }

        private void DrawIssuesTab()
        {
            int errors, warnings, infos;
            UnitValidator.Count(_issues, out errors, out warnings, out infos);

            EditorGUILayout.BeginHorizontal();
            GUI.contentColor = errors > 0 ? UnitEditorUI.Error : UnitEditorUI.Ok;
            EditorGUILayout.LabelField("错误 " + errors + "　警告 " + warnings + "　提示 " + infos, EditorStyles.boldLabel);
            GUI.contentColor = Color.white;
            EditorGUILayout.EndHorizontal();

            string[] filters = { "全部", "只看错误", "只看警告", "只看提示" };
            int newFilter = GUILayout.Toolbar(_issueLevelFilter, filters, EditorStyles.miniButton, GUILayout.Height(20f));
            if (newFilter != _issueLevelFilter)
                _issueLevelFilter = newFilter;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("一键修复可修项", GUILayout.Height(22f)))
                RunAutoFix();
            if (GUILayout.Button("重新体检", GUILayout.Height(22f)))
            {
                _issuesDirty = true;
                EnsureIssues();
            }
            if (GUILayout.Button("导出报告", GUILayout.Height(22f)))
                ExportIssueReport();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("提示：数值类问题（HP 为 0、射程为 0…）不会自动改，避免工具替你把平衡改坏。", EditorStyles.miniLabel);

            _issueScroll = EditorGUILayout.BeginScrollView(_issueScroll, GUILayout.ExpandHeight(true));

            if (_issues.Count == 0)
                EditorGUILayout.HelpBox("兵种数据很干净，没有发现问题。", MessageType.Info);

            int shownIssues = 0;
            for (int i = 0; i < _issues.Count; i++)
            {
                UnitIssue issue = _issues[i];
                if (!MatchesIssueFilter(issue))
                    continue;

                shownIssues++;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                GUI.contentColor = issue.LevelColor;
                EditorGUILayout.LabelField((issue.Level == UnitIssueLevel.Error ? "[错误] " : issue.Level == UnitIssueLevel.Warning ? "[警告] " : "[提示] ") + issue.Title, EditorStyles.boldLabel);
                GUI.contentColor = Color.white;

                EditorGUILayout.LabelField(issue.Detail, UnitEditorUI.RichLabel);

                EditorGUILayout.BeginHorizontal();
                if (!issue.IsCatalogLevel)
                {
                    EditorGUILayout.LabelField("兵种：" + issue.UnitLabel, EditorStyles.miniLabel);
                    if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(44f)))
                    {
                        SelectUnit(issue.RecordIndex);
                        // 以前这里跳去「数值对比表」，等于把人从体检页踢出去、还得自己回中栏找字段。
                        // 现在留在体检页（方便接着处理下一条），中栏会自动切到这个兵种。
                        _rightTab = 1;
                        SetRightCollapsed(false);
                        SetStatus("已定位到「" + issue.UnitLabel + "」：中栏改对应字段，改完这里会自动重算。", MessageType.Info);
                        Repaint();
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("工程级问题", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }

            if (shownIssues == 0 && _issues.Count > 0)
                EditorGUILayout.HelpBox("当前筛选（" + filters[_issueLevelFilter] + "）下没有条目。", MessageType.Info);

            EditorGUILayout.EndScrollView();
        }

        private void ExportIssueReport()
        {
            EnsureIssues();
            string path = EditorUtility.SaveFilePanel("导出兵种体检报告", UnitEditorPrefs.BackupFolder, "兵种体检报告", "txt");
            if (string.IsNullOrEmpty(path))
                return;

            var sb = new StringBuilder();
            sb.AppendLine("兵种体检报告　" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("兵种数：" + _catalog.Count + "　种族：" + string.Join("、", new List<string>(_catalog.Races).ToArray()));
            sb.AppendLine(new string('-', 60));

            for (int i = 0; i < _issues.Count; i++)
            {
                UnitIssue issue = _issues[i];
                sb.AppendLine("[" + issue.Level + "] " + issue.Title + "　" + (issue.IsCatalogLevel ? "工程级" : issue.UnitLabel));
                sb.AppendLine("    " + issue.Detail);
            }

            try
            {
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
                SetStatus("报告已导出：" + path, MessageType.Info);
            }
            catch (Exception e)
            {
                SetStatus("导出失败：" + e.Message, MessageType.Error);
            }
        }
        #endregion

        #region 设置
        private void DrawSettingsTab()
        {
            _settingsScroll = EditorGUILayout.BeginScrollView(_settingsScroll, GUILayout.ExpandHeight(true));

            EditorGUILayout.LabelField("资源注册表", EditorStyles.boldLabel);
            var kinds = new[] { UnitResKind.Sprite, UnitResKind.Prefab, UnitResKind.Audio, UnitResKind.AnimatorController, UnitResKind.AnimationClip };
            UnitResourceIndex index = UnitResourceIndex.Instance;
            for (int i = 0; i < kinds.Length; i++)
            {
                UnitResKind kind = kinds[i];
                EditorGUILayout.LabelField(UnitResourceIndex.KindName(kind) + "：" +
                    (index.RegistryFound(kind) ? index.KeyCount(kind) + " 个键" : "未找到（" + index.RegistryPath(kind) + "）"), EditorStyles.miniLabel);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("重新读取注册表"))
            {
                UnitResourceIndex.Invalidate();
                _issuesDirty = true;
                Repaint();
            }
            if (GUILayout.Button("重新扫描兵种"))
                Rescan(false);

            if (GUILayout.Button("重新读取兵营归属"))
            {
                UnitBarracksIndex.Invalidate();
                _issuesDirty = true;
                SetStatus("兵营归属已重新读取。", MessageType.Info);
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("批量调值", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("作用对象：勾选的 " + _checked.Count + " 个兵种（没勾选时用「全部兵种」）", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("打开批量操作 ▾"))
                ShowBatchMenu();
            if (GUILayout.Button("全选所有兵种"))
            {
                for (int i = 0; i < _catalog.Count; i++)
                    _checked.Add(i);
            }
            if (GUILayout.Button("清空勾选"))
                _checked.Clear();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("备份", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("目录：" + UnitEditorPrefs.BackupFolder, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("（默认跟随工具目录，改过文件夹名也会自动找到）", EditorStyles.miniLabel);
            UnitEditorPrefs.AutoBackupOnSave = EditorGUILayout.ToggleLeft("保存前提示备份", UnitEditorPrefs.AutoBackupOnSave);
            int keepCount = EditorGUILayout.IntSlider("每个资产保留份数", UnitEditorPrefs.KeepBackupCount, 3, 200);
            if (keepCount != UnitEditorPrefs.KeepBackupCount)
                UnitEditorPrefs.KeepBackupCount = keepCount;   // 只在变化时写 EditorPrefs（OnGUI 每个事件都会跑）
            EditorGUILayout.LabelField("按资产名分别保留最新 N 份，超出的自动删除（不是全目录总数）。", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("立即备份全部兵种"))
            {
                var paths = new List<string>();
                for (int i = 0; i < _catalog.Count; i++)
                {
                    UnitRecord record = _catalog.Records[i];
                    if (record.HasDefinition) paths.Add(record.DefinitionPath);
                    if (record.HasPrefab) paths.Add(record.PrefabPath);
                }

                int n = UnitBackup.BackupAssets(paths, "手动");
                SetStatus("已备份 " + n + " 个资产到 " + UnitEditorPrefs.BackupFolder, MessageType.Info);
            }
            if (GUILayout.Button("打开备份目录"))
            {
                Directory.CreateDirectory(UnitEditorPrefs.BackupFolder);
                EditorUtility.RevealInFinder(UnitEditorPrefs.BackupFolder);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("更改备份目录"))
            {
                string folder = EditorUtility.OpenFolderPanel("选择备份目录", UnitEditorPrefs.BackupFolder, string.Empty);
                if (!string.IsNullOrEmpty(folder))
                {
                    UnitEditorPrefs.BackupFolder = folder;
                    SetStatus("备份目录已改为：" + folder, MessageType.Info);
                }
            }
            if (GUILayout.Button("恢复默认（跟随工具目录）"))
            {
                UnitEditorPrefs.BackupFolder = UnitEditorPrefs.DefaultBackupFolder;
                SetStatus("备份目录已恢复默认：" + UnitEditorPrefs.BackupFolder, MessageType.Info);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("数据来源", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("单位预制体：" + UnitCatalog.PrefabRoot, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("训练配置：" + UnitCatalog.DefinitionRoot, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("扫描时跳过 " + _catalog.SkippedPrefabCount + " 个预制体" +
                                       "（没有 GameObjectProperty 组件，或落在排除目录里）", EditorStyles.miniLabel);
            EditorGUILayout.LabelField("本工具源码：" + UnitEditorPrefs.ToolRoot + "\\UnitVisualEditor（目录联接挂进工程，不改动工程其它文件）", EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();
        }
        #endregion
        #endregion

        #region 批量操作
        private void ShowBatchMenu()
        {
            bool useAll = _checked.Count == 0;
            int targetCount = useAll ? _catalog.Count : _checked.Count;
            if (targetCount == 0)
            {
                SetStatus("没有可操作的兵种。", MessageType.Warning);
                return;
            }

            var menu = new GenericMenu();
            menu.AddDisabledItem(new GUIContent(useAll
                ? "作用对象：全部 " + targetCount + " 个（未勾选任何兵种！建议先在左栏勾选）"
                : "作用对象：已勾选 " + targetCount + " 个"));
            menu.AddSeparator(string.Empty);

            AddScaleItem(menu, "生命值", "maxHp", 1.1f, targetCount);
            AddScaleItem(menu, "生命值", "maxHp", 0.9f, targetCount);
            AddScaleItem(menu, "攻击力", "atk", 1.1f, targetCount);
            AddScaleItem(menu, "攻击力", "atk", 0.9f, targetCount);
            AddScaleItem(menu, "魔攻", "magicAtk", 1.1f, targetCount);
            AddScaleItem(menu, "魔攻", "magicAtk", 0.9f, targetCount);
            AddScaleItem(menu, "移速", "moveSpeed", 1.1f, targetCount);
            AddScaleItem(menu, "移速", "moveSpeed", 0.9f, targetCount);
            AddScaleItem(menu, "训练冷却", "cooldown", 1.1f, targetCount, true);
            AddScaleItem(menu, "训练冷却", "cooldown", 0.9f, targetCount, true);

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("把选中兵种的数值复制给其它兵种…"), false, CopyStatsToSelection);
            menu.AddItem(new GUIContent("清空勾选"), false, () =>
            {
                _checked.Clear();
                Repaint();
            });
            menu.ShowAsContext();
        }

        private void AddScaleItem(GenericMenu menu, string label, string field, float factor, int targetCount, bool definitionField = false)
        {
            string percent = ((factor - 1f) * 100f).ToString("+0;-0") + "%";
            menu.AddItem(new GUIContent("整体调整/" + label + " " + percent + "（" + targetCount + " 个）"), false,
                () => ApplyScale(label, field, factor, definitionField));
        }

        /// <summary>
        /// 取批量操作的目标。
        /// 注意"未勾选 = 全部兵种"这个默认值：一次误操作会把整个工程的数值乘一遍，
        /// 所以调用方必须在确认弹窗里把这个事实说清楚（见 ApplyScale）。
        /// </summary>
        private List<int> BatchTargets()
        {
            var targets = new List<int>(_checked);
            if (targets.Count == 0)
            {
                for (int i = 0; i < _catalog.Count; i++)
                    targets.Add(i);
            }
            return targets;
        }

        /// <summary>按下标取记录，越界返回 null（列表重扫/资产被外部改动后下标可能失效）。</summary>
        private UnitRecord RecordAt(int index)
        {
            if (index < 0 || index >= _catalog.Count)
                return null;   // 目录变了：跳过，绝不用错位的下标去改别的兵种

            return _catalog.Records[index];
        }

        private void ApplyScale(string label, string field, float factor, bool definitionField)
        {
            List<int> targets = BatchTargets();
            if (targets.Count == 0)
                return;

            bool allTargets = _checked.Count == 0;
            string targetsText = allTargets
                ? "全部 " + targets.Count + " 个兵种（你没有勾选任何兵种，未勾选时默认作用于全部）"
                : "已勾选的 " + targets.Count + " 个兵种";

            if (!EditorUtility.DisplayDialog("批量调值",
                    "将把 " + targetsText + " 的「" + label + "」乘以 " + factor.ToString("0.##") +
                    "。\n\n改的是预制体/训练配置资产，可 Ctrl+Z 撤销，执行前会自动备份。",
                    allTargets ? "确定改全部 " + targets.Count + " 个" : "执行（改 " + targets.Count + " 个）",
                    "取消"))
                return;

            var paths = new List<string>();
            for (int i = 0; i < targets.Count; i++)
            {
                UnitRecord record = RecordAt(targets[i]);
                if (record == null)
                    continue;
                if (record.HasPrefab) paths.Add(record.PrefabPath);
                if (record.HasDefinition) paths.Add(record.DefinitionPath);
            }
            int backups = UnitBackup.BackupAssets(paths, "批量调值前");

            int changed = 0;
            int skipped = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                UnitRecord record = RecordAt(targets[i]);
                if (record == null)
                {
                    skipped++;
                    continue;
                }

                if (definitionField)
                {
                    TroopDefinition definition = record.Definition;
                    if (definition == null)
                        continue;

                    var so = new SerializedObject(definition);
                    SerializedProperty prop = so.FindProperty(field);
                    if (prop == null)
                        continue;

                    if (prop.propertyType == SerializedPropertyType.Float)
                    {
                        // 和预制体分支一样要有下限：反复点 ×0.9 会把冷却压到 0.1 秒以下
                        // （TroopDefinition.cooldown 带 [Min(.1f)]），兵营会瞬间刷兵。
                        prop.floatValue = Mathf.Max(0.1f, prop.floatValue * factor);
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(definition);
                        changed++;
                    }
                    else if (prop.propertyType == SerializedPropertyType.Integer)
                    {
                        prop.intValue = Mathf.Max(1, Mathf.RoundToInt(prop.intValue * factor));
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(definition);
                        changed++;
                    }
                }
                else
                {
                    UnitStatSession session = UnitStatSession.Open(record);
                    if (!session.Valid)
                        continue;

                    UnitStats stats = session.Stats;
                    var so = new SerializedObject(session.Prop);
                    SerializedProperty prop = so.FindProperty(field);
                    if (prop == null)
                        continue;

                    if (prop.propertyType == SerializedPropertyType.Integer)
                    {
                        prop.intValue = Mathf.Max(1, Mathf.RoundToInt(prop.intValue * factor));
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(session.Prop);
                        changed++;
                    }
                    else if (prop.propertyType == SerializedPropertyType.Float)
                    {
                        prop.floatValue = Mathf.Max(0.01f, prop.floatValue * factor);
                        so.ApplyModifiedProperties();
                        EditorUtility.SetDirty(session.Prop);
                        changed++;
                    }
                }
            }

            _catalog.InvalidateStats();
            if (_session != null)
                _session.RefreshStats();
            _issuesDirty = true;
            if (changed > 0)
                MarkDirty();
            SetStatus("已调整 " + changed + " 个兵种的「" + label + "」（备份 " + backups + " 个资产）" +
                      (skipped > 0 ? "，跳过 " + skipped + " 个（目录已变化）" : "") + "。", MessageType.Info);
            Repaint();
        }

        /// <summary>把当前选中兵种的数值复制到勾选的兵种上。</summary>
        private void CopyStatsToSelection()
        {
            if (_selectedIndex < 0 || _session == null || !_session.Valid)
            {
                SetStatus("先选中一个要作为模板的兵种。", MessageType.Warning);
                return;
            }

            var targets = new List<int>(_checked);
            targets.Remove(_selectedIndex);
            if (targets.Count == 0)
            {
                SetStatus("先勾选要接收数值的兵种（模板兵种本身除外）。", MessageType.Warning);
                return;
            }

            if (!EditorUtility.DisplayDialog("复制数值",
                    "把「" + _catalog.Records[_selectedIndex].Name + "」的战斗数值复制到勾选的 " + targets.Count + " 个兵种？\n\n" +
                    "（生命/防御/攻击/射程/移速/击退/占地等，不含图标与动画键）",
                    "复制", "取消"))
                return;

            string[] fields = { "maxHp", "defense", "magicDefense", "atk", "magicAtk", "atkRate", "isRemoteAtk", "atkRange", "moveSpeed", "repel", "antiRepel", "suckBlood", "baseMissChance", "occupySpace", "barSustainTime" };

            var source = new SerializedObject(_session.Prop);
            var backups = new List<string>();
            for (int i = 0; i < targets.Count; i++)
            {
                UnitRecord record = RecordAt(targets[i]);
                if (record != null && record.HasPrefab)
                    backups.Add(record.PrefabPath);
            }
            UnitBackup.BackupAssets(backups, "复制数值前");

            int changed = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                UnitRecord record = RecordAt(targets[i]);
                if (record == null)
                    continue;

                UnitStatSession target = UnitStatSession.Open(record);
                if (!target.Valid)
                    continue;

                var targetSo = new SerializedObject(target.Prop);
                for (int f = 0; f < fields.Length; f++)
                {
                    SerializedProperty src = source.FindProperty(fields[f]);
                    SerializedProperty dst = targetSo.FindProperty(fields[f]);
                    if (src == null || dst == null)
                        continue;

                    switch (dst.propertyType)
                    {
                        case SerializedPropertyType.Integer: dst.intValue = src.intValue; break;
                        case SerializedPropertyType.Float: dst.floatValue = src.floatValue; break;
                        case SerializedPropertyType.Boolean: dst.boolValue = src.boolValue; break;
                        case SerializedPropertyType.Vector2Int: dst.vector2IntValue = src.vector2IntValue; break;
                    }
                }
                targetSo.ApplyModifiedProperties();
                EditorUtility.SetDirty(target.Prop);
                changed++;
            }

            _catalog.InvalidateStats();
            _issuesDirty = true;
            if (changed > 0)
                MarkDirty();
            SetStatus("已把数值复制到 " + changed + " 个兵种。", MessageType.Info);
            Repaint();
        }

        /// <summary>
        /// 批量补全训练 ID（TroopDefinition.id）：
        /// 空的按单位预制体名生成，重复的追加序号；统一用"小写+下划线"风格，与工程现有 id 保持一致。
        /// 关卡/敌方队伍配置是按这个 id 引用兵种的，缺了或重了都会引用错。
        /// </summary>
        private void FillTrainingIds()
        {
            var targets = new List<int>();
            var currentIds = new List<string>();

            for (int i = 0; i < _catalog.Count; i++)
            {
                UnitRecord record = _catalog.Records[i];
                TroopDefinition definition = record.Definition;
                if (definition == null)
                    continue;

                string current = definition.Id;
                bool empty = string.IsNullOrEmpty(current);
                bool duplicate = !empty && _catalog.IsTrainingIdTaken(current.Trim(), i);
                if (!empty && !duplicate)
                    continue;

                targets.Add(i);
                currentIds.Add(empty ? "（空）" : current.Trim() + "（重复）");
            }

            if (targets.Count == 0)
            {
                SetStatus("所有兵种的训练 ID 都已填好且没有重复。", MessageType.Info);
                return;
            }

            if (!EditorUtility.DisplayDialog("批量补全训练 ID",
                    "有 " + targets.Count + " 个兵种需要补训练 ID（空的和重复的）：\n" +
                    string.Join("、", currentIds.GetRange(0, Mathf.Min(8, currentIds.Count)).ToArray()) +
                    (currentIds.Count > 8 ? " …" : string.Empty) +
                    "\n\n规则：按单位预制体名生成小写下划线 ID，重名时追加 _2、_3；已正确的不动。\n" +
                    "执行前会自动备份，可 Ctrl+Z 撤销。",
                    "开始补全", "取消"))
                return;

            var paths = new List<string>();
            for (int i = 0; i < targets.Count; i++)
                paths.Add(_catalog.Records[targets[i]].DefinitionPath);
            int backups = UnitBackup.BackupAssets(paths, "补全训练ID前");

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("批量补全训练 ID");

            var report = new List<string>();
            int done = 0;
            for (int t = 0; t < targets.Count; t++)
            {
                int i = targets[t];
                UnitRecord record = _catalog.Records[i];
                TroopDefinition definition = record.Definition;
                if (definition == null)
                    continue;

                string newId = _catalog.MakeUniqueTrainingId(record, i);
                var so = new SerializedObject(definition);
                SerializedProperty prop = so.FindProperty("id");
                if (prop == null)
                    continue;

                prop.stringValue = newId;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(definition);

                record.DefinitionId = newId;   // 同步缓存，后续唯一性判断才准
                report.Add(record.PrefabName + "：" + currentIds[t] + " → " + newId);
                done++;
            }

            Undo.CollapseUndoOperations(group);
            _issuesDirty = true;
            if (done > 0)
                MarkDirty();
            SetStatus("已补全 " + done + " 个训练 ID（备份 " + backups + " 个资产）。", MessageType.Info);
            if (report.Count > 0)
                Debug.Log("[兵种编辑器] 训练 ID 补全明细：\n" + string.Join("\n", report.ToArray()));
            Repaint();
        }

        /// <summary>为「有预制体但没有训练配置」的兵种批量新建 TroopDefinition。</summary>
        private void CreateMissingDefinitions()
        {
            var missing = new List<UnitRecord>();
            for (int i = 0; i < _catalog.Count; i++)
            {
                UnitRecord record = _catalog.Records[i];
                if (record.HasPrefab && !record.HasDefinition)
                    missing.Add(record);
            }

            if (missing.Count == 0)
            {
                SetStatus("所有兵种都有训练配置，不需要新建。", MessageType.Info);
                return;
            }

            if (!EditorUtility.DisplayDialog("新建训练配置",
                    "有 " + missing.Count + " 个兵种没有 TroopDefinition：\n" +
                    string.Join("、", ToNameArray(missing, 8)) + (missing.Count > 8 ? " …" : string.Empty) +
                    "\n\n将为它们新建训练配置（id / 显示名 / 单位预制体键按预制体名填好，兵营阶级 1、冷却 5 秒、单次产出 1）。\n" +
                    "新建后还需要在兵营预制体的 BuildingTraining.Troops 里挂上，玩家才能在兵营里训练。",
                    "新建", "取消"))
                return;

            int created = 0;
            for (int i = 0; i < missing.Count; i++)
            {
                UnitRecord record = missing[i];
                string folder = UnitCatalog.DefinitionRoot + "/" + (string.IsNullOrEmpty(record.Race) ? "未分类" : record.Race);
                EnsureFolder(folder);

                string path = folder + "/" + record.PrefabName + ".asset";
                if (AssetDatabase.LoadAssetAtPath<TroopDefinition>(path) != null)
                    continue;

                var definition = ScriptableObject.CreateInstance<TroopDefinition>();
                AssetDatabase.CreateAsset(definition, path);

                // 训练 ID 统一走目录的生成规则（与「批量补全训练 ID」同一套清洗 + 查重）。
                // 以前这里用的是本文件里另一份 SanitizeId：不查重、不处理 '.'、也不折叠 '__'，
                // 一次建多个兵种可能出现重复 id，风格也和批量补全对不上。
                string newId = _catalog.MakeUniqueTrainingId(record, -1);

                var so = new SerializedObject(definition);
                SetIfExists(so, "id", newId);
                SetIfExists(so, "displayName", record.PrefabName);
                SetIfExists(so, "prefabKey", record.PrefabName);
                SetIfExists(so, "tier", 1);
                SetIfExists(so, "cooldown", 5f);
                SetIfExists(so, "trainCount", 1);
                SetIfExists(so, "unlockLevel", 1);   // 不能是 0：兵营面板只排"解锁等级 1/2/3"三排
                SetIfExists(so, "threatScore", 1f);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(definition);

                // 立刻回填缓存：否则同一轮里后面的兵种查重时看不到刚分配的 id，会生成重复的
                record.DefinitionPath = path;
                record.DefinitionId = newId;
                record.Id = newId;
                created++;
            }

            AssetDatabase.SaveAssets();
            if (created > 0)
                MarkDirty();
            Rescan(true);
            SetStatus("新建了 " + created + " 个训练配置（记得在兵营的 BuildingTraining.Troops 里挂上）。", MessageType.Info);
        }

        private static string[] ToNameArray(List<UnitRecord> records, int max)
        {
            var names = new List<string>();
            for (int i = 0; i < records.Count && i < max; i++)
                names.Add(records[i].PrefabName);
            return names.ToArray();
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
                return;

            string parent = Path.GetDirectoryName(assetFolder).Replace('\\', '/');
            string leaf = Path.GetFileName(assetFolder);
            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static void SetIfExists(SerializedObject so, string field, object value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                return;

            if (value is string) prop.stringValue = (string)value;
            else if (value is int) prop.intValue = (int)value;
            else if (value is float) prop.floatValue = (float)value;
            else if (value is bool) prop.boolValue = (bool)value;
        }

        private static string SanitizeId(string text)
        {
            return UnitCatalog.SanitizeTrainingId(text);
        }

        private void ShowCsvMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("导出数值表 CSV（Excel 可直接打开）"), false, () =>
            {
                string path = EditorUtility.SaveFilePanel("导出兵种数值表", UnitEditorPrefs.BackupFolder, "兵种数值表", "csv");
                if (string.IsNullOrEmpty(path))
                    return;

                string message;
                if (UnitEditorCsv.Export(_catalog, path, out message))
                    SetStatus(message.Replace("\n", " "), MessageType.Info);
                else
                    SetStatus(message, MessageType.Error);
            });

            menu.AddItem(new GUIContent("导出空白模板"), false, () =>
            {
                string path = EditorUtility.SaveFilePanel("导出模板", UnitEditorPrefs.BackupFolder, "兵种数值表模板", "csv");
                if (string.IsNullOrEmpty(path))
                    return;

                string message;
                UnitEditorCsv.ExportTemplate(path, out message);
                SetStatus(message.Replace("\n", " "), MessageType.Info);
            });

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("从 CSV 导入（先预览再确认）"), false, ImportCsv);
            menu.ShowAsContext();
        }

        private void ImportCsv()
        {
            string path = EditorUtility.OpenFilePanel("选择兵种数值表 CSV", UnitEditorPrefs.BackupFolder, "csv");
            if (string.IsNullOrEmpty(path))
                return;

            UnitCsvPlan plan = UnitEditorCsv.BuildPlan(_catalog, path);

            var sb = new StringBuilder();
            sb.AppendLine("文件：" + Path.GetFileName(path));
            sb.AppendLine("能匹配上的兵种：" + plan.MatchedCount + " 行");
            if (plan.UnmatchedCount > 0)
                sb.AppendLine("匹配不上（名字对不上，会跳过）：" + plan.UnmatchedCount + " 行");
            if (plan.MissingColumns.Count > 0)
                sb.AppendLine("表里缺少的列（这些字段保持原样）：" + string.Join("、", plan.MissingColumns.ToArray()));

            if (plan.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("需要注意：");
                for (int i = 0; i < plan.Warnings.Count && i < 10; i++)
                    sb.AppendLine("· " + plan.Warnings[i]);
                if (plan.Warnings.Count > 10)
                    sb.AppendLine("· …还有 " + (plan.Warnings.Count - 10) + " 条");
            }

            sb.AppendLine();
            sb.AppendLine("匹配键是第一列「单位预制体名」，空单元格 = 保持原值。");
            sb.AppendLine("写入前会自动备份，可 Ctrl+Z 撤销。");

            if (!EditorUtility.DisplayDialog("确认导入", sb.ToString(), "开始导入", "取消"))
                return;

            string result = UnitEditorCsv.Apply(_catalog, plan, true);

            // 导入可能改了训练 ID / 显示名等"记录级缓存字段"，必须重建目录，
            // 否则左栏与对比表会继续显示旧值（看起来像导入失败）。
            RefreshCatalogPreservingSelection();
            if (plan.MatchedCount > 0)
                MarkDirty();
            SetStatus(result, MessageType.Info);
            Repaint();
        }

        private void ShowBackupMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("备份全部兵种资产"), false, () =>
            {
                var paths = new List<string>();
                for (int i = 0; i < _catalog.Count; i++)
                {
                    UnitRecord record = _catalog.Records[i];
                    if (record.HasDefinition) paths.Add(record.DefinitionPath);
                    if (record.HasPrefab) paths.Add(record.PrefabPath);
                }

                int n = UnitBackup.BackupAssets(paths, "手动");
                SetStatus("已备份 " + n + " 个资产到 " + UnitEditorPrefs.BackupFolder, MessageType.Info);
            });

            menu.AddItem(new GUIContent("打开备份目录"), false, () =>
            {
                Directory.CreateDirectory(UnitEditorPrefs.BackupFolder);
                EditorUtility.RevealInFinder(UnitEditorPrefs.BackupFolder);
            });

            List<string> backups = UnitBackup.ListBackups();
            if (backups.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("（还没有任何备份）"));
            }
            else
            {
                menu.AddSeparator(string.Empty);
                for (int i = 0; i < backups.Count && i < 15; i++)
                {
                    string file = backups[i];
                    menu.AddItem(new GUIContent("打开：" + Path.GetFileName(file)), false, () => EditorUtility.RevealInFinder(file));
                }
            }

            menu.ShowAsContext();
        }
        #endregion

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            int errors, warnings, infos;
            UnitValidator.Count(_issues, out errors, out warnings, out infos);
            EditorGUILayout.LabelField("兵种 " + _catalog.Count + "　问题 " + (errors + warnings + infos) + "（错 " + errors + "）　勾选 " + _checked.Count,
                EditorStyles.miniLabel, GUILayout.Width(320f));

            if (_dirtySinceSave)
            {
                GUI.contentColor = UnitEditorUI.Warn;
                EditorGUILayout.LabelField("● 有未保存的改动（点工具栏「保存全部」）", EditorStyles.miniLabel, GUILayout.Width(220f));
                GUI.contentColor = Color.white;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("工具 " + UnitEditorPrefs.SourceStamp(), EditorStyles.miniLabel, GUILayout.Width(130f));

            GUI.contentColor = _statusType == MessageType.Error ? UnitEditorUI.Error
                : _statusType == MessageType.Warning ? UnitEditorUI.Warn
                : UnitEditorUI.Muted;
            EditorGUILayout.LabelField(_status, EditorStyles.miniLabel);
            GUI.contentColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }
    }
}