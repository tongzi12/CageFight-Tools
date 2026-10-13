using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightBookTool
{
    public enum BookIssueLevel
    {
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// 一条体检结果。带 SuggestedValue 的问题可以一键修复。
    /// </summary>
    public sealed class BookIssue
    {
        public BookIssueLevel Level;
        public string Code = string.Empty;
        public string Title = string.Empty;
        public string Detail = string.Empty;
        public int EntryIndex = -1;
        public string EntryLabel = string.Empty;
        public string Field = string.Empty;
        public string CurrentValue = string.Empty;
        public string SuggestedValue;
        public bool Fixable;

        public bool IsAssetLevel { get { return EntryIndex < 0; } }

        public Color LevelColor
        {
            get
            {
                if (Level == BookIssueLevel.Error) return BookEditorUI.Error;
                if (Level == BookIssueLevel.Warning) return BookEditorUI.Warn;
                return BookEditorUI.Muted;
            }
        }
    }

    /// <summary>
    /// 图鉴数据体检 + 一键修复。
    /// 目的：让不懂技术的策划在改完内容后，一眼看到「哪些条目上线会出问题」。
    /// </summary>
    public static class BookEditorValidator
    {
        public static List<BookIssue> Validate(BookEditorData data)
        {
            var issues = new List<BookIssue>();
            if (data == null || !data.IsLoaded)
                return issues;

            BookResourceIndex index = BookResourceIndex.Instance;
            if (!index.SpriteRegistryFound)
                Add(issues, BookIssueLevel.Error, "ASSET-REG", "找不到 SpriteRegistry", "未在 " + BookResourceIndex.SpriteRegistryPath + " 找到精灵注册表，头像/立绘无法预览与校验。", -1, string.Empty);
            if (!index.PrefabRegistryFound)
                Add(issues, BookIssueLevel.Error, "ASSET-REG", "找不到 PrefabRegistry", "未在 " + BookResourceIndex.PrefabRegistryPath + " 找到预制体注册表，单位键无法校验。", -1, string.Empty);

            List<string> categories = data.Categories;
            List<string> raceOptions = data.RaceOptions;
            List<BookEntry> entries = data.EntriesSnapshot();

            if (categories.Count == 0)
                Add(issues, BookIssueLevel.Error, "ASSET-CAT", "派系页签为空", "BookCatalog.categories 为空，游戏内图鉴不会有任何页签。", -1, string.Empty);
            if (entries.Count == 0)
                Add(issues, BookIssueLevel.Info, "ASSET-ENTRY", "没有任何图鉴条目", "当前图鉴是空的，点左侧「＋ 新增条目」开始。", -1, string.Empty);

            var idOwner = new Dictionary<string, int>(StringComparer.Ordinal);
            var prefabOwner = new Dictionary<string, int>(StringComparer.Ordinal);
            var usedCategories = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < entries.Count; i++)
            {
                BookEntry e = entries[i];
                if (e == null)
                {
                    Add(issues, BookIssueLevel.Error, "E-NULL", "空槽位", "该位置没有数据（通常是删除条目留下的空位）。执行「整理顺序」或保存时会自动清理。", i, "（空）");
                    continue;
                }

                string label = string.IsNullOrEmpty(e.displayName) ? (string.IsNullOrEmpty(e.id) ? "条目 " + (i + 1) : e.id) : e.displayName;

                // --- 唯一 ID ---
                if (string.IsNullOrEmpty(e.id))
                {
                    string suggested = data.MakeUniqueId(string.IsNullOrEmpty(e.prefabKey) ? label : e.prefabKey, i);
                    Add(issues, BookIssueLevel.Error, "E-ID-EMPTY", "缺唯一 ID", "解锁进度靠 ID 记录，缺 ID 会导致该条目永远无法解锁。将生成：" + suggested, i, label, "id", string.Empty, suggested);
                }
                else
                {
                    string trimmed = e.id.Trim();
                    if (trimmed != e.id)
                        Add(issues, BookIssueLevel.Warning, "E-ID-SPACE", "ID 首尾有空白", "ID 前后空白看不见，容易和别的条目混掉，将改为 " + Quote(trimmed) + "。", i, label, "id", e.id, trimmed);
                    else if (HasControlChars(e.id))
                        Add(issues, BookIssueLevel.Warning, "E-ID-CHARS", "ID 含控制字符", "ID 里有换行/制表符等控制字符，会让解锁记录对不上。将改为：" + BookEditorData.SanitizeId(e.id), i, label, "id", e.id, BookEditorData.SanitizeId(e.id));

                    int owner;
                    if (idOwner.TryGetValue(trimmed, out owner))
                    {
                        string suggested = data.MakeUniqueId(trimmed + "_" + (i + 1), i);
                        Add(issues, BookIssueLevel.Error, "E-ID-DUP", "ID 重复", "与第 " + (owner + 1) + " 条重复（ID=" + Quote(trimmed) + "），两人会共用同一份解锁记录。将改为：" + suggested, i, label, "id", e.id, suggested);
                    }
                    else
                    {
                        idOwner[trimmed] = i;
                    }
                }

                // --- 角色名 ---
                if (string.IsNullOrEmpty(e.displayName))
                {
                    string suggested = !string.IsNullOrEmpty(e.prefabKey) ? e.prefabKey : (string.IsNullOrEmpty(e.id) ? "新角色" : e.id);
                    Add(issues, BookIssueLevel.Error, "E-NAME", "缺角色名", "Card 上的名字会显示为空。将用 " + Quote(suggested) + " 填充。", i, label, "displayName", string.Empty, suggested);
                }

                // --- 派系 ---
                if (string.IsNullOrEmpty(e.categoryId))
                {
                    string suggested = categories.Count > 0 ? categories[0] : string.Empty;
                    Add(issues, BookIssueLevel.Error, "E-CAT-EMPTY", "缺所属派系", "没有派系就不会出现在任何页签里（当前算作「未分类」）。将归入 " + Quote(suggested) + "。", i, label, "categoryId", string.Empty, suggested);
                }
                else
                {
                    if (!categories.Contains(e.categoryId))
                        Add(issues, BookIssueLevel.Error, "E-CAT-INVALID", "派系不在页签列表", "派系 " + Quote(e.categoryId) + " 不在 categories 里，游戏内会被归到「未分类」页。将改为 " + Quote(categories.Count > 0 ? categories[0] : string.Empty) + "。", i, label, "categoryId", e.categoryId, categories.Count > 0 ? categories[0] : string.Empty);
                    else
                        usedCategories.Add(e.categoryId);
                }

                // --- 种族 ---
                if (string.IsNullOrEmpty(e.raceId))
                {
                    string suggested = raceOptions.Contains(e.categoryId) ? e.categoryId : (raceOptions.Count > 0 ? raceOptions[0] : string.Empty);
                    Add(issues, BookIssueLevel.Warning, "E-RACE-EMPTY", "缺种族图标", "Card 上不会有任何种族图标亮起。将用 " + Quote(suggested) + "。", i, label, "raceId", string.Empty, suggested);
                }
                else if (!raceOptions.Contains(e.raceId))
                {
                    string suggested = raceOptions.Contains(e.categoryId) ? e.categoryId : (raceOptions.Count > 0 ? raceOptions[0] : string.Empty);
                    Add(issues, BookIssueLevel.Warning, "E-RACE-INVALID", "种族不在选项里", "种族 " + Quote(e.raceId) + " 不在 raceOptions 里，游戏内不会有图标亮起。将改为 " + Quote(suggested) + "。", i, label, "raceId", e.raceId, suggested);
                }

                // --- 头像 ---
                if (string.IsNullOrEmpty(e.avatarSpriteKey))
                {
                    Add(issues, BookIssueLevel.Warning, "E-AVATAR-EMPTY", "缺头像资源键", "图鉴格子会是空白格。请在右侧「头像」里搜索选择，或填好单位键后点「智能匹配」。", i, label, "avatarSpriteKey", string.Empty);
                }
                else if (!index.HasSprite(e.avatarSpriteKey))
                {
                    string suggested = SuggestKey(e.avatarSpriteKey, index.SpriteKeys);
                    Add(issues, BookIssueLevel.Error, "E-AVATAR-INVALID", "头像资源键无效", "SpriteRegistry 里没有键 " + Quote(e.avatarSpriteKey) + "，出包后头像加载失败。" + (suggested != null ? "疑似应为 " + Quote(suggested) + "。" : string.Empty), i, label, "avatarSpriteKey", e.avatarSpriteKey, suggested);
                }

                // --- 立绘 ---
                if (string.IsNullOrEmpty(e.roleSpriteKey))
                {
                    Add(issues, BookIssueLevel.Warning, "E-ROLE-EMPTY", "缺立绘资源键", "Card 立绘会是空白。请在右侧「立绘」里搜索选择，或填好单位键后点「智能匹配」。", i, label, "roleSpriteKey", string.Empty);
                }
                else if (!index.HasSprite(e.roleSpriteKey))
                {
                    string suggested = SuggestKey(e.roleSpriteKey, index.SpriteKeys);
                    Add(issues, BookIssueLevel.Error, "E-ROLE-INVALID", "立绘资源键无效", "SpriteRegistry 里没有键 " + Quote(e.roleSpriteKey) + "，出包后立绘加载失败。" + (suggested != null ? "疑似应为 " + Quote(suggested) + "。" : string.Empty), i, label, "roleSpriteKey", e.roleSpriteKey, suggested);
                }
                else if (e.roleSpriteKey.IndexOf("-move", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    Add(issues, BookIssueLevel.Info, "E-ROLE-FRAME", "立绘可能是移动帧", "键 " + Quote(e.roleSpriteKey) + " 看起来是 move 动作帧，卡面通常用 -attack_0。", i, label, "roleSpriteKey", e.roleSpriteKey);
                }

                if (!string.IsNullOrEmpty(e.avatarSpriteKey) && e.avatarSpriteKey == e.roleSpriteKey)
                    Add(issues, BookIssueLevel.Info, "E-KEY-SAME", "头像与立绘用了同一张图", "两者键相同（" + Quote(e.avatarSpriteKey) + "），确认是有意为之。", i, label);

                // --- 编辑器预览直引 vs 资源键 ---
                // 游戏只认资源键，编辑器优先用直引；两者不一致时，编辑器里看到的图和游戏里不是同一张。
                if (BookEditorData.IsEditorSpriteOutOfSync(e, true, out string avatarKeyName, out string avatarRefName))
                    Add(issues, BookIssueLevel.Warning, "E-AVATAR-SYNC", "编辑器预览图与头像键不一致",
                        "资源键指向「" + avatarKeyName + "」，而资产里存的编辑器预览直引指向「" + avatarRefName +
                        "」。游戏只认资源键，所以编辑器里显示的和游戏里不是同一张图。一键修复会按资源键同步预览直引。",
                        i, label, "avatarSpriteSync", avatarRefName, avatarKeyName);

                if (BookEditorData.IsEditorSpriteOutOfSync(e, false, out string roleKeyName, out string roleRefName))
                    Add(issues, BookIssueLevel.Warning, "E-ROLE-SYNC", "编辑器预览图与立绘键不一致",
                        "资源键指向「" + roleKeyName + "」，而资产里存的编辑器预览直引指向「" + roleRefName +
                        "」。游戏只认资源键，所以编辑器里显示的和游戏里不是同一张图。一键修复会按资源键同步预览直引。",
                        i, label, "roleSpriteSync", roleRefName, roleKeyName);

                // --- 单位预制体键（解锁判定） ---
                if (string.IsNullOrEmpty(e.prefabKey))
                {
                    string exact = SuggestUnitKeyFromId(e.id, index.PrefabKeys);
                    if (exact != null)
                    {
                        Add(issues, BookIssueLevel.Warning, "E-PREFAB-EMPTY", "缺单位预制体键（已找到同名单位）",
                            "没有单位键时这条图鉴永远锁着。ID 与注册表里的单位同名，将自动填为 " + Quote(exact) + "。",
                            i, label, "prefabKey", string.Empty, exact);
                    }
                    else
                    {
                        string fromRole = GuessUnitKeyFromRoleKey(e.roleSpriteKey, index.PrefabKeys);
                        string fuzzy = fromRole ?? SuggestKey(e.id, index.PrefabKeys);
                        Add(issues, BookIssueLevel.Warning, "E-PREFAB-EMPTY", "缺单位预制体键",
                            "没有单位键时，这条图鉴只能一直锁着（关卡内遇见/获得都无法解锁）。" +
                            (fuzzy != null
                                ? (fromRole != null ? "按立绘键推断，疑似 " : "注册表里最相近的单位键是 ") + Quote(fuzzy) + "，请确认后手动选择。"
                                : "请用「搜索」按钮选对应单位。"),
                            i, label, "prefabKey", string.Empty);
                    }
                }
                else if (!index.HasPrefab(e.prefabKey))
                {
                    string suggested = SuggestKey(e.prefabKey, index.PrefabKeys);
                    Add(issues, BookIssueLevel.Error, "E-PREFAB-INVALID", "单位预制体键无效", "PrefabRegistry 里没有键 " + Quote(e.prefabKey) + "，该条目永远不会解锁。" + (suggested != null ? "疑似应为 " + Quote(suggested) + "。" : string.Empty), i, label, "prefabKey", e.prefabKey, suggested);
                }
                else
                {
                    int owner;
                    string prefabKey = e.prefabKey.Trim();
                    if (prefabOwner.TryGetValue(prefabKey, out owner))
                        Add(issues, BookIssueLevel.Warning, "E-PREFAB-DUP", "单位键重复", "与第 " + (owner + 1) + " 条共用单位 " + Quote(prefabKey) + "，解锁时会同时解锁两条（多数情况下只应有一条）。", i, label, "prefabKey", e.prefabKey);
                    else
                        prefabOwner[prefabKey] = i;
                }

                // --- 星级 ---
                if (e.star < 1 || e.star > 7)
                {
                    int clamped = Mathf.Clamp(e.star, 1, 7);
                    Add(issues, BookIssueLevel.Error, "E-STAR", "星级超出 1-7", "当前 star=" + e.star + "，游戏内 Card 不会亮任何一颗星。将改为 " + clamped + "。", i, label, "star", e.star.ToString(), clamped.ToString());
                }

                // --- 数值文本 ---
                if (string.IsNullOrEmpty(e.repelText) && string.IsNullOrEmpty(e.quantityText) && string.IsNullOrEmpty(e.scopeText) &&
                    string.IsNullOrEmpty(e.timeText) && string.IsNullOrEmpty(e.weightText) && string.IsNullOrEmpty(e.coinText))
                {
                    Add(issues, BookIssueLevel.Info, "E-TEXTS", "六项数值全为空", "Card 上六行数值都会是空的。", i, label);
                }

                if (string.IsNullOrEmpty(e.explainText))
                    Add(issues, BookIssueLevel.Info, "E-EXPLAIN", "简介为空", "Card 底部简介会显示为空。", i, label, "explainText", string.Empty);
            }

            // --- 页签空置 ---
            for (int i = 0; i < categories.Count; i++)
            {
                if (!usedCategories.Contains(categories[i]))
                    Add(issues, BookIssueLevel.Info, "E-CAT-UNUSED", "页签下没有条目", "派系 " + Quote(categories[i]) + " 在游戏内会是一页空格子。", -1, categories[i]);
            }

            return issues;
        }

        /// <summary>把可自动修复的问题全部修掉，返回实际修复条数。</summary>
        public static int ApplySafeFixes(BookEditorData data, List<BookIssue> issues)
        {
            if (data == null || !data.IsLoaded || issues == null)
                return 0;

            int fixedCount = 0;
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("图鉴体检一键修复");

            foreach (BookIssue issue in issues)
            {
                if (issue == null || !issue.Fixable || issue.IsAssetLevel)
                    continue;

                BookEntry entry = data.EntryAt(issue.EntryIndex);
                if (entry == null)
                    continue;

                // 预览直引同步：按资源键把 avatarSprite / roleSprite 校正过来
                if (issue.Field == "avatarSpriteSync")
                {
                    if (data.SyncEditorSprite(entry, true))
                        fixedCount++;
                    continue;
                }

                if (issue.Field == "roleSpriteSync")
                {
                    if (data.SyncEditorSprite(entry, false))
                        fixedCount++;
                    continue;
                }

                Undo.RecordObject(data.Catalog, "图鉴体检一键修复");
                switch (issue.Field)
                {
                    case "id":
                        entry.id = issue.SuggestedValue ?? string.Empty;
                        break;
                    case "displayName":
                        entry.displayName = string.IsNullOrEmpty(issue.SuggestedValue)
                            ? (!string.IsNullOrEmpty(entry.prefabKey) ? entry.prefabKey : entry.id)
                            : issue.SuggestedValue;
                        break;
                    case "categoryId":
                        entry.categoryId = issue.SuggestedValue ?? string.Empty;
                        break;
                    case "raceId":
                        entry.raceId = issue.SuggestedValue ?? string.Empty;
                        break;
                    case "avatarSpriteKey":
                        entry.avatarSpriteKey = issue.SuggestedValue ?? entry.avatarSpriteKey;
                        break;
                    case "roleSpriteKey":
                        entry.roleSpriteKey = issue.SuggestedValue ?? entry.roleSpriteKey;
                        break;
                    case "prefabKey":
                        entry.prefabKey = issue.SuggestedValue ?? entry.prefabKey;
                        break;
                    case "star":
                        int star;
                        if (int.TryParse(issue.SuggestedValue, out star))
                            entry.star = Mathf.Clamp(star, 1, 7);
                        break;
                    // 「简介为空」是 Info 级提示，没有可自动补的内容：
                    // 刻意不在这里处理，让它落到 default: continue，避免报出「已修复」却没改数据。
                    default:
                        continue;
                }

                EditorUtility.SetDirty(data.Catalog);
                fixedCount++;
            }

            Undo.CollapseUndoOperations(group);
            if (fixedCount > 0)
                data.MarkDirty();

            return fixedCount;
        }

        /// <summary>
        /// 从立绘资源键推断单位键：立绘键一般长这样 "Damb ass-attack_0"、"BOOM bro_0"、"Crystallized giant beast_12"，
        /// 去掉动作名与帧号就是单位名。只在推断结果确实存在于 PrefabRegistry 时返回。
        /// </summary>
        public static string GuessUnitKeyFromRoleKey(string roleKey, IReadOnlyList<string> prefabKeys)
        {
            if (string.IsNullOrEmpty(roleKey) || prefabKeys == null || prefabKeys.Count == 0)
                return null;

            string core = System.Text.RegularExpressions.Regex.Replace(
                roleKey.Trim(), @"[-_ ](attack|move|idle|free|MOVE)?[-_ ]?\d+$", string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            core = System.Text.RegularExpressions.Regex.Replace(
                core, @"[-_ ](attack|attck|move|idle|free)$", string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            core = core.Trim(' ', '-', '_');
            if (core.Length < 2 || string.Equals(core, roleKey.Trim(), StringComparison.OrdinalIgnoreCase))
                return null;

            for (int i = 0; i < prefabKeys.Count; i++)
            {
                if (string.Equals(prefabKeys[i], core, StringComparison.OrdinalIgnoreCase))
                    return prefabKeys[i];
            }
            return null;
        }

        /// <summary>ID 里只禁止真正会出问题的字符（换行/制表符等控制字符）；空格、中文、短横线都可以安全使用
        ///（ID 在 UserGlobalInfo 里是按 List&lt;string&gt; 逐项存的，不拼分隔符）。</summary>
        private static bool HasControlChars(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsControl(text[i]))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 按条目 ID 推断单位预制体键，只接受高置信匹配：完全相同 / 忽略大小写相同 / 去掉符号后相同
        ///（且唯一）。含空格、下划线差异也算同一类，例如 "Miner head" ↔ "Miner_head"。
        /// 只做这种确定性匹配，避免把图鉴解锁指到错误单位上。
        /// </summary>
        public static string SuggestUnitKeyFromId(string entryId, IReadOnlyList<string> prefabKeys)
        {
            if (string.IsNullOrEmpty(entryId) || prefabKeys == null || prefabKeys.Count == 0)
                return null;

            string id = entryId.Trim();
            string normalized = Normalize(id);
            if (string.IsNullOrEmpty(normalized))
                return null;

            string result = null;
            int hits = 0;

            for (int i = 0; i < prefabKeys.Count; i++)
            {
                string key = prefabKeys[i];
                if (string.IsNullOrEmpty(key))
                    continue;

                bool match = string.Equals(key, id, StringComparison.Ordinal) ||
                             string.Equals(key, id, StringComparison.OrdinalIgnoreCase) ||
                             Normalize(key) == normalized;

                if (!match)
                    continue;

                result = key;
                hits++;
                if (hits > 1)
                    return null;   // 有歧义就不猜
            }

            return hits == 1 ? result : null;
        }

        /// <summary>按键名就近匹配注册表里的真实键（只在能唯一确定时才给建议，避免改错）。</summary>
        private static string SuggestKey(string raw, IReadOnlyList<string> keys)
        {
            if (string.IsNullOrEmpty(raw) || keys == null || keys.Count == 0)
                return null;

            string trimmed = raw.Trim();
            string normalized = Normalize(trimmed);

            string exactIgnoreCase = null;
            int exactCount = 0;
            string uniqueContains = null;
            int containsCount = 0;

            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                if (string.Equals(key, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    exactIgnoreCase = key;
                    exactCount++;
                    continue;
                }

                if (Normalize(key) == normalized)
                {
                    exactIgnoreCase = key;
                    exactCount++;
                    continue;
                }

                if (key.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    trimmed.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    uniqueContains = key;
                    containsCount++;
                }
            }

            if (exactCount == 1)
                return exactIgnoreCase;
            if (exactCount == 0 && containsCount == 1)
                return uniqueContains;
            return null;
        }

        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private static string Quote(string text)
        {
            return "\"" + (text ?? string.Empty) + "\"";
        }

        private static void Add(
            List<BookIssue> issues,
            BookIssueLevel Level,
            string Code,
            string Title,
            string Detail,
            int EntryIndex,
            string EntryLabel,
            string Field = null,
            string CurrentValue = null,
            string SuggestedValue = null)
        {
            var issue = new BookIssue();
            issue.Level = Level;
            issue.Code = Code;
            issue.Title = Title;
            issue.Detail = Detail;
            issue.EntryIndex = EntryIndex;
            issue.EntryLabel = EntryLabel ?? string.Empty;
            issue.Field = Field ?? string.Empty;
            issue.CurrentValue = CurrentValue ?? string.Empty;
            issue.SuggestedValue = SuggestedValue;
            issue.Fixable = SuggestedValue != null && !string.IsNullOrEmpty(Field);
            issues.Add(issue);
        }

        /// <summary>统计各级别数量。</summary>
        public static void Count(List<BookIssue> issues, out int errors, out int warnings, out int infos)
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

                if (issues[i].Level == BookIssueLevel.Error) errors++;
                else if (issues[i].Level == BookIssueLevel.Warning) warnings++;
                else infos++;
            }
        }

        /// <summary>建立「条目下标 -> 该条目全部问题」的索引，供列表里打标记。</summary>
        public static Dictionary<int, List<BookIssue>> GroupByEntry(List<BookIssue> issues)
        {
            var map = new Dictionary<int, List<BookIssue>>();
            if (issues == null)
                return map;

            for (int i = 0; i < issues.Count; i++)
            {
                BookIssue issue = issues[i];
                if (issue == null || issue.IsAssetLevel)
                    continue;

                List<BookIssue> list;
                if (!map.TryGetValue(issue.EntryIndex, out list))
                {
                    list = new List<BookIssue>();
                    map.Add(issue.EntryIndex, list);
                }
                list.Add(issue);
            }
            return map;
        }
    }
}
