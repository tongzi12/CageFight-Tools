using UnityEditor;
using UnityEngine;

namespace CageFightDialogueTool
{
    /// <summary>对话编辑器的绘制工具箱（配色 + 常用小控件 + 气泡预览）。</summary>
    public static class DialogueEditorUI
    {
        #region 配色
        public static readonly Color Accent = new Color(0.29f, 0.62f, 0.96f, 1f);
        public static readonly Color Ok = new Color(0.35f, 0.78f, 0.44f, 1f);
        public static readonly Color Warn = new Color(0.96f, 0.72f, 0.20f, 1f);
        public static readonly Color Error = new Color(0.94f, 0.36f, 0.34f, 1f);
        public static readonly Color Muted = new Color(0.62f, 0.62f, 0.62f, 1f);
        public static readonly Color RowEven = new Color(1f, 1f, 1f, 0.03f);
        public static readonly Color SectionBg = new Color(0f, 0f, 0f, 0.16f);
        public static readonly Color Selected = new Color(0.24f, 0.45f, 0.75f, 0.55f);
        public static readonly Color GridLine = new Color(1f, 1f, 1f, 0.07f);
        public static readonly Color AvatarBg = new Color(0.13f, 0.14f, 0.16f, 1f);
        #endregion

        #region 缓存的贴图与样式
        private static Texture2D _white;
        private static GUIStyle _miniLabel;
        private static GUIStyle _miniBold;
        private static GUIStyle _rightMini;
        private static GUIStyle _wrapTextArea;
        private static GUIStyle _previewText;
        private static GUIStyle _searchStyle;
        private static GUIStyle _searchCancelStyle;
        private static GUIStyle _placeholderStyle;
        private static GUIStyle _tagStyle;
        private static GUIStyle _centeredMuted;

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

        public static GUIStyle MiniLabel
        {
            get
            {
                if (_miniLabel == null)
                {
                    _miniLabel = new GUIStyle(EditorStyles.miniLabel);
                    _miniLabel.wordWrap = false;
                    _miniLabel.clipping = TextClipping.Clip;
                }
                return _miniLabel;
            }
        }

        public static GUIStyle MiniBold
        {
            get
            {
                if (_miniBold == null)
                {
                    _miniBold = new GUIStyle(EditorStyles.miniBoldLabel);
                    _miniBold.wordWrap = false;
                    _miniBold.clipping = TextClipping.Clip;
                }
                return _miniBold;
            }
        }

        /// <summary>右对齐小字（数字列用：右对齐才比得出大小）。</summary>
        public static GUIStyle RightMini
        {
            get
            {
                if (_rightMini == null)
                {
                    _rightMini = new GUIStyle(EditorStyles.miniLabel);
                    _rightMini.alignment = TextAnchor.MiddleRight;
                    _rightMini.clipping = TextClipping.Clip;
                    _rightMini.wordWrap = false;
                }
                return _rightMini;
            }
        }

        /// <summary>
        /// 会换行的多行文本框。
        /// **必须自己造**：`EditorStyles.textArea` 的 wordWrap 是 false，
        /// 中文长句会被截断并出现横向滚动条（图鉴/注释编辑器都踩过这条）。
        /// </summary>
        public static GUIStyle WrapTextArea
        {
            get
            {
                if (_wrapTextArea == null)
                {
                    _wrapTextArea = new GUIStyle(EditorStyles.textArea);
                    _wrapTextArea.wordWrap = true;
                    _wrapTextArea.clipping = TextClipping.Clip;
                }
                return _wrapTextArea;
            }
        }

        /// <summary>气泡里的正文：黑字、会换行（与运行时一致：导游台词和局内对话都是黑字）。</summary>
        public static GUIStyle PreviewText
        {
            get
            {
                if (_previewText == null)
                {
                    _previewText = new GUIStyle(EditorStyles.label);
                    _previewText.wordWrap = true;
                    _previewText.richText = false;
                    _previewText.normal.textColor = Color.black;
                    _previewText.padding = new RectOffset(2, 2, 2, 2);
                }
                return _previewText;
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

        public static void Space(float px = 4f)
        {
            GUILayout.Space(px);
        }

        /// <summary>一行小标题 + 说明。</summary>
        public static void Section(string title, string hint = null)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel, GUILayout.Width(120f));
            if (!string.IsNullOrEmpty(hint))
                EditorGUILayout.LabelField(hint, MiniLabel);
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>色块标签。</summary>
        public static void Tag(string text, Color color)
        {
            if (_tagStyle == null)
            {
                _tagStyle = new GUIStyle(EditorStyles.miniLabel);
                _tagStyle.normal.textColor = Color.black;
                _tagStyle.alignment = TextAnchor.MiddleCenter;
            }

            float w = EditorStyles.miniLabel.CalcSize(new GUIContent(text)).x + 12f;
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), EditorStyles.miniLabel,
                GUILayout.Width(w), GUILayout.Height(15f));

            Color old = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, 0.20f);
            GUI.DrawTexture(rect, White);
            GUI.color = old;
            Frame(rect, color, 1f);

