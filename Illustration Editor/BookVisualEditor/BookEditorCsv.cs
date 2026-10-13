using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CageFightBookTool
{
    public enum BookCsvImportMode
    {
        /// <summary>按 ID 更新已有条目，表里多出来的行作为新条目追加（推荐）。</summary>
        UpdateAndAppend = 0,
        /// <summary>只更新已有条目，表里的新行直接忽略。</summary>
        UpdateOnly = 1,
        /// <summary>清空现有条目，完全按表格重建（危险，会丢未在表里的条目）。</summary>
        ReplaceAll = 2
    }

    /// <summary>表格里的一行。</summary>
    public sealed class BookCsvRow
    {
        public int LineNumber;
        public string Id = string.Empty;
        public int MatchedIndex = -1;
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Get(string field)
        {
            string value;
            return Values.TryGetValue(field, out value) ? value : null;
        }
    }

    /// <summary>导入计划：先解析、先预览，策划确认后才真正写数据。</summary>
    public sealed class BookCsvPlan
    {
        public string FilePath = string.Empty;
        public BookCsvImportMode Mode = BookCsvImportMode.UpdateAndAppend;
        public readonly List<BookCsvRow> Rows = new List<BookCsvRow>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> MissingColumns = new List<string>();
        public int UpdateCount;
        public int AppendCount;
        public int IgnoredCount;
        public int UnchangedCount;

        public bool HasAnythingToDo { get { return UpdateCount > 0 || AppendCount > 0; } }
    }

    /// <summary>
    /// 图鉴 CSV 导入 / 导出。
    /// 策划可以用 Excel / WPS 批量填表，再回导到 BookCatalog 资产。
    /// 导出为 UTF-8(BOM)，Excel 直接打开中文不乱码；导入兼容 UTF-8 / GBK。
    /// </summary>
    public static class BookEditorCsv
    {
        /// <summary>列定义：字段名 + 中文表头。顺序即导出顺序。</summary>
        public static readonly string[] Columns =
        {
            "id", "displayName", "categoryId", "raceId", "star", "prefabKey",
            "avatarSpriteKey", "roleSpriteKey",
            "repelText", "quantityText", "scopeText", "timeText", "weightText", "coinText",
            "explainText"
        };

        private static readonly Dictionary<string, string> ChineseNames = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "id", "唯一ID（改动会丢解锁进度，慎改）" },
            { "displayName", "角色名" },
            { "categoryId", "所属派系" },
            { "raceId", "种族图标" },
            { "star", "星级1-7" },
            { "prefabKey", "单位预制体键" },
            { "avatarSpriteKey", "头像资源键" },
            { "roleSpriteKey", "立绘资源键" },
            { "repelText", "Repel文本" },
            { "quantityText", "Quiantity文本" },
            { "scopeText", "Scope文本" },
            { "timeText", "time文本" },
            { "weightText", "weight文本" },
            { "coinText", "coin文本" },
            { "explainText", "简介" }
        };

        public static string ChineseName(string field)
        {
            string name;
            return ChineseNames.TryGetValue(field, out name) ? name : field;
        }

        #region 导出
        /// <summary>导出全部条目到 CSV。</summary>
        public static bool Export(BookEditorData data, string filePath, out string message)
        {
            message = string.Empty;
            if (data == null || !data.IsLoaded)
            {
                message = "没有打开图鉴资产。";
                return false;
            }

            List<BookEntry> entries = data.EntriesSnapshot();
            var sb = new StringBuilder();

            for (int i = 0; i < Columns.Length; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(Escape(Columns[i] + "（" + ChineseName(Columns[i]) + "）"));
            }
            sb.Append("\r\n");

            for (int i = 0; i < entries.Count; i++)
            {
                BookEntry e = entries[i];
                if (e == null)
                    continue;

                sb.Append(Escape(e.id)).Append(',');
                sb.Append(Escape(e.displayName)).Append(',');
                sb.Append(Escape(e.categoryId)).Append(',');
                sb.Append(Escape(e.raceId)).Append(',');
                sb.Append(e.star.ToString()).Append(',');
                sb.Append(Escape(e.prefabKey)).Append(',');
                sb.Append(Escape(e.avatarSpriteKey)).Append(',');
                sb.Append(Escape(e.roleSpriteKey)).Append(',');
                sb.Append(Escape(e.repelText)).Append(',');
                sb.Append(Escape(e.quantityText)).Append(',');
                sb.Append(Escape(e.scopeText)).Append(',');
                sb.Append(Escape(e.timeText)).Append(',');
                sb.Append(Escape(e.weightText)).Append(',');
                sb.Append(Escape(e.coinText)).Append(',');
                sb.Append(Escape(e.explainText));
                sb.Append("\r\n");
            }

            try
            {
                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
                message = "已导出 " + entries.Count + " 条到：\n" + filePath;
                return true;
            }
            catch (Exception ex)
            {
                message = "导出失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>导出空白模板（表头 + 一条示例），给策划照着填。</summary>
        public static bool ExportTemplate(string filePath, List<string> categories, List<string> raceOptions, out string message)
        {
            message = string.Empty;
            string category = categories != null && categories.Count > 0 ? categories[0] : "mouse";
            string race = raceOptions != null && raceOptions.Count > 0 ? raceOptions[0] : "mouse";

            var sb = new StringBuilder();
            for (int i = 0; i < Columns.Length; i++)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(Escape(Columns[i] + "（" + ChineseName(Columns[i]) + "）"));
            }
            sb.Append("\r\n");
            sb.Append(Escape("example_unit")).Append(',');
            sb.Append(Escape("示例角色")).Append(',');
            sb.Append(Escape(category)).Append(',');
            sb.Append(Escape(race)).Append(',');
            sb.Append("1,");
            sb.Append(Escape("Miner rat")).Append(',');
            sb.Append(Escape("Mouse Troop UI_0")).Append(',');
            sb.Append(Escape("Miner rat-attack_0")).Append(',');
            sb.Append("1,3,2,9,1,30,");
            sb.Append(Escape("攻击会产生1金币。"));
            sb.Append("\r\n");

            try
            {
                File.WriteAllText(filePath, sb.ToString(), new UTF8Encoding(true));
                message = "模板已导出：\n" + filePath;
                return true;
            }
            catch (Exception ex)
            {
                message = "导出模板失败：" + ex.Message;
                return false;
            }
        }
        #endregion

        #region 解析
        /// <summary>读取并解析 CSV，生成导入计划（不写数据）。</summary>
        public static BookCsvPlan BuildPlan(BookEditorData data, string filePath, BookCsvImportMode mode, bool emptyCellMeansClear)
        {
            var plan = new BookCsvPlan();
            plan.FilePath = filePath;
            plan.Mode = mode;

            string text;
            try
            {
                text = ReadTextAutoEncoding(filePath);
            }
            catch (Exception ex)
            {
                plan.Warnings.Add("读取文件失败：" + ex.Message);
                return plan;
            }

            List<List<string>> table = ParseCsv(text);
            if (table.Count == 0)
            {
                plan.Warnings.Add("文件是空的。");
                return plan;
            }

            // 表头 -> 字段名
            var columnFields = new List<string>();
            List<string> header = table[0];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < header.Count; i++)
            {
                string field = ResolveField(header[i]);
                columnFields.Add(field);
                if (field != null)
                    seen.Add(field);
            }

            if (columnFields.Count == 0 || !seen.Contains("id"))
                plan.Warnings.Add("表头里没有找到 id 列（第一行必须是导出的表头或包含 id）。");

            for (int i = 0; i < Columns.Length; i++)
            {
                if (!seen.Contains(Columns[i]))
                    plan.MissingColumns.Add(Columns[i]);
            }

            // 现有 ID -> 下标
            var idToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            List<BookEntry> entries = data.EntriesSnapshot();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null || string.IsNullOrEmpty(entries[i].id))
                    continue;
                if (!idToIndex.ContainsKey(entries[i].id))
                    idToIndex.Add(entries[i].id, i);
            }

            var usedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int r = 1; r < table.Count; r++)
            {
                List<string> cells = table[r];
                if (IsEmptyRow(cells))
                    continue;

                var row = new BookCsvRow();
                row.LineNumber = r + 1;
                for (int c = 0; c < cells.Count && c < columnFields.Count; c++)
                {
                    string field = columnFields[c];
                    if (field == null)
                        continue;

                    string value = cells[c];
                    if (value == null)
                        continue;

                    if (!emptyCellMeansClear && field != "id" && value.Length == 0)
                        continue;   // 空单元格保留原值

                    row.Values[field] = value;
                }

                row.Id = row.Get("id");
                if (string.IsNullOrEmpty(row.Id))
                {
                    plan.Warnings.Add("第 " + row.LineNumber + " 行没有 id，已跳过。");
                    continue;
                }

                row.Id = row.Id.Trim();
                if (!usedIds.Add(row.Id))
                {
                    plan.Warnings.Add("第 " + row.LineNumber + " 行的 id \"" + row.Id + "\" 在表里重复，只保留第一行。");
                    continue;
                }

                int matched;
                if (idToIndex.TryGetValue(row.Id, out matched))
                {
                    row.MatchedIndex = matched;
                    plan.UpdateCount++;
                }
                else if (mode == BookCsvImportMode.UpdateOnly)
                {
                    plan.IgnoredCount++;
                    continue;
                }
                else
                {
                    row.MatchedIndex = -1;
                    plan.AppendCount++;
                }

                plan.Rows.Add(row);
            }

            return plan;
        }

        /// <summary>把导入计划写进资产（会先自动备份）。返回结果说明。</summary>
        public static string Apply(BookEditorData data, BookCsvPlan plan, bool backupFirst)
        {
            if (data == null || !data.IsLoaded || plan == null)
                return "没有可应用的计划。";

            if (!plan.HasAnythingToDo)
                return "没有需要修改的行。";

            if (backupFirst)
            {
                string backup = data.BackupNow("CSV导入前");
                if (!string.IsNullOrEmpty(backup))
                    Debug.Log("[图鉴编辑器] 已备份：" + backup);
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("图鉴 CSV 导入");

            int updated = 0;
            int appended = 0;

            if (plan.Mode == BookCsvImportMode.ReplaceAll)
            {
                var all = new List<int>();
                for (int i = 0; i < data.EntryCount; i++)
                    all.Add(i);
                data.DeleteEntries(all);
            }

            for (int i = 0; i < plan.Rows.Count; i++)
            {
                BookCsvRow row = plan.Rows[i];
                int index = row.MatchedIndex;

                if (index >= 0 && plan.Mode == BookCsvImportMode.ReplaceAll)
                    index = -1;   // 已清空，全部按新增处理。

                if (index < 0)
                {
                    index = AppendFromRow(data, row);
                    if (index >= 0)
                        appended++;
                }
                else
                {
                    BookEntry entry = data.EntryAt(index);
                    if (entry == null)
                        continue;

                    Undo.RecordObject(data.Catalog, "图鉴 CSV 导入");
                    ApplyValues(entry, row, plan.Warnings);
                    EditorUtility.SetDirty(data.Catalog);
                    updated++;
                }
            }

            // CSV 改了资源键之后，编辑器预览直引也要跟着同步，否则编辑器显示和游戏里不是同一张图
            data.SyncAllEditorSprites();
            data.MarkDirty();
            Undo.CollapseUndoOperations(group);

            return "CSV 导入完成：更新 " + updated + " 条，新增 " + appended + " 条。" +
                   (plan.IgnoredCount > 0 ? "（忽略表中新行 " + plan.IgnoredCount + " 条）" : string.Empty);
        }
        #endregion

        #region 内部实现
        private static int AppendFromRow(BookEditorData data, BookCsvRow row)
        {
            string category = row.Get("categoryId");
            int index = data.AddEntry(string.IsNullOrEmpty(category) ? null : category.Trim());
            if (index < 0)
                return -1;

            BookEntry entry = data.EntryAt(index);
            if (entry == null)
                return -1;

            Undo.RecordObject(data.Catalog, "图鉴 CSV 导入");
            ApplyValues(entry, row, null);
            EditorUtility.SetDirty(data.Catalog);
            return index;
        }

        private static void ApplyValues(BookEntry entry, BookCsvRow row, List<string> warnings)
        {
            foreach (KeyValuePair<string, string> pair in row.Values)
            {
                string field = pair.Key;
                string value = pair.Value;
                switch (field)
                {
                    case "id":
                        entry.id = value.Trim();
                        break;
                    case "displayName":
                        entry.displayName = value;
                        break;
                    case "categoryId":
                        entry.categoryId = value.Trim();
                        break;
                    case "raceId":
                        entry.raceId = value.Trim();
                        break;
                    case "star":
                        int star;
                        if (int.TryParse(value.Trim(), out star))
                            entry.star = Mathf.Clamp(star, 1, 7);
                        else if (warnings != null && !string.IsNullOrEmpty(value.Trim()))
                            warnings.Add("第 " + row.LineNumber + " 行 star 无法解析：" + value);
                        break;
                    case "prefabKey":
                        entry.prefabKey = value.Trim();
                        break;
                    case "avatarSpriteKey":
                        entry.avatarSpriteKey = value.Trim();
                        break;
                    case "roleSpriteKey":
                        entry.roleSpriteKey = value.Trim();
                        break;
                    case "repelText":
                        entry.repelText = value;
                        break;
                    case "quantityText":
                        entry.quantityText = value;
                        break;
                    case "scopeText":
                        entry.scopeText = value;
                        break;
                    case "timeText":
                        entry.timeText = value;
                        break;
                    case "weightText":
                        entry.weightText = value;
                        break;
                    case "coinText":
                        entry.coinText = value;
                        break;
                    case "explainText":
                        entry.explainText = value;
                        break;
                }
            }
        }

        /// <summary>表头单元格 -> 字段名。支持 "id（唯一ID）"、"id(唯一ID)"、"唯一ID"、纯中文字段名。</summary>
        private static string ResolveField(string headerCell)
        {
            if (string.IsNullOrEmpty(headerCell))
                return null;

            string cell = headerCell.Trim().TrimStart('\uFEFF');
            int cut = cell.IndexOfAny(new[] { '(', '（' });
            string key = cut > 0 ? cell.Substring(0, cut).Trim() : cell.Trim();

            for (int i = 0; i < Columns.Length; i++)
            {
                if (string.Equals(Columns[i], key, StringComparison.OrdinalIgnoreCase))
                    return Columns[i];
            }

            // 允许直接用中文表头
            foreach (KeyValuePair<string, string> pair in ChineseNames)
            {
                if (string.Equals(pair.Value, cell, StringComparison.OrdinalIgnoreCase))
                    return pair.Key;
            }

            // 允许带括号的中文表头：把括号去掉再比一次
            int cut2 = cell.IndexOfAny(new[] { '(', '（' });
            if (cut2 > 0)
            {
                string bare = cell.Substring(0, cut2).Trim();
                foreach (KeyValuePair<string, string> pair in ChineseNames)
                {
                    if (string.Equals(pair.Value, bare, StringComparison.OrdinalIgnoreCase))
                        return pair.Key;
                }
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

            bool needQuote = value.IndexOf(',') >= 0 || value.IndexOf('"') >= 0 ||
                             value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0;
            if (!needQuote)
                return value;

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        /// <summary>读取文本：优先按 BOM/UTF-8，失败回退 GBK（Excel 另存为 CSV 的常见编码）。</summary>
        private static string ReadTextAutoEncoding(string filePath)
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            try
            {
                var strict = new UTF8Encoding(false, true);
                return strict.GetString(bytes);
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

        /// <summary>RFC4180 风格 CSV 解析（支持引号、逗号、换行、双引号转义）。</summary>
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
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        row.Add(cell.ToString());
                        cell.Length = 0;
                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add(cell.ToString());
                        cell.Length = 0;
                        rows.Add(row);
                        row = new List<string>();
                        break;
                    default:
                        cell.Append(c);
                        break;
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
