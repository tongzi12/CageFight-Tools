using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightDialogueTool
{
    /// <summary>
    /// 对话可视化编辑器：一个窗口、两页。
    ///
    /// **局外 · 选关界面**：编辑每关 `StageConfig.guideDialogues`（玩家在选关界面点这一关时
    /// 弹出的导游台词，头像 + 文本 + 语音）。
    ///
    /// **局内 · 对战文本**：编辑 `DialogueConfigSO`（一条：立绘 Key + 文本）与
    /// `DialogueSeriesSO`（按顺序引用多条，运行时点一下推进一句）。
    ///
    /// 两页都只**只读**使用运行时类型，写数据只写对话相关资产，游戏源码一行不改。
    /// 每条规则的依据（哪一行运行时行为）都写在 <see cref="DialogueValidator"/> 的注释里。
    /// </summary>
    public sealed class DialogueEditorWindow : EditorWindow
    {
        private const string WindowTitle = "对话编辑器";
        private const float ToolbarHeight = 24f;
        private const float StatusHeight = 20f;

        private static readonly string[] PageLabels = { "局外 · 选关界面", "局内 · 对战文本" };
        private static readonly string[] GuideFilterLabels = { "全部关卡", "有台词", "没台词" };
        private static readonly string[] MatchFilterLabels = { "全部", "只看有问题的", "只看散条" };

        #region 状态
        private int _page;
        private Vector2 _sideScroll;
        private Vector2 _mainScroll;
        private string _search = string.Empty;
        private int _guideFilter;
        private int _matchFilter;
        private bool _showProblems = true;
        private bool _showPreview = true;

        private StageDialogueInfo _stage;
        private DialogueSeriesInfo _series;
        private DialogueConfigSO _looseConfig;

        private string _status = string.Empty;
        private MessageType _statusType = MessageType.None;

        private List<DialogueProblem> _guideProblems = new List<DialogueProblem>();
        private List<DialogueProblem> _matchProblems = new List<DialogueProblem>();
        private bool _problemsDirty = true;
        #endregion

        [MenuItem("工具/对话可视化编辑器 %&d", false, 110)]
        public static void OpenWindow()
        {
            var window = GetWindow<DialogueEditorWindow>(false, WindowTitle, true);
            window.minSize = new Vector2(980f, 560f);
            window.titleContent = new GUIContent(WindowTitle);
            window.Show();
        }

        private void OnEnable()
        {
            _page = Mathf.Clamp(DialoguePrefs.Page, 0, PageLabels.Length - 1);
            _showPreview = DialoguePrefs.ShowPreview;
            DialogueTool.Rebuild();
            TryRestoreSelection();
        }

        private void OnFocus()
        {
            // 工程资产可能被别处改过（加了新关卡、改了注册表、导入了新立绘）
            DialogueTool.Invalidate();
            DialogueTool.Rebuild();
            _problemsDirty = true;
            Repaint();
        }

        private void OnDestroy()
        {
            DialoguePrefs.Page = _page;
            DialoguePrefs.ShowPreview = _showPreview;

            if (_preview != null)
            {
                AudioSource source = _preview;
                _preview = null;
                DestroyImmediate(source.gameObject);
            }
        }

        private void TryRestoreSelection()
        {
            string lastStage = DialoguePrefs.LastStagePath;
            if (!string.IsNullOrEmpty(lastStage))
            {
                for (int i = 0; i < DialogueTool.Stages.Count; i++)
                {
                    if (string.Equals(DialogueTool.Stages[i].Path, lastStage, StringComparison.OrdinalIgnoreCase))
                    {
                        _stage = DialogueTool.Stages[i];
                        break;
                    }
                }
            }

            if (_stage == null && DialogueTool.Stages.Count > 0)
                _stage = DialogueTool.Stages[0];

            string lastSeries = DialoguePrefs.LastSeriesPath;
            if (!string.IsNullOrEmpty(lastSeries))
                _series = DialogueTool.FindSeries(lastSeries);

            if (_series == null && DialogueTool.Series.Count > 0)
                _series = DialogueTool.Series[0];
        }

        #region 主循环
        private void OnGUI()
        {
            DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            DrawSideColumn();

            Rect splitter = GUILayoutUtility.GetRect(5f, 5f, GUILayout.Width(5f), GUILayout.ExpandHeight(true));
            float maxSide = Mathf.Max(200f, position.width - 420f);
            float side = DialogueEditorUI.Splitter(splitter, DialoguePrefs.SideWidth, 200f, maxSide);
            if (!Mathf.Approximately(side, DialoguePrefs.SideWidth))
                DialoguePrefs.SideWidth = side;

            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (_page == 1)
                DrawMatchPage();
            else
                DrawGuidePage();
            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();

            DrawStatusBar();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(ToolbarHeight));

            int page = GUILayout.Toolbar(_page, PageLabels, EditorStyles.toolbarButton, GUILayout.Width(196f));
            if (page != _page)
            {
                _page = page;
                DialoguePrefs.Page = _page;
                _problemsDirty = true;
                Repaint();
            }

            if (GUILayout.Button(new GUIContent("重新扫描", "重读关卡、对话资产与两张注册表"),
                    EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                DialogueTool.Invalidate();
                DialogueTool.Rebuild();
                TryRestoreSelection();
                _problemsDirty = true;
                SetStatus("已重新扫描：" + DialogueTool.Stages.Count + " 个关卡 / " +
                          DialogueTool.Series.Count + " 个系列 / " + DialogueTool.Configs.Count + " 条配置。",
                    MessageType.Info);
            }

            using (new EditorGUI.DisabledScope(!DialogueTool.IsDirty))
            {
                GUI.backgroundColor = DialogueTool.IsDirty ? new Color(1f, 0.85f, 0.5f) : Color.white;
                if (GUILayout.Button(DialogueTool.IsDirty ? "● 保存" : "保存", EditorStyles.toolbarButton, GUILayout.Width(54f)))
                    SaveNow();
                GUI.backgroundColor = Color.white;
            }

            if (GUILayout.Button(new GUIContent("撤销", "撤销上一步修改（Edit/Undo，Ctrl+Z）"),
                    EditorStyles.toolbarButton, GUILayout.Width(42f)))
                Undo.PerformUndo();

            bool preview = GUILayout.Toggle(_showPreview, new GUIContent("预览", "每条下面画一个气泡示意（左立绘 + 右文字）"),
                EditorStyles.toolbarButton, GUILayout.Width(46f));
            if (preview != _showPreview)
            {
                _showPreview = preview;
                DialoguePrefs.ShowPreview = _showPreview;
            }

            bool problems = GUILayout.Toggle(_showProblems, new GUIContent("体检", "把每条的问题挂在它自己那一行上"),
                EditorStyles.toolbarButton, GUILayout.Width(46f));
            if (problems != _showProblems)
                _showProblems = problems;

            GUILayout.FlexibleSpace();

            int errors = DialogueValidator.CountLevel(CurrentProblems(), DialogueLevel.Error);
            int warnings = DialogueValidator.CountLevel(CurrentProblems(), DialogueLevel.Warning);
            int infos = DialogueValidator.CountLevel(CurrentProblems(), DialogueLevel.Info);

            GUI.contentColor = errors > 0 ? DialogueEditorUI.Error : (warnings > 0 ? DialogueEditorUI.Warn : DialogueEditorUI.Ok);
            GUILayout.Label((_page == 1 ? "局内" : "局外") + " 体检 " + errors + " 错 / " + warnings + " 警 / " + infos + " 提示",
                EditorStyles.toolbarButton, GUILayout.Width(170f));
            GUI.contentColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            if (_page == 1)
            {
                EditorGUILayout.LabelField(_series != null ? "系列 " + _series.Name : "局内 · 对战文本",
                    DialogueEditorUI.MiniLabel, GUILayout.Width(220f));
            }
            else
            {
                EditorGUILayout.LabelField(_stage != null ? _stage.CardLabel + "　" + _stage.TypeLabel : "局外 · 选关界面",
                    DialogueEditorUI.MiniLabel, GUILayout.Width(220f));
            }

            EditorGUILayout.LabelField(DialogueTool.IsDirty ? "● 有未保存的修改" : "已保存",
                DialogueEditorUI.MiniLabel, GUILayout.Width(110f));

            GUILayout.FlexibleSpace();

            GUI.contentColor = _statusType == MessageType.Error ? DialogueEditorUI.Error
                : _statusType == MessageType.Warning ? DialogueEditorUI.Warn
                : DialogueEditorUI.Muted;
            EditorGUILayout.LabelField(_status, DialogueEditorUI.MiniLabel);
            GUI.contentColor = Color.white;

            EditorGUILayout.LabelField("工具 " + DialogueTool.SourceStamp(), DialogueEditorUI.MiniLabel, GUILayout.Width(120f));
            EditorGUILayout.EndHorizontal();
        }

        private void SetStatus(string text, MessageType type)
        {
            _status = text;
            _statusType = type;
        }

        private List<DialogueProblem> CurrentProblems()
        {
            if (_problemsDirty)
            {
                _guideProblems = DialogueValidator.CheckGuideAll();
                DialogueValidator.Sort(_guideProblems);
                _matchProblems = DialogueValidator.CheckInMatchAll();
                DialogueValidator.Sort(_matchProblems);
                _problemsDirty = false;
            }

            return _page == 1 ? _matchProblems : _guideProblems;
        }

        private void SaveNow()
        {
            string result = DialogueTool.SaveAll(DialoguePrefs.AutoBackupOnSave);
            DialogueTool.Invalidate();
            DialogueTool.Rebuild();
            TryRestoreSelection();
            _problemsDirty = true;
            SetStatus(result, MessageType.Info);
            Repaint();
        }
        #endregion

        #region 左栏
        private void DrawSideColumn()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(DialoguePrefs.SideWidth), GUILayout.ExpandHeight(true));

            _search = DialogueEditorUI.SearchField(_search, "搜索名字 / 文案", GUILayout.ExpandWidth(true));

            if (_page == 1)
                DrawMatchSideList();
            else
                DrawGuideSideList();

            EditorGUILayout.EndVertical();
        }

        private void DrawGuideSideList()
        {
            _guideFilter = GUILayout.Toolbar(_guideFilter, GuideFilterLabels, EditorStyles.miniButton, GUILayout.Height(18f));
            EditorGUILayout.Space(2f);

            _sideScroll = EditorGUILayout.BeginScrollView(_sideScroll, GUILayout.ExpandHeight(true));
            int shown = 0;

            for (int i = 0; i < DialogueTool.Stages.Count; i++)
            {
                StageDialogueInfo info = DialogueTool.Stages[i];
                if (!MatchesGuideFilter(info))
                    continue;

                DrawSideRow(info.CardLabel, info.SpokenCount + " 句 / " + info.LineCount + " 条", info == _stage,
                    () =>
                    {
                        _stage = info;
                        DialoguePrefs.LastStagePath = info.Path;
                        _mainScroll = Vector2.zero;
                    });
                shown++;
            }

            if (shown == 0)
                EditorGUILayout.LabelField("没有符合条件的关卡。", DialogueEditorUI.MiniLabel);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField("共 " + DialogueTool.Stages.Count + " 关，显示 " + shown + " 关", DialogueEditorUI.MiniLabel);
        }

        private void DrawMatchSideList()
        {
            _matchFilter = GUILayout.Toolbar(_matchFilter, MatchFilterLabels, EditorStyles.miniButton, GUILayout.Height(18f));
            EditorGUILayout.Space(2f);

            _sideScroll = EditorGUILayout.BeginScrollView(_sideScroll, GUILayout.ExpandHeight(true));

            EditorGUILayout.LabelField("对话系列（DialogueSeriesSO）", EditorStyles.miniBoldLabel);
            int seriesShown = 0;
            if (_matchFilter != 2)
            {
                for (int i = 0; i < DialogueTool.Series.Count; i++)
                {
                    DialogueSeriesInfo info = DialogueTool.Series[i];
                    int lines = info.ReadLines().Count;
                    if (!MatchesSeriesRow(info.Name, lines + " 条"))
                        continue;

                    DrawSideRow(info.Name, lines + " 条", info == _series, () =>
                    {
                        _series = info;
                        _looseConfig = null;
                        DialoguePrefs.LastSeriesPath = info.Path;
                        _mainScroll = Vector2.zero;
                    });
                    seriesShown++;
                }
            }

            if (seriesShown == 0)
                EditorGUILayout.LabelField(_matchFilter == 2 ? "（筛选：只看散条）" : "（没有符合条件的系列）",
                    DialogueEditorUI.MiniLabel);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("散条（没被任何系列引用）", EditorStyles.miniBoldLabel);

            int looseShown = 0;
            for (int i = 0; i < DialogueTool.Configs.Count; i++)
            {
                DialogueConfigInfo info = DialogueTool.Configs[i];
                if (info.RefCount > 0)
                    continue;
                if (_matchFilter == 1 && !HasProblemFor(info.Name))
                    continue;

                string sub = DialogueCore.OneLine(info.Text, 14);
                string q = DialogueCore.Clean(_search);
                if (q.Length > 0 &&
                    info.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    sub.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                DrawSideRow(info.Name, sub, info.Asset == _looseConfig, () =>
                {
                    _looseConfig = info.Asset;
                    _series = null;
                    _mainScroll = Vector2.zero;
                });
                looseShown++;
            }

            if (looseShown == 0)
                EditorGUILayout.LabelField("（没有散条）", DialogueEditorUI.MiniLabel);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField(DialogueTool.Series.Count + " 个系列 / " + DialogueTool.Configs.Count + " 条配置",
                DialogueEditorUI.MiniLabel);
        }

        private bool MatchesGuideFilter(StageDialogueInfo info)
        {
            if (_guideFilter == 1 && info.LineCount == 0)
                return false;
            if (_guideFilter == 2 && info.LineCount > 0)
                return false;

            string q = DialogueCore.Clean(_search);
            if (q.Length == 0)
                return true;

            if (info.CardLabel.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            List<GuideDialogueEntry> lines = info.Lines;
            for (int i = 0; lines != null && i < lines.Count; i++)
            {
                if (lines[i] != null && lines[i].text != null &&
                    lines[i].text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        /// <summary>"只看有问题的"筛选：这个资产名下有没有体检结果。</summary>
        private bool HasProblemFor(string whereName)
        {
            List<DialogueProblem> problems = CurrentProblems();
            for (int i = 0; i < problems.Count; i++)
            {
                if (string.Equals(problems[i].Where, whereName, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        private bool MatchesSeriesRow(string name, string sub)
        {
            if (_matchFilter == 1 && !HasProblemFor(name))
                return false;

            string q = DialogueCore.Clean(_search);
            if (q.Length == 0)
                return true;

            return (name != null && name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (sub != null && sub.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawSideRow(string title, string sub, bool selected, Action onClick)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 30f, GUILayout.ExpandWidth(true));
            bool hover = rect.Contains(Event.current.mousePosition);

            if (selected)
                DialogueEditorUI.FillRect(rect, DialogueEditorUI.Selected);
            else if (hover)
                DialogueEditorUI.FillRect(rect, DialogueEditorUI.RowEven);

            GUI.Label(new Rect(rect.x + 6f, rect.y + 2f, rect.width - 12f, 15f), title, DialogueEditorUI.MiniBold);
            GUI.Label(new Rect(rect.x + 6f, rect.y + 15f, rect.width - 12f, 14f), sub, DialogueEditorUI.MiniLabel);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
            {
                Event.current.Use();
                onClick?.Invoke();
                GUI.FocusControl(null);
                Repaint();
            }

            DialogueEditorUI.FillRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), DialogueEditorUI.GridLine);
        }
        #endregion

        #region 局外页
        private void DrawGuidePage()
        {
            if (_stage == null)
            {
                EditorGUILayout.HelpBox("左边选一个关卡。", MessageType.Info);
                return;
            }

            DrawGuideHeader();

            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll, GUILayout.ExpandHeight(true));

            List<GuideDialogueEntry> lines = _stage.Lines;
            if (lines == null || lines.Count == 0)
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox("这一关还没有导游台词。\n\n" +
                                        "玩家在选关界面点到这一关时，会按下面的顺序一条条弹出台词（头像 + 气泡文字 + 语音）。\n" +
                                        "一个字段都没配也不会报错 —— 就是什么都不弹。",
                    MessageType.Info);
            }

            for (int i = 0; lines != null && i < lines.Count; i++)
                DrawGuideCard(_stage, i, lines[i]);

            EditorGUILayout.Space(4f);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("＋ 加一条台词", GUILayout.Width(120f)))
                AddGuideLine(_stage);
            GUILayout.FlexibleSpace();
            if (lines != null && lines.Count > 1 && GUILayout.Button("一次性清空本关台词", GUILayout.Width(140f)))
                ClearGuideLines(_stage);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8f);

            EditorGUILayout.EndScrollView();
        }

        private void DrawGuideHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(_stage.CardLabel, EditorStyles.boldLabel, GUILayout.Width(180f));
            DialogueEditorUI.Tag(_stage.TypeLabel, DialogueEditorUI.Accent);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("选关界面点这一关时依次弹出　·　共 " + _stage.LineCount + " 条（有文案 " + _stage.SpokenCount + " 条）",
                DialogueEditorUI.MiniLabel);
            EditorGUILayout.EndHorizontal();

            // 全局 / 选关界面级别的问题（注册表读不到、还有多少关没台词）挂在这里，
            // 不然它们只会体现在工具栏的数字上，人找不到是哪儿。
            if (_showProblems)
            {
                DrawProblemsFor(_guideProblems, "选关界面");
                DrawProblemsFor(_guideProblems, "工程");
            }
        }

        private void DrawGuideCard(StageDialogueInfo stage, int index, GuideDialogueEntry entry)
        {
            StageConfig config = stage.Config;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawCardHeader("第 " + (index + 1) + " 条", index, stage.LineCount,
                () => MoveGuideLine(stage, index, index - 1),
                () => MoveGuideLine(stage, index, index + 1),
                () => DuplicateGuideLine(stage, index),
                () => RemoveGuideLine(stage, index));

            if (entry == null)
            {
                EditorGUILayout.HelpBox("这一条是空引用（列表里有个洞）：点右上「删」清掉它。", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.BeginHorizontal();

            // 头像槽（点一下换图）
            Rect slot = GUILayoutUtility.GetRect(48f, 48f, GUILayout.Width(48f), GUILayout.Height(48f));
            string avatarKey = DialogueCore.Clean(entry.avatarSpriteKey);
            Sprite avatar = DialogueTool.GetSprite(avatarKey);
            if (avatar != null)
            {
                DialogueEditorUI.FillRect(slot, DialogueEditorUI.AvatarBg);
                DialogueEditorUI.DrawSpriteFit(slot, avatar, Color.white);
            }
            else
            {
                DialogueEditorUI.DrawEmptySlot(slot, avatarKey.Length == 0 ? "无头像" : "Key 无效");
            }

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && slot.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                PickSprite(avatarKey, key =>
                {
                    Undo.RecordObject(config, "修改导游头像");
                    entry.avatarSpriteKey = key ?? string.Empty;
                    DialogueTool.MarkDirty(config);
                    _problemsDirty = true;
                });
            }

            EditorGUILayout.BeginVertical();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("头像 Key", DialogueEditorUI.MiniLabel, GUILayout.Width(58f));
            string newAvatar = EditorGUILayout.TextField(avatarKey, GUILayout.ExpandWidth(true));
            if (newAvatar != avatarKey)
            {
                Undo.RecordObject(config, "修改导游头像 Key");
                entry.avatarSpriteKey = DialogueCore.Clean(newAvatar);
                DialogueTool.MarkDirty(config);
                _problemsDirty = true;
            }
            if (GUILayout.Button("选图", EditorStyles.miniButton, GUILayout.Width(40f)))
            {
                PickSprite(avatarKey, key =>
                {
                    Undo.RecordObject(config, "修改导游头像");
                    entry.avatarSpriteKey = key ?? string.Empty;
                    DialogueTool.MarkDirty(config);
                    _problemsDirty = true;
                });
            }
            if (GUILayout.Button("清", EditorStyles.miniButton, GUILayout.Width(26f)))
            {
                Undo.RecordObject(config, "清空导游头像");
                entry.avatarSpriteKey = string.Empty;
                DialogueTool.MarkDirty(config);
                _problemsDirty = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("语音 Key", DialogueEditorUI.MiniLabel, GUILayout.Width(58f));
            string audioKey = DialogueCore.Clean(entry.audioKey);
            string newAudio = EditorGUILayout.TextField(audioKey, GUILayout.ExpandWidth(true));
            if (newAudio != audioKey)
            {
                Undo.RecordObject(config, "修改导游语音 Key");
                entry.audioKey = DialogueCore.Clean(newAudio);
                DialogueTool.MarkDirty(config);
                _problemsDirty = true;
            }
            if (GUILayout.Button("选音", EditorStyles.miniButton, GUILayout.Width(40f)))
            {
                PickAudio(audioKey, key =>
                {
                    Undo.RecordObject(config, "修改导游语音");
                    entry.audioKey = key ?? string.Empty;
                    DialogueTool.MarkDirty(config);
                    _problemsDirty = true;
                });
            }
            if (GUILayout.Button("试听", EditorStyles.miniButton, GUILayout.Width(40f)))
                PlayAudio(audioKey);
            if (GUILayout.Button("清", EditorStyles.miniButton, GUILayout.Width(26f)))
            {
                Undo.RecordObject(config, "清空导游语音");
                entry.audioKey = string.Empty;
                DialogueTool.MarkDirty(config);
                _problemsDirty = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("台词（会按这个顺序一条条弹）", DialogueEditorUI.MiniLabel);
            EditorGUI.BeginChangeCheck();
            string text = EditorGUILayout.TextArea(entry.text ?? string.Empty, DialogueEditorUI.WrapTextArea, GUILayout.MinHeight(46f));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(config, "修改导游台词");
                entry.text = text;
                DialogueTool.MarkDirty(config);
                _problemsDirty = true;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            DrawCardProblems(_guideProblems, stage.StageId, index);

            if (_showPreview)
            {
                float height = 62f;
                Rect preview = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));
                DialogueEditorUI.DrawBubblePreview(preview, avatar, entry.text, "（这条没写台词：运行时会被跳过，什么都不显示）");
            }

            EditorGUILayout.Space(2f);
            EditorGUILayout.EndVertical();
        }

        private void DrawCardProblems(List<DialogueProblem> problems, int stageId, int index)
        {
            if (!_showProblems)
                return;

            string where = "Stage" + stageId + " 第 " + (index + 1) + " 条";
            DrawProblemsFor(problems, where);
        }

        /// <summary>把属于"这一条"的体检结果直接画在卡片里（比让人翻体检列表快得多）。</summary>
        private void DrawProblemsFor(List<DialogueProblem> problems, string where)
        {
            for (int i = 0; i < problems.Count; i++)
            {
                DialogueProblem problem = problems[i];
                if (!string.Equals(problem.Where, where, StringComparison.Ordinal))
                    continue;

                DrawProblemRow(problem);
            }
        }

        /// <summary>画"以某前缀开头"的问题（例如某个系列自己的问题：空系列、重复引用、空槽位）。</summary>
        private void DrawProblemsForPrefix(List<DialogueProblem> problems, string prefix)
        {
            for (int i = 0; i < problems.Count; i++)
            {
                DialogueProblem problem = problems[i];
                if (!string.Equals(problem.Where, prefix, StringComparison.Ordinal) &&
                    !problem.Where.StartsWith(prefix + " ", StringComparison.Ordinal))
                    continue;

                DrawProblemRow(problem);
            }
        }

        private void DrawProblemRow(DialogueProblem problem)
        {
            Color color = problem.Level == DialogueLevel.Error ? DialogueEditorUI.Error
                : problem.Level == DialogueLevel.Warning ? DialogueEditorUI.Warn
                : DialogueEditorUI.Muted;

            EditorGUILayout.BeginHorizontal();
            DialogueEditorUI.Tag(problem.Level == DialogueLevel.Error ? "错误"
                : problem.Level == DialogueLevel.Warning ? "警告" : "提示", color);
            EditorGUILayout.LabelField(problem.Message, DialogueEditorUI.MiniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void AddGuideLine(StageDialogueInfo stage)
        {
            StageConfig config = stage.Config;
            Undo.RecordObject(config, "新增导游台词");
            if (config.guideDialogues == null)
                config.guideDialogues = new List<GuideDialogueEntry>();
            config.guideDialogues.Add(new GuideDialogueEntry());
            DialogueTool.MarkDirty(config);
            _problemsDirty = true;
            Repaint();
        }

        private void RemoveGuideLine(StageDialogueInfo stage, int index)
        {
            StageConfig config = stage.Config;
            if (config.guideDialogues == null || index < 0 || index >= config.guideDialogues.Count)
                return;

            Undo.RecordObject(config, "删除导游台词");
            config.guideDialogues.RemoveAt(index);
            DialogueTool.MarkDirty(config);
            _problemsDirty = true;
            Repaint();
        }

        private void DuplicateGuideLine(StageDialogueInfo stage, int index)
        {
            StageConfig config = stage.Config;
            List<GuideDialogueEntry> lines = config.guideDialogues;
            if (lines == null || index < 0 || index >= lines.Count || lines[index] == null)
                return;

            Undo.RecordObject(config, "复制导游台词");
            var copy = new GuideDialogueEntry
            {
                avatarSpriteKey = lines[index].avatarSpriteKey,
                audioKey = lines[index].audioKey,
                text = lines[index].text
            };
            lines.Insert(index + 1, copy);
            DialogueTool.MarkDirty(config);
            _problemsDirty = true;
            Repaint();
        }

        private void MoveGuideLine(StageDialogueInfo stage, int from, int to)
        {
            StageConfig config = stage.Config;
            if (!DialogueCore.TryMove(config.guideDialogues, from, to))
                return;

            Undo.RecordObject(config, "调整导游台词顺序");
            DialogueTool.MarkDirty(config);
            Repaint();
        }

        private void ClearGuideLines(StageDialogueInfo stage)
        {
            if (!EditorUtility.DisplayDialog("清空本关台词",
                    "要清掉 " + stage.CardLabel + " 的全部 " + stage.LineCount + " 条导游台词吗？\n\n" +
                    "（保存前会自动备份，Ctrl+Z 也能撤销）", "清空", "算了"))
                return;

            StageConfig config = stage.Config;
            Undo.RecordObject(config, "清空导游台词");
            config.guideDialogues.Clear();
            DialogueTool.MarkDirty(config);
            _problemsDirty = true;
            Repaint();
        }
        #endregion

        #region 局内页
        private void DrawMatchPage()
        {
            if (_series == null && _looseConfig == null)
            {
                DrawMatchEmptyState();
                return;
            }

            if (_looseConfig != null)
            {
                DrawLooseConfig();
                return;
            }

            List<DialogueConfigSO> lines = _series.ReadLines();

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("系列 " + _series.Name, EditorStyles.boldLabel, GUILayout.Width(200f));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("运行时点一下推进一句　·　共 " + lines.Count + " 条", DialogueEditorUI.MiniLabel);
            EditorGUILayout.EndHorizontal();

            // 系列自己的问题（空系列 / 重复引用 / 空槽位）与工程级问题，挂在这里
            if (_showProblems)
            {
                DrawProblemsForPrefix(_matchProblems, _series.Name);
                DrawProblemsFor(_matchProblems, "工程");
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("＋ 新建一条配置并加入", GUILayout.Width(160f)))
            {
                string folder = System.IO.Path.GetDirectoryName(_series.Path)?.Replace('\\', '/');
                string path;
                string error;
                DialogueConfigSO created = DialogueTool.CreateConfig(folder, out path, out error);
                if (created == null)
                    SetStatus("新建失败：" + error, MessageType.Warning);
                else
                {
                    DialogueTool.AppendToSeries(_series.Asset, created);
                    _problemsDirty = true;
                    SetStatus("已新建配置：" + path + "（记得点保存）", MessageType.Info);
                }
            }

            if (GUILayout.Button("＋ 从现有配置里挑一条", GUILayout.Width(170f)))
                ShowAddExistingMenu(_series);

            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("查这个系列被谁引用",
                    "扫一遍工程里的 prefab / 场景 / 资产，看谁引用了这个对话系列（按需扫描，可能要几秒）"),
                    EditorStyles.miniButton, GUILayout.Width(150f)))
                ReportReferences(_series.Path);

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4f);

            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll, GUILayout.ExpandHeight(true));

            if (lines.Count == 0)
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox("这个系列一条都不引用：播它不会有任何内容。\n" +
                                        "用上面的「新建一条配置并加入」或「从现有配置里挑一条」往里加。", MessageType.Info);
            }

            for (int i = 0; i < lines.Count; i++)
                DrawMatchCard(_series, i, lines[i], lines.Count);

            EditorGUILayout.Space(8f);
            EditorGUILayout.EndScrollView();
        }

        private void DrawMatchEmptyState()
        {
            EditorGUILayout.Space(10f);
            EditorGUILayout.HelpBox(
                "局内对话（对战中弹出的文本框）用的是两样东西：\n\n" +
                "· DialogueConfigSO = 一条：立绘 Key + 文案\n" +
                "· DialogueSeriesSO = 一个系列：按顺序引用若干条，运行时点一下推进一句\n\n" +
                "左边选一个系列来编辑，或者选「散条」里的一条单独改。\n" +
                "还没有资产就新建一个系列：", MessageType.Info);

            if (GUILayout.Button("新建对话系列（DialogueSeriesSO）", GUILayout.Width(240f)))
            {
                string path;
                string error;
                DialogueSeriesSO created = DialogueTool.CreateSeries(DialogueTool.DefaultScriptFolder, out path, out error);
                if (created == null)
                    SetStatus("新建失败：" + error, MessageType.Warning);
                else
                {
                    DialogueTool.Invalidate();
                    DialogueTool.Rebuild();
                    _series = DialogueTool.FindSeries(path);
                    SetStatus("已新建系列：" + path + "（记得点保存）", MessageType.Info);
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("现有：　" + DialogueTool.Series.Count + " 个系列　·　" +
                                       DialogueTool.Configs.Count + " 条配置　·　" +
                                       DialogueTool.Configs.Count + " 条里被引用的见左栏", DialogueEditorUI.MiniLabel);
        }

        private void DrawLooseConfig()
        {
            DialogueConfigInfo info = DialogueTool.FindConfig(_looseConfig);
            string name = info != null ? info.Name : _looseConfig.name;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("散条 " + name, EditorStyles.boldLabel, GUILayout.Width(220f));
            DialogueEditorUI.Tag("没被任何系列引用", DialogueEditorUI.Warn);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("它现在不会出现在任何对话里（除非被某个系列引用）", DialogueEditorUI.MiniLabel);
            EditorGUILayout.EndHorizontal();

            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll, GUILayout.ExpandHeight(true));
            DrawConfigFields(_looseConfig, info != null ? info.Path : string.Empty, true);
            EditorGUILayout.Space(8f);
            EditorGUILayout.EndScrollView();
        }

        private void DrawMatchCard(DialogueSeriesInfo series, int index, DialogueConfigSO line, int count)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            DrawCardHeader("第 " + (index + 1) + " 条", index, count,
                () => MoveSeriesLine(series, index, index - 1),
                () => MoveSeriesLine(series, index, index + 1),
                () => DuplicateSeriesLine(series, index),
                () => RemoveSeriesLine(series, index));

            if (line == null)
            {
                EditorGUILayout.HelpBox("这一格是空的：运行时会被跳过（TryGetNextValid），但位置会留着。点「删」清掉。",
                    MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            DialogueConfigInfo info = DialogueTool.FindConfig(line);
            DrawConfigFields(line, info != null ? info.Path : string.Empty, false);

            if (_showProblems && info != null)
                DrawProblemsFor(_matchProblems, info.Name);

            EditorGUILayout.Space(2f);
            EditorGUILayout.EndVertical();
        }

        /// <summary>一条局内配置的字段（系列卡片里、和"散条"编辑里共用）。</summary>
        private void DrawConfigFields(DialogueConfigSO config, string path, bool showName)
        {
            EditorGUILayout.BeginHorizontal();

            string key = DialogueCore.Clean(config.PortraitSpriteKey);
            Sprite portrait = DialogueTool.GetSprite(key);

            Rect slot = GUILayoutUtility.GetRect(56f, 56f, GUILayout.Width(56f), GUILayout.Height(56f));
            if (portrait != null)
            {
                DialogueEditorUI.FillRect(slot, DialogueEditorUI.AvatarBg);
                DialogueEditorUI.DrawSpriteFit(slot, portrait, Color.white);
            }
            else
            {
                DialogueEditorUI.DrawEmptySlot(slot, key.Length == 0 ? "无立绘" : "Key 无效");
            }

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && slot.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                PickSprite(key, picked =>
                {
                    DialogueTool.WriteConfigPortraitKey(config, picked);
                    DialogueTool.MarkDirty(config);
                    _problemsDirty = true;
                });
            }

            EditorGUILayout.BeginVertical();

            if (showName)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("资产", DialogueEditorUI.MiniLabel, GUILayout.Width(58f));
                EditorGUILayout.LabelField(path, DialogueEditorUI.MiniLabel);
                if (GUILayout.Button("选中", EditorStyles.miniButton, GUILayout.Width(40f)))
                    EditorGUIUtility.PingObject(config);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("立绘 Key", DialogueEditorUI.MiniLabel, GUILayout.Width(58f));
            string newKey = EditorGUILayout.TextField(key, GUILayout.ExpandWidth(true));
            if (newKey != key)
            {
                DialogueTool.WriteConfigPortraitKey(config, newKey);
                DialogueTool.MarkDirty(config);
                _problemsDirty = true;
            }
            if (GUILayout.Button("选图", EditorStyles.miniButton, GUILayout.Width(40f)))
            {
                PickSprite(key, picked =>
                {
                    DialogueTool.WriteConfigPortraitKey(config, picked);
                    DialogueTool.MarkDirty(config);
                    _problemsDirty = true;
                });
            }
            if (GUILayout.Button("清", EditorStyles.miniButton, GUILayout.Width(26f)))
            {
                DialogueTool.WriteConfigPortraitKey(config, string.Empty);
                DialogueTool.MarkDirty(config);
                _problemsDirty = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("文案", DialogueEditorUI.MiniLabel);
            EditorGUI.BeginChangeCheck();
            string text = EditorGUILayout.TextArea(config.Text ?? string.Empty, DialogueEditorUI.WrapTextArea, GUILayout.MinHeight(46f));
            if (EditorGUI.EndChangeCheck())
            {
                DialogueTool.WriteConfigText(config, text);
                DialogueTool.MarkDirty(config);
                _problemsDirty = true;
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            if (_showPreview)
            {
                Rect preview = GUILayoutUtility.GetRect(0f, 62f, GUILayout.ExpandWidth(true));
                DialogueEditorUI.DrawBubblePreview(preview, portrait, config.Text,
                    "（这条没写文案：播到它会弹一个空框）");
            }
        }

        private void ShowAddExistingMenu(DialogueSeriesInfo series)
        {
            var menu = new GenericMenu();
            List<DialogueConfigSO> used = series.ReadLines();

            for (int i = 0; i < DialogueTool.Configs.Count; i++)
            {
                DialogueConfigInfo info = DialogueTool.Configs[i];
                bool already = used.Contains(info.Asset);
                string label = info.Name + "　" + DialogueCore.OneLine(info.Text, 16);

                if (already)
                    menu.AddDisabledItem(new GUIContent(label + "（已在系列里）"));
                else
                {
                    DialogueConfigSO picked = info.Asset;
                    menu.AddItem(new GUIContent(label), false, () =>
                    {
                        DialogueTool.AppendToSeries(series.Asset, picked);
                        _problemsDirty = true;
                        Repaint();
                    });
                }
            }

            if (DialogueTool.Configs.Count == 0)
                menu.AddDisabledItem(new GUIContent("（工程里没有 DialogueConfigSO）"));

            menu.ShowAsContext();
        }

        private void MoveSeriesLine(DialogueSeriesInfo series, int from, int to)
        {
            List<DialogueConfigSO> lines = series.ReadLines();
            if (!DialogueCore.TryMove(lines, from, to))
                return;

            Undo.RecordObject(series.Asset, "调整对话顺序");
            DialogueTool.WriteSeriesLines(series.Asset, lines);
            DialogueTool.MarkDirty(series.Asset);
            Repaint();
        }

        private void DuplicateSeriesLine(DialogueSeriesInfo series, int index)
        {
            List<DialogueConfigSO> lines = series.ReadLines();
            if (index < 0 || index >= lines.Count || lines[index] == null)
                return;

            // 「复制」= 复制出一条**新的资产**并插在后面，改文案不会互相影响；
            // 想同一份被引用两次就用「从现有配置里挑一条」。
            string folder = System.IO.Path.GetDirectoryName(series.Path)?.Replace('\\', '/');
            string path;
            string error;
            DialogueConfigSO created = DialogueTool.CreateConfig(folder, out path, out error);
            if (created == null)
            {
                SetStatus("复制失败：" + error, MessageType.Warning);
                return;
            }

            DialogueConfigSO source = lines[index];
            DialogueTool.WriteConfigText(created, source.Text);
            DialogueTool.WriteConfigPortraitKey(created, source.PortraitSpriteKey);

            Undo.RecordObject(series.Asset, "复制一条对话");
            lines.Insert(index + 1, created);
            DialogueTool.WriteSeriesLines(series.Asset, lines);
            DialogueTool.MarkDirty(series.Asset);
            DialogueTool.MarkDirty(created);

            DialogueTool.Invalidate();
            DialogueTool.Rebuild();
            _series = DialogueTool.FindSeries(series.Path);
            _problemsDirty = true;
            SetStatus("已复制出 " + path + "（记得点保存）", MessageType.Info);
        }

        private void RemoveSeriesLine(DialogueSeriesInfo series, int index)
        {
            List<DialogueConfigSO> lines = series.ReadLines();
            if (index < 0 || index >= lines.Count)
                return;

            Undo.RecordObject(series.Asset, "从系列里移除一条");
            lines.RemoveAt(index);
            DialogueTool.WriteSeriesLines(series.Asset, lines);
            DialogueTool.MarkDirty(series.Asset);
            _problemsDirty = true;
            Repaint();
        }
        #endregion

        #region 卡片公共部分
        private void DrawCardHeader(string title, int index, int count, Action moveUp, Action moveDown, Action duplicate, Action remove)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(title, EditorStyles.boldLabel, GUILayout.Width(64f));
            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(index <= 0))
            {
                if (GUILayout.Button(new GUIContent("▲", "上移一位"), EditorStyles.miniButton, GUILayout.Width(24f)))
                    moveUp?.Invoke();
            }

            using (new EditorGUI.DisabledScope(index >= count - 1))
            {
                if (GUILayout.Button(new GUIContent("▼", "下移一位"), EditorStyles.miniButton, GUILayout.Width(24f)))
                    moveDown?.Invoke();
            }

            if (GUILayout.Button(new GUIContent("复制", "复制出一条新的（改它不会动原来那条）"),
                    EditorStyles.miniButton, GUILayout.Width(44f)))
                duplicate?.Invoke();

            GUI.backgroundColor = new Color(1f, 0.75f, 0.75f);
            if (GUILayout.Button("删", EditorStyles.miniButton, GUILayout.Width(28f)))
                remove?.Invoke();
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }
        #endregion

        #region 资源选择 / 试听 / 引用扫描
        private static void PickSprite(string currentKey, Action<string> onPick)
        {
            DialogueEditorPickerWindow.Open("选择立绘 Key", DialogueEditorPickerWindow.Kind.Sprite, currentKey,
                onPick, () => onPick?.Invoke(string.Empty));
        }

        private static void PickAudio(string currentKey, Action<string> onPick)
        {
            DialogueEditorPickerWindow.Open("选择语音 Key", DialogueEditorPickerWindow.Kind.Audio, currentKey,
                onPick, () => onPick?.Invoke(string.Empty));
        }

        private AudioSource _preview;

        private void PlayAudio(string key)
        {
            AudioClip clip = DialogueTool.GetAudio(key);
            if (clip == null)
            {
                SetStatus(string.IsNullOrEmpty(key) ? "这条没填语音。" : ("AudioRegistry 里没有「" + key + "」这个 Key。"),
                    MessageType.Warning);
                return;
            }

            if (_preview == null)
            {
                var go = new GameObject("DialogueToolPreview") { hideFlags = HideFlags.HideAndDontSave };
                _preview = go.AddComponent<AudioSource>();
            }

            _preview.Stop();
            _preview.clip = clip;
            _preview.Play();
            SetStatus("试听：" + key, MessageType.Info);
        }

        private void ReportReferences(string assetPath)
        {
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                SetStatus("取不到 GUID：" + assetPath, MessageType.Warning);
                return;
            }

            var hits = new List<string>();
            string[] folders = { "Assets" };
            string[] kinds = { "t:Prefab", "t:Scene" };

            for (int k = 0; k < kinds.Length; k++)
            {
                string[] guids = AssetDatabase.FindAssets(kinds[k], folders);
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    if (string.IsNullOrEmpty(path))
                        continue;

                    string[] deps = AssetDatabase.GetDependencies(path, false);
                    for (int d = 0; d < deps.Length; d++)
                    {
                        if (string.Equals(deps[d], assetPath, StringComparison.OrdinalIgnoreCase))
                        {
                            hits.Add(path);
                            break;
                        }
                    }
                }
            }

            // 脚本里的引用（例如 TextTest.cs 那样的字段）看不到，这里如实说明。
            if (hits.Count == 0)
                SetStatus("没有 prefab / 场景 引用这个系列（脚本里的字段引用扫不到，那种要自己看代码）", MessageType.Warning);
            else
                SetStatus("被 " + hits.Count + " 处引用：" + string.Join("，", hits.ToArray()), MessageType.Info);

            Repaint();
        }
        #endregion
    }
}