            GUI.Label(rect, text, _tagStyle);
        }

        /// <summary>把 Sprite 等比画进矩形（保持长宽比，居中）。</summary>
        public static void DrawSpriteFit(Rect rect, Sprite sprite, Color tint)
        {
            if (sprite == null)
                return;

            Texture2D texture = sprite.texture;
            if (texture == null)
                return;

            Rect tex = sprite.textureRect;
            Rect uv = new Rect(tex.x / texture.width, tex.y / texture.height,
                tex.width / texture.width, tex.height / texture.height);

            float spriteAspect = tex.width / Mathf.Max(1f, tex.height);
            float rectAspect = rect.width / Mathf.Max(1f, rect.height);

            Rect draw = rect;
            if (spriteAspect > rectAspect)
            {
                draw.height = rect.width / spriteAspect;
                draw.y += (rect.height - draw.height) * 0.5f;
            }
            else
            {
                draw.width = rect.height * spriteAspect;
                draw.x += (rect.width - draw.width) * 0.5f;
            }

            Color old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(draw, texture, uv, true);
            GUI.color = old;
        }

        /// <summary>空槽提示。</summary>
        public static void DrawEmptySlot(Rect rect, string text = "无图")
        {
            if (_centeredMuted == null)
            {
                _centeredMuted = new GUIStyle(EditorStyles.miniLabel);
                _centeredMuted.alignment = TextAnchor.MiddleCenter;
                _centeredMuted.normal.textColor = Muted;
            }

            FillRect(rect, AvatarBg);
            Frame(rect, GridLine, 1f);
            GUI.Label(rect, text, _centeredMuted);
        }

        /// <summary>一条拖不动的细分割条（用来手动排版整窗两栏）。</summary>
        public static float Splitter(Rect rect, float currentWidth, float min, float max)
        {
            int control = GUIUtility.GetControlID(FocusType.Passive);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            Event e = Event.current;
            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                GUIUtility.hotControl = control;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == control)
            {
                currentWidth += e.delta.x;
                currentWidth = Mathf.Clamp(currentWidth, min, max);
                GUI.changed = true;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == control)
            {
                GUIUtility.hotControl = 0;
                e.Use();
            }

            bool hot = GUIUtility.hotControl == control;
            FillRect(rect, hot ? Accent : GridLine);
            return currentWidth;
        }

        /// <summary>搜索框（带占位文字与清空按钮）。</summary>
        public static string SearchField(string text, string placeholder, params GUILayoutOption[] options)
        {
            if (_searchStyle == null)
            {
                _searchStyle = new GUIStyle(EditorStyles.toolbarSearchField);
                _searchCancelStyle = new GUIStyle(EditorStyles.toolbarSearchField);
                _searchCancelStyle.fixedWidth = 0f;
            }

            EditorGUILayout.BeginHorizontal(options);
            string result = EditorGUILayout.TextField(text ?? string.Empty, _searchStyle);

            if (string.IsNullOrEmpty(result))
            {
                if (_placeholderStyle == null)
                {
                    _placeholderStyle = new GUIStyle(EditorStyles.miniLabel);
                    _placeholderStyle.normal.textColor = Muted;
                }

                Rect rect = GUILayoutUtility.GetLastRect();
                rect.x += 16f;
                GUI.Label(rect, placeholder, _placeholderStyle);
            }

            if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(18f)))
                result = string.Empty;

            EditorGUILayout.EndHorizontal();
            return result;
        }

        /// <summary>
        /// 按运行时的样子画一个对话气泡（左立绘 + 右侧黑字正文）。
        /// 这不是运行时的真实控件，只是"会长成什么样"的示意：气泡宽度按窗口宽度走，
        /// 字号/间距与游戏内并不完全一致。
        /// </summary>
        public static void DrawBubblePreview(Rect rect, Sprite portrait, string text, string emptyHint)
        {
            FillRect(rect, new Color(0.82f, 0.82f, 0.78f, 1f));
            Frame(rect, new Color(0f, 0f, 0f, 0.35f), 1f);

            float avatarSize = Mathf.Min(56f, rect.height - 12f);
            Rect avatarRect = new Rect(rect.x + 8f, rect.y + (rect.height - avatarSize) * 0.5f, avatarSize, avatarSize);

            if (portrait != null)
            {
                FillRect(avatarRect, AvatarBg);
                DrawSpriteFit(avatarRect, portrait, Color.white);
            }
            else
            {
                DrawEmptySlot(avatarRect, "无头像");
            }

            Rect textRect = new Rect(avatarRect.xMax + 10f, rect.y + 6f,
                Mathf.Max(40f, rect.xMax - avatarRect.xMax - 18f), rect.height - 12f);

            string body = DialogueCore.IsBlank(text) ? emptyHint : text;
            GUI.Label(textRect, body, DialogueCore.IsBlank(text) ? PreviewEmptyText : PreviewText);
        }

        private static GUIStyle _previewEmptyText;

        /// <summary>空文案时的示意文字（浅一点的灰，区别于真台词）。</summary>
        private static GUIStyle PreviewEmptyText
        {
            get
            {
                if (_previewEmptyText == null)
                {
                    _previewEmptyText = new GUIStyle(PreviewText);
                    _previewEmptyText.normal.textColor = new Color(0f, 0f, 0f, 0.45f);
                }
                return _previewEmptyText;
            }
        }
        #endregion
    }
}
