using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightUnitTool
{
    public enum UnitIssueLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>一条兵种体检结果。</summary>
    public sealed class UnitIssue
    {
        public UnitIssueLevel Level;
        public string Code = string.Empty;
        public string Title = string.Empty;
        public string Detail = string.Empty;
        public int RecordIndex = -1;
        public string UnitLabel = string.Empty;
        public string SuggestedValue;

        public bool Fixable;
        public bool IsCatalogLevel { get { return RecordIndex < 0; } }

        public Color LevelColor
        {
            get
            {
                if (Level == UnitIssueLevel.Error) return UnitEditorUI.Error;
                if (Level == UnitIssueLevel.Warning) return UnitEditorUI.Warn;
                return UnitEditorUI.Muted;
            }
        }
    }

    /// <summary>
    /// 兵种体检：
    /// 1) 数据完整性（预制体/训练配置/资源键是否存在）；
    /// 2) 数值合理性（HP、攻速、射程、移速为 0 之类会让单位在场上失效的配置）；
    /// 3) 平衡离群（同阶内 HP / 攻击力明显偏离中位数）。
    /// </summary>
    public static class UnitValidator
    {
        /// <summary>同阶离群判定：偏离中位数超过该比例就提示。</summary>
        private const float OutlierRatio = 0.6f;

        public static List<UnitIssue> Validate(UnitCatalog catalog)
        {
            var issues = new List<UnitIssue>();
            if (catalog == null)
                return issues;

            UnitResourceIndex index = UnitResourceIndex.Instance;

            if (!index.RegistryFound(UnitResKind.Prefab))
                Add(issues, UnitIssueLevel.Error, "U-REG", "找不到 PrefabRegistry", "未在 " + index.RegistryPath(UnitResKind.Prefab) + " 找到预制体注册表，单位预制体无法校验。");
            if (!index.RegistryFound(UnitResKind.Sprite))
                Add(issues, UnitIssueLevel.Error, "U-REG", "找不到 SpriteRegistry", "未在 " + index.RegistryPath(UnitResKind.Sprite) + " 找到精灵注册表，图标与动画贴图无法校验。");

            var prefabKeyOwners = new Dictionary<string, int>(StringComparer.Ordinal);
            var unitIdOwners = new Dictionary<string, int>(StringComparer.Ordinal);

            // 同一轮体检里已经建议出去的训练 ID：防止"两个重复条目被改成同一个新 id"
            var reservedTrainingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var barracksTierMaxHp = new Dictionary<int, List<float>>();
            var barracksTierAtk = new Dictionary<int, List<float>>();

            for (int i = 0; i < catalog.Count; i++)
            {
                UnitRecord record = catalog.Records[i];
                string label = string.IsNullOrEmpty(record.Name) ? record.Id : record.Name;

                if (!record.HasPrefab)
                {
                    Add(issues, UnitIssueLevel.Error, "U-PREFAB-MISSING", "找不到单位预制体",
                        "训练配置里的 prefabKey（" + Quote(record.PrefabName) + "）在 PrefabRegistry 里找不到对应预制体，玩家训练出来会是空。" +
                        "请确认预制体已打包进注册表。", i, label);
                    continue;
                }

                UnitStats stats = catalog.GetStats(record);
                if (!stats.Valid)
                {
                    Add(issues, UnitIssueLevel.Error, "U-NO-PROP", "预制体不是有效兵种",
                        "预制体上没有 GameObjectProperty 组件（" + stats.Error + "），无法调数值。", i, label);
                    continue;
                }

                bool barracks = UnitCatalog.IsBarracksRace(record.Race);
                CheckNumbers(issues, i, label, stats, barracks);

                // --- 资源键 ---
                // 运行时能不能发射投射物，只取决于 atkObj 是否为空：
                // CharacterAI 里 `if (_prop == null || string.IsNullOrEmpty(_prop.atkObj)) return;`。
                // isRemoteAtk 在整个工程里没有任何读取点，只是给编辑者看的标注。
                if (string.IsNullOrEmpty(stats.AtkObjKey))
                {
                    if (stats.IsRemoteAtk)
                        Add(issues, UnitIssueLevel.Warning, "U-ATKOBJ-EMPTY", "勾了「远程攻击」但没配投射物",
                            "投射物资源键（atkObj）为空，运行时 CharacterAI 会直接返回、什么都不发射。请选一个 Bullet- 开头的预制体键。", i, label);
                }
                else
                {
                    if (!index.Has(UnitResKind.Prefab, stats.AtkObjKey))
                        Add(issues, UnitIssueLevel.Error, "U-ATKOBJ-INVALID", "投射物资源键无效",
                            "PrefabRegistry 里没有 " + Quote(stats.AtkObjKey) + "，远程攻击会打不出子弹。", i, label, FixKeySuggestion(UnitResKind.Prefab, stats.AtkObjKey, null));
                    else if (!stats.IsRemoteAtk)
                        Add(issues, UnitIssueLevel.Info, "U-ATKOBJ-MELEE", "没勾「远程攻击」但有投射物",
                            "填了 " + Quote(stats.AtkObjKey) + " 就会发弹道 —— 运行时只看 atkObj 是否为空，" +
                            "「远程攻击」勾选框本身在游戏里没有读取点。确认是有意为之（例如近战也发弹道）。", i, label);
                }

                if (!string.IsNullOrEmpty(stats.SpriteKey) && !index.Has(UnitResKind.Sprite, stats.SpriteKey))
                    Add(issues, UnitIssueLevel.Error, "U-SPRITE-INVALID", "基础动画贴图键无效",
                        "SpriteRegistry 里没有 " + Quote(stats.SpriteKey) + "，单位出场会没有贴图。", i, label);

                if (string.IsNullOrEmpty(stats.SpriteKey))
                    Add(issues, barracks ? UnitIssueLevel.Warning : UnitIssueLevel.Info, "U-SPRITE-EMPTY", "基础动画贴图键为空",
                        "StageTexture.spriteKey 为空，召唤预载时拿不到贴图（若全部由动画控制器驱动可忽略）。", i, label);

                if (!string.IsNullOrEmpty(stats.AnimatorControllerKey))
                {
                    if (!index.Has(UnitResKind.AnimatorController, stats.AnimatorControllerKey))
                        Add(issues, UnitIssueLevel.Error, "U-AC-INVALID", "动画控制器键无效",
                            "AnimatorControllerRegistry 里没有 " + Quote(stats.AnimatorControllerKey) + "，单位不会有任何动作。", i, label);
                }
                else
                {
                    Add(issues, barracks ? UnitIssueLevel.Warning : UnitIssueLevel.Info, "U-AC-EMPTY", "动画控制器键为空",
                        "单位不会播放攻击/移动动画。" + (barracks ? string.Empty : "（" + record.Race + " 目录不是兵营兵种，可能由其它系统驱动动画，确认后忽略。）"), i, label);
                }

                for (int a = 0; a < stats.AudioKeys.Length; a++)
                {
                    string audioKey = stats.AudioKeys[a];
                    if (string.IsNullOrEmpty(audioKey))
                        continue;
                    if (!index.Has(UnitResKind.Audio, audioKey))
                        Add(issues, UnitIssueLevel.Error, "U-AUDIO-INVALID", "音效键无效",
                            "AudioRegistry 里没有 audioKey" + (a + 1) + " = " + Quote(audioKey) + "。", i, label);
                }

                // --- 训练配置 ---
                if (!record.HasDefinition)
                {
                    Add(issues, barracks ? UnitIssueLevel.Warning : UnitIssueLevel.Info, "U-NO-DEF", "没有训练配置（TroopDefinition）",
                        barracks
                            ? "这个预制体在 TroopDefinitions 里没有对应资产：玩家在兵营里训练不到它，图鉴条目也没法用它的键解锁。可用「为缺失的兵种新建训练配置」一键补齐。"
                            : "所在目录（" + record.Race + "）不是兵营可训练种族，通常不需要 TroopDefinition；确认它只作为敌方/工程师单位使用即可。",
                        i, label);
                }
                else
                {
                    TroopDefinition definition = record.Definition;
                    if (definition != null)
                    {
                        string defPath = record.DefinitionPath;

                        if (string.IsNullOrEmpty(definition.DisplayName))
                            Add(issues, UnitIssueLevel.Info, "U-DEF-NAME", "训练配置缺显示名",
                                "displayName 目前运行时不在任何 UI 上显示（兵营格子只画图标），" +
                                "填上只是为了本工具和导出表格里能读懂这一条。将用 " + Quote(record.PrefabName) + " 填充。", i, label, record.PrefabName);

                        if (string.IsNullOrEmpty(definition.Id))
                        {
                            string suggestedId = SuggestTrainingId(catalog, record, i, reservedTrainingIds);
                            Add(issues, UnitIssueLevel.Warning, "U-DEF-ID", "训练配置缺 id",
                                "id 是关卡/敌方队伍配置引用兵种的键，将补成 " + Quote(suggestedId) + "。",
                                i, label, suggestedId);
                        }
                        else
                        {
                            string trimmedId = definition.Id.Trim();
                            int idOwner;
                            if (unitIdOwners.TryGetValue(trimmedId, out idOwner))
                            {
                                string suggestedId = SuggestTrainingId(catalog, record, i, reservedTrainingIds);
                                Add(issues, UnitIssueLevel.Warning, "U-DEF-ID-DUP", "训练 ID 重复",
                                    "与第 " + (idOwner + 1) + " 个兵种共用训练 ID " + Quote(trimmedId) +
                                    "：关卡/敌方队伍配置是按 id 引用兵种的，重复会让引用落到不确定的那个兵种上。" +
                                    "将改为 " + Quote(suggestedId) + "。",
                                    i, label, suggestedId);
                            }
                            else
                            {
                                unitIdOwners[trimmedId] = i;
                            }
                        }

                        if (record.HasDefinition && !UnitBarracksIndex.Get(record).IsWired)
                            Add(issues, UnitIssueLevel.Warning, "U-NO-BARRACKS", "训练配置没挂到任何兵营",
                                "它不在任何兵营预制体的 BuildingTraining.troops 里，玩家在兵营界面永远看不到、也训练不到。" +
                                "请在对应兵营预制体（Assets/RemoteResource/Prefabricated body/Build/）的 BuildingTraining.troops 中把它拖进去。", i, label);

                        // 归属是互斥的：实测 44 个兵种只挂普通 24 个、只挂黑暗 20 个，没有两边都挂的。
                        // 万一日后有人把同一个兵种拖进两种兵营，这里会提示（这类兵种会在两个兵营面板里都出现）。
                        if (record.HasDefinition)
                        {
                            BarracksInfo barracksInfo = UnitBarracksIndex.Get(record);
                            if (barracksInfo.HasNormal && barracksInfo.HasDark)
                                Add(issues, UnitIssueLevel.Info, "U-BARRACKS-BOTH", "同时挂在普通与黑暗兵营",
                                    "它同时出现在普通兵营（" + string.Join("、", barracksInfo.NormalBarracks.ToArray()) +
                                    "）和黑暗兵营（" + string.Join("、", barracksInfo.DarkBarracks.ToArray()) +
                                    "）里，两个兵营面板都能训练它。当前工程里没有这类兵种，确认是否有意为之。", i, label);
                        }

                        // 兵营训练面板只有 1/2/3 三排，排号就是 unlockLevel（兵营阶级）：
                        // BuildingTraining.GetTroop(row) 只取 UnlockLevel == row 的兵种，
                        // 所以越界的兵种在任何一排都不显示 —— 等于玩家永远练不到它。
                        if (definition.UnlockLevel < 1 || definition.UnlockLevel > 3)
                        {
                            int safeUnlock = Mathf.Clamp(definition.UnlockLevel, 1, 3);
                            Add(issues, UnitIssueLevel.Warning, "U-DEF-UNLOCK", "兵营阶级超出面板范围",
                                "兵营阶级（unlockLevel）= " + definition.UnlockLevel + "，兵营训练面板只有 1/2/3 三排，" +
                                "这个兵种不会出现在任何一排里（玩家练不到）。将改为 " + safeUnlock + "。",
                                i, label, safeUnlock.ToString());
                        }

                        if (definition.Cooldown <= 0f)
                            Add(issues, UnitIssueLevel.Error, "U-DEF-CD", "训练冷却为 0",
                                "cooldown = " + definition.Cooldown + "，兵营会瞬间刷出无限兵。", i, label, "1");
                        else if (definition.Cooldown < 1.5f)
                            Add(issues, UnitIssueLevel.Warning, "U-DEF-CD-LOW", "训练冷却过短",
                                "cooldown = " + definition.Cooldown + " 秒，产出速度可能过快，确认是有意为之。", i, label);

                        if (definition.TrainCount <= 0)
                            Add(issues, UnitIssueLevel.Error, "U-DEF-COUNT", "单次产出数量为 0",
                                "trainCount = " + definition.TrainCount + "，训练一次不产出任何单位。", i, label, "1");

                        if (string.IsNullOrEmpty(definition.IconKey))
                            Add(issues, barracks ? UnitIssueLevel.Warning : UnitIssueLevel.Info, "U-DEF-ICON", "训练配置缺图标键",
                                "兵营里该兵种头像为空。可在本页「图标」里搜索选择（形如 Mouse Troop UI_0）。", i, label);

                        // 两个概念不同：解锁等级（兵营阶级）决定玩家在兵营第几排看到它，
                        // tier 只是敌方 AI 的选兵权重。AI 阶级低却要很晚的兵营阶级解锁，通常是无意的。
                        if (definition.Tier <= 1 && definition.UnlockLevel > 2)
                            Add(issues, UnitIssueLevel.Info, "U-DEF-TIER", "AI 阶级与兵营阶级不匹配",
                                "AI 威胁阶级 tier = " + definition.Tier + "（低），却要求兵营阶级 " + definition.UnlockLevel +
                                " 才解锁，确认是否有意为之。", i, label);

                        // tier 只被 TeamCommander.ScoreTroop 用来算权重（max(0,Tier)*10），
                        // 超出 1~3 时敌方 AI 的评分口径会失真，所以报出来。
                        if (definition.Tier < 1 || definition.Tier > 3)
                        {
                            int safeTier = Mathf.Clamp(definition.Tier, 1, 3);
                            Add(issues, UnitIssueLevel.Warning, "U-DEF-TIER-RANGE", "AI 威胁阶级超出 1~3",
                                "tier = " + definition.Tier + "，敌方 AI 的选兵权重会按 " + Mathf.Max(0, definition.Tier) * 10 +
                                " 算（本意是 1~3 阶）。它不影响兵营里的排数 —— 那是「兵营阶级」。将改为 " + safeTier + "。",
                                i, label, safeTier.ToString());
                        }

                        string prefabKey = definition.PrefabKey;
                        if (!string.IsNullOrEmpty(prefabKey))
                        {
                            int owner;
                            if (prefabKeyOwners.TryGetValue(prefabKey, out owner))
                                Add(issues, UnitIssueLevel.Warning, "U-DEF-DUP", "两个兵种共用同一单位键",
                                    "与第 " + (owner + 1) + " 条共用 " + Quote(prefabKey) + "，数值其实是同一份预制体，改一个两条都会变。", i, label);
                            else
                                prefabKeyOwners[prefabKey] = i;
                        }
                        else
                        {
                            Add(issues, UnitIssueLevel.Error, "U-DEF-PREFAB", "训练配置缺单位预制体键",
                                "prefabKey 为空，兵营训练不出东西。将填为 " + Quote(record.PrefabName) + "。", i, label, record.PrefabName);
                        }

                        // 分桶按**兵营阶级**（UnlockLevel）而不是 AI 权重阶级：
                        // 同一排解锁的兵才是互相竞争的，拿它们比中位数才有意义。
                        // 越界的值单独成桶（不夹到 1/2/3 里污染真实分桶），并已由 U-DEF-UNLOCK 报警。
                        Collect(barracksTierMaxHp, definition.UnlockLevel, stats.MaxHp);
                        Collect(barracksTierAtk, definition.UnlockLevel, stats.Atk);
                    }
                }
            }

            // --- 同阶离群（同兵营阶级）---
            AddOutliers(issues, catalog, barracksTierMaxHp, "U-OUT-HP", "生命值", record => catalog.GetStats(record).MaxHp);
            AddOutliers(issues, catalog, barracksTierAtk, "U-OUT-ATK", "攻击力", record => catalog.GetStats(record).Atk);

            return issues;
        }

        private static void CheckNumbers(List<UnitIssue> issues, int index0, string label, UnitStats stats, bool barracks)
        {
            if (stats.MaxHp <= 0)
                Add(issues, UnitIssueLevel.Error, "U-HP", "生命值为 0", "maxHp = 0，单位一出场就死。", index0, label);
            else if (stats.MaxHp < 10)
                Add(issues, UnitIssueLevel.Warning, "U-HP-LOW", "生命值极低", "maxHp = " + stats.MaxHp + "，基本一击必杀，确认是有意为之。", index0, label);

            if (stats.AtkRate <= 0f)
                Add(issues, UnitIssueLevel.Warning, "U-ATKRATE", "攻速系数为 0",
                    "atkRate = 0。角色出手节奏由攻击动画事件驱动，这个值不影响它出手，但会让攻速类 Buff（精准/狂暴）加成完全失效，" +
                    "若该预制体被当哨塔用则会被钳到最快射速。建议填 1。", index0, label);

            if (stats.AtkRange.x <= 0 || stats.AtkRange.y <= 0)
                Add(issues, UnitIssueLevel.Error, "U-RANGE", "攻击范围为 0", "atkRange = (" + stats.AtkRange.x + ", " + stats.AtkRange.y + ")，单位永远找不到攻击目标。", index0, label);

            if (stats.MoveSpeed <= 0f)
                Add(issues, UnitIssueLevel.Error, "U-SPEED", "移动速度为 0", "moveSpeed = 0，近战单位永远走不到敌人面前。", index0, label);
            else if (stats.MoveSpeed > 12f)
                Add(issues, UnitIssueLevel.Warning, "U-SPEED-HIGH", "移动速度偏高", "moveSpeed = " + stats.MoveSpeed + "，可能穿过其它单位的防重叠半径。", index0, label);

            if (stats.Atk <= 0 && stats.MagicAtk <= 0)
                Add(issues, UnitIssueLevel.Warning, "U-ATK-ZERO", "攻击与魔攻都是 0", "这个单位打不出任何伤害（除非纯辅助/纯阻挡）。", index0, label);

            if (stats.OccupySpace.x <= 0 || stats.OccupySpace.y <= 0)
                Add(issues, UnitIssueLevel.Error, "U-OCCUPY", "占地尺寸为 0", "occupySpace = (" + stats.OccupySpace.x + ", " + stats.OccupySpace.y + ")，地图占位逻辑会异常。", index0, label);

            if (stats.BaseMissChance < 0f || stats.BaseMissChance > 1f)
                Add(issues, UnitIssueLevel.Error, "U-MISS", "基础未命中率越界", "baseMissChance = " + stats.BaseMissChance + "，应在 0~1 之间。", index0, label);

            if (stats.AntiOverlapRadius < 0f)
                Add(issues, UnitIssueLevel.Error, "U-RADIUS", "防重叠半径为负", "UnitAntiOverlap.radius = " + stats.AntiOverlapRadius + "。", index0, label);
        }

        private static void Collect(Dictionary<int, List<float>> table, int tier, float value)
        {
            List<float> list;
            if (!table.TryGetValue(tier, out list))
            {
                list = new List<float>();
                table.Add(tier, list);
            }
            list.Add(value);
        }

        private static void AddOutliers(
            List<UnitIssue> issues,
            UnitCatalog catalog,
            Dictionary<int, List<float>> table,
            string code,
            string name,
            Func<UnitRecord, float> getValue)
        {
            foreach (KeyValuePair<int, List<float>> pair in table)
            {
                if (pair.Value.Count < 4)
                    continue;   // 同阶样本太少，不做离群判断

                var sorted = new List<float>(pair.Value);
                sorted.Sort();
                float median = sorted[sorted.Count / 2];
                if (median <= 0f)
                    continue;

                for (int i = 0; i < catalog.Count; i++)
                {
                    UnitRecord record = catalog.Records[i];
                    if (!record.HasDefinition)
                        continue;

                    TroopDefinition definition = record.Definition;
                    if (definition == null || definition.UnlockLevel != pair.Key)
                        continue;

                    // 没有预制体 / 没有 GameObjectProperty 的兵种，GetStats 返回的是 Error 状态、
                    // 各数值全为 0 —— 它们已经在上面报过 U-PREFAB-MISSING / U-NO-PROP，
                    // 这里再算一遍差值只会得到"-100%"这种假离群，把真实平衡问题淹掉。
                    if (!catalog.GetStats(record).Valid)
                        continue;

                    float value = getValue(record);
                    float ratio = value / median;
                    if (ratio > 1f + OutlierRatio || ratio < 1f - OutlierRatio)
                    {
                        string label = string.IsNullOrEmpty(record.Name) ? record.Id : record.Name;
                        Add(issues, UnitIssueLevel.Info, code, name + "偏离同兵营阶级水平",
                            "兵营 " + pair.Key + " 阶的" + name + "中位数是 " + median.ToString("0.#") + "，" + label + " 是 " + value.ToString("0.#") +
                            "（" + (ratio >= 1f ? "+" : "") + ((ratio - 1f) * 100f).ToString("0") + "%），确认是否符合定位。", i, label);
                    }
                }
            }
        }

        /// <summary>把一个无效的键就近匹配成注册表里的真实键（唯一命中才给建议）。</summary>
        private static string FixKeySuggestion(UnitResKind kind, string badKey, IReadOnlyList<string> extraKeys)
        {
            if (string.IsNullOrEmpty(badKey))
                return null;

            UnitResourceIndex index = UnitResourceIndex.Instance;
            List<string> hits = index.Search(kind, badKey.Trim(), 40);
            string exact = null;
            int exactCount = 0;
            for (int i = 0; i < hits.Count; i++)
            {
                if (string.Equals(hits[i], badKey.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    exact = hits[i];
                    exactCount++;
                }
            }

            return exactCount == 1 ? exact : null;
        }

        /// <summary>
        /// 一键修复：只做安全的确定性修复 ——
        /// 补训练配置里空的 displayName / id / prefabKey、把越界的解锁等级夹回 1~3、
        /// 把训练冷却/产出数量的非法值改成 1，以及修正能唯一确定的无效资源键（投射物）。
        /// 数值（HP/攻击/防御等平衡项）一律不动，避免把平衡改坏。
        /// 注意：这里处理的 code 必须与 RunAutoFix 统计的 fixable 集合保持一致，
        /// 否则弹窗会说"将修复 N 处"而实际只改了 M 处。
        /// </summary>
        public static int ApplySafeFixes(UnitCatalog catalog, List<UnitIssue> issues)
        {
            if (catalog == null || issues == null)
                return 0;

            int fixedCount = 0;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("兵种体检一键修复");

            // 先备份所有会被改到的资产
            var paths = new List<string>();
            for (int i = 0; i < issues.Count; i++)
            {
                UnitRecord record = RecordOf(catalog, issues[i]);
                if (record == null)
                    continue;

                // 投射物键改的是预制体，其余改的是 TroopDefinition
                if (issues[i].Code == "U-ATKOBJ-INVALID")
                {
                    if (record.HasPrefab)
                        paths.Add(record.PrefabPath);
                }
                else if (record.HasDefinition)
                {
                    paths.Add(record.DefinitionPath);
                }
            }
            int backups = UnitBackup.BackupAssets(paths, "一键修复前");
            if (backups > 0)
                Debug.Log("[兵种编辑器] 一键修复前已备份 " + backups + " 个资产到 " + UnitEditorPrefs.BackupFolder);

            for (int i = 0; i < issues.Count; i++)
            {
                UnitIssue issue = issues[i];
                UnitRecord record = RecordOf(catalog, issue);
                if (record == null)
                    continue;

                // --- 投射物资源键在预制体的 GameObjectProperty 上，不在 TroopDefinition 上 ---
                if (issue.Code == "U-ATKOBJ-INVALID")
                {
                    if (!FixProjectileKey(record, issue.SuggestedValue))
                        continue;

                    fixedCount++;
                    continue;
                }

                TroopDefinition definition = record.Definition;
                if (definition == null)
                    continue;

                string field;
                if (issue.Code == "U-DEF-NAME") field = "displayName";
                else if (issue.Code == "U-DEF-ID") field = "id";
                else if (issue.Code == "U-DEF-ID-DUP") field = "id";
                else if (issue.Code == "U-DEF-PREFAB") field = "prefabKey";
                else if (issue.Code == "U-DEF-UNLOCK") field = "unlockLevel";
                else if (issue.Code == "U-DEF-TIER-RANGE") field = "tier";
                else if (issue.Code == "U-DEF-CD") field = "cooldown";
                else if (issue.Code == "U-DEF-COUNT") field = "trainCount";
                else continue;

                var so = new SerializedObject(definition);
                SerializedProperty prop = so.FindProperty(field);
                if (prop == null)
                    continue;

                if (!WritePropertyValue(prop, issue.SuggestedValue))
                    continue;

                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(definition);

                // 同步目录缓存，否则列表/表头/对比表还显示旧值（会看起来像"修复没生效"）
                if (field == "id" || field == "displayName")
                    record.SyncDefinitionCache();

                fixedCount++;
            }

            Undo.CollapseUndoOperations(group);
            return fixedCount;
        }

        /// <summary>取出问题对应的记录；已在本轮修过的问题（记录下标失效）返回 null。</summary>
        private static UnitRecord RecordOf(UnitCatalog catalog, UnitIssue issue)
        {
            if (issue == null || !issue.Fixable || issue.IsCatalogLevel)
                return null;
            if (issue.RecordIndex < 0 || issue.RecordIndex >= catalog.Count)
                return null;

            return catalog.Records[issue.RecordIndex];
        }

        /// <summary>把无效的投射物键写成唯一命中的真实键（在预制体的 GameObjectProperty.atkObj 上）。</summary>
        private static bool FixProjectileKey(UnitRecord record, string suggested)
        {
            if (!record.HasPrefab || string.IsNullOrEmpty(suggested))
                return false;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(record.PrefabPath);
            if (prefab == null)
                return false;

            GameObjectProperty prop = prefab.GetComponent<GameObjectProperty>();
            if (prop == null)
                return false;

            var so = new SerializedObject(prop);
            SerializedProperty atkObj = so.FindProperty("atkObj");
            if (atkObj == null || !WritePropertyValue(atkObj, suggested))
                return false;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(prop);
            return true;
        }

        /// <summary>
        /// 按属性的真实类型写入建议值。
        /// 注意：不能一律用 stringValue —— 对 int/float/bool 字段那样写是不会生效的
        /// （早期版本正是这么写的，所以"解锁等级/冷却"这类建议值修不进去）。
        /// </summary>
        private static bool WritePropertyValue(SerializedProperty prop, string suggested)
        {
            suggested = suggested ?? string.Empty;

            switch (prop.propertyType)
            {
                case SerializedPropertyType.String:
                    prop.stringValue = suggested;
                    return true;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Enum:
                    int intValue;
                    if (!int.TryParse(suggested.Trim(), out intValue))
                        return false;
                    prop.intValue = intValue;
                    return true;
                case SerializedPropertyType.Float:
                    float floatValue;
                    if (!float.TryParse(suggested.Trim(), out floatValue))
                        return false;
                    prop.floatValue = floatValue;
                    return true;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = suggested.Trim() == "1" || suggested.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 生成训练 ID 建议，并保证同一轮体检里不重复 ——
        /// 否则两个共用同一 id 的兵种会被建议成同一个新 id，修完还是重复。
        /// </summary>
        private static string SuggestTrainingId(UnitCatalog catalog, UnitRecord record, int index, HashSet<string> reserved)
        {
            string baseId = catalog.MakeUniqueTrainingId(record, index);

            // MakeUniqueTrainingId 只保证不和"工程里现有的 id"撞车；同一轮体检会给多个兵种建议 id，
            // 建议值之间也要互不重复；而且追加 _2 之后依然可能撞上已有 id（例如工程里已有 wolf_2_2），
            // 所以继续在循环里查重，而不是简单地 id += "_2"。
            string id = baseId;
            int n = 2;
            while (catalog.IsTrainingIdTaken(id, index) || reserved.Contains(id))
            {
                id = baseId + "_" + n;
                if (++n > 1000)
                {
                    id = baseId + "_x";
                    break;
                }
            }

            reserved.Add(id);
            return id;
        }

        public static void Count(List<UnitIssue> issues, out int errors, out int warnings, out int infos)
        {
            errors = 0;
            warnings = 0;
            infos = 0;
            if (issues == null)
                return;

            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i] == null)
                    continue;
                if (issues[i].Level == UnitIssueLevel.Error) errors++;
                else if (issues[i].Level == UnitIssueLevel.Warning) warnings++;
                else infos++;
            }
        }

        /// <summary>按兵种建立问题索引，供列表打标记。</summary>
        public static Dictionary<int, List<UnitIssue>> GroupByRecord(List<UnitIssue> issues)
        {
            var map = new Dictionary<int, List<UnitIssue>>();
            if (issues == null)
                return map;

            for (int i = 0; i < issues.Count; i++)
            {
                UnitIssue issue = issues[i];
                if (issue == null || issue.IsCatalogLevel)
                    continue;

                List<UnitIssue> list;
                if (!map.TryGetValue(issue.RecordIndex, out list))
                {
                    list = new List<UnitIssue>();
                    map.Add(issue.RecordIndex, list);
                }
                list.Add(issue);
            }
            return map;
        }

        private static string Quote(string text)
        {
            return "\"" + (text ?? string.Empty) + "\"";
        }

        private static void Add(
            List<UnitIssue> issues,
            UnitIssueLevel level,
            string code,
            string title,
            string detail,
            int recordIndex = -1,
            string unitLabel = null,
            string suggestedValue = null)
        {
            var issue = new UnitIssue();
            issue.Level = level;
            issue.Code = code;
            issue.Title = title;
            issue.Detail = detail;
            issue.RecordIndex = recordIndex;
            issue.UnitLabel = unitLabel ?? string.Empty;
            issue.SuggestedValue = suggestedValue;
            issue.Fixable = suggestedValue != null && recordIndex >= 0;
            issues.Add(issue);
        }
    }
}
