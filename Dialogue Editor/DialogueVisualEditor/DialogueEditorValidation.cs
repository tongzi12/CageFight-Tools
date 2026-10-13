using System;
using System.Collections.Generic;

namespace CageFightDialogueTool
{
    /// <summary>一条体检结果。</summary>
    public struct DialogueProblem
    {
        public DialogueLevel Level;
        public string Code;
        public string Where;      // 哪一关 / 哪个资产 / 第几条
        public string Message;

        public bool CanFix;       // 只有"确定性安全"的问题才给一键修复
    }

    /// <summary>
    /// 对话体检。分两套（对应界面上两页），每条规则都对着运行时真实行为写：
    ///
    /// **局外·选关界面**（`GuideDialoguePlayer`，Assets/Runtime/UISystem/GameStart/GuideDialoguePlayer.cs）
    ///   · 文本为空 → 运行时 `continue` 直接跳过这一条（:99），玩家看不到任何东西 → 警告（不是错误）
    ///   · 头像 Key 为空 → 本条不显示头像（:119-122）→ 提示
    ///   · 头像 Key 非空但注册表里没有 → 取不到 Sprite，头像不显示，编辑器会打错误日志 → 错误
    ///   · 音频 Key 为空 → 本条静音（:292）→ 提示
    ///   · 音频 Key 非空但注册表里没有 → 运行时会 LogError"请确认该 Key 已登记到 AudioRegistry"（:311-314）→ 错误
    ///
    /// **局内·对战文本**（`DialogueManager`，Assets/Runtime/TextSystem/DialogueManager.cs）
    ///   · 立绘 Key 为空 → **不查资源**，Image 用 Unity UI 内建白色纹理，对话照常播（:346-348）→ 提示
    ///   · 立绘 Key 非空但「当前关卡预载缓存」里没有 → **整条对话被拒绝**，只会打 OnDialogueFailed（:378-386）→ 错误
    ///     这一条尤其要提醒：立绘 Key 必须出现在相关关卡的 `StageConfig.sprites` 里，光在注册表里有还不够
    ///   · 文本为空 → 会弹出一个空对话框 → 警告
    /// </summary>
    public static class DialogueValidator
    {
        #region 局外 · 选关界面
        /// <summary>一关的导游台词体检。</summary>
        public static List<DialogueProblem> CheckGuide(StageDialogueInfo stage)
        {
            var problems = new List<DialogueProblem>();
            if (stage == null || stage.Config == null)
                return problems;

            List<GuideDialogueEntry> lines = stage.Lines;
            string where = "Stage" + stage.StageId;

            if (lines == null || lines.Count == 0)
            {
                problems.Add(New(DialogueLevel.Info, "guide.empty", where,
                    "这一关没有导游台词：选关界面点它不会弹任何东西（不报错，就是没词）"));
                return problems;
            }

            // 文案本身的问题交给纯算术内核（可脱离 Unity 单测）
            var texts = new List<string>();
            for (int i = 0; i < lines.Count; i++)
                texts.Add(lines[i] != null ? lines[i].text : null);

            List<DialogueIssue> textIssues = DialogueCore.CheckTexts(texts);
            for (int i = 0; i < textIssues.Count; i++)
            {
                DialogueIssue issue = textIssues[i];
                problems.Add(New(issue.Level, issue.Code, where + " 第 " + (issue.Index + 1) + " 条", issue.Message));
            }

            for (int i = 0; i < lines.Count; i++)
            {
                GuideDialogueEntry entry = lines[i];
                if (entry == null)
                {
                    problems.Add(New(DialogueLevel.Warning, "guide.null", where + " 第 " + (i + 1) + " 条",
                        "这一条是空引用（列表里有个洞）；运行时会跳过它，建议删掉"));
                    continue;
                }

                string avatar = DialogueCore.Clean(entry.avatarSpriteKey);
                if (avatar.Length == 0)
                {
                    problems.Add(New(DialogueLevel.Info, "guide.noAvatar", where + " 第 " + (i + 1) + " 条",
                        "没填头像 Key：这条不显示导游头像（只弹气泡文字）"));
                }
                else if (!DialogueTool.HasSpriteKey(avatar))
                {
                    problems.Add(New(DialogueLevel.Error, "guide.badAvatar", where + " 第 " + (i + 1) + " 条",
                        "头像 Key「" + avatar + "」不在 SpriteRegistry 里：运行时会取不到图，头像不显示并打错误日志"));
                }

                string audio = DialogueCore.Clean(entry.audioKey);
                if (audio.Length == 0)
                {
                    problems.Add(New(DialogueLevel.Info, "guide.noAudio", where + " 第 " + (i + 1) + " 条",
                        "没填音频 Key：这条静音"));
                }
                else if (!DialogueTool.HasAudioKey(audio))
                {
                    problems.Add(New(DialogueLevel.Error, "guide.badAudio", where + " 第 " + (i + 1) + " 条",
                        "音频 Key「" + audio + "」不在 AudioRegistry 里：运行时会打错误日志提示该 Key 未登记到 AudioRegistry"));
                }
            }

            return problems;
        }

