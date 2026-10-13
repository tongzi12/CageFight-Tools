using System;
using System.Collections.Generic;

namespace CageFightStageTool
{
    /// <summary>程序化构建关卡的难度档。</summary>
    public enum StageDifficulty
    {
        Low = 0,     // 低：让玩家把这一关当"练手"过
        Mid = 1,     // 中：按关卡编号的正常曲线
        High = 2,    // 高：压着玩家打
        Wall = 3     // 卡点：必须有意识地换阵容/升级才过得去
    }

    /// <summary>可参与"按概率均衡挑选"的候选兵种（由 Unity 侧从兵种表 + 出场统计喂进来）。</summary>
    public sealed class StageGenTroop
    {
        public string Id = string.Empty;
        public string Label = string.Empty;
        public int Tier = 1;          // AI 威胁阶级（1..3）
        public int UnlockLevel = 1;   // 兵营阶级（= 解锁等级）
        public int Upkeep;
        public float CurrentChance;   // 它现在在全部关卡里的平均出现概率（来自 TroopRosterMath）
        public bool Dark;             // 出自黑暗兵营

        /// <summary>难度档允许的阶级区间（低档不用高阶兵）。</summary>
        public bool AllowedFor(StageDifficulty difficulty)
        {
            int unlock = UnlockLevel < 1 ? 1 : (UnlockLevel > 3 ? 3 : UnlockLevel);
            switch (difficulty)
            {
                case StageDifficulty.Low: return unlock <= 2;
                case StageDifficulty.Mid: return unlock <= 3;
                default: return unlock >= 2;      // 高 / 卡点：主要用 2、3 阶
            }
        }
    }

    /// <summary>生成输入。</summary>
    public sealed class StageGenRequest
    {
        public int StageId = 1;
        public UserGlobalInfo.StageType StageType = UserGlobalInfo.StageType.Defense;
        public StageDifficulty Difficulty = StageDifficulty.Mid;
        public string EnemyRaceId = string.Empty;
        public int Seed = 20261012;

        /// <summary>本关的候选兵种（该种族兵营里能出的兵）。</summary>
        public readonly List<StageGenTroop> Candidates = new List<StageGenTroop>();

        /// <summary>是否把本关已经配了的东西也当参考（现值，用于对照表）。</summary>
        public int CurrentDefenseTime;
        public int CurrentEnemyStartGold;
        public int CurrentEnemyIncome;
        public int CurrentEnemyLevel;
        public int CurrentWhitelistCount;
    }

    /// <summary>生成出来的一套数值（还没写进任何资产）。</summary>
    public sealed class StageGenPlan
    {
        public StageDifficulty Difficulty;
        public int StageId;

        public int DefenseTime;
        public int PlayerStartGold;
        public int EnemyStartGold;
        public int EnemyIncome;
        public int EnemyLevel;          // 敌方建筑/魔法等级（5 个字段同值）
        public int PlayerLevelBonus;    // 提示用：玩家这一关大致该有的等级

        public int MaxBarracks;
        public int MaxDarkBarracks;
        public int MaxSentries;
        public int SlotBarracks;
        public int SlotDarkBarracks;
        public int SlotSentries;

        public int VictoryGoldBars;
        public int VictoryDiamonds;

        /// <summary>挑中的兵种 id（按解锁时间升序）。</summary>
        public readonly List<string> TroopIds = new List<string>();
        public readonly List<int> UnlockTimes = new List<int>();

        /// <summary>每个兵种的解锁时间（id → 秒）。</summary>
        public readonly Dictionary<string, int> UnlockTimeOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public readonly List<string> Notes = new List<string>();

        public int WhitelistCount { get { return TroopIds.Count; } }

        /// <summary>一套"能直接写进 StageConfig"的敌队数值摘要（给界面上的对照表用）。</summary>
        public string Describe()
        {
            return "时限 " + DefenseTime + "s　玩家起始金 " + PlayerStartGold +
                   "　敌方起始金 " + EnemyStartGold + "　敌方每秒 " + EnemyIncome +
                   "　敌方等级 " + EnemyLevel +
                   "　上限 " + MaxBarracks + "/" + MaxDarkBarracks + "/" + MaxSentries +
                   "　槽位 " + SlotBarracks + "/" + SlotDarkBarracks + "/" + SlotSentries +
                   "　白名单 " + WhitelistCount + " 个";
        }
    }

