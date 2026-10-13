using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

namespace CageFightUnitTool
{
    /// <summary>
    /// 「新建兵种」的执行端（只有 Unity 能做的部分）：复制预制体、改 Key、建训练配置、
    /// 登记 PrefabRegistry、挂进兵营的 BuildingTraining.troops、加入 Addressables 分组。
    ///
    /// 与纯逻辑的分工：
    ///   · 名字清理、查重、路径、"还差什么"清单 → <see cref="UnitFactoryCore"/>（可脱离 Unity 单测）
    ///   · 真正写盘 → 这里（只能靠编译闸 + 界面上的「试运行」+ 人工目测把关）
    ///
    /// 每一步都遵守工程既有约定：
    ///   · 预制体放 `Assets/RemoteResource/Prefabricated body/&lt;种族&gt;/`，资源键 = 预制体名（与 RegistryBuilder 一致）
    ///   · 训练配置放 `Assets/RemoteResource/TroopDefinitions/&lt;种族&gt;/`（与兵种编辑器既有的"新建训练配置"一致）
    ///   · 改注册表/兵营这类**已有资产**前先备份到 `UnitBackups`（复用 <see cref="UnitBackup"/>）
    ///   · 兵营归属不在兵种上，而在兵营预制体的 `BuildingTraining.troops`（isDarkBarracks 区分普通/黑暗）
    /// </summary>
    public static class UnitFactoryRunner
    {
        /// <summary>副本写在预制体名字后面，方便一眼认出这是刚生成的骨架。</summary>
        private const string AddressableGroupPrefix = "Remote Prefab ";

        #region 上下文
        /// <summary>
        /// 把"现有事实"收集好喂给纯逻辑：撞名、Key 在不在注册表里、这个种族有哪些兵营。
        ///
        /// ⚠️ 存在性标志必须是**这一次真正会用的那个 Key** 的：沿用模板时看模板的 Key，
        /// 自己填了新 Key 时看新填的那个（否则"填了个不存在的 Key"永远查不出来）。
        /// </summary>
        public static UnitFactoryContext BuildContext(UnitCatalog catalog, UnitRecord template, UnitFactoryRequest request)
        {
            var context = new UnitFactoryContext();
            string race = request != null && !string.IsNullOrEmpty(request.Race)
                ? request.Race
                : (template != null ? template.Race : string.Empty);

            context.PrefabFolder = UnitCatalog.PrefabRoot + "/" + race;
            context.DefinitionFolder = UnitCatalog.DefinitionRoot + "/" + race;

            // 现有预制体名（只看这个种族的目录：不同种族同名是可以的，但要提醒）
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { context.PrefabFolder });
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                if (!string.IsNullOrEmpty(path))
                    context.ExistingPrefabNames.Add(Path.GetFileNameWithoutExtension(path));
            }

            if (catalog != null)
            {
                for (int i = 0; i < catalog.Count; i++)
                {
                    UnitRecord record = catalog.Records[i];
                    if (record == null)
                        continue;
                    if (!string.IsNullOrEmpty(record.DefinitionId))
                        context.ExistingTrainingIds.Add(record.DefinitionId);
                    if (!string.IsNullOrEmpty(record.PrefabName) && !context.ExistingPrefabNames.Contains(record.PrefabName))
                        context.ExistingPrefabNames.Add(record.PrefabName);
                }
            }

            UnitResourceIndex index = UnitResourceIndex.Instance;
            IReadOnlyList<string> keys = index.Keys(UnitResKind.Prefab);
            for (int i = 0; i < keys.Count; i++)
                context.ExistingRegistryKeys.Add(keys[i]);

            // 模板身上现在挂的 Key（"沿用模板"时要照着填）
            if (template != null && !string.IsNullOrEmpty(template.PrefabPath))
            {
                UnitStats stats = UnitStatsReader.Read(template.PrefabPath);
                if (stats != null)
                {
                    context.TemplateSpriteKey = stats.SpriteKey ?? string.Empty;
                    context.TemplateAnimatorKey = stats.AnimatorControllerKey ?? string.Empty;
                    context.TemplateBulletKey = stats.AtkObjKey ?? string.Empty;
                    context.TemplateAudioKey = stats.AudioKeys != null && stats.AudioKeys.Length > 0
                        ? (stats.AudioKeys[0] ?? string.Empty)
                        : string.Empty;
                }

                if (template.Definition != null)
                    context.TemplateIconKey = template.Definition.IconKey ?? string.Empty;
            }

