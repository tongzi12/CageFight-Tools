using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightUpgradeTool
{
    public enum UpgradeIssueLevel
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>一条体检结论。Code 是稳定标识（便于以后按代码屏蔽某条规则）。</summary>
    public sealed class UpgradeIssue
    {
        public UpgradeIssueLevel Level;
        public string Code = string.Empty;
        public string Title = string.Empty;
        public string Detail = string.Empty;

        /// <summary>定位用：点「定位」跳到哪个页签。</summary>
        public string Tab = string.Empty;

        public bool Fixable;
        public string SuggestedValue;

        /// <summary>一键修复的执行体（就地修改内存里的存档；写盘仍由用户点保存）。</summary>
        public Action<UserGlobalInfoData> Fix;

        public Color LevelColor
        {
            get
            {
                switch (Level)
                {
                    case UpgradeIssueLevel.Error: return UpgradeEditorUI.Error;
                    case UpgradeIssueLevel.Warning: return UpgradeEditorUI.Warn;
                    default: return UpgradeEditorUI.Muted;
                }
            }
        }

        public string LevelLabel
        {
            get
            {
                switch (Level)
                {
                    case UpgradeIssueLevel.Error: return "[错误]";
                    case UpgradeIssueLevel.Warning: return "[警告]";
                    default: return "[提示]";
                }
            }
        }
    }

    /// <summary>页签名字（体检的「定位」按钮与主窗口共用同一份常量，避免对不上）。</summary>
    public static class UpgradeTab
    {
        public const string Level = "等级与货币";
        public const string Loadout = "出战选择";
        public const string Book = "图鉴与进度";
        public const string Backup = "备份与对比";

        public static readonly string[] All = { Level, Loadout, Book, Backup };
    }

    /// <summary>
    /// 存档体检：把"策划看不出来、但进游戏会出问题"的配置挑出来。
    ///
    /// 每条规则都对着运行时的**同一条校验**写的，注释里给出出处。
    /// 最要紧的一类规则叫「**整份存档失效**」：UserGlobalInfo.TryValidate 是整体校验，
    /// 任何一条不过 → TryDeserializeFromJson 返回 false → 进游戏时存档**被丢弃**，
    /// 内存里退回默认值（等级 1/1/1、货币 0、图鉴与通关进度全空），
    /// 之后任何一个变更都会把这份"默认值"写回文件 —— 等于存档被清空。
    /// </summary>
    public static class UpgradeValidator
    {
        /// <summary>
        /// 选装 ID 的长度上限。出处：UserGlobalInfo.cs:14 的 private const MaxLoadoutIdLength = 128
        /// （私有常量，值抄过来；超过它 AreLoadoutIdsValid 会失败 → 同样是"整份存档失效"）。
        /// </summary>
        public const int MaxLoadoutIdLength = 128;

        /// <summary>校验一份存档。<paramref name="fileExists"/> = 磁盘上是否真的有这份存档。</summary>
        public static void Validate(UserGlobalInfoData data, bool fileExists, List<UpgradeIssue> output)
        {
            if (data == null || output == null)
                return;

            ValidateFileState(fileExists, output);
            ValidateLevels(data, output);
            ValidateCurrency(data, output);
            ValidateSettings(data, output);
            ValidateLoadout(data, output);
            ValidateBook(data, output);
            ValidateProgress(data, output);
        }

        #region 存档文件本身
        private static void ValidateFileState(bool fileExists, List<UpgradeIssue> output)
        {
            if (!fileExists)
            {
                var issue = New(UpgradeIssueLevel.Info, "save.missing", "还没有存档文件",
                    "「" + UpgradeSave.SavePath + "」不存在 —— 没进过 Play 就没有这份文件。" +
                    "可以点「一键修复」按游戏默认值新建一份（等级 1/1/1、音量 1、伤害显示开）。");
                issue.Tab = UpgradeTab.Level;
                issue.Fixable = true;
                issue.SuggestedValue = "按游戏默认值新建存档";
                issue.Fix = d => { /* 默认值本来就在内存里，写盘即新建 */ };
                output.Add(issue);
                return;
            }

            if (EditorApplication.isPlaying)
            {
                output.Add(New(UpgradeIssueLevel.Warning, "save.playing", "Unity 正在 Play：现在写盘不会生效",
                    "游戏在进入关卡/菜单时**只读一次**存档（UserGlobalInfoPersistence.LoadRoutine，loadStarted 只跑一次），" +
                    "而运行中有任何数据变更就会在 0.5 秒后把**内存里的旧值**写回文件（HandleChanged → SaveRoutine）。" +
                    "所以 Play 中改这份文件：① 界面上看不到变化，② 很快被游戏覆盖。请先停止 Play 再改。"));
            }
            else
            {
                double age = UpgradeSave.FileAgeSeconds;
                if (age < 60.0)
                {
                    output.Add(New(UpgradeIssueLevel.Warning, "save.recentWrite", "存档刚被写过（" + Mathf.RoundToInt((float)age) + " 秒前）",
                        "这份文件 60 秒内被写过 —— 可能刚从 Play 里退出、也可能**游戏还在跑**。" +
                        "如果 Unity 正在 Play，你写的值会被游戏在 0.5 秒后覆盖回旧值。"));
                }
            }
        }
        #endregion

        #region 等级（本工具的主角）
        private static void ValidateLevels(UserGlobalInfoData data, List<UpgradeIssue> output)
        {
            bool tooLow =
                data.barracksLevel < 1 || data.darkBarracksLevel < 1 || data.sentryTowerLevel < 1;
            if (tooLow)
            {
                var issue = New(UpgradeIssueLevel.Error, "level.tooLow", "建筑等级小于 1：整份存档会被游戏判为无效",
                    "当前 兵营 " + data.barracksLevel + " / 黑暗兵营 " + data.darkBarracksLevel +
                    " / 哨塔 " + data.sentryTowerLevel + "。" +
                    "游戏校验要求这三个等级 **≥ 1**（UserGlobalInfo.TryValidate → barracksLevel/darkBarracksLevel/sentryTowerLevel 不能小于 1），" +
                    "任何一条不过，TryDeserializeFromJson 就返回 false —— 进游戏时**这份存档会被整个丢弃**，" +
                    "内存退回默认值（等级 1/1/1、货币 0、图鉴与通关进度全空），之后任一变更还会把这些默认值写回文件。");
                issue.Tab = UpgradeTab.Level;
                issue.Fixable = true;
                issue.SuggestedValue = "把三个建筑等级都夹到 1";
                issue.Fix = d =>
                {
                    d.barracksLevel = Mathf.Max(1, d.barracksLevel);
                    d.darkBarracksLevel = Mathf.Max(1, d.darkBarracksLevel);
                    d.sentryTowerLevel = Mathf.Max(1, d.sentryTowerLevel);
                };
                output.Add(issue);
            }

            if (data.attackMagicLevel < 0 || data.defenseMagicLevel < 0)
            {
                var issue = New(UpgradeIssueLevel.Error, "level.magicNegative", "魔法等级是负数：整份存档会被游戏判为无效",
                    "当前 攻击魔法 " + data.attackMagicLevel + " / 防御魔法 " + data.defenseMagicLevel + "。" +
                    "游戏校验要求它们 **≥ 0**（UserGlobalInfo.TryValidate），否则整份存档被丢弃（后果同上一条）。");
                issue.Tab = UpgradeTab.Level;
                issue.Fixable = true;
                issue.SuggestedValue = "把两个魔法等级夹到 0";
                issue.Fix = d =>
                {
                    d.attackMagicLevel = Mathf.Max(0, d.attackMagicLevel);
                    d.defenseMagicLevel = Mathf.Max(0, d.defenseMagicLevel);
                };
                output.Add(issue);
            }

            if (data.attackMagicLevel == 0 || data.defenseMagicLevel == 0)
            {
                output.Add(New(UpgradeIssueLevel.Info, "level.magicZero", "魔法等级 0 会被运行时抬到 1",
                    "注入建筑时写的是 `Mathf.Max(1, UserGlobalInfo.AttackMagicLevel/DefenseMagicLevel)`" +
                    "（StageObjectInstantiator.cs:164-165），所以 0 与 1 在这一条路上没有区别；" +
                    "面板上显示的数字仍然是 0。"));
            }
        }
        #endregion

        #region 货币
        private static void ValidateCurrency(UserGlobalInfoData data, List<UpgradeIssue> output)
        {
            if (data.goldBarCount >= 0 && data.diamondCount >= 0)
                return;

            var issue = New(UpgradeIssueLevel.Error, "currency.negative", "货币是负数：整份存档会被游戏判为无效",
                "当前 金条 " + data.goldBarCount + " / 钻石 " + data.diamondCount + "。" +
                "游戏校验要求二者 **≥ 0**（UserGlobalInfo.TryValidate），否则整份存档被丢弃（后果同等级那一条）。");
            issue.Tab = UpgradeTab.Level;
            issue.Fixable = true;
            issue.SuggestedValue = "把两种货币夹到 0";
            issue.Fix = d =>
            {
                d.goldBarCount = Mathf.Max(0, d.goldBarCount);
                d.diamondCount = Mathf.Max(0, d.diamondCount);
            };
            output.Add(issue);
        }
        #endregion

        #region 设置与版本
        private static void ValidateSettings(UserGlobalInfoData data, List<UpgradeIssue> output)
        {
            if (float.IsNaN(data.volume) || float.IsInfinity(data.volume) || data.volume < 0f || data.volume > 1f)
            {
                var issue = New(UpgradeIssueLevel.Error, "setting.volume", "音量越界：整份存档会被游戏判为无效",
                    "当前 volume = " + data.volume + "。游戏校验要求它是 0~1 之间的有效数值（UserGlobalInfo.TryValidate），" +
                    "否则整份存档被丢弃（后果同等级那一条）。");
                issue.Tab = UpgradeTab.Level;
                issue.Fixable = true;
                issue.SuggestedValue = "音量夹到 0~1（非法值按 1）";
                issue.Fix = d => d.volume = float.IsNaN(d.volume) || float.IsInfinity(d.volume) ? 1f : Mathf.Clamp01(d.volume);
                output.Add(issue);
            }

            if (data.unlockedStage < 1u)
            {
                var issue = New(UpgradeIssueLevel.Error, "stage.tooLow", "已解锁关卡小于 1：整份存档会被游戏判为无效",
                    "当前 unlockedStage = " + data.unlockedStage + "。游戏校验要求 **≥ 1**" +
                    "（UserGlobalInfo.TryValidate: 'unlockedStage 不能小于 1'），否则整份存档被丢弃（后果同等级那一条）。");
                issue.Tab = UpgradeTab.Book;
                issue.Fixable = true;
                issue.SuggestedValue = "改成 1";
                issue.Fix = d => d.unlockedStage = 1u;
                output.Add(issue);
            }

            int current = UserGlobalInfoData.CurrentSchemaVersion;
            if (data.schemaVersion > current)
            {
                var issue = New(UpgradeIssueLevel.Error, "schema.tooNew", "存档版本高于当前游戏支持的版本：游戏会拒绝加载",
                    "存档 schemaVersion = " + data.schemaVersion + "，游戏当前支持到 " + current +
                    "（UserGlobalInfoData.CurrentSchemaVersion）。TryMigrate 遇到更高的版本会直接报错" +
                    "（'存档版本 N 高于当前支持版本'）→ TryDeserializeFromJson 失败 → 存档被丢弃。");
                issue.Tab = UpgradeTab.Backup;
                issue.Fixable = true;
                issue.SuggestedValue = "把版本号改成 " + current + "（字段按当前结构解释）";
                issue.Fix = d => d.schemaVersion = UserGlobalInfoData.CurrentSchemaVersion;
                output.Add(issue);
            }
            else if (data.schemaVersion < current)
            {
                output.Add(New(UpgradeIssueLevel.Info, "schema.older", "存档是旧版本，游戏会自动迁移",
                    "存档 schemaVersion = " + data.schemaVersion + "，当前是 " + current +
                    "（UserGlobalInfo.TryMigrate 逐版本迁移；其中版本 5→6 会把三个建筑等级的 0 抬到 1）。" +
                    "本工具保存时会写成当前版本 " + current + "。"));
            }
        }
        #endregion

        #region 出战选择
        private static void ValidateLoadout(UserGlobalInfoData data, List<UpgradeIssue> output)
        {
            CheckLoadoutId(data.selectedEngineerId, "工程师", UpgradeOptions.Engineers, output, true);
            CheckLoadoutId(data.selectedRaceId, "种族", UpgradeOptions.Races, output, true);
            CheckLoadoutId(data.selectedSpellSlot1Id, "法术槽 1", UpgradeOptions.Spells, output, false);
            CheckLoadoutId(data.selectedSpellSlot2Id, "法术槽 2", UpgradeOptions.Spells, output, false);

            string key = data.selectedRaceMainBasePrefabKey;
            List<string> keys = UpgradeOptions.MainBaseKeys;
            if (string.IsNullOrWhiteSpace(key))
            {
                output.Add(New(UpgradeIssueLevel.Warning, "loadout.mainBaseEmpty", "大本营预制体键为空",
                    "关卡配了「我方大本营位置」时，运行时先按**所选种族的 Buildings 表**里 MainBase 条目解析预制体，" +
                    "只有种族没配该条目时才回落到这个键（StageObjectInstantiator.ResolveRaceMainBaseKey:203-223）。" +
                    "所以：种族的 MainBase 条目正常时这个键无所谓；一旦种族没配，运行时只会打一条警告并**跳过生成我方大本营**" +
                    "（同上 :121-127，不会阻断进关，但这一局没有基地）。"));
            }
            else if (keys.Count > 0 && !keys.Contains(key))
            {
                var issue = New(UpgradeIssueLevel.Warning, "loadout.mainBaseUnknown", "大本营预制体键不在任何种族的建筑表里",
                    "key = 「" + key + "」在工程里所有 RaceDefinition 的 MainBase 条目里都找不到。" +
                    "它现在只是「种族没配 MainBase 时的回落值」；一旦走到那一步，而该键又没被预载，" +
                    "运行时是 LogError + 返回 false → **整个关卡实例化失败（进不去关）**" +
                    "（StageObjectInstantiator.cs:129-136、LoadingState.cs:87-91）。建议直接选一个工程里真实存在的键。");
                issue.Tab = UpgradeTab.Loadout;
                issue.Fixable = false;
                output.Add(issue);
            }
        }

        /// <summary>
        /// 检查一个选装 ID：为空 / 超长 / 工程里找不到。
        /// <paramref name="emptyIsWarning"/>：工程师与种族为空是真的缺配置（警告）；
        /// **法术槽为空是合法状态**（玩家没选第二/第三个法术时它本来就是空的），所以只给提示。
        /// </summary>
        private static void CheckLoadoutId(string value, string label, List<string> options, List<UpgradeIssue> output, bool emptyIsWarning)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                output.Add(New(emptyIsWarning ? UpgradeIssueLevel.Warning : UpgradeIssueLevel.Info, "loadout.empty",
                    label + " 没选",
                    emptyIsWarning
                        ? "该项为空 → 进游戏时选装解析不到这一项（种族为空还会让大本营预制体只能靠存档里那个回落键）。"
                        : "法术槽为空是**正常状态**：玩家没解锁/没选这一格时它就是空的（PlayerLoadoutManager 按局内槽位解析，空槽位不生效）。"));
                return;
            }

            if (value.Length > MaxLoadoutIdLength)
            {
                var issue = New(UpgradeIssueLevel.Error, "loadout.tooLong", label + "的 ID 太长：整份存档会被游戏判为无效",
                    "长度 " + value.Length + " 超过上限 " + MaxLoadoutIdLength +
                    "（UserGlobalInfo.AreLoadoutIdsValid）→ 整份存档被丢弃（后果同等级那一条）。");
                issue.Fixable = true;
                issue.SuggestedValue = "截断到 " + MaxLoadoutIdLength + " 个字符";
                issue.Fix = d =>
                {
                    if (d.selectedEngineerId != null && d.selectedEngineerId.Length > MaxLoadoutIdLength) d.selectedEngineerId = d.selectedEngineerId.Substring(0, MaxLoadoutIdLength);
                    if (d.selectedRaceId != null && d.selectedRaceId.Length > MaxLoadoutIdLength) d.selectedRaceId = d.selectedRaceId.Substring(0, MaxLoadoutIdLength);
                    if (d.selectedSpellSlot1Id != null && d.selectedSpellSlot1Id.Length > MaxLoadoutIdLength) d.selectedSpellSlot1Id = d.selectedSpellSlot1Id.Substring(0, MaxLoadoutIdLength);
                    if (d.selectedSpellSlot2Id != null && d.selectedSpellSlot2Id.Length > MaxLoadoutIdLength) d.selectedSpellSlot2Id = d.selectedSpellSlot2Id.Substring(0, MaxLoadoutIdLength);
                    if (d.selectedRaceMainBasePrefabKey != null && d.selectedRaceMainBasePrefabKey.Length > MaxLoadoutIdLength) d.selectedRaceMainBasePrefabKey = d.selectedRaceMainBasePrefabKey.Substring(0, MaxLoadoutIdLength);
                };
                output.Add(issue);
                return;
            }

            if (options.Count > 0 && !options.Contains(value))
            {
                output.Add(New(UpgradeIssueLevel.Warning, "loadout.unknown", label + "「" + value + "」在工程里找不到",
                    "工程里的候选有 " + options.Count + " 个（" + UpgradeSave.DescribeList(options) + "）。" +
                    "找不到说明这个 ID 已经改了名或被删了 → 运行时选装解析不到这一项。"));
            }
        }
        #endregion

        #region 图鉴
        private static void ValidateBook(UserGlobalInfoData data, List<UpgradeIssue> output)
        {
            List<string> catalog = UpgradeOptions.BookIds;
            ReportUnknownIds(data.bookUnlockedIds, catalog, "图鉴已解锁", "book.unlockUnknown", output);
            ReportUnknownIds(data.bookOwnedIds, catalog, "图鉴已拥有", "book.ownUnknown", output);

            // 拥有但未解锁：BookCatalog 的语义是"见过=解锁、获得=拥有"，两者独立记录。
            if (data.bookOwnedIds != null && data.bookUnlockedIds != null)
            {
                var missing = new List<string>();
                for (int i = 0; i < data.bookOwnedIds.Count; i++)
                {
                    string id = data.bookOwnedIds[i];
                    if (!string.IsNullOrEmpty(id) && !data.bookUnlockedIds.Contains(id))
                        missing.Add(id);
                }

                if (missing.Count > 0)
                {
                    output.Add(New(UpgradeIssueLevel.Info, "book.ownedNotUnlocked", "有 " + missing.Count + " 个条目「已拥有但未解锁」",
                        "图鉴里解锁与拥有是两套记录（UserGlobalInfoData.bookUnlockedIds / bookOwnedIds）：" +
                        "已解锁未拥有显示为灰白头像。这些条目只在拥有列表里：" + UpgradeSave.DescribeList(missing) +
                        "。正常流程下「获得即视为解锁」（BookCatalog 注释），所以这种状态一般是改出来的。"));
                }
            }
        }

        private static void ReportUnknownIds(List<string> ids, List<string> known, string label, string code, List<UpgradeIssue> output)
        {
            if (ids == null || ids.Count == 0 || known.Count == 0)
                return;

            var unknown = new List<string>();
            for (int i = 0; i < ids.Count; i++)
            {
                string id = ids[i];
                if (!string.IsNullOrEmpty(id) && !known.Contains(id) && !unknown.Contains(id))
                    unknown.Add(id);
            }

            if (unknown.Count > 0)
            {
                output.Add(New(UpgradeIssueLevel.Warning, code, label + "里有 " + unknown.Count + " 个 ID 不在图鉴目录里",
                    "图鉴目录（BookCatalog.asset）里没有这些 ID：" + UpgradeSave.DescribeList(unknown) +
                    "。它们对应的图鉴格子永远不会亮 —— 命名改了、条目删了，或者本来就是手填进来的。工具只报告，不擅自删。"));
            }
        }
        #endregion

        #region 通关进度
        private static void ValidateProgress(UserGlobalInfoData data, List<UpgradeIssue> output)
        {
            CheckStageList(data.clearedNormalStageIds, "普通模式已通关", "progress.normal", output);
            CheckStageList(data.clearedMasterStageIds, "大师模式已通关", "progress.master", output);
        }

        private static void CheckStageList(List<int> list, string label, string code, List<UpgradeIssue> output)
        {
            if (list == null || list.Count == 0)
                return;

            var duplicates = new List<int>();
            for (int i = 0; i < list.Count; i++)
            {
                if (list.IndexOf(list[i]) != i && !duplicates.Contains(list[i]))
                    duplicates.Add(list[i]);
            }

            if (duplicates.Count > 0)
            {
                var issue = New(UpgradeIssueLevel.Warning, code + ".dup", label + "里有重复关卡编号",
                    "重复的：" + UpgradeSave.DescribeList(duplicates) + "。游戏里 IsStageCleared 只判「在不在列表里」，" +
                    "重复不影响判定，但会让「已通关数量」这类统计虚高。");
                issue.Tab = UpgradeTab.Book;
                issue.Fixable = true;
                issue.SuggestedValue = "去重";
                issue.Fix = d =>
                {
                    d.clearedNormalStageIds = Distinct(d.clearedNormalStageIds);
                    d.clearedMasterStageIds = Distinct(d.clearedMasterStageIds);
                };
                output.Add(issue);
            }

            List<int> known = UpgradeOptions.StageIds;
            if (known.Count == 0)
                return;

            var unknown = new List<int>();
            for (int i = 0; i < list.Count; i++)
            {
                if (!known.Contains(list[i]) && !unknown.Contains(list[i]))
                    unknown.Add(list[i]);
            }

            if (unknown.Count > 0)
            {
                output.Add(New(UpgradeIssueLevel.Warning, code + ".unknown", label + "里有工程里不存在的关卡编号",
                    "编号：" + UpgradeSave.DescribeList(unknown) + "。工程里现有的关卡编号：" +
                    UpgradeSave.DescribeList(known) + "。这些编号对应的关卡已经删了/改了号 —— 奖励判定用不到它们。"));
            }
        }

        private static List<int> Distinct(List<int> list)
        {
            var result = new List<int>();
            if (list == null)
                return result;

            for (int i = 0; i < list.Count; i++)
            {
                if (!result.Contains(list[i]))
                    result.Add(list[i]);
            }
            return result;
        }
        #endregion

        #region 一键修复
        /// <summary>
        /// 执行所有"可修"的修复（就地改内存存档），返回修了几处。
        /// **只做安全确定的夹取/去重/截断与补默认值**，绝不删条目、绝不动没报错的字段。
        /// </summary>
        public static int ApplySafeFixes(UserGlobalInfoData data, List<UpgradeIssue> issues)
        {
            if (data == null || issues == null)
                return 0;

            int fixedCount = 0;
            for (int i = 0; i < issues.Count; i++)
            {
                UpgradeIssue issue = issues[i];
                if (issue == null || !issue.Fixable || issue.Fix == null)
                    continue;

                issue.Fix(data);
                fixedCount++;
            }

            // 列表类字段的空引用兜底（游戏读的时候会自行建列表，但写出去 null 会让 JSON 缺字段）。
            if (data.bookUnlockedIds == null) { data.bookUnlockedIds = new List<string>(); fixedCount++; }
            if (data.bookOwnedIds == null) { data.bookOwnedIds = new List<string>(); fixedCount++; }
            if (data.clearedNormalStageIds == null) { data.clearedNormalStageIds = new List<int>(); fixedCount++; }
            if (data.clearedMasterStageIds == null) { data.clearedMasterStageIds = new List<int>(); fixedCount++; }

            return fixedCount;
        }
        #endregion

        private static UpgradeIssue New(UpgradeIssueLevel level, string code, string title, string detail)
        {
            return new UpgradeIssue
            {
                Level = level,
                Code = code,
                Title = title,
                Detail = detail,
                Tab = UpgradeTab.Level
            };
        }
    }
}