    /// <summary>
    /// **程序化构建关卡**的纯逻辑内核：按「关卡编号 + 难度档」算出一整套敌方数值、
    /// 槽位上限、白名单与解锁时间，并**按兵种当下的出场概率去均衡挑选**。
    ///
    /// 为什么把数字这么定（校准自工程里现有 21 关的真实数据）：
    ///   · 敌方建筑/魔法等级：实测 Stage1=1 → Stage19=13，约 `1 + round((id-1) × 0.65)`；
    ///   · 防御关时限：实测 100 → 500（Stage9 起基本顶到 500），约 `100 + 45 × (id-1)`，上限 500；
    ///   · 敌方起始金 2000 → 8000、每秒 100 → 200（随编号缓慢上升）；
    ///   · 槽位：Stage9 之后稳定在 **普通 4 / 黑暗 2 / 哨塔 5**，早期 1/0/2 长上来；
    ///   · 白名单：1 → 9 个，约 `1 + 0.4 × id`。
    /// 难度档就是在这条"基准曲线"上做**有界的**加减（并夹在实测包络里），
    /// 这样"低难度的一关"仍然是这一关该有的规模，而不是凭空造出一个不像本工程的关卡。
    ///
    /// 兵种挑选（"兼顾出现概率"）：候选池按**当前平均出现概率升序**排，优先挑出现得少的，
    /// 这样多生成几关之后整体分布会越来越平 —— 而不是反复堆在同一个兵种上。
    /// 同概率时用确定性随机（种子）打散，保证每次预览结果一致、可复现。
    /// </summary>
    public static class StageGenerator
    {
        public static string DifficultyName(StageDifficulty difficulty)
        {
            switch (difficulty)
            {
                case StageDifficulty.Low: return "低";
                case StageDifficulty.High: return "高";
                case StageDifficulty.Wall: return "卡点";
                default: return "中";
            }
        }

        // ── 实测包络（脚本从工程里 21 个 Stage 资产抠出来的区间）──
        //    时限 100..500　玩家金 2000..6000　敌方起始金 2000..8000　敌方每秒 100..200
        //    槽位 普通 1..4 / 黑暗 0..2 / 哨塔 2..5　白名单 1..9　敌方等级 1..13
        // 四个难度档**都**活在这个包络里；"强度类"（等级 / 经济）允许高、卡点往上顶，
        // 但不超过各自字段的硬上限（等级 20、每秒 400、起始金 20000）。
        public const int EnvelopeTimeMin = 100;
        public const int EnvelopeTimeMax = 500;
        public const int EnvelopePlayerGoldMin = 2000;
        public const int EnvelopePlayerGoldMax = 6000;
        public const int EnvelopeEnemyGoldMin = 2000;
        public const int EnvelopeEnemyGoldMax = 8000;
        public const int EnvelopeIncomeMin = 100;
        public const int EnvelopeIncomeMax = 200;
        public const int EnvelopeLevelMax = 13;
        public const int EnvelopeWhitelistMax = 9;

        // ── 基准曲线（校准自现有 21 关）──
        public static int BaseEnemyLevel(int stageId)
        {
            return Clamp(1 + (int)Math.Round((stageId - 1) * 0.65), 1, EnvelopeLevelMax);
        }

        public static int BaseDefenseTime(int stageId)
        {
            return Clamp(100 + 45 * (stageId - 1), EnvelopeTimeMin, EnvelopeTimeMax);
        }

        public static int BaseEnemyStartGold(int stageId)
        {
            return Clamp(2000 + 250 * (stageId - 1), EnvelopeEnemyGoldMin, EnvelopeEnemyGoldMax);
        }

        public static int BaseEnemyIncome(int stageId)
        {
            return Clamp(100 + 5 * (stageId - 1), EnvelopeIncomeMin, EnvelopeIncomeMax);
        }

        public static int BaseSlotBarracks(int stageId)
        {
            if (stageId <= 1) return 1;
            if (stageId <= 2) return 2;
            if (stageId <= 4) return 3;
            return 4;
        }

