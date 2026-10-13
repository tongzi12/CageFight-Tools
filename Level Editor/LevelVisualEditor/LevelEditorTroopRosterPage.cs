using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightStageTool
{
    /// <summary>
    /// **兵种一览**（关卡编辑器的子页面，R1.32）。
    ///
    /// 一次回答三件事，并且**能就地改**兵种的头像与显示名：
    ///   · 每个兵种出现在**哪些关卡**（列表 + 点一下就跳过去）
    ///   · **出现概率**（覆盖率 = 出现关数 ÷ 统计关数；平均概率 = 每关"候选池均分"后取平均）
    ///   · **第一次出现在第几关**
    ///
    /// 「可能出的兵种」与界面上「本关出现的兵种」同一套口径（`StageResourceIndex.GetStageTroops`）：
    /// 敌方队伍填了兵种白名单就取白名单；白名单为空则与运行时一致按**整族预载**
    /// （该种族普通兵营 + 黑暗兵营的全部兵）。
    ///
    /// ⚠️ 概率是**估算**：真实对局里敌方 AI 还要看金币、威胁权重与解锁时间，
    /// 不是"从候选池里等概率随机抽"。界面上把这句话写在说明行里。
    ///
    /// **字号**：整页（行高、头像、方块、说明文字）都按 `LevelEditorPrefs.RosterFontSize` 走，
    /// 工具栏上能直接切「小/中/大/特大」——默认中号（14），比 Unity 的 mini 样式大一档，
    /// 因为在 2K 屏上 mini 字太小。
    ///
    /// 只写兵种资产的两个字段（`displayName` / `iconKey`），并在保存前备份；关卡资产一个字段都不动。
    /// </summary>
    public sealed class LevelEditorTroopRosterPage
    {
        #region 字号与尺寸（全部由字号推导）
        /// <summary>可选字号：小 / 中（默认）/ 大 / 特大。</summary>
        private static readonly int[] FontSizes = { 12, 14, 16, 18 };
        private static readonly string[] FontLabels = { "小", "中", "大", "特大" };

        private int _fontSize = 14;

        private float RowHeightPx { get { return _fontSize + 20f; } }
        private float HeaderHeightPx { get { return _fontSize + 12f; } }
        private float AvatarSize { get { return _fontSize * 2.2f; } }
        private float AvatarColumn { get { return AvatarSize + 8f; } }
        private float WidthName { get { return _fontSize * 11f; } }
        private float WidthId { get { return _fontSize * 8.5f; } }
        private float WidthRace { get { return _fontSize * 6.4f; } }
        private float WidthTier { get { return _fontSize * 5.2f; } }
        private float WidthCount { get { return _fontSize * 4.4f; } }
        private float WidthCoverage { get { return _fontSize * 4.6f; } }
        private float WidthChance { get { return _fontSize * 5f; } }
        private float WidthStage { get { return _fontSize * 5.4f; } }
        private float DetailHeightPx { get { return _fontSize * 9.5f; } }
        private float ChipWidth { get { return _fontSize * 8.4f; } }
        private float ChipHeight { get { return _fontSize + 10f; } }
        private float StageListWidth { get { return _fontSize * 13f; } }
        #endregion

        private sealed class Row
        {
            public TroopDefinition Troop;
            public string Id;
            public string Name;
            public string IconKey;
            public string Race;
            public bool Dark;
            public int UnlockLevel;
            public TroopRosterStats Stats;
        }

        private readonly List<Row> _rows = new List<Row>();
        private readonly List<RosterStage> _stages = new List<RosterStage>();
        private readonly HashSet<TroopDefinition> _dirty = new HashSet<TroopDefinition>();

        private string _search = string.Empty;
        private int _raceFilter;
        private string[] _raceLabels = { "全部种族" };
        private int _sort;
        private bool _onlyMissingIcon;

        private Vector2 _scroll;
        private int _selected = -1;
        private bool _built;
        private string _status = string.Empty;
        private MessageType _statusType = MessageType.None;

        private static readonly string[] SortLabels =
        {
            "出现概率 ↓", "出现关数 ↓", "首次关卡 ↑", "名字 ↑", "训练 ID ↑"
        };

        private Action<int> _jumpToStage;

        public LevelEditorTroopRosterPage()
        {
            _fontSize = NearestFontSize(LevelEditorPrefs.RosterFontSize);
        }

        public string Status { get { return _status; } }
        public MessageType StatusType { get { return _statusType; } }
        public int DirtyCount { get { return _dirty.Count; } }

        /// <summary>主窗口把"跳到某一关"的能力交给这一页（点 StageN / 关卡方块时用）。</summary>
        public void SetJumpHandler(Action<int> jumpToStage)
        {
            _jumpToStage = jumpToStage;
        }

        private static int NearestFontSize(int value)
        {
            int best = FontSizes[1];
            int bestDelta = int.MaxValue;
            for (int i = 0; i < FontSizes.Length; i++)
            {
                int delta = Math.Abs(FontSizes[i] - value);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    best = FontSizes[i];
                }
            }
            return best;
        }

        private int FontIndex
        {
            get
            {
                for (int i = 0; i < FontSizes.Length; i++)
                {
                    if (FontSizes[i] == _fontSize)
                        return i;
                }
                return 1;
            }
        }

        #region 样式（随字号重建）
        private int _styleFontSize = -1;
        private GUIStyle _label;
        private GUIStyle _labelBold;
        private GUIStyle _right;
        private GUIStyle _rightBold;
        private GUIStyle _wrap;
        private GUIStyle _centered;
        private GUIStyle _tag;
        private GUIStyle _field;
        private GUIStyle _hint;
        private GUIStyle _empty;

        private void EnsureStyles()
        {
            if (_styleFontSize == _fontSize)
                return;

            _styleFontSize = _fontSize;

            _label = new GUIStyle(EditorStyles.label);
            _label.fontSize = _fontSize;
            _label.clipping = TextClipping.Clip;

            _labelBold = new GUIStyle(EditorStyles.label);
            _labelBold.fontSize = _fontSize;
            _labelBold.fontStyle = FontStyle.Bold;
            _labelBold.clipping = TextClipping.Clip;

            _right = new GUIStyle(_label);
            _right.alignment = TextAnchor.MiddleRight;

            _rightBold = new GUIStyle(_labelBold);
            _rightBold.alignment = TextAnchor.MiddleRight;

            _wrap = new GUIStyle(EditorStyles.label);
            _wrap.fontSize = Mathf.Max(11, _fontSize - 1);
            _wrap.wordWrap = true;

            _centered = new GUIStyle(_label);
            _centered.alignment = TextAnchor.MiddleCenter;

            _tag = new GUIStyle(_label);
            _tag.alignment = TextAnchor.MiddleCenter;
            _tag.normal.textColor = Color.black;

            _field = new GUIStyle(EditorStyles.textField);
            _field.fontSize = _fontSize;

            _hint = new GUIStyle(_label);
            _hint.normal.textColor = LevelEditorUI.Muted;

            _empty = new GUIStyle(_centered);
            _empty.normal.textColor = LevelEditorUI.Muted;
        }
        #endregion

        #region 采集
        public void Invalidate()
        {
            _built = false;
        }

        /// <summary>重扫：关卡侧的"每关可能出的兵种" + 兵种表（头像 / 显示名）。</summary>
        public void Rebuild()
        {
            EnsureStyles();

            _rows.Clear();
            _stages.Clear();

            StageResourceIndex index = StageResourceIndex.Instance;
            List<StageInfo> stages = StageEditorData.DiscoverStages();
            stages.RemoveAll(s => s == null || s.Config == null || s.IsLocalTest);
            stages.Sort((a, b) => a.StageId.CompareTo(b.StageId));

            for (int i = 0; i < stages.Count; i++)
            {
                var stage = new RosterStage
                {
                    StageId = stages[i].StageId,
                    Label = "Stage" + stages[i].StageId
                };

                List<TroopDefinition> troops = index.GetStageTroops(stages[i].Config);
                for (int t = 0; t < troops.Count; t++)
                {
                    TroopDefinition troop = troops[t];
                    if (troop == null)
                        continue;

                    string id = TroopKey(troop);
                    if (!stage.TroopIds.Contains(id))
                        stage.TroopIds.Add(id);
                }

                _stages.Add(stage);
            }

            var raceLabels = new List<string> { "全部种族" };
            var races = new List<RaceDefinition>();
            for (int i = 0; i < index.Races.Count; i++)
            {
                RaceDefinition race = index.Races[i];
                if (race == null || string.IsNullOrEmpty(race.Id))
                    continue;
                races.Add(race);
                raceLabels.Add(race.Id);
            }
            _raceLabels = raceLabels.ToArray();

            var ownership = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int r = 0; r < races.Count; r++)
            {
                for (int dark = 0; dark <= 1; dark++)
                {
                    List<TroopDefinition> list = index.GetBarracksTroops(races[r].Id, dark == 1);
                    for (int t = 0; t < list.Count; t++)
                    {
                        TroopDefinition troop = list[t];
                        if (troop == null)
                            continue;

                        string key = TroopKey(troop);
                        if (!ownership.ContainsKey(key))
                            ownership[key] = races[r].Id + "|" + dark;
                    }
                }
            }

            IReadOnlyList<TroopDefinition> all = index.Troops;
            for (int i = 0; i < all.Count; i++)
            {
                TroopDefinition troop = all[i];
                if (troop == null)
                    continue;

                string id = TroopKey(troop);
                string owner;
                string race = string.Empty;
                bool dark = false;
                if (ownership.TryGetValue(id, out owner))
                {
                    string[] parts = owner.Split('|');
                    race = parts[0];
                    dark = parts.Length > 1 && parts[1] == "1";
                }

                _rows.Add(new Row
                {
                    Troop = troop,
                    Id = id,
                    Name = string.IsNullOrEmpty(troop.DisplayName) ? troop.name : troop.DisplayName,
                    IconKey = troop.IconKey ?? string.Empty,
                    Race = race,
                    Dark = dark,
                    UnlockLevel = Mathf.Clamp(troop.UnlockLevel, 1, 3),
                    Stats = TroopRosterMath.Compute(_stages, id)
                });
            }

            Sort();

            _built = true;
            _status = "共 " + _rows.Count + " 个兵种 × " + _stages.Count + " 个关卡。";
            _statusType = MessageType.Info;
        }

        private static string TroopKey(TroopDefinition troop)
        {
            return string.IsNullOrEmpty(troop.Id) ? troop.name : troop.Id;
        }

        private void Sort()
        {
            switch (_sort)
            {
                case 1:
                    _rows.Sort((a, b) =>
                    {
                        int c = b.Stats.Appearances.CompareTo(a.Stats.Appearances);
                        return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
                    });
                    break;
                case 2:
                    _rows.Sort((a, b) =>
                    {
                        int c = SortFirst(a).CompareTo(SortFirst(b));
                        return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
                    });
                    break;
                case 3: _rows.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name)); break;
                case 4: _rows.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id)); break;
                default:
                    _rows.Sort((a, b) =>
                    {
                        int c = b.Stats.AverageChance.CompareTo(a.Stats.AverageChance);
                        return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
                    });
                    break;
            }
        }

        private static int SortFirst(Row row)
        {
            return row.Stats.NeverAppeared ? int.MaxValue : row.Stats.FirstStageId;
        }
        #endregion

        #region 保存
        /// <summary>保存改过的兵种资产（改前逐个备份）。</summary>
        public void Save(bool backup)
        {
            if (_dirty.Count == 0)
            {
                _status = "兵种没有需要保存的改动。";
                _statusType = MessageType.Info;
                return;
            }

            var troops = new List<TroopDefinition>(_dirty);
            var paths = new List<string>();
            for (int i = 0; i < troops.Count; i++)
            {
                string path = AssetDatabase.GetAssetPath(troops[i]);
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }
            paths.Sort(StringComparer.OrdinalIgnoreCase);

            int backedUp = 0;
            if (backup)
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    if (StageEditorData.BackupFile(paths[i], "保存兵种前") != null)
                        backedUp++;
                }
            }

            AssetDatabase.SaveAssets();

            int saved = troops.Count;
            _dirty.Clear();
            _status = "已保存 " + saved + " 个兵种资产" +
                      (backup ? "（备份 " + backedUp + " 份）" : string.Empty) + "。";
            _statusType = MessageType.Info;
        }
        #endregion

        #region 绘制
        public void Draw()
        {
            EnsureStyles();

            if (!_built)
                Rebuild();

            DrawToolbar();
            DrawSummary();
            DrawHeaderRow();

            EditorGUILayout.BeginVertical(GUILayout.ExpandHeight(true));
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

            int shown = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (!Matches(_rows[i]))
                    continue;

                DrawRow(_rows[i], i);
                shown++;
            }

            if (shown == 0)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.LabelField("没有符合条件的兵种。", _hint);
            }

            EditorGUILayout.EndScrollView();

            DrawDetailPanel();

            EditorGUILayout.EndVertical();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(EditorStyles.toolbar.fixedHeight));

            if (GUILayout.Button(new GUIContent("重新扫描", "重读关卡与兵种资产，重算全部统计"),
                    EditorStyles.toolbarButton, GUILayout.Width(62f)))
            {
                StageResourceIndex.Invalidate();
                Rebuild();
            }

            using (new EditorGUI.DisabledScope(_dirty.Count == 0))
            {
                GUI.backgroundColor = _dirty.Count > 0 ? new Color(1f, 0.85f, 0.5f) : Color.white;
                if (GUILayout.Button(new GUIContent(_dirty.Count > 0 ? "● 保存兵种（" + _dirty.Count + "）" : "保存兵种",
                        "只写兵种资产（显示名 / 头像）；保存前自动备份被改的资产"),
                        EditorStyles.toolbarButton, GUILayout.Width(_dirty.Count > 0 ? 116f : 64f)))
                    Save(LevelEditorPrefs.AutoBackupOnSave);
                GUI.backgroundColor = Color.white;
            }

            if (GUILayout.Button(new GUIContent("撤销", "撤销上一步（Ctrl+Z）"),
                    EditorStyles.toolbarButton, GUILayout.Width(42f)))
                Undo.PerformUndo();

            EditorGUILayout.LabelField("字号", EditorStyles.miniLabel, GUILayout.Width(30f));
            int font = EditorGUILayout.Popup(FontIndex, FontLabels, EditorStyles.toolbarPopup, GUILayout.Width(56f));
            if (font != FontIndex)
            {
                _fontSize = FontSizes[Mathf.Clamp(font, 0, FontSizes.Length - 1)];
                LevelEditorPrefs.RosterFontSize = _fontSize;
                _styleFontSize = -1;
                Repaint();
            }

            EditorGUILayout.LabelField("种族", EditorStyles.miniLabel, GUILayout.Width(30f));
            int race = EditorGUILayout.Popup(_raceFilter, _raceLabels, EditorStyles.toolbarPopup, GUILayout.Width(104f));
            if (race != _raceFilter)
                _raceFilter = race;

            EditorGUILayout.LabelField("排序", EditorStyles.miniLabel, GUILayout.Width(30f));
            int sort = EditorGUILayout.Popup(_sort, SortLabels, EditorStyles.toolbarPopup, GUILayout.Width(96f));
            if (sort != _sort)
            {
                _sort = sort;
                Sort();
            }

            bool onlyMissing = GUILayout.Toggle(_onlyMissingIcon,
                new GUIContent("只看没头像的", "把 iconKey 为空、或在 SpriteRegistry 里找不到的兵种挑出来"),
                EditorStyles.toolbarButton, GUILayout.Width(88f));
            if (onlyMissing != _onlyMissingIcon)
                _onlyMissingIcon = onlyMissing;

            GUILayout.FlexibleSpace();

            string search = LevelEditorUI.SearchField(_search, "搜索：名字 / 训练 ID", GUILayout.Width(190f));
            if (search != _search)
            {
                _search = search;
                _selected = -1;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummary()
        {
            int never = 0;
            int missingIcon = 0;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Stats.NeverAppeared)
                    never++;
                if (string.IsNullOrEmpty(_rows[i].IconKey) || StageResourceIndex.Instance.GetSprite(_rows[i].IconKey) == null)
                    missingIcon++;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("共 " + _rows.Count + " 个兵种", _labelBold, GUILayout.Width(_fontSize * 8f));
            Tag("有出场 " + (_rows.Count - never), LevelEditorUI.Ok);
            Tag("从没出场 " + never, never > 0 ? LevelEditorUI.Error : LevelEditorUI.Muted);
            Tag("没头像 " + missingIcon, missingIcon > 0 ? LevelEditorUI.Warn : LevelEditorUI.Muted);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("统计关卡 " + _stages.Count + " 个（本地测试关未计入）", _hint);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(
                "口径：「可能出的兵种」= 敌方队伍白名单非空取白名单，否则整族兵营（普通 + 黑暗）。" +
                "覆盖率 = 出现关数 ÷ 统计关数；平均概率 = 每关按「候选池均分」算 1÷候选数、未出现算 0，再对全部关卡取平均 —— " +
                "真实对局还要看 AI 的钱、威胁权重与解锁时间，这是估算不是保证。",
                _wrap);
            EditorGUILayout.Space(2f);
        }

        /// <summary>自己画标签（用本页字号），不用 LevelEditorUI.Tag —— 那个是 mini 字号，太小。</summary>
        private void Tag(string text, Color color)
        {
            float width = _tag.CalcSize(new GUIContent(text)).x + 14f;
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), _tag,
                GUILayout.Width(width), GUILayout.Height(_fontSize + 6f));

            Color old = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, 0.20f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
            LevelEditorUI.Frame(rect, color, 1f);
            GUI.Label(rect, text, _tag);
        }

        private static readonly string[] HeaderTips =
        {
            "兵种头像（= TroopDefinition.iconKey）。点一下换图。",
            "显示名（TroopDefinition.displayName），可以直接在这里改。",
            "训练 ID（关卡与敌方队伍白名单按它引用兵种）",
            "所属种族",
            "兵营·阶（阶 = 解锁等级）",
            "出现在多少个关卡里",
            "覆盖率 = 出现关数 ÷ 统计关数",
            "平均出现概率（详见上方口径）",
            "第一次出现在哪一关（可点，跳过去）",
            "最近一次出现在哪一关（可点，跳过去）",
            "这个兵种出现过的全部关卡"
        };

        private void DrawHeaderRow()
        {
            Rect rect = GUILayoutUtility.GetRect(0f, HeaderHeightPx, GUILayout.ExpandWidth(true));
            LevelEditorUI.FillRect(rect, LevelEditorUI.SectionBg);

            float x = rect.x + 4f;
            float stageListWidth;
            float raceWidth;
            bool showRace;
            float nameWidth = ComputeColumns(rect.width, out stageListWidth, out raceWidth, out showRace);
            float y = rect.y + (HeaderHeightPx - _fontSize - 4f) * 0.5f + 1f;
            int i = 0;

            GUI.Label(new Rect(x, y, AvatarColumn - 2f, _fontSize + 4f), new GUIContent("头像", HeaderTips[i++]), _labelBold);
            x += AvatarColumn;
            GUI.Label(new Rect(x, y, nameWidth, _fontSize + 4f), new GUIContent("显示名", HeaderTips[i++]), _labelBold);
            x += nameWidth;
            GUI.Label(new Rect(x, y, WidthId, _fontSize + 4f), new GUIContent("训练 ID", HeaderTips[i++]), _labelBold);
            x += WidthId;
            i++;   // 种族
            if (showRace)
            {
                GUI.Label(new Rect(x, y, raceWidth, _fontSize + 4f), new GUIContent("种族", HeaderTips[3]), _labelBold);
                x += raceWidth;
            }
            GUI.Label(new Rect(x, y, WidthTier, _fontSize + 4f), new GUIContent("兵营·阶", HeaderTips[i++]), _labelBold);
            x += WidthTier;
            GUI.Label(new Rect(x, y, WidthCount, _fontSize + 4f), new GUIContent("出现关数", HeaderTips[i++]), _rightBold);
            x += WidthCount;
            GUI.Label(new Rect(x, y, WidthCoverage, _fontSize + 4f), new GUIContent("覆盖率", HeaderTips[i++]), _rightBold);
            x += WidthCoverage;
            GUI.Label(new Rect(x, y, WidthChance, _fontSize + 4f), new GUIContent("平均概率", HeaderTips[i++]), _rightBold);
            x += WidthChance;
            GUI.Label(new Rect(x, y, WidthStage, _fontSize + 4f), new GUIContent("首次出现", HeaderTips[i++]), _labelBold);
            x += WidthStage;
            GUI.Label(new Rect(x, y, WidthStage, _fontSize + 4f), new GUIContent("最近出现", HeaderTips[i++]), _labelBold);
            x += WidthStage;
            if (stageListWidth > 0f)
            {
                GUI.Label(new Rect(x, y, Mathf.Max(40f, rect.xMax - x - 6f), _fontSize + 4f),
                    new GUIContent("出现的关卡", HeaderTips[i]), _labelBold);
            }

            LevelEditorUI.FillRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), LevelEditorUI.GridLine);
        }

        private float NameColumnWidth(Rect row)
        {
            float stageList;
            float race;
            bool showRace;
            return ComputeColumns(row.width, out stageList, out race, out showRace);
        }

        /// <summary>
        /// 按窗口宽度分配列宽：**先保证"出现的关卡"这一列不被挤出屏幕**，
        /// 再按余量给"显示名"；宽度实在不够时依次收掉"种族"列与"出现的关卡"列
        /// （种族在显示名的 tooltip 里还有，关卡列表在下面选中行那块里还有）。
        /// 字号调大之后列会变宽，所以这一步是必须的 —— 否则特大字号下最后一列直接看不见。
        /// 约束：`essential + race + name + list == totalWidth`（不超、不溢出）。
        /// </summary>
        private float ComputeColumns(float totalWidth, out float stageListWidth, out float raceWidth, out bool showRace)
        {
            float essential = AvatarColumn + WidthId + WidthTier + WidthCount + WidthCoverage + WidthChance +
                              WidthStage * 2f + 16f;

            // 显示名的下限；低于它就把"种族"收掉
            float minName = _fontSize * 8f;
            showRace = totalWidth - essential - WidthRace >= minName;
            raceWidth = showRace ? WidthRace : 0f;

            float leftover = Mathf.Max(0f, totalWidth - essential - raceWidth);
            float minList = _fontSize * 4f;

            // 优先级：显示名（能编辑的最重要）> 出现的关卡 > 种族。
            // 放不下"列表下限"时先收列表，而不是把显示名压成 0 宽（那样名字既看不到也改不了）。
            float name;
            if (leftover >= minName + minList)
            {
                name = Mathf.Max(minName, leftover * 0.5f);
                stageListWidth = leftover - name;
            }
            else
            {
                name = leftover;
                stageListWidth = 0f;
            }

            return name;
        }

        private void DrawRow(Row row, int index)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, RowHeightPx, GUILayout.ExpandWidth(true));
            bool selected = index == _selected;
            bool hover = rect.Contains(Event.current.mousePosition);

            if (selected)
                LevelEditorUI.FillRect(rect, LevelEditorUI.Selected);
            else if (hover)
                LevelEditorUI.FillRect(rect, LevelEditorUI.RowEven);

            float textHeight = _fontSize + 6f;
            float textY = rect.y + (RowHeightPx - textHeight) * 0.5f;
            float x = rect.x + 4f;
            float stageListWidth;
            float raceWidth;
            bool showRace;
            float nameWidth = ComputeColumns(rect.width, out stageListWidth, out raceWidth, out showRace);

            // ── 头像：点一下换图
            Rect avatarRect = new Rect(x + 2f, rect.y + (RowHeightPx - AvatarSize) * 0.5f, AvatarSize, AvatarSize);
            Sprite icon = StageResourceIndex.Instance.GetSprite(row.IconKey);
            if (icon != null)
            {
                LevelEditorUI.FillRect(avatarRect, LevelEditorUI.PreviewBg);
                LevelEditorUI.DrawSpriteFit(avatarRect, icon, Color.white);
            }
            else
            {
                LevelEditorUI.FillRect(avatarRect, LevelEditorUI.PreviewBg);
                LevelEditorUI.Frame(avatarRect, LevelEditorUI.GridLine, 1f);
                GUI.Label(avatarRect, row.IconKey.Length == 0 ? "无" : "×", _empty);
            }

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                avatarRect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                TroopDefinition target = row.Troop;
                LevelEditorPickerWindow.Open("兵种头像", row.Name, StageResourceIndex.StageKeyKind.Sprite,
                    key =>
                    {
                        StageEditorData.SetTroopIconKey(target, key);
                        _dirty.Add(target);
                        _built = false;
                        Repaint();
                    },
                    () =>
                    {
                        StageEditorData.SetTroopIconKey(target, string.Empty);
                        _dirty.Add(target);
                        _built = false;
                        Repaint();
                    },
                    null);
                return;
            }

            x += AvatarColumn;

            // ── 显示名：直接改
            Rect nameRect = new Rect(x, rect.y + 2f, nameWidth - 6f, RowHeightPx - 4f);
            EditorGUI.BeginChangeCheck();
            string newName = EditorGUI.TextField(nameRect, row.Name, _field);
            if (EditorGUI.EndChangeCheck())
            {
                StageEditorData.SetTroopDisplayName(row.Troop, newName);
                row.Name = string.IsNullOrEmpty(newName) ? row.Troop.name : newName;
                _dirty.Add(row.Troop);
                Repaint();
            }
            x += nameWidth;

            GUI.Label(new Rect(x, textY, WidthId, textHeight), row.Id, _label);
            x += WidthId;

            if (showRace)
            {
                GUI.Label(new Rect(x, textY, raceWidth, textHeight),
                    string.IsNullOrEmpty(row.Race) ? "—" : row.Race, _label);
                x += raceWidth;
            }

            Color oldColor = GUI.contentColor;
            GUI.contentColor = row.Dark ? LevelEditorUI.Boss : LevelEditorUI.Muted;
            GUI.Label(new Rect(x, textY, WidthTier, textHeight),
                (row.Dark ? "黑暗" : "普通") + "·" + row.UnlockLevel + "阶", _label);
            GUI.contentColor = oldColor;
            x += WidthTier;

            GUI.Label(new Rect(x, textY, WidthCount, textHeight), row.Stats.Appearances.ToString(), _right);
            x += WidthCount;

            GUI.Label(new Rect(x, textY, WidthCoverage, textHeight),
                TroopRosterMath.Percent(row.Stats.Coverage), _right);
            x += WidthCoverage;

            GUI.contentColor = row.Stats.NeverAppeared ? LevelEditorUI.Muted : Color.white;
            GUI.Label(new Rect(x, textY, WidthChance, textHeight),
                TroopRosterMath.Percent(row.Stats.AverageChance), _rightBold);
            GUI.contentColor = oldColor;
            x += WidthChance;

            DrawStageCell(new Rect(x, textY, WidthStage, textHeight), row.Stats.FirstStageId, "跳到首次出现的关卡");
            x += WidthStage;
            DrawStageCell(new Rect(x, textY, WidthStage, textHeight), row.Stats.LastStageId, "跳到最近出现的关卡");
            x += WidthStage;

            if (stageListWidth > 0f)
            {
                GUI.Label(new Rect(x, textY, Mathf.Max(40f, rect.xMax - x - 6f), textHeight),
                    DescribeStages(row.Stats), _hint);
            }
            LevelEditorUI.FillRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(1f, 1f, 1f, 0.05f));

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
            {
                Event.current.Use();
                _selected = _selected == index ? -1 : index;
                Repaint();
            }
        }

        private string DescribeStages(TroopRosterStats stats)
        {
            if (stats.NeverAppeared)
                return "（没有出现过）";

            var parts = new List<string>();
            for (int i = 0; i < stats.StageIds.Count; i++)
                parts.Add("S" + stats.StageIds[i]);

            return string.Join(" ", parts.ToArray());
        }

        private void DrawStageCell(Rect rect, int stageId, string tooltip)
        {
            if (stageId <= 0)
            {
                GUI.Label(rect, "—", _right);
                return;
            }

            bool hover = rect.Contains(Event.current.mousePosition);
            Color old = GUI.contentColor;
            GUI.contentColor = hover ? Color.white : LevelEditorUI.Accent;
            GUI.Label(rect, new GUIContent("Stage" + stageId, tooltip + "（点一下跳过去）"), hover ? _rightBold : _right);
            GUI.contentColor = old;

            if (hover)
                EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
            {
                Event.current.Use();
                if (_jumpToStage != null)
                {
                    int target = stageId;
                    EditorApplication.delayCall += () => _jumpToStage(target);
                }
            }
        }

        /// <summary>选中行下面那块：把它出现过的**每一关**列出来（可点），并标出该关的候选池大小。</summary>
        private void DrawDetailPanel()
        {
            Rect area = GUILayoutUtility.GetRect(0f, DetailHeightPx, GUILayout.ExpandWidth(true));
            LevelEditorUI.FillRect(area, LevelEditorUI.SectionBg);
            LevelEditorUI.FillRect(new Rect(area.x, area.y, area.width, 1f), LevelEditorUI.GridLine);

            float pad = 8f;
            if (_selected < 0 || _selected >= _rows.Count)
            {
                GUI.Label(new Rect(area.x + pad, area.y + 6f, area.width - pad * 2f, _fontSize + 6f),
                    "点表格里的任意一行 → 这里列出它出现过的每一关（可点）、以及那一关的候选兵种数。", _hint);
                return;
            }

            Row row = _rows[_selected];

            GUI.Label(new Rect(area.x + pad, area.y + 5f, area.width - pad * 2f, _fontSize + 6f),
                row.Name + "（" + row.Id + "）　出现 " + row.Stats.Appearances + " / " + _stages.Count + " 关　" +
                "覆盖率 " + TroopRosterMath.Percent(row.Stats.Coverage) +
                "　平均概率 " + TroopRosterMath.Percent(row.Stats.AverageChance) +
                "　最高单关概率 " + TroopRosterMath.Percent(row.Stats.PeakChance) +
                (row.Stats.PeakStageId > 0 ? "（Stage" + row.Stats.PeakStageId + "）" : string.Empty),
                _labelBold);

            if (row.Stats.NeverAppeared)
            {
                GUI.Label(new Rect(area.x + pad, area.y + _fontSize + 12f, area.width - pad * 2f, _fontSize + 6f),
                    "这个兵种一次都没出现过：没有任何一关的敌方队伍会出它。", _hint);
                return;
            }

            GUI.Label(new Rect(area.x + pad, area.y + _fontSize + 12f, area.width - pad * 2f, _fontSize + 6f),
                "出现过的关卡（括号里是这一关的候选兵种数 —— 越少越容易被抽到）：", _hint);

            float x = area.x + pad;
            float y = area.y + _fontSize + 30f;

            for (int i = 0; i < row.Stats.StageIds.Count; i++)
            {
                int stageId = row.Stats.StageIds[i];
                if (x + ChipWidth > area.xMax - pad)
                {
                    x = area.x + pad;
                    y += ChipHeight + 4f;
                    if (y + ChipHeight > area.yMax - 4f)
                    {
                        GUI.Label(new Rect(x, y, StageListWidth, _fontSize + 6f), "…还有更多关卡", _hint);
                        break;
                    }
                }

                int pool = TroopRosterMath.PoolSizeOfStage(_stages, stageId);
                string label = "Stage" + stageId + "（" + pool + "）";
                Rect chip = new Rect(x, y, ChipWidth, ChipHeight);
                bool hover = chip.Contains(Event.current.mousePosition);

                LevelEditorUI.FillRect(chip, hover ? LevelEditorUI.Selected : new Color(1f, 1f, 1f, 0.06f));
                LevelEditorUI.Frame(chip, LevelEditorUI.GridLine);
                GUI.Label(chip, new GUIContent(label,
                        "这一关共 " + pool + " 个候选兵种 → 单关概率约 " +
                        TroopRosterMath.Percent(pool > 0 ? 1d / pool : 0d) + "；点一下跳到这一关"),
                    _centered);

                if (hover)
                    EditorGUIUtility.AddCursorRect(chip, MouseCursor.Link);

                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
                {
                    Event.current.Use();
                    if (_jumpToStage != null)
                    {
                        int target = stageId;
                        EditorApplication.delayCall += () => _jumpToStage(target);
                    }
                }

                x += ChipWidth + 6f;
            }
        }

        private bool Matches(Row row)
        {
            if (_onlyMissingIcon &&
                !string.IsNullOrEmpty(row.IconKey) &&
                StageResourceIndex.Instance.GetSprite(row.IconKey) != null)
                return false;

            if (_raceFilter > 0 && _raceFilter < _raceLabels.Length &&
                !string.Equals(row.Race, _raceLabels[_raceFilter], StringComparison.OrdinalIgnoreCase))
                return false;

            string q = (_search ?? string.Empty).Trim();
            if (q.Length == 0)
                return true;

            return row.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   row.Id.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void Repaint()
        {
            EditorWindow focused = EditorWindow.focusedWindow;
            if (focused != null)
                focused.Repaint();
        }
        #endregion
    }
}
