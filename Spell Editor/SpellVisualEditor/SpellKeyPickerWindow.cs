using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightSpellTool
{
    /// <summary>
    /// 通用资源键选择器（独立可停靠窗口）。
    ///
    /// 可以像普通 Unity 窗口一样**任意拖边缘缩放、最大化、停靠**；选完一个键不会自动关闭，
    /// 适合连续给多个字段选键（图形/动画/音效/表里各列）。
    /// </summary>
    public sealed class SpellKeyPickerWindow : EditorWindow
    {
        private const int MaxResults = 600;

        private SpellResKind _kind;
        private Action<string> _onPick;
        private Action _onClear;
        private HashSet<string> _usedKeys;
        private string _targetLabel = string.Empty;
        private string _currentKey = string.Empty;

        private string _search = string.Empty;
        private string _lastSearch = "\u0000";
        private bool _onlyUsed;
        private bool _lastOnlyUsed;
        private Vector2 _scroll;
        private float _thumbSize = 22f;
        private readonly List<string> _results = new List<string>();

        public static SpellKeyPickerWindow Open(string fieldLabel, string targetLabel, SpellResKind kind, Action<string> onPick, Action onClear, HashSet<string> usedKeys, string currentKey = null)
        {
            var window = GetWindow<SpellKeyPickerWindow>(false, "资源键选择", true);
            window.minSize = new Vector2(320f, 200f);
            window.titleContent = new GUIContent(SpellResourceIndex.KindName(kind) + " 键选择");
            window._kind = kind;
            window._onPick = onPick;
            window._onClear = onClear;
            window._usedKeys = usedKeys;
            window._currentKey = currentKey ?? string.Empty;
            window._targetLabel = (fieldLabel ?? string.Empty) +
                                  (string.IsNullOrEmpty(targetLabel) ? string.Empty : "　→　" + targetLabel);

            // 复用已经开着的窗口时也要清干净：否则上一次的搜索词会留着，
            // 列表看起来"键变少了"，还以为是注册表出问题。
            window._search = string.Empty;
            window._lastSearch = "\u0000";
            window._onlyUsed = false;
            window._lastOnlyUsed = false;
            window._scroll = Vector2.zero;

            window.Show();
            window.Repaint();
            window.Focus();
            return window;
        }

        /// <summary>目标换人了（例如切换了兵种）就关掉，避免键填到已经不该改的字段上。</summary>
        public static void CloseIfOpen()
        {
            SpellKeyPickerWindow[] windows = Resources.FindObjectsOfTypeAll<SpellKeyPickerWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null)
                    windows[i].Close();
            }
        }

        private void OnGUI()
        {
            SpellResourceIndex index = SpellResourceIndex.Instance;

            EditorGUILayout.BeginVertical();

            EditorGUILayout.LabelField(
                SpellResourceIndex.KindName(_kind) + " 资源键（共 " + index.KeyCount(_kind) + " 个）",
                EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(_targetLabel))
                EditorGUILayout.LabelField("正在填：" + _targetLabel, EditorStyles.miniLabel);

            if (!index.RegistryFound(_kind))
            {
                EditorGUILayout.HelpBox("找不到注册表：" + index.RegistryPath(_kind) +
                                        "\n请先在工程里生成资源注册表（一键生成全部资源注册表）。", MessageType.Error);
            }

            _search = SpellEditorUI.SearchField(_search, "输入关键字，比如 Miner rat、Troop UI、-attack_0 …");

            EditorGUILayout.BeginHorizontal();
            if (_usedKeys != null && _usedKeys.Count > 0)
                _onlyUsed = EditorGUILayout.ToggleLeft("只看已被其它兵种用过的键（" + _usedKeys.Count + " 个）", _onlyUsed, GUILayout.Width(280f));
            _thumbSize = EditorGUILayout.Slider("行高", _thumbSize, 20f, 64f);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("窗口可任意拉伸 / 最大化 / 停靠", EditorStyles.miniLabel, GUILayout.Width(190f));
            EditorGUILayout.EndHorizontal();

            if (!string.Equals(_search, _lastSearch, StringComparison.Ordinal) || _onlyUsed != _lastOnlyUsed)
            {
                _lastSearch = _search;
                _lastOnlyUsed = _onlyUsed;
                Rebuild(index);
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
                bool isCurrent = string.Equals(key, _currentKey, StringComparison.Ordinal);

                if (isCurrent)
                    SpellEditorUI.FillRect(rowRect, new Color(SpellEditorUI.Ok.r, SpellEditorUI.Ok.g, SpellEditorUI.Ok.b, 0.22f));
                else if (hover)
                    SpellEditorUI.FillRect(rowRect, new Color(SpellEditorUI.Accent.r, SpellEditorUI.Accent.g, SpellEditorUI.Accent.b, 0.25f));

                DrawThumb(new Rect(rowRect.x + 2f, rowRect.y + 1f, _thumbSize, _thumbSize), key);

                // 编号帧在行首标出帧号：一列 18 帧时能一眼看出缺了哪一帧、按没按顺序排。
                string prefix;
                int frame;
                string frameTag = TrySplitFrameNumber(key, out prefix, out frame) ? "#" + frame + "　" : string.Empty;
                GUI.Label(new Rect(rowRect.x + _thumbSize + 6f, rowRect.y + (rowHeight - 18f) * 0.5f, rowRect.width - _thumbSize - 10f, 18f),
                    (isCurrent ? "● " : string.Empty) + frameTag + key, EditorStyles.miniLabel);

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
            if (_onClear != null && GUILayout.Button("清空该字段", GUILayout.Width(100f)))
                _onClear();
            if (GUILayout.Button("关闭", GUILayout.Width(70f)))
                Close();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("点一行即填入当前字段（可连续点多个）", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawThumb(Rect rect, string key)
        {
            SpellResourceIndex index = SpellResourceIndex.Instance;
            switch (_kind)
            {
                case SpellResKind.Sprite:
                    SpellEditorUI.DrawSpriteFit(rect, index.GetSprite(key), Color.white, 1f);
                    break;
                case SpellResKind.Prefab:
                    Texture thumb = SpellEditorUI.GetPrefabThumbnail(index.GetPrefab(key));
                    if (thumb != null)
                        GUI.DrawTexture(rect, thumb, ScaleMode.ScaleToFit);
                    break;
                default:
                    SpellEditorUI.FillRect(rect, new Color(1f, 1f, 1f, 0.08f));
                    break;
            }
        }

        private void Rebuild(SpellResourceIndex index)
        {
            _results.Clear();
            List<string> hits = index.Search(_kind, _search, MaxResults);
            for (int i = 0; i < hits.Count && _results.Count < MaxResults; i++)
            {
                if (_onlyUsed && (_usedKeys == null || !_usedKeys.Contains(hits[i])))
                    continue;

                _results.Add(hits[i]);
            }

            SortFrameSeries(_results);
        }

        /// <summary>
        /// 把同一个"编号系列"的键按帧号排在一起。
        ///
        /// 注册表里的顺序是资源导入顺序，不是帧号顺序（实测 "BabaDoctor Y-7 AP_15" 会排在
        /// "_0" 前面），所以一列 18 帧看起来是乱序、挑帧要来回翻。
        /// 只调整同一系列内部的相对顺序、保持各系列之间原有的先后，不打乱搜索排名的整体观感。
        /// </summary>
        private static void SortFrameSeries(List<string> keys)
        {
            var groups = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var groupOrder = new List<string>();
            var frames = new Dictionary<int, int>();

            for (int i = 0; i < keys.Count; i++)
            {
                string prefix;
                int frame;
                if (!TrySplitFrameNumber(keys[i], out prefix, out frame))
                    continue;

                List<int> members;
                if (!groups.TryGetValue(prefix, out members))
                {
                    members = new List<int>();
                    groups.Add(prefix, members);
                    groupOrder.Add(prefix);
                }

                members.Add(i);
                frames[i] = frame;
            }

            for (int g = 0; g < groupOrder.Count; g++)
            {
                List<int> slots = groups[groupOrder[g]];
                if (slots.Count < 2)
                    continue;

                var values = new List<string>(slots.Count);
                for (int i = 0; i < slots.Count; i++)
                    values.Add(keys[slots[i]]);

                var sortedSlots = new List<int>(slots);
                sortedSlots.Sort((a, b) =>
                {
                    int cmp = frames[a].CompareTo(frames[b]);
                    return cmp != 0 ? cmp : a.CompareTo(b);
                });

                for (int i = 0; i < sortedSlots.Count; i++)
                    keys[sortedSlots[i]] = values[i];
            }
        }

        /// <summary>把 "Miner rat-move_17" 拆成前缀 "Miner rat-move_" 与帧号 17。</summary>
        private static bool TrySplitFrameNumber(string key, out string prefix, out int frame)
        {
            prefix = string.Empty;
            frame = 0;
            if (string.IsNullOrEmpty(key))
                return false;

            int i = key.Length;
            while (i > 0 && char.IsDigit(key[i - 1]))
                i--;

            if (i == key.Length)
                return false;   // 结尾不是数字：不是编号帧

            prefix = key.Substring(0, i);
            return int.TryParse(key.Substring(i), out frame);
        }
    }
}