        /// <summary>全部关卡的导游台词体检（外加"还有多少关没词"的进度提示）。</summary>
        public static List<DialogueProblem> CheckGuideAll()
        {
            var problems = new List<DialogueProblem>();
            IReadOnlyList<StageDialogueInfo> stages = DialogueTool.Stages;

            int withLines = 0;
            int blankStages = 0;
            for (int i = 0; i < stages.Count; i++)
            {
                List<DialogueProblem> one = CheckGuide(stages[i]);
                for (int k = 0; k < one.Count; k++)
                {
                    // 单关规则里的「这关没台词」在这里汇总成一条，不然 14 关会刷 14 行
                    if (one[k].Code == "guide.empty")
                        continue;
                    problems.Add(one[k]);
                }

                if (stages[i].LineCount > 0)
                    withLines++;
                else
                    blankStages++;
            }

            if (blankStages > 0)
            {
                problems.Add(New(DialogueLevel.Info, "guide.coverage", "选关界面",
                    "共 " + stages.Count + " 关，其中 " + blankStages + " 关没有导游台词（有台词的 " + withLines + " 关）"));
            }

            if (!DialogueTool.SpriteRegistryFound)
                problems.Add(New(DialogueLevel.Error, "registry.sprite", "工程",
                    "读不到 SpriteRegistry.asset：所有头像 Key 都没法校验（资产可能被改名或挪走了）"));
            if (!DialogueTool.AudioRegistryFound)
                problems.Add(New(DialogueLevel.Error, "registry.audio", "工程",
                    "读不到 AudioRegistry.asset：所有音频 Key 都没法校验"));

            return problems;
        }
        #endregion

        #region 局内 · 对战文本
        /// <summary>一条局内对话配置的体检。</summary>
        public static List<DialogueProblem> CheckConfig(DialogueConfigSO config, string path)
        {
            var problems = new List<DialogueProblem>();
            if (config == null)
                return problems;

            string where = string.IsNullOrEmpty(path) ? config.name : System.IO.Path.GetFileNameWithoutExtension(path);

            var texts = new List<string> { config.Text };
            List<DialogueIssue> textIssues = DialogueCore.CheckTexts(texts, false);
            for (int i = 0; i < textIssues.Count; i++)
            {
                DialogueProblem problem = New(textIssues[i].Level, textIssues[i].Code, where, textIssues[i].Message);
                // "运行时被跳过"是局外的行为；局内是弹空框，改一下措辞
                if (problem.Code == "text.blank")
                    problem.Message = "这条没有文案：播到它会弹出一个空对话框";
                problems.Add(problem);
            }

            string key = DialogueCore.Clean(config.PortraitSpriteKey);
            if (key.Length == 0)
            {
                problems.Add(New(DialogueLevel.Info, "match.noPortrait", where,
                    "没填立绘 Key：运行时不查资源，立绘位置用 Unity UI 内建白图（对话能正常播）"));
            }
            else if (!DialogueTool.HasSpriteKey(key))
            {
                problems.Add(New(DialogueLevel.Error, "match.badPortrait", where,
                    "立绘 Key「" + key + "」不在 SpriteRegistry 里：解析不到，对话会失败"));
            }
            else if (!DialogueTool.IsPreloadedByAnyStage(key))
            {
                problems.Add(New(DialogueLevel.Error, "match.notPreloaded", where,
                    "立绘 Key「" + key + "」没有任何一关的预载清单（StageConfig.sprites）里有它：" +
                    "进关后照样取不到图，DialogueManager 会**直接拒绝整条对话**（只打 OnDialogueFailed）"));
            }

            return problems;
        }

