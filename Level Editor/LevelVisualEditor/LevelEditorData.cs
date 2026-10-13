using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CageFightStageTool
{
    /// <summary>
    /// 编辑器偏好（前缀 CageFight.LevelVisualEditor.）。
    /// 与图鉴编辑器同一个做法：关掉窗口再打开，栏宽/页签/开关都还在。
    /// </summary>
    public static class LevelEditorPrefs
    {
        private const string Prefix = "CageFight.LevelVisualEditor.";

        private static string Get(string key, string fallback)
        {
            return EditorPrefs.GetString(Prefix + key, fallback);
        }

        public static string LastStagePath
        {
            get { return Get("LastStagePath", string.Empty); }
            set { EditorPrefs.SetString(Prefix + "LastStagePath", value ?? string.Empty); }
        }

        public static string BackupFolder
        {
            get { return Get("BackupFolder", StageEditorData.DefaultBackupFolder); }
            set { EditorPrefs.SetString(Prefix + "BackupFolder", value ?? string.Empty); }
        }

        public static int KeepBackupCount
        {
            get { return EditorPrefs.GetInt(Prefix + "KeepBackupCount", 20); }
            set { EditorPrefs.SetInt(Prefix + "KeepBackupCount", Mathf.Clamp(value, 1, 500)); }
        }

        public static bool AutoBackupOnSave
        {
            get { return EditorPrefs.GetBool(Prefix + "AutoBackupOnSave", true); }
            set { EditorPrefs.SetBool(Prefix + "AutoBackupOnSave", value); }
        }

        /// <summary>
        /// 锁定地图尺寸：开启后体检与坐标换算只用设置里的宽高，不再读当前打开场景的 MapCells。
        /// 默认关（保持原来的"跟着场景走"行为，不改变老用户结论）。
        /// </summary>
        public static bool LockMapSize
        {
            get { return EditorPrefs.GetBool(Prefix + "LockMapSize", false); }
            set { EditorPrefs.SetBool(Prefix + "LockMapSize", value); }
        }

        public static float ListWidth
        {
            get { return EditorPrefs.GetFloat(Prefix + "ListWidth", 300f); }
            set { EditorPrefs.SetFloat(Prefix + "ListWidth", value); }
        }

        public static float RightWidth
        {
            get { return EditorPrefs.GetFloat(Prefix + "RightWidth", 430f); }
            set { EditorPrefs.SetFloat(Prefix + "RightWidth", value); }
        }

        /// <summary>右栏（体检 / 设置）是否收起。收起后中栏拿到全部宽度 —— 体检面板很占地方。</summary>
        public static bool RightCollapsed
        {
            get { return EditorPrefs.GetBool(Prefix + "RightCollapsed", false); }
            set { EditorPrefs.SetBool(Prefix + "RightCollapsed", value); }
        }

        /// <summary>主窗口当前页：0 = 关卡编辑，1 = 兵种一览（R1.32）。</summary>
        public static int Page
        {
            get { return EditorPrefs.GetInt(Prefix + "Page", 0); }
            set { EditorPrefs.SetInt(Prefix + "Page", value); }
        }

        /// <summary>
        /// 「兵种一览」的字号（12 = 小 / 14 = 中（默认）/ 16 = 大 / 18 = 特大）。
        /// 表格整页的行高、头像、方块都按它算 —— 小字号在 2K 屏上看着太累。
        /// </summary>
        public static int RosterFontSize
        {
            get { return EditorPrefs.GetInt(Prefix + "RosterFontSize", 14); }
            set { EditorPrefs.SetInt(Prefix + "RosterFontSize", Mathf.Clamp(value, 10, 22)); }
        }

        public static int RightTab
        {
            get { return EditorPrefs.GetInt(Prefix + "RightTab", 0); }
            set { EditorPrefs.SetInt(Prefix + "RightTab", value); }
        }

        /// <summary>战场画布默认格数。真正的格数以场景里的 MapCells 为准，这里只是没打开场景时的兜底。</summary>
        public static int MapWidth
        {
            get { return EditorPrefs.GetInt(Prefix + "MapWidth", 74); }
            set { EditorPrefs.SetInt(Prefix + "MapWidth", Mathf.Clamp(value, 8, 512)); }
        }

        public static int MapHeight
        {
            get { return EditorPrefs.GetInt(Prefix + "MapHeight", 8); }
            set { EditorPrefs.SetInt(Prefix + "MapHeight", Mathf.Clamp(value, 2, 128)); }
        }

        public static bool ShowObjectLabels
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowObjectLabels", false); }
            set { EditorPrefs.SetBool(Prefix + "ShowObjectLabels", value); }
        }

        public static bool GroupObjectsByPrefab
        {
            get { return EditorPrefs.GetBool(Prefix + "GroupObjectsByPrefab", true); }
            set { EditorPrefs.SetBool(Prefix + "GroupObjectsByPrefab", value); }
        }

        /// <summary>时间轴推演的时长（秒）。0 = 自动取本关最长时限。</summary>
        public static float TimelineSeconds
        {
            get { return EditorPrefs.GetFloat(Prefix + "TimelineSeconds", 0f); }
            set { EditorPrefs.SetFloat(Prefix + "TimelineSeconds", Mathf.Clamp(value, 0f, 3600f)); }
        }

        /// <summary>
        /// 按关卡类型存一套「通关奖励模板」（R1.29）。
        /// 存成一行紧凑字符串 gold;diamond;customNormal;normalGold;normalDiamond，
        /// 空串 = 这一类型还没设过模板（新建关卡时就不覆盖默认奖励）。
        /// 为什么放 EditorPrefs 而不是工程资产：模板是**策划本机的默认值**，不该进游戏包、
        /// 也不该改动任何游戏资产；与本工具其它偏好（栏宽/页签/备份份数）同一处管理。
        /// </summary>
        public static StageRewardTemplate GetRewardTemplate(UserGlobalInfo.StageType stageType)
        {
            return StageRewardTemplate.Parse(Get(TemplateKey(stageType), string.Empty));
        }

        public static void SetRewardTemplate(UserGlobalInfo.StageType stageType, StageRewardTemplate template)
        {
            EditorPrefs.SetString(Prefix + TemplateKey(stageType), template.ToPrefsString());
        }

        public static void ClearRewardTemplate(UserGlobalInfo.StageType stageType)
        {
            EditorPrefs.DeleteKey(Prefix + TemplateKey(stageType));
        }

        /// <summary>写注释的人（记住上一次填的名字，省得每次重打）。</summary>
        public static string NoteAuthor
        {
            get { return Get("NoteAuthor", string.Empty); }
            set { EditorPrefs.SetString(Prefix + "NoteAuthor", value ?? string.Empty); }
        }

        private static string TemplateKey(UserGlobalInfo.StageType stageType)
        {
            return "RewardTemplate." + stageType;
        }
    }

    /// <summary>
    /// 一套通关奖励模板（R1.29）：按关卡类型各存一份，用来「套用到同类型的所有关卡」，
    /// 并作为**新建关卡**的奖励默认值。
    /// 字段与 StageConfig 里的 5 个奖励字段一一对应（大师金条/钻石 + 普通是否自定义 + 普通金条/钻石）。
    /// </summary>
    public struct StageRewardTemplate
    {
        /// <summary>是否已经设置过（false = 这一类型还没模板，界面显示「未设置」）。</summary>
        public bool Valid;

        public int GoldBars;          // 大师模式金条
        public int Diamonds;          // 大师模式钻石
        public bool CustomNormal;     // 普通模式是否单独填
        public int NormalGoldBars;    // 普通模式金条（自定义时生效）
        public int NormalDiamonds;    // 普通模式钻石（自定义时生效）

        public static StageRewardTemplate Parse(string text)
        {
            var template = new StageRewardTemplate();
            if (string.IsNullOrEmpty(text))
                return template;

            string[] parts = text.Split(';');
            if (parts.Length < 5)
                return template;

            int gold, diamond, normalGold, normalDiamond;
            if (!int.TryParse(parts[0], out gold) ||
                !int.TryParse(parts[1], out diamond) ||
                !int.TryParse(parts[3], out normalGold) ||
                !int.TryParse(parts[4], out normalDiamond))
                return template;

            template.GoldBars = Mathf.Max(0, gold);
            template.Diamonds = Mathf.Max(0, diamond);
            template.CustomNormal = parts[2] == "1";
            template.NormalGoldBars = Mathf.Max(0, normalGold);
            template.NormalDiamonds = Mathf.Max(0, normalDiamond);
            template.Valid = true;
            return template;
        }

        public string ToPrefsString()
        {
            return GoldBars + ";" + Diamonds + ";" + (CustomNormal ? "1" : "0") + ";" +
                   NormalGoldBars + ";" + NormalDiamonds;
        }

        /// <summary>普通模式**实发**金条（不勾自定义时 = 大师的一半，与运行时同一套算法）。</summary>
        public int EffectiveNormalGoldBars
        {
            get { return CustomNormal ? NormalGoldBars : GoldBars / 2; }
        }

        public int EffectiveNormalDiamonds
        {
            get { return CustomNormal ? NormalDiamonds : Diamonds / 2; }
        }

        /// <summary>一句话说明（按钮 tooltip / 体检文案共用）。</summary>
        public string Describe()
        {
            return Valid
                ? "大师 " + GoldBars + " 金条 / " + Diamonds + " 钻石；普通 " +
                  (CustomNormal ? "自定义 " + NormalGoldBars + " 金条 / " + NormalDiamonds + " 钻石"
                                : "自动一半 " + EffectiveNormalGoldBars + " 金条 / " + EffectiveNormalDiamonds + " 钻石")
                : "（还没为这一类型设置模板）";
        }

        public static string TypeName(UserGlobalInfo.StageType stageType)
        {
            switch (stageType)
            {
                case UserGlobalInfo.StageType.Attack: return "进攻关";
                case UserGlobalInfo.StageType.Boss: return "Boss 关";
                default: return "防御关";
            }
        }
    }

    /// <summary>一条关卡注释（R1.30）：按关卡编号索引，写的人是最后保存的那个。</summary>
    [Serializable]
    public sealed class StageNote
    {
        public int StageId;
        public string Text = string.Empty;
        public string Author = string.Empty;
        public string UpdatedUtc = string.Empty;

        /// <summary>列表里显示的一行摘要（取注释首行，超长截断）。</summary>
        public string Summary(int maxLength = 60)
        {
            if (string.IsNullOrEmpty(Text))
                return string.Empty;

            string firstLine = Text;
            int cut = firstLine.IndexOf('\n');
            if (cut >= 0)
                firstLine = firstLine.Substring(0, cut);

            firstLine = firstLine.Trim();
            if (firstLine.Length > maxLength)
                firstLine = firstLine.Substring(0, maxLength) + "…";
            return firstLine;
        }

        public string TimeLabel
        {
            get
            {
                DateTime parsed;
                if (!DateTime.TryParse(UpdatedUtc, out parsed))
                    return string.Empty;

                return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
        }
    }

    /// <summary>StageNotes.json 的根对象。</summary>
    [Serializable]
    public sealed class StageNoteFile
    {
        public int Version = 1;
        public List<StageNote> Notes = new List<StageNote>();
    }

    /// <summary>
    /// 关卡注释仓库（R1.30）：一份 JSON 放在**工具目录**（`&lt;ToolRoot&gt;/StageNotes.json`），按关卡编号索引。
    ///
    /// 为什么存在工具目录、而不是写进关卡资产：
    ///   · 注释是给人看的协作信息，没必要跟着游戏包发出去；
    ///   · 这样**不用动任何游戏代码**（StageConfig 一行不改、不加字段）；
    ///   · 与备份目录同一层，找得到、能直接抄给别人、也方便整份复制带走。
    /// 代价：它是**工具侧的**数据 —— 想发给没装这个工具的人，用窗口里的「复制」或「导出全部为 Markdown」。
    ///
    /// 键用**关卡编号**（存档与 Addressables 地址都用它，改文件名不影响）；
    /// 关卡被删/改号后留下的条目会在注释窗口里列成「失效注释」，由你决定删不删。
    /// </summary>
    public static class StageNoteStore
    {
        private static StageNoteFile _cache;

        public static string FilePath
        {
            get { return Path.Combine(StageEditorData.ToolRoot, "StageNotes.json"); }
        }

        public static string MarkdownPath
        {
            get { return Path.Combine(StageEditorData.ToolRoot, "StageNotes.md"); }
        }

        private static StageNoteFile Data
        {
            get
            {
                if (_cache != null)
                    return _cache;

                _cache = new StageNoteFile();
                try
                {
                    if (File.Exists(FilePath))
                    {
                        string json = File.ReadAllText(FilePath);
                        var parsed = JsonUtility.FromJson<StageNoteFile>(json);
                        if (parsed != null && parsed.Notes != null)
                            _cache = parsed;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[关卡编辑器] 读取关卡注释失败：" + exception.Message);
                }

                if (_cache.Notes == null)
                    _cache.Notes = new List<StageNote>();

                return _cache;
            }
        }

        /// <summary>取某一关的注释；没有则返回 null。</summary>
        public static StageNote Get(int stageId)
        {
            List<StageNote> notes = Data.Notes;
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i] != null && notes[i].StageId == stageId)
                    return notes[i];
            }
            return null;
        }

        public static bool Has(int stageId)
        {
            StageNote note = Get(stageId);
            return note != null && !string.IsNullOrEmpty(note.Text);
        }

        public static List<StageNote> All()
        {
            var result = new List<StageNote>();
            List<StageNote> notes = Data.Notes;
            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i] != null && !string.IsNullOrEmpty(notes[i].Text))
                    result.Add(notes[i]);
            }

            result.Sort((a, b) => a.StageId.CompareTo(b.StageId));
            return result;
        }

        /// <summary>写入/更新某一关的注释；文本为空 = 删除这一条。</summary>
        public static void Set(int stageId, string text, string author)
        {
            if (string.IsNullOrEmpty(text))
            {
                Remove(stageId);
                return;
            }

            StageNote note = Get(stageId);
            if (note == null)
            {
                note = new StageNote { StageId = stageId };
                Data.Notes.Add(note);
            }

            note.Text = text;
            note.Author = author ?? string.Empty;
            note.UpdatedUtc = DateTime.UtcNow.ToString("o");
            Save();
        }

        public static void Remove(int stageId)
        {
            for (int i = Data.Notes.Count - 1; i >= 0; i--)
            {
                if (Data.Notes[i] != null && Data.Notes[i].StageId == stageId)
                    Data.Notes.RemoveAt(i);
            }

            Save();
        }

        private static void Save()
        {
            try
            {
                string json = JsonUtility.ToJson(Data, true);
                File.WriteAllText(FilePath, json, new System.Text.UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[关卡编辑器] 写入关卡注释失败：" + exception.Message);
            }
        }

        /// <summary>
        /// 导出全部注释为 Markdown（写 &lt;ToolRoot&gt;/StageNotes.md），返回写出的文件路径。
        /// 用途：直接贴给别人、或放进版本库让不装工具的人也能看。
        /// </summary>
        public static string ExportMarkdown(List<StageInfo> stages)
        {
            var builder = new System.Text.StringBuilder();
            builder.AppendLine("# 关卡注释");
            builder.AppendLine();
            builder.AppendLine("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            builder.AppendLine();

            List<StageNote> notes = All();
            for (int i = 0; i < notes.Count; i++)
            {
                StageNote note = notes[i];
                StageInfo info = FindStage(stages, note.StageId);
                builder.AppendLine("## " + (info != null ? info.Label : ("Stage" + note.StageId)) +
                                   "　（编号 " + note.StageId + "）");
                if (info != null)
                    builder.AppendLine("资产：`" + info.Path + "`");
                if (!string.IsNullOrEmpty(note.Author) || !string.IsNullOrEmpty(note.TimeLabel))
                    builder.AppendLine("最后更新：" + note.Author + "　" + note.TimeLabel);
                builder.AppendLine();
                builder.AppendLine(note.Text);
                builder.AppendLine();
            }

            try
            {
                File.WriteAllText(MarkdownPath, builder.ToString(), new System.Text.UTF8Encoding(false));
                return MarkdownPath;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[关卡编辑器] 导出注释 Markdown 失败：" + exception.Message);
                return null;
            }
        }

        public static StageInfo FindStage(List<StageInfo> stages, int stageId)
        {
            if (stages == null)
                return null;

            for (int i = 0; i < stages.Count; i++)
            {
                if (stages[i] != null && stages[i].StageId == stageId)
                    return stages[i];
            }
            return null;
        }
    }

    /// <summary>
    /// 运行时地图尺寸：以场景里的 MapCells 为准（关卡资产里没有尺寸字段）。
    ///
    /// 已核实：ToolTest / ToolTest 1 是 72×8，StageScene 是 74×8，FirstScene 是 65×6；
    /// MapCells 的代码默认值 20×20 只是兜底。1 格 = 1 世界单位，
    /// 格 (x, y) 的中心是 (x+0.5, y+0.5)，有效范围 x ∈ [0, width)、y ∈ [0, height)。
    /// 体检里「坐标越界 / 槽位在玩家半场」这几条规则要用它。
    /// </summary>
    public static class StageMap
    {
        private static int _cachedWidth;
        private static int _cachedHeight;
        private static string _cachedSource;

        public static int Width { get { EnsureResolved(); return _cachedWidth; } }
        public static int Height { get { EnsureResolved(); return _cachedHeight; } }
        /// <summary>尺寸来源说明，直接显示在界面上，避免"我到底按多大的地图在算"。</summary>
        public static string SourceLabel { get { EnsureResolved(); return _cachedSource; } }

        public static void Invalidate()
        {
            _cachedSource = null;
        }

        private static void EnsureResolved()
        {
            if (!string.IsNullOrEmpty(_cachedSource))
                return;

            // 「锁定地图尺寸」：勾上之后**完全不看当前打开的场景**。
            // 为什么需要它：地图尺寸原本取自"当前场景的 MapCells"，换一个场景打开同一份关卡数据，
            // 坐标越界 / 半场分界 / 槽位合法性的结论都会跟着变 —— 同一份数据给出不同体检结果。
            if (LevelEditorPrefs.LockMapSize)
            {
                _cachedWidth = Mathf.Max(1, LevelEditorPrefs.MapWidth);
                _cachedHeight = Mathf.Max(1, LevelEditorPrefs.MapHeight);
                _cachedSource = "已锁定：" + _cachedWidth + "×" + _cachedHeight + "（设置里指定，不随当前场景变化）";
                return;
            }

            int width, height;
            string source;
            if (TryGetFromScene(out width, out height, out source))
            {
                _cachedWidth = width;
                _cachedHeight = height;
                _cachedSource = source;
                return;
            }

            _cachedWidth = LevelEditorPrefs.MapWidth;
            _cachedHeight = LevelEditorPrefs.MapHeight;
            _cachedSource = "设置里的兜底值（当前场景没有 MapCells）";
        }

        private static bool TryGetFromScene(out int width, out int height, out string source)
        {
            width = height = 0;
            source = null;

            try
            {
                MapCells[] all = UnityEngine.Object.FindObjectsOfType<MapCells>(true);
                for (int i = 0; i < all.Length; i++)
                {
                    MapCells cells = all[i];
                    if (cells == null)
                        continue;

                    UnityEngine.SceneManagement.Scene scene = cells.gameObject.scene;
                    if (!scene.IsValid() || !scene.isLoaded)
                        continue;

                    width = Mathf.Max(1, cells.width);
                    height = Mathf.Max(1, cells.height);
                    source = "场景「" + scene.name + "」的 MapCells（" + width + "×" + height + "）";
                    return true;
                }
            }
            catch (Exception)
            {
                // 场景不可用就退回默认值。
            }

            return false;
        }
    }

    /// <summary>
    /// 运行时事实：界面上所有"数值 → 结论"的换算都必须走这里，
    /// 每条都注明出处，改游戏代码时回来对一遍即可。
    ///
    /// 这些是**照抄**运行时代码的公式，不是近似值——工具只在编辑器里用它们做推演，
    /// 不参与任何运行时计算。
    /// </summary>
    public static class StageFacts
    {
        /// <summary>等级缩放系数 1.1^(等级-1)。出处：Runtime\Gameplay\GameObjectProperty.cs:360-372（LevelScale.PerLevel / Pow）。</summary>
        public const float LevelScalePerLevel = 1.1f;

        /// <summary>等级 ≤ 1 不缩放。作用对象：maxHp / atk / magicAtk（建筑侧只作用于 maxHp / atk）。</summary>
        public static float LevelScale(int level)
        {
            return Mathf.Pow(LevelScalePerLevel, Mathf.Max(0, level - 1));
        }

        /// <summary>
        /// 曲线模式下的玩家每秒收入。
        /// 出处：Runtime\Gameplay\EconomicSystem\Coins.cs:136-158（ComputeCurveIncome）逐行照抄。
        ///
        /// 语义要点（与运行时一致，别"顺手优化"）：
        ///   · phase == null 跳过；
        ///   · t &lt;= phase.startTime 时该阶段贡献 0（正好等于开始时刻，第一步还没触发）；
        ///   · endTime &lt;= startTime 表示持续到关卡结束（窗口末端取 t）；
        ///   · 步数 = floor(窗口长度 / stepInterval)（向下取整，所以是阶梯不是斜坡）；
        ///   · 各阶段的增量**累加**到同一个 income；
        ///   · 最后统一 Max(0, RoundToInt(income)) —— 不是每阶段各自取整。
        /// </summary>
        public static int ComputeCurveIncome(float baseIncome, IList<IncomeGrowthPhase> phases, float t)
        {
            float income = baseIncome;
            if (phases != null)
            {
                for (int i = 0; i < phases.Count; i++)
                {
                    IncomeGrowthPhase phase = phases[i];
                    if (phase == null || t <= phase.startTime)
                        continue;

                    float windowEnd = phase.endTime > phase.startTime
                        ? Mathf.Min(t, phase.endTime)
                        : t;
                    float span = Mathf.Max(0f, windowEnd - phase.startTime);
                    float interval = Mathf.Max(0.1f, phase.stepInterval);
                    int steps = Mathf.FloorToInt(span / interval);
                    income += steps * Mathf.Max(0f, phase.stepAmount);
                }
            }

            return Mathf.Max(0, Mathf.RoundToInt(income));
        }

        /// <summary>
        /// 未启用曲线时的场景时间阶梯。
        /// 出处：Coins.cs:35-54 —— 从场景 coinPerSec 起，把所有 phaseTimes[i] &lt;= t 的值**依次覆盖**，最后一个命中的胜出（不是累加）。
        /// </summary>
        public static int ComputeStepIncome(int sceneCoinPerSec, float[] phaseTimes, int[] phaseCoinPerSec, float t)
        {
            int value = sceneCoinPerSec;
            if (phaseTimes != null && phaseCoinPerSec != null)
            {
                int count = Mathf.Min(phaseTimes.Length, phaseCoinPerSec.Length);
                for (int i = 0; i < count; i++)
                {
                    if (t >= phaseTimes[i])
                        value = phaseCoinPerSec[i];
                }
            }
            return value;
        }

        /// <summary>净收入 = max(0, 每秒收入 − 维护费总和)。出处：Coins.cs:73。维护费只压低入账流速，不动余额、不欠费。</summary>
        public static int NetCoinPerSec(int currentCoinPerSec, int totalUpkeep)
        {
            return Mathf.Max(0, currentCoinPerSec - totalUpkeep);
        }

        /// <summary>本关玩家每秒收入（按关卡是否启用曲线自动选分支）。</summary>
        public static int PlayerIncomePerSec(StageConfig config, float t, int sceneCoinPerSec, float[] scenePhaseTimes, int[] scenePhaseCoins)
        {
            if (config == null)
                return sceneCoinPerSec;

            if (config.useIncomeCurve)
                return ComputeCurveIncome(Mathf.Max(0f, config.baseGoldPerSecond), config.incomeGrowth, t);

            return ComputeStepIncome(sceneCoinPerSec, scenePhaseTimes, scenePhaseCoins, t);
        }

        /// <summary>敌方队伍的每秒收入（与玩家同一套阶段公式，基准是队伍自己的 baseGoldPerSecond）。出处：StageConfig.EnemyTeamConfig + Coins.ComputeCurveIncome 同构。</summary>
        public static int EnemyIncomePerSec(EnemyTeamConfig team, float t)
        {
            if (team == null)
                return 0;

            return ComputeCurveIncome(Mathf.Max(0f, team.baseGoldPerSecond), team.incomeGrowth, t);
        }

        /// <summary>我方大本营左下基准格的 X。出处：StageConfig.friendlyMainBaseGridPosition。</summary>
        public static int FriendlyBaseX(StageConfig config)
        {
            return config != null ? config.friendlyMainBaseGridPosition.x : 0;
        }

        /// <summary>敌方大本营的 X = 我方大本营 X + enemyBaseDistance（至少 1）。出处：Runtime\Gameplay\Building\BuildingBase.cs:147-159（IsInOwnHalf）。</summary>
        public static int EnemyBaseX(StageConfig config)
        {
            if (config == null)
                return 0;
            return FriendlyBaseX(config) + Mathf.Max(1, config.enemyBaseDistance);
        }

        /// <summary>半场分界格 = 两者中点（整数除法）。两侧都是闭区间：中点格双方都能建。出处：BuildingBase.cs:147-159。</summary>
        public static int MidpointX(StageConfig config)
        {
            if (config == null)
                return 0;
            return (FriendlyBaseX(config) + EnemyBaseX(config)) / 2;
        }

        /// <summary>
        /// 行军时间估算（格 → 秒）：只按格子数与一个可调"每格秒数"换算，
        /// 因为运行时单位的移速由 Prefab 上的 GameObjectProperty.moveSpeed 决定，
        /// 关卡数据里并没有速度字段。真正的秒数以界面上的"每格秒数"参数为准。
        /// </summary>
        public static float CellsToSeconds(int cells, float secondsPerCell)
        {
            return Mathf.Max(0, cells) * Mathf.Max(0.01f, secondsPerCell);
        }
    }

    /// <summary>
    /// 玩家的**初始金币**（写在**游戏场景**的 Coins 组件上，不是关卡字段）。
    ///
    /// 已核实的事实：
    ///   · `Coins.coins`（`Runtime\Gameplay\EconomicSystem\Coins.cs:9`）是场景序列化值 ——
    ///     进关卡时它就是玩家的开局余额。`Coins.OnLevelStart()`（`:114-129`）只记录局内时间起点
    ///     并读取收入曲线，**不会重置 coins**，所以它是**全局一个值**，所有关卡共用。
    ///   · 敌人侧是每队一个 `EnemyTeamConfig.startGold`（关卡字段）；玩家侧**没有**对应字段，
    ///     关卡配置里预留的扩展位 `StageSettings`（`StageConfig.cs:11-14`）目前是个**空占位类**。
    ///   · 所以本工具只读写场景里的那一行值：**不新增游戏代码、不改运行时逻辑**。
    ///     想要"每关不同"的初始金币，需要给关卡加字段并让 Coins 开局读取它（那属于改游戏代码）。
    /// </summary>
    public static class PlayerEconomy
    {
        /// <summary>游戏场景（进关卡时加载的那个场景）。</summary>
        public const string GameplayScenePath = "Assets/Scenes/StageScene.unity";

        private const string CoinsScriptPath = "Assets/Runtime/Gameplay/EconomicSystem/Coins.cs";

        /// <summary>游戏场景是否正开在 Unity 里 —— 开着的时候**不能**从外部改文件（会被场景保存覆盖）。</summary>
        public static bool IsGameplaySceneLoaded()
        {
            UnityEngine.SceneManagement.Scene scene =
                UnityEngine.SceneManagement.SceneManager.GetSceneByPath(GameplayScenePath);
            return scene.IsValid() && scene.isLoaded;
        }

        /// <summary>读场景里 Coins 的初始金币。</summary>
        public static bool TryReadStartCoins(out int value, out string message)
        {
            value = 0;
            message = null;

            if (!File.Exists(GameplayScenePath))
            {
                message = "找不到游戏场景：" + GameplayScenePath;
                return false;
            }

            string[] lines = File.ReadAllLines(GameplayScenePath);
            int index = FindCoinsLineIndex(lines);
            if (index < 0)
            {
                message = "在 " + GameplayScenePath + " 里没找到 Coins 组件的 coins 字段。";
                return false;
            }

            string raw = lines[index].Substring(lines[index].IndexOf(':') + 1).Trim();
            int parsed;
            if (!int.TryParse(raw, out parsed))
            {
                message = "coins 的值读不出来：「" + lines[index].Trim() + "」";
                return false;
            }

            value = parsed;
            return true;
        }

        /// <summary>
        /// 把初始金币写回场景（**只改一行**，写前自动备份场景资产）。
        /// 场景正被打开时拒绝写入 —— 否则 Unity 保存场景时会把这次写入盖回去。
        /// </summary>
        public static bool TryWriteStartCoins(int value, out string message)
        {
            message = null;
            if (value < 0)
                value = 0;

            if (!File.Exists(GameplayScenePath))
            {
                message = "找不到游戏场景：" + GameplayScenePath;
                return false;
            }

            if (IsGameplaySceneLoaded())
            {
                message = "游戏场景正开在 Unity 里（" + GameplayScenePath + "）：先从 Unity 切到别的场景，" +
                          "否则保存场景时会把这次写入盖回去。";
                return false;
            }

            string[] lines = File.ReadAllLines(GameplayScenePath);
            int index = FindCoinsLineIndex(lines);
            if (index < 0)
            {
                message = "没找到 Coins 的 coins 字段，未写入。";
                return false;
            }

            string oldLine = lines[index].Trim();
            string indent = lines[index].Substring(0, lines[index].IndexOf('c'));
            string newLine = indent + "coins: " + value;
            if (string.Equals(lines[index], newLine, StringComparison.Ordinal))
            {
                message = "场景里已经是 " + value + "，无需写入。";
                return false;
            }

            // 只替换这一行，不整文件重写 —— 场景文件是 CRLF（实测 StageScene.unity 含 CRLF），
            // 整文件重写会带来行尾/编码漂移的风险，也会让 diff 变成"整个文件都被改了"。
            string raw;
            try
            {
                raw = File.ReadAllText(GameplayScenePath);
            }
            catch (Exception exception)
            {
                message = "读取场景失败：" + exception.Message;
                return false;
            }

            int offset = 0;
            for (int i = 0; i < index; i++)
            {
                int next = raw.IndexOf('\n', offset);
                if (next < 0)
                {
                    message = "场景行数与磁盘内容对不上（可能刚被 Unity 改写），请点「重新读取」再试。";
                    return false;
                }
                offset = next + 1;
            }

            int lineEnd = raw.IndexOf('\n', offset);
            if (lineEnd < 0)
                lineEnd = raw.Length;
            int contentEnd = lineEnd;
            if (contentEnd > offset && raw[contentEnd - 1] == '\r')
                contentEnd--;   // 保留原本的 \r\n

            StageEditorData.BackupFile(GameplayScenePath, "改初始金币前");

            string replaced = raw.Substring(0, offset) + newLine + raw.Substring(contentEnd);
            File.WriteAllText(GameplayScenePath, replaced, new System.Text.UTF8Encoding(false));
            AssetDatabase.ImportAsset(GameplayScenePath);

            message = "已把游戏场景里 Coins 的初始金币改成 " + value + "（" + oldLine + " → coins: " + value + "）。";
            return true;
        }

        /// <summary>
        /// 找到 Coins 组件里那一行 `coins:`：先用 Coins 脚本的 guid 定位组件块，再往下找字段。
        /// （Unity 的 MonoBehaviour 先写 m_Script，再按字段声明顺序写各字段，所以往下找是稳定的。）
        /// </summary>
        private static int FindCoinsLineIndex(string[] lines)
        {
            string guid = AssetDatabase.AssetPathToGUID(CoinsScriptPath);
            if (string.IsNullOrEmpty(guid))
                return -1;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(guid, StringComparison.Ordinal) < 0 ||
                    lines[i].IndexOf("m_Script", StringComparison.Ordinal) < 0)
                    continue;

                for (int k = i + 1; k < Math.Min(lines.Length, i + 40); k++)
                {
                    if (lines[k].StartsWith("---", StringComparison.Ordinal))
                        break;
                    if (lines[k].TrimStart().StartsWith("coins:", StringComparison.Ordinal))
                        return k;
                }
            }

            return -1;
        }
    }

    /// <summary>
    /// 资源索引：Sprite / 预制体 / 兵种 / 种族 / 全局规则。
    ///
    /// 为什么自己扫而不问 ResourceManager：
    ///   ResourceManager 的 prefabRegistry / spriteRegistry 是 private 字段、没有 getter
    ///   （ResourceManager.cs:41、:56），而且它的 GetSprite(key) 查的是"本局已加载缓存"
    ///   （ResourceManager.cs:374-378），编辑器里没进关卡就是空的。
    ///   所以这里直接加载注册表资产、遍历 mappings 自建索引——只读，不依赖运行场景。
    /// </summary>
    public sealed class StageResourceIndex
    {
        public const string SpriteRegistryPath = "Assets/RemoteResource/SpriteRegistry.asset";
        public const string PrefabRegistryPath = "Assets/RemoteResource/PrefabRegistry.asset";
        public const string StageGroupAssetPath = "Assets/AddressableAssetsData/AssetGroups/StageConfig.asset";

        private static StageResourceIndex _instance;

        private readonly List<string> _spriteKeys = new List<string>();
        private readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);

        private readonly List<string> _prefabKeys = new List<string>();
        private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);

        private readonly List<TroopDefinition> _troops = new List<TroopDefinition>();
        private readonly Dictionary<string, TroopDefinition> _troopById = new Dictionary<string, TroopDefinition>(StringComparer.OrdinalIgnoreCase);

        private readonly List<RaceDefinition> _races = new List<RaceDefinition>();
        private readonly Dictionary<string, RaceDefinition> _raceById = new Dictionary<string, RaceDefinition>(StringComparer.OrdinalIgnoreCase);

        private readonly List<GlobalRuleDefinition> _globalRules = new List<GlobalRuleDefinition>();

        private readonly HashSet<string> _addressableStageAddresses = new HashSet<string>(StringComparer.Ordinal);
        private bool _addressableScanned;

        private bool _built;

        public bool SpriteRegistryFound { get; private set; }
        public bool PrefabRegistryFound { get; private set; }

        public static StageResourceIndex Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new StageResourceIndex();
                    _instance.EnsureBuilt();
                }
                return _instance;
            }
        }

        public static void Invalidate()
        {
            _instance = null;
        }

        public IReadOnlyList<string> SpriteKeys { get { EnsureBuilt(); return _spriteKeys; } }
        public IReadOnlyList<string> PrefabKeys { get { EnsureBuilt(); return _prefabKeys; } }
        public IReadOnlyList<TroopDefinition> Troops { get { EnsureBuilt(); return _troops; } }
        public IReadOnlyList<RaceDefinition> Races { get { EnsureBuilt(); return _races; } }
        public IReadOnlyList<GlobalRuleDefinition> GlobalRules { get { EnsureBuilt(); return _globalRules; } }

        private void EnsureBuilt()
        {
            if (_built)
                return;
            _built = true;
            Rebuild();
        }

        public void Rebuild()
        {
            _spriteKeys.Clear();
            _sprites.Clear();
            _prefabKeys.Clear();
            _prefabs.Clear();
            _troops.Clear();
            _troopById.Clear();
            _races.Clear();
            _raceById.Clear();
            _globalRules.Clear();
            _prefabStats.Clear();
            _barracksTroops.Clear();

            BuildSprites();
            BuildPrefabs();
            BuildGenericRegistries();
            BuildTroops();
            BuildRaces();
            BuildGlobalRules();

            _spriteKeys.Sort(StringComparer.OrdinalIgnoreCase);
            _prefabKeys.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private void BuildSprites()
        {
            SpriteRegistry registry = AssetDatabase.LoadAssetAtPath<SpriteRegistry>(SpriteRegistryPath);
            if (registry == null)
            {
                // 资产可能被改名/挪走，退回按类型搜索。
                string[] guids = AssetDatabase.FindAssets("t:SpriteRegistry");
                if (guids.Length > 0)
                    registry = AssetDatabase.LoadAssetAtPath<SpriteRegistry>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            SpriteRegistryFound = registry != null;
            if (registry == null || registry.mappings == null)
                return;

            for (int i = 0; i < registry.mappings.Count; i++)
            {
                SpriteMapping mapping = registry.mappings[i];
                if (mapping == null || string.IsNullOrEmpty(mapping.key))
                    continue;

                if (_sprites.ContainsKey(mapping.key))
                    continue;   // 与运行时一致：重复 key 只认第一个（SpriteRegistry.cs:48）

                _spriteKeys.Add(mapping.key);
                _sprites[mapping.key] = mapping.sprite;   // 编辑器下的强引用，仅用于预览
            }
        }

        private void BuildPrefabs()
        {
            PrefabRegistry registry = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(PrefabRegistryPath);
            if (registry == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:PrefabRegistry");
                if (guids.Length > 0)
                    registry = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            PrefabRegistryFound = registry != null;
            if (registry == null || registry.mappings == null)
                return;

            for (int i = 0; i < registry.mappings.Count; i++)
            {
                PrefabMapping mapping = registry.mappings[i];
                if (mapping == null || string.IsNullOrEmpty(mapping.key))
                    continue;

                if (_prefabs.ContainsKey(mapping.key))
                    continue;

                _prefabKeys.Add(mapping.key);
                _prefabs[mapping.key] = mapping.prefab;
            }
        }

        private void BuildTroops()
        {
            string[] guids = AssetDatabase.FindAssets("t:TroopDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                TroopDefinition troop = AssetDatabase.LoadAssetAtPath<TroopDefinition>(path);
                if (troop == null)
                    continue;

                _troops.Add(troop);
                if (!string.IsNullOrEmpty(troop.Id) && !_troopById.ContainsKey(troop.Id))
                    _troopById[troop.Id] = troop;
            }

            // 按「兵营阶级 → 解锁等级 → 显示名」排序，和游戏里兵营的排列一致
            _troops.Sort((a, b) =>
            {
                int ta = a != null ? a.Tier : 0;
                int tb = b != null ? b.Tier : 0;
                if (ta != tb) return ta.CompareTo(tb);

                int ua = a != null ? a.UnlockLevel : 0;
                int ub = b != null ? b.UnlockLevel : 0;
                if (ua != ub) return ua.CompareTo(ub);

                string na = a != null ? (string.IsNullOrEmpty(a.DisplayName) ? a.Id : a.DisplayName) : string.Empty;
                string nb = b != null ? (string.IsNullOrEmpty(b.DisplayName) ? b.Id : b.DisplayName) : string.Empty;
                return string.Compare(na, nb, StringComparison.CurrentCulture);
            });
        }

        private void BuildRaces()
        {
            string[] guids = AssetDatabase.FindAssets("t:RaceDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                RaceDefinition race = AssetDatabase.LoadAssetAtPath<RaceDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (race == null)
                    continue;

                _races.Add(race);
                if (!string.IsNullOrEmpty(race.Id) && !_raceById.ContainsKey(race.Id))
                    _raceById[race.Id] = race;
            }

            _races.Sort((a, b) => string.Compare(
                a != null ? a.Id : string.Empty,
                b != null ? b.Id : string.Empty,
                StringComparison.OrdinalIgnoreCase));
        }

        private void BuildGlobalRules()
        {
            string[] guids = AssetDatabase.FindAssets("t:GlobalRuleDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                GlobalRuleDefinition rule = AssetDatabase.LoadAssetAtPath<GlobalRuleDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (rule != null)
                    _globalRules.Add(rule);
            }

            _globalRules.Sort((a, b) => string.Compare(
                a != null ? a.DisplayName : string.Empty,
                b != null ? b.DisplayName : string.Empty,
                StringComparison.CurrentCulture));
        }

        #region 兵种数值 / 兵营兵种表
        /// <summary>
        /// 预制体上的基础数值。运行时单位出生时按「兵营等级」缩放的就是这几个
        /// （GameObjectProperty.ApplyLevelScale 作用于 maxHp / atk / magicAtk，射程与移速不缩放）。
        /// </summary>
        public struct PrefabStats
        {
            public bool HasProperty;
            public int MaxHp;
            public int Atk;
            public int MagicAtk;
            public float MoveSpeed;
            public int OccupyX;
            public int OccupyY;
        }

        private readonly Dictionary<string, PrefabStats> _prefabStats = new Dictionary<string, PrefabStats>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<TroopDefinition>> _barracksTroops = new Dictionary<string, List<TroopDefinition>>(StringComparer.Ordinal);

        /// <summary>按预制体键读基础数值。返回是否找到了这个预制体（找到但没挂组件时 HasProperty 为 false）。</summary>
        public bool TryGetPrefabStats(string prefabKey, out PrefabStats stats)
        {
            EnsureBuilt();

            stats = default(PrefabStats);
            if (string.IsNullOrEmpty(prefabKey))
                return false;

            if (_prefabStats.TryGetValue(prefabKey, out stats))
                return stats.HasProperty || _prefabStats.ContainsKey(prefabKey);

            GameObject prefab = GetPrefab(prefabKey);
            if (prefab == null)
                return false;

            GameObjectProperty prop = prefab.GetComponent<GameObjectProperty>();
            if (prop != null)
            {
                stats.HasProperty = true;
                stats.MaxHp = prop.maxHp;
                stats.Atk = prop.atk;
                stats.MagicAtk = prop.magicAtk;
                stats.MoveSpeed = prop.moveSpeed;
                stats.OccupyX = prop.occupySpace.x;
                stats.OccupyY = prop.occupySpace.y;
            }

            _prefabStats[prefabKey] = stats;
            return true;
        }

        /// <summary>
        /// 某个种族兵营（普通 / 黑暗）预制体上挂的兵种表。
        /// 链路：RaceDefinition.buildings 里找 Barracks / DarkBarracks → 预制体上的 BuildingTraining.Troops。
        /// 白名单留空时，敌方 AI 能出的就是这张表（再按 unlockLevel 与兵营等级过滤）。
        /// </summary>
        public List<TroopDefinition> GetBarracksTroops(string raceId, bool dark)
        {
            EnsureBuilt();

            var result = new List<TroopDefinition>();
            if (string.IsNullOrEmpty(raceId))
                return result;

            string cacheKey = raceId + (dark ? "|dark" : "|normal");
            List<TroopDefinition> cached;
            if (_barracksTroops.TryGetValue(cacheKey, out cached))
                return cached;

            RaceDefinition race = GetRace(raceId);
            if (race != null && race.Buildings != null)
            {
                BuildingType wanted = dark ? BuildingType.DarkBarracks : BuildingType.Barracks;

                for (int i = 0; i < race.Buildings.Count; i++)
                {
                    RaceBuildingEntry entry = race.Buildings[i];
                    if (entry == null || entry.buildingType != wanted || string.IsNullOrEmpty(entry.prefabKey))
                        continue;

                    GameObject prefab = GetPrefab(entry.prefabKey);
                    if (prefab == null)
                        break;

                    BuildingTraining training = prefab.GetComponent<BuildingTraining>();
                    if (training == null || training.Troops == null)
                        break;

                    for (int k = 0; k < training.Troops.Length; k++)
                    {
                        TroopDefinition troop = training.Troops[k];
                        if (troop != null && !result.Contains(troop))
                            result.Add(troop);
                    }
                    break;
                }
            }

            // 与兵营面板里的排列一致：阶级 → 解锁等级 → 名字
            result.Sort((a, b) =>
            {
                int ta = a != null ? a.Tier : 0;
                int tb = b != null ? b.Tier : 0;
                if (ta != tb) return ta.CompareTo(tb);

                int ua = a != null ? a.UnlockLevel : 0;
                int ub = b != null ? b.UnlockLevel : 0;
                if (ua != ub) return ua.CompareTo(ub);

                string na = a != null ? (string.IsNullOrEmpty(a.DisplayName) ? a.Id : a.DisplayName) : string.Empty;
                string nb = b != null ? (string.IsNullOrEmpty(b.DisplayName) ? b.Id : b.DisplayName) : string.Empty;
                return string.Compare(na, nb, StringComparison.CurrentCulture);
            });

            _barracksTroops[cacheKey] = result;
            return result;
        }

        /// <summary>
        /// 本关"敌方队伍会出的兵"清单（按队伍顺序合并、按 Id 去重），用于「按白名单自动填展示图」。
        ///
        /// 与运行时**同一套语义**，出处：TroopDefinition.cs:141-167（TroopPreloader.PreloadTeamTroops）——
        ///   · troopUnlocks 填了具体兵种 = 本局只出这些兵（按 Id 匹配、空条目忽略）→ 用白名单；
        ///   · troopUnlocks 为空（或全是空条目）= 本局全部兵种可用 → **整族预载**
        ///     → 用该种族的普通兵营 + 黑暗兵营兵种表。
        /// 多支队伍按顺序拼接，跨队伍的重复兵只保留第一次出现的那个。
        /// </summary>
        public List<TroopDefinition> GetStageTroops(StageConfig config)
        {
            var result = new List<TroopDefinition>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (config == null || config.enemyTeams == null)
                return result;

            for (int t = 0; t < config.enemyTeams.Count; t++)
            {
                EnemyTeamConfig team = config.enemyTeams[t];
                if (team == null)
                    continue;

                var candidates = new List<TroopDefinition>();
                if (team.troopUnlocks != null)
                {
                    for (int u = 0; u < team.troopUnlocks.Count; u++)
                    {
                        EnemyTroopUnlock unlock = team.troopUnlocks[u];
                        if (unlock != null && unlock.Troop != null)
                            candidates.Add(unlock.Troop);
                    }
                }

                if (candidates.Count == 0)
                {
                    // 与运行时一致：未配白名单 = 整族预载（普通兵营 + 黑暗兵营）。
                    candidates.AddRange(GetBarracksTroops(team.raceId, false));
                    candidates.AddRange(GetBarracksTroops(team.raceId, true));
                }

                for (int i = 0; i < candidates.Count; i++)
                {
                    TroopDefinition troop = candidates[i];
                    if (troop == null)
                        continue;

                    string id = string.IsNullOrEmpty(troop.Id) ? troop.name : troop.Id;
                    if (!seen.Add(id))
                        continue;

                    result.Add(troop);
                }
            }

            return result;
        }
        #endregion

        #region 另外四张注册表（音频/纹理/动画片段/动画控制器）
        /// <summary>关卡资产里那六份资源键清单分别对应哪一种注册表。</summary>
        public enum StageKeyKind
        {
            Prefab,
            Sprite,
            Audio,
            Texture,
            AnimationClip,
            AnimatorController
        }

        public const string AudioRegistryPath = "Assets/RemoteResource/AudioRegistry.asset";
        public const string TextureRegistryPath = "Assets/RemoteResource/TextureRegistry.asset";
        public const string AnimationClipRegistryPath = "Assets/RemoteResource/AnimationClipRegistry.asset";
        public const string AnimatorControllerRegistryPath = "Assets/RemoteResource/AnimatorControllerRegistry.asset";

        private readonly Dictionary<StageKeyKind, List<string>> _genericKeys = new Dictionary<StageKeyKind, List<string>>();
        private readonly Dictionary<StageKeyKind, HashSet<string>> _genericKeySets = new Dictionary<StageKeyKind, HashSet<string>>();

        public static string KindLabel(StageKeyKind kind)
        {
            switch (kind)
            {
                case StageKeyKind.Prefab: return "预制体";
                case StageKeyKind.Sprite: return "Sprite";
                case StageKeyKind.Audio: return "音频";
                case StageKeyKind.Texture: return "纹理";
                case StageKeyKind.AnimationClip: return "动画片段";
                default: return "动画控制器";
            }
        }

        /// <summary>
        /// 通用注册表读取：这四张表和 Sprite/Prefab 注册表结构一致（mappings[].key），
        /// 用 SerializedObject 按字段名读，就不需要为每张表引用一个类型。
        /// 读不到时返回空集合，调用方据此跳过校验（宁可不报，也不误报）。
        /// </summary>
        private void BuildGenericRegistries()
        {
            _genericKeys.Clear();
            _genericKeySets.Clear();

            BuildGenericRegistry(StageKeyKind.Audio, AudioRegistryPath, "AudioRegistry");
            BuildGenericRegistry(StageKeyKind.Texture, TextureRegistryPath, "TextureRegistry");
            BuildGenericRegistry(StageKeyKind.AnimationClip, AnimationClipRegistryPath, "AnimationClipRegistry");
            BuildGenericRegistry(StageKeyKind.AnimatorController, AnimatorControllerRegistryPath, "AnimatorControllerRegistry");
        }

        private void BuildGenericRegistry(StageKeyKind kind, string assetPath, string typeName)
        {
            var list = new List<string>();
            var set = new HashSet<string>(StringComparer.Ordinal);
            _genericKeys[kind] = list;
            _genericKeySets[kind] = set;

            ScriptableObject registry = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (registry == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:" + typeName);
                if (guids.Length > 0)
                    registry = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (registry == null)
                return;

            var serialized = new SerializedObject(registry);
            SerializedProperty mappings = serialized.FindProperty("mappings");
            if (mappings == null || !mappings.isArray)
                return;

            for (int i = 0; i < mappings.arraySize; i++)
            {
                SerializedProperty element = mappings.GetArrayElementAtIndex(i);
                SerializedProperty keyProp = element != null ? element.FindPropertyRelative("key") : null;
                if (keyProp == null || string.IsNullOrEmpty(keyProp.stringValue))
                    continue;

                if (set.Add(keyProp.stringValue))
                    list.Add(keyProp.stringValue);
            }

            list.Sort(StringComparer.OrdinalIgnoreCase);
        }

        public IReadOnlyList<string> KeysOf(StageKeyKind kind)
        {
            EnsureBuilt();

            if (kind == StageKeyKind.Prefab) return _prefabKeys;
            if (kind == StageKeyKind.Sprite) return _spriteKeys;

            List<string> list;
            return _genericKeys.TryGetValue(kind, out list) ? (IReadOnlyList<string>)list : new List<string>();
        }

        /// <summary>这张注册表是否读到了（读不到就不做该校验）。</summary>
        public bool HasRegistry(StageKeyKind kind)
        {
            EnsureBuilt();

            switch (kind)
            {
                case StageKeyKind.Prefab: return PrefabRegistryFound;
                case StageKeyKind.Sprite: return SpriteRegistryFound;
                default:
                    {
                        HashSet<string> set;
                        return _genericKeySets.TryGetValue(kind, out set) && set.Count > 0;
                    }
            }
        }

        public bool HasKey(StageKeyKind kind, string key)
        {
            EnsureBuilt();
            if (string.IsNullOrEmpty(key))
                return false;

            switch (kind)
            {
                case StageKeyKind.Prefab: return _prefabs.ContainsKey(key);
                case StageKeyKind.Sprite: return _sprites.ContainsKey(key);
                default:
                    {
                        HashSet<string> set;
                        return _genericKeySets.TryGetValue(kind, out set) && set.Contains(key);
                    }
            }
        }

        public List<string> SearchKeysOf(StageKeyKind kind, string query, int max = 600)
        {
            var result = new List<string>();
            IReadOnlyList<string> source = KeysOf(kind);
            string q = (query ?? string.Empty).Trim();

            for (int i = 0; i < source.Count && result.Count < max; i++)
            {
                if (q.Length == 0 || Contains(source[i], q))
                    result.Add(source[i]);
            }
            return result;
        }
        #endregion

        #region 查询
        public bool HasSprite(string key)
        {
            EnsureBuilt();
            return !string.IsNullOrEmpty(key) && _sprites.ContainsKey(key);
        }

        public bool HasPrefab(string key)
        {
            EnsureBuilt();
            return !string.IsNullOrEmpty(key) && _prefabs.ContainsKey(key);
        }

        public Sprite GetSprite(string key)
        {
            EnsureBuilt();
            Sprite sprite;
            return !string.IsNullOrEmpty(key) && _sprites.TryGetValue(key, out sprite) ? sprite : null;
        }

        public GameObject GetPrefab(string key)
        {
            EnsureBuilt();
            GameObject prefab;
            return !string.IsNullOrEmpty(key) && _prefabs.TryGetValue(key, out prefab) ? prefab : null;
        }

        public TroopDefinition GetTroop(string id)
        {
            EnsureBuilt();
            TroopDefinition troop;
            return !string.IsNullOrEmpty(id) && _troopById.TryGetValue(id, out troop) ? troop : null;
        }

        public RaceDefinition GetRace(string id)
        {
            EnsureBuilt();
            RaceDefinition race;
            return !string.IsNullOrEmpty(id) && _raceById.TryGetValue(id, out race) ? race : null;
        }

        public List<string> SearchSprites(string query, int max = 600)
        {
            return SearchKeys(_spriteKeys, query, max);
        }

        public List<string> SearchPrefabs(string query, int max = 600)
        {
            return SearchKeys(_prefabKeys, query, max);
        }

        /// <summary>兵种候选：按 显示名 / Id / 预制体键 匹配，返回的字符串是 Id（用于落库）。</summary>
        public List<TroopDefinition> SearchTroops(string query, int max = 400)
        {
            EnsureBuilt();
            var result = new List<TroopDefinition>();
            string q = (query ?? string.Empty).Trim();

            for (int i = 0; i < _troops.Count && result.Count < max; i++)
            {
                TroopDefinition troop = _troops[i];
                if (troop == null)
                    continue;

                if (q.Length == 0 ||
                    Contains(troop.Id, q) || Contains(troop.DisplayName, q) ||
                    Contains(troop.PrefabKey, q) || Contains(troop.IconKey, q))
                {
                    result.Add(troop);
                }
            }
            return result;
        }

        private static List<string> SearchKeys(List<string> source, string query, int max)
        {
            var result = new List<string>();
            string q = (query ?? string.Empty).Trim();

            for (int i = 0; i < source.Count && result.Count < max; i++)
            {
                if (q.Length == 0 || Contains(source[i], q))
                    result.Add(source[i]);
            }
            return result;
        }

        private static bool Contains(string source, string query)
        {
            return !string.IsNullOrEmpty(source) && source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }
        #endregion

        #region Addressables 上架状态（读 + 写，不引用 Addressables 程序集）
        /// <summary>
        /// 本关是否已登记进 Addressables 组 StageConfig（没登记 = 出包加载不到）。
        ///
        /// 实现方式：把组资产当普通 ScriptableObject 读它的 m_SerializeEntries[].m_Address，
        /// 这样就不需要引用 Unity.Addressables.Editor，工具也不会因为 Addressables 版本变化而编译不过。
        /// </summary>
        public bool IsStageRegistered(int stageId)
        {
            EnsureAddressables();
            return _addressableStageAddresses.Contains("Stage" + stageId);
        }

        public IReadOnlyCollection<string> RegisteredStageAddresses
        {
            get { EnsureAddressables(); return _addressableStageAddresses; }
        }

        public bool AddressableGroupFound { get; private set; }

        private void EnsureAddressables()
        {
            if (_addressableScanned)
                return;
            _addressableScanned = true;

            _addressableStageAddresses.Clear();

            ScriptableObject group = AssetDatabase.LoadAssetAtPath<ScriptableObject>(StageGroupAssetPath);
            if (group == null)
                return;

            AddressableGroupFound = true;

            var serialized = new SerializedObject(group);
            SerializedProperty entries = serialized.FindProperty("m_SerializeEntries");
            if (entries == null || !entries.isArray)
                return;

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty element = entries.GetArrayElementAtIndex(i);
                SerializedProperty address = element != null ? element.FindPropertyRelative("m_Address") : null;
                if (address != null && !string.IsNullOrEmpty(address.stringValue))
                    _addressableStageAddresses.Add(address.stringValue);
            }
        }

        public static void InvalidateAddressables()
        {
            if (_instance != null)
            {
                _instance._addressableScanned = false;
                _instance._addressableStageAddresses.Clear();
            }
        }

        /// <summary>
        /// 把某个关卡资产登记进 Addressables 组 StageConfig（= **上架**）。
        ///
        /// 地址固定 <c>Stage{stageId}</c>、标签固定 <c>StartupPreload</c>，两者都有硬依据：
        ///   · 地址：StageConfigLoader.LoadConfigs 按 $"Stage{i}" 逐个查 location，**遇到缺口直接停止加载**
        ///     （StageConfigLoader.cs:177-207）→ 少一个地址，这一关（以及它后面的关）在选关界面直接消失；
        ///   · 标签：抖音打包校验要求 StageConfig 组的每个条目都带 StartupPreload
        ///     （RemoteStartupBuildValidation.cs:74-77 + :93-95 的 RequiresPreload），
        ///     少了它打包会报「启动资源缺少 StartupPreload 标签」；RemoteStartup 也按这个标签做启动预载
        ///     （RemoteStartup.cs:15 PreloadLabel）。
        ///
        /// 实现方式与只读检查完全一致：把分组资产当普通 ScriptableObject 改它的 m_SerializeEntries，
        /// **不引用 Unity.Addressables.Editor** —— 所以 Addressables 升级不会让工具编译不过。
        /// 写入前自动备份分组资产；地址已存在时不重复插条目，只补缺失的标签。
        /// </summary>
        public static bool RegisterStage(string stageAssetPath, int stageId, out string message)
        {
            message = null;
            const string label = "StartupPreload";
            string address = "Stage" + stageId;

            if (string.IsNullOrEmpty(stageAssetPath))
            {
                message = "没有关卡资产路径，上架失败。";
                return false;
            }

            ScriptableObject group = AssetDatabase.LoadAssetAtPath<ScriptableObject>(StageGroupAssetPath);
            if (group == null)
            {
                message = "读不到分组资产：" + StageGroupAssetPath + "（Addressables 还没初始化？）";
                return false;
            }

            var serialized = new SerializedObject(group);
            SerializedProperty entries = serialized.FindProperty("m_SerializeEntries");
            if (entries == null || !entries.isArray)
            {
                message = "分组资产里没有 m_SerializeEntries 数组，上架失败。";
                return false;
            }

            // ① 地址已经在组里 → 只补标签
            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty element = entries.GetArrayElementAtIndex(i);
                SerializedProperty addressProperty = element.FindPropertyRelative("m_Address");
                if (addressProperty == null || !string.Equals(addressProperty.stringValue, address, StringComparison.Ordinal))
                    continue;

                SerializedProperty labels = element.FindPropertyRelative("m_SerializedLabels");
                if (labels != null && HasLabel(labels, label))
                {
                    message = address + " 已经登记过了（组里第 " + i + " 条），无需重复上架。";
                    return false;
                }

                StageEditorData.BackupFile(StageGroupAssetPath, "补上架标签前");
                if (labels == null)
                {
                    message = "组里第 " + i + " 条没有 m_SerializedLabels 字段，补标签失败（请在 Addressables 面板手工加 " + label + "）。";
                    return false;
                }

                AddLabel(labels, label);
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(group);
                AssetDatabase.SaveAssets();
                InvalidateAddressables();
                message = address + " 已经在组里，补上了 " + label + " 标签。";
                return true;
            }

            // ② 新插一条
            StageEditorData.BackupFile(StageGroupAssetPath, "上架前");

            int index = entries.arraySize;
            entries.arraySize = index + 1;
            SerializedProperty added = entries.GetArrayElementAtIndex(index);
            SetString(added, "m_GUID", AssetDatabase.AssetPathToGUID(stageAssetPath));
            SetString(added, "m_Address", address);
            SetBool(added, "m_ReadOnly", false);

            SerializedProperty newLabels = added.FindPropertyRelative("m_SerializedLabels");
            if (newLabels != null)
            {
                newLabels.arraySize = 1;
                newLabels.GetArrayElementAtIndex(0).stringValue = label;
            }

            SetBool(added, "FlaggedDuringContentUpdateRestriction", false);

            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(group);
            AssetDatabase.SaveAssets();
            InvalidateAddressables();

            message = "已上架：" + address + "（地址 " + address + "、标签 " + label + "，组里第 " + index + " 条）。";
            return true;
        }

        /// <summary>
        /// 把某个关卡从 Addressables 组 StageConfig 里移除（= **下架**）。
        ///
        /// 为什么需要它：删除关卡资产后，分组里那条登记会**指向一个不存在的资产** ——
        /// 出包时 Addressables 会报错、选关加载到那一格也拿不到内容。上架已经一键，下架也该一键。
        /// 同样只改分组资产的 `m_SerializeEntries`（不引用 Unity.Addressables.Editor），改前自动备份分组资产。
        /// </summary>
        public static bool UnregisterStage(int stageId, out string message)
        {
            message = null;
            string address = "Stage" + stageId;

            ScriptableObject group = AssetDatabase.LoadAssetAtPath<ScriptableObject>(StageGroupAssetPath);
            if (group == null)
            {
                message = "读不到分组资产：" + StageGroupAssetPath;
                return false;
            }

            var serialized = new SerializedObject(group);
            SerializedProperty entries = serialized.FindProperty("m_SerializeEntries");
            if (entries == null || !entries.isArray)
            {
                message = "分组资产里没有 m_SerializeEntries 数组，下架失败。";
                return false;
            }

            for (int i = 0; i < entries.arraySize; i++)
            {
                SerializedProperty element = entries.GetArrayElementAtIndex(i);
                SerializedProperty addressProperty = element.FindPropertyRelative("m_Address");
                if (addressProperty == null ||
                    !string.Equals(addressProperty.stringValue, address, StringComparison.Ordinal))
                    continue;

                StageEditorData.BackupFile(StageGroupAssetPath, "下架前");
                entries.DeleteArrayElementAtIndex(i);
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(group);
                AssetDatabase.SaveAssets();
                InvalidateAddressables();
                message = "已下架：" + address + "（从组 StageConfig 第 " + i + " 条移除）。";
                return true;
            }

            message = address + " 本来就不在组里，无需下架。";
            return false;
        }

        private static void SetString(SerializedProperty owner, string field, string value)
        {
            SerializedProperty property = owner.FindPropertyRelative(field);
            if (property != null)
                property.stringValue = value;
        }

        private static void SetBool(SerializedProperty owner, string field, bool value)
        {
            SerializedProperty property = owner.FindPropertyRelative(field);
            if (property != null)
                property.boolValue = value;
        }

        private static bool HasLabel(SerializedProperty labels, string label)
        {
            for (int i = 0; i < labels.arraySize; i++)
            {
                if (string.Equals(labels.GetArrayElementAtIndex(i).stringValue, label, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private static void AddLabel(SerializedProperty labels, string label)
        {
            int index = labels.arraySize;
            labels.arraySize = index + 1;
            labels.GetArrayElementAtIndex(index).stringValue = label;
        }
        #endregion
    }

    /// <summary>一个关卡资产的元信息（缓存用，避免每帧都去 AssetDatabase 问）。</summary>
    public sealed class StageInfo
    {
        public string Path;
        public int StageId;
        public string Name;
        public StageConfig Config;

        /// <summary>本地测试关（880+），不参与批量操作、也不要求上架。</summary>
        public bool IsLocalTest
        {
            get { return StageId >= 880; }
        }

        public string Label
        {
            get { return "Stage" + StageId; }
        }

        public string TypeLabel
        {
            get
            {
                if (Config == null)
                    return "?";
                switch (Config.stageType)
                {
                    case UserGlobalInfo.StageType.Attack: return "进攻";
                    case UserGlobalInfo.StageType.Boss: return "Boss";
                    default: return "防守";
                }
            }
        }

        /// <summary>本关的主时限（秒）：按玩法类型取对应字段。</summary>
        public float MainTimeLimit
        {
            get
            {
                if (Config == null)
                    return 0f;
                switch (Config.stageType)
                {
                    case UserGlobalInfo.StageType.Attack: return Mathf.Max(0f, Config.attackTimeLimit);
                    case UserGlobalInfo.StageType.Boss: return Mathf.Max(0f, Config.bossTimeLimit);
                    default: return Mathf.Max(0f, Config.DefenseTime);
                }
            }
        }
    }

    /// <summary>
    /// 关卡编辑会话：加载一份 StageConfig、改、保存（带工程外备份）、放弃、回滚。
    ///
    /// 写盘范围只有当前这一份 StageN.asset —— 与图鉴编辑器同一条红线。
    /// </summary>
    public sealed class StageEditorData
    {
        public const string DefaultStagesFolder = "Assets/RemoteResource/Stages";

        /// <summary>工具源码所在文件夹的默认名字（仓库根下）。改过名也没关系：会自动扫描定位。</summary>
        public const string DefaultToolFolderName = "Level Editor";

        private const string ToolMarkerFile = "LevelEditorData.cs";

        private static string _cachedToolRoot;

        public StageInfo Current { get; private set; }
        public string AssetPath { get { return Current != null ? Current.Path : null; } }
        public StageConfig Stage { get { return Current != null ? Current.Config : null; } }
        public bool IsLoaded { get { return Current != null && Current.Config != null; } }
        public bool IsDirty { get; private set; }

        /// <summary>数据变化（条目增删改）时触发，用于刷新界面。</summary>
        public event Action Changed;

        #region 工程 / 工具目录定位
        /// <summary>仓库根目录（Unity 工程的上一级）。</summary>
        public static string RepoRoot
        {
            get
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.GetFullPath(Path.Combine(projectRoot, ".."));
            }
        }

        /// <summary>
        /// 定位工具目录：先按默认名找，找不到就扫仓库根的子目录，用「里面有 LevelVisualEditor\LevelEditorData.cs」识别。
        /// 这样文件夹改名后备份目录仍然正确。
        /// </summary>
        public static string ToolRoot
        {
            get
            {
                if (!string.IsNullOrEmpty(_cachedToolRoot))
                    return _cachedToolRoot;

                string repoRoot = RepoRoot;
                string preferred = Path.Combine(repoRoot, DefaultToolFolderName);
                if (IsToolFolder(preferred))
                {
                    _cachedToolRoot = preferred;
                    return _cachedToolRoot;
                }

                try
                {
                    string[] candidates = Directory.GetDirectories(repoRoot);
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        if (IsToolFolder(candidates[i]))
                        {
                            _cachedToolRoot = candidates[i];
                            return _cachedToolRoot;
                        }
                    }
                }
                catch (Exception)
                {
                    // 扫描失败就用默认名兜底。
                }

                _cachedToolRoot = preferred;
                return _cachedToolRoot;
            }
        }

        private static bool IsToolFolder(string folder)
        {
            return !string.IsNullOrEmpty(folder) &&
                   File.Exists(Path.Combine(folder, "LevelVisualEditor", ToolMarkerFile));
        }

        /// <summary>默认备份目录：工具目录下的 StageBackups（在工程之外，不会被 Unity 导入）。</summary>
        public static string DefaultBackupFolder
        {
            get { return Path.Combine(ToolRoot, "StageBackups"); }
        }

        private static string _cachedSourceStamp;

        /// <summary>
        /// 本工具的"版本标识"：取挂载目录里最新 .cs 的修改时间。
        ///
        /// **必须缓存**：状态栏每帧都画它，而它的实现是 `AssetDatabase.FindAssets`（资源库查询）
        /// 加上每个脚本两次文件系统调用（`File.Exists` / `File.GetLastWriteTime`）。
        /// 不缓存就等于**每帧一次资源库查询 + 十几次磁盘 stat** —— 拖动 / 悬停时每秒几十次重绘，
        /// 窗口自然一顿一顿的。兵种编辑器早就踩过这个坑并修好了（"状态栏版本标识不再每帧查询"），
        /// 这里是把它找回来。
        /// </summary>
        public static string SourceStamp()
        {
            if (string.IsNullOrEmpty(_cachedSourceStamp))
                _cachedSourceStamp = ComputeSourceStamp();
            return _cachedSourceStamp;
        }

        /// <summary>重新扫描 / 重新聚焦时清一次，让状态栏的版本标识跟上。</summary>
        public static void InvalidateSourceStamp()
        {
            _cachedSourceStamp = null;
        }

        private static string ComputeSourceStamp()
        {
            try
            {
                string[] guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/Editor/StageSystem" });
                DateTime newest = DateTime.MinValue;
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (string.IsNullOrEmpty(path) || !File.Exists(path))
                        continue;

                    DateTime written = File.GetLastWriteTime(path);
                    if (written > newest)
                        newest = written;
                }

                return newest == DateTime.MinValue ? "未知" : newest.ToString("yyyy-MM-dd HH:mm");
            }
            catch (Exception)
            {
                return "未知";
            }
        }
        #endregion

        #region 发现关卡
        /// <summary>扫描工程里全部 StageConfig 资产，按关卡编号升序。</summary>
        public static List<StageInfo> DiscoverStages()
        {
            var result = new List<StageInfo>();
            string[] guids = AssetDatabase.FindAssets("t:StageConfig");

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                StageConfig config = AssetDatabase.LoadAssetAtPath<StageConfig>(path);
                if (config == null)
                    continue;

                result.Add(new StageInfo
                {
                    Path = path,
                    StageId = config.stageId,
                    Name = Path.GetFileNameWithoutExtension(path),
                    Config = config
                });
            }

            // 同名/同编号时以 Stages 目录下的为准（其余当作副本剔除，避免重复列出）
            var dedup = new Dictionary<int, StageInfo>();
            for (int i = 0; i < result.Count; i++)
            {
                StageInfo info = result[i];
                StageInfo existing;
                bool preferThis = !dedup.TryGetValue(info.StageId, out existing)
                                  || (!existing.Path.StartsWith(DefaultStagesFolder, StringComparison.OrdinalIgnoreCase)
                                      && info.Path.StartsWith(DefaultStagesFolder, StringComparison.OrdinalIgnoreCase));

                if (preferThis)
                    dedup[info.StageId] = info;
            }

            result = dedup.Values.ToList();
            result.Sort((a, b) => a.StageId.CompareTo(b.StageId));
            return result;
        }
        #endregion

        #region 加载 / 保存
        public bool Load(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            StageConfig config = AssetDatabase.LoadAssetAtPath<StageConfig>(assetPath);
            if (config == null)
                return false;

            Current = new StageInfo
            {
                Path = assetPath,
                StageId = config.stageId,
                Name = Path.GetFileNameWithoutExtension(assetPath),
                Config = config
            };
            IsDirty = false;
            LevelEditorPrefs.LastStagePath = assetPath;
            return true;
        }

        public void Unload()
        {
            Current = null;
            IsDirty = false;
        }

        /// <summary>只标脏：直接改字段（不经 SerializedObject）之后必须走这里。</summary>
        public void MarkDirty()
        {
            if (!IsLoaded)
                return;

            EditorUtility.SetDirty(Stage);
            IsDirty = true;
            RaiseChanged();
        }

        public void SaveToDisk(bool withBackup)
        {
            if (!IsLoaded)
                return;

            if (withBackup)
                BackupNow("保存前");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            IsDirty = false;
            RaiseChanged();
        }

        /// <summary>从磁盘重新加载，丢弃内存里的改动。</summary>
        public void RevertToDisk()
        {
            if (!IsLoaded)
                return;

            string path = AssetPath;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            Load(path);
            RaiseChanged();
        }
        #endregion

        #region 私有序列化字段（只能用反射写，公开字段一律直接写）
        private static readonly Dictionary<string, FieldInfo> _fieldCache = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);

        private static FieldInfo FindField(Type type, string name)
        {
            string cacheKey = type.FullName + "|" + name;
            FieldInfo field;
            if (_fieldCache.TryGetValue(cacheKey, out field) && field != null)
                return field;

            Type t = type;
            while (t != null)
            {
                field = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (field != null)
                    break;
                t = t.BaseType;
            }

            _fieldCache[cacheKey] = field;
            return field;
        }

        /// <summary>
        /// 写兵种的显示名 / 头像键（`TroopDefinition` 的字段都是 private [SerializeField]，只能反射写）。
        /// 放在数据层是因为反射 helper 在这里；界面层不直接碰反射。
        /// </summary>
        public static bool SetTroopDisplayName(TroopDefinition troop, string displayName)
        {
            if (troop == null)
                return false;

            Undo.RecordObject(troop, "修改兵种显示名");
            bool ok = SetPrivate(troop, "displayName", (displayName ?? string.Empty).Trim());
            EditorUtility.SetDirty(troop);
            return ok;
        }

        /// <summary>写兵种头像键（= 兵营/列表里显示的那个图标，`TroopDefinition.iconKey`）。</summary>
        public static bool SetTroopIconKey(TroopDefinition troop, string iconKey)
        {
            if (troop == null)
                return false;

            Undo.RecordObject(troop, "修改兵种头像");
            bool ok = SetPrivate(troop, "iconKey", (iconKey ?? string.Empty).Trim());
            EditorUtility.SetDirty(troop);
            return ok;
        }

        /// <summary>写兵种的动画贴图键（`animationSpriteKey`，局内加载用的那份）。</summary>
        public static bool SetTroopAnimationSpriteKey(TroopDefinition troop, string spriteKey)
        {
            if (troop == null)
                return false;

            Undo.RecordObject(troop, "修改兵种动画贴图");
            bool ok = SetPrivate(troop, "animationSpriteKey", (spriteKey ?? string.Empty).Trim());
            EditorUtility.SetDirty(troop);
            return ok;
        }

        /// <summary>读私有序列化字段（兵种头像键这类）。</summary>
        public static string GetTroopPrivateString(TroopDefinition troop, string field)
        {
            if (troop == null)
                return string.Empty;

            object value = GetPrivate(troop, field);
            return value as string ?? string.Empty;
        }

        private static object GetPrivate(object target, string name)
        {
            if (target == null)
                return null;
            FieldInfo field = FindField(target.GetType(), name);
            return field != null ? field.GetValue(target) : null;
        }

        private static bool SetPrivate(object target, string name, object value)
        {
            if (target == null)
                return false;
            FieldInfo field = FindField(target.GetType(), name);
            if (field == null)
                return false;

            field.SetValue(target, value);
            return true;
        }

        /// <summary>
        /// 通关奖励（victoryGoldBars / victoryDiamonds / normalVictory* / customNormalReward
        /// 都是 private [SerializeField]，只能反射写）。
        /// 普通模式默认是大师的一半；勾了 customNormalReward 就用 normalVictory* 两个自定义值。
        /// </summary>
        public void SetVictoryRewards(
            int goldBars, int diamonds,
            bool customNormal, int normalGoldBars, int normalDiamonds)
        {
            if (!IsLoaded)
                return;

            Undo.RecordObject(Stage, "修改通关奖励");
            SetPrivate(Stage, "victoryGoldBars", Mathf.Max(0, goldBars));
            SetPrivate(Stage, "victoryDiamonds", Mathf.Max(0, diamonds));
            SetPrivate(Stage, "customNormalReward", customNormal);
            SetPrivate(Stage, "normalVictoryGoldBars", Mathf.Max(0, normalGoldBars));
            SetPrivate(Stage, "normalVictoryDiamonds", Mathf.Max(0, normalDiamonds));
            MarkDirty();
        }

        public int VictoryGoldBars { get { return IsLoaded ? Stage.VictoryGoldBars : 0; } }
        public int VictoryDiamonds { get { return IsLoaded ? Stage.VictoryDiamonds : 0; } }
        public bool CustomNormalReward { get { return IsLoaded && Stage.CustomNormalReward; } }
        public int NormalVictoryGoldBars { get { return IsLoaded ? Stage.NormalVictoryGoldBars : 0; } }
        public int NormalVictoryDiamonds { get { return IsLoaded ? Stage.NormalVictoryDiamonds : 0; } }

        /// <summary>
        /// 普通模式自定义奖励的**原始输入值**（不勾自定义时也要能显示和编辑，
        /// 所以直接读字段，而不是读那个"自动取一半"的计算属性）。
        /// </summary>
        public int CustomNormalGoldBarsRaw
        {
            get
            {
                object raw = IsLoaded ? GetPrivate(Stage, "normalVictoryGoldBars") : null;
                return raw is int ? (int)raw : 0;
            }
        }

        public int CustomNormalDiamondsRaw
        {
            get
            {
                object raw = IsLoaded ? GetPrivate(Stage, "normalVictoryDiamonds") : null;
                return raw is int ? (int)raw : 0;
            }
        }

        /// <summary>把当前加载的关卡的奖励读成一套模板（「存为本类型模板」用）。</summary>
        public StageRewardTemplate ReadRewardTemplate()
        {
            return new StageRewardTemplate
            {
                Valid = true,
                GoldBars = VictoryGoldBars,
                Diamonds = VictoryDiamonds,
                CustomNormal = CustomNormalReward,
                NormalGoldBars = CustomNormalGoldBarsRaw,
                NormalDiamonds = CustomNormalDiamondsRaw
            };
        }

        /// <summary>
        /// 把一套模板写进某个关卡（新建关卡与「套用到所有同类型关」共用；静态，因为要作用于任意关卡）。
        /// 只写这 5 个奖励字段，其余字段一个不碰。
        /// </summary>
        public static void ApplyRewardTemplate(StageConfig config, StageRewardTemplate template)
        {
            if (config == null || !template.Valid)
                return;

            SetPrivateField(config, "victoryGoldBars", Mathf.Max(0, template.GoldBars));
            SetPrivateField(config, "victoryDiamonds", Mathf.Max(0, template.Diamonds));
            SetPrivateField(config, "customNormalReward", template.CustomNormal);
            SetPrivateField(config, "normalVictoryGoldBars", Mathf.Max(0, template.NormalGoldBars));
            SetPrivateField(config, "normalVictoryDiamonds", Mathf.Max(0, template.NormalDiamonds));
        }

        /// <summary>
        /// 某个关卡的奖励是否与这套模板一致（体检提示用）。
        /// 不勾自定义时只比大师值与那个开关 —— 因为此时普通模式走的是「大师的一半」，两个自定义值不参与结算。
        /// </summary>
        public static bool MatchesRewardTemplate(StageConfig config, StageRewardTemplate template)
        {
            if (config == null || !template.Valid)
                return true;

            if (config.VictoryGoldBars != Mathf.Max(0, template.GoldBars) ||
                config.VictoryDiamonds != Mathf.Max(0, template.Diamonds) ||
                config.CustomNormalReward != template.CustomNormal)
                return false;

            if (!template.CustomNormal)
                return true;

            return config.NormalVictoryGoldBars == Mathf.Max(0, template.NormalGoldBars) &&
                   config.NormalVictoryDiamonds == Mathf.Max(0, template.NormalDiamonds);
        }

        /// <summary>全局规则列表（private [SerializeField] + 只读属性），整表替换。</summary>
        public void SetGlobalRules(List<GlobalRuleDefinition> rules)
        {
            if (!IsLoaded)
                return;

            Undo.RecordObject(Stage, "修改全局规则");
            SetPrivate(Stage, "globalRules", rules ?? new List<GlobalRuleDefinition>());
            MarkDirty();
        }

        public List<GlobalRuleDefinition> GetGlobalRules()
        {
            if (!IsLoaded)
                return new List<GlobalRuleDefinition>();

            object raw = GetPrivate(Stage, "globalRules");
            var list = raw as List<GlobalRuleDefinition>;
            return list ?? new List<GlobalRuleDefinition>();
        }

        /// <summary>给批量操作等外部调用用的反射写入（只用于 private [SerializeField] 字段）。</summary>
        public static void SetPrivateField(object target, string name, object value)
        {
            SetPrivate(target, name, value);
        }

        /// <summary>某个兵种在这支队伍白名单里的下标；不在名单里返回 -1。（白名单是标记，卡片靠它决定显示"加入"还是"移出"。）</summary>
        public static int IndexOfTroop(EnemyTeamConfig team, TroopDefinition troop)
        {
            if (team == null || team.troopUnlocks == null || troop == null)
                return -1;

            for (int i = 0; i < team.troopUnlocks.Count; i++)
            {
                EnemyTroopUnlock entry = team.troopUnlocks[i];
                if (entry != null && entry.Troop == troop)
                    return i;
            }
            return -1;
        }
        /// <summary>白名单条目在队伍列表里的下标（卡片要用它来定位 / 删除，不能用值相等代替）。找不到返回 -1。</summary>
        public static int IndexOfUnlock(EnemyTeamConfig team, EnemyTroopUnlock entry)
        {
            if (team == null || team.troopUnlocks == null || entry == null)
                return -1;

            for (int i = 0; i < team.troopUnlocks.Count; i++)
            {
                if (ReferenceEquals(team.troopUnlocks[i], entry))
                    return i;
            }
            return -1;
        }
        /// <summary>敌方兵种白名单条目的两个 private 字段（troop / unlockTime）。</summary>
        public void SetTroopUnlock(EnemyTroopUnlock entry, TroopDefinition troop, float unlockTime)
        {
            if (entry == null || !IsLoaded)
                return;

            Undo.RecordObject(Stage, "修改敌方兵种白名单");
            SetPrivate(entry, "troop", troop);
            SetPrivate(entry, "unlockTime", Mathf.Max(0f, unlockTime));
            MarkDirty();
        }

        public static TroopDefinition GetTroopOf(EnemyTroopUnlock entry)
        {
            return entry != null ? entry.Troop : null;
        }

        public static float GetUnlockTimeOf(EnemyTroopUnlock entry)
        {
            return entry != null ? entry.UnlockTime : 0f;
        }
        #endregion

        #region 备份
        public string AbsoluteAssetFile()
        {
            if (string.IsNullOrEmpty(AssetPath))
                return null;

            if (Path.IsPathRooted(AssetPath))
                return AssetPath;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, AssetPath));
        }

        /// <summary>把当前资产文件复制一份到备份目录（工程之外，不污染 Unity）。</summary>
        public string BackupNow(string reason)
        {
            if (!IsLoaded)
                return null;

            return BackupFile(AssetPath, reason);
        }

        /// <summary>
        /// 按资产路径做文件级备份（不要求该关卡当前处于加载状态，批量操作用它逐关备份）。
        /// 注意：备份的是**磁盘上的文件**，所以要在修改并保存之前调用。
        /// </summary>
        public static string BackupFile(string assetPath, string reason)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string sourceFile = Path.IsPathRooted(assetPath) ? assetPath : Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            if (!File.Exists(sourceFile))
                return null;

            string folder = LevelEditorPrefs.BackupFolder;
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[关卡编辑器] 备份目录创建失败：" + e.Message);
                return null;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safeReason = Sanitize(reason);
            string baseName = Path.GetFileNameWithoutExtension(assetPath) + "_" + stamp + "_" + safeReason;
            string target = Path.Combine(folder, baseName + ".asset");

            try
            {
                int n = 2;
                while (File.Exists(target))
                {
                    target = Path.Combine(folder, baseName + "_" + n + ".asset");
                    n++;
                }

                File.Copy(sourceFile, target, false);
                TrimBackups(folder, LevelEditorPrefs.KeepBackupCount);
                return target;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[关卡编辑器] 备份失败：" + e.Message);
                return null;
            }
        }

        public static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "备份";

            var sb = new System.Text.StringBuilder(text.Length);
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool bad = false;
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (invalid[j] == c) { bad = true; break; }
                }
                sb.Append(bad ? '_' : c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 清理旧备份：**每个关卡**保留最新的 keep 份。
        ///
        /// 旧实现是"整个目录按文件名排序、删掉前 keep 个"。文件名形如
        /// `关卡名_yyyyMMdd_HHmmss_原因.asset` —— **关卡名前缀会主导排序**，时间戳根本轮不到，
        /// 于是删掉的不是最旧的，而是"名字靠前的那个关卡"的备份，并且会一路删到它一份不剩，
        /// 另一个关卡更老的备份反而留着。实测（StageBackups 的 19 份）：
        /// 会先删 2026-09-29 的 Stage1，而全目录真正最老的是 2026-09-25 的 Stage2。
        ///
        /// 第二个修正：只处理**认得出是备份**的文件（`关卡名_yyyyMMdd_HHmmss_原因.asset`）。
        /// 备份目录是可以在设置页指到任意文件夹的，万一指到 `Assets` 下面，
        /// 旧逻辑的 `*.asset` 通配删除会把**真正的关卡资产**一起删掉。认不出的一律不碰。
        ///
        /// 与建筑编辑器 / 兵种编辑器同一套做法（那边早有这个修复，这里是补上）。
        /// </summary>
        private static void TrimBackups(string folder, int keep)
        {
            if (keep <= 0)
                return;

            try
            {
                string[] files = Directory.GetFiles(folder, "*.asset");
                if (files.Length <= keep)
                    return;

                var byStage = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length; i++)
                {
                    string stageName;
                    string stamp;
                    if (!TryParseBackupName(Path.GetFileName(files[i]), out stageName, out stamp))
                        continue;   // 认不出来就不碰：可能是被指到资产目录里的真资产

                    List<string> list;
                    if (!byStage.TryGetValue(stageName, out list))
                    {
                        list = new List<string>();
                        byStage[stageName] = list;
                    }
                    list.Add(files[i]);
                }

                foreach (var pair in byStage)
                {
                    List<string> list = pair.Value;
                    if (list.Count <= keep)
                        continue;

                    // 同一个关卡分组内，前缀相同、时间戳定宽 —— 所以名字序就是时间序
                    list.Sort(StringComparer.Ordinal);
                    int removeCount = list.Count - keep;
                    for (int i = 0; i < removeCount; i++)
                        File.Delete(list[i]);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[关卡编辑器] 清理旧备份失败：" + e.Message);
            }
        }

        /// <summary>
        /// 解析备份文件名 `关卡名_yyyyMMdd_HHmmss_原因.asset`（原因后面还可能带冲突序号 `_2`）。
        /// 认不出来返回 false —— 调用方必须当成"别碰这个文件"（真正的关卡资产就是认不出来的那种）。
        /// 关卡名本身可以带下划线，所以从左边找**第一组**「8 位数字 + 6 位数字」来定位时间戳。
        /// </summary>
        private static bool TryParseBackupName(string fileName, out string stageName, out string stamp)
        {
            stageName = null;
            stamp = null;

            string name = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(name))
                return false;

            string[] parts = name.Split('_');
            if (parts.Length < 4)   // 关卡名 _ yyyyMMdd _ HHmmss _ 原因，至少 4 段
                return false;

            for (int i = 1; i < parts.Length - 2; i++)
            {
                if (parts[i].Length != 8 || parts[i + 1].Length != 6)
                    continue;
                if (!IsAllDigits(parts[i]) || !IsAllDigits(parts[i + 1]))
                    continue;

                stageName = string.Join("_", parts, 0, i);
                stamp = parts[i] + "_" + parts[i + 1];
                return true;
            }

            return false;
        }

        private static bool IsAllDigits(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                    return false;
            }
            return true;
        }

        /// <summary>
        /// 列出全部备份文件。排序是**按文件名倒序**（不是按时间）：
        /// 同一次备份的多个文件靠在一起，但不同关卡之间是按关卡名排的 ——
        /// 「恢复」菜单只取前 25 个，所以关卡多的时候后面的可能翻不到。
        /// </summary>
        public static List<string> ListBackups()
        {
            var result = new List<string>();
            string folder = LevelEditorPrefs.BackupFolder;
            if (!Directory.Exists(folder))
                return result;

            try
            {
                string[] files = Directory.GetFiles(folder, "*.asset");
                Array.Sort(files, StringComparer.Ordinal);
                for (int i = files.Length - 1; i >= 0; i--)
                    result.Add(files[i]);
            }
            catch (Exception)
            {
                // 备份目录不可读时当作没有备份。
            }
            return result;
        }

        /// <summary>用备份文件覆盖当前关卡资产（危险操作，调用方必须先确认）。</summary>
        public bool RestoreBackup(string backupFile, string targetAssetPath)
        {
            if (string.IsNullOrEmpty(backupFile) || !File.Exists(backupFile) || string.IsNullOrEmpty(targetAssetPath))
                return false;

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string target = Path.GetFullPath(Path.Combine(projectRoot, targetAssetPath));

                if (File.Exists(target))
                    BackupNow("恢复前");

                File.Copy(backupFile, target, true);
                AssetDatabase.ImportAsset(targetAssetPath, ImportAssetOptions.ForceUpdate);
                Load(targetAssetPath);
                IsDirty = false;
                RaiseChanged();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[关卡编辑器] 恢复备份失败：" + e.Message);
                return false;
            }
        }
        #endregion

        private void RaiseChanged()
        {
            Action handler = Changed;
            if (handler != null)
                handler();
        }
    }
}
