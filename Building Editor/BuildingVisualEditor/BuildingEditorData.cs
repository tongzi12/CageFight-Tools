using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CageFightBuildingTool
{
    /// <summary>
    /// 编辑器偏好（前缀 CageFight.BuildingVisualEditor.）。
    /// 与图鉴 / 兵种 / 关卡三个编辑器同一个做法：关掉窗口再打开，栏宽/页签/开关都还在。
    /// </summary>
    public static class BuildingEditorPrefs
    {
        private const string Prefix = "CageFight.BuildingVisualEditor.";

        private static string Get(string key, string fallback)
        {
            return EditorPrefs.GetString(Prefix + key, fallback);
        }

        public static string LastBuildingPath
        {
            get { return Get("LastBuildingPath", string.Empty); }
            set { EditorPrefs.SetString(Prefix + "LastBuildingPath", value ?? string.Empty); }
        }

        public static string BackupFolder
        {
            get { return Get("BackupFolder", BuildingEditorData.DefaultBackupFolder); }
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

        public static float ListWidth
        {
            get { return EditorPrefs.GetFloat(Prefix + "ListWidth", 300f); }
            set { EditorPrefs.SetFloat(Prefix + "ListWidth", value); }
        }

        public static float RightWidth
        {
            get { return EditorPrefs.GetFloat(Prefix + "RightWidth", 420f); }
            set { EditorPrefs.SetFloat(Prefix + "RightWidth", value); }
        }

        /// <summary>右栏（体检 / 设置）是否收起。收起后中栏拿到全部宽度 —— 体检面板很占地方。</summary>
        public static bool RightCollapsed
        {
            get { return EditorPrefs.GetBool(Prefix + "RightCollapsed", false); }
            set { EditorPrefs.SetBool(Prefix + "RightCollapsed", value); }
        }

        public static int RightTab
        {
            get { return EditorPrefs.GetInt(Prefix + "RightTab", 0); }
            set { EditorPrefs.SetInt(Prefix + "RightTab", value); }
        }

        /// <summary>中栏三级预览的显示方式：0=三级并排 1=只显示选中级。</summary>
        public static int PreviewMode
        {
            get { return EditorPrefs.GetInt(Prefix + "PreviewMode", 0); }
            set { EditorPrefs.SetInt(Prefix + "PreviewMode", Mathf.Clamp(value, 0, 1)); }
        }

        public static bool GroupByRace
        {
            get { return EditorPrefs.GetBool(Prefix + "GroupByRace", true); }
            set { EditorPrefs.SetBool(Prefix + "GroupByRace", value); }
        }
    }

    /// <summary>
    /// 运行时事实：界面上所有"数值 → 结论"的换算都走这里，每条注明出处。
    /// 这些是**照抄**运行时代码的公式，工具只在编辑器里用它们做推断，不参与任何运行时计算。
    /// </summary>
    public static class BuildingFacts
    {
        /// <summary>等级缩放系数 1.1^(等级-1)。出处：Runtime\Gameplay\GameObjectProperty.cs:360-372（LevelScale.PerLevel / Pow）。</summary>
        public const float LevelScalePerLevel = 1.1f;

        public static float LevelScale(int level)
        {
            return Mathf.Pow(LevelScalePerLevel, Mathf.Max(0, level - 1));
        }

        /// <summary>
        /// 建筑按哪一套等级缩放。
        /// 出处：BuildUP.cs:451-465 ResolveBuildingScaleLevel() —— 哨塔取 sentryTowerLevel、兵营取 barracksLevel、其它一律 1。
        /// 也就是说大本营这类建筑**不受等级缩放影响**。
        /// </summary>
        public static string ScaleLevelSourceLabel(bool isSentry, bool isBarracks)
        {
            if (isSentry) return "哨塔等级（prop.sentryTowerLevel）";
            if (isBarracks) return "兵营等级（prop.barracksLevel，黑暗兵营走 darkBarracksLevel）";
            return "不缩放（既不是哨塔也不是兵营，scale 恒为 1）";
        }

        /// <summary>建造费 = BuildUP.levels[0].cost。出处：BuildingPlace.cs:355-363 GetBuildCost。</summary>
        public static int BuildCost(BuildUP buildUp)
        {
            if (buildUp == null || buildUp.levels == null || buildUp.levels.Length == 0)
                return 0;
            return Mathf.Max(0, buildUp.levels[0].cost);
        }

        /// <summary>从「当前等级」升到下一级的费用；不能升级时为 0。出处：BuildUP.cs:66-79 CanUpgrade / Cost。</summary>
        public static int CurrentUpgradeCost(BuildUP buildUp)
        {
            if (buildUp == null || buildUp.levels == null || buildUp.levels.Length == 0)
                return 0;
            if (!buildUp.CanUpgrade)
                return 0;

            int level = Mathf.Clamp(buildUp.CurrentLevel, 0, buildUp.levels.Length - 1);
            if (level >= buildUp.levels.Length - 1)
                return 0;
            return Mathf.Max(0, buildUp.levels[level + 1].cost);
        }

        /// <summary>从第 level 级升到第 level+1 级的费用（编辑器里按表格逐级展示用，level 是 0 起）。</summary>
        public static int UpgradeCostAt(BuildUP buildUp, int level)
        {
            if (buildUp == null || buildUp.levels == null || buildUp.levels.Length == 0)
                return 0;

            int next = Mathf.Clamp(level, 0, buildUp.levels.Length - 1) + 1;
            if (next >= buildUp.levels.Length)
                return 0;
            return Mathf.Max(0, buildUp.levels[next].cost);
        }

        /// <summary>升到满级一共要花多少（建造 + 各次升级）。</summary>
        public static int TotalCostToMax(BuildUP buildUp)
        {
            if (buildUp == null || buildUp.levels == null || buildUp.levels.Length == 0)
                return 0;

            int total = 0;
            for (int i = 0; i < buildUp.levels.Length; i++)
                total += Mathf.Max(0, buildUp.levels[i].cost);
            return total;
        }

        /// <summary>拆除返还 = (建造费 + 已付升级费) / 2，整数除法向下取整。出处：BuildUP.cs:81-86 TotalSpent / RefundAmount。</summary>
        public static int RefundOf(int totalSpent)
        {
            return Mathf.Max(0, totalSpent / 2);
        }

        /// <summary>该级的总花费（建造 + 前几级的升级费）。用于算"到第 N 级一共花了多少、拆了退多少"。</summary>
        public static int SpentToReachLevel(BuildUP buildUp, int level)
        {
            if (buildUp == null || buildUp.levels == null || buildUp.levels.Length == 0)
                return 0;

            int total = 0;
            int last = Mathf.Clamp(level, 0, buildUp.levels.Length - 1);
            for (int i = 0; i <= last; i++)
                total += Mathf.Max(0, buildUp.levels[i].cost);
            return total;
        }

        /// <summary>
        /// 网格左下基准坐标。出处：BuildingBase.cs:184-190 GetBasePos —— (int)(pos - size/2 + 0.5)。
        /// 注意它用的是**四舍五入**，而摆建筑时用的是 FloorToInt（BuildingPlace.cs:325-334），两者差半格；
        /// 本工具只做展示，一律按 GetBasePos 的口径。
        /// </summary>
        public static Vector2Int BaseGridOf(Vector3 worldPosition, Vector2Int occupySpace)
        {
            return new Vector2Int(
                (int)(worldPosition.x - occupySpace.x / 2f + 0.5f),
                (int)(worldPosition.y - occupySpace.y / 2f + 0.5f));
        }

        /// <summary>半场分界格 = 两者中点（整数除法），奇左偶右、闭区间。出处：BuildingBase.cs:147-159。</summary>
        public static int MidpointX(int friendlyBaseX, int enemyBaseDistance)
        {
            int enemyX = friendlyBaseX + Mathf.Max(1, enemyBaseDistance);
            return (friendlyBaseX + enemyX) / 2;
        }
    }

    /// <summary>
    /// 资源索引：预制体键 / Sprite 键 / 音频键 + 种族定义 + 兵种定义。
    ///
    /// 为什么自己扫而不问 ResourceManager：它的注册表是 private 字段、没有 getter，
    /// 而 GetSprite(key) 查的是"本局已加载缓存"，编辑器里没进关卡就是空的。
    /// 所以这里直接加载注册表资产、遍历 mappings 自建索引——只读，不依赖运行场景。
    /// </summary>
    public sealed class BuildingResourceIndex
    {
        public const string PrefabRegistryPath = "Assets/RemoteResource/PrefabRegistry.asset";
        public const string SpriteRegistryPath = "Assets/RemoteResource/SpriteRegistry.asset";
        public const string AudioRegistryPath = "Assets/RemoteResource/AudioRegistry.asset";

        private static BuildingResourceIndex _instance;

        private readonly List<string> _prefabKeys = new List<string>();
        private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);

        private readonly HashSet<string> _spriteKeys = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Sprite 键 → 编辑器强引用（仅用于预览缩略图；游戏只认键）。</summary>
        private readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private readonly HashSet<string> _audioKeys = new HashSet<string>(StringComparer.Ordinal);

        private readonly List<RaceDefinition> _races = new List<RaceDefinition>();
        private readonly Dictionary<string, RaceDefinition> _raceById = new Dictionary<string, RaceDefinition>(StringComparer.OrdinalIgnoreCase);

        private readonly List<TroopDefinition> _troops = new List<TroopDefinition>();

        private bool _built;

        public bool PrefabRegistryFound { get; private set; }
        public bool SpriteRegistryFound { get; private set; }
        public bool AudioRegistryFound { get; private set; }

        public static BuildingResourceIndex Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new BuildingResourceIndex();
                    _instance.EnsureBuilt();
                }
                return _instance;
            }
        }

        public static void Invalidate()
        {
            _instance = null;
        }

        public IReadOnlyList<string> PrefabKeys { get { EnsureBuilt(); return _prefabKeys; } }
        public int SpriteKeyCount { get { EnsureBuilt(); return _spriteKeys.Count; } }
        public int AudioKeyCount { get { EnsureBuilt(); return _audioKeys.Count; } }
        public IReadOnlyList<RaceDefinition> Races { get { EnsureBuilt(); return _races; } }
        public IReadOnlyList<TroopDefinition> Troops { get { EnsureBuilt(); return _troops; } }

        private void EnsureBuilt()
        {
            if (_built)
                return;
            _built = true;
            Rebuild();
        }

        public void Rebuild()
        {
            _prefabKeys.Clear();
            _prefabs.Clear();
            _spriteKeys.Clear();
            _sprites.Clear();
            _audioKeys.Clear();
            _races.Clear();
            _raceById.Clear();
            _troops.Clear();

            PrefabRegistryFound = BuildPrefabRegistry();
            SpriteRegistryFound = BuildSpriteRegistry();
            AudioRegistryFound = BuildKeySet(AudioRegistryPath, "AudioRegistry", _audioKeys);

            BuildRaces();
            BuildTroops();

            _prefabKeys.Sort(StringComparer.OrdinalIgnoreCase);
        }

        private bool BuildPrefabRegistry()
        {
            PrefabRegistry registry = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(PrefabRegistryPath);
            if (registry == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:PrefabRegistry");
                if (guids.Length > 0)
                    registry = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (registry == null || registry.mappings == null)
                return false;

            for (int i = 0; i < registry.mappings.Count; i++)
            {
                PrefabMapping mapping = registry.mappings[i];
                if (mapping == null || string.IsNullOrEmpty(mapping.key) || _prefabs.ContainsKey(mapping.key))
                    continue;

                _prefabKeys.Add(mapping.key);
                _prefabs[mapping.key] = mapping.prefab;
            }
            return true;
        }

        /// <summary>
        /// SpriteRegistry 用**强类型**读：除了键，还要拿到编辑器强引用才能画缩略图
        /// （`SpriteMapping.sprite` 只在 UNITY_EDITOR 下存在，游戏里只认键）。
        /// </summary>
        private bool BuildSpriteRegistry()
        {
            SpriteRegistry registry = AssetDatabase.LoadAssetAtPath<SpriteRegistry>(SpriteRegistryPath);
            if (registry == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:SpriteRegistry");
                if (guids.Length > 0)
                    registry = AssetDatabase.LoadAssetAtPath<SpriteRegistry>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (registry == null || registry.mappings == null)
                return false;

            for (int i = 0; i < registry.mappings.Count; i++)
            {
                SpriteMapping mapping = registry.mappings[i];
                if (mapping == null || string.IsNullOrEmpty(mapping.key))
                    continue;

                _spriteKeys.Add(mapping.key);
                if (!_sprites.ContainsKey(mapping.key))
                    _sprites[mapping.key] = mapping.sprite;
            }
            return true;
        }

        public Sprite GetSprite(string key)
        {
            EnsureBuilt();
            Sprite sprite;
            return !string.IsNullOrEmpty(key) && _sprites.TryGetValue(key, out sprite) ? sprite : null;
        }

        /// <summary>通用注册表读取：这几张表结构一致（mappings[].key），用 SerializedObject 按字段名读。</summary>
        private bool BuildKeySet(string assetPath, string typeName, HashSet<string> target)
        {
            ScriptableObject registry = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
            if (registry == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:" + typeName);
                if (guids.Length > 0)
                    registry = AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            if (registry == null)
                return false;

            var serialized = new SerializedObject(registry);
            SerializedProperty mappings = serialized.FindProperty("mappings");
            if (mappings == null || !mappings.isArray)
                return false;

            for (int i = 0; i < mappings.arraySize; i++)
            {
                SerializedProperty element = mappings.GetArrayElementAtIndex(i);
                SerializedProperty keyProp = element != null ? element.FindPropertyRelative("key") : null;
                if (keyProp != null && !string.IsNullOrEmpty(keyProp.stringValue))
                    target.Add(keyProp.stringValue);
            }
            return true;
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

            _races.Sort((a, b) => string.Compare(a != null ? a.Id : string.Empty,
                b != null ? b.Id : string.Empty, StringComparison.OrdinalIgnoreCase));
        }

        private void BuildTroops()
        {
            string[] guids = AssetDatabase.FindAssets("t:TroopDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                TroopDefinition troop = AssetDatabase.LoadAssetAtPath<TroopDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (troop != null)
                    _troops.Add(troop);
            }

            _troops.Sort((a, b) =>
            {
                int ua = a != null ? a.UnlockLevel : 0;
                int ub = b != null ? b.UnlockLevel : 0;
                if (ua != ub) return ua.CompareTo(ub);

                string na = a != null ? (string.IsNullOrEmpty(a.DisplayName) ? a.Id : a.DisplayName) : string.Empty;
                string nb = b != null ? (string.IsNullOrEmpty(b.DisplayName) ? b.Id : b.DisplayName) : string.Empty;
                return string.Compare(na, nb, StringComparison.CurrentCulture);
            });
        }

        public bool HasPrefab(string key)
        {
            EnsureBuilt();
            return !string.IsNullOrEmpty(key) && _prefabs.ContainsKey(key);
        }

        public GameObject GetPrefab(string key)
        {
            EnsureBuilt();
            GameObject prefab;
            return !string.IsNullOrEmpty(key) && _prefabs.TryGetValue(key, out prefab) ? prefab : null;
        }

        public bool HasSprite(string key)
        {
            EnsureBuilt();
            return !string.IsNullOrEmpty(key) && _spriteKeys.Contains(key);
        }

        public bool HasAudio(string key)
        {
            EnsureBuilt();
            return !string.IsNullOrEmpty(key) && _audioKeys.Contains(key);
        }

        public RaceDefinition GetRace(string id)
        {
            EnsureBuilt();
            RaceDefinition race;
            return !string.IsNullOrEmpty(id) && _raceById.TryGetValue(id, out race) ? race : null;
        }

        public List<string> SearchPrefabs(string query, int max = 600)
        {
            var result = new List<string>();
            string q = (query ?? string.Empty).Trim();
            for (int i = 0; i < _prefabKeys.Count && result.Count < max; i++)
            {
                if (q.Length == 0 || _prefabKeys[i].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(_prefabKeys[i]);
            }
            return result;
        }

        public List<string> SearchSprites(string query, int max = 600)
        {
            var result = new List<string>();
            string q = (query ?? string.Empty).Trim();
            foreach (string key in _spriteKeys)
            {
                if (result.Count >= max)
                    break;
                if (q.Length == 0 || key.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(key);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        public List<string> SearchAudios(string query, int max = 600)
        {
            var result = new List<string>();
            string q = (query ?? string.Empty).Trim();
            foreach (string key in _audioKeys)
            {
                if (result.Count >= max)
                    break;
                if (q.Length == 0 || key.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(key);
            }
            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }
    }

    /// <summary>建筑归属：种族 × 建筑类型（来自 RaceDefinition.buildings 这张表）。</summary>
    public sealed class BuildingOwnership
    {
        public string RaceId = string.Empty;
        public BuildingType Type = BuildingType.None;
        public string PrefabKey = string.Empty;

        public string TypeLabel { get { return BuildingInfo.TypeLabelOf(Type); } }
    }

    /// <summary>一个建筑预制体 + 它身上的组件 + 它所在的种族建筑表条目。</summary>
    public sealed class BuildingInfo
    {
        public string Path;
        public GameObject Prefab;

        public BuildUP BuildUp;
        public BuildingBase Base;
        public GameObjectProperty Property;
        public BuildingTraining Training;
        public BuildingTowerAI Tower;

        /// <summary>拆除模式开关（它的 demolishPrefabKey / demolishSoundKey 是资源键）。</summary>
        public BuildingRemoveButton RemoveButton;

        /// <summary>建筑血量表现（hitSoundKey 是资源键）。</summary>
        public BuildingHealth Health;

        /// <summary>哨塔维护费（upkeepPerLevel 按 0 起等级索引）。</summary>
        public BuildingUpkeep Upkeep;

        /// <summary>所有引用了这个预制体的 (种族, 类型) 条目（同一个建筑可能被多个种族共用）。</summary>
        public readonly List<BuildingOwnership> Owners = new List<BuildingOwnership>();

        public string Name { get { return Prefab != null ? Prefab.name : Path; } }

        public bool IsBarracks { get { return Training != null; } }
        public bool IsSentry { get { return Tower != null; } }
        public bool IsMainBase
        {
            get { return Owners.Exists(o => o.Type == BuildingType.MainBase); }
        }

        /// <summary>主类型（用于分组与图标）：优先按种族建筑表里的记录，取不到就按组件推断。</summary>
        public BuildingType PrimaryType
        {
            get
            {
                for (int i = 0; i < Owners.Count; i++)
                {
                    if (Owners[i].Type != BuildingType.None)
                        return Owners[i].Type;
                }

                if (Training != null)
                    return Training.IsDarkBarracks ? BuildingType.DarkBarracks : BuildingType.Barracks;
                if (Tower != null)
                    return BuildingType.Sentry;
                return BuildingType.None;
            }
        }

        public string RaceLabel
        {
            get
            {
                if (Owners.Count == 0)
                    return "（不在任何种族建筑表里）";

                var ids = new List<string>();
                for (int i = 0; i < Owners.Count; i++)
                {
                    if (!string.IsNullOrEmpty(Owners[i].RaceId) && !ids.Contains(Owners[i].RaceId))
                        ids.Add(Owners[i].RaceId);
                }
                return ids.Count == 0 ? "（种族为空）" : string.Join(" / ", ids.ToArray());
            }
        }

        public string TypeLabel { get { return TypeLabelOf(PrimaryType); } }

        /// <summary>三级数据（BuildUP.levels）。没有 BuildUP 的建筑（如中立营地）返回空数组。</summary>
        public bool HasLevels
        {
            get { return BuildUp != null && BuildUp.levels != null && BuildUp.levels.Length > 0; }
        }

        public static string TypeLabelOf(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.Barracks: return "普通兵营";
                case BuildingType.DarkBarracks: return "黑暗兵营";
                case BuildingType.Sentry: return "哨塔";
                case BuildingType.MainBase: return "大本营";
                default: return "未分类";
            }
        }
    }

    /// <summary>
    /// 建筑编辑会话：扫描全部建筑预制体、加载一份、用 SerializedObject 改、保存（带工程外备份）、放弃。
    ///
    /// 写入范围：建筑预制体（.prefab）与种族建筑表（RaceDefinition .asset）——
    /// 这两样都是策划调数值的正常落点；**不碰任何运行时代码**。
    /// 注意：改建筑预制体是**全局**的（所有关卡、所有种族共用），工具里每一处都会写明这一点。
    /// </summary>
    public sealed class BuildingEditorData
    {
        public const string DefaultBuildFolder = "Assets/RemoteResource/Prefabricated body/Build";

        /// <summary>工具源码所在文件夹的默认名字（仓库根下）。改过名也没关系：会自动扫描定位。</summary>
        public const string DefaultToolFolderName = "Building Editor";

        private const string ToolMarkerFile = "BuildingEditorData.cs";

        private static string _cachedToolRoot;

        private readonly List<BuildingInfo> _all = new List<BuildingInfo>();
        private readonly Dictionary<string, BuildingInfo> _byPath = new Dictionary<string, BuildingInfo>(StringComparer.Ordinal);

        private SerializedObject _serialized;
        private bool _dirty;

        public BuildingInfo Current { get; private set; }
        public string AssetPath { get { return Current != null ? Current.Path : null; } }
        public GameObject Prefab { get { return Current != null ? Current.Prefab : null; } }
        public bool IsLoaded { get { return Current != null && Current.Prefab != null; } }
        public bool IsDirty { get { return _dirty; } }

        /// <summary>数据变化时触发，用于刷新界面。</summary>
        public event Action Changed;

        /// <summary>全部建筑（按 种族 → 类型 → 名字 排序）。</summary>
        public IReadOnlyList<BuildingInfo> All { get { return _all; } }

        #region 工程 / 工具目录定位
        public static string RepoRoot
        {
            get
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.GetFullPath(Path.Combine(projectRoot, ".."));
            }
        }

        /// <summary>定位工具目录：先按默认名找，找不到就扫仓库根的子目录，用「里面有 BuildingVisualEditor\BuildingEditorData.cs」识别。</summary>
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
                   File.Exists(Path.Combine(folder, "BuildingVisualEditor", ToolMarkerFile));
        }

        public static string DefaultBackupFolder
        {
            get { return Path.Combine(ToolRoot, "BuildingBackups"); }
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
                string[] guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/Editor/BuildingSystem" });
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

        #region 扫描
        /// <summary>
        /// 扫描工程里全部建筑预制体。
        ///
        /// 判定方式：预制体上挂了 BuildUP 或 BuildingBase 就算建筑
        /// （实测工程里 9 个：2 种族 × 4 类型 + 一个中立营地 Camp，它没有 BuildUP）。
        /// 同时读全部 RaceDefinition.buildings，反查出「谁在用这个建筑」。
        /// </summary>
        public void Rescan()
        {
            _all.Clear();
            _byPath.Clear();

            string[] guids = AssetDatabase.FindAssets("t:Prefab");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                BuildUP buildUp = prefab.GetComponent<BuildUP>();
                BuildingBase buildingBase = prefab.GetComponent<BuildingBase>();
                if (buildUp == null && buildingBase == null)
                    continue;

                var info = new BuildingInfo
                {
                    Path = path,
                    Prefab = prefab,
                    BuildUp = buildUp,
                    Base = buildingBase,
                    Property = prefab.GetComponent<GameObjectProperty>(),
                    Training = prefab.GetComponent<BuildingTraining>(),
                    Tower = prefab.GetComponent<BuildingTowerAI>(),
                    RemoveButton = prefab.GetComponent<BuildingRemoveButton>(),
                    Health = prefab.GetComponent<BuildingHealth>(),
                    Upkeep = prefab.GetComponent<BuildingUpkeep>()
                };

                _all.Add(info);
                _byPath[path] = info;
            }

            BuildOwnerships();

            _all.Sort((a, b) =>
            {
                int cmp = string.Compare(a.RaceLabel, b.RaceLabel, StringComparison.CurrentCulture);
                if (cmp != 0) return cmp;
                cmp = ((int)a.PrimaryType).CompareTo((int)b.PrimaryType);
                if (cmp != 0) return cmp;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
        }

        /// <summary>读全部 RaceDefinition.buildings，反查每个建筑被哪些 (种族, 类型) 用到。</summary>
        private void BuildOwnerships()
        {
            BuildingResourceIndex index = BuildingResourceIndex.Instance;

            for (int r = 0; r < index.Races.Count; r++)
            {
                RaceDefinition race = index.Races[r];
                if (race == null || race.Buildings == null)
                    continue;

                for (int i = 0; i < race.Buildings.Count; i++)
                {
                    RaceBuildingEntry entry = race.Buildings[i];
                    if (entry == null || string.IsNullOrEmpty(entry.prefabKey))
                        continue;

                    BuildingInfo info = FindByPrefabKey(entry.prefabKey);
                    if (info == null)
                        continue;

                    // 同一个 (种族, 类型) 只记一次
                    bool exists = info.Owners.Exists(o => o.RaceId == race.Id && o.Type == entry.buildingType && o.PrefabKey == entry.prefabKey);
                    if (exists)
                        continue;

                    info.Owners.Add(new BuildingOwnership
                    {
                        RaceId = race.Id,
                        Type = entry.buildingType,
                        PrefabKey = entry.prefabKey
                    });
                }
            }
        }

        /// <summary>按预制体名（= 资源键）找到对应的建筑。种族建筑表里存的是资源键，而资源键就是预制体名。</summary>
        public BuildingInfo FindByPrefabKey(string prefabKey)
        {
            if (string.IsNullOrEmpty(prefabKey))
                return null;

            for (int i = 0; i < _all.Count; i++)
            {
                if (string.Equals(_all[i].Name, prefabKey, StringComparison.OrdinalIgnoreCase))
                    return _all[i];
            }
            return null;
        }
        #endregion

        #region 加载 / 保存
        public bool Load(string assetPath)
        {
            _serialized = null;

            BuildingInfo info = null;
            if (!string.IsNullOrEmpty(assetPath))
                _byPath.TryGetValue(assetPath, out info);

            if (info == null)
            {
                Current = null;
                _dirty = false;
                return false;
            }

            Current = info;
            // 用 SerializedObject 写入 = 与 Inspector 完全一致，Ctrl+Z 原生可用。
            _serialized = new SerializedObject(info.Prefab);
            _dirty = false;
            BuildingEditorPrefs.LastBuildingPath = assetPath;
            return true;
        }

        public void Unload()
        {
            Current = null;
            _serialized = null;
            _dirty = false;
        }

        /// <summary>当前的 SerializedObject（每次写入前重新取，避免和中间改动打架）。</summary>
        public SerializedObject Serialized
        {
            get
            {
                if (Prefab == null)
                    return null;
                if (_serialized == null || _serialized.targetObject != Prefab)
                    _serialized = new SerializedObject(Prefab);
                return _serialized;
            }
        }

        /// <summary>标脏（用 SerializedObject 改完之后调）。</summary>
        public void MarkDirty()
        {
            if (!IsLoaded)
                return;

            EditorUtility.SetDirty(Prefab);
            _dirty = true;
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
            _dirty = false;
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

        public string BackupNow(string reason)
        {
            if (!IsLoaded)
                return null;
            return BackupFile(AssetPath, reason);
        }

        /// <summary>
        /// 按资产路径做文件级备份（不要求该资产当前处于加载状态，批量操作用它逐个备份）。
        /// 备份的是**磁盘上的文件**，所以要在修改并保存之前调用。
        /// </summary>
        public static string BackupFile(string assetPath, string reason)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string sourceFile = Path.IsPathRooted(assetPath) ? assetPath : Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            if (!File.Exists(sourceFile))
                return null;

            string folder = BuildingEditorPrefs.BackupFolder;
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[建筑编辑器] 备份目录创建失败：" + e.Message);
                return null;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safeReason = Sanitize(reason);
            string ext = Path.GetExtension(assetPath);
            string baseName = Path.GetFileNameWithoutExtension(assetPath) + "_" + stamp + "_" + safeReason;
            string target = Path.Combine(folder, baseName + ext);

            try
            {
                int n = 2;
                while (File.Exists(target))
                {
                    target = Path.Combine(folder, baseName + "_" + n + ext);
                    n++;
                }

                File.Copy(sourceFile, target, false);
                TrimBackups(folder, BuildingEditorPrefs.KeepBackupCount);
                return target;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[建筑编辑器] 备份失败：" + e.Message);
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

        private static void TrimBackups(string folder, int keep)
        {
            if (keep <= 0)
                return;

            try
            {
                string[] files = Directory.GetFiles(folder);
                if (files.Length <= keep)
                    return;

                // 按「资产名」分组各自保留最新 N 份 —— 一次"保存全部"会有几十个文件，
                // 全局排序删除会把刚生成的备份当场删掉（兵种编辑器踩过这个坑）。
                var byAsset = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileName(files[i]);
                    int cut = name.IndexOf('_');
                    string assetName = cut > 0 ? name.Substring(0, cut) : name;

                    List<string> list;
                    if (!byAsset.TryGetValue(assetName, out list))
                    {
                        list = new List<string>();
                        byAsset[assetName] = list;
                    }
                    list.Add(files[i]);
                }

                foreach (var pair in byAsset)
                {
                    List<string> list = pair.Value;
                    if (list.Count <= keep)
                        continue;

                    list.Sort(StringComparer.Ordinal);
                    int removeCount = list.Count - keep;
                    for (int i = 0; i < removeCount; i++)
                        File.Delete(list[i]);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[建筑编辑器] 清理旧备份失败：" + e.Message);
            }
        }

        /// <summary>列出全部备份文件（按时间倒序，最新在前）。</summary>
        public static List<string> ListBackups()
        {
            var result = new List<string>();
            string folder = BuildingEditorPrefs.BackupFolder;
            if (!Directory.Exists(folder))
                return result;

            try
            {
                string[] files = Directory.GetFiles(folder);
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

        /// <summary>用备份文件覆盖指定资产（危险操作，调用方必须先确认）。</summary>
        public static bool RestoreBackup(string backupFile, string targetAssetPath)
        {
            if (string.IsNullOrEmpty(backupFile) || !File.Exists(backupFile) || string.IsNullOrEmpty(targetAssetPath))
                return false;

            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string target = Path.GetFullPath(Path.Combine(projectRoot, targetAssetPath));

                if (File.Exists(target))
                    BackupFile(targetAssetPath, "恢复前");

                File.Copy(backupFile, target, true);
                AssetDatabase.ImportAsset(targetAssetPath, ImportAssetOptions.ForceUpdate);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[建筑编辑器] 恢复备份失败：" + e.Message);
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
