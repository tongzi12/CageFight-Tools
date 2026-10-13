using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CageFightStageTool
{
    /// <summary>
    /// 关卡可视化编辑器（主窗口）。
    ///
    ///   左栏 —— 全部关卡的卡片带：编号 / 玩法 / 时限 / 敌队数 / 对象数 / 上架状态 / 体检角标；
    ///   中栏 —— 本关出现的兵种：按敌方队伍列出会出的兵 + 本关实际数值，可直接在这里调配白名单与解锁时间；
    ///   右栏 —— 数值表单 / 时间轴推演 / 数据体检 / 设置；
    ///   工具栏 —— 保存、撤销、一键修复、批量改、备份恢复。
    ///
    /// 所有改动都有 Undo；保存前自动备份到工具目录的 StageBackups。
    /// 写盘范围只有当前这一份 StageN.asset。
    /// </summary>
    public sealed class LevelEditorWindow : EditorWindow
    {
        private const string MenuPath = "工具/关卡可视化编辑器 %&l";
        private const float MinListWidth = 240f;
        private const float MaxListWidth = 420f;
        private const float MinRightWidth = 320f;
        private const float MaxRightWidth = 720f;

        #region 状态
        private readonly StageEditorData _data = new StageEditorData();

        private List<StageInfo> _stages = new List<StageInfo>();
        private List<StageIssue> _issues = new List<StageIssue>();
        private bool _issuesDirty = true;

        private string _search = string.Empty;
        private int _typeFilter;            // 0=全部 1=防守 2=进攻 3=Boss
        private int _healthFilter;          // 0=全部 1=只看有错 2=只看没上架 3=只看本地测试关
        private bool _onlyWithIssues;

        private Vector2 _listScroll;
        private Vector2 _formScroll;
        private Vector2 _issueScroll;
        private Vector2 _objectScroll;
        private Vector2 _welcomeScroll;

        private float _listWidth = 300f;
        private float _rightWidth = 430f;
        private int _rightTab;

        /// <summary>右栏（体检 / 设置）是否收起。收起后中栏拿到全部宽度 —— 体检面板很占地方。</summary>
        private bool _rightCollapsed;

        private string _status = string.Empty;

        /// <summary>「玩家初始金币」里待写入场景的数值（只暂存在窗口里，不落盘）。</summary>
        private int _startGoldEdit;

        // ── 建筑槽位「自动编排」面板状态（纯界面状态，不写进关卡资产） ──
        private bool _autoLayoutFold;
        private StageLayout.Options _autoOptions;
        private int _autoOptionsTeam = -1;      // 参数按队伍缓存；-1 = 还没按哪支队初始化过
        private string _autoReport = string.Empty;

        private double _lastValidateTime;

        /// <summary>
        /// 体检节流间隔（秒）。编辑时每个按键都会把体检标脏，而体检要遍历全部对象 ——
        /// 不节流的话打字 / 拖数值时每一帧都在重算，界面就会一顿一顿的。
        /// </summary>
        private const double ValidateInterval = 0.2;
        private MessageType _statusType = MessageType.None;

        private static readonly string[] RightTabLabels = { "数值", "体检", "设置" };
        private static readonly string[] PageLabels = { "关卡", "兵种一览" };

        /// <summary>当前页：0 = 关卡编辑，1 = 兵种一览（R1.32）。</summary>
        private int _page;
        private LevelEditorTroopRosterPage _rosterPage;
        private static readonly string[] TypeFilterLabels = { "全部玩法", "防守", "进攻", "Boss" };
        private static readonly string[] HealthFilterLabels = { "全部状态", "只看有错的", "只看没上架的", "只看本地测试关" };
        #endregion

        #region 生命周期
        [MenuItem(MenuPath, false, 100)]
        public static void OpenWindow()
        {
            var window = GetWindow<LevelEditorWindow>(false, "关卡编辑器", true);
            window.minSize = new Vector2(1180f, 620f);
            window.Show();
        }

        [MenuItem("Assets/打开关卡可视化编辑器", false, 2000)]
        private static void OpenFromContext()
        {
            StageConfig config = Selection.activeObject as StageConfig;
            LevelEditorWindow window = GetWindow<LevelEditorWindow>(false, "关卡编辑器", true);
            window.minSize = new Vector2(1180f, 620f);
            window.Show();
            if (config != null)
                window.LoadStage(AssetDatabase.GetAssetPath(config));
        }

        [MenuItem("Assets/打开关卡可视化编辑器", true)]
        private static bool OpenFromContextValidate()
        {
            return Selection.activeObject is StageConfig;
        }

        private void OnEnable()
        {
            _listWidth = LevelEditorPrefs.ListWidth;
            _rightWidth = LevelEditorPrefs.RightWidth;
            _rightTab = Mathf.Clamp(LevelEditorPrefs.RightTab, 0, RightTabLabels.Length - 1);
            _rightCollapsed = LevelEditorPrefs.RightCollapsed;
            _page = Mathf.Clamp(LevelEditorPrefs.Page, 0, PageLabels.Length - 1);
            if (_rosterPage == null)
            {
                _rosterPage = new LevelEditorTroopRosterPage();
                _rosterPage.SetJumpHandler(JumpToStage);
            }
            _data.Changed += OnDataChanged;
            RefreshStageList();
            TryAutoLoad();
        }

        private void OnDisable()
        {
            _data.Changed -= OnDataChanged;
            LevelEditorPrefs.ListWidth = _listWidth;
            LevelEditorPrefs.RightWidth = _rightWidth;
            LevelEditorPrefs.RightTab = _rightTab;
            LevelEditorPrefs.RightCollapsed = _rightCollapsed;
        }

        private void OnFocus()
        {
            // 场景可能换了（MapCells 尺寸跟着变），重新取一次。
            StageMap.Invalidate();
            StageEditorData.InvalidateSourceStamp();
            RefreshStageList();

            // 关卡或兵种资产可能被别处改过 → 兵种一览的数据要重算
            if (_rosterPage != null)
                _rosterPage.Invalidate();
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is StageConfig)
            {
                string path = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (path != _data.AssetPath && !_data.IsDirty)
                    LoadStage(path);
            }
        }

        private void OnDataChanged()
        {
            _issuesDirty = true;
            Repaint();
        }

        private void RefreshStageList()
        {
            _stages = StageEditorData.DiscoverStages();
            _issuesDirty = true;
        }

        private void TryAutoLoad()
        {
            if (_data.IsLoaded)
                return;

            string last = LevelEditorPrefs.LastStagePath;
            if (!string.IsNullOrEmpty(last) && File.Exists(last) && LoadStage(last))
                return;

            if (_stages.Count > 0)
                LoadStage(_stages[0].Path);
        }

        /// <summary>切换到另一页（0 = 关卡编辑，1 = 兵种一览），并记住选择。</summary>
        private void SetPage(int page)
        {
            _page = Mathf.Clamp(page, 0, PageLabels.Length - 1);
            LevelEditorPrefs.Page = _page;

            if (_page == 1 && _rosterPage != null)
            {
                _rosterPage.Invalidate();
                _rosterPage.Rebuild();
            }

            Repaint();
        }

        /// <summary>兵种一览里点 StageN 跳关：复用同一套 LoadStage（因此同样会先问"有未保存的改动"）。</summary>
        private void JumpToStage(int stageId)
        {
            for (int i = 0; i < _stages.Count; i++)
            {
                if (_stages[i] == null || _stages[i].StageId != stageId)
                    continue;

                SetPage(0);
                if (!LoadStage(_stages[i].Path))
                    return;

                SetStatus("已跳到 Stage" + stageId + "（从兵种一览点过来的）。", MessageType.Info);
                return;
            }

            SetStatus("找不到 Stage" + stageId + " 的资产（可能已被删除或改名）。", MessageType.Warning);
        }

        /// <summary>程序化构建写完之后：重算体检、重算兵种一览，并把结果写到状态栏。</summary>
        private void OnGeneratorApplied()
        {
            _issuesDirty = true;
            if (_rosterPage != null)
                _rosterPage.Invalidate();
            SetStatus("程序化构建已写入当前关卡（还没落盘）：点「保存」写文件，保存前会自动备份。", MessageType.Info);
            Repaint();
        }

        /// <summary>切换到另一个关卡。</summary>
        public bool LoadStage(string assetPath)
        {
            if (_data.IsLoaded && _data.IsDirty && !string.Equals(_data.AssetPath, assetPath, StringComparison.OrdinalIgnoreCase))
            {
                bool save = EditorUtility.DisplayDialog("有未保存的修改",
                    "当前关卡「" + _data.Current.Label + "」还有未保存的修改，切换关卡会丢掉它们。\n\n要先保存吗？",
                    "保存并切换", "直接切换");
                if (save)
                    SaveNow(LevelEditorPrefs.AutoBackupOnSave);
            }

            if (!_data.Load(assetPath))
            {
                SetStatus("加载失败：" + assetPath, MessageType.Error);
                return false;
            }

            // 选键窗口的回调是绑在"当时那一关"的字段上的，切关卡后它就不该再能写值了。
            LevelEditorPickerWindow.CloseIfOpen();

            ClearEditorFocus();
            _issuesDirty = true;
            SetStatus("已加载：" + assetPath, MessageType.Info);
            Repaint();
            return true;
        }

        /// <summary>
        /// 切关卡时必须清掉 IMGUI 的文本编辑焦点，否则上一个字段的编辑缓冲会串到新关卡上。
        /// （这条是图鉴编辑器踩过的坑，原样继承。）
        /// </summary>
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

        #region 折叠状态（记住我收起了哪些区）
        private const string FoldPrefPrefix = "CageFight.LevelVisualEditor.Fold.";

        /// <summary>折叠状态存 EditorPrefs（和栏宽、页签一样），关掉窗口再打开还是收起的。</summary>
        private static readonly string[] FoldKeys =
        {
            "placed", "barracks.normal", "barracks.dark", "barracks.others", "tier.1", "tier.2", "tier.3",
            "team.0", "team.1", "team.2", "team.3",
            "form.玩法与时限", "form.阵地", "form.本关敌方等级", "form.玩家经济",
            "form.敌方队伍", "form.通关奖励", "form.展示", "form.全局规则",
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
            // 下一次读到的还是旧值，整条窗口的折叠开关都会弹回去（"点了没反应"）。
            FoldCache[key] = collapsed;
            EditorPrefs.SetBool(FoldPrefPrefix + key, collapsed);
        }

        /// <summary>中栏 + 右栏一起全部展开 / 全部收起。</summary>
        private static void SetAllFold(bool collapsed)
        {
            for (int i = 0; i < FoldKeys.Length; i++)
                SetCollapsed(FoldKeys[i], collapsed);
        }

        /// <summary>数值表单的分区标题，可折叠。返回 true = 已收起，调用方直接 return 就行。</summary>
        private bool FormSection(string key, string title, string hint)
        {
            string foldKey = "form." + key;
            bool expanded = !IsCollapsed(foldKey);
            bool now = LevelEditorUI.FoldHeader(expanded, title, hint, LevelEditorUI.Accent);
            if (now != expanded)
                SetCollapsed(foldKey, !now);
            return !now;
        }
        #endregion

        #region 主循环
        private void OnGUI()
        {
            if (Event.current.type == EventType.KeyDown && Event.current.control && Event.current.keyCode == KeyCode.S)
            {
                SaveNow(LevelEditorPrefs.AutoBackupOnSave);
                Event.current.Use();
            }

            DrawToolbar();

            if (_page == 1)
            {
                // 兵种一览（R1.32）：整页交给它，主窗口只负责工具栏切换与状态栏
                if (_rosterPage == null)
                {
                    _rosterPage = new LevelEditorTroopRosterPage();
                    _rosterPage.SetJumpHandler(JumpToStage);
                }

                EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
                _rosterPage.Draw();
                EditorGUILayout.EndVertical();

                DrawStatusBar();
                return;
            }

            if (_stages.Count == 0)
            {
                DrawWelcome("工程里没有找到任何 StageConfig 资产。\n\n" +
                            "关卡由「关卡构建 → 创建新关卡」从场景导出，生成在 Assets/RemoteResource/Stages/ 下。");
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
            float maxListWidth = Mathf.Max(MinListWidth, Mathf.Min(MaxListWidth, position.width - 300f - rightWidth));
            _listWidth = LevelEditorUI.Splitter(leftSplitter, listWidth, MinListWidth, maxListWidth);

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawMiddleColumn();
            EditorGUILayout.EndVertical();

            // 收起时整条右栏不画（连分割条也不画），中栏自动拿到全部宽度
            if (!_rightCollapsed)
            {
                Rect rightSplitter = GUILayoutUtility.GetRect(5f, 5f, GUILayout.Width(5f), GUILayout.ExpandHeight(true));
                float maxRightWidth = Mathf.Max(MinRightWidth, Mathf.Min(MaxRightWidth, position.width - 300f - _listWidth));
                _rightWidth = LevelEditorUI.Splitter(rightSplitter, rightWidth, MinRightWidth, maxRightWidth);
    
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
            _issues.Clear();

            int mapW = StageMap.Width;
            int mapH = StageMap.Height;

            for (int i = 0; i < _stages.Count; i++)
                StageValidator.Validate(_stages[i], mapW, mapH, _issues);

            StageValidator.ValidateStageIdContinuity(_stages, _issues);
        }

        private int ErrorCount
        {
            get { int e, w, i; StageValidator.Count(_issues, out e, out w, out i); return e; }
        }

        private int WarningCount
        {
            get { int e, w, i; StageValidator.Count(_issues, out e, out w, out i); return w; }
        }

        private int InfoCount
        {
            get { int e, w, i; StageValidator.Count(_issues, out e, out w, out i); return i; }
        }
        #endregion

        #region 工具栏
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // 页切换放最左：这是"编某一关"还是"横着看全部兵种"的分界
            int page = GUILayout.Toolbar(_page, PageLabels, EditorStyles.toolbarButton, GUILayout.Width(132f));
            if (page != _page)
                SetPage(page);

            if (_page == 1)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("改的是兵种资产（显示名 / 头像），关卡资产不受影响。",
                    LevelEditorUI.MiniLabel, GUILayout.Width(320f));
                EditorGUILayout.EndHorizontal();
                return;
            }

            if (GUILayout.Button("重新扫描关卡", EditorStyles.toolbarButton, GUILayout.Width(96f)))
            {
                RescanEverything();
                SetStatus("已重新扫描：" + _stages.Count + " 个关卡资产。", MessageType.Info);
            }

            // 关卡的新增 / 复制 / 删除都收在这个入口里：工具栏再摆三个按钮就太挤，
            // 而且"删关卡"这种不可逆动作摊在面上容易被误点。
            if (GUILayout.Button("关卡 ▾", EditorStyles.toolbarButton, GUILayout.Width(58f)))
                ShowStageMenu();

            // 程序化构建（R1.33）：按难度档一次算出整套数值 + 槽位 + 白名单，先预览再写入
            using (new EditorGUI.DisabledScope(!_data.IsLoaded))
            {
                if (GUILayout.Button(new GUIContent("程序化构建…",
                        "选「低 / 中 / 高 / 卡点」→ 按关卡编号算出整套敌方数值、槽位与兵种白名单（先给对照表预览，再写入）"),
                        EditorStyles.toolbarButton, GUILayout.Width(86f)))
                    LevelEditorStageGeneratorWindow.Open(_data, OnGeneratorApplied);
            }

            using (new EditorGUI.DisabledScope(!_data.IsLoaded))
            {
                GUI.backgroundColor = _data.IsDirty ? new Color(1f, 0.85f, 0.5f) : Color.white;
                if (GUILayout.Button(_data.IsDirty ? "● 保存 (Ctrl+S)" : "保存 (Ctrl+S)", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                    SaveNow(LevelEditorPrefs.AutoBackupOnSave);
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("撤销", EditorStyles.toolbarButton, GUILayout.Width(46f)))
                    Undo.PerformUndo();

                // 上架：把本关登记进 Addressables 组 StageConfig（写的是分组资产，不碰游戏代码）。
                // 已上架 / 本地测试关时按钮禁用，避免重复点。
                StageInfo current = _data.Current;
                bool registered = current != null && !current.IsLocalTest &&
                                  StageResourceIndex.Instance.IsStageRegistered(current.StageId);
                using (new EditorGUI.DisabledScope(current == null || current.IsLocalTest || registered))
                {
                    if (GUILayout.Button(registered ? "已上架" : "上架", EditorStyles.toolbarButton, GUILayout.Width(54f)))
                        RegisterCurrentStage();
                }

                // 进关实测：保存 → 切选关场景 → Play（不改任何场景数据）。
                using (new EditorGUI.DisabledScope(current == null))
                {
                    if (GUILayout.Button("进关实测", EditorStyles.toolbarButton, GUILayout.Width(66f)))
                        PlayTestCurrentStage();
                }
            }

            GUILayout.FlexibleSpace();

            int errors = ErrorCount;
            int warnings = WarningCount;
            int infos = InfoCount;
            GUI.contentColor = errors > 0 ? LevelEditorUI.Error : (warnings > 0 ? LevelEditorUI.Warn : LevelEditorUI.Ok);
            GUILayout.Label("体检 " + errors + " 错 / " + warnings + " 警 / " + infos + " 提示", EditorStyles.toolbarButton, GUILayout.Width(180f));
            GUI.contentColor = Color.white;

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

            if (GUILayout.Button("批量改 ▾", EditorStyles.toolbarButton, GUILayout.Width(66f)))
                ShowBatchMenu();

            if (GUILayout.Button("备份 ▾", EditorStyles.toolbarButton, GUILayout.Width(58f)))
                ShowBackupMenu();

            if (GUILayout.Button("设置", EditorStyles.toolbarButton, GUILayout.Width(40f)))
            {
                SetRightCollapsed(false);
                _rightTab = 2;
                LevelEditorPrefs.RightTab = _rightTab;
                Repaint();
            }

            EditorGUILayout.EndHorizontal();
        }

        #region 关卡的新增 / 复制 / 删除
        private void ShowStageMenu()
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("新建关卡（从当前场景导出）…"), false,
                () => LevelEditorNewStageWindow.Open(CurrentStageOrNull(), false, OnStageCreated));

            menu.AddItem(new GUIContent("复制当前关卡为新关卡…"), _data.IsLoaded,
                () => LevelEditorNewStageWindow.Open(CurrentStageOrNull(), true, OnStageCreated));

            menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent("删除当前关卡…"), _data.IsLoaded, DeleteCurrentStage);

            menu.ShowAsContext();
        }

        private StageConfig CurrentStageOrNull()
        {
            return _data.IsLoaded ? _data.Stage : null;
        }

        /// <summary>新建窗口结束后的收尾：重扫、切到新关卡、把结果写到状态栏。</summary>
        private void OnStageCreated(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
            {
                SetStatus("新建关卡没有成功：场景导出没有产出资产（多半是己方大本营标记不合法，见弹出的提示）。",
                    MessageType.Warning);
                Repaint();
                return;
            }

            RescanEverything();

            if (LoadStage(assetPath))
                SetStatus("已新建并切到：" + assetPath + "。还没上架 —— 点工具栏「上架」即可登记进 Addressables。",
                    MessageType.Info);
            else
                SetStatus("已新建 " + assetPath + "，但加载失败。", MessageType.Warning);

            Repaint();
        }

        /// <summary>
        /// 删除当前关卡。三重保护：二次确认（写明会删掉什么）+ 删除前自动备份 + 走
        /// <c>MoveAssetToTrash</c>（进系统回收站，不是硬删）。
        /// </summary>
        private void DeleteCurrentStage()
        {
            if (!_data.IsLoaded)
                return;

            StageInfo info = _data.Current;
            StageConfig config = info.Config;

            int objects = config.objects != null ? config.objects.Count : 0;
            int teams = config.enemyTeams != null ? config.enemyTeams.Count : 0;
            bool registered = StageResourceIndex.Instance.IsStageRegistered(info.StageId);

            var message = new System.Text.StringBuilder();
            message.AppendLine("要删除 " + info.Label + " 吗？");
            message.AppendLine();
            message.AppendLine("· 资产：" + info.Path);
            message.AppendLine("· 内容：预摆 " + objects + " 个、敌队 " + teams + " 支");
            message.AppendLine("· 删除前会自动备份一份到 StageBackups（可用工具栏「备份 ▾」找回）");
            message.AppendLine("· 文件走系统回收站，不是硬删");
            if (_data.IsDirty)
                message.AppendLine("· 这个关卡还有未保存的修改，会一起丢掉");

            if (registered)
            {
                message.AppendLine();
                message.AppendLine("它登记在 Addressables 组 StageConfig 里 —— 删除时会**一起把那条登记移除**" +
                                   "（否则出包会引用到一个已经不存在的资产）。分组资产改动前也会自动备份。");
            }

            if (!EditorUtility.DisplayDialog("删除关卡", message.ToString(), "删除 " + info.Label, "取消"))
                return;

            string assetPath = info.Path;
            int stageId = info.StageId;
            string backup = StageEditorData.BackupFile(assetPath, "删除前");

            // 先下架再删资产：分组里留一条指向已删资产的登记，出包时会报错。
            string unregisterMessage = null;
            bool unregistered = registered && StageResourceIndex.UnregisterStage(stageId, out unregisterMessage);

            // 先卸载再删：否则窗口还握着这个资产的引用，重扫时会拿到一个正在被销毁的对象
            _data.Unload();
            LevelEditorPrefs.LastStagePath = string.Empty;

            bool moved = AssetDatabase.MoveAssetToTrash(assetPath);
            AssetDatabase.Refresh();

            RescanEverything();

            // 删完顺手切到剩下的第一个关卡，不留一个空窗口
            if (_stages.Count > 0)
                LoadStage(_stages[0].Path);

            if (!moved)
            {
                SetStatus("删除失败（MoveAssetToTrash 返回 false）：" + assetPath, MessageType.Error);
            }
            else
            {
                var status = new System.Text.StringBuilder();
                status.Append("已删除 " + assetPath);
                if (!string.IsNullOrEmpty(backup))
                    status.Append("（备份：" + Path.GetFileName(backup) + "）");
                if (unregistered)
                    status.Append("；Addressables 已下架。");
                else if (registered && !string.IsNullOrEmpty(unregisterMessage))
                    status.Append("；Addressables 下架未完成：" + unregisterMessage);
                SetStatus(status.ToString(), unregistered || !registered ? MessageType.Info : MessageType.Warning);
            }

            Repaint();
        }

        /// <summary>重扫关卡列表 + 资源索引 + 地图 + 缩略图缓存 + 体检，增删关卡后都走它。</summary>
        private void RescanEverything()
        {
            RefreshStageList();
            StageResourceIndex.Invalidate();
            StageResourceIndex.InvalidateAddressables();
            StageMap.Invalidate();
            LevelEditorUI.ClearThumbnailCache();
            EnsureIssues();
            if (_rosterPage != null)
                _rosterPage.Invalidate();
        }
        #endregion

        private void SaveNow(bool backup)
        {
            if (!_data.IsLoaded)
                return;

            string backupFile = null;
            if (backup)
                backupFile = _data.BackupNow("保存前");

            _data.SaveToDisk(false);
            _issuesDirty = true;

            SetStatus("已保存：" + _data.AssetPath +
                      (string.IsNullOrEmpty(backupFile) ? string.Empty : "（旧版本已备份到 " + Path.GetFileName(backupFile) + "）"),
                      MessageType.Info);
            Repaint();
        }

        /// <summary>
        /// 「上架」：把当前关卡登记进 Addressables 组 StageConfig。
        ///
        /// 地址 = <c>Stage{编号}</c>、标签 = <c>StartupPreload</c>（两个依据都写在
        /// StageResourceIndex.RegisterStage 的注释里）。写的是**分组资产**，
        /// 工具自己实现、不引用 Addressables 程序集、不改任何游戏代码；写前自动备份分组资产。
        /// </summary>
        private void RegisterCurrentStage()
        {
            StageInfo info = _data.Current;
            if (info == null)
                return;

            if (info.IsLocalTest)
            {
                SetStatus("编号 " + info.StageId + " 是本地测试关（≥880），不要求上架。", MessageType.Info);
                return;
            }

            string message;
            bool done = StageResourceIndex.RegisterStage(info.Path, info.StageId, out message);

            RefreshStageList();
            _issuesDirty = true;
            SetStatus(message, done ? MessageType.Info : MessageType.Warning);
            if (done)
                Debug.Log("[关卡编辑器] 上架 " + info.Name + "：" + message);
            Repaint();
        }

        /// <summary>
        /// 这一关在选关界面的第几页（1 基）。
        ///
        /// 依据：`StageConfigLoader` 每页固定 7 个按钮（`ButtonsPerPage = 7`，StageConfigLoader.cs:15），
        /// 页码 = 序号 ÷ 7 向下取整 + 1；本地测试关（编号 ≥ 880）不参与选关，跳过。
        /// </summary>
        private int PageOfStage(StageInfo info)
        {
            if (info == null)
                return 1;

            var formal = new List<StageInfo>();
            for (int i = 0; i < _stages.Count; i++)
            {
                StageInfo stage = _stages[i];
                if (stage != null && !stage.IsLocalTest && stage.StageId > 0)
                    formal.Add(stage);
            }
            formal.Sort((a, b) => a.StageId.CompareTo(b.StageId));

            for (int i = 0; i < formal.Count; i++)
            {
                if (formal[i] == info ||
                    string.Equals(formal[i].Path, info.Path, StringComparison.OrdinalIgnoreCase))
                    return i / 7 + 1;
            }

            return 1;
        }

        /// <summary>
        /// 「进关实测」：保存 → 切到选关场景 → 直接进入 Play，并告诉你这一关在第几页。
        ///
        /// 为什么不"直接跳进某一关"：进关是 SceneFSM 的流程（选关 → 加载 → 局内）。
        /// 从编辑器硬塞一个关卡进去必须绕过它 —— 旧的 `StageLoadTestButton` 就是那样做的：
        /// 它按 `objects` 反推要预载的资源键，**空预摆的关卡会直接警告退出**，而且绕过经济/胜负判定。
        /// 这里改成"把真实的选关流程跑起来"，行为与真人测试一致，而且**不改任何场景数据**。
        /// </summary>
        private void PlayTestCurrentStage()
        {
            StageInfo info = _data.Current;
            if (info == null)
                return;

            const string menuScenePath = "Assets/Scenes/ToolTest 1.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(menuScenePath) == null)
            {
                SetStatus("找不到选关场景：" + menuScenePath, MessageType.Error);
                return;
            }

            int page = PageOfStage(info);

            if (!EditorUtility.DisplayDialog("进关实测",
                    "将做三件事：\n" +
                    "① 保存当前改动（按设置决定是否先备份）\n" +
                    "② 切到选关场景 " + menuScenePath + "\n" +
                    "③ 进入 Play 模式\n\n" +
                    "进游戏后在选关界面翻到第 " + page + " 页（每页 7 关），选中「" + info.Label + "」。\n" +
                    "场景数据不会被修改。",
                    "开始实测", "取消"))
                return;

            if (_data.IsDirty)
                SaveNow(LevelEditorPrefs.AutoBackupOnSave);

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorSceneManager.OpenScene(menuScenePath, OpenSceneMode.Single);
            EditorApplication.isPlaying = true;

            SetStatus("已进入 Play：翻到第 " + page + " 页选「" + info.Label + "」。", MessageType.Info);
        }

        private void RunAutoFix()
        {
            if (!_data.IsLoaded)
                return;

            EnsureIssues();

            int mapW = StageMap.Width;
            int mapH = StageMap.Height;

            // 先数一下"本关"能修几处，避免一键修复给人"改了一大堆"的错觉
            var probe = new List<StageIssue>();
            StageValidator.Validate(_data.Current, mapW, mapH, probe);

            int fixable = 0;
            for (int i = 0; i < probe.Count; i++)
            {
                if (probe[i] != null && probe[i].Fixable)
                    fixable++;
            }

            if (fixable == 0)
            {
                SetStatus("本关没有可以安全自动修复的问题。", MessageType.Info);
                return;
            }

            if (!EditorUtility.DisplayDialog("一键修复",
                    "将对「" + _data.Current.Label + "」修复 " + fixable + " 处问题：\n\n" +
                    "· 大本营 / Boss / 槽位坐标夹进地图内\n" +
                    "· 敌方基地距离、5 个敌方等级、收入阶段步长 夹到合法范围\n" +
                    "· 敌方队伍编号规范成合法的偶数队\n" +
                    "· 删除空的队伍 / 阶段 / 槽位 / 兵种条目（空兵种条目会让运行时抛 NullReferenceException）\n" +
                    "· 某类型槽位比该类型上限多时，把上限提到槽位数（普通兵营 / 黑暗兵营 / 哨塔分开算）\n" +
                    "· 把缺的**种族建筑键追加**进 prefabs（只做加法：大本营没预载会让整个关卡实例化失败，\n" +
                    "  进关就是一张空地图；清单里原有的键一个都不动）\n" +
                    "· 本关没上架就**上架**到 Addressables 组 StageConfig（地址 Stage{编号} + 标签 StartupPreload；\n" +
                    "  写的是分组资产，改前自动备份）\n\n" +
                    "不会删关卡、不会改编号、不会删任何资源键。可 Ctrl+Z 撤销。",
                    "开始修复", "取消"))
                return;

            _data.BackupNow("一键修复前");
            int fixedCount = StageValidator.ApplySafeFixes(_data.Current, mapW, mapH);
            _data.MarkDirty();
            _issuesDirty = true;

            SetStatus("已修复 " + fixedCount + " 处。", fixedCount > 0 ? MessageType.Info : MessageType.Warning);
            Repaint();
        }
        #endregion

        #region 左栏：关卡卡片带
        private void DrawLeftColumn()
        {
            LevelEditorUI.Section("关卡", _stages.Count + " 个资产");

            _search = LevelEditorUI.SearchField(_search, "搜索：编号 / 名称");

            // 两个下拉各占一行：列被拖窄时也不会撑破列宽、逼出横向滚动条
            // （一旦出现横向滚动条，列表会被横向滚走，卡片左边的内容就看不见了）
            _typeFilter = EditorGUILayout.Popup(_typeFilter, TypeFilterLabels, GUILayout.ExpandWidth(true));
            _healthFilter = EditorGUILayout.Popup(_healthFilter, HealthFilterLabels, GUILayout.ExpandWidth(true));

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
            _listScroll.x = 0f;   // 列表永远不横向滚动：横向滚走会让卡片左边的内容看不见

            int shown = 0;
            for (int i = 0; i < _stages.Count; i++)
            {
                StageInfo info = _stages[i];
                if (!MatchesFilter(info))
                    continue;

                DrawStageCard(info);
                shown++;
            }

            if (shown == 0)
                EditorGUILayout.LabelField("没有符合条件的关卡。", LevelEditorUI.MiniLabel);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("共 " + _stages.Count + " 个关卡，显示 " + shown + " 个", LevelEditorUI.MiniLabel);
            EditorGUILayout.LabelField("地图：" + StageMap.SourceLabel, LevelEditorUI.MiniLabel);

            // 注释总览入口（R1.30）：写下来的协作信息都要能一眼找到
            int noted = StageNoteStore.All().Count;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("注释：" + noted + " 关有", LevelEditorUI.MiniLabel, GUILayout.Width(88f));
            if (GUILayout.Button(new GUIContent("注释总览",
                    "打开注释窗口：看所有已写的注释、点一条就能改；也能导出成 Markdown 发给别人"),
                    EditorStyles.miniButton, GUILayout.Width(72f)))
                LevelEditorNoteWindow.Open(-1, null, null);
            EditorGUILayout.EndHorizontal();
        }

        private bool MatchesFilter(StageInfo info)
        {
            if (info == null)
                return false;

            string q = (_search ?? string.Empty).Trim();
            if (q.Length > 0)
            {
                bool hit = info.Label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                           || (info.Name ?? string.Empty).IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hit)
                    return false;
            }

            if (_typeFilter > 0)
            {
                int wanted = _typeFilter - 1;   // 0=Defense 1=Attack 2=Boss
                if ((int)info.Config.stageType != wanted)
                    return false;
            }

            switch (_healthFilter)
            {
                case 1:
                    if (StageValidator.WorstLevelOf(_issues, info.Path) != (int)StageIssueLevel.Error)
                        return false;
                    break;
                case 2:
                    if (info.IsLocalTest || StageResourceIndex.Instance.IsStageRegistered(info.StageId))
                        return false;
                    break;
                case 3:
                    if (!info.IsLocalTest)
                        return false;
                    break;
            }

            return true;
        }

        private void DrawStageCard(StageInfo info)
        {
            bool selected = _data.AssetPath == info.Path;

            Rect card = GUILayoutUtility.GetRect(0f, 52f, GUILayout.ExpandWidth(true));
            if (selected)
                LevelEditorUI.FillRect(card, LevelEditorUI.Selected);
            else if (card.Contains(Event.current.mousePosition))
                LevelEditorUI.FillRect(card, new Color(1f, 1f, 1f, 0.06f));
            else if (info.IsLocalTest)
                LevelEditorUI.FillRect(card, new Color(1f, 1f, 1f, 0.02f));

            int worst = StageValidator.WorstLevelOf(_issues, info.Path);
            Color barColor = worst == (int)StageIssueLevel.Error ? LevelEditorUI.Error
                : worst == (int)StageIssueLevel.Warning ? LevelEditorUI.Warn
                : worst == (int)StageIssueLevel.Info ? LevelEditorUI.Muted
                : LevelEditorUI.Ok;
            LevelEditorUI.FillRect(new Rect(card.x, card.y + 2f, 3f, card.height - 4f), barColor);

            // 左侧文字宽度跟着卡片走，不用固定宽度（列被拖窄时会画到卡片外面去）
            // 右边要留出：上架角标 48 + 注释按钮 22 + 间距 —— 所以比原来多留 24px。
            float leftTextWidth = Mathf.Max(60f, card.width - 128f);

            GUI.Label(new Rect(card.x + 9f, card.y + 3f, leftTextWidth, 16f), info.Label, EditorStyles.miniBoldLabel);
            GUI.Label(new Rect(card.x + 9f, card.y + 18f, leftTextWidth, 14f),
                info.TypeLabel + "　" + FormatSeconds(info.MainTimeLimit), LevelEditorUI.MiniLabel);

            // 第二行：敌队数 / 对象数
            int teamCount = info.Config.enemyTeams != null ? info.Config.enemyTeams.Count : 0;
            int objectCount = info.Config.objects != null ? info.Config.objects.Count : 0;
            GUI.Label(new Rect(card.x + 9f, card.y + 32f, leftTextWidth, 14f),
                "敌队 " + teamCount + "　对象 " + objectCount, LevelEditorUI.MiniLabel);

            // 右上角角标：上架状态
            bool registered = StageResourceIndex.Instance.IsStageRegistered(info.StageId);
            var badge = new Rect(card.xMax - 78f, card.y + 3f, 48f, 15f);
            if (info.IsLocalTest)
                LevelEditorUI.TagAt(badge, "测试关", LevelEditorUI.Muted);
            else if (registered)
                LevelEditorUI.TagAt(badge, "已上架", LevelEditorUI.Ok);
            else
                LevelEditorUI.TagAt(badge, "未上架", LevelEditorUI.Error);

            // 右上角最右：注释按钮（R1.30）。有注释时用强调色并显示 ●，点开就是注释窗口。
            // 用 MouseDown 自己处理（而不是 GUI.Button 的 MouseUp），这样点它不会顺带把这一关也选中。
            bool hasNote = StageNoteStore.Has(info.StageId);
            StageNote note = hasNote ? StageNoteStore.Get(info.StageId) : null;
            var noteRect = new Rect(card.xMax - 26f, card.y + 3f, 22f, 16f);
            var noteContent = new GUIContent(
                hasNote ? "注●" : "注",
                hasNote
                    ? "这一关有注释（" + (string.IsNullOrEmpty(note.Author) ? "没署名" : note.Author) + " " +
                      note.TimeLabel + "）：\n" + note.Summary(120) + "\n\n点开可以编辑"
                    : "给这一关写注释：改动原因 / 注意点 / 待办，别人打开同一个工具就能看到");
            bool noteHover = noteRect.Contains(Event.current.mousePosition);
            Color noteAccent = hasNote ? LevelEditorUI.Accent : LevelEditorUI.Muted;
            LevelEditorUI.FillRect(noteRect, LevelEditorUI.TagFill(noteAccent));
            LevelEditorUI.Frame(noteRect, noteHover ? Color.white : noteAccent, 1f);
            GUI.Label(noteRect, noteContent, LevelEditorUI.TagTextStyle);

            bool noteClicked = Event.current.type == EventType.MouseDown &&
                               Event.current.button == 0 && noteHover;
            if (noteClicked)
            {
                LevelEditorNoteWindow.Open(info.StageId, info.Label, info.Path);
                Event.current.Use();
            }

            // 右下角：问题计数
            if (worst >= 0)
            {
                int count = 0;
                for (int i = 0; i < _issues.Count; i++)
                {
                    StageIssue issue = _issues[i];
                    if (issue != null && string.Equals(issue.StagePath, info.Path, StringComparison.Ordinal))
                        count++;
                }
                GUI.Label(new Rect(card.xMax - 90f, card.y + 32f, 84f, 14f), count + " 个问题", LevelEditorUI.MiniLabel);
            }
            else
            {
                GUI.contentColor = LevelEditorUI.Ok;
                GUI.Label(new Rect(card.xMax - 90f, card.y + 32f, 84f, 14f), "无问题", LevelEditorUI.MiniLabel);
                GUI.contentColor = Color.white;
            }

            if (!noteClicked && Event.current.type == EventType.MouseDown && card.Contains(Event.current.mousePosition))
            {
                if (!selected)
                    LoadStage(info.Path);
                Event.current.Use();
            }
        }

        private static string FormatSeconds(float seconds)
        {
            if (seconds <= 0f)
                return "不限时";

            int total = Mathf.RoundToInt(seconds);
            return (total / 60) + ":" + (total % 60).ToString("00");
        }
        #endregion

        #region 中栏：本关出现的兵种

        private Vector2 _troopScroll;
        private string _troopSearch = string.Empty;
        private int _troopFilter;                 // 0=全部 1=只看白名单里的兵 2=只看场上预摆
        private static readonly string[] TroopFilterLabels = { "全部", "只看白名单里的兵", "只看场上预摆的" };

        /// <summary>
        /// 中栏主区：本关会出现的兵种。
        ///
        /// 分类轴 = **兵种解锁等级**（TroopDefinition.unlockLevel，1/2/3），也就是游戏内训练面板的「三排阶数」：
        /// BuildingTraining.GetTroop(row, index) 的注释写明「面板按解锁等级 1/2/3 分排」，
        /// TroopTrainingPanel.Refresh() 也是 for (row = 1..3) 逐排填充。
        ///
        /// 注意别和 TroopDefinition.tier 混了 —— 那个字段在 TeamCommander.ScoreTroop 里被当 AI 打分权重用
        /// （score = Tier * 10），分布也很偏（34/6/4），不是这里的「阶」。
        ///
        /// **白名单是标记、不是视图模式**：兵种列表永远完整列出（该种族兵营预制体上挂的全部兵种），
        /// 每张卡片自己显示「加入白名单」或「移出白名单」。
        /// （早期版本在白名单非空时切成"只显示名单"模式，结果加一个兵就看不见其余 12 个，
        ///   2 阶 / 3 阶 / 黑暗兵营整组消失 —— 没法继续调配，已改掉。）
        /// </summary>
        private void DrawMiddleColumn()
        {
            if (!_data.IsLoaded)
            {
                EditorGUILayout.Space(20f);
                EditorGUILayout.HelpBox("左边点一个关卡开始。", MessageType.Info);
                return;
            }

            StageConfig config = _data.Stage;

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("本关出现的兵种", EditorStyles.boldLabel, GUILayout.Width(104f));
            _troopFilter = GUILayout.Toolbar(_troopFilter, TroopFilterLabels, EditorStyles.toolbarButton, GUILayout.Width(268f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("全部展开", EditorStyles.toolbarButton, GUILayout.Width(62f)))
            {
                SetAllFold(false);
                Repaint();
            }
            if (GUILayout.Button("全部收起", EditorStyles.toolbarButton, GUILayout.Width(62f)))
            {
                SetAllFold(true);
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            _troopSearch = LevelEditorUI.SearchField(_troopSearch, "搜索：兵种名 / Id / 预制体键 / 资源键");

            _troopScroll = EditorGUILayout.BeginScrollView(_troopScroll, GUILayout.ExpandHeight(true));
            _troopScroll.x = 0f;

            if (_troopFilter != 2)
                DrawEnemyTeamTroops(config);

            if (_troopFilter != 1)
                DrawPlacedObjectsBlock(config);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField(
                "分阶依据：兵种解锁等级（需兵营 Lv1/2/3），与游戏内训练面板的三排一致。" +
                "本工具只改本关的兵种白名单（写 StageN.asset），不改兵营预制体。",
                LevelEditorUI.RichLabel);

            EditorGUILayout.EndScrollView();
        }

        #region 敌方队伍会出的兵
        private void DrawEnemyTeamTroops(StageConfig config)
        {
            List<EnemyTeamConfig> teams = config.enemyTeams;

            LevelEditorUI.Section("敌方队伍会出的兵",
                (teams == null ? 0 : teams.Count) + " 支队伍 · 敌方兵营等级 Lv" + config.enemyBarracksLevel +
                "（本关兵种数值 ×" + StageFacts.LevelScale(config.enemyBarracksLevel).ToString("0.###") + "）");

            if (teams == null || teams.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "本关没有配敌方队伍，所以不会有敌方出兵。\n\n" +
                    "在右栏「数值 → 敌方队伍」里点「＋ 新增一支敌方队伍」即可开始配。",
                    MessageType.Info);
                return;
            }

            for (int i = 0; i < teams.Count; i++)
            {
                if (teams[i] != null)
                    DrawTroopTeamBlock(config, teams[i], i);
            }
        }

        private void DrawTroopTeamBlock(StageConfig config, EnemyTeamConfig team, int teamIndex)
        {
            StageResourceIndex index = StageResourceIndex.Instance;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ---------- 队伍头部（可折叠） ----------
            string teamFoldKey = "team." + teamIndex;
            bool teamExpanded = !IsCollapsed(teamFoldKey);

            EditorGUILayout.BeginHorizontal();
            bool teamNow = LevelEditorUI.FoldToggle(teamExpanded);
            if (teamNow != teamExpanded)
            {
                SetCollapsed(teamFoldKey, !teamNow);
                teamExpanded = teamNow;
            }
            EditorGUILayout.LabelField("第 " + (teamIndex + 1) + " 支队伍", EditorStyles.boldLabel, GUILayout.Width(88f));

            string raceLabel = string.IsNullOrEmpty(team.raceId) ? "（没填种族 → 本队不会启用）" : team.raceId;
            GUI.contentColor = string.IsNullOrEmpty(team.raceId) ? LevelEditorUI.Error : Color.white;
            EditorGUILayout.LabelField("种族 " + raceLabel, LevelEditorUI.MiniLabel, GUILayout.Width(160f));
            GUI.contentColor = Color.white;

            // 模式角标放在左边流动位置（放最右会随内容溢出被裁掉）
            int whitelistCount = team.troopUnlocks != null ? team.troopUnlocks.Count : 0;
            if (whitelistCount == 0)
                LevelEditorUI.Tag("自动解锁全部兵种", LevelEditorUI.Muted);
            else
                LevelEditorUI.Tag("白名单 " + whitelistCount + " 个", LevelEditorUI.Accent);

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // 经济信息单独一行：挤在同一行会把这一行撑得比视口宽，右边的角标就被裁了
            EditorGUILayout.LabelField("编号 " + team.teamId + "　起始 " + team.startGold + "　每秒 " +
                team.baseGoldPerSecond.ToString("0.#") + "　" + team.strategy, LevelEditorUI.MiniLabel);

            RaceDefinition race = string.IsNullOrEmpty(team.raceId) ? null : index.GetRace(team.raceId);
            if (!string.IsNullOrEmpty(team.raceId) && race == null)
            {
                GUI.contentColor = LevelEditorUI.Error;
                EditorGUILayout.LabelField("工程里找不到种族资产 RaceDefinition「" + team.raceId + "」。", LevelEditorUI.MiniLabel);
                GUI.contentColor = Color.white;
                EditorGUILayout.EndVertical();
                return;
            }

            if (teamExpanded)
            {
                EditorGUILayout.BeginHorizontal();
                LevelEditorUI.Hint(whitelistCount == 0
                    ? "本队没设白名单 → 本局自动解锁兵营里的全部兵种。给某个兵点「加入白名单」即可开始限定。"
                    : "本局只能出白名单里的兵。下面每张卡片可以「移出白名单」；解锁(秒) 是该兵种本局可训练的时间门槛。");
                GUILayout.FlexibleSpace();
                if (whitelistCount > 0 &&
                    GUILayout.Button("清空白名单（改回自动解锁全部）", EditorStyles.miniButton, GUILayout.Width(190f)))
                {
                    if (EditorUtility.DisplayDialog("清空白名单",
                            "第 " + (teamIndex + 1) + " 支队伍的白名单会被清空，本局改为自动解锁兵营全部兵种。\n\n可 Ctrl+Z 撤销。",
                            "清空", "取消"))
                    {
                        Undo.RecordObject(config, "清空兵种白名单");
                        team.troopUnlocks.Clear();
                        _data.MarkDirty();
                    }
                }
                EditorGUILayout.EndHorizontal();

                var handled = new HashSet<EnemyTroopUnlock>();
                bool any = false;
                any |= DrawBarracksSection(config, team, teamIndex, race, false, handled);
                any |= DrawBarracksSection(config, team, teamIndex, race, true, handled);

                if (!any)
                {
                    EditorGUILayout.LabelField(
                        race == null
                            ? "填上种族之后，这里会列出它兵营能出的全部兵种。"
                            : "这个种族的建筑表里没有「兵营 / 黑暗兵营」条目，或兵营预制体上没挂 BuildingTraining（没有兵种表）。",
                        LevelEditorUI.MiniLabel);
                }

                DrawOthersGroup(config, team, handled);
                DrawAddTroopRow(config, team, teamIndex, race);
            }
            else
            {
                EditorGUILayout.LabelField("（已收起 —— 点左边的 ▶ 展开）", LevelEditorUI.MiniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 画一个兵营（普通 / 黑暗）的全部兵种，按 1/2/3 阶分三组。
        /// **永远列全部**，白名单只作为每张卡片上的标记（加 / 移出）。
        /// </summary>
        private bool DrawBarracksSection(StageConfig config, EnemyTeamConfig team, int teamIndex,
            RaceDefinition race, bool dark, HashSet<EnemyTroopUnlock> handled)
        {
            StageResourceIndex index = StageResourceIndex.Instance;

            string prefabKey = ResolveBarracksPrefabKey(race, dark);
            List<TroopDefinition> barracksTroops = race != null
                ? index.GetBarracksTroops(race.Id, dark)
                : new List<TroopDefinition>();

            if (barracksTroops.Count == 0)
                return false;

            int whitelistInThis = CountWhitelistEntries(team, barracksTroops);
            string countText = whitelistInThis > 0
                ? barracksTroops.Count + " 个兵种 · 白名单 " + whitelistInThis
                : barracksTroops.Count + " 个兵种";

            string title = dark ? "黑暗兵营" : "普通兵营";
            string hint = string.IsNullOrEmpty(prefabKey) ? "种族建筑表里没有配置这个兵营" : prefabKey;

            string barracksFoldKey = "barracks." + (dark ? "dark" : "normal");
            bool barracksExpanded = !IsCollapsed(barracksFoldKey);
            bool barracksNow = LevelEditorUI.FoldHeader(barracksExpanded, title, hint,
                dark ? LevelEditorUI.Boss : LevelEditorUI.Friendly, countText);
            if (barracksNow != barracksExpanded)
                SetCollapsed(barracksFoldKey, !barracksNow);
            if (!barracksNow)
                return true;

            for (int tier = 1; tier <= 3; tier++)
            {
                var inTier = new List<TroopDefinition>();
                for (int i = 0; i < barracksTroops.Count; i++)
                {
                    TroopDefinition troop = barracksTroops[i];
                    if (troop != null && TierOf(troop) == tier)
                        inTier.Add(troop);
                }

                if (inTier.Count == 0)
                    continue;

                int whitelistInTier = 0;
                for (int i = 0; i < inTier.Count; i++)
                {
                    if (WhitelistContains(team, inTier[i]))
                        whitelistInTier++;
                }

                // 「只看白名单里的兵」过滤：这一阶一个都没进名单就整阶跳过
                if (_troopFilter == 1 && whitelistInTier == 0)
                    continue;

                string tierCountText = whitelistInTier > 0
                    ? inTier.Count + " 个兵种 · 白名单 " + whitelistInTier
                    : inTier.Count + " 个兵种";

                string tierFoldKey = "tier." + tier;
                bool tierExpanded = !IsCollapsed(tierFoldKey);
                bool tierNow = LevelEditorUI.FoldHeader(tierExpanded,
                    tier + " 阶（需兵营 Lv" + tier + " 解锁）", null, LevelEditorUI.Accent, tierCountText, 12);
                if (tierNow != tierExpanded)
                    SetCollapsed(tierFoldKey, !tierNow);
                if (!tierNow)
                {
                    // 收起时也要把白名单条目记为"已处理"，否则它们会掉进「其他」组里重复出现
                    if (handled != null)
                    {
                        for (int i = 0; i < team.troopUnlocks.Count; i++)
                        {
                            EnemyTroopUnlock entry = team.troopUnlocks[i];
                            if (entry != null && entry.Troop != null && inTier.Contains(entry.Troop))
                                handled.Add(entry);
                        }
                    }
                    continue;
                }

                for (int i = 0; i < inTier.Count; i++)
                {
                    TroopDefinition troop = inTier[i];

                    if (_troopFilter == 1 && !WhitelistContains(team, troop))
                        continue;

                    if (handled != null)
                    {
                        for (int k = 0; k < team.troopUnlocks.Count; k++)
                        {
                            EnemyTroopUnlock entry = team.troopUnlocks[k];
                            if (entry != null && entry.Troop == troop)
                            {
                                handled.Add(entry);
                                break;
                            }
                        }
                    }

                    DrawTroopCard(config, team, troop);
                }
            }

            return true;
        }

        /// <summary>白名单里有、但不属于该种族任何兵营兵种表的条目（换了种族之后的残留、或手填的）。</summary>
        private void DrawOthersGroup(StageConfig config, EnemyTeamConfig team, HashSet<EnemyTroopUnlock> handled)
        {
            if (team.troopUnlocks == null || team.troopUnlocks.Count == 0)
                return;

            var others = new List<EnemyTroopUnlock>();
            for (int i = 0; i < team.troopUnlocks.Count; i++)
            {
                EnemyTroopUnlock entry = team.troopUnlocks[i];
                if (entry == null)
                    continue;
                if (handled == null || !handled.Contains(entry))
                    others.Add(entry);
            }

            if (others.Count == 0)
                return;

            string othersFoldKey = "barracks.others";
            bool othersExpanded = !IsCollapsed(othersFoldKey);
            bool othersNow = LevelEditorUI.FoldHeader(othersExpanded,
                "其他（不在该种族兵营的兵种表里）", "本局仍然可以出，但不在兵营的兵种表上",
                LevelEditorUI.Warn, others.Count + " 个");
            if (othersNow != othersExpanded)
                SetCollapsed(othersFoldKey, !othersNow);
            if (!othersNow)
                return;

            for (int i = 0; i < others.Count; i++)
            {
                EnemyTroopUnlock entry = others[i];
                if (entry == null || entry.Troop == null)
                    continue;

                DrawTroopCard(config, team, entry.Troop);
            }
        }

        private static string ResolveBarracksPrefabKey(RaceDefinition race, bool dark)
        {
            if (race == null || race.Buildings == null)
                return string.Empty;

            BuildingType wanted = dark ? BuildingType.DarkBarracks : BuildingType.Barracks;
            for (int i = 0; i < race.Buildings.Count; i++)
            {
                RaceBuildingEntry entry = race.Buildings[i];
                if (entry != null && entry.buildingType == wanted)
                    return entry.prefabKey;
            }
            return string.Empty;
        }

        private static int CountWhitelistEntries(EnemyTeamConfig team, List<TroopDefinition> troops)
        {
            if (team == null || team.troopUnlocks == null || troops == null)
                return 0;

            int count = 0;
            for (int i = 0; i < team.troopUnlocks.Count; i++)
            {
                EnemyTroopUnlock entry = team.troopUnlocks[i];
                if (entry != null && entry.Troop != null && troops.Contains(entry.Troop))
                    count++;
            }
            return count;
        }

        private static bool WhitelistContains(EnemyTeamConfig team, TroopDefinition troop)
        {
            if (team == null || team.troopUnlocks == null || troop == null)
                return false;

            for (int i = 0; i < team.troopUnlocks.Count; i++)
            {
                EnemyTroopUnlock entry = team.troopUnlocks[i];
                if (entry != null && entry.Troop == troop)
                    return true;
            }
            return false;
        }

        /// <summary>「阶」= 兵种解锁等级（游戏内训练面板的三排）。夹到 1..3，越界值也归到最近的阶。</summary>
        private static int TierOf(TroopDefinition troop)
        {
            if (troop == null)
                return 1;
            return Mathf.Clamp(troop.UnlockLevel, 1, 3);
        }

        private void DrawAddTroopRow(StageConfig config, EnemyTeamConfig team, int teamIndex, RaceDefinition race)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("＋ 加入白名单（按 兵营 × 阶 分组）", EditorStyles.miniButton, GUILayout.Width(260f)))
            {
                var menu = new GenericMenu();
                bool any = false;

                if (race != null)
                {
                    for (int dark = 0; dark <= 1; dark++)
                    {
                        List<TroopDefinition> list = StageResourceIndex.Instance.GetBarracksTroops(race.Id, dark == 1);
                        if (list.Count == 0)
                            continue;

                        any = true;
                        for (int tier = 1; tier <= 3; tier++)
                        {
                            bool headerAdded = false;
                            for (int i = 0; i < list.Count; i++)
                            {
                                TroopDefinition troop = list[i];
                                if (troop == null || TierOf(troop) != tier)
                                    continue;

                                if (!headerAdded)
                                {
                                    menu.AddDisabledItem(new GUIContent("—— " + (dark == 1 ? "黑暗兵营" : "普通兵营") + " · " + tier + " 阶 ——"));
                                    headerAdded = true;
                                }

                                TroopDefinition captured = troop;
                                menu.AddItem(new GUIContent(TroopLabel(captured)), WhitelistContains(team, captured),
                                    () => AddTroopToWhitelist(config, team, captured, true));
                            }
                        }
                    }
                }

                if (any)
                    menu.AddSeparator(string.Empty);

                menu.AddDisabledItem(new GUIContent("—— 工程里全部兵种 ——"));
                for (int tier = 1; tier <= 3; tier++)
                {
                    bool headerAdded = false;
                    for (int i = 0; i < StageResourceIndex.Instance.Troops.Count; i++)
                    {
                        TroopDefinition troop = StageResourceIndex.Instance.Troops[i];
                        if (troop == null || TierOf(troop) != tier)
                            continue;

                        if (!headerAdded)
                        {
                            menu.AddDisabledItem(new GUIContent("—— 其它 · " + tier + " 阶 ——"));
                            headerAdded = true;
                        }

                        TroopDefinition captured = troop;
                        menu.AddItem(new GUIContent(TroopLabel(captured)), WhitelistContains(team, captured),
                            () => AddTroopToWhitelist(config, team, captured, true));
                    }
                }

                menu.ShowAsContext();
            }

            if (GUILayout.Button("搜索兵种…", EditorStyles.miniButton, GUILayout.Width(90f)))
            {
                EnemyTeamConfig targetTeam = team;
                LevelEditorPickerWindow.OpenTroop("兵种白名单", _data.Current.Label + " 第" + (teamIndex + 1) + "队",
                    troop => { AddTroopToWhitelist(config, targetTeam, troop, true); Repaint(); },
                    null, null);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private static string TroopLabel(TroopDefinition troop)
        {
            if (troop == null)
                return "（空）";

            string name = string.IsNullOrEmpty(troop.DisplayName) ? troop.Id : troop.DisplayName;
            return name + "　（" + troop.Id + "　" + TierOf(troop) + " 阶）";
        }

        private void AddTroopToWhitelist(StageConfig config, EnemyTeamConfig team, TroopDefinition troop, bool repaint)
        {
            if (config == null || team == null || troop == null)
                return;

            if (WhitelistContains(team, troop))
            {
                if (repaint)
                    SetStatus("「" + troop.Id + "」已经在这支队伍的白名单里了。", MessageType.Warning);
                return;
            }

            if (team.troopUnlocks == null)
                team.troopUnlocks = new List<EnemyTroopUnlock>();

            Undo.RecordObject(config, "加入兵种白名单");
            var entry = new EnemyTroopUnlock();
            team.troopUnlocks.Add(entry);
            _data.SetTroopUnlock(entry, troop, 0f);

            if (repaint)
            {
                SetStatus("已把「" + troop.Id + "」（" + TierOf(troop) + " 阶）加入白名单，解锁时间 0 = 开局即可出。" +
                          "注意：白名单一旦非空，本队就只能出名单里的兵。", MessageType.Info);
                Repaint();
            }
        }
        #endregion

        #region 兵种卡片
        /// <summary>
        /// 一个兵种的卡片。是否在白名单里由卡片自己判断（白名单是标记，不是视图模式）。
        /// </summary>
        private void DrawTroopCard(StageConfig config, EnemyTeamConfig team, TroopDefinition troop)
        {
            if (troop == null)
                return;

            if (!MatchesTroopSearch(troop))
                return;

            int whitelistIndex = StageEditorData.IndexOfTroop(team, troop);
            bool inWhitelist = whitelistIndex >= 0;
            float unlockTime = inWhitelist ? StageEditorData.GetUnlockTimeOf(team.troopUnlocks[whitelistIndex]) : 0f;

            StageResourceIndex index = StageResourceIndex.Instance;
            float scale = StageFacts.LevelScale(_data.Stage.enemyBarracksLevel);

            EditorGUILayout.BeginHorizontal();

            // ---------- 图标 ----------
            var iconRect = GUILayoutUtility.GetRect(44f, 44f, GUILayout.Width(44f), GUILayout.Height(44f));
            LevelEditorUI.FillRect(iconRect, new Color(0f, 0f, 0f, 0.22f));
            Sprite icon = index.GetSprite(troop.IconKey);
            LevelEditorUI.DrawSpriteFit(iconRect, icon, Color.white, 2f);
            if (icon == null)
                LevelEditorUI.Frame(iconRect, LevelEditorUI.Error, 1f);

            // ---------- 内容 ----------
            EditorGUILayout.BeginVertical();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(string.IsNullOrEmpty(troop.DisplayName) ? troop.Id : troop.DisplayName,
                EditorStyles.miniBoldLabel, GUILayout.Width(138f));
            EditorGUILayout.LabelField(troop.Id, LevelEditorUI.MiniLabel, GUILayout.Width(96f));
            GUILayout.FlexibleSpace();

            if (inWhitelist)
            {
                EditorGUILayout.LabelField("解锁(秒)", LevelEditorUI.MiniLabel, GUILayout.Width(50f));
                float newTime = EditorGUILayout.FloatField(unlockTime, GUILayout.Width(52f));
                if (!Mathf.Approximately(newTime, unlockTime))
                    _data.SetTroopUnlock(team.troopUnlocks[whitelistIndex], troop, newTime);
            }

            if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(34f)))
                EditorGUIUtility.PingObject(troop);

            if (inWhitelist)
            {
                if (GUILayout.Button("移出白名单", EditorStyles.miniButton, GUILayout.Width(74f)))
                {
                    Undo.RecordObject(config, "移出兵种白名单");
                    string removed = troop.Id;
                    team.troopUnlocks.RemoveAt(whitelistIndex);
                    _data.MarkDirty();
                    SetStatus("已把「" + removed + "」移出白名单。", MessageType.Info);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();
                    return;
                }
            }
            else
            {
                if (GUILayout.Button("加入白名单", EditorStyles.miniButton, GUILayout.Width(74f)))
                {
                    AddTroopToWhitelist(config, team, troop, true);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();
                    return;
                }
            }

            EditorGUILayout.EndHorizontal();

            // 第二行：标签 + 静态参数（放第二行，免得把第一行的名字和按钮挤没）
            EditorGUILayout.BeginHorizontal();
            LevelEditorUI.Tag(TroopTagLabel(troop.Tag), LevelEditorUI.Muted);
            LevelEditorUI.Tag(TierOf(troop) + " 阶", LevelEditorUI.Accent);
            if (inWhitelist)
                LevelEditorUI.Tag("白名单", LevelEditorUI.Ok);
            if (troop.RequiresOwnership)
                LevelEditorUI.Tag("需拥有", LevelEditorUI.Warn);
            if (icon == null)
                LevelEditorUI.Tag("图标键无效", LevelEditorUI.Error);

            // 「需兵营 Lv」不在卡片上重复（阶标题已经写明）；参数压短，避免这一行比视口宽
            EditorGUILayout.LabelField(
                "维护费 " + troop.Upkeep + "/秒　冷却 " + troop.Cooldown.ToString("0.#") + "s　产出 " + troop.TrainCount +
                "　AI 阶梯 " + troop.Tier + " · 威胁 " + troop.ThreatScore.ToString("0.##"),
                LevelEditorUI.MiniLabel);
            EditorGUILayout.EndHorizontal();

            // 第三行：本关实际数值（照运行时：生命/攻击/魔攻 × 1.1^(敌方兵营等级-1)）
            StageResourceIndex.PrefabStats stats;
            bool hasStats = index.TryGetPrefabStats(troop.PrefabKey, out stats);

            EditorGUILayout.BeginHorizontal();
            if (hasStats && stats.HasProperty)
            {
                GUI.contentColor = LevelEditorUI.Ok;
                EditorGUILayout.LabelField(
                    "本关实际：生命 " + Mathf.Max(1, Mathf.RoundToInt(stats.MaxHp * scale)) +
                    "　攻 " + Mathf.Max(1, Mathf.RoundToInt(stats.Atk * scale)) +
                    "　魔攻 " + Mathf.Max(1, Mathf.RoundToInt(stats.MagicAtk * scale)) +
                    "　（基础 " + stats.MaxHp + "/" + stats.Atk + "/" + stats.MagicAtk + " ×" + scale.ToString("0.###") +
                    "　移速 " + stats.MoveSpeed.ToString("0.##") + " 格/秒）",
                    LevelEditorUI.MiniLabel);
                GUI.contentColor = Color.white;
            }
            else
            {
                GUI.contentColor = LevelEditorUI.Warn;
                EditorGUILayout.LabelField(hasStats
                    ? "预制体上没挂 GameObjectProperty，读不到基础数值。"
                    : "预制体键「" + troop.PrefabKey + "」不在 PrefabRegistry 里，读不到基础数值。",
                    LevelEditorUI.MiniLabel);
                GUI.contentColor = Color.white;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private bool MatchesTroopSearch(TroopDefinition troop)
        {
            string query = (_troopSearch ?? string.Empty).Trim();
            if (query.Length == 0)
                return true;

            return Contains(troop.Id, query) || Contains(troop.DisplayName, query) ||
                   Contains(troop.PrefabKey, query) || Contains(troop.IconKey, query);
        }

        private static string TroopTagLabel(TroopTag tag)
        {
            switch (tag)
            {
                case TroopTag.Melee: return "近战";
                case TroopTag.Ranged: return "远程";
                case TroopTag.Siege: return "攻城";
                case TroopTag.Tank: return "肉盾";
                case TroopTag.Support: return "辅助";
                default: return "未分类";
            }
        }

        private static bool Contains(string source, string query)
        {
            return !string.IsNullOrEmpty(source) && source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        #endregion

        #region 场上预摆
        private void DrawPlacedObjectsBlock(StageConfig config)
        {
            List<StageObjectData> objects = config.objects;
            int total = objects != null ? objects.Count : 0;

            bool placedExpanded = !IsCollapsed("placed");
            bool placedNow = LevelEditorUI.FoldHeader(placedExpanded, "场上预摆",
                "关卡开始时按世界坐标直接生成、不做网格吸附", LevelEditorUI.Neutral, total + " 个对象");
            if (placedNow != placedExpanded)
                SetCollapsed("placed", !placedNow);
            if (!placedNow)
                return;

            if (total == 0)
            {
                EditorGUILayout.LabelField("本关没有预摆对象。", LevelEditorUI.MiniLabel);
                return;
            }

            // 整关清空：老关卡（Stage4 / 5 / 9999）动辄几十个坐标越界的预摆对象，逐个删不现实。
            // **与上面的搜索 / 筛选无关** —— 所以按钮和弹窗里都把数量写清楚，避免"以为只清筛选出来的那些"。
            EditorGUILayout.BeginHorizontal();
            LevelEditorUI.Hint("整关共 " + total + " 个预摆对象（不受上面的搜索 / 筛选影响）。");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("清空全部预摆对象", EditorStyles.miniButton, GUILayout.Width(120f)))
            {
                string label = _data.Current != null ? _data.Current.Label : "本关";
                if (EditorUtility.DisplayDialog("清空场上预摆",
                        label + " 的 " + total + " 个预摆对象会被全部删除。\n\n" +
                        "· 清空的是整关，和上面的搜索 / 筛选无关；\n" +
                        "· 只是改了内存里的资产，还没写盘 —— 可 Ctrl+Z 撤销，点「保存」才落盘。",
                        "清空 " + total + " 个", "取消"))
                {
                    Undo.RecordObject(config, "清空场上预摆");
                    config.objects.Clear();
                    _data.MarkDirty();

                    // 清空之后**这一帧剩下的控件数量和 Layout 阶段就对不上了**：下面那张按
                    // prefabKey 聚合的表、以及两条告警条都可能整个消失。必须立刻中止本次 OnGUI
                    // 重排，让下一帧按新数据重新 Layout。
                    //
                    // 注意不能只写 `return`：那会跳过外面的 EndScrollView，报 Mismatched LayoutGroup。
                    // 也不能继续往下画：`total` 是本帧开头取的旧值，而 objects 已经空了，
                    // 下面 `objects[i]` 会抛 ArgumentOutOfRangeException（这个真踩过）。
                    GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndHorizontal();

            var order = new List<string>();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var kinds = new Dictionary<string, string>(StringComparer.Ordinal);
            var samples = new Dictionary<string, GameObjectPropertyData>(StringComparer.Ordinal);
            int outside = 0;
            int sideZero = 0;

            int mapW = StageMap.Width;
            int mapH = StageMap.Height;

            // 用 objects.Count 而不是上面那个 total：total 是给标题与告警条用的本帧快照，
            // 一旦有人在本帧中间改了表（例如上面的清空按钮），按下标取就会越界。
            for (int i = 0; i < objects.Count; i++)
            {
                StageObjectData data = objects[i];
                if (data == null)
                    continue;

                string key = string.IsNullOrEmpty(data.prefabKey) ? "（无预制体键）" : data.prefabKey;

                int c;
                if (!counts.TryGetValue(key, out c))
                {
                    counts[key] = 0;
                    order.Add(key);
                }
                counts[key] = c + 1;

                GameObjectPropertyData prop = null;
                if (data.components != null)
                {
                    for (int k = 0; k < data.components.Count; k++)
                    {
                        var p = data.components[k] as GameObjectPropertyData;
                        if (p != null) { prop = p; break; }
                    }
                }

                if (prop != null)
                {
                    if (!samples.ContainsKey(key))
                        samples[key] = prop;

                    if (prop.side == 0)
                        sideZero++;

                    kinds[key] = (prop.objectType & GameObjectType.Character) != 0 ? "兵种" : "建筑";
                }
                else if (!kinds.ContainsKey(key))
                {
                    kinds[key] = "无组件数据";
                }

                Vector3 position = data.transform.position;
                if (position.x < 0f || position.y < 0f || position.x >= mapW || position.y >= mapH)
                    outside++;
            }

            order.Sort((a, b) =>
            {
                string ka, kb;
                kinds.TryGetValue(a, out ka);
                kinds.TryGetValue(b, out kb);
                int cmp = string.Compare(ka, kb, StringComparison.CurrentCulture);
                return cmp != 0 ? cmp : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });

            string query = (_troopSearch ?? string.Empty).Trim();

            for (int i = 0; i < order.Count; i++)
            {
                string key = order[i];

                if (query.Length > 0 && !Contains(key, query))
                    continue;

                string kind;
                kinds.TryGetValue(key, out kind);

                GameObjectPropertyData prop;
                samples.TryGetValue(key, out prop);

                EditorGUILayout.BeginHorizontal();

                var thumbRect = GUILayoutUtility.GetRect(30f, 30f, GUILayout.Width(30f), GUILayout.Height(30f));
                LevelEditorUI.FillRect(thumbRect, new Color(0f, 0f, 0f, 0.2f));

                TroopDefinition asTroop = FindTroopByPrefabKey(key);
                Sprite icon = asTroop != null ? StageResourceIndex.Instance.GetSprite(asTroop.IconKey) : null;
                if (icon != null)
                    LevelEditorUI.DrawSpriteFit(thumbRect, icon, Color.white, 2f);
                else
                    LevelEditorUI.DrawEmptySlot(thumbRect, LevelEditorUI.Muted, "无图");

                EditorGUILayout.LabelField(key, EditorStyles.miniBoldLabel, GUILayout.Width(180f));

                if (asTroop != null)
                    LevelEditorUI.Tag(TierOf(asTroop) + " 阶", LevelEditorUI.Accent);

                LevelEditorUI.Tag(kind, kind == "兵种" ? LevelEditorUI.Friendly : LevelEditorUI.Neutral);

                bool keyOk = StageResourceIndex.Instance.HasPrefab(key);
                if (!keyOk)
                    LevelEditorUI.Tag("键不在注册表", LevelEditorUI.Error);

                if (prop != null)
                {
                    string sideText = prop.side == 0
                        ? "队伍0(会被当敌方)"
                        : (prop.side % 2 == 0 ? "队伍" + prop.side + "(敌方)" : "队伍" + prop.side + "(我方)");
                    EditorGUILayout.LabelField(
                        "×" + counts[key] + "　" + sideText + "　生命 " + prop.maxHp + "　攻 " + prop.atk + "/" + prop.magicAtk +
                        "　占地 " + prop.occupySpace.x + "×" + prop.occupySpace.y,
                        LevelEditorUI.MiniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField("×" + counts[key], LevelEditorUI.MiniLabel);
                }

                GUILayout.FlexibleSpace();
                if (keyOk && GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(36f)))
                    EditorGUIUtility.PingObject(StageResourceIndex.Instance.GetPrefab(key));
                EditorGUILayout.EndHorizontal();
            }

            if (outside > 0)
            {
                EditorGUILayout.HelpBox(
                    "有 " + outside + " / " + total + " 个预摆对象的世界坐标落在运行时地图（" + mapW + "×" + mapH +
                    "）之外。运行时是按世界坐标直接生成的、不做网格吸附，所以它们确实会出现在界外。",
                    MessageType.Warning);
            }

            if (sideZero > 0)
            {
                EditorGUILayout.HelpBox(
                    "有 " + sideZero + " 个对象的 side = 0，运行时会按敌方处理并注入敌方等级。中立装饰物应改成奇数队（如 1）。",
                    MessageType.Warning);
            }
        }

        /// <summary>按预制体键反查兵种定义（预摆对象只有键，没有资产引用）。</summary>
        private static TroopDefinition FindTroopByPrefabKey(string prefabKey)
        {
            if (string.IsNullOrEmpty(prefabKey))
                return null;

            IReadOnlyList<TroopDefinition> troops = StageResourceIndex.Instance.Troops;
            for (int i = 0; i < troops.Count; i++)
            {
                TroopDefinition troop = troops[i];
                if (troop != null && string.Equals(troop.PrefabKey, prefabKey, StringComparison.OrdinalIgnoreCase))
                    return troop;
            }
            return null;
        }
        #endregion

        #endregion
        #region 右栏
        /// <summary>收起 / 展开右栏。收起后中栏自动拿到整条宽度。</summary>
        private void SetRightCollapsed(bool collapsed)
        {
            _rightCollapsed = collapsed;
            LevelEditorPrefs.RightCollapsed = collapsed;
            Repaint();
        }

        private void DrawRightColumn()
        {
            // 标题栏右侧的 ✕：收起整条右栏，把宽度让给中栏（体检面板很占地方）
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(24f)))
            {
                SetRightCollapsed(true);
                EditorGUILayout.EndHorizontal();
                return;
            }
            EditorGUILayout.EndHorizontal();
            int tab = GUILayout.Toolbar(_rightTab, RightTabLabels, EditorStyles.toolbarButton, GUILayout.Height(22f));
            if (tab != _rightTab)
            {
                _rightTab = tab;
                LevelEditorPrefs.RightTab = tab;
            }

            if (!_data.IsLoaded)
            {
                EditorGUILayout.HelpBox("还没有选中关卡。", MessageType.Info);
                return;
            }

            switch (_rightTab)
            {
                case 1:
                    DrawIssuesTab();
                    break;
                case 2:
                    DrawSettingsTab();
                    break;
                default:
                    DrawFormTab();
                    break;
            }
        }

        #region 数值表单
        private void DrawFormTab()
        {
            StageConfig config = _data.Stage;
            _formScroll = EditorGUILayout.BeginScrollView(_formScroll);

            DrawPlaySection(config);
            DrawBaseSection(config);
            DrawEnemyLevelSection(config);
            DrawEconomySection(config);
            DrawPlayerStartGoldSection(config);
            DrawEnemyTeamSections(config);
            DrawRewardSection(config);
            DrawPresentationSection(config);
            DrawGlobalRuleSection(config);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("改完点工具栏「保存 (Ctrl+S)」。所有改动都在 Ctrl+Z 里。", LevelEditorUI.MiniLabel);

            EditorGUILayout.EndScrollView();
        }

        private void DrawPlaySection(StageConfig config)
        {
            if (FormSection("玩法与时限", "玩法与时限", "带 ● 的是玩家直接感受到的"))
                return;

            EnumRow("● 玩法类型", config.stageType, v => config.stageType = v,
                "防守 = 守满时限获胜；进攻 = 摧毁敌方大本营；Boss = 击败 Boss。时限为 0 表示不限时。");

            FloatRow("防守时限（秒）", config.DefenseTime, v => config.DefenseTime = v,
                "只在玩法类型 = 防守 时生效（StageGoalTracker.cs:64-79）。");
            FloatRow("进攻时限（秒）", config.attackTimeLimit, v => config.attackTimeLimit = v, "0 = 不限时。");
            FloatRow("Boss 时限（秒）", config.bossTimeLimit, v => config.bossTimeLimit = v, "0 = 不限时。");

            switch (config.stageType)
            {
                case UserGlobalInfo.StageType.Attack:
                    EditorGUILayout.HelpBox("本关按「进攻」结算：到点未摧毁敌方大本营即失败。", MessageType.Info);
                    break;
                case UserGlobalInfo.StageType.Boss:
                    EditorGUILayout.HelpBox("本关按「Boss」结算：到点未击败 Boss 即失败。Boss 预制体键在下面「阵地」里填。", MessageType.Info);
                    break;
                default:
                    EditorGUILayout.HelpBox("本关按「防守」结算：守满时限即胜利（提前推掉敌方大本营也直接胜利）。", MessageType.Info);
                    break;
            }

            // 关卡编号单独写一行（不用 IntRow）：IntRow 是「先 Undo + 标脏、再调 setter」，
            // 而改编号要先弹确认 —— 用 IntRow 的话点「取消」也会把资产标成"有未保存的修改"。
            EditorGUI.BeginChangeCheck();
            int newStageId = EditorGUILayout.IntField(
                new GUIContent("关卡编号（stageId）",
                    "选关按地址 Stage{编号} 连续加载，「下一关」取 Stage{编号+1}。改编号牵连很多地方，会先弹确认。"),
                config.stageId);
            if (EditorGUI.EndChangeCheck())
                ChangeStageId(config, newStageId);

            // 编号与文件名不一致时**当场**提示 —— 不等体检。
            // 体检里本来就有这条规则（stageId.fileMismatch，Error），但体检不是必经之路：
            // 改编号 → 保存 → 进关，这条错误就不会进入视野。所以把它挪到动作旁边。
            string expectedName = "Stage" + config.stageId;
            string currentName = _data.Current != null ? _data.Current.Name : null;
            if (!string.IsNullOrEmpty(currentName) && !string.Equals(currentName, expectedName, StringComparison.Ordinal))
            {
                EditorGUILayout.HelpBox(
                    "资产文件名与编号不一致：文件是「" + currentName + "」，而编号 " + config.stageId +
                    " 要求文件名是「" + expectedName + "」。\n" +
                    "两种改法：① 把上面的编号改回「" + currentName + "」里的数字；" +
                    "② 在 Project 面板把资产重命名为「" + expectedName + "」（GUID 不变，已经引用它的地方不受影响）。\n" +
                    "还有一件事别忘了：选关按地址 Stage1、Stage2… 连续加载，遇到缺口就停止 —— " +
                    "改完编号要去 Addressables 组 StageConfig 里登记地址 " + expectedName + "。",
                    MessageType.Error);

                if (GUILayout.Button("在 Project 里定位这个资产", EditorStyles.miniButton, GUILayout.Width(180f)))
                    EditorGUIUtility.PingObject(config);
            }
        }

        /// <summary>
        /// 改关卡编号。**必须先弹确认**：编号牵连的东西比看上去多，而且大多是
        /// "改完当场看不出来、出包或读档时才发现"的错。依据：
        /// · `StageConfigLoader.LoadConfigs`（GameStart/StageConfigLoader.cs）按地址 Stage{i} 连续加载，**遇到第一个缺口就停止**；
        /// · `GameOverActionButton.cs:124`「下一关」取 Stage{编号+1}；
        /// · 存档按编号记进度。
        ///
        /// 确认之后才 Undo + 标脏 + 重扫（重扫是为了让 StageInfo 里的编号跟着更新，
        /// 否则左栏卡片和状态栏还显示旧编号）。
        /// </summary>
        private void ChangeStageId(StageConfig config, int newId)
        {
            if (config == null || newId == config.stageId)
                return;

            if (newId < 0)
            {
                SetStatus("关卡编号必须是非负整数（当前 " + config.stageId + "）。", MessageType.Error);
                Repaint();
                return;
            }

            int oldId = config.stageId;
            string fileName = _data.Current != null ? _data.Current.Name : string.Empty;
            bool wasLocalTest = oldId >= 880;
            bool willBeLocalTest = newId >= 880;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("要把 " + (string.IsNullOrEmpty(fileName) ? "本关" : fileName) +
                          " 的编号从 " + oldId + " 改成 " + newId + " 吗？");
            sb.AppendLine();
            sb.AppendLine("牵连到的地方：");
            sb.AppendLine("· 选关按地址 Stage1、Stage2… 连续加载，遇到第一个缺口就停止 —— 缺口之后的关卡玩家永远看不到");
            if (!string.IsNullOrEmpty(fileName) && !string.Equals(fileName, "Stage" + newId, StringComparison.Ordinal))
                sb.AppendLine("· 资产文件名仍是「" + fileName + "」，而体检要求它等于「Stage" + newId + "」");
            sb.AppendLine("· Addressables 里登记的地址是「Stage" + oldId + "」，改成 " + newId +
                          " 后要另外登记地址 Stage" + newId);
            if (wasLocalTest != willBeLocalTest)
            {
                sb.AppendLine(willBeLocalTest
                    ? "· 编号 ≥880 → 变成「本地测试关」：不再要求上架 Addressables，也不参与批量操作"
                    : "· 编号 <880 → **不再是「本地测试关」**：体检会开始要求它上架 Addressables（组里没有就会报错）");
            }
            sb.AppendLine("· 存档按编号记进度，改编号会让老存档对不上");
            sb.AppendLine();
            sb.Append("确定要改吗？");

            if (!EditorUtility.DisplayDialog("修改关卡编号", sb.ToString(), "改编号为 " + newId, "取消"))
            {
                Repaint();   // 输入框画的是 config.stageId，重画一次就回到真实值
                return;
            }

            Undo.RecordObject(config, "修改关卡编号");
            config.stageId = newId;
            _data.MarkDirty();

            // StageInfo 里的编号是快照，不跟着更新的话左栏卡片与状态栏还显示旧编号
            if (_data.Current != null)
                _data.Current.StageId = newId;

            RefreshStageList();
            EnsureIssues();
            Repaint();
        }

        private void DrawBaseSection(StageConfig config)
        {
            if (FormSection("阵地", "阵地", "坐标都是网格坐标（1 格 = 1 世界单位）"))
                return;

            ToggleRow("启用我方大本营", config.hasFriendlyMainBaseGridPosition, v => config.hasFriendlyMainBaseGridPosition = v,
                "关闭后运行时不会生成我方大本营，且「被拆即败」的判定整体失效（StageGoalTracker.cs:53）。");

            using (new EditorGUI.DisabledScope(!config.hasFriendlyMainBaseGridPosition))
            {
                Vector2IntRow("我方大本营（左下格）", config.friendlyMainBaseGridPosition, v => config.friendlyMainBaseGridPosition = v,
                    "大本营预制体由玩家所选种族决定，这里只给位置。");
            }

            // ---------- 打开这个开关会踩的致命坑，当场提示 ----------
            // 运行时按**玩家所选种族**取 MainBase 预制体，取不到就 LogError + return false，
            // 把**整关**的实例化掐掉（进关只有一张空地图，Console 报 Failed to instantiate stage objects.）。
            // Stage2 / Stage3 各踩过一次，所以提示放在开关旁边，而不是等体检跑完才看见。
            // （卡在哪：关卡的资源集合只来自本关的六张清单，缺了就是没预载 —— 见 key.raceBase 那条规则。）
            if (config.hasFriendlyMainBaseGridPosition)
            {
                bool missingBase;
                List<string> missingKeys = StageValidator.MissingRaceBuildingKeys(config, out missingBase);
                if (missingBase)
                {
                    EditorGUILayout.HelpBox(
                        "本关 prefabs 清单里没有种族大本营键（缺 " + missingKeys.Count + " 个）。\n" +
                        "开着我方大本营 = 运行时要去取「玩家所选种族」的 MainBase 预制体；" +
                        "清单里没有它就不会被预载，取不到时运行时直接中止**整关**实例化 —— " +
                        "进关只有一张空地图。",
                        MessageType.Error);

                    if (GUILayout.Button("把缺的 " + missingKeys.Count + " 个种族建筑键补进本关 prefabs（只追加，不删不改）"))
                    {
                        Undo.RecordObject(config, "补种族建筑键");
                        int added = StageValidator.AddMissingRaceBuildingKeys(config);
                        _data.MarkDirty();
                        _issuesDirty = true;
                        SetStatus("已补进 " + added + " 个种族建筑键。点「保存」写盘后生效。", MessageType.Info);
                    }
                }
            }

            IntRow("敌我基地距离（格）", config.enemyBaseDistance, v => config.enemyBaseDistance = Mathf.Max(1, v),
                "敌方大本营 x = 我方大本营 x + 这个距离（StageGoalTracker.cs:196）。");

            int midpoint = StageFacts.MidpointX(config);
            LevelEditorUI.Hint("推导：敌方大本营 x = " + StageFacts.EnemyBaseX(config) +
                               "　半场分界 x = " + midpoint + "（两侧闭区间，双方都能在中点格建造）");

            Vector2IntRow("Boss 出生格（左下）", config.bossGridPosition, v => config.bossGridPosition = v, "只在 Boss 关生效。");

            EditorGUILayout.BeginHorizontal();
            TextRow("Boss 预制体键", config.bossPrefabKey, v => config.bossPrefabKey = (v ?? string.Empty).Trim(),
                "须登记 PrefabRegistry 并按关卡预载。空 = Boss 关永远无法获胜。");
            if (GUILayout.Button("选预制体", EditorStyles.miniButton, GUILayout.Width(66f)))
            {
                LevelEditorPickerWindow.Open("Boss 预制体键", _data.Current.Label, StageResourceIndex.StageKeyKind.Prefab,
                    key => { config.bossPrefabKey = key; _data.MarkDirty(); Repaint(); },
                    () => { config.bossPrefabKey = string.Empty; _data.MarkDirty(); Repaint(); },
                    null);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawEnemyLevelSection(StageConfig config)
        {
            if (FormSection("本关敌方等级", "本关敌方等级", "决定敌方建筑血量与其训练兵种的生命/攻击/魔攻"))
                return;

            IntRow("敌方普通兵营等级", config.enemyBarracksLevel, v => config.enemyBarracksLevel = Mathf.Max(1, v));
            IntRow("敌方黑暗兵营等级", config.enemyDarkBarracksLevel, v => config.enemyDarkBarracksLevel = Mathf.Max(1, v));
            IntRow("敌方哨塔等级", config.enemySentryTowerLevel, v => config.enemySentryTowerLevel = Mathf.Max(1, v));
            IntRow("敌方防御魔法等级", config.enemyDefenseMagicLevel, v => config.enemyDefenseMagicLevel = Mathf.Max(1, v));
            IntRow("敌方攻击魔法等级", config.enemyAttackMagicLevel, v => config.enemyAttackMagicLevel = Mathf.Max(1, v));

            float scale = StageFacts.LevelScale(config.enemyBarracksLevel);
            LevelEditorUI.Hint("换算：等级缩放 = 1.1^(等级-1) = ×" + scale.ToString("0.###") +
                               "（作用在生命 / 攻击 / 魔攻上，射程与移速不缩放）");
        }

        private void DrawEconomySection(StageConfig config)
        {
            if (FormSection("玩家经济", "玩家经济", "决定玩家的收入曲线"))
                return;

            ToggleRow("启用收入曲线", config.useIncomeCurve, v => config.useIncomeCurve = v,
                "关闭时沿用场景 Coins 的时间阶梯；开启时用下面的基准值与阶段表。");

            using (new EditorGUI.DisabledScope(!config.useIncomeCurve))
            {
                FloatRow("开局每秒金币", config.baseGoldPerSecond, v => config.baseGoldPerSecond = Mathf.Max(0f, v));

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("收入增长阶段", EditorStyles.boldLabel, GUILayout.Width(120f));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("＋ 加一阶段", EditorStyles.miniButton, GUILayout.Width(84f)))
                {
                    Undo.RecordObject(config, "新增收入阶段");
                    if (config.incomeGrowth == null)
                        config.incomeGrowth = new List<IncomeGrowthPhase>();

                    float lastEnd = 0f;
                    if (config.incomeGrowth.Count > 0 && config.incomeGrowth[config.incomeGrowth.Count - 1] != null)
                        lastEnd = config.incomeGrowth[config.incomeGrowth.Count - 1].endTime;

                    config.incomeGrowth.Add(new IncomeGrowthPhase
                    {
                        startTime = lastEnd,
                        endTime = lastEnd + 60f,
                        stepInterval = 10f,
                        stepAmount = 10f
                    });
                    _data.MarkDirty();
                }
                EditorGUILayout.EndHorizontal();

                if (config.incomeGrowth == null || config.incomeGrowth.Count == 0)
                {
                    EditorGUILayout.LabelField("（没有阶段：收入恒为「开局每秒金币」）", LevelEditorUI.MiniLabel);
                }
                else
                {
                    for (int i = 0; i < config.incomeGrowth.Count; i++)
                    {
                        IncomeGrowthPhase phase = config.incomeGrowth[i];
                        if (phase == null)
                            continue;

                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                        // ---------- 第一行：阶段号 + 起 / 止 + 删 ----------
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("阶段 " + (i + 1), EditorStyles.boldLabel, GUILayout.Width(54f));

                        float start = FloatField("起（秒）", phase.startTime, 56f);
                        if (!Mathf.Approximately(start, phase.startTime))
                        {
                            Undo.RecordObject(config, "修改收入阶段");
                            phase.startTime = Mathf.Max(0f, start);
                            _data.MarkDirty();
                        }

                        float end = FloatField("止（秒）", phase.endTime, 56f);
                        if (!Mathf.Approximately(end, phase.endTime))
                        {
                            Undo.RecordObject(config, "修改收入阶段");
                            phase.endTime = Mathf.Max(0f, end);
                            _data.MarkDirty();
                        }

                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button("删", EditorStyles.miniButton, GUILayout.Width(26f)))
                        {
                            Undo.RecordObject(config, "删除收入阶段");
                            config.incomeGrowth.RemoveAt(i);
                            _data.MarkDirty();
                            // 跳出前必须把本阶段块开着的两个水平组和垂直组配平，
                            // 否则本帧组栈不平衡（Unity 报 Mismatched LayoutGroup）。
                            EditorGUILayout.EndHorizontal();
                            EditorGUILayout.EndVertical();
                            break;
                        }
                        EditorGUILayout.EndHorizontal();

                        // ---------- 第二行：「每步」两个字段单独一行 ----------
                        // 原来 4 个字段挤在同一行：右栏拖到最窄（320）时标签必被裁，
                        // 表现就是「每步增量」显示成「每步增」—— 看着像"字段显示不全"。
                        EditorGUILayout.BeginHorizontal();

                        float interval = FloatField("每步秒数", phase.stepInterval, 56f);
                        if (!Mathf.Approximately(interval, phase.stepInterval))
                        {
                            Undo.RecordObject(config, "修改收入阶段");
                            phase.stepInterval = Mathf.Max(0.1f, interval);
                            _data.MarkDirty();
                        }

                        float amount = FloatField("每步增量", phase.stepAmount, 56f);
                        if (!Mathf.Approximately(amount, phase.stepAmount))
                        {
                            Undo.RecordObject(config, "修改收入阶段");
                            phase.stepAmount = Mathf.Max(0f, amount);
                            _data.MarkDirty();
                        }

                        EditorGUILayout.EndHorizontal();

                        bool openEnded = phase.endTime <= phase.startTime;
                        int atLimit = StageFacts.ComputeCurveIncome(config.baseGoldPerSecond, new List<IncomeGrowthPhase> { phase },
                            openEnded ? phase.startTime + phase.stepInterval * 12f : phase.endTime);
                        LevelEditorUI.Hint(openEnded
                            ? "止 ≤ 起 = 持续到关卡结束；12 步后约为 " + atLimit + "/秒"
                            : "到阶段末约为 " + atLimit + "/秒");

                        EditorGUILayout.EndVertical();
                    }
                }
            }

            if (!config.useIncomeCurve)
            {
                EditorGUILayout.HelpBox("当前走场景 Coins 的时间阶梯，本关的基准值与阶段表不生效。", MessageType.Info);
            }
        }

        /// <summary>
        /// 「玩家初始金币」分区。
        ///
        /// 这个值**不在关卡资产里**：它是游戏场景 `StageScene` 上 Coins 组件的 `coins`（场景序列化值），
        /// 进关卡时就是玩家的开局余额，而且 `Coins.OnLevelStart()` 不会重置它 → **全局一个值，所有关卡共用**。
        /// 所以这里做的是"读场景 / 写场景（一行、先备份）"，并在界面上把这点写清楚，
        /// 免得以为它是"只影响当前关卡"的字段。
        /// </summary>
        private void DrawPlayerStartGoldSection(StageConfig config)
        {
            if (FormSection("玩家初始金币", "玩家初始金币", "本关可以单独指定；不指定就沿用全局值"))
                return;

            // ── 第一层：本关覆盖（关卡字段，每关可以不同）──────────────────────────────
            LevelEditorUI.Section("本关（每关不同）", "关卡字段 hasPlayerStartGold / playerStartGold");
            DrawStageStartGoldRows(config);

            // ── 第二层：全局默认（游戏场景上的 Coins.coins）────────────────────────────
            LevelEditorUI.Section("全局默认（游戏场景）",
                "没勾「本关独立指定」时用的值 —— 写在 StageScene 的 Coins 组件上，所有关卡共用");

            int current;
            string readMessage;
            bool read = PlayerEconomy.TryReadStartCoins(out current, out readMessage);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("场景里的当前值", GUILayout.Width(100f));
            if (read)
            {
                EditorGUILayout.LabelField(current.ToString(), EditorStyles.boldLabel, GUILayout.Width(80f));
                _startGoldEdit = current;
            }
            else
            {
                GUI.contentColor = LevelEditorUI.Error;
                EditorGUILayout.LabelField(readMessage, LevelEditorUI.MiniLabel);
                GUI.contentColor = Color.white;
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("重新读取", EditorStyles.miniButton, GUILayout.Width(64f)))
            {
                _issuesDirty = true;
                Repaint();
            }
            if (GUILayout.Button("打开游戏场景", EditorStyles.miniButton, GUILayout.Width(90f)))
            {
                if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    EditorSceneManager.OpenScene(PlayerEconomy.GameplayScenePath, OpenSceneMode.Single);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _startGoldEdit = EditorGUILayout.IntField("要写入的值", Mathf.Max(0, _startGoldEdit));
            using (new EditorGUI.DisabledScope(!read || PlayerEconomy.IsGameplaySceneLoaded()))
            {
                if (GUILayout.Button("写入场景", EditorStyles.miniButton, GUILayout.Width(80f)))
                {
                    if (!EditorUtility.DisplayDialog("写入玩家初始金币",
                            "将把游戏场景 " + PlayerEconomy.GameplayScenePath + " 里 Coins 的初始金币改成 " +
                            Mathf.Max(0, _startGoldEdit) + "。\n\n" +
                            "· 这是**全局值**：所有关卡开局都用它（不是只影响当前关卡）\n" +
                            "· 只改一行、写前自动备份场景资产\n" +
                            "· 场景正开在 Unity 里时不能写（会被场景保存覆盖）",
                            "写入", "取消"))
                        return;

                    string message;
                    if (PlayerEconomy.TryWriteStartCoins(Mathf.Max(0, _startGoldEdit), out message))
                    {
                        SetStatus(message, MessageType.Info);
                        Debug.Log("[关卡编辑器] " + message);
                    }
                    else
                    {
                        SetStatus(message, MessageType.Warning);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            if (PlayerEconomy.IsGameplaySceneLoaded())
            {
                EditorGUILayout.HelpBox("游戏场景当前正开着：先切到别的场景再写，「写入场景」现在不可用。" +
                                        "（否则 Unity 保存场景时会把这次写入盖回去。）", MessageType.Warning);
            }

            EditorGUILayout.LabelField("依据：Coins.coins 是场景序列化值（Coins.cs:9），OnLevelStart() 里会应用本关的" +
                                       "hasPlayerStartGold（Coins.cs 的 ApplyStageStartGold）→ 关卡勾了就每关不同，没勾就沿用这个全局值。",
                LevelEditorUI.RichLabel);
        }

        /// <summary>
        /// 关卡侧的「玩家初始金币」两个字段（`hasPlayerStartGold` / `playerStartGold`）。
        ///
        /// 这里用 **SerializedObject 按字段名**读写，而不是直接引用 C# 字段：工具因此**不依赖**
        /// "游戏代码刚加完字段、Unity 还没重新编译出新的 CageFightRuntime.dll" 这个时间点 ——
        /// 字段还没生效时界面会明确告诉你切回 Unity 编译一次，而不是抛编译错误。
        /// </summary>
        private void DrawStageStartGoldRows(StageConfig config)
        {
            var serialized = new SerializedObject(config);
            SerializedProperty flag = serialized.FindProperty("hasPlayerStartGold");
            SerializedProperty value = serialized.FindProperty("playerStartGold");

            if (flag == null || value == null)
            {
                EditorGUILayout.HelpBox("关卡字段 hasPlayerStartGold / playerStartGold 还没生效：" +
                                        "游戏代码里已经加了它们（StageConfig.cs），但 Unity 还没重新编译出新的运行时程序集。" +
                                        "切回 Unity 让它编译一次（窗口会自动重新加载），就能在这里勾选与填值。",
                    MessageType.Warning);
                return;
            }

            EditorGUI.BeginChangeCheck();
            bool newFlag = EditorGUILayout.ToggleLeft("本关独立指定开局金币（不与全局值相关）", flag.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(config, "修改本关初始金币开关");
                flag.boolValue = newFlag;
                serialized.ApplyModifiedProperties();
                _data.MarkDirty();
                _issuesDirty = true;
            }

            using (new EditorGUI.DisabledScope(!flag.boolValue))
            {
                EditorGUI.BeginChangeCheck();
                int newValue = EditorGUILayout.IntField("本关开局金币", Mathf.Max(0, value.intValue));
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(config, "修改本关初始金币");
                    value.intValue = Mathf.Max(0, newValue);
                    serialized.ApplyModifiedProperties();
                    _data.MarkDirty();
                    _issuesDirty = true;
                }
            }

            EditorGUILayout.LabelField(flag.boolValue
                    ? "本关用自己的值：" + Mathf.Max(0, value.intValue) + "（与下面那个全局值无关）"
                    : "本关没有覆盖 → 沿用下面的全局值。",
                LevelEditorUI.MiniLabel);
        }

        private void DrawEnemyTeamSections(StageConfig config)
        {
            List<EnemyTeamConfig> teams = config.enemyTeams;
            int count = teams != null ? teams.Count : 0;

            if (FormSection("敌方队伍", "敌方队伍", count + " 支（最多 4 支：队伍 2/4/6/8）"))
                return;

            if (GUILayout.Button("＋ 新增一支敌方队伍", GUILayout.Height(22f)))
            {
                Undo.RecordObject(config, "新增敌方队伍");
                if (config.enemyTeams == null)
                    config.enemyTeams = new List<EnemyTeamConfig>();

                int teamId = 2;
                var used = new HashSet<int>();
                for (int i = 0; i < config.enemyTeams.Count; i++)
                {
                    if (config.enemyTeams[i] != null)
                        used.Add(config.enemyTeams[i].teamId);
                }
                while (used.Contains(teamId))
                    teamId += 2;

                config.enemyTeams.Add(new EnemyTeamConfig
                {
                    teamId = teamId,
                    raceId = string.Empty,
                    startGold = 200,
                    baseGoldPerSecond = 10f,
                    homeGridPosition = new Vector2Int(StageFacts.EnemyBaseX(config), Mathf.Clamp(config.friendlyMainBaseGridPosition.y, 0, Mathf.Max(0, StageMap.Height - 1))),
                    buildSlots = new List<EnemyBuildSlot>()
                });
                _data.MarkDirty();
            }

            if (count == 0)
            {
                EditorGUILayout.LabelField("（本关没有敌方队伍：不会有敌方 AI 出兵）", LevelEditorUI.MiniLabel);
                return;
            }

            for (int i = 0; i < teams.Count; i++)
            {
                EnemyTeamConfig team = teams[i];
                if (team == null)
                    continue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                string rightTeamFoldKey = "team." + i;
                bool rightTeamExpanded = !IsCollapsed(rightTeamFoldKey);
                bool rightTeamNow = LevelEditorUI.FoldToggle(rightTeamExpanded);
                if (rightTeamNow != rightTeamExpanded)
                {
                    SetCollapsed(rightTeamFoldKey, !rightTeamNow);
                    rightTeamExpanded = rightTeamNow;
                }

                EditorGUILayout.LabelField("第 " + (i + 1) + " 支队伍", EditorStyles.boldLabel, GUILayout.Width(90f));
                if (i == 0)
                    LevelEditorUI.Tag("敌方大本营取这一支的种族", LevelEditorUI.Accent);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("删", EditorStyles.miniButton, GUILayout.Width(26f)))
                {
                    Undo.RecordObject(config, "删除敌方队伍");
                    config.enemyTeams.RemoveAt(i);
                    _data.MarkDirty();
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                if (!rightTeamExpanded)
                {
                    EditorGUILayout.LabelField("（已收起 —— 点左边的 ▶ 展开）", LevelEditorUI.MiniLabel);
                    EditorGUILayout.EndVertical();
                    continue;
                }

                IntRow("队伍编号", team.teamId, v => team.teamId = v, "必须是 ≥2 的偶数（偶数队才是敌方）。");

                EditorGUILayout.BeginHorizontal();
                TextRow("种族 ID", team.raceId, v => team.raceId = (v ?? string.Empty).Trim(), "与 RaceDefinition.Id 一致，决定可建建筑与兵种。");
                if (GUILayout.Button("选种族", EditorStyles.miniButton, GUILayout.Width(56f)))
                {
                    RaceDefinition[] races = new RaceDefinition[StageResourceIndex.Instance.Races.Count];
                    for (int r = 0; r < races.Length; r++)
                        races[r] = StageResourceIndex.Instance.Races[r];

                    var menu = new GenericMenu();
                    if (races.Length == 0)
                        menu.AddDisabledItem(new GUIContent("（工程里没有 RaceDefinition）"));
                    for (int r = 0; r < races.Length; r++)
                    {
                        RaceDefinition race = races[r];
                        if (race == null)
                            continue;
                        menu.AddItem(new GUIContent(race.Id + "　" + race.DisplayName), false, () =>
                        {
                            Undo.RecordObject(config, "选择种族");
                            team.raceId = race.Id;
                            _data.MarkDirty();
                            Repaint();
                        });
                    }
                    menu.ShowAsContext();
                }
                EditorGUILayout.EndHorizontal();

                IntRow("起始金币", team.startGold, v => team.startGold = Mathf.Max(0, v));
                FloatRow("每秒金币", team.baseGoldPerSecond, v => team.baseGoldPerSecond = Mathf.Max(0f, v));

                // 敌队的收入曲线：与玩家侧同构，但**无条件生效**（不受 useIncomeCurve 影响）。
                if (team.incomeGrowth == null)
                    team.incomeGrowth = new List<IncomeGrowthPhase>();
                DrawIncomeGrowthList(config, team.incomeGrowth, team.baseGoldPerSecond, "收入增长阶段");

                // —— 经济与扩张节奏（原先这些字段只能手改资产，界面上没有）——
                LevelEditorUI.Section("扩张节奏", "AI 什么时候攒钱、什么时候开新建筑");
                IntRow("预留底线", team.reserve, v => team.reserve = Mathf.Max(0, v),
                    "净收入低于这个值时不开启新的维护费来源（0 = 只要求净收入为正）。");
                FloatRow("盈余持续（秒）", team.surplusSeconds, v => team.surplusSeconds = Mathf.Max(0.5f, v),
                    "盈余要持续这么久才扩张（开新兵营 / 上兵种）。");

                LevelEditorUI.Section("策略与回防", "被打到什么程度才回防、回防多远、愿意花多少维护费");
                EnumRow("策略", team.strategy, v => team.strategy = v, "龟缩 = 重经济防守；均衡；猛攻 = 重产出进攻。");
                FloatRow("回防威胁阈值", team.threatThreshold, v => team.threatThreshold = Mathf.Max(0f, v),
                    "阵地半径内玩家单位威胁值超过它 → 进入回防态。");
                FloatRow("回防半径（格）", team.defendRadius, v => team.defendRadius = Mathf.Max(1f, v),
                    "以阵地锚点为中心统计玩家威胁的范围。");
                FloatRow("防守支出占比", team.defenseSpendRatio, v => team.defenseSpendRatio = Mathf.Clamp01(v),
                    "防守建筑维护费占净收入的比例上限（回防时不超过，其余留给兵种生产）。");

                Vector2IntRow("阵地锚点（格）", team.homeGridPosition, v => team.homeGridPosition = v,
                    "回防统计与动态补建都以它为圆心。");

                LevelEditorUI.Section("建造", "槽位表之外的补充规则");
                IntRow("兵营上限", team.maxBarracks, v => team.maxBarracks = Mathf.Max(0, v),
                    "普通兵营的数量上限（**不含黑暗兵营**）：槽位表用完后，经济允许时在锚点附近动态补建。" +
                    "运行时的上限是硬约束 —— 槽位表里的普通兵营槽位超过这个数时，多出来的不会开工。");
                IntRow("黑暗兵营上限", team.maxDarkBarracks, v => team.maxDarkBarracks = Mathf.Max(0, v),
                    "黑暗兵营的数量上限，**与「兵营上限」分开计算**（普通兵营不占这个名额，黑暗兵营也不占普通兵营的名额）。" +
                    "另外还要求本方大本营局内等级 ≥ 2（UpgradeLevelRules.CanBuildDarkBarracksForSide）才动手。" +
                    "填 0 = 这一局不建黑暗兵营。");
                IntRow("哨塔上限", team.maxSentries, v => team.maxSentries = Mathf.Max(0, v),
                    "槽位表用完后，回防/富余时动态补建哨塔的数量上限。");
                ToggleRow("打乱建造顺序", team.randomizeBuildOrder, v => team.randomizeBuildOrder = v,
                    "每局随机打乱槽位建造顺序（增加变化性）。");
                Vector2Row("开工间隔（秒）", team.buildGapRange, v => team.buildGapRange = new Vector2(
                        Mathf.Max(0.1f, v.x), Mathf.Max(0.1f, v.y)),
                    "相邻两次开工的随机间隔：x = 最短、y = 最长。");
                IntRow("位置抖动（格）", team.positionJitter, v => team.positionJitter = Mathf.Max(0, v),
                    "运行时在槽位坐标 ±N 格内随机偏移（0 = 精确位置）。注意：抖动会让实际落点偏离槽位，" +
                    "怕撞车就把「间隔」调大或把这里设为 0。");

                DrawBuildSlots(config, team, i);
                DrawTroopUnlocks(config, team, i);

                EditorGUILayout.EndVertical();
            }
        }

        private void DrawBuildSlots(StageConfig config, EnemyTeamConfig team, int teamIndex)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("建筑槽位", EditorStyles.boldLabel, GUILayout.Width(80f));
            GUILayout.FlexibleSpace();
            _autoLayoutFold = EditorGUILayout.Foldout(_autoLayoutFold, "自动编排", true, EditorStyles.foldout);
            if (GUILayout.Button("＋ 加槽位", EditorStyles.miniButton, GUILayout.Width(66f)))
            {
                Undo.RecordObject(config, "新增建筑槽位");
                if (team.buildSlots == null)
                    team.buildSlots = new List<EnemyBuildSlot>();

                team.buildSlots.Add(new EnemyBuildSlot
                {
                    buildingType = BuildingType.Barracks,
                    gridPosition = new Vector2Int(Mathf.Max(team.homeGridPosition.x, StageFacts.MidpointX(config)), team.homeGridPosition.y)
                });
                _data.MarkDirty();
            }
            EditorGUILayout.EndHorizontal();

            if (_autoLayoutFold)
                DrawAutoLayout(config, team, teamIndex);

            if (team.buildSlots == null || team.buildSlots.Count == 0)
            {
                EditorGUILayout.LabelField("（没有槽位：AI 只会在锚点附近动态补建）", LevelEditorUI.MiniLabel);
                return;
            }

            int midpoint = StageFacts.MidpointX(config);
            for (int s = 0; s < team.buildSlots.Count; s++)
            {
                EnemyBuildSlot slot = team.buildSlots[s];
                if (slot == null)
                    continue;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("#" + s, LevelEditorUI.MiniLabel, GUILayout.Width(22f));

                var types = new[] { BuildingType.None, BuildingType.Barracks, BuildingType.DarkBarracks, BuildingType.Sentry, BuildingType.MainBase };
                var labels = new[] { "未指定", "兵营", "黑暗兵营", "哨塔", "大本营" };
                int current = Mathf.Max(0, Array.IndexOf(types, slot.buildingType));
                int picked = EditorGUILayout.Popup(current, labels, GUILayout.Width(78f));
                if (picked != current)
                {
                    Undo.RecordObject(config, "修改建筑槽位类型");
                    slot.buildingType = types[picked];
                    _data.MarkDirty();
                }

                int x = EditorGUILayout.IntField(slot.gridPosition.x, GUILayout.Width(42f));
                int y = EditorGUILayout.IntField(slot.gridPosition.y, GUILayout.Width(42f));
                if (x != slot.gridPosition.x || y != slot.gridPosition.y)
                {
                    Undo.RecordObject(config, "修改建筑槽位坐标");
                    slot.gridPosition = new Vector2Int(x, y);
                    _data.MarkDirty();
                }

                if (slot.gridPosition.x < midpoint)
                {
                    GUI.contentColor = LevelEditorUI.Error;
                    EditorGUILayout.LabelField("在玩家半场（会被跳过）", LevelEditorUI.MiniLabel, GUILayout.Width(150f));
                    GUI.contentColor = Color.white;
                }
                else
                {
                    GUILayout.FlexibleSpace();
                }

                if (GUILayout.Button("删", EditorStyles.miniButton, GUILayout.Width(24f)))
                {
                    Undo.RecordObject(config, "删除建筑槽位");
                    team.buildSlots.RemoveAt(s);
                    _data.MarkDirty();
                    // 跳出前先把本帧开着的水平组配平，否则 Unity 报 Mismatched LayoutGroup。
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        /// <summary>
        /// 建筑槽位「自动编排」面板：在大本营周围生成互不重叠的兵营 / 哨塔槽位。
        ///
        /// 存在的理由（现象 → 原因）：运行时给槽位落建筑时会检查"半场 / 与其它建筑至少隔 1 格 /
        /// 占地每一格在地图内且无人占用"（TeamCommander.TryBuildSlot → TryPlaceAt → HasBuildingNeighbor），
        /// 任一项不过就不建造，只每 5 秒重试一次 —— 坐标重叠的槽位因此永远不会开工，
        /// 玩家看到的就是"哨塔不建"。这个面板直接生成一开始就合法的坐标。
        /// 全部逻辑在 StageLayout（LevelEditorAutoLayout.cs）里，游戏代码一行不改。
        /// </summary>
        private void DrawAutoLayout(StageConfig config, EnemyTeamConfig team, int teamIndex)
        {
            // 参数按队伍初始化：换了一支队伍就按那支队的上限重算一次，避免"照抄上一队的数量"。
            if (_autoOptionsTeam != teamIndex)
            {
                _autoOptions = StageLayout.Options.Default(team);
                _autoOptionsTeam = teamIndex;
                _autoReport = string.Empty;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                "在大本营周围自动排序：兵营从贴大本营的第一列朝外铺，哨塔只排在所有兵营的更前方" +
                "（因此哨塔不会出现在大本营后面/侧面，也不会混在兵营之间）。" +
                "普通兵营与黑暗兵营**分开计数**、共用同一片区域（前沿的列先给普通兵营，黑暗兵营补后面的列）；" +
                "每个位置都保证在地图内、在半场分界右侧、且与其它建筑至少空「间隔」格 —— " +
                "运行时那四条放置规则一开始就全部满足，不会再出现「重叠所以不建造」。",
                LevelEditorUI.MiniLabel);

            // 参数分两行：数量一行、几何一行（六个输入框挤一行会在窄窗口里被压扁）。
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("普通兵营", GUILayout.Width(58f));
            _autoOptions.barracks = Mathf.Clamp(EditorGUILayout.IntField(_autoOptions.barracks, GUILayout.Width(36f)), 0, 24);
            EditorGUILayout.LabelField("黑暗兵营", GUILayout.Width(58f));
            _autoOptions.darkBarracks = Mathf.Clamp(EditorGUILayout.IntField(_autoOptions.darkBarracks, GUILayout.Width(36f)), 0, 24);
            EditorGUILayout.LabelField("哨塔", GUILayout.Width(34f));
            _autoOptions.sentries = Mathf.Clamp(EditorGUILayout.IntField(_autoOptions.sentries, GUILayout.Width(36f)), 0, 24);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("间隔", GUILayout.Width(58f));
            _autoOptions.gap = Mathf.Clamp(EditorGUILayout.IntField(_autoOptions.gap, GUILayout.Width(36f)), 1, 4);
            EditorGUILayout.LabelField("上/下行", GUILayout.Width(58f));
            _autoOptions.rowsAbove = Mathf.Clamp(EditorGUILayout.IntField(_autoOptions.rowsAbove, GUILayout.Width(36f)), 0, 8);
            _autoOptions.rowsBelow = Mathf.Clamp(EditorGUILayout.IntField(_autoOptions.rowsBelow, GUILayout.Width(36f)), 0, 8);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("按上面参数编排槽位（覆盖本队现有槽位）", GUILayout.Height(22f)))
            {
                Undo.RecordObject(config, "自动编排建筑槽位");
                string report;
                if (StageLayout.TryArrange(config, team, _autoOptions, out report))
                {
                    _autoReport = "✔ " + report;
                    _data.MarkDirty();
                    EditorUtility.SetDirty(config);
                    Debug.Log("[关卡编辑器] 自动编排 第 " + (teamIndex + 1) + " 支队伍：" + report);
                }
                else
                {
                    _autoReport = "✘ " + report;
                }
            }

            if (GUILayout.Button("恢复为上限数", EditorStyles.miniButton, GUILayout.Width(96f)))
            {
                _autoOptions = StageLayout.Options.Default(team);
                _autoReport = string.Empty;
            }

            if (GUILayout.Button("只修冲突槽位", EditorStyles.miniButton, GUILayout.Width(96f)))
            {
                Undo.RecordObject(config, "挪开冲突的建筑槽位");
                int moved = StageLayout.FixOverlaps(config, team, 6);
                if (moved > 0)
                {
                    _autoReport = "✔ 挪开了 " + moved + " 个冲突槽位（其余位置保持不变）。";
                    _data.MarkDirty();
                    EditorUtility.SetDirty(config);
                }
                else
                {
                    _autoReport = "现有槽位没有发现「重叠/间距不足」，没有改动。";
                }
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_autoReport))
                EditorGUILayout.LabelField(_autoReport, LevelEditorUI.MiniLabel);

            EditorGUILayout.LabelField(
                "占地大小按种族建筑预制体上的 occupySpace 实算（兵营 2×2、Mouse 哨塔 2×1、大本营 4×2）；" +
                "槽位坐标是建筑左下角，与 StageConfig.EnemyBuildSlot 的运行时语义一致。",
                LevelEditorUI.MiniLabel);

            EditorGUILayout.EndVertical();
        }

        private void DrawTroopUnlocks(StageConfig config, EnemyTeamConfig team, int teamIndex)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("兵种白名单", EditorStyles.boldLabel, GUILayout.Width(80f));
            GUILayout.FlexibleSpace();

            int unlockCount = team.troopUnlocks != null ? team.troopUnlocks.Count : 0;
            EditorGUILayout.LabelField(unlockCount == 0 ? "空 = 全部兵种可用" : unlockCount + " 条", LevelEditorUI.MiniLabel, GUILayout.Width(110f));

            if (GUILayout.Button("＋ 加兵种", EditorStyles.miniButton, GUILayout.Width(66f)))
            {
                Undo.RecordObject(config, "新增兵种白名单条目");
                if (team.troopUnlocks == null)
                    team.troopUnlocks = new List<EnemyTroopUnlock>();

                // 先放一个空条目，紧接着让用户从选键窗口挑——空条目在体检里会被标出来。
                team.troopUnlocks.Add(new EnemyTroopUnlock());
                _data.MarkDirty();

                int addedIndex = team.troopUnlocks.Count - 1;
                LevelEditorPickerWindow.OpenTroop("兵种白名单", _data.Current.Label + " 第" + (teamIndex + 1) + "队",
                    troop =>
                    {
                        if (addedIndex < team.troopUnlocks.Count)
                            _data.SetTroopUnlock(team.troopUnlocks[addedIndex], troop, 0f);
                        Repaint();
                    },
                    null, null);
            }
            EditorGUILayout.EndHorizontal();

            if (team.troopUnlocks == null || team.troopUnlocks.Count == 0)
            {
                EditorGUILayout.LabelField("留空表示本队本局可以训练兵营里的全部兵种。", LevelEditorUI.MiniLabel);
                return;
            }

            for (int u = 0; u < team.troopUnlocks.Count; u++)
            {
                EnemyTroopUnlock unlock = team.troopUnlocks[u];
                if (unlock == null)
                    continue;

                TroopDefinition troop = unlock.Troop;

                EditorGUILayout.BeginHorizontal();

                if (troop == null)
                {
                    GUI.contentColor = LevelEditorUI.Error;
                    EditorGUILayout.LabelField("（空条目 —— 运行时会让整队出兵判断抛异常）", LevelEditorUI.MiniLabel, GUILayout.Width(220f));
                    GUI.contentColor = Color.white;
                }
                else
                {
                    Sprite icon = StageResourceIndex.Instance.GetSprite(troop.IconKey);
                    var thumb = GUILayoutUtility.GetRect(20f, 20f, GUILayout.Width(20f), GUILayout.Height(20f));
                    LevelEditorUI.FillRect(thumb, new Color(0f, 0f, 0f, 0.18f));
                    LevelEditorUI.DrawSpriteFit(thumb, icon, Color.white, 1f);

                    GUI.contentColor = icon != null ? Color.white : LevelEditorUI.Error;
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(troop.IconKey) ? "（无图标键）" : troop.IconKey,
                        LevelEditorUI.MiniLabel, GUILayout.Width(130f));
                    GUI.contentColor = Color.white;
                }

                if (GUILayout.Button("换", EditorStyles.miniButton, GUILayout.Width(24f)))
                {
                    int index = u;
                    EnemyTroopUnlock target = unlock;
                    LevelEditorPickerWindow.OpenTroop("兵种白名单", _data.Current.Label, picked =>
                    {
                        _data.SetTroopUnlock(target, picked, StageEditorData.GetUnlockTimeOf(target));
                        Repaint();
                    }, null, null);
                }

                float unlockTime = unlock.UnlockTime;
                float newTime = EditorGUILayout.FloatField(unlockTime, GUILayout.Width(54f));
                if (!Mathf.Approximately(newTime, unlockTime))
                    _data.SetTroopUnlock(unlock, unlock.Troop, newTime);

                EditorGUILayout.LabelField("秒解锁", LevelEditorUI.MiniLabel, GUILayout.Width(50f));
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("删", EditorStyles.miniButton, GUILayout.Width(24f)))
                {
                    Undo.RecordObject(config, "删除兵种白名单条目");
                    team.troopUnlocks.RemoveAt(u);
                    _data.MarkDirty();
                    // 跳出前先把本帧开着的水平组配平，否则 Unity 报 Mismatched LayoutGroup。
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawRewardSection(StageConfig config)
        {
            if (FormSection("通关奖励", "通关奖励", "普通/大师各自可填，大师按差额补发"))
                return;

            int goldBars = _data.VictoryGoldBars;
            int diamonds = _data.VictoryDiamonds;
            bool customNormal = _data.CustomNormalReward;
            int normalGold = _data.CustomNormalGoldBarsRaw;
            int normalDiamonds = _data.CustomNormalDiamondsRaw;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("大师模式", EditorStyles.boldLabel, GUILayout.Width(64f));
            EditorGUILayout.LabelField("通关发全量", LevelEditorUI.MiniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginChangeCheck();
            goldBars = EditorGUILayout.IntField("金条（大师）", goldBars);
            diamonds = EditorGUILayout.IntField("钻石（大师）", diamonds);
            if (EditorGUI.EndChangeCheck())
                _data.SetVictoryRewards(goldBars, diamonds, customNormal, normalGold, normalDiamonds);

            LevelEditorUI.Space(4f);

            EditorGUI.BeginChangeCheck();
            customNormal = EditorGUILayout.ToggleLeft("普通模式单独填（不勾 = 自动取大师的一半）", customNormal);
            if (EditorGUI.EndChangeCheck())
                _data.SetVictoryRewards(goldBars, diamonds, customNormal, normalGold, normalDiamonds);

            using (new EditorGUI.DisabledScope(!customNormal))
            {
                EditorGUI.BeginChangeCheck();
                normalGold = EditorGUILayout.IntField("金条（普通）", normalGold);
                normalDiamonds = EditorGUILayout.IntField("钻石（普通）", normalDiamonds);
                if (EditorGUI.EndChangeCheck())
                    _data.SetVictoryRewards(goldBars, diamonds, customNormal, normalGold, normalDiamonds);
            }

            int effectiveNormalGold = customNormal ? Mathf.Max(0, normalGold) : Mathf.Max(0, goldBars) / 2;
            int effectiveNormalDiamonds = customNormal ? Mathf.Max(0, normalDiamonds) : Mathf.Max(0, diamonds) / 2;

            LevelEditorUI.Hint(
                "普通模式实发：" + effectiveNormalGold + " 金条 / " + effectiveNormalDiamonds + " 钻石" +
                (customNormal ? "（自定义）" : "（= 大师的一半）") +
                "；大师模式补发差额：" + Mathf.Max(0, goldBars - effectiveNormalGold) + " 金条 / " +
                Mathf.Max(0, diamonds - effectiveNormalDiamonds) + " 钻石" +
                "（普通没领过时发全量）。X3 激励视频再补 2 倍。");

            LevelEditorUI.Space(6f);
            DrawRewardTemplateBlock(config);
        }

        /// <summary>
        /// 通关奖励模板（R1.29）：按**关卡类型**（防御 / 进攻 / Boss）各存一套默认奖励，
        /// 可一键套用到该类型的所有关卡；「新建关卡」也会拿它当默认值。
        /// 模板存在 EditorPrefs 里（本机、不进游戏包、不动任何游戏资产）。
        /// </summary>
        private void DrawRewardTemplateBlock(StageConfig config)
        {
            UserGlobalInfo.StageType stageType = config.stageType;
            string typeName = StageRewardTemplate.TypeName(stageType);
            StageRewardTemplate template = LevelEditorPrefs.GetRewardTemplate(stageType);

            bool clearClicked = false;
            bool saveClicked = false;
            bool applyHereClicked = false;
            bool applyAllClicked = false;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("奖励模板 · " + typeName, EditorStyles.boldLabel, GUILayout.Width(132f));
            LevelEditorUI.Tag(template.Valid ? "已设置" : "未设置",
                template.Valid ? LevelEditorUI.Ok : LevelEditorUI.Muted);
            GUILayout.FlexibleSpace();
            if (template.Valid &&
                GUILayout.Button(new GUIContent("清掉模板", "删掉这一类型的模板；已经套用到关卡上的数值不受影响"),
                    EditorStyles.miniButton, GUILayout.Width(64f)))
                clearClicked = true;
            EditorGUILayout.EndHorizontal();

            LevelEditorUI.Hint("当前模板：" + template.Describe());

            int sameTypeCount = CountStagesOfType(stageType);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("用本关的值存为模板",
                    "把本关这 5 个奖励值记成「" + typeName + "」的默认模板")))
                saveClicked = true;

            using (new EditorGUI.DisabledScope(!template.Valid))
            {
                if (GUILayout.Button(new GUIContent("套用到本关", "用模板覆盖本关的奖励值（可 Ctrl+Z 撤销）")))
                    applyHereClicked = true;

                if (GUILayout.Button(new GUIContent("套用到所有" + typeName + "（" + sameTypeCount + " 关）",
                        "把模板写进工程里所有" + typeName + "（本地测试关不动）；逐关文件级备份 + 可 Ctrl+Z")))
                    applyAllClicked = true;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            LevelEditorUI.Hint("新建" + typeName + "时会自动套用这套模板（还没设置过就不覆盖默认奖励）。");

            if (clearClicked)
            {
                LevelEditorPrefs.ClearRewardTemplate(stageType);
                SetStatus("已清掉「" + typeName + "」的奖励模板（已套用到关卡的数值不变）。", MessageType.Info);
                Repaint();
                return;
            }

            if (saveClicked)
            {
                LevelEditorPrefs.SetRewardTemplate(stageType,
                    _data.ReadRewardTemplate());
                SetStatus("已把本关的奖励存为「" + typeName + "」模板：" +
                    LevelEditorPrefs.GetRewardTemplate(stageType).Describe(), MessageType.Info);
                Repaint();
            }

            if (applyHereClicked)
            {
                Undo.RecordObject(config, "套用奖励模板");
                StageEditorData.ApplyRewardTemplate(config, template);
                _data.MarkDirty();
                SetStatus("已把「" + typeName + "」模板套用到本关。", MessageType.Info);
            }

            if (applyAllClicked)
            {
                StageRewardTemplate captured = template;
                ApplyBatchAll("套用" + typeName + "奖励模板：" + captured.Describe(),
                    (target, info) => StageEditorData.ApplyRewardTemplate(target, captured),
                    info => info.Config != null && info.Config.stageType == stageType,
                    typeName);
            }
        }

        /// <summary>工程里有多少个该类型的正式关（本地测试关不计）。</summary>
        private int CountStagesOfType(UserGlobalInfo.StageType stageType)
        {
            int count = 0;
            for (int i = 0; i < _stages.Count; i++)
            {
                StageInfo info = _stages[i];
                if (info != null && !info.IsLocalTest && info.Config != null && info.Config.stageType == stageType)
                    count++;
            }
            return count;
        }

        private void DrawPresentationSection(StageConfig config)
        {
            if (FormSection("展示", "展示", "选关界面上玩家看到的东西"))
                return;

            EditorGUI.BeginChangeCheck();
            Sprite icon = (Sprite)EditorGUILayout.ObjectField("关卡图标", config.icon, typeof(Sprite), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(config, "修改关卡图标");
                config.icon = icon;
                _data.MarkDirty();
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("选关展示图", GUILayout.Width(120f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    new GUIContent("▼ 按白名单自动填", "按本关敌方队伍会出的兵种自动填展示图：\n" +
                        "· 填了兵种白名单 → 用白名单里兵种的图标（IconKey → SpriteRegistry），按配置顺序去重；\n" +
                        "· 白名单为空（= 全部兵种可用）→ 与运行时一样按「整族预载」，用该种族普通兵营 + 黑暗兵营的兵种表；\n" +
                        "· 最多 4 张（运行时按从左到右居中横排）；图标键解析不到时回落到兵种的 EditorIcon。\n" +
                        "只覆盖「选关展示图」，不动白名单本身。可 Ctrl+Z 撤销。"),
                    EditorStyles.miniButton, GUILayout.Width(112f)))
            {
                AutoFillDisplayIcons(config);
            }
            if (GUILayout.Button("＋ 加一张", EditorStyles.miniButton, GUILayout.Width(66f)))
            {
                Undo.RecordObject(config, "新增选关展示图");
                if (config.displayIcons == null)
                    config.displayIcons = new List<Sprite>();
                config.displayIcons.Add(null);
                _data.MarkDirty();
            }
            EditorGUILayout.EndHorizontal();

            if (config.displayIcons != null)
            {
                for (int i = 0; i < config.displayIcons.Count; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUI.BeginChangeCheck();
                    Sprite sprite = (Sprite)EditorGUILayout.ObjectField("#" + (i + 1), config.displayIcons[i], typeof(Sprite), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(config, "修改选关展示图");
                        config.displayIcons[i] = sprite;
                        _data.MarkDirty();
                    }
                    if (GUILayout.Button("删", EditorStyles.miniButton, GUILayout.Width(24f)))
                    {
                        Undo.RecordObject(config, "删除选关展示图");
                        config.displayIcons.RemoveAt(i);
                        _data.MarkDirty();
                        // 跳出前先把本帧开着的水平组配平，否则 Unity 报 Mismatched LayoutGroup。
                        EditorGUILayout.EndHorizontal();
                        break;
                    }
                    EditorGUILayout.EndHorizontal();
                }

                if (config.displayIcons.Count > StageValidator.MaxDisplayIcons)
                    EditorGUILayout.HelpBox("超过 4 张会挤出去（弹窗按从左到右居中横排）。", MessageType.Warning);
            }
        }

        /// <summary>
        /// 「⇩ 按白名单自动填」：把本关敌方队伍**会出的兵种**图标填进选关展示图。
        ///
        /// 数据链路（只写 config.displayIcons，白名单本身一个字段都不动）：
        ///   EnemyTeamConfig.troopUnlocks[].Troop → TroopDefinition.IconKey（[ResourceKey(typeof(Sprite))]）
        ///   → StageResourceIndex.GetSprite(key) → config.displayIcons[i]
        /// 兵种清单走 StageResourceIndex.GetStageTroops（白名单为空时按运行时语义回落"整族预载"）。
        /// 数量上限 StageValidator.MaxDisplayIcons：StageConfig.displayIcons 的注释写明
        /// 运行时按从左到右居中横排，超过 4 张会挤出去。
        /// 图标键解析不到时回落到 TroopDefinition.EditorIcon（编辑器里直接拖的那个 Sprite）。
        /// </summary>
        private void AutoFillDisplayIcons(StageConfig config)
        {
            StageResourceIndex index = StageResourceIndex.Instance;
            List<TroopDefinition> troops = index.GetStageTroops(config);

            if (troops.Count == 0)
            {
                _status = "自动填展示图失败：本关没有任何敌方队伍 / 兵种可用（先给队伍配 raceId 或兵种白名单）。";
                return;
            }

            var icons = new List<Sprite>();
            var missing = new List<string>();
            for (int i = 0; i < troops.Count && icons.Count < StageValidator.MaxDisplayIcons; i++)
            {
                TroopDefinition troop = troops[i];
                Sprite sprite = string.IsNullOrEmpty(troop.IconKey) ? null : index.GetSprite(troop.IconKey);
                if (sprite == null)
                    sprite = troop.EditorIcon;   // 兜底：兵种资产上直接拖的那个 Sprite

                if (sprite == null)
                {
                    missing.Add(string.IsNullOrEmpty(troop.DisplayName) ? troop.Id : troop.DisplayName);
                    continue;
                }

                icons.Add(sprite);
            }

            if (icons.Count == 0)
            {
                _status = "自动填展示图失败：" + troops.Count + " 个兵种都没有可用图标" +
                          "（IconKey 不在 SpriteRegistry，且没配 EditorIcon）。";
                return;
            }

            Undo.RecordObject(config, "按白名单自动填选关展示图");
            config.displayIcons = icons;
            EditorUtility.SetDirty(config);
            _data.MarkDirty();

            string msg = "已按白名单自动填 " + icons.Count + " 张选关展示图（本关兵种 " + troops.Count +
                         " 个，最多取 " + StageValidator.MaxDisplayIcons + " 张）";
            if (missing.Count > 0)
                msg += "；跳过没图标的 " + missing.Count + " 个：" + string.Join("、", missing.ToArray());
            _status = msg;
            Debug.Log("[关卡编辑器] " + msg);
        }

        private void DrawGlobalRuleSection(StageConfig config)
        {
            List<GlobalRuleDefinition> rules = _data.GetGlobalRules();

            if (FormSection("全局规则", "全局规则", rules.Count + " 条（开局即生效）"))
                return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("＋ 加一条规则", EditorStyles.miniButton, GUILayout.Width(96f)))
            {
                var menu = new GenericMenu();
                if (StageResourceIndex.Instance.GlobalRules.Count == 0)
                    menu.AddDisabledItem(new GUIContent("（工程里没有 GlobalRuleDefinition 资产）"));

                for (int i = 0; i < StageResourceIndex.Instance.GlobalRules.Count; i++)
                {
                    GlobalRuleDefinition rule = StageResourceIndex.Instance.GlobalRules[i];
                    if (rule == null)
                        continue;

                    menu.AddItem(new GUIContent(rule.DisplayName + "（" + rule.Category + "）"), false, () =>
                    {
                        List<GlobalRuleDefinition> list = _data.GetGlobalRules();
                        list.Add(rule);
                        _data.SetGlobalRules(list);
                        Repaint();
                    });
                }
                menu.ShowAsContext();
            }
            EditorGUILayout.EndHorizontal();

            if (rules.Count == 0)
            {
                EditorGUILayout.LabelField("（没有全局规则：本局不会有额外规则字幕）", LevelEditorUI.MiniLabel);
                return;
            }

            for (int i = 0; i < rules.Count; i++)
            {
                GlobalRuleDefinition rule = rules[i];
                EditorGUILayout.BeginHorizontal();

                if (rule == null)
                {
                    GUI.contentColor = LevelEditorUI.Error;
                    EditorGUILayout.LabelField("（空位）", LevelEditorUI.MiniLabel, GUILayout.Width(180f));
                    GUI.contentColor = Color.white;
                }
                else
                {
                    var thumb = new Rect(GUILayoutUtility.GetRect(22f, 22f, GUILayout.Width(22f), GUILayout.Height(22f)));
                    LevelEditorUI.FillRect(thumb, new Color(0f, 0f, 0f, 0.2f));
                    LevelEditorUI.DrawSpriteFit(thumb, rule.Icon, Color.white, 1f);

                    EditorGUILayout.LabelField(rule.DisplayName, LevelEditorUI.MiniLabel, GUILayout.Width(150f));
                    EditorGUILayout.LabelField(rule.Category.ToString(), LevelEditorUI.MiniLabel, GUILayout.Width(60f));
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(rule.RuleId) ? "（没有 ruleId，开发未实现）" : rule.RuleId,
                        LevelEditorUI.MiniLabel);
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("删", EditorStyles.miniButton, GUILayout.Width(24f)))
                {
                    List<GlobalRuleDefinition> list = _data.GetGlobalRules();
                    list.RemoveAt(i);
                    _data.SetGlobalRules(list);
                    Repaint();
                    // 跳出前先把本帧开着的水平组配平，否则 Unity 报 Mismatched LayoutGroup。
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        #endregion


        #region 体检面板
        private void DrawIssuesTab()
        {
            int errors = ErrorCount;
            int warnings = WarningCount;
            int infos = InfoCount;

            EditorGUILayout.BeginHorizontal();
            GUI.contentColor = errors > 0 ? LevelEditorUI.Error : LevelEditorUI.Ok;
            EditorGUILayout.LabelField("错误 " + errors + "　警告 " + warnings + "　提示 " + infos, EditorStyles.boldLabel);
            GUI.contentColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("只看本关", GUILayout.Height(22f)))
                SetStatus("已按当前关卡过滤（再点一次「全部关卡」取消）。", MessageType.Info);
            if (GUILayout.Button("全部关卡", GUILayout.Height(22f)))
                SetStatus("显示全部关卡的问题。", MessageType.Info);
            if (GUILayout.Button("重新体检", GUILayout.Height(22f)))
            {
                StageResourceIndex.Invalidate();
                StageResourceIndex.InvalidateAddressables();
                LevelEditorUI.ClearThumbnailCache();
                _issuesDirty = true;
                EnsureIssues();
            }
            if (GUILayout.Button("一键修复本关", GUILayout.Height(22f)))
                RunAutoFix();
            EditorGUILayout.EndHorizontal();

            _issueScroll = EditorGUILayout.BeginScrollView(_issueScroll, GUILayout.ExpandHeight(true));

            if (_issues.Count == 0)
                EditorGUILayout.HelpBox("所有关卡都没有发现问题。", MessageType.Info);

            string currentPath = _data.AssetPath;

            for (int i = 0; i < _issues.Count; i++)
            {
                StageIssue issue = _issues[i];
                if (issue == null)
                    continue;

                bool isCurrent = string.IsNullOrEmpty(issue.StagePath) || issue.StagePath == currentPath;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                GUI.contentColor = issue.LevelColor;
                EditorGUILayout.LabelField(issue.LevelLabel + " " + issue.Title, EditorStyles.boldLabel);
                GUI.contentColor = Color.white;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(issue.Detail, LevelEditorUI.RichLabel);

                EditorGUILayout.BeginHorizontal();
                if (string.IsNullOrEmpty(issue.StagePath))
                {
                    EditorGUILayout.LabelField("全局", LevelEditorUI.MiniLabel, GUILayout.Width(200f));
                }
                else
                {
                    EditorGUILayout.LabelField("Stage" + issue.StageId + (isCurrent ? "（当前）" : string.Empty),
                        LevelEditorUI.MiniLabel, GUILayout.Width(120f));

                    if (!isCurrent && GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(44f)))
                    {
                        LoadStage(issue.StagePath);
                        Repaint();
                    }
                }

                if (issue.Fixable)
                {
                    GUI.contentColor = LevelEditorUI.Ok;
                    EditorGUILayout.LabelField("→ " + issue.SuggestedValue, LevelEditorUI.MiniLabel);
                    GUI.contentColor = Color.white;
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }
        #endregion

        #region 设置
        private void DrawSettingsTab()
        {
            _formScroll = EditorGUILayout.BeginScrollView(_formScroll);

            LevelEditorUI.Section("资源注册表");
            StageResourceIndex index = StageResourceIndex.Instance;
            EditorGUILayout.LabelField("SpriteRegistry：" + (index.SpriteRegistryFound ? index.SpriteKeys.Count + " 个键" : "未找到"), LevelEditorUI.MiniLabel);
            EditorGUILayout.LabelField("PrefabRegistry：" + (index.PrefabRegistryFound ? index.PrefabKeys.Count + " 个键" : "未找到"), LevelEditorUI.MiniLabel);
            EditorGUILayout.LabelField("AudioRegistry：" + (index.HasRegistry(StageResourceIndex.StageKeyKind.Audio) ? index.KeysOf(StageResourceIndex.StageKeyKind.Audio).Count + " 个键" : "未找到"), LevelEditorUI.MiniLabel);
            EditorGUILayout.LabelField("TextureRegistry：" + (index.HasRegistry(StageResourceIndex.StageKeyKind.Texture) ? index.KeysOf(StageResourceIndex.StageKeyKind.Texture).Count + " 个键" : "未找到"), LevelEditorUI.MiniLabel);
            EditorGUILayout.LabelField("动画片段 / 控制器：" +
                index.KeysOf(StageResourceIndex.StageKeyKind.AnimationClip).Count + " / " +
                index.KeysOf(StageResourceIndex.StageKeyKind.AnimatorController).Count + " 个键", LevelEditorUI.MiniLabel);
            EditorGUILayout.LabelField("兵种定义：" + index.Troops.Count + " 个　种族：" + index.Races.Count + " 个　全局规则：" + index.GlobalRules.Count + " 个", LevelEditorUI.MiniLabel);

            if (GUILayout.Button("重新读取全部注册表"))
            {
                StageResourceIndex.Invalidate();
                StageResourceIndex.InvalidateAddressables();
                LevelEditorUI.ClearThumbnailCache();
                _issuesDirty = true;
                SetStatus("已重新读取注册表。", MessageType.Info);
                Repaint();
            }

            LevelEditorUI.Section("Addressables");
            EditorGUILayout.LabelField("组：" + StageResourceIndex.StageGroupAssetPath, LevelEditorUI.MiniLabel);
            EditorGUILayout.LabelField(index.AddressableGroupFound
                ? "已登记的关卡地址：" + string.Join("、", new List<string>(index.RegisteredStageAddresses).ToArray())
                : "没读到分组资产（Addressables 可能还没初始化）", LevelEditorUI.RichLabel);
            EditorGUILayout.LabelField("上架（写分组资产）在工具栏「上架」或「一键修复」里；删除关卡时不会自动移除条目。",
                LevelEditorUI.MiniLabel);

            LevelEditorUI.Section("地图尺寸");
            EditorGUILayout.LabelField(StageMap.SourceLabel, LevelEditorUI.RichLabel);

            // 「锁定尺寸」：默认关（沿用当前场景的 MapCells）。锁上之后不再看场景，
            // 解决"同一份关卡数据在不同场景下体检结论不同"（坐标越界/半场分界都跟着地图尺寸走）。
            bool locked = EditorGUILayout.ToggleLeft("锁定地图尺寸（不随当前打开的场景变化）", LevelEditorPrefs.LockMapSize);
            if (locked != LevelEditorPrefs.LockMapSize)
            {
                LevelEditorPrefs.LockMapSize = locked;
                StageMap.Invalidate();
                _issuesDirty = true;
            }

            int w = EditorGUILayout.IntField("宽度", LevelEditorPrefs.MapWidth);
            if (w != LevelEditorPrefs.MapWidth)
            {
                LevelEditorPrefs.MapWidth = w;
                StageMap.Invalidate();
                _issuesDirty = true;
            }
            int h = EditorGUILayout.IntField("高度", LevelEditorPrefs.MapHeight);
            if (h != LevelEditorPrefs.MapHeight)
            {
                LevelEditorPrefs.MapHeight = h;
                StageMap.Invalidate();
                _issuesDirty = true;
            }

            EditorGUILayout.LabelField(LevelEditorPrefs.LockMapSize
                ? "已锁定：体检与坐标换算都用上面这两个值，与当前打开的场景无关。"
                : "未锁定：优先用当前打开场景里 MapCells 的尺寸；这两个值只在找不到 MapCells 时兜底。",
                LevelEditorUI.MiniLabel);

            LevelEditorUI.Section("备份");
            EditorGUILayout.LabelField("目录：" + LevelEditorPrefs.BackupFolder, LevelEditorUI.MiniLabel);
            EditorGUILayout.LabelField("（默认跟随工具目录，改过文件夹名也会自动找到；在工程之外，不会进包、不会进版本库）", LevelEditorUI.MiniLabel);

            LevelEditorPrefs.AutoBackupOnSave = EditorGUILayout.ToggleLeft("保存前自动备份", LevelEditorPrefs.AutoBackupOnSave);
            LevelEditorPrefs.KeepBackupCount = EditorGUILayout.IntSlider("每个关卡保留份数", LevelEditorPrefs.KeepBackupCount, 5, 200);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("立即备份"))
            {
                string file = _data.BackupNow("手动");
                SetStatus(string.IsNullOrEmpty(file) ? "备份失败。" : "已备份：" + Path.GetFileName(file),
                    string.IsNullOrEmpty(file) ? MessageType.Error : MessageType.Info);
            }
            if (GUILayout.Button("打开备份目录"))
            {
                Directory.CreateDirectory(LevelEditorPrefs.BackupFolder);
                EditorUtility.RevealInFinder(LevelEditorPrefs.BackupFolder);
            }
            EditorGUILayout.EndHorizontal();


            LevelEditorUI.Section("当前关卡");
            EditorGUILayout.LabelField(string.IsNullOrEmpty(_data.AssetPath) ? "（未加载）" : _data.AssetPath, LevelEditorUI.MiniLabel);

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
                EditorGUIUtility.PingObject(_data.Stage);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("（本工具源码位于 " + StageEditorData.ToolRoot +
                "\\LevelVisualEditor，通过目录联接接入工程；只写当前这一份 StageN.asset，不改工程里其它任何文件。）",
                LevelEditorUI.RichLabel);

            EditorGUILayout.EndScrollView();
        }
        #endregion
        #endregion

        #region 批量改 / 备份菜单
        private void ShowBatchMenu()
        {
            var menu = new GenericMenu();
            menu.AddDisabledItem(new GUIContent("作用于编号 1~999 的正式关（本地测试关不动）"));

            menu.AddItem(new GUIContent("全体时限 ×1.1"), false, () => ApplyBatchAll("时限 ×1.1", (config, info) =>
            {
                config.DefenseTime *= 1.1f;
                config.attackTimeLimit *= 1.1f;
                config.bossTimeLimit *= 1.1f;
            }));

            menu.AddItem(new GUIContent("全体时限 ×0.9"), false, () => ApplyBatchAll("时限 ×0.9", (config, info) =>
            {
                config.DefenseTime *= 0.9f;
                config.attackTimeLimit *= 0.9f;
                config.bossTimeLimit *= 0.9f;
            }));

            menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent("敌方兵营等级 +1"), false, () => ApplyBatchAll("敌方兵营等级 +1", (config, info) => config.enemyBarracksLevel = Mathf.Max(1, config.enemyBarracksLevel + 1)));
            menu.AddItem(new GUIContent("敌方兵营等级 -1"), false, () => ApplyBatchAll("敌方兵营等级 -1", (config, info) => config.enemyBarracksLevel = Mathf.Max(1, config.enemyBarracksLevel - 1)));
            menu.AddItem(new GUIContent("敌方哨塔等级 +1"), false, () => ApplyBatchAll("敌方哨塔等级 +1", (config, info) => config.enemySentryTowerLevel = Mathf.Max(1, config.enemySentryTowerLevel + 1)));
            menu.AddItem(new GUIContent("敌方哨塔等级 -1"), false, () => ApplyBatchAll("敌方哨塔等级 -1", (config, info) => config.enemySentryTowerLevel = Mathf.Max(1, config.enemySentryTowerLevel - 1)));

            menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent("基地距离统一 60 格"), false, () => ApplyBatchAll("基地距离 60", (config, info) => config.enemyBaseDistance = 60));
            menu.AddItem(new GUIContent("基地距离统一 70 格"), false, () => ApplyBatchAll("基地距离 70", (config, info) => config.enemyBaseDistance = 70));

            menu.AddSeparator(string.Empty);

            menu.AddItem(new GUIContent("通关奖励 ×1.2"), false, () => ApplyBatchAll("通关奖励 ×1.2", (config, info) =>
            {
                // 奖励字段是 private [SerializeField]，只能反射写；普通模式自定义值一起放大，保持比例
                StageEditorData.SetPrivateField(config, "victoryGoldBars", Mathf.RoundToInt(config.VictoryGoldBars * 1.2f));
                StageEditorData.SetPrivateField(config, "victoryDiamonds", Mathf.RoundToInt(config.VictoryDiamonds * 1.2f));
                if (config.CustomNormalReward)
                {
                    StageEditorData.SetPrivateField(config, "normalVictoryGoldBars", Mathf.RoundToInt(config.NormalVictoryGoldBars * 1.2f));
                    StageEditorData.SetPrivateField(config, "normalVictoryDiamonds", Mathf.RoundToInt(config.NormalVictoryDiamonds * 1.2f));
                }
            }));

            menu.AddItem(new GUIContent("各队每秒金币 ×1.2"), false, () => ApplyBatchAll("敌队经济 ×1.2", (config, info) =>
            {
                if (config.enemyTeams == null)
                    return;
                for (int i = 0; i < config.enemyTeams.Count; i++)
                {
                    if (config.enemyTeams[i] != null)
                        config.enemyTeams[i].baseGoldPerSecond *= 1.2f;
                }
            }));

            menu.AddItem(new GUIContent("各队每秒金币 ×0.8"), false, () => ApplyBatchAll("敌队经济 ×0.8", (config, info) =>
            {
                if (config.enemyTeams == null)
                    return;
                for (int i = 0; i < config.enemyTeams.Count; i++)
                {
                    if (config.enemyTeams[i] != null)
                        config.enemyTeams[i].baseGoldPerSecond *= 0.8f;
                }
            }));

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("上架所有未上架的正式关"), false, RegisterAllMissingStages);

            menu.ShowAsContext();
        }

        /// <summary>
        /// 批量上架：把所有"正式关但没登记进 Addressables 组 StageConfig"的关卡一次登记完。
        /// 地址 Stage{编号} + 标签 StartupPreload（依据见 StageResourceIndex.RegisterStage 的注释）；
        /// 每插一条前都会自动备份分组资产（只在第一次插入时备份，避免刷一屏备份）。
        /// </summary>
        private void RegisterAllMissingStages()
        {
            var targets = new List<StageInfo>();
            for (int i = 0; i < _stages.Count; i++)
            {
                StageInfo info = _stages[i];
                if (info == null || info.IsLocalTest)
                    continue;
                if (!StageResourceIndex.Instance.IsStageRegistered(info.StageId))
                    targets.Add(info);
            }

            if (targets.Count == 0)
            {
                SetStatus("所有正式关都已经上架了。", MessageType.Info);
                return;
            }

            var names = new List<string>();
            for (int i = 0; i < targets.Count && i < 12; i++)
                names.Add(targets[i].Name);

            if (!EditorUtility.DisplayDialog("批量上架",
                    "将把 " + targets.Count + " 个关卡登记进 Addressables 组 StageConfig：\n\n" +
                    string.Join("、", names.ToArray()) + (targets.Count > names.Count ? " …" : string.Empty) + "\n\n" +
                    "地址 = Stage{编号}、标签 = StartupPreload（与现有条目完全同构）。\n" +
                    "写的是分组资产，改前自动备份；不碰任何游戏代码。",
                    "上架", "取消"))
                return;

            int done = 0;
            int failed = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                string message;
                if (StageResourceIndex.RegisterStage(targets[i].Path, targets[i].StageId, out message))
                {
                    done++;
                    Debug.Log("[关卡编辑器] 上架 " + targets[i].Name + "：" + message);
                }
                else
                {
                    failed++;
                    Debug.LogWarning("[关卡编辑器] 上架 " + targets[i].Name + " 未完成：" + message);
                }
            }

            RefreshStageList();
            _issuesDirty = true;
            SetStatus("批量上架完成：成功 " + done + " 个" + (failed > 0 ? "、未完成 " + failed + " 个（见 Console）" : string.Empty) + "。",
                failed > 0 ? MessageType.Warning : MessageType.Info);
            Repaint();
        }

        private void ApplyBatchAll(string opName, Action<StageConfig, StageInfo> mutator,
            Func<StageInfo, bool> filter = null, string scopeText = null)
        {
            var targets = new List<StageInfo>();
            for (int i = 0; i < _stages.Count; i++)
            {
                StageInfo info = _stages[i];
                if (info == null || info.IsLocalTest)
                    continue;
                if (filter != null && !filter(info))
                    continue;

                targets.Add(info);
            }

            if (targets.Count == 0)
            {
                SetStatus("没有可作用的" + (scopeText ?? "正式") + "关。", MessageType.Warning);
                return;
            }

            if (!EditorUtility.DisplayDialog("批量修改",
                    "将对 " + targets.Count + " 个" + (scopeText ?? "正式") + "关卡执行「" + opName + "」。\n\n" +
                    "每个关卡在改动前都会自动备份一份，全部改动都在 Ctrl+Z 里。\n" +
                    "本地测试关（编号 ≥880）不会被改动。",
                    "执行", "取消"))
                return;

            int changed = 0;
            int backedUp = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                StageInfo info = targets[i];
                StageConfig config = info.Config;
                if (config == null)
                    continue;

                // 逐关备份：备份的是磁盘上的旧文件，所以必须在 SetDirty/SaveAssets 之前做。
                if (!string.IsNullOrEmpty(StageEditorData.BackupFile(info.Path, "批量" + opName)))
                    backedUp++;

                Undo.RecordObject(config, "批量：" + opName);
                mutator(config, info);
                EditorUtility.SetDirty(config);
                changed++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            _issuesDirty = true;
            RefreshStageList();

            SetStatus("已对 " + changed + " 个关卡执行：" + opName + "（逐关备份 " + backedUp + " 份，可 Ctrl+Z 撤销）", MessageType.Info);
            Repaint();
        }

        private void ShowBackupMenu()
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("立即备份当前关卡"), false, () =>
            {
                string file = _data.BackupNow("手动");
                SetStatus(string.IsNullOrEmpty(file) ? "备份失败。" : "已备份：" + Path.GetFileName(file),
                    string.IsNullOrEmpty(file) ? MessageType.Error : MessageType.Info);
            });

            menu.AddItem(new GUIContent("打开备份目录"), false, () =>
            {
                Directory.CreateDirectory(LevelEditorPrefs.BackupFolder);
                EditorUtility.RevealInFinder(LevelEditorPrefs.BackupFolder);
            });

            // 对比：先看清"上次改了什么"再决定要不要恢复 —— 以前只能整体恢复，看不了差异。
            using (new EditorGUI.DisabledScope(!_data.IsLoaded))
            {
                menu.AddItem(new GUIContent("与备份对比…"), false, () =>
                {
                    if (!_data.IsLoaded)
                    {
                        SetStatus("先打开一个关卡再对比。", MessageType.Warning);
                        return;
                    }
                    LevelEditorBackupDiffWindow.Open(_data.Current.Path);
                });
            }

            List<string> backups = StageEditorData.ListBackups();
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
                SetStatus("先选一个关卡，再恢复备份。", MessageType.Warning);
                return;
            }

            if (!EditorUtility.DisplayDialog("恢复备份",
                    "将用这个备份覆盖当前关卡资产：\n" + Path.GetFileName(file) + "\n\n目标：" + target +
                    "\n\n当前内容会先自动备份一份（文件级覆盖，Ctrl+Z 无效）。",
                    "确认恢复", "取消"))
                return;

            if (_data.RestoreBackup(file, target))
            {
                _issuesDirty = true;
                SetStatus("已恢复：" + Path.GetFileName(file), MessageType.Info);
            }
            else
            {
                SetStatus("恢复失败，详见 Console。", MessageType.Error);
            }
        }
        #endregion

        #region 欢迎页 / 状态栏 / 表单小控件
        private void DrawWelcome(string message)
        {
            _welcomeScroll = EditorGUILayout.BeginScrollView(_welcomeScroll);
            EditorGUILayout.Space(16f);
            EditorGUILayout.LabelField("关卡可视化编辑器", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("给策划用的关卡编辑器：看战场、调数值、查配置问题，不用碰代码。", LevelEditorUI.RichLabel);
            EditorGUILayout.Space(10f);

            EditorGUILayout.HelpBox(message, MessageType.Info);

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("全部功能", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "· 左栏：全部关卡卡片带，带玩法 / 时限 / 敌队数 / 对象数 / 上架状态 / 问题角标\n" +
                "· 中栏：本关出现的兵种 —— 按敌方队伍列出会出的兵（或该种族兵营的全部兵种）、本关实际生命/攻击、解锁时间，可直接增减白名单\n" +
                "· 右栏：数值表单（玩法时限 / 阵地 / 敌方等级 / 玩家经济 / 敌方队伍含槽位与兵种白名单 / 奖励 / 展示 / 全局规则）\n" +
                "· 体检：编号连续性、Addressables 上架、胜负条件配得对不对、坐标越界、资源键有效性、兵种白名单空条目……可一键修复安全项\n" +
                "· 批量改：时限 / 敌方等级 / 基地距离 / 奖励 / 敌队经济，逐关备份\n" +
                "· 安全：所有改动可 Ctrl+Z；保存与批量前自动备份到工具目录的 StageBackups",
                LevelEditorUI.RichLabel);

            EditorGUILayout.EndScrollView();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            if (_page == 1)
            {
                EditorGUILayout.LabelField("兵种一览（只改兵种资产）", LevelEditorUI.MiniLabel, GUILayout.Width(160f));
                if (_rosterPage != null && _rosterPage.DirtyCount > 0)
                {
                    GUI.contentColor = LevelEditorUI.Warn;
                    EditorGUILayout.LabelField("● 有 " + _rosterPage.DirtyCount + " 个兵种未保存",
                        LevelEditorUI.MiniLabel, GUILayout.Width(150f));
                    GUI.contentColor = Color.white;
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("工具 " + StageEditorData.SourceStamp(), LevelEditorUI.MiniLabel, GUILayout.Width(140f));

                GUI.contentColor = _rosterPage != null && _rosterPage.StatusType == MessageType.Warning
                    ? LevelEditorUI.Warn
                    : _rosterPage != null && _rosterPage.StatusType == MessageType.Error
                        ? LevelEditorUI.Error
                        : LevelEditorUI.Muted;
                EditorGUILayout.LabelField(_rosterPage != null ? _rosterPage.Status : string.Empty, LevelEditorUI.MiniLabel);
                GUI.contentColor = Color.white;
                EditorGUILayout.EndHorizontal();
                return;
            }

            if (_data.IsLoaded)
            {
                string dirty = _data.IsDirty ? "　● 有未保存的修改" : "　已保存";
                EditorGUILayout.LabelField("当前 " + _data.Current.Label + "　" + _data.Current.TypeLabel + dirty,
                    LevelEditorUI.MiniLabel, GUILayout.Width(280f));

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("工具 " + StageEditorData.SourceStamp(), LevelEditorUI.MiniLabel, GUILayout.Width(140f));
            }
            else
            {
                GUILayout.FlexibleSpace();
            }

            GUI.contentColor = _statusType == MessageType.Error ? LevelEditorUI.Error
                : _statusType == MessageType.Warning ? LevelEditorUI.Warn
                : LevelEditorUI.Muted;
            EditorGUILayout.LabelField(_status, LevelEditorUI.MiniLabel);
            GUI.contentColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        #region 表单小控件（统一在这里挂 Undo + 标脏）
        private void TextRow(string label, string value, Action<string> setter, string tooltip = null)
        {
            EditorGUI.BeginChangeCheck();
            string newValue = EditorGUILayout.TextField(new GUIContent(label, tooltip), value ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data.Stage, "修改关卡：" + label);
                setter(newValue);
                _data.MarkDirty();
            }
        }

        private void IntRow(string label, int value, Action<int> setter, string tooltip = null)
        {
            EditorGUI.BeginChangeCheck();
            int newValue = EditorGUILayout.IntField(new GUIContent(label, tooltip), value);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data.Stage, "修改关卡：" + label);
                setter(newValue);
                _data.MarkDirty();
            }
        }

        private void FloatRow(string label, float value, Action<float> setter, string tooltip = null)
        {
            EditorGUI.BeginChangeCheck();
            float newValue = EditorGUILayout.FloatField(new GUIContent(label, tooltip), value);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data.Stage, "修改关卡：" + label);
                setter(newValue);
                _data.MarkDirty();
            }
        }

        private void ToggleRow(string label, bool value, Action<bool> setter, string tooltip = null)
        {
            EditorGUI.BeginChangeCheck();
            bool newValue = EditorGUILayout.Toggle(new GUIContent(label, tooltip), value);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data.Stage, "修改关卡：" + label);
                setter(newValue);
                _data.MarkDirty();
            }
        }

        private void EnumRow<T>(string label, T value, Action<T> setter, string tooltip = null) where T : struct, IConvertible
        {
            if (!typeof(T).IsEnum)
                return;

            EditorGUI.BeginChangeCheck();
            T newValue = (T)(object)EditorGUILayout.EnumPopup(new GUIContent(label, tooltip), (Enum)(object)value);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data.Stage, "修改关卡：" + label);
                setter(newValue);
                _data.MarkDirty();
            }
        }

        private void Vector2IntRow(string label, Vector2Int value, Action<Vector2Int> setter, string tooltip = null)
        {
            EditorGUI.BeginChangeCheck();
            Vector2Int newValue = EditorGUILayout.Vector2IntField(new GUIContent(label, tooltip), value);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data.Stage, "修改关卡：" + label);
                setter(newValue);
                _data.MarkDirty();
            }
        }

        /// <summary>Vector2 行（敌方队伍的「开工间隔」用的是 Vector2 而不是 Vector2Int）。</summary>
        private void Vector2Row(string label, Vector2 value, Action<Vector2> setter, string tooltip = null)
        {
            EditorGUI.BeginChangeCheck();
            Vector2 newValue = EditorGUILayout.Vector2Field(new GUIContent(label, tooltip), value);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data.Stage, "修改关卡：" + label);
                setter(newValue);
                _data.MarkDirty();
            }
        }

        /// <summary>
        /// 「收入增长阶段」列表编辑器。
        ///
        /// 玩家侧与敌方队伍侧**语义完全相同**，所以共用这一份：
        ///   · 玩家：`StageConfig.incomeGrowth`，只在 `useIncomeCurve = true` 时生效（出身 LevelEditorData.PlayerIncomePerSec）；
        ///   · 敌队：`EnemyTeamConfig.incomeGrowth`，**无条件生效**，基准是队伍自己的 `baseGoldPerSecond`
        ///     （出处 `StageFacts.EnemyIncomePerSec` → `ComputeCurveIncome`）。
        /// 阶段里「止 ≤ 起」表示持续到关卡结束；每步增量是**累加**的阶梯，不是斜坡。
        /// </summary>
        private void DrawIncomeGrowthList(StageConfig config, List<IncomeGrowthPhase> phases, float baseIncome, string title)
        {
            if (phases == null)
                return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel, GUILayout.Width(120f));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("＋ 加一阶段", EditorStyles.miniButton, GUILayout.Width(84f)))
            {
                Undo.RecordObject(config, "新增收入阶段");
                float lastEnd = 0f;
                if (phases.Count > 0 && phases[phases.Count - 1] != null)
                    lastEnd = phases[phases.Count - 1].endTime;

                phases.Add(new IncomeGrowthPhase
                {
                    startTime = lastEnd,
                    endTime = lastEnd + 60f,
                    stepInterval = 10f,
                    stepAmount = 10f
                });
                _data.MarkDirty();
            }
            EditorGUILayout.EndHorizontal();

            if (phases.Count == 0)
            {
                EditorGUILayout.LabelField("（没有阶段：收入恒为基准值）", LevelEditorUI.MiniLabel);
                return;
            }

            for (int i = 0; i < phases.Count; i++)
            {
                IncomeGrowthPhase phase = phases[i];
                if (phase == null)
                    continue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("阶段 " + (i + 1), EditorStyles.boldLabel, GUILayout.Width(54f));

                float start = FloatField("起（秒）", phase.startTime, 56f);
                if (!Mathf.Approximately(start, phase.startTime))
                {
                    Undo.RecordObject(config, "修改收入阶段");
                    phase.startTime = Mathf.Max(0f, start);
                    _data.MarkDirty();
                }

                float end = FloatField("止（秒）", phase.endTime, 56f);
                if (!Mathf.Approximately(end, phase.endTime))
                {
                    Undo.RecordObject(config, "修改收入阶段");
                    phase.endTime = Mathf.Max(0f, end);
                    _data.MarkDirty();
                }

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("删", EditorStyles.miniButton, GUILayout.Width(26f)))
                {
                    Undo.RecordObject(config, "删除收入阶段");
                    phases.RemoveAt(i);
                    _data.MarkDirty();
                    // 跳出前把本阶段块开着的两个组配平，否则本帧组栈不平衡（Unity 报 Mismatched LayoutGroup）。
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                // 「每步」两个字段单独一行：挤在同一行时右栏拖窄会裁掉标签（历史上出现过「每步增量」显示成「每步增」）。
                EditorGUILayout.BeginHorizontal();

                float interval = FloatField("每步秒数", phase.stepInterval, 56f);
                if (!Mathf.Approximately(interval, phase.stepInterval))
                {
                    Undo.RecordObject(config, "修改收入阶段");
                    phase.stepInterval = Mathf.Max(0.1f, interval);
                    _data.MarkDirty();
                }

                float amount = FloatField("每步增量", phase.stepAmount, 56f);
                if (!Mathf.Approximately(amount, phase.stepAmount))
                {
                    Undo.RecordObject(config, "修改收入阶段");
                    phase.stepAmount = Mathf.Max(0f, amount);
                    _data.MarkDirty();
                }

                EditorGUILayout.EndHorizontal();

                bool openEnded = phase.endTime <= phase.startTime;
                int atLimit = StageFacts.ComputeCurveIncome(Mathf.Max(0f, baseIncome), new List<IncomeGrowthPhase> { phase },
                    openEnded ? phase.startTime + phase.stepInterval * 12f : phase.endTime);
                LevelEditorUI.Hint(openEnded
                    ? "止 ≤ 起 = 持续到关卡结束；12 步后约为 " + atLimit + "/秒"
                    : "到阶段末约为 " + atLimit + "/秒");

                EditorGUILayout.EndVertical();
            }
        }

        /// <summary>
        /// 「标签 + 数字框」的小控件：标签在左、输入框在右，各占各的宽度。
        ///
        /// 标签宽度**按文字实测宽度算**。原来写的是 `labelWidth * 0.55` —— 传 52 就只有 29px，
        /// 而「每步增量」四个字要 44px 以上，于是标签被裁成「每步增」，
        /// 看着像"字段显示不全"（其实字段没坏，是标签被挤没了）。
        /// </summary>
        /// <param name="fieldWidth">输入框宽度；标签宽度由文字自动算，不受这个值影响。</param>
        private static float FloatField(string label, float value, float fieldWidth)
        {
            float labelWidth = LevelEditorUI.MiniLabel.CalcSize(new GUIContent(label)).x + 4f;
            EditorGUILayout.LabelField(label, LevelEditorUI.MiniLabel, GUILayout.Width(labelWidth));
            return EditorGUILayout.FloatField(value, GUILayout.Width(fieldWidth));
        }
        #endregion
        #endregion
    }
}
