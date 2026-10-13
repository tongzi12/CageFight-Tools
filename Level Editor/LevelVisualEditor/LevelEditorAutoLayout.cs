using System;
using System.Collections.Generic;
using UnityEngine;

namespace CageFightStageTool
{
    /// <summary>
    /// 建筑槽位自动编排 + 槽位合法性判定。**全部在编辑器侧完成，游戏代码一行不改。**
    ///
    /// 为什么需要它：运行时 TeamCommander 给每个槽位落建筑前会做四项检查，只要有一项不过就
    /// **不建造**——先试 ±positionJitter 的抖动位置，再试槽位原坐标，都不行就每 5 秒重试一次
    /// 并打一条「位置非法（越界/被占）」的警告。也就是说：坐标互相重叠/贴太近的槽位，
    /// 那一格建筑实际上永远造不出来（玩家看到的现象就是"哨塔不建"）。
    ///
    /// 四项检查的出处（本文件是**只读照抄**，改游戏代码时回来对一遍即可）：
    ///   · 半场     ：gridPosition.x ≥ MinBuildX，MinBuildX = (我方大本营X + 敌方大本营X) / 2
    ///                出处：TeamCommander.cs:325-336（MinBuildX）、:832-843、:869-871
    ///   · 建筑间距 ：与其它建筑占地至少隔 1 格（只统计建筑，游走的兵种不算）
    ///                出处：TeamCommander.cs:938-977（HasBuildingNeighbor，margin = 1）、:879-883
    ///   · 逐格占位 ：footprint 每一格都要在地图内且无人占用
    ///                出处：TeamCommander.cs:719-731（IsCellPlaceable）
    ///   · 占地基准 ：gridPosition 是**建筑左下角**，中心 = gridPosition + occupySpace / 2
    ///                出处：StageConfig.cs:52-53（字段注释）、TeamCommander.cs:893-897
    ///
    /// 占地大小取自种族建筑预制体上的 GameObjectProperty.occupySpace（运行时用的就是它），
    /// 读不到种族/预制体时才退回 <see cref="FallbackFootprint"/> 里按实测填的兜底值。
    /// </summary>
    public static class StageLayout
    {
        /// <summary>编排参数（界面上那几行输入框）。</summary>
        public struct Options
        {
            /// <summary>生成几个普通兵营槽位。</summary>
            public int barracks;
            /// <summary>生成几个黑暗兵营槽位（与普通兵营**分开计数**，互不占名额）。</summary>
            public int darkBarracks;
            /// <summary>生成几个哨塔槽位。</summary>
            public int sentries;
            /// <summary>建筑之间至少空几格（运行时硬性要求 ≥ 1）。</summary>
            public int gap;
            /// <summary>大本营占地之上额外可用行数。</summary>
            public int rowsAbove;
            /// <summary>大本营占地之下额外可用行数。</summary>
            public int rowsBelow;

            /// <summary>
            /// 按队伍配置给一套默认值：数量取"建筑上限"，间隔 1 格，上下各留 2 行。
            /// 黑暗兵营**不兜底成 1**：上限是 0 就说明这支队不要黑暗兵营，别硬塞槽位。
            /// </summary>
            public static Options Default(EnemyTeamConfig team)
            {
                Options o;
                o.barracks = Mathf.Max(1, team != null ? team.maxBarracks : 3);
                o.darkBarracks = Mathf.Max(0, team != null ? team.maxDarkBarracks : 2);
                o.sentries = Mathf.Max(1, team != null ? team.maxSentries : 2);
                o.gap = 1;
                o.rowsAbove = 2;
                o.rowsBelow = 2;
                return o;
            }
        }

        /// <summary>格子矩形（左下角 + 宽高），只用来做占地相交判断。</summary>
        private struct CellRect
        {
            public int x;
            public int y;
            public int w;
            public int h;

            public CellRect(Vector2Int position, Vector2Int size)
            {
                x = position.x;
                y = position.y;
                w = Mathf.Max(1, size.x);
                h = Mathf.Max(1, size.y);
            }

            public bool Intersects(CellRect other)
            {
                return x < other.x + other.w && other.x < x + w &&
                       y < other.y + other.h && other.y < y + h;
            }
        }

