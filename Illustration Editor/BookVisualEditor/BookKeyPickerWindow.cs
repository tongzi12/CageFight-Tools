using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightBookTool
{
    /// <summary>
    /// 资源键选择器（独立可停靠窗口）。
    ///
    /// 相比弹窗的好处：可以像普通 Unity 窗口一样**任意拖边缘缩放、最大化、停靠**在编辑器任意位置，
    /// 而且选完一个键后不会自动关闭，可以连续给多个字段选键。
    ///
    /// 用法：主窗口里点「搜索 / 搜索选图」，本窗口会显示当前正在改哪个字段、目标条目是谁。
    /// </summary>
    public sealed class BookKeyPickerWindow : EditorWindow
    {
        private const int MaxResults = 600;

        private bool _isSprite;
        private Action<string> _onPick;
        private Action _onClear;
        private HashSet<string> _usedKeys;
        private string _targetLabel = string.Empty;

        private readonly List<string> _source = new List<string>();
        private readonly List<string> _results = new List<string>();
        private string _search = string.Empty;
        private string _lastSearch = "\u0000";
        private bool _onlyUsed;
        private bool _lastOnlyUsed;
        private Vector2 _scroll;
        private float _thumbSize = 22f;

        /// <summary>打开（或复用）选择器窗口。</summary>
        public static BookKeyPickerWindow Open(string fieldLabel, string targetLabel, bool isSprite, Action<string> onPick, Action onClear, HashSet<string> usedKeys)
        {
            var window = GetWindow<BookKeyPickerWindow>(false, "资源键选择", true);
            window.minSize = new Vector2(320f, 200f);
            window.titleContent = new GUIContent(isSprite ? "Sprite 键选择" : "预制体键选择");
            window._isSprite = isSprite;
            window._onPick = onPick;
            window._onClear = onClear;
            window._usedKeys = usedKeys;
            window._targetLabel = (fieldLabel ?? string.Empty) +
                                  (string.IsNullOrEmpty(targetLabel) ? string.Empty : "　→　" + targetLabel);
            window.RebuildSource();
            window._lastSearch = "\u0000";   // 强制重建结果列表
            window.Show();
            window.Repaint();
            window.Focus();
            return window;
        }

        /// <summary>目标都换了（例如切换了条目）就关掉，避免键填到已经不该改的字段上。</summary>
        public static void CloseIfOpen()
        {
            BookKeyPickerWindow[] windows = Resources.FindObjectsOfTypeAll<BookKeyPickerWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null)
                    windows[i].Close();
            }
        }

        private void RebuildSource()
        {
            _source.Clear();
            BookResourceIndex index = BookResourceIndex.Instance;
            IReadOnlyList<string> keys = _isSprite ? index.SpriteKeys : index.PrefabKeys;
            for (int i = 0; i < keys.Count; i++)
                _source.Add(keys[i]);
        }

        private void OnGUI()
        {
            BookResourceIndex index = BookResourceIndex.Instance;

            EditorGUILayout.BeginVertical();

            EditorGUILayout.LabelField(
                (_isSprite ? "Sprite 资源键（共 " + _source.Count + " 个）" : "预制体资源键（共 " + _source.Count + " 个）"),
                EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(_targetLabel))
                EditorGUILayout.LabelField("正在填：" + _targetLabel, EditorStyles.miniLabel);

            _search = BookEditorUI.SearchField(_search, "输入关键字，比如 Miner rat、Troop UI_0 …");

            EditorGUILayout.BeginHorizontal();
            _onlyUsed = EditorGUILayout.ToggleLeft("只看已被其它条目用过的键", _onlyUsed, GUILayout.Width(190f));
            _thumbSize = EditorGUILayout.Slider("行高", _thumbSize, 20f, 64f);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("窗口可任意拉伸 / 最大化 / 停靠", EditorStyles.miniLabel, GUILayout.Width(190f));
            EditorGUILayout.EndHorizontal();

            if (!string.Equals(_search, _lastSearch, StringComparison.Ordinal) || _onlyUsed != _lastOnlyUsed)
            {
                _lastSearch = _search;
                _lastOnlyUsed = _onlyUsed;
                RebuildResults(index);
            }

            EditorGUILayout.LabelField(
                "命中 " + _results.Count + " 个" + (_results.Count >= MaxResults ? "（已截断，请加关键字缩小范围）" : string.Empty),
                EditorStyles.miniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            float rowHeight = Mathf.Max(22f, _thumbSize + 2f);

            for (int i = 0; i < _results.Count; i++)
            {
                string key = _results[i];
                Rect rowRect = GUILayoutUtility.GetRect(0f, rowHeight, GUILayout.ExpandWidth(true));
                bool hover = rowRect.Contains(Event.current.mousePosition);

                if (hover)
                    BookEditorUI.FillRect(rowRect, new Color(BookEditorUI.Accent.r, BookEditorUI.Accent.g, BookEditorUI.Accent.b, 0.25f));

                var thumbRect = new Rect(rowRect.x + 2f, rowRect.y + 1f, _thumbSize, _thumbSize);
                if (_isSprite)
                {
                    BookEditorUI.DrawSpriteFit(thumbRect, index.GetSprite(key), Color.white, 1f);
                }
                else
                {
                    Texture preview = BookEditorUI.GetPrefabThumbnail(index.GetPrefab(key));
                    if (preview != null)
                        GUI.DrawTexture(thumbRect, preview, ScaleMode.ScaleToFit);
                }

                GUI.Label(new Rect(rowRect.x + _thumbSize + 6f, rowRect.y + (rowHeight - 18f) * 0.5f, rowRect.width - _thumbSize - 10f, 18f),
                    key, EditorStyles.miniLabel);

                if (hover && Event.current.type == EventType.MouseDown)
                {
                    if (_onPick != null)
                        _onPick(key);
                    Repaint();
                }
            }

            if (_results.Count == 0)
                EditorGUILayout.LabelField("没有匹配的键。", EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("清空该字段", GUILayout.Width(100f)))
            {
                if (_onClear != null)
                    _onClear();
            }
            if (GUILayout.Button("关闭", GUILayout.Width(70f)))
                Close();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("点一行即填入当前字段（可连续点多个）", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void RebuildResults(BookResourceIndex index)
        {
            _results.Clear();
            List<string> hits = _isSprite
                ? index.SearchSprites(_search, MaxResults)
                : index.SearchUnitPrefabs(_search, MaxResults);

            for (int i = 0; i < hits.Count && _results.Count < MaxResults; i++)
            {
                if (_onlyUsed && (_usedKeys == null || !_usedKeys.Contains(hits[i])))
                    continue;

                _results.Add(hits[i]);
            }
        }
    }
}