        public static int BaseSlotDarkBarracks(int stageId)
        {
            if (stageId <= 3) return 0;
            if (stageId <= 8) return 1;
            return 2;
        }

        public static int BaseSlotSentries(int stageId)
        {
            return Clamp(2 + (int)Math.Round(stageId / 4.0), 2, 5);
        }

        public static int BaseWhitelistCount(int stageId)
        {
            return Clamp((int)Math.Round(1 + stageId * 0.4), 1, 9);
        }

        /// <summary>算一整套数值。不碰任何资产。</summary>
        public static StageGenPlan Build(StageGenRequest request)
        {
            var plan = new StageGenPlan();
            if (request == null)
                return plan;

            request.StageId = Clamp(request.StageId, 1, 999);
            plan.StageId = request.StageId;
            plan.Difficulty = request.Difficulty;

            int id = request.StageId;
            bool defense = request.StageType == UserGlobalInfo.StageType.Defense;
            bool boss = request.StageType == UserGlobalInfo.StageType.Boss;

            // ── 难度档的系数（低 → 卡点，单调）──
            double incomeFactor, goldFactor, playerGoldFactor, timeFactor, levelOffset, whitelistFactor;
            int slotBonus;
            switch (request.Difficulty)
            {
                case StageDifficulty.Low:
                    incomeFactor = 0.70; goldFactor = 0.70; playerGoldFactor = 1.50;
                    timeFactor = 0.80; levelOffset = -2; whitelistFactor = 0.60; slotBonus = -1;
                    break;
                case StageDifficulty.High:
                    incomeFactor = 1.35; goldFactor = 1.30; playerGoldFactor = 0.70;
                    timeFactor = 1.25; levelOffset = 2; whitelistFactor = 1.30; slotBonus = 1;
                    break;
                case StageDifficulty.Wall:
                    incomeFactor = 1.70; goldFactor = 1.60; playerGoldFactor = 0.55;
                    timeFactor = 1.50; levelOffset = 4; whitelistFactor = 2.00; slotBonus = 2;
                    break;
                default:
                    incomeFactor = 1.00; goldFactor = 1.00; playerGoldFactor = 1.00;
                    timeFactor = 1.00; levelOffset = 0; whitelistFactor = 1.00; slotBonus = 0;
                    break;
            }

            plan.EnemyLevel = Clamp((int)Math.Round(BaseEnemyLevel(id) + levelOffset), 1, 20);
            plan.PlayerLevelBonus = Clamp(plan.EnemyLevel - 2, 1, 20);

            // 强度类：低/中夹在实测区间里；高/卡点允许往上顶，但不超字段硬上限
            bool soft = request.Difficulty == StageDifficulty.Low || request.Difficulty == StageDifficulty.Mid;
            int goldCeil = soft ? EnvelopeEnemyGoldMax : 20000;
            int incomeCeil = soft ? EnvelopeIncomeMax : 400;

            plan.EnemyStartGold = Clamp(RoundTo(BaseEnemyStartGold(id) * goldFactor, 500),
                EnvelopeEnemyGoldMin, goldCeil);
            plan.EnemyIncome = Clamp(RoundTo(BaseEnemyIncome(id) * incomeFactor, 10),
                EnvelopeIncomeMin, incomeCeil);

            // 玩家起始金：实测 2000..6000，四个难度档都待在这个区间里
            // （低难度不是"给你更多钱"，而是"敌人更弱"；把玩家金也拉出去就变成另一个游戏了）
            int basePlayerGold = Clamp(2000 + 500 * ((id - 1) / 3), EnvelopePlayerGoldMin, EnvelopePlayerGoldMax);
            plan.PlayerStartGold = Clamp(RoundTo(basePlayerGold * playerGoldFactor, 500),
                EnvelopePlayerGoldMin, EnvelopePlayerGoldMax);

            // 时限：**四个档都不越出实测区间（100..500）**。卡点的压迫感来自等级、经济与解锁节奏，
            // 不是把一局拉到 12 分钟 —— 工程里最长的一关就是 500s。
            int baseTime = defense ? BaseDefenseTime(id) : Math.Max(120, BaseDefenseTime(id) / 2);
            plan.DefenseTime = Clamp(RoundTo(baseTime * timeFactor, 10), EnvelopeTimeMin, EnvelopeTimeMax);

            // 槽位与上限：槽位决定"建在哪"，上限决定"最多几座"；上限必须 ≥ 槽位。
            // **上限夹在工程实测包络里（普通 4 / 黑暗 2 / 哨塔 5）**：现有 21 关最大的就是 4/2/5，
            // 难度档再往上加也不会超 —— 难度的主要杠杆是经济、等级与解锁节奏，
            // 而不是把兵营数量堆到工程里从没出现过的规模（那样出来的关卡不像本工程的关）。
            const int EnvelopeBarracks = 4;
            const int EnvelopeDarkBarracks = 2;
            const int EnvelopeSentries = 5;

            // 槽位下限：**任何难度档都得留下能打的最低配置**（至少 1 个兵营 + 2 个哨塔），
            // 否则"低难度"会把早期关卡压成一座哨塔，连出兵的地方都没有。
            plan.SlotBarracks = Clamp(BaseSlotBarracks(id) + slotBonus, 1, EnvelopeBarracks);
            plan.SlotDarkBarracks = Clamp(BaseSlotDarkBarracks(id) + slotBonus, 0, EnvelopeDarkBarracks);
            plan.SlotSentries = Clamp(BaseSlotSentries(id) + slotBonus, 2, EnvelopeSentries);

            plan.MaxBarracks = plan.SlotBarracks;
            plan.MaxDarkBarracks = plan.SlotDarkBarracks;
            plan.MaxSentries = plan.SlotSentries;

            // 奖励：与关卡编号同向增长（现有实测 30/20 → 100/10 量级）
            plan.VictoryGoldBars = Clamp(RoundTo(20 + 8 * id, 5), 10, 400);
            plan.VictoryDiamonds = Clamp(10 + (id / 3) * 5, 5, 200);

            // ── 白名单：数量 + 具体兵种 ──
            var picked = PickTroops(request, whitelistFactor);
            AssignUnlockTimes(plan, picked, request);

            // ── 说明（界面上直接显示，便于核对"为什么是这个数"）──
            plan.Notes.Add("难度档「" + DifficultyName(request.Difficulty) + "」= 基准曲线 × 系数（敌方收入 ×" +
                           incomeFactor.ToString("0.##") + "、起始金 ×" + goldFactor.ToString("0.##") +
                           "、玩家金 ×" + playerGoldFactor.ToString("0.##") + "、时限 ×" + timeFactor.ToString("0.##") +
                           "、等级 " + (levelOffset >= 0 ? "+" : "") + levelOffset + "）");
            plan.Notes.Add("基准（第 " + id + " 关）= 等级 " + BaseEnemyLevel(id) + "、时限 " + BaseDefenseTime(id) +
                           "s、起始金 " + BaseEnemyStartGold(id) + "、每秒 " + BaseEnemyIncome(id) +
                           "、槽位 " + BaseSlotBarracks(id) + "/" + BaseSlotDarkBarracks(id) + "/" + BaseSlotSentries(id));
            plan.Notes.Add("槽位上限夹在工程实测包络内（普通 4 / 黑暗 2 / 哨塔 5）：难度靠经济、等级与解锁节奏，" +
                           "不靠把兵营堆到工程里从没出现过的规模。");
            if (boss)
                plan.Notes.Add("Boss 关：敌方经济与槽位仍然会给，但真正决定胜负的是 Boss 预制体（在右栏「关卡」里配）。");
            if (plan.DefenseTime > 500)
                plan.Notes.Add("时限超过了工程里现有最长的一关（500s）—— 卡点难度本来就是要有压迫感，确认这是你要的。");

            return plan;
        }

