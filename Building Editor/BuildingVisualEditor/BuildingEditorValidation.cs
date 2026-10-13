using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CageFightBuildingTool
{
    public enum BuildingIssueLevel
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>一条体检结论。Code 是稳定标识（便于以后按代码屏蔽某条规则）。</summary>
    public sealed class BuildingIssue
    {
        public BuildingIssueLevel Level;
        public string Code = string.Empty;
        public string Title = string.Empty;
        public string Detail = string.Empty;

        /// <summary>资产级问题（种族建筑表、跨建筑比较）没有 BuildingPath。</summary>
        public string BuildingPath = string.Empty;
        public string BuildingName = string.Empty;

        public bool Fixable;
        public string SuggestedValue;

        public Color LevelColor
        {
            get
            {
                switch (Level)
                {
                    case BuildingIssueLevel.Error: return BuildingEditorUI.Error;
                    case BuildingIssueLevel.Warning: return BuildingEditorUI.Warn;
                    default: return BuildingEditorUI.Muted;
                }
            }
        }

        public string LevelLabel
        {
            get
            {
                switch (Level)
                {
                    case BuildingIssueLevel.Error: return "[错误]";
                    case BuildingIssueLevel.Warning: return "[警告]";
                    default: return "[提示]";
                }
            }
        }
    }

    /// <summary>
    /// 通过 SerializedObject 读组件的字段。
    ///
    /// 为什么全部走 SerializedObject：建筑的绝大多数参数是<b>私有 [SerializeField]</b>
    /// （BuildUP.removable、6 个拆除表现参数、BuildingTraining.panelPrefabKey / avatarRenderer、
    /// BuildingTowerAI 的那些…），直接读字段编译不过，而 SerializedObject 既不受访问级别限制，
    /// 又和写入路径（Inspector 同款）完全一致。
    /// </summary>
    public static class BuildingFields
    {
        /// <summary>
        /// 按目标对象缓存 SerializedObject。
        ///
        /// <b>这是本工具最要紧的一处性能修复</b>：每次 new SerializedObject(...) 都要重建整棵属性树，
        /// 而体检要对 9 个建筑 × 十几个字段逐个读 → 一次体检就是几百次重建，界面直接卡住。
        /// 读之前 Update() 保证拿到的是最新值（别处改过之后也准）。
        ///
        /// <b>2026-10-05 第三次卡顿排查：真凶是"每次读都 Update()"</b>
        ///
        /// 上一轮（README R1.3）修掉了"每帧 new SerializedObject"（**构造**成本）和每帧磁盘 I/O，
        /// 但缓存命中那条分支里的 `Update()` 留了下来 —— 于是变成"构造只一次、**每次读都重新序列化一遍**"。
        /// 量级：绘制一帧约 19~22 次 Update（7 个 Edit 各 2 次 + 预览卡 3~6 次 + 占地 2 次），
        /// 一次体检 19 处读取 × 9 个建筑 = **171 次 Update + 171 次 FindProperty**，
        /// 而编辑时每敲一个键都把体检标脏 → 每 0.2 秒重来一遍（节流只降频率、没降单次成本）。
        ///
        /// 为什么前两轮没抓到：那两轮的排查方法是 grep
        /// `AssetDatabase.*` / `File.*` / `EditorPrefs.Get*` / `FindObjectsOfType` / `new SerializedObject` /
        /// `AssetPreview.*` —— **`SerializedObject.Update()` 不在那张清单里**。
        /// 对比：关卡编辑器与兵种编辑器在每帧路径上 `Update()` 是 **0 次**（它们的数据模型字段是公开的，
        /// 直接读；图鉴的 9 次全在用户操作的写路径上）。只有本工具必须走 SerializedObject
        /// （建筑组件的字段基本是私有的），所以只有它卡。
        ///
        /// 现在的规矩：
        /// · **读**（每帧很多次）—— 每趟 UI 对同一个对象最多 Update 一次，用 <see cref="BeginPass"/> 划趟；
        /// · **写**（用户操作，很少）—— 走 <see cref="GetSerializedForWrite"/>，每次都强制刷新。
        /// </summary>
        private static readonly Dictionary<int, SerializedObject> Cache = new Dictionary<int, SerializedObject>();

        /// <summary>本趟 UI 里已经刷新过的对象。读路径靠它把 N 次 Update 压成 1 次。</summary>
        private static readonly HashSet<int> RefreshedThisPass = new HashSet<int>();

        /// <summary>
        /// 每趟 UI 开头调一次（见 `OnGUI`），表示"接下来这一批读取各自可以刷新一次"。
        /// 不调的话读到的仍是上次刷新时的值 —— 所以它必须排在所有绘制与体检之前。
        /// </summary>
        public static void BeginPass()
        {
            RefreshedThisPass.Clear();
        }

        /// <summary>读路径：同一个对象每趟最多 Update() 一次。</summary>
        public static SerializedObject GetSerialized(UnityEngine.Object target)
        {
            return GetSerialized(target, false);
        }

        /// <summary>
        /// 写路径：**每次调用都强制 Update()**。
        ///
        /// 必须这样：调用方改完属性会 `ApplyModifiedProperties()`，那是"把整棵属性树写回对象"——
        /// 如果树是旧的，就会连同别处刚做的改动一起覆盖掉。写是用户操作、频率低，这点刷新成本无所谓。
        /// </summary>
        public static SerializedObject GetSerializedForWrite(UnityEngine.Object target)
        {
            return GetSerialized(target, true);
        }

        private static SerializedObject GetSerialized(UnityEngine.Object target, bool forceRefresh)
        {
            if (target == null)
                return null;

            int id = target.GetInstanceID();
            SerializedObject serialized;
            if (!Cache.TryGetValue(id, out serialized) || serialized == null || serialized.targetObject != target)
            {
                serialized = new SerializedObject(target);
                Cache[id] = serialized;
                serialized.Update();
                RefreshedThisPass.Add(id);
            }
            else if (forceRefresh)
            {
                serialized.Update();
                RefreshedThisPass.Add(id);
            }
            else if (RefreshedThisPass.Add(id))
            {
                // Add 返回 true = 本趟第一次碰它 → 刷一次；之后本趟再读同一个对象就直接用缓存
                serialized.Update();
            }

            return serialized;
        }

        /// <summary>重新扫描 / 重新聚焦时清一次缓存，避免握着已经换代的实例。</summary>
        public static void InvalidateCache()
        {
            Cache.Clear();
            RefreshedThisPass.Clear();
        }

        public static SerializedProperty Get(UnityEngine.Object target, string name)
        {
            SerializedObject serialized = GetSerialized(target);
            return serialized != null ? serialized.FindProperty(name) : null;
        }

        public static string GetString(UnityEngine.Object target, string name, string fallback = "")
        {
            SerializedProperty p = Get(target, name);
            return p != null && p.propertyType == SerializedPropertyType.String ? p.stringValue : fallback;
        }

        public static int GetInt(UnityEngine.Object target, string name, int fallback = 0)
        {
            SerializedProperty p = Get(target, name);
            return p != null && p.propertyType == SerializedPropertyType.Integer ? p.intValue : fallback;
        }

        public static float GetFloat(UnityEngine.Object target, string name, float fallback = 0f)
        {
            SerializedProperty p = Get(target, name);
            return p != null && p.propertyType == SerializedPropertyType.Float ? p.floatValue : fallback;
        }

        public static bool GetBool(UnityEngine.Object target, string name, bool fallback = false)
        {
            SerializedProperty p = Get(target, name);
            return p != null && p.propertyType == SerializedPropertyType.Boolean ? p.boolValue : fallback;
        }

        public static Vector2Int GetVector2Int(UnityEngine.Object target, string name)
        {
            SerializedProperty p = Get(target, name);
            if (p != null && p.propertyType == SerializedPropertyType.Vector2Int)
                return p.vector2IntValue;
            return Vector2Int.zero;
        }

        public static UnityEngine.Object GetObjectRef(UnityEngine.Object target, string name)
        {
            SerializedProperty p = Get(target, name);
            return p != null && p.propertyType == SerializedPropertyType.ObjectReference ? p.objectReferenceValue : null;
        }
    }

    /// <summary>
    /// 建筑体检：把"策划看不出来、但出包/运行时会出问题"的配置挑出来。
    ///
    /// 每条规则的依据都能在运行时代码里找到，注释里写了出处；
    /// 判断不了的（例如注册表没读到）一律<b>跳过而不误报</b>。
    /// </summary>
    public static class BuildingValidator
    {
        /// <summary>BuildUP.levels 的语义就是三级（代码里 new LevelData[3]，显示等级 = level+1）。</summary>
        public const int ExpectedLevelCount = 3;

        /// <summary>玩家侧建筑固定 side = 1（BuildUP.IsPlayerTeam => prop.side == 1）。</summary>
        public const int PlayerSide = 1;

        public static void ValidateAll(List<BuildingInfo> buildings, List<BuildingIssue> output)
        {
            if (output == null)
                return;

            output.Clear();

            if (buildings == null)
                return;

            for (int i = 0; i < buildings.Count; i++)
                Validate(buildings[i], output);

            ValidateRaceTables(output);
            ValidateBarracksCross(output, buildings);
        }

        private static BuildingIssue New(BuildingInfo info, BuildingIssueLevel level, string code, string title, string detail)
        {
            return new BuildingIssue
            {
                Level = level,
                Code = code,
                Title = title,
                Detail = detail,
                BuildingPath = info != null ? info.Path : string.Empty,
                BuildingName = info != null ? info.Name : string.Empty
            };
        }

        #region 单个建筑
        public static void Validate(BuildingInfo info, List<BuildingIssue> output)
        {
            if (info == null || info.Prefab == null || output == null)
                return;

            ValidateCore(info, output);
            ValidateLevels(info, output);
            ValidateOccupancy(info, output);

            if (info.Training != null)
                ValidateBarracks(info, output);

            if (info.Tower != null)
                ValidateSentry(info, output);

            ValidateEffectKeys(info, output);
            ValidateOwnership(info, output);
        }

        private static void ValidateCore(BuildingInfo info, List<BuildingIssue> output)
        {
            if (info.BuildUp == null)
            {
                output.Add(New(info, BuildingIssueLevel.Info, "B-NO-BUILDUP", "没有 BuildUP 组件",
                    "这个建筑没有等级表（BuildUP.levels），既不能建造也不能升级 —— " +
                    "中立的场景营地（如 Camp）属于正常情况；如果它是玩家能造的建筑，那就是配错了。"));
            }

            if (info.Base == null)
            {
                output.Add(New(info, BuildingIssueLevel.Error, "B-NO-BASE", "没有 BuildingBase 组件",
                    "占地登记、合法性检查、施工流程、朝向翻转都在 BuildingBase 上；缺了它这个预制体不会被当成可放置建筑。"));
            }

            if (info.Property == null)
            {
                output.Add(New(info, BuildingIssueLevel.Error, "B-NO-PROPERTY", "没有 GameObjectProperty 组件",
                    "占地尺寸、施工时长、血量/攻击都在 GameObjectProperty 上；缺了它 BuildUP.ApplyLevel 写数值时会直接跳过。"));
                return;
            }

            int side = BuildingFields.GetInt(info.Property, "side", 1);
            if (side != PlayerSide)
            {
                var issue = New(info, BuildingIssueLevel.Warning, "B-SIDE", "预制体上的 side 不是 1（玩家）",
                    "side = " + side + "。玩家的建造/升级/拆除只对 side == 1 开放（BuildUP.cs:73 IsPlayerTeam）；" +
                    "预制体上的 side 是默认值，敌方建筑在运行时会由生成方改成偶数队。");
                issue.Fixable = true;
                issue.SuggestedValue = "side = 1";
                output.Add(issue);
            }

            GameObjectType type = (GameObjectType)BuildingFields.GetInt(info.Property, "objectType", (int)GameObjectType.Building);
            if ((type & GameObjectType.Building) == 0)
            {
                output.Add(New(info, BuildingIssueLevel.Error, "B-OBJECT-TYPE", "objectType 没勾 Building",
                    "当前值 " + type + "。建造按钮与放置系统按 (objectType & GameObjectType.Building) != 0 判断是不是建筑" +
                    "（BuildingPlace.cs:82），没勾的话会被当成非建筑。"));
            }
        }

        private static void ValidateLevels(BuildingInfo info, List<BuildingIssue> output)
        {
            if (info.BuildUp == null)
                return;

            if (info.BuildUp.levels == null || info.BuildUp.levels.Length == 0)
            {
                var issue = New(info, BuildingIssueLevel.Error, "B-LEVELS-EMPTY", "等级表是空的",
                    "BuildUP.levels 为空 → 建造费、血量、攻击、升级全都取不到值。");
                issue.Fixable = true;
                issue.SuggestedValue = "补成 1 级";
                output.Add(issue);
                return;
            }

            int count = info.BuildUp.levels.Length;
            if (count != ExpectedLevelCount)
            {
                var issue = New(info, BuildingIssueLevel.Warning, "B-LEVELS-LEN", "等级表不是 3 级",
                    "当前 " + count + " 级。工具与游戏文案都按「三级」表述（显示等级 = 索引 + 1），" +
                    "少一级会让最高级缺失、多出来的级在实际游戏里也用不到。");
                issue.Fixable = true;
                issue.SuggestedValue = count < ExpectedLevelCount
                    ? "用最后一级的值补齐到 3 级"
                    : "截断到 3 级";
                output.Add(issue);
            }

            for (int i = 0; i < count; i++)
            {
                BuildUP.LevelData level = info.BuildUp.levels[i];
                string name = "第 " + (i + 1) + " 级";

                // 会攻击的建筑才检查攻击/攻击范围。实扫发现：兵营、黑暗兵营、大本营的
                // levels[].attack 与 attackRange <b>全都是 0</b>，而 ApplyLevel 会无条件把
                // prop.atkRange 写成 data.attackRange —— 对它们 0 是正常值，报了就是误报。
                bool attacker = info.Tower != null;

                if (level.sprite == null)
                {
                    // BuildUP.ApplyLevel: if (body && data.sprite) body.sprite = data.sprite;
                    // → sprite 为空时<b>不换图</b>，会沿用上一级/初始外观，看起来像"升级没生效"。
                    output.Add(New(info, i == 0 ? BuildingIssueLevel.Error : BuildingIssueLevel.Warning,
                        "B-LEVEL-SPRITE", name + "没有外观图（sprite 为空）",
                        "升级时的换图是 if (body && data.sprite) body.sprite = data.sprite;（BuildUP.cs:442-443）——" +
                        "sprite 为空就<b>不换图</b>，会沿用上一级的外观。" +
                        (i == 0 ? "第 1 级是初始外观，不能为空。" : string.Empty)));
                }

                if (level.maxHp <= 0)
                {
                    output.Add(New(info, BuildingIssueLevel.Error, "B-LEVEL-HP", name + "生命为 0 或负数",
                        "maxHp = " + level.maxHp + "。升级时 health.SetMaxHp(max(1, round(maxHp * scale)))，会退化成 1 血。"));
                }

                if (attacker && (level.attackRange.x <= 0 || level.attackRange.y <= 0))
                {
                    output.Add(New(info, BuildingIssueLevel.Error, "B-LEVEL-RANGE", name + "攻击范围为 0",
                        "attackRange = (" + level.attackRange.x + ", " + level.attackRange.y + ")。哨塔靠以自身为中心的矩形索敌" +
                        "（halfX/halfY = range/2），任一维为 0 就永远选不到目标、一炮都打不出去。" +
                        "注意<b>攻击范围不参与等级缩放</b>（BuildUP.cs:440 直接赋值 prop.atkRange = data.attackRange），三级要分别填。"));
                }

                if (attacker && level.attack <= 0)
                {
                    output.Add(New(info, BuildingIssueLevel.Warning, "B-LEVEL-ATK-ZERO", name + "攻击为 0",
                        "哨塔的伤害取 levels[i].attack（再乘 1.1^(哨塔等级-1)），为 0 时打出去的子弹没有伤害。"));
                }

                if (level.cost < 0)
                {
                    var issue = New(info, BuildingIssueLevel.Error, "B-LEVEL-COST-NEG", name + "造价是负数",
                        "cost = " + level.cost + "。第 1 级是<b>建造费</b>、第 2/3 级是<b>升级费</b>，负数会被 max(0, cost) 夹成 0。");
                    issue.Fixable = true;
                    issue.SuggestedValue = "cost = 0";
                    output.Add(issue);
                }

                // 与上一级比：升级反而变差
                if (i > 0)
                {
                    BuildUP.LevelData prev = info.BuildUp.levels[i - 1];

                    if (level.maxHp <= prev.maxHp)
                    {
                        output.Add(New(info, BuildingIssueLevel.Warning, "B-LEVEL-HP-DESC", name + "生命没有比上一级高",
                            prev.maxHp + " → " + level.maxHp + "。花了升级费却更脆，通常是漏改或填反了。"));
                    }

                    if (attacker && level.attack < prev.attack)
                    {
                        output.Add(New(info, BuildingIssueLevel.Warning, "B-LEVEL-ATK-DESC", name + "攻击比上一级低",
                            prev.attack + " → " + level.attack + "。"));
                    }

                    if (level.cost < prev.cost)
                    {
                        output.Add(New(info, BuildingIssueLevel.Info, "B-LEVEL-COST-DESC", name + "的升级费比上一级便宜",
                            prev.cost + " → " + level.cost + "。三级造价递减不一定是错，但值得确认。"));
                    }
                }
            }

            if (info.BuildUp.levels[0].cost <= 0)
            {
                output.Add(New(info, BuildingIssueLevel.Warning, "B-BUILD-FREE", "建造费是 0",
                    "GetBuildCost 取 levels[0].cost，为 0 时 BuildingPlace 会跳过扣费（cost > 0 才扣）——" +
                    "这个建筑可以无限免费摆。"));
            }

            float buildTime = info.Property != null ? BuildingFields.GetFloat(info.Property, "buildTime", 0f) : 0f;
            string buildAnime = info.Property != null ? BuildingFields.GetString(info.Property, "buildAnime") : string.Empty;
            if (buildTime > 0f && string.IsNullOrEmpty(buildAnime))
            {
                output.Add(New(info, BuildingIssueLevel.Warning, "B-BUILD-ANIME", "填了施工时长但没有施工特效键",
                    "buildTime = " + buildTime.ToString("0.##") + " 秒，但 buildAnime 为空。" +
                    "BuildingBase.cs:131 里 buildAnime 为空则<b>根本不进入施工流程</b> → 这个时长形同虚设，建筑会瞬间完工。"));
            }

            if (!string.IsNullOrEmpty(buildAnime) &&
                BuildingResourceIndex.Instance.PrefabRegistryFound &&
                !BuildingResourceIndex.Instance.HasPrefab(buildAnime))
            {
                output.Add(New(info, BuildingIssueLevel.Error, "B-BUILD-ANIME-KEY", "施工特效键不在注册表里",
                    "buildAnime = \"" + buildAnime + "\" 不在 PrefabRegistry 中，出包后加载不到。"));
            }
        }

        private static void ValidateOccupancy(BuildingInfo info, List<BuildingIssue> output)
        {
            if (info.Property == null)
                return;

            Vector2Int occupy = BuildingFields.GetVector2Int(info.Property, "occupySpace");
            if (occupy.x <= 0 || occupy.y <= 0)
            {
                var issue = New(info, BuildingIssueLevel.Error, "B-OCCUPY", "占地尺寸不合法",
                    "occupySpace = (" + occupy.x + ", " + occupy.y + ")。占地决定网格占用与合法性检查" +
                    "（BuildingBase.GetOccupyCells），任一维为 0 会让占用登记形同虚设、建筑可以互相叠放。");
                issue.Fixable = true;
                issue.SuggestedValue = "夹到至少 1×1";
                output.Add(issue);
            }
        }

        /// <summary>
        /// 建筑身上的各种「资源键」是不是都在注册表里。
        /// 建筑是<b>混用</b>的：每级外观图是强引用（不用查表），而施工/升级/拆除/音效/面板/弹幕全是资源键。
        /// 出处：BuildUP.cs:28,35,42 / GameObjectProperty.cs:31,83 / BuildingRemoveButton.cs:20 /
        ///       BuildingHealth.cs:22 / BuildingTowerAI.cs:24,37 / BuildingTraining.cs:29。
        /// </summary>
        private static void ValidateEffectKeys(BuildingInfo info, List<BuildingIssue> output)
        {
            BuildingResourceIndex index = BuildingResourceIndex.Instance;
            bool prefabOk = index.PrefabRegistryFound;
            bool audioOk = index.AudioRegistryFound;

            if (info.BuildUp != null)
            {
                CheckPrefabKey(info, output, index, prefabOk, info.BuildUp, "upgradeEffectPrefabKey", "升级特效键", "BuildUP.upgradeEffectPrefabKey");
                CheckAudioKey(info, output, index, audioOk, info.BuildUp, "upgradeSoundKey", "升级音效键");
                CheckAudioKey(info, output, index, audioOk, info.BuildUp, "demolishSoundKey", "拆除音效键");

                // 升级/果冻/拆除的表现时长：负数没有意义
                CheckNonNegative(info, output, info.BuildUp, "effectTime", "升级动画时长");
                CheckNonNegative(info, output, info.BuildUp, "jellyTime", "果冻动画时长");
                CheckNonNegative(info, output, info.BuildUp, "demolishFallTime", "拆除掉落时长");
            }

            if (info.RemoveButton != null)
            {
                CheckPrefabKey(info, output, index, prefabOk, info.RemoveButton, "demolishPrefabKey", "拆除动画键", "BuildingRemoveButton.demolishPrefabKey");
                CheckAudioKey(info, output, index, audioOk, info.RemoveButton, "demolishSoundKey", "拆除音效键");
            }

            if (info.Health != null)
            {
                CheckAudioKey(info, output, index, audioOk, info.Health, "hitSoundKey", "受击音效键");
            }

            // 哨塔维护费：upkeepPerLevel 按 <b>0 起等级</b>索引（BuildingUpkeep.cs:48-56）
            if (info.Upkeep != null)
            {
                SerializedProperty levels = BuildingFields.Get(info.Upkeep, "upkeepPerLevel");
                if (levels != null && levels.isArray)
                {
                    if (levels.arraySize < ExpectedLevelCount)
                    {
                        output.Add(New(info, BuildingIssueLevel.Warning, "B-UPKEEP-LEN", "哨塔维护费表不足 3 级",
                            "upkeepPerLevel 只有 " + levels.arraySize + " 项，而建筑是三级（索引 0 起）；" +
                            "缺的那几级会取不到值，维护费按 0 算。"));
                    }

                    for (int i = 0; i < levels.arraySize; i++)
                    {
                        SerializedProperty entry = levels.GetArrayElementAtIndex(i);
                        if (entry != null && entry.intValue < 0)
                        {
                            var issue = New(info, BuildingIssueLevel.Error, "B-UPKEEP-NEG", "第 " + (i + 1) + " 级维护费是负数",
                                "upkeepPerLevel[" + i + "] = " + entry.intValue + "。维护费会以 isDefense:true 注册进队伍账本，负数等于倒贴收入。");
                            issue.Fixable = true;
                            issue.SuggestedValue = "改成 0";
                            output.Add(issue);
                        }
                    }
                }
            }
        }

        private static void CheckPrefabKey(BuildingInfo info, List<BuildingIssue> output, BuildingResourceIndex index,
            bool registryOk, UnityEngine.Object target, string field, string label, string path)
        {
            string key = BuildingFields.GetString(target, field);
            if (string.IsNullOrEmpty(key) || !registryOk || index.HasPrefab(key))
                return;

            output.Add(New(info, BuildingIssueLevel.Error, "B-KEY-" + field, label + "不在注册表里",
                path + " = \"" + key + "\" 不在 PrefabRegistry 中，出包后加载不到。"));
        }

        private static void CheckAudioKey(BuildingInfo info, List<BuildingIssue> output, BuildingResourceIndex index,
            bool registryOk, UnityEngine.Object target, string field, string label)
        {
            string key = BuildingFields.GetString(target, field);
            if (string.IsNullOrEmpty(key) || !registryOk || index.HasAudio(key))
                return;

            output.Add(New(info, BuildingIssueLevel.Warning, "B-AUDIO-" + field, label + "不在注册表里",
                field + " = \"" + key + "\" 不在 AudioRegistry 中，出包后这一声会静音。"));
        }

        private static void CheckNonNegative(BuildingInfo info, List<BuildingIssue> output, UnityEngine.Object target, string field, string label)
        {
            SerializedProperty p = BuildingFields.Get(target, field);
            if (p == null || p.propertyType != SerializedPropertyType.Float || p.floatValue >= 0f)
                return;

            var issue = New(info, BuildingIssueLevel.Warning, "B-NEG-" + field, label + "是负数",
                field + " = " + p.floatValue.ToString("0.###") + "，负数时长没有意义。");
            issue.Fixable = true;
            issue.SuggestedValue = "改成 0";
            output.Add(issue);
        }

        private static void ValidateOwnership(BuildingInfo info, List<BuildingIssue> output)
        {
            if (info.Owners.Count > 0)
                return;

            string label = info.BuildUp == null ? "中立建筑" : "可建造建筑";
            var issue = New(info, info.BuildUp == null ? BuildingIssueLevel.Info : BuildingIssueLevel.Warning,
                "B-NO-OWNER", "不在任何种族的建筑表里",
                "没有种族在 RaceDefinition.buildings 里引用这个预制体 → 玩家永远造不出它。" +
                "（当前按「" + label + "」判断；中立的场景营地属于正常情况。）");
            output.Add(issue);
        }
        #endregion

        #region 兵营
        private static void ValidateBarracks(BuildingInfo info, List<BuildingIssue> output)
        {
            BuildingTraining training = info.Training;
            BuildingResourceIndex index = BuildingResourceIndex.Instance;

            TroopDefinition[] troops = training.Troops;
            string barracksLabel = training.IsDarkBarracks ? "黑暗兵营" : "普通兵营";

            if (troops == null || troops.Length == 0)
            {
                output.Add(New(info, BuildingIssueLevel.Error, "B-TROOPS-EMPTY", "兵营的兵种表是空的",
                    "BuildingTraining.troops 为空 → 玩家点开这个兵营时一个兵种格子都没有，什么都造不了。"));
            }
            else
            {
                int nullCount = 0;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var duplicated = new List<string>();
                var outOfRange = new List<string>();

                for (int i = 0; i < troops.Length; i++)
                {
                    TroopDefinition troop = troops[i];
                    if (troop == null)
                    {
                        nullCount++;
                        continue;
                    }

                    string id = string.IsNullOrEmpty(troop.Id) ? troop.name : troop.Id;
                    if (!seen.Add(id))
                        duplicated.Add(id);

                    // 兵营面板只填 row = 1..3（GetTroop(row, index) 取 UnlockLevel == row）
                    if (troop.UnlockLevel < 1 || troop.UnlockLevel > 3)
                        outOfRange.Add(id + "（解锁等级 " + troop.UnlockLevel + "）");
                }

                if (nullCount > 0)
                {
                    // 原先这条是 **Error 级**，理由是"运行时会抛 NullReferenceException" ——
                    // **那个理由是错的**：troops 的每一个运行时消费者都判了 null
                    //（TroopDefinition.cs:100-103 的 Preload、BuildingTraining.cs:74 的 GetTroop、
                    //  TroopDefinition.cs:191 / 207、TeamCommander.cs:1086）。
                    // 真实后果只是"训练面板里少一格 / 该条目被跳过"，所以降为警告。
                    var issue = New(info, BuildingIssueLevel.Warning, "B-TROOP-NULL", "兵种表里有 " + nullCount + " 个空条目",
                        "运行时每个消费者都判了 null，不会崩；但训练面板按这张表填充，" +
                        "空条目会占掉一个格子或被跳过，所以还是清掉干净。");
                    issue.Fixable = true;
                    issue.SuggestedValue = "删除这 " + nullCount + " 个空条目";
                    output.Add(issue);
                }

                if (duplicated.Count > 0)
                {
                    output.Add(New(info, BuildingIssueLevel.Warning, "B-TROOP-DUP", "兵种表里有重复兵种",
                        "重复：" + string.Join("、", duplicated.ToArray()) +
                        "。GetTroop(row, index) 按解锁等级逐排取，重复会让兵营面板上出现两个一样的格子。"));
                }

                if (outOfRange.Count > 0)
                {
                    output.Add(New(info, BuildingIssueLevel.Warning, "B-TROOP-UNLOCK", "有兵种的解锁等级超出兵营面板范围",
                        string.Join("、", outOfRange.ToArray()) +
                        "。兵营面板只填 row = 1..3（BuildingTraining.GetTroop 取 UnlockLevel == row），" +
                        "越界的兵种在面板上任何一排都不会出现 —— 要在<b>兵种编辑器</b>里改它的兵营阶级。"));
                }
            }

            // isDarkBarracks 与名字对不上（最容易配错的一处）
            string name = info.Name ?? string.Empty;
            bool nameSaysDark = name.IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0;
            if (training.IsDarkBarracks != nameSaysDark)
            {
                var issue = New(info, BuildingIssueLevel.Warning, "B-DARK-FLAG", "黑暗兵营标记与名字对不上",
                    "isDarkBarracks = " + training.IsDarkBarracks + "，但预制体名字是「" + name + "」。" +
                    "这个标记决定它按哪套等级结算（勾了走 darkBarracksLevel，否则走 barracksLevel）——" +
                    "配错会让敌方的黑暗兵营等级作用在普通兵营上。");
                issue.Fixable = true;
                issue.SuggestedValue = nameSaysDark ? "勾上 isDarkBarracks" : "取消 isDarkBarracks";
                output.Add(issue);
            }

            string panelKey = BuildingFields.GetString(training, "panelPrefabKey");
            if (string.IsNullOrEmpty(panelKey))
            {
                output.Add(New(info, BuildingIssueLevel.Warning, "B-PANEL-KEY-EMPTY", "兵营面板预制体键为空",
                    "panelPrefabKey 为空 → 点开兵营时打不开训练面板。"));
            }
            else if (index.PrefabRegistryFound && !index.HasPrefab(panelKey))
            {
                output.Add(New(info, BuildingIssueLevel.Error, "B-PANEL-KEY", "兵营面板预制体键不在注册表里",
                    "panelPrefabKey = \"" + panelKey + "\" 不在 PrefabRegistry 中，出包后加载不到。"));
            }

            if (BuildingFields.GetObjectRef(training, "avatarRenderer") == null)
            {
                output.Add(New(info, BuildingIssueLevel.Warning, "B-AVATAR-RENDERER", "兵营没配头像渲染器",
                    "avatarRenderer 为空 → 兵营左上角不会显示当前训练的兵种头像（ApplyAvatar 会直接跳过）。" +
                    "它是预制体内部引用，需要在预制体上拖。"));
            }

            UnityEngine.Object cooldownMask = BuildingFields.GetObjectRef(training, "cooldownMaskRenderer");
            if (cooldownMask == null)
            {
                output.Add(New(info, BuildingIssueLevel.Info, "B-COOLDOWN-MASK", "兵营没配冷却遮罩渲染器",
                    "cooldownMaskRenderer 为空 → 训练冷却期间没有遮罩动画（不影响功能）。"));
            }

            output.Add(New(info, BuildingIssueLevel.Info, "B-BARRACKS-INFO", barracksLabel + "：挂了 " +
                (troops != null ? troops.Length : 0) + " 个兵种",
                "归属是<b>只读展示</b>：这份兵种表在建筑预制体上，本工具可以增删它（写的是预制体，会影响所有用到这个兵营的地方）。"));
        }
        #endregion

        #region 哨塔
        private static void ValidateSentry(BuildingInfo info, List<BuildingIssue> output)
        {
            BuildingTowerAI tower = info.Tower;
            BuildingResourceIndex index = BuildingResourceIndex.Instance;

            Vector2Int range = info.Property != null ? BuildingFields.GetVector2Int(info.Property, "atkRange") : Vector2Int.zero;
            if (range.x <= 0 || range.y <= 0)
            {
                output.Add(New(info, BuildingIssueLevel.Error, "B-TOWER-RANGE", "哨塔攻击范围为 0",
                    "索敌是以塔为中心的矩形 atkRange（halfX/halfY = range/2，BuildingTowerAI），" +
                    "任一维为 0 就永远选不到目标、一炮都打不出去。"));
            }

            string projectileKey = BuildingFields.GetString(tower, "projectilePrefabKey");
            string atkObjKey = info.Property != null ? BuildingFields.GetString(info.Property, "atkObj") : string.Empty;
            if (string.IsNullOrEmpty(projectileKey) && string.IsNullOrEmpty(atkObjKey))
            {
                output.Add(New(info, BuildingIssueLevel.Warning, "B-TOWER-PROJECTILE-EMPTY", "哨塔没有配投射物",
                    "projectilePrefabKey 与 GameObjectProperty.atkObj 都为空 → 射击时没有子弹可用。"));
            }
            else if (!string.IsNullOrEmpty(projectileKey) && index.PrefabRegistryFound && !index.HasPrefab(projectileKey))
            {
                output.Add(New(info, BuildingIssueLevel.Error, "B-TOWER-PROJECTILE-KEY", "哨塔投射物键不在注册表里",
                    "projectilePrefabKey = \"" + projectileKey + "\" 不在 PrefabRegistry 中，出包后加载不到。"));
            }

            float interval = BuildingFields.GetFloat(tower, "attackInterval", 0f);
            if (interval < 0f)
            {
                var issue = New(info, BuildingIssueLevel.Warning, "B-TOWER-INTERVAL", "射击间隔是负数",
                    "attackInterval = " + interval.ToString("0.###") + "。0 表示用 GameObjectProperty.atkRate，负数没有意义。");
                issue.Fixable = true;
                issue.SuggestedValue = "attackInterval = 0";
                output.Add(issue);
            }

            string soundKey = BuildingFields.GetString(tower, "attackSoundKey");
            if (!string.IsNullOrEmpty(soundKey) && index.AudioRegistryFound && !index.HasAudio(soundKey))
            {
                output.Add(New(info, BuildingIssueLevel.Warning, "B-TOWER-SOUND-KEY", "哨塔射击音效键不在注册表里",
                    "attackSoundKey = \"" + soundKey + "\" 不在 AudioRegistry 中，出包后这一声会静音。"));
            }

            if (BuildingFields.GetObjectRef(tower, "shootPoint") == null)
            {
                output.Add(New(info, BuildingIssueLevel.Info, "B-TOWER-SHOOTPOINT", "哨塔没指定射击点",
                    "shootPoint 为空时会按名字找子物体（\"Shoot point\" / \"ShootPoint\"）；" +
                    "找不到就退化成用塔自身位置发射。"));
            }

            string damageType = BuildingFields.GetString(tower, "damageType", "normal");
            output.Add(New(info, BuildingIssueLevel.Info, "B-TOWER-INFO", "哨塔：伤害类型 " + damageType,
                "射程 " + range.x + "×" + range.y + " 格；间隔 " +
                (interval > 0f ? interval.ToString("0.###") + " 秒" : "用 atkRate") +
                "。<b>攻击范围不参与等级缩放</b>，三级要分别填。"));
        }
        #endregion

        #region 种族建筑表（跨建筑）
        private static void ValidateRaceTables(List<BuildingIssue> output)
        {
            BuildingResourceIndex index = BuildingResourceIndex.Instance;

            if (index.Races.Count == 0)
            {
                output.Add(new BuildingIssue
                {
                    Level = BuildingIssueLevel.Warning,
                    Code = "B-NO-RACES",
                    Title = "工程里没读到任何 RaceDefinition",
                    Detail = "种族建筑表是「种族 → 建筑类型 → 预制体键」的唯一来源，读不到就没法校验建筑归属。"
                });
                return;
            }

            var required = new[]
            {
                BuildingType.Barracks, BuildingType.DarkBarracks, BuildingType.Sentry, BuildingType.MainBase
            };

            for (int r = 0; r < index.Races.Count; r++)
            {
                RaceDefinition race = index.Races[r];
                if (race == null)
                    continue;

                var seenTypes = new HashSet<BuildingType>();
                var duplicates = new List<string>();

                IReadOnlyList<RaceBuildingEntry> entries = race.Buildings;
                if (entries != null)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        RaceBuildingEntry entry = entries[i];
                        if (entry == null)
                            continue;

                        string typeLabel = BuildingInfo.TypeLabelOf(entry.buildingType);

                        if (!seenTypes.Add(entry.buildingType))
                            duplicates.Add(typeLabel);

                        if (string.IsNullOrEmpty(entry.prefabKey))
                        {
                            output.Add(new BuildingIssue
                            {
                                Level = BuildingIssueLevel.Error,
                                Code = "B-RACE-KEY-EMPTY",
                                Title = "种族「" + race.Id + "」的" + typeLabel + "没有填预制体键",
                                Detail = "空键会让 BuildingButton.ResolvePrefab() 查不到，回落成建造按钮上的默认预制体 —— " +
                                         "这个种族就会造出别的种族的建筑。"
                            });
                            continue;
                        }

                        if (index.PrefabRegistryFound && !index.HasPrefab(entry.prefabKey))
                        {
                            output.Add(new BuildingIssue
                            {
                                Level = BuildingIssueLevel.Error,
                                Code = "B-RACE-KEY-INVALID",
                                Title = "种族「" + race.Id + "」的" + typeLabel + "键不在注册表里",
                                Detail = "prefabKey = \"" + entry.prefabKey + "\" 不在 PrefabRegistry 中，出包后加载不到。"
                            });
                        }
                    }
                }

                if (duplicates.Count > 0)
                {
                    output.Add(new BuildingIssue
                    {
                        Level = BuildingIssueLevel.Warning,
                        Code = "B-RACE-DUP",
                        Title = "种族「" + race.Id + "」的建筑表里有重复类型",
                        Detail = "重复：" + string.Join("、", duplicates.ToArray()) +
                                 "。解析按类型匹配、命中第一个，后面的条目不起作用。"
                    });
                }

                for (int i = 0; i < required.Length; i++)
                {
                    if (seenTypes.Contains(required[i]))
                        continue;

                    output.Add(new BuildingIssue
                    {
                        Level = BuildingIssueLevel.Error,
                        Code = "B-RACE-MISSING-" + required[i],
                        Title = "种族「" + race.Id + "」缺「" + BuildingInfo.TypeLabelOf(required[i]) + "」条目",
                        Detail = "RaceDefinition.buildings 里没有这个类型 → BuildingButton.ResolvePrefab() 查不到，" +
                                 "会回落成建造按钮上的默认预制体（通常是别的种族的建筑）。"
                    });
                }
            }
        }

        /// <summary>同一个兵种被挂进了普通兵营和黑暗兵营（兵种编辑器也有这条提示，这里从建筑侧再查一遍）。</summary>
        private static void ValidateBarracksCross(List<BuildingIssue> output, List<BuildingInfo> buildings)
        {
            if (buildings == null)
                return;

            var normal = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dark = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < buildings.Count; i++)
            {
                BuildingInfo info = buildings[i];
                if (info == null || info.Training == null || info.Training.Troops == null)
                    continue;

                HashSet<string> target = info.Training.IsDarkBarracks ? dark : normal;
                for (int k = 0; k < info.Training.Troops.Length; k++)
                {
                    TroopDefinition troop = info.Training.Troops[k];
                    if (troop == null)
                        continue;
                    target.Add(string.IsNullOrEmpty(troop.Id) ? troop.name : troop.Id);
                }
            }

            var both = new List<string>();
            foreach (string id in normal)
            {
                if (dark.Contains(id))
                    both.Add(id);
            }

            if (both.Count > 0)
            {
                output.Add(new BuildingIssue
                {
                    Level = BuildingIssueLevel.Info,
                    Code = "B-TROOP-BOTH",
                    Title = "有 " + both.Count + " 个兵种同时挂在普通兵营和黑暗兵营",
                    Detail = string.Join("、", both.ToArray()) +
                             "。这是允许的（两边面板都会出现），但如果是有意的限定，值得再确认一次。"
                });
            }

            // 一个兵种都没被任何兵营挂上
            BuildingResourceIndex index = BuildingResourceIndex.Instance;
            var orphans = new List<string>();
            for (int i = 0; i < index.Troops.Count; i++)
            {
                TroopDefinition troop = index.Troops[i];
                if (troop == null)
                    continue;

                string id = string.IsNullOrEmpty(troop.Id) ? troop.name : troop.Id;
                if (!normal.Contains(id) && !dark.Contains(id))
                    orphans.Add(id);
            }

            if (orphans.Count > 0)
            {
                output.Add(new BuildingIssue
                {
                    Level = BuildingIssueLevel.Warning,
                    Code = "B-TROOP-ORPHAN",
                    Title = "有 " + orphans.Count + " 个兵种没有挂在任何兵营上",
                    Detail = "例如：" + string.Join("、", orphans.GetRange(0, Mathf.Min(8, orphans.Count)).ToArray()) +
                             (orphans.Count > 8 ? " 等" : string.Empty) +
                             "。玩家在兵营界面永远看不到它们 —— 要挂上得改兵营预制体的 BuildingTraining.troops（本工具的建筑详情页里可以加）。"
                });
            }
        }
        #endregion

        #region 汇总 / 一键修复
        public static void Count(List<BuildingIssue> issues, out int errors, out int warnings, out int infos)
        {
            errors = warnings = infos = 0;
            if (issues == null)
                return;

            for (int i = 0; i < issues.Count; i++)
            {
                BuildingIssue issue = issues[i];
                if (issue == null)
                    continue;

                switch (issue.Level)
                {
                    case BuildingIssueLevel.Error: errors++; break;
                    case BuildingIssueLevel.Warning: warnings++; break;
                    default: infos++; break;
                }
            }
        }

        public static Dictionary<string, List<BuildingIssue>> GroupByBuilding(List<BuildingIssue> issues)
        {
            var map = new Dictionary<string, List<BuildingIssue>>(StringComparer.Ordinal);
            if (issues == null)
                return map;

            for (int i = 0; i < issues.Count; i++)
            {
                BuildingIssue issue = issues[i];
                if (issue == null)
                    continue;

                string key = issue.BuildingPath ?? string.Empty;
                List<BuildingIssue> list;
                if (!map.TryGetValue(key, out list))
                {
                    list = new List<BuildingIssue>();
                    map[key] = list;
                }
                list.Add(issue);
            }
            return map;
        }

        /// <summary>某个建筑最严重的级别（用于列表角标）。找不到问题时返回 -1。</summary>
        public static int WorstLevelOf(List<BuildingIssue> issues, string buildingPath)
        {
            if (issues == null || string.IsNullOrEmpty(buildingPath))
                return -1;

            int worst = -1;
            for (int i = 0; i < issues.Count; i++)
            {
                BuildingIssue issue = issues[i];
                if (issue == null || !string.Equals(issue.BuildingPath, buildingPath, StringComparison.Ordinal))
                    continue;

                int level = (int)issue.Level;
                if (level > worst)
                    worst = level;
            }
            return worst;
        }

        /// <summary>
        /// 一键修复：只做<b>确定安全</b>的修正。
        ///
        /// 三级数值一律不动（那是策划的平衡决策）；种族建筑表也不动（该用哪个预制体只有人知道）。
        /// 全部通过 SerializedObject 写入，与 Inspector 一致、可 Ctrl+Z。
        /// </summary>
        public static int ApplySafeFixes(BuildingInfo info)
        {
            if (info == null || info.Prefab == null)
                return 0;

            int fixedCount = 0;

            // 1) 等级表长度补齐 / 截断
            if (info.BuildUp != null)
            {
                var levelsSo = new SerializedObject(info.BuildUp);
                SerializedProperty levels = levelsSo.FindProperty("levels");
                if (levels != null && levels.isArray)
                {
                    if (levels.arraySize == 0)
                    {
                        levels.arraySize = 1;
                        fixedCount++;
                    }

                    // 负数造价夹到 0
                    for (int i = 0; i < levels.arraySize; i++)
                    {
                        SerializedProperty cost = levels.GetArrayElementAtIndex(i).FindPropertyRelative("cost");
                        if (cost != null && cost.intValue < 0)
                        {
                            cost.intValue = 0;
                            fixedCount++;
                        }
                    }

                    while (levels.arraySize < ExpectedLevelCount)
                    {
                        int last = levels.arraySize - 1;
                        levels.InsertArrayElementAtIndex(levels.arraySize);
                        SerializedProperty added = levels.GetArrayElementAtIndex(levels.arraySize - 1);
                        SerializedProperty source = levels.GetArrayElementAtIndex(last);
                        CopyLevelData(source, added);
                        fixedCount++;
                    }

                    while (levels.arraySize > ExpectedLevelCount)
                    {
                        levels.DeleteArrayElementAtIndex(levels.arraySize - 1);
                        fixedCount++;
                    }

                    levelsSo.ApplyModifiedProperties();
                }
            }

            // 2) 占地尺寸夹到至少 1×1
            if (info.Property != null)
            {
                var propSo = new SerializedObject(info.Property);
                bool changed = false;

                SerializedProperty occupy = propSo.FindProperty("occupySpace");
                if (occupy != null && occupy.propertyType == SerializedPropertyType.Vector2Int)
                {
                    Vector2Int value = occupy.vector2IntValue;
                    if (value.x <= 0 || value.y <= 0)
                    {
                        occupy.vector2IntValue = new Vector2Int(Mathf.Max(1, value.x), Mathf.Max(1, value.y));
                        changed = true;
                        fixedCount++;
                    }
                }

                SerializedProperty side = propSo.FindProperty("side");
                if (side != null && side.propertyType == SerializedPropertyType.Integer && side.intValue != PlayerSide)
                {
                    side.intValue = PlayerSide;
                    changed = true;
                    fixedCount++;
                }

                if (changed)
                    propSo.ApplyModifiedProperties();
            }

            // 3) 兵营：删掉空条目；isDarkBarracks 按名字纠正
            if (info.Training != null)
            {
                var trainingSo = new SerializedObject(info.Training);
                bool changed = false;

                SerializedProperty troops = trainingSo.FindProperty("troops");
                if (troops != null && troops.isArray)
                {
                    for (int i = troops.arraySize - 1; i >= 0; i--)
                    {
                        SerializedProperty element = troops.GetArrayElementAtIndex(i);
                        if (element != null && element.propertyType == SerializedPropertyType.ObjectReference &&
                            element.objectReferenceValue == null)
                        {
                            // troops 是**对象引用数组**：`DeleteArrayElementAtIndex` 第一次只把元素置空、
                            // **不缩短数组**，所以必须再删一次。少这一次的后果不是"没删干净"这么轻 ——
                            // 空条目还在 → 体检继续报同一条 → 再点一键修复再报，来回永远修不掉
                            //（"已修复 N 处"但错误还在）。
                            // 图鉴编辑器在 BookEditorData.cs:1035 / 1062 / 1133 三处都写了这个守卫，这里漏了。
                            // 注意 BuildUP.levels 是**结构体**数组，单删就是有效的，不要照抄到这里。
                            int before = troops.arraySize;
                            troops.DeleteArrayElementAtIndex(i);
                            if (troops.arraySize == before)
                                troops.DeleteArrayElementAtIndex(i);

                            fixedCount++;
                            changed = true;
                        }
                    }
                }

                string name = info.Name ?? string.Empty;
                bool nameSaysDark = name.IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0;
                SerializedProperty darkFlag = trainingSo.FindProperty("isDarkBarracks");
                if (darkFlag != null && darkFlag.propertyType == SerializedPropertyType.Boolean &&
                    darkFlag.boolValue != nameSaysDark)
                {
                    darkFlag.boolValue = nameSaysDark;
                    fixedCount++;
                    changed = true;
                }

                if (changed)
                    trainingSo.ApplyModifiedProperties();
            }

            // 4) 哨塔：射击间隔负数归零
            if (info.Tower != null)
            {
                var towerSo = new SerializedObject(info.Tower);
                SerializedProperty interval = towerSo.FindProperty("attackInterval");
                if (interval != null && interval.propertyType == SerializedPropertyType.Float && interval.floatValue < 0f)
                {
                    interval.floatValue = 0f;
                    towerSo.ApplyModifiedProperties();
                    fixedCount++;
                }
            }

            if (fixedCount > 0)
            {
                EditorUtility.SetDirty(info.Prefab);
                AssetDatabase.SaveAssets();
            }

            return fixedCount;
        }

        private static void CopyLevelData(SerializedProperty source, SerializedProperty target)
        {
            if (source == null || target == null)
                return;

            string[] fields = { "sprite", "maxHp", "attack", "attackRange", "cost" };
            for (int i = 0; i < fields.Length; i++)
            {
                SerializedProperty from = source.FindPropertyRelative(fields[i]);
                SerializedProperty to = target.FindPropertyRelative(fields[i]);
                if (from == null || to == null)
                    continue;

                switch (from.propertyType)
                {
                    case SerializedPropertyType.ObjectReference:
                        to.objectReferenceValue = from.objectReferenceValue;
                        break;
                    case SerializedPropertyType.Integer:
                        to.intValue = from.intValue;
                        break;
                    case SerializedPropertyType.Float:
                        to.floatValue = from.floatValue;
                        break;
                    case SerializedPropertyType.Vector2Int:
                        to.vector2IntValue = from.vector2IntValue;
                        break;
                }
            }
        }
        #endregion
    }
}
