using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightStageTool
{
    /// <summary>
    /// 资源键 / 兵种选择窗口（独立可停靠窗口）。
    ///
    /// 相比弹窗的好处：可以像普通 Unity 窗口一样拖边缘缩放、最大化、停靠，
    /// 而且选完一个不会自动关闭，可以连续给多个字段选。行高可拖，方便看清缩略图。
    /// </summary>
    public sealed class LevelEditorPickerWindow : EditorWindow
    {
        private const int MaxResults = 600;

        private enum Mode
        {
            Key,
            Troop
        }

        private Mode _mode = Mode.Key;
        private StageResourceIndex.StageKeyKind _kind = StageResourceIndex.StageKeyKind.Sprite;

        private Action<string> _onPickKey;
        private Action _onClear;
        private Action<TroopDefinition> _onPickTroop;
        private HashSet<string> _usedKeys;
        private string _targetLabel = string.Empty;

        private readonly List<string> _keyResults = new List<string>();
        private readonly List<TroopDefinition> _troopResults = new List<TroopDefinition>();
        private string _search = string.Empty;
        private string _lastSearch = "\u0000";
        private bool _onlyUsed;
        private bool _lastOnlyUsed;
        private Vector2 _scroll;
        private float _rowHeight = 24f;
        private int _lastKind = -1;

        /// <summary>打开资源键选择器。</summary>
        public static LevelEditorPickerWindow Open(string fieldLabel, string targetLabel,
            StageResourceIndex.StageKeyKind kind, Action<string> onPick, Action onClear, HashSet<string> usedKeys)
        {
            var window = GetWindow<LevelEditorPickerWindow>(false, "资源键选择", true);
            window.minSize = new Vector2(320f, 220f);
            window.titleContent = new GUIContent(StageResourceIndex.KindLabel(kind) + "键选择");
            window._mode = Mode.Key;
            window._kind = kind;
            window._onPickKey = onPick;
            window._onClear = onClear;
            window._onPickTroop = null;
            window._usedKeys = usedKeys;
            window._targetLabel = (fieldLabel ?? string.Empty) +
                                  (string.IsNullOrEmpty(targetLabel) ? string.Empty : "　→　" + targetLabel);
            window._lastSearch = "\u0000";
            window._lastKind = -1;
            window.Show();
            window.Repaint();
            window.Focus();
            return window;
        }

        /// <summary>打开兵种选择器（用于敌方兵种白名单）。</summary>
        public static LevelEditorPickerWindow OpenTroop(string fieldLabel, string targetLabel,
            Action<TroopDefinition> onPick, Action onClear, HashSet<string> usedKeys)
        {
            var window = GetWindow<LevelEditorPickerWindow>(false, "兵种选择", true);
            window.minSize = new Vector2(360f, 240f);
            window.titleContent = new GUIContent("兵种选择");
            window._mode = Mode.Troop;
            window._onPickTroop = onPick;
            window._onPickKey = null;
            window._onClear = onClear;
            window._usedKeys = usedKeys;
            window._targetLabel = (fieldLabel ?? string.Empty) +
                                  (string.IsNullOrEmpty(targetLabel) ? string.Empty : "　→　" + targetLabel);
            window._lastSearch = "\u0000";
            window._lastKind = -1;
            window.Show();
            window.Repaint();
            window.Focus();
            return window;
        }

        /// <summary>目标已经换了（例如切换了关卡）就关掉，避免值填到不该改的字段上。</summary>
        public static void CloseIfOpen()
        {
            LevelEditorPickerWindow[] windows = Resources.FindObjectsOfTypeAll<LevelEditorPickerWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null)
                    windows[i].Close();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical();

            if (_mode == Mode.Troop)
            {
                EditorGUILayout.LabelField("兵种（共 " + StageResourceIndex.Instance.Troops.Count + " 个）", EditorStyles.boldLabel);
            }
            else
            {
                EditorGUILayout.LabelField(StageResourceIndex.KindLabel(_kind) + "资源键（共 " +
                    StageResourceIndex.Instance.KeysOf(_kind).Count + " 个）", EditorStyles.boldLabel);
            }

            if (!string.IsNullOrEmpty(_targetLabel))
                EditorGUILayout.LabelField("正在填：" + _targetLabel, LevelEditorUI.MiniLabel);

            _search = LevelEditorUI.SearchField(_search, _mode == Mode.Troop ? "输入关键字：兵种名 / Id / 预制体键" : "输入关键字，例如 Miner rat、Cat Troop UI_0 …");

            EditorGUILayout.BeginHorizontal();
            _onlyUsed = EditorGUILayout.ToggleLeft("只看已被别处用过的键", _onlyUsed, GUILayout.Width(180f));
            _rowHeight = EditorGUILayout.Slider("行高", _rowHeight, 20f, 72f);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("窗口可拉伸 / 最大化 / 停靠", LevelEditorUI.MiniLabel, GUILayout.Width(180f));
            EditorGUILayout.EndHorizontal();

            if (!string.Equals(_search, _lastSearch, StringComparison.Ordinal) ||
                _onlyUsed != _lastOnlyUsed || _lastKind != (int)_kind)
            {
                _lastSearch = _search;
                _lastOnlyUsed = _onlyUsed;
                _lastKind = (int)_kind;
                Rebuild();
            }

            int hitCount = _mode == Mode.Troop ? _troopResults.Count : _keyResults.Count;
            EditorGUILayout.LabelField("命中 " + hitCount + " 个" +
                (hitCount >= MaxResults ? "（已截断，请加关键字缩小范围）" : string.Empty), LevelEditorUI.MiniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));

            if (_mode == Mode.Troop)
                DrawTroopRows();
            else
                DrawKeyRows();

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
            EditorGUILayout.LabelField("点一行即填入（可连续点多个）", LevelEditorUI.MiniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void DrawKeyRows()
        {
            StageResourceIndex index = StageResourceIndex.Instance;

            for (int i = 0; i < _keyResults.Count; i++)
            {
                string key = _keyResults[i];
                Rect row = GUILayoutUtility.GetRect(0f, _rowHeight, GUILayout.ExpandWidth(true));
                bool hover = row.Contains(Event.current.mousePosition);

                if (hover)
                    LevelEditorUI.FillRect(row, new Color(LevelEditorUI.Accent.r, LevelEditorUI.Accent.g, LevelEditorUI.Accent.b, 0.25f));

                float thumb = Mathf.Min(_rowHeight - 2f, 40f);
                var thumbRect = new Rect(row.x + 2f, row.y + 1f, thumb, thumb);
                LevelEditorUI.DrawSpriteFit(thumbRect, index.GetSprite(key), Color.white, 1f);

                GUI.Label(new Rect(row.x + thumb + 7f, row.y + (row.height - 16f) * 0.5f, row.width - thumb - 12f, 16f),
                    key, EditorStyles.miniLabel);

                if (hover && Event.current.type == EventType.MouseDown)
                {
                    if (_onPickKey != null)
                        _onPickKey(key);
                    Repaint();
                }
            }

            if (_keyResults.Count == 0)
                EditorGUILayout.LabelField("没有匹配的键。", LevelEditorUI.MiniLabel);
        }

        private void DrawTroopRows()
        {
            StageResourceIndex index = StageResourceIndex.Instance;

            for (int i = 0; i < _troopResults.Count; i++)
            {
                TroopDefinition troop = _troopResults[i];
                if (troop == null)
                    continue;

                Rect row = GUILayoutUtility.GetRect(0f, _rowHeight, GUILayout.ExpandWidth(true));
                bool hover = row.Contains(Event.current.mousePosition);

                if (hover)
                    LevelEditorUI.FillRect(row, new Color(LevelEditorUI.Accent.r, LevelEditorUI.Accent.g, LevelEditorUI.Accent.b, 0.25f));

                float thumb = Mathf.Min(_rowHeight - 2f, 40f);
                var thumbRect = new Rect(row.x + 2f, row.y + 1f, thumb, thumb);
                LevelEditorUI.FillRect(thumbRect, new Color(0f, 0f, 0f, 0.2f));
                LevelEditorUI.DrawSpriteFit(thumbRect, index.GetSprite(troop.IconKey), Color.white, 1f);

                float x = row.x + thumb + 7f;

                GUI.Label(new Rect(x, row.y + 1f, row.width - thumb - 12f, 15f),
                    (string.IsNullOrEmpty(troop.DisplayName) ? troop.Id : troop.DisplayName) + "　" + troop.Id,
                    EditorStyles.miniBoldLabel);

                bool iconOk = index.HasSprite(troop.IconKey);
                GUI.contentColor = iconOk ? LevelEditorUI.Muted : LevelEditorUI.Error;
                GUI.Label(new Rect(x, row.y + 16f, row.width - thumb - 12f, 14f),
                    "阶级 " + troop.Tier + "　解锁需兵营 Lv" + troop.UnlockLevel + "　维护费 " + troop.Upkeep +
                    "　冷却 " + troop.Cooldown.ToString("0.#") + "s　产出 " + troop.TrainCount +
                    "　图标 " + (iconOk ? troop.IconKey : troop.IconKey + "（不在注册表）"),
                    EditorStyles.miniLabel);
                GUI.contentColor = Color.white;

                if (hover && Event.current.type == EventType.MouseDown)
                {
                    if (_onPickTroop != null)
                        _onPickTroop(troop);
                    Repaint();
                }
            }

            if (_troopResults.Count == 0)
                EditorGUILayout.LabelField("没有匹配的兵种。", LevelEditorUI.MiniLabel);
        }

        private void Rebuild()
        {
            if (_mode == Mode.Troop)
            {
                _troopResults.Clear();
                List<TroopDefinition> hits = StageResourceIndex.Instance.SearchTroops(_search, MaxResults);
                for (int i = 0; i < hits.Count && _troopResults.Count < MaxResults; i++)
                    _troopResults.Add(hits[i]);
                return;
            }

            _keyResults.Clear();
            List<string> keys = StageResourceIndex.Instance.SearchKeysOf(_kind, _search, MaxResults);
            for (int i = 0; i < keys.Count && _keyResults.Count < MaxResults; i++)
            {
                if (_onlyUsed && (_usedKeys == null || !_usedKeys.Contains(keys[i])))
                    continue;
                _keyResults.Add(keys[i]);
            }
        }
    }
}
