using UnityEditor;
using UnityEngine;

namespace CageFightUpgradeTool
{
    /// <summary>
    /// 等级编辑器的绘制工具箱：配色、分区标题、折叠头、标签、分割条、搜索框、键值行。
    ///
    /// 视觉语言与另外五个可视化编辑器完全一致（同一套 IMGUI 自绘做法），只做绘制，
    /// 不持有任何游戏数据。
    /// </summary>
    public static class UpgradeEditorUI
    {
        #region 配色
        public static readonly Color Accent = new Color(0.29f, 0.62f, 0.96f, 1f);
        public static readonly Color Ok = new Color(0.35f, 0.78f, 0.44f, 1f);
        public static readonly Color Warn = new Color(0.96f, 0.72f, 0.20f, 1f);
        public static readonly Color Error = new Color(0.94f, 0.36f, 0.34f, 1f);
        public static readonly Color Muted = new Color(0.62f, 0.62f, 0.62f, 1f);
        public static readonly Color Selected = new Color(0.24f, 0.45f, 0.75f, 0.55f);
        public static readonly Color RowEven = new Color(1f, 1f, 1f, 0.03f);
        public static readonly Color SectionBg = new Color(0f, 0f, 0f, 0.16f);

        /// <summary>等级/货币这类"成长"数据的强调色。</summary>
        public static readonly Color Growth = new Color(0.98f, 0.78f, 0.32f, 1f);
        /// <summary>存档/文件类信息的颜色。</summary>
        public static readonly Color FileInfo = new Color(0.55f, 0.80f, 0.92f, 1f);
        #endregion

        #region 缓存的贴图与样式
        private static Texture2D _white;
        private static GUIStyle _sectionStyle;
        private static GUIStyle _miniLabel;
        private static GUIStyle _richLabel;
        private static GUIStyle _centeredMini;
        private static GUIStyle _searchStyle;
        private static GUIStyle _searchCancelStyle;
        private static GUIStyle _tagTextStyle;

        public static Texture2D White
        {
            get
            {
                if (_white == null)
                {
                    _white = new Texture2D(1, 1);
                    _white.SetPixel(0, 0, Color.white);
                    _white.Apply();
                    _white.hideFlags = HideFlags.HideAndDontSave;
                }
                return _white;
            }
        }

        public static GUIStyle SectionStyle
        {
            get
            {
                if (_sectionStyle == null)
                {
                    _sectionStyle = new GUIStyle(EditorStyles.boldLabel);
                    _sectionStyle.fontSize = 12;
                }
                return _sectionStyle;
            }
        }

        public static GUIStyle MiniLabel
        {
            get
            {
                if (_miniLabel == null)
                {
                    _miniLabel = new GUIStyle(EditorStyles.miniLabel);
                    _miniLabel.wordWrap = false;
                }
                return _miniLabel;
            }
        }

        public static GUIStyle RichLabel
        {
            get
            {
                if (_richLabel == null)
                {
                    _richLabel = new GUIStyle(EditorStyles.label);
                    _richLabel.richText = true;
                    _richLabel.wordWrap = true;
                }
                return _richLabel;
            }
        }

        public static GUIStyle CenteredMini
        {
            get
            {
                if (_centeredMini == null)
                {
                    _centeredMini = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                    _centeredMini.clipping = TextClipping.Clip;
                }
                return _centeredMini;
            }
        }

        /// <summary>标签文字样式：深黑色、加粗、居中（浅底 + 深字，暗色皮肤下才看得清）。</summary>
        public static GUIStyle TagTextStyle
        {
            get
            {
                if (_tagTextStyle == null)
                {
                    _tagTextStyle = new GUIStyle(EditorStyles.miniLabel);
                    _tagTextStyle.alignment = TextAnchor.MiddleCenter;
                    _tagTextStyle.fontStyle = FontStyle.Bold;
                    _tagTextStyle.clipping = TextClipping.Clip;
                    _tagTextStyle.normal.textColor = new Color(0.05f, 0.06f, 0.08f, 1f);
                }
                return _tagTextStyle;
            }
        }
        #endregion

        #region 基础绘制
        public static void FillRect(Rect rect, Color color)
        {
            Color old = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, White);
            GUI.color = old;
        }

