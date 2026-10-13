using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightDialogueTool
{
    /// <summary>
    /// 资源 Key 选择窗口（立绘 / 语音）。
    ///
    /// 只列**注册表里真的有的 Key** —— 手打一个不存在的 Key 运行时必然出问题，
    /// 所以这边干脆只让人从清单里挑，顺手还能看到图/听到声。
    /// </summary>
    public sealed class DialogueEditorPickerWindow : EditorWindow
    {
        public enum Kind
        {
            Sprite,
            Audio
        }

        private const float ThumbSize = 64f;
        private const float RowHeight = 22f;

        private Kind _kind;
        private Action<string> _onPick;
        private Action _onClear;
        private string _caption;
        private string _search = string.Empty;
        private Vector2 _scroll;
        private List<string> _keys = new List<string>();
        private AudioSource _preview;
        private static GUIStyle _centeredUpper;

        private static GUIStyle CenteredUpper
        {
            get
            {
                if (_centeredUpper == null)
                {
                    _centeredUpper = new GUIStyle(DialogueEditorUI.MiniLabel);
                    _centeredUpper.alignment = TextAnchor.UpperCenter;
                }
                return _centeredUpper;
            }
        }

        public static void Open(string title, Kind kind, string currentKey, Action<string> onPick, Action onClear)
        {
            var window = GetWindow<DialogueEditorPickerWindow>(true, title, true);
            window.minSize = new Vector2(360f, 420f);
            window.titleContent = new GUIContent(title);
            window._kind = kind;
            window._onPick = onPick;
            window._onClear = onClear;
            window._caption = currentKey;
            window._search = string.Empty;
            window.Rebuild();
            window.Show();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4f);

            EditorGUILayout.BeginHorizontal();
            _search = DialogueEditorUI.SearchField(_search, "搜索 Key", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("刷新", EditorStyles.miniButton, GUILayout.Width(44f)))
                Rebuild();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(_kind == Kind.Sprite ? "SpriteRegistry 里的立绘 Key" : "AudioRegistry 里的语音 Key",
                DialogueEditorUI.MiniLabel);

            string current = DialogueCore.Clean(_caption);
            if (current.Length > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("当前：" + current, DialogueEditorUI.MiniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("清空", EditorStyles.miniButton, GUILayout.Width(48f)))
                {
                    _onClear?.Invoke();
                    Close();
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(2f);

            List<string> shown = Filtered();
            if (shown.Count == 0)
            {
                EditorGUILayout.HelpBox(_keys.Count == 0
                    ? "注册表里一个 Key 都没有（读不到注册表资产？）"
                    : "没有匹配的 Key。", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            if (_kind == Kind.Sprite)
                DrawSpriteGrid(shown);
            else
                DrawList(shown);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.LabelField("共 " + shown.Count + " / " + _keys.Count + " 个 Key", DialogueEditorUI.MiniLabel);
        }

        /// <summary>立绘按图挑（看名字挑图太费眼）：每格一张缩略图 + Key 名。</summary>
        private void DrawSpriteGrid(List<string> shown)
        {
            const float cell = 84f;
            const float thumb = 64f;

            float width = position.width - 24f;
            int columns = Mathf.Max(1, Mathf.FloorToInt(width / cell));
            float cellWidth = width / columns;

            int row = 0;
            while (row * columns < shown.Count)
            {
                Rect line = GUILayoutUtility.GetRect(0f, cell, GUILayout.ExpandWidth(true));

                for (int c = 0; c < columns; c++)
                {
                    int index = row * columns + c;
                    if (index >= shown.Count)
                        break;

                    string key = shown[index];
                    Rect box = new Rect(line.x + c * cellWidth + 4f, line.y + 2f, cellWidth - 8f, cell);

                    bool selected = string.Equals(key, DialogueCore.Clean(_caption), StringComparison.Ordinal);
                    if (selected)
                        DialogueEditorUI.FillRect(box, DialogueEditorUI.Selected);

                    Rect thumbRect = new Rect(box.x + (box.width - thumb) * 0.5f, box.y + 2f, thumb, thumb);
                    Sprite sprite = DialogueTool.GetSprite(key);
                    if (sprite != null)
                    {
                        DialogueEditorUI.FillRect(thumbRect, DialogueEditorUI.AvatarBg);
                        DialogueEditorUI.DrawSpriteFit(thumbRect, sprite, Color.white);
                    }
                    else
                    {
                        DialogueEditorUI.DrawEmptySlot(thumbRect, "取不到图");
                    }

                    GUIStyle centered = CenteredUpper;
                    GUI.Label(new Rect(box.x, thumbRect.yMax + 1f, box.width, 16f),
                        DialogueCore.OneLine(key, 12), centered);

                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                        thumbRect.Contains(Event.current.mousePosition))
                    {
                        Event.current.Use();
                        _onPick?.Invoke(key);
                        Close();
                        return;
                    }
                }

                row++;
            }
        }

        /// <summary>语音用列表（听，不看）。</summary>
        private void DrawList(List<string> shown)
        {
            for (int i = 0; i < shown.Count; i++)
                DrawRow(shown[i]);
        }

        private List<string> Filtered()
        {
            var result = new List<string>();
            string q = DialogueCore.Clean(_search);

            for (int i = 0; i < _keys.Count; i++)
            {
                if (q.Length == 0 || _keys[i].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(_keys[i]);
            }

            return result;
        }

        private void DrawRow(string key)
        {
            Rect rect = GUILayoutUtility.GetRect(0f, RowHeight, GUILayout.ExpandWidth(true));
            bool hover = rect.Contains(Event.current.mousePosition);

            if (string.Equals(key, DialogueCore.Clean(_caption), StringComparison.Ordinal))
                DialogueEditorUI.FillRect(rect, DialogueEditorUI.Selected);
            else if (hover)
                DialogueEditorUI.FillRect(rect, DialogueEditorUI.RowEven);

            GUI.Label(new Rect(rect.x + 6f, rect.y + 3f, rect.width - 12f, 16f), key, DialogueEditorUI.MiniLabel);

            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
            {
                Event.current.Use();
                _onPick?.Invoke(key);

                if (_kind == Kind.Audio)
                    PreviewAudio(key);
                else
                    Close();
            }

            if (hover)
                Repaint();
        }

        private void PreviewAudio(string key)
        {
            AudioClip clip = DialogueTool.GetAudio(key);
            if (clip == null)
                return;

            if (_preview == null)
            {
                var go = new GameObject("DialogueToolPreview") { hideFlags = HideFlags.HideAndDontSave };
                _preview = go.AddComponent<AudioSource>();
            }

            _preview.Stop();
            _preview.clip = clip;
            _preview.Play();
        }

        private void OnDestroy()
        {
            if (_preview != null)
                DestroyImmediate(_preview.gameObject);
        }

        private void Rebuild()
        {
            _keys = new List<string>(_kind == Kind.Sprite ? DialogueTool.SpriteKeys : DialogueTool.AudioKeys);
            _keys.Sort(StringComparer.OrdinalIgnoreCase);
            Repaint();
        }
    }
}