            // 模板的 Key 在不在注册表
            context.SpriteKeyExists = index.Has(UnitResKind.Sprite, context.TemplateSpriteKey);
            context.AnimatorKeyExists = index.Has(UnitResKind.AnimatorController, context.TemplateAnimatorKey);
            context.IconKeyExists = index.Has(UnitResKind.Sprite, context.TemplateIconKey);
            context.BulletKeyExists = index.Has(UnitResKind.Prefab, context.TemplateBulletKey);
            context.AudioKeyExists = index.Has(UnitResKind.Audio, context.TemplateAudioKey);

            // 用户自己填的 Key 在不在注册表（两组分开，纯逻辑那边按"这一次用哪个"取值）
            if (request != null && !request.ReuseTemplateArtKeys)
            {
                context.OwnSpriteKeyExists = index.Has(UnitResKind.Sprite, request.SpriteKey);
                context.OwnAnimatorKeyExists = index.Has(UnitResKind.AnimatorController, request.AnimatorControllerKey);
                context.OwnIconKeyExists = index.Has(UnitResKind.Sprite, request.IconKey);
                context.OwnBulletKeyExists = index.Has(UnitResKind.Prefab, request.BulletKey);
                context.OwnAudioKeyExists = index.Has(UnitResKind.Audio, request.AudioKey);
            }

            List<GameObject> barracks = FindBarracksPrefabs(race);
            for (int i = 0; i < barracks.Count; i++)
            {
                BuildingTraining training = barracks[i].GetComponentInChildren<BuildingTraining>(true);
                bool dark = training != null && training.IsDarkBarracks;
                context.AvailableBarracks.Add(barracks[i].name + (dark ? "（黑暗）" : "（普通）"));
            }

