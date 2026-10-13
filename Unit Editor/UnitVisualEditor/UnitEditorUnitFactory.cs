using System;
using System.Collections.Generic;

namespace CageFightUnitTool
{
    /// <summary>新建兵种时一次要填的全部输入（纯数据，界面填完交给它）。</summary>
    public sealed class UnitFactoryRequest
    {
        public string Race = string.Empty;                 // 目录/种族名（Cat / Mouse / …）
        public string TemplatePrefabName = string.Empty;   // 模板兵种（用来复制）
        public string TemplatePrefabPath = string.Empty;

        /// <summary>新预制体名：同时是**文件名**、**PrefabRegistry 的资源键**、训练 ID 的默认来源。建议英文/拼音。</summary>
        public string NewPrefabName = string.Empty;

        /// <summary>显示名（玩家看到的名字，可以是中文）。</summary>
        public string DisplayName = string.Empty;

        /// <summary>训练 ID；留空 = 按预制体名生成（与工程现有 id 风格一致）。</summary>
        public string TrainingId = string.Empty;

        public int UnlockLevel = 1;                        // 兵营阶级（1/2/3）
        public bool DarkBarracks;                          // 挂普通还是黑暗兵营

        public bool CopyTemplateStats = true;              // 沿用模板的战斗数值（不勾 = 只建骨架）
        public bool ReuseTemplateArtKeys = true;           // 沿用模板的美术 Key（贴图/动画/子弹）

        public string SpriteKey = string.Empty;
        public string AnimatorControllerKey = string.Empty;
        public string IconKey = string.Empty;
        public string BulletKey = string.Empty;
        public string AudioKey = string.Empty;             // 语音 Key（预制体上 StageAudio.audioKey1）

        /// <summary>兵种标签（TroopTag 枚举值）：出生时会注入到单位上，敌方 AI 的威胁判定会读它。</summary>
        public int Tag;

        public int Cooldown = 5;
        public int TrainCount = 1;
        public int Upkeep;
        public float ThreatScore = 1f;

        public bool CreateDefinition = true;               // 建 TroopDefinition
        public bool RegisterPrefabKey = true;              // 登记 PrefabRegistry
        public bool AttachToBarracks = true;               // 挂进兵营的 BuildingTraining.troops
    }

    /// <summary>一次新建的"计划"：先算出来给人看（试运行），再照着写盘。</summary>
    public sealed class UnitFactoryPlan
    {
        public string PrefabPath = string.Empty;
        public string PrefabKey = string.Empty;
        public string DefinitionPath = string.Empty;
        public string TrainingId = string.Empty;
        public string DisplayName = string.Empty;

        /// <summary>要写哪些东西（给人看的清单，顺序就是执行顺序）。</summary>
        public readonly List<string> Steps = new List<string>();

        /// <summary>拦人的问题（有错就不许执行）。</summary>
        public readonly List<string> Errors = new List<string>();

        /// <summary>不拦人、但要说一声的。</summary>
        public readonly List<string> Warnings = new List<string>();

        /// <summary>建完之后**还需要人工做**的事（工具做不到的，写清楚去哪做）。</summary>
        public readonly List<string> Todo = new List<string>();

        public bool CanRun { get { return Errors.Count == 0; } }
    }

    /// <summary>
    /// 界面/Unity 侧算好后喂给纯逻辑的上下文：现有哪些名字占用了、Key 在不在注册表里、这个种族有哪些兵营。
    /// 这些判断需要查资产，所以由 Unity 侧算好，纯逻辑只负责"拿这些事实做决策"。
    /// </summary>
    public sealed class UnitFactoryContext
    {
        public readonly List<string> ExistingTrainingIds = new List<string>();
        public readonly List<string> ExistingPrefabNames = new List<string>();
        public readonly List<string> ExistingRegistryKeys = new List<string>();
        public readonly List<string> AvailableBarracks = new List<string>();   // 该种族能挂的兵营（普通+黑暗）

        public string TemplateSpriteKey = string.Empty;
        public string TemplateAnimatorKey = string.Empty;
        public string TemplateIconKey = string.Empty;
        public string TemplateBulletKey = string.Empty;
        public string TemplateAudioKey = string.Empty;
        public int TemplateTag;

        public bool SpriteKeyExists;
        public bool AnimatorKeyExists;
        public bool IconKeyExists;
        public bool BulletKeyExists;
        public bool AudioKeyExists;

