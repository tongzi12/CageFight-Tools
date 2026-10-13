using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CageFightSpellTool
{
    public enum SpellIssueLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>一条体检结论。RecordIndex 指向 SpellCatalog.Records 的下标，-1 = 注册表/工程级的结论。</summary>
    public sealed class SpellIssue
    {
        public SpellIssueLevel Level;
        public string Code;
        public string Title;
        public string Detail;
        public int RecordIndex = -1;
        public bool Fixable;
        public string SuggestedValue;

        public string RecordLabel;
    }

    /// <summary>
    /// 法术体检。每条规则都注明**运行时代码出处**，全部是"照抄运行时行为"，不是风格建议。
    ///
    /// 三条会让法术彻底不可用的路径（都在 EngineerSpellCaster / LoadoutDefinitionRegistry 里）：
    ///   · CastPrefabKey 为空 → Cast() 第一行就 return false（EngineerSpellCaster.cs:41-42）；
    ///   · 键不在 PrefabRegistry → GetGameObject 取不到 → LogError「法术预制体未预加载」（:52-59）；
    ///   · 预制体根节点缺少执行器接口 → LogError「法术预制体根节点缺少施法执行器」并回收实例（:136-169）。
    /// </summary>
    public static class SpellValidator
    {
        public static List<SpellIssue> Validate(SpellCatalog catalog)
        {
            var issues = new List<SpellIssue>();
            if (catalog == null)
                return issues;

            SpellResourceIndex index = SpellResourceIndex.Instance;

            ValidateRegistryLevel(catalog, issues);

            for (int i = 0; i < catalog.Records.Count; i++)
            {
                SpellRecord record = catalog.Records[i];
                if (record == null || record.Definition == null)
                {
                    Add(issues, SpellIssueLevel.Error, "spell.null", i, record,
                        "第 " + (i + 1) + " 条记录加载失败",
                        "资产存在但加载不出 SpellDefinition（可能类型变了或文件损坏）。");
                    continue;
                }

                ValidateIdentity(catalog, i, record, issues);
                ValidateRegistration(catalog, i, record, issues);
                ValidateCastPrefab(i, record, index, issues);
                ValidateIcon(i, record, index, issues);
                ValidateWarningCircle(i, record, index, issues);
                ValidateNumbers(i, record, issues);
                ValidateFieldRelevance(i, record, issues);
            }

            return issues;
        }

        // ─────────────────────────── 身份／登记 ───────────────────────────

        private static void ValidateIdentity(SpellCatalog catalog, int index, SpellRecord record, List<SpellIssue> issues)
        {
            SpellDefinition definition = record.Definition;

            if (string.IsNullOrWhiteSpace(definition.Id))
            {
                var issue = Add(issues, SpellIssueLevel.Error, "spell.id.empty", index, record,
                    "ID 是空的",
                    "LoadoutDefinitionRegistry.AddSpells 会跳过 ID 为空的定义（LoadoutDefinitionRegistry.cs:142），" +
                    "ResourceManager.TryGetSpell(id) 也就永远查不到这个法术 —— 玩家在选装界面和法术栏都拿不到它。");
                issue.Fixable = true;
                issue.SuggestedValue = "用资产名生成一个 ID：" + SanitizeId(record.FileName);
            }
            else
            {
                int others = catalog.CountWithId(definition.Id, record);
                if (others > 0)
                {
                    Add(issues, SpellIssueLevel.Error, "spell.id.duplicate", index, record,
                        "ID 与另一个法术重复：" + definition.Id,
                        "注册表 AddSpells 里有 ContainsKey 检查：重复时打一条「重复法术 ID」警告，并且**后登记的那条作废**" +
                        "（LoadoutDefinitionRegistry.cs:143-147）→ 其中一条法术在游戏里永远选不到。" +
                        "工具不会替你改 ID（ID 是存档与装备引用的依据），请手工决定保留哪一个。");
                }

                if (!string.Equals(definition.Id, record.FileName, StringComparison.Ordinal))
                {
                    Add(issues, SpellIssueLevel.Warning, "spell.id.fileNameMismatch", index, record,
                        "ID 与资产文件名不一致",
                        "ID = " + definition.Id + "，文件名 = " + record.FileName + "。" +
                        "运行时只认 ID（文件名不参与查找），但两边不一致时以后很难对上号，建议改成一致。");
                }
            }

            if (string.IsNullOrWhiteSpace(definition.DisplayName))
            {
                Add(issues, SpellIssueLevel.Warning, "spell.displayName.empty", index, record,
                    "显示名是空的",
                    "选装界面/法术栏显示的名字取自 displayName（为空时退回资产名）。");
            }
        }

        private static void ValidateRegistration(SpellCatalog catalog, int index, SpellRecord record, List<SpellIssue> issues)
        {
            if (!catalog.RegistryFound)
                return;

            if (!record.Registered)
            {
                var issue = Add(issues, SpellIssueLevel.Error, "spell.notRegistered", index, record,
                    "没有登记进 LoadoutDefinitionRegistry",
                    "ResourceManager.TryGetSpell / SpellDefinitions 只遍历注册表的 spellDefinitions 列表" +
                    "（ResourceManager.cs:404-436）→ 没登记的资产在游戏里等于不存在：" +
                    "选装界面列不出来、法术栏也装不上。可以直接用「一键修复」登记。");
                issue.Fixable = true;
                issue.SuggestedValue = "追加到 LoadoutDefinitionRegistry.spellDefinitions";
            }
        }

        private static void ValidateRegistryLevel(SpellCatalog catalog, List<SpellIssue> issues)
        {
            if (!catalog.RegistryFound)
            {
                Add(issues, SpellIssueLevel.Error, "registry.missing", -1, null,
                    "找不到注册表资产",
                    SpellCatalog.RegistryAssetPath + " 加载不到 —— 法术的「上架」状态无法判断，登记功能也不可用。");
                return;
            }

            // 同一个资产在列表里出现多次：Initialize 里的字典会忽略后来者，等于白摆一条。
            var seen = new HashSet<SpellDefinition>();
            int duplicates = 0;
            IReadOnlyList<SpellDefinition> list = catalog.Registry.SpellDefinitions;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    SpellDefinition definition = list[i];
                    if (definition == null)
                        continue;
                    if (!seen.Add(definition))
                        duplicates++;
                }
            }

            if (duplicates > 0)
            {
                var issue = Add(issues, SpellIssueLevel.Warning, "registry.duplicateEntry", -1, null,
                    "注册表里有 " + duplicates + " 条重复条目",
                    "同一个法术资产在 spellDefinitions 里出现了多次。运行时不报错（字典会忽略重复），" +
                    "但列表长度会虚高，以后排查容易看错。一键修复会删掉后面重复的那些。");
                issue.Fixable = true;
                issue.SuggestedValue = "移除重复的注册表条目";
            }

            // 默认法术槽为空：TryGetDefaultLoadout 只强制校验工程师与种族（:81-85），
            // NormalizeDefaults 会把「填了但查不到」的默认槽清空（:174-179）。
            string slot1 = ReadPrivateString(catalog.Registry, "defaultSpellSlot1Id");
            string slot2 = ReadPrivateString(catalog.Registry, "defaultSpellSlot2Id");
            if (string.IsNullOrWhiteSpace(slot1) && string.IsNullOrWhiteSpace(slot2))
            {
                Add(issues, SpellIssueLevel.Info, "registry.defaultSlotsEmpty", -1, null,
                    "默认出装的两个法术槽都是空的",
                    "注册表允许为空（TryGetDefaultLoadout 只要求工程师与种族有效，LoadoutDefinitionRegistry.cs:81-85）：" +
                    "默认出装不含法术，玩家要自己在选装界面选。想让新玩家一上来就有法术的话，把 defaultSpellSlot1Id / 2Id 填成已登记的 ID。");
            }
        }

        // ─────────────────────────── 施法预制体 / 执行器 ───────────────────────────

        private static void ValidateCastPrefab(int index, SpellRecord record, SpellResourceIndex index2, List<SpellIssue> issues)
        {
            SpellDefinition definition = record.Definition;
            string key = definition.CastPrefabKey;

            if (string.IsNullOrWhiteSpace(key))
            {
                Add(issues, SpellIssueLevel.Error, "spell.castPrefab.empty", index, record,
                    "施法预制体键是空的",
                    "EngineerSpellCaster.Cast 第一步就检查 CastPrefabKey 是否为空，为空直接 return false" +
                    "（EngineerSpellCaster.cs:41-42）→ 点法术按钮什么都不会发生。");
                return;
            }

            if (!index2.Has(SpellResKind.Prefab, key))
            {
                var issue = Add(issues, SpellIssueLevel.Error, "spell.castPrefab.notInRegistry", index, record,
                    "施法预制体键不在 PrefabRegistry：" + key,
                    "运行时用 ResourceManager.GetGameObject(key) 取预制体，取不到会 LogError「法术预制体未预加载」并放弃施法" +
                    "（EngineerSpellCaster.cs:52-59）。键必须先登记进 PrefabRegistry。");
                string fixedKey = FixKeySuggestion(SpellResKind.Prefab, key);
                if (fixedKey != null)
                {
                    issue.Fixable = true;
                    issue.SuggestedValue = "改成注册表里唯一的同名键：" + fixedKey;
                }
                else
                {
                    issue.SuggestedValue = "在 PrefabRegistry 里补这个键，或把键改成正确的名字";
                }
                return;
            }

            GameObject prefab = index2.GetPrefab(key);
            if (prefab == null)
            {
                Add(issues, SpellIssueLevel.Error, "spell.castPrefab.assetMissing", index, record,
                    "PrefabRegistry 里「" + key + "」没有指向任何预制体",
                    "键在注册表里，但那条登记的资产引用是空的（或资产已被删除/移出工程）。" +
                    "运行时同样会 LogError「法术预制体未预加载」并放弃施法（EngineerSpellCaster.cs:52-59）——" +
                    "表现和「键不在注册表」一样，但修法不同：要去 PrefabRegistry 把这条键重新指向预制体。");
                return;
            }

            bool hasDirect = prefab.GetComponent(typeof(IEngineerDirectSpellInstance)) != null;
            bool hasAimed = prefab.GetComponent(typeof(IEngineerAimedSpellInstance)) != null;
            bool hasPlain = prefab.GetComponent(typeof(IEngineerSpellInstance)) != null;

            if (!hasDirect && !hasAimed && !hasPlain)
            {
                Add(issues, SpellIssueLevel.Error, "spell.executor.missing", index, record,
                    "施法预制体根节点没有任何执行器",
                    key + " 上既没有 IEngineerDirectSpellInstance（原地法术用 EngineerSpellDirectEffect）、" +
                    "也没有 IEngineerAimedSpellInstance / IEngineerSpellInstance。" +
                    "运行时会 LogError「法术预制体根节点缺少施法执行器」并把实例回收（EngineerSpellCaster.cs:151-169）→ 法术没有任何效果。");
                return;
            }

            if (definition.DeliveryType == SpellDeliveryType.DirectSpawn && !hasDirect)
            {
                var issue = Add(issues, SpellIssueLevel.Error, "spell.executor.directMismatch", index, record,
                    "投递方式与预制体不匹配：DirectSpawn 需要 EngineerSpellDirectEffect",
                    "deliveryType = DirectSpawn 时，运行时只找 IEngineerDirectSpellInstance，找不到就 LogError" +
                    "「原地法术预制体根节点缺少 EngineerSpellDirectEffect」并回收实例（EngineerSpellCaster.cs:136-148）。" +
                    "而 " + key + " 上有的是 " + (hasAimed ? "IEngineerAimedSpellInstance" : "IEngineerSpellInstance") + "。");
                issue.Fixable = true;
                issue.SuggestedValue = "把 deliveryType 改成 Projectile（与预制体现有的执行器一致）";
            }
            else if (definition.DeliveryType == SpellDeliveryType.Projectile && !hasAimed && !hasPlain)
            {
                var issue = Add(issues, SpellIssueLevel.Error, "spell.executor.projectileMismatch", index, record,
                    "投递方式与预制体不匹配：Projectile 需要弹道/瞄准执行器",
                    "deliveryType = Projectile 时，运行时会依次找 IEngineerAimedSpellInstance、IEngineerSpellInstance，" +
                    "都没有就 LogError「法术预制体根节点缺少施法执行器」并回收（EngineerSpellCaster.cs:151-169）。" +
                    "而 " + key + " 上只有 IEngineerDirectSpellInstance（原地法术）。");
                issue.Fixable = true;
                issue.SuggestedValue = "把 deliveryType 改成 DirectSpawn（与预制体现有的执行器一致）";
            }
        }

        // ─────────────────────────── 图标 / 警示圈 ───────────────────────────

        private static void ValidateIcon(int index, SpellRecord record, SpellResourceIndex res, List<SpellIssue> issues)
        {
            string key = record.Definition.IconKey;
            if (string.IsNullOrWhiteSpace(key))
            {
                Add(issues, SpellIssueLevel.Warning, "spell.icon.empty", index, record,
                    "图标键是空的",
                    "法术栏按钮与选装界面显示的都是 IconKey（PlayerLoadoutManager.cs:226 预载它）→ 空键会显示成空白。");
                return;
            }

            if (!res.Has(SpellResKind.Sprite, key))
            {
                var issue = Add(issues, SpellIssueLevel.Warning, "spell.icon.notInRegistry", index, record,
                    "图标键不在 SpriteRegistry：" + key,
                    "运行时用 LoadRegisteredSprite(IconKey) 预载图标，键不在注册表就加载不出来 → 法术栏是空白图标。");
                string fixedKey = FixKeySuggestion(SpellResKind.Sprite, key);
                if (fixedKey != null)
                {
                    issue.Fixable = true;
                    issue.SuggestedValue = "改成注册表里唯一的同名键：" + fixedKey;
                }
            }
        }

        private static void ValidateWarningCircle(int index, SpellRecord record, SpellResourceIndex res, List<SpellIssue> issues)
        {
            SpellDefinition definition = record.Definition;
            string key = definition.WarningCircleKey;

            if (definition.ShowWarningCircle)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    Add(issues, SpellIssueLevel.Warning, "spell.warningCircle.empty", index, record,
                        "开了警示圈但没配圈贴图",
                        "showWarningCircle = true 时运行时会预载 WarningCircleKey（PlayerLoadoutManager.cs:207-208），" +
                        "空键就没有圈可显示 —— 拖拽瞄准时玩家看不到落点范围。");
                }
                else if (!res.Has(SpellResKind.Sprite, key))
                {
                    var issue = Add(issues, SpellIssueLevel.Warning, "spell.warningCircle.notInRegistry", index, record,
                        "警示圈键不在 SpriteRegistry：" + key,
                        "同上：键解析不出来 → 警示圈不显示。");
                    string fixedKey = FixKeySuggestion(SpellResKind.Sprite, key);
                    if (fixedKey != null)
                    {
                        issue.Fixable = true;
                        issue.SuggestedValue = "改成注册表里唯一的同名键：" + fixedKey;
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(key))
            {
                Add(issues, SpellIssueLevel.Info, "spell.warningCircle.unused", index, record,
                    "配了警示圈贴图但没开显示",
                    "showWarningCircle = false，运行时不预载也不用这个键（PlayerLoadoutManager.cs:207）→ 配了等于没配。");
            }
        }

        // ─────────────────────────── 数值范围 ───────────────────────────

        private static void ValidateNumbers(int index, SpellRecord record, List<SpellIssue> issues)
        {
            SpellDefinition d = record.Definition;

            CheckMin(issues, index, record, "cooldown", "冷却", d.Cooldown, 0f,
                "SpellDefinition.cooldown 上标了 [Min(0)]（SpellDefinition.cs:23）。");
            CheckMin(issues, index, record, "goldCost", "金币消耗", d.GoldCost, 0f,
                "SpellDefinition.goldCost 上标了 [Min(0)]（SpellDefinition.cs:25）：负值会让扣费逻辑倒着走。");
            CheckMin(issues, index, record, "directCastTime", "原地施法前摇", d.DirectCastTime, 0.01f,
                "[Min(.01)]（SpellDefinition.cs:31）：前摇 ≤ 0 时 WaitForSeconds 立刻返回，表现会跳帧。");
            CheckMin(issues, index, record, "maxDistance", "最远距离", d.MaxDistance, 0.1f,
                "[Min(.1)]（SpellDefinition.cs:37）：它同时是「沿朝向施法」时的落点距离（EngineerSpellCaster.cs:28-29）。");
            CheckMin(issues, index, record, "flightTime", "飞行时间", d.FlightTime, 0.05f,
                "[Min(.05)]（SpellDefinition.cs:39）：≤ 0 会让弹道瞬间到达或插值除零。");
            CheckMin(issues, index, record, "warningCircleScale", "警示圈缩放", d.WarningCircleScale, 0.01f,
                "[Min(.01)]（SpellDefinition.cs:44）：≤ 0 时圈会被缩成不可见。");

            if (d.ArcHeight < 0f)
            {
                var issue = Add(issues, SpellIssueLevel.Warning, "spell.number.arcHeight", index, record,
                    "抛物线高度是负数：" + d.ArcHeight.ToString("0.##"),
                    "SpellDefinition.arcHeight 上标了 [Min(0)]（SpellDefinition.cs:38）。");
                issue.Fixable = true;
                issue.SuggestedValue = "夹到 0";
            }
        }

        private static void CheckMin(List<SpellIssue> issues, int index, SpellRecord record, string field, string label,
                                     float value, float min, string evidence)
        {
            if (value >= min)
                return;

            var issue = Add(issues, SpellIssueLevel.Warning, "spell.number." + field, index, record,
                label + " 超出合法范围：" + value.ToString("0.###"),
                evidence + "（Inspector 会拦，但数据里可以直接写非法值。）");
            issue.Fixable = true;
            issue.SuggestedValue = "夹到 " + min.ToString("0.###");
        }

        // ─────────────────────────── 字段与运行时行为是否对得上 ───────────────────────────

        private static void ValidateFieldRelevance(int index, SpellRecord record, List<SpellIssue> issues)
        {
            SpellDefinition d = record.Definition;

            if (d.DeliveryType == SpellDeliveryType.DirectSpawn)
            {
                Add(issues, SpellIssueLevel.Info, "spell.field.projectileUnused", index, record,
                    "DirectSpawn 法术：弹道三项不参与运行时",
                    "deliveryType = DirectSpawn 时，运行时不走 SpawnInstance 的弹道分支，" +
                    "MaxDistance（仅在沿朝向施法取落点时用）、ArcHeight、FlightTime 都不会被这个法术使用" +
                    "（EngineerSpellCaster.cs:67-82、131-149）。");
            }
            else if (d.MaxDistance <= 0f && d.FlightTime <= 0f)
            {
                Add(issues, SpellIssueLevel.Warning, "spell.field.projectileZero", index, record,
                    "弹道距离与飞行时间都为 0",
                    "Projectile 法术靠这两项决定飞多远、飞多久（EngineerSpellCaster.cs:28-30、131-134）。");
            }

            if (d.ReleaseMode == SpellReleaseMode.Custom && d.FaceEngineerDirection)
            {
                Add(issues, SpellIssueLevel.Info, "spell.field.faceDirectionUnused", index, record,
                    "releaseMode = Custom：朝向跟随不生效",
                    "faceEngineerDirection 只在 releaseMode = FromEngineer 时参与旋转（EngineerSpellCaster.cs:62-65）。");
            }
        }

        // ─────────────────────────── 工具函数 ───────────────────────────

        private static string FixKeySuggestion(SpellResKind kind, string badKey)
        {
            SpellResourceIndex index = SpellResourceIndex.Instance;
            string normalized = Normalize(badKey);

            string found = null;
            int hits = 0;
            IReadOnlyList<string> keys = index.Keys(kind);
            for (int i = 0; i < keys.Count; i++)
            {
                if (!string.Equals(Normalize(keys[i]), normalized, StringComparison.OrdinalIgnoreCase))
                    continue;

                hits++;
                found = keys[i];
            }

            return hits == 1 ? found : null;
        }

        private static string Normalize(string key)
        {
            if (string.IsNullOrEmpty(key))
                return string.Empty;

            var builder = new StringBuilder(key.Length);
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (c != ' ' && c != '\t' && c != '-' && c != '_')
                    builder.Append(c);
            }
            return builder.ToString();
        }

        private static string SanitizeId(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "new_spell";

            var builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c))
                    builder.Append(char.ToLowerInvariant(c));
                else if (c == ' ' || c == '-' || c == '_')
                    builder.Append('_');
            }
            return builder.Length == 0 ? "new_spell" : builder.ToString();
        }

        private static string ReadPrivateString(UnityEngine.Object target, string field)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            return property != null ? property.stringValue : string.Empty;
        }

        private static SpellIssue Add(List<SpellIssue> issues, SpellIssueLevel level, string code, int recordIndex,
                                      SpellRecord record, string title, string detail)
        {
            var issue = new SpellIssue
            {
                Level = level,
                Code = code,
                RecordIndex = recordIndex,
                RecordLabel = record != null ? record.Label : "(注册表)",
                Title = title,
                Detail = detail
            };
            issues.Add(issue);
            return issue;
        }

        public static void Count(List<SpellIssue> issues, out int errors, out int warnings, out int infos)
        {
            errors = warnings = infos = 0;
            if (issues == null)
                return;

            for (int i = 0; i < issues.Count; i++)
            {
                switch (issues[i].Level)
                {
                    case SpellIssueLevel.Error: errors++; break;
                    case SpellIssueLevel.Warning: warnings++; break;
                    default: infos++; break;
                }
            }
        }

        // ─────────────────────────── 一键修复（只做确定性、数据层面的修复） ───────────────────────────

        /// <summary>
        /// 安全修复：
        ///   · 登记没登记的法术（追加进注册表）
        ///   · 删掉注册表里重复的条目
        ///   · 数字夹到字段约束范围
        ///   · 键的大小写/分隔符校正（只有注册表里唯一匹配时才改）
        ///   · deliveryType 与预制体执行器同步（只有能唯一判断时才改）
        ///   · ID 为空时用资产名生成一个（**不会**动已有的 ID —— ID 是引用的依据）
        /// 修复前对**被改动的资产**逐个备份。
        /// </summary>
        public static int ApplySafeFixes(SpellCatalog catalog, List<SpellIssue> issues)
        {
            if (catalog == null || issues == null)
                return 0;

            int fixedCount = 0;

            // ① 注册表级：去重
            int removed = catalog.RemoveDuplicateRegistryEntries();
            if (removed > 0)
                fixedCount += removed;

            // ② 逐个法术
            for (int i = 0; i < issues.Count; i++)
            {
                SpellIssue issue = issues[i];
                if (!issue.Fixable || issue.RecordIndex < 0 || issue.RecordIndex >= catalog.Records.Count)
                    continue;

                SpellRecord record = catalog.Records[issue.RecordIndex];
                if (record == null || record.Definition == null)
                    continue;

                bool changed = false;

                switch (issue.Code)
                {
                    case "spell.notRegistered":
                        {
                            string message;
                            if (catalog.Register(record, out message))
                            {
                                record.Registered = true;
                                changed = true;
                            }
                            break;
                        }

                    case "spell.id.empty":
                        changed = WriteString(record, "id", SanitizeId(record.FileName));
                        break;

                    case "spell.castPrefab.notInRegistry":
                        changed = FixKey(record, "castPrefabKey", SpellResKind.Prefab);
                        break;

                    case "spell.icon.notInRegistry":
                        changed = FixKey(record, "iconKey", SpellResKind.Sprite);
                        break;

                    case "spell.warningCircle.notInRegistry":
                        changed = FixKey(record, "warningCircleKey", SpellResKind.Sprite);
                        break;

                    case "spell.number.cooldown":
                        changed = WriteFloat(record, "cooldown", Mathf.Max(0f, record.Definition.Cooldown));
                        break;

                    case "spell.number.goldCost":
                        changed = WriteInt(record, "goldCost", Mathf.Max(0, record.Definition.GoldCost));
                        break;

                    case "spell.number.directCastTime":
                        changed = WriteFloat(record, "directCastTime", Mathf.Max(0.01f, record.Definition.DirectCastTime));
                        break;

                    case "spell.number.maxDistance":
                        changed = WriteFloat(record, "maxDistance", Mathf.Max(0.1f, record.Definition.MaxDistance));
                        break;

                    case "spell.number.arcHeight":
                        changed = WriteFloat(record, "arcHeight", Mathf.Max(0f, record.Definition.ArcHeight));
                        break;

                    case "spell.number.flightTime":
                        changed = WriteFloat(record, "flightTime", Mathf.Max(0.05f, record.Definition.FlightTime));
                        break;

                    case "spell.number.warningCircleScale":
                        changed = WriteFloat(record, "warningCircleScale", Mathf.Max(0.01f, record.Definition.WarningCircleScale));
                        break;

                    case "spell.executor.directMismatch":
                        changed = WriteEnum(record, "deliveryType", (int)SpellDeliveryType.Projectile);
                        break;

                    case "spell.executor.projectileMismatch":
                        changed = WriteEnum(record, "deliveryType", (int)SpellDeliveryType.DirectSpawn);
                        break;
                }

                if (changed)
                {
                    SpellBackup.BackupAsset(record.AssetPath, "一键修复前");
                    fixedCount++;
                }
            }

            if (fixedCount > 0)
                AssetDatabase.SaveAssets();

            return fixedCount;
        }

        private static bool FixKey(SpellRecord record, string field, SpellResKind kind)
        {
            string current = ReadString(record, field);
            string suggestion = FixKeySuggestion(kind, current);
            return suggestion != null && WriteString(record, field, suggestion);
        }

        private static string ReadString(SpellRecord record, string field)
        {
            var so = new SerializedObject(record.Definition);
            SerializedProperty property = so.FindProperty(field);
            return property != null ? property.stringValue : string.Empty;
        }

        private static bool WriteString(SpellRecord record, string field, string value)
        {
            var so = new SerializedObject(record.Definition);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
                return false;
            if (property.stringValue == value)
                return false;
            property.stringValue = value;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(record.Definition);
            return true;
        }

        private static bool WriteFloat(SpellRecord record, string field, float value)
        {
            var so = new SerializedObject(record.Definition);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
                return false;
            if (Mathf.Approximately(property.floatValue, value))
                return false;
            property.floatValue = value;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(record.Definition);
            return true;
        }

        private static bool WriteInt(SpellRecord record, string field, int value)
        {
            var so = new SerializedObject(record.Definition);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
                return false;
            if (property.intValue == value)
                return false;
            property.intValue = value;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(record.Definition);
            return true;
        }

        private static bool WriteEnum(SpellRecord record, string field, int value)
        {
            var so = new SerializedObject(record.Definition);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
                return false;
            if (property.enumValueIndex == value)
                return false;
            property.enumValueIndex = value;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(record.Definition);
            return true;
        }
    }
}
