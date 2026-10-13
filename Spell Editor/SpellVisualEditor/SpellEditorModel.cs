using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CageFightSpellTool
{
    /// <summary>
    /// 法术编辑器能用的资源键种类。
    /// 法术本身只用到 Sprite（图标 / 警示圈）与 Prefab（施法预制体），
    /// 其余四类保留是为了让键选择器与"注册表状态"面板和另外四个编辑器保持一致。
    /// </summary>
    public enum SpellResKind
    {
        Sprite,
        Prefab,
        Audio,
        Texture,
        AnimationClip,
        AnimatorController
    }

    /// <summary>编辑器偏好（前缀 CageFight.SpellVisualEditor.）。关掉窗口再打开，栏宽/页签都还在。</summary>
    public static class SpellEditorPrefs
    {
        private const string Prefix = "CageFight.SpellVisualEditor.";

        private static string Get(string key, string fallback)
        {
            return EditorPrefs.GetString(Prefix + key, fallback);
        }

        private static void Set(string key, string value)
        {
            EditorPrefs.SetString(Prefix + key, value ?? string.Empty);
        }

        public static string LastSpellPath
        {
            get { return Get("LastSpellPath", string.Empty); }
            set { Set("LastSpellPath", value); }
        }

        public static string BackupFolder
        {
            get { return Get("BackupFolder", SpellEditorData.DefaultBackupFolder); }
            set { Set("BackupFolder", value); }
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

        public static float ListWidth
        {
            get
            {
                float value;
                return float.TryParse(Get("ListWidth", "300"), out value) ? Mathf.Clamp(value, 200f, 700f) : 300f;
            }
            set { Set("ListWidth", value.ToString("0")); }
        }

        public static float RightWidth
        {
            get
            {
                float value;
                return float.TryParse(Get("RightWidth", "430"), out value) ? Mathf.Clamp(value, 260f, 900f) : 430f;
            }
            set { Set("RightWidth", value.ToString("0")); }
        }

        public static bool RightCollapsed
        {
            get { return Get("RightCollapsed", "0") == "1"; }
            set { Set("RightCollapsed", value ? "1" : "0"); }
        }

        public static int RightTab
        {
            get
            {
                int value;
                return int.TryParse(Get("RightTab", "0"), out value) ? Mathf.Clamp(value, 0, 2) : 0;
            }
            set { Set("RightTab", value.ToString()); }
        }

        public static string Search
        {
            get { return Get("Search", string.Empty); }
            set { Set("Search", value); }
        }
    }

    /// <summary>
    /// 只读的注册表索引（与关卡编辑器的 StageResourceIndex 同一套做法）：
    /// 直接加载六张注册表资产、遍历它们的映射列表自建索引，**不依赖运行场景、也不问 ResourceManager**。
    ///
    /// 为什么要自己扫：ResourceManager 的注册表字段是 private、没有任何 getter，
    /// 而且它的 GetSprite(key) 查的是"本局已加载缓存"（ResourceManager.cs:374-378），
    /// 编辑器里没进关卡就是空的。
    /// </summary>
    public sealed class SpellResourceIndex
    {
        private const string SpriteRegistryPath = "Assets/RemoteResource/SpriteRegistry.asset";
        private const string PrefabRegistryPath = "Assets/RemoteResource/PrefabRegistry.asset";
        private const string AudioRegistryPath = "Assets/RemoteResource/AudioRegistry.asset";
        private const string TextureRegistryPath = "Assets/RemoteResource/TextureRegistry.asset";
        private const string AnimationClipRegistryPath = "Assets/RemoteResource/AnimationClipRegistry.asset";
        private const string AnimatorControllerRegistryPath = "Assets/RemoteResource/AnimatorControllerRegistry.asset";

        private static SpellResourceIndex _instance;

        private readonly Dictionary<SpellResKind, List<string>> _keys = new Dictionary<SpellResKind, List<string>>();
        private readonly Dictionary<SpellResKind, HashSet<string>> _keySets = new Dictionary<SpellResKind, HashSet<string>>();
        private readonly Dictionary<SpellResKind, Dictionary<string, UnityEngine.Object>> _assets =
            new Dictionary<SpellResKind, Dictionary<string, UnityEngine.Object>>();
        private readonly Dictionary<SpellResKind, bool> _found = new Dictionary<SpellResKind, bool>();

        private bool _built;

        public static SpellResourceIndex Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new SpellResourceIndex();
                    _instance.EnsureBuilt();
                }
                return _instance;
            }
        }

        public static void Invalidate()
        {
            _instance = null;
        }

        public static string RegistryPathOf(SpellResKind kind)
        {
            switch (kind)
            {
                case SpellResKind.Sprite: return SpriteRegistryPath;
                case SpellResKind.Prefab: return PrefabRegistryPath;
                case SpellResKind.Audio: return AudioRegistryPath;
                case SpellResKind.Texture: return TextureRegistryPath;
                case SpellResKind.AnimationClip: return AnimationClipRegistryPath;
                default: return AnimatorControllerRegistryPath;
            }
        }

        /// <summary>某个键种类对应的注册表资产路径（键选择器会显示它，方便直接去定位）。</summary>
        public string RegistryPath(SpellResKind kind)
        {
            return RegistryPathOf(kind);
        }

        public bool RegistryFound(SpellResKind kind)
        {
            EnsureBuilt();
            return _found[kind];
        }

        public static string KindName(SpellResKind kind)
        {
            switch (kind)
            {
                case SpellResKind.Sprite: return "Sprite";
                case SpellResKind.Prefab: return "预制体";
                case SpellResKind.Audio: return "音频";
                case SpellResKind.Texture: return "纹理";
                case SpellResKind.AnimationClip: return "动画片段";
                default: return "动画控制器";
            }
        }

        private void EnsureBuilt()
        {
            if (_built)
                return;
            _built = true;

            foreach (SpellResKind kind in Enum.GetValues(typeof(SpellResKind)))
            {
                _keys[kind] = new List<string>();
                _keySets[kind] = new HashSet<string>(StringComparer.Ordinal);
                _assets[kind] = new Dictionary<string, UnityEngine.Object>(StringComparer.Ordinal);
                _found[kind] = false;
            }

            foreach (SpellResKind kind in Enum.GetValues(typeof(SpellResKind)))
                Scan(kind, RegistryPathOf(kind));
        }

        /// <summary>
        /// 注册表的结构是一张 mappings 列表，每项形如 `- key: xxx` + 一个资产引用（两种形式并存）。
        /// 用 SerializedObject 读，避免手写 YAML 解析；读取失败（资产不在/结构变了）就只标记「未找到」。
        /// </summary>
        private void Scan(SpellResKind kind, string registryPath)
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
                UnityEngine.Object value = ResolveAsset(entry);
                if (value != null)
                    _assets[kind][key] = value;
            }

            _keys[kind].Sort(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 从一条注册表条目里取出资产引用。
        ///
        /// 为什么不能按字段名取：六张注册表的字段名**各不相同** ——
        /// PrefabRegistry 用 `prefab`、SpriteRegistry 用 `sprite`、AudioRegistry 用 `audioClip`、
        /// TextureRegistry 用 `texture`、AnimationClipRegistry 用 `animationClip`、
        /// AnimatorControllerRegistry 用 `animatorController`；而且每条都同时带一个 AssetReference
        /// （`prefabReference` / `spriteReference` / … 里有 `m_AssetGUID`）。**六张表里都没有 `m_Asset` 这个字段**。
        /// 早先只找 `m_Asset` 的后果：键明明在注册表里却取不到资产，界面还会误报成「键不在注册表」
        /// （实测踩到：`Dancing statue Magic` 在 PrefabRegistry 第 1003 行，却被报成不在注册表）。
        ///
        /// 现在的做法（不猜名字，扫这个条目的子属性）：
        ///   1) 任何 ObjectReference 且**有值** → 就是它（这一路能拿到 SpriteRegistry 的子精灵，
        ///      因为 `sprite:` 直接指向图集里的那一张）；
        ///   2) 都没有值 → 找带 `m_AssetGUID` 的子结构，用 guid 反查资产路径再加载。
        /// </summary>
        private static UnityEngine.Object ResolveAsset(SerializedProperty entry)
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
                    continue;
                }

                if (guid == null && iterator.propertyType == SerializedPropertyType.String &&
                    iterator.name == "m_AssetGUID" && !string.IsNullOrEmpty(iterator.stringValue))
                {
                    guid = iterator.stringValue;
                }
            }

            if (direct != null)
                return direct;

            if (!string.IsNullOrEmpty(guid))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.IsNullOrEmpty(assetPath))
                    return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            }

            return null;
        }

        public int KeyCount(SpellResKind kind)
        {
            EnsureBuilt();
            return _keys[kind].Count;
        }

        public IReadOnlyList<string> Keys(SpellResKind kind)
        {
            EnsureBuilt();
            return _keys[kind];
        }

        public bool Has(SpellResKind kind, string key)
        {
            EnsureBuilt();
            return !string.IsNullOrEmpty(key) && _keySets[kind].Contains(key);
        }

        public UnityEngine.Object Get(SpellResKind kind, string key)
        {
            EnsureBuilt();
            if (string.IsNullOrEmpty(key))
                return null;

            UnityEngine.Object value;
            return _assets[kind].TryGetValue(key, out value) ? value : null;
        }

        public Sprite GetSprite(string key) { return Get(SpellResKind.Sprite, key) as Sprite; }
        public GameObject GetPrefab(string key) { return Get(SpellResKind.Prefab, key) as GameObject; }
        public AudioClip GetAudio(string key) { return Get(SpellResKind.Audio, key) as AudioClip; }
        public Texture GetTexture(string key) { return Get(SpellResKind.Texture, key) as Texture; }
        public AnimationClip GetClip(string key) { return Get(SpellResKind.AnimationClip, key) as AnimationClip; }
        public RuntimeAnimatorController GetAnimator(string key)
        {
            return Get(SpellResKind.AnimatorController, key) as RuntimeAnimatorController;
        }

        /// <summary>模糊搜索键（给键选择器用）：前缀命中优先，然后包含命中。</summary>
        public List<string> Search(SpellResKind kind, string query, int max)
        {
            EnsureBuilt();

            var result = new List<string>();
            List<string> all = _keys[kind];
            if (string.IsNullOrEmpty(query))
            {
                for (int i = 0; i < all.Count && result.Count < max; i++)
                    result.Add(all[i]);
                return result;
            }

            for (int i = 0; i < all.Count && result.Count < max; i++)
            {
                if (all[i].IndexOf(query, StringComparison.OrdinalIgnoreCase) == 0)
                    result.Add(all[i]);
            }
            for (int i = 0; i < all.Count && result.Count < max; i++)
            {
                string key = all[i];
                if (key.IndexOf(query, StringComparison.OrdinalIgnoreCase) > 0)
                    result.Add(key);
            }

            return result;
        }
    }

    /// <summary>一条法术记录：资产 + 加载出来的定义。</summary>
    public sealed class SpellRecord
    {
        public string AssetPath;
        public SpellDefinition Definition;
        public bool Registered;          // 是否登记在 LoadoutDefinitionRegistry.spellDefinitions 里
        public int RegistryIndex = -1;   // 在注册表里的下标（-1 = 没登记）

        public string Id
        {
            get { return Definition != null ? Definition.Id : string.Empty; }
        }

        public string DisplayName
        {
            get
            {
                if (Definition == null)
                    return "(空)";
                return string.IsNullOrEmpty(Definition.DisplayName) ? Definition.name : Definition.DisplayName;
            }
        }

        public string FileName
        {
            get { return string.IsNullOrEmpty(AssetPath) ? string.Empty : Path.GetFileNameWithoutExtension(AssetPath); }
        }

        /// <summary>列表上显示的标签：资产名（= 文件名）通常就是作者认的名字。</summary>
        public string Label
        {
            get { return FileName; }
        }
    }

    /// <summary>法术编辑器的数据层：扫描 / 加载 / 保存 / 备份 / 注册表登记。</summary>
    public sealed class SpellCatalog
    {
        /// <summary>法术资产都在这里（LoadoutDefinitions\Magic）。</summary>
        public const string SpellFolder = "Assets/RemoteResource/LoadoutDefinitions/Magic";

        /// <summary>法术的"上架"位置：LoadoutDefinitionRegistry.spellDefinitions（按 ID 查找的唯一来源）。</summary>
        public const string RegistryAssetPath = "Assets/RemoteResource/LoadoutDefinitionRegistry.asset";

        private readonly List<SpellRecord> _records = new List<SpellRecord>();

        public IReadOnlyList<SpellRecord> Records { get { return _records; } }
        public LoadoutDefinitionRegistry Registry { get; private set; }
        public bool RegistryFound { get; private set; }
        public bool FolderFound { get; private set; }
        public string ScanMessage { get; private set; }

        public void Rebuild()
        {
            _records.Clear();
            Registry = AssetDatabase.LoadAssetAtPath<LoadoutDefinitionRegistry>(RegistryAssetPath);
            RegistryFound = Registry != null;
            FolderFound = AssetDatabase.IsValidFolder(SpellFolder);

            var registered = new Dictionary<SpellDefinition, int>();
            if (Registry != null && Registry.SpellDefinitions != null)
            {
                for (int i = 0; i < Registry.SpellDefinitions.Count; i++)
                {
                    SpellDefinition definition = Registry.SpellDefinitions[i];
                    if (definition != null && !registered.ContainsKey(definition))
                        registered[definition] = i;
                }
            }

            if (!FolderFound)
            {
                ScanMessage = "找不到法术资产目录：" + SpellFolder;
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:SpellDefinition", new[] { SpellFolder });
            var paths = new List<string>();
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                    paths.Add(path);
            }
            paths.Sort(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < paths.Count; i++)
            {
                SpellDefinition definition = AssetDatabase.LoadAssetAtPath<SpellDefinition>(paths[i]);
                if (definition == null)
                    continue;

                var record = new SpellRecord { AssetPath = paths[i], Definition = definition };
                int registryIndex;
                if (registered.TryGetValue(definition, out registryIndex))
                {
                    record.Registered = true;
                    record.RegistryIndex = registryIndex;
                }

                _records.Add(record);
            }

            ScanMessage = _records.Count == 0
                ? "目录里没有法术资产（" + SpellFolder + "）"
                : null;
        }

        public SpellRecord Find(string assetPath)
        {
            for (int i = 0; i < _records.Count; i++)
            {
                if (string.Equals(_records[i].AssetPath, assetPath, StringComparison.OrdinalIgnoreCase))
                    return _records[i];
            }
            return null;
        }

        /// <summary>同一 ID 出现了几次（含自己）：注册表按 ID 建字典，重复会打警告且后来者被忽略。</summary>
        public int CountWithId(string id, SpellRecord except)
        {
            if (string.IsNullOrEmpty(id))
                return 0;

            int count = 0;
            for (int i = 0; i < _records.Count; i++)
            {
                SpellRecord record = _records[i];
                if (record == except || record.Definition == null)
                    continue;
                if (string.Equals(record.Id, id, StringComparison.Ordinal))
                    count++;
            }
            return count;
        }

        public bool IsIdTaken(string id, SpellRecord except)
        {
            return CountWithId(id, except) > 0;
        }

        /// <summary>
        /// 把法术登记进 LoadoutDefinitionRegistry.spellDefinitions（"上架"）。
        /// 注册表是**数据资产**，不是代码 —— 这里只改它的序列化列表，写完立刻保存。
        /// </summary>
        public bool Register(SpellRecord record, out string message)
        {
            message = null;
            if (record == null || record.Definition == null)
            {
                message = "记录为空，登记失败。";
                return false;
            }

            if (Registry == null)
            {
                message = "找不到注册表资产：" + RegistryAssetPath;
                return false;
            }

            if (record.Registered)
            {
                message = record.Label + " 已经在注册表里（下标 " + record.RegistryIndex + "）。";
                return false;
            }

            var so = new SerializedObject(Registry);
            SerializedProperty list = so.FindProperty("spellDefinitions");
            if (list == null || !list.isArray)
            {
                message = "注册表里找不到 spellDefinitions 列表，登记失败。";
                return false;
            }

            SpellBackup.BackupAsset(RegistryAssetPath, "登记法术前");

            int index = list.arraySize;
            list.InsertArrayElementAtIndex(index);
            list.GetArrayElementAtIndex(index).objectReferenceValue = record.Definition;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(Registry);
            AssetDatabase.SaveAssets();

            message = "已把 " + record.Label + "（ID " + record.Id + "）登记到注册表第 " + index + " 项。";
            return true;
        }

        /// <summary>把注册表里重复的条目去重（只删后面的重复项，保留第一次出现的）。</summary>
        public int RemoveDuplicateRegistryEntries()
        {
            if (Registry == null)
                return 0;

            var so = new SerializedObject(Registry);
            SerializedProperty list = so.FindProperty("spellDefinitions");
            if (list == null || !list.isArray)
                return 0;

            var seen = new HashSet<SpellDefinition>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            int removed = 0;

            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                SpellDefinition definition = list.GetArrayElementAtIndex(i).objectReferenceValue as SpellDefinition;
                if (definition == null)
                    continue;

                string id = definition.Id;
                bool duplicate = !seen.Add(definition) || (!string.IsNullOrEmpty(id) && !seenIds.Add(id));
                if (!duplicate)
                    continue;

                list.DeleteArrayElementAtIndex(i);
                removed++;
            }

            if (removed > 0)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(Registry);
                AssetDatabase.SaveAssets();
            }

            return removed;
        }
    }

    /// <summary>
    /// 备份：每次保存/一键修复前，把**被改动的那个资产**复制到备份目录。
    /// 命名 <c>资产名_时间戳_原因.asset</c>；淘汰只按"同名资产"分组保留最近 N 份，
    /// 文件名不合法的一律不动（避免误删真实资产）。
    /// </summary>
    public static class SpellBackup
    {
        public static string BackupAsset(string assetPath, string reason)
        {
            return BackupAsset(assetPath, reason, true);
        }

        public static string BackupAsset(string assetPath, string reason, bool trim)
        {
            if (string.IsNullOrEmpty(assetPath) || !File.Exists(assetPath))
                return null;

            string folder = SpellEditorPrefs.BackupFolder;
            if (string.IsNullOrEmpty(folder))
                folder = SpellEditorData.DefaultBackupFolder;

            try
            {
                Directory.CreateDirectory(folder);
                string name = Path.GetFileNameWithoutExtension(assetPath);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string target = Path.Combine(folder, Sanitize(name) + "_" + stamp + "_" + Sanitize(reason) + ".asset");

                int suffix = 1;
                while (File.Exists(target))
                    target = Path.Combine(folder, Sanitize(name) + "_" + stamp + "_" + Sanitize(reason) + "_" + (suffix++) + ".asset");

                File.Copy(assetPath, target, false);
                if (trim)
                    Trim(folder, SpellEditorPrefs.BackupKeep);

                return target;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[法术编辑器] 备份失败：" + exception.Message);
                return null;
            }
        }

        public static List<string> ListBackups()
        {
            var result = new List<string>();
            string folder = SpellEditorPrefs.BackupFolder;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                return result;

            string[] files = Directory.GetFiles(folder, "*.asset");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (int i = files.Length - 1; i >= 0; i--)
                result.Add(files[i]);
            return result;
        }

        /// <summary>
        /// 按"源资产名"分组，每组只保留最近 keep 份。
        /// 关键点：先校验文件名符合 <c>名_yyyyMMdd_HHmmss_原因</c> 才参与淘汰 ——
        /// 否则一个叫 <c>Stage2.asset</c> 的真实资产会被当成 Stage2 的备份删掉。
        /// </summary>
        private static void Trim(string folder, int keep)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(folder, "*.asset");
            }
            catch (Exception)
            {
                return;
            }

            var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++)
            {
                string assetName;
                string stamp;
                if (!TryParseBackupName(Path.GetFileNameWithoutExtension(files[i]), out assetName, out stamp))
                    continue;

                List<string> list;
                if (!groups.TryGetValue(assetName, out list))
                {
                    list = new List<string>();
                    groups[assetName] = list;
                }
                list.Add(files[i]);
            }

            foreach (KeyValuePair<string, List<string>> group in groups)
            {
                List<string> list = group.Value;
                if (list.Count <= keep)
                    continue;

                list.Sort(StringComparer.OrdinalIgnoreCase);
                int removeCount = list.Count - keep;
                for (int i = 0; i < removeCount; i++)
                {
                    try
                    {
                        File.Delete(list[i]);
                    }
                    catch (Exception)
                    {
                        // 删不掉就留着，不影响使用
                    }
                }
            }
        }

        private static bool TryParseBackupName(string fileName, out string assetName, out string stamp)
        {
            assetName = null;
            stamp = null;
            if (string.IsNullOrEmpty(fileName))
                return false;

            int last = fileName.LastIndexOf('_');
            if (last <= 0)
                return false;

            int middle = fileName.LastIndexOf('_', last - 1);
            if (middle <= 0)
                return false;

            string date = fileName.Substring(middle + 1, last - middle - 1);
            string time = fileName.Substring(last + 1);
            if (date.Length != 8 || time.Length != 6 || !IsAllDigits(date) || !IsAllDigits(time))
                return false;

            assetName = fileName.Substring(0, middle);
            stamp = date + "_" + time;
            return assetName.Length > 0;
        }

        private static bool IsAllDigits(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] < '0' || text[i] > '9')
                    return false;
            }
            return text.Length > 0;
        }

        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "backup";

            char[] invalid = Path.GetInvalidFileNameChars();
            var builder = new System.Text.StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool bad = false;
                for (int k = 0; k < invalid.Length; k++)
                {
                    if (invalid[k] == c)
                    {
                        bad = true;
                        break;
                    }
                }
                builder.Append(bad ? '_' : c);
            }
            return builder.ToString();
        }
    }

    /// <summary>工具用到的固定路径与默认值。</summary>
    public static class SpellEditorData
    {
        /// <summary>备份目录（相对工程根，和另外四个编辑器一样放在工具目录里）。</summary>
        public const string DefaultBackupFolder = "Spell Editor/SpellBackups";
    }
}