        /// <summary>
        /// 某个种族某种建筑的占地（格）。优先读种族建筑预制体上的 GameObjectProperty.occupySpace；
        /// 种族/预制体缺失时返回兜底值。
        /// </summary>
        public static Vector2Int Footprint(string raceId, BuildingType type)
        {
            StageResourceIndex index = StageResourceIndex.Instance;
            RaceDefinition race = index != null ? index.GetRace(raceId) : null;

            if (race != null && race.Buildings != null)
            {
                for (int i = 0; i < race.Buildings.Count; i++)
                {
                    RaceBuildingEntry entry = race.Buildings[i];
                    if (entry == null || entry.buildingType != type || string.IsNullOrEmpty(entry.prefabKey))
                        continue;

                    GameObject prefab = index.GetPrefab(entry.prefabKey);
                    GameObjectProperty prop = prefab != null ? prefab.GetComponent<GameObjectProperty>() : null;
                    if (prop != null)
                        return new Vector2Int(Mathf.Max(1, prop.occupySpace.x), Mathf.Max(1, prop.occupySpace.y));

                    break;   // 键在但预制体取不到：走兜底值，别再往下找同类型的第二个
                }
            }

            return FallbackFootprint(type);
        }

        /// <summary>
        /// 读不到种族建筑时的兜底占地，按 RemoteResource\Prefabricated body\Build 下 prefab 的实测值：
        /// Mouse/Cat Base1 = 4×2、Barracks1 = 2×2、Mouse Dark barracks1 = 2×2、
        /// Mouse Sentry tower1 = 2×1、Cat Sentry tower1 = 1×1（哨塔取大不取小，宁松不挤）。
        /// </summary>
        public static Vector2Int FallbackFootprint(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.MainBase:
                    return new Vector2Int(4, 2);
                case BuildingType.Sentry:
                    return new Vector2Int(2, 1);
                case BuildingType.Barracks:
                case BuildingType.DarkBarracks:
                    return new Vector2Int(2, 2);
                default:
                    return Vector2Int.one;
            }
        }

