using System;
using System.Collections.Generic;

namespace CageFightDialogueTool
{
    /// <summary>文案问题的严重程度（与 Unity 的 MessageType 无关，这里是为了能脱离 Unity 单测）。</summary>
    public enum DialogueLevel
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>一条文案问题。</summary>
    public struct DialogueIssue
    {
        /// <summary>第几条（0 基）。</summary>
        public int Index;

        public DialogueLevel Level;
        public string Code;
        public string Message;
    }

    /// <summary>一组台词的统计（用于界面上"共 N 条 / 最长 M 字"这类一眼信息）。</summary>
    public struct DialogueSummary
    {
        public int Count;          // 条数
        public int BlankCount;     // 空 / 纯空白的条数
        public int TotalChars;     // 全部文本字符数（不含首尾空白）
        public int LongestChars;   // 最长一条的字符数
        public int LongestIndex;   // 最长那条的下标（没有文本时 -1）
    }

    /// <summary>
    /// 对话编辑器的**纯逻辑内核**：只吃字符串列表，不碰 Unity、不碰资产，
    /// 所以能脱离编辑器单测（见 README 的验证记录）。
    ///
    /// 这里只放"与游戏运行时无关、但很容易写错"的判断：空文案、首尾空白、
    /// 重复文案、占位文案、移位的边界、长度统计。
    /// 关键 Key 能不能解析必须交给 Unity 侧（要看注册表），不在这里假装能判。
    /// </summary>
    public static class DialogueCore
    {
        /// <summary>超过这个字数只给"提示"，不是错误 —— 气泡宽度并没有在代码里限制，得靠眼睛看。</summary>
        public const int LongLineHint = 40;

        /// <summary>空或纯空白（半角/全角空格、制表符、换行都算空白）。</summary>
        public static bool IsBlank(string text)
        {
            return string.IsNullOrEmpty(text) || text.Trim().Length == 0;
        }

        /// <summary>首尾有多余空白（半角/全角都算）—— 运行时会把整串原样画出来，所以值得提醒。</summary>
        public static bool HasEdgeWhitespace(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return text.Length != text.Trim().Length;
        }

        /// <summary>去掉首尾空白；null 当空串。</summary>
        public static string Clean(string text)
        {
            return text == null ? string.Empty : text.Trim();
        }

        /// <summary>
        /// 压成一行给列表用：换行/制表符换成空格，压掉连续空格，超长截断加省略号。
        /// 列表里显示多行文本会把卡片撑乱，所以列表只显示这个"一行摘要"。
        /// </summary>
        public static string OneLine(string text, int max = 34)
        {
            if (IsBlank(text))
                return "(空文案)";

            string flat = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            while (flat.IndexOf("  ", StringComparison.Ordinal) >= 0)
                flat = flat.Replace("  ", " ");
            flat = flat.Trim();

            if (max > 0 && flat.Length > max)
                flat = flat.Substring(0, max) + "…";

            return flat;
        }

        /// <summary>
        /// 把 from 位置的元素移到 to 位置（用于"上移 / 下移"）。
        /// 越界或不合法返回 false，**不做任何修改** —— 界面上的按钮要据此决定是否置灰。
        /// </summary>
        public static bool TryMove<T>(IList<T> list, int from, int to)
        {
            if (list == null || from < 0 || from >= list.Count || to < 0 || to >= list.Count || from == to)
                return false;

            T item = list[from];
            list.RemoveAt(from);
            list.Insert(to, item);
            return true;
        }

        /// <summary>上移一位；已经在第一条时返回 false。</summary>
        public static bool MoveUp<T>(IList<T> list, int index)
        {
            return TryMove(list, index, index - 1);
        }

        /// <summary>下移一位；已经在最后一条时返回 false。</summary>
        public static bool MoveDown<T>(IList<T> list, int index)
        {
            return TryMove(list, index, index + 1);
        }

