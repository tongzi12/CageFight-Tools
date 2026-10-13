using System;
using System.Collections.Generic;

namespace CageFightStageTool
{
    /// <summary>参与统计的一个关卡：编号 + 这一关"可能出"的兵种 id 列表。</summary>
    public sealed class RosterStage
    {
        public int StageId;
        public readonly List<string> TroopIds = new List<string>();
        public string Label = string.Empty;      // 可选：显示用（例如 "Stage7 防御"）
    }

    /// <summary>一个兵种的出场统计（全部由 <see cref="TroopRosterMath"/> 算出来）。</summary>
    public struct TroopRosterStats
    {
        /// <summary>出现在多少个关卡里。</summary>
        public int Appearances;

        /// <summary>第一次出现在哪一关（0 = 没出现过）。</summary>
        public int FirstStageId;

        /// <summary>最近一次出现在哪一关（0 = 没出现过）。</summary>
        public int LastStageId;

        /// <summary>出现过的关卡编号（升序）。</summary>
        public List<int> StageIds;

        /// <summary>覆盖率 = 出现关卡数 ÷ 统计关卡数（0..1）。</summary>
        public double Coverage;

        /// <summary>
        /// **平均出现概率**（0..1）：每一关里它被抽中的概率按"候选池均分"算（1 ÷ 该关候选兵种数），
        /// 再把所有关卡（含没它的关，算 0）取平均。
        /// 这是**估算**：真实对局里 AI 还要看钱、威胁权重与解锁时间，不是纯随机。
        /// </summary>
        public double AverageChance;

        /// <summary>在它出现过的关卡里，最高的那个单关概率（1 ÷ 该关候选兵种数）；没出现过 = 0。</summary>
        public double PeakChance;

        /// <summary>最高单关概率发生在哪一关（0 = 无）。</summary>
        public int PeakStageId;

        public bool NeverAppeared { get { return Appearances <= 0; } }
    }

    /// <summary>
    /// 「兵种一览」子页面的**纯逻辑内核**：只吃"每关可能出的兵种 id 列表"，
    /// 算出每个兵种出现在哪些关卡、覆盖率、平均出现概率、第一次出现在第几关。
    ///
    /// 口径与界面上「本关出现的兵种」完全同源（`StageResourceIndex.GetStageTroops`）：
    ///   · 敌方队伍填了兵种白名单 → 这一关可能出的就是白名单里的兵；
    ///   · 白名单为空 → 与运行时一致按**整族预载**（该种族普通兵营 + 黑暗兵营的全部兵）。
    ///
    /// 放在这里（而不是写进窗口）的原因：这里面全是集合与算术，**能脱离 Unity 单测** ——
    /// 概率这种东西最怕"看起来对"，必须能一条条断言（见 README 的验证记录）。
    /// </summary>
    public static class TroopRosterMath
    {
        /// <summary>去重后的候选池大小（同一关里重复填了同一个兵只算一个）。</summary>
        public static int PoolSize(IList<string> troopIds)
        {
            return Distinct(troopIds).Count;
        }

        /// <summary>不区分大小写去重，保持原顺序。</summary>
        public static List<string> Distinct(IList<string> values)
        {
            var result = new List<string>();
            if (values == null)
                return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < values.Count; i++)
            {
                string value = values[i];
                if (string.IsNullOrEmpty(value))
                    continue;

                // 先裁再判空：白名单里可能出现"只有空格"的条目（手抖），
                // 它既不是兵种也不该把候选池撑大。
                value = value.Trim();
                if (value.Length == 0)
                    continue;

                if (seen.Add(value))
                    result.Add(value);
            }
            return result;
        }

        /// <summary>
        /// 算一个兵种的出场情况。
        /// </summary>
        /// <param name="stages">参与统计的关卡（**升序**，含没有敌方队伍的关）。</param>
        /// <param name="troopId">兵种 id（训练 ID，大小写不敏感）。</param>
        public static TroopRosterStats Compute(IList<RosterStage> stages, string troopId)
        {
            var stats = new TroopRosterStats { StageIds = new List<int>() };
            if (stages == null || stages.Count == 0 || string.IsNullOrEmpty(troopId))
                return stats;

            double chanceSum = 0d;
            int stageCount = 0;

            for (int i = 0; i < stages.Count; i++)
            {
                RosterStage stage = stages[i];
                if (stage == null)
                    continue;

                stageCount++;

                List<string> pool = Distinct(stage.TroopIds);
                if (pool.Count == 0)
                    continue;   // 这一关没有敌方队伍：谁都抽不到，但分母仍要算上它

                if (!Contains(pool, troopId))
                    continue;

                stats.StageIds.Add(stage.StageId);
                stats.Appearances++;
                if (stats.Appearances == 1)
                    stats.FirstStageId = stage.StageId;
                stats.LastStageId = stage.StageId;

                double chance = 1d / pool.Count;
                chanceSum += chance;
                if (chance > stats.PeakChance)
                {
                    stats.PeakChance = chance;
                    stats.PeakStageId = stage.StageId;
                }
            }

            if (stageCount > 0)
            {
                stats.Coverage = (double)stats.Appearances / stageCount;
                stats.AverageChance = chanceSum / stageCount;
            }

            return stats;
        }

        /// <summary>一次算完全部兵种（对每个 id 调 <see cref="Compute"/>）。顺序与传入的 id 列表一致。</summary>
        public static Dictionary<string, TroopRosterStats> ComputeAll(
            IList<RosterStage> stages, IList<string> troopIds)
        {
            var result = new Dictionary<string, TroopRosterStats>(StringComparer.OrdinalIgnoreCase);
            if (troopIds == null)
                return result;

            for (int i = 0; i < troopIds.Count; i++)
            {
                string id = troopIds[i];
                if (string.IsNullOrEmpty(id) || result.ContainsKey(id))
                    continue;

                result[id] = Compute(stages, id);
            }

            return result;
        }

        /// <summary>把 0..1 的比例写成百分比文字（界面上要的是"看起来是概率"的东西）。</summary>
        public static string Percent(double ratio)
        {
            if (ratio <= 0d)
                return "0%";
            if (ratio >= 1d)
                return "100%";

            double percent = ratio * 100d;
            if (percent >= 10d)
                return percent.ToString("0") + "%";
            if (percent >= 1d)
                return percent.ToString("0.#") + "%";
            return percent.ToString("0.##") + "%";
        }

        /// <summary>统计里出现过的全部关卡编号（升序去重），用于界面上的关卡范围显示。</summary>
        public static List<int> AllStageIds(IList<RosterStage> stages)
        {
            var ids = new List<int>();
            if (stages == null)
                return ids;

            for (int i = 0; i < stages.Count; i++)
            {
                if (stages[i] != null && !ids.Contains(stages[i].StageId))
                    ids.Add(stages[i].StageId);
            }

            ids.Sort();
            return ids;
        }

        /// <summary>某一关的候选池大小（没这一关返回 0）—— 界面上给"该关 N 个兵里选"用。</summary>
        public static int PoolSizeOfStage(IList<RosterStage> stages, int stageId)
        {
            if (stages == null)
                return 0;

            for (int i = 0; i < stages.Count; i++)
            {
                if (stages[i] != null && stages[i].StageId == stageId)
                    return PoolSize(stages[i].TroopIds);
            }
            return 0;
        }

        private static bool Contains(IList<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