        /// <summary>
        /// 在大本营周围重新生成整套建筑槽位（覆盖该队现有槽位表）。成功返回 true，report 是给界面/日志看的一句话说明。
        ///
        /// 布局思路（R1.22 起定为「哨塔在兵营前面」的直线阵型，玩家从左侧来袭）：
        ///   · 兵营：从大本营**正前方的第一列**开始，一列列朝玩家方向铺（离基地由近到远）；
        ///           前方铺满了才回落到大本营的上/下两侧与后方 —— 那几个区域**只放兵营**。
        ///   · 黑暗兵营：也是兵营，**与普通兵营共用同一片候选区**（含上/下/后方回落区，不会跑到哨塔前面去），
        ///           只是**排在普通兵营之后** —— 前沿的列先给普通兵营，黑暗兵营补后面的列。
        ///           它的数量与普通兵营**分开计数**，运行时的「黑暗兵营上限」也是独立的一份。
        ///   · 哨塔：**只放在所有兵营的更前方** —— 取已放兵营里最靠前的那条左边缘，
        ///           哨塔的右边缘再加 gap 必须还在它左边；没有兵营时以大本营左边缘为界。
        ///           所以哨塔：① 永远不会出现在大本营后面或侧面，② 永远排在兵营前面（不混在兵营之间）。
        ///           前方空间实在不够时，**少放并如实报告**，绝不退回到兵营后面去凑数。
        ///   · 任何位置都保证：在地图内、x ≥ 半场分界、与其它建筑占地至少空 gap 格（gap ≥ 1）。
        /// </summary>
        public static bool TryArrange(StageConfig config, EnemyTeamConfig team, Options opt, out string report)
        {
            report = string.Empty;
            if (config == null || team == null)
            {
                report = "没有可编排的队伍。";
                return false;
            }

            int mapW = Mathf.Max(1, StageMap.Width);
            int mapH = Mathf.Max(1, StageMap.Height);
            int mid = StageFacts.MidpointX(config);
            Vector2Int anchor = team.homeGridPosition;
            Vector2Int baseFp = Footprint(team.raceId, BuildingType.MainBase);
            Vector2Int barFp = Footprint(team.raceId, BuildingType.Barracks);
            Vector2Int darkFp = Footprint(team.raceId, BuildingType.DarkBarracks);
            Vector2Int senFp = Footprint(team.raceId, BuildingType.Sentry);
            int gap = Mathf.Max(1, opt.gap);

            // 已占用：敌方大本营本身（它按 anchor 自动生成，占地就是种族大本营的 occupySpace）。
            var buildings = new List<CellRect>();
            buildings.Add(new CellRect(anchor, baseFp));

            int rowsBelow = Mathf.Max(0, opt.rowsBelow);
            int rowsAbove = Mathf.Max(0, opt.rowsAbove);
            int yMin = Mathf.Clamp(anchor.y - rowsBelow, 0, mapH - 1);
            int yMax = Mathf.Clamp(anchor.y + baseFp.y - 1 + rowsAbove, 0, mapH - 1);

            // 行顺序：以大本营竖中轴为中心上下交替，先贴基地再一格格向外。
            var rows = new List<int>();
            int centerY = anchor.y + baseFp.y / 2;
            if (centerY >= yMin && centerY <= yMax)
                rows.Add(centerY);
            for (int d = 1; d <= mapH; d++)
            {
                int down = centerY - d;
                int up = centerY + d;
                bool any = false;
                if (down >= yMin)
                {
                    rows.Add(down);
                    any = true;
                }
                if (up <= yMax)
                {
                    rows.Add(up);
                    any = true;
                }
                if (!any)
                    break;
            }

            // 候选 A：正前方（大本营左边缘之前，一列列朝玩家方向走）。兵营与哨塔共用这份候选。
            var front = new List<Vector2Int>();
            for (int x = anchor.x - gap - 1; x >= mid; x--)
            {
                for (int r = 0; r < rows.Count; r++)
                    front.Add(new Vector2Int(x, rows[r]));
            }

            // 候选 B：侧后方（大本营上下的行 → 大本营后方的列）—— **只给兵营当回落区**，哨塔不许来这里。
            var flank = new List<Vector2Int>();
            for (int r = 0; r < rows.Count; r++)
            {
                int y = rows[r];
                if (y >= anchor.y && y < anchor.y + baseFp.y)
                    continue;   // 大本营自己占的这两行，留给"后方"枚举
                for (int x = anchor.x + baseFp.x - 1; x >= mid; x--)
                    flank.Add(new Vector2Int(x, y));
            }
            for (int x = anchor.x + baseFp.x + gap; x < mapW; x++)
            {
                for (int r = 0; r < rows.Count; r++)
                    flank.Add(new Vector2Int(x, rows[r]));
            }

            // ① 先给哨塔**预留最前方的一段列**：兵营不许把整条前方吃光，否则哨塔一个都排不到前面去。
            //    预留列数按"一列能竖着放几个哨塔"折算（宁可多留一列）；前方列数不够时按上限截断。
            int frontCols = anchor.x - gap - 1 - mid + 1;
            if (frontCols < 0)
                frontCols = 0;

            int reserveCols = 0;
            if (opt.sentries > 0 && frontCols > 0)
            {
                int perCol = BuildingPerColumn(rows, senFp.y, gap);
                reserveCols = Mathf.Min(frontCols, Mathf.CeilToInt(opt.sentries / (float)Mathf.Max(1, perCol)));
            }
            int reserveMaxX = mid + reserveCols - 1;   // x ≤ 这一列的列都留给哨塔

            var barrackFront = new List<Vector2Int>();
            for (int i = 0; i < front.Count; i++)
            {
                if (reserveCols <= 0 || front[i].x > reserveMaxX)
                    barrackFront.Add(front[i]);
            }

            // ② 放兵营：从贴着大本营的第一列朝外铺（只用没被预留的那段），铺满了才回落到上/下两侧与后方。
            var barrackSlots = new List<EnemyBuildSlot>();
            int barrackGot = Fill(BuildingType.Barracks, barFp, Mathf.Max(0, opt.barracks), barrackFront, flank,
                buildings, gap, mid, mapW, mapH, barrackSlots);

            // ③ 放黑暗兵营：也是兵营，共用普通兵营那片候选区（含回落区），只是**排在普通兵营之后**
            //    —— 前沿的列先给普通兵营，黑暗兵营补后面的列。它与普通兵营**分开计数**，
            //    运行时的「黑暗兵营上限」也是独立的（TeamCommander.CountBarracks(true)），互不占名额。
            var darkSlots = new List<EnemyBuildSlot>();
            int darkGot = Fill(BuildingType.DarkBarracks, darkFp, Mathf.Max(0, opt.darkBarracks), barrackFront, flank,
                buildings, gap, mid, mapW, mapH, darkSlots);

            // ④ 再放哨塔：只放在**所有兵营（含黑暗兵营）的更前方**。
            //    界线 = 已放兵营里最靠前的那条左边缘（没有兵营时就是大本营左边缘）；
            //    哨塔的右边缘 + gap 必须还在界线左边 → 哨塔不可能出现在大本营后面/侧面，
            //    也不可能插在兵营之间或后面。前方放不下就少放，不再往兵营后面退。
            int frontLimit = anchor.x;
            for (int i = 0; i < barrackSlots.Count; i++)
            {
                int left = barrackSlots[i].gridPosition.x;
                if (left < frontLimit)
                    frontLimit = left;
            }
            for (int i = 0; i < darkSlots.Count; i++)
            {
                int left = darkSlots[i].gridPosition.x;
                if (left < frontLimit)
                    frontLimit = left;
            }

            int sentryMaxX = frontLimit - senFp.x - gap;
            var sentryCells = new List<Vector2Int>();
            for (int i = 0; i < front.Count; i++)
            {
                if (front[i].x > sentryMaxX)
                    continue;   // 这一列离兵营（或大本营）不够远，哨塔不许占
                sentryCells.Add(front[i]);   // front 本身按 x 由大到小，筛完仍是从紧贴界线向外
            }

            var sentrySlots = new List<EnemyBuildSlot>();
            int sentryGot = Fill(BuildingType.Sentry, senFp, Mathf.Max(0, opt.sentries), sentryCells, null,
                buildings, gap, mid, mapW, mapH, sentrySlots);

            if (sentryGot + barrackGot + darkGot == 0)
            {
                report = "没有找到合法位置：请检查阵地锚点是否在地图内、地图是否够宽、上下行数是否被压成 0。";
                return false;
            }

            var slots = new List<EnemyBuildSlot>();
            slots.AddRange(barrackSlots);
            slots.AddRange(darkSlots);
            slots.AddRange(sentrySlots);
            team.buildSlots = slots;

            report = "普通兵营 " + barrackGot + "/" + Mathf.Max(0, opt.barracks) +
                     "、黑暗兵营 " + darkGot + "/" + Mathf.Max(0, opt.darkBarracks) +
                     "、哨塔 " + sentryGot + "/" + Mathf.Max(0, opt.sentries) +
                     "；间隔 " + gap + " 格；行范围 y ∈ [" + yMin + ", " + yMax + "]" +
                     "；半场分界 x ≥ " + mid + "；哨塔只排在兵营前面（前沿界线 x ≤ " + sentryMaxX + "）。";
            if (barrackGot < Mathf.Max(0, opt.barracks))
                report += " 普通兵营没放完：可调大「上/下行」或减少普通兵营数。";
            if (darkGot < Mathf.Max(0, opt.darkBarracks))
                report += " 黑暗兵营没放完：它排在普通兵营之后、与普通兵营共用同一片区域 —— 可减少普通兵营数或调大「上/下行」。";
            if (sentryGot < Mathf.Max(0, opt.sentries))
                report += " 哨塔没放完：哨塔按规则只放在兵营前面，那一带空间不够 —— 可减少兵营数（把前方让给哨塔）、调大「上/下行」或减少哨塔数。";
            if (team.positionJitter > 0)
                report += " 注意：该队「位置抖动」= " + team.positionJitter +
                          " 格，运行时会先试抖动位置；本编排给的坐标是抖动失败后的精确落点，仍然有效。";

            return true;
        }

