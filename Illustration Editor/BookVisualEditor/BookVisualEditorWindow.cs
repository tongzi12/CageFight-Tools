using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CageFightBookTool
{
    /// <summary>
    /// 图鉴可视化编辑器（主窗口）。
    ///
    /// 给不懂技术的策划用：
    ///   左栏 —— 派系分组 + 搜索 + 勾选，找条目不靠滚动；
    ///   中栏 —— 点选即改的表单，派系/种族/星级是下拉，头像/立绘是「搜索资源键 + 缩略图预览」；
    ///   右栏 —— 游戏内预览（12 格图鉴页 / Card）+ 数据体检；
    ///   工具栏 —— 保存、撤销、一键修复、CSV 导入导出、备份恢复。
    ///
    /// 所有改动都有 Undo 记录；保存前会自动把资产备份到工具目录下的 BookBackups 文件夹。
    /// </summary>
    public sealed class BookVisualEditorWindow : EditorWindow
    {
        private const string MenuPath = "工具/图鉴可视化编辑器 %&b";
        private const float MinListWidth = 240f;
        private const float MaxListWidth = 520f;
        private const float MinPreviewWidth = 300f;
        private const float MaxPreviewWidth = 720f;

        #region 状态
        private readonly BookEditorData _data = new BookEditorData();

        private int _selectedIndex = -1;

        /// <summary>上一次绘制表单时对应的条目下标：用于在「切换条目」时清掉 IMGUI 的文本编辑焦点。</summary>
        private int _focusOwnerIndex = -2;
        private string _search = string.Empty;
        private int _categoryFilter = -1;          // -1 = 全部
        private int _starFilter = 0;               // 0 = 全部
        private bool _onlyWithIssues;
        private int _listOrderMode;
        /// <summary>列表排序方式：游戏内按星级升序显示，默认与游戏一致，避免"数据里排后面的新条目"看起来跑错位置。</summary>
        private static readonly string[] ListOrderModes = { "按游戏顺序（星级升序）", "按数据原始顺序" };
        private readonly HashSet<int> _checked = new HashSet<int>();

        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private Vector2 _issueScroll;
        private Vector2 _welcomeScroll;

        private List<BookIssue> _issues = new List<BookIssue>();
        private Dictionary<int, List<BookIssue>> _issueMap = new Dictionary<int, List<BookIssue>>();
        private bool _issuesDirty = true;

        private readonly List<bool> _categoryFold = new List<bool>();
        private bool _unassignedFold = true;

        private int _previewTab;
        private int _previewPage;
        private int _rightTab;                     // 0=图鉴页 1=Card 2=体检 3=设置

        /// <summary>右栏是否收起。收起后中栏拿到全部宽度 —— 体检面板很占地方。</summary>
        private bool _rightCollapsed;
        private BookPreviewUnlockMode _unlockMode = BookPreviewUnlockMode.AllUnlockedOwned;
        private BookCardRenderer _cardRenderer;

        /// <summary>真实 Card 渲染的目标分辨率（游戏里 Book Canvas 是 1:1 像素，所以这里就是像素级一致）。</summary>
        private int _cardRenderResolution;
        private bool _cardFitToPanel = true;
        private float _cardZoom = 0.5f;

        private static readonly string[] CardResolutionLabels =
        {
            "1080×1920（手机竖屏，默认）",
            "1920×1080（横屏）",
            "1440×2560（2K 竖屏）",
            "720×1280（合入机型）",
            "1080×2340（全面屏）"
        };

        private static readonly int[][] CardResolutions =
        {
            new[] { 1080, 1920 },
            new[] { 1920, 1080 },
            new[] { 1440, 2560 },
            new[] { 720, 1280 },
            new[] { 1080, 2340 }
        };

        private float _listWidth = 300f;
        private float _previewWidth = 430f;

        private string _status = string.Empty;
        private MessageType _statusType = MessageType.None;
        #endregion

        #region 生命周期
        [MenuItem(MenuPath, false, 100)]
        public static void OpenWindow()
        {
            var window = GetWindow<BookVisualEditorWindow>(false, "图鉴编辑器", true);
            window.minSize = new Vector2(1080f, 560f);
            window.Show();
        }

        /// <summary>右键 BookCatalog 资产也能直接打开。</summary>
        [MenuItem("Assets/打开图鉴可视化编辑器", false, 2000)]
        private static void OpenFromContext()
        {
            BookCatalog catalog = Selection.activeObject as BookCatalog;
            OpenWindow();
            BookVisualEditorWindow window = GetWindow<BookVisualEditorWindow>();
            if (catalog != null)
                window.LoadCatalog(AssetDatabase.GetAssetPath(catalog));
        }

        [MenuItem("Assets/打开图鉴可视化编辑器", true)]
        private static bool OpenFromContextValidate()
        {
            return Selection.activeObject is BookCatalog;
        }

        private void OnEnable()
        {
            _listWidth = BookEditorPrefs.ListWidth;
            _rightCollapsed = BookEditorPrefs.RightCollapsed;
            _previewWidth = BookEditorPrefs.PreviewWidth;
            _unlockMode = (BookPreviewUnlockMode)BookEditorPrefs.PreviewUnlockMode;
            _listOrderMode = Mathf.Clamp(BookEditorPrefs.ListOrderMode, 0, ListOrderModes.Length - 1);
            _cardRenderResolution = Mathf.Clamp(BookEditorPrefs.CardRenderResolution, 0, CardResolutionLabels.Length - 1);
            _data.Changed += OnDataChanged;
            TryAutoLoad();
        }

        private void OnDisable()
        {
            _data.Changed -= OnDataChanged;
            BookEditorPrefs.ListWidth = _listWidth;
            BookEditorPrefs.RightCollapsed = _rightCollapsed;
            BookEditorPrefs.PreviewWidth = _previewWidth;
            BookEditorPrefs.PreviewUnlockMode = (int)_unlockMode;
            BookEditorPrefs.ListOrderMode = _listOrderMode;
            DisposeCardRenderer();
        }

        private void OnDestroy()
        {
            DisposeCardRenderer();
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is BookCatalog)
            {
                string path = AssetDatabase.GetAssetPath(Selection.activeObject);
                if (path != _data.AssetPath && !_data.IsDirty)
                    LoadCatalog(path);
            }
        }

        private void OnDataChanged()
        {
            _issuesDirty = true;
            Repaint();
        }

        private void TryAutoLoad()
        {
            if (Selection.activeObject is BookCatalog)
            {
                LoadCatalog(AssetDatabase.GetAssetPath(Selection.activeObject));
                return;
            }

            string last = BookEditorPrefs.LastCatalogPath;
            if (FileExists(last) && LoadCatalog(last))
                return;

            if (FileExists(BookEditorData.DefaultCatalogPath))
                LoadCatalog(BookEditorData.DefaultCatalogPath);
        }

        private static bool FileExists(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath) && File.Exists(assetPath);
        }

        /// <summary>加载图鉴资产。</summary>
        public bool LoadCatalog(string assetPath)
        {
            if (_data.IsLoaded && _data.IsDirty && !_data.AssetPath.Equals(assetPath, StringComparison.OrdinalIgnoreCase))
            {
                if (!EditorUtility.DisplayDialog("有未保存的修改",
                        "当前图鉴还有未保存的修改，切换资产会丢掉它们。\n要先保存吗？",
                        "保存并切换", "直接切换"))
                {
                    // 用户选择直接切换：继续往下走。
                }
                else
                {
                    SaveNow(false);
                }
            }

            bool ok = _data.Load(assetPath);
            if (!ok)
            {
                SetStatus("加载失败：" + assetPath + "（确认这个路径下是 BookCatalog 资产）", MessageType.Error);
                return false;
            }

            BookEditorPrefs.LastCatalogPath = assetPath;
            SelectEntry(-1);
            _checked.Clear();
            _previewTab = 0;
            _previewPage = 0;
            _issuesDirty = true;
            DisposeCardRenderer();
            SetStatus("已加载：" + assetPath, MessageType.Info);
            Repaint();
            return true;
        }
        #endregion

        #region 主循环
        private void OnGUI()
        {
            // 兜底：如果选中项是别处改的（删除条目后下标变化等），也清一次编辑焦点，
            // 避免上一个条目的输入缓冲继续留在界面上。
            if (_focusOwnerIndex != _selectedIndex)
            {
                ClearEditorFocus();
                _focusOwnerIndex = _selectedIndex;
            }

            if (Event.current.type == EventType.KeyDown && Event.current.control && Event.current.keyCode == KeyCode.S)
            {
                SaveNow(BookEditorPrefs.AutoBackupOnSave);
                Event.current.Use();
            }

            DrawToolbar();

            if (!_data.IsLoaded)
            {
                DrawWelcome();
                return;
            }

            EnsureIssues();

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical(GUILayout.Width(_listWidth), GUILayout.ExpandHeight(true));
            DrawLeftColumn();
            EditorGUILayout.EndVertical();

            Rect leftSplitter = GUILayoutUtility.GetRect(5f, 5f, GUILayout.Width(5f), GUILayout.ExpandHeight(true));
            _listWidth = BookEditorUI.Splitter(leftSplitter, _listWidth, MinListWidth, MaxListWidth);

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawMiddleColumn();
            EditorGUILayout.EndVertical();

            // 收起时整条右栏不画（连分割条也不画），中栏自动拿到全部宽度
            if (!_rightCollapsed)
            {
                Rect rightSplitter = GUILayoutUtility.GetRect(5f, 5f, GUILayout.Width(5f), GUILayout.ExpandHeight(true));
                _previewWidth = BookEditorUI.Splitter(rightSplitter, _previewWidth, MinPreviewWidth, MaxPreviewWidth);
    
                EditorGUILayout.BeginVertical(GUILayout.Width(_previewWidth), GUILayout.ExpandHeight(true));
                DrawRightColumn();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndHorizontal();

            DrawStatusBar();
        }

        private void EnsureIssues()
        {
            if (!_issuesDirty)
                return;

            _issuesDirty = false;
            _issues = BookEditorValidator.Validate(_data);
            _issueMap = BookEditorValidator.GroupByEntry(_issues);
        }

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
        }

        /// <summary>
        /// 切换当前编辑的条目。
        ///
        /// 必须在这里清掉 IMGUI 的文本编辑焦点：否则在某个输入框里还带着光标时去点另一个条目，
        /// 那个输入框会继续沿用上一个条目的"编辑缓冲"——表现是显示成上一条的文字，
        /// 更糟的是这串旧文字会被当成"用户改动"写进新选中的条目。
        /// </summary>
        private void SelectEntry(int index)
        {
            if (_selectedIndex == index)
                return;

            ClearEditorFocus();
            _selectedIndex = index;
            _focusOwnerIndex = index;
        }

        /// <summary>丢弃当前文本框的编辑状态（光标 / 选区 / 编辑缓冲）。</summary>
        private static void ClearEditorFocus()
        {
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
        }

        /// <summary>
        /// 选键窗口是独立窗口，可能一直开着；用户中途切换了条目时，
        /// 旧的选键回调不能往已经不相关的条目上写值。
        /// </summary>
        private bool IsStillCurrent(BookEntry entry)
        {
            if (entry != null && ReferenceEquals(entry, _data.EntryAt(_selectedIndex)))
                return true;

            SetStatus("当前选中的条目已经变了，请在目标字段上重新点「搜索 / 搜索选图」。", MessageType.Warning);
            return false;
        }
        #endregion

        #region 工具栏
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUI.BeginChangeCheck();
            BookCatalog picked = (BookCatalog)EditorGUILayout.ObjectField(_data.Catalog, typeof(BookCatalog), false, GUILayout.Width(190f));
            if (EditorGUI.EndChangeCheck() && picked != null)
                LoadCatalog(AssetDatabase.GetAssetPath(picked));

            if (_rightCollapsed &&
                GUILayout.Button("▸ 展开右栏", EditorStyles.toolbarButton, GUILayout.Width(84f)))
            {
                SetRightCollapsed(false);
            }

            using (new EditorGUI.DisabledScope(!_data.IsLoaded))
            {
                GUI.backgroundColor = _data.IsDirty ? new Color(1f, 0.85f, 0.5f) : Color.white;
                if (GUILayout.Button(_data.IsDirty ? "● 保存 (Ctrl+S)" : "保存 (Ctrl+S)", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                    SaveNow(BookEditorPrefs.AutoBackupOnSave);
                GUI.backgroundColor = Color.white;

                if (GUILayout.Button("撤销", EditorStyles.toolbarButton, GUILayout.Width(46f)))
                    Undo.PerformUndo();

                if (GUILayout.Button("全选显示项", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    CheckAllVisible(true);

                if (GUILayout.Button("取消勾选", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                    _checked.Clear();

                if (GUILayout.Button("整理顺序", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                {
                    _data.SortEntries();
                    SetStatus("已按「派系 → 星级 → 名称」整理条目顺序。", MessageType.Info);
                }
            }

            GUILayout.FlexibleSpace();

            int errors, warnings, infos;
            BookEditorValidator.Count(_issues, out errors, out warnings, out infos);
            if (_data.IsLoaded)
            {
                GUI.contentColor = errors > 0 ? BookEditorUI.Error : (warnings > 0 ? BookEditorUI.Warn : BookEditorUI.Ok);
                GUILayout.Label("体检 " + errors + " 错 / " + warnings + " 警 / " + infos + " 提示", EditorStyles.toolbarButton, GUILayout.Width(170f));
                GUI.contentColor = Color.white;
                if (GUILayout.Button("一键修复", EditorStyles.toolbarButton, GUILayout.Width(66f)))
                    RunAutoFix();
            }

            if (GUILayout.Button("表格", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                ShowCsvMenu();

            if (GUILayout.Button("备份", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                ShowBackupMenu();

            if (GUILayout.Button("设置", EditorStyles.toolbarButton, GUILayout.Width(40f)))
                ShowSettingsMenu();

            EditorGUILayout.EndHorizontal();
        }

        private void SaveNow(bool backup)
        {
            if (!_data.IsLoaded)
                return;

            _data.CompactNulls();
            if (backup)
            {
                string file = _data.BackupNow("保存前");
                if (!string.IsNullOrEmpty(file))
                    Debug.Log("[图鉴编辑器] 已自动备份到：" + file);
            }

            _data.SaveToDisk(false);
            AssetDatabase.Refresh();
            SetStatus("已保存" + (backup ? "（旧版本已备份到工具目录的 BookBackups）" : string.Empty) + "：" + _data.AssetPath, MessageType.Info);
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
                SetStatus("没有可自动修复的问题。", MessageType.Info);
                return;
            }

            if (!EditorUtility.DisplayDialog("一键修复",
                    "将自动修复 " + fixable + " 处问题（补 ID、补角色名、修正派系/种族、修正无效资源键、修正星级）。\n\n" +
                    "只做安全的确定性修复，不会删条目。可随时 Ctrl+Z 撤销。",
                    "开始修复", "取消"))
                return;

            string backup = _data.BackupNow("一键修复前");
            int fixedCount = BookEditorValidator.ApplySafeFixes(_data, _issues);
            _issuesDirty = true;
            SetStatus("已修复 " + fixedCount + " 处。" + (string.IsNullOrEmpty(backup) ? string.Empty : "备份：" + Path.GetFileName(backup)), MessageType.Info);
            Repaint();
        }
        #endregion

        #region 左栏：列表
        private void DrawLeftColumn()
        {
            BookEditorUI.Section("图鉴条目", "共 " + _data.EntryCount + " 条");

            _search = BookEditorUI.SearchField(_search, "搜索：角色名 / ID / 资源键 / 简介");

            EditorGUILayout.BeginHorizontal();
            List<string> categories = _data.Categories;
            var filterOptions = new List<string> { "全部派系" };
            filterOptions.AddRange(categories);
            filterOptions.Add("（未分类）");
            int filterIndex = _categoryFilter + 1;
            if (filterIndex < 0 || filterIndex >= filterOptions.Count)
                filterIndex = 0;
            filterIndex = EditorGUILayout.Popup(filterIndex, filterOptions.ToArray(), GUILayout.Width(130f));
            _categoryFilter = filterIndex - 1;

            var starOptions = new List<string> { "全部星级" };
            for (int i = 1; i <= 7; i++)
                starOptions.Add(i + " 星");
            _starFilter = EditorGUILayout.Popup(_starFilter, starOptions.ToArray(), GUILayout.Width(90f));
            EditorGUILayout.EndHorizontal();

            _onlyWithIssues = EditorGUILayout.ToggleLeft("只看有问题的条目（" + _issueMap.Count + " 条）", _onlyWithIssues);
            _listOrderMode = EditorGUILayout.Popup("列表排序", _listOrderMode, ListOrderModes);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));

            bool filtered = !string.IsNullOrEmpty(_search) || _categoryFilter != -1 || _starFilter != 0 || _onlyWithIssues;
            if (filtered)
                DrawFlatList();
            else
                DrawGroupedList();

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("＋ 新增条目", GUILayout.Height(22f)))
            {
                string category = categories.Count > 0 ? categories[Mathf.Max(0, _previewTab)] : string.Empty;
                int index = _data.AddEntry(category);
                if (index >= 0)
                {
                    SelectEntry(index);
                    _search = string.Empty;
                    SetStatus("已在「" + category + "」新增条目，记得填角色名和资源键。", MessageType.Info);
                }
            }

            using (new EditorGUI.DisabledScope(_selectedIndex < 0))
            {
                if (GUILayout.Button("复制", GUILayout.Height(22f), GUILayout.Width(46f)))
                {
                    int newIndex = _data.DuplicateEntry(_selectedIndex);
                    if (newIndex >= 0)
                        SelectEntry(newIndex);
                }

                if (GUILayout.Button("删除", GUILayout.Height(22f), GUILayout.Width(46f)))
                    DeleteSelectedEntry();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("批量操作 ▾", GUILayout.Height(20f)))
                ShowBatchMenu();
            if (GUILayout.Button("智能匹配图片", GUILayout.Height(20f)))
                RunSmartMatch();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("勾选 = 批量操作对象（当前 " + _checked.Count + " 条）", EditorStyles.miniLabel);
        }

        /// <summary>
        /// 与游戏内 BookController.Refresh() 的排序保持一致：同派系内按星级升序，再按角色名。
        /// 这样"新加的条目排在数据末尾但游戏里显示在前面"的情况就不会让人困惑。
        /// </summary>
        private static int CompareGameOrder(BookEntry a, BookEntry b)
        {
            int sa = a != null ? a.star : 0;
            int sb = b != null ? b.star : 0;
            if (sa != sb)
                return sa.CompareTo(sb);

            string na = a != null ? a.displayName : string.Empty;
            string nb = b != null ? b.displayName : string.Empty;
            return string.Compare(na, nb, StringComparison.CurrentCulture);
        }

        private void SortForDisplay(List<int> indices, List<BookEntry> entries)
        {
            if (_listOrderMode != 0 || indices.Count < 2)
                return;

            indices.Sort((x, y) =>
            {
                BookEntry a = x >= 0 && x < entries.Count ? entries[x] : null;
                BookEntry b = y >= 0 && y < entries.Count ? entries[y] : null;
                return CompareGameOrder(a, b);
            });
        }

        private void DrawGroupedList()
        {
            List<BookEntry> entries = _data.EntriesSnapshot();
            List<string> categories = _data.Categories;

            while (_categoryFold.Count < categories.Count)
                _categoryFold.Add(true);

            for (int c = 0; c < categories.Count; c++)
            {
                var indices = new List<int>();
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i] != null && entries[i].categoryId == categories[c])
                        indices.Add(i);
                }

                SortForDisplay(indices, entries);

                bool fold = _categoryFold[c];
                EditorGUILayout.BeginHorizontal();
                fold = EditorGUILayout.Foldout(fold, "【" + categories[c] + "】 " + indices.Count + " 条", true, EditorStyles.foldoutHeader);
                if (GUILayout.Button("＋", EditorStyles.miniButton, GUILayout.Width(22f)))
                {
                    int index = _data.AddEntry(categories[c]);
                    if (index >= 0)
                        SelectEntry(index);
                }
                EditorGUILayout.EndHorizontal();
                _categoryFold[c] = fold;

                if (!fold)
                    continue;

                EditorGUI.indentLevel++;
                for (int i = 0; i < indices.Count; i++)
                    DrawEntryRow(indices[i], entries[indices[i]]);
                EditorGUI.indentLevel--;
            }

            // 未分类
            var unassigned = new List<int>();
            for (int i = 0; i < entries.Count; i++)
            {
                BookEntry e = entries[i];
                if (e == null)
                    continue;
                if (string.IsNullOrEmpty(e.categoryId) || !categories.Contains(e.categoryId))
                    unassigned.Add(i);
            }

            if (unassigned.Count > 0)
            {
                SortForDisplay(unassigned, entries);
                EditorGUILayout.BeginHorizontal();
                _unassignedFold = EditorGUILayout.Foldout(_unassignedFold, "【未分类】 " + unassigned.Count + " 条（不在任何页签里，玩家看不到）", true, EditorStyles.foldoutHeader);
                EditorGUILayout.EndHorizontal();
                if (_unassignedFold)
                {
                    EditorGUI.indentLevel++;
                    for (int i = 0; i < unassigned.Count; i++)
                        DrawEntryRow(unassigned[i], entries[unassigned[i]]);
                    EditorGUI.indentLevel--;
                }
            }
        }

        private void DrawFlatList()
        {
            List<int> indices = CollectVisibleIndices();
            EditorGUILayout.LabelField("筛选结果 " + indices.Count + " 条", EditorStyles.miniLabel);

            List<BookEntry> entries = _data.EntriesSnapshot();
            for (int i = 0; i < indices.Count; i++)
                DrawEntryRow(indices[i], entries[indices[i]]);
        }

        /// <summary>按当前搜索/筛选条件收集条目下标。</summary>
        private List<int> CollectVisibleIndices()
        {
            var result = new List<int>();
            List<BookEntry> entries = _data.EntriesSnapshot();
            List<string> categories = _data.Categories;
            string query = (_search ?? string.Empty).Trim();

            for (int i = 0; i < entries.Count; i++)
            {
                BookEntry e = entries[i];
                if (e == null)
                    continue;

                if (_categoryFilter >= 0)
                {
                    if (_categoryFilter < categories.Count)
                    {
                        if (e.categoryId != categories[_categoryFilter])
                            continue;
                    }
                    else if (!string.IsNullOrEmpty(e.categoryId) && categories.Contains(e.categoryId))
                    {
                        continue;   // 「未分类」筛选项
                    }
                }

                if (_starFilter != 0 && e.star != _starFilter)
                    continue;

                if (_onlyWithIssues && !_issueMap.ContainsKey(i))
                    continue;

                if (!string.IsNullOrEmpty(query) && !MatchesSearch(e, query))
                    continue;

                result.Add(i);
            }

            SortForDisplay(result, entries);
            return result;
        }

        private static bool MatchesSearch(BookEntry e, string query)
        {
            return Contains(e.displayName, query) || Contains(e.id, query) || Contains(e.prefabKey, query) ||
                   Contains(e.avatarSpriteKey, query) || Contains(e.roleSpriteKey, query) ||
                   Contains(e.categoryId, query) || Contains(e.raceId, query) || Contains(e.explainText, query);
        }

        private static bool Contains(string source, string query)
        {
            return !string.IsNullOrEmpty(source) && source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>一行条目：勾选框 + 缩略图 + 名字 + 星级 + 问题标记。</summary>
        private void DrawEntryRow(int index, BookEntry entry)
        {
            if (entry == null)
            {
                EditorGUILayout.LabelField("（空槽位，保存时自动清理）", EditorStyles.miniLabel);
                return;
            }

            Rect row = GUILayoutUtility.GetRect(0f, 26f, GUILayout.ExpandWidth(true));
            bool selected = index == _selectedIndex;
            List<BookIssue> entryIssues;
            bool hasIssues = _issueMap.TryGetValue(index, out entryIssues) && entryIssues != null && entryIssues.Count > 0;

            if (selected)
                BookEditorUI.FillRect(row, BookEditorUI.Selected);
            else if (row.Contains(Event.current.mousePosition))
                BookEditorUI.FillRect(row, new Color(1f, 1f, 1f, 0.06f));

            // 勾选框
            var checkRect = new Rect(row.x + 2f, row.y + 5f, 15f, 15f);
            bool wasChecked = _checked.Contains(index);
            bool nowChecked = EditorGUI.Toggle(checkRect, wasChecked);
            if (nowChecked != wasChecked)
            {
                if (nowChecked) _checked.Add(index);
                else _checked.Remove(index);
            }

            // 缩略图（资源键优先：游戏只认资源键）
            var thumbRect = new Rect(row.x + 21f, row.y + 2f, 22f, 22f);
            Sprite avatar = BookEditorData.ResolveAvatarSprite(entry);
            BookEditorUI.DrawSpriteFit(thumbRect, avatar, Color.white, 1f);

            // 名字 / ID
            string name = string.IsNullOrEmpty(entry.displayName) ? "（未填角色名）" : entry.displayName;
            var nameRect = new Rect(row.x + 47f, row.y + 2f, row.width - 100f, 16f);
            GUI.Label(nameRect, name, EditorStyles.miniBoldLabel);

            var subRect = new Rect(row.x + 47f, row.y + 14f, row.width - 100f, 12f);
            GUI.Label(subRect, (string.IsNullOrEmpty(entry.id) ? "无 ID" : entry.id) + "　" + entry.star + "★", EditorStyles.miniLabel);

            // 问题标记
            if (hasIssues)
            {
                Color c = BookEditorUI.Muted;
                for (int i = 0; i < entryIssues.Count; i++)
                {
                    if (entryIssues[i].Level == BookIssueLevel.Error) { c = BookEditorUI.Error; break; }
                    if (entryIssues[i].Level == BookIssueLevel.Warning) c = BookEditorUI.Warn;
                }
                BookEditorUI.FillRect(new Rect(row.xMax - 10f, row.y + 8f, 8f, 8f), c);
            }

            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition) && !checkRect.Contains(Event.current.mousePosition))
            {
                SelectEntry(index);
                SyncPreviewToEntry(entry);
                Event.current.Use();
                Repaint();
            }
        }

        private void SyncPreviewToEntry(BookEntry entry)
        {
            if (entry == null)
                return;

            List<string> categories = _data.Categories;
            int tab = categories.IndexOf(entry.categoryId);
            if (entry.categoryId == null || tab < 0)
                tab = categories.Count;   // 未分类页签
            _previewTab = Mathf.Max(0, tab);

            int slot = 0;
            List<int> indices = BuildPreviewIndices(_previewTab);
            for (int i = 0; i < indices.Count; i++)
            {
                if (_data.EntryAt(indices[i]) == entry)
                {
                    slot = i;
                    break;
                }
            }
            _previewPage = slot / BookEditorPreview.PageSize;
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

        private void DeleteSelectedEntry()
        {
            if (_selectedIndex < 0)
                return;

            BookEntry entry = _data.EntryAt(_selectedIndex);
            string name = entry != null && !string.IsNullOrEmpty(entry.displayName) ? entry.displayName : ("条目 " + (_selectedIndex + 1));

            if (!EditorUtility.DisplayDialog("删除条目", "确定删除「" + name + "」吗？（可 Ctrl+Z 撤销）", "删除", "取消"))
                return;

            _data.BackupNow("删除条目前");
            if (_data.DeleteEntry(_selectedIndex))
            {
                _checked.Remove(_selectedIndex);
                SelectEntry(-1);
                SetStatus("已删除「" + name + "」。", MessageType.Info);
            }
        }
        #endregion

        #region 中栏：详细表单
        private void DrawMiddleColumn()
        {
            if (_selectedIndex < 0 || _data.EntryAt(_selectedIndex) == null)
            {
                EditorGUILayout.Space(20f);
                EditorGUILayout.HelpBox("左侧点选一个条目开始编辑。\n\n" +
                                        "· 派系 / 种族 / 星级是下拉选择，不会填错格式\n" +
                                        "· 头像与立绘点「搜索」按键名选图，右侧立刻能看到效果\n" +
                                        "· 改完点工具栏「保存 (Ctrl+S)」，旧版本会自动备份到工具目录的 BookBackups", MessageType.Info);
                return;
            }

            BookEntry entry = _data.EntryAt(_selectedIndex);
            _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);

            DrawEntryHeader(entry);

            DrawBasicSection(entry);
            DrawSpriteSection(entry);
            DrawTextSection(entry);
            DrawExplainSection(entry);

            EditorGUILayout.Space(8f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("复制此条目", GUILayout.Height(24f)))
            {
                int newIndex = _data.DuplicateEntry(_selectedIndex);
                if (newIndex >= 0)
                    SelectEntry(newIndex);
            }
            if (GUILayout.Button("在资产里定位", GUILayout.Height(24f)))
                EditorGUIUtility.PingObject(_data.Catalog);
            if (GUILayout.Button("删除此条目", GUILayout.Height(24f)))
                DeleteSelectedEntry();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }

        private void DrawEntryHeader(BookEntry entry)
        {
            EditorGUILayout.BeginHorizontal();

            var previewRect = GUILayoutUtility.GetRect(64f, 64f, GUILayout.Width(64f), GUILayout.Height(64f));
            Sprite avatar = BookEditorData.ResolveAvatarSprite(entry);
            BookEditorUI.FillRect(previewRect, new Color(0f, 0f, 0f, 0.2f));
            BookEditorUI.DrawSpriteFit(previewRect, avatar, Color.white, 2f);

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(string.IsNullOrEmpty(entry.displayName) ? "（未填角色名）" : entry.displayName, EditorStyles.boldLabel);
            EditorGUILayout.LabelField("条目 " + (_selectedIndex + 1) + " / " + _data.EntryCount + "　派系：" + (string.IsNullOrEmpty(entry.categoryId) ? "未分类" : entry.categoryId), EditorStyles.miniLabel);

            List<BookIssue> entryIssues;
            if (_issueMap.TryGetValue(_selectedIndex, out entryIssues) && entryIssues.Count > 0)
            {
                for (int i = 0; i < entryIssues.Count && i < 3; i++)
                {
                    GUI.contentColor = entryIssues[i].LevelColor;
                    EditorGUILayout.LabelField("· " + entryIssues[i].Title + "：" + entryIssues[i].Detail, BookEditorUI.MiniLabel);
                    GUI.contentColor = Color.white;
                }
            }
            else
            {
                GUI.contentColor = BookEditorUI.Ok;
                EditorGUILayout.LabelField("· 这条数据没有问题", BookEditorUI.MiniLabel);
                GUI.contentColor = Color.white;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);
        }

        private void DrawBasicSection(BookEntry entry)
        {
            BookEditorUI.Section("基本信息", "带 ● 的是玩家能直接看到的内容");

            TextRow("● 角色名", entry.displayName, value =>
            {
                entry.displayName = value;
            });

            TextRow("唯一 ID", entry.id, value =>
            {
                entry.id = value;
            }, "解锁进度按这个 ID 记录。改名会让老玩家的解锁记录对不上，一般不要动。");

            List<string> categories = _data.Categories;
            PopupRow("● 所属派系（页签）", categories, entry.categoryId, value =>
            {
                entry.categoryId = value;
            });

            List<string> raceOptions = _data.RaceOptions;
            PopupRow("● 种族图标", raceOptions, entry.raceId, value =>
            {
                entry.raceId = value;
            });

            StarRow("● 星级", entry.star, value =>
            {
                entry.star = value;
            });

            EditorGUILayout.BeginHorizontal();
            TextRow("单位预制体键", entry.prefabKey, value =>
            {
                entry.prefabKey = value.Trim();
            }, "关卡内遇到 / 获得该单位时解锁。点右边按钮可搜索选择。");
            if (GUILayout.Button("搜索", EditorStyles.miniButton, GUILayout.Width(44f)))
            {
                BookEntry target = entry;
                BookKeyPickerWindow.Open("单位预制体键", target.displayName, false,
                    key =>
                    {
                        if (!IsStillCurrent(target)) return;
                        Undo.RecordObject(_data.Catalog, "选择单位预制体键");
                        target.prefabKey = key;
                        _data.MarkDirty();
                        Repaint();
                    },
                    () =>
                    {
                        if (!IsStillCurrent(target)) return;
                        Undo.RecordObject(_data.Catalog, "清空单位预制体键");
                        target.prefabKey = string.Empty;
                        _data.MarkDirty();
                        Repaint();
                    },
                    null);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("　", BookEditorUI.MiniLabel);
        }

        private void DrawSpriteSection(BookEntry entry)
        {
            BookEditorUI.Section("图片资源", "出包后按「资源键」加载，键必须存在于 SpriteRegistry");

            DrawSpriteField("● 图鉴格子头像", entry, true);
            EditorGUILayout.Space(4f);
            DrawSpriteField("● Card 立绘", entry, false);

            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("按单位键智能匹配两张图", GUILayout.Height(22f)))
                SmartMatchEntry(entry, _selectedIndex, true);
            EditorGUILayout.LabelField("提示：也可以把 Sprite 直接拖到左边小图上", BookEditorUI.MiniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSpriteField(string label, BookEntry entry, bool isAvatar)
        {
            string currentKey = isAvatar ? entry.avatarSpriteKey : entry.roleSpriteKey;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(112f));

            var thumbRect = GUILayoutUtility.GetRect(46f, 46f, GUILayout.Width(46f), GUILayout.Height(46f));
            Sprite sprite = isAvatar ? BookEditorData.ResolveAvatarSprite(entry) : BookEditorData.ResolveRoleSprite(entry);
            BookEditorUI.FillRect(thumbRect, new Color(0f, 0f, 0f, 0.2f));
            BookEditorUI.DrawSpriteFit(thumbRect, sprite, Color.white, 2f);
            HandleSpriteDrop(thumbRect, entry, isAvatar);

            EditorGUILayout.BeginVertical();
            string typed = EditorGUILayout.TextField(currentKey);
            if (!string.Equals(typed, currentKey, StringComparison.Ordinal))
            {
                Undo.RecordObject(_data.Catalog, "修改资源键");
                if (isAvatar) entry.avatarSpriteKey = typed.Trim();
                else entry.roleSpriteKey = typed.Trim();
                // 键变了就同步编辑器预览直引，避免「编辑器显示」和「游戏显示」不是同一张图
                _data.SyncEditorSprite(entry, isAvatar);
                _data.MarkDirty();
            }

            bool valid = BookResourceIndex.Instance.HasSprite(currentKey);
            if (!string.IsNullOrEmpty(currentKey))
            {
                GUI.contentColor = valid ? BookEditorUI.Ok : BookEditorUI.Error;
                EditorGUILayout.LabelField(valid ? "键有效，出包后可正常加载" : "键不存在于 SpriteRegistry，出包会加载失败", BookEditorUI.MiniLabel);
                GUI.contentColor = Color.white;
            }
            else
            {
                EditorGUILayout.LabelField("未设置（游戏内会显示空白）", BookEditorUI.MiniLabel);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("搜索选图", EditorStyles.miniButton))
            {
                BookEntry target = entry;
                bool avatar = isAvatar;
                BookKeyPickerWindow.Open(isAvatar ? "图鉴格子头像" : "Card 立绘", target.displayName, true,
                    key =>
                    {
                        if (!IsStillCurrent(target)) return;
                        Undo.RecordObject(_data.Catalog, "选择资源键");
                        if (avatar) target.avatarSpriteKey = key;
                        else target.roleSpriteKey = key;
                        _data.SyncEditorSprite(target, avatar);   // 键与预览直引保持一致
                        _data.MarkDirty();
                        Repaint();
                    },
                    () =>
                    {
                        if (!IsStillCurrent(target)) return;
                        Undo.RecordObject(_data.Catalog, "清空资源键");
                        if (avatar) target.avatarSpriteKey = string.Empty;
                        else target.roleSpriteKey = string.Empty;
                        _data.SyncEditorSprite(target, avatar);
                        _data.MarkDirty();
                        Repaint();
                    },
                    null);
            }

            if (GUILayout.Button("按单位键猜一个", EditorStyles.miniButton))
                GuessKeyFor(entry, isAvatar);

            if (GUILayout.Button("清空", EditorStyles.miniButton, GUILayout.Width(40f)))
            {
                Undo.RecordObject(_data.Catalog, "清空资源键");
                if (isAvatar) entry.avatarSpriteKey = string.Empty;
                else entry.roleSpriteKey = string.Empty;
                _data.SyncEditorSprite(entry, isAvatar);
                _data.MarkDirty();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>支持把 Sprite 直接拖到小图上自动填键（保留旧 Inspector 的顺手操作）。</summary>
        private void HandleSpriteDrop(Rect rect, BookEntry entry, bool isAvatar)
        {
            Event e = Event.current;
            if (!rect.Contains(e.mousePosition))
                return;

            if (e.type == EventType.DragUpdated)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                e.Use();
            }
            else if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                UnityEngine.Object[] objects = DragAndDrop.objectReferences;
                for (int i = 0; i < objects.Length; i++)
                {
                    Sprite sprite = objects[i] as Sprite;
                    if (sprite == null)
                        continue;

                    string key;
                    if (BookResourceIndex.Instance.TryGetKeyForSprite(sprite, out key))
                    {
                        Undo.RecordObject(_data.Catalog, "拖入 Sprite 填键");
                        if (isAvatar) entry.avatarSpriteKey = key;
                        else entry.roleSpriteKey = key;
                        _data.SyncEditorSprite(entry, isAvatar);
                        _data.MarkDirty();
                        SetStatus("已按拖入的 Sprite 填键：" + key, MessageType.Info);
                    }
                    else
                    {
                        SetStatus("这个 Sprite 不在 SpriteRegistry 里，无法作为资源键。请先运行资源注册表生成。", MessageType.Warning);
                    }
                    break;
                }
                e.Use();
            }
        }

        private void GuessKeyFor(BookEntry entry, bool isAvatar)
        {
            if (string.IsNullOrEmpty(entry.prefabKey))
            {
                SetStatus("请先填「单位预制体键」，才能猜图片键。", MessageType.Warning);
                return;
            }

            List<string> guesses = isAvatar
                ? BookResourceIndex.Instance.GuessAvatarKeys(entry.prefabKey)
                : BookResourceIndex.Instance.GuessRoleKeys(entry.prefabKey);

            if (guesses.Count == 0)
            {
                SetStatus("没有猜出候选键，请用「搜索选图」手动选择。", MessageType.Warning);
                return;
            }

            var menu = new GenericMenu();
            for (int i = 0; i < guesses.Count && i < 24; i++)
            {
                string key = guesses[i];
                menu.AddItem(new GUIContent(key), false, () =>
                {
                    Undo.RecordObject(_data.Catalog, "填入候选资源键");
                    if (isAvatar) entry.avatarSpriteKey = key;
                    else entry.roleSpriteKey = key;
                    _data.SyncEditorSprite(entry, isAvatar);
                    _data.MarkDirty();
                    SetStatus("已填入：" + key, MessageType.Info);
                    Repaint();
                });
            }
            menu.ShowAsContext();
        }

        private void DrawTextSection(BookEntry entry)
        {
            BookEditorUI.Section("Card 数值文本", "填什么显示什么，卡面六行的文字");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            TextRow("Repel", entry.repelText, v => entry.repelText = v);
            TextRow("Quiantity", entry.quantityText, v => entry.quantityText = v);
            TextRow("Scope", entry.scopeText, v => entry.scopeText = v);
            EditorGUILayout.EndVertical();

            EditorGUILayout.BeginVertical();
            TextRow("time", entry.timeText, v => entry.timeText = v);
            TextRow("weight", entry.weightText, v => entry.weightText = v);
            TextRow("coin", entry.coinText, v => entry.coinText = v);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawExplainSection(BookEntry entry)
        {
            BookEditorUI.Section("Card 简介", "多行文本，卡面底部");

            EditorGUI.BeginChangeCheck();
            string text = EditorGUILayout.TextArea(entry.explainText, GUILayout.MinHeight(64f));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data.Catalog, "修改图鉴简介");
                entry.explainText = text;
                _data.MarkDirty();
            }
        }

        #region 表单小控件
        private void TextRow(string label, string value, Action<string> setter, string tooltip = null)
        {
            EditorGUI.BeginChangeCheck();
            string newValue = EditorGUILayout.TextField(new GUIContent(label, tooltip), value);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data.Catalog, "修改图鉴条目");
                setter(newValue);
                _data.MarkDirty();
            }
        }

        private void PopupRow(string label, List<string> options, string current, Action<string> setter, string tooltip = null)
        {
            var labels = new List<string>();
            for (int i = 0; i < options.Count; i++)
                labels.Add(string.IsNullOrEmpty(options[i]) ? "（空）" : options[i]);
            if (labels.Count == 0)
                labels.Add("（没有可选项）");

            int index = options.IndexOf(current);
            if (index < 0)
            {
                labels.Insert(0, string.IsNullOrEmpty(current) ? "（未设置）" : current + "（不在列表里）");
                index = 0;
            }

            EditorGUI.BeginChangeCheck();
            int selected = EditorGUILayout.Popup(new GUIContent(label, tooltip), index, labels.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                string value = selected >= 0 && selected < options.Count ? options[selected] : current;
                Undo.RecordObject(_data.Catalog, "修改图鉴条目");
                setter(value);
                _data.MarkDirty();
            }
        }

        private void StarRow(string label, int current, Action<int> setter, string tooltip = null)
        {
            var labels = new string[7];
            var values = new int[7];
            for (int i = 0; i < 7; i++)
            {
                labels[i] = (i + 1) + " 星";
                values[i] = i + 1;
            }

            EditorGUI.BeginChangeCheck();
            int value2 = EditorGUILayout.IntPopup(label, Mathf.Clamp(current, 1, 7), labels, values);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_data.Catalog, "修改图鉴星级");
                setter(value2);
                _data.MarkDirty();
            }
        }
        #endregion
        #endregion

        #region 右栏：预览 / 体检 / 设置
        /// <summary>收起 / 展开右栏。收起后中栏自动拿到整条宽度。</summary>
        private void SetRightCollapsed(bool collapsed)
        {
            _rightCollapsed = collapsed;
            BookEditorPrefs.RightCollapsed = collapsed;
            Repaint();
        }

        private void DrawRightColumn()
        {
            EditorGUILayout.BeginHorizontal();
            // 标题栏右侧的 ✕：收起整条右栏，把宽度让给中栏（体检面板很占地方）
            string[] tabs = { "图鉴页预览", "Card 预览", "数据体检", "设置" };
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
                case 0:
                    DrawBookPagePreview();
                    break;
                case 1:
                    DrawCardPreview();
                    break;
                case 2:
                    DrawIssuesPanel();
                    break;
                default:
                    DrawSettingsPanel();
                    break;
            }
        }

        private List<string> BuildPreviewTabLabels()
        {
            var labels = new List<string>();
            List<string> categories = _data.Categories;
            List<BookEntry> entries = _data.EntriesSnapshot();

            for (int c = 0; c < categories.Count; c++)
            {
                int count = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i] != null && entries[i].categoryId == categories[c])
                        count++;
                }
                labels.Add(categories[c] + " (" + count + ")");
            }

            int unassigned = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                BookEntry e = entries[i];
                if (e == null)
                    continue;
                if (string.IsNullOrEmpty(e.categoryId) || !categories.Contains(e.categoryId))
                    unassigned++;
            }
            if (unassigned > 0)
                labels.Add("未分类 (" + unassigned + ")");

            return labels;
        }

        private List<int> BuildPreviewIndices(int tabIndex)
        {
            var result = new List<int>();
            List<string> categories = _data.Categories;
            List<BookEntry> entries = _data.EntriesSnapshot();
            bool unassignedTab = tabIndex >= categories.Count;
            string categoryId = unassignedTab ? null : (tabIndex >= 0 && tabIndex < categories.Count ? categories[tabIndex] : null);

            for (int i = 0; i < entries.Count; i++)
            {
                BookEntry e = entries[i];
                if (e == null)
                    continue;

                if (unassignedTab)
                {
                    if (!string.IsNullOrEmpty(e.categoryId) && categories.Contains(e.categoryId))
                        continue;
                }
                else if (e.categoryId != categoryId)
                {
                    continue;
                }

                result.Add(i);
            }

            // 与游戏内一致：同派系内按星级升序。
            result.Sort((a, b) =>
            {
                BookEntry ea = _data.EntryAt(a);
                BookEntry eb = _data.EntryAt(b);
                int sa = ea != null ? ea.star : 0;
                int sb = eb != null ? eb.star : 0;
                if (sa != sb)
                    return sa.CompareTo(sb);
                string na = ea != null ? ea.displayName : string.Empty;
                string nb = eb != null ? eb.displayName : string.Empty;
                return string.Compare(na, nb, StringComparison.CurrentCulture);
            });

            return result;
        }

        private void DrawBookPagePreview()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("解锁状态模拟", GUILayout.Width(84f));
            var modes = new[] { "全部已解锁", "解锁但未拥有（灰白）", "全部上锁", "只解锁前一半" };
            int mode = EditorGUILayout.Popup((int)_unlockMode, modes);
            _unlockMode = (BookPreviewUnlockMode)mode;
            EditorGUILayout.EndHorizontal();

            List<string> tabLabels = BuildPreviewTabLabels();
            List<int> indices = BuildPreviewIndices(_previewTab);

            Rect area = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            int clicked = BookEditorPreview.DrawGridPage(area, _data, tabLabels, ref _previewTab, indices, ref _previewPage, _selectedIndex, _issueMap, _unlockMode);
            if (clicked >= 0)
            {
                SelectEntry(clicked);
                Repaint();
            }

            EditorGUILayout.LabelField("这与游戏内 Book 面板一致：每页 12 格、按星级排序、未解锁盖锁、未拥有显示灰白。", BookEditorUI.MiniLabel);
        }

        private void DrawCardPreview()
        {
            // 真实 Card 预制体渲染已"收起来"（实验性，暂未调通）：入口从预览页撤掉，
            // 需要时可在「设置」页重新打开。这里始终用稳定可靠的示意图。
            EditorGUILayout.LabelField(
                "按条目数据绘制的卡面示意图（稳定的保底预览）。真实 Card 预制体渲染已收起，见「设置」页。",
                BookEditorUI.MiniLabel);

            if (BookEditorPrefs.UseRealCardRender)
                DrawRealCardPreviewExperimental();

            BookEntry entry = _selectedIndex >= 0 ? _data.EntryAt(_selectedIndex) : null;

            Rect area = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (entry == null)
            {
                BookEditorUI.FillRect(area, BookEditorUI.PreviewBg);
                GUI.Label(area, "先在左侧选中一个条目", EditorStyles.centeredGreyMiniLabel);
                return;
            }

            if (BookEditorPrefs.UseRealCardRender)
            {
                if (DrawRealCardInto(area, entry))
                    return;
            }

            BookEditorPreview.DrawCardImGui(area, entry);
        }

        /// <summary>设置页里重新打开实验开关时才会走这里（默认关闭）。</summary>
        private void DrawRealCardPreviewExperimental()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("实验：真实 Card 预制体渲染（未调通，仅供排查用）", BookEditorUI.MiniLabel, GUILayout.Width(300f));
            _cardFitToPanel = EditorGUILayout.ToggleLeft("适应窗口", _cardFitToPanel, GUILayout.Width(78f));
            if (!_cardFitToPanel)
                _cardZoom = EditorGUILayout.Slider(_cardZoom, 0.1f, 2f);
            if (GUILayout.Button("刷新", EditorStyles.miniButton, GUILayout.Width(44f)))
            {
                DisposeCardRenderer();
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>把真实渲染结果画到指定区域；成功返回 true。</summary>
        private bool DrawRealCardInto(Rect area, BookEntry entry)
        {
            if (_cardRenderer == null)
                _cardRenderer = new BookCardRenderer();

            Rect crop;
            Texture texture = _cardRenderer.Render(entry, out crop);

            if (_cardRenderer.NeedsWarmup)
                Repaint();

            if (texture == null)
                return false;

            BookEditorUI.FillRect(area, BookEditorUI.PreviewBg);

            Vector2 cardPixels = _cardRenderer.CropPixelSize(crop);
            float aspect = cardPixels.x / Mathf.Max(1f, cardPixels.y);

            Rect dest = _cardFitToPanel
                ? FitCentered(area, aspect, 8f)
                : new Rect(
                    area.center.x - cardPixels.x * _cardZoom * 0.5f,
                    area.center.y - cardPixels.y * _cardZoom * 0.5f,
                    cardPixels.x * _cardZoom,
                    cardPixels.y * _cardZoom);

            GUI.DrawTextureWithTexCoords(dest, texture, crop, true);
            EditorGUILayout.LabelField("诊断：" + (_cardRenderer.Diagnostics ?? "无"), BookEditorUI.RichLabel);
            return true;
        }
        private static Rect FitCentered(Rect area, float aspect, float padding)
        {
            float availW = Mathf.Max(1f, area.width - padding * 2f);
            float availH = Mathf.Max(1f, area.height - padding * 2f);
            float w = availW;
            float h = w / Mathf.Max(0.0001f, aspect);
            if (h > availH)
            {
                h = availH;
                w = h * aspect;
            }
            return new Rect(area.x + (area.width - w) * 0.5f, area.y + (area.height - h) * 0.5f, w, h);
        }

        private void DrawIssuesPanel()
        {
            int errors, warnings, infos;
            BookEditorValidator.Count(_issues, out errors, out warnings, out infos);

            EditorGUILayout.BeginHorizontal();
            GUI.contentColor = errors > 0 ? BookEditorUI.Error : BookEditorUI.Ok;
            EditorGUILayout.LabelField("错误 " + errors + "　警告 " + warnings + "　提示 " + infos, EditorStyles.boldLabel);
            GUI.contentColor = Color.white;
            EditorGUILayout.EndHorizontal();

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

            _issueScroll = EditorGUILayout.BeginScrollView(_issueScroll, GUILayout.ExpandHeight(true));

            if (_issues.Count == 0)
                EditorGUILayout.HelpBox("数据很干净，没有发现问题。", MessageType.Info);

            for (int i = 0; i < _issues.Count; i++)
            {
                BookIssue issue = _issues[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                GUI.contentColor = issue.LevelColor;
                EditorGUILayout.LabelField((issue.Level == BookIssueLevel.Error ? "[错误] " : issue.Level == BookIssueLevel.Warning ? "[警告] " : "[提示] ") + issue.Title, EditorStyles.boldLabel);
                GUI.contentColor = Color.white;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(issue.Detail, BookEditorUI.RichLabel);

                EditorGUILayout.BeginHorizontal();
                if (!issue.IsAssetLevel)
                {
                    EditorGUILayout.LabelField("条目 " + (issue.EntryIndex + 1) + "：" + issue.EntryLabel, BookEditorUI.MiniLabel);
                    if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(44f)))
                    {
                        SelectEntry(issue.EntryIndex);
                        BookEntry entry = _data.EntryAt(issue.EntryIndex);
                        if (entry != null)
                            SyncPreviewToEntry(entry);
                        Repaint();
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("资产级问题", BookEditorUI.MiniLabel);
                }

                if (issue.Fixable)
                {
                    EditorGUILayout.LabelField("→ " + issue.SuggestedValue, BookEditorUI.MiniLabel);
                }
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.EndScrollView();
        }

        private void ExportIssueReport()
        {
            EnsureIssues();
            string path = EditorUtility.SaveFilePanel("导出体检报告", BookEditorPrefs.BackupFolder, "图鉴体检报告", "txt");
            if (string.IsNullOrEmpty(path))
                return;

            var sb = new StringBuilder();
            sb.AppendLine("图鉴体检报告　" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("资产：" + _data.AssetPath);
            sb.AppendLine("条目数：" + _data.EntryCount);
            sb.AppendLine(new string('-', 60));

            for (int i = 0; i < _issues.Count; i++)
            {
                BookIssue issue = _issues[i];
                sb.AppendLine("[" + issue.Level + "] " + issue.Title + "　" + (issue.IsAssetLevel ? "资产级" : "条目 " + (issue.EntryIndex + 1) + " " + issue.EntryLabel));
                sb.AppendLine("    " + issue.Detail);
                if (issue.Fixable)
                    sb.AppendLine("    建议改为：" + issue.SuggestedValue);
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

        private void DrawSettingsPanel()
        {
            EditorGUILayout.LabelField("资源注册表", EditorStyles.boldLabel);
            BookResourceIndex index = BookResourceIndex.Instance;
            EditorGUILayout.LabelField("SpriteRegistry：" + (index.SpriteRegistryFound ? index.SpriteKeys.Count + " 个键" : "未找到"), BookEditorUI.MiniLabel);
            EditorGUILayout.LabelField("PrefabRegistry：" + (index.PrefabRegistryFound ? index.PrefabKeys.Count + " 个键" : "未找到"), BookEditorUI.MiniLabel);
            if (GUILayout.Button("重新读取注册表"))
            {
                BookResourceIndex.Invalidate();
                _issuesDirty = true;
                Repaint();
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("备份", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("目录：" + BookEditorPrefs.BackupFolder, BookEditorUI.MiniLabel);
            EditorGUILayout.LabelField("（默认跟随工具目录，改过文件夹名也会自动找到）", BookEditorUI.MiniLabel);
            BookEditorPrefs.AutoBackupOnSave = EditorGUILayout.ToggleLeft("保存前自动备份", BookEditorPrefs.AutoBackupOnSave);
            BookEditorPrefs.KeepBackupCount = EditorGUILayout.IntSlider("每个资产保留份数", BookEditorPrefs.KeepBackupCount, 5, 200);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("立即备份"))
            {
                string file = _data.BackupNow("手动");
                SetStatus(string.IsNullOrEmpty(file) ? "备份失败。" : "已备份：" + file, string.IsNullOrEmpty(file) ? MessageType.Error : MessageType.Info);
            }
            if (GUILayout.Button("打开备份目录"))
            {
                Directory.CreateDirectory(BookEditorPrefs.BackupFolder);
                EditorUtility.RevealInFinder(BookEditorPrefs.BackupFolder);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("更改备份目录"))
            {
                string folder = EditorUtility.OpenFolderPanel("选择备份目录", BookEditorPrefs.BackupFolder, string.Empty);
                if (!string.IsNullOrEmpty(folder))
                {
                    BookEditorPrefs.BackupFolder = folder;
                    SetStatus("备份目录已改为：" + folder, MessageType.Info);
                }
            }
            if (GUILayout.Button("恢复默认（跟随工具目录）"))
            {
                BookEditorPrefs.BackupFolder = BookEditorData.DefaultBackupFolder;
                SetStatus("备份目录已恢复默认：" + BookEditorPrefs.BackupFolder, MessageType.Info);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Card 预览", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("默认用示意图（按条目数据绘制，稳定可靠）。", BookEditorUI.MiniLabel);
            bool useReal = EditorGUILayout.ToggleLeft("实验：用真实 Card 预制体渲染（未调通，默认关闭）", BookEditorPrefs.UseRealCardRender);
            if (useReal != BookEditorPrefs.UseRealCardRender)
            {
                BookEditorPrefs.UseRealCardRender = useReal;
                DisposeCardRenderer();
                SetStatus(useReal ? "已打开实验渲染（可能显示空白，仅供排查）。" : "已收起实验渲染，回到示意图。", MessageType.Info);
            }
            EditorGUILayout.LabelField("预制体：" + BookEditorPrefs.CardPrefabPath, BookEditorUI.MiniLabel);
            if (GUILayout.Button("选择 Card 预制体"))
            {
                string path = EditorUtility.OpenFilePanel("选择 Card 预制体", Application.dataPath, "prefab");
                if (!string.IsNullOrEmpty(path))
                {
                    int indexOfAssets = path.Replace('\\', '/').IndexOf("/Assets/", StringComparison.Ordinal);
                    if (indexOfAssets >= 0)
                    {
                        BookEditorPrefs.CardPrefabPath = path.Replace('\\', '/').Substring(indexOfAssets + 1);
                        DisposeCardRenderer();
                    }
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("图片一致性", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("按资源键同步全部条目的「编辑器预览图」。游戏只认资源键，预览直引不同步时编辑器显示会和游戏不一致。", BookEditorUI.RichLabel);
            if (GUILayout.Button("同步全部条目的预览图（按资源键）"))
            {
                int changed = _data.SyncAllEditorSprites();
                _issuesDirty = true;
                SetStatus(changed > 0 ? "已同步 " + changed + " 处预览图。" : "所有条目的预览图已经和资源键一致。", MessageType.Info);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("当前资产", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(string.IsNullOrEmpty(_data.AssetPath) ? "（未加载）" : _data.AssetPath, BookEditorUI.MiniLabel);

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
            if (GUILayout.Button("另存为副本"))
            {
                string path = EditorUtility.SaveFilePanelInProject("另存为新的图鉴资产", "BookCatalog_副本", "asset", "复制一份用于试算");
                if (!string.IsNullOrEmpty(path))
                {
                    string result = _data.SaveAsCopy(path);
                    SetStatus(string.IsNullOrEmpty(result) ? "另存失败。" : "已另存为：" + result, string.IsNullOrEmpty(result) ? MessageType.Error : MessageType.Info);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("（本工具源码位于 " + BookEditorData.ToolRoot + "\\BookVisualEditor，通过目录联接接入工程；不会改动工程里的其它文件。）", BookEditorUI.RichLabel);
        }
        #endregion

        #region 批量操作 / CSV / 备份菜单
        private void ShowBatchMenu()
        {
            if (_checked.Count == 0 && _selectedIndex < 0)
            {
                SetStatus("先勾选条目（或选中一条），再使用批量操作。", MessageType.Warning);
                return;
            }

            int targetCount = _checked.Count > 0 ? _checked.Count : 1;
            var menu = new GenericMenu();

            menu.AddDisabledItem(new GUIContent("作用对象：" + (_checked.Count > 0 ? "已勾选 " + targetCount + " 条" : "当前选中 1 条")));

            List<string> categories = _data.Categories;
            for (int i = 0; i < categories.Count; i++)
            {
                string category = categories[i];
                menu.AddItem(new GUIContent("移入派系/" + category), false, () => ApplyBatch("移入派系 " + category, entry => entry.categoryId = category));
            }

            List<string> races = _data.RaceOptions;
            for (int i = 0; i < races.Count; i++)
            {
                string race = races[i];
                menu.AddItem(new GUIContent("设置种族图标/" + race), false, () => ApplyBatch("设置种族 " + race, entry => entry.raceId = race));
            }

            for (int star = 1; star <= 7; star++)
            {
                int value = star;
                menu.AddItem(new GUIContent("设置星级/" + value + " 星"), false, () => ApplyBatch("设置 " + value + " 星", entry => entry.star = value));
            }

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("智能匹配图片（按单位键）"), false, RunSmartMatch);
            menu.AddItem(new GUIContent("按 ID 匹配单位预制体键（只填空着的，只认同名单位）"), false, FillPrefabKeysById);
            menu.AddItem(new GUIContent("清空勾选"), false, () =>
            {
                _checked.Clear();
                Repaint();
            });

            menu.ShowAsContext();
        }

        /// <summary>
        /// 按条目 ID 批量补「单位预制体键」：只处理当前为空的条目，
        /// 且只在注册表里存在同名（含忽略大小写/下划线差异）单位时才填，避免解锁指错单位。
        /// 没勾选任何条目时作用于全部条目。
        /// </summary>
        private void FillPrefabKeysById()
        {
            List<int> targets = new List<int>(_checked);
            bool allEntries = targets.Count == 0;
            if (allEntries)
            {
                for (int i = 0; i < _data.EntryCount; i++)
                    targets.Add(i);
            }

            if (targets.Count == 0)
                return;

            IReadOnlyList<string> prefabKeys = BookResourceIndex.Instance.PrefabKeys;
            var filled = new List<string>();
            var unmatched = new List<string>();

            _data.BackupNow("匹配单位键前");
            Undo.RecordObject(_data.Catalog, "按 ID 匹配单位预制体键");

            for (int i = 0; i < targets.Count; i++)
            {
                BookEntry entry = _data.EntryAt(targets[i]);
                if (entry == null || !string.IsNullOrEmpty(entry.prefabKey))
                    continue;

                string key = BookEditorValidator.SuggestUnitKeyFromId(entry.id, prefabKeys);
                if (key == null)
                {
                    unmatched.Add(string.IsNullOrEmpty(entry.displayName) ? entry.id : entry.displayName);
                    continue;
                }

                entry.prefabKey = key;
                filled.Add((string.IsNullOrEmpty(entry.displayName) ? entry.id : entry.displayName) + " → " + key);
            }

            if (filled.Count > 0)
                _data.MarkDirty();

            _issuesDirty = true;
            var sb = new StringBuilder();
            sb.Append("按 ID 匹配单位键：填好 " + filled.Count + " 条");
            if (unmatched.Count > 0)
                sb.Append("，没找到同名单位 " + unmatched.Count + " 条（" + string.Join("、", unmatched.ToArray()) + "）—— 这些请在「单位预制体键」旁点搜索手动选。");
            SetStatus(sb.ToString(), filled.Count > 0 ? MessageType.Info : MessageType.Warning);

            if (filled.Count > 0)
                Debug.Log("[图鉴编辑器] 按 ID 匹配单位键：\n" + string.Join("\n", filled.ToArray()));

            Repaint();
        }

        private void ApplyBatch(string opName, Action<BookEntry> mutator)
        {
            List<int> targets = new List<int>(_checked);
            if (targets.Count == 0 && _selectedIndex >= 0)
                targets.Add(_selectedIndex);

            if (targets.Count == 0)
                return;

            _data.BackupNow("批量操作前");
            Undo.RecordObject(_data.Catalog, "批量：" + opName);
            int done = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                BookEntry entry = _data.EntryAt(targets[i]);
                if (entry == null)
                    continue;

                mutator(entry);
                done++;
            }

            _data.MarkDirty();
            SetStatus("已对 " + done + " 条执行：" + opName, MessageType.Info);
            Repaint();
        }

        private void RunSmartMatch()
        {
            List<int> targets = new List<int>(_checked);
            if (targets.Count == 0 && _selectedIndex >= 0)
                targets.Add(_selectedIndex);

            if (targets.Count == 0)
            {
                SetStatus("先勾选条目，再点「智能匹配图片」。", MessageType.Warning);
                return;
            }

            _data.BackupNow("智能匹配前");
            Undo.RecordObject(_data.Catalog, "智能匹配图片");
            int changed = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (SmartMatchEntry(_data.EntryAt(targets[i]), targets[i], false))
                    changed++;
            }

            if (changed > 0)
                _data.MarkDirty();

            SetStatus("智能匹配完成：补全 " + changed + " 个条目的图片键（更准的选择请用「搜索选图」）。", MessageType.Info);
            Repaint();
        }

        /// <summary>
        /// 按单位预制体键补图鉴头像 / 立绘键。返回是否有改动。
        ///
        /// 优先级：**训练配置（TroopDefinition）里的 iconKey / animationSpriteKey** 最可靠
        /// （图鉴格子的头像本来就是兵营那个图标），其次才按单位名去前缀猜；
        /// 头像最后还有一招：从「种族 Troop UI 图集」里挑一个没被其它条目用过的键。
        /// </summary>
        private bool SmartMatchEntry(BookEntry entry, int index, bool interactive)
        {
            if (entry == null)
                return false;

            if (string.IsNullOrEmpty(entry.prefabKey))
            {
                if (interactive)
                    SetStatus("这条还没有填「单位预制体键」，无法智能匹配。先在「单位预制体键」旁点「搜索」选一个单位。", MessageType.Warning);
                return false;
            }

            BookResourceIndex index0 = BookResourceIndex.Instance;
            TroopDefinition definition = BookTroopDefinitionIndex.Find(entry.prefabKey);

            string definitionIcon = definition != null ? definition.IconKey : null;
            string definitionAnim = definition != null ? definition.AnimationSpriteKey : null;
            if (!string.IsNullOrEmpty(definitionIcon) && !index0.HasSprite(definitionIcon))
                definitionIcon = null;
            if (!string.IsNullOrEmpty(definitionAnim) && !index0.HasSprite(definitionAnim))
                definitionAnim = null;

            bool changed = false;
            string avatarSource = string.Empty;
            string roleSource = string.Empty;

            var usedKeys = new HashSet<string>(StringComparer.Ordinal);
            List<BookEntry> entries = _data.EntriesSnapshot();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null || i == index)
                    continue;
                if (!string.IsNullOrEmpty(entries[i].avatarSpriteKey))
                    usedKeys.Add(entries[i].avatarSpriteKey);
            }

            // --- 立绘 ---
            if (string.IsNullOrEmpty(entry.roleSpriteKey))
            {
                if (!string.IsNullOrEmpty(definitionAnim))
                {
                    entry.roleSpriteKey = definitionAnim;
                    roleSource = "取自训练配置";
                    changed = true;
                }
                else
                {
                    List<string> roleGuesses = index0.GuessRoleKeys(entry.prefabKey);
                    if (roleGuesses.Count > 0)
                    {
                        entry.roleSpriteKey = roleGuesses[0];
                        roleSource = "按单位名猜的";
                        changed = true;
                    }
                }
            }

            // --- 头像 ---
            if (string.IsNullOrEmpty(entry.avatarSpriteKey))
            {
                if (!string.IsNullOrEmpty(definitionIcon))
                {
                    entry.avatarSpriteKey = definitionIcon;
                    avatarSource = "取自训练配置";
                    changed = true;
                }
                else
                {
                    List<string> avatarGuesses = index0.GuessAvatarKeys(entry.prefabKey);
                    for (int i = 0; i < avatarGuesses.Count; i++)
                    {
                        if (usedKeys.Contains(avatarGuesses[i]))
                            continue;

                        entry.avatarSpriteKey = avatarGuesses[i];
                        avatarSource = "按单位名猜的";
                        changed = true;
                        break;
                    }

                    if (!changed)
                    {
                        // 兜底：从该种族的「Troop UI 图集」里挑一个还没被用过的图标
                        string race = !string.IsNullOrEmpty(entry.categoryId) ? entry.categoryId : entry.raceId;
                        List<string> raceGuesses = index0.GuessAvatarKeysByRace(race, usedKeys);
                        if (raceGuesses.Count > 0)
                        {
                            entry.avatarSpriteKey = raceGuesses[0];
                            avatarSource = "从 " + race + " Troop UI 图集里挑的空位";
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
            {
                // 键被补全后同步预览直引（否则编辑器显示会和游戏不一致）
                _data.SyncEditorSprite(entry, true);
                _data.SyncEditorSprite(entry, false);
            }

            if (interactive)
            {
                Undo.RecordObject(_data.Catalog, "智能匹配图片");
                _data.MarkDirty();

                if (!changed)
                {
                    bool nothingMissing = !string.IsNullOrEmpty(entry.roleSpriteKey) && !string.IsNullOrEmpty(entry.avatarSpriteKey);
                    SetStatus(nothingMissing
                        ? "这条的头像与立绘都已经填好了，不需要匹配。"
                        : "没匹配到：「" + entry.prefabKey + "」在注册表里找不到同名图片" +
                          (definition == null ? "，而且它没有训练配置（TroopDefinition）可参考。" : "，训练配置里也没写图标/动画贴图键。") +
                          "请点「搜索选图」手动选。", MessageType.Warning);
                }
                else
                {
                    SetStatus("已补全：" + (string.IsNullOrEmpty(entry.displayName) ? entry.id : entry.displayName) +
                              " → 头像 " + (string.IsNullOrEmpty(entry.avatarSpriteKey) ? "（未填）" : entry.avatarSpriteKey + "（" + avatarSource + "）") +
                              "，立绘 " + (string.IsNullOrEmpty(entry.roleSpriteKey) ? "（未填）" : entry.roleSpriteKey + "（" + roleSource + "）"),
                              MessageType.Info);
                }
            }

            return changed;
        }

        private void ShowCsvMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("导出全部条目到 CSV（Excel 可直接打开）"), false, () =>
            {
                string path = EditorUtility.SaveFilePanel("导出图鉴 CSV", BookEditorPrefs.BackupFolder, Path.GetFileNameWithoutExtension(_data.AssetPath) + "_图鉴", "csv");
                if (string.IsNullOrEmpty(path))
                    return;

                string message;
                if (BookEditorCsv.Export(_data, path, out message))
                    SetStatus(message.Replace("\n", " "), MessageType.Info);
                else
                    SetStatus(message, MessageType.Error);
            });

            menu.AddItem(new GUIContent("导出空白模板（给策划照着填）"), false, () =>
            {
                string path = EditorUtility.SaveFilePanel("导出模板", BookEditorPrefs.BackupFolder, "图鉴模板", "csv");
                if (string.IsNullOrEmpty(path))
                    return;

                string message;
                BookEditorCsv.ExportTemplate(path, _data.Categories, _data.RaceOptions, out message);
                SetStatus(message.Replace("\n", " "), MessageType.Info);
            });

            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("从 CSV 导入 / 按 ID 更新已有条目 + 追加新行（推荐）"), false, () => ImportCsv(BookCsvImportMode.UpdateAndAppend));
            menu.AddItem(new GUIContent("从 CSV 导入 / 只更新已有条目（忽略表里的新行）"), false, () => ImportCsv(BookCsvImportMode.UpdateOnly));
            menu.AddItem(new GUIContent("从 CSV 导入 / 清空后完全按表格重建（危险）"), false, () => ImportCsv(BookCsvImportMode.ReplaceAll));
            menu.ShowAsContext();
        }

        private void ImportCsv(BookCsvImportMode mode)
        {
            if (mode == BookCsvImportMode.ReplaceAll)
            {
                if (!EditorUtility.DisplayDialog("危险操作",
                        "「清空后完全按表格重建」会先删掉当前图鉴里的全部 " + _data.EntryCount + " 条条目，再用表格内容重建。\n\n" +
                        "只在表里包含全部内容时才这么做。导入前会自动备份，可 Ctrl+Z 撤销。",
                        "我明白，继续", "取消"))
                    return;
            }

            string path = EditorUtility.OpenFilePanel("选择要导入的 CSV", BookEditorPrefs.BackupFolder, "csv");
            if (string.IsNullOrEmpty(path))
                return;

            BookCsvPlan plan = BookEditorCsv.BuildPlan(_data, path, mode, false);

            var sb = new StringBuilder();
            sb.AppendLine("文件：" + Path.GetFileName(path));
            sb.AppendLine("将更新已有条目：" + plan.UpdateCount + " 条");
            sb.AppendLine("将新增条目：" + plan.AppendCount + " 条");
            if (plan.MissingColumns.Count > 0)
                sb.AppendLine("表里缺少的列（这些字段保持原样）：" + string.Join("、", plan.MissingColumns.ToArray()));

            if (plan.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("需要注意：");
                for (int i = 0; i < plan.Warnings.Count && i < 12; i++)
                    sb.AppendLine("· " + plan.Warnings[i]);
                if (plan.Warnings.Count > 12)
                    sb.AppendLine("· …还有 " + (plan.Warnings.Count - 12) + " 条");
            }

            sb.AppendLine();
            sb.AppendLine("导入前会自动备份当前资产，导入后可 Ctrl+Z 撤销。");
            sb.AppendLine("空单元格 = 保持原值（不会清空）。");
            sb.AppendLine("匹配规则：按「唯一 ID」找已有条目" +
                          (mode == BookCsvImportMode.UpdateAndAppend ? "，表里的新 ID 会作为新条目追加。"
                              : mode == BookCsvImportMode.UpdateOnly ? "；表里的新 ID 会被忽略。"
                              : "；当前条目会先全部删除再重建。"));

            if (!EditorUtility.DisplayDialog("确认导入", sb.ToString(), "开始导入", "取消"))
                return;

            string result = BookEditorCsv.Apply(_data, plan, true);
            _issuesDirty = true;
            SetStatus(result, MessageType.Info);
            Repaint();
        }

        private void ShowBackupMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("立即备份当前资产"), false, () =>
            {
                string file = _data.BackupNow("手动");
                SetStatus(string.IsNullOrEmpty(file) ? "备份失败。" : "已备份：" + file, string.IsNullOrEmpty(file) ? MessageType.Error : MessageType.Info);
            });

            menu.AddItem(new GUIContent("打开备份目录"), false, () =>
            {
                Directory.CreateDirectory(BookEditorPrefs.BackupFolder);
                EditorUtility.RevealInFinder(BookEditorPrefs.BackupFolder);
            });

            List<string> backups = BookEditorData.ListBackups();
            if (backups.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("（还没有任何备份）"));
            }
            else
            {
                menu.AddSeparator(string.Empty);
                for (int i = 0; i < backups.Count && i < 20; i++)
                {
                    string file = backups[i];
                    menu.AddItem(new GUIContent("恢复：" + Path.GetFileName(file)), false, () => RestoreBackup(file));
                }
            }

            menu.ShowAsContext();
        }

        private void RestoreBackup(string file)
        {
            if (!EditorUtility.DisplayDialog("恢复备份",
                    "将用这个备份覆盖当前图鉴资产：\n" + Path.GetFileName(file) + "\n\n当前内容会先自动备份一份，恢复后可 Ctrl+Z 无效（属于文件级覆盖）。",
                    "确认恢复", "取消"))
                return;

            if (_data.RestoreBackup(file))
            {
                _issuesDirty = true;
                SetStatus("已恢复：" + Path.GetFileName(file), MessageType.Info);
            }
            else
            {
                SetStatus("恢复失败，详见 Console。", MessageType.Error);
            }
        }

        private void ShowSettingsMenu()
        {
            _rightTab = 3;
            SetRightCollapsed(false);
            Repaint();
        }
        #endregion

        #region 欢迎页 / 状态栏
        private void DrawWelcome()
        {
            _welcomeScroll = EditorGUILayout.BeginScrollView(_welcomeScroll);
            EditorGUILayout.Space(16f);
            EditorGUILayout.LabelField("图鉴可视化编辑器", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("给策划用的图鉴内容编辑器：改文字、换图、调星级、看效果，不用碰代码。", BookEditorUI.RichLabel);
            EditorGUILayout.Space(10f);

            EditorGUILayout.HelpBox(
                "还没有打开图鉴资产。\n\n" +
                "方式一：把 Assets/Resource/LocalResource/Book/BookCatalog.asset 拖到上面的资产框里；\n" +
                "方式二：在 Project 面板里右键 BookCatalog 资产 → 「打开图鉴可视化编辑器」；\n" +
                "方式三：点下面的按钮直接加载默认资产。",
                MessageType.Info);

            if (GUILayout.Button("加载默认图鉴资产（BookCatalog.asset）", GUILayout.Height(28f)))
            {
                if (!LoadCatalog(BookEditorData.DefaultCatalogPath))
                    SetStatus("默认路径下没有找到 BookCatalog 资产，请手动拖入。", MessageType.Warning);
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("全部功能", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "· 左栏：按派系分组浏览、搜索（角色名/ID/资源键/简介）、星级与问题筛选、勾选批量操作\n" +
                "· 中栏：点选即改；派系/种族/星级为下拉；头像与立绘支持「搜索资源键」和拖拽 Sprite 自动填键\n" +
                "· 右栏：图鉴页预览（12 格、翻页、解锁/灰白模拟）、Card 预览（示意图）、数据体检、设置\n" +
                "· 体检：重复 ID、缺字段、无效资源键、无效单位键、星级越界…可一键修复\n" +
                "· 表格：CSV 导出/导入（Excel 批量填写后回导，先预览再确认）\n" +
                "· 安全：所有改动可 Ctrl+Z；保存/批量/导入前自动备份到工具目录的 BookBackups",
                BookEditorUI.RichLabel);

            EditorGUILayout.EndScrollView();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            if (_data.IsLoaded)
            {
                int errors, warnings, infos;
                BookEditorValidator.Count(_issues, out errors, out warnings, out infos);
                string dirty = _data.IsDirty ? "　● 有未保存的修改" : "　已保存";
                EditorGUILayout.LabelField("条目 " + _data.EntryCount + "　问题 " + (errors + warnings + infos) + "（错 " + errors + "）" + dirty, EditorStyles.miniLabel, GUILayout.Width(320f));

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("工具 " + BookEditorData.SourceStamp(), EditorStyles.miniLabel, GUILayout.Width(130f));
            }

            GUI.contentColor = _statusType == MessageType.Error ? BookEditorUI.Error
                : _statusType == MessageType.Warning ? BookEditorUI.Warn
                : BookEditorUI.Muted;
            EditorGUILayout.LabelField(_status, BookEditorUI.MiniLabel);
            GUI.contentColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }
        #endregion

        private void DisposeCardRenderer()
        {
            if (_cardRenderer == null)
                return;

            _cardRenderer.Dispose();
            _cardRenderer = null;
        }
    }
}