        /// <summary>
        /// 按"当前出现概率"均衡挑选兵种：出现得少的优先，同概率用种子打散。
        /// 数量按难度档在基准白名单数上做系数，并夹在 1..9（与现有实测一致）。
        /// </summary>
        private static List<StageGenTroop> PickTroops(StageGenRequest request, double whitelistFactor)
        {
            var allowed = new List<StageGenTroop>();
            for (int i = 0; i < request.Candidates.Count; i++)
            {
                StageGenTroop troop = request.Candidates[i];
                if (troop == null || string.IsNullOrEmpty(troop.Id))
                    continue;
                if (!troop.AllowedFor(request.Difficulty))
                    continue;
                if (Contains(allowed, troop.Id))
                    continue;

                allowed.Add(troop);
            }

            // 候选不够就放宽到全部（低难度也可能某族只有高阶兵）
            if (allowed.Count == 0)
            {
                for (int i = 0; i < request.Candidates.Count; i++)
                {
                    StageGenTroop troop = request.Candidates[i];
                    if (troop != null && !string.IsNullOrEmpty(troop.Id) && !Contains(allowed, troop.Id))
                        allowed.Add(troop);
                }
            }

            int wanted = Clamp((int)Math.Round(BaseWhitelistCount(request.StageId) * whitelistFactor), 1, 9);
            wanted = Math.Min(wanted, allowed.Count);

            // 均衡规则：先按"当前出现概率"升序，再按 id 稳定排序；
            // 然后用种子在**概率相同的组内**打散，避免每次都挑到同一批。
            allowed.Sort((a, b) =>
            {
                int c = a.CurrentChance.CompareTo(b.CurrentChance);
                if (c != 0) return c;
                return string.CompareOrdinal(a.Id, b.Id);
            });

            var rng = new Random(request.Seed);
            var result = new List<StageGenTroop>();
            for (int i = 0; i < allowed.Count && result.Count < wanted; i++)
            {
                // 同一概率段内（相差不到 1%）随机决定先拿谁
                int j = i;
                while (j + 1 < allowed.Count &&
                       Math.Abs(allowed[j + 1].CurrentChance - allowed[i].CurrentChance) < 0.01 &&
                       result.Count + (j - i + 1) <= wanted)
                {
                    j++;
                }

                int pick = j > i ? i + rng.Next(j - i + 1) : i;
                result.Add(allowed[pick]);
                if (pick != i)
                {
                    StageGenTroop temp = allowed[i];
                    allowed[i] = allowed[pick];
                    allowed[pick] = temp;
                }
            }

            return result;
        }

