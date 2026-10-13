using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightEngineerTool
{
    /// <summary>
    /// 选装注册表（LoadoutDefinitionRegistry.asset）的写入：
    /// 把工程师登记进 engineerDefinitions、设置默认工程师、移出。
    ///
    /// 走 SerializedObject 而不是调用 LoadoutDefinitionRegistry.ReplaceDefinitions ——
    /// 后者会**整表替换**工程师+种族+法术三张列表，一次手滑就可能把法术表清空。
    /// 这里只动工程师那一条，其余字段一个不碰。
    /// </summary>
    public static class EngineerRegistryWriter
    {
        public static string Add(EngineerRecord record, string reason = "补进注册表前")
        {
            if (record == null || record.Definition == null)
                return null;

            const string path = EngineerResourceIndex.LoadoutRegistryPath;
            LoadoutDefinitionRegistry registry = AssetDatabase.LoadAssetAtPath<LoadoutDefinitionRegistry>(path);
            if (registry == null)
            {
                Debug.LogError("[工程师编辑器] 找不到选装注册表：" + path);
                return null;
            }

            string backup = EngineerWriter.Backup(path, reason);
            var so = new SerializedObject(registry);
            SerializedProperty list = so.FindProperty("engineerDefinitions");
            if (list == null || !list.isArray)
            {
                Debug.LogError("[工程师编辑器] 注册表里找不到 engineerDefinitions 列表。");
                return backup;
            }

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == record.Definition)
                    return backup;   // 已经在里面了
            }

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = record.Definition;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            EngineerResourceIndex.Invalidate();
            return backup;
        }

        public static string Remove(EngineerRecord record, string reason = "移出注册表前")
        {
            if (record == null || record.Definition == null)
                return null;

            const string path = EngineerResourceIndex.LoadoutRegistryPath;
            LoadoutDefinitionRegistry registry = AssetDatabase.LoadAssetAtPath<LoadoutDefinitionRegistry>(path);
            if (registry == null)
                return null;

            string backup = EngineerWriter.Backup(path, reason);
            var so = new SerializedObject(registry);
            SerializedProperty list = so.FindProperty("engineerDefinitions");
            if (list == null || !list.isArray)
                return backup;

            for (int i = list.arraySize - 1; i >= 0; i--)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == record.Definition)
                    list.DeleteArrayElementAtIndex(i);
            }

            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            EngineerResourceIndex.Invalidate();
            return backup;
        }

        /// <summary>设置注册表的默认工程师（defaultEngineerId）。</summary>
        public static string SetDefaultEngineer(string engineerId, string reason = "改默认工程师前")
        {
            const string path = EngineerResourceIndex.LoadoutRegistryPath;
            LoadoutDefinitionRegistry registry = AssetDatabase.LoadAssetAtPath<LoadoutDefinitionRegistry>(path);
            if (registry == null)
                return null;

            string backup = EngineerWriter.Backup(path, reason);
            var so = new SerializedObject(registry);
            SerializedProperty prop = so.FindProperty("defaultEngineerId");
            if (prop != null)
            {
                prop.stringValue = engineerId ?? string.Empty;
                so.ApplyModifiedProperties();
                AssetDatabase.SaveAssets();
                EngineerResourceIndex.Invalidate();
            }
            return backup;
        }
    }

    /// <summary>
    /// 工程师定义资产（EngineerDefinition）的写入。
    /// 字段都是 private [SerializeField]，所以统一走 SerializedObject，不反射、不猜字段名。
    /// </summary>
    public static class EngineerAssetWriter
    {
        /// <summary>删掉待机帧数组里的空项（一键修复用）。</summary>
        public static string RemoveBlankIdleFrames(EngineerRecord record)
        {
            if (record == null || record.Definition == null)
                return null;

            string backup = EngineerWriter.Backup(record.AssetPath, "清理空待机帧前");
            var so = new SerializedObject(record.Definition);
            SerializedProperty frames = so.FindProperty("idlePortraitFrameKeys");
            if (frames == null || !frames.isArray)
                return backup;

            for (int i = frames.arraySize - 1; i >= 0; i--)
            {
                if (string.IsNullOrEmpty(frames.GetArrayElementAtIndex(i).stringValue))
                    frames.DeleteArrayElementAtIndex(i);
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(record.Definition);
            AssetDatabase.SaveAssets();
            record.LastBackup = backup;
            return backup;
        }

        /// <summary>把待机帧率夹回 1~20（一键修复用）。</summary>
        public static string ClampIdleFrameRate(EngineerRecord record)
        {
            if (record == null || record.Definition == null)
                return null;

            string backup = EngineerWriter.Backup(record.AssetPath, "夹待机帧率前");
            var so = new SerializedObject(record.Definition);
            SerializedProperty rate = so.FindProperty("idlePortraitFrameRate");
            if (rate != null)
            {
                rate.floatValue = Mathf.Clamp(rate.floatValue, 1f, 20f);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(record.Definition);
                AssetDatabase.SaveAssets();
            }
            record.LastBackup = backup;
            return backup;
        }

        /// <summary>按一串键覆写待机帧数组（界面上增删/改键后调用）。</summary>
        public static string SetIdleFrames(EngineerRecord record, List<string> keys)
        {
            if (record == null || record.Definition == null)
                return null;

            var so = new SerializedObject(record.Definition);
            SerializedProperty frames = so.FindProperty("idlePortraitFrameKeys");
            if (frames == null || !frames.isArray)
                return null;

            frames.arraySize = keys != null ? keys.Count : 0;
            for (int i = 0; i < frames.arraySize; i++)
                frames.GetArrayElementAtIndex(i).stringValue = keys[i] ?? string.Empty;

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(record.Definition);
            return null;   // 真正的写盘与备份由调用方的「保存」统一做
        }
    }
}
