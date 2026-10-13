using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CageFightBookTool
{
    /// <summary>
    /// 图鉴可视化编辑器的偏好设置（存在 EditorPrefs 里，跨 Unity 会话保留）。
    /// </summary>
    public static class BookEditorPrefs
    {
        private const string Prefix = "CageFight.BookVisualEditor.";

        /// <summary>上次打开的图鉴资产路径。</summary>
        public static string LastCatalogPath
        {
            get { return EditorPrefs.GetString(Prefix + "LastCatalogPath", BookEditorData.DefaultCatalogPath); }
            set { EditorPrefs.SetString(Prefix + "LastCatalogPath", value); }
        }

        /// <summary>备份目录（默认：仓库根下的工具目录\BookBackups，与工程分离，不污染 Unity 资产库）。</summary>
        public static string BackupFolder
        {
            get { return EditorPrefs.GetString(Prefix + "BackupFolder", BookEditorData.DefaultBackupFolder); }
            set { EditorPrefs.SetString(Prefix + "BackupFolder", value); }
        }

        /// <summary>备份保留份数。</summary>
        public static int KeepBackupCount
        {
            get { return EditorPrefs.GetInt(Prefix + "KeepBackupCount", 60); }
            set { EditorPrefs.SetInt(Prefix + "KeepBackupCount", value); }
        }

        /// <summary>保存前自动备份。</summary>
        public static bool AutoBackupOnSave
        {
            get { return EditorPrefs.GetBool(Prefix + "AutoBackupOnSave", true); }
            set { EditorPrefs.SetBool(Prefix + "AutoBackupOnSave", value); }
        }

        /// <summary>Card 预览使用真实预制体渲染（实验性，已收起：默认关闭）。
        /// 注意 key 带 .v2 —— 这是历史上打开过该开关的用户也会回到"关闭"状态。</summary>
        public static bool UseRealCardRender
        {
            get { return EditorPrefs.GetBool(Prefix + "UseRealCardRender.v2", false); }
            set { EditorPrefs.SetBool(Prefix + "UseRealCardRender.v2", value); }
        }

        /// <summary>Card 预制体路径（图鉴详情卡）。</summary>
        public static string CardPrefabPath
        {
            get { return EditorPrefs.GetString(Prefix + "CardPrefabPath", "Assets/RemoteResource/CDprofab/Card.prefab"); }
            set { EditorPrefs.SetString(Prefix + "CardPrefabPath", value); }
        }

        /// <summary>左栏宽度。</summary>
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

        /// <summary>右栏（预览）宽度。</summary>
        public static float PreviewWidth
        {
            get { return EditorPrefs.GetFloat(Prefix + "PreviewWidth", 430f); }
            set { EditorPrefs.SetFloat(Prefix + "PreviewWidth", value); }
        }

        /// <summary>预览里模拟解锁状态：0=全部解锁 1=全部锁定 2=仅已拥有 3=按进度。</summary>
        public static int PreviewUnlockMode
        {
            get { return EditorPrefs.GetInt(Prefix + "PreviewUnlockMode", 0); }
            set { EditorPrefs.SetInt(Prefix + "PreviewUnlockMode", value); }
        }

        /// <summary>左栏列表顺序：0=按游戏顺序（星级升序，与游戏一致）1=按数据原始顺序。</summary>
        public static int ListOrderMode
        {
            get { return EditorPrefs.GetInt(Prefix + "ListOrderMode", 0); }
            set { EditorPrefs.SetInt(Prefix + "ListOrderMode", value); }
        }

        /// <summary>真实 Card 渲染用的目标分辨率（索引，见 BookVisualEditorWindow.CardResolutionLabels）。</summary>
        public static int CardRenderResolution
        {
            get { return EditorPrefs.GetInt(Prefix + "CardRenderResolution", 0); }
            set { EditorPrefs.SetInt(Prefix + "CardRenderResolution", value); }
        }
    }

    /// <summary>
    /// 训练配置（TroopDefinition）索引：按单位预制体键反查训练配置。
    ///
    /// 为什么图鉴要用它：图鉴格子的头像键其实就等于训练配置里的 <c>iconKey</c>（兵营里那个图标），
    /// 立绘键通常等于 <c>animationSpriteKey</c>；而按单位名去猜（如 "Cat Knight UI_0"）在猫系等单位上
    /// 完全猜不中——猫系头像在 `Cat Troop UI_*` 这套图集里，名字里根本没有单位名。
    /// </summary>
    public static class BookTroopDefinitionIndex
    {
        private static Dictionary<string, TroopDefinition> _byPrefabKey;

        public static void Invalidate()
        {
            _byPrefabKey = null;
        }

        /// <summary>按单位预制体键取训练配置；找不到返回 null。</summary>
        public static TroopDefinition Find(string prefabKey)
        {
            if (string.IsNullOrEmpty(prefabKey))
                return null;

            EnsureBuilt();

            TroopDefinition definition;
            return _byPrefabKey.TryGetValue(prefabKey.Trim(), out definition) ? definition : null;
        }

        private static void EnsureBuilt()
        {
            if (_byPrefabKey != null)
                return;

            _byPrefabKey = new Dictionary<string, TroopDefinition>(StringComparer.Ordinal);

            string[] guids = AssetDatabase.FindAssets("t:TroopDefinition");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                TroopDefinition definition = AssetDatabase.LoadAssetAtPath<TroopDefinition>(path);
                if (definition == null)
                    continue;

                string prefabKey = definition.PrefabKey;
                if (string.IsNullOrEmpty(prefabKey) || _byPrefabKey.ContainsKey(prefabKey))
                    continue;

                _byPrefabKey.Add(prefabKey, definition);
            }
        }
    }

    /// <summary>
    /// 资源键索引：只读缓存 SpriteRegistry / PrefabRegistry，
    /// 让策划用「搜索 + 下拉」选资源键，而不是手抄键名或拖资产。
    /// </summary>
    public sealed class BookResourceIndex
    {
        public const string SpriteRegistryPath = "Assets/RemoteResource/SpriteRegistry.asset";
        public const string PrefabRegistryPath = "Assets/RemoteResource/PrefabRegistry.asset";

        private static BookResourceIndex _instance;

        private readonly List<string> _spriteKeys = new List<string>();
        private readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private readonly Dictionary<Sprite, string> _spriteToKey = new Dictionary<Sprite, string>();
        private readonly List<string> _prefabKeys = new List<string>();
        private readonly Dictionary<string, GameObject> _prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        private bool _built;

        public static BookResourceIndex Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new BookResourceIndex();
                    _instance.Rebuild();
                }
                return _instance;
            }
        }

        public IReadOnlyList<string> SpriteKeys { get { EnsureBuilt(); return _spriteKeys; } }
        public IReadOnlyList<string> PrefabKeys { get { EnsureBuilt(); return _prefabKeys; } }
        public bool SpriteRegistryFound { get; private set; }
        public bool PrefabRegistryFound { get; private set; }

        /// <summary>丢弃缓存并在下次访问时重建（资源注册表变化后调用）。</summary>
        public static void Invalidate()
        {
            _instance = null;
        }

        private void EnsureBuilt()
        {
            if (!_built)
                Rebuild();
        }

        public void Rebuild()
        {
            _built = true;
            _spriteKeys.Clear();
            _sprites.Clear();
            _spriteToKey.Clear();
            _prefabKeys.Clear();
            _prefabs.Clear();

            SpriteRegistry spriteRegistry = AssetDatabase.LoadAssetAtPath<SpriteRegistry>(SpriteRegistryPath);
            SpriteRegistryFound = spriteRegistry != null;
            if (spriteRegistry != null)
            {
                for (int i = 0; i < spriteRegistry.mappings.Count; i++)
                {
                    SpriteMapping mapping = spriteRegistry.mappings[i];
                    if (mapping == null || string.IsNullOrEmpty(mapping.key))
                        continue;

                    if (!_sprites.ContainsKey(mapping.key))
                    {
                        _sprites.Add(mapping.key, mapping.sprite);
                        _spriteKeys.Add(mapping.key);
                        if (mapping.sprite != null && !_spriteToKey.ContainsKey(mapping.sprite))
                            _spriteToKey.Add(mapping.sprite, mapping.key);
                    }
                }
            }

            PrefabRegistry prefabRegistry = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(PrefabRegistryPath);
            PrefabRegistryFound = prefabRegistry != null;
            if (prefabRegistry != null)
            {
                for (int i = 0; i < prefabRegistry.mappings.Count; i++)
                {
                    PrefabMapping mapping = prefabRegistry.mappings[i];
                    if (mapping == null || string.IsNullOrEmpty(mapping.key))
                        continue;

                    if (!_prefabs.ContainsKey(mapping.key))
                    {
                        _prefabs.Add(mapping.key, mapping.prefab);
                        _prefabKeys.Add(mapping.key);
                    }
                }
            }
        }

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

        /// <summary>取编辑器预览用 Sprite 强引用（不触发 Addressables 加载）。</summary>
        public Sprite GetSprite(string key)
        {
            EnsureBuilt();
            if (string.IsNullOrEmpty(key))
                return null;

            Sprite sprite;
            return _sprites.TryGetValue(key, out sprite) ? sprite : null;
        }

        public GameObject GetPrefab(string key)
        {
            EnsureBuilt();
            if (string.IsNullOrEmpty(key))
                return null;

            GameObject prefab;
            return _prefabs.TryGetValue(key, out prefab) ? prefab : null;
        }

        /// <summary>把拖进来的 Sprite 反查成资源键（对应旧 Inspector 的「拖拽自动填键」）。</summary>
        public bool TryGetKeyForSprite(Sprite sprite, out string key)
        {
            EnsureBuilt();
            key = null;
            if (sprite == null)
                return false;

            return _spriteToKey.TryGetValue(sprite, out key);
        }

        /// <summary>按关键字模糊搜索精灵键（不区分大小写，优先前缀命中）。</summary>
        public List<string> SearchSprites(string query, int max = 80)
        {
            return SearchKeys(_spriteKeys, query, max);
        }

        /// <summary>按关键字模糊搜索预制体键。</summary>
        public List<string> SearchPrefabs(string query, int max = 80)
        {
            return SearchKeys(_prefabKeys, query, max);
        }

        /// <summary>按键名推断「单位预制体」候选（排除子弹/派生体等非图鉴单位）。</summary>
        public List<string> SearchUnitPrefabs(string query, int max = 80)
        {
            List<string> all = SearchKeys(_prefabKeys, query, max * 3);
            var result = new List<string>();
            for (int i = 0; i < all.Count && result.Count < max; i++)
            {
                string key = all[i];
                if (key.StartsWith("Bullet", StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith("Derivative-", StringComparison.OrdinalIgnoreCase))
                    continue;

                result.Add(key);
            }
            return result;
        }

        private static List<string> SearchKeys(List<string> source, string query, int max)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(query))
            {
                for (int i = 0; i < source.Count && i < max; i++)
                    result.Add(source[i]);
                return result;
            }

            string q = query.Trim();
            // 第一轮：前缀命中；第二轮：包含命中。两轮都保持注册表原顺序。
            for (int pass = 0; pass < 2 && result.Count < max; pass++)
            {
                for (int i = 0; i < source.Count && result.Count < max; i++)
                {
                    string key = source[i];
                    bool hit = pass == 0
                        ? key.StartsWith(q, StringComparison.OrdinalIgnoreCase)
                        : key.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (hit && !result.Contains(key))
                        result.Add(key);
                }
            }
            return result;
        }

        /// <summary>
        /// 按单位预制体键推断立绘键候选：
        /// 1) 训练配置里的 animationSpriteKey（最可靠，是该项目自己填的）；
        /// 2) "键-attack_0"（游戏里卡面常用的攻击帧）；
        /// 3) 其它以该单位名开头的动画帧。
        /// </summary>
        public List<string> GuessRoleKeys(string prefabKey)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(prefabKey))
                return result;

            EnsureBuilt();

            TroopDefinition definition = BookTroopDefinitionIndex.Find(prefabKey);
            if (definition != null)
                TryAddIfExists(result, definition.AnimationSpriteKey);

            TryAddIfExists(result, prefabKey + "-attack_0");
            TryAddIfExists(result, prefabKey + "_0");
            TryAddIfExists(result, prefabKey + "-move_0");
            TryAddIfExists(result, prefabKey);

            for (int i = 0; i < _spriteKeys.Count; i++)
            {
                string key = _spriteKeys[i];
                if (key.StartsWith(prefabKey + "-", StringComparison.Ordinal) ||
                    key.StartsWith(prefabKey + "_", StringComparison.Ordinal))
                {
                    if (!result.Contains(key))
                        result.Add(key);
                }
            }
            return result;
        }

        /// <summary>
        /// 按单位预制体键推断头像键候选：
        /// 1) 训练配置里的 iconKey（图鉴格子用的就是兵营那个图标，最可靠）；
        /// 2) "键 UI_0" 之类的直接命名；
        /// 3) 该种族的 Troop UI 图集（形如 "Cat Troop UI_3"）。
        /// </summary>
        public List<string> GuessAvatarKeys(string prefabKey)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(prefabKey))
                return result;

            EnsureBuilt();

            TroopDefinition definition = BookTroopDefinitionIndex.Find(prefabKey);
            if (definition != null)
                TryAddIfExists(result, definition.IconKey);

            TryAddIfExists(result, prefabKey + " UI_0");
            TryAddIfExists(result, prefabKey + " Troop UI_0");
            TryAddIfExists(result, prefabKey + "_0");

            for (int i = 0; i < _spriteKeys.Count; i++)
            {
                string key = _spriteKeys[i];
                if (key.IndexOf("Troop UI", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    key.StartsWith(prefabKey, StringComparison.OrdinalIgnoreCase))
                {
                    if (!result.Contains(key))
                        result.Add(key);
                }
            }
            return result;
        }

        /// <summary>
        /// 按种族取「兵营图标图集」里的候选键（形如 Cat Troop UI_*），
        /// 排除其它条目已经用掉的键。没有训练配置时的兜底手段。
        /// </summary>
        public List<string> GuessAvatarKeysByRace(string raceId, HashSet<string> usedKeys)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(raceId))
                return result;

            EnsureBuilt();
            string prefix = raceId + " Troop UI";

            for (int i = 0; i < _spriteKeys.Count; i++)
            {
                string key = _spriteKeys[i];
                if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (usedKeys != null && usedKeys.Contains(key))
                    continue;

                result.Add(key);
            }
            return result;
        }

        private void TryAddIfExists(List<string> result, string key)
        {
            if (_sprites.ContainsKey(key) && !result.Contains(key))
                result.Add(key);
        }
    }

    /// <summary>
    /// 图鉴数据访问层：加载 BookCatalog 资产、读写条目、增删改移、保存与自动备份。
    /// 所有写入都走 Undo 记录，策划可以 Ctrl+Z 回退。
    /// </summary>
    public sealed class BookEditorData
    {
        public const string DefaultCatalogPath = "Assets/Resource/LocalResource/Book/BookCatalog.asset";

        /// <summary>工具源码所在文件夹的默认名字（仓库根下）。改过名也没关系：会自动扫描定位。</summary>
        public const string DefaultToolFolderName = "Illustration Editor";

        private const string ToolMarkerFile = "BookEditorData.cs";

        private static string _cachedToolRoot;

        /// <summary>
        /// 定位本工具所在目录（仓库根 / 工具文件夹）。
        /// 先按默认名字找，找不到就扫描仓库根的子目录，用「里面有 BookVisualEditor\BookEditorData.cs」来识别。
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

        /// <summary>仓库根目录（Unity 工程的上一级）。</summary>
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
            return !string.IsNullOrEmpty(folder) &&
                   File.Exists(Path.Combine(folder, "BookVisualEditor", ToolMarkerFile));
        }

        /// <summary>默认备份目录：工具目录下的 BookBackups（在工程之外，不会被 Unity 导入）。</summary>
        public static string DefaultBackupFolder
        {
            get { return Path.Combine(ToolRoot, "BookBackups"); }
        }

        /// <summary>
        /// 本工具的"版本标识"：取工具源码文件夹里最新的 .cs 修改时间。
        /// 界面上直接显示它，省得再问"我现在跑的是哪一版"——改完源码重新编译后这个时间会自己变。
        /// </summary>
        public static string SourceStamp()
        {
            try
            {
                string[] guids = AssetDatabase.FindAssets("t:MonoScript", new[] { "Assets/Editor/BookSystem" });
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

        private SerializedObject _serialized;
        private SerializedProperty _entriesProp;
        private SerializedProperty _categoriesProp;
        private SerializedProperty _raceOptionsProp;
        private bool _dirty;

        public BookCatalog Catalog { get; private set; }
        public string AssetPath { get; private set; }
        public bool IsLoaded { get { return Catalog != null && _serialized != null; } }
        public bool IsDirty { get { return _dirty; } }

        /// <summary>数据发生变化（条目增删改）时触发，用于刷新界面。</summary>
        public event Action Changed;

        #region 加载 / 保存
        /// <summary>加载指定路径的图鉴资产。返回是否成功。</summary>
        public bool Load(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return false;

            BookCatalog catalog = AssetDatabase.LoadAssetAtPath<BookCatalog>(assetPath);
            if (catalog == null)
                return false;

            Catalog = catalog;
            AssetPath = assetPath;
            _serialized = new SerializedObject(catalog);
            _entriesProp = _serialized.FindProperty("entries");
            _categoriesProp = _serialized.FindProperty("categories");
            _raceOptionsProp = _serialized.FindProperty("raceOptions");
            _dirty = false;
            return _entriesProp != null;
        }

        public void Unload()
        {
            Catalog = null;
            AssetPath = null;
            _serialized = null;
            _entriesProp = null;
            _categoriesProp = null;
            _raceOptionsProp = null;
            _dirty = false;
        }

        /// <summary>把序列化对象的改动写回资产并标脏。</summary>
        public void Apply()
        {
            if (!IsLoaded)
                return;

            _serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(Catalog);
            _dirty = true;
        }

        /// <summary>
        /// 只标脏、不写序列化缓存。
        /// 直接改 BookEntry 字段（不经过 SerializedObject）之后必须用这个，
        /// 否则 ApplyModifiedProperties 会把序列化缓存里的旧值写回去、覆盖掉刚改的内容。
        /// </summary>
        public void MarkDirty()
        {
            if (!IsLoaded)
                return;

            EditorUtility.SetDirty(Catalog);
            _dirty = true;
            RaiseChanged();
        }

        /// <summary>保存到磁盘（可选先自动备份）。</summary>
        public void SaveToDisk(bool withBackup)
        {
            if (!IsLoaded)
                return;

            if (withBackup)
                BackupNow("手动保存");

            AssetDatabase.SaveAssetIfDirty(Catalog);
            _dirty = false;
        }

        /// <summary>丢弃内存改动，从磁盘重新读。</summary>
        public void RevertToDisk()
        {
            if (!IsLoaded)
                return;

            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            Load(AssetPath);
            RaiseChanged();
        }

        /// <summary>另存为新的图鉴资产（用于试算/分叉，不影响原资产）。</summary>
        public string SaveAsCopy(string newAssetPath)
        {
            if (!IsLoaded)
                return null;

            Apply();
            if (!AssetDatabase.CopyAsset(AssetPath, newAssetPath))
                return null;

            AssetDatabase.SaveAssets();
            return newAssetPath;
        }
        #endregion

        #region 图片解析与同步
        /// <summary>
        /// 取图鉴格子头像：**资源键优先**（游戏只认资源键），键为空或无效时才退回编辑器直引。
        /// 这个顺序很重要：反过来的话，编辑器显示会和游戏里显示的不是同一张图。
        /// </summary>
        public static Sprite ResolveAvatarSprite(BookEntry entry)
        {
            if (entry == null)
                return null;

            if (!string.IsNullOrEmpty(entry.avatarSpriteKey))
            {
                Sprite byKey = BookResourceIndex.Instance.GetSprite(entry.avatarSpriteKey);
                if (byKey != null)
                    return byKey;
            }

            return entry.AvatarEditorFallback;
        }

        /// <summary>取 Card 立绘：同样资源键优先。</summary>
        public static Sprite ResolveRoleSprite(BookEntry entry)
        {
            if (entry == null)
                return null;

            if (!string.IsNullOrEmpty(entry.roleSpriteKey))
            {
                Sprite byKey = BookResourceIndex.Instance.GetSprite(entry.roleSpriteKey);
                if (byKey != null)
                    return byKey;
            }

            return entry.RoleEditorFallback;
        }

        /// <summary>编辑器直引与资源键是否不一致（不一致 = 编辑器看到的和游戏里的不是同一张图）。</summary>
        public static bool IsEditorSpriteOutOfSync(BookEntry entry, bool isAvatar, out string keyName, out string referenceName)
        {
            keyName = null;
            referenceName = null;

#if UNITY_EDITOR
            if (entry == null)
                return false;

            string key = isAvatar ? entry.avatarSpriteKey : entry.roleSpriteKey;
            if (string.IsNullOrEmpty(key))
                return false;

            Sprite byKey = BookResourceIndex.Instance.GetSprite(key);
            if (byKey == null)
                return false;

            Sprite reference = isAvatar ? entry.avatarSprite : entry.roleSprite;
            if (reference == null || ReferenceEquals(reference, byKey))
                return false;

            keyName = byKey.name;
            referenceName = reference.name;
            return true;
#else
            return false;
#endif
        }

        /// <summary>
        /// 按资源键同步「编辑器预览直引」。选键/改键之后必须调用，
        /// 否则资产里会留着上一次的图，编辑器显示与游戏不一致（游戏只认资源键）。
        /// </summary>
        public bool SyncEditorSprite(BookEntry entry, bool isAvatar)
        {
            if (!IsLoaded || entry == null)
                return false;

#if UNITY_EDITOR
            string key = isAvatar ? entry.avatarSpriteKey : entry.roleSpriteKey;
            Sprite target = string.IsNullOrEmpty(key) ? null : BookResourceIndex.Instance.GetSprite(key);
            Sprite current = isAvatar ? entry.avatarSprite : entry.roleSprite;
            if (ReferenceEquals(current, target))
                return false;

            Undo.RecordObject(Catalog, "同步编辑器预览图");
            if (isAvatar)
                entry.avatarSprite = target;
            else
                entry.roleSprite = target;

            EditorUtility.SetDirty(Catalog);
            return true;
#else
            return false;
#endif
        }

        /// <summary>把全部条目的编辑器预览直引同步到资源键，返回改了几条。</summary>
        public int SyncAllEditorSprites()
        {
            if (!IsLoaded)
                return 0;

            int changed = 0;
            List<BookEntry> entries = EntriesSnapshot();
            for (int i = 0; i < entries.Count; i++)
            {
                if (SyncEditorSprite(entries[i], true))
                    changed++;
                if (SyncEditorSprite(entries[i], false))
                    changed++;
            }

            if (changed > 0)
                MarkDirty();

            return changed;
        }
        #endregion

        #region 读取
        /// <summary>派系页签 ID 列表。</summary>
        public List<string> Categories
        {
            get
            {
                var result = new List<string>();
                if (!IsLoaded || _categoriesProp == null)
                    return result;

                for (int i = 0; i < _categoriesProp.arraySize; i++)
                    result.Add(_categoriesProp.GetArrayElementAtIndex(i).stringValue);
                return result;
            }
        }

        /// <summary>Card 种族图标选项。</summary>
        public List<string> RaceOptions
        {
            get
            {
                var result = new List<string>();
                if (!IsLoaded || _raceOptionsProp == null)
                    return result;

                for (int i = 0; i < _raceOptionsProp.arraySize; i++)
                    result.Add(_raceOptionsProp.GetArrayElementAtIndex(i).stringValue);
                return result;
            }
        }

        public int EntryCount
        {
            get { return IsLoaded ? _entriesProp.arraySize : 0; }
        }

        public BookEntry EntryAt(int index)
        {
            if (!IsLoaded || index < 0 || index >= _entriesProp.arraySize)
                return null;

            IReadOnlyList<BookEntry> entries = Catalog.Entries;
            return index < entries.Count ? entries[index] : null;
        }

        /// <summary>当前全部条目的快照列表（可能含 null：被删空槽位）。</summary>
        public List<BookEntry> EntriesSnapshot()
        {
            var result = new List<BookEntry>();
            if (!IsLoaded)
                return result;

            IReadOnlyList<BookEntry> entries = Catalog.Entries;
            for (int i = 0; i < _entriesProp.arraySize; i++)
                result.Add(i < entries.Count ? entries[i] : null);
            return result;
        }

        public int IndexOf(BookEntry entry)
        {
            if (entry == null || !IsLoaded)
                return -1;

            IReadOnlyList<BookEntry> entries = Catalog.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                if (ReferenceEquals(entries[i], entry))
                    return i;
            }
            return -1;
        }

        public List<string> IdsInUse(int exceptIndex = -1)
        {
            var result = new List<string>();
            List<BookEntry> entries = EntriesSnapshot();
            for (int i = 0; i < entries.Count; i++)
            {
                if (i == exceptIndex || entries[i] == null)
                    continue;

                result.Add(entries[i].id);
            }
            return result;
        }
        #endregion

        #region 条目结构操作
        /// <summary>
        /// 在末尾新增条目，返回新条目在数组里的下标。
        ///
        /// 注意：Unity 的 SerializedProperty.arraySize++ 给「可序列化类」数组扩容时，
        /// **新元素复制的是最后一个元素**（不是空对象）。所以这里扩完必须先整段清零，
        /// 否则新条目会带着上一条的名字/ID/图片键/单位键，看起来像"凭空多了一条重复数据"。
        /// </summary>
        public int AddEntry(string categoryId)
        {
            if (!IsLoaded)
                return -1;

            _serialized.Update();
            int index = _entriesProp.arraySize;
            _entriesProp.arraySize = index + 1;

            // 清空新元素的所有字段（含以后新增的字段，用遍历方式，避免漏掉）
            SerializedProperty element = _entriesProp.GetArrayElementAtIndex(index);
            ResetSerializedProperty(element);

            _serialized.ApplyModifiedProperties();

            BookEntry entry = EntryAt(index);
            if (entry != null)
            {
                Undo.RecordObject(Catalog, "新增图鉴条目");
                entry.id = string.Empty;
                entry.displayName = string.Empty;
                entry.categoryId = categoryId ?? string.Empty;
                entry.star = 1;
                entry.prefabKey = string.Empty;
                entry.avatarSpriteKey = string.Empty;
                entry.roleSpriteKey = string.Empty;
                if (!string.IsNullOrEmpty(categoryId) && RaceOptions.Contains(categoryId))
                    entry.raceId = categoryId;
                else if (RaceOptions.Count > 0)
                    entry.raceId = RaceOptions[0];
                else
                    entry.raceId = string.Empty;
                EditorUtility.SetDirty(Catalog);
            }

            _dirty = true;
            RaiseChanged();
            return index;
        }

        /// <summary>把一个序列化属性（含子属性）重置为默认值。</summary>
        private static void ResetSerializedProperty(SerializedProperty property)
        {
            if (property == null)
                return;

            SerializedProperty iterator = property.Copy();
            SerializedProperty end = iterator.GetEndProperty();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;

                switch (iterator.propertyType)
                {
                    case SerializedPropertyType.String:
                        iterator.stringValue = string.Empty;
                        break;
                    case SerializedPropertyType.Integer:
                        iterator.intValue = 0;
                        break;
                    case SerializedPropertyType.Float:
                        iterator.floatValue = 0f;
                        break;
                    case SerializedPropertyType.Boolean:
                        iterator.boolValue = false;
                        break;
                    case SerializedPropertyType.ObjectReference:
                        iterator.objectReferenceValue = null;
                        break;
                    case SerializedPropertyType.Vector2:
                        iterator.vector2Value = Vector2.zero;
                        break;
                    case SerializedPropertyType.Vector2Int:
                        iterator.vector2IntValue = Vector2Int.zero;
                        break;
                    case SerializedPropertyType.Vector3:
                        iterator.vector3Value = Vector3.zero;
                        break;
                    case SerializedPropertyType.Enum:
                        iterator.enumValueIndex = 0;
                        break;
                    case SerializedPropertyType.Color:
                        iterator.colorValue = Color.white;
                        break;
                    default:
                        break;
                }
            }
        }

        /// <summary>复制条目（插在原件后面），返回新条目下标。</summary>
        public int DuplicateEntry(int index)
        {
            if (!IsLoaded)
                return -1;

            BookEntry source = EntryAt(index);
            if (source == null)
                return -1;

            _serialized.Update();
            _entriesProp.InsertArrayElementAtIndex(index);
            _serialized.ApplyModifiedProperties();

            int newIndex = index + 1;
            BookEntry copy = EntryAt(newIndex);
            if (copy != null)
            {
                Undo.RecordObject(Catalog, "复制图鉴条目");
                copy.id = source.id + "_copy";
                copy.displayName = source.displayName + "（副本）";
                copy.categoryId = source.categoryId;
                copy.raceId = source.raceId;
                copy.avatarSpriteKey = source.avatarSpriteKey;
                copy.roleSpriteKey = source.roleSpriteKey;
#if UNITY_EDITOR
                copy.avatarSprite = source.avatarSprite;
                copy.roleSprite = source.roleSprite;
#endif
                copy.star = source.star;
                copy.prefabKey = source.prefabKey;
                copy.repelText = source.repelText;
                copy.quantityText = source.quantityText;
                copy.scopeText = source.scopeText;
                copy.timeText = source.timeText;
                copy.weightText = source.weightText;
                copy.coinText = source.coinText;
                copy.explainText = source.explainText;
                EditorUtility.SetDirty(Catalog);
            }

            _dirty = true;
            RaiseChanged();
            return newIndex;
        }

        public bool DeleteEntry(int index)
        {
            if (!IsLoaded || index < 0 || index >= _entriesProp.arraySize)
                return false;

            _serialized.Update();
            int before = _entriesProp.arraySize;
            _entriesProp.DeleteArrayElementAtIndex(index);
            if (_entriesProp.arraySize == before)
            {
                // 元素是对象引用类型时第一次删除只是置空，需要再删一次。
                _entriesProp.DeleteArrayElementAtIndex(index);
            }
            _serialized.ApplyModifiedProperties();

            _dirty = true;
            RaiseChanged();
            return true;
        }

        public void DeleteEntries(List<int> indices)
        {
            if (!IsLoaded || indices == null || indices.Count == 0)
                return;

            indices.Sort();
            for (int i = indices.Count - 1; i >= 0; i--)
            {
                _serialized.Update();
                int index = indices[i];
                if (index < 0 || index >= _entriesProp.arraySize)
                    continue;

                int before = _entriesProp.arraySize;
                _entriesProp.DeleteArrayElementAtIndex(index);
                if (_entriesProp.arraySize == before)
                    _entriesProp.DeleteArrayElementAtIndex(index);
                _serialized.ApplyModifiedProperties();
            }

            _dirty = true;
            RaiseChanged();
        }

        public void MoveEntry(int from, int to)
        {
            if (!IsLoaded || from == to)
                return;

            _serialized.Update();
            if (from < 0 || from >= _entriesProp.arraySize)
                return;

            to = Mathf.Clamp(to, 0, _entriesProp.arraySize - 1);
            _entriesProp.MoveArrayElement(from, to);
            _serialized.ApplyModifiedProperties();
            _dirty = true;
            RaiseChanged();
        }

        /// <summary>把条目移到指定派系分组的末尾（拖拽换派系用）。</summary>
        public void MoveEntryToCategoryEnd(int index, string categoryId)
        {
            if (!IsLoaded)
                return;

            BookEntry entry = EntryAt(index);
            if (entry == null)
                return;

            Undo.RecordObject(Catalog, "修改条目派系");
            entry.categoryId = categoryId ?? string.Empty;
            EditorUtility.SetDirty(Catalog);

            int last = -1;
            List<BookEntry> entries = EntriesSnapshot();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].categoryId == categoryId)
                    last = i;
            }

            if (last >= 0 && last != index)
                MoveEntry(index, last);
            else
                RaiseChanged();

            _dirty = true;
        }

        /// <summary>清理空槽位（数组里的 null 元素）。</summary>
        public int CompactNulls()
        {
            if (!IsLoaded)
                return 0;

            int removed = 0;
            for (int i = _entriesProp.arraySize - 1; i >= 0; i--)
            {
                _serialized.Update();
                SerializedProperty element = _entriesProp.GetArrayElementAtIndex(i);
                if (element != null && element.FindPropertyRelative("displayName") != null)
                    continue;

                int before = _entriesProp.arraySize;
                _entriesProp.DeleteArrayElementAtIndex(i);
                if (_entriesProp.arraySize == before)
                    _entriesProp.DeleteArrayElementAtIndex(i);
                _serialized.ApplyModifiedProperties();
                removed++;
            }

            if (removed > 0)
            {
                _dirty = true;
                RaiseChanged();
            }
            return removed;
        }

        /// <summary>整理顺序：按（派系顺序, 星级, 角色名）排序，与游戏内展示顺序一致。</summary>
        public void SortEntries()
        {
            if (!IsLoaded)
                return;

            _serialized.Update();
            int n = _entriesProp.arraySize;
            if (n < 2)
                return;

            List<string> categories = Categories;
            for (int i = 0; i < n - 1; i++)
            {
                int best = i;
                for (int j = i + 1; j < n; j++)
                {
                    if (CompareEntries(j, best, categories) < 0)
                        best = j;
                }

                if (best != i)
                    _entriesProp.MoveArrayElement(best, i);
            }

            _serialized.ApplyModifiedProperties();
            _dirty = true;
            RaiseChanged();
        }

        private int CompareEntries(int a, int b, List<string> categories)
        {
            SerializedProperty pa = _entriesProp.GetArrayElementAtIndex(a);
            SerializedProperty pb = _entriesProp.GetArrayElementAtIndex(b);

            int ca = CategoryOrder(GetString(pa, "categoryId"), categories);
            int cb = CategoryOrder(GetString(pb, "categoryId"), categories);
            if (ca != cb)
                return ca.CompareTo(cb);

            int sa = GetInt(pa, "star");
            int sb = GetInt(pb, "star");
            if (sa != sb)
                return sa.CompareTo(sb);

            return string.Compare(GetString(pa, "displayName"), GetString(pb, "displayName"), StringComparison.CurrentCulture);
        }

        private static int CategoryOrder(string categoryId, List<string> categories)
        {
            int index = categories.IndexOf(categoryId);
            return index < 0 ? int.MaxValue : index;
        }

        private static string GetString(SerializedProperty element, string field)
        {
            SerializedProperty prop = element != null ? element.FindPropertyRelative(field) : null;
            return prop != null ? prop.stringValue : string.Empty;
        }

        private static int GetInt(SerializedProperty element, string field)
        {
            SerializedProperty prop = element != null ? element.FindPropertyRelative(field) : null;
            return prop != null ? prop.intValue : 0;
        }
        #endregion

        #region 派系 / 种族选项
        public void SetCategories(List<string> categories)
        {
            if (!IsLoaded || _categoriesProp == null || categories == null)
                return;

            _serialized.Update();
            _categoriesProp.arraySize = categories.Count;
            for (int i = 0; i < categories.Count; i++)
                _categoriesProp.GetArrayElementAtIndex(i).stringValue = categories[i];
            _serialized.ApplyModifiedProperties();
            _dirty = true;
            RaiseChanged();
        }

        public void SetRaceOptions(List<string> raceOptions)
        {
            if (!IsLoaded || _raceOptionsProp == null || raceOptions == null)
                return;

            _serialized.Update();
            _raceOptionsProp.arraySize = raceOptions.Count;
            for (int i = 0; i < raceOptions.Count; i++)
                _raceOptionsProp.GetArrayElementAtIndex(i).stringValue = raceOptions[i];
            _serialized.ApplyModifiedProperties();
            _dirty = true;
            RaiseChanged();
        }

        /// <summary>生成不与现有条目冲突的唯一 ID。</summary>
        public string MakeUniqueId(string preferred, int selfIndex = -1)
        {
            string baseId = SanitizeId(string.IsNullOrEmpty(preferred) ? "entry" : preferred);
            List<string> used = IdsInUse(selfIndex);
            if (!used.Contains(baseId))
                return baseId;

            for (int i = 2; i < 10000; i++)
            {
                string candidate = baseId + "_" + i;
                if (!used.Contains(candidate))
                    return candidate;
            }
            return baseId + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        /// <summary>把任意文本清理成安全的 ID 片段（保留中文与字母数字，其余换成下划线）。</summary>
        public static string SanitizeId(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "entry";

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(c);
                else if (c == ' ' || c == '.')
                    sb.Append('_');
            }

            string result = sb.ToString().Trim('_');
            return string.IsNullOrEmpty(result) ? "entry" : result;
        }
        #endregion

        #region 备份
        /// <summary>资产文件的绝对路径（Unity 的工作目录就是工程根，这里显式拼接以免受启动参数影响）。</summary>
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
            if (!IsLoaded || string.IsNullOrEmpty(AssetPath))
                return null;

            string sourceFile = AbsoluteAssetFile();
            if (string.IsNullOrEmpty(sourceFile) || !File.Exists(sourceFile))
                return null;

            string folder = BookEditorPrefs.BackupFolder;
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[图鉴编辑器] 备份目录创建失败：" + e.Message);
                return null;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string safeReason = SanitizeId(reason);
            string target = Path.Combine(folder, Path.GetFileNameWithoutExtension(AssetPath) + "_" + stamp + "_" + safeReason + ".asset");

            try
            {
                int n = 2;
                while (File.Exists(target))
                {
                    target = Path.Combine(folder, Path.GetFileNameWithoutExtension(AssetPath) + "_" + stamp + "_" + safeReason + "_" + n + ".asset");
                    n++;
                }

                File.Copy(sourceFile, target, false);
                TrimBackups(folder, BookEditorPrefs.KeepBackupCount);
                return target;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[图鉴编辑器] 备份失败：" + e.Message);
                return null;
            }
        }

        /// <summary>
        /// 清理旧备份：**每个资产**保留最新的 keep 份。
        ///
        /// 旧实现是"整个目录按文件名排序、删掉前 keep 个"。文件名形如
        /// `资产名_yyyyMMdd_HHmmss_原因.asset` —— **资产名前缀会主导排序**，时间戳根本轮不到，
        /// 于是删掉的不是最旧的，而是"名字靠前的那个资产"的备份，并且会一路删到它一份不剩，
        /// 另一个资产更老的备份反而留着。
        ///
        /// 第二个修正：只处理**认得出是备份**的文件（`资产名_yyyyMMdd_HHmmss_原因.asset`）。
        /// 备份目录是可以在设置页指到任意文件夹的（无二次确认），万一指到 `Assets` 下面，
        /// 旧逻辑的 `*.asset` 通配删除会把**真正的资产文件**一起删掉。认不出的一律不碰。
        ///
        /// 与建筑编辑器 / 兵种编辑器同一套做法（那边早有这个修复，这里是补上）；
        /// 关卡编辑器的同名函数是同一天补的，两边代码保持逐行对应，方便以后对账。
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

                var byAsset = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length; i++)
                {
                    string assetName;
                    string stamp;
                    if (!TryParseBackupName(Path.GetFileName(files[i]), out assetName, out stamp))
                        continue;   // 认不出来就不碰：可能是被指到资产目录里的真资产

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

                    // 同一个资产分组内，前缀相同、时间戳定宽 —— 所以名字序就是时间序
                    list.Sort(StringComparer.Ordinal);
                    int removeCount = list.Count - keep;
                    for (int i = 0; i < removeCount; i++)
                        File.Delete(list[i]);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[图鉴编辑器] 清理旧备份失败：" + e.Message);
            }
        }

        /// <summary>
        /// 解析备份文件名 `资产名_yyyyMMdd_HHmmss_原因.asset`（原因后面还可能带冲突序号 `_2`）。
        /// 认不出来返回 false —— 调用方必须当成"别碰这个文件"（真正的资产文件就是认不出来的那种）。
        /// 资产名本身可以带下划线，所以从左边找**第一组**「8 位数字 + 6 位数字」来定位时间戳。
        /// </summary>
        private static bool TryParseBackupName(string fileName, out string assetName, out string stamp)
        {
            assetName = null;
            stamp = null;

            string name = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(name))
                return false;

            string[] parts = name.Split('_');
            if (parts.Length < 4)   // 资产名 _ yyyyMMdd _ HHmmss _ 原因，至少 4 段
                return false;

            for (int i = 1; i < parts.Length - 2; i++)
            {
                if (parts[i].Length != 8 || parts[i + 1].Length != 6)
                    continue;
                if (!IsAllDigits(parts[i]) || !IsAllDigits(parts[i + 1]))
                    continue;

                assetName = string.Join("_", parts, 0, i);
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
        /// 同一次备份的多个文件靠在一起，但不同资产之间是按资产名排的。
        /// </summary>
        public static List<string> ListBackups()
        {
            var result = new List<string>();
            string folder = BookEditorPrefs.BackupFolder;
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

        /// <summary>用备份文件覆盖当前资产（危险操作，调用方必须先确认）。</summary>
        public bool RestoreBackup(string backupFile)
        {
            if (!IsLoaded || string.IsNullOrEmpty(backupFile) || !File.Exists(backupFile))
                return false;

            try
            {
                BackupNow("恢复前");
                File.Copy(backupFile, AbsoluteAssetFile(), true);
                AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
                Load(AssetPath);
                RaiseChanged();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[图鉴编辑器] 恢复备份失败：" + e.Message);
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