using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightSpellTool
{
    /// <summary>
    /// 法术可视化编辑器（Spell Visual Editor）。
    ///
    /// 管的是 <c>SpellDefinition</c>（Assets\RemoteResource\LoadoutDefinitions\Magic\*.asset）
    /// 以及它们的"上架"状态（LoadoutDefinitionRegistry.spellDefinitions）。
    ///
    /// 设计原则与另外四个编辑器一致：
    ///   · **只读地**引用运行时类型，游戏代码一行不改；写数据只写法术资产与注册表资产，保存前自动备份；
    ///   · 界面上每一个"数值 → 结论"的判断都能追到运行时代码出处（体检里逐条写明）；
    ///   · 工具源码放在工程之外，靠目录联接挂进来（tools\junction.ps1）。
    /// </summary>
    public sealed class SpellEditorWindow : EditorWindow
    {
        private const string MenuPath = "工具/法术可视化编辑器 %&s";
        private const string WindowTitle = "法术编辑器";
        private const float ToolbarHeight = 24f;
        private const float StatusHeight = 22f;

        private readonly SpellCatalog _catalog = new SpellCatalog();
        private readonly List<SpellIssue> _issues = new List<SpellIssue>();
        private bool _issuesDirty = true;
        private double _lastValidateTime;

        private string _search = string.Empty;
        private string _selectedPath;
        private Vector2 _listScroll;
        private Vector2 _formScroll;
        private Vector2 _issueScroll;

        private float _listWidth = 300f;
        private float _rightWidth = 430f;
        private int _rightTab;
        private bool _rightCollapsed;

        private string _status = string.Empty;
        private MessageType _statusType = MessageType.None;

        private bool _unsaved;

        /// <summary>当前选中记录的 SerializedObject 缓存（每帧重建代价太高）。</summary>
        private SpellRecord _sessionRecord;
        private SerializedObject _sessionSo;

        private bool _onlyShowSelectedIssues;

        /// <summary>伤害字段改在施放预制体上，所以单独记住哪些预制体被改过（保存时一起备份）。</summary>
        private readonly HashSet<string> _dirtyPrefabs = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>当前法术的伤害/效果报告缓存（选中变化时重建，避免每帧扫预制体）。</summary>
        private SpellRecord _effectRecord;
        private SpellEffectReport _effectReport;

        /// <summary>「显示预制体全部字段」开关：打开后除伤害白名单外，还列出组件上的其它直观字段。</summary>
        private bool _showAllPrefabFields;

        #region 生命周期

        [MenuItem(MenuPath, false, 100)]
        public static void OpenWindow()
        {
            var window = GetWindow<SpellEditorWindow>(false, WindowTitle, true);
            window.minSize = new Vector2(1080f, 600f);
            window.Show();
        }

        [MenuItem("Assets/打开法术可视化编辑器", false, 2000)]
        private static void OpenFromContext()
        {
            var window = GetWindow<SpellEditorWindow>(false, WindowTitle, true);
            window.minSize = new Vector2(1080f, 600f);
            window.Show();

            SpellDefinition definition = Selection.activeObject as SpellDefinition;
            if (definition != null)
            {
                window.Rescan(true);
                string path = AssetDatabase.GetAssetPath(definition);
                for (int i = 0; i < window._catalog.Records.Count; i++)
                {
                    if (string.Equals(window._catalog.Records[i].AssetPath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        window.SelectRecord(window._catalog.Records[i]);
                        break;
                    }
                }
            }
        }

        [MenuItem("Assets/打开法术可视化编辑器", true)]
        private static bool OpenFromContextValidate()
        {
            return Selection.activeObject is SpellDefinition;
        }

        private void OnEnable()
        {
            _listWidth = SpellEditorPrefs.ListWidth;
            _rightWidth = SpellEditorPrefs.RightWidth;
            _rightTab = SpellEditorPrefs.RightTab;
            _rightCollapsed = SpellEditorPrefs.RightCollapsed;
            _search = SpellEditorPrefs.Search;
            titleContent = new GUIContent(WindowTitle);
            Rescan(true);
        }

        private void OnDisable()
        {
            SpellEditorPrefs.ListWidth = _listWidth;
            SpellEditorPrefs.RightWidth = _rightWidth;
            SpellEditorPrefs.RightTab = _rightTab;
            SpellEditorPrefs.RightCollapsed = _rightCollapsed;
            SpellEditorPrefs.Search = _search;
            CloseSession();
        }

        #endregion

        #region 数据与状态

        private SpellRecord Selected
        {
            get
            {
                if (string.IsNullOrEmpty(_selectedPath))
                    return null;
                return _catalog.Find(_selectedPath);
            }
        }

        private void SelectRecord(SpellRecord record)
        {
            _selectedPath = record != null ? record.AssetPath : null;
            CloseSession();
            if (record != null)
                SpellEditorPrefs.LastSpellPath = record.AssetPath;
            Repaint();
        }

        private void CloseSession()
        {
            _sessionRecord = null;
            _sessionSo = null;
        }

        /// <summary>取当前记录对应的 SerializedObject（字段是 private [SerializeField]，只能这样写）。</summary>
        private SerializedObject Session(SpellRecord record)
        {
            if (record == null || record.Definition == null)
                return null;

            if (_sessionSo == null || _sessionRecord != record || _sessionSo.targetObject != record.Definition)
            {
                _sessionRecord = record;
                _sessionSo = new SerializedObject(record.Definition);
            }

            return _sessionSo;
        }

        private void Rescan(bool silent)
        {
            SpellResourceIndex.Invalidate();
            _catalog.Rebuild();
            _issuesDirty = true;
            CloseSession();

            if (Selected == null && _catalog.Records.Count > 0)
            {
                // 优先回到上次编辑的那个法术
                string last = SpellEditorPrefs.LastSpellPath;
                SpellRecord target = !string.IsNullOrEmpty(last) ? _catalog.Find(last) : null;
                SelectRecord(target ?? _catalog.Records[0]);
            }

            if (!silent)
                SetStatus("重扫完成：" + _catalog.Records.Count + " 个法术资产。", MessageType.Info);
            Repaint();
        }

        private void EnsureIssues()
        {
            if (!_issuesDirty)
                return;

            double now = EditorApplication.timeSinceStartup;
            if (now - _lastValidateTime < 0.5)
                return;

            _lastValidateTime = now;
            _issues.Clear();
            _issues.AddRange(SpellValidator.Validate(_catalog));
            _issuesDirty = false;
        }

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
        }

        private void MarkDirty()
        {
            _unsaved = true;
            _issuesDirty = true;
            Repaint();
        }

        #endregion

        #region OnGUI

        private void OnGUI()
        {
            EnsureIssues();

            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.S &&
                (Event.current.control || Event.current.command))
            {
                SaveAll();
                Event.current.Use();
            }

            DrawToolbar();

            Rect body = new Rect(0f, ToolbarHeight, position.width, position.height - ToolbarHeight - StatusHeight);
            if (body.width < 400f || body.height < 200f)
            {
                DrawStatusBar();
                return;
            }

            float rightActual = _rightCollapsed ? 28f : _rightWidth;
            var leftRect = new Rect(body.x, body.y, _listWidth, body.height);
            var rightRect = new Rect(body.xMax - rightActual, body.y, rightActual, body.height);
            var middleRect = new Rect(leftRect.xMax, body.y, Mathf.Max(120f, rightRect.x - leftRect.xMax), body.height);

            GUILayout.BeginArea(leftRect);
            DrawLeftColumn();
            GUILayout.EndArea();

            float divider = SpellEditorUI.Splitter(new Rect(leftRect.xMax - 2f, body.y, 4f, body.height),
                _listWidth, 200f, Mathf.Max(220f, position.width - 560f));
            if (!Mathf.Approximately(divider, _listWidth))
            {
                _listWidth = divider;
                SpellEditorPrefs.ListWidth = _listWidth;
            }

            GUILayout.BeginArea(middleRect);
            DrawMiddleColumn();
            GUILayout.EndArea();

            GUILayout.BeginArea(rightRect);
            DrawRightColumn();
            GUILayout.EndArea();

            DrawStatusBar();
        }

        #endregion

        #region 工具栏

        private void DrawToolbar()
        {
            var rect = new Rect(0f, 0f, position.width, ToolbarHeight);
            EditorGUI.DrawRect(rect, new Color(0.22f, 0.23f, 0.25f, 1f));

            GUILayout.BeginArea(new Rect(6f, 2f, position.width - 12f, ToolbarHeight - 4f));
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("重扫扫描", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                Rescan(false);

            if (GUILayout.Button(_unsaved ? "保存 *（Ctrl+S）" : "保存（Ctrl+S）",
                    EditorStyles.toolbarButton, GUILayout.Width(120f)))
                SaveAll();

            GUILayout.Space(6f);

            int errors;
            int warnings;
            int infos;
            SpellValidator.Count(_issues, out errors, out warnings, out infos);

            if (GUILayout.Button("体检 " + errors + "/" + warnings + "/" + infos,
                    EditorStyles.toolbarButton, GUILayout.Width(110f)))
            {
                _issuesDirty = true;
                EnsureIssues();
                _rightTab = 0;
                SetStatus("体检：错误 " + errors + "、警告 " + warnings + "、提示 " + infos + "。",
                    errors > 0 ? MessageType.Error : warnings > 0 ? MessageType.Warning : MessageType.Info);
            }

            if (GUILayout.Button("一键修复", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                RunAutoFix();

            if (GUILayout.Button("备份 ▾", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                ShowBackupMenu();

            GUILayout.FlexibleSpace();

            if (GUILayout.Button(_rightCollapsed ? "◀ 面板" : "面板 ▶", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                _rightCollapsed = !_rightCollapsed;
                SpellEditorPrefs.RightCollapsed = _rightCollapsed;
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void SaveAll()
        {
            if (_sessionSo != null)
                _sessionSo.ApplyModifiedProperties();

            int saved = 0;
            for (int i = 0; i < _catalog.Records.Count; i++)
            {
                SpellRecord record = _catalog.Records[i];
                if (record == null || record.Definition == null)
                    continue;

                // 保存前备份：备份的是"当前磁盘上的旧内容"，也就是这一次改动的回退点
                if (EditorUtility.GetDirtyCount(record.Definition) > 0)
                {
                    SpellBackup.BackupAsset(record.AssetPath, "保存前");
                    EditorUtility.SetDirty(record.Definition);
                    saved++;
                }
            }

            // 伤害字段改在施放预制体上 → 那些预制体也要备份（备份的是磁盘上的旧内容）
            int prefabs = 0;
            foreach (string prefabPath in _dirtyPrefabs)
            {
                if (string.IsNullOrEmpty(prefabPath))
                    continue;
                SpellBackup.BackupAsset(prefabPath, "保存前");
                prefabs++;
            }
            _dirtyPrefabs.Clear();

            AssetDatabase.SaveAssets();
            _unsaved = false;
            SetStatus(saved > 0 || prefabs > 0
                    ? "已保存 " + saved + " 个法术资产" + (prefabs > 0 ? "、" + prefabs + " 个施放预制体" : string.Empty) +
                      "（备份已写入 " + SpellEditorPrefs.BackupFolder + "）。"
                    : "没有需要保存的改动。", MessageType.Info);
            _issuesDirty = true;
        }

        private void RunAutoFix()
        {
            _issuesDirty = true;
            EnsureIssues();

            int fixable = 0;
            for (int i = 0; i < _issues.Count; i++)
            {
                if (_issues[i].Fixable)
                    fixable++;
            }

            if (fixable == 0)
            {
                SetStatus("没有可自动修复的问题（一键修复只做确定性、数据层面的修复）。", MessageType.Info);
                return;
            }

            if (!EditorUtility.DisplayDialog("一键修复",
                    "将修复 " + fixable + " 处确定性问题：\n" +
                    "· 把没登记的法术补进注册表\n" +
                    "· 删掉注册表里重复的条目\n" +
                    "· 数字夹到字段约束范围\n" +
                    "· 键名大小写/分隔符校正（仅当注册表里唯一匹配）\n" +
                    "· deliveryType 与施法预制体的执行器同步\n" +
                    "· ID 为空时用资产名生成\n\n" +
                    "不会改已有 ID、不会删法术资产。修复前逐个备份被改动的资产。",
                    "开始修复", "取消"))
                return;

            SpellBackup.BackupAsset(SpellCatalog.RegistryAssetPath, "一键修复前");
            int fixedCount = SpellValidator.ApplySafeFixes(_catalog, _issues);
            Rescan(true);
            SetStatus("一键修复完成：" + fixedCount + " 处。", fixedCount > 0 ? MessageType.Info : MessageType.Warning);
        }

        private void ShowBackupMenu()
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("打开备份目录"), false, () =>
            {
                string folder = SpellEditorPrefs.BackupFolder;
                System.IO.Directory.CreateDirectory(folder);
                EditorUtility.RevealInFinder(folder);
            });
            menu.AddItem(new GUIContent("备份目录写到 Console"), false, () =>
                Debug.Log("[法术编辑器] 备份目录：" + SpellEditorPrefs.BackupFolder));
            menu.AddSeparator(string.Empty);

            List<string> backups = SpellBackup.ListBackups();
            if (backups.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("（还没有备份）"));
            }
            else
            {
                int shown = Mathf.Min(backups.Count, 15);
                for (int i = 0; i < shown; i++)
                {
                    string path = backups[i];
                    menu.AddItem(new GUIContent(System.IO.Path.GetFileName(path)), false,
                        () => EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path)));
                }
            }

            menu.ShowAsContext();
        }

        #endregion

        #region 左栏：法术列表

        private void DrawLeftColumn()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            EditorGUILayout.Space(4f);
            _search = SpellEditorUI.SearchField(_search, "搜索 ID / 名称 / 文件名", GUILayout.Height(20f));
            if (_search != SpellEditorPrefs.Search)
                SpellEditorPrefs.Search = _search;

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField(_catalog.Records.Count + " 个法术资产" +
                (_catalog.RegistryFound ? "（注册表 " + (_catalog.Registry != null && _catalog.Registry.SpellDefinitions != null
                    ? _catalog.Registry.SpellDefinitions.Count.ToString() : "?") + " 项）" : "（注册表缺失）"),
                SpellEditorUI.MiniLabel);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _catalog.Records.Count; i++)
            {
                SpellRecord record = _catalog.Records[i];
                if (!Matches(record))
                    continue;
                DrawRecordRow(record, i);
            }
            EditorGUILayout.EndScrollView();

            if (!_catalog.FolderFound && !string.IsNullOrEmpty(_catalog.ScanMessage))
                EditorGUILayout.HelpBox(_catalog.ScanMessage, MessageType.Error);

            EditorGUILayout.EndVertical();
        }

        private bool Matches(SpellRecord record)
        {
            if (string.IsNullOrEmpty(_search))
                return true;
            return Contains(record.Id, _search) || Contains(record.DisplayName, _search) || Contains(record.FileName, _search);
        }

        private static bool Contains(string source, string query)
        {
            return !string.IsNullOrEmpty(source) &&
                   source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void DrawRecordRow(SpellRecord record, int index)
        {
            bool selected = record == Selected;
            int errors = 0;
            int warnings = 0;
            for (int i = 0; i < _issues.Count; i++)
            {
                SpellIssue issue = _issues[i];
                if (issue.RecordIndex != index)
                    continue;
                if (issue.Level == SpellIssueLevel.Error) errors++;
                else if (issue.Level == SpellIssueLevel.Warning) warnings++;
            }

            Rect row = GUILayoutUtility.GetRect(0f, 34f, GUILayout.ExpandWidth(true));
            if (selected)
                EditorGUI.DrawRect(row, SpellEditorUI.Selected);
            else if ((index & 1) == 1)
                EditorGUI.DrawRect(row, SpellEditorUI.RowAlt);

            // 图标预览
            var iconRect = new Rect(row.x + 4f, row.y + 3f, 28f, 28f);
            Sprite icon = record.Definition != null ? SpellResourceIndex.Instance.GetSprite(record.Definition.IconKey) : null;
            if (icon != null)
                SpellEditorUI.DrawSpriteFit(iconRect, icon, Color.white, 1f);
            else
                SpellEditorUI.DrawEmptySlot(iconRect, SpellEditorUI.Muted);

            EditorGUI.LabelField(new Rect(row.x + 38f, row.y + 2f, row.width - 44f, 16f), record.Label, EditorStyles.boldLabel);

            string sub = (string.IsNullOrEmpty(record.Id) ? "（无 ID）" : record.Id) +
                         (record.Registered ? "　已登记" : "　未登记");
            Color subColor = record.Registered ? SpellEditorUI.Ok : SpellEditorUI.Error;
            var subStyle = new GUIStyle(SpellEditorUI.MiniLabel);
            subStyle.normal.textColor = subColor;
            EditorGUI.LabelField(new Rect(row.x + 38f, row.y + 17f, row.width - 44f, 15f), sub, subStyle);

            if (errors > 0 || warnings > 0)
            {
                string badge = (errors > 0 ? errors + " 错" : string.Empty) +
                               (errors > 0 && warnings > 0 ? " / " : string.Empty) +
                               (warnings > 0 ? warnings + " 警" : string.Empty);
                var badgeStyle = new GUIStyle(SpellEditorUI.MiniLabel);
                badgeStyle.normal.textColor = errors > 0 ? SpellEditorUI.Error : SpellEditorUI.Warn;
                badgeStyle.alignment = TextAnchor.UpperRight;
                EditorGUI.LabelField(new Rect(row.xMax - 76f, row.y + 2f, 72f, 16f), badge, badgeStyle);
            }

            if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
            {
                SelectRecord(record);
                Event.current.Use();
            }
        }

        #endregion

        #region 中栏：法术表单

        private void DrawMiddleColumn()
        {
            SpellRecord record = Selected;
            if (record == null || record.Definition == null)
            {
                EditorGUILayout.Space(12f);
                EditorGUILayout.HelpBox("左侧选一个法术开始编辑。" +
                                        (string.IsNullOrEmpty(_catalog.ScanMessage) ? string.Empty : "\n" + _catalog.ScanMessage),
                    MessageType.Info);
                return;
            }

            SerializedObject so = Session(record);
            if (so == null)
                return;

            _formScroll = EditorGUILayout.BeginScrollView(_formScroll, GUILayout.ExpandHeight(true));

            DrawHeader(record, so);

            SpellEditorUI.Section("身份", "ID 是存档与装备引用的依据；显示名只是界面文案");
            DrawIdentitySection(record, so);

            SpellEditorUI.Section("消耗", "施放一次的代价：扣多少金币、隔多久能再放一次");
            DrawCostSection(so, record.Definition);

            SpellEditorUI.Section("表现", "图标与施法预制体都走资源键（注册表里必须存在）");
            DrawPresentationSection(record, so);

            SpellEditorUI.Section("伤害与效果",
                "伤害不在 SpellDefinition 上，而在施放预制体的执行器组件上" +
                "（EngineerSpellDirectEffect / EngineerSpellProjectile / MagicEnergySing / 各类 Buff 实例）");
            DrawEffectSection(record);

            SpellEditorUI.Section("投递", "决定运行时找哪一种执行器组件");
            DrawDeliverySection(so);

            SpellEditorUI.Section("瞄准", "拖拽瞄准与「沿朝向施法」的相关参数");
            DrawAimSection(so);

            SpellEditorUI.Section("警示圈", "拖拽时显示的落点范围圈");
            DrawWarningSection(so);

            SpellEditorUI.Section("注册表", "法术的「上架」状态：没登记就等于游戏里不存在");
            DrawRegistrySection(record);

            EditorGUILayout.Space(12f);
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader(SpellRecord record, SerializedObject so)
        {
            EditorGUILayout.BeginHorizontal();

            var previewRect = GUILayoutUtility.GetRect(72f, 72f, GUILayout.Width(72f), GUILayout.Height(72f));
            EditorGUI.DrawRect(previewRect, SpellEditorUI.PreviewBg);
            Sprite icon = SpellResourceIndex.Instance.GetSprite(record.Definition.IconKey);
            if (icon != null)
                SpellEditorUI.DrawSpriteFit(previewRect, icon, Color.white, 4f);

            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(record.DisplayName, new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 });
            EditorGUILayout.LabelField(record.AssetPath, SpellEditorUI.MiniLabel);
            EditorGUILayout.LabelField(record.Registered ? "已登记进注册表" : "未登记（游戏里选不到）", SpellEditorUI.MiniLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("在 Project 里定位", EditorStyles.miniButton, GUILayout.Width(110f)))
                EditorGUIUtility.PingObject(record.Definition);
            if (GUILayout.Button("复制 ID", EditorStyles.miniButton, GUILayout.Width(70f)))
            {
                EditorGUIUtility.systemCopyBuffer = record.Id ?? string.Empty;
                SetStatus("已复制 ID：" + record.Id, MessageType.Info);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawIdentitySection(SpellRecord record, SerializedObject so)
        {
            StringRow(so, record.Definition, "id", "ID", "稳定 ID：LoadoutDefinitionRegistry 按它建字典，装备与存档也按它引用。" +
                                                     "改 ID 等于换了一个法术，旧引用会失效。");
            StringRow(so, record.Definition, "displayName", "显示名", "选装界面与法术栏显示的名字（空则退回资产名）。");

            if (!string.IsNullOrEmpty(record.Id) && _catalog.IsIdTaken(record.Id, record))
            {
                EditorGUILayout.HelpBox("这个 ID 与另一个法术重复：注册表里后登记的那条会被忽略" +
                                        "（LoadoutDefinitionRegistry.cs:143-147），工具不会替你改 ID。", MessageType.Error);
            }
        }

        /// <summary>
        /// 「消耗」分组：施放一次扣多少金币、隔多久能再放。
        ///
        /// 金币：SpellDefinition.goldCost（[Min(0)]，SpellDefinition.cs:24-26，注释写明「按钮右下角角标实时显示该值」）。
        ///   · 0 = 免费；
        ///   · 实际扣费在 GameplaySpellBar：按下时先 CanAfford 判断、再 ChargeGold 扣（GameplaySpellBar.cs:384-403）；
        ///   · 这里把输入夹在 ≥ 0 —— 负值会让扣费逻辑倒着走（体检也会报）。
        /// 冷却：SpellDefinition.cooldown（[Min(0)]，SpellDefinition.cs:23），两次施放之间的最短间隔。
        /// </summary>
        private void DrawCostSection(SerializedObject so, SpellDefinition definition)
        {
            IntRow(so, definition, "goldCost", "施法消耗（金币）",
                "每次施放扣除的金币；0 = 免费。法术栏按钮右下角的角标显示的就是这个值。" +
                "按下时的扣费在 GameplaySpellBar.CanAfford / ChargeGold（GameplaySpellBar.cs:384-403）。",
                0, 999999);

            FloatRow(so, definition, "cooldown", "冷却（秒）",
                "两次施放之间的最短间隔；0 = 不限制（SpellDefinition.cooldown，[Min(0)]，SpellDefinition.cs:23）。",
                0f, 9999f);

            // 直观参考：给策划看一眼"这一发有多贵"的比例关系
            int cost = definition.GoldCost;
            float cooldown = definition.Cooldown;
            if (cost > 0 && cooldown > 0f)
            {
                float perMinute = 60f / Mathf.Max(0.01f, cooldown) * cost;
                EditorGUILayout.LabelField("参考：满冷却连放约 " + perMinute.ToString("0") + " 金币/分钟（" +
                                           (60f / Mathf.Max(0.01f, cooldown)).ToString("0.#") + " 次/分钟 × " + cost + "）。",
                    SpellEditorUI.MiniLabel);
            }
            else if (cost <= 0)
            {
                EditorGUILayout.LabelField("免费法术：不扣金币，只受冷却限制。", SpellEditorUI.MiniLabel);
            }
        }

        private void DrawPresentationSection(SpellRecord record, SerializedObject so)
        {
            KeyRow(record, "iconKey", SpellResKind.Sprite, "图标键", "法术栏按钮与选装界面的图标（SpriteRegistry）。");
            KeyRow(record, "castPrefabKey", SpellResKind.Prefab, "施法预制体键", "运行时用 GameObjectPool 实例化它，" +
                                                                              "并在根节点上找执行器接口（PrefabRegistry）。");

            // 执行器状态：这是最容易"配了却没效果"的一环
            string castKey = record.Definition.CastPrefabKey;
            GameObject prefab = string.IsNullOrEmpty(castKey) ? null : SpellResourceIndex.Instance.GetPrefab(castKey);
            if (prefab == null)
            {
                // 三种情况要分开说 —— 以前一律说成「不在 PrefabRegistry」，是误报（实测踩过：
                // Dancing statue Magic 明明在注册表里，却因为工具读不到资产引用而被这样报出来）
                string reason;
                if (string.IsNullOrEmpty(castKey))
                    reason = "施法预制体键为空：EngineerSpellCaster.Cast 会直接 return false（法术点了没反应）。";
                else if (!SpellResourceIndex.Instance.Has(SpellResKind.Prefab, castKey))
                    reason = "键「" + castKey + "」不在 PrefabRegistry 里：运行时会 LogError「法术预制体未预加载」并放弃施法。";
                else
                    reason = "键「" + castKey + "」在 PrefabRegistry 里，但**它指向的预制体资产取不到**" +
                             "（资产的引用是空的，或者资产已被删除/移出工程）。运行时会 LogError「法术预制体未预加载」。";
                EditorGUILayout.HelpBox(reason.Replace("**", string.Empty), MessageType.Error);
            }
            else
            {
                bool hasDirect = prefab.GetComponent(typeof(IEngineerDirectSpellInstance)) != null;
                bool hasAimed = prefab.GetComponent(typeof(IEngineerAimedSpellInstance)) != null;
                bool hasPlain = prefab.GetComponent(typeof(IEngineerSpellInstance)) != null;
                string found = (hasDirect ? "EngineerSpellDirectEffect（原地） " : string.Empty) +
                               (hasAimed ? "瞄准执行器 " : string.Empty) +
                               (hasPlain ? "普通执行器" : string.Empty);
                EditorGUILayout.LabelField("预制体上的执行器：" +
                    (string.IsNullOrEmpty(found) ? "（一个都没有）" : found.Trim()),
                    SpellEditorUI.MiniLabel);
            }
        }

        /// <summary>
        /// 「伤害与效果」分组：读施放预制体上的执行器组件，按白名单列出伤害/效果字段就地编辑。
        ///
        /// 为什么写在这里：`SpellDefinition` 一个伤害字段都没有（18 个字段全是 id / 图标 / 冷却 /
        /// 金币 / 施放预制体键 / 投递 / 弹道 / 警示圈），伤害写在**施放预制体的执行器组件**上。
        /// 改这里 = 改预制体资产 → **所有用到这个法术的关卡一起变**（不是关卡局部数据）。
        /// 保存时会先把预制体的旧内容备份到 SpellBackups。
        /// </summary>
        private void DrawEffectSection(SpellRecord record)
        {
            if (_effectRecord != record || _effectReport == null)
            {
                _effectRecord = record;
                _effectReport = SpellEffects.Read(record.Definition);
            }

            SpellEffectReport report = _effectReport;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("摘要", GUILayout.Width(52f));
            EditorGUILayout.LabelField(report.Summary, EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(report.Prefab == null))
            {
                if (GUILayout.Button("打开施放预制体", EditorStyles.miniButton, GUILayout.Width(104f)))
                    AssetDatabase.OpenAsset(report.Prefab);
                if (GUILayout.Button("在 Project 里定位", EditorStyles.miniButton, GUILayout.Width(104f)))
                    EditorGUIUtility.PingObject(report.Prefab);
            }
            _showAllPrefabFields = GUILayout.Toggle(_showAllPrefabFields, "显示预制体全部字段",
                EditorStyles.miniButton, GUILayout.Width(130f));
            if (!string.IsNullOrEmpty(report.PrefabPath))
                EditorGUILayout.LabelField(report.PrefabPath, SpellEditorUI.MiniLabel);
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(report.Note))
                EditorGUILayout.HelpBox(report.Note, report.HasCastPrefab ? MessageType.Info : MessageType.Warning);

            // 共享施放预制体：改这个预制体的伤害会一起改到别的法术 —— 这条最容易漏，必须显式提示。
            // 实测：Bababoyi / Broom cat / Kende / Omniscient magic 四个法术共用预制体 B-39。
            var sharing = new List<string>();
            for (int i = 0; i < _catalog.Records.Count; i++)
            {
                SpellRecord other = _catalog.Records[i];
                if (other == null || other == record || other.Definition == null)
                    continue;
                if (string.Equals(other.Definition.CastPrefabKey, record.Definition.CastPrefabKey, StringComparison.Ordinal))
                    sharing.Add(other.Label);
            }

            if (sharing.Count > 0)
            {
                EditorGUILayout.HelpBox(
                    "这个施放预制体被 " + (sharing.Count + 1) + " 个法术共用：" + string.Join("、", sharing.ToArray()) +
                    "\n改这里的伤害会一起改到它们。要单独调某一个，得先给它换一个独立预制体。",
                    MessageType.Warning);
            }

            if (!report.HasCastPrefab)
                return;

            for (int c = 0; c < report.Components.Count; c++)
            {
                SpellEffectComponent component = report.Components[c];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(component.TypeName, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("定位组件", EditorStyles.miniButton, GUILayout.Width(62f)))
                    EditorGUIUtility.PingObject(component.Component);
                EditorGUILayout.EndHorizontal();

                var serialized = new SerializedObject(component.Component);
                bool changed = false;

                for (int f = 0; f < component.Fields.Count; f++)
                {
                    SpellEffectField field = component.Fields[f];
                    SerializedProperty property = serialized.FindProperty(field.PropertyPath);
                    if (property == null)
                        continue;

                    if (property.propertyType == SerializedPropertyType.Integer)
                    {
                        EditorGUI.BeginChangeCheck();
                        int value = EditorGUILayout.IntField(new GUIContent(field.Label, field.Tooltip), property.intValue);
                        if (EditorGUI.EndChangeCheck())
                        {
                            if (!float.IsNegativeInfinity(field.MinValue))
                                value = Mathf.Max((int)field.MinValue, value);
                            property.intValue = value;
                            changed = true;
                        }
                    }
                    else if (property.propertyType == SerializedPropertyType.Float)
                    {
                        EditorGUI.BeginChangeCheck();
                        float value = EditorGUILayout.FloatField(new GUIContent(field.Label, field.Tooltip), property.floatValue);
                        if (EditorGUI.EndChangeCheck())
                        {
                            property.floatValue = Mathf.Clamp(value, field.MinValue, float.MaxValue);
                            changed = true;
                        }
                    }
                    else if (property.propertyType == SerializedPropertyType.Boolean)
                    {
                        EditorGUI.BeginChangeCheck();
                        bool value = EditorGUILayout.Toggle(new GUIContent(field.Label, field.Tooltip), property.boolValue);
                        if (EditorGUI.EndChangeCheck())
                        {
                            property.boolValue = value;
                            changed = true;
                        }
                    }
                    else if (property.propertyType == SerializedPropertyType.Enum)
                    {
                        // DamageType 的枚举名是 normal / magic，显示成中文更好读；其它枚举照原名
                        string[] names = field.PropertyPath == "damageType"
                            ? new[] { "物理（normal）", "魔法（magic）" }
                            : property.enumDisplayNames;
                        int current = Mathf.Clamp(property.enumValueIndex, 0, Mathf.Max(0, names.Length - 1));
                        EditorGUI.BeginChangeCheck();
                        int picked = EditorGUILayout.Popup(new GUIContent(field.Label, field.Tooltip), current, names);
                        if (EditorGUI.EndChangeCheck())
                        {
                            property.enumValueIndex = picked;
                            changed = true;
                        }
                    }
                }

                // 引用的 Buff 实例：范围法术真正的效果参数在这些资产上
                for (int b = 0; b < component.BuffPaths.Count; b++)
                {
                    SerializedProperty property = serialized.FindProperty(component.BuffPaths[b]);
                    if (property == null)
                        continue;

                    EditorGUILayout.BeginHorizontal();
                    EditorGUI.BeginChangeCheck();
                    UnityEngine.Object value = EditorGUILayout.ObjectField(
                        new GUIContent("施加的状态", "这个组件会施加的 Buff 实例（字段 " + component.BuffPaths[b] + "）；效果数值在那个资产上"),
                        property.objectReferenceValue, typeof(BuffBase), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        property.objectReferenceValue = value;
                        changed = true;
                    }
                    using (new EditorGUI.DisabledScope(property.objectReferenceValue == null))
                    {
                        if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(42f)))
                            EditorGUIUtility.PingObject(property.objectReferenceValue);
                    }
                    EditorGUILayout.EndHorizontal();
                }

                // 「显示预制体全部字段」：除伤害白名单外，还能就地改组件上的其它直观字段
                if (_showAllPrefabFields)
                {
                    EditorGUILayout.LabelField("组件全部字段（数组与嵌套结构请在预制体 Inspector 里改）",
                        SpellEditorUI.MiniLabel);
                    if (DrawRawComponentFields(serialized))
                        changed = true;
                }

                if (changed)
                {
                    Undo.RecordObject(component.Component, "修改法术伤害");
                    serialized.ApplyModifiedProperties();
                    EditorUtility.SetDirty(component.Component);
                    _dirtyPrefabs.Add(report.PrefabPath);
                    _effectReport = SpellEffects.Read(record.Definition);   // 数值变了，摘要重算
                    MarkDirty();
                }

                EditorGUILayout.EndVertical();
            }

            if (report.Components.Count > 0)
            {
                EditorGUILayout.LabelField("改的是「施放预制体资产」，不是关卡局部数据 —— 所有用到这个法术的地方都会一起变。" +
                                           "Ctrl+S 保存前会把该预制体的旧内容备份到 " + SpellEditorPrefs.BackupFolder + "。",
                    SpellEditorUI.MiniLabel);
            }
        }

        /// <summary>
        /// 「显示预制体全部字段」用的通用绘制：按序列化类型给编辑器，只处理**直观字段**
        /// （数字 / 开关 / 文本 / 枚举 / 向量 / 颜色 / 对象引用）；
        /// 数组与嵌套结构（Generic）跳过 —— 那些在预制体 Inspector 里改更安全，也避免把这一屏塞满。
        /// 返回是否有改动。
        /// </summary>
        private static bool DrawRawComponentFields(SerializedObject serialized)
        {
            SerializedProperty iterator = serialized.GetIterator();
            bool enterChildren = true;
            bool changed = false;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.propertyPath == "m_Script" || iterator.isArray)
                    continue;

                EditorGUI.BeginChangeCheck();
                switch (iterator.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        iterator.intValue = EditorGUILayout.IntField(iterator.displayName, iterator.intValue);
                        break;
                    case SerializedPropertyType.Float:
                        iterator.floatValue = EditorGUILayout.FloatField(iterator.displayName, iterator.floatValue);
                        break;
                    case SerializedPropertyType.Boolean:
                        iterator.boolValue = EditorGUILayout.Toggle(iterator.displayName, iterator.boolValue);
                        break;
                    case SerializedPropertyType.String:
                        iterator.stringValue = EditorGUILayout.TextField(iterator.displayName, iterator.stringValue);
                        break;
                    case SerializedPropertyType.Enum:
                        {
                            string[] names = iterator.enumDisplayNames;
                            int current = Mathf.Clamp(iterator.enumValueIndex, 0, Mathf.Max(0, names.Length - 1));
                            iterator.enumValueIndex = EditorGUILayout.Popup(iterator.displayName, current, names);
                            break;
                        }
                    case SerializedPropertyType.Vector2:
                        iterator.vector2Value = EditorGUILayout.Vector2Field(iterator.displayName, iterator.vector2Value);
                        break;
                    case SerializedPropertyType.Vector3:
                        iterator.vector3Value = EditorGUILayout.Vector3Field(iterator.displayName, iterator.vector3Value);
                        break;
                    case SerializedPropertyType.Color:
                        iterator.colorValue = EditorGUILayout.ColorField(iterator.displayName, iterator.colorValue);
                        break;
                    case SerializedPropertyType.ObjectReference:
                        iterator.objectReferenceValue = EditorGUILayout.ObjectField(iterator.displayName,
                            iterator.objectReferenceValue, typeof(UnityEngine.Object), false);
                        break;
                    default:
                        continue;
                }

                if (EditorGUI.EndChangeCheck())
                    changed = true;
            }

            return changed;
        }

        private void DrawDeliverySection(SerializedObject so)
        {
            EnumRow(so, null, "deliveryType", "投递方式",
                new[] { "Projectile（弹道/瞄准）", "DirectSpawn（原地生成）" },
                "DirectSpawn 需要预制体上是 IEngineerDirectSpellInstance；Projectile 找 IEngineerAimedSpellInstance " +
                "或 IEngineerSpellInstance（EngineerSpellCaster.cs:136-169）。");
            FloatRow(so, null, "directCastTime", "原地前摇（秒）", "仅 DirectSpawn 用：先播「Cast spell」再等这段时间" +
                                                                   "（EngineerSpellCaster.cs:84-117）。");
            EnumRow(so, null, "releaseMode", "释放方式", new[] { "Custom（脚本自己决定）", "FromEngineer（从工程兵发出）" },
                "FromEngineer 时按施法者朝向旋转，并加上 spawnOffset（EngineerSpellCaster.cs:62-65、131-134）。");
            Vector3Row(so, null, "spawnOffset", "生成偏移", "只在 FromEngineer + Projectile 时叠加到施法点。");
            BoolRow(so, null, "faceEngineerDirection", "跟随施法者朝向", "只在 releaseMode = FromEngineer 时生效。");
        }

        private void DrawAimSection(SerializedObject so)
        {
            BoolRow(so, null, "dragAim", "拖拽瞄准", "关掉就只能朝施法者朝向施法（落点距离 = 最远距离）。");
            FloatRow(so, null, "maxDistance", "最远距离（格）", "沿朝向施法时的落点距离（EngineerSpellCaster.cs:28-29）。");
            FloatRow(so, null, "arcHeight", "抛物线高度", "仅 Projectile 使用。");
            FloatRow(so, null, "flightTime", "飞行时间（秒）", "仅 Projectile 使用。");
        }

        private void DrawWarningSection(SerializedObject so)
        {
            BoolRow(so, null, "showWarningCircle", "显示警示圈", "开启后运行时会预载圈贴图（PlayerLoadoutManager.cs:207-208）。");
            KeyRow(null, "warningCircleKey", SpellResKind.Sprite, "警示圈贴图键", "同上，SpriteRegistry 里的键。", so);
            FloatRow(so, null, "warningCircleScale", "警示圈缩放", "≤ 0 会把圈缩成看不见。");
        }

        private void DrawRegistrySection(SpellRecord record)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("状态", GUILayout.Width(70f));
            if (record.Registered)
            {
                GUI.contentColor = SpellEditorUI.Ok;
                EditorGUILayout.LabelField("已登记（第 " + record.RegistryIndex + " 项）");
            }
            else
            {
                GUI.contentColor = SpellEditorUI.Error;
                EditorGUILayout.LabelField("未登记 —— 游戏里选不到这个法术");
            }
            GUI.contentColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(record.Registered))
            {
                if (GUILayout.Button("登记进注册表", GUILayout.Height(22f)))
                {
                    string message;
                    if (_catalog.Register(record, out message))
                    {
                        Rescan(true);
                        SetStatus(message, MessageType.Info);
                    }
                    else
                    {
                        SetStatus(message, MessageType.Warning);
                    }
                }
            }

            if (GUILayout.Button("定位注册表资产", EditorStyles.miniButton, GUILayout.Width(110f)))
            {
                LoadoutDefinitionRegistry registry = _catalog.Registry;
                if (registry != null)
                    EditorGUIUtility.PingObject(registry);
            }
            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region 右栏：体检 / 设置

        private void DrawRightColumn()
        {
            if (_rightCollapsed)
            {
                if (GUILayout.Button("◀", EditorStyles.miniButton, GUILayout.Width(24f)))
                {
                    _rightCollapsed = false;
                    SpellEditorPrefs.RightCollapsed = false;
                }
                return;
            }

            EditorGUILayout.BeginVertical();
            _rightTab = GUILayout.Toolbar(_rightTab, new[] { "体检", "设置" }, EditorStyles.toolbarButton);
            SpellEditorPrefs.RightTab = _rightTab;

            if (_rightTab == 0)
                DrawIssuesTab();
            else
                DrawSettingsTab();

            EditorGUILayout.EndVertical();
        }

        private void DrawIssuesTab()
        {
            int errors;
            int warnings;
            int infos;
            SpellValidator.Count(_issues, out errors, out warnings, out infos);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("错误 " + errors, SpellEditorUI.MiniLabel, GUILayout.Width(64f));
            EditorGUILayout.LabelField("警告 " + warnings, SpellEditorUI.MiniLabel, GUILayout.Width(64f));
            EditorGUILayout.LabelField("提示 " + infos, SpellEditorUI.MiniLabel, GUILayout.Width(64f));
            GUILayout.FlexibleSpace();
            _onlyShowSelectedIssues = GUILayout.Toggle(_onlyShowSelectedIssues, "只看当前法术", EditorStyles.miniButton,
                GUILayout.Width(96f));
            EditorGUILayout.EndHorizontal();

            SpellRecord selected = Selected;
            int selectedIndex = -1;
            for (int i = 0; i < _catalog.Records.Count; i++)
            {
                if (_catalog.Records[i] == selected)
                {
                    selectedIndex = i;
                    break;
                }
            }

            _issueScroll = EditorGUILayout.BeginScrollView(_issueScroll, GUILayout.ExpandHeight(true));
            bool any = false;
            for (int i = 0; i < _issues.Count; i++)
            {
                SpellIssue issue = _issues[i];
                if (_onlyShowSelectedIssues && issue.RecordIndex != selectedIndex)
                    continue;

                any = true;
                Color color = issue.Level == SpellIssueLevel.Error ? SpellEditorUI.Error
                    : issue.Level == SpellIssueLevel.Warning ? SpellEditorUI.Warn : SpellEditorUI.Muted;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                GUI.contentColor = color;
                EditorGUILayout.LabelField(issue.Level == SpellIssueLevel.Error ? "错误"
                    : issue.Level == SpellIssueLevel.Warning ? "警告" : "提示", EditorStyles.boldLabel, GUILayout.Width(40f));
                GUI.contentColor = Color.white;
                EditorGUILayout.LabelField(issue.RecordLabel, SpellEditorUI.MiniLabel, GUILayout.Width(120f));
                GUILayout.FlexibleSpace();
                if (issue.RecordIndex >= 0 &&
                    GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(40f)))
                {
                    if (issue.RecordIndex < _catalog.Records.Count)
                        SelectRecord(_catalog.Records[issue.RecordIndex]);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField(issue.Title, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(issue.Detail, SpellEditorUI.RichLabel);

                if (issue.Fixable && !string.IsNullOrEmpty(issue.SuggestedValue))
                    EditorGUILayout.LabelField("可一键修复：" + issue.SuggestedValue, SpellEditorUI.MiniLabel);

                EditorGUILayout.EndVertical();
            }

            if (!any)
                EditorGUILayout.HelpBox("没有发现问题。", MessageType.Info);

            EditorGUILayout.EndScrollView();
        }

        private void DrawSettingsTab()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("备份", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("备份目录", GUILayout.Width(70f));
            string folder = EditorGUILayout.TextField(SpellEditorPrefs.BackupFolder);
            if (folder != SpellEditorPrefs.BackupFolder)
                SpellEditorPrefs.BackupFolder = folder;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("每个资产保留", GUILayout.Width(90f));
            int keep = EditorGUILayout.IntField(SpellEditorPrefs.BackupKeep, GUILayout.Width(50f));
            if (keep != SpellEditorPrefs.BackupKeep)
                SpellEditorPrefs.BackupKeep = keep;
            EditorGUILayout.LabelField("份", GUILayout.Width(20f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("淘汰只按「同名资产」分组，且只删文件名符合 " +
                                       "资产名_yyyyMMdd_HHmmss_原因 的备份 —— 真实资产不会被误删。", SpellEditorUI.MiniLabel);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("路径", EditorStyles.boldLabel);
            SpellEditorUI.KeyValue("法术目录", SpellCatalog.SpellFolder);
            SpellEditorUI.KeyValue("注册表", SpellCatalog.RegistryAssetPath);
            SpellEditorUI.KeyValue("备份", SpellEditorPrefs.BackupFolder);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("快捷键", EditorStyles.boldLabel);
            SpellEditorUI.KeyValue("保存", "Ctrl+S");
            SpellEditorUI.KeyValue("打开窗口", "Ctrl+Alt+S（工具菜单里也有）");

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("注册表索引", EditorStyles.boldLabel, GUILayout.Width(120f));
            EditorGUILayout.LabelField("Sprite " + SpellResourceIndex.Instance.KeyCount(SpellResKind.Sprite) +
                                       " 键 / Prefab " + SpellResourceIndex.Instance.KeyCount(SpellResKind.Prefab) + " 键",
                SpellEditorUI.MiniLabel);

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("重新扫描工程", GUILayout.Height(22f)))
                Rescan(false);
        }

        private void DrawStatusBar()
        {
            var rect = new Rect(0f, position.height - StatusHeight, position.width, StatusHeight);
            EditorGUI.DrawRect(rect, new Color(0.19f, 0.20f, 0.22f, 1f));

            Color color = _statusType == MessageType.Error ? SpellEditorUI.Error
                : _statusType == MessageType.Warning ? SpellEditorUI.Warn
                : _statusType == MessageType.Info ? SpellEditorUI.Ok : SpellEditorUI.Muted;

            var style = new GUIStyle(EditorStyles.miniLabel);
            style.normal.textColor = color;
            EditorGUI.LabelField(new Rect(8f, rect.y + 3f, position.width - 16f, StatusHeight - 6f),
                string.IsNullOrEmpty(_status) ? "就绪" : _status, style);
        }

        #endregion

        #region 字段控件

        private void Apply(SerializedObject so, UnityEngine.Object target)
        {
            if (so != null)
                so.ApplyModifiedProperties();
            if (target != null)
                EditorUtility.SetDirty(target);
            MarkDirty();
        }

        private void StringRow(SerializedObject so, UnityEngine.Object target, string field, string label, string tooltip)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
            {
                EditorGUILayout.LabelField(label, "（字段缺失：" + field + "）");
                return;
            }

            EditorGUI.BeginChangeCheck();
            string value = EditorGUILayout.TextField(new GUIContent(label, tooltip), property.stringValue);
            if (EditorGUI.EndChangeCheck())
            {
                property.stringValue = value;
                Apply(so, target);
            }
        }

        private void BoolRow(SerializedObject so, UnityEngine.Object target, string field, string label, string tooltip)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
                return;

            EditorGUI.BeginChangeCheck();
            bool value = EditorGUILayout.Toggle(new GUIContent(label, tooltip), property.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                property.boolValue = value;
                Apply(so, target);
            }
        }

        private void FloatRow(SerializedObject so, UnityEngine.Object target, string field, string label, string tooltip,
                              float min = float.NegativeInfinity, float max = float.PositiveInfinity)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
                return;

            EditorGUI.BeginChangeCheck();
            float value = EditorGUILayout.FloatField(new GUIContent(label, tooltip), property.floatValue);
            if (EditorGUI.EndChangeCheck())
            {
                property.floatValue = Mathf.Clamp(value, min, max);
                Apply(so, target);
            }
        }

        private void IntRow(SerializedObject so, UnityEngine.Object target, string field, string label, string tooltip,
                            int min = int.MinValue, int max = int.MaxValue)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
                return;

            EditorGUI.BeginChangeCheck();
            int value = EditorGUILayout.IntField(new GUIContent(label, tooltip), property.intValue);
            if (EditorGUI.EndChangeCheck())
            {
                property.intValue = Mathf.Clamp(value, min, max);
                Apply(so, target);
            }
        }

        private void Vector3Row(SerializedObject so, UnityEngine.Object target, string field, string label, string tooltip)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
                return;

            EditorGUI.BeginChangeCheck();
            Vector3 value = EditorGUILayout.Vector3Field(new GUIContent(label, tooltip), property.vector3Value);
            if (EditorGUI.EndChangeCheck())
            {
                property.vector3Value = value;
                Apply(so, target);
            }
        }

        private void EnumRow(SerializedObject so, UnityEngine.Object target, string field, string label, string[] names, string tooltip)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
                return;

            int current = Mathf.Clamp(property.enumValueIndex, 0, names.Length - 1);
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup(new GUIContent(label, tooltip), current, names);
            if (EditorGUI.EndChangeCheck())
            {
                property.enumValueIndex = picked;
                Apply(so, target);
            }
        }

        /// <summary>
        /// 资源键一行：显示键 + 预览 + [选] + [清]。
        /// 键的"合法性"直接读注册表（SpellResourceIndex），点「选」打开通用键选择器。
        /// </summary>
        private void KeyRow(SpellRecord record, string field, SpellResKind kind, string label, string tooltip,
                            SerializedObject shared = null)
        {
            SerializedObject so = shared ?? Session(record);
            UnityEngine.Object target = shared != null ? shared.targetObject : (record != null ? record.Definition : null);
            if (so == null)
                return;

            SerializedProperty property = so.FindProperty(field);
            if (property == null)
                return;

            string key = property.stringValue;
            bool inRegistry = string.IsNullOrEmpty(key) || SpellResourceIndex.Instance.Has(kind, key);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(new GUIContent(label, tooltip), GUILayout.Width(110f));

            EditorGUI.BeginChangeCheck();
            string edited = EditorGUILayout.TextField(key);
            if (EditorGUI.EndChangeCheck())
            {
                property.stringValue = edited;
                Apply(so, target);
                key = edited;
                inRegistry = string.IsNullOrEmpty(key) || SpellResourceIndex.Instance.Has(kind, key);
            }

            // 预览
            var thumbRect = GUILayoutUtility.GetRect(22f, 18f, GUILayout.Width(22f), GUILayout.Height(18f));
            if (kind == SpellResKind.Sprite)
            {
                Sprite sprite = SpellResourceIndex.Instance.GetSprite(key);
                if (sprite != null)
                    SpellEditorUI.DrawSpriteFit(thumbRect, sprite, Color.white, 1f);
                else
                    SpellEditorUI.DrawEmptySlot(thumbRect, SpellEditorUI.Muted);
            }

            if (GUILayout.Button("选", EditorStyles.miniButton, GUILayout.Width(26f)))
            {
                SpellKeyPickerWindow.Open(label, target != null ? target.name : string.Empty, kind,
                    picked =>
                    {
                        var targetSo = new SerializedObject(target);
                        SerializedProperty p = targetSo.FindProperty(field);
                        if (p == null)
                            return;
                        p.stringValue = picked;
                        targetSo.ApplyModifiedProperties();
                        EditorUtility.SetDirty(target);
                        MarkDirty();
                    },
                    () =>
                    {
                        var targetSo = new SerializedObject(target);
                        SerializedProperty p = targetSo.FindProperty(field);
                        if (p == null)
                            return;
                        p.stringValue = string.Empty;
                        targetSo.ApplyModifiedProperties();
                        EditorUtility.SetDirty(target);
                        MarkDirty();
                    },
                    BuildUsedKeys(kind, field),
                    key);
            }

            if (GUILayout.Button("清", EditorStyles.miniButton, GUILayout.Width(26f)) && !string.IsNullOrEmpty(key))
            {
                property.stringValue = string.Empty;
                Apply(so, target);
            }

            EditorGUILayout.EndHorizontal();

            if (!inRegistry)
            {
                GUI.contentColor = SpellEditorUI.Error;
                EditorGUILayout.LabelField("　　" + key + " 不在 " + SpellResourceIndex.KindName(kind) +
                                           " 注册表里（运行时解析不出来）", SpellEditorUI.MiniLabel);
                GUI.contentColor = Color.white;
            }
        }

        /// <summary>同一类键在别的法术里已经被用过的集合（键选择器里标「已被使用」）。</summary>
        private HashSet<string> BuildUsedKeys(SpellResKind kind, string exceptField)
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            string[] fields = { "iconKey", "castPrefabKey", "warningCircleKey" };

            for (int i = 0; i < _catalog.Records.Count; i++)
            {
                SpellRecord record = _catalog.Records[i];
                if (record == null || record.Definition == null)
                    continue;

                for (int f = 0; f < fields.Length; f++)
                {
                    string field = fields[f];
                    if (field == exceptField && record == Selected)
                        continue;

                    var so = new SerializedObject(record.Definition);
                    SerializedProperty property = so.FindProperty(field);
                    if (property == null || string.IsNullOrEmpty(property.stringValue))
                        continue;

                    SpellResKind fieldKind = field == "castPrefabKey" ? SpellResKind.Prefab : SpellResKind.Sprite;
                    if (fieldKind == kind)
                        used.Add(property.stringValue);
                }
            }

            return used;
        }

        #endregion
    }
}
