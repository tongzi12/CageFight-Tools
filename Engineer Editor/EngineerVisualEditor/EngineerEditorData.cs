using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CageFightEngineerTool
{
    /// <summary>
    /// 工程师编辑器能用的资源键种类（工程师本身只用到 Sprite 与 Prefab，
    /// 其余四类保留是为了让键选择器与「注册表状态」面板和另外六个编辑器保持一致）。
    /// </summary>
    public enum EngineerResKind
    {
        Sprite,
        Prefab,
        Audio,
        Texture,
        AnimationClip,
        AnimatorController
    }

    /// <summary>窗口状态（EditorPrefs，前缀 CageFight.EngineerVisualEditor.）。</summary>
    public static class EngineerEditorPrefs
    {
        private const string Prefix = "CageFight.EngineerVisualEditor.";

        private static string Get(string key, string fallback)
        {
            return EditorPrefs.GetString(Prefix + key, fallback);
        }

        private static void Set(string key, string value)
        {
            EditorPrefs.SetString(Prefix + key, value ?? string.Empty);
        }

        public static string LastEngineerPath
        {
            get { return Get("LastEngineerPath", string.Empty); }
            set { Set("LastEngineerPath", value); }
        }

        public static string Search
        {
            get { return Get("Search", string.Empty); }
            set { Set("Search", value); }
        }

        public static float LeftWidth
        {
            get
            {
                float value;
                return float.TryParse(Get("LeftWidth", "320"), out value) ? Mathf.Clamp(value, 220f, 900f) : 320f;
            }
            set { Set("LeftWidth", value.ToString("0")); }
        }

        public static int BackupKeep
        {
            get
            {
                int value;
                return int.TryParse(Get("BackupKeep", "20"), out value) ? Mathf.Clamp(value, 1, 500) : 20;
            }
            set { Set("BackupKeep", Mathf.Clamp(value, 1, 500).ToString()); }
        }

        public static int RightTab
        {
            get
            {
                int value;
                return int.TryParse(Get("RightTab", "0"), out value) ? Mathf.Clamp(value, 0, 4) : 0;
            }
            set { Set("RightTab", value.ToString()); }
        }
    }

    /// <summary>工具目录定位（备份放这里 —— 在工程之外，不会被 Unity 导入）。</summary>
    public static class EngineerPaths
    {
        /// <summary>工具源码所在文件夹的默认名字（仓库根下）。改过名也没关系：会自动扫描定位。</summary>
        public const string DefaultToolFolderName = "Engineer Editor";

        private const string ToolMarkerFile = "EngineerEditorData.cs";

        private static string _cachedToolRoot;

        public static string RepoRoot
        {
            get
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.GetFullPath(Path.Combine(projectRoot, ".."));
            }
        }

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
                   File.Exists(Path.Combine(folder, "EngineerVisualEditor", ToolMarkerFile));
        }

        public static string BackupFolder
        {
            get { return Path.Combine(ToolRoot, "EngineerBackups"); }
        }
    }

    /// <summary>
    /// 只读的资源索引（与法术编辑器 SpellResourceIndex、关卡编辑器 StageResourceIndex 同一套做法）：
    /// 直接加载六张注册表资产 + 选装注册表，遍历映射自建索引，**不问 ResourceManager**。
    ///
    /// 为什么自己扫：ResourceManager 的注册表字段是 private、没有 getter，
    /// 而且编辑器里没进关卡时它的资源缓存是空的（ResourceManager.cs:374-378）。
    /// </summary>
    public sealed class EngineerResourceIndex
    {
        private const string SpriteRegistryPath = "Assets/RemoteResource/SpriteRegistry.asset";
        private const string PrefabRegistryPath = "Assets/RemoteResource/PrefabRegistry.asset";
        private const string AudioRegistryPath = "Assets/RemoteResource/AudioRegistry.asset";
        private const string TextureRegistryPath = "Assets/RemoteResource/TextureRegistry.asset";
        private const string AnimationClipRegistryPath = "Assets/RemoteResource/AnimationClipRegistry.asset";
        private const string AnimatorControllerRegistryPath = "Assets/RemoteResource/AnimatorControllerRegistry.asset";

        /// <summary>选装注册表：工程师/种族/法术的稳定 ID 唯一来源（LoadoutDefinitionRegistry.cs:5-6）。</summary>
        public const string LoadoutRegistryPath = "Assets/RemoteResource/LoadoutDefinitionRegistry.asset";

        /// <summary>图鉴目录：出战工程师会按 prefabKey 标记「已拥有」（PlayerLoadoutSpawner.cs:51）。</summary>
        public const string BookCatalogPath = "Assets/Resource/LocalResource/Book/BookCatalog.asset";

        private static EngineerResourceIndex _instance;

        private readonly Dictionary<EngineerResKind, List<string>> _keys = new Dictionary<EngineerResKind, List<string>>();
        private readonly Dictionary<EngineerResKind, HashSet<string>> _keySets = new Dictionary<EngineerResKind, HashSet<string>>();
        private readonly Dictionary<EngineerResKind, Dictionary<string, UnityEngine.Object>> _assets =
            new Dictionary<EngineerResKind, Dictionary<string, UnityEngine.Object>>();
        private readonly Dictionary<EngineerResKind, bool> _found = new Dictionary<EngineerResKind, bool>();

        private readonly Dictionary<string, string> _assetPathByKey = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<string> _registeredEngineerPaths = new List<string>();
        private readonly List<string> _spellIds = new List<string>();
        private readonly HashSet<string> _bookPrefabKeys = new HashSet<string>(StringComparer.Ordinal);

        private string _defaultEngineerId = string.Empty;
        private string _defaultRaceId = string.Empty;
        private string _defaultSpellSlot1Id = string.Empty;
        private string _defaultSpellSlot2Id = string.Empty;

        private bool _built;

        public static EngineerResourceIndex Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new EngineerResourceIndex();
                    _instance.EnsureBuilt();
                }
                return _instance;
            }
        }

        public static void Invalidate()
        {
            _instance = null;
        }

        public static string RegistryPathOf(EngineerResKind kind)
        {
            switch (kind)
            {
                case EngineerResKind.Sprite: return SpriteRegistryPath;
                case EngineerResKind.Prefab: return PrefabRegistryPath;
                case EngineerResKind.Audio: return AudioRegistryPath;
                case EngineerResKind.Texture: return TextureRegistryPath;
                case EngineerResKind.AnimationClip: return AnimationClipRegistryPath;
                default: return AnimatorControllerRegistryPath;
            }
        }

        public static string KindName(EngineerResKind kind)
        {
            switch (kind)
            {
                case EngineerResKind.Sprite: return "Sprite";
                case EngineerResKind.Prefab: return "预制体";
                case EngineerResKind.Audio: return "音频";
                case EngineerResKind.Texture: return "纹理";
                case EngineerResKind.AnimationClip: return "动画片段";
                default: return "动画控制器";
            }
        }

        public bool RegistryFound(EngineerResKind kind)
        {
            EnsureBuilt();
            return _found[kind];
        }

        public List<string> Keys(EngineerResKind kind)
        {
            EnsureBuilt();
            return _keys[kind];
        }

        public bool HasKey(EngineerResKind kind, string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            EnsureBuilt();
            return _keySets[kind].Contains(key);
        }

        public UnityEngine.Object AssetOf(EngineerResKind kind, string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            EnsureBuilt();
            UnityEngine.Object value;
            return _assets[kind].TryGetValue(key, out value) ? value : null;
        }

        /// <summary>资产路径（按资源键查；用于「定位」按钮）。</summary>
        public string AssetPathOf(EngineerResKind kind, string key)
        {
            UnityEngine.Object asset = AssetOf(kind, key);
            return asset != null ? AssetDatabase.GetAssetPath(asset) : null;
        }

        public List<string> RegisteredEngineerPaths
        {
            get { EnsureBuilt(); return _registeredEngineerPaths; }
        }

        public bool IsEngineerRegistered(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;
            EnsureBuilt();
            return _registeredEngineerPaths.Contains(assetPath);
        }

        /// <summary>注册表里的法术 ID（选装注册表 spellDefinitions 列表里的定义）。</summary>
        public List<string> SpellIds
        {
            get { EnsureBuilt(); return _spellIds; }
        }

        public bool HasSpellId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return false;
            EnsureBuilt();
            return _spellIds.Contains(id);
        }

        /// <summary>图鉴目录里登记过的单位预制体键（出战工程师按 prefabKey 标记「已拥有」）。</summary>
        public HashSet<string> BookPrefabKeys
        {
            get { EnsureBuilt(); return _bookPrefabKeys; }
        }

        public string DefaultEngineerId { get { EnsureBuilt(); return _defaultEngineerId; } }
        public string DefaultRaceId { get { EnsureBuilt(); return _defaultRaceId; } }
        public string DefaultSpellSlot1Id { get { EnsureBuilt(); return _defaultSpellSlot1Id; } }
        public string DefaultSpellSlot2Id { get { EnsureBuilt(); return _defaultSpellSlot2Id; } }

        private void EnsureBuilt()
        {
            if (_built)
                return;
            _built = true;

            foreach (EngineerResKind kind in Enum.GetValues(typeof(EngineerResKind)))
            {
                _keys[kind] = new List<string>();
                _keySets[kind] = new HashSet<string>(StringComparer.Ordinal);
                _assets[kind] = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
                _found[kind] = false;
            }

            foreach (EngineerResKind kind in Enum.GetValues(typeof(EngineerResKind)))
                ScanRegistry(kind, RegistryPathOf(kind));

            ScanLoadoutRegistry();
            ScanBookCatalog();
        }

        /// <summary>
        /// 注册表结构是一张 mappings 列表，每项形如 `- key: xxx` + 一个资产引用（两种形式并存）。
        /// 用 SerializedObject 读，避免手写 YAML 解析；读不到就只标记「未找到」。
        /// </summary>
        private void ScanRegistry(EngineerResKind kind, string registryPath)
        {
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(registryPath);
            if (asset == null)
                return;

            _found[kind] = true;
            var so = new SerializedObject(asset);
            SerializedProperty mappings = so.FindProperty("mappings") ?? so.FindProperty("m_Mappings");
            if (mappings == null || !mappings.isArray)
                return;

            for (int i = 0; i < mappings.arraySize; i++)
            {
                SerializedProperty entry = mappings.GetArrayElementAtIndex(i);
                SerializedProperty keyProp = entry.FindPropertyRelative("key");
                if (keyProp == null)
                    continue;

                string key = keyProp.stringValue;
                if (string.IsNullOrEmpty(key) || !_keySets[kind].Add(key))
                    continue;

                _keys[kind].Add(key);
                UnityEngine.Object value = ResolveEntryAsset(entry);
                if (value != null)
                {
                    _assets[kind][key] = value;
                    string path = AssetDatabase.GetAssetPath(value);
                    if (!string.IsNullOrEmpty(path) && !_assetPathByKey.ContainsKey(key))
                        _assetPathByKey[key] = path;
                }
            }

            _keys[kind].Sort(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>选装注册表：已登记的工程师资产路径 + 法术 ID + 默认选择。</summary>
        private void ScanLoadoutRegistry()
        {
            LoadoutDefinitionRegistry registry =
                AssetDatabase.LoadAssetAtPath<LoadoutDefinitionRegistry>(LoadoutRegistryPath);
            if (registry == null)
                return;

            var so = new SerializedObject(registry);
            SerializedProperty engineers = so.FindProperty("engineerDefinitions");
            if (engineers != null && engineers.isArray)
            {
                for (int i = 0; i < engineers.arraySize; i++)
                {
                    UnityEngine.Object value = engineers.GetArrayElementAtIndex(i).objectReferenceValue;
                    string path = value != null ? AssetDatabase.GetAssetPath(value) : null;
                    if (!string.IsNullOrEmpty(path) && !_registeredEngineerPaths.Contains(path))
                        _registeredEngineerPaths.Add(path);
                }
            }

            SerializedProperty spells = so.FindProperty("spellDefinitions");
            if (spells != null && spells.isArray)
            {
                for (int i = 0; i < spells.arraySize; i++)
                {
                    SpellDefinition spell = spells.GetArrayElementAtIndex(i).objectReferenceValue as SpellDefinition;
                    if (spell != null && !string.IsNullOrEmpty(spell.Id) && !_spellIds.Contains(spell.Id))
                        _spellIds.Add(spell.Id);
                }
            }

            _spellIds.Sort(StringComparer.OrdinalIgnoreCase);

            SerializedProperty prop = so.FindProperty("defaultEngineerId");
            _defaultEngineerId = prop != null ? prop.stringValue : string.Empty;
            prop = so.FindProperty("defaultRaceId");
            _defaultRaceId = prop != null ? prop.stringValue : string.Empty;
            prop = so.FindProperty("defaultSpellSlot1Id");
            _defaultSpellSlot1Id = prop != null ? prop.stringValue : string.Empty;
            prop = so.FindProperty("defaultSpellSlot2Id");
            _defaultSpellSlot2Id = prop != null ? prop.stringValue : string.Empty;
        }

        private void ScanBookCatalog()
        {
            BookCatalog catalog = AssetDatabase.LoadAssetAtPath<BookCatalog>(BookCatalogPath);
            if (catalog == null || catalog.Entries == null)
                return;

            for (int i = 0; i < catalog.Entries.Count; i++)
            {
                BookEntry entry = catalog.Entries[i];
                if (entry != null && !string.IsNullOrEmpty(entry.prefabKey))
                    _bookPrefabKeys.Add(entry.prefabKey);
            }
        }

        /// <summary>
        /// 从一条注册表条目里取出资产引用：扫条目的全部子属性，
        /// 先取「任何有值的 ObjectReference」，都没有再按 `m_AssetGUID` 反查 —— 六张表的字段名各不相同，不能猜名字。
        /// </summary>
        private static UnityEngine.Object ResolveEntryAsset(SerializedProperty entry)
        {
            SerializedProperty iterator = entry.Copy();
            SerializedProperty end = entry.GetEndProperty();
            UnityEngine.Object direct = null;
            string guid = null;

            while (iterator.NextVisible(true) && !SerializedProperty.EqualContents(iterator, end))
            {
                if (iterator.propertyType == SerializedPropertyType.ObjectReference)
                {
                    if (direct == null && iterator.objectReferenceValue != null)
                        direct = iterator.objectReferenceValue;
                }
                else if (guid == null && iterator.propertyType == SerializedPropertyType.String &&
                         iterator.name == "m_AssetGUID")
                {
                    guid = iterator.stringValue;
                }
            }

            if (direct != null)
                return direct;

            if (!string.IsNullOrEmpty(guid))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(path))
                    return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            }

            return null;
        }
    }

    /// <summary>一个工程师（定义资产 + 它指到的预制体）。</summary>
    public sealed class EngineerRecord
    {
        public string AssetPath = string.Empty;
        public EngineerDefinition Definition;

        public string Id = string.Empty;
        public string DisplayName = string.Empty;
        public string IconKey = string.Empty;
        public string PrefabKey = string.Empty;
        public string InnateSpellId = string.Empty;
        public string PortraitFrameKey = string.Empty;
        public List<string> IdleFrameKeys = new List<string>();
        public float IdleFrameRate = 6f;

        /// <summary>预制体资产路径（按 prefabKey 从 PrefabRegistry 反查；找不到为空）。</summary>
        public string PrefabPath = string.Empty;

        public bool IsRegistered;

        /// <summary>最近一次备份写的文件（状态栏用）。</summary>
        public string LastBackup;

        public GameObject PrefabAsset
        {
            get
            {
                return string.IsNullOrEmpty(PrefabPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }
        }
    }

    /// <summary>全部工程师 + 注册表状态的快照。</summary>
    public sealed class EngineerCatalog
    {
        public readonly List<EngineerRecord> Records = new List<EngineerRecord>();

        public static EngineerCatalog Build()
        {
            var catalog = new EngineerCatalog();
            EngineerResourceIndex index = EngineerResourceIndex.Instance;

            string[] guids = AssetDatabase.FindAssets("t:EngineerDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                EngineerDefinition definition = AssetDatabase.LoadAssetAtPath<EngineerDefinition>(path);
                if (definition == null)
                    continue;

                var record = new EngineerRecord
                {
                    AssetPath = path,
                    Definition = definition,
                    Id = definition.Id ?? string.Empty,
                    DisplayName = definition.DisplayName ?? string.Empty,
                    IconKey = definition.IconKey ?? string.Empty,
                    PrefabKey = definition.PrefabKey ?? string.Empty,
                    InnateSpellId = definition.InnateSpellId ?? string.Empty,
                    PortraitFrameKey = definition.PortraitFrameKey ?? string.Empty,
                    IdleFrameRate = definition.IdlePortraitFrameRate,
                    IsRegistered = index.IsEngineerRegistered(path)
                };

                string[] frames = definition.IdlePortraitFrameKeys;
                for (int f = 0; f < frames.Length; f++)
                    record.IdleFrameKeys.Add(frames[f] ?? string.Empty);

                if (!string.IsNullOrEmpty(record.PrefabKey))
                    record.PrefabPath = index.AssetPathOf(EngineerResKind.Prefab, record.PrefabKey) ?? string.Empty;

                catalog.Records.Add(record);
            }

            catalog.Records.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            return catalog;
        }

        public EngineerRecord Find(string assetPath)
        {
            for (int i = 0; i < Records.Count; i++)
            {
                if (string.Equals(Records[i].AssetPath, assetPath, StringComparison.OrdinalIgnoreCase))
                    return Records[i];
            }
            return null;
        }

        public int RegisteredCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Records.Count; i++)
                {
                    if (Records[i].IsRegistered)
                        count++;
                }
                return count;
            }
        }
    }

    /// <summary>写盘：备份 + 保存资产。所有写入都先备份到工具目录的 EngineerBackups。</summary>
    public static class EngineerWriter
    {
        /// <summary>把某个资产文件备份一份（返回备份路径）。</summary>
        public static string Backup(string assetPath, string reason)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            string full = Path.GetFullPath(assetPath);
            if (!File.Exists(full))
                return null;

            try
            {
                Directory.CreateDirectory(EngineerPaths.BackupFolder);
                string safe = Path.GetFileNameWithoutExtension(full);
                string extension = Path.GetExtension(full);
                foreach (char c in Path.GetInvalidFileNameChars())
                    safe = safe.Replace(c, '_');

                string target = Path.Combine(EngineerPaths.BackupFolder,
                    safe + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + reason + extension);
                File.Copy(full, target, true);
                TrimBackups(EngineerPaths.BackupFolder, EngineerEditorPrefs.BackupKeep);
                return target;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[工程师编辑器] 备份失败：" + exception.Message);
                return null;
            }
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

                var infos = new List<FileInfo>();
                for (int i = 0; i < files.Length; i++)
                    infos.Add(new FileInfo(files[i]));

                // 按「资产名」分组，每组各自保留 keep 份 —— 否则备份多的资产会把少的挤掉（关卡编辑器踩过这个坑）。
                var groups = new Dictionary<string, List<FileInfo>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < infos.Count; i++)
                {
                    string name = infos[i].Name;
                    int cut = name.LastIndexOf('_');
                    if (cut > 0)
                    {
                        int cut2 = name.LastIndexOf('_', Math.Max(0, cut - 1));
                        if (cut2 > 0)
                            name = name.Substring(0, cut2);
                    }

                    List<FileInfo> list;
                    if (!groups.TryGetValue(name, out list))
                    {
                        list = new List<FileInfo>();
                        groups[name] = list;
                    }
                    list.Add(infos[i]);
                }

                foreach (KeyValuePair<string, List<FileInfo>> group in groups)
                {
                    List<FileInfo> list = group.Value;
                    if (list.Count <= keep)
                        continue;

                    list.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                    for (int i = keep; i < list.Count; i++)
                        list[i].Delete();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[工程师编辑器] 清理旧备份失败：" + exception.Message);
            }
        }
    }

    /// <summary>
    /// 工程师预制体的读写入口。
    ///
    /// 只碰**预制体资产上的数据**（EngineerController 与 GameObjectProperty 的公开字段），
    /// 不碰任何脚本；写前备份整个 .prefab 文件。
    /// </summary>
    public static class EngineerPrefabAccess
    {
        public static EngineerController GetController(GameObject prefab)
        {
            return prefab != null ? prefab.GetComponent<EngineerController>() : null;
        }

        public static GameObjectProperty GetProperty(GameObject prefab)
        {
            return prefab != null ? prefab.GetComponent<GameObjectProperty>() : null;
        }

        /// <summary>该预制体上有没有 "Shoot point" 子节点（缺了抛射法术起点会回落到施法锚点）。</summary>
        public static bool HasShootPoint(GameObject prefab)
        {
            return prefab != null && prefab.transform.Find("Shoot point") != null;
        }

        /// <summary>提交一批改动：备份 + 标脏 + 保存。</summary>
        public static string Commit(EngineerRecord record, UnityEngine.Object changed, string reason)
        {
            if (record == null || changed == null)
                return null;

            string backup = EngineerWriter.Backup(record.PrefabPath, reason);
            EditorUtility.SetDirty(changed);
            AssetDatabase.SaveAssets();
            record.LastBackup = backup;
            return backup;
        }

        /// <summary>读动画控制器里的 bool 参数名集合（校验 moveParameter 用）。</summary>
        public static HashSet<string> BoolParametersOf(Animator animator)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            if (animator == null || animator.runtimeAnimatorController == null)
                return result;

            var so = new SerializedObject(animator.runtimeAnimatorController);
            SerializedProperty parameters = so.FindProperty("m_AnimatorParameters");
            if (parameters == null || !parameters.isArray)
                return result;

            for (int i = 0; i < parameters.arraySize; i++)
            {
                SerializedProperty entry = parameters.GetArrayElementAtIndex(i);
                SerializedProperty type = entry.FindPropertyRelative("m_Type");
                SerializedProperty name = entry.FindPropertyRelative("m_Name");
                if (type == null || name == null)
                    continue;

                // m_Type：1 = Float，3 = Bool，4 = Trigger，9 = Int（Unity 的 AnimatorControllerParameterType）。
                if (type.intValue == 3 || type.intValue == 4)
                    result.Add(name.stringValue);
            }

            return result;
        }
    }
}