        /// <summary>
        /// 上面那组是**模板的** Key 在不在注册表；这一组是**用户自己填的** Key 在不在。
        /// 两组分开是刻意的：只有一个标志时，"这个标志指的是哪个 Key"全靠调用方自觉，
        /// 一旦搞混，"填了个不存在的 Key"就永远查不出来（这个坑在自测里真的踩到过）。
        /// </summary>
        public bool OwnSpriteKeyExists;
        public bool OwnAnimatorKeyExists;
        public bool OwnIconKeyExists;
        public bool OwnBulletKeyExists;
        public bool OwnAudioKeyExists;

        public string PrefabFolder = string.Empty;      // Assets/RemoteResource/Prefabricated body/<Race>
        public string DefinitionFolder = string.Empty;  // Assets/RemoteResource/TroopDefinitions/<Race>
    }

    /// <summary>
    /// 「新建兵种」的**纯逻辑内核**：名字清理、查重、计划与清单的生成。
    ///
    /// 放在这里的原因很直接：真正写盘的那一步（复制预制体、建资产、改注册表）
    /// 只有 Unity 能验，但**"会写出什么名字、会不会撞车、哪些 Key 不存在、建完还差什么"
    /// 这些判断全是纯字符串与列表运算** —— 那部分就可以脱离 Unity 单测（见 README）。
    ///
    /// 命名规则必须与工程现有的「批量补全训练 ID」**完全一致**（`UnitEditorModel.SanitizeTrainingId`：
    /// 小写 + 下划线、折叠连续下划线、去首尾下划线、空则 unit），否则一个新兵种会带出第二种风格。
    /// </summary>
    public static class UnitFactoryCore
    {
        /// <summary>兵营阶级只允许 1/2/3（兵营面板只排这三排）。</summary>
        public const int MinUnlockLevel = 1;
        public const int MaxUnlockLevel = 3;

        #region 名字
        /// <summary>与 UnitEditorModel.SanitizeTrainingId 同一套规则：小写 + 下划线。</summary>
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

        /// <summary>
        /// 文件名清理：只去掉文件系统不认的字符与首尾空白（**中文保留**）。
        /// 与训练 ID 不同 —— 预制体名可以是「Cat 火箭兵」这种，但要提醒 id/资源键会跟着变中文。
        /// </summary>
        public static string SanitizeAssetName(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var sb = new System.Text.StringBuilder(text.Length);
            char[] invalid = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
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

            string result = sb.ToString().Trim();
            while (result.Contains("  "))
                result = result.Replace("  ", " ");
            return result;
        }

