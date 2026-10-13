using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CageFightUnitTool
{
    /// <summary>表格里的一行。</summary>
    public sealed class UnitCsvRow
    {
        public int LineNumber;
        public string PrefabName = string.Empty;
        public int MatchedIndex = -1;
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Get(string field)
        {
            string v;
            return Values.TryGetValue(field, out v) ? v : null;
        }
    }

    /// <summary>导入计划：先解析预览，确认后才写盘。</summary>
    public sealed class UnitCsvPlan
    {
        public string FilePath = string.Empty;
        public readonly List<UnitCsvRow> Rows = new List<UnitCsvRow>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> MissingColumns = new List<string>();
        public int MatchedCount;
        public int UnmatchedCount;

        public bool HasAnythingToDo { get { return MatchedCount > 0; } }
    }

    /// <summary>
    /// 兵种数值表：导出成 CSV 给策划在 Excel 里整表调数值，再回导。
    /// 匹配键是「单位预制体名」（第一列），改名字对不上就不会被覆盖。
    /// </summary>
    public static class UnitEditorCsv
    {
        /// <summary>字段定义：字段名 / 中文表头 / 归属（def = TroopDefinition，prop = 预制体上的数值）。</summary>
        private sealed class Column
        {
            public string Field;
            public string Title;
            public bool IsDefinition;
        }

        private static readonly List<Column> Columns = new List<Column>
        {
            new Column { Field = "prefab",                Title = "单位预制体名（匹配键，请勿改）" },
            new Column { Field = "displayName",           Title = "显示名",        IsDefinition = true },
            new Column { Field = "id",                    Title = "训练ID",        IsDefinition = true },
            new Column { Field = "tag",                   Title = "定位标签(0无1近战2远程3攻城4肉盾5辅助)", IsDefinition = true },
            new Column { Field = "threatScore",           Title = "威胁权重",      IsDefinition = true },
            new Column { Field = "tier",                  Title = "AI威胁阶级1-3", IsDefinition = true },
            new Column { Field = "unlockLevel",           Title = "兵营阶级1-3",   IsDefinition = true },
            new Column { Field = "trainCount",            Title = "单次产出",      IsDefinition = true },
            new Column { Field = "cooldown",              Title = "训练冷却",      IsDefinition = true },
            new Column { Field = "upkeep",                Title = "维护费",        IsDefinition = true },
            new Column { Field = "requiresOwnership",     Title = "需要拥有(0/1)", IsDefinition = true },
            new Column { Field = "iconKey",               Title = "兵营图标键",    IsDefinition = true },
            new Column { Field = "prefabKey",             Title = "单位预制体键",  IsDefinition = true },
            new Column { Field = "animatorControllerKey", Title = "动画控制器键(训练配置)", IsDefinition = true },
            new Column { Field = "animationSpriteKey",    Title = "基础动画贴图键(训练配置)", IsDefinition = true },

            new Column { Field = "maxHp",                 Title = "生命值" },
            new Column { Field = "defense",               Title = "物理防御" },
            new Column { Field = "magicDefense",          Title = "魔法防御" },
            new Column { Field = "atk",                   Title = "物理攻击" },
            new Column { Field = "magicAtk",              Title = "魔法攻击" },
            new Column { Field = "atkRate",               Title = "攻速系数" },
            new Column { Field = "isRemoteAtk",           Title = "远程(0/1)" },
            new Column { Field = "atkRangeX",             Title = "射程X" },
            new Column { Field = "atkRangeY",             Title = "射程Y" },
            new Column { Field = "moveSpeed",             Title = "移动速度" },
            new Column { Field = "repel",                 Title = "击退强度" },
            new Column { Field = "antiRepel",             Title = "抗击退" },
            new Column { Field = "suckBlood",             Title = "吸血比例" },
            new Column { Field = "baseMissChance",        Title = "基础未命中率0-1" },
            new Column { Field = "occupySpaceX",          Title = "占地宽" },
            new Column { Field = "occupySpaceY",          Title = "占地高" },
            new Column { Field = "atkObj",                Title = "投射物资源键" },
            new Column { Field = "spriteKey",             Title = "动画贴图键(预制体)" },
            new Column { Field = "animatorControllerKeyProp", Title = "动画控制器键(预制体)" },
            new Column { Field = "deathAngularSpeed",     Title = "死亡旋转速度" },
            new Column { Field = "deathFadeAlpha",        Title = "死亡透明度" },
            new Column { Field = "deathDarkFactor",       Title = "死亡变暗系数" },
            new Column { Field = "antiOverlapRadius",     Title = "防重叠半径" }
        };

        static UnitEditorCsv()
        {
            for (int i = 1; i <= 8; i++)
                Columns.Add(new Column { Field = "audioKey" + i, Title = "音效键" + i });
        }

        #region 导出
        public static bool Export(UnitCatalog catalog, string filePath, out string message)
        {
            message = string.Empty;
            if (catalog == null || catalog.Count == 0)
            {
                message = "没有扫描到任何兵种，先到「设置」页点「重新扫描兵种」。";
                return false;
            }

            var sb = new StringBuilder();
            for (int i = 0; i < Columns.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(Escape(Columns[i].Field + "（" + Columns[i].Title + "）"));
            }
            sb.Append("\r\n");

            for (int i = 0; i < catalog.Count; i++)
            {
                UnitRecord record = catalog.Records[i];
                UnitStats stats = catalog.GetStats(record);
                TroopDefinition definition = record.Definition;

                for (int c = 0; c < Columns.Count; c++)
                {
                    if (c > 0)
                        sb.Append(',');

                    Column column = Columns[c];
                    sb.Append(Escape(CellValue(record, stats, definition, column)));
                }
                sb.Append("\r\n");
            }

            try
            {
                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
                message = "已导出 " + catalog.Count + " 个兵种的数值表：\n" + filePath;
                return true;
            }
            catch (Exception e)
            {
                message = "导出失败：" + e.Message;
                return false;
            }
        }

        public static bool ExportTemplate(string filePath, out string message)
        {
            message = string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < Columns.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(Escape(Columns[i].Field + "（" + Columns[i].Title + "）"));
            }
            sb.Append("\r\n");
            // 示例行的第一列故意用一个匹配不到任何兵种的名字 ——
            // 早期模板直接放了真实的 "Miner rat" 数据，策划不改就导入会把矿工鼠的数值整行覆盖。
            sb.Append("示例行请删除,示例兵种,sample_unit,1,1,1,0,1,12,0,0,Mouse Troop UI_0,Miner rat,,,100,0,0,20,20,1,0,1,2,3,0.4,1,0,0,1,1,,,,180,0.5,0.4,0.2,,,,,,,,\r\n");

            try
            {
                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
                message = "模板已导出（含一行示例，第一列故意匹配不到任何兵种，可直接删掉）：\n" + filePath;
                return true;
            }
            catch (Exception e)
            {
                message = "导出模板失败：" + e.Message;
                return false;
            }
        }

        private static string CellValue(UnitRecord record, UnitStats stats, TroopDefinition definition, Column column)
        {
            switch (column.Field)
            {
                case "prefab": return record.PrefabName;
            }

            if (column.IsDefinition)
            {
                if (definition == null)
                    return string.Empty;

                switch (column.Field)
                {
                    case "displayName": return definition.DisplayName;
                    case "id": return definition.Id;
                    case "tag": return ((int)definition.Tag).ToString();
                    case "threatScore": return definition.ThreatScore.ToString("0.###");
                    case "tier": return definition.Tier.ToString();
                    case "unlockLevel": return definition.UnlockLevel.ToString();
                    case "trainCount": return definition.TrainCount.ToString();
                    case "cooldown": return definition.Cooldown.ToString("0.###");
                    case "upkeep": return definition.Upkeep.ToString();
                    case "requiresOwnership": return definition.RequiresOwnership ? "1" : "0";
                    case "iconKey": return definition.IconKey;
                    case "prefabKey": return definition.PrefabKey;
                    case "animatorControllerKey": return definition.AnimatorControllerKey;
                    case "animationSpriteKey": return definition.AnimationSpriteKey;
                }
                return string.Empty;
            }

            if (!stats.Valid)
                return string.Empty;

            switch (column.Field)
            {
                case "maxHp": return stats.MaxHp.ToString();
                case "defense": return stats.Defense.ToString();
                case "magicDefense": return stats.MagicDefense.ToString();
                case "atk": return stats.Atk.ToString();
                case "magicAtk": return stats.MagicAtk.ToString();
                case "atkRate": return stats.AtkRate.ToString("0.###");
                case "isRemoteAtk": return stats.IsRemoteAtk ? "1" : "0";
                case "atkRangeX": return stats.AtkRange.x.ToString();
                case "atkRangeY": return stats.AtkRange.y.ToString();
                case "moveSpeed": return stats.MoveSpeed.ToString("0.###");
                case "repel": return stats.Repel.ToString("0.###");
                case "antiRepel": return stats.AntiRepel.ToString("0.###");
                case "suckBlood": return stats.SuckBlood.ToString("0.###");
                case "baseMissChance": return stats.BaseMissChance.ToString("0.###");
                case "occupySpaceX": return stats.OccupySpace.x.ToString();
                case "occupySpaceY": return stats.OccupySpace.y.ToString();
                case "atkObj": return stats.AtkObjKey;
                case "spriteKey": return stats.SpriteKey;
                case "animatorControllerKeyProp": return stats.AnimatorControllerKey;
                case "deathAngularSpeed": return stats.DeathAngularSpeed.ToString("0.###");
                case "deathFadeAlpha": return stats.DeathFadeAlpha.ToString("0.###");
                case "deathDarkFactor": return stats.DeathDarkFactor.ToString("0.###");
                case "antiOverlapRadius": return stats.AntiOverlapRadius.ToString("0.###");
            }

            if (column.Field.StartsWith("audioKey", StringComparison.Ordinal))
            {
                int index;
                if (int.TryParse(column.Field.Substring("audioKey".Length), out index) && index >= 1 && index <= 8)
                    return stats.AudioKeys[index - 1];
            }

            return string.Empty;
        }
        #endregion

        #region 导入
        public static UnitCsvPlan BuildPlan(UnitCatalog catalog, string filePath)
        {
            var plan = new UnitCsvPlan();
            plan.FilePath = filePath;

            string text;
            try
            {
                text = ReadTextAutoEncoding(filePath);
            }
            catch (Exception e)
            {
                plan.Warnings.Add("读取文件失败：" + e.Message);
                return plan;
            }

            List<List<string>> table = ParseCsv(text);
            if (table.Count == 0)
            {
                plan.Warnings.Add("文件是空的。");
                return plan;
            }

            var columnFields = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string header in table[0])
            {
                string field = ResolveField(header);
                columnFields.Add(field);
                if (field != null)
                    seen.Add(field);
            }

            if (!seen.Contains("prefab"))
                plan.Warnings.Add("表头里没有找到「单位预制体名」列（第一列），无法匹配兵种。");

            for (int i = 0; i < Columns.Count; i++)
            {
                if (!seen.Contains(Columns[i].Field))
                    plan.MissingColumns.Add(Columns[i].Title);
            }

            var byPrefabName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < catalog.Count; i++)
            {
                string name = catalog.Records[i].PrefabName;
                if (!string.IsNullOrEmpty(name) && !byPrefabName.ContainsKey(name))
                    byPrefabName.Add(name, i);
            }

            for (int r = 1; r < table.Count; r++)
            {
                List<string> cells = table[r];
                if (IsEmptyRow(cells))
                    continue;

                var row = new UnitCsvRow();
                row.LineNumber = r + 1;
                for (int c = 0; c < cells.Count && c < columnFields.Count; c++)
                {
                    string field = columnFields[c];
                    if (field == null)
                        continue;

                    string value = cells[c];
                    if (value == null || (field != "prefab" && value.Length == 0))
                        continue;   // 空单元格 = 保持原值

                    row.Values[field] = value;
                }

                row.PrefabName = (row.Get("prefab") ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(row.PrefabName))
                {
                    plan.Warnings.Add("第 " + row.LineNumber + " 行没有单位预制体名，已跳过。");
                    continue;
                }

                int matched;
                if (!byPrefabName.TryGetValue(row.PrefabName, out matched))
                {
                    plan.UnmatchedCount++;
                    plan.Warnings.Add("第 " + row.LineNumber + " 行 " + row.PrefabName + " 在本工程兵种里找不到，已跳过（名字必须和预制体一致）。");
                    continue;
                }

                row.MatchedIndex = matched;
                plan.MatchedCount++;
                plan.Rows.Add(row);
            }

            return plan;
        }

        /// <summary>把计划写进资产与预制体（会先备份）。返回结果说明。</summary>
        public static string Apply(UnitCatalog catalog, UnitCsvPlan plan, bool backupFirst)
        {
            if (catalog == null || plan == null || !plan.HasAnythingToDo)
                return "没有需要修改的行。";

            if (backupFirst)
            {
                var paths = new List<string>();
                for (int i = 0; i < plan.Rows.Count; i++)
                {
                    UnitRecord record = catalog.Records[plan.Rows[i].MatchedIndex];
                    if (record.HasDefinition)
                        paths.Add(record.DefinitionPath);
                    if (record.HasPrefab)
                        paths.Add(record.PrefabPath);
                }
                int n = UnitBackup.BackupAssets(paths, "CSV导入前");
                if (n > 0)
                    Debug.Log("[兵种编辑器] CSV 导入前已备份 " + n + " 个资产到 " + UnitEditorPrefs.BackupFolder);
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("兵种数值表导入");

            var definitionTouched = new HashSet<string>(StringComparer.Ordinal);
            var prefabTouched = new HashSet<string>(StringComparer.Ordinal);
            int applied = 0;
            var warnings = new List<string>();
            var cellSkips = new List<string>();

            for (int i = 0; i < plan.Rows.Count; i++)
            {
                UnitCsvRow row = plan.Rows[i];
                UnitRecord record = catalog.Records[row.MatchedIndex];

                // 1) 训练配置字段
                TroopDefinition definition = record.Definition;
                bool needDefinition = false;
                foreach (KeyValuePair<string, string> pair in row.Values)
                {
                    if (IsDefinitionField(pair.Key))
                    {
                        needDefinition = true;
                        break;
                    }
                }

                if (needDefinition && definition == null)
                {
                    warnings.Add(record.PrefabName + " 没有训练配置，训练相关列已忽略。");
                }
                else if (definition != null && needDefinition)
                {
                    var so = new SerializedObject(definition);
                    foreach (KeyValuePair<string, string> pair in row.Values)
                    {
                        if (!IsDefinitionField(pair.Key))
                            continue;

                        ApplyDefinitionValue(so, pair.Key, pair.Value, row, warnings);
                    }
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(definition);
                    definitionTouched.Add(record.DefinitionPath);
                }

                // 2) 预制体数值
                if (record.HasPrefab && ApplyPrefabValues(catalog, record, row, warnings, cellSkips))
                {
                    prefabTouched.Add(record.PrefabPath);
                    applied++;
                }
            }

            Undo.CollapseUndoOperations(group);
            catalog.InvalidateStats();

            var sb = new StringBuilder();
            sb.Append("导入完成：更新 ").Append(plan.Rows.Count).Append(" 行");
            sb.Append("（训练配置 ").Append(definitionTouched.Count).Append(" 个资产、数值 ").Append(prefabTouched.Count).Append(" 个预制体）。");
            if (plan.UnmatchedCount > 0)
                sb.Append(" 跳过 ").Append(plan.UnmatchedCount).Append(" 行（名字对不上）。");

            // 解析失败的单元格要摆在明面上：它们保持了原值，但策划必须知道"这一格没导进去"，
            // 否则只会看到"导入了但数值没变"。全部明细仍然打到 Console。
            if (cellSkips.Count > 0)
            {
                sb.Append("\n\n有 ").Append(cellSkips.Count).Append(" 格数值格式不合法，已保持原值（没有覆盖）：");
                int shown = Mathf.Min(cellSkips.Count, 8);
                for (int i = 0; i < shown; i++)
                    sb.Append("\n· ").Append(cellSkips[i]);
                if (cellSkips.Count > shown)
                    sb.Append("\n· …其余 ").Append(cellSkips.Count - shown).Append(" 条见 Console。");
            }

            if (warnings.Count > cellSkips.Count)
                sb.Append("\n另有 ").Append(warnings.Count - cellSkips.Count).Append(" 条提醒见 Console。");

            for (int i = 0; i < warnings.Count; i++)
                Debug.LogWarning("[兵种编辑器] " + warnings[i]);

            return sb.ToString();
        }

        private static bool IsDefinitionField(string field)
        {
            for (int i = 0; i < Columns.Count; i++)
            {
                if (Columns[i].Field == field)
                    return Columns[i].IsDefinition;
            }
            return false;
        }

        private static void ApplyDefinitionValue(SerializedObject so, string field, string value, UnitCsvRow row, List<string> warnings)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
                return;

            switch (prop.propertyType)
            {
                case SerializedPropertyType.String:
                    prop.stringValue = value.Trim();
                    break;
                case SerializedPropertyType.Integer:
                    int intValue;
                    if (int.TryParse(value.Trim(), out intValue))
                        prop.intValue = intValue;
                    else
                        warnings.Add("第 " + row.LineNumber + " 行 " + field + " 不是整数：" + value);
                    break;
                case SerializedPropertyType.Float:
                    float floatValue;
                    if (float.TryParse(value.Trim(), out floatValue))
                        prop.floatValue = floatValue;
                    else
                        warnings.Add("第 " + row.LineNumber + " 行 " + field + " 不是数字：" + value);
                    break;
                case SerializedPropertyType.Boolean:
                    // 认不出来的写法不能当成 false 写进去（"是"、"Y"、"1.0" 都会命中这里）
                    string boolText = value.Trim();
                    if (boolText == "1" || boolText.Equals("true", StringComparison.OrdinalIgnoreCase))
                        prop.boolValue = true;
                    else if (boolText == "0" || boolText.Equals("false", StringComparison.OrdinalIgnoreCase))
                        prop.boolValue = false;
                    else
                        warnings.Add("第 " + row.LineNumber + " 行 " + field + " 不是 0/1（或 true/false），已保持原值：" + value);
                    break;
                case SerializedPropertyType.Enum:
                    int enumValue;
                    if (int.TryParse(value.Trim(), out enumValue))
                    {
                        // 写 intValue 而不是 enumValueIndex：后者是"枚举名单里的下标"，
                        // 只有当枚举值从 0 连续排列时才等于数值（TroopTag 目前恰好如此，但别依赖它）。
                        // 顺便挡一下越界值 —— 导出时写的是 (int)枚举，回导时越界会静默写坏字段。
                        int enumMax = prop.enumDisplayNames.Length - 1;
                        if (enumValue < 0 || enumValue > enumMax)
                            warnings.Add("第 " + row.LineNumber + " 行 " + field + " 的枚举值 " + enumValue +
                                         " 超出 0~" + enumMax + "，已保持原值。");
                        else
                            prop.intValue = enumValue;
                    }
                    else
                        warnings.Add("第 " + row.LineNumber + " 行 " + field + " 不是枚举序号：" + value);
                    break;
            }
        }

        private static bool ApplyPrefabValues(UnitCatalog catalog, UnitRecord record, UnitCsvRow row, List<string> warnings)
        {
            return ApplyPrefabValues(catalog, record, row, warnings, null);
        }

        /// <summary>
        /// 把一行的预制体数值写进去。
        /// 两个坑都在这里：
        /// ① 解析失败**不能写硬编码兜底值**（早期版本防御写 0、生命值写 1…，Excel 里一个全角空格
        ///    就能把兵种数值静默清掉）。现在失败时写回"当前值"，等于跳过该格，并记一条提示。
        /// ② atkRange / occupySpace 是 Vector2Int，另一半必须从属性本身读、不能读 session.Stats ——
        ///    Stats 是 Apply 开始时的一次快照且写完不回写，先写 X 再写 Y 会用旧 X 覆盖掉新 X。
        /// </summary>
        private static bool ApplyPrefabValues(UnitCatalog catalog, UnitRecord record, UnitCsvRow row, List<string> warnings, List<string> cellSkips)
        {
            UnitStatSession session = UnitStatSession.Open(record);
            if (!session.Valid)
            {
                warnings.Add(record.PrefabName + "：" + session.Error);
                return false;
            }

            GameObjectProperty prop = session.Prop;
            CharacterHealth health = session.Health;
            UnitAntiOverlap overlap = session.Overlap;

            bool changed = false;
            foreach (KeyValuePair<string, string> pair in row.Values)
            {
                string field = pair.Key;
                string raw = pair.Value;
                if (field == "prefab" || IsDefinitionField(field))
                    continue;

                switch (field)
                {
                    case "maxHp": changed |= session.SetInt(prop, "maxHp", ParseInt(raw, prop.maxHp, row, field, warnings, cellSkips)); break;
                    case "defense": changed |= session.SetInt(prop, "defense", ParseInt(raw, prop.defense, row, field, warnings, cellSkips)); break;
                    case "magicDefense": changed |= session.SetInt(prop, "magicDefense", ParseInt(raw, prop.magicDefense, row, field, warnings, cellSkips)); break;
                    case "atk": changed |= session.SetInt(prop, "atk", ParseInt(raw, prop.atk, row, field, warnings, cellSkips)); break;
                    case "magicAtk": changed |= session.SetInt(prop, "magicAtk", ParseInt(raw, prop.magicAtk, row, field, warnings, cellSkips)); break;
                    case "atkRate": changed |= session.SetFloat(prop, "atkRate", ParseFloat(raw, prop.atkRate, row, field, warnings, cellSkips)); break;
                    case "isRemoteAtk":
                        changed |= session.SetBool(prop, "isRemoteAtk", ParseBool(raw, prop.isRemoteAtk, row, field, warnings, cellSkips));
                        break;
                    case "atkRangeX":
                        {
                            Vector2Int range = prop.atkRange;
                            range.x = ParseInt(raw, range.x, row, field, warnings, cellSkips);
                            changed |= session.SetVector2Int(prop, "atkRange", range);
                            break;
                        }
                    case "atkRangeY":
                        {
                            Vector2Int range = prop.atkRange;
                            range.y = ParseInt(raw, range.y, row, field, warnings, cellSkips);
                            changed |= session.SetVector2Int(prop, "atkRange", range);
                            break;
                        }
                    case "moveSpeed": changed |= session.SetFloat(prop, "moveSpeed", ParseFloat(raw, prop.moveSpeed, row, field, warnings, cellSkips)); break;
                    case "repel": changed |= session.SetFloat(prop, "repel", ParseFloat(raw, prop.repel, row, field, warnings, cellSkips)); break;
                    case "antiRepel": changed |= session.SetFloat(prop, "antiRepel", ParseFloat(raw, prop.antiRepel, row, field, warnings, cellSkips)); break;
                    case "suckBlood": changed |= session.SetFloat(prop, "suckBlood", ParseFloat(raw, prop.suckBlood, row, field, warnings, cellSkips)); break;
                    case "baseMissChance": changed |= session.SetFloat(prop, "baseMissChance", ParseFloat(raw, prop.baseMissChance, row, field, warnings, cellSkips)); break;
                    case "occupySpaceX":
                        {
                            Vector2Int space = prop.occupySpace;
                            space.x = ParseInt(raw, space.x, row, field, warnings, cellSkips);
                            changed |= session.SetVector2Int(prop, "occupySpace", space);
                            break;
                        }
                    case "occupySpaceY":
                        {
                            Vector2Int space = prop.occupySpace;
                            space.y = ParseInt(raw, space.y, row, field, warnings, cellSkips);
                            changed |= session.SetVector2Int(prop, "occupySpace", space);
                            break;
                        }
                    case "atkObj": changed |= session.SetString(prop, "atkObj", raw.Trim()); break;
                    case "spriteKey":
                        if (session.Texture != null) changed |= session.SetString(session.Texture, "spriteKey", raw.Trim());
                        break;
                    case "animatorControllerKeyProp":
                        if (session.AnimatorController != null) changed |= session.SetString(session.AnimatorController, "animatorControllerKey", raw.Trim());
                        break;
                    case "deathAngularSpeed":
                        if (health != null) changed |= session.SetFloat(health, "deathAngularSpeed", ParseFloat(raw, CurrentFloat(health, "deathAngularSpeed", 180f), row, field, warnings, cellSkips));
                        break;
                    case "deathFadeAlpha":
                        if (health != null) changed |= session.SetFloat(health, "deathFadeAlpha", ParseFloat(raw, CurrentFloat(health, "deathFadeAlpha", 0.5f), row, field, warnings, cellSkips));
                        break;
                    case "deathDarkFactor":
                        if (health != null) changed |= session.SetFloat(health, "deathDarkFactor", ParseFloat(raw, CurrentFloat(health, "deathDarkFactor", 0.4f), row, field, warnings, cellSkips));
                        break;
                    case "antiOverlapRadius":
                        if (overlap != null) changed |= session.SetFloat(overlap, "radius", ParseFloat(raw, overlap.radius, row, field, warnings, cellSkips));
                        break;
                    default:
                        if (field.StartsWith("audioKey", StringComparison.Ordinal) && session.Audio != null)
                            changed |= session.SetString(session.Audio, field, raw.Trim());
                        break;
                }
            }

            if (changed)
                session.RefreshStats();

            return changed;
        }

        private static int ParseInt(string raw, int keepValue, UnitCsvRow row, string field, List<string> warnings, List<string> cellSkips)
        {
            int value;
            if (int.TryParse(raw.Trim(), out value))
                return value;

            SkipCell(row, field, raw, "不是整数", warnings, cellSkips);
            return keepValue;
        }

        private static float ParseFloat(string raw, float keepValue, UnitCsvRow row, string field, List<string> warnings, List<string> cellSkips)
        {
            float value;
            if (float.TryParse(raw.Trim(), out value))
                return value;

            SkipCell(row, field, raw, "不是数字", warnings, cellSkips);
            return keepValue;
        }

        private static bool ParseBool(string raw, bool keepValue, UnitCsvRow row, string field, List<string> warnings, List<string> cellSkips)
        {
            string text = raw.Trim();
            if (text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (text == "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;

            SkipCell(row, field, raw, "不是 0/1（或 true/false）", warnings, cellSkips);
            return keepValue;
        }

        /// <summary>记一条"这一格没写、保持原值"的提示，供导入摘要直接在界面上显示。</summary>
        private static void SkipCell(UnitCsvRow row, string field, string raw, string reason, List<string> warnings, List<string> cellSkips)
        {
            string message = "第 " + row.LineNumber + " 行「" + field + "」" + reason + "，已保持原值：" + raw;
            warnings.Add(message);
            if (cellSkips != null)
                cellSkips.Add(message);
        }

        /// <summary>
        /// 读组件上某个序列化字段的当前值。
        /// deathAngularSpeed / deathFadeAlpha / deathDarkFactor 在 CharacterHealth 里是 private [SerializeField]，
        /// 点不出来，只能走 SerializedObject（与 Inspector 同一条路）。
        /// </summary>
        private static float CurrentFloat(UnityEngine.Object target, string field, float fallback)
        {
            if (target == null)
                return fallback;

            SerializedProperty prop = new SerializedObject(target).FindProperty(field);
            return prop != null ? prop.floatValue : fallback;
        }
        #endregion

        #region 解析工具
        private static string ResolveField(string headerCell)
        {
            if (string.IsNullOrEmpty(headerCell))
                return null;

            string cell = headerCell.Trim().TrimStart('\uFEFF');
            int cut = cell.IndexOfAny(new[] { '(', '（' });
            string key = cut > 0 ? cell.Substring(0, cut).Trim() : cell.Trim();

            for (int i = 0; i < Columns.Count; i++)
            {
                if (string.Equals(Columns[i].Field, key, StringComparison.OrdinalIgnoreCase))
                    return Columns[i].Field;
            }

            for (int i = 0; i < Columns.Count; i++)
            {
                if (string.Equals(Columns[i].Title, cell, StringComparison.OrdinalIgnoreCase))
                    return Columns[i].Field;
            }

            return null;
        }

        private static bool IsEmptyRow(List<string> cells)
        {
            for (int i = 0; i < cells.Count; i++)
            {
                if (!string.IsNullOrEmpty(cells[i]) && !string.IsNullOrEmpty(cells[i].Trim()))
                    return false;
            }
            return true;
        }

        private static string Escape(string value)
        {
            if (value == null)
                return string.Empty;

            bool needQuote = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
            return needQuote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        private static string ReadTextAutoEncoding(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                try
                {
                    return Encoding.GetEncoding(936).GetString(bytes);
                }
                catch (Exception)
                {
                    return Encoding.UTF8.GetString(bytes);
                }
            }
        }

        private static List<List<string>> ParseCsv(string text)
        {
            var rows = new List<List<string>>();
            if (string.IsNullOrEmpty(text))
                return rows;

            var row = new List<string>();
            var cell = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        cell.Append(c);
                    }
                    continue;
                }

                switch (c)
                {
                    case '"': inQuotes = true; break;
                    case ',': row.Add(cell.ToString()); cell.Length = 0; break;
                    case '\r': break;
                    case '\n':
                        row.Add(cell.ToString());
                        cell.Length = 0;
                        rows.Add(row);
                        row = new List<string>();
                        break;
                    default: cell.Append(c); break;
                }
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }

            return rows;
        }
        #endregion
    }
}
