using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightUpgradeTool
{
    /// <summary>
    /// 等级编辑器（Upgrade Visual Editor）—— 直接编辑**玩家全局存档**里的成长数据。
    ///
    /// 管的东西（全部在一份 JSON 里，工程之外）：
    ///   Application.persistentDataPath/SaveData/user_global_info.json
    ///   · 三个建筑等级：兵营 / 黑暗兵营 / 哨塔（≥1）—— 决定局内建筑的缩放与"总等级"
    ///   · 两个魔法等级：攻击 / 防御（≥0）
    ///   · 货币：金条 / 钻石
    ///   · 出战选择：工程师 / 种族 / 两个法术槽 / 大本营预制体键
    ///   · 图鉴解锁与拥有、两种模式的通关进度、音量与伤害显示
    ///
    /// 设计原则与另外五个编辑器一致：
    ///   · **只读地**引用运行时类型（UserGlobalInfoData / EngineerDefinition / RaceDefinition /
    ///     SpellDefinition / BookCatalog / StageConfig），游戏源码一行不改，工程资产一个不写；
    ///   · 写盘只写这一份存档 JSON，写前**自动备份**，并且**Unity 正在 Play 时禁止写**（写了没意义）；
    ///   · 界面上每个「数值 → 结论」的判断都能追到运行时代码出处（体检里逐条写明）；
    ///   · 工具源码放在工程之外，靠目录联接挂进来（tools\junction.ps1）。
    /// </summary>
    public sealed class UpgradeEditorWindow : EditorWindow
    {
        private const string MenuPath = "工具/等级可视化编辑器 %&p";
        private const string WindowTitle = "等级编辑器";
        private const float ToolbarHeight = 24f;
        private const float StatusHeight = 22f;

        private UserGlobalInfoData _data;
        private string _loadedJson;
        private string _loadError;
        private bool _fileExists;
        private bool _dirty;

        private readonly List<UpgradeIssue> _issues = new List<UpgradeIssue>();
        private bool _issuesDirty = true;

        private float _leftWidth = 340f;
        private bool _leftCollapsed;
        private int _tab;

        private Vector2 _leftScroll;
        private Vector2 _contentScroll;
        private Vector2 _backupScroll;

        private string _status = string.Empty;
        private MessageType _statusType = MessageType.None;

        private List<SaveBackupInfo> _backups;
        private string _compareBackupPath;
        private List<SaveFieldRow> _compareRows;
        private string _compareInfo = string.Empty;

        private string _bookSearch = string.Empty;
        private bool _showUnknownBookIds;
        private bool _prettyPrint;

        private int _quickLevel = 1;
        private int _quickGold = 100000;
        private int _quickDiamond = 100000;
        private int _quickClearTo = 20;

        #region 生命周期
        [MenuItem(MenuPath, false, 100)]
        public static void OpenWindow()
        {
            var window = GetWindow<UpgradeEditorWindow>(false, WindowTitle, true);
            window.minSize = new Vector2(980f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            _leftWidth = UpgradeEditorPrefs.LeftWidth;
            _leftCollapsed = UpgradeEditorPrefs.LeftCollapsed;
            _tab = Mathf.Clamp(UpgradeEditorPrefs.RightTab, 0, UpgradeTab.All.Length - 1);
            _bookSearch = UpgradeEditorPrefs.BookSearch;
            _showUnknownBookIds = UpgradeEditorPrefs.ShowUnknownBookIds;
            _prettyPrint = UpgradeEditorPrefs.PrettyPrint;
            titleContent = new GUIContent(WindowTitle);
            Reload();
        }

        private void OnDisable()
        {
            UpgradeEditorPrefs.LeftWidth = _leftWidth;
            UpgradeEditorPrefs.LeftCollapsed = _leftCollapsed;
            UpgradeEditorPrefs.RightTab = _tab;
            UpgradeEditorPrefs.BookSearch = _bookSearch;
            UpgradeEditorPrefs.ShowUnknownBookIds = _showUnknownBookIds;
            UpgradeEditorPrefs.PrettyPrint = _prettyPrint;
        }

        private void OnFocus()
        {
            // 工程资产可能被别的窗口改动（导入/删资产）→ 下拉候选重扫一次。
            UpgradeOptions.Invalidate();
            _backups = null;
        }
        #endregion

        #region 读写
        private void Reload()
        {
            UserGlobalInfoData data;
            string rawJson;
            string error;
            bool ok = UpgradeSave.TryLoad(out data, out rawJson, out error);

            _data = data;
            _loadedJson = rawJson;
            _loadError = ok ? null : error;
            _fileExists = UpgradeSave.Exists;
            _dirty = false;
            _issuesDirty = true;
            _compareRows = null;
            _compareBackupPath = null;
            _backups = null;

            SetStatus(ok ? "已读取存档。" : "还没读到存档：" + error, ok ? MessageType.Info : MessageType.Warning);
        }

        private void Save()
        {
            if (_data == null)
                return;

            if (EditorApplication.isPlaying)
            {
                SetStatus("Unity 正在 Play：现在写盘不会生效（游戏内存里是旧值，且 0.5 秒后会把旧值写回来）。请先停止 Play。", MessageType.Warning);
                return;
            }

            string json = UpgradeSave.Serialize(_data, _prettyPrint);
            string backupPath;
            string error;
            if (!UpgradeSave.TryWrite(json, "保存前", out backupPath, out error))
            {
                SetStatus("写入失败：" + error, MessageType.Error);
                return;
            }

            _loadedJson = json;
            _fileExists = true;
            _dirty = false;
            _issuesDirty = true;
            _backups = null;

            SetStatus("已保存。" + (string.IsNullOrEmpty(backupPath) ? string.Empty : "（原文件已备份：" + System.IO.Path.GetFileName(backupPath) + "）"),
                MessageType.Info);
            Repaint();
        }

        private void MarkDirty()
        {
            _dirty = true;
            _issuesDirty = true;
            Repaint();
        }

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
        }

        private void RefreshIssues()
        {
            _issues.Clear();
            UpgradeValidator.Validate(_data, _fileExists, _issues);
            _issuesDirty = false;
        }
        #endregion

        #region 绘制总入口
        private void OnGUI()
        {
            // 工具栏按钮上写着「保存 (Ctrl+S)」，那就真的绑上这个键（不然标签是骗人的）。
            if (Event.current.type == EventType.KeyDown && Event.current.control && Event.current.keyCode == KeyCode.S)
            {
                if (!EditorApplication.isPlaying)
                    Save();
                Event.current.Use();
            }

            if (_data == null)
                Reload();

            if (_issuesDirty)
                RefreshIssues();

            DrawToolbar();

            Rect body = new Rect(0f, ToolbarHeight, position.width, position.height - ToolbarHeight - StatusHeight);
            float leftWidth = _leftCollapsed ? 26f : Mathf.Clamp(_leftWidth, 240f, Mathf.Max(260f, body.width - 420f));

            var leftRect = new Rect(body.x, body.y, leftWidth, body.height);
            var splitterRect = new Rect(leftRect.xMax, body.y, 5f, body.height);
            var rightRect = new Rect(splitterRect.xMax, body.y, body.width - leftWidth - 5f, body.height);

            DrawLeftColumn(leftRect);

            float updated = UpgradeEditorUI.Splitter(splitterRect, leftWidth, 240f, Mathf.Max(260f, body.width - 420f));
            if (!_leftCollapsed && !Mathf.Approximately(updated, leftWidth))
                _leftWidth = updated;

            DrawRightColumn(rightRect);
            DrawStatusBar();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(ToolbarHeight));

            if (GUILayout.Button("重新读取", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                if (!_dirty || EditorUtility.DisplayDialog("重新读取",
                        "当前有未保存的修改，重新读取会丢弃它们。要继续吗？", "丢弃并重新读取", "取消"))
                    Reload();
            }

            using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
            {
                if (GUILayout.Button("保存 (Ctrl+S)", EditorStyles.toolbarButton, GUILayout.Width(96f)))
                    Save();
            }

            if (GUILayout.Button("备份一份", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                string path = UpgradeSave.CreateBackup("手动备份");
                if (string.IsNullOrEmpty(path))
                    SetStatus("备份失败或还没有存档文件。", MessageType.Warning);
                else
                {
                    _backups = null;
                    SetStatus("已备份：" + System.IO.Path.GetFileName(path), MessageType.Info);
                }
            }

            if (GUILayout.Button("打开目录", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                EditorUtility.RevealInFinder(UpgradeSave.SavePath);

            GUILayout.Space(8f);

            if (EditorApplication.isPlaying)
            {
                Color old = GUI.contentColor;
                GUI.contentColor = UpgradeEditorUI.Warn;
                GUILayout.Label("● Unity 正在 Play —— 写盘已禁用（先停止 Play）", EditorStyles.miniLabel);
                GUI.contentColor = old;
            }
            else if (_dirty)
            {
                Color old = GUI.contentColor;
                GUI.contentColor = UpgradeEditorUI.Growth;
                GUILayout.Label("● 有未保存的修改", EditorStyles.miniLabel);
                GUI.contentColor = old;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(_leftCollapsed ? "▸ 左栏已收起" : string.Empty, EditorStyles.miniLabel, GUILayout.Width(80f));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatusBar()
        {
            Rect rect = new Rect(0f, position.height - StatusHeight, position.width, StatusHeight);
            UpgradeEditorUI.FillRect(rect, new Color(0f, 0f, 0f, 0.18f));
            UpgradeEditorUI.FillRect(new Rect(rect.x, rect.y, 3f, rect.height),
                _statusType == MessageType.Error ? UpgradeEditorUI.Error :
                _statusType == MessageType.Warning ? UpgradeEditorUI.Warn : UpgradeEditorUI.Accent);

            Color old = GUI.contentColor;
            GUI.contentColor = _statusType == MessageType.Error ? UpgradeEditorUI.Error :
                               _statusType == MessageType.Warning ? UpgradeEditorUI.Warn : Color.white;
            GUI.Label(new Rect(rect.x + 10f, rect.y + 3f, rect.width - 20f, rect.height - 4f), _status ?? string.Empty, EditorStyles.miniLabel);
            GUI.contentColor = old;
        }
        #endregion

        #region 左栏：快捷动作 + 体检
        private void DrawLeftColumn(Rect rect)
        {
            GUILayout.BeginArea(rect);
            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);

            if (_leftCollapsed)
            {
                if (GUILayout.Button("▶", EditorStyles.miniButton, GUILayout.Width(20f)))
                    _leftCollapsed = false;
                EditorGUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("◀ 收起", EditorStyles.miniButton, GUILayout.Width(52f)))
                _leftCollapsed = true;
            EditorGUILayout.LabelField("快捷动作与体检", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            DrawQuickActions();
            UpgradeEditorUI.Space(8f);
            DrawIssueList();

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawQuickActions()
        {
            UpgradeEditorUI.Section("快捷动作", "测试用，先改内存、点保存才落盘");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("建筑等级", GUILayout.Width(56f));
            _quickLevel = Mathf.Clamp(EditorGUILayout.DelayedIntField(_quickLevel, GUILayout.Width(50f)), 1, 999);
            if (GUILayout.Button("三个建筑等级 → " + _quickLevel, EditorStyles.miniButton))
            {
                if (Confirm("把三个建筑等级都改成 " + _quickLevel + "？", "这会直接改内存里的存档值（点保存才写盘）。"))
                {
                    _data.barracksLevel = _quickLevel;
                    _data.darkBarracksLevel = _quickLevel;
                    _data.sentryTowerLevel = _quickLevel;
                    MarkDirty();
                    SetStatus("三个建筑等级 → " + _quickLevel + "（还没写盘）", MessageType.Info);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("等级归零（建筑 1 / 魔法 0）", EditorStyles.miniButton))
            {
                if (Confirm("等级归零？", "三个建筑等级 → 1（游戏允许的最小值，写 0 会让整份存档被判定无效），两个魔法等级 → 0。"))
                {
                    _data.barracksLevel = 1;
                    _data.darkBarracksLevel = 1;
                    _data.sentryTowerLevel = 1;
                    _data.attackMagicLevel = 0;
                    _data.defenseMagicLevel = 0;
                    MarkDirty();
                    SetStatus("等级已归零（建筑 1/1/1、魔法 0/0）；还没写盘。", MessageType.Info);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("金条", GUILayout.Width(30f));
            _quickGold = Mathf.Max(0, EditorGUILayout.DelayedIntField(_quickGold, GUILayout.Width(72f)));
            EditorGUILayout.LabelField("钻石", GUILayout.Width(30f));
            _quickDiamond = Mathf.Max(0, EditorGUILayout.DelayedIntField(_quickDiamond, GUILayout.Width(72f)));
            if (GUILayout.Button("补满", EditorStyles.miniButton, GUILayout.Width(48f)))
            {
                _data.goldBarCount = _quickGold;
                _data.diamondCount = _quickDiamond;
                MarkDirty();
                SetStatus("货币 → 金条 " + _quickGold + " / 钻石 " + _quickDiamond + "（还没写盘）", MessageType.Info);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("图鉴全解锁 + 全拥有", EditorStyles.miniButton))
            {
                List<string> ids = UpgradeOptions.BookIds;
                if (ids.Count == 0)
                    SetStatus("图鉴目录里没有条目，或 BookCatalog 资产没读到。", MessageType.Warning);
                else if (Confirm("图鉴全部解锁并拥有？", "会把图鉴目录里的 " + ids.Count + " 个条目都写进两个列表。"))
                {
                    _data.bookUnlockedIds = new List<string>(ids);
                    _data.bookOwnedIds = new List<string>(ids);
                    MarkDirty();
                    SetStatus("图鉴已全解锁/全拥有（" + ids.Count + " 条）；还没写盘。", MessageType.Info);
                }
            }

            if (GUILayout.Button("清空图鉴", EditorStyles.miniButton, GUILayout.Width(72f)))
            {
                if (Confirm("清空图鉴解锁与拥有记录？", "两个列表都会被清空。"))
                {
                    _data.bookUnlockedIds = new List<string>();
                    _data.bookOwnedIds = new List<string>();
                    MarkDirty();
                    SetStatus("图鉴记录已清空（还没写盘）。", MessageType.Info);
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("清空通关进度", EditorStyles.miniButton))
            {
                if (Confirm("清空两种模式的通关记录？", "清空后所有关卡的通关奖励都能重新领取（GameOverRewards 判重的就是这两个列表）。"))
                {
                    _data.clearedNormalStageIds = new List<int>();
                    _data.clearedMasterStageIds = new List<int>();
                    MarkDirty();
                    SetStatus("通关进度已清空（还没写盘）。", MessageType.Info);
                }
            }

            EditorGUILayout.LabelField("到第", GUILayout.Width(30f));
            _quickClearTo = Mathf.Clamp(EditorGUILayout.DelayedIntField(_quickClearTo, GUILayout.Width(40f)), 1, 999);
            if (GUILayout.Button("关标记已通关", EditorStyles.miniButton, GUILayout.Width(96f)))
            {
                var ids = new List<int>();
                List<int> known = UpgradeOptions.StageIds;
                for (int i = 0; i < known.Count; i++)
                {
                    if (known[i] <= _quickClearTo && known[i] < UpgradeSave.LocalTestStageId)
                        ids.Add(known[i]);
                }

                if (ids.Count == 0)
                    SetStatus("工程里没有编号 ≤ " + _quickClearTo + " 的正式关。", MessageType.Warning);
                else if (Confirm("把这 " + ids.Count + " 关标记为普通模式已通关？", "编号：" + UpgradeSave.DescribeList(ids)))
                {
                    _data.clearedNormalStageIds = ids;
                    MarkDirty();
                    SetStatus("已标记 " + ids.Count + " 关通关（还没写盘）。", MessageType.Info);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("全部回默认值（等于删档）", EditorStyles.miniButton))
            {
                if (Confirm("把整份存档恢复成游戏默认值？", "等级 1/1/1、货币 0、图鉴与通关进度全空、出战选择全空、音量 1。点保存才写盘。"))
                {
                    _data = UpgradeSave.NewDefault();
                    MarkDirty();
                    SetStatus("已恢复默认值（还没写盘）。", MessageType.Info);
                }
            }
        }

        private void DrawIssueList()
        {
            int errors = 0, warnings = 0, fixable = 0;
            for (int i = 0; i < _issues.Count; i++)
            {
                if (_issues[i].Level == UpgradeIssueLevel.Error) errors++;
                else if (_issues[i].Level == UpgradeIssueLevel.Warning) warnings++;
                if (_issues[i].Fixable) fixable++;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("体检", EditorStyles.boldLabel, GUILayout.Width(30f));
            UpgradeEditorUI.Tag(errors + " 错误", errors > 0 ? UpgradeEditorUI.Error : UpgradeEditorUI.Muted);
            UpgradeEditorUI.Tag(warnings + " 警告", warnings > 0 ? UpgradeEditorUI.Warn : UpgradeEditorUI.Muted);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button(fixable > 0 ? ("一键修复（" + fixable + " 处）") : "没有可自动修复的问题"))
            {
                if (fixable == 0)
                    return;

                if (Confirm("一键修复 " + fixable + " 处问题？",
                        "只做安全确定的修复：越界值夹回合法范围、超长 ID 截断、重复项去重、版本号补齐。\n" +
                        "不会删任何条目、不会改没报错的字段。改完还要点「保存」才写盘（写盘前自动备份）。"))
                {
                    int count = UpgradeValidator.ApplySafeFixes(_data, _issues);
                    MarkDirty();
                    SetStatus("已修复 " + count + " 处（还没写盘）。", MessageType.Info);
                }
            }

            if (_issues.Count == 0)
            {
                UpgradeEditorUI.Hint("体检通过：这份存档没有发现问题。");
                return;
            }

            for (int i = 0; i < _issues.Count; i++)
                DrawIssue(_issues[i]);
        }

        private void DrawIssue(UpgradeIssue issue)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            Color old = GUI.contentColor;
            GUI.contentColor = issue.LevelColor;
            EditorGUILayout.LabelField(issue.LevelLabel + " " + issue.Title, EditorStyles.boldLabel);
            GUI.contentColor = old;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(issue.Detail, UpgradeEditorUI.RichLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(48f)))
            {
                for (int t = 0; t < UpgradeTab.All.Length; t++)
                {
                    if (UpgradeTab.All[t] == issue.Tab)
                    {
                        _tab = t;
                        UpgradeEditorPrefs.RightTab = t;
                        break;
                    }
                }
                GUI.FocusControl(null);
            }

            if (issue.Fixable)
            {
                if (GUILayout.Button("修复：" + issue.SuggestedValue, EditorStyles.miniButton))
                {
                    if (issue.Fix != null)
                        issue.Fix(_data);
                    MarkDirty();
                    SetStatus("已按建议修复（还没写盘）。", MessageType.Info);
                }
            }
            else
            {
                EditorGUILayout.LabelField("（这条只报告，工具不擅自改）", EditorStyles.miniLabel, GUILayout.Width(180f));
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region 右栏：页签 + 内容
        private void DrawRightColumn(Rect rect)
        {
            GUILayout.BeginArea(rect);

            _tab = GUILayout.Toolbar(_tab, UpgradeTab.All, EditorStyles.toolbarButton, GUILayout.Height(22f));
            UpgradeEditorUI.Space(4f);

            _contentScroll = EditorGUILayout.BeginScrollView(_contentScroll);

            switch (UpgradeTab.All[Mathf.Clamp(_tab, 0, UpgradeTab.All.Length - 1)])
            {
                case UpgradeTab.Level: DrawLevelTab(); break;
                case UpgradeTab.Loadout: DrawLoadoutTab(); break;
                case UpgradeTab.Book: DrawBookTab(); break;
                default: DrawBackupTab(); break;
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        #region ① 等级与货币
        private void DrawLevelTab()
        {
            DrawSaveInfoSection();
            DrawLevelSection();
            DrawCurrencySection();
            DrawSettingsSection();
        }

        private void DrawSaveInfoSection()
        {
            UpgradeEditorUI.Section("存档", _fileExists ? "工程之外的一份 JSON" : "还没有这份文件");

            UpgradeEditorUI.KeyValue("路径", UpgradeSave.SavePath, 60f);
            UpgradeEditorUI.KeyValue("状态", _fileExists
                ? ("存在，" + UpgradeSave.FileSize + " 字节，最后写入 " + UpgradeSave.FileTime.ToString("yyyy-MM-dd HH:mm:ss") + "（" +
                   Mathf.RoundToInt((float)Mathf.Min(99999f, (float)UpgradeSave.FileAgeSeconds)) + " 秒前）")
                : "不存在（没进过 Play 就没有）", 60f);
            UpgradeEditorUI.KeyValue("存档版本", (_data != null ? _data.schemaVersion.ToString() : "?") +
                "（游戏当前支持到 " + UserGlobalInfoData.CurrentSchemaVersion + "）", 60f);

            if (!string.IsNullOrEmpty(_loadError))
                UpgradeEditorUI.Note("⚠ " + _loadError, UpgradeEditorUI.Warn);

            UpgradeEditorUI.Hint("提示：这份文件在工程之外（%USERPROFILE%\\AppData\\LocalLow\\JXX\\CageFight\\SaveData），" +
                                 "本工具不会碰任何游戏脚本或工程资产。");
            UpgradeEditorUI.Space();
        }

        private void DrawLevelSection()
        {
            int totalLevel = Mathf.Max(1, Mathf.Min(_data.barracksLevel, Mathf.Min(_data.darkBarracksLevel, _data.sentryTowerLevel)));
            float scale = Mathf.Pow(1.1f, totalLevel - 1);

            UpgradeEditorUI.Section("玩家等级", "本工具的主角：改这里等于给玩家改养成进度");

            UpgradeEditorUI.Note("总等级 = 三个建筑等级里**最低**的那个 = " + totalLevel +
                "（UserGlobalInfo.TotalLevel）→ 法术伤害按 ×1.1^(总等级-1) = ×" + scale.ToString("0.###") +
                " 结算（EngineerSpellCaster.cs:18 等）；建筑与兵种等级也按同一套 1.1 倍率缩放。", UpgradeEditorUI.Growth);

            IntRow("兵营等级", _data.barracksLevel, 1, 999, v => { _data.barracksLevel = v; MarkDirty(); },
                "局内兵营/兵种按 barracksLevel 缩放（StageObjectInstantiator.cs:171）。");
            IntRow("黑暗兵营等级", _data.darkBarracksLevel, 1, 999, v => { _data.darkBarracksLevel = v; MarkDirty(); },
                "黑暗兵营走 darkBarracksLevel；**敌方 AI 也用它**（关卡里的「敌方黑暗兵营等级」是另一套，互不影响）。");
            IntRow("哨塔等级", _data.sentryTowerLevel, 1, 999, v => { _data.sentryTowerLevel = v; MarkDirty(); },
                "哨塔按 sentryTowerLevel 缩放。");

            UpgradeEditorUI.Space(4f);
            IntRow("攻击魔法等级", _data.attackMagicLevel, 0, 999, v => { _data.attackMagicLevel = v; MarkDirty(); },
                "允许 0（注入建筑时会被 Mathf.Max(1, …) 抬到 1，所以 0 与 1 在建筑那条路上没区别）。");
            IntRow("防御魔法等级", _data.defenseMagicLevel, 0, 999, v => { _data.defenseMagicLevel = v; MarkDirty(); },
                "同上。");

            UpgradeEditorUI.Space(4f);
            int nextCost = Mathf.Max(1, _data.barracksLevel) * 100;
            UpgradeEditorUI.Hint("游戏内升级一次的花费 = 当前等级 × 100 金条（UpgradeLevelRules.BaseCostPerLevel，" +
                                 "升级三项都从各自等级算）→ 按现在的兵营等级，升到下一级要 " + nextCost + " 金条。");
            UpgradeEditorUI.Space();
        }

        private void DrawCurrencySection()
        {
            UpgradeEditorUI.Section("货币", "金条用于建筑升级，钻石用于魔法升级");
            IntRow("金条", _data.goldBarCount, 0, 99999999, v => { _data.goldBarCount = v; MarkDirty(); });
            IntRow("钻石", _data.diamondCount, 0, 99999999, v => { _data.diamondCount = v; MarkDirty(); });
            UpgradeEditorUI.Space();
        }

        private void DrawSettingsSection()
        {
            UpgradeEditorUI.Section("设置与进度");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("音量", GUILayout.Width(110f));
            float volume = EditorGUILayout.Slider(_data.volume, 0f, 1f);
            if (!Mathf.Approximately(volume, _data.volume))
            {
                _data.volume = volume;
                MarkDirty();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("显示伤害数字", GUILayout.Width(110f));
            bool showDamage = EditorGUILayout.Toggle(_data.showDamage);
            if (showDamage != _data.showDamage)
            {
                _data.showDamage = showDamage;
                MarkDirty();
            }
            EditorGUILayout.EndHorizontal();

            int unlocked = (int)Mathf.Clamp(_data.unlockedStage, 0f, 99999f);
            IntRow("已解锁关卡", unlocked, 1, 9999, v => { _data.unlockedStage = (uint)v; MarkDirty(); },
                "⚠ 运行时**没有任何读取方**：选关不是按它锁的（选关只按 Addressables 地址连续性）。改它不会改变可玩关卡，只有存档记录意义。");
            UpgradeEditorUI.Space();
        }

        private void IntRow(string label, int value, int min, int max, Action<int> setter, string hint = null)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110f));

            int next = EditorGUILayout.DelayedIntField(value, GUILayout.Width(76f));
            if (GUILayout.Button("−", EditorStyles.miniButton, GUILayout.Width(22f)))
                next = value - 1;
            if (GUILayout.Button("＋", EditorStyles.miniButton, GUILayout.Width(22f)))
                next = value + 1;

            int clamped = Mathf.Clamp(next, min, max);
            if (clamped != value)
                setter(clamped);

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("范围 " + min + " ~ " + max, EditorStyles.miniLabel, GUILayout.Width(96f));
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(hint))
                EditorGUILayout.LabelField(hint, UpgradeEditorUI.RichLabel);
        }
        #endregion

        #region ② 出战选择
        private void DrawLoadoutTab()
        {
            UpgradeEditorUI.Section("出战选择", "存档里记的是稳定 ID，运行时时按 ID 解析定义资产");

            UpgradeEditorUI.Note("这几个 ID 就是选装界面存下来的（UserGlobalInfo.SetLoadoutSelection）。" +
                "留空 = 没选，运行时解析不到；下拉候选是从工程资产里现扫的（只读，不写）。", UpgradeEditorUI.FileInfo);

            DropdownRow("工程师", _data.selectedEngineerId, UpgradeOptions.Engineers,
                v => { _data.selectedEngineerId = v; MarkDirty(); });
            DropdownRow("种族", _data.selectedRaceId, UpgradeOptions.Races,
                v => { _data.selectedRaceId = v; MarkDirty(); });
            DropdownRow("法术槽 1", _data.selectedSpellSlot1Id, UpgradeOptions.Spells,
                v => { _data.selectedSpellSlot1Id = v; MarkDirty(); });
            DropdownRow("法术槽 2", _data.selectedSpellSlot2Id, UpgradeOptions.Spells,
                v => { _data.selectedSpellSlot2Id = v; MarkDirty(); });

            UpgradeEditorUI.Space();
            UpgradeEditorUI.Section("大本营预制体键", "进关时生成我方大本营用的预制体");

            UpgradeEditorUI.Note("运行时的解析顺序：**先看所选种族的 Buildings 表里 MainBase 条目**，" +
                "种族没配才回落这个键（StageObjectInstantiator.ResolveRaceMainBaseKey:203-223）。" +
                "这个键若指向一个没被预载的预制体，且真的走到回落那一步，运行时会 LogError → " +
                "整关实例化失败（**进不去关**）。所以这里只给工程里真实挂着的键。", UpgradeEditorUI.Warn);

            DropdownRow("大本营预制体键", _data.selectedRaceMainBasePrefabKey, UpgradeOptions.MainBaseKeys,
                v => { _data.selectedRaceMainBasePrefabKey = v; MarkDirty(); });

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("手动输入", GUILayout.Width(110f));
            string manual = EditorGUILayout.TextField(_data.selectedRaceMainBasePrefabKey ?? string.Empty);
            if (!string.Equals(manual, _data.selectedRaceMainBasePrefabKey, StringComparison.Ordinal))
            {
                _data.selectedRaceMainBasePrefabKey = manual;
                MarkDirty();
            }
            EditorGUILayout.EndHorizontal();

            List<string> keys = UpgradeOptions.MainBaseKeys;
            UpgradeEditorUI.Hint("工程里现有的候选：" + (keys.Count == 0 ? "（一个都没扫到）" : UpgradeSave.DescribeList(keys)));
            UpgradeEditorUI.Space();
        }

        private void DropdownRow(string label, string value, List<string> options, Action<string> setter)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110f));

            var labels = new List<string>();
            var values = new List<string>();
            labels.Add("（空）");
            values.Add(string.Empty);
            for (int i = 0; i < options.Count; i++)
            {
                labels.Add(options[i]);
                values.Add(options[i]);
            }

            if (!values.Contains(value ?? string.Empty))
            {
                labels.Add((value ?? string.Empty) + "  ← 不在工程里");
                values.Add(value ?? string.Empty);
            }

            int current = Mathf.Max(0, values.IndexOf(value ?? string.Empty));
            int picked = EditorGUILayout.Popup(current, labels.ToArray(), GUILayout.ExpandWidth(true));
            if (picked != current && picked >= 0 && picked < values.Count)
                setter(values[picked]);

            EditorGUILayout.EndHorizontal();
        }
        #endregion

        #region ③ 图鉴与进度
        private void DrawBookTab()
        {
            DrawBookSection();
            UpgradeEditorUI.Space(8f);
            DrawProgressSection();
        }

        private void DrawBookSection()
        {
            int unlockedCount = _data.bookUnlockedIds != null ? _data.bookUnlockedIds.Count : 0;
            int ownedCount = _data.bookOwnedIds != null ? _data.bookOwnedIds.Count : 0;

            UpgradeEditorUI.Section("图鉴", "解锁 " + unlockedCount + " 条 / 拥有 " + ownedCount + " 条");

            if (_data.bookUnlockedIds == null)
                _data.bookUnlockedIds = new List<string>();
            if (_data.bookOwnedIds == null)
                _data.bookOwnedIds = new List<string>();

            EditorGUILayout.BeginHorizontal();
            _bookSearch = UpgradeEditorUI.SearchField(_bookSearch, "搜索条目 ID", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("全部解锁", EditorStyles.miniButton, GUILayout.Width(72f)))
            {
                List<string> ids = UpgradeOptions.BookIds;
                for (int i = 0; i < ids.Count; i++)
                {
                    if (!_data.bookUnlockedIds.Contains(ids[i]))
                        _data.bookUnlockedIds.Add(ids[i]);
                }
                MarkDirty();
            }
            if (GUILayout.Button("全部拥有", EditorStyles.miniButton, GUILayout.Width(72f)))
            {
                List<string> ids = UpgradeOptions.BookIds;
                for (int i = 0; i < ids.Count; i++)
                {
                    if (!_data.bookOwnedIds.Contains(ids[i]))
                        _data.bookOwnedIds.Add(ids[i]);
                }
                MarkDirty();
            }
            EditorGUILayout.EndHorizontal();

            List<string> all = UpgradeOptions.BookIds;
            if (all.Count == 0)
            {
                UpgradeEditorUI.Hint("没读到图鉴目录（BookCatalog.asset）—— 检查工程里是否存在该资产。");
            }
            else
            {
                UpgradeEditorUI.KeyValue("目录条目", all.Count + " 条（来自 BookCatalog.asset）", 66f);
                for (int i = 0; i < all.Count; i++)
                {
                    string id = all[i];
                    if (!string.IsNullOrEmpty(_bookSearch) &&
                        id.IndexOf(_bookSearch, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    EditorGUILayout.BeginHorizontal();
                    bool unlocked = _data.bookUnlockedIds.Contains(id);
                    bool owned = _data.bookOwnedIds.Contains(id);

                    EditorGUILayout.LabelField(id, GUILayout.Width(200f));

                    bool newUnlocked = EditorGUILayout.ToggleLeft("解锁", unlocked, GUILayout.Width(58f));
                    if (newUnlocked != unlocked)
                    {
                        if (newUnlocked) _data.bookUnlockedIds.Add(id);
                        else _data.bookUnlockedIds.Remove(id);
                        MarkDirty();
                    }

                    bool newOwned = EditorGUILayout.ToggleLeft("拥有", owned, GUILayout.Width(58f));
                    if (newOwned != owned)
                    {
                        if (newOwned) _data.bookOwnedIds.Add(id);
                        else _data.bookOwnedIds.Remove(id);
                        MarkDirty();
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }
            }

            // 存档里有、目录里没有的 ID（只在开关打开时列出来）
            var unknown = new List<string>();
            CollectUnknown(_data.bookUnlockedIds, all, unknown);
            CollectUnknown(_data.bookOwnedIds, all, unknown);
            if (unknown.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                _showUnknownBookIds = EditorGUILayout.ToggleLeft("显示目录里没有的 " + unknown.Count + " 个 ID", _showUnknownBookIds);
                if (GUILayout.Button("移除这些 ID", EditorStyles.miniButton, GUILayout.Width(96f)))
                {
                    if (Confirm("从图鉴记录里移除 " + unknown.Count + " 个不在目录里的 ID？", "编号：" + UpgradeSave.DescribeList(unknown)))
                    {
                        for (int i = 0; i < unknown.Count; i++)
                        {
                            _data.bookUnlockedIds.Remove(unknown[i]);
                            _data.bookOwnedIds.Remove(unknown[i]);
                        }
                        MarkDirty();
                    }
                }
                EditorGUILayout.EndHorizontal();

                if (_showUnknownBookIds)
                {
                    for (int i = 0; i < unknown.Count; i++)
                        UpgradeEditorUI.Hint("  · " + unknown[i] + "（不在 BookCatalog 里）");
                }
            }
        }

        private static void CollectUnknown(List<string> ids, List<string> known, List<string> output)
        {
            if (ids == null || known.Count == 0)
                return;

            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (!string.IsNullOrEmpty(id) && !known.Contains(id) && !output.Contains(id))
                    output.Add(id);
            }
        }

        private void DrawProgressSection()
        {
            UpgradeEditorUI.Section("通关进度", "记录的是「已领过通关奖励」的关卡编号");

            UpgradeEditorUI.Note("这两个列表的**唯一读取方**是通关结算界面判重（GameOverRewards.cs:121-122 → IsStageCleared）：" +
                "在列表里 = 该关奖励已经领过。清空它们就能反复测奖励结算；标记某关就能让它显示成已通关。", UpgradeEditorUI.FileInfo);

            DrawStageList("普通模式", _data.clearedNormalStageIds, list => { _data.clearedNormalStageIds = list; MarkDirty(); });
            UpgradeEditorUI.Space(4f);
            DrawStageList("大师模式", _data.clearedMasterStageIds, list => { _data.clearedMasterStageIds = list; MarkDirty(); });
            UpgradeEditorUI.Space();
        }

        private void DrawStageList(string label, List<int> list, Action<List<int>> setter)
        {
            if (list == null)
                list = new List<int>();

            UpgradeEditorUI.Section(label, list.Count + " 关");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("添加关卡编号", GUILayout.Width(96f));
            int add = EditorGUILayout.DelayedIntField(0, GUILayout.Width(64f));
            if (GUILayout.Button("加入", EditorStyles.miniButton, GUILayout.Width(48f)) && add > 0)
            {
                if (!list.Contains(add))
                {
                    list.Add(add);
                    list.Sort();
                    setter(list);
                }
            }
            if (GUILayout.Button("清空", EditorStyles.miniButton, GUILayout.Width(48f)))
            {
                if (Confirm("清空「" + label + "」的通关记录？", "共 " + list.Count + " 关。"))
                    setter(new List<int>());
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (list.Count == 0)
            {
                UpgradeEditorUI.Hint("（空）");
                return;
            }

            List<int> known = UpgradeOptions.StageIds;
            EditorGUILayout.BeginHorizontal();
            int removeAt = -1;
            for (int i = 0; i < list.Count; i++)
            {
                int id = list[i];
                bool unknown = known.Count > 0 && !known.Contains(id);
                Color old = GUI.contentColor;
                if (unknown)
                    GUI.contentColor = UpgradeEditorUI.Warn;

                if (GUILayout.Button(id + (unknown ? "✗" : string.Empty), EditorStyles.miniButton, GUILayout.Width(46f)))
                    removeAt = i;

                GUI.contentColor = old;

                if ((i + 1) % 14 == 0)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (removeAt >= 0)
            {
                list.RemoveAt(removeAt);
                setter(list);
            }

            UpgradeEditorUI.Hint("点编号即可移除；带 ✗ 的编号在工程里找不到对应关卡（可能是改过号或已删除）。");
        }
        #endregion

        #region ④ 备份与对比
        private void DrawBackupTab()
        {
            UpgradeEditorUI.Section("备份", "每次写盘前自动备份一份到工具目录");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("保留份数", GUILayout.Width(66f));
            int keep = Mathf.Clamp(EditorGUILayout.DelayedIntField(UpgradeEditorPrefs.KeepBackupCount, GUILayout.Width(56f)), 1, 500);
            if (keep != UpgradeEditorPrefs.KeepBackupCount)
                UpgradeEditorPrefs.KeepBackupCount = keep;

            bool pretty = EditorGUILayout.ToggleLeft("写盘时缩进（便于手改）", _prettyPrint, GUILayout.Width(200f));
            if (pretty != _prettyPrint)
            {
                _prettyPrint = pretty;
                UpgradeEditorPrefs.PrettyPrint = pretty;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            UpgradeEditorUI.KeyValue("备份目录", UpgradePaths.BackupFolder, 66f);
            UpgradeEditorUI.Space();

            if (GUILayout.Button("立即备份当前存档（手动备份）"))
            {
                string path = UpgradeSave.CreateBackup("手动备份");
                if (string.IsNullOrEmpty(path))
                    SetStatus("备份失败或还没有存档文件。", MessageType.Warning);
                else
                {
                    _backups = null;
                    SetStatus("已备份：" + System.IO.Path.GetFileName(path), MessageType.Info);
                }
            }

            if (_backups == null)
                _backups = UpgradeSave.ListBackups();

            UpgradeEditorUI.Section("备份列表", _backups.Count + " 份");
            if (_backups.Count == 0)
            {
                UpgradeEditorUI.Hint("还没有备份 —— 只要用本工具保存过一次，就会自动出现。");
                return;
            }

            _backupScroll = EditorGUILayout.BeginScrollView(_backupScroll, GUILayout.Height(180f));
            for (int i = 0; i < _backups.Count; i++)
            {
                SaveBackupInfo info = _backups[i];
                bool selected = string.Equals(info.Path, _compareBackupPath, StringComparison.OrdinalIgnoreCase);

                Rect row = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
                if (selected)
                    UpgradeEditorUI.FillRect(row, UpgradeEditorUI.Selected);
                else if (i % 2 == 0)
                    UpgradeEditorUI.FillRect(row, UpgradeEditorUI.RowEven);

                if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                    SelectBackup(info);

                GUI.Label(new Rect(row.x + 6f, row.y + 2f, 150f, 16f), info.Time.ToString("MM-dd HH:mm:ss"), EditorStyles.miniLabel);
                GUI.Label(new Rect(row.x + 162f, row.y + 2f, row.width - 250f, 16f), info.Name, EditorStyles.miniLabel);
                GUI.Label(new Rect(row.xMax - 84f, row.y + 2f, 80f, 16f), info.Size + " B", EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();

            if (string.IsNullOrEmpty(_compareBackupPath))
            {
                UpgradeEditorUI.Hint("点一份备份，就能和「当前内存里的存档」逐字段对比，或把它恢复回去。");
                return;
            }

            UpgradeEditorUI.Space();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("选中：" + System.IO.Path.GetFileName(_compareBackupPath), EditorStyles.miniLabel);

            if (GUILayout.Button("与当前对比", EditorStyles.miniButton, GUILayout.Width(90f)))
                CompareWith(_compareBackupPath);

            if (GUILayout.Button("恢复这份备份", EditorStyles.miniButton, GUILayout.Width(110f)))
            {
                if (Confirm("用这份备份覆盖当前存档？", "覆盖前会把现在的存档再备份一份（原因记为「恢复前」）。"))
                {
                    string savedBackup;
                    string error;
                    if (UpgradeSave.TryRestore(_compareBackupPath, out savedBackup, out error))
                    {
                        Reload();
                        SetStatus("已恢复备份。" + (string.IsNullOrEmpty(savedBackup) ? string.Empty : "（覆盖前已备份）"), MessageType.Info);
                    }
                    else
                    {
                        SetStatus("恢复失败：" + error, MessageType.Error);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_compareInfo))
                UpgradeEditorUI.Note(_compareInfo, UpgradeEditorUI.FileInfo);

            if (_compareRows != null)
                DrawCompareTable(_compareRows);
        }

        private void SelectBackup(SaveBackupInfo info)
        {
            _compareBackupPath = info != null ? info.Path : null;
            _compareRows = null;
            _compareInfo = string.Empty;
            Repaint();
        }

        private void CompareWith(string backupPath)
        {
            UserGlobalInfoData backup;
            string error;
            if (!UpgradeSave.TryReadBackup(backupPath, out backup, out error))
            {
                _compareInfo = error;
                _compareRows = null;
                return;
            }

            _compareRows = UpgradeSave.Diff(backup, _data);

            int changed = 0;
            for (int i = 0; i < _compareRows.Count; i++)
            {
                if (_compareRows[i].Changed)
                    changed++;
            }

            _compareInfo = "左边是备份、右边是当前内存里的值：共 " + _compareRows.Count + " 个字段，其中 " + changed + " 个不同。";
        }

        private void DrawCompareTable(List<SaveFieldRow> rows)
        {
            UpgradeEditorUI.Space();
            UpgradeEditorUI.Section("逐字段对比", "备份 → 当前");

            for (int i = 0; i < rows.Count; i++)
            {
                SaveFieldRow row = rows[i];
                Rect rect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
                if (row.Changed)
                    UpgradeEditorUI.FillRect(rect, new Color(UpgradeEditorUI.Warn.r, UpgradeEditorUI.Warn.g, UpgradeEditorUI.Warn.b, 0.16f));
                else if (i % 2 == 0)
                    UpgradeEditorUI.FillRect(rect, UpgradeEditorUI.RowEven);

                GUI.Label(new Rect(rect.x + 6f, rect.y + 1f, 130f, 16f), row.Label, EditorStyles.miniLabel);
                GUI.Label(new Rect(rect.x + 140f, rect.y + 1f, 200f, 16f), row.Before, EditorStyles.miniLabel);
                GUI.Label(new Rect(rect.x + 348f, rect.y + 1f, 20f, 16f), "→", EditorStyles.miniLabel);

                Color old = GUI.contentColor;
                if (row.Changed)
                    GUI.contentColor = UpgradeEditorUI.Growth;
                GUI.Label(new Rect(rect.x + 372f, rect.y + 1f, rect.width - 380f, 16f), row.After, EditorStyles.miniLabel);
                GUI.contentColor = old;
            }
        }
        #endregion
        #endregion

        #region 小工具
        private static bool Confirm(string title, string message)
        {
            return EditorUtility.DisplayDialog(title, message, "确定", "取消");
        }
        #endregion
    }
}
