using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CageFightStageTool
{
    /// <summary>
    /// 「新建关卡」窗口。两种来源：
    ///
    /// · **从当前场景导出** —— 直接复用现成的「关卡构建 → 创建新关卡」流程（<see cref="StageExporter"/>），
    ///   把场景里打了 <c>StageObjectMarker</c> 的物体、己方大本营标记与扫描到的全部资源键导成一个新资产。
    ///   这样工具本身不复制一套场景导出逻辑，两边行为永远一致。
    /// · **复制当前关卡** —— 把当前关卡整个拷一份（预摆对象 / 敌方队伍 / 资源清单都在），
    ///   适合"照着一关改一关"，不用回场景重新导出。
    ///
    /// 两种来源都**只新增资产**，不动任何原有文件；目标路径已被占用时必须显式确认覆盖，
    /// 且覆盖前自动备份一份（走和「保存」同一套备份目录，可用「备份 ▾」找回）。
    /// </summary>
    public sealed class LevelEditorNewStageWindow : EditorWindow
    {
        private const string WindowTitle = "新建关卡";

        /// <summary>本地测试关（编号 ≥880）不参与"下一关编号"的推算。</summary>
        private const int LocalTestStageId = 880;

        private enum Source
        {
            FromScene,
            Duplicate
        }

        private Source _source = Source.FromScene;
        private int _stageId = 1;
        private UserGlobalInfo.StageType _stageType = UserGlobalInfo.StageType.Defense;
        private float _duration = 120f;
        private Sprite _icon;

        private StageConfig _duplicateSource;
        private Action<string> _onFinished;

        // 目标路径占用检查：只在编号变化时碰一次磁盘，不是每帧。
        private int _checkedId = -1;
        private bool _occupied;

        /// <param name="duplicateSource">「复制当前关卡」的源；当前没有加载关卡时传 null。</param>
        /// <param name="startWithDuplicate">打开时默认停在「复制当前关卡」这一页。</param>
        /// <param name="onFinished">结束回调。参数是新建资产的路径；失败时为 null。</param>
        public static void Open(StageConfig duplicateSource, bool startWithDuplicate, Action<string> onFinished)
        {
            var window = GetWindow<LevelEditorNewStageWindow>(true, WindowTitle, true);
            window.minSize = new Vector2(430f, 350f);
            window.titleContent = new GUIContent(WindowTitle);
            window._duplicateSource = duplicateSource;
            window._onFinished = onFinished;
            // 没有源就没法"复制"，此时无论调用方想要哪一页都落到"从当前场景导出"
            window._source = startWithDuplicate && duplicateSource != null ? Source.Duplicate : Source.FromScene;
            window._stageId = NextFreeStageId();
            window._checkedId = -1;
            window.Show();
            window.Focus();
        }

        /// <summary>下一个可用的正式关编号：取现有正式关的最大编号 +1，被占就继续往后找。</summary>
        private static int NextFreeStageId()
        {
            var used = new HashSet<int>();
            int max = 0;

            List<StageInfo> stages = StageEditorData.DiscoverStages();
            for (int i = 0; i < stages.Count; i++)
            {
                int id = stages[i].StageId;
                used.Add(id);
                // 测试关（≥880）不参与推算，否则新建正式关会直接跳到 880+
                if (id < LocalTestStageId && id > max)
                    max = id;
            }

            int next = max + 1;
            while (used.Contains(next))
                next++;
            return next;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4f);

            DrawSourcePicker();
            EditorGUILayout.Space(6f);
            DrawFields();

            EditorGUILayout.Space(6f);
            DrawTargetLine();

            GUILayout.FlexibleSpace();
            DrawButtons();
        }

        private void DrawSourcePicker()
        {
            EditorGUILayout.LabelField("来源", EditorStyles.boldLabel);

            _source = (Source)GUILayout.Toolbar((int)_source, new[] { "从当前场景导出", "复制当前关卡" });

            if (_source == Source.FromScene)
            {
                EditorGUILayout.LabelField(
                    "走现成的「关卡构建 → 创建新关卡」流程：读取**当前打开的场景**，" +
                    "收集打了 StageObjectMarker 的物体、己方大本营标记与全部资源键。",
                    LevelEditorUI.RichLabel);
            }
            else if (_duplicateSource == null)
            {
                GUI.contentColor = LevelEditorUI.Error;
                EditorGUILayout.LabelField("当前没有加载任何关卡，无法复制。请先切到「从当前场景导出」。", LevelEditorUI.MiniLabel);
                GUI.contentColor = Color.white;
            }
            else
            {
                int objects = _duplicateSource.objects != null ? _duplicateSource.objects.Count : 0;
                int teams = _duplicateSource.enemyTeams != null ? _duplicateSource.enemyTeams.Count : 0;
                EditorGUILayout.LabelField(
                    "复制 Stage" + _duplicateSource.stageId + "（预摆 " + objects + " 个、敌队 " + teams +
                    " 支、资源清单一起带过来），不用回场景重新导出。",
                    LevelEditorUI.RichLabel);
            }
        }

        private void DrawFields()
        {
            EditorGUILayout.LabelField("新关卡", EditorStyles.boldLabel);

            _stageId = Mathf.Max(0, EditorGUILayout.IntField("关卡编号", _stageId));

            using (new EditorGUI.DisabledScope(_source == Source.Duplicate))
            {
                _stageType = (UserGlobalInfo.StageType)EditorGUILayout.EnumPopup("玩法类型", _stageType);
                _duration = Mathf.Max(0f, EditorGUILayout.FloatField("防守时限（秒）", _duration));
                _icon = (Sprite)EditorGUILayout.ObjectField("关卡图标", _icon, typeof(Sprite), false);
            }

            if (_source == Source.Duplicate)
            {
                EditorGUILayout.LabelField("玩法 / 时限 / 图标沿用源关卡，创建后在右栏改。", LevelEditorUI.MiniLabel);
                EditorGUILayout.Space(18f);
            }
            else
            {
                EditorGUILayout.LabelField(
                    "进攻 / Boss 关的时限是另外两个字段（attackTimeLimit / bossTimeLimit），" +
                    "这里只填防守时限，创建后在右栏「玩法与时限」里改。",
                    LevelEditorUI.RichLabel);
            }

            DrawRewardTemplatePreview();
        }

        /// <summary>
        /// 创建前先告诉策划：新关卡的通关奖励会用哪一套（按关卡类型的奖励模板，R1.29）。
        /// </summary>
        private void DrawRewardTemplatePreview()
        {
            UserGlobalInfo.StageType type = _source == Source.Duplicate && _duplicateSource != null
                ? _duplicateSource.stageType
                : _stageType;

            StageRewardTemplate template = LevelEditorPrefs.GetRewardTemplate(type);
            LevelEditorUI.Space(4f);
            EditorGUILayout.LabelField("通关奖励", EditorStyles.boldLabel);

            if (template.Valid)
            {
                EditorGUILayout.LabelField(
                    "会用「" + StageRewardTemplate.TypeName(type) + "」的奖励模板：" + template.Describe() +
                    "（创建后可在右栏「通关奖励」里改）", LevelEditorUI.RichLabel);
            }
            else
            {
                EditorGUILayout.LabelField(
                    "「" + StageRewardTemplate.TypeName(type) + "」还没设过奖励模板 → 奖励沿用默认值" +
                    "（关卡构建的默认 / 复制源的数值），创建后可在右栏存模板。", LevelEditorUI.RichLabel);
            }
        }

        private void DrawTargetLine()
        {
            EditorGUILayout.LabelField("写入路径", EditorStyles.boldLabel);

            string assetPath = AssetPathFor(_stageId);
            if (RefreshOccupied())
            {
                GUI.contentColor = LevelEditorUI.Warn;
                EditorGUILayout.LabelField(assetPath + "（已存在 —— 会覆盖，覆盖前自动备份）", LevelEditorUI.MiniLabel);
                GUI.contentColor = Color.white;
            }
            else
            {
                EditorGUILayout.LabelField(assetPath, LevelEditorUI.MiniLabel);
            }
        }

        private void DrawButtons()
        {
            bool canCreate = !(_source == Source.Duplicate && _duplicateSource == null);
            bool occupied = RefreshOccupied();

            EditorGUILayout.BeginHorizontal();

            using (new EditorGUI.DisabledScope(!canCreate))
            {
                string label = occupied ? "覆盖创建（先备份）" : "创建";
                if (GUILayout.Button(label, GUILayout.Height(30f)))
                    Create();
            }

            if (GUILayout.Button("取消", GUILayout.Height(30f), GUILayout.Width(70f)))
                Close();

            EditorGUILayout.EndHorizontal();

            if (!canCreate)
                EditorGUILayout.LabelField("选了「复制当前关卡」但没有源，创建按钮不可用。", LevelEditorUI.MiniLabel);
        }

        #region 创建
        private void Create()
        {
            string assetPath = AssetPathFor(_stageId);

            if (_source == Source.Duplicate)
                CreateByDuplicate(assetPath);
            else
                CreateFromScene(assetPath);

            Close();
        }

        /// <summary>复制当前关卡。</summary>
        private void CreateByDuplicate(string assetPath)
        {
            if (_duplicateSource == null)
            {
                Finish(null);
                return;
            }

            // 覆盖前先把磁盘上的旧文件备份走，和「保存」同一套备份目录。
            if (RefreshOccupied())
                StageEditorData.BackupFile(assetPath, "覆盖前");

            // ScriptableObject 的 Instantiate 是走序列化的深拷贝：预摆对象、敌方队伍这些
            // 可序列化字段都会是新的一份，不会和源关卡共享引用。
            StageConfig copy = Instantiate(_duplicateSource);
            copy.name = "Stage" + _stageId;
            copy.stageId = _stageId;

            AssetDatabase.CreateAsset(copy, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ApplyRewardTemplate(assetPath);
            Finish(assetPath);
        }

        /// <summary>从当前场景导出（复用 StageExporter，不复制它的逻辑）。</summary>
        private void CreateFromScene(string assetPath)
        {
            string folder = StageEditorData.DefaultStagesFolder;

            bool occupied = RefreshOccupied();
            if (occupied)
                StageEditorData.BackupFile(assetPath, "覆盖前");

            // StageExporter 返回 void：己方大本营标记不合法时它会自己弹窗并**直接返回**，
            // 不会创建资产。所以这里用"文件有没有变"来判断到底成没成，失败时状态栏给个提示。
            string absolute = ToAbsolutePath(assetPath);
            DateTime beforeWrite = DateTime.MinValue;
            long beforeLength = -1;
            bool existedBefore = File.Exists(absolute);
            if (existedBefore)
            {
                beforeWrite = File.GetLastWriteTimeUtc(absolute);
                beforeLength = new FileInfo(absolute).Length;
            }

            StageExporter.ExportStage((uint)_stageId, folder, _stageType, _duration, _icon);
            AssetDatabase.Refresh();

            bool created = File.Exists(absolute) && (!existedBefore
                || File.GetLastWriteTimeUtc(absolute) != beforeWrite
                || new FileInfo(absolute).Length != beforeLength);

            if (created)
                ApplyRewardTemplate(assetPath);

            Finish(created ? assetPath : null);
        }

        /// <summary>
        /// 用「该关卡类型的奖励模板」覆盖新建关卡的奖励值（R1.29）。
        /// 没设过模板就什么都不做 —— 保持导出器 / 复制源的原始奖励。
        /// </summary>
        private static void ApplyRewardTemplate(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            StageConfig config = AssetDatabase.LoadAssetAtPath<StageConfig>(assetPath);
            if (config == null)
                return;

            StageRewardTemplate template = LevelEditorPrefs.GetRewardTemplate(config.stageType);
            if (!template.Valid)
                return;

            StageEditorData.ApplyRewardTemplate(config, template);
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            Debug.Log("[关卡编辑器] 新建 " + config.name + "：已套用「" +
                StageRewardTemplate.TypeName(config.stageType) + "」奖励模板 —— " + template.Describe());
        }

        private void Finish(string assetPath)
        {
            Action<string> callback = _onFinished;
            _onFinished = null;
            if (callback != null)
                callback(assetPath);
        }
        #endregion

        #region 路径
        private static string AssetPathFor(int stageId)
        {
            return StageEditorData.DefaultStagesFolder + "/Stage" + stageId + ".asset";
        }

        private static string ToAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath));
        }

        private bool RefreshOccupied()
        {
            if (_checkedId != _stageId)
            {
                _checkedId = _stageId;
                _occupied = File.Exists(ToAbsolutePath(AssetPathFor(_stageId)));
            }
            return _occupied;
        }
        #endregion
    }
}