        /// <summary>
        /// 按「主选区优先、放不下再溢出到次选区」的顺序采集若干槽位。
        /// <paramref name="secondary"/> 传 null = **不许溢出**（哨塔就是这样：前方放不下宁可少放）。
        /// </summary>
        private static int Fill(BuildingType type, Vector2Int fp, int want,
                                List<Vector2Int> primary, List<Vector2Int> secondary,
                                List<CellRect> buildings, int gap, int mid, int mapW, int mapH,
                                List<EnemyBuildSlot> output)
        {
            if (want <= 0)
                return 0;

            int placed = Consume(type, fp, want, primary, buildings, gap, mid, mapW, mapH, output);
            if (placed < want && secondary != null)
                placed += Consume(type, fp, want - placed, secondary, buildings, gap, mid, mapW, mapH, output);
            return placed;
        }

        /// <summary>一列里竖着最多能放几个该建筑（同一列内也要满足 gap 间距）。</summary>
        private static int BuildingPerColumn(List<int> rows, int height, int gap)
        {
            var placed = new List<int>();
            for (int r = 0; r < rows.Count; r++)
            {
                bool free = true;
                for (int k = 0; k < placed.Count; k++)
                {
                    if (Mathf.Abs(rows[r] - placed[k]) < height + gap)
                    {
                        free = false;
                        break;
                    }
                }

                if (free)
                    placed.Add(rows[r]);
            }

            return placed.Count;
        }

