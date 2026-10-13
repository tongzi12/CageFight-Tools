using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightStageTool
{
    /// <summary>
    /// **程序化构建关卡**（R1.33）：选「低 / 中 / 高 / 卡点」→ 按关卡编号算出整套敌方数值、
    /// 槽位、白名单与解锁时间 → **先给对照表预览** → 确认后一次写进当前关卡。
    ///
    /// 一句话说明它为什么"合理"：
    ///   · 数字不是拍脑袋，是照着工程里现有 21 关的实测曲线算的（等级 1→13、时限 100→500、
    ///     槽位 4/2/5 封顶…），难度档只在曲线上做有界加减，并夹在实测包络里；
    ///   · 兵种不是随便挑，是**按当前出场概率从低到高挑**（优先补齐出现少的），挑完还能看到每个兵种的概率变化；
    ///   · 槽位坐标不重新发明 —— 直接调工程里已有的「建筑槽位自动编排」（`StageLayout`），
    ///     那套逻辑已经处理过互不重叠、间隔 ≥1 格、半场位置这些硬规则。
    ///
    /// 只写**数值与配置**，不动场景里的预摆对象（那是场景数据，工具不该凭空造）。
    /// </summary>
    public sealed class LevelEditorStageGeneratorWindow : EditorWindow
    {
        private StageEditorData _data;
        private Action _onApplied;

        private StageDifficulty _difficulty = StageDifficulty.Mid;
        private string _raceId = string.Empty;
        private string[] _raceOptions = { "（没有种族）" };
        private int _raceIndex;
        private int _seed = 20261012;

        private bool _applyEconomy = true;
        private bool _applyLevels = true;
        private bool _applySlots = true;
        private bool _applyWhitelist = true;
        private bool _applyPlayerGold = true;
        private bool _applyTime = true;
        private bool _applyRewards;

        private StageGenPlan _plan;
        private string _report = string.Empty;
        private Vector2 _scroll;

        private static readonly string[] DifficultyLabels = { "低", "中", "高", "卡点" };

        public static void Open(StageEditorData data, Action onApplied)
        {
            var window = GetWindow<LevelEditorStageGeneratorWindow>(true, "程序化构建关卡", true);
            window.minSize = new Vector2(620f, 640f);
            window.titleContent = new GUIContent("程序化构建关卡");
            window._data = data;
            window._onApplied = onApplied;
            window._plan = null;
            window._report = string.Empty;
            window.BuildRaceOptions();
            window.Show();
        }

        private void BuildRaceOptions()
        {
            var labels = new List<string>();
            StageResourceIndex index = StageResourceIndex.Instance;
            for (int i = 0; i < index.Races.Count; i++)
            {
                RaceDefinition race = index.Races[i];
                if (race != null && !string.IsNullOrEmpty(race.Id))
                    labels.Add(race.Id);
            }

            if (labels.Count == 0)
                labels.Add("（没有种族）");

            _raceOptions = labels.ToArray();

            // 默认取当前关卡敌方队伍已经在用的种族（多半就是你要继续的那一支）
            StageConfig config = CurrentConfig();
            if (config != null && config.enemyTeams != null && config.enemyTeams.Count > 0 &&
                !string.IsNullOrEmpty(config.enemyTeams[0].raceId))
                _raceId = config.enemyTeams[0].raceId;

            _raceIndex = Mathf.Max(0, Array.IndexOf(_raceOptions, _raceId));
            if (_raceIndex < _raceOptions.Length)
                _raceId = _raceOptions[_raceIndex];
        }

        private StageConfig CurrentConfig()
        {
            return _data != null && _data.IsLoaded ? _data.Stage : null;
        }

        private void OnGUI()
        {
            StageConfig config = CurrentConfig();
            if (config == null)
            {
                EditorGUILayout.HelpBox("先在左栏选一个关卡。", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "按「关卡编号 + 难度档」一次算出一整套数值，并放好建筑槽位。\n\n" +
                "· 曲线照着工程现有 21 关的实测数据：敌方等级 1→13、防御时限 100→500、槽位最多 4/2/5\n" +
                "· 难度档只在曲线上做**有界**加减 —— 低难度的一关仍然是「这一关该有的规模」，不会变成另一款游戏\n" +
                "· 兵种按**当前出场概率从低到高**挑（优先补齐出场少的），解锁时间错开，避免一开局全涌出来\n" +
                "· 槽位坐标走工程里已有的「自动编排」，互不重叠、间隔 ≥1 格这些硬规则不用重写\n" +
                "· 预览里每一项都有「现在 → 生成」的对照；只写你勾选的那些字段，随时 Ctrl+Z",
                MessageType.Info);

            DrawInputs(config);
            DrawPreview(config);

            EditorGUILayout.EndScrollView();
        }

        private void DrawInputs(StageConfig config)
        {
            EditorGUILayout.LabelField("① 参数", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("难度档", GUILayout.Width(64f));
            _difficulty = (StageDifficulty)EditorGUILayout.Popup((int)_difficulty, DifficultyLabels, GUILayout.Width(80f));
            EditorGUILayout.LabelField("（低 = 练手 / 中 = 按编号的正常曲线 / 高 = 压着打 / 卡点 = 必须换阵容）",
                LevelEditorUI.MiniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("敌方种族", GUILayout.Width(64f));
            int race = EditorGUILayout.Popup(_raceIndex, _raceOptions, GUILayout.Width(160f));
            if (race != _raceIndex)
            {
                _raceIndex = race;
                _raceId = _raceOptions[Mathf.Clamp(_raceIndex, 0, _raceOptions.Length - 1)];
                _plan = null;
            }

            EditorGUILayout.LabelField("随机种子", GUILayout.Width(64f));
            int seed = EditorGUILayout.IntField(_seed, GUILayout.Width(110f));
            if (seed != _seed)
            {
                _seed = seed;
                _plan = null;
            }

            if (GUILayout.Button("换一个", EditorStyles.miniButton, GUILayout.Width(56f)))
            {
                _seed = UnityEngine.Random.Range(1, 999999);
                _plan = null;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("　　（种子只影响「同概率时先挑哪个兵」，数值曲线是确定的）", LevelEditorUI.MiniLabel);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("② 写哪些字段", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            _applyEconomy = EditorGUILayout.ToggleLeft("敌方经济（起始金 / 每秒）", _applyEconomy, GUILayout.Width(190f));
            _applyLevels = EditorGUILayout.ToggleLeft("敌方等级（建筑 + 魔法）", _applyLevels, GUILayout.Width(190f));
            _applySlots = EditorGUILayout.ToggleLeft("建筑槽位（含上限）", _applySlots, GUILayout.Width(170f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _applyWhitelist = EditorGUILayout.ToggleLeft("兵种白名单 + 解锁时间", _applyWhitelist, GUILayout.Width(190f));
            _applyPlayerGold = EditorGUILayout.ToggleLeft("玩家起始金", _applyPlayerGold, GUILayout.Width(140f));
            _applyTime = EditorGUILayout.ToggleLeft("防守时限", _applyTime, GUILayout.Width(110f));
            _applyRewards = EditorGUILayout.ToggleLeft("通关奖励", _applyRewards, GUILayout.Width(110f));
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("　　通关奖励默认**不覆盖**（用「奖励模板」或右栏自己填更合适）", LevelEditorUI.MiniLabel);

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("生成预览（不写盘）", GUILayout.Width(160f)))
                RebuildPlan(config);
            using (new EditorGUI.DisabledScope(_plan == null))
            {
                GUI.backgroundColor = new Color(1f, 0.85f, 0.5f);
                if (GUILayout.Button("写进当前关卡", GUILayout.Width(130f)))
                    Apply(config);
                GUI.backgroundColor = Color.white;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void RebuildPlan(StageConfig config)
        {
            var request = new StageGenRequest
            {
                StageId = config.stageId,
                StageType = config.stageType,
                Difficulty = _difficulty,
                EnemyRaceId = _raceId,
                Seed = _seed,
                CurrentDefenseTime = (int)config.DefenseTime,
                CurrentEnemyStartGold = config.enemyTeams != null && config.enemyTeams.Count > 0 ? config.enemyTeams[0].startGold : 0,
                CurrentEnemyIncome = config.enemyTeams != null && config.enemyTeams.Count > 0 ? (int)config.enemyTeams[0].baseGoldPerSecond : 0,
                CurrentEnemyLevel = config.enemyBarracksLevel,
                CurrentWhitelistCount = config.enemyTeams != null && config.enemyTeams.Count > 0 && config.enemyTeams[0].troopUnlocks != null
                    ? config.enemyTeams[0].troopUnlocks.Count : 0
            };

            // 候选兵种：这个种族兵营里能出的兵 + 它当下的平均出现概率（复用兵种一览那套统计）
            StageResourceIndex index = StageResourceIndex.Instance;
            List<RosterStage> stages = BuildRosterStages();
            for (int dark = 0; dark <= 1; dark++)
            {
                List<TroopDefinition> troops = index.GetBarracksTroops(_raceId, dark == 1);
                for (int i = 0; i < troops.Count; i++)
                {
                    TroopDefinition troop = troops[i];
                    if (troop == null)
                        continue;

                    string id = string.IsNullOrEmpty(troop.Id) ? troop.name : troop.Id;
                    if (ContainsId(request.Candidates, id))
                        continue;

                    TroopRosterStats stats = TroopRosterMath.Compute(stages, id);
                    request.Candidates.Add(new StageGenTroop
                    {
                        Id = id,
                        Label = string.IsNullOrEmpty(troop.DisplayName) ? troop.name : troop.DisplayName,
                        Tier = troop.Tier,
                        UnlockLevel = troop.UnlockLevel,
                        Upkeep = troop.Upkeep,
                        CurrentChance = (float)stats.AverageChance,
                        Dark = dark == 1
                    });
                }
            }

            _plan = StageGenerator.Build(request);

            // 预览要显示"名字 / 阶级 / 当下概率"，这里把候选表存成按 id 查的字典
            _candidateLookup = new Dictionary<string, StageGenTroop>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < request.Candidates.Count; i++)
                _candidateLookup[request.Candidates[i].Id] = request.Candidates[i];

            _report = string.Empty;
            Repaint();
        }

        /// <summary>当前每关"可能出的兵种"（与兵种一览同一套口径），用来算每个兵种当下的出现概率。</summary>
        private static List<RosterStage> BuildRosterStages()
        {
            var result = new List<RosterStage>();
            StageResourceIndex index = StageResourceIndex.Instance;
            List<StageInfo> stages = StageEditorData.DiscoverStages();
            stages.RemoveAll(s => s == null || s.Config == null || s.IsLocalTest);
            stages.Sort((a, b) => a.StageId.CompareTo(b.StageId));

            for (int i = 0; i < stages.Count; i++)
            {
                var stage = new RosterStage { StageId = stages[i].StageId };
                List<TroopDefinition> troops = index.GetStageTroops(stages[i].Config);
                for (int t = 0; t < troops.Count; t++)
                {
                    TroopDefinition troop = troops[t];
                    if (troop == null)
                        continue;

                    string id = string.IsNullOrEmpty(troop.Id) ? troop.name : troop.Id;
                    if (!stage.TroopIds.Contains(id))
                        stage.TroopIds.Add(id);
                }
                result.Add(stage);
            }

            return result;
        }

        private static bool ContainsId(List<StageGenTroop> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private void DrawPreview(StageConfig config)
        {
            if (_plan == null)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("还没生成预览。", LevelEditorUI.MiniLabel);
                return;
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("③ 预览：现在 → 生成（第 " + config.stageId + " 关 · " +
                                       StageGenerator.DifficultyName(_plan.Difficulty) + "难度）", EditorStyles.boldLabel);

            EnemyTeamConfig team = config.enemyTeams != null && config.enemyTeams.Count > 0 ? config.enemyTeams[0] : null;

            DrawCompareRow("防守时限", ((int)config.DefenseTime) + "s", _plan.DefenseTime + "s", _applyTime);
            DrawCompareRow("玩家起始金", config.playerStartGold.ToString(), _plan.PlayerStartGold.ToString(), _applyPlayerGold);
            DrawCompareRow("敌方起始金", team != null ? team.startGold.ToString() : "（没有敌队）",
                _plan.EnemyStartGold.ToString(), _applyEconomy);
            DrawCompareRow("敌方每秒", team != null ? ((int)team.baseGoldPerSecond).ToString() : "—",
                _plan.EnemyIncome.ToString(), _applyEconomy);
            DrawCompareRow("敌方等级", config.enemyBarracksLevel + "（建筑/魔法同一档）",
                _plan.EnemyLevel + "（玩家大致该到 " + _plan.PlayerLevelBonus + " 档）", _applyLevels);
            DrawCompareRow("槽位 普通/黑暗/哨塔",
                team != null ? (CountSlots(team, 1) + "/" + CountSlots(team, 2) + "/" + CountSlots(team, 3)) : "—",
                _plan.SlotBarracks + "/" + _plan.SlotDarkBarracks + "/" + _plan.SlotSentries, _applySlots);
            DrawCompareRow("上限 普通/黑暗/哨塔",
                team != null ? (team.maxBarracks + "/" + team.maxDarkBarracks + "/" + team.maxSentries) : "—",
                _plan.MaxBarracks + "/" + _plan.MaxDarkBarracks + "/" + _plan.MaxSentries, _applySlots);
            DrawCompareRow("白名单",
                team != null && team.troopUnlocks != null ? team.troopUnlocks.Count + " 个" : "0 个",
                _plan.WhitelistCount + " 个", _applyWhitelist);
            DrawCompareRow("奖励（金条/钻石）",
                config.VictoryGoldBars + "/" + config.VictoryDiamonds,
                _plan.VictoryGoldBars + "/" + _plan.VictoryDiamonds, _applyRewards);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("白名单与解锁时间（按当前出现概率从低到高挑的）", EditorStyles.boldLabel);
            for (int i = 0; i < _plan.TroopIds.Count; i++)
            {
                string id = _plan.TroopIds[i];
                StageGenTroop troop = FindCandidate(id);
                EditorGUILayout.LabelField("　" + (i + 1) + ". " + (troop != null ? troop.Label : id) +
                                           "（" + id + "）　" + (troop != null ? (troop.Dark ? "黑暗" : "普通") + "·" + troop.UnlockLevel + "阶" : string.Empty) +
                                           "　解锁 " + _plan.UnlockTimes[i] + "s" +
                                           "　当前平均概率 " + (troop != null ? TroopRosterMath.Percent(troop.CurrentChance) : "—"),
                    LevelEditorUI.MiniLabel);
            }

            if (_plan.WhitelistCount == 0)
                EditorGUILayout.LabelField("　（这个种族没有可用候选：白名单会留空 = 运行时整族预载）", LevelEditorUI.MiniLabel);

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("为什么是这些数", EditorStyles.boldLabel);
            for (int i = 0; i < _plan.Notes.Count; i++)
                EditorGUILayout.LabelField("　· " + _plan.Notes[i], EditorStyles.wordWrappedMiniLabel);

            if (!string.IsNullOrEmpty(_report))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("写入结果", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(_report, EditorStyles.textArea, GUILayout.MinHeight(70f));
            }
        }


        private StageGenTroop FindCandidate(string id)
        {
            if (_plan == null)
                return null;

            // 候选表只用来显示"名字/阶级/概率"，重新按需查一次（数量很少，不值得缓存成状态）
            return _candidateLookup != null && _candidateLookup.TryGetValue(id, out StageGenTroop troop) ? troop : null;
        }

        private Dictionary<string, StageGenTroop> _candidateLookup;

        private static void DrawCompareRow(string label, string current, string generated, bool willApply)
        {
            EditorGUILayout.BeginHorizontal();
            GUI.contentColor = willApply ? Color.white : LevelEditorUI.Muted;
            EditorGUILayout.LabelField(willApply ? "✔ " + label : "　 " + label, GUILayout.Width(200f));
            EditorGUILayout.LabelField(current, LevelEditorUI.MiniLabel, GUILayout.Width(150f));
            EditorGUILayout.LabelField("→", LevelEditorUI.MiniLabel, GUILayout.Width(18f));
            EditorGUILayout.LabelField(generated, EditorStyles.miniBoldLabel);
            GUI.contentColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private static int CountSlots(EnemyTeamConfig team, int buildingType)
        {
            if (team.buildSlots == null)
                return 0;

            int n = 0;
            for (int i = 0; i < team.buildSlots.Count; i++)
            {
                if (team.buildSlots[i] != null && (int)team.buildSlots[i].buildingType == buildingType)
                    n++;
            }
            return n;
        }

        private void Apply(StageConfig config)
        {
            if (_plan == null)
                return;

            if (config.enemyTeams == null || config.enemyTeams.Count == 0)
            {
                EditorUtility.DisplayDialog("程序化构建关卡",
                    "这一关还没有敌方队伍：敌队是「关卡构建」导出时按场景里的敌方大本营生成的，\n" +
                    "工具不会凭空造一支（那会缺 homeGridPosition 等场景相关数据）。\n\n" +
                    "请先用带敌方大本营的场景重新导出这一关，再来生成数值。", "知道了");
                return;
            }

            EnemyTeamConfig team = config.enemyTeams[0];
            var lines = new List<string>();

            // 敌队是 [Serializable] 普通类、不是 UnityEngine.Object，所以只记录 config 一个快照
            // （敌队数据是序列化在关卡资产里的，config 的快照就覆盖了它）。
            Undo.RecordObject(config, "程序化构建关卡");

            if (_applyTime)
            {
                config.DefenseTime = _plan.DefenseTime;
                lines.Add("防守时限 → " + _plan.DefenseTime + "s");
            }

            if (_applyPlayerGold)
            {
                config.hasPlayerStartGold = true;
                config.playerStartGold = _plan.PlayerStartGold;
                lines.Add("玩家起始金 → " + _plan.PlayerStartGold);
            }

            if (_applyLevels)
            {
                config.enemyBarracksLevel = _plan.EnemyLevel;
                config.enemyDarkBarracksLevel = _plan.EnemyLevel;
                config.enemySentryTowerLevel = _plan.EnemyLevel;
                config.enemyDefenseMagicLevel = _plan.EnemyLevel;
                config.enemyAttackMagicLevel = _plan.EnemyLevel;
                lines.Add("敌方 5 项等级 → " + _plan.EnemyLevel);
            }

            if (_applyEconomy)
            {
                team.startGold = _plan.EnemyStartGold;
                team.baseGoldPerSecond = _plan.EnemyIncome;
                lines.Add("敌方起始金 → " + _plan.EnemyStartGold + "，每秒 → " + _plan.EnemyIncome);
            }

            if (_applyRewards)
            {
                _data.SetVictoryRewards(_plan.VictoryGoldBars, _plan.VictoryDiamonds,
                    config.CustomNormalReward, config.NormalVictoryGoldBars, config.NormalVictoryDiamonds);
                lines.Add("通关奖励（大师）→ " + _plan.VictoryGoldBars + " 金条 / " + _plan.VictoryDiamonds + " 钻石");
            }

            if (_applySlots)
            {
                team.maxBarracks = _plan.MaxBarracks;
                team.maxDarkBarracks = _plan.MaxDarkBarracks;
                team.maxSentries = _plan.MaxSentries;

                // 槽位坐标不重新发明：直接用工程里已有的「建筑槽位自动编排」
                StageLayout.Options options = StageLayout.Options.Default(team);
                string arrangeReport;
                if (StageLayout.TryArrange(config, team, options, out arrangeReport))
                    lines.Add("槽位已自动编排（" + arrangeReport + "）");
                else
                    lines.Add("槽位编排失败：" + arrangeReport + "（数值已写入，槽位请用中栏的「自动编排」重试）");
            }

            if (_applyWhitelist)
            {
                team.troopUnlocks.Clear();
                int added = 0;
                for (int i = 0; i < _plan.TroopIds.Count; i++)
                {
                    TroopDefinition troop = StageResourceIndex.Instance.GetTroop(_plan.TroopIds[i]);
                    if (troop == null)
                        continue;

                    var entry = new EnemyTroopUnlock();
                    _data.SetTroopUnlock(entry, troop, _plan.UnlockTimes[i]);
                    team.troopUnlocks.Add(entry);
                    added++;
                }
                lines.Add("白名单 → " + added + " 个兵种（解锁时间已错开）");
            }

            _data.MarkDirty();
            _report = string.Join("\n", lines.ToArray());
            _onApplied?.Invoke();

            EditorUtility.DisplayDialog("程序化构建关卡：已写入",
                _report + "\n\n记得点工具栏「保存」落盘（保存前会自动备份）。\n" +
                "写完之后建议看一眼右栏「体检」，确认没有错误。", "知道了");

            Repaint();
        }
    }
}
