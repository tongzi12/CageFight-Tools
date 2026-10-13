using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightUnitTool
{
    /// <summary>兵种编辑器的绘制工具箱（与图鉴编辑器各自独立，互不依赖）。</summary>
    public static class UnitEditorUI
    {
        #region 配色
        public static readonly Color Accent = new Color(0.29f, 0.62f, 0.96f, 1f);
        public static readonly Color Ok = new Color(0.35f, 0.78f, 0.44f, 1f);
        public static readonly Color Warn = new Color(0.96f, 0.72f, 0.20f, 1f);
        public static readonly Color Error = new Color(0.94f, 0.36f, 0.34f, 1f);
        public static readonly Color Muted = new Color(0.62f, 0.62f, 0.62f, 1f);
        public static readonly Color Selected = new Color(0.24f, 0.45f, 0.75f, 0.55f);
        public static readonly Color SectionBg = new Color(0f, 0f, 0f, 0.16f);
        public static readonly Color RowAlt = new Color(1f, 1f, 1f, 0.035f);
        public static readonly Color PreviewBg = new Color(0.13f, 0.14f, 0.16f, 1f);
        #endregion

        private static Texture2D _white;
        private static GUIStyle _sectionStyle;
        private static GUIStyle _miniLabel;
        private static GUIStyle _searchStyle;
        private static GUIStyle _searchCancelStyle;
        private static GUIStyle _richLabel;
        private static GUIStyle _cellStyle;
        private static GUIStyle _headerStyle;

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
                    _miniLabel = new GUIStyle(EditorStyles.miniLabel);
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

        public static GUIStyle CellStyle
        {
            get
            {
                if (_cellStyle == null)
                {
                    _cellStyle = new GUIStyle(EditorStyles.miniLabel);
                    _cellStyle.alignment = TextAnchor.MiddleRight;
                    _cellStyle.padding = new RectOffset(4, 4, 0, 0);
                }
                return _cellStyle;
            }
        }

        public static GUIStyle HeaderStyle
        {
            get
            {
                if (_headerStyle == null)
                {
                    _headerStyle = new GUIStyle(EditorStyles.miniBoldLabel);
                    _headerStyle.alignment = TextAnchor.MiddleRight;
                    _headerStyle.padding = new RectOffset(4, 4, 0, 0);
                    _headerStyle.normal.textColor = new Color(0.85f, 0.88f, 0.95f, 1f);
                }
                return _headerStyle;
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

        public static void Tag(string text, Color color)
        {
            Rect rect = GUILayoutUtility.GetRect(new GUIContent(text), EditorStyles.miniLabel, GUILayout.Width(EditorStyles.miniLabel.CalcSize(new GUIContent(text)).x + 10f), GUILayout.Height(15f));
            FillRect(rect, new Color(color.r, color.g, color.b, 0.22f));
            Frame(rect, color, 1f);
            GUI.Label(rect, text, EditorStyles.centeredGreyMiniLabel);
        }

        /// <summary>可拖拽的分割条。</summary>
        public static float Splitter(Rect rect, float current, float min, float max)
        {
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            int control = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;

            if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
            {
                GUIUtility.hotControl = control;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == control)
            {
                current = Mathf.Clamp(current + e.delta.x, min, max);
                e.Use();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == control)
            {
                GUIUtility.hotControl = 0;
                e.Use();
            }

            FillRect(rect, new Color(0f, 0f, 0f, 0.25f));
            return current;
        }

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

        #region Sprite / 缩略图
        private static readonly Dictionary<int, Texture> _prefabThumbs = new Dictionary<int, Texture>();

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

            float scale = Mathf.Min(Mathf.Max(1f, rect.width - padding * 2f) / sw, Mathf.Max(1f, rect.height - padding * 2f) / sh);
            float w = sw * scale;
            float h = sh * scale;
            DrawSprite(new Rect(rect.center.x - w * 0.5f, rect.center.y - h * 0.5f, w, h), sprite, tint);
        }

        public static void DrawEmptySlot(Rect rect, Color tint = default(Color))
        {
            Color c = tint == default(Color) ? Muted : tint;
            FillRect(rect, new Color(c.r, c.g, c.b, 0.10f));
            Frame(rect, new Color(c.r, c.g, c.b, 0.45f), 1f);
            GUI.Label(rect, "无图", EditorStyles.centeredGreyMiniLabel);
        }

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

        public static void KeyValue(string key, string value, float labelWidth = 84f)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(key, MiniLabel, GUILayout.Width(labelWidth));
            EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(value) ? "（空）" : value, MiniLabel, GUILayout.Height(15f));
            EditorGUILayout.EndHorizontal();
        }
        #endregion

        #region 对比表绘制
        /// <summary>表头单元格，返回是否被点击（用于排序）。</summary>
        public static bool HeaderCell(Rect rect, string label, int columnIndex, int currentSortColumn, bool ascending, string tooltip)
        {
            bool active = columnIndex == currentSortColumn;
            FillRect(rect, active ? new Color(Accent.r, Accent.g, Accent.b, 0.30f) : new Color(1f, 1f, 1f, 0.06f));

            string text = label + (active ? (ascending ? " ▲" : " ▼") : string.Empty);
            GUI.Label(rect, new GUIContent(text, tooltip), HeaderStyle);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                return true;
            }
            return false;
        }

        public static void Cell(Rect rect, string text, Color color, bool bold = false)
        {
            GUIStyle style = bold ? EditorStyles.miniBoldLabel : CellStyle;
            Color old = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(rect, text, style);
            GUI.contentColor = old;
        }

        /// <summary>单元格里的迷你条形图（用于一眼看出数值高低）。</summary>
        private static GUIStyle _cellLeftStyle;

        public static GUIStyle CellLeftStyle
        {
            get
            {
                if (_cellLeftStyle == null)
                {
                    _cellLeftStyle = new GUIStyle(EditorStyles.miniLabel);
                    _cellLeftStyle.alignment = TextAnchor.MiddleLeft;
                    _cellLeftStyle.padding = new RectOffset(4, 4, 0, 0);
                }
                return _cellLeftStyle;
            }
        }

        /// <summary>左对齐单元格（ID 之类用左对齐更好读）。</summary>
        public static void CellLeft(Rect rect, string text, Color color)
        {
            Color old = GUI.contentColor;
            GUI.contentColor = color;
            GUI.Label(rect, text, CellLeftStyle);
            GUI.contentColor = old;
        }

        public static void CellBar(Rect rect, float value, float max, Color color)
        {
            if (max <= 0f)
                return;

            var bar = new Rect(rect.x + 3f, rect.yMax - 4f, (rect.width - 6f) * Mathf.Clamp01(value / max), 2f);
            FillRect(bar, new Color(color.r, color.g, color.b, 0.75f));
        }
        #endregion
    }
}
