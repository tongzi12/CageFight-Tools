using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightEngineerTool
{
    public enum EngineerIssueLevel
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>一条体检结论。Code 是稳定标识（便于以后按代码屏蔽某条规则）。</summary>
    public sealed class EngineerIssue
    {
        public EngineerIssueLevel Level;
        public string Code = string.Empty;
        public string Title = string.Empty;
        public string Detail = string.Empty;

        /// <summary>定位用：点「定位」跳到哪个区块（与主窗口的分区标题一致）。</summary>
        public string Section = string.Empty;

        public bool Fixable;
        public string SuggestedValue;

        /// <summary>一键修复的执行体（就地改资产；写盘由调用方统一做，写前已备份）。</summary>
        public Action Fix;

        public Color LevelColor
        {
            get
            {
                switch (Level)
                {
                    case EngineerIssueLevel.Error: return EngineerEditorUI.Error;
                    case EngineerIssueLevel.Warning: return EngineerEditorUI.Warn;
                    default: return EngineerEditorUI.Muted;
                }
            }
        }

        public string LevelLabel
        {
            get
            {
                switch (Level)
                {
                    case EngineerIssueLevel.Error: return "[错误]";
                    case EngineerIssueLevel.Warning: return "[警告]";
                    default: return "[提示]";
                }
            }
        }
    }

    /// <summary>
    /// 工程师体检：把「策划看不出来、但进游戏会出问题」的配置挑出来。
    ///
    /// 每条规则都对着运行时的同一条链路写的，出处写在详情里。最要紧的两条是：
    ///   · **没登记进选装注册表** → 选装界面里根本没有它（LoadoutSelectionPanel 遍历的是注册表列表），
    ///     而存档里的选择是按 ID 解析的 → 解析失败时 PlayerLoadoutSpawner 报「未能解析当前工程师」，
    ///     **这一局没有工程师**；
    ///   · **prefabKey 不在 PrefabRegistry** → 预载走 LoadRegisteredGameObject（PlayerLoadoutManager.cs:183），
    ///     键不在注册表就加载不到 → GetGameObject 返回 null → 同上，工程师不生成。
    /// </summary>
    public static class EngineerValidator
    {
        /// <summary>校验一个工程师。<paramref name="all"/> 用于查重 ID。</summary>
        public static void Validate(EngineerRecord record, List<EngineerRecord> all, List<EngineerIssue> output)
        {
            if (record == null || output == null)
                return;

            ValidateIdentity(record, all, output);
            ValidateRegistry(record, output);
            ValidateKeys(record, output);
            ValidatePortrait(record, output);
            ValidateSpell(record, output);
            ValidatePrefab(record, output);
            ValidateBook(record, output);
        }

        #region 标识
        private static void ValidateIdentity(EngineerRecord record, List<EngineerRecord> all, List<EngineerIssue> output)
        {
            if (string.IsNullOrWhiteSpace(record.Id))
            {
                output.Add(New(EngineerIssueLevel.Error, "def.idEmpty", "id 为空",
                    "选装注册表的 AddEngineers 会直接跳过没有 ID 的定义（LoadoutDefinitionRegistry.cs:110），" +
                    "存档也是按 ID 记录玩家选择的 —— 这个工程师既选不到也存不住。",
                    SectionIdentity));
            }

            if (string.IsNullOrWhiteSpace(record.DisplayName))
            {
                output.Add(New(EngineerIssueLevel.Warning, "def.nameEmpty", "displayName 为空",
                    "选装界面/图鉴上会显示空白名字（EngineerDefinition.DisplayName）。", SectionIdentity));
            }

            if (all != null && !string.IsNullOrWhiteSpace(record.Id))
            {
                int duplicates = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] != null && !ReferenceEquals(all[i], record) &&
                        string.Equals(all[i].Id, record.Id, StringComparison.Ordinal))
                        duplicates++;
                }

                if (duplicates > 0)
                {
                    output.Add(New(EngineerIssueLevel.Error, "def.idDup", "ID 与其它 " + duplicates + " 个工程师重复",
                        "注册表按 ID 建索引，重复时只保留第一个并打一条警告（LoadoutDefinitionRegistry.cs:111-115）→ " +
                        "另一个永远选不到，且存档里选的是哪一个无法区分。", SectionIdentity));
                }
            }
        }
        #endregion

        #region 注册表
        private static void ValidateRegistry(EngineerRecord record, List<EngineerIssue> output)
        {
            EngineerResourceIndex index = EngineerResourceIndex.Instance;

            if (!record.IsRegistered)
            {
                var issue = New(EngineerIssueLevel.Error, "reg.missing", "没有登记进选装注册表",
                    "LoadoutDefinitionRegistry.engineerDefinitions 里没有这个资产。" +
                    "选装界面遍历的正是注册表列表（LoadoutSelectionPanel.cs:230/241 → ResourceManager.EngineerDefinitions），" +
                    "所以**界面里根本看不到它**；如果存档里已经选了它的 ID，TryGetSelectedEngineer 会失败， " +
                    "PlayerLoadoutSpawner 打「未能解析当前工程师」并返回 false（PlayerLoadoutSpawner.cs:35-39）→ 这一局没有工程师。",
                    SectionRegistry);
                issue.Fixable = true;
                issue.SuggestedValue = "加入选装注册表";
                issue.Fix = () => EngineerRegistryWriter.Add(record);
                output.Add(issue);
            }

            string defaultId = index.DefaultEngineerId;
            if (string.IsNullOrWhiteSpace(defaultId))
            {
                var issue = New(EngineerIssueLevel.Warning, "reg.defaultEmpty", "注册表没有默认工程师",
                    "defaultEngineerId 为空 → TryGetDefaultLoadout 返回 false（LoadoutDefinitionRegistry.cs:81-84，" +
                    "它要求工程师与种族都有效）→ 新存档没有默认选择，第一次进选装界面是空的。", SectionRegistry);
                issue.Fixable = true;
                issue.SuggestedValue = "把默认工程师设为当前这个";
                issue.Fix = () => EngineerRegistryWriter.SetDefaultEngineer(record.Id);
                output.Add(issue);
            }
            else if (!EngineerRegistryHasId(defaultId))
            {
                var issue = New(EngineerIssueLevel.Warning, "reg.defaultInvalid", "默认工程师 ID 在注册表里找不到",
                    "defaultEngineerId = 「" + defaultId + "」不在已登记的工程师里 → TryGetDefaultLoadout 失败（同上）。",
                    SectionRegistry);
                issue.Fixable = true;
                issue.SuggestedValue = "把默认工程师设为当前这个";
                issue.Fix = () => EngineerRegistryWriter.SetDefaultEngineer(record.Id);
                output.Add(issue);
            }
        }

        private static bool EngineerRegistryHasId(string id)
        {
            EngineerResourceIndex index = EngineerResourceIndex.Instance;
            List<string> paths = index.RegisteredEngineerPaths;
            for (int i = 0; i < paths.Count; i++)
            {
                EngineerDefinition definition = AssetDatabase.LoadAssetAtPath<EngineerDefinition>(paths[i]);
                if (definition != null && string.Equals(definition.Id, id, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }
        #endregion

        #region 资源键
        private static void ValidateKeys(EngineerRecord record, List<EngineerIssue> output)
        {
            EngineerResourceIndex index = EngineerResourceIndex.Instance;

            if (string.IsNullOrWhiteSpace(record.IconKey))
            {
                output.Add(New(EngineerIssueLevel.Warning, "def.iconEmpty", "iconKey 为空",
                    "选装列表里这个工程师没有小图标（EngineerDefinition.IconKey，SpriteRegistry 键）。", SectionKeys));
            }
            else if (!index.HasKey(EngineerResKind.Sprite, record.IconKey))
            {
                output.Add(New(EngineerIssueLevel.Warning, "def.iconMissing", "iconKey 不在 SpriteRegistry 里",
                    "键「" + record.IconKey + "」在 SpriteRegistry（" + index.Keys(EngineerResKind.Sprite).Count +
                    " 个键）里找不到 → 运行时 GetSprite 返回 null，界面显示空白。", SectionKeys));
            }

            if (string.IsNullOrWhiteSpace(record.PrefabKey))
            {
                output.Add(New(EngineerIssueLevel.Error, "def.prefabEmpty", "prefabKey 为空",
                    "PlayerLoadoutSpawner 用它取预制体（PlayerLoadoutSpawner.cs:43），为空时 GetGameObject 返回 null → " +
                    "打「工程师预制体未预载或对象池未就绪」并返回 false（:44-48）→ **这一局没有工程师**。", SectionKeys));
            }
            else if (!index.HasKey(EngineerResKind.Prefab, record.PrefabKey))
            {
                output.Add(New(EngineerIssueLevel.Error, "def.prefabMissing", "prefabKey 不在 PrefabRegistry 里",
                    "键「" + record.PrefabKey + "」不在 PrefabRegistry（" + index.Keys(EngineerResKind.Prefab).Count +
                    " 个键）里。进游戏时工程师预制体是按注册表预载的（PlayerLoadoutManager.cs:183 " +
                    "LoadRegisteredGameObject）→ 键没登记就加载不到 → 同上，**这一局没有工程师**。", SectionKeys));
            }
            else if (string.IsNullOrEmpty(record.PrefabPath))
            {
                output.Add(New(EngineerIssueLevel.Error, "def.prefabAssetMissing", "prefabKey 指向的预制体资产取不到",
                    "键在注册表里，但按它反查不到资产（资产可能被删/改名）→ 预载会失败，工程师不生成。", SectionKeys));
            }
        }
        #endregion

        #region 立绘与待机帧
        private static void ValidatePortrait(EngineerRecord record, List<EngineerIssue> output)
        {
            EngineerResourceIndex index = EngineerResourceIndex.Instance;

            if (string.IsNullOrWhiteSpace(record.PortraitFrameKey))
            {
                output.Add(New(EngineerIssueLevel.Warning, "def.portraitEmpty", "portraitFrameKey 为空",
                    "菜单立绘为空 → EngineerMenuPortrait 会回退到 iconKey（EngineerMenuPortrait.cs:135-140），" +
                    "立绘位显示的是小图标。", SectionPortrait));
            }
            else if (!index.HasKey(EngineerResKind.Sprite, record.PortraitFrameKey))
            {
                output.Add(New(EngineerIssueLevel.Warning, "def.portraitMissing", "portraitFrameKey 不在 SpriteRegistry 里",
                    "键「" + record.PortraitFrameKey + "」找不到 → 立绘加载不到，界面是空的。", SectionPortrait));
            }

            if (record.IdleFrameKeys.Count == 0)
            {
                output.Add(New(EngineerIssueLevel.Info, "def.idleEmpty", "没有配待机帧",
                    "idlePortraitFrameKeys 为空 → EngineerMenuPortrait 只显示静态立绘（EngineerMenuPortrait.cs:84-87）。" +
                    "（全工程 5 个工程师目前都没配。）", SectionPortrait));
            }

            var blanks = new List<int>();
            var missing = new List<string>();
            for (int i = 0; i < record.IdleFrameKeys.Count; i++)
            {
                string key = record.IdleFrameKeys[i];
                if (string.IsNullOrEmpty(key))
                {
                    blanks.Add(i);
                    continue;
                }
                if (!index.HasKey(EngineerResKind.Sprite, key) && !missing.Contains(key))
                    missing.Add(key);
            }

            if (blanks.Count > 0)
            {
                var issue = New(EngineerIssueLevel.Warning, "def.idleBlank", "待机帧里有 " + blanks.Count + " 个空项",
                    "序号：" + Join(blanks) + "。空项会让那一帧不显示（EngineerMenuPortrait 逐键取图）。", SectionPortrait);
                issue.Fixable = true;
                issue.SuggestedValue = "删掉这些空项";
                issue.Fix = () => EngineerAssetWriter.RemoveBlankIdleFrames(record);
                output.Add(issue);
            }

            if (missing.Count > 0)
            {
                output.Add(New(EngineerIssueLevel.Warning, "def.idleMissing", "待机帧里有 " + missing.Count + " 个键不在 SpriteRegistry 里",
                    "键：" + string.Join("、", missing.ToArray()) + " → 播到那一帧会丢图。工具只报告，不擅自删（可能是刚改名还没登记）。",
                    SectionPortrait));
            }

            if (record.IdleFrameRate < 1f || record.IdleFrameRate > 20f)
            {
                var issue = New(EngineerIssueLevel.Warning, "def.idleRate", "待机帧率超出 1~20",
                    "idlePortraitFrameRate = " + record.IdleFrameRate.ToString("0.###") +
                    "，字段上的 [Range(1,20)] 只夹 Inspector 显示；运行时按原值算（EngineerMenuPortrait.cs:107 " +
                    "用 1/帧率，仅对 0 做了兜底）。", SectionPortrait);
                issue.Fixable = true;
                issue.SuggestedValue = "夹到 1~20";
                issue.Fix = () => EngineerAssetWriter.ClampIdleFrameRate(record);
                output.Add(issue);
            }
        }
        #endregion

        #region 自带法术
        private static void ValidateSpell(EngineerRecord record, List<EngineerIssue> output)
        {
            EngineerResourceIndex index = EngineerResourceIndex.Instance;

            if (string.IsNullOrWhiteSpace(record.InnateSpellId))
            {
                output.Add(New(EngineerIssueLevel.Warning, "def.spellEmpty", "没有自带法术（innateSpellId 为空）",
                    "局内法术槽位 0 = 工程师自带法术（PlayerLoadoutManager.cs:151/161），为空时这一格没有法术；" +
                    "选装界面预载也会跳过它（LoadoutSelectionPanel.cs:242-243）。", SectionSpell));
                return;
            }

            if (!index.HasSpellId(record.InnateSpellId))
            {
                output.Add(New(EngineerIssueLevel.Warning, "def.spellMissing", "自带法术 ID 不在选装注册表的法术列表里",
                    "innateSpellId = 「" + record.InnateSpellId + "」在注册表 spellDefinitions（" + index.SpellIds.Count +
                    " 个）里找不到 → ResourceManager.TryGetSpell 失败（PlayerLoadoutManager.cs:184）→ 自带法术不可用，" +
                    "施法预制体也不会被预载。", SectionSpell));
            }
        }
        #endregion

        #region 预制体
        private static void ValidatePrefab(EngineerRecord record, List<EngineerIssue> output)
        {
            GameObject prefab = record.PrefabAsset;
            if (prefab == null)
                return;   // 上面「预制体取不到」已经报过了

            EngineerController controller = EngineerPrefabAccess.GetController(prefab);
            GameObjectProperty property = EngineerPrefabAccess.GetProperty(prefab);

            if (controller == null)
            {
                output.Add(New(EngineerIssueLevel.Error, "prefab.noController", "预制体上没有 EngineerController",
                    "PlayerLoadoutSpawner 拿到预制体后立刻要它（PlayerLoadoutSpawner.cs:83-90）→ 缺了就 LogError、" +
                    "把实例还给对象池并返回 false → **这一局没有工程师**。", SectionPrefab));
            }

            if (property == null)
            {
                output.Add(New(EngineerIssueLevel.Error, "prefab.noProperty", "预制体上没有 GameObjectProperty",
                    "EngineerController 声明了 [RequireComponent(GameObjectProperty)]（EngineerController.cs:7），" +
                    "但它只在编辑器加组件时生效 —— 预制的侧向/等级上下文注入都读这个组件。", SectionPrefab));
            }

            if (controller != null && property != null && controller.maxHp != property.maxHp)
            {
                output.Add(New(EngineerIssueLevel.Info, "prefab.hpMismatch",
                    "两处血量不一致（EngineerController " + controller.maxHp + " / GameObjectProperty " + property.maxHp + "）",
                    "工程师自己的血量池用的是 **EngineerController.maxHp**（EngineerController.cs:117/125/183），" +
                    "GameObjectProperty.maxHp 在这个单位上不参与结算 —— 两者的数值不一样时容易看错。", SectionPrefab));
            }

            if (controller != null)
            {
                Animator animator = prefab.GetComponent<Animator>();
                if (animator == null || animator.runtimeAnimatorController == null)
                {
                    output.Add(New(EngineerIssueLevel.Info, "prefab.noAnimator", "预制体没有 Animator 或没配控制器",
                        "移动动画不会播（EngineerController.UpdateAnimation 会直接返回）。", SectionPrefab));
                }
                else
                {
                    HashSet<string> bools = EngineerPrefabAccess.BoolParametersOf(animator);
                    if (bools.Count > 0 && !bools.Contains(controller.moveParameter))
                    {
                        output.Add(New(EngineerIssueLevel.Warning, "prefab.moveParam", "移动参数名与动画控制器对不上",
                            "moveParameter = 「" + controller.moveParameter + "」，但控制器里的 bool/trigger 参数是：" +
                            string.Join("、", new List<string>(bools).ToArray()) + " → SetBool 无效，**移动动画不会播**（EngineerController.cs:113/174）。",
                            SectionPrefab));
                    }
                }

                if (controller.spellAnchor == null)
                {
                    output.Add(New(EngineerIssueLevel.Info, "prefab.spellAnchor", "没配 spellAnchor（施法锚点）",
                        "会在运行时回退成自身 transform（EngineerController.cs:116）→ 法术从脚底发出。", SectionPrefab));
                }
            }

            if (!EngineerPrefabAccess.HasShootPoint(prefab))
            {
                output.Add(New(EngineerIssueLevel.Info, "prefab.shootPoint", "预制体里没有「Shoot point」子节点",
                    "抛射法术的起点会回退到施法锚点（EngineerController.cs:67-82）→ 出手位置偏低。", SectionPrefab));
            }
        }
        #endregion

        #region 图鉴
        private static void ValidateBook(EngineerRecord record, List<EngineerIssue> output)
        {
            EngineerResourceIndex index = EngineerResourceIndex.Instance;
            if (index.BookPrefabKeys.Count == 0 || string.IsNullOrWhiteSpace(record.PrefabKey))
                return;

            if (!index.BookPrefabKeys.Contains(record.PrefabKey))
            {
                output.Add(New(EngineerIssueLevel.Info, "book.prefabKey", "prefabKey 不在图鉴目录里",
                    "出战工程师时走的是 BookProgress.MarkOwned(engineer.PrefabKey)（PlayerLoadoutSpawner.cs:51），" +
                    "而图鉴按 BookEntry.prefabKey 匹配条目（BookCatalog.cs:100-112）→ 这个键不在图鉴目录里，" +
                    "**出战这个工程师不会点亮任何图鉴条目**。", SectionRegistry));
            }
        }
        #endregion

        #region 一键修复
        /// <summary>
        /// 执行所有「可修」的修复（就地改资产），返回修了几处。
        /// **只做安全确定的修复**：补登记、清空项、夹范围、补默认值；绝不删资源键、绝不动不报错的字段。
        /// </summary>
        public static int ApplySafeFixes(List<EngineerIssue> issues)
        {
            if (issues == null)
                return 0;

            int fixedCount = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                EngineerIssue issue = issues[i];
                if (issue == null || !issue.Fixable || issue.Fix == null)
                    continue;

                issue.Fix();
                fixedCount++;
            }

            AssetDatabase.SaveAssets();
            return fixedCount;
        }
        #endregion

        #region 常量与工具
        public const string SectionIdentity = "标识";
        public const string SectionKeys = "资源键";
        public const string SectionPortrait = "立绘与待机帧";
        public const string SectionSpell = "自带法术";
        public const string SectionPrefab = "局内数值（预制体）";
        public const string SectionRegistry = "选装注册表";

        public static readonly string[] Sections =
        {
            SectionIdentity, SectionKeys, SectionPortrait, SectionSpell, SectionPrefab, SectionRegistry
        };

        private static EngineerIssue New(EngineerIssueLevel level, string code, string title, string detail, string section)
        {
            return new EngineerIssue
            {
                Level = level,
                Code = code,
                Title = title,
                Detail = detail,
                Section = section
            };
        }

        private static string Join(List<int> values)
        {
            var parts = new List<string>();
            for (int i = 0; i < values.Count; i++)
                parts.Add(values[i].ToString());
            return string.Join("、", parts.ToArray());
        }
        #endregion
    }
}