        /// <summary>一个对话系列的体检（含空槽位、重复引用、空系列）。</summary>
        public static List<DialogueProblem> CheckSeries(DialogueSeriesSO series, string path)
        {
            var problems = new List<DialogueProblem>();
            if (series == null)
                return problems;

            string where = string.IsNullOrEmpty(path) ? series.name : System.IO.Path.GetFileNameWithoutExtension(path);
            List<DialogueConfigSO> lines = DialogueTool.ReadSeriesLines(series);

            if (lines.Count == 0)
            {
                problems.Add(New(DialogueLevel.Warning, "series.empty", where,
                    "这个系列一条都不引用：播它不会有任何内容"));
                return problems;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i] == null)
                {
                    problems.Add(New(DialogueLevel.Warning, "series.hole", where + " 第 " + (i + 1) + " 条",
                        "这一格是空的：运行时 TryGetNextValid 会跳过它，但列表里留个洞容易看错位置"));
                }
            }

            // 同一个配置被引用多次：改一处两处都会变（现有 Dialogue1/Series 就是 Config1 重复两次）
            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i] == null)
                    continue;

                for (int k = 0; k < i; k++)
                {
                    if (lines[k] != lines[i])
                        continue;

                    problems.Add(New(DialogueLevel.Info, "series.repeat", where + " 第 " + (i + 1) + " 条",
                        "与第 " + (k + 1) + " 条是**同一条配置**（不是复制出来的新条目）：改文案会两处一起变；" +
                        "想各写各的就用「复制」"));
                    break;
                }
            }

            return problems;
        }

        /// <summary>局内部分的全局体检：每条配置 + 每个系列 + 孤儿资产。</summary>
        public static List<DialogueProblem> CheckInMatchAll()
        {
            var problems = new List<DialogueProblem>();
            if (!DialogueTool.SpriteRegistryFound)
            {
                problems.Add(New(DialogueLevel.Error, "registry.sprite", "工程",
                    "读不到 SpriteRegistry.asset：立绘 Key 都没法校验"));
                return problems;
            }

            IReadOnlyList<DialogueConfigInfo> configs = DialogueTool.Configs;
            for (int i = 0; i < configs.Count; i++)
            {
                List<DialogueProblem> one = CheckConfig(configs[i].Asset, configs[i].Path);
                for (int k = 0; k < one.Count; k++)
                    problems.Add(one[k]);
            }

            IReadOnlyList<DialogueSeriesInfo> series = DialogueTool.Series;
            for (int i = 0; i < series.Count; i++)
            {
                List<DialogueProblem> one = CheckSeries(series[i].Asset, series[i].Path);
                for (int k = 0; k < one.Count; k++)
                    problems.Add(one[k]);
            }

            int orphans = 0;
            for (int i = 0; i < configs.Count; i++)
            {
                if (configs[i].RefCount == 0)
                    orphans++;
            }

            if (orphans > 0)
            {
                problems.Add(New(DialogueLevel.Info, "config.orphan", "工程",
                    "有 " + orphans + " 条配置没有被任何系列引用（散条）：不会出现在任何对话里"));
            }

            if (configs.Count == 0 && series.Count == 0)
            {
                problems.Add(New(DialogueLevel.Info, "match.none", "工程",
                    "工程里还没有任何局内对话资产（DialogueConfigSO / DialogueSeriesSO）"));
            }

            return problems;
        }
        #endregion

        /// <summary>按严重程度排序（错误在前），方便界面上直接列。</summary>
        public static void Sort(List<DialogueProblem> problems)
        {
            if (problems == null)
                return;

            problems.Sort((a, b) =>
            {
                int c = b.Level.CompareTo(a.Level);
                return c != 0 ? c : string.CompareOrdinal(a.Where, b.Where);
            });
        }

        public static int CountLevel(List<DialogueProblem> problems, DialogueLevel level)
        {
            if (problems == null)
                return 0;

            int n = 0;
            for (int i = 0; i < problems.Count; i++)
            {
                if (problems[i].Level == level)
                    n++;
            }
            return n;
        }

        private static DialogueProblem New(DialogueLevel level, string code, string where, string message)
        {
            return new DialogueProblem { Level = level, Code = code, Where = where, Message = message };
        }
    }
}