        /// <summary>解锁时间：按挑中的顺序错开，最后一个不晚于时限（否则那一兵种本局出不来）。</summary>
        private static void AssignUnlockTimes(StageGenPlan plan, List<StageGenTroop> picked, StageGenRequest request)
        {
            int count = picked.Count;
            if (count == 0)
            {
                plan.Notes.Add("没有可用的候选兵种：白名单留空（运行时按「整族预载」处理，敌方会出该族全部兵）。");
                return;
            }

            // 第 1 个开局就能出，后面的均匀铺在时限的前 60% 里
            int span = Math.Max(0, (int)(plan.DefenseTime * 0.6));
            int step = count > 1 ? Math.Max(10, span / (count - 1)) : 0;

            for (int i = 0; i < count; i++)
            {
                int unlock = i * step;
                if (plan.DefenseTime > 0)
                    unlock = Math.Min(unlock, Math.Max(0, plan.DefenseTime - 10));   // 留出 10 秒能真的出场

                StageGenTroop troop = picked[i];
                plan.TroopIds.Add(troop.Id);
                plan.UnlockTimes.Add(unlock);
                plan.UnlockTimeOf[troop.Id] = unlock;
            }

            plan.Notes.Add("白名单挑了 " + count + " 个兵种（按当前出现概率**从低到高**挑，优先补齐出场少的）：" +
                           string.Join("、", plan.TroopIds.ToArray()));
            if (step > 0)
                plan.Notes.Add("解锁时间每 " + step + " 秒放一个（最后一个 " + plan.UnlockTimes[count - 1] +
                               "s，时限 " + plan.DefenseTime + "s）—— 错开是为了让压力「逐段上来」，而不是一开局全涌出来。");
        }

        private static bool Contains(List<StageGenTroop> list, string id)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Id, id, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private static int RoundTo(double value, int unit)
        {
            if (unit <= 1)
                return (int)Math.Round(value);
            return (int)Math.Round(value / unit) * unit;
        }
    }
}
