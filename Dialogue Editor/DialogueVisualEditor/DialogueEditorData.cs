using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace CageFightDialogueTool
{
    /// <summary>对话编辑器的编辑器偏好（前缀 CageFight.DialogueVisualEditor.）。</summary>
    public static class DialoguePrefs
    {
        private const string Prefix = "CageFight.DialogueVisualEditor.";

        public static string LastStagePath
        {
            get { return EditorPrefs.GetString(Prefix + "LastStagePath", string.Empty); }
            set { EditorPrefs.SetString(Prefix + "LastStagePath", value ?? string.Empty); }
        }

        public static string LastSeriesPath
        {
            get { return EditorPrefs.GetString(Prefix + "LastSeriesPath", string.Empty); }
            set { EditorPrefs.SetString(Prefix + "LastSeriesPath", value ?? string.Empty); }
        }

        /// <summary>左栏宽度。</summary>
        public static float SideWidth
        {
            get { return EditorPrefs.GetFloat(Prefix + "SideWidth", 264f); }
            set { EditorPrefs.SetFloat(Prefix + "SideWidth", value); }
        }

        public static int Page
        {
            get { return EditorPrefs.GetInt(Prefix + "Page", 0); }
            set { EditorPrefs.SetInt(Prefix + "Page", value); }
        }

        /// <summary>卡片上是否显示"会长成什么样"的预览块。</summary>
        public static bool ShowPreview
        {
            get { return EditorPrefs.GetBool(Prefix + "ShowPreview", true); }
            set { EditorPrefs.SetBool(Prefix + "ShowPreview", value); }
        }

        public static bool AutoBackupOnSave
        {
            get { return EditorPrefs.GetBool(Prefix + "AutoBackupOnSave", true); }
            set { EditorPrefs.SetBool(Prefix + "AutoBackupOnSave", value); }
        }

        public static int KeepBackupCount
        {
            get { return EditorPrefs.GetInt(Prefix + "KeepBackupCount", 30); }
            set { EditorPrefs.SetInt(Prefix + "KeepBackupCount", Mathf.Clamp(value, 1, 500)); }
        }
    }

    /// <summary>一个关卡 + 它的「局外·选关界面」导游台词。</summary>
    public sealed class StageDialogueInfo
    {
        public string Path;
        public string Name;
        public StageConfig Config;
        public int StageId;
        public string TypeLabel;

        /// <summary>列表里显示的一行文字。</summary>
        public string CardLabel
        {
            get { return "Stage" + StageId + "　" + Name; }
        }

        public List<GuideDialogueEntry> Lines
        {
            get { return Config != null ? Config.guideDialogues : null; }
        }

        public int LineCount
        {
            get { return Lines != null ? Lines.Count : 0; }
        }

        /// <summary>条目里真正有文案的条数（运行时空文案会被跳过）。</summary>
        public int SpokenCount
        {
            get
            {
                List<GuideDialogueEntry> lines = Lines;
                if (lines == null)
                    return 0;

                int n = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i] != null && !DialogueCore.IsBlank(lines[i].text))
                        n++;
                }
                return n;
            }
        }
    }

    /// <summary>一个局内对话系列资产（DialogueSeriesSO）。</summary>
    public sealed class DialogueSeriesInfo
    {
        public string Path;
        public string Name;
        public DialogueSeriesSO Asset;

        public List<DialogueConfigSO> ReadLines()
        {
            return DialogueTool.ReadSeriesLines(Asset);
        }
    }

    /// <summary>一条局内对话配置资产（DialogueConfigSO）。</summary>
    public sealed class DialogueConfigInfo
    {
        public string Path;
        public string Name;
        public DialogueConfigSO Asset;
        public int RefCount;      // 被多少个系列引用

        public string Text
        {
            get { return Asset != null ? DialogueCore.Clean(Asset.Text) : string.Empty; }
        }

        public string PortraitKey
        {
            get { return Asset != null ? DialogueCore.Clean(Asset.PortraitSpriteKey) : string.Empty; }
        }
    }

    /// <summary>
    /// 对话编辑器的数据层：资产索引、注册表只读访问、私有序列化字段读写、脏标记与保存、备份。
    ///
    /// 只**只读**使用运行时类型（StageConfig / DialogueConfigSO / DialogueSeriesSO /
    /// SpriteRegistry / AudioRegistry），写数据只写对话相关的三个资产：
    ///   · 局外：StageN.asset 的 guideDialogues 字段（public 字段，直接改）
    ///   · 局内：DialogueConfigSO / DialogueSeriesSO（private [SerializeField]，只能反射写）
    /// 游戏源码一行不改。
    /// </summary>
    public static class DialogueTool
    {
        public const string StagesFolder = "Assets/RemoteResource/Stages";
        public const string SpriteRegistryPath = "Assets/RemoteResource/SpriteRegistry.asset";
        public const string AudioRegistryPath = "Assets/RemoteResource/AudioRegistry.asset";

        /// <summary>新建对话资产的默认目录（工程里现有的对话资产就在这下面）。</summary>
        public const string DefaultScriptFolder = "Assets/Resource/LocalResource/Text";

        public const string DefaultToolFolderName = "Dialogue Editor";
        private const string ToolMarkerFile = "DialogueEditorData.cs";

        #region 工程 / 工具目录
        public static string RepoRoot
        {
            get
            {
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                return Path.GetFullPath(Path.Combine(projectRoot, ".."));
            }
        }

        private static string _cachedToolRoot;

        /// <summary>工具目录：先按默认名找，找不到就扫仓库根，用「里面有 DialogueVisualEditor\DialogueEditorData.cs」识别。</summary>
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
                   File.Exists(Path.Combine(folder, "DialogueVisualEditor", ToolMarkerFile));
        }

        public static string BackupFolder
        {
            get { return Path.Combine(ToolRoot, "DialogueBackups"); }
        }

        private static string _cachedSourceStamp;

        /// <summary>本工具的版本标识（取挂载目录里最新 .cs 的修改时间）；必须缓存，状态栏每帧都画它。</summary>
        public static string SourceStamp()
        {
            if (!string.IsNullOrEmpty(_cachedSourceStamp))
                return _cachedSourceStamp;

            try
            {
                string dir = Path.Combine(ToolRoot, "DialogueVisualEditor");
                if (Directory.Exists(dir))
                {
                    DateTime newest = DateTime.MinValue;
                    string[] files = Directory.GetFiles(dir, "*.cs");
                    for (int i = 0; i < files.Length; i++)
                    {
                        DateTime t = File.GetLastWriteTime(files[i]);
                        if (t > newest)
                            newest = t;
                    }
                    if (newest != DateTime.MinValue)
                        _cachedSourceStamp = newest.ToString("MM-dd HH:mm");
                }
            }
            catch (Exception)
            {
                // 取不到就不显示。
            }

            if (string.IsNullOrEmpty(_cachedSourceStamp))
                _cachedSourceStamp = "?";

            return _cachedSourceStamp;
        }
        #endregion

        #region 索引
        private static bool _built;
        private static readonly List<StageDialogueInfo> _stages = new List<StageDialogueInfo>();
        private static readonly List<DialogueSeriesInfo> _series = new List<DialogueSeriesInfo>();
        private static readonly List<DialogueConfigInfo> _configs = new List<DialogueConfigInfo>();
        private static readonly List<string> _spriteKeys = new List<string>();
        private static readonly List<string> _audioKeys = new List<string>();
        private static readonly Dictionary<string, Sprite> _sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly Dictionary<string, AudioClip> _audios = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        private static readonly HashSet<string> _stagePreloadSprites = new HashSet<string>(StringComparer.Ordinal);

        public static bool SpriteRegistryFound { get; private set; }
        public static bool AudioRegistryFound { get; private set; }

        public static void Invalidate()
        {
            _built = false;
            _cachedSourceStamp = null;
        }

        private static void EnsureBuilt()
        {
            if (_built)
                return;
            Rebuild();
        }

        /// <summary>重扫全部相关资产（关卡 / 对话配置 / 对话系列 / 两张注册表的 Key）。</summary>
        public static void Rebuild()
        {
            _stages.Clear();
            _series.Clear();
            _configs.Clear();
            _spriteKeys.Clear();
            _audioKeys.Clear();
            _sprites.Clear();
            _audios.Clear();
            _stagePreloadSprites.Clear();

            BuildStages();
            BuildDialogues();
            BuildSprites();
            BuildAudios();

            _stages.Sort((a, b) =>
            {
                int c = a.StageId.CompareTo(b.StageId);
                return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
            });
            _series.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            _configs.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            _built = true;
        }

        private static void BuildStages()
        {
            string[] guids = AssetDatabase.FindAssets("t:StageConfig");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                StageConfig config = AssetDatabase.LoadAssetAtPath<StageConfig>(path);
                if (config == null)
                    continue;

                var info = new StageDialogueInfo
                {
                    Path = path,
                    Name = Path.GetFileNameWithoutExtension(path),
                    Config = config,
                    StageId = config.stageId,
                    TypeLabel = TypeLabelOf(config.stageType)
                };
                _stages.Add(info);

                // 局内对话的立绘必须由"当前关卡的预载清单"提供，所以把每关的 sprites 汇总起来。
                if (config.sprites == null)
                    continue;

                for (int k = 0; k < config.sprites.Count; k++)
                {
                    string key = config.sprites[k];
                    if (!string.IsNullOrEmpty(key))
                        _stagePreloadSprites.Add(key);
                }
            }
        }

        private static void BuildDialogues()
        {
            var refCount = new Dictionary<DialogueConfigSO, int>();

            string[] seriesGuids = AssetDatabase.FindAssets("t:DialogueSeriesSO");
            for (int i = 0; i < seriesGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(seriesGuids[i]);
                DialogueSeriesSO asset = AssetDatabase.LoadAssetAtPath<DialogueSeriesSO>(path);
                if (asset == null)
                    continue;

                _series.Add(new DialogueSeriesInfo
                {
                    Path = path,
                    Name = Path.GetFileNameWithoutExtension(path),
                    Asset = asset
                });

                List<DialogueConfigSO> lines = ReadSeriesLines(asset);
                for (int k = 0; k < lines.Count; k++)
                {
                    DialogueConfigSO line = lines[k];
                    if (line == null)
                        continue;

                    int n;
                    refCount.TryGetValue(line, out n);
                    refCount[line] = n + 1;
                }
            }

            string[] configGuids = AssetDatabase.FindAssets("t:DialogueConfigSO");
            for (int i = 0; i < configGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(configGuids[i]);
                DialogueConfigSO asset = AssetDatabase.LoadAssetAtPath<DialogueConfigSO>(path);
                if (asset == null)
                    continue;

                int n;
                refCount.TryGetValue(asset, out n);

                _configs.Add(new DialogueConfigInfo
                {
                    Path = path,
                    Name = Path.GetFileNameWithoutExtension(path),
                    Asset = asset,
                    RefCount = n
                });
            }
        }

        private static void BuildSprites()
        {
            SpriteRegistry registry = AssetDatabase.LoadAssetAtPath<SpriteRegistry>(SpriteRegistryPath);
            if (registry == null)
            {
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
                if (mapping == null || string.IsNullOrEmpty(mapping.key) || _sprites.ContainsKey(mapping.key))
                    continue;   // 与运行时一致：重复 Key 只认第一个

                _spriteKeys.Add(mapping.key);
                _sprites[mapping.key] = mapping.sprite;   // 编辑器下的强引用，仅用于预览
            }
        }

        private static void BuildAudios()
        {
            AudioRegistry registry = AssetDatabase.LoadAssetAtPath<AudioRegistry>(AudioRegistryPath);
            if (registry == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:AudioRegistry");
                if (guids.Length > 0)
                    registry = AssetDatabase.LoadAssetAtPath<AudioRegistry>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            AudioRegistryFound = registry != null;
            if (registry == null || registry.mappings == null)
                return;

            for (int i = 0; i < registry.mappings.Count; i++)
            {
                AudioMapping mapping = registry.mappings[i];
                if (mapping == null || string.IsNullOrEmpty(mapping.key) || _audios.ContainsKey(mapping.key))
                    continue;

                _audioKeys.Add(mapping.key);
                _audios[mapping.key] = mapping.audioClip;
            }
        }

        public static IReadOnlyList<StageDialogueInfo> Stages { get { EnsureBuilt(); return _stages; } }
        public static IReadOnlyList<DialogueSeriesInfo> Series { get { EnsureBuilt(); return _series; } }
        public static IReadOnlyList<DialogueConfigInfo> Configs { get { EnsureBuilt(); return _configs; } }
        public static IReadOnlyList<string> SpriteKeys { get { EnsureBuilt(); return _spriteKeys; } }
        public static IReadOnlyList<string> AudioKeys { get { EnsureBuilt(); return _audioKeys; } }

        public static bool HasSpriteKey(string key)
        {
            EnsureBuilt();
            return !string.IsNullOrEmpty(key) && _sprites.ContainsKey(key);
        }

        public static bool HasAudioKey(string key)
        {
            EnsureBuilt();
            return !string.IsNullOrEmpty(key) && _audios.ContainsKey(key);
        }

        public static Sprite GetSprite(string key)
        {
            EnsureBuilt();
            Sprite sprite;
            return !string.IsNullOrEmpty(key) && _sprites.TryGetValue(key, out sprite) ? sprite : null;
        }

        public static AudioClip GetAudio(string key)
        {
            EnsureBuilt();
            AudioClip clip;
            return !string.IsNullOrEmpty(key) && _audios.TryGetValue(key, out clip) ? clip : null;
        }

        /// <summary>这个立绘 Key 有没有出现在任何一关的预载清单（StageConfig.sprites）里。</summary>
        public static bool IsPreloadedByAnyStage(string key)
        {
            EnsureBuilt();
            return !string.IsNullOrEmpty(key) && _stagePreloadSprites.Contains(key);
        }

        /// <summary>哪些关卡的预载清单里有这个 Key（局内对话要靠它才拿得到立绘）。</summary>
        public static List<string> StagesPreloading(string key)
        {
            EnsureBuilt();
            var result = new List<string>();
            if (string.IsNullOrEmpty(key))
                return result;

            for (int i = 0; i < _stages.Count; i++)
            {
                StageConfig config = _stages[i].Config;
                if (config == null || config.sprites == null)
                    continue;

                if (config.sprites.Contains(key))
                    result.Add("Stage" + _stages[i].StageId);
            }

            return result;
        }

        public static List<string> SearchSpriteKeys(string query, int max = 400)
        {
            EnsureBuilt();
            return SearchKeys(_spriteKeys, query, max);
        }

        public static List<string> SearchAudioKeys(string query, int max = 400)
        {
            EnsureBuilt();
            return SearchKeys(_audioKeys, query, max);
        }

        private static List<string> SearchKeys(List<string> source, string query, int max)
        {
            var result = new List<string>();
            string q = (query ?? string.Empty).Trim();

            for (int i = 0; i < source.Count && result.Count < max; i++)
            {
                if (q.Length == 0 || source[i].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(source[i]);
            }

            return result;
        }

        public static DialogueSeriesInfo FindSeries(string path)
        {
            EnsureBuilt();
            for (int i = 0; i < _series.Count; i++)
            {
                if (string.Equals(_series[i].Path, path, StringComparison.OrdinalIgnoreCase))
                    return _series[i];
            }
            return null;
        }

        public static DialogueConfigInfo FindConfig(DialogueConfigSO asset)
        {
            EnsureBuilt();
            for (int i = 0; i < _configs.Count; i++)
            {
                if (_configs[i].Asset == asset)
                    return _configs[i];
            }
            return null;
        }

        private static string TypeLabelOf(UserGlobalInfo.StageType type)
        {
            switch (type)
            {
                case UserGlobalInfo.StageType.Defense: return "防御";
                case UserGlobalInfo.StageType.Attack: return "进攻";
                case UserGlobalInfo.StageType.Boss: return "Boss";
                default: return type.ToString();
            }
        }
        #endregion

        #region 私有序列化字段（DialogueConfigSO / DialogueSeriesSO 的字段都是 private [SerializeField]）
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

        /// <summary>读一个对话系列的台词列表（series.dialogues 是 private [SerializeField] List）。</summary>
        public static List<DialogueConfigSO> ReadSeriesLines(DialogueSeriesSO series)
        {
            if (series == null)
                return new List<DialogueConfigSO>();

            var list = GetPrivate(series, "dialogues") as List<DialogueConfigSO>;
            return list ?? new List<DialogueConfigSO>();
        }

        /// <summary>整份写回一个系列的台词列表（调用前调用方自己负责 Undo 与脏标记）。</summary>
        public static bool WriteSeriesLines(DialogueSeriesSO series, List<DialogueConfigSO> lines)
        {
            if (series == null)
                return false;

            return SetPrivate(series, "dialogues", lines);
        }

        public static string ReadConfigText(DialogueConfigSO config)
        {
            return config != null ? DialogueCore.Clean(config.Text) : string.Empty;
        }

        public static void WriteConfigText(DialogueConfigSO config, string text)
        {
            if (config == null)
                return;

            Undo.RecordObject(config, "修改对话文案");
            SetPrivate(config, "text", text ?? string.Empty);
        }

        public static string ReadConfigPortraitKey(DialogueConfigSO config)
        {
            return config != null ? DialogueCore.Clean(config.PortraitSpriteKey) : string.Empty;
        }

        public static void WriteConfigPortraitKey(DialogueConfigSO config, string key)
        {
            if (config == null)
                return;

            Undo.RecordObject(config, "修改立绘 Key");
            SetPrivate(config, "portraitSpriteKey", (key ?? string.Empty).Trim());
        }
        #endregion

        #region 脏标记 / 保存 / 撤销快照
        private static readonly HashSet<UnityEngine.Object> _dirty = new HashSet<UnityEngine.Object>();

        public static bool IsDirty { get { return _dirty.Count > 0; } }
        public static int DirtyCount { get { return _dirty.Count; } }

        public static void MarkDirty(UnityEngine.Object asset)
        {
            if (asset == null)
                return;

            _dirty.Add(asset);
            EditorUtility.SetDirty(asset);
        }

        public static void ClearDirty()
        {
            _dirty.Clear();
        }

        public static List<UnityEngine.Object> DirtyObjects()
        {
            var list = new List<UnityEngine.Object>(_dirty);
            list.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b)));
            return list;
        }

        /// <summary>保存所有改过的资产；返回给人看的结果文字。</summary>
        public static string SaveAll(bool backup)
        {
            if (_dirty.Count == 0)
                return "没有需要保存的改动。";

            var paths = new List<string>();
            foreach (UnityEngine.Object asset in _dirty)
            {
                string path = AssetDatabase.GetAssetPath(asset);
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }
            paths.Sort(StringComparer.OrdinalIgnoreCase);

            int backedUp = 0;
            if (backup)
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    if (BackupFile(paths[i], "保存前") != null)
                        backedUp++;
                }
            }

            AssetDatabase.SaveAssets();

            int saved = _dirty.Count;
            _dirty.Clear();

            return "已保存 " + saved + " 个资产" +
                   (backup ? "（备份 " + backedUp + " 份到 DialogueBackups）" : string.Empty) + "。";
        }
        #endregion

        #region 新建资产
        public static DialogueConfigSO CreateConfig(string folder, out string path, out string error)
        {
            path = null;
            error = null;

            if (!EnsureFolder(folder, out error))
                return null;

            path = AssetDatabase.GenerateUniqueAssetPath(folder + "/NewDialogueConfig.asset");
            var asset = ScriptableObject.CreateInstance<DialogueConfigSO>();
            AssetDatabase.CreateAsset(asset, path);
            WriteConfigText(asset, "（新对话：在这里写文案）");
            MarkDirty(asset);
            EditorGUIUtility.PingObject(asset);
            return asset;
        }

        public static DialogueSeriesSO CreateSeries(string folder, out string path, out string error)
        {
            path = null;
            error = null;

            if (!EnsureFolder(folder, out error))
                return null;

            path = AssetDatabase.GenerateUniqueAssetPath(folder + "/NewDialogueSeries.asset");
            var asset = ScriptableObject.CreateInstance<DialogueSeriesSO>();
            AssetDatabase.CreateAsset(asset, path);
            MarkDirty(asset);
            EditorGUIUtility.PingObject(asset);
            return asset;
        }

        private static bool EnsureFolder(string folder, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(folder))
            {
                error = "目录为空。";
                return false;
            }

            if (AssetDatabase.IsValidFolder(folder))
                return true;

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            {
                error = "目录不合法：" + folder;
                return false;
            }

            if (!AssetDatabase.IsValidFolder(parent) && !EnsureFolder(parent, out error))
                return false;

            string guid = AssetDatabase.CreateFolder(parent, leaf);
            if (string.IsNullOrEmpty(guid))
            {
                error = "创建目录失败：" + folder;
                return false;
            }

            return true;
        }

        /// <summary>把一条已有配置追加到某个系列末尾。</summary>
        public static bool AppendToSeries(DialogueSeriesSO series, DialogueConfigSO config)
        {
            if (series == null || config == null)
                return false;

            List<DialogueConfigSO> lines = ReadSeriesLines(series);
            Undo.RecordObject(series, "对话系列加一条");
            lines.Add(config);
            WriteSeriesLines(series, lines);
            MarkDirty(series);
            return true;
        }
        #endregion

        #region 备份
        public static string BackupFile(string assetPath, string reason)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string sourceFile = Path.IsPathRooted(assetPath) ? assetPath : Path.GetFullPath(Path.Combine(projectRoot, assetPath));
            if (!File.Exists(sourceFile))
                return null;

            string folder = BackupFolder;
            try
            {
                Directory.CreateDirectory(folder);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[对话编辑器] 备份目录创建失败：" + e.Message);
                return null;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string baseName = Path.GetFileNameWithoutExtension(assetPath) + "_" + stamp + "_" + Sanitize(reason);
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

                // .meta 一起备份：将来要还原时 GUID 不变，引用不会断。
                string sourceMeta = sourceFile + ".meta";
                if (File.Exists(sourceMeta))
                    File.Copy(sourceMeta, target + ".meta", true);

                TrimBackups();
                return target;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[对话编辑器] 备份失败：" + e.Message);
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

        /// <summary>每个资产只保留最新的 N 份备份（按"资产名"分组，不是整个目录一起排）。</summary>
        public static void TrimBackups()
        {
            string folder = BackupFolder;
            if (!Directory.Exists(folder))
                return;

            try
            {
                string[] files = Directory.GetFiles(folder, "*.asset");
                var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                var stamps = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileNameWithoutExtension(files[i]);
                    int cut = name.LastIndexOf('_');
                    string assetName = name;
                    DateTime stamp = File.GetLastWriteTime(files[i]);

                    // 名字形如 <资产名>_yyyyMMdd_HHmmss_<原因>[_n]：从右往左找时间戳。
                    string[] parts = name.Split('_');
                    if (parts.Length >= 3)
                    {
                        int stampIndex = -1;
                        for (int p = 1; p < parts.Length - 1; p++)
                        {
                            if (parts[p].Length == 8 && parts[p + 1].Length == 6 &&
                                IsDigits(parts[p]) && IsDigits(parts[p + 1]))
                            {
                                stampIndex = p;
                                break;
                            }
                        }

                        if (stampIndex > 0)
                        {
                            assetName = string.Join("_", parts, 0, stampIndex);
                            DateTime parsed;
                            if (DateTime.TryParseExact(parts[stampIndex] + parts[stampIndex + 1], "yyyyMMddHHmmss",
                                    System.Globalization.CultureInfo.InvariantCulture,
                                    System.Globalization.DateTimeStyles.None, out parsed))
                                stamp = parsed;
                        }
                    }

                    List<string> list;
                    if (!groups.TryGetValue(assetName, out list))
                    {
                        list = new List<string>();
                        groups[assetName] = list;
                    }
                    list.Add(files[i]);
                    stamps[files[i]] = stamp;
                }

                int keep = DialoguePrefs.KeepBackupCount;
                foreach (KeyValuePair<string, List<string>> pair in groups)
                {
                    List<string> list = pair.Value;
                    list.Sort((a, b) => stamps[b].CompareTo(stamps[a]));

                    for (int i = keep; i < list.Count; i++)
                    {
                        File.Delete(list[i]);
                        if (File.Exists(list[i] + ".meta"))
                            File.Delete(list[i] + ".meta");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[对话编辑器] 清理旧备份失败：" + e.Message);
            }
        }

        private static bool IsDigits(string text)
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

        /// <summary>最近的备份文件（给"回滚"用），按时间倒序。</summary>
        public static List<string> ListBackups()
        {
            var result = new List<string>();
            string folder = BackupFolder;
            if (!Directory.Exists(folder))
                return result;

            try
            {
                result.AddRange(Directory.GetFiles(folder, "*.asset"));
            }
            catch (Exception)
            {
                return result;
            }

            result.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
            return result;
        }

        /// <summary>把一份备份还原回目标资产；还原前会先把当前状态再备份一份。</summary>
        public static bool RestoreBackup(string backupFile, string targetAssetPath)
        {
            if (string.IsNullOrEmpty(backupFile) || !File.Exists(backupFile) || string.IsNullOrEmpty(targetAssetPath))
                return false;

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string target = Path.IsPathRooted(targetAssetPath)
                ? targetAssetPath
                : Path.GetFullPath(Path.Combine(projectRoot, targetAssetPath));

            if (!File.Exists(target))
                return false;

            BackupFile(targetAssetPath, "还原前");
            File.Copy(backupFile, target, true);
            AssetDatabase.ImportAsset(targetAssetPath, ImportAssetOptions.ForceUpdate);
            Invalidate();
            return true;
        }
        #endregion
    }
}
