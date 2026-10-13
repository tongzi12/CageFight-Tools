using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightSpellTool
{
    /// <summary>一个可编辑的伤害/效果字段。</summary>
    public sealed class SpellEffectField
    {
        public string PropertyPath;      // 组件上的序列化路径（如 damage / damage.initialDamage / baseDamage）
        public string Label;             // 中文标签
        public string Tooltip;           // 出处与单位
        public SerializedPropertyType Type;
        public float MinValue = float.NegativeInfinity;
        public bool IsBuff;              // 指向 BuffBase 实例的引用字段
    }

    /// <summary>施法预制体上的一个执行器组件（或其它带伤害字段的组件）。</summary>
    public sealed class SpellEffectComponent
    {
        public string TypeName;
        public MonoBehaviour Component;
        public readonly List<SpellEffectField> Fields = new List<SpellEffectField>();

        /// <summary>这个组件引用的 Buff 实例（范围法术真正的"效果"参数在这些资产里）。</summary>
        public readonly List<UnityEngine.Object> Buffs = new List<UnityEngine.Object>();

        /// <summary>与 <see cref="Buffs"/> 一一对应的序列化字段路径（窗口里用来就地改引用）。</summary>
        public readonly List<string> BuffPaths = new List<string>();
    }

    /// <summary>一个法术的伤害/效果全貌。</summary>
    public sealed class SpellEffectReport
    {
        public bool HasCastPrefab;
        public GameObject Prefab;
        public string PrefabPath;
        public readonly List<SpellEffectComponent> Components = new List<SpellEffectComponent>();

        /// <summary>一句话摘要（列表/详情头部显示），如「直接伤害 150（魔法）· 半径 3 · 击退 10」。</summary>
        public string Summary = string.Empty;

        /// <summary>没有直伤字段时的解释（范围型法术的伤害其实是 Buff 层数）。</summary>
        public string Note;
    }

    /// <summary>
    /// 读法术的"伤害与效果"。
    ///
    /// 为什么伤害不在 SpellDefinition 上：`SpellDefinition` 只有 18 个字段（id / 图标 / 冷却 / 金币 /
    /// 施法预制体键 / 投递方式 / 弹道参数 / 警示圈…）—— **没有任何伤害字段**。
    /// 真正的伤害写在**施法预制体的执行器组件**上（EngineerSpellCaster 通过三个接口之一调用它们）：
    ///   · `EngineerSpellDirectEffect.damage` / `.repel` / `.damageType`  —— 原地直伤（EngineerSpellDirectEffect.cs:9-11）
    ///   · `EngineerSpellProjectile.damage` / `.repel` / `.damageType`    —— 弹道直伤（EngineerSpellProjectile.cs:7-10）
    ///   · `MagicEnergySing.baseDamage` / `.radius` / `.repel` / `.damageType` —— 大招直伤（MagicEnergySing.cs:16-27）
    ///   · `UnstableOreMagicProjectile.baseDamage` / `.explosionRadius` / `.repel` —— 抛射爆炸（UnstableOreMagicProjectile.cs:19-32）
    ///   · `HugeCheeseMagic` / `HundredYearsIce` / `DancingStatueMagic` / `StrangeMineMagic` —— **没有直伤**，
    ///     它们的"效果"是**施加 Buff**（巨化 / 冰冻 / 麻痹 / 创伤），数值在对应的 Buff 资产上；
    ///     组件上能调的是半径、持续、施加间隔。
    ///
    /// 所以本工具的做法是：**扫描施法预制体上的组件，按白名单列出伤害/效果字段**，
    /// 并明确告诉用户"这个法术的伤害在哪"（有直伤就给摘要，没有就列出它引用的 Buff 并说明要去改哪个资产）。
    /// </summary>
    public static class SpellEffects
    {
        /// <summary>
        /// 白名单：字段名 → (中文标签, tooltip)。
        /// 只列**影响伤害/效果**的字段；targetLayers / 音效 / 动画状态名这类配置不列（避免噪声）。
        /// </summary>
        private static readonly Dictionary<string, string[]> Known = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            { "damage",            new[] { "直接伤害", "直接造成的伤害值（EngineerSpellDirectEffect / EngineerSpellProjectile 的 damage）" } },
            { "baseDamage",        new[] { "基础伤害（等级跟随）", "代码注释写明是「等级跟随的基准」——实际伤害会随施法者等级放大（MagicEnergySing.cs:16-17、UnstableOreMagicProjectile.cs:19-20）" } },
            { "initialDamage",     new[] { "基础伤害", "Damage 结构体的 initialDamage（Damage.cs:33）：进入防御/抗性结算前的原始伤害" } },
            { "damageType",        new[] { "伤害类型", "决定走物理防御还是魔法防御（DamageComputor，Damage.cs:40）" } },
            { "repel",             new[] { "击退强度", "命中后的击退力度" } },
            { "radius",            new[] { "影响半径（格）", "范围判定的半径" } },
            { "explosionRadius",   new[] { "爆炸半径（格）", "爆炸伤害的判定半径" } },
            { "duration",          new[] { "持续时间（秒）", "效果持续时长" } },
            { "tickInterval",      new[] { "施加间隔（秒）", "每隔多少秒施加一层效果 —— 总层数 ≈ 持续时间 ÷ 施加间隔" } },
            { "atk",               new[] { "物理攻击", "GameObjectProperty.atk" } },
            { "magicAtk",          new[] { "魔法攻击", "GameObjectProperty.magicAtk" } },
            { "damageMultiplier",  new[] { "造成伤害倍率", "GameObjectProperty.damageMultiplier（如愤怒增伤）" } },
            { "damageTakenMultiplier", new[] { "受到伤害倍率", "GameObjectProperty.damageTakenMultiplier" } },
            { "damageReduction",   new[] { "最终伤害减免", "GameObjectProperty.damageReduction（0~1）" } },
            { "amount",            new[] { "施加层数", "ProjectileBuffCarrier.amount：命中时施加几层" } }
        };

        private static readonly Dictionary<string, float> Minimums = new Dictionary<string, float>(StringComparer.Ordinal)
        {
            { "damage", 0f }, { "baseDamage", 0f }, { "initialDamage", 0f },
            { "radius", 0.1f }, { "explosionRadius", 0.1f },
            { "duration", 0.1f }, { "tickInterval", 0.05f },
            { "atk", 0f }, { "magicAtk", 0f }, { "amount", 1f }
        };

        public static SpellEffectReport Read(SpellDefinition definition)
        {
            var report = new SpellEffectReport();
            if (definition == null)
                return report;

            string castKey = definition.CastPrefabKey;
            GameObject prefab = string.IsNullOrEmpty(castKey) ? null : SpellResourceIndex.Instance.GetPrefab(castKey);
            if (prefab == null)
            {
                if (string.IsNullOrEmpty(castKey))
                {
                    report.Note = "施法预制体键是空的 → 这个法术在游戏里点了没反应（EngineerSpellCaster.cs:41-42 直接 return false），也就没有伤害可调。";
                }
                else if (!SpellResourceIndex.Instance.Has(SpellResKind.Prefab, castKey))
                {
                    report.Note = "施法预制体键「" + castKey + "」不在 PrefabRegistry 里 → 取不到预制体，伤害字段读不出来。" +
                                  "（先在 PrefabRegistry 里登记这个键指向的预制体。）";
                }
                else
                {
                    report.Note = "键「" + castKey + "」在 PrefabRegistry 里，但它指向的**预制体资产取不到**" +
                                  "（引用为空，或资产已被删除/移出工程）→ 伤害字段读不出来。";
                }
                return report;
            }

            report.HasCastPrefab = true;
            report.Prefab = prefab;
            report.PrefabPath = AssetDatabase.GetAssetPath(prefab);

            MonoBehaviour[] behaviours = prefab.GetComponents<MonoBehaviour>();
            var summary = new List<string>();

            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                var serialized = new SerializedObject(behaviour);
                var component = new SpellEffectComponent
                {
                    TypeName = behaviour.GetType().Name,
                    Component = behaviour
                };

                SerializedProperty iterator = serialized.GetIterator();
                while (iterator.NextVisible(true))
                {
                    string name = iterator.name;

                    // Buff 实例引用：范围法术真正的效果参数在这些资产上，列出来并给定位按钮
                    if (iterator.propertyType == SerializedPropertyType.ObjectReference &&
                        iterator.objectReferenceValue is BuffBase)
                    {
                        component.Buffs.Add(iterator.objectReferenceValue);
                        component.BuffPaths.Add(iterator.propertyPath);
                        continue;
                    }

                    if (iterator.propertyType == SerializedPropertyType.ObjectReference && iterator.objectReferenceValue == null
                        && name.EndsWith("Buff", StringComparison.Ordinal))
                    {
                        component.Buffs.Add(null);
                        component.BuffPaths.Add(iterator.propertyPath);
                        continue;
                    }

                    string[] meta;
                    if (!Known.TryGetValue(name, out meta))
                        continue;

                    var field = new SpellEffectField
                    {
                        PropertyPath = iterator.propertyPath,
                        Label = meta[0],
                        Tooltip = meta[1],
                        Type = iterator.propertyType
                    };

                    float min;
                    if (Minimums.TryGetValue(name, out min))
                        field.MinValue = min;

                    component.Fields.Add(field);
                }

                if (component.Fields.Count > 0 || component.Buffs.Count > 0)
                    report.Components.Add(component);
            }

            report.Summary = BuildSummary(report, definition);

            if (report.Components.Count == 0)
            {
                report.Note = "这个施法预制体上没有任何「被工具识别的伤害/效果字段」：可能它的效果完全由动画事件或代码里的常量决定，" +
                              "或者执行器组件被改成了别的类型。建议点「打开施放预制体」看一眼实际挂了什么。";
            }
            else
            {
                bool hasDirectDamage = false;
                for (int i = 0; i < report.Components.Count && !hasDirectDamage; i++)
                {
                    for (int k = 0; k < report.Components[i].Fields.Count; k++)
                    {
                        string leaf = LeafOf(report.Components[i].Fields[k].PropertyPath);
                        if (leaf == "damage" || leaf == "baseDamage" || leaf == "initialDamage" ||
                            leaf == "atk" || leaf == "magicAtk")
                        {
                            hasDirectDamage = true;
                            break;
                        }
                    }
                }

                if (!hasDirectDamage)
                {
                    report.Note = "这个法术没有直接伤害：它的效果是给目标施加状态（Buff），" +
                                  "所以下面只有半径 / 持续 / 施加间隔可调 —— 真正的效果数值在它引用的 Buff 资产上，" +
                                  "点每个 Buff 旁边的「定位」就能去改。";
                }
            }

            return report;
        }

        private static string LeafOf(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
                return propertyPath;
            int dot = propertyPath.LastIndexOf('.');
            return dot >= 0 ? propertyPath.Substring(dot + 1) : propertyPath;
        }

        /// <summary>一句话摘要：把识别到的关键数字拼起来（详情头部显示，便于一眼对比不同法术的强度）。</summary>
        private static string BuildSummary(SpellEffectReport report, SpellDefinition definition)
        {
            var parts = new List<string>();
            string damageType = null;
            string directDamage = null;
            string radius = null;
            string rhythm = null;
            var buffs = new List<string>();

            for (int i = 0; i < report.Components.Count; i++)
            {
                SpellEffectComponent component = report.Components[i];
                var serialized = new SerializedObject(component.Component);
                string componentDamage = null;

                for (int k = 0; k < component.Fields.Count; k++)
                {
                    SpellEffectField field = component.Fields[k];
                    SerializedProperty property = serialized.FindProperty(field.PropertyPath);
                    if (property == null)
                        continue;

                    string leaf = LeafOf(field.PropertyPath);
                    switch (leaf)
                    {
                        case "damage":
                        case "baseDamage":
                        case "initialDamage":
                        case "atk":
                        case "magicAtk":
                            componentDamage = property.propertyType == SerializedPropertyType.Integer
                                ? property.intValue.ToString()
                                : property.floatValue.ToString("0.##");
                            break;
                        case "damageType":
                            damageType = DamageTypeName(property.enumValueIndex);
                            break;
                        case "radius":
                        case "explosionRadius":
                            radius = property.floatValue.ToString("0.##");
                            break;
                        case "duration":
                        case "tickInterval":
                            rhythm = (rhythm == null ? string.Empty : rhythm + " / ") + property.floatValue.ToString("0.##") + "s";
                            break;
                    }
                }

                if (componentDamage != null)
                    directDamage = componentDamage;

                for (int b = 0; b < component.Buffs.Count; b++)
                {
                    if (component.Buffs[b] != null)
                        buffs.Add(component.Buffs[b].GetType().Name);
                }
            }

            if (directDamage != null)
                parts.Add("直接伤害 " + directDamage + (damageType != null ? "（" + damageType + "）" : string.Empty));
            if (radius != null)
                parts.Add("半径 " + radius);
            if (rhythm != null)
                parts.Add("节奏 " + rhythm);
            if (buffs.Count > 0)
                parts.Add("施加 " + string.Join(" / ", buffs.ToArray()));

            return parts.Count > 0 ? string.Join(" · ", parts.ToArray()) : "（没有可识别的伤害参数）";
        }

        public static string DamageTypeName(int enumIndex)
        {
            // DamageType { normal, magic }（Damage.cs:6-…）：显示成中文更好读
            return enumIndex == 1 ? "魔法" : "物理";
        }

        /// <summary>字段当前值（用于摘要与显示；找不到字段时返回 null）。</summary>
        public static SerializedProperty Find(SerializedObject serialized, SpellEffectField field)
        {
            return serialized != null ? serialized.FindProperty(field.PropertyPath) : null;
        }
    }
}
