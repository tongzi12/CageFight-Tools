using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CageFightUnitTool
{
    /// <summary>资源键种类（对应工程里的 5 张注册表）。</summary>
    public enum UnitResKind
    {
        Sprite = 0,
        Prefab = 1,
        Audio = 2,
        AnimatorController = 3,
        AnimationClip = 4
    }

    /// <summary>兵种编辑器偏好设置。</summary>
    public static class UnitEditorPrefs
    {
        private const string Prefix = "CageFight.UnitVisualEditor.";

        /// <summary>工具源码默认文件夹名（仓库根下）。改名后会自动扫描定位。</summary>
        public const string DefaultToolFolderName = "Unit Editor";

        private const string ToolMarkerFile = "UnitEditorModel.cs";

        private static string _cachedToolRoot;

        /// <summary>定位本工具所在目录（仓库根 / 工具文件夹）。改名后靠「含 UnitVisualEditor\UnitEditorModel.cs」识别。</summary>
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
                    // 扫不到就用默认名兜底。
                }

                _cachedToolRoot = preferred;
                return _cachedToolRoot;
            }
        }

        /// <summary>仓库根（Unity 工程的上一级）。</summary>
        public static string RepoRoot
        {
            get
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.GetFullPath(Path.Combine(projectRoot, ".."));
            }
        }

        private static bool IsToolFolder(string folder)
        {
            return !string.IsNullOrEmpty(folder) && File.Exists(Path.Combine(folder, "UnitVisualEditor", ToolMarkerFile));
        }

        /// <summary>默认备份目录：工具目录下的 UnitBackups（工程之外）。</summary>
        public static string DefaultBackupFolder
        {
            get { return Path.Combine(ToolRoot, "UnitBackups"); }
        }

        private static string _cachedSourceStamp;

        /// <summary>
        /// 本工具的"版本标识"：取工具源码里最新的 .cs 修改时间。
        /// 界面状态栏直接显示，省得再问"我现在跑的是哪一版"。
        /// 结果在本次域内缓存：状态栏每帧都会问一次，而 FindAssets + 每个脚本一次
        /// File.GetLastWriteTime，鼠标划过窗口就能感到掉帧。
        /// 改了工具源码会触发重新编译（域重载），静态缓存自然失效，所以不会一直显示旧版本。
        /// </summary>
        public static string SourceStamp()
        {
            if (_cachedSourceStamp == null)
                _cachedSourceStamp = ComputeSourceStamp();

            return _cachedSourceStamp;
        }

        private static string ComputeSourceStamp()
        {
            try
            {
                string[] guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/Editor/UnitSystem" });
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

        public static string BackupFolder
        {
            get { return EditorPrefs.GetString(Prefix + "BackupFolder", DefaultBackupFolder); }
            set { EditorPrefs.SetString(Prefix + "BackupFolder", value); }
        }

        /// <summary>每个资产保留的备份份数（按资产名分组清理，不是全局文件总数）。</summary>
        public static int KeepBackupCount
        {
            get { return EditorPrefs.GetInt(Prefix + "KeepBackupCount", 20); }
            set { EditorPrefs.SetInt(Prefix + "KeepBackupCount", value); }
        }

        public static bool AutoBackupOnSave
        {
            get { return EditorPrefs.GetBool(Prefix + "AutoBackupOnSave", true); }
            set { EditorPrefs.SetBool(Prefix + "AutoBackupOnSave", value); }
        }

        /// <summary>左栏列表行内是否显示训练 ID（TroopDefinition.id）。</summary>
        public static bool ShowTrainingIdInList
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowTrainingIdInList", true); }
            set { EditorPrefs.SetBool(Prefix + "ShowTrainingIdInList", value); }
        }

        public static float ListWidth
        {
            get { return EditorPrefs.GetFloat(Prefix + "ListWidth", 300f); }
            set { EditorPrefs.SetFloat(Prefix + "ListWidth", value); }
        }

        /// <summary>右栏是否收起。收起后中栏拿到全部宽度（体检面板很占地方）。</summary>
        public static bool RightCollapsed
        {
            get { return EditorPrefs.GetBool(Prefix + "RightCollapsed", false); }
            set { EditorPrefs.SetBool(Prefix + "RightCollapsed", value); }
        }

        public static float RightWidth
        {
            get { return EditorPrefs.GetFloat(Prefix + "RightWidth", 460f); }
            set { EditorPrefs.SetFloat(Prefix + "RightWidth", value); }
        }

        /// <summary>对比表排序字段（列索引）。</summary>
        public static int TableSortColumn
        {
            get { return EditorPrefs.GetInt(Prefix + "TableSortColumn", 0); }
            set { EditorPrefs.SetInt(Prefix + "TableSortColumn", value); }
        }

        public static bool TableSortAscending
        {
            get { return EditorPrefs.GetBool(Prefix + "TableSortAscending", true); }
            set { EditorPrefs.SetBool(Prefix + "TableSortAscending", value); }
        }
    }

    /// <summary>
    /// 资源键索引：把工程里 5 张注册表（Sprite / Prefab / Audio / AnimatorController / AnimationClip）
    /// 统一成「键 → 资产」查询，供下拉搜索、校验与预览。
    /// 5 张注册表的 Mapping 字段名不同，这里用 SerializedObject 统一读，避免写 5 套代码。
    /// </summary>
    public sealed class UnitResourceIndex
    {
        private sealed class KindData
        {
            public string RegistryPath;
            public string AssetField;
            public readonly List<string> Keys = new List<string>();
            public readonly Dictionary<string, UnityEngine.Object> Assets = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
            public readonly Dictionary<UnityEngine.Object, string> Reverse = new Dictionary<UnityEngine.Object, string>();
            public bool Found;
        }

        private static UnitResourceIndex _instance;

        private readonly Dictionary<UnitResKind, KindData> _kinds = new Dictionary<UnitResKind, KindData>();
        private bool _built;

        public static UnitResourceIndex Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new UnitResourceIndex();
                    _instance.Rebuild();
                }
                return _instance;
            }
        }

        public static void Invalidate()
        {
            _instance = null;
        }

        private KindData Get(UnitResKind kind)
        {
            if (!_built)
                Rebuild();

            KindData data;
            if (_kinds.TryGetValue(kind, out data))
                return data;

            data = new KindData();
            _kinds[kind] = data;
            return data;
        }

        public void Rebuild()
        {
            _built = true;
            _kinds.Clear();

            Register(UnitResKind.Sprite, "Assets/RemoteResource/SpriteRegistry.asset", "sprite");
            Register(UnitResKind.Prefab, "Assets/RemoteResource/PrefabRegistry.asset", "prefab");
            Register(UnitResKind.Audio, "Assets/RemoteResource/AudioRegistry.asset", "audioClip");
            Register(UnitResKind.AnimatorController, "Assets/RemoteResource/AnimatorControllerRegistry.asset", "animatorController");
            Register(UnitResKind.AnimationClip, "Assets/RemoteResource/AnimationClipRegistry.asset", "animationClip");
        }

        private void Register(UnitResKind kind, string registryPath, string assetField)
        {
            var data = new KindData();
            data.RegistryPath = registryPath;
            data.AssetField = assetField;
            _kinds[kind] = data;

            ScriptableObject registry = AssetDatabase.LoadAssetAtPath<ScriptableObject>(registryPath);
            data.Found = registry != null;
            if (registry == null)
                return;

            var so = new SerializedObject(registry);
            SerializedProperty mappings = so.FindProperty("mappings");
            if (mappings == null || !mappings.isArray)
                return;

            for (int i = 0; i < mappings.arraySize; i++)
            {
                SerializedProperty element = mappings.GetArrayElementAtIndex(i);
                SerializedProperty keyProp = element.FindPropertyRelative("key");
                SerializedProperty assetProp = element.FindPropertyRelative(assetField);

                string key = keyProp != null ? keyProp.stringValue : null;
                if (string.IsNullOrEmpty(key) || data.Assets.ContainsKey(key))
                    continue;

                UnityEngine.Object asset = assetProp != null ? assetProp.objectReferenceValue : null;
                data.Keys.Add(key);
                data.Assets.Add(key, asset);
                if (asset != null && !data.Reverse.ContainsKey(asset))
                    data.Reverse.Add(asset, key);
            }
        }

        public bool RegistryFound(UnitResKind kind)
        {
            return Get(kind).Found;
        }

        public string RegistryPath(UnitResKind kind)
        {
            return Get(kind).RegistryPath;
        }

        public IReadOnlyList<string> Keys(UnitResKind kind)
        {
            return Get(kind).Keys;
        }

        public int KeyCount(UnitResKind kind)
        {
            return Get(kind).Keys.Count;
        }

        public bool Has(UnitResKind kind, string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;

            return Get(kind).Assets.ContainsKey(key);
        }

        public UnityEngine.Object GetAsset(UnitResKind kind, string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;

            UnityEngine.Object asset;
            return Get(kind).Assets.TryGetValue(key, out asset) ? asset : null;
        }

        public Sprite GetSprite(string key)
        {
            return GetAsset(UnitResKind.Sprite, key) as Sprite;
        }

        public GameObject GetPrefab(string key)
        {
            return GetAsset(UnitResKind.Prefab, key) as GameObject;
        }

        public AudioClip GetAudio(string key)
        {
            return GetAsset(UnitResKind.Audio, key) as AudioClip;
        }

        public RuntimeAnimatorController GetAnimator(string key)
        {
            return GetAsset(UnitResKind.AnimatorController, key) as RuntimeAnimatorController;
        }

        public AnimationClip GetClip(string key)
        {
            return GetAsset(UnitResKind.AnimationClip, key) as AnimationClip;
        }

        /// <summary>把拖进来的资产反查成资源键。</summary>
        public bool TryGetKey(UnitResKind kind, UnityEngine.Object asset, out string key)
        {
            key = null;
            if (asset == null)
                return false;

            return Get(kind).Reverse.TryGetValue(asset, out key);
        }

        /// <summary>按关键字搜索资源键（前缀优先）。</summary>
        public List<string> Search(UnitResKind kind, string query, int max = 80)
        {
            KindData data = Get(kind);
            var result = new List<string>();

            if (string.IsNullOrEmpty(query))
            {
                for (int i = 0; i < data.Keys.Count && i < max; i++)
                    result.Add(data.Keys[i]);
                return result;
            }

            string q = query.Trim();
            for (int pass = 0; pass < 2 && result.Count < max; pass++)
            {
                for (int i = 0; i < data.Keys.Count && result.Count < max; i++)
                {
                    string key = data.Keys[i];
                    bool hit = pass == 0
                        ? key.StartsWith(q, StringComparison.OrdinalIgnoreCase)
                        : key.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (hit && !result.Contains(key))
                        result.Add(key);
                }
            }
            return result;
        }

        public static string KindName(UnitResKind kind)
        {
            switch (kind)
            {
                case UnitResKind.Sprite: return "Sprite";
                case UnitResKind.Prefab: return "Prefab";
                case UnitResKind.Audio: return "AudioClip";
                case UnitResKind.AnimatorController: return "AnimatorController";
                default: return "AnimationClip";
            }
        }
    }

    /// <summary>一个兵种：训练配置（TroopDefinition，可空）+ 单位预制体（可空）。</summary>
    public sealed class UnitRecord
    {
        public string Race = string.Empty;          // 目录名（Mouse / Cat / Chicken / …）
        public string Name = string.Empty;          // 显示名（优先 TroopDefinition.displayName）
        public string Id = string.Empty;            // TroopDefinition.id 或预制体名
        public string PrefabName = string.Empty;    // 预制体资源名
        public string PrefabPath = string.Empty;    // 预制体资产路径（可空 = 找不到）
        public string DefinitionPath = string.Empty; // TroopDefinition 资产路径（可空 = 没有训练配置）
        public string DefinitionId = string.Empty;

        public bool HasPrefab { get { return !string.IsNullOrEmpty(PrefabPath); } }
        public bool HasDefinition { get { return !string.IsNullOrEmpty(DefinitionPath); } }

        /// <summary>
        /// 写盘之后同步目录缓存。
        /// Name / DefinitionId / Id 只在 Rebuild() 扫描时填一次；一键修复或表单直接改资产后
        /// 不同步的话，左栏列表、标题、对比表的「训练 ID」列会一直显示旧值
        /// （表现成「刚补好的训练 ID 还是显示未填，但体检说没问题」），按训练 ID 搜索也搜不到新值。
        /// </summary>
        public void SyncDefinitionCache()
        {
            TroopDefinition definition = Definition;
            if (definition == null)
                return;

            if (!string.IsNullOrEmpty(definition.DisplayName))
                Name = definition.DisplayName;

            DefinitionId = definition.Id ?? string.Empty;
            Id = string.IsNullOrEmpty(definition.Id) ? PrefabName : definition.Id;
        }

        public TroopDefinition Definition
        {
            get { return HasDefinition ? AssetDatabase.LoadAssetAtPath<TroopDefinition>(DefinitionPath) : null; }
        }
    }

    /// <summary>兵种数值快照（只读，用于列表/对比表/体检）。</summary>
    public sealed class UnitStats
    {
        public string PrefabPath = string.Empty;
        public bool Valid;
        public string Error = string.Empty;

        // GameObjectProperty：基础
        public int ObjectType;
        public int Side;
        public int MaxHp;
        public int Defense;
        public int MagicDefense;

        // GameObjectProperty：攻击
        public bool IsRemoteAtk;
        public int Atk;
        public float AtkRate;
        public int MagicAtk;
        public float Repel;
        public float AntiRepel;
        public Vector2Int AtkRange;
        public string AtkObjKey = string.Empty;
        public float BaseMissChance;

        // GameObjectProperty：额外
        public float BarSustainTime;
        public float BuildTime;
        public float MoveSpeed;
        public float SuckBlood;
        public Vector2Int OccupySpace;

        // CharacterHealth：死亡表现
        public float DeathAngularSpeed;
        public float DeathFadeAlpha;
        public float DeathDarkFactor;

        // UnitAntiOverlap
        public float AntiOverlapRadius;

        // 视觉 / 音频
        public string SpriteKey = string.Empty;
        public string AnimatorControllerKey = string.Empty;
        public readonly string[] AudioKeys = new string[8];

        /// <summary>工程里角色实际出手节奏由攻击动画驱动，atkRate 只影响哨塔与 Buff，
        /// 所以这里只给「攻击 × 攻速」的粗算值，界面里明确标注仅供参考。</summary>
        public float RoughDps
        {
            get { return Mathf.Max(0, Mathf.Max(Atk, MagicAtk)) * Mathf.Max(0f, AtkRate); }
        }

        public int FilledAudioKeyCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < AudioKeys.Length; i++)
                {
                    if (!string.IsNullOrEmpty(AudioKeys[i]))
                        count++;
                }
                return count;
            }
        }
    }

    /// <summary>
    /// 选中兵种的写入会话：打开预制体，缓存各组件，
    /// 所有写入都走 SerializedObject.ApplyModifiedProperties（与 Inspector 一致，支持 Ctrl+Z）。
    /// </summary>
    public sealed class UnitStatSession
    {
        public UnitRecord Record { get; private set; }
        public GameObject PrefabRoot { get; private set; }
        public GameObjectProperty Prop { get; private set; }
        public CharacterHealth Health { get; private set; }
        public StageTexture Texture { get; private set; }
        public StageAnimatorController AnimatorController { get; private set; }
        public StageAudio Audio { get; private set; }
        public UnitAntiOverlap Overlap { get; private set; }

        public bool Valid { get; private set; }
        public string Error { get; private set; }
        public UnitStats Stats { get; private set; }

        public static UnitStatSession Open(UnitRecord record)
        {
            var session = new UnitStatSession();
            session.Record = record;
            session.Error = string.Empty;

            if (record == null || !record.HasPrefab)
            {
                session.Error = "没有找到单位预制体。";
                return session;
            }

            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(record.PrefabPath);
            if (root == null)
            {
                session.Error = "预制体加载失败：" + record.PrefabPath;
                return session;
            }

            session.PrefabRoot = root;
            session.Prop = root.GetComponent<GameObjectProperty>();
            session.Health = root.GetComponent<CharacterHealth>();
            session.Texture = root.GetComponent<StageTexture>();
            session.AnimatorController = root.GetComponent<StageAnimatorController>();
            session.Audio = root.GetComponent<StageAudio>();
            session.Overlap = root.GetComponent<UnitAntiOverlap>();

            if (session.Prop == null)
            {
                session.Error = "预制体上没有 GameObjectProperty 组件，这不是一个可调数值的兵种。";
                return session;
            }

            session.Valid = true;
            session.Stats = UnitStatsReader.Read(record.PrefabPath);
            return session;
        }

        public void Reload()
        {
            UnitStatSession fresh = Open(Record);
            PrefabRoot = fresh.PrefabRoot;
            Prop = fresh.Prop;
            Health = fresh.Health;
            Texture = fresh.Texture;
            AnimatorController = fresh.AnimatorController;
            Audio = fresh.Audio;
            Overlap = fresh.Overlap;
            Valid = fresh.Valid;
            Error = fresh.Error;
            Stats = fresh.Stats;
        }

        /// <summary>刷新数值快照（写完之后调用）。</summary>
        public void RefreshStats()
        {
            if (Record != null && Record.HasPrefab)
                Stats = UnitStatsReader.Read(Record.PrefabPath);
        }

        #region 写入（全部可撤销）
        public bool SetInt(UnityEngine.Object target, string field, int value)
        {
            SerializedProperty prop = Find(target, field);
            if (prop == null)
                return false;

            if (prop.intValue == value)
                return false;

            prop.intValue = value;
            Apply(target, prop.serializedObject);
            return true;
        }

        public bool SetFloat(UnityEngine.Object target, string field, float value)
        {
            SerializedProperty prop = Find(target, field);
            if (prop == null)
                return false;

            if (Mathf.Approximately(prop.floatValue, value))
                return false;

            prop.floatValue = value;
            Apply(target, prop.serializedObject);
            return true;
        }

        public bool SetBool(UnityEngine.Object target, string field, bool value)
        {
            SerializedProperty prop = Find(target, field);
            if (prop == null || prop.boolValue == value)
                return false;

            prop.boolValue = value;
            Apply(target, prop.serializedObject);
            return true;
        }

        public bool SetString(UnityEngine.Object target, string field, string value)
        {
            SerializedProperty prop = Find(target, field);
            if (prop == null)
                return false;

            value = value ?? string.Empty;
            if (prop.stringValue == value)
                return false;

            prop.stringValue = value;
            Apply(target, prop.serializedObject);
            return true;
        }

        public bool SetVector2Int(UnityEngine.Object target, string field, Vector2Int value)
        {
            SerializedProperty prop = Find(target, field);
            if (prop == null)
                return false;

            if (prop.vector2IntValue == value)
                return false;

            prop.vector2IntValue = value;
            Apply(target, prop.serializedObject);
            return true;
        }

        private static SerializedProperty Find(UnityEngine.Object target, string field)
        {
            if (target == null || string.IsNullOrEmpty(field))
                return null;

            var so = new SerializedObject(target);
            SerializedProperty prop = so.FindProperty(field);
            return prop;
        }

        private static void Apply(UnityEngine.Object target, SerializedObject so)
        {
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }
        #endregion
    }

    /// <summary>从预制体读取兵种数值快照。</summary>
    public static class UnitStatsReader
    {
        public static UnitStats Read(string prefabPath)
        {
            var stats = new UnitStats();
            stats.PrefabPath = prefabPath;

            if (string.IsNullOrEmpty(prefabPath))
            {
                stats.Error = "没有预制体路径。";
                return stats;
            }

            GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (root == null)
            {
                stats.Error = "预制体加载失败。";
                return stats;
            }

            GameObjectProperty prop = root.GetComponent<GameObjectProperty>();
            if (prop == null)
            {
                stats.Error = "预制体上没有 GameObjectProperty。";
                return stats;
            }

            stats.Valid = true;
            stats.ObjectType = (int)prop.objectType;
            stats.Side = prop.side;
            stats.MaxHp = prop.maxHp;
            stats.Defense = prop.defense;
            stats.MagicDefense = prop.magicDefense;
            stats.IsRemoteAtk = prop.isRemoteAtk;
            stats.Atk = prop.atk;
            stats.AtkRate = prop.atkRate;
            stats.MagicAtk = prop.magicAtk;
            stats.Repel = prop.repel;
            stats.AntiRepel = prop.antiRepel;
            stats.AtkRange = prop.atkRange;
            stats.AtkObjKey = prop.atkObj ?? string.Empty;
            stats.BaseMissChance = prop.baseMissChance;
            stats.BarSustainTime = prop.barSustainTime;
            stats.BuildTime = prop.buildTime;
            stats.MoveSpeed = prop.moveSpeed;
            stats.SuckBlood = prop.suckBlood;
            stats.OccupySpace = prop.occupySpace;

            CharacterHealth health = root.GetComponent<CharacterHealth>();
            if (health != null)
            {
                // 这三个字段在 CharacterHealth 里是 private [SerializeField]，
                // 只能经 SerializedObject 读取（写入同理，见 UnitStatSession.SetFloat）。
                var healthSo = new SerializedObject(health);
                stats.DeathAngularSpeed = ReadFloat(healthSo, "deathAngularSpeed", 180f);
                stats.DeathFadeAlpha = ReadFloat(healthSo, "deathFadeAlpha", 0.5f);
                stats.DeathDarkFactor = ReadFloat(healthSo, "deathDarkFactor", 0.4f);
            }

            UnitAntiOverlap overlap = root.GetComponent<UnitAntiOverlap>();
            if (overlap != null)
                stats.AntiOverlapRadius = overlap.radius;

            StageTexture texture = root.GetComponent<StageTexture>();
            if (texture != null)
                stats.SpriteKey = texture.spriteKey ?? string.Empty;

            StageAnimatorController animator = root.GetComponent<StageAnimatorController>();
            if (animator != null)
                stats.AnimatorControllerKey = animator.animatorControllerKey ?? string.Empty;

            StageAudio audio = root.GetComponent<StageAudio>();
            if (audio != null)
            {
                stats.AudioKeys[0] = audio.audioKey1;
                stats.AudioKeys[1] = audio.audioKey2;
                stats.AudioKeys[2] = audio.audioKey3;
                stats.AudioKeys[3] = audio.audioKey4;
                stats.AudioKeys[4] = audio.audioKey5;
                stats.AudioKeys[5] = audio.audioKey6;
                stats.AudioKeys[6] = audio.audioKey7;
                stats.AudioKeys[7] = audio.audioKey8;
            }

            return stats;
        }

        private static float ReadFloat(SerializedObject so, string field, float fallback)
        {
            SerializedProperty prop = so.FindProperty(field);
            return prop != null ? prop.floatValue : fallback;
        }
    }

    /// <summary>
    /// 兵种目录：扫描全工程的 TroopDefinition 与「兵种预制体」，合并成一条条 UnitRecord。
    /// </summary>
    public sealed class UnitCatalog
    {
        public const string PrefabRoot = "Assets/RemoteResource/Prefabricated body";
        public const string DefinitionRoot = "Assets/RemoteResource/TroopDefinitions";

        /// <summary>这些子目录不是兵种本体（子弹/建筑/特效/UI/法术效果）。
        /// 注意子弹目录有 "Bullet"、"Cat Bullet"、"Mouse Bullet" 多种命名，所以按「名字里含 Bullet」判断。</summary>
        private static readonly string[] ExcludedFolderNames = { "Build", "UI", "Vfx", "Magic" };

        private readonly List<UnitRecord> _records = new List<UnitRecord>();
        private readonly Dictionary<string, UnitStats> _statsCache = new Dictionary<string, UnitStats>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<UnitRecord>> _recordsByPrefabPath = new Dictionary<string, List<UnitRecord>>(StringComparer.Ordinal);
        private readonly List<string> _races = new List<string>();

        /// <summary>扫描时跳过（没有 GameObjectProperty 或落在排除目录）的预制体数量，仅用于设置页展示。</summary>
        public int SkippedPrefabCount { get; private set; }

        public IReadOnlyList<UnitRecord> Records { get { return _records; } }
        public IReadOnlyList<string> Races { get { return _races; } }
        public int Count { get { return _records.Count; } }

        /// <summary>
        /// 把任意文本清理成训练 ID 风格：小写 + 下划线，与工程现有 id（cat_knight、miner_rat）一致。
        /// </summary>
        public static string SanitizeTrainingId(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "unit";

            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c))
                    sb.Append(char.ToLowerInvariant(c));
                else if (c == ' ' || c == '-' || c == '_' || c == '.')
                    sb.Append('_');
            }

            string result = sb.ToString();
            while (result.Contains("__"))
                result = result.Replace("__", "_");

            result = result.Trim('_');
            return string.IsNullOrEmpty(result) ? "unit" : result;
        }

        /// <summary>该训练 ID 是否已被别的兵种占用。</summary>
        public bool IsTrainingIdTaken(string id, int exceptIndex)
        {
            if (string.IsNullOrEmpty(id))
                return false;

            for (int i = 0; i < _records.Count; i++)
            {
                if (i == exceptIndex)
                    continue;
                if (string.Equals(_records[i].DefinitionId, id, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>按单位预制体名生成一个没被占用的训练 ID（重复时追加 _2、_3…）。</summary>
        public string MakeUniqueTrainingId(UnitRecord record, int index)
        {
            string source = record == null
                ? "unit"
                : (string.IsNullOrEmpty(record.PrefabName) ? record.Name : record.PrefabName);

            string baseId = SanitizeTrainingId(source);
            if (!IsTrainingIdTaken(baseId, index))
                return baseId;

            for (int n = 2; n < 1000; n++)
            {
                string candidate = baseId + "_" + n;
                if (!IsTrainingIdTaken(candidate, index))
                    return candidate;
            }
            return baseId + "_x";
        }

        /// <summary>重新扫描兵种目录。</summary>
        public void Rebuild()
        {
            _records.Clear();
            _statsCache.Clear();
            _recordsByPrefabPath.Clear();
            _races.Clear();
            SkippedPrefabCount = 0;

            var byPrefabName = new Dictionary<string, UnitRecord>(StringComparer.OrdinalIgnoreCase);

            // 1) 先扫预制体（兵种本体）
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabRoot });
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                if (string.IsNullOrEmpty(path) || IsExcluded(path))
                {
                    SkippedPrefabCount++;
                    continue;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponent<GameObjectProperty>() == null)
                {
                    // 没有核心属性组件的不是兵种（子部件、特效等），静默跳过。
                    SkippedPrefabCount++;
                    continue;
                }

                var record = new UnitRecord();
                record.Race = GetRace(path);
                record.PrefabName = prefab.name;
                record.Name = prefab.name;
                record.Id = prefab.name;
                record.PrefabPath = path;
                _records.Add(record);

                if (!byPrefabName.ContainsKey(prefab.name))
                    byPrefabName.Add(prefab.name, record);
            }

            // 2) 再扫 TroopDefinition，按 prefabKey 合并（找不到预制体的也单独列出来）
            string[] definitionGuids = AssetDatabase.FindAssets("t:TroopDefinition", new[] { DefinitionRoot });
            for (int i = 0; i < definitionGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(definitionGuids[i]);
                TroopDefinition definition = AssetDatabase.LoadAssetAtPath<TroopDefinition>(path);
                if (definition == null)
                    continue;

                string prefabKey = definition.PrefabKey;
                UnitRecord target = null;
                if (!string.IsNullOrEmpty(prefabKey))
                    byPrefabName.TryGetValue(prefabKey, out target);

                if (target == null)
                {
                    target = new UnitRecord();
                    target.Race = GetRaceFromDefinitionPath(path);
                    target.PrefabName = prefabKey ?? string.Empty;
                    target.Name = string.IsNullOrEmpty(definition.DisplayName) ? definition.name : definition.DisplayName;

                    if (!string.IsNullOrEmpty(prefabKey))
                    {
                        GameObject prefab = UnitResourceIndex.Instance.GetPrefab(prefabKey);
                        if (prefab != null)
                        {
                            target.PrefabPath = AssetDatabase.GetAssetPath(prefab);
                            _records.Add(target);
                        }
                        else
                        {
                            _records.Add(target);   // 有训练配置但找不到预制体：也要列出来，体检会报错
                        }
                    }
                    else
                    {
                        _records.Add(target);
                    }
                }
                else
                {
                    target.Name = string.IsNullOrEmpty(definition.DisplayName) ? target.PrefabName : definition.DisplayName;
                }

                target.DefinitionPath = path;
                target.DefinitionId = definition.Id;
                target.Id = string.IsNullOrEmpty(definition.Id) ? target.PrefabName : definition.Id;
            }

            // 3) 索引 + 种族列表
            for (int i = 0; i < _records.Count; i++)
            {
                UnitRecord record = _records[i];
                if (!string.IsNullOrEmpty(record.PrefabPath))
                {
                    List<UnitRecord> list;
                    if (!_recordsByPrefabPath.TryGetValue(record.PrefabPath, out list))
                    {
                        list = new List<UnitRecord>();
                        _recordsByPrefabPath.Add(record.PrefabPath, list);
                    }
                    list.Add(record);
                }

                string race = string.IsNullOrEmpty(record.Race) ? "未分类" : record.Race;
                if (!_races.Contains(race))
                    _races.Add(race);
            }

            _races.Sort(StringComparer.OrdinalIgnoreCase);
            _records.Sort((a, b) =>
            {
                int race = string.Compare(a.Race, b.Race, StringComparison.OrdinalIgnoreCase);
                return race != 0 ? race : string.Compare(a.Name, b.Name, StringComparison.CurrentCulture);
            });
        }

        private static bool IsExcluded(string path)
        {
            string normalized = path.Replace('\\', '/');
            string rest = normalized.StartsWith(PrefabRoot + "/", StringComparison.OrdinalIgnoreCase)
                ? normalized.Substring(PrefabRoot.Length + 1)
                : normalized;

            string[] segments = rest.Split('/');
            for (int i = 0; i < segments.Length - 1; i++)   // 最后一段是文件名
            {
                string segment = segments[i];
                if (segment.IndexOf("Bullet", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;

                for (int e = 0; e < ExcludedFolderNames.Length; e++)
                {
                    if (string.Equals(segment, ExcludedFolderNames[e], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        /// <summary>是否兵营可训练种族（Mouse / Cat / Chicken）。Enemy、Engineer 是敌方与工程师单位，
        /// 不走 TroopDefinition 那套训练流程，体检时按「非兵营兵种」放宽。</summary>
        public static bool IsBarracksRace(string race)
        {
            return string.Equals(race, "Mouse", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(race, "Cat", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(race, "Chicken", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRace(string prefabPath)
        {
            string normalized = prefabPath.Replace('\\', '/');
            string prefix = PrefabRoot + "/";
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return "未分类";

            string rest = normalized.Substring(prefix.Length);
            int slash = rest.IndexOf('/');
            return slash > 0 ? rest.Substring(0, slash) : "未分类";
        }

        private static string GetRaceFromDefinitionPath(string definitionPath)
        {
            string normalized = definitionPath.Replace('\\', '/');
            string prefix = DefinitionRoot + "/";
            if (!normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return "未分类";

            string rest = normalized.Substring(prefix.Length);
            int slash = rest.IndexOf('/');
            return slash > 0 ? rest.Substring(0, slash) : "未分类";
        }

        /// <summary>取数值快照（带缓存）。force=true 时重新读盘。</summary>
        public UnitStats GetStats(UnitRecord record, bool force = false)
        {
            if (record == null || !record.HasPrefab)
                return new UnitStats { Error = "没有预制体" };

            UnitStats cached;
            if (!force && _statsCache.TryGetValue(record.PrefabPath, out cached))
                return cached;

            UnitStats stats = UnitStatsReader.Read(record.PrefabPath);
            _statsCache[record.PrefabPath] = stats;
            return stats;
        }

        /// <summary>丢弃数值缓存（写完盘后调用）。</summary>
        public void InvalidateStats()
        {
            _statsCache.Clear();
        }

        public List<UnitRecord> RecordsSharingPrefab(string prefabPath)
        {
            List<UnitRecord> list;
            if (!string.IsNullOrEmpty(prefabPath) && _recordsByPrefabPath.TryGetValue(prefabPath, out list))
                return list;

            return new List<UnitRecord>();
        }
    }

    /// <summary>某个兵种挂在哪些兵营里（普通 / 黑暗）。</summary>
    public sealed class BarracksInfo
    {
        public readonly List<string> NormalBarracks = new List<string>();
        public readonly List<string> DarkBarracks = new List<string>();

        public bool HasNormal { get { return NormalBarracks.Count > 0; } }
        public bool HasDark { get { return DarkBarracks.Count > 0; } }
        public bool IsWired { get { return HasNormal || HasDark; } }

        /// <summary>对比表用的短标签。</summary>
        public string ShortLabel
        {
            get
            {
                if (HasNormal && HasDark) return "两者";
                if (HasNormal) return "普通";
                if (HasDark) return "黑暗";
                return "—";
            }
        }

        /// <summary>详情：形如 "普通兵营 Mouse Barracks1；黑暗兵营 Mouse Dark barracks1"。</summary>
        public string Detail
        {
            get
            {
                if (!IsWired)
                    return "没有挂到任何兵营（玩家在兵营界面永远看不到它）";

                var parts = new List<string>();
                if (HasNormal)
                    parts.Add("普通兵营 " + string.Join("、", NormalBarracks.ToArray()));
                if (HasDark)
                    parts.Add("黑暗兵营 " + string.Join("、", DarkBarracks.ToArray()));
                return string.Join("；", parts.ToArray());
            }
        }
    }

    /// <summary>
    /// 兵营归属索引：扫描所有兵营预制体上的 BuildingTraining，得出"每个兵种挂在哪个兵营"。
    ///
    /// 为什么需要它：普通/黑暗兵营的区分**不在兵种上，而在兵营预制体上**——
    /// BuildingTraining.isDarkBarracks 决定按哪套等级结算，troops 数组决定该兵营能训练哪些兵种。
    /// 光看 TroopDefinition 是看不出归属的。</summary>
    public static class UnitBarracksIndex
    {
        private const string SearchRoot = "Assets/RemoteResource";

        private static Dictionary<string, BarracksInfo> _byDefinitionPath;
        private static List<string> _barracksNames;

        public static IReadOnlyList<string> BarracksNames
        {
            get { EnsureBuilt(); return _barracksNames; }
        }

        public static void Invalidate()
        {
            _byDefinitionPath = null;
            _barracksNames = null;
        }

        /// <summary>取某个兵种的兵营归属（没有训练配置时返回空信息）。</summary>
        public static BarracksInfo Get(UnitRecord record)
        {
            EnsureBuilt();
            if (record == null || string.IsNullOrEmpty(record.DefinitionPath))
                return new BarracksInfo();

            BarracksInfo info;
            return _byDefinitionPath.TryGetValue(record.DefinitionPath, out info) ? info : new BarracksInfo();
        }

        private static void EnsureBuilt()
        {
            if (_byDefinitionPath != null)
                return;

            _byDefinitionPath = new Dictionary<string, BarracksInfo>(StringComparer.Ordinal);
            _barracksNames = new List<string>();

            // 兵营预制体名字里都带 "Barracks"，先用名字过滤，避免加载全工程预制体
            string[] guids = AssetDatabase.FindAssets("Barracks t:Prefab", new[] { SearchRoot });
            if (guids.Length == 0)
            {
                // 兜底：名字过滤没命中时，扫该目录下全部预制体，再按名字/组件筛
                string[] all = AssetDatabase.FindAssets("t:Prefab", new[] { SearchRoot + "/Prefabricated body" });
                var filtered = new List<string>();
                for (int i = 0; i < all.Length; i++)
                {
                    string p = AssetDatabase.GUIDToAssetPath(all[i]);
                    if (!string.IsNullOrEmpty(p) && System.IO.Path.GetFileNameWithoutExtension(p)
                            .IndexOf("Barracks", StringComparison.OrdinalIgnoreCase) >= 0)
                        filtered.Add(all[i]);
                }
                guids = filtered.ToArray();
            }

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                BuildingTraining training = prefab.GetComponentInChildren<BuildingTraining>(true);
                if (training == null)
                    continue;

                bool dark = training.IsDarkBarracks;
                _barracksNames.Add(prefab.name + (dark ? "（黑暗）" : "（普通）"));

                TroopDefinition[] troops = training.Troops;
                if (troops == null)
                    continue;

                for (int t = 0; t < troops.Length; t++)
                {
                    TroopDefinition troop = troops[t];
                    if (troop == null)
                        continue;

                    string troopPath = AssetDatabase.GetAssetPath(troop);
                    if (string.IsNullOrEmpty(troopPath))
                        continue;

                    BarracksInfo info;
                    if (!_byDefinitionPath.TryGetValue(troopPath, out info))
                    {
                        info = new BarracksInfo();
                        _byDefinitionPath.Add(troopPath, info);
                    }

                    if (dark)
                    {
                        if (!info.DarkBarracks.Contains(prefab.name))
                            info.DarkBarracks.Add(prefab.name);
                    }
                    else
                    {
                        if (!info.NormalBarracks.Contains(prefab.name))
                            info.NormalBarracks.Add(prefab.name);
                    }
                }
            }
        }
    }

    /// <summary>备份工具：把资产（或预制体）文件复制到工具目录的 UnitBackups。</summary>
    public static class UnitBackup
    {
        /// <summary>备份一个资产文件（按 .asset/.prefab 原样复制）。</summary>
        public static string BackupAsset(string assetPath, string reason)
        {
            return BackupAsset(assetPath, reason, true);
        }

        /// <summary>备份一个资产文件；trim=false 时不做清理（批量备份用，最后统一清一次）。</summary>
        public static string BackupAsset(string assetPath, string reason, bool trim)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string sourceFile = Path.IsPathRooted(assetPath) ? assetPath : Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            if (!File.Exists(sourceFile))
                return null;

            string folder = UnitEditorPrefs.BackupFolder;
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[兵种编辑器] 备份目录创建失败：" + e.Message);
                return null;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string name = Path.GetFileNameWithoutExtension(sourceFile);
            string ext = Path.GetExtension(sourceFile);
            string target = Path.Combine(folder, name + "_" + stamp + "_" + Sanitize(reason) + ext);

            try
            {
                int n = 2;
                while (File.Exists(target))
                {
                    target = Path.Combine(folder, name + "_" + stamp + "_" + Sanitize(reason) + "_" + n + ext);
                    n++;
                }

                File.Copy(sourceFile, target, false);
                if (trim)
                    Trim(folder, UnitEditorPrefs.KeepBackupCount);
                return target;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[兵种编辑器] 备份失败：" + e.Message);
                return null;
            }
        }

        /// <summary>批量备份多个资产（去重）。</summary>
        public static int BackupAssets(IEnumerable<string> assetPaths, string reason)
        {
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int count = 0;
            string folder = UnitEditorPrefs.BackupFolder;
            foreach (string path in assetPaths)
            {
                if (string.IsNullOrEmpty(path) || !done.Add(path))
                    continue;

                // 逐个备份时不清理，最后统一清一次 —— 否则 120 个文件会做 120 次目录遍历。
                if (!string.IsNullOrEmpty(BackupAsset(path, reason, false)))
                    count++;
            }

            if (count > 0)
                Trim(folder, UnitEditorPrefs.KeepBackupCount);

            return count;
        }

        public static List<string> ListBackups()
        {
            var result = new List<string>();
            string folder = UnitEditorPrefs.BackupFolder;
            if (!Directory.Exists(folder))
                return result;

            try
            {
                string[] files = Directory.GetFiles(folder);
                Array.Sort(files, StringComparer.Ordinal);
                for (int i = files.Length - 1; i >= 0; i--)
                {
                    string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                    if (ext == ".asset" || ext == ".prefab")
                        result.Add(files[i]);
                }
            }
            catch (Exception)
            {
                // 目录不可读时当作没有备份。
            }
            return result;
        }

        /// <summary>
        /// 清理旧备份：**每个资产**保留最新的 keep 份。
        ///
        /// 以前是"整个目录按文件名排序，只留前 keep 个" —— 文件名是
        /// 「资产名_时间戳_原因.ext」，所以：
        /// ① 一次"保存全部"（57 个兵种 × 预制体 + 训练配置 ≈ 120 个文件）就会立刻超过 80，
        ///    按名字排序靠前的那批备份（包括**刚生成的新备份**）当场被删掉；
        /// ② 结果就是"上一版"经常找不到，而备份最大的价值恰恰是能回退一版。
        /// 现在按资产名分组、组内按时间戳从新到旧保留。
        /// </summary>
        private static void Trim(string folder, int keep)
        {
            if (keep <= 0 || string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return;

            try
            {
                var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                string[] files = Directory.GetFiles(folder);
                for (int i = 0; i < files.Length; i++)
                {
                    string assetName;
                    string stamp;
                    if (!TryParseBackupName(Path.GetFileName(files[i]), out assetName, out stamp))
                        continue;   // 不是本工具生成的备份：不动它

                    List<string> list;
                    if (!groups.TryGetValue(assetName, out list))
                    {
                        list = new List<string>();
                        groups.Add(assetName, list);
                    }
                    list.Add(files[i]);
                }

                foreach (KeyValuePair<string, List<string>> pair in groups)
                {
                    List<string> list = pair.Value;
                    if (list.Count <= keep)
                        continue;

                    // 时间戳是 yyyyMMdd_HHmmss，字符串排序即时间排序；倒序保留最新 keep 个
                    list.Sort(StringComparer.Ordinal);
                    int removeCount = list.Count - keep;
                    for (int i = 0; i < removeCount; i++)
                        File.Delete(list[i]);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[兵种编辑器] 清理旧备份失败：" + e.Message);
            }
        }

        /// <summary>从「资产名_yyyyMMdd_HHmmss_原因[_n].ext」里取资产名与时间戳。</summary>
        private static bool TryParseBackupName(string fileName, out string assetName, out string stamp)
        {
            assetName = null;
            stamp = null;
            if (string.IsNullOrEmpty(fileName))
                return false;

            string body = Path.GetFileNameWithoutExtension(fileName);
            System.Text.RegularExpressions.Match match =
                System.Text.RegularExpressions.Regex.Match(body, @"^(?<name>.+)_(?<stamp>\d{8}_\d{6})_");
            if (!match.Success)
                return false;

            assetName = match.Groups["name"].Value;
            stamp = match.Groups["stamp"].Value;
            return true;
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "backup";

            var sb = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(c);
            }
            return sb.Length == 0 ? "backup" : sb.ToString();
        }
    }
}