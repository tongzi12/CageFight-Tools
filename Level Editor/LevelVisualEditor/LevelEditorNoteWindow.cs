using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightStageTool
{
    /// <summary>
    /// 关卡注释窗口（R1.30）：写「这一关的备注」，给协作用 —— 谁在哪一关做了什么改动、
    /// 还差什么、为什么这么配，都能留在这里，别人打开同一个工具就能看到。
    ///
    /// 数据落在**工具目录**的 `StageNotes.json`（<see cref="StageNoteStore"/>），
    /// 按关卡编号索引、不写进关卡资产，所以：不改游戏代码、不进游戏包。
    /// 要发给没装工具的人：用「复制」把内容贴过去，或「导出为 Markdown」生成一份 md 文件。
    ///
    /// 窗口可停靠、非模态：写注释时不影响继续在关卡编辑器里操作。
    ///
    /// 排版约定（和另外几个编辑器一致）：顶部**关卡头**（helpBox 卡片）→ 中部**编辑区填满剩余高度**
    /// （注释框自带滚动，长文本不会把按钮顶出去）→ 底部**总览**（固定高度、可滚动）+ **状态条**。
    /// 长路径单独占一行，绝不和按钮抢同一行的宽度（老版就是被路径挤到把「打开所在文件夹」截断的）。
    /// </summary>
    public sealed class LevelEditorNoteWindow : EditorWindow
    {
        private const string WindowTitle = "关卡注释";
        private const float OverviewHeight = 148f;
        private const float StatusHeight = 20f;
        private const float MinTextHeight = 120f;

        private int _stageId = -1;
        private string _label = string.Empty;
        private string _assetPath = string.Empty;

        private string _text = string.Empty;
        private string _author = string.Empty;
        private Vector2 _textScroll;
        private Vector2 _listScroll;

        private string _status = string.Empty;
        private MessageType _statusType = MessageType.None;
        private bool _unsaved;

        /// <summary>打开某一关的注释。传 stageId &lt; 0 时只显示总览（编辑区给出提示）。</summary>
        public static LevelEditorNoteWindow Open(int stageId, string label, string assetPath)
        {
            var window = GetWindow<LevelEditorNoteWindow>(false, WindowTitle, true);
            window.minSize = new Vector2(460f, 460f);
            window.titleContent = new GUIContent(WindowTitle);
            window.SelectTarget(stageId, label, assetPath);
            window.Show();
            window.Focus();
            return window;
        }

        /// <summary>切换正在编辑的关卡（左栏点「注」按钮、或总览里点一条时调用）。</summary>
        public void SelectTarget(int stageId, string label, string assetPath)
        {
            _stageId = stageId;
            _label = label ?? string.Empty;
            _assetPath = assetPath ?? string.Empty;
            _author = LevelEditorPrefs.NoteAuthor;

            StageNote note = stageId >= 0 ? StageNoteStore.Get(stageId) : null;
            _text = note != null ? note.Text : string.Empty;
            _status = string.Empty;
            _statusType = MessageType.None;
            _unsaved = false;
            _textScroll = Vector2.zero;
            Repaint();
        }

        private void OnGUI()
        {
            DrawHeader();

            EditorGUILayout.Space(4f);
            DrawEditor();

            EditorGUILayout.Space(4f);
            DrawOverview();

            DrawStatusBar();
        }

        #region 顶部：关卡头卡片
        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                _stageId < 0 ? "关卡注释" : (string.IsNullOrEmpty(_label) ? ("Stage" + _stageId) : _label),
                EditorStyles.boldLabel, GUILayout.Width(160f));

            if (_stageId >= 0)
            {
                EditorGUILayout.LabelField("编号 " + _stageId, LevelEditorUI.MiniLabel, GUILayout.Width(72f));
                bool hasNote = StageNoteStore.Has(_stageId);
                LevelEditorUI.Tag(hasNote ? "已写注释" : "还没写", hasNote ? LevelEditorUI.Accent : LevelEditorUI.Muted);
            }

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(_stageId < 0 || string.IsNullOrEmpty(_assetPath)))
            {
                if (GUILayout.Button(new GUIContent("定位关卡", "在 Project 面板里选中这一关的资产"),
                        EditorStyles.miniButton, GUILayout.Width(64f)))
                    PingAsset(_assetPath);
            }
            EditorGUILayout.EndHorizontal();

            StageNote existing = _stageId >= 0 ? StageNoteStore.Get(_stageId) : null;
            EditorGUILayout.LabelField(
                _stageId < 0
                    ? "在关卡编辑器左栏点某一关卡片右上角的「注」按钮，或从下面的列表里选一条。"
                    : (existing != null
                        ? ("最后更新：" + (string.IsNullOrEmpty(existing.Author) ? "（没署名）" : existing.Author) +
                           "　" + existing.TimeLabel)
                        : "这一关还没有注释 —— 写点东西，别人打开同一个工具就能看到。"),
                LevelEditorUI.RichLabel);

            EditorGUILayout.EndVertical();
        }
        #endregion

        #region 中部：编辑区（填满剩余高度）
        private void DrawEditor()
        {
            if (_stageId < 0)
            {
                // 空状态也画成一个框，别让中间空一大片（老版就是这里半屏空白）
                Rect empty = GUILayoutUtility.GetRect(0f, 120f, GUILayout.ExpandWidth(true));
                LevelEditorUI.FillRect(empty, new Color(1f, 1f, 1f, 0.03f));
                LevelEditorUI.Frame(empty, new Color(1f, 1f, 1f, 0.10f), 1f);
                GUI.Label(new Rect(empty.x + 10f, empty.y + 8f, empty.width - 20f, 18f), "还没选关卡",
                    EditorStyles.boldLabel);
                GUI.Label(new Rect(empty.x + 10f, empty.y + 30f, empty.width - 20f, 40f),
                    "· 关卡编辑器左栏：点某关卡片右上角的「注」\n· 或者从下面的「所有注释」里点一条切过来",
                    LevelEditorUI.MiniLabel);
                GUILayout.FlexibleSpace();
                return;
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("署名", GUILayout.Width(32f));
            EditorGUI.BeginChangeCheck();
            _author = EditorGUILayout.TextField(_author, GUILayout.Width(150f));
            if (EditorGUI.EndChangeCheck())
                _unsaved = true;
            EditorGUILayout.LabelField("（会记在注释里，方便别人知道找谁问）", LevelEditorUI.MiniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2f);
            LevelEditorUI.Section("注释", "注意点 / 待办 / 改动原因");
            DrawTextArea();

            EditorGUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_unsaved ? "● 保存 (Ctrl+S)" : "保存 (Ctrl+S)", GUILayout.Height(24f), GUILayout.Width(126f)))
                Save();

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_text)))
            {
                if (GUILayout.Button(new GUIContent("复制", "把注释内容复制到剪贴板，直接贴给别人"),
                        GUILayout.Height(24f), GUILayout.Width(60f)))
                {
                    EditorGUIUtility.systemCopyBuffer = _text ?? string.Empty;
                    SetStatus("注释已复制到剪贴板，可直接贴给别人。", MessageType.Info);
                }
            }

            using (new EditorGUI.DisabledScope(!StageNoteStore.Has(_stageId)))
            {
                if (GUILayout.Button(new GUIContent("清空", "删掉这一关的注释（会先问一次）"),
                        GUILayout.Height(24f), GUILayout.Width(60f)))
                {
                    if (EditorUtility.DisplayDialog("清空注释",
                            "删掉「" + (string.IsNullOrEmpty(_label) ? ("Stage" + _stageId) : _label) + "」的注释？",
                            "清空", "取消"))
                    {
                        StageNoteStore.Remove(_stageId);
                        _text = string.Empty;
                        _unsaved = false;
                        SetStatus("已清空这一关的注释。", MessageType.Info);
                    }
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(CharCountLabel(), LevelEditorUI.MiniLabel, GUILayout.Width(96f));
            EditorGUILayout.EndHorizontal();

            // 长路径单独一行：绝不和按钮抢宽度（老版就是被它挤到截断）
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("数据文件：" + StageNoteStore.FilePath, LevelEditorUI.MiniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("打开所在文件夹", "在资源管理器里定位 StageNotes.json"),
                    EditorStyles.miniButton, GUILayout.Width(96f)))
                RevealOrFolder(StageNoteStore.FilePath);
            EditorGUILayout.EndHorizontal();

            if (Event.current.type == EventType.KeyDown && Event.current.control && Event.current.keyCode == KeyCode.S)
            {
                Save();
                Event.current.Use();
            }
        }

        /// <summary>
        /// 注释框用的样式：**自动换行**。
        /// Unity 默认的 EditorStyles.textArea 是 wordWrap = false —— 长句会一路往右顶，
        /// 结果是文字被右边界截断、底下还多出一条横向滚动条（这是实测踩到的一处）。
        /// </summary>
        private static GUIStyle _noteTextStyle;

        private static GUIStyle NoteTextStyle
        {
            get
            {
                if (_noteTextStyle == null)
                {
                    _noteTextStyle = new GUIStyle(EditorStyles.textArea);
                    _noteTextStyle.wordWrap = true;
                    _noteTextStyle.richText = false;
                }
                return _noteTextStyle;
            }
        }

        /// <summary>注释框：**填满剩余高度**；文本比框长时框内自己滚动（内容高度按行数算）。</summary>
        private void DrawTextArea()
        {
            GUIStyle style = NoteTextStyle;
            int lines = string.IsNullOrEmpty(_text) ? 5 : _text.Split('\n').Length;
            float contentHeight = Mathf.Max(MinTextHeight, lines * style.lineHeight + 12f);

            _textScroll = EditorGUILayout.BeginScrollView(_textScroll,
                GUILayout.MinHeight(MinTextHeight), GUILayout.ExpandHeight(true));

            EditorGUI.BeginChangeCheck();
            _text = EditorGUILayout.TextArea(_text ?? string.Empty, style,
                GUILayout.Height(contentHeight), GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
                _unsaved = true;

            EditorGUILayout.EndScrollView();
        }

        private string CharCountLabel()
        {
            int chars = string.IsNullOrEmpty(_text) ? 0 : _text.Length;
            return _unsaved ? (chars + " 字　未保存") : (chars + " 字");
        }

        private void Save()
        {
            if (_stageId < 0)
                return;

            LevelEditorPrefs.NoteAuthor = _author ?? string.Empty;
            StageNoteStore.Set(_stageId, _text, _author);
            _unsaved = false;
            SetStatus(string.IsNullOrEmpty(_text) ? "注释已删除。" : "注释已保存。", MessageType.Info);
            Repaint();
        }
        #endregion

        #region 底部：所有注释总览
        private void DrawOverview()
        {
            List<StageNote> notes = StageNoteStore.All();
            List<StageInfo> stages = StageEditorData.DiscoverStages();

            var dangling = new List<StageNote>();
            for (int i = 0; i < notes.Count; i++)
            {
                if (StageNoteStore.FindStage(stages, notes[i].StageId) == null)
                    dangling.Add(notes[i]);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("所有注释", EditorStyles.boldLabel, GUILayout.Width(60f));
            LevelEditorUI.Tag(notes.Count + " 关", notes.Count > 0 ? LevelEditorUI.Ok : LevelEditorUI.Muted);
            if (dangling.Count > 0)
                LevelEditorUI.Tag(dangling.Count + " 条失效", LevelEditorUI.Warn);
            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(notes.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("导出为 Markdown",
                        "写一份 " + StageNoteStore.MarkdownPath + "：给没装这个工具的人看，或放进版本库"),
                        EditorStyles.miniButton, GUILayout.Width(116f)))
                {
                    string path = StageNoteStore.ExportMarkdown(stages);
                    SetStatus(string.IsNullOrEmpty(path) ? "导出失败，看 Console。" : ("已导出：" + path),
                        string.IsNullOrEmpty(path) ? MessageType.Warning : MessageType.Info);
                    if (!string.IsNullOrEmpty(path))
                        EditorUtility.RevealInFinder(path);
                }
            }
            EditorGUILayout.EndHorizontal();

            if (notes.Count == 0)
            {
                Rect empty = GUILayoutUtility.GetRect(0f, 40f, GUILayout.ExpandWidth(true));
                LevelEditorUI.FillRect(empty, new Color(1f, 1f, 1f, 0.03f));
                LevelEditorUI.Frame(empty, new Color(1f, 1f, 1f, 0.10f), 1f);
                GUI.Label(new Rect(empty.x + 8f, empty.y + 4f, empty.width - 16f, 16f), "还没有任何注释",
                    EditorStyles.miniBoldLabel);
                GUI.Label(new Rect(empty.x + 8f, empty.y + 20f, empty.width - 16f, 16f),
                    "在关卡编辑器左栏点某一关卡片右上角的「注」，就能写第一条。", LevelEditorUI.MiniLabel);
                return;
            }

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Height(OverviewHeight));
            for (int i = 0; i < notes.Count; i++)
            {
                StageNote note = notes[i];
                StageInfo info = StageNoteStore.FindStage(stages, note.StageId);
                bool here = note.StageId == _stageId;

                Rect row = GUILayoutUtility.GetRect(0f, 20f, GUILayout.ExpandWidth(true));
                if (here)
                    LevelEditorUI.FillRect(row, LevelEditorUI.Selected);
                else if (i % 2 == 0)
                    LevelEditorUI.FillRect(row, LevelEditorUI.RowEven);

                if (Event.current.type == EventType.MouseDown && row.Contains(Event.current.mousePosition))
                {
                    SelectTarget(note.StageId, info != null ? info.Label : string.Empty,
                        info != null ? info.Path : string.Empty);
                    Event.current.Use();
                }

                var title = new GUIContent(
                    info != null ? info.Label : ("Stage" + note.StageId + "（关卡已不在）"),
                    note.Text);
                GUI.Label(new Rect(row.x + 6f, row.y + 2f, 92f, 16f), title,
                    info != null ? EditorStyles.miniBoldLabel : LevelEditorUI.MiniLabel);
                GUI.Label(new Rect(row.x + 100f, row.y + 2f, Mathf.Max(20f, row.width - 262f), 16f),
                    note.Summary(72), LevelEditorUI.MiniLabel);
                GUI.Label(new Rect(row.xMax - 156f, row.y + 2f, 66f, 16f),
                    string.IsNullOrEmpty(note.Author) ? "—" : note.Author, LevelEditorUI.MiniLabel);
                GUI.Label(new Rect(row.xMax - 88f, row.y + 2f, 84f, 16f), note.TimeLabel, LevelEditorUI.MiniLabel);
            }
            EditorGUILayout.EndScrollView();

            if (dangling.Count > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("有 " + dangling.Count + " 条注释对应的关卡已经不在了（删了或改号了）。",
                    LevelEditorUI.MiniLabel);
                if (GUILayout.Button("清掉失效注释", EditorStyles.miniButton, GUILayout.Width(96f)))
                {
                    if (EditorUtility.DisplayDialog("清掉失效注释",
                            "删除这 " + dangling.Count + " 条找不到对应关卡的注释？", "删除", "取消"))
                    {
                        for (int i = 0; i < dangling.Count; i++)
                            StageNoteStore.Remove(dangling[i].StageId);
                        SetStatus("已清掉 " + dangling.Count + " 条失效注释。", MessageType.Info);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        #endregion

        #region 状态条与工具
        private void DrawStatusBar()
        {
            Rect rect = new Rect(0f, position.height - StatusHeight, position.width, StatusHeight);
            LevelEditorUI.FillRect(rect, new Color(0f, 0f, 0f, 0.18f));
            LevelEditorUI.FillRect(new Rect(rect.x, rect.y, 3f, rect.height),
                _statusType == MessageType.Warning ? LevelEditorUI.Warn :
                _statusType == MessageType.Error ? LevelEditorUI.Error : LevelEditorUI.Accent);

            Color old = GUI.contentColor;
            GUI.contentColor = _statusType == MessageType.Warning ? LevelEditorUI.Warn :
                               _statusType == MessageType.Error ? LevelEditorUI.Error : Color.white;
            GUI.Label(new Rect(rect.x + 10f, rect.y + 2f, rect.width - 20f, rect.height - 2f),
                string.IsNullOrEmpty(_status)
                    ? "保存后写进 StageNotes.json；要发给别人用「复制」或「导出为 Markdown」。"
                    : _status,
                LevelEditorUI.MiniLabel);
            GUI.contentColor = old;
        }

        private void SetStatus(string message, MessageType type)
        {
            _status = message;
            _statusType = type;
        }

        private static void PingAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
                return;

            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }

        /// <summary>文件还没生成过时，定位它的目录（RevealInFinder 对不存在的文件无效）。</summary>
        private static void RevealOrFolder(string filePath)
        {
            if (System.IO.File.Exists(filePath))
            {
                EditorUtility.RevealInFinder(filePath);
                return;
            }

            string folder = System.IO.Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(folder) && System.IO.Directory.Exists(folder))
                EditorUtility.RevealInFinder(folder);
        }
        #endregion
    }
}