        /// <summary>沿着候选格依次尝试放置，返回实际放下的数量（已放下的会记进 buildings，后续一律避让）。</summary>
        private static int Consume(BuildingType type, Vector2Int fp, int want, List<Vector2Int> cells,
                                   List<CellRect> buildings, int gap, int mid, int mapW, int mapH,
                                   List<EnemyBuildSlot> output)
        {
            int placed = 0;
            for (int i = 0; i < cells.Count && placed < want; i++)
            {
                Vector2Int pos = cells[i];
                if (pos.x < mid)
                    continue;   // 半场规则：x < 分界的槽位运行时会直接跳过

                var rect = new CellRect(pos, fp);
                if (!IsFree(rect, buildings, gap, mapW, mapH))
                    continue;

                buildings.Add(rect);
                output.Add(new EnemyBuildSlot { buildingType = type, gridPosition = pos });
                placed++;
            }

            return placed;
        }

        /// <summary>候选占地是否可用：每一格都在地图内，且与任何已有建筑至少空 gap 格。</summary>
        private static bool IsFree(CellRect want, List<CellRect> buildings, int gap, int mapW, int mapH)
        {
            if (want.x < 0 || want.y < 0 || want.x + want.w > mapW || want.y + want.h > mapH)
                return false;

            for (int i = 0; i < buildings.Count; i++)
            {
                CellRect b = buildings[i];
                var expanded = new CellRect(
                    new Vector2Int(b.x - gap, b.y - gap),
                    new Vector2Int(b.w + gap * 2, b.h + gap * 2));
                if (want.Intersects(expanded))
                    return false;
            }

            return true;
        }

        /// <summary>两个占地之间是否"间距不足 margin 格"（与运行时 HasBuildingNeighbor 同义）。</summary>
        private static bool TooClose(CellRect a, CellRect b, int margin)
        {
            var expanded = new CellRect(
                new Vector2Int(b.x - margin, b.y - margin),
                new Vector2Int(b.w + margin * 2, b.h + margin * 2));
            return a.Intersects(expanded);
        }

