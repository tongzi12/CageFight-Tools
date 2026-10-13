using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CageFightStageTool
{
    public enum StageIssueLevel
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>一条体检结论。Code 是稳定标识（便于以后按代码屏蔽某条规则）。</summary>
    public sealed class StageIssue
    {
        public StageIssueLevel Level;
        public string Code = string.Empty;
        public string Title = string.Empty;
        public string Detail = string.Empty;
        public string Field = string.Empty;

        /// <summary>资产级问题没有 StagePath。</summary>
        public string StagePath = string.Empty;
        public int StageId;

        public bool Fixable;
        public string SuggestedValue;

        public Color LevelColor
        {
            get
            {
                switch (Level)
                {
                    case StageIssueLevel.Error: return LevelEditorUI.Error;
                    case StageIssueLevel.Warning: return LevelEditorUI.Warn;
                    default: return LevelEditorUI.Muted;
                }
            }
        }

        public string LevelLabel
        {
            get
            {
                switch (Level)
                {
                    case StageIssueLevel.Error: return "[错误]";
                    case StageIssueLevel.Warning: return "[警告]";
                    default: return "[提示]";
                }
            }
        }
    }

    /// <summary>
    /// 关卡体检：把"策划看不出来、但出包/运行时会出问题"的配置挑出来。
    ///
    /// 每条规则的依据都可以在运行时代码里找到，注释里写了出处；
    /// 判断不了的（例如注册表没读到）一律**跳过而不误报**。
    /// </summary>
    public static class StageValidator
    {
        /// <summary>敌方最多 4 支（StageConfig.enemyTeams 注释：队伍 2/4/6/8，最多 4 支）。</summary>
        public const int MaxEnemyTeams = 4;

        /// <summary>选关弹窗最中间的展示图最多 4 个（StageConfig.displayIcons 注释）。</summary>
        public const int MaxDisplayIcons = 4;

        /// <summary>校验一个关卡。mapWidth/mapHeight 是运行时网格尺寸（来自场景 MapCells）。</summary>
        public static void Validate(StageInfo info, int mapWidth, int mapHeight, List<StageIssue> output)
        {
            if (info == null || info.Config == null || output == null)
                return;

            StageConfig config = info.Config;
            StageResourceIndex index = StageResourceIndex.Instance;

            ValidateIdentity(info, output);
            ValidateBasePosition(info, mapWidth, mapHeight, output);
            ValidateWinLose(info, index, mapWidth, mapHeight, output);
            ValidateEnemyTeams(info, index, mapWidth, mapHeight, output);
            ValidatePlayerEconomy(info, output);
            ValidateObjects(info, index, mapWidth, mapHeight, output);
            ValidateResourceKeyLists(info, index, output);
            ValidateRaceBuildings(info, index, output);
            ValidatePresentation(info, index, output);
        }

        private static StageIssue New(StageInfo info, StageIssueLevel level, string code, string title, string detail)
        {
            return new StageIssue
            {
                Level = level,
                Code = code,
                Title = title,
                Detail = detail,
                StagePath = info != null ? info.Path : string.Empty,
                StageId = info != null ? info.StageId : 0
            };
        }

        #region 编号与上架
        private static void ValidateIdentity(StageInfo info, List<StageIssue> output)
        {
            StageConfig config = info.Config;

            if (config.stageId <= 0)
            {
                output.Add(New(info, StageIssueLevel.Error, "stageId.invalid", "关卡编号无效",
                    "stageId = " + config.stageId + "。「下一关」按 Stage{stageId+1} 找地址（GameOverActionButton.cs:124），编号必须是正整数。"));
            }

            // 资产文件名必须与编号一致：StageConfigLoader 是按地址 Stage{i} 连续找的，
            // 而导出器写的是 Stage{stageId}.asset，两者错位就会加载到别的关。
            string expectedName = "Stage" + config.stageId;
            if (!string.Equals(info.Name, expectedName, StringComparison.Ordinal))
            {
                output.Add(New(info, StageIssueLevel.Error, "stageId.fileMismatch", "资产文件名与编号不一致",
                    "文件名是「" + info.Name + "」，但 stageId = " + config.stageId + "（期望 " + expectedName + "）。选关按地址 Stage{编号} 连续加载，错位会导致关卡显示与内容不符。"));
            }

            if (!info.IsLocalTest)
            {
                if (!StageResourceIndex.Instance.IsStageRegistered(config.stageId))
                {
                    var issue = New(info, StageIssueLevel.Error, "addressable.missing", "没有登记 Addressables",
                        "地址 " + info.Label + " 不在 Addressables 组 StageConfig 里，出包后选关列表看不到这一关" +
                        "（StageConfigLoader.LoadConfigs 遇到缺口直接停止加载）。可用「一键修复」上架，" +
                        "或在 Addressables 面板把该资产加进组 StageConfig、地址填 " + info.Label + "、标签 StartupPreload。");
                    issue.Fixable = true;
                    issue.SuggestedValue = "加进组 StageConfig：地址 " + info.Label + "、标签 StartupPreload";
                    output.Add(issue);
                }
            }
            else
            {
                output.Add(New(info, StageIssueLevel.Info, "localTest", "本地测试关",
                    "编号 " + config.stageId + " 视为本地测试关：不要求上架，也不参与批量操作。"));
            }
        }

        /// <summary>正式关（1..N）的编号连续性：有缺口时选关会在缺口处截断。</summary>
        public static void ValidateStageIdContinuity(List<StageInfo> stages, List<StageIssue> output)
        {
            if (stages == null || output == null)
                return;

            var ids = new List<int>();
            for (int i = 0; i < stages.Count; i++)
            {
                if (stages[i] != null && !stages[i].IsLocalTest && stages[i].StageId > 0)
                    ids.Add(stages[i].StageId);
            }

            if (ids.Count == 0)
                return;

            ids.Sort();
            int max = ids[ids.Count - 1];
            var present = new HashSet<int>(ids);
            var gaps = new List<int>();
            for (int id = 1; id <= max; id++)
            {
                if (!present.Contains(id))
                    gaps.Add(id);
            }

            if (gaps.Count > 0)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < gaps.Count && i < 12; i++)
                {
                    if (i > 0) sb.Append("、");
                    sb.Append("Stage").Append(gaps[i]);
                }
                if (gaps.Count > 12) sb.Append(" 等");

                output.Add(new StageIssue
                {
                    Level = StageIssueLevel.Error,
                    Code = "stageId.gap",
                    Title = "关卡编号不连续（缺 " + gaps.Count + " 个）",
                    Detail = "缺：" + sb + "。选关是按 Stage1、Stage2… 连续地址找的，遇到第一个缺口就停止加载" +
                             "（StageConfigLoader.LoadConfigs），所以缺口之后的关卡玩家永远看不到。"
                });
            }
        }
        #endregion

        #region 阵地
        private static void ValidateBasePosition(StageInfo info, int mapWidth, int mapHeight, List<StageIssue> output)
        {
            StageConfig config = info.Config;

            if (!config.hasFriendlyMainBaseGridPosition)
            {
                output.Add(New(info, StageIssueLevel.Warning, "base.disabled", "没有启用我方大本营",
                    "hasFriendlyMainBaseGridPosition = false：运行时不会生成我方大本营，" +
                    "而且「大本营被拆即败」的判定会整体失效（StageGoalTracker.cs:53）。"));
                return;
            }

            Vector2Int pos = config.friendlyMainBaseGridPosition;
            if (pos.x < 0 || pos.y < 0 || pos.x >= mapWidth || pos.y >= mapHeight)
            {
                var issue = New(info, StageIssueLevel.Error, "base.outOfMap", "我方大本营在运行时地图外",
                    "坐标 (" + pos.x + ", " + pos.y + ") 超出地图 " + mapWidth + "×" + mapHeight + "。");
                issue.Fixable = true;
                issue.SuggestedValue = "夹到 (" + Mathf.Clamp(pos.x, 0, mapWidth - 1) + ", " + Mathf.Clamp(pos.y, 0, mapHeight - 1) + ")";
                output.Add(issue);
            }

            if (config.enemyBaseDistance < 1)
            {
                var issue = New(info, StageIssueLevel.Error, "base.distance", "敌我基地距离无效",
                    "enemyBaseDistance = " + config.enemyBaseDistance + "，运行时会按 max(1, 该值) 算（StageGoalTracker.cs:196），实际距离与配置不符。");
                issue.Fixable = true;
                issue.SuggestedValue = "改成 1";
                output.Add(issue);
            }
            else
            {
                int enemyX = pos.x + config.enemyBaseDistance;
                if (enemyX >= mapWidth)
                {
                    output.Add(New(info, StageIssueLevel.Error, "base.enemyOutOfMap", "敌方大本营在运行时地图外",
                        "我方大本营 x=" + pos.x + " + 距离 " + config.enemyBaseDistance + " = " + enemyX + "，超出地图宽度 " + mapWidth +
                        "（StageGoalTracker.cs:196-201 直接按这个 x 生成，不做夹取）。"));
                }
            }
        }
        #endregion

        #region 玩法与胜负
        private static void ValidateWinLose(StageInfo info, StageResourceIndex index, int mapWidth, int mapHeight, List<StageIssue> output)
        {
            StageConfig config = info.Config;
            UserGlobalInfo.StageType type = config.stageType;

            switch (type)
            {
                case UserGlobalInfo.StageType.Attack:
                    if (config.attackTimeLimit <= 0f)
                    {
                        output.Add(New(info, StageIssueLevel.Warning, "win.attackNoLimit", "进攻关没有时限",
                            "attackTimeLimit = 0 表示不限时；进攻关是把「摧毁敌方大本营」当胜利条件，不限时意味着打不掉就永远不结束。"));
                    }
                    break;

                case UserGlobalInfo.StageType.Boss:
                    if (config.bossTimeLimit <= 0f)
                    {
                        output.Add(New(info, StageIssueLevel.Warning, "win.bossNoLimit", "Boss 关没有时限",
                            "bossTimeLimit = 0 表示不限时。"));
                    }
                    if (string.IsNullOrEmpty(config.bossPrefabKey))
                    {
                        output.Add(New(info, StageIssueLevel.Error, "win.bossMissing", "Boss 关没有配 Boss 预制体",
                            "bossPrefabKey 为空 → 运行时 Boss 不会生成（StageGoalTracker.cs:224-231），玩家永远无法获胜，只能等到超时判负。"));
                    }
                    break;

                default:
                    if (config.DefenseTime <= 0f)
                    {
                        output.Add(New(info, StageIssueLevel.Warning, "win.defenseNoLimit", "防守关没有时限",
                            "DefenseTime = 0 表示不限时，倒计时也不显示（StageGoalTracker.cs:81）。"));
                    }
                    break;
            }

            // Boss 关必须能生成 Boss；Boss 预制体键必须在注册表里
            if (type == UserGlobalInfo.StageType.Boss && !string.IsNullOrEmpty(config.bossPrefabKey) &&
                index.PrefabRegistryFound && !index.HasPrefab(config.bossPrefabKey))
            {
                output.Add(New(info, StageIssueLevel.Error, "win.bossKeyInvalid", "Boss 预制体键不在注册表里",
                    "bossPrefabKey = \"" + config.bossPrefabKey + "\" 不在 PrefabRegistry 中，出包后加载不到。"));
            }

            // 反向：不是 Boss 关却配了 Boss → 静默失效（Stage7 的现状）
            if (type != UserGlobalInfo.StageType.Boss && !string.IsNullOrEmpty(config.bossPrefabKey))
            {
                output.Add(New(info, StageIssueLevel.Warning, "win.bossUnused", "配了 Boss 预制体但玩法类型不是 Boss 关",
                    "bossPrefabKey = \"" + config.bossPrefabKey + "\" 只有在 stageType = Boss 时才会生成" +
                    "（StageGoalTracker.cs:64-79 的 switch），当前类型是「" + info.TypeLabel + "」，这个 Boss 永远不会出现。"));
            }

            if (type == UserGlobalInfo.StageType.Boss)
            {
                Vector2Int boss = config.bossGridPosition;
                if (boss.x < 0 || boss.y < 0 || boss.x >= mapWidth || boss.y >= mapHeight)
                {
                    var issue = New(info, StageIssueLevel.Error, "win.bossOutOfMap", "Boss 出生点在运行时地图外",
                        "bossGridPosition = (" + boss.x + ", " + boss.y + ")，超出地图 " + mapWidth + "×" + mapHeight + "。");
                    issue.Fixable = true;
                    issue.SuggestedValue = "夹到 (" + Mathf.Clamp(boss.x, 0, mapWidth - 1) + ", " + Mathf.Clamp(boss.y, 0, mapHeight - 1) + ")";
                    output.Add(issue);
                }
            }

            // 进攻/Boss 关要能赢，前提是敌方有队伍且队伍有种族
            if (type == UserGlobalInfo.StageType.Attack)
            {
                EnemyTeamConfig first = config.enemyTeams != null && config.enemyTeams.Count > 0 ? config.enemyTeams[0] : null;
                if (first == null || string.IsNullOrEmpty(first.raceId))
                {
                    output.Add(New(info, StageIssueLevel.Error, "win.noEnemyBase", "进攻关生成不出敌方大本营",
                        "敌方大本营取 enemyTeams[0] 的 raceId 去查种族的 MainBase 建筑（StageGoalTracker.cs:166-201）；" +
                        "当前" + (first == null ? "没有任何敌方队伍" : "第一支队伍的 raceId 为空") + "，运行时只会打一条警告并跳过 → 永远无法获胜。"));
                }
            }
        }
        #endregion

        #region 敌方队伍
        private static void ValidateEnemyTeams(StageInfo info, StageResourceIndex index, int mapWidth, int mapHeight, List<StageIssue> output)
        {
            StageConfig config = info.Config;
            List<EnemyTeamConfig> teams = config.enemyTeams;

            if (teams == null || teams.Count == 0)
            {
                if (config.stageType != UserGlobalInfo.StageType.Boss)
                {
                    output.Add(New(info, StageIssueLevel.Info, "team.none", "没有敌方队伍",
                        "本关不会有敌方 AI 出兵（Boss 关可以没有队伍）。"));
                }
                return;
            }

            if (teams.Count > MaxEnemyTeams)
            {
                output.Add(New(info, StageIssueLevel.Warning, "team.tooMany", "敌方队伍超过 4 支",
                    "当前 " + teams.Count + " 支。GameplayState 只给前 4 支队伍配置指挥官（队伍编号 2/4/6/8），多出来的不会生效。"));
            }

            var seenTeamIds = new HashSet<int>();
            for (int i = 0; i < teams.Count; i++)
            {
                EnemyTeamConfig team = teams[i];
                if (team == null)
                {
                    var issue = New(info, StageIssueLevel.Error, "team.null", "第 " + (i + 1) + " 支敌方队伍是空引用", "列表里有 null 条目。");
                    issue.Fixable = true;
                    issue.SuggestedValue = "删除这个空队伍";
                    output.Add(issue);
                    continue;
                }

                string prefix = "第 " + (i + 1) + " 支队伍：";

                if (team.teamId < 2)
                {
                    var issue = New(info, StageIssueLevel.Warning, "team.idClamped", prefix + "队伍编号会被夹到 2",
                        "teamId = " + team.teamId + "，运行时按 max(2, teamId) 处理（TeamCommander.cs:81），偶数队才是敌方。");
                    issue.Fixable = true;
                    issue.SuggestedValue = "teamId = " + Mathf.Max(2, team.teamId + (team.teamId % 2 == 0 ? 0 : 1));
                    output.Add(issue);
                }
                else if (!seenTeamIds.Add(team.teamId))
                {
                    output.Add(New(info, StageIssueLevel.Error, "team.idDup", prefix + "队伍编号重复",
                        "teamId = " + team.teamId + " 已经被前面的队伍用了；同编号会互相覆盖指挥官注册。"));
                }

                if (string.IsNullOrEmpty(team.raceId))
                {
                    output.Add(New(info, StageIssueLevel.Error, "team.noRace", prefix + "没有种族",
                        "raceId 为空 → 本队不会启用（GameplayState 要求 raceId 非空），也不会建任何建筑。"));
                }
                else if (index.Races.Count > 0 && index.GetRace(team.raceId) == null)
                {
                    output.Add(New(info, StageIssueLevel.Error, "team.raceUnknown", prefix + "种族不存在",
                        "raceId = \"" + team.raceId + "\" 在工程里找不到对应的 RaceDefinition 资产。"));
                }

                if (team.startGold <= 0 && team.baseGoldPerSecond <= 0f && (team.incomeGrowth == null || team.incomeGrowth.Count == 0))
                {
                    output.Add(New(info, StageIssueLevel.Warning, "team.noEconomy", prefix + "整局没有收入",
                        "起始金币 0、每秒产量 0、也没有增长阶段 → 这一队永远造不出任何东西。"));
                }

                if ((team.buildSlots == null || team.buildSlots.Count == 0) && team.maxBarracks <= 0 &&
                    team.maxDarkBarracks <= 0 && team.maxSentries <= 0)
                {
                    output.Add(New(info, StageIssueLevel.Warning, "team.noPlan", prefix + "没有任何建造计划",
                        "建筑槽位为空，且兵营/黑暗兵营/哨塔上限都是 0 → 这一队不会建任何建筑、也就不会出兵。"));
                }

                int midpoint = StageFacts.MidpointX(config);
                if (team.buildSlots != null)
                {
                    for (int s = 0; s < team.buildSlots.Count; s++)
                    {
                        EnemyBuildSlot slot = team.buildSlots[s];
                        if (slot == null)
                        {
                            var issue = New(info, StageIssueLevel.Error, "team.slotNull", prefix + "第 " + (s + 1) + " 个建筑槽位是空引用", "列表里有 null 槽位。");
                            issue.Fixable = true;
                            issue.SuggestedValue = "删除这个空槽位";
                            output.Add(issue);
                            continue;
                        }

                        if (slot.gridPosition.x < midpoint)
                        {
                            var issue = New(info, StageIssueLevel.Warning, "team.slotWrongHalf",
                                prefix + "第 " + (s + 1) + " 个槽位在玩家半场（会被跳过）",
                                "槽位 x = " + slot.gridPosition.x + "，小于半场分界 " + midpoint + "；" +
                                "敌方 AI 遇到 x < 分界的槽位会直接跳过（TeamCommander 的 MinBuildX 检查）。");
                            issue.Fixable = true;
                            issue.SuggestedValue = "把 x 改到 ≥ " + midpoint;
                            output.Add(issue);
                        }
                        else if (slot.gridPosition.x >= mapWidth || slot.gridPosition.y < 0 || slot.gridPosition.y >= mapHeight)
                        {
                            var issue = New(info, StageIssueLevel.Warning, "team.slotOutOfMap",
                                prefix + "第 " + (s + 1) + " 个槽位在地图外",
                                "坐标 (" + slot.gridPosition.x + ", " + slot.gridPosition.y + ") 超出地图 " + mapWidth + "×" + mapHeight + "，位置合法性检查会失败。");
                            issue.Fixable = true;
                            issue.SuggestedValue = "夹到 (" + Mathf.Clamp(slot.gridPosition.x, 0, mapWidth - 1) + ", " +
                                                   Mathf.Clamp(slot.gridPosition.y, 0, mapHeight - 1) + ")";
                            output.Add(issue);
                        }
                    }

                    // 占地重叠 / 间距不足：运行时会**直接不建造**这个槽位，是最容易被忽略的一类错误。
                    // 运行时判定链：TryBuildSlot 先试 ±positionJitter 抖动位置、再试槽位原坐标，
                    // 两者都要过 TryPlaceAt → HasBuildingNeighbor(..., margin = 1)（与其它建筑至少隔 1 格）
                    // 与逐格占位检查；都不过就设 _retrySlot 每 5 秒重试一次并打警告 ——
                    // 现象就是"这一格的哨塔/兵营永远不建"。占地大小按种族建筑的 occupySpace 实算。
                    // 半场 / 越界 / 空引用已由上面的规则单独报过，这里只报重叠这一类，避免同一槽位刷两条。
                    for (int s = 0; s < team.buildSlots.Count; s++)
                    {
                        string why;
                        if (StageLayout.TryCheckSlot(config, team, s, out why))
                            continue;
                        if (!StageLayout.IsOverlapReason(why))
                            continue;

                        var issue = New(info, StageIssueLevel.Error, "team.slotOverlap",
                            prefix + "第 " + (s + 1) + " 个槽位位置非法：" + why,
                            "运行时给这个槽位落建筑时先试 ±位置抖动、再试原坐标，两者都要满足「与其它建筑至少隔 1 格」" +
                            "（TeamCommander.TryPlaceAt → HasBuildingNeighbor，margin = 1），都不过就每 5 秒重试一次并打警告：" +
                            "这一格建筑实际上永远造不出来（玩家看到的就是「哨塔不建造」）。" +
                            "占地大小按种族建筑预制体的 occupySpace 实算，槽位坐标是建筑左下角。");
                        issue.Fixable = true;
                        issue.SuggestedValue = "挪到最近的合法格（一键修复），或用「建筑槽位 → 自动编排」重排整队";
                        output.Add(issue);
                    }
                }

                ValidateSlotCaps(info, team, i, output);

                Vector2Int home = team.homeGridPosition;
                if (home.x < 0 || home.y < 0 || home.x >= mapWidth || home.y >= mapHeight)
                {
                    var issue = New(info, StageIssueLevel.Warning, "team.homeOutOfMap", prefix + "阵地锚点在地图外",
                        "homeGridPosition = (" + home.x + ", " + home.y + ")，超出地图 " + mapWidth + "×" + mapHeight + "；" +
                        "回防统计与动态补建都以它为圆心，越界会让这一队的行为变得不可预期。");
                    issue.Fixable = true;
                    issue.SuggestedValue = "夹到 (" + Mathf.Clamp(home.x, 0, mapWidth - 1) + ", " +
                                           Mathf.Clamp(home.y, 0, mapHeight - 1) + ")";
                    output.Add(issue);
                }

                ValidateTroopUnlocks(info, index, team, i, output);
            }

            if (teams.Count > 1)
            {
                output.Add(New(info, StageIssueLevel.Warning, "team.onlyFirstHasBase", "只有第一支队伍会生成敌方大本营",
                    "敌方大本营固定取 enemyTeams[0] 的 raceId 与 friendX + enemyBaseDistance 的位置" +
                    "（StageGoalTracker.cs:166-201），第 2 支及以后的队伍只会用 homeGridPosition 作为阵地锚点。"));
            }
        }

        /// <summary>
        /// 槽位表里各类型槽位数量 vs 该类型上限。
        /// 运行时的上限是**硬约束**（TeamCommander.IsSlotTypeAtCap：槽位表决定「在哪、按什么顺序建」，
        /// 上限决定「最多几座」），所以"设计了 5 个黑暗兵营槽位、黑暗兵营上限却只有 2"这种配置，
        /// 多出来的 3 个槽位会一直等着、永远不开工 —— 这类矛盾只能在这里挑出来。
        /// 普通兵营 / 黑暗兵营 / 哨塔三种**分开数**：它们各占各的名额，互不影响。
        /// </summary>
        private static void ValidateSlotCaps(StageInfo info, EnemyTeamConfig team, int teamIndex, List<StageIssue> output)
        {
            if (team == null || team.buildSlots == null || team.buildSlots.Count == 0)
                return;

            int barracks, darkBarracks, sentries;
            CountSlotsByType(team, out barracks, out darkBarracks, out sentries);

            string prefix = "第 " + (teamIndex + 1) + " 支队伍：";
            AddCapIssue(info, output, prefix, "普通兵营", "team.slotOverCapBarracks", "兵营上限",
                barracks, team.maxBarracks);
            AddCapIssue(info, output, prefix, "黑暗兵营", "team.slotOverCapDark", "黑暗兵营上限",
                darkBarracks, team.maxDarkBarracks);
            AddCapIssue(info, output, prefix, "哨塔", "team.slotOverCapSentry", "哨塔上限",
                sentries, team.maxSentries);
        }

        /// <summary>一条「槽位比上限多」的警告；没超就不报。</summary>
        private static void AddCapIssue(StageInfo info, List<StageIssue> output, string prefix, string kind,
                                        string code, string capField, int slots, int cap)
        {
            if (slots <= cap)
                return;

            var issue = New(info, StageIssueLevel.Warning, code,
                prefix + kind + "槽位比上限多 " + (slots - cap) + " 个（多出来的不会开工）",
                "槽位表里有 " + slots + " 个" + kind + "槽位，" + capField + " = " + cap + "。" +
                "运行时的上限是硬约束（TeamCommander.IsSlotTypeAtCap：「槽位表决定在哪、按什么顺序建，" + capField +
                "决定最多几座」），超出上限的 " + (slots - cap) + " 个槽位不会开工 ——" +
                "位置留着不动，把上限调大后仍会建。两种改法：把「" + capField + "」提到 " + slots + "，或删掉多余的槽位。");
            issue.Fixable = true;
            issue.SuggestedValue = "把「" + capField + "」提到 " + slots;
            output.Add(issue);
        }

        /// <summary>按建筑类型数槽位表：普通兵营 = Barracks、黑暗兵营 = DarkBarracks（两者分开计数）。</summary>
        private static void CountSlotsByType(EnemyTeamConfig team, out int barracks, out int darkBarracks, out int sentries)
        {
            barracks = 0;
            darkBarracks = 0;
            sentries = 0;
            if (team == null || team.buildSlots == null)
                return;

            for (int s = 0; s < team.buildSlots.Count; s++)
            {
                EnemyBuildSlot slot = team.buildSlots[s];
                if (slot == null)
                    continue;

                switch (slot.buildingType)
                {
                    case BuildingType.Barracks: barracks++; break;
                    case BuildingType.DarkBarracks: darkBarracks++; break;
                    case BuildingType.Sentry: sentries++; break;
                }
            }
        }

        /// <summary>把三种上限分别提到对应类型的槽位数（只加不减、绝不动槽位）；返回改动条数。一键修复用。</summary>
        private static int AlignSlotCapsToSlotCount(EnemyTeamConfig team)
        {
            if (team == null)
                return 0;

            int barracks, darkBarracks, sentries;
            CountSlotsByType(team, out barracks, out darkBarracks, out sentries);

            int fixedCount = 0;
            if (barracks > team.maxBarracks) { team.maxBarracks = barracks; fixedCount++; }
            if (darkBarracks > team.maxDarkBarracks) { team.maxDarkBarracks = darkBarracks; fixedCount++; }
            if (sentries > team.maxSentries) { team.maxSentries = sentries; fixedCount++; }
            return fixedCount;
        }

        private static void ValidateTroopUnlocks(StageInfo info, StageResourceIndex index, EnemyTeamConfig team, int teamIndex, List<StageIssue> output)
        {
            List<EnemyTroopUnlock> unlocks = team.troopUnlocks;
            if (unlocks == null || unlocks.Count == 0)
                return;   // 空表 = 全兵种可用，是合法默认

            string prefix = "第 " + (teamIndex + 1) + " 支队伍：";
            float limit = info.MainTimeLimit;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int nullCount = 0;

            for (int i = 0; i < unlocks.Count; i++)
            {
                EnemyTroopUnlock unlock = unlocks[i];
                if (unlock == null)
                    continue;

                TroopDefinition troop = unlock.Troop;
                if (troop == null)
                {
                    nullCount++;
                    continue;
                }

                if (!seen.Add(troop.Id))
                {
                    output.Add(New(info, StageIssueLevel.Warning, "team.troopDup", prefix + "兵种白名单里有重复兵种",
                        "「" + troop.Id + "」出现了不止一次，后一条的解锁时间不起作用（按 Id 匹配，命中第一条即返回）。"));
                }

                if (limit > 0f && unlock.UnlockTime > limit)
                {
                    output.Add(New(info, StageIssueLevel.Warning, "team.troopTooLate", prefix + "有兵种的解锁时间晚于关卡时限",
                        "「" + troop.Id + "」解锁时间 " + unlock.UnlockTime.ToString("0.#") + " 秒，超过本关时限 " + limit.ToString("0.#") +
                        " 秒 → 本局永远出不来。"));
                }
            }

            if (nullCount > 0)
            {
                // 这条是硬错误：运行时 IsTroopUnlocked 会直接 unlock.Troop.Id，空条目会抛 NullReferenceException。
                var issue = New(info, StageIssueLevel.Error, "team.troopNull",
                    prefix + "兵种白名单里有 " + nullCount + " 个空条目",
                    "运行时按 unlock.Troop.Id 匹配（TeamCommander.cs:1137），空条目会抛 NullReferenceException，整支队伍的出兵判断都会中断。");
                issue.Fixable = true;
                issue.SuggestedValue = "删除这 " + nullCount + " 个空条目";
                output.Add(issue);
            }
        }
        #endregion

        #region 玩家经济
        private static void ValidatePlayerEconomy(StageInfo info, List<StageIssue> output)
        {
            StageConfig config = info.Config;

            if (!config.useIncomeCurve)
            {
                output.Add(New(info, StageIssueLevel.Info, "eco.stepMode", "玩家收入走场景时间阶梯",
                    "useIncomeCurve = false → 用场景 Coins 的 coinPerSec + phaseTimes/phaseCoinPerSec 阶梯" +
                    "（Coins.cs:35-54），关卡里的 baseGoldPerSecond / incomeGrowth 本关不生效。"));
                return;
            }

            if (config.baseGoldPerSecond <= 0f)
            {
                output.Add(New(info, StageIssueLevel.Warning, "eco.zeroBase", "收入曲线的基准值为 0",
                    "baseGoldPerSecond = 0 且第 0 秒没有任何阶段命中时，开局收入为 0（Coins.cs:136-158 的基准值就是它）。"));
            }

            List<IncomeGrowthPhase> phases = config.incomeGrowth;
            if (phases == null || phases.Count == 0)
            {
                output.Add(New(info, StageIssueLevel.Warning, "eco.noPhases", "启用了收入曲线但没有增长阶段",
                    "曲线退化成常数 baseGoldPerSecond = " + config.baseGoldPerSecond.ToString("0.#") + "，整局收入不变。"));
                return;
            }

            float limit = info.MainTimeLimit;
            for (int i = 0; i < phases.Count; i++)
            {
                IncomeGrowthPhase phase = phases[i];
                if (phase == null)
                {
                    var issue = New(info, StageIssueLevel.Error, "eco.phaseNull", "第 " + (i + 1) + " 个收入阶段是空引用", "列表里有 null 阶段。");
                    issue.Fixable = true;
                    issue.SuggestedValue = "删除这个空阶段";
                    output.Add(issue);
                    continue;
                }

                if (phase.stepInterval < 0.1f)
                {
                    var issue = New(info, StageIssueLevel.Warning, "eco.intervalClamped", "第 " + (i + 1) + " 个收入阶段的步长会被夹到 0.1 秒",
                        "stepInterval = " + phase.stepInterval + "，运行时按 max(0.1, stepInterval) 算（Coins.cs:151），实际增长比配置快。");
                    issue.Fixable = true;
                    issue.SuggestedValue = "stepInterval = 0.1";
                    output.Add(issue);
                }

                if (phase.endTime <= phase.startTime)
                {
                    output.Add(New(info, StageIssueLevel.Info, "eco.openEnded", "第 " + (i + 1) + " 个收入阶段持续到关卡结束",
                        "endTime ≤ startTime 表示不封口（Coins.cs:147-149），会一直按步长增长到本关结束。"));
                }

                if (limit > 0f && phase.startTime >= limit)
                {
                    output.Add(New(info, StageIssueLevel.Info, "eco.phaseBeyondLimit", "第 " + (i + 1) + " 个收入阶段在本关时限之后",
                        "阶段从 " + phase.startTime.ToString("0.#") + " 秒开始，但本关时限是 " + limit.ToString("0.#") + " 秒，本局用不到。"));
                }

                if (phase.stepInterval > 0.1f && phase.stepAmount <= 0f)
                {
                    output.Add(New(info, StageIssueLevel.Info, "eco.phaseNoAmount", "第 " + (i + 1) + " 个收入阶段每步增量为 0",
                        "stepAmount = 0，这个阶段不会带来任何增长。"));
                }
            }
        }
        #endregion

        #region 预摆对象
        private static void ValidateObjects(StageInfo info, StageResourceIndex index, int mapWidth, int mapHeight, List<StageIssue> output)
        {
            StageConfig config = info.Config;
            List<StageObjectData> objects = config.objects;

            if (objects == null || objects.Count == 0)
            {
                output.Add(New(info, StageIssueLevel.Info, "obj.empty", "没有任何预摆对象",
                    "本关除了玩家与敌方大本营之外不会实例化任何东西。"));
                return;
            }

            int outside = 0;
            int sideZero = 0;
            int badKey = 0;
            int emptyKey = 0;
            int emptyComponents = 0;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var badKeySamples = new List<string>();

            for (int i = 0; i < objects.Count; i++)
            {
                StageObjectData data = objects[i];
                if (data == null)
                    continue;

                Vector3 p = data.transform.position;
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;

                // 运行时按世界坐标直接实例化、不做任何吸附（StageObjectInstantiator.cs:68-70），
                // 所以"在地图外"就是真的在地图外。
                if (p.x < 0f || p.y < 0f || p.x >= mapWidth || p.y >= mapHeight)
                    outside++;

                if (string.IsNullOrEmpty(data.prefabKey))
                    emptyKey++;
                else if (index.PrefabRegistryFound && !index.HasPrefab(data.prefabKey))
                {
                    badKey++;
                    if (badKeySamples.Count < 5 && !badKeySamples.Contains(data.prefabKey))
                        badKeySamples.Add(data.prefabKey);
                }

                if (data.components == null || data.components.Count == 0)
                    emptyComponents++;
                else
                {
                    for (int c = 0; c < data.components.Count; c++)
                    {
                        var propData = data.components[c] as GameObjectPropertyData;
                        // side=0 会被当成敌方：TeamRules.IsEnemySide(0) == true（GameObjectProperty.cs:381），
                        // 且 ApplyEnemyLevels 按 side % 2 == 0 注入敌方等级（StageObjectInstantiator.cs:238）。
                        if (propData != null && propData.side == 0)
                        {
                            sideZero++;
                            break;
                        }
                    }
                }
            }

            if (outside > 0)
            {
                output.Add(New(info, StageIssueLevel.Warning, "obj.outOfMap",
                    "有 " + outside + " / " + objects.Count + " 个预摆对象落在运行时地图外",
                    "本关对象的世界坐标范围是 x ∈ [" + minX.ToString("0.##") + ", " + maxX.ToString("0.##") + "]，y ∈ [" +
                    minY.ToString("0.##") + ", " + maxY.ToString("0.##") + "]，而运行时地图是 " + mapWidth + "×" + mapHeight +
                    "（1 格 = 1 世界单位，有效范围 x ∈ [0, " + mapWidth + ")、y ∈ [0, " + mapHeight + ")）。" +
                    "实例化是按世界坐标直赋、不做吸附的，所以这些对象确实会生成在界外。"));
            }

            if (sideZero > 0)
            {
                output.Add(New(info, StageIssueLevel.Warning, "obj.sideZero",
                    "有 " + sideZero + " 个预摆对象的 side = 0（会被当成敌方）",
                    "side = 0 是偶数队，TeamRules.IsEnemySide(0) 为 true，运行时会按敌方处理并注入敌方等级" +
                    "（StageObjectInstantiator.cs:238）。中立的场景装饰物应改成奇数队（如 1）。"));
            }

            if (emptyKey > 0)
            {
                output.Add(New(info, StageIssueLevel.Warning, "obj.emptyKey", "有 " + emptyKey + " 个预摆对象没有预制体键",
                    "prefabKey 为空的对象在实例化时会被直接跳过（StageObjectInstantiator.cs:48-49）。"));
            }

            if (badKey > 0)
            {
                output.Add(New(info, StageIssueLevel.Error, "obj.badKey", "有 " + badKey + " 个预摆对象的预制体键不在注册表里",
                    "例如：" + string.Join("、", badKeySamples.ToArray()) +
                    "。运行时找不到预制体只会打一条警告并跳过，关卡内容会静默缺失。请重新导出该关卡或修正键名。"));
            }

            if (emptyComponents > 0 && emptyComponents == objects.Count)
            {
                output.Add(New(info, StageIssueLevel.Info, "obj.noComponents", "全部预摆对象都没有组件数据",
                    "本关所有对象的 components 都是空的，运行时不会注入任何属性（这可能是正常的，如图案装饰）。"));
            }
        }
        #endregion

        #region 资源键清单
        private static void ValidateResourceKeyLists(StageInfo info, StageResourceIndex index, List<StageIssue> output)
        {
            StageConfig config = info.Config;

            CheckKeyList(info, index, output, "预制体", "prefabs", StageResourceIndex.StageKeyKind.Prefab, config.prefabs);
            CheckKeyList(info, index, output, "Sprite", "sprites", StageResourceIndex.StageKeyKind.Sprite, config.sprites);
            CheckKeyList(info, index, output, "音频", "audios", StageResourceIndex.StageKeyKind.Audio, config.audios);
            CheckKeyList(info, index, output, "纹理", "textures", StageResourceIndex.StageKeyKind.Texture, config.textures);
            CheckKeyList(info, index, output, "动画片段", "animationClips", StageResourceIndex.StageKeyKind.AnimationClip, config.animationClips);
            CheckKeyList(info, index, output, "动画控制器", "animatorControllers", StageResourceIndex.StageKeyKind.AnimatorController, config.animatorControllers);
        }

        private static void CheckKeyList(StageInfo info, StageResourceIndex index, List<StageIssue> output,
            string label, string fieldName, StageResourceIndex.StageKeyKind kind, List<string> keys)
        {
            if (keys == null || keys.Count == 0)
                return;

            // 注册表没读到就不校验，避免误报。
            if (!index.HasRegistry(kind))
                return;

            var missing = new List<string>();
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.IsNullOrEmpty(keys[i]))
                    continue;
                if (!index.HasKey(kind, keys[i]))
                {
                    if (missing.Count < 6)
                        missing.Add(keys[i]);
                }
            }

            if (missing.Count == 0)
                return;

            int total = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                if (!string.IsNullOrEmpty(keys[i]) && !index.HasKey(kind, keys[i]))
                    total++;
            }

            output.Add(New(info, StageIssueLevel.Error, "key." + fieldName, label + "资源键有 " + total + " 个不在注册表里",
                "例如：" + string.Join("、", missing.ToArray()) +
                "。这六份清单是出包时按标签预载的依据，键不存在会导致加载失败。"));
        }
        #endregion

        #region 种族建筑在不在资源清单里
        /// <summary>
        /// 本关的 <c>prefabs</c> 清单有没有列出「运行时一定会去取的种族建筑」。
        ///
        /// 依据（都能在运行时代码里查到）：
        /// · 关卡的资源集合**只**来自 StageConfig 的六张清单 —— <c>ResourceManager.CoLoadStageResources</c>
        ///   遍历的是 prefabs / audios / textures / animationClips / animatorControllers / sprites
        ///   （ResourceManager.cs:627-683），**从来不读 stage.objects**。
        /// · 我方大本营按**玩家所选种族**现查种族资产的 MainBase 条目拿键
        ///   （StageObjectInstantiator.cs:119 与 203 的 ResolveRaceMainBaseKey）；
        ///   敌方大本营按 enemyTeams[0].raceId 查同一个条目；玩家兵营按所选种族查
        ///   （LoadingState.cs:72 → TroopPreloader.PreloadRaceTroops）。
        /// · 键没预载 → <c>ResourceManager.GetGameObject</c> 返回 null → 我方大本营那条
        ///   **LogError 并 return false** → <c>InstantiateStage</c> 整体返回 false →
        ///   LoadingState 只打一条 "Failed to instantiate stage objects." 就 yield break，
        ///   **进关看到的是一张空地图**（Stage2 的真实故障）。
        /// · 兵营 / 哨塔缺失不会中断进关，但 BuildingButton 会「回落默认建筑」（BuildingButton.cs:78），
        ///   本局兵种预载与建筑外观都会不对 —— 所以只报 Warning。
        ///
        /// 玩家是**运行时**才选种族的，所以清单得列全所有种族的建筑；Stage1 正是这么导出的
        /// （Mouse / Cat 各 4 个键）。
        /// </summary>
        private static void ValidateRaceBuildings(StageInfo info, StageResourceIndex index, List<StageIssue> output)
        {
            StageConfig config = info.Config;

            // 注册表没读到就不校验，避免误报
            if (!index.HasRegistry(StageResourceIndex.StageKeyKind.Prefab))
                return;

            bool missingBase;
            List<string> missing = MissingRaceBuildingKeys(config, out missingBase);
            if (missing.Count == 0)
                return;

            string keyList = string.Join("、", missing.ToArray());

            // **只有我方大本营会阻断进关**：TryInstantiateFriendlyMainBase 一失败，
            // InstantiateStage 就整体返回 false，LoadingState 直接 yield break。
            // 敌方大本营是打起来之后由 StageGoalTracker 造的，键缺了只是"生成不出敌方大本营"
            // （永远赢不了），**不会**让你进不去 —— 所以那种情况走下面的警告。
            // 两个条件缺一不可：少了 missingBase，一个"大本营在清单里、只缺兵营"的关卡会被误报成致命错误。
            if (missingBase && config.hasFriendlyMainBaseGridPosition)
            {
                var issue = New(info, StageIssueLevel.Error, "key.raceBase",
                    "本关要生成我方大本营，但它的种族建筑没在资源清单里（会进不去关）",
                    "缺：" + keyList + "。\n" +
                    "运行时的资源集合只来自本关的 prefabs / audios / … 六张清单（ResourceManager.cs:627-683），" +
                    "而大本营是按**玩家所选种族**现查种族资产的 MainBase 条目拿键的" +
                    "（StageObjectInstantiator.cs:119 / 203）。玩家是运行时才选种族的，清单里就得列全。" +
                    "键没预载 → GetGameObject 返回 null → TryInstantiateFriendlyMainBase 直接 LogError 并 return false " +
                    "→ 整个 InstantiateStage 返回 false → 只打一条「Failed to instantiate stage objects.」就终止，" +
                    "进关只有一张空地图。\n" +
                    "「一键修复」会把这些键补进 prefabs（只追加，不删不改）。");
                issue.Fixable = true;
                issue.SuggestedValue = "追加 " + missing.Count + " 个键：" + keyList;
                output.Add(issue);
                return;
            }

            string enemyNote = EnemyMainBaseMissing(config, index)
                ? "\n注意本关还会生成**敌方**大本营（取 enemyTeams[0].raceId 的 MainBase）—— " +
                  "缺了它不会阻断进关，但敌方大本营造不出来，本关永远无法获胜。"
                : string.Empty;

            var warning = New(info, StageIssueLevel.Warning, "key.raceBuildings",
                "资源清单里缺 " + missing.Count + " 个种族建筑预制体",
                "缺：" + keyList + "。\n" +
                "玩家是运行时才选种族的，兵营 / 哨塔按所选种族现查种族资产的 Buildings 表；" +
                "键没预载时 BuildingButton 会「回落默认建筑」（BuildingButton.cs:78）—— " +
                "本局兵种预载与建筑外观都会不对，但不会阻断进关。" + enemyNote + "\n" +
                "「一键修复」会把这些键补进 prefabs（只追加，不删不改）。");
            warning.Fixable = true;
            warning.SuggestedValue = "追加 " + missing.Count + " 个键：" + keyList;
            output.Add(warning);
        }

        /// <summary>
        /// 本关 prefabs 清单里缺的种族建筑键（按种族资产顺序、建筑条目顺序），已去重。
        /// 校验与一键修复共用它，避免两处逻辑走偏。
        /// </summary>
        /// <param name="missingBase">缺的里面有没有 MainBase（决定"缺了会不会进不去关"）。</param>
        public static List<string> MissingRaceBuildingKeys(StageConfig config, out bool missingBase)
        {
            missingBase = false;
            var result = new List<string>();
            if (config == null)
                return result;

            IReadOnlyList<RaceDefinition> races = StageResourceIndex.Instance.Races;
            if (races == null || races.Count == 0)
                return result;

            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (config.prefabs != null)
            {
                for (int i = 0; i < config.prefabs.Count; i++)
                {
                    if (!string.IsNullOrEmpty(config.prefabs[i]))
                        declared.Add(config.prefabs[i]);
                }
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int r = 0; r < races.Count; r++)
            {
                RaceDefinition race = races[r];
                if (race == null || race.Buildings == null)
                    continue;

                for (int b = 0; b < race.Buildings.Count; b++)
                {
                    RaceBuildingEntry entry = race.Buildings[b];
                    if (entry == null || entry.buildingType == BuildingType.None)
                        continue;

                    string key = entry.prefabKey;
                    if (string.IsNullOrEmpty(key) || declared.Contains(key) || !seen.Add(key))
                        continue;

                    if (entry.buildingType == BuildingType.MainBase)
                        missingBase = true;
                    result.Add(key);
                }
            }

            return result;
        }

        /// <summary>
        /// 把本关缺的种族建筑键**追加**进 prefabs，返回追加了几个。
        ///
        /// **只做加法**：清单里原有的键一个都不动、不删、不排序。
        /// 校验（<see cref="ValidateRaceBuildings"/>）、一键修复、以及「阵地」区块里那个即时提示按钮
        /// 共用这一处逻辑 —— 同一条修复写三遍，迟早只改一处。
        /// </summary>
        public static int AddMissingRaceBuildingKeys(StageConfig config)
        {
            bool missingBase;
            List<string> missing = MissingRaceBuildingKeys(config, out missingBase);
            if (missing.Count == 0)
                return 0;

            if (config.prefabs == null)
                config.prefabs = new List<string>();

            for (int i = 0; i < missing.Count; i++)
                config.prefabs.Add(missing[i]);

            return missing.Count;
        }

        /// <summary>进攻 / Boss 关要生成敌方大本营，它取 enemyTeams[0].raceId 的 MainBase —— 那个键缺了同样致命。</summary>
        private static bool EnemyMainBaseMissing(StageConfig config, StageResourceIndex index)
        {
            if (config.stageType == UserGlobalInfo.StageType.Defense)
                return false;

            List<EnemyTeamConfig> teams = config.enemyTeams;
            if (teams == null || teams.Count == 0 || teams[0] == null || string.IsNullOrEmpty(teams[0].raceId))
                return false;   // 这种情况由 win.noEnemyBase 那条规则负责报

            RaceDefinition race = index.GetRace(teams[0].raceId);
            if (race == null || race.Buildings == null)
                return false;

            for (int i = 0; i < race.Buildings.Count; i++)
            {
                RaceBuildingEntry entry = race.Buildings[i];
                if (entry == null || entry.buildingType != BuildingType.MainBase || string.IsNullOrEmpty(entry.prefabKey))
                    continue;

                return config.prefabs == null || !config.prefabs.Contains(entry.prefabKey);
            }

            return false;
        }
        #endregion

        #region 展示与奖励
        private static void ValidatePresentation(StageInfo info, StageResourceIndex index, List<StageIssue> output)
        {
            StageConfig config = info.Config;

            if (config.displayIcons != null && config.displayIcons.Count > MaxDisplayIcons)
            {
                output.Add(New(info, StageIssueLevel.Warning, "ui.tooManyIcons", "选关展示图超过 4 个",
                    "当前 " + config.displayIcons.Count + " 个，运行时会按从左到右居中横排，超过 4 个会挤出去。"));
            }

            if (config.icon == null)
            {
                output.Add(New(info, StageIssueLevel.Info, "ui.noIcon", "没有关卡图标",
                    "选关界面的关卡图标为空。"));
            }

            if (config.displayIcons != null)
            {
                int nullIcons = 0;
                for (int i = 0; i < config.displayIcons.Count; i++)
                {
                    if (config.displayIcons[i] == null)
                        nullIcons++;
                }
                if (nullIcons > 0)
                {
                    output.Add(New(info, StageIssueLevel.Warning, "ui.nullIcon", "选关展示图里有 " + nullIcons + " 个空位",
                        "空的展示位会让这一排图标错位。"));
                }
            }

            // 导游对话（StageConfig.guideDialogues）的体检已移到「对话可视化编辑器」（R1.31）：
            // 那份数据在那边有专门的编辑页与更完整的规则（含"运行时其实会跳过空文案"这类纠正过的口径）。
            // 这里不再重复检查，免得同一个问题在两处出现两种说法。

            if (config.VictoryGoldBars <= 0 && config.VictoryDiamonds <= 0)
            {
                output.Add(New(info, StageIssueLevel.Info, "reward.zero", "通关奖励都是 0",
                    "金条与钻石都是 0，通关后玩家不会有奖励结算。"));
            }

            // 普通/大师两种模式的数值关系（GameOverRewards 按「大师值 − 普通已领」补发差额）。
            if (config.CustomNormalReward)
            {
                int normalGold = config.NormalVictoryGoldBars;
                int normalDiamond = config.NormalVictoryDiamonds;
                string detail =
                    "本关勾了「普通模式单独填」：普通 " + normalGold + " 金条 / " + normalDiamond + " 钻石，" +
                    "大师 " + config.VictoryGoldBars + " 金条 / " + config.VictoryDiamonds + " 钻石。";

                if (normalGold > config.VictoryGoldBars || normalDiamond > config.VictoryDiamonds)
                {
                    output.Add(New(info, StageIssueLevel.Warning, "reward.normalAboveMaster", "普通模式奖励高于大师模式",
                        detail + " 大师模式发的是「大师值 − 普通已领的部分」，普通更高时大师那一档会补 0" +
                        "（也就是说大师难度打完拿不到额外奖励）。"));
                }
                else
                {
                    output.Add(New(info, StageIssueLevel.Info, "reward.customNormal", "普通模式用了自定义奖励",
                        detail + " 大师模式会补差额：" +
                        (config.VictoryGoldBars - normalGold) + " 金条 / " +
                        (config.VictoryDiamonds - normalDiamond) + " 钻石（普通没领过时发全量）。"));
                }
            }

            // 奖励模板（R1.29）：本关奖励与「本关类型」的模板不一致 → 提示（模板只是策划本机的默认值，
            // 个别关刻意不一样是允许的，所以只提示、不报警，也不自动改）。
            StageRewardTemplate template = LevelEditorPrefs.GetRewardTemplate(config.stageType);
            if (template.Valid && !StageEditorData.MatchesRewardTemplate(config, template))
            {
                output.Add(New(info, StageIssueLevel.Info, "reward.templateDiff",
                    "通关奖励与「" + StageRewardTemplate.TypeName(config.stageType) + "」模板不一致",
                    "本关：大师 " + config.VictoryGoldBars + " 金条 / " + config.VictoryDiamonds + " 钻石" +
                    (config.CustomNormalReward
                        ? "，普通自定义 " + config.NormalVictoryGoldBars + " / " + config.NormalVictoryDiamonds
                        : "，普通自动一半 " + config.NormalVictoryGoldBars + " / " + config.NormalVictoryDiamonds) +
                    "。模板：" + template.Describe() +
                    "。要统一就在右栏「通关奖励 → 奖励模板」里点「套用到本关」或「套用到所有" +
                    StageRewardTemplate.TypeName(config.stageType) + "」。"));
            }

            List<GlobalRuleDefinition> rules = config.GlobalRules;
            if (rules != null)
            {
                int nullRules = 0;
                for (int i = 0; i < rules.Count; i++)
                {
                    if (rules[i] == null)
                        nullRules++;
                }
                if (nullRules > 0)
                {
                    output.Add(New(info, StageIssueLevel.Error, "rule.null", "全局规则里有 " + nullRules + " 个空位",
                        "空的规则条目会让开局显示效果时取不到图标。"));
                }
            }

            int[] levels = { config.enemyBarracksLevel, config.enemyDarkBarracksLevel, config.enemySentryTowerLevel, config.enemyDefenseMagicLevel, config.enemyAttackMagicLevel };
            for (int i = 0; i < levels.Length; i++)
            {
                if (levels[i] < 1)
                {
                    output.Add(New(info, StageIssueLevel.Warning, "level.invalid", "有敌方等级小于 1",
                        "5 个敌方等级都带 [Min(1)]，小于 1 时等级缩放会退化成 1.0（LevelScale.Pow 对 ≤1 返回 1），实际强度与配置不符。"));
                    break;
                }
            }
        }
        #endregion

        #region 汇总 / 一键修复
        public static void Count(List<StageIssue> issues, out int errors, out int warnings, out int infos)
        {
            errors = warnings = infos = 0;
            if (issues == null)
                return;

            for (int i = 0; i < issues.Count; i++)
            {
                StageIssue issue = issues[i];
                if (issue == null)
                    continue;

                switch (issue.Level)
                {
                    case StageIssueLevel.Error: errors++; break;
                    case StageIssueLevel.Warning: warnings++; break;
                    default: infos++; break;
                }
            }
        }

        public static Dictionary<string, List<StageIssue>> GroupByStage(List<StageIssue> issues)
        {
            var map = new Dictionary<string, List<StageIssue>>(StringComparer.Ordinal);
            if (issues == null)
                return map;

            for (int i = 0; i < issues.Count; i++)
            {
                StageIssue issue = issues[i];
                if (issue == null)
                    continue;

                string key = string.IsNullOrEmpty(issue.StagePath) ? string.Empty : issue.StagePath;
                List<StageIssue> list;
                if (!map.TryGetValue(key, out list))
                {
                    list = new List<StageIssue>();
                    map[key] = list;
                }
                list.Add(issue);
            }
            return map;
        }

        /// <summary>某一关最严重的级别（用于卡片 / 列表上的角标）。找不到问题时返回 -1。</summary>
        public static int WorstLevelOf(List<StageIssue> issues, string stagePath)
        {
            if (issues == null || string.IsNullOrEmpty(stagePath))
                return -1;

            int worst = -1;
            for (int i = 0; i < issues.Count; i++)
            {
                StageIssue issue = issues[i];
                if (issue == null || !string.Equals(issue.StagePath, stagePath, StringComparison.Ordinal))
                    continue;

                int level = (int)issue.Level;
                if (level > worst)
                    worst = level;
            }
            return worst;
        }

        /// <summary>
        /// 一键修复：只做**确定安全**的修正，不删关卡、不改编号。
        /// 会动资源键的只有一处："把缺的种族建筑键**追加**进 prefabs"（只做加法）——
        /// 它直击"大本营预制体没预载 → InstantiateStage 返回 false → 进关只有一张空地图"。
        /// 会动 Addressables 的只有一处："把本关上架到组 StageConfig"（地址 Stage{编号} + 标签 StartupPreload）——
        /// 它直击"没上架 → 选关列表看不到这一关"；写的是分组资产、不碰游戏代码，写前自动备份。
        /// 返回修了几处。
        /// </summary>
        public static int ApplySafeFixes(StageInfo info, int mapWidth, int mapHeight)
        {
            if (info == null || info.Config == null)
                return 0;

            StageConfig config = info.Config;
            int fixedCount = 0;

            Undo.RecordObject(config, "关卡一键修复");

            // 1) 我方大本营坐标夹进地图
            if (config.hasFriendlyMainBaseGridPosition)
            {
                Vector2Int pos = config.friendlyMainBaseGridPosition;
                var clamped = new Vector2Int(Mathf.Clamp(pos.x, 0, mapWidth - 1), Mathf.Clamp(pos.y, 0, mapHeight - 1));
                if (clamped != pos)
                {
                    config.friendlyMainBaseGridPosition = clamped;
                    fixedCount++;
                }
            }

            // 2) 敌方基地距离至少 1
            if (config.enemyBaseDistance < 1)
            {
                config.enemyBaseDistance = 1;
                fixedCount++;
            }

            // 3) Boss 出生点夹进地图
            {
                Vector2Int boss = config.bossGridPosition;
                var clamped = new Vector2Int(Mathf.Clamp(boss.x, 0, mapWidth - 1), Mathf.Clamp(boss.y, 0, mapHeight - 1));
                if (clamped != boss)
                {
                    config.bossGridPosition = clamped;
                    fixedCount++;
                }
            }

            // 4) 五个敌方等级至少 1
            if (config.enemyBarracksLevel < 1) { config.enemyBarracksLevel = 1; fixedCount++; }
            if (config.enemyDarkBarracksLevel < 1) { config.enemyDarkBarracksLevel = 1; fixedCount++; }
            if (config.enemySentryTowerLevel < 1) { config.enemySentryTowerLevel = 1; fixedCount++; }
            if (config.enemyDefenseMagicLevel < 1) { config.enemyDefenseMagicLevel = 1; fixedCount++; }
            if (config.enemyAttackMagicLevel < 1) { config.enemyAttackMagicLevel = 1; fixedCount++; }

            // 5) 收入阶段步长至少 0.1（运行时会夹，配置与运行时对齐）
            if (config.incomeGrowth != null)
            {
                for (int i = 0; i < config.incomeGrowth.Count; i++)
                {
                    IncomeGrowthPhase phase = config.incomeGrowth[i];
                    if (phase != null && phase.stepInterval < 0.1f)
                    {
                        phase.stepInterval = 0.1f;
                        fixedCount++;
                    }
                }
            }

            // 6) 删掉收入阶段里的空条目
            if (config.incomeGrowth != null)
            {
                int removed = config.incomeGrowth.RemoveAll(p => p == null);
                fixedCount += removed;
            }

            // 7) 敌方队伍：编号合法化、空条目清理、空兵种条目清理、槽位夹到敌方半场
            if (config.enemyTeams != null)
            {
                int midpoint = StageFacts.MidpointX(config);

                config.enemyTeams.RemoveAll(t => t == null);

                var usedIds = new HashSet<int>();
                for (int i = 0; i < config.enemyTeams.Count; i++)
                {
                    EnemyTeamConfig team = config.enemyTeams[i];
                    if (team == null)
                        continue;

                    int wanted = Mathf.Max(2, team.teamId);
                    if (wanted % 2 != 0)
                        wanted++;   // 偶数队才是敌方
                    while (!usedIds.Add(wanted))
                        wanted += 2;

                    if (team.teamId != wanted)
                    {
                        team.teamId = wanted;
                        fixedCount++;
                    }

                    if (team.startGold < 0) { team.startGold = 0; fixedCount++; }
                    if (team.baseGoldPerSecond < 0f) { team.baseGoldPerSecond = 0f; fixedCount++; }

                    if (team.incomeGrowth != null)
                        fixedCount += team.incomeGrowth.RemoveAll(p => p == null);

                    // 空兵种条目会让运行时抛 NullReferenceException，删掉是安全且必要的
                    if (team.troopUnlocks != null)
                        fixedCount += team.troopUnlocks.RemoveAll(u => u == null || u.Troop == null);

                    if (team.buildSlots != null)
                    {
                        fixedCount += team.buildSlots.RemoveAll(s => s == null);

                        for (int s = 0; s < team.buildSlots.Count; s++)
                        {
                            EnemyBuildSlot slot = team.buildSlots[s];
                            if (slot == null)
                                continue;

                            if (slot.gridPosition.x < midpoint)
                            {
                                slot.gridPosition = new Vector2Int(midpoint, slot.gridPosition.y);
                                fixedCount++;
                            }

                            int x = Mathf.Clamp(slot.gridPosition.x, 0, mapWidth - 1);
                            int y = Mathf.Clamp(slot.gridPosition.y, 0, mapHeight - 1);
                            if (x != slot.gridPosition.x || y != slot.gridPosition.y)
                            {
                                slot.gridPosition = new Vector2Int(x, y);
                                fixedCount++;
                            }
                        }

                        // 盖到一起的槽位：只把冲突的那几个挪到最近的合法格，其余位置一个不动
                        // （保留手工摆位，不重排整队）。判定与「自动编排」共用 StageLayout，
                        // 与运行时 HasBuildingNeighbor(margin = 1) 同义。
                        fixedCount += StageLayout.FixOverlaps(config, team, 6);
                    }

                    Vector2Int home = team.homeGridPosition;
                    var clampedHome = new Vector2Int(Mathf.Clamp(home.x, 0, mapWidth - 1), Mathf.Clamp(home.y, 0, mapHeight - 1));
                    if (clampedHome != home)
                    {
                        team.homeGridPosition = clampedHome;
                        fixedCount++;
                    }

                    // 某类型槽位比该类型上限多：上限是硬约束（运行时 IsSlotTypeAtCap），多出来的槽位永远不开工。
                    // 修复 = 把上限提到槽位数（只加不减、绝不动槽位表）。
                    fixedCount += AlignSlotCapsToSlotCount(team);
                }
            }

            // 8) 把缺的种族建筑键补进 prefabs。
            //    **只追加、不删不改**：清单里已有的键（导出的兵种 / 弹道 / 矿石…）一个都不动。
            //    这是唯一一条会"改资源键"的安全修复，因为它只做加法，而且直击"进不去关"。
            fixedCount += AddMissingRaceBuildingKeys(config);

            // 9) 没上架就上架（写 Addressables 分组资产，不碰任何游戏代码）。
            //    地址 Stage{编号} + 标签 StartupPreload，依据见 StageResourceIndex.RegisterStage 的注释。
            //    本地测试关（编号 ≥ 880）不要求上架，跳过。
            if (!info.IsLocalTest && !StageResourceIndex.Instance.IsStageRegistered(config.stageId))
            {
                string registerMessage;
                if (StageResourceIndex.RegisterStage(info.Path, config.stageId, out registerMessage))
                {
                    fixedCount++;
                    Debug.Log("[关卡编辑器] 上架 " + info.Name + "：" + registerMessage);
                }
                else
                {
                    Debug.LogWarning("[关卡编辑器] 上架 " + info.Name + " 未完成：" + registerMessage);
                }
            }

            if (fixedCount > 0)
            {
                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }

            return fixedCount;
        }
        #endregion
    }
}