        public static void Frame(Rect rect, Color color, float thickness = 1f)
        {
            FillRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            FillRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            FillRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            FillRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        /// <summary>分区标题（带背景条 + 副标题）。</summary>
        public static void Section(string title, string hint = null)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 22f, GUILayout.ExpandWidth(true));
            FillRect(rect, SectionBg);
            FillRect(new Rect(rect.x, rect.y, 3f, rect.height), Accent);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 3f, rect.width - 12f, 18f), title, SectionStyle);
            if (!string.IsNullOrEmpty(hint))
                GUI.Label(new Rect(rect.x + 8f + SectionStyle.CalcSize(new GUIContent(title)).x + 10f, rect.y + 4f, rect.width, 16f), hint, MiniLabel);
            GUILayout.Space(4f);
        }

        public static void Hint(string text)
        {
            EditorGUILayout.LabelField(text ?? string.Empty, MiniLabel);
        }

        /// <summary>一段可换行的说明文字（用于讲清"为什么"）。</summary>
        public static void Note(string text, Color color)
        {
            Color old = GUI.contentColor;
            GUI.contentColor = color;
            EditorGUILayout.LabelField(text ?? string.Empty, RichLabel);
            GUI.contentColor = old;
        }

        /// <summary>
        /// 可点击折叠的分组头（整行可点）。返回点击后的展开状态（true = 展开）。
        /// </summary>
        public static bool FoldHeader(bool expanded, string title, string hint, Color accent,
            string rightText = null, int indent = 0)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, 21f, GUILayout.ExpandWidth(true));
            bool hover = rect.Contains(Event.current.mousePosition);

            FillRect(rect, hover ? new Color(1f, 1f, 1f, 0.10f) : new Color(1f, 1f, 1f, 0.05f));
            FillRect(new Rect(rect.x + indent, rect.y, 3f, rect.height), accent);

            float x = rect.x + indent + 6f;
            GUI.Label(new Rect(x, rect.y + 2f, 14f, 17f), expanded ? "▼" : "▶", CenteredMini);
            x += 16f;

            GUI.Label(new Rect(x, rect.y + 2f, rect.width - (x - rect.x) - 8f, 17f), title, EditorStyles.boldLabel);
            x += EditorStyles.boldLabel.CalcSize(new GUIContent(title)).x + 8f;

            // 计数紧跟在标题后面（左对齐流动），**不做右对齐**：
            // 右对齐靠 rect.xMax 定位，滚动视图内容一旦比视口宽，rect.xMax 就会跑到可视区外，计数被裁掉。
            if (!string.IsNullOrEmpty(rightText))
            {
                float tagWidth = EditorStyles.miniLabel.CalcSize(new GUIContent(rightText)).x + 12f;
                if (x + tagWidth <= rect.xMax - 4f)
                {
                    TagAt(new Rect(x, rect.y + 3f, tagWidth, 15f), rightText, accent);
                    x += tagWidth + 6f;
                }
            }

            if (!string.IsNullOrEmpty(hint) && x < rect.xMax - 40f)
                GUI.Label(new Rect(x, rect.y + 3f, rect.xMax - x - 6f, 16f), hint, MiniLabel);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition) && Event.current.button == 0)
            {
                expanded = !expanded;
                Event.current.Use();
            }

            return expanded;
        }

        /// <summary>一个小的 ▼/▶ 折叠开关按钮，返回点击后的展开状态。</summary>
        public static bool FoldToggle(bool expanded, float width = 20f)
        {
            if (GUILayout.Button(expanded ? "▼" : "▶", EditorStyles.miniButton, GUILayout.Width(width)))
                expanded = !expanded;
            return expanded;
        }

        public static void Space(float px = 6f)
        {
            GUILayout.Space(px);
        }

        /// <summary>标签底色：把分类色提亮成浅色（配合深黑字）。</summary>
        public static Color TagFill(Color accent)
        {
            return Color.Lerp(Color.white, new Color(accent.r, accent.g, accent.b, 1f), 0.32f);
        }

        /// <summary>一行小色块 + 文本，用于状态标签。</summary>
        public static void Tag(string text, Color color)
        {
            float w = Mathf.Max(TagTextStyle.CalcSize(new GUIContent(text)).x + 12f,
                EditorStyles.miniLabel.CalcSize(new GUIContent(text)).x + 10f);
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), EditorStyles.miniLabel,
                GUILayout.Width(w), GUILayout.Height(15f));
            FillRect(rect, TagFill(color));
            Frame(rect, color, 1f);
            GUI.Label(rect, text, TagTextStyle);
        }

        /// <summary>在指定矩形里画一个小标签（不参与布局）。</summary>
        public static void TagAt(Rect rect, string text, Color color)
        {
            FillRect(rect, TagFill(color));
            Frame(rect, color, 1f);
            GUI.Label(rect, text, TagTextStyle);
        }

        /// <summary>
        /// 可拖拽的分割条，返回新的宽度。
        /// 命中区域左右各外扩 3px（线只有 5px 很难瞄准），悬停/拖动时把线画亮加粗。
        /// </summary>
        public static float Splitter(Rect rect, float currentWidth, float min, float max, bool vertical = true)
        {
            int control = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;

            Rect hit = vertical
                ? new Rect(rect.x - 3f, rect.y, rect.width + 6f, rect.height)
                : new Rect(rect.x, rect.y - 3f, rect.width, rect.height + 6f);

            bool hovering = hit.Contains(e.mousePosition);
            bool dragging = GUIUtility.hotControl == control;

            EditorGUIUtility.AddCursorRect(hit, vertical ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);

            if (e.type == EventType.MouseDown && e.button == 0 && hovering)
            {
                GUIUtility.hotControl = control;
                dragging = true;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == control)
            {
                float delta = vertical ? e.delta.x : e.delta.y;
                float safeMin = Mathf.Min(min, max);
                currentWidth = Mathf.Clamp(currentWidth + delta, safeMin, Mathf.Max(min, max));
                dragging = true;
                e.Use();
                GUI.changed = true;
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == control)
            {
                GUIUtility.hotControl = 0;
                dragging = false;
                e.Use();
            }

            bool active = hovering || dragging;
            Color barColor = active
                ? new Color(Accent.r, Accent.g, Accent.b, 0.85f)
                : new Color(1f, 1f, 1f, 0.10f);

            if (vertical)
            {
                float w = active ? 3f : 1f;
                FillRect(new Rect(rect.x + rect.width * 0.5f - w * 0.5f, rect.y, w, rect.height), barColor);
            }
            else
            {
                float h = active ? 3f : 1f;
                FillRect(new Rect(rect.x, rect.y + rect.height * 0.5f - h * 0.5f, rect.width, h), barColor);
            }

            return currentWidth;
        }

        /// <summary>模拟游戏内搜索框样式的输入框（带清除按钮）。</summary>
        public static string SearchField(string text, string placeholder, params GUILayoutOption[] options)
        {
            if (_searchStyle == null)
                _searchStyle = GUI.skin.FindStyle("ToolbarSeachTextField") ?? GUI.skin.FindStyle("ToolbarSearchTextField") ?? GUI.skin.textField;
            if (_searchCancelStyle == null)
                _searchCancelStyle = GUI.skin.FindStyle("ToolbarSeachCancelButton") ?? GUI.skin.FindStyle("ToolbarSearchCancelButton");

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, options);
            text = GUILayout.TextField(text ?? string.Empty, _searchStyle, GUILayout.ExpandWidth(true));
            if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(placeholder))
            {
                Rect last = GUILayoutUtility.GetLastRect();
                GUI.Label(new Rect(last.x + 4f, last.y, last.width, last.height), placeholder, EditorStyles.miniLabel);
            }
            if (_searchCancelStyle != null && GUILayout.Button(string.Empty, _searchCancelStyle, GUILayout.Width(16f)))
                text = string.Empty;
            EditorGUILayout.EndHorizontal();
            return text;
        }

        /// <summary>一条横向占比条：底槽 + 填充 + 居中文本。</summary>
        public static void Bar(Rect rect, float ratio, Color color, string label = null)
        {
            FillRect(rect, new Color(1f, 1f, 1f, 0.07f));
            float w = Mathf.Clamp01(ratio) * rect.width;
            if (w > 0f)
                FillRect(new Rect(rect.x, rect.y, w, rect.height), new Color(color.r, color.g, color.b, 0.75f));
            Frame(rect, new Color(color.r, color.g, color.b, 0.5f), 1f);
            if (!string.IsNullOrEmpty(label))
                GUI.Label(rect, label, CenteredMini);
        }

        /// <summary>一行「键 -> 值」的只读展示。</summary>
        public static void KeyValue(string key, string value, float keyWidth = 96f)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(key, MiniLabel, GUILayout.Width(keyWidth));
            EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(value) ? "（空）" : value, EditorStyles.miniLabel, GUILayout.Height(15f));
            EditorGUILayout.EndHorizontal();
        }
        #endregion
    }
}
