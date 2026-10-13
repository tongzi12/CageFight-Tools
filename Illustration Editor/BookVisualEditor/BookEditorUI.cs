using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightBookTool
{
    /// <summary>
    /// 图鉴编辑器的绘制工具箱：配色、Sprite 缩略图、分区标题、分割条、搜索框、星级等。
    /// 只做 UI 绘制，不持有任何游戏数据。
    /// </summary>
    public static class BookEditorUI
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
        public static readonly Color PreviewBg = new Color(0.13f, 0.14f, 0.16f, 1f);
        #endregion

        private static Texture2D _white;
        private static GUIStyle _sectionStyle;
        private static GUIStyle _miniLabel;
        private static GUIStyle _searchStyle;
        private static GUIStyle _searchCancelStyle;
        private static GUIStyle _richLabel;

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
            EditorGUILayout.LabelField(text, MiniLabel);
        }

        public static void Space(float px = 6f)
        {
            GUILayout.Space(px);
        }

        /// <summary>一行小色块 + 文本，用于状态标签。</summary>
        public static void Tag(string text, Color color)
        {
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), EditorStyles.miniLabel, GUILayout.Width(EditorStyles.miniLabel.CalcSize(new GUIContent(text)).x + 10f), GUILayout.Height(15f));
            FillRect(rect, new Color(color.r, color.g, color.b, 0.22f));
            Frame(rect, color, 1f);
            GUI.Label(rect, text, EditorStyles.centeredGreyMiniLabel);
        }

        /// <summary>可拖拽的分割条，返回新的左栏宽度。</summary>
        public static float Splitter(Rect rect, float currentWidth, float min, float max, bool vertical = true)
        {
            EditorGUIUtility.AddCursorRect(rect, vertical ? MouseCursor.ResizeHorizontal : MouseCursor.ResizeVertical);
            int control = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;

            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                GUIUtility.hotControl = control;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == control)
            {
                currentWidth += vertical ? e.delta.x : e.delta.y;
                currentWidth = Mathf.Clamp(currentWidth, min, max);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == control)
            {
                GUIUtility.hotControl = 0;
                e.Use();
            }

            FillRect(rect, new Color(0f, 0f, 0f, 0.25f));
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
        #endregion

        #region Sprite / 纹理绘制
        private static readonly Dictionary<int, Texture> _prefabThumbs = new Dictionary<int, Texture>();

        /// <summary>按贴图子区域绘制 Sprite（支持图集切片，比 AssetPreview 更可靠）。</summary>
        public static void DrawSprite(Rect rect, Sprite sprite, Color tint)
        {
            if (sprite == null || sprite.texture == null)
            {
                DrawEmptySlot(rect, tint);
                return;
            }

            Texture texture = sprite.texture;
            Rect tr = sprite.textureRect;
            if (tr.width <= 0f || tr.height <= 0f)
            {
                DrawEmptySlot(rect, tint);
                return;
            }

            var uv = new Rect(tr.x / texture.width, tr.y / texture.height, tr.width / texture.width, tr.height / texture.height);
            Color old = GUI.color;
            GUI.color = tint;
            GUI.DrawTextureWithTexCoords(rect, texture, uv, true);
            GUI.color = old;
        }

        /// <summary>等比缩放居中绘制 Sprite。</summary>
        public static void DrawSpriteFit(Rect rect, Sprite sprite, Color tint, float padding = 2f)
        {
            if (sprite == null)
            {
                DrawEmptySlot(rect, tint);
                return;
            }

            float sw = sprite.rect.width;
            float sh = sprite.rect.height;
            if (sw <= 0f || sh <= 0f)
            {
                DrawSprite(rect, sprite, tint);
                return;
            }

            float availW = Mathf.Max(1f, rect.width - padding * 2f);
            float availH = Mathf.Max(1f, rect.height - padding * 2f);
            float scale = Mathf.Min(availW / sw, availH / sh);
            float w = sw * scale;
            float h = sh * scale;
            var target = new Rect(rect.center.x - w * 0.5f, rect.center.y - h * 0.5f, w, h);
            DrawSprite(target, sprite, tint);
        }

        /// <summary>空位绘制：虚线感灰框 + 问号。</summary>
        public static void DrawEmptySlot(Rect rect, Color tint = default(Color))
        {
            Color c = tint == default(Color) ? Muted : tint;
            FillRect(rect, new Color(c.r, c.g, c.b, 0.10f));
            Frame(rect, new Color(c.r, c.g, c.b, 0.45f), 1f);
            GUI.Label(rect, "无图", EditorStyles.centeredGreyMiniLabel);
        }

        /// <summary>预制体缩略图（AssetPreview 缓存）。</summary>
        public static Texture GetPrefabThumbnail(GameObject prefab)
        {
            if (prefab == null)
                return null;

            int id = prefab.GetInstanceID();
            Texture cached;
            if (_prefabThumbs.TryGetValue(id, out cached) && cached != null)
                return cached;

            Texture thumb = AssetPreview.GetAssetPreview(prefab);
            if (thumb != null)
                _prefabThumbs[id] = thumb;
            return thumb;
        }

        /// <summary>绘制星级（1-7，只亮所选那一颗，与游戏内 Card 一致）。</summary>
        public static void DrawStars(Rect rect, int star, int max = 7)
        {
            float slot = rect.width / max;
            for (int i = 0; i < max; i++)
            {
                var cell = new Rect(rect.x + i * slot, rect.y, slot - 1f, rect.height);
                bool lit = (i + 1) == star;
                FillRect(cell, lit ? new Color(Warn.r, Warn.g, Warn.b, 0.85f) : new Color(1f, 1f, 1f, 0.08f));
                if (lit)
                    Frame(cell, Color.white, 1f);
            }
        }

        /// <summary>一行「键 -> 值」的只读展示。</summary>
        public static void KeyValue(string key, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(key, MiniLabel, GUILayout.Width(74f));
            EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(value) ? "（空）" : value, EditorStyles.miniLabel, GUILayout.Height(15f));
            EditorGUILayout.EndHorizontal();
        }
        #endregion
    }
}
