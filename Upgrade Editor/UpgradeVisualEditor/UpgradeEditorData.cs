using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace CageFightUpgradeTool
{
    /// <summary>「备份 vs 当前」逐字段对比表的一行。</summary>
    public sealed class SaveFieldRow
    {
        public string Key = string.Empty;
        public string Label = string.Empty;
        public string Before = string.Empty;
        public string After = string.Empty;
        public bool Changed;
    }

    /// <summary>一份存档备份。</summary>
    public sealed class SaveBackupInfo
    {
        public string Path = string.Empty;
        public string Name = string.Empty;
        public DateTime Time;
        public long Size;
    }

    /// <summary>
    /// 存档的定位 / 读 / 写 / 备份 / 对比。
    ///
    /// 本工具管的**只有一份文件**，而且它在 Unity 工程**之外**：
    ///   Application.persistentDataPath/SaveData/user_global_info.json
    ///   Windows Editor = %USERPROFILE%\AppData\LocalLow\JXX\CageFight\SaveData\user_global_info.json
    ///
    /// 出处（逐条核对过，不是猜的）：
    ///   · 目录名 SaveData + .json 后缀：LocalJsonSaveStorage.cs:11-29
    ///   · 文件名 user_global_info：UserGlobalInfoPersistence.cs:15（saveFileName）
    ///   · 写入时机：UserGlobalInfoPersistence.HandleChanged → SaveRoutine（数据一变，0.5 秒后回写）
    ///   · 读取时机：进游戏时读一次（UserGlobalInfoPersistence.LoadRoutine，loadStarted 只跑一次）
    ///
    /// 因此：**Unity 正在 Play 时写这份文件没有意义**（游戏内存里是旧值，之后任何一个变更都会把
    /// 旧值写回来）。界面在 Play 中会禁用保存，并说明「停止 Play 再改」。
    ///
    /// 序列化用的是运行时的 <see cref="UserGlobalInfoData"/>（同一个类型、同一份字段表），
    /// 不自己造镜像类 —— 这样游戏改了字段，工具会在编译期发现，不会悄悄写出一份缺字段的存档。
    /// </summary>
    public static class UpgradeSave
    {
        /// <summary>存档文件名（不含扩展名），与 UserGlobalInfoPersistence.saveFileName 一致。</summary>
        public const string SaveFileName = "user_global_info";

        /// <summary>存档目录名，与 LocalJsonSaveStorage.SaveDirectoryName 一致。</summary>
        public const string SaveDirName = "SaveData";

        /// <summary>本地测试关的编号起点（与关卡编辑器一致：≥ 880 视为本地测试关）。</summary>
        public const int LocalTestStageId = 880;

        public static string SaveDirectory
        {
            get { return Path.Combine(Application.persistentDataPath, SaveDirName); }
        }

        public static string SavePath
        {
            get { return Path.Combine(SaveDirectory, SaveFileName + ".json"); }
        }

        public static bool Exists
        {
            get { return File.Exists(SavePath); }
        }

        /// <summary>文件最后写入时间；文件不存在时返回 DateTime.MinValue。</summary>
        public static DateTime FileTime
        {
            get
            {
                try
                {
                    return File.Exists(SavePath) ? File.GetLastWriteTime(SavePath) : DateTime.MinValue;
                }
                catch (Exception)
                {
                    return DateTime.MinValue;
                }
            }
        }

        public static long FileSize
        {
            get
            {
                try
                {
                    return File.Exists(SavePath) ? new FileInfo(SavePath).Length : 0L;
                }
                catch (Exception)
                {
                    return 0L;
                }
            }
        }

        /// <summary>文件距上次写入过了多少秒（∞ = 文件不存在）。</summary>
        public static double FileAgeSeconds
        {
            get
            {
                DateTime t = FileTime;
                if (t == DateTime.MinValue)
                    return double.PositiveInfinity;
                return (DateTime.Now - t).TotalSeconds;
            }
        }

        #region 读
        /// <summary>
        /// 读取存档。返回值 = 是否读到了一份可解析的存档。
        /// 读不到时 <paramref name="data"/> 仍会给出一份**默认值**（等级 1/1/1、音量 1），
        /// 界面据此提供「按默认值新建存档」。
        /// </summary>
        public static bool TryLoad(out UserGlobalInfoData data, out string rawJson, out string error)
        {
            data = NewDefault();
            rawJson = null;
            error = null;

            if (!Exists)
            {
                error = "存档还不存在（没进过 Play 就没有这份文件）";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(SavePath, Encoding.UTF8);
            }
            catch (Exception exception)
            {
                error = "读取存档失败：" + exception.Message;
                return false;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "存档是空文件";
                return false;
            }

            try
            {
                // 与游戏同一条路径：先建默认对象（字段初始值生效），再覆盖有值的字段。
                // 尤其 schemaVersion：**缺失时必须从 0 起算**，不能被默认的 6 顶掉，
                // 否则老存档会跳过迁移（UserGlobalInfo.cs:437-441 就是这么写的）。
                UserGlobalInfoData candidate = NewDefault();
                candidate.schemaVersion = 0;
                JsonUtility.FromJsonOverwrite(json, candidate);

                // 版本 0/1 的货币字段叫 coinCount（UserGlobalInfo.cs:447-454）；
                // JsonUtility 读不到它，所以这里用正则补一次迁移。
                if (candidate.schemaVersion <= 1)
                {
                    int legacy;
                    if (TryReadIntField(json, "coinCount", out legacy))
                        candidate.goldBarCount = legacy;
                }

                data = candidate;
                rawJson = json;
                return true;
            }
            catch (Exception exception)
            {
                error = "JSON 解析失败：" + exception.Message;
                return false;
            }
        }

        /// <summary>一份"游戏默认值"的存档（等级 1/1/1、音量 1、伤害显示开）。</summary>
        public static UserGlobalInfoData NewDefault()
        {
            return new UserGlobalInfoData();
        }

        private static bool TryReadIntField(string json, string field, out int value)
        {
            value = 0;
            Match match = Regex.Match(json, "\"" + Regex.Escape(field) + "\"\\s*:\\s*(-?\\d+)");
            if (!match.Success)
                return false;
            return int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
        #endregion

        #region 写
        /// <summary>序列化。<paramref name="pretty"/> = 缩进多行（便于手改，也是默认）。</summary>
        public static string Serialize(UserGlobalInfoData data, bool pretty)
        {
            if (data == null)
                return string.Empty;

            // 与游戏 SerializeToJson 一致：写盘前把版本号钉到当前版本。
            data.schemaVersion = UserGlobalInfoData.CurrentSchemaVersion;
            return JsonUtility.ToJson(data, pretty);
        }

        /// <summary>
        /// 写回存档：**先备份、再整文件写入**（UTF-8 无 BOM，与游戏写出来的一致）。
        /// 写的是工程之外的一份 JSON，不触发 Unity 导入、不碰任何工程资产。
        /// </summary>
        public static bool TryWrite(string json, string reason, out string backupPath, out string error)
        {
            backupPath = null;
            error = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "内容是空的，拒绝写入";
                return false;
            }

            try
            {
                Directory.CreateDirectory(SaveDirectory);

                if (Exists)
                    backupPath = CreateBackup(reason);

                File.WriteAllText(SavePath, json, new UTF8Encoding(false));
                return true;
            }
            catch (Exception exception)
            {
                error = "写入存档失败：" + exception.Message;
                return false;
            }
        }
        #endregion

        #region 备份
        /// <summary>定时的备份文件名：user_global_info_20261009_211530_保存前.json。</summary>
        private static string BackupName(DateTime time, string reason)
        {
            string safe = string.IsNullOrEmpty(reason) ? "备份" : reason;
            foreach (char c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c, '_');

            return SaveFileName + "_" + time.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + safe + ".json";
        }

        /// <summary>把当前存档复制一份到工具目录的 SaveBackups；返回备份路径（没有存档时返回 null）。</summary>
        public static string CreateBackup(string reason)
        {
            if (!Exists)
                return null;

            try
            {
                Directory.CreateDirectory(UpgradePaths.BackupFolder);
                string target = Path.Combine(UpgradePaths.BackupFolder, BackupName(DateTime.Now, reason));
                File.Copy(SavePath, target, true);
                TrimBackups(UpgradePaths.BackupFolder, UpgradeEditorPrefs.KeepBackupCount);
                return target;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[等级编辑器] 备份存档失败：" + exception.Message);
                return null;
            }
        }

        /// <summary>按时间倒序列出备份。</summary>
        public static List<SaveBackupInfo> ListBackups()
        {
            var list = new List<SaveBackupInfo>();
            string folder = UpgradePaths.BackupFolder;
            if (!Directory.Exists(folder))
                return list;

            try
            {
                string[] files = Directory.GetFiles(folder, "*.json");
                for (int i = 0; i < files.Length; i++)
                {
                    var info = new FileInfo(files[i]);
                    list.Add(new SaveBackupInfo
                    {
                        Path = info.FullName,
                        Name = info.Name,
                        Time = info.LastWriteTime,
                        Size = info.Length
                    });
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[等级编辑器] 读取备份目录失败：" + exception.Message);
                return list;
            }

            list.Sort((a, b) => b.Time.CompareTo(a.Time));
            return list;
        }

        /// <summary>只保留最新 keep 份（按写入时间）。</summary>
        private static void TrimBackups(string folder, int keep)
        {
            if (keep <= 0)
                return;

            try
            {
                string[] files = Directory.GetFiles(folder, "*.json");
                if (files.Length <= keep)
                    return;

                var infos = new List<FileInfo>();
                for (int i = 0; i < files.Length; i++)
                    infos.Add(new FileInfo(files[i]));

                infos.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                for (int i = keep; i < infos.Count; i++)
                    infos[i].Delete();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[等级编辑器] 清理旧备份失败：" + exception.Message);
            }
        }

        /// <summary>把某份备份读成存档对象（用于对比或恢复）。</summary>
        public static bool TryReadBackup(string backupPath, out UserGlobalInfoData data, out string error)
        {
            data = null;
            error = null;

            try
            {
                string json = File.ReadAllText(backupPath, Encoding.UTF8);
                UserGlobalInfoData candidate = NewDefault();
                candidate.schemaVersion = 0;
                JsonUtility.FromJsonOverwrite(json, candidate);
                data = candidate;
                return true;
            }
            catch (Exception exception)
            {
                error = "读取备份失败：" + exception.Message;
                return false;
            }
        }

        /// <summary>用某份备份覆盖当前存档（覆盖前也会备份一次现在的）。</summary>
        public static bool TryRestore(string backupPath, out string savedBackup, out string error)
        {
            savedBackup = null;
            error = null;

            try
            {
                if (!File.Exists(backupPath))
                {
                    error = "备份文件不存在：" + backupPath;
                    return false;
                }

                Directory.CreateDirectory(SaveDirectory);
                if (Exists)
                    savedBackup = CreateBackup("恢复前");

                File.Copy(backupPath, SavePath, true);
                return true;
            }
            catch (Exception exception)
            {
                error = "恢复备份失败：" + exception.Message;
                return false;
            }
        }
        #endregion

        #region 对比
        /// <summary>
        /// 两份存档逐字段对比（用于「备份 vs 当前」）。
        /// 字段表**写在这里而不是反射**：这样顺序稳定、名字是中文、含义一栏有出处，
        /// 也顺带成为这份存档结构的一份人工清单。
        /// </summary>
        public static List<SaveFieldRow> Diff(UserGlobalInfoData before, UserGlobalInfoData after)
        {
            var rows = new List<SaveFieldRow>();
            if (before == null || after == null)
                return rows;

            AddRow(rows, "schemaVersion", "存档版本", before.schemaVersion, after.schemaVersion);
            AddRow(rows, "barracksLevel", "兵营等级", before.barracksLevel, after.barracksLevel);
            AddRow(rows, "darkBarracksLevel", "黑暗兵营等级", before.darkBarracksLevel, after.darkBarracksLevel);
            AddRow(rows, "sentryTowerLevel", "哨塔等级", before.sentryTowerLevel, after.sentryTowerLevel);
            AddRow(rows, "attackMagicLevel", "攻击魔法等级", before.attackMagicLevel, after.attackMagicLevel);
            AddRow(rows, "defenseMagicLevel", "防御魔法等级", before.defenseMagicLevel, after.defenseMagicLevel);
            AddRow(rows, "goldBarCount", "金条", before.goldBarCount, after.goldBarCount);
            AddRow(rows, "diamondCount", "钻石", before.diamondCount, after.diamondCount);
            AddRow(rows, "unlockedStage", "已解锁关卡", before.unlockedStage, after.unlockedStage);
            AddRow(rows, "volume", "音量", before.volume, after.volume);
            AddRow(rows, "showDamage", "显示伤害数字", before.showDamage, after.showDamage);
            AddRow(rows, "selectedEngineerId", "出战工程师", before.selectedEngineerId, after.selectedEngineerId);
            AddRow(rows, "selectedRaceId", "出战种族", before.selectedRaceId, after.selectedRaceId);
            AddRow(rows, "selectedSpellSlot1Id", "法术槽 1", before.selectedSpellSlot1Id, after.selectedSpellSlot1Id);
            AddRow(rows, "selectedSpellSlot2Id", "法术槽 2", before.selectedSpellSlot2Id, after.selectedSpellSlot2Id);
            AddRow(rows, "selectedRaceMainBasePrefabKey", "大本营预制体键", before.selectedRaceMainBasePrefabKey, after.selectedRaceMainBasePrefabKey);
            AddRow(rows, "bookUnlockedIds", "图鉴已解锁", DescribeList(before.bookUnlockedIds), DescribeList(after.bookUnlockedIds));
            AddRow(rows, "bookOwnedIds", "图鉴已拥有", DescribeList(before.bookOwnedIds), DescribeList(after.bookOwnedIds));
            AddRow(rows, "clearedNormalStageIds", "普通模式已通关", DescribeList(before.clearedNormalStageIds), DescribeList(after.clearedNormalStageIds));
            AddRow(rows, "clearedMasterStageIds", "大师模式已通关", DescribeList(before.clearedMasterStageIds), DescribeList(after.clearedMasterStageIds));
            return rows;
        }

        private static void AddRow(List<SaveFieldRow> rows, string key, string label, string before, string after)
        {
            rows.Add(new SaveFieldRow
            {
                Key = key,
                Label = label,
                Before = before,
                After = after,
                Changed = !string.Equals(before, after, StringComparison.Ordinal)
            });
        }

        private static void AddRow(List<SaveFieldRow> rows, string key, string label, int before, int after)
        {
            AddRow(rows, key, label, before.ToString(CultureInfo.InvariantCulture), after.ToString(CultureInfo.InvariantCulture));
        }

        private static void AddRow(List<SaveFieldRow> rows, string key, string label, uint before, uint after)
        {
            AddRow(rows, key, label, before.ToString(CultureInfo.InvariantCulture), after.ToString(CultureInfo.InvariantCulture));
        }

        private static void AddRow(List<SaveFieldRow> rows, string key, string label, float before, float after)
        {
            AddRow(rows, key, label,
                before.ToString("0.###", CultureInfo.InvariantCulture),
                after.ToString("0.###", CultureInfo.InvariantCulture));
        }

        private static void AddRow(List<SaveFieldRow> rows, string key, string label, bool before, bool after)
        {
            AddRow(rows, key, label, before ? "开" : "关", after ? "开" : "关");
        }

        /// <summary>列表类字段的紧凑描述：共 N 个（a, b, c…）。</summary>
        public static string DescribeList(List<string> list)
        {
            if (list == null || list.Count == 0)
                return "（空）";

            var builder = new StringBuilder();
            builder.Append("共 ").Append(list.Count).Append(" 个（");
            int show = Mathf.Min(6, list.Count);
            for (int i = 0; i < show; i++)
            {
                if (i > 0)
                    builder.Append(", ");
                builder.Append(list[i]);
            }
            if (list.Count > show)
                builder.Append(", …");
            builder.Append("）");
            return builder.ToString();
        }

        /// <summary>列表类字段的紧凑描述（int 版）。</summary>
        public static string DescribeList(List<int> list)
        {
            if (list == null || list.Count == 0)
                return "（空）";

            var builder = new StringBuilder();
            builder.Append("共 ").Append(list.Count).Append(" 个（");
            int show = Mathf.Min(10, list.Count);
            for (int i = 0; i < show; i++)
            {
                if (i > 0)
                    builder.Append(", ");
                builder.Append(list[i].ToString(CultureInfo.InvariantCulture));
            }
            if (list.Count > show)
                builder.Append(", …");
            builder.Append("）");
            return builder.ToString();
        }
        #endregion
    }

    /// <summary>工具目录定位（备份放这里 —— 在工程之外，不会被 Unity 导入）。</summary>
    public static class UpgradePaths
    {
        /// <summary>工具源码所在文件夹的默认名字（仓库根下）。改过名也没关系：会自动扫描定位。</summary>
        public const string DefaultToolFolderName = "Upgrade Editor";

        private const string ToolMarkerFile = "UpgradeEditorData.cs";

        private static string _cachedToolRoot;

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
        /// 定位工具目录：先按默认名找，找不到就扫仓库根的子目录，
        /// 用「里面有 UpgradeVisualEditor\UpgradeEditorData.cs」识别 —— 文件夹改名后备份目录仍然正确。
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
                   File.Exists(Path.Combine(folder, "UpgradeVisualEditor", ToolMarkerFile));
        }

        /// <summary>备份目录：工具目录下的 SaveBackups。</summary>
        public static string BackupFolder
        {
            get { return Path.Combine(ToolRoot, "SaveBackups"); }
        }
    }

    /// <summary>窗口状态（EditorPrefs，前缀 CageFight.UpgradeVisualEditor.）。</summary>
    public static class UpgradeEditorPrefs
    {
        private const string Prefix = "CageFight.UpgradeVisualEditor.";

        public static float LeftWidth
        {
            get { return EditorPrefs.GetFloat(Prefix + "LeftWidth", 340f); }
            set { EditorPrefs.SetFloat(Prefix + "LeftWidth", value); }
        }

        public static bool LeftCollapsed
        {
            get { return EditorPrefs.GetBool(Prefix + "LeftCollapsed", false); }
            set { EditorPrefs.SetBool(Prefix + "LeftCollapsed", value); }
        }

        public static int RightTab
        {
            get { return EditorPrefs.GetInt(Prefix + "RightTab", 0); }
            set { EditorPrefs.SetInt(Prefix + "RightTab", value); }
        }

        /// <summary>备份保留份数（每次写盘前都会备份一份）。</summary>
        public static int KeepBackupCount
        {
            get { return EditorPrefs.GetInt(Prefix + "KeepBackupCount", 40); }
            set { EditorPrefs.SetInt(Prefix + "KeepBackupCount", value); }
        }

        /// <summary>写盘时是否缩进（true = 多行，便于手工查看；false = 与游戏写入的单行格式一致）。</summary>
        public static bool PrettyPrint
        {
            get { return EditorPrefs.GetBool(Prefix + "PrettyPrint", true); }
            set { EditorPrefs.SetBool(Prefix + "PrettyPrint", value); }
        }

        public static string BookSearch
        {
            get { return EditorPrefs.GetString(Prefix + "BookSearch", string.Empty); }
            set { EditorPrefs.SetString(Prefix + "BookSearch", value ?? string.Empty); }
        }

        public static bool ShowUnknownBookIds
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowUnknownBookIds", true); }
            set { EditorPrefs.SetBool(Prefix + "ShowUnknownBookIds", value); }
        }
    }

    /// <summary>
    /// 界面下拉选项：**只读地**从工程里枚举定义资产（不写、不改任何工程文件）。
    /// 每个来源都写了出处，方便和运行时对照。
    /// </summary>
    public static class UpgradeOptions
    {
        private static List<string> _engineers;
        private static List<string> _races;
        private static List<string> _spells;
        private static List<string> _mainBaseKeys;
        private static List<string> _bookIds;
        private static List<int> _stageIds;

        /// <summary>工程师可选项：Assets/RemoteResource/LoadoutDefinitions/*.asset（EngineerDefinition.Id）。</summary>
        public static List<string> Engineers
        {
            get
            {
                if (_engineers == null)
                    _engineers = CollectIds<EngineerDefinition>(d => d.Id);
                return _engineers;
            }
        }

        /// <summary>种族可选项：LoadoutDefinitions/Race/*.asset（RaceDefinition.Id）。</summary>
        public static List<string> Races
        {
            get
            {
                if (_races == null)
                    _races = CollectIds<RaceDefinition>(d => d.Id);
                return _races;
            }
        }

        /// <summary>法术可选项：LoadoutDefinitions/Magic/*.asset（SpellDefinition.Id）。</summary>
        public static List<string> Spells
        {
            get
            {
                if (_spells == null)
                    _spells = CollectIds<SpellDefinition>(d => d.Id);
                return _spells;
            }
        }

        /// <summary>
        /// 大本营预制体键候选：所有 RaceDefinition.buildings 里 MainBase 条目的 prefabKey。
        /// 这个键是**进关时我方大本营用的预制体**（LoadingState.cs:84-87 → 未预载会让整关实例化失败），
        /// 所以只给"工程里真实挂着的那些键"，避免手输错一个字符就进不去关。
        /// </summary>
        public static List<string> MainBaseKeys
        {
            get
            {
                if (_mainBaseKeys != null)
                    return _mainBaseKeys;

                var keys = new List<string>();
                var races = new List<RaceDefinition>();
                CollectAssets(races);
                for (int i = 0; i < races.Count; i++)
                {
                    RaceDefinition race = races[i];
                    if (race == null || race.Buildings == null)
                        continue;

                    for (int b = 0; b < race.Buildings.Count; b++)
                    {
                        RaceBuildingEntry entry = race.Buildings[b];
                        if (entry == null || entry.buildingType != BuildingType.MainBase)
                            continue;
                        if (string.IsNullOrEmpty(entry.prefabKey) || keys.Contains(entry.prefabKey))
                            continue;
                        keys.Add(entry.prefabKey);
                    }
                }

                keys.Sort(StringComparer.OrdinalIgnoreCase);
                _mainBaseKeys = keys;
                return _mainBaseKeys;
            }
        }

        /// <summary>图鉴条目 id：BookCatalog 资产里的全部 BookEntry.id。</summary>
        public static List<string> BookIds
        {
            get
            {
                if (_bookIds != null)
                    return _bookIds;

                var ids = new List<string>();
                var catalogs = new List<BookCatalog>();
                CollectAssets(catalogs);
                for (int i = 0; i < catalogs.Count; i++)
                {
                    BookCatalog catalog = catalogs[i];
                    if (catalog == null || catalog.Entries == null)
                        continue;

                    for (int e = 0; e < catalog.Entries.Count; e++)
                    {
                        BookEntry entry = catalog.Entries[e];
                        if (entry == null || string.IsNullOrEmpty(entry.id) || ids.Contains(entry.id))
                            continue;
                        ids.Add(entry.id);
                    }
                }

                ids.Sort(StringComparer.OrdinalIgnoreCase);
                _bookIds = ids;
                return _bookIds;
            }
        }

        /// <summary>工程里的关卡编号（所有 StageConfig 资产，升序）。</summary>
        public static List<int> StageIds
        {
            get
            {
                if (_stageIds != null)
                    return _stageIds;

                var ids = new List<int>();
                var configs = new List<StageConfig>();
                CollectAssets(configs);
                for (int i = 0; i < configs.Count; i++)
                {
                    StageConfig config = configs[i];
                    if (config == null || config.stageId <= 0 || ids.Contains(config.stageId))
                        continue;
                    ids.Add(config.stageId);
                }

                ids.Sort();
                _stageIds = ids;
                return _stageIds;
            }
        }

        /// <summary>工程资产变了（导入 / 删除）之后清缓存。</summary>
        public static void Invalidate()
        {
            _engineers = null;
            _races = null;
            _spells = null;
            _mainBaseKeys = null;
            _bookIds = null;
            _stageIds = null;
        }

        private static List<string> CollectIds<T>(Func<T, string> idSelector) where T : UnityEngine.Object
        {
            var list = new List<string>();
            var assets = new List<T>();
            CollectAssets(assets);

            for (int i = 0; i < assets.Count; i++)
            {
                string id = idSelector(assets[i]);
                if (string.IsNullOrEmpty(id) || list.Contains(id))
                    continue;
                list.Add(id);
            }

            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private static void CollectAssets<T>(List<T> output) where T : UnityEngine.Object
        {
            string[] guids;
            try
            {
                guids = AssetDatabase.FindAssets("t:" + typeof(T).Name);
            }
            catch (Exception)
            {
                return;
            }

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                    continue;

                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                    output.Add(asset);
            }
        }
    }
}