        /// <summary>
        /// 单个槽位是否满足运行时全部放置规则。不满足时给出人话原因（体检与一键修复共用）。
        /// 注意：只报"第一个"问题（越界 → 半场 → 大本营 → 其它槽位），够用且不会刷屏。
        /// </summary>
        public static bool TryCheckSlot(StageConfig config, EnemyTeamConfig team, int slotIndex, out string reason)
        {
            reason = null;
            if (config == null || team == null || team.buildSlots == null)
                return true;
            if (slotIndex < 0 || slotIndex >= team.buildSlots.Count)
                return true;

            EnemyBuildSlot slot = team.buildSlots[slotIndex];
            if (slot == null)
            {
                reason = "槽位是空引用";
                return false;
            }

            if (slot.buildingType == BuildingType.None)
            {
                reason = "建筑类型是「未指定」，运行时找不到预制体会跳过";
                return false;
            }

            int mapW = Mathf.Max(1, StageMap.Width);
            int mapH = Mathf.Max(1, StageMap.Height);
            int mid = StageFacts.MidpointX(config);
            Vector2Int fp = Footprint(team.raceId, slot.buildingType);
            var want = new CellRect(slot.gridPosition, fp);

            if (want.x < 0 || want.y < 0 || want.x + want.w > mapW || want.y + want.h > mapH)
            {
                reason = "占地 " + fp.x + "×" + fp.y + "（左下角 " + slot.gridPosition.x + ", " + slot.gridPosition.y +
                         "）超出地图 " + mapW + "×" + mapH;
                return false;
            }

            if (slot.gridPosition.x < mid)
            {
                reason = "在玩家半场（x = " + slot.gridPosition.x + " < 半场分界 " + mid + "）";
                return false;
            }

            if (slot.buildingType != BuildingType.MainBase)
            {
                var baseRect = new CellRect(team.homeGridPosition, Footprint(team.raceId, BuildingType.MainBase));
                if (TooClose(want, baseRect, 1))
                {
                    reason = "与大本营占地重叠（或间距不足 1 格）";
                    return false;
                }
            }

            if (config.hasFriendlyMainBaseGridPosition)
            {
                var friendRect = new CellRect(config.friendlyMainBaseGridPosition, Footprint(team.raceId, BuildingType.MainBase));
                if (TooClose(want, friendRect, 1))
                {
                    reason = "与我方大本营占地重叠（或间距不足 1 格）";
                    return false;
                }
            }

            for (int i = 0; i < team.buildSlots.Count; i++)
            {
                if (i == slotIndex)
                    continue;

                EnemyBuildSlot other = team.buildSlots[i];
                if (other == null || other.buildingType == BuildingType.None)
                    continue;

                var otherRect = new CellRect(other.gridPosition, Footprint(team.raceId, other.buildingType));
                if (TooClose(want, otherRect, 1))
                {
                    reason = "与第 " + (i + 1) + " 个槽位占地重叠（或间距不足 1 格）";
                    return false;
                }
            }

            return true;
        }

        /// <summary>这个槽位的问题是不是"重叠/间距"这一类（体检去重、一键修复筛选用）。</summary>
        public static bool IsOverlapReason(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                return false;
            return reason.IndexOf("重叠", StringComparison.Ordinal) >= 0 ||
                   reason.IndexOf("间距", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 在槽位原坐标周围逐圈找一个合法落点（半径由近到远）。一键修复用它**只挪冲突的槽位**，
        /// 不重排整队，手工摆过的位置尽量保住。找到返回 true，result 是建议坐标。
        /// </summary>
        public static bool TryFindFreeSlot(StageConfig config, EnemyTeamConfig team, int slotIndex, int maxRadius, out Vector2Int result)
        {
            result = Vector2Int.zero;
            if (config == null || team == null || team.buildSlots == null)
                return false;
            if (slotIndex < 0 || slotIndex >= team.buildSlots.Count)
                return false;

            EnemyBuildSlot slot = team.buildSlots[slotIndex];
            if (slot == null)
                return false;

            Vector2Int origin = slot.gridPosition;
            int radius = Mathf.Max(1, maxRadius);
            for (int r = 1; r <= radius; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy)) != r)
                            continue;

                        Vector2Int candidate = new Vector2Int(origin.x + dx, origin.y + dy);
                        slot.gridPosition = candidate;   // 临时借用同一套判定，取完立刻还原
                        string why;
                        bool ok = TryCheckSlot(config, team, slotIndex, out why);
                        slot.gridPosition = origin;
                        if (!ok)
                            continue;

                        result = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 只把「与其它建筑占地重叠 / 间距不足」的槽位挪到最近的合法格，其它槽位一个都不动
        /// （手工摆过的位置尽量保住）。返回被挪动的槽位数。
        /// 界面上的「只修冲突槽位」与体检的「一键修复」都走这里，保证两边判定完全一致。
        /// </summary>
        public static int FixOverlaps(StageConfig config, EnemyTeamConfig team, int searchRadius)
        {
            if (config == null || team == null || team.buildSlots == null)
                return 0;

            int moved = 0;
            for (int s = 0; s < team.buildSlots.Count; s++)
            {
                EnemyBuildSlot slot = team.buildSlots[s];
                if (slot == null)
                    continue;

                string why;
                if (TryCheckSlot(config, team, s, out why))
                    continue;
                if (!IsOverlapReason(why))
                    continue;   // 半场 / 越界这一类交给调用方原有的修复分支，别在这里抢

                Vector2Int target;
                if (!TryFindFreeSlot(config, team, s, searchRadius, out target))
                    continue;
                if (target == slot.gridPosition)
                    continue;

                slot.gridPosition = target;
                moved++;
            }

            return moved;
        }
    }
}