        /// <summary>有没有非 ASCII（中文等）字符 —— 文件名能用，但训练 ID / 资源键会跟着变，值得提醒。</summary>
        public static bool HasNonAscii(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] > 127)
                    return true;
            }
            return false;
        }

        /// <summary>在已占用集合之外生成不冲突的 id（撞了就追加 _2、_3…，与批量补全同一套）。</summary>
        public static string MakeUniqueId(string baseId, IList<string> taken)
        {
            string id = SanitizeTrainingId(baseId);
            if (!Contains(taken, id))
                return id;

            for (int n = 2; n < 1000; n++)
            {
                string candidate = id + "_" + n;
                if (!Contains(taken, candidate))
                    return candidate;
            }
            return id + "_x";
        }

        /// <summary>不区分大小写地找（工程里 id 是拿来当引用键的，大小写不同也算撞）。</summary>
        public static bool Contains(IList<string> list, string value)
        {
            if (list == null || string.IsNullOrEmpty(value))
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        #endregion

        #region 计划
        /// <summary>
        /// 算出一份"要做什么"的计划：路径、最终 id、要写的步骤、拦人的错误、提醒、以及建完还差什么。
        /// **不碰任何资产** —— 界面上的「试运行」和真正执行前的确认都读它。
        /// </summary>
        public static UnitFactoryPlan BuildPlan(UnitFactoryRequest request, UnitFactoryContext context)
        {
            var plan = new UnitFactoryPlan();
            if (request == null)
            {
                plan.Errors.Add("没有输入。");
                return plan;
            }

            context = context ?? new UnitFactoryContext();
            request.NewPrefabName = SanitizeAssetName(request.NewPrefabName);
            plan.DisplayName = string.IsNullOrEmpty(request.DisplayName)
                ? request.NewPrefabName
                : request.DisplayName.Trim();

            #region 必填与合法性
            if (string.IsNullOrEmpty(request.Race))
                plan.Errors.Add("没选种族。");
            if (string.IsNullOrEmpty(request.TemplatePrefabName))
                plan.Errors.Add("没选模板兵种（新预制体是从它复制出来的）。");
            if (string.IsNullOrEmpty(request.NewPrefabName))
                plan.Errors.Add("没填新兵种的名字（会用作预制体文件名与资源键）。");
            if (string.IsNullOrEmpty(plan.DisplayName))
                plan.Errors.Add("没填显示名。");

            if (request.UnlockLevel < MinUnlockLevel || request.UnlockLevel > MaxUnlockLevel)
            {
                request.UnlockLevel = Clamp(request.UnlockLevel, MinUnlockLevel, MaxUnlockLevel);
                plan.Warnings.Add("兵营阶级超出 1..3，已按 " + request.UnlockLevel + " 处理（兵营面板只排这三排）。");
            }

            if (request.CreateDefinition && request.Cooldown < 0)
                plan.Errors.Add("训练冷却不能是负数。");
            if (request.CreateDefinition && request.TrainCount < 0)
                plan.Errors.Add("单次产出数量不能是负数。");
            if (request.CreateDefinition && request.Upkeep < 0)
                plan.Errors.Add("维护费不能是负数。");
            #endregion

            if (plan.Errors.Count > 0)
                return plan;

            #region 撞名
            if (Contains(context.ExistingPrefabNames, request.NewPrefabName))
                plan.Errors.Add("已有同名预制体「" + request.NewPrefabName + "」：换一个名字，或先去兵种列表里改那个。");

            plan.PrefabKey = request.NewPrefabName;
            if (request.RegisterPrefabKey && Contains(context.ExistingRegistryKeys, plan.PrefabKey))
            {
                plan.Errors.Add("PrefabRegistry 里已经有「" + plan.PrefabKey + "」这个键了：换名字，或把「登记 PrefabRegistry」关掉手工处理。");
            }

            if (HasNonAscii(request.NewPrefabName))
            {
                plan.Warnings.Add("名字里有中文/全角字符：文件名没问题，但**训练 ID 与资源键也会是中文**。" +
                                  "现有的 id 都是英文（cat_knight、miner_rat），关卡与敌方队伍是按 id 引用的，建议改成英文或拼音。");
            }
            #endregion

            #region 训练 ID
            string wanted = string.IsNullOrEmpty(request.TrainingId) ? request.NewPrefabName : request.TrainingId;
            plan.TrainingId = MakeUniqueId(wanted, context.ExistingTrainingIds);
            if (!string.Equals(plan.TrainingId, SanitizeTrainingId(wanted), StringComparison.Ordinal))
            {
                plan.Warnings.Add("训练 ID「" + SanitizeTrainingId(wanted) + "」已被占用，改用「" + plan.TrainingId + "」。");
            }
            #endregion

            #region 美术 Key
            string sprite = request.ReuseTemplateArtKeys ? context.TemplateSpriteKey : request.SpriteKey;
            string animator = request.ReuseTemplateArtKeys ? context.TemplateAnimatorKey : request.AnimatorControllerKey;
            string icon = request.ReuseTemplateArtKeys ? context.TemplateIconKey : request.IconKey;
            string bullet = request.ReuseTemplateArtKeys ? context.TemplateBulletKey : request.BulletKey;
            string audio = request.ReuseTemplateArtKeys ? context.TemplateAudioKey : request.AudioKey;

            bool spriteExists = request.ReuseTemplateArtKeys ? context.SpriteKeyExists : context.OwnSpriteKeyExists;
            bool animatorExists = request.ReuseTemplateArtKeys ? context.AnimatorKeyExists : context.OwnAnimatorKeyExists;
            bool iconExists = request.ReuseTemplateArtKeys ? context.IconKeyExists : context.OwnIconKeyExists;
            bool bulletExists = request.ReuseTemplateArtKeys ? context.BulletKeyExists : context.OwnBulletKeyExists;
            bool audioExists = request.ReuseTemplateArtKeys ? context.AudioKeyExists : context.OwnAudioKeyExists;

            if (string.IsNullOrEmpty(sprite))
                plan.Warnings.Add("基础动画贴图 Key 是空的：预制体上 StageTexture.spriteKey 没填，角色不会显示贴图。");
            else if (!spriteExists)
                plan.Warnings.Add("贴图 Key「" + sprite + "」不在 SpriteRegistry 里：运行时加载不到（关卡预载也拿不到）。");

            if (string.IsNullOrEmpty(animator))
                plan.Warnings.Add("动画控制器 Key 是空的：预制体上 StageAnimatorController.animatorControllerKey 没填，角色不会动。");
            else if (!animatorExists)
                plan.Warnings.Add("动画控制器 Key「" + animator + "」不在 AnimatorControllerRegistry 里。");

            if (!string.IsNullOrEmpty(bullet) && !bulletExists)
                plan.Warnings.Add("子弹 Key「" + bullet + "」不在 PrefabRegistry 里：远程兵种打不出子弹。");

            if (!string.IsNullOrEmpty(audio) && !audioExists)
                plan.Warnings.Add("语音 Key「" + audio + "」不在 AudioRegistry 里：运行时这段语音放不出来。");

            if (request.CreateDefinition)
            {
                if (string.IsNullOrEmpty(icon))
                    plan.Warnings.Add("兵营图标 Key 是空的：兵营里这个兵种没有图标。");
                else if (!iconExists)
                    plan.Warnings.Add("兵营图标 Key「" + icon + "」不在 SpriteRegistry 里。");
            }
            #endregion

            #region 路径
            plan.PrefabPath = Join(context.PrefabFolder, plan.PrefabKey + ".prefab");
            if (request.CreateDefinition)
                plan.DefinitionPath = Join(context.DefinitionFolder, plan.PrefabKey + ".asset");
            #endregion

            #region 步骤
            plan.Steps.Add("① 复制模板预制体：" + request.TemplatePrefabName + " → " + plan.PrefabPath);
            plan.Steps.Add("② 把根节点改名为「" + plan.PrefabKey + "」" +
                           (request.ReuseTemplateArtKeys
                               ? "（美术 Key 沿用模板）"
                               : "，并改美术 Key（贴图 / 动画 / 子弹 / 语音）"));
            if (request.CopyTemplateStats)
                plan.Steps.Add("　 战斗数值沿用模板（想改就去「战斗数值」页改这个新兵种）");

            if (request.RegisterPrefabKey)
                plan.Steps.Add("③ 登记 PrefabRegistry：" + plan.PrefabKey + " → 新预制体");

            if (request.CreateDefinition)
            {
                plan.Steps.Add("④ 新建训练配置：" + plan.DefinitionPath);
                plan.Steps.Add("　 id=" + plan.TrainingId + "　显示名=" + plan.DisplayName +
                               "　阶级=" + request.UnlockLevel + "　冷却=" + request.Cooldown + "s" +
                               "　产出=" + request.TrainCount + "　维护=" + request.Upkeep);
            }

            if (request.AttachToBarracks && request.CreateDefinition)
            {
                string barracks = request.DarkBarracks ? "黑暗兵营" : "普通兵营";
                plan.Steps.Add("⑤ 挂进" + barracks + "的 BuildingTraining.troops（玩家才训练得到它）");
            }
            #endregion

            #region 还差什么（工具做不到 / 不该替你做的）
            plan.Todo.Add("Addressables：新预制体要进对应的资源组（和同种族的兵种放同一组），否则出包后加载不到 —— " +
                          "这一步在 Addressables Groups 窗口做。");
            if (context.AvailableBarracks.Count == 0)
                plan.Todo.Add("没找到这个种族的兵营预制体：兵营挂载这一步会跳过，要手工往 BuildingTraining.troops 里加。");
            plan.Todo.Add("关卡预载：新兵种要在它出现的关卡里被带进本局（关卡编辑器的资源清单 / 敌方队伍白名单）。");
            plan.Todo.Add("美术资源：贴图、动画控制器、图标、语音如果还是新的，先按现有流程登记进各自的注册表。");
            #endregion

            return plan;
        }

        private static string Join(string folder, string leaf)
        {
            if (string.IsNullOrEmpty(folder))
                return leaf;
            return folder.TrimEnd('/') + "/" + leaf;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
        #endregion
    }
}