            return context;
        }

        /// <summary>试运行：只算计划，不碰任何资产。</summary>
        public static UnitFactoryPlan Plan(UnitCatalog catalog, UnitFactoryRequest request)
        {
            UnitRecord template = FindRecord(catalog, request != null ? request.TemplatePrefabName : null);
            UnitFactoryContext context = BuildContext(catalog, template, request);
            return UnitFactoryCore.BuildPlan(request, context);
        }
        #endregion

        #region 执行
        /// <summary>
        /// 真正写盘。返回给人看的报告（每一步的结果 + 还差什么）。
        /// 有一步失败就停下并如实说明**已经写进去的东西**（不做静默回滚：资产新建后回滚反而更容易留下半成品）。
        /// </summary>
        public static string Run(UnitCatalog catalog, UnitFactoryRequest request, out UnitFactoryPlan plan)
        {
            plan = Plan(catalog, request);
            if (!plan.CanRun)
                return "计划没通过，没有写任何东西。";

            var report = new List<string>();
            UnitRecord template = FindRecord(catalog, request.TemplatePrefabName);
            if (template == null || string.IsNullOrEmpty(template.PrefabPath))
                return "找不到模板预制体：" + request.TemplatePrefabName;

            string race = string.IsNullOrEmpty(request.Race) ? template.Race : request.Race;
            string prefabFolder = UnitCatalog.PrefabRoot + "/" + race;
            string definitionFolder = UnitCatalog.DefinitionRoot + "/" + race;

            if (!EnsureFolder(prefabFolder, report))
                return Report(report);
            EnsureFolder(definitionFolder, report);

            // ── ① 复制模板 → 新预制体，并在复制出来的那份上改名字与 Key ──
            GameObject prefabAsset = null;
            try
            {
                GameObject contents = PrefabUtility.LoadPrefabContents(template.PrefabPath);
                try
                {
                    // ⚠️ **必须把根节点改名**：资源键是「根 GameObject 的名字」，不是文件名
                    // （RegistryBuilder.BuildPrefabs 用的是 prefab.name，重复的名字会被跳过并警告）。
                    // 不改名的话，新预制体在注册表里会跟模板撞名。
                    contents.name = plan.PrefabKey;
                    report.Add("· 根节点已改名：" + plan.PrefabKey);

                    if (HasChildNamed(contents, "HPUp"))
                        report.Add("· 模板带血条子物体（HPUp/HPBottom），一起复制过来了");

                    ApplyArtKeys(contents, request, report);
                    prefabAsset = PrefabUtility.SaveAsPrefabAsset(contents, plan.PrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }
            catch (Exception e)
            {
                report.Add("× 复制预制体失败：" + e.Message);
                return Report(report);
            }

            if (prefabAsset == null)
            {
                report.Add("× 预制体没有生成成功（SaveAsPrefabAsset 返回 null）。");
                return Report(report);
            }

            report.Add("√ 预制体：" + plan.PrefabPath);

            // ── ② 加入 Addressables 分组（要在登记注册表之前，注册表是按分组条目生成的）──
            string addressableNote = RegisterAddressable(prefabAsset, plan.PrefabKey, race);
            report.Add(addressableNote);

            // ── ③ 登记 PrefabRegistry（只加一条，不改别的）──
            if (request.RegisterPrefabKey)
            {
                string registryNote;
                if (AddPrefabRegistryEntry(prefabAsset, plan.PrefabKey, out registryNote))
                    report.Add("√ " + registryNote);
                else
                    report.Add("× " + registryNote);
            }

            // ── ④ 建训练配置 ──
            TroopDefinition definition = null;
            if (request.CreateDefinition)
            {
                definition = CreateDefinition(definitionFolder, plan, request, out string defNote);
                report.Add((definition != null ? "√ " : "× ") + defNote);
            }

            // ── ⑤ 挂进兵营的 BuildingTraining.troops ──
            if (request.AttachToBarracks && definition != null)
            {
                string barracksNote;
                if (AttachToBarracks(race, request.DarkBarracks, definition, out barracksNote))
                    report.Add("√ " + barracksNote);
                else
                    report.Add("× " + barracksNote);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            UnitResourceIndex.Invalidate();
            UnitBarracksIndex.Invalidate();

            report.Add(string.Empty);
            report.Add("还差什么（工具做不到的）：");
            for (int i = 0; i < plan.Todo.Count; i++)
                report.Add("　· " + plan.Todo[i]);

            return Report(report);
        }

        private static string Report(List<string> lines)
        {
            return string.Join("\n", lines.ToArray());
        }
        #endregion

        #region 分步实现
        /// <summary>改新预制体上的资源 Key（贴图 / 动画控制器 / 子弹），沿用模板时什么都不做。</summary>
        private static void ApplyArtKeys(GameObject root, UnitFactoryRequest request, List<string> report)
        {
            if (request.ReuseTemplateArtKeys)
            {
                report.Add("· 美术 Key 沿用模板（贴图 / 动画控制器 / 子弹都没改）");
                return;
            }

            var changed = new List<string>();

            StageTexture texture = root.GetComponent<StageTexture>();
            if (texture != null && !string.IsNullOrEmpty(request.SpriteKey))
            {
                SetField(texture, "spriteKey", request.SpriteKey);
                changed.Add("贴图=" + request.SpriteKey);
            }

            StageAnimatorController animator = root.GetComponent<StageAnimatorController>();
            if (animator != null && !string.IsNullOrEmpty(request.AnimatorControllerKey))
            {
                SetField(animator, "animatorControllerKey", request.AnimatorControllerKey);
                changed.Add("动画=" + request.AnimatorControllerKey);
            }

            GameObjectProperty property = root.GetComponent<GameObjectProperty>();
            if (property != null && !string.IsNullOrEmpty(request.BulletKey))
            {
                SetField(property, "atkObj", request.BulletKey);
                changed.Add("子弹=" + request.BulletKey);
            }

            // 语音：StageAudio.audioKey1（key 约定是「美术名 + A」；不填就是这条兵没有语音）
            StageAudio audio = root.GetComponent<StageAudio>();
            if (audio != null)
            {
                SetField(audio, "audioKey1", request.AudioKey ?? string.Empty);
                changed.Add(string.IsNullOrEmpty(request.AudioKey) ? "语音=清空" : ("语音=" + request.AudioKey));
            }

            report.Add(changed.Count > 0
                ? "· 美术 Key 已改：" + string.Join("，", changed.ToArray())
                : "· 美术 Key 没改（没填新 Key，或预制体上没有对应组件）");
        }

        private static bool HasChildNamed(GameObject root, string name)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && string.Equals(all[i].name, name, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        /// <summary>把新预制体加进 `Remote Prefab &lt;种族&gt;` 分组；地址对齐成资源键。</summary>
        private static string RegisterAddressable(GameObject prefab, string key, string race)
        {
            try
            {
                AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
                if (settings == null)
                    return "× Addressables 还没配置（Addressables Groups 面板里没有 Settings）：这一步跳过，要手工加分组。";

                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
                if (string.IsNullOrEmpty(guid))
                    return "× 取不到新预制体的 GUID：Addressables 这一步跳过。";

                AddressableAssetGroup group = settings.FindGroup(AddressableGroupPrefix + race);
                if (group == null)
                {
                    // 名字对不上就从现成分组里挑一个同种族的，避免新建分组打乱打包规划
                    for (int i = 0; i < settings.groups.Count && group == null; i++)
                    {
                        AddressableAssetGroup candidate = settings.groups[i];
                        if (candidate != null &&
                            candidate.Name.IndexOf(race, StringComparison.OrdinalIgnoreCase) >= 0 &&
                            candidate.Name.IndexOf("Prefab", StringComparison.OrdinalIgnoreCase) >= 0)
                            group = candidate;
                    }
                }

                if (group == null)
                {
                    group = settings.DefaultGroup;
                    if (group == null)
                        return "× 没找到「" + AddressableGroupPrefix + race + "」分组，也没有默认分组：要手工加进 Addre​ssables。";
                }

                var entry = settings.CreateOrMoveEntry(guid, group);
                if (entry == null)
                    return "× 往分组里加条目失败：要手工加进 Addressables。";

                entry.address = key;   // 与工程约定一致：地址 = 预制体名 = 资源键
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, entry, true, true);
                AssetDatabase.SaveAssets();

                return "√ Addressables：已加入分组「" + group.Name + "」（地址 " + key + "）";
            }
            catch (Exception e)
            {
                return "× Addressables 这一步出错（其余步骤照常）：" + e.Message;
            }
        }

        /// <summary>往 PrefabRegistry 里加一条（key + 强引用 + AssetReference 的 GUID）。</summary>
        private static bool AddPrefabRegistryEntry(GameObject prefab, string key, out string note)
        {
            string path = UnitResourceIndex.Instance.RegistryPath(UnitResKind.Prefab);
            if (string.IsNullOrEmpty(path))
            {
                note = "找不到 PrefabRegistry 资产：资源键没登记，要跑一次「关卡构建/资源构建/一键生成全部资源注册表」。";
                return false;
            }

            var registry = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(path);
            if (registry == null)
            {
                note = "读不到 PrefabRegistry：" + path;
                return false;
            }

            UnitBackup.BackupAsset(path, "新建兵种前");

            var so = new SerializedObject(registry);
            SerializedProperty mappings = so.FindProperty("mappings");
            if (mappings == null)
            {
                note = "PrefabRegistry 上没有 mappings 字段（结构变了？）：这一步跳过。";
                return false;
            }

            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
            for (int i = 0; i < mappings.arraySize; i++)
            {
                SerializedProperty existing = mappings.GetArrayElementAtIndex(i).FindPropertyRelative("key");
                if (existing != null && string.Equals(existing.stringValue, key, StringComparison.Ordinal))
                {
                    note = "PrefabRegistry 里已经有这个键了：" + key;
                    return false;
                }
            }

            mappings.InsertArrayElementAtIndex(mappings.arraySize);
            SerializedProperty entry = mappings.GetArrayElementAtIndex(mappings.arraySize - 1);

            SerializedProperty keyProp = entry.FindPropertyRelative("key");
            if (keyProp != null)
                keyProp.stringValue = key;

            SerializedProperty prefabProp = entry.FindPropertyRelative("prefab");
            if (prefabProp != null)
                prefabProp.objectReferenceValue = prefab;   // 编辑器下的强引用（预览用）

            SerializedProperty referenceProp = entry.FindPropertyRelative("prefabReference");
            if (referenceProp != null)
            {
                SerializedProperty referenceGuid = referenceProp.FindPropertyRelative("m_AssetGUID");
                if (referenceGuid != null)
                    referenceGuid.stringValue = guid;       // Addressables 引用（打包用）
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(registry);

            note = "PrefabRegistry：" + key + " → " + prefab.name + "（已备份原注册表）";
            return true;
        }

        /// <summary>建 TroopDefinition 并填字段（字段全是 private [SerializeField]，只能走 SerializedObject）。</summary>
        private static TroopDefinition CreateDefinition(
            string folder, UnitFactoryPlan plan, UnitFactoryRequest request, out string note)
        {
            if (AssetDatabase.LoadAssetAtPath<TroopDefinition>(plan.DefinitionPath) != null)
            {
                note = "训练配置已存在，没有覆盖：" + plan.DefinitionPath;
                return null;
            }

            var definition = ScriptableObject.CreateInstance<TroopDefinition>();
            AssetDatabase.CreateAsset(definition, plan.DefinitionPath);

            string icon = request.ReuseTemplateArtKeys ? string.Empty : request.IconKey;
            string animationSprite = request.ReuseTemplateArtKeys ? string.Empty : request.SpriteKey;
            string animator = request.ReuseTemplateArtKeys ? string.Empty : request.AnimatorControllerKey;

            var so = new SerializedObject(definition);
            SetIfExists(so, "id", plan.TrainingId);
            SetIfExists(so, "displayName", plan.DisplayName);
            SetIfExists(so, "prefabKey", plan.PrefabKey);
            SetIfExists(so, "animatorControllerKey", animator);
            SetIfExists(so, "animationSpriteKey", animationSprite);
            SetIfExists(so, "iconKey", icon);
            SetIfExists(so, "unlockLevel", request.UnlockLevel);
            SetIfExists(so, "tier", Mathf.Clamp(request.UnlockLevel, 1, 3));   // AI 威胁阶级与兵营阶级对齐起步
            SetIfExists(so, "tag", request.Tag);                               // 兵种标签（AI 威胁判定用，出生时注入到单位上）
            SetIfExists(so, "cooldown", (float)request.Cooldown);
            SetIfExists(so, "trainCount", request.TrainCount);
            SetIfExists(so, "upkeep", request.Upkeep);
            SetIfExists(so, "threatScore", request.ThreatScore);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(definition);

            note = "训练配置：" + plan.DefinitionPath + "（id=" + plan.TrainingId + "）";
            return definition;
        }

        /// <summary>把训练配置挂进对应兵营预制体的 BuildingTraining.troops。</summary>
        private static bool AttachToBarracks(string race, bool dark, TroopDefinition definition, out string note)
        {
            List<GameObject> barracks = FindBarracksPrefabs(race);
            GameObject target = null;
            for (int i = 0; i < barracks.Count; i++)
            {
                BuildingTraining training = barracks[i].GetComponentInChildren<BuildingTraining>(true);
                if (training != null && training.IsDarkBarracks == dark)
                {
                    target = barracks[i];
                    break;
                }
            }

            if (target == null)
            {
                note = "没找到「" + race + "」的" + (dark ? "黑暗" : "普通") +
                       "兵营预制体：要手工把训练配置拖进 BuildingTraining.Troops。";
                return false;
            }

            string path = AssetDatabase.GetAssetPath(target);
            BuildingTraining component = target.GetComponentInChildren<BuildingTraining>(true);
            UnitBackup.BackupAsset(path, "新建兵种前");

            var so = new SerializedObject(component);
            SerializedProperty troops = so.FindProperty("troops");
            if (troops == null)
            {
                note = "兵营预制体上没有 troops 字段：" + path;
                return false;
            }

            for (int i = 0; i < troops.arraySize; i++)
            {
                SerializedProperty slot = troops.GetArrayElementAtIndex(i);
                if (slot.objectReferenceValue == definition)
                {
                    note = "这个训练配置已经在兵营里了：" + target.name;
                    return false;
                }
            }

            troops.InsertArrayElementAtIndex(troops.arraySize);
            troops.GetArrayElementAtIndex(troops.arraySize - 1).objectReferenceValue = definition;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(component);

            note = "已挂进兵营：" + target.name + "（troops 第 " + troops.arraySize + " 条，已备份该预制体）";
            return true;
        }

        /// <summary>
        /// 找某个种族的兵营预制体：优先按种族资产 `RaceDefinition.buildings` 里的
        /// `BuildingType.Barracks(1)/DarkBarracks(2)` 条目找（这是**权威**链路，运行时就按它查），
        /// 找不到再退回"名字里带 Barracks 的预制体"。
        /// </summary>
        private static List<GameObject> FindBarracksPrefabs(string race)
        {
            var result = new List<GameObject>();
            if (string.IsNullOrEmpty(race))
                return result;

            string[] raceGuids = AssetDatabase.FindAssets("t:RaceDefinition");
            for (int i = 0; i < raceGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(raceGuids[i]);
                var definition = AssetDatabase.LoadAssetAtPath<RaceDefinition>(path);
                if (definition == null)
                    continue;

                bool sameRace = string.Equals(definition.Id, race, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(Path.GetFileNameWithoutExtension(path), race, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(definition.name, race, StringComparison.OrdinalIgnoreCase);
                if (!sameRace || definition.Buildings == null)
                    continue;

                for (int b = 0; b < definition.Buildings.Count; b++)
                {
                    RaceBuildingEntry entry = definition.Buildings[b];
                    if (entry == null || string.IsNullOrEmpty(entry.prefabKey))
                        continue;
                    if (entry.buildingType != BuildingType.Barracks && entry.buildingType != BuildingType.DarkBarracks)
                        continue;

                    GameObject prefab = UnitResourceIndex.Instance.GetPrefab(entry.prefabKey);
                    if (prefab != null && !result.Contains(prefab))
                        result.Add(prefab);
                }

                if (result.Count > 0)
                    return result;
            }

            // 兜底：按名字找（与 UnitBarracksIndex 同一套过滤）
            string[] guids = AssetDatabase.FindAssets("Barracks t:Prefab", new[] { "Assets/RemoteResource" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                    continue;
                if (path.IndexOf("/" + race + "/", StringComparison.OrdinalIgnoreCase) < 0 &&
                    Path.GetFileName(path).IndexOf(race, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null && prefab.GetComponentInChildren<BuildingTraining>(true) != null && !result.Contains(prefab))
                    result.Add(prefab);
            }

            return result;
        }

        private static UnitRecord FindRecord(UnitCatalog catalog, string prefabName)
        {
            if (catalog == null || string.IsNullOrEmpty(prefabName))
                return null;

            for (int i = 0; i < catalog.Count; i++)
            {
                UnitRecord record = catalog.Records[i];
                if (record != null && string.Equals(record.PrefabName, prefabName, StringComparison.OrdinalIgnoreCase))
                    return record;
            }
            return null;
        }

        private static bool EnsureFolder(string folder, List<string> report)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return true;

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
            {
                report.Add("× 目录不合法：" + folder);
                return false;
            }

            if (!AssetDatabase.IsValidFolder(parent) && !EnsureFolder(parent, report))
                return false;

            string guid = AssetDatabase.CreateFolder(parent, leaf);
            if (string.IsNullOrEmpty(guid))
            {
                report.Add("× 创建目录失败：" + folder);
                return false;
            }

            report.Add("· 新建目录：" + folder);
            return true;
        }

        private static void SetField(UnityEngine.Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
                return;

            if (value is string)
                property.stringValue = (string)value;
            else if (value is int)
                property.intValue = (int)value;
            else if (value is float)
                property.floatValue = (float)value;
            else if (value is bool)
                property.boolValue = (bool)value;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private static void SetIfExists(SerializedObject so, string field, object value)
        {
            SerializedProperty property = so.FindProperty(field);
            if (property == null)
                return;

            if (value is string)
                property.stringValue = (string)value;
            else if (value is int)
                property.intValue = (int)value;
            else if (value is float)
                property.floatValue = (float)value;
            else if (value is bool)
                property.boolValue = (bool)value;
        }
        #endregion
    }
}