        /// <summary>
        /// 在 index 之前是否已经出现过**完全相同**的文案；有就返回那一条的下标，没有返回 -1。
        /// 只用来给"这两句一模一样"的提示，不阻止保存（台词重复是合法的，只是常常是复制粘贴忘了改）。
        /// </summary>
        public static int IndexOfSameBefore(IList<string> texts, int index)
        {
            if (texts == null || index <= 0 || index >= texts.Count)
                return -1;

            string target = texts[index];
            if (IsBlank(target))
                return -1;

            for (int i = 0; i < index; i++)
            {
                if (string.Equals(texts[i], target, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// 看起来像占位文案：待填 / TODO / test / 纯数字 / 纯符号。
        /// 这类东西最容易跟着版本发出去，所以单独挑出来提醒（只是提示，不拦人）。
        /// </summary>
        public static bool LooksLikePlaceholder(string text)
        {
            if (IsBlank(text))
                return false;

            string t = text.Trim().ToLowerInvariant();

            string[] markers = { "todo", "待填", "待补", "占位", "placeholder", "test", "测试", "xxxx", "xxx", "??" };
            for (int i = 0; i < markers.Length; i++)
            {
                if (t.IndexOf(markers[i], StringComparison.Ordinal) >= 0)
                    return true;
            }

            bool hasDigit = false;
            bool hasLetterOrHan = false;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c >= '0' && c <= '9')
                {
                    hasDigit = true;
                    continue;
                }

                // 中日韩统一表意文字：算"真的写了字"。
                if (c >= 0x4E00 && c <= 0x9FFF)
                {
                    hasLetterOrHan = true;
                    continue;
                }

                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
                    hasLetterOrHan = true;
            }

            return hasDigit && !hasLetterOrHan;   // 整条只有数字和符号，例如 "12312399"
        }

        /// <summary>统计一组台词：条数、空条数、总字数、最长那条。</summary>
        public static DialogueSummary Summarize(IList<string> texts)
        {
            var summary = new DialogueSummary();
            summary.LongestIndex = -1;

            if (texts == null)
                return summary;

            summary.Count = texts.Count;
            for (int i = 0; i < texts.Count; i++)
            {
                string cleaned = Clean(texts[i]);
                if (cleaned.Length == 0)
                {
                    summary.BlankCount++;
                    continue;
                }

                summary.TotalChars += cleaned.Length;
                if (cleaned.Length > summary.LongestChars)
                {
                    summary.LongestChars = cleaned.Length;
                    summary.LongestIndex = i;
                }
            }

            return summary;
        }

        /// <summary>
        /// 一组台词的文案体检。只判"跟文案本身有关"的事，Key 的问题交给 Unity 侧。
        /// </summary>
        public static List<DialogueIssue> CheckTexts(IList<string> texts, bool allowDuplicateHint = true)
        {
            var issues = new List<DialogueIssue>();
            if (texts == null)
                return issues;

            for (int i = 0; i < texts.Count; i++)
            {
                string raw = texts[i];

                if (IsBlank(raw))
                {
                    issues.Add(New(i, DialogueLevel.Warning, "text.blank",
                        "第 " + (i + 1) + " 条没有文案（运行时会跳过/弹出空框，看这一处是谁在用）"));
                    continue;
                }

                if (HasEdgeWhitespace(raw))
                {
                    issues.Add(New(i, DialogueLevel.Info, "text.edgeSpace",
                        "第 " + (i + 1) + " 条首尾有多余空白，画到气泡里会多出一截空"));
                }

                int length = Clean(raw).Length;
                if (length > LongLineHint)
                {
                    issues.Add(New(i, DialogueLevel.Info, "text.long",
                        "第 " + (i + 1) + " 条有 " + length + " 字，偏长（气泡宽度没在代码里限制，注意目测会不会溢出）"));
                }

                if (LooksLikePlaceholder(raw))
                {
                    issues.Add(New(i, DialogueLevel.Warning, "text.placeholder",
                        "第 " + (i + 1) + " 条看着像占位文案：「" + OneLine(raw, 20) + "」"));
                }

                if (allowDuplicateHint)
                {
                    int same = IndexOfSameBefore(texts, i);
                    if (same >= 0)
                    {
                        issues.Add(New(i, DialogueLevel.Info, "text.duplicate",
                            "第 " + (i + 1) + " 条与第 " + (same + 1) + " 条一模一样（复制来的？）"));
                    }
                }
            }

            return issues;
        }

        /// <summary>把问题列表压成一句状态栏文字（按最严重的一档说）。</summary>
        public static string Describe(IList<DialogueIssue> issues)
        {
            if (issues == null || issues.Count == 0)
                return "文案没问题。";

            int errors = 0, warnings = 0, infos = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].Level == DialogueLevel.Error) errors++;
                else if (issues[i].Level == DialogueLevel.Warning) warnings++;
                else infos++;
            }

            return "文案体检：" + errors + " 错 / " + warnings + " 警 / " + infos + " 提示";
        }

        private static DialogueIssue New(int index, DialogueLevel level, string code, string message)
        {
            return new DialogueIssue { Index = index, Level = level, Code = code, Message = message };
        }
    }
}
