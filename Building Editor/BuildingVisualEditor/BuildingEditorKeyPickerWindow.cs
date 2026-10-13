using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightBuildingTool
{
    /// <summary>
    /// 资源键选择窗口（独立可停靠窗口）。
    ///
    /// 建筑身上是**混用**的：每级外观图是强引用 Sprite（不用选键），
    /// 而施工/升级/拆除/音效/面板/弹幕都是资源键 —— 这个窗口就是给后者用的。
    ///
    /// 相比弹窗的好处：可以像普通 Unity 窗口一样拖边缘缩放、最大化、停靠，
    /// 而且选完一个不会自动关闭，可以连续给多个字段选。行高可拖，方便看清缩略图。
    /// </summary>
    public sealed class BuildingKeyPickerWindow : EditorWindow
    {
        public enum Kind
        {
            Prefab,
            Sprite,
            Audio
        }

        private const int MaxResults = 600;

        private Kind _kind = Kind.Prefab;
        private Action<string> _onPick;
        private Action _onClear;
        private string _targetLabel = string.Empty;

        private readonly List<string> _results = new List<string>();
        private string _search = string.Empty;
        private string _lastSearch = "\u0000";
        private int _lastKind = -1;
        private Vector2 _scroll;
        private float _rowHeight = 24f;

        public static BuildingKeyPickerWindow Open(string fieldLabel, string targetLabel, Kind kind,
            Action<string> onPick, Action onClear)
        {
            var window = GetWindow<BuildingKeyPickerWindow>(false, "资源键选择", true);
            window.minSize = new Vector2(320f, 220f);
            window.titleContent = new GUIContent(KindLabel(kind) + "键选择");
            window._kind = kind;
            window._onPick = onPick;
            window._onClear = onClear;
            window._targetLabel = (fieldLabel ?? string.Empty) +
                                  (string.IsNullOrEmpty(targetLabel) ? string.Empty : "　→　" + targetLabel);
            window._lastSearch = "\u0000";
            window._lastKind = -1;
            window.Show();
            window.Repaint();
            window.Focus();
            return window;
        }

        /// <summary>目标已经换了（例如切换了建筑）就关掉，避免值填到不该改的字段上。</summary>
        public static void CloseIfOpen()
        {
            BuildingKeyPickerWindow[] windows = Resources.FindObjectsOfTypeAll<BuildingKeyPickerWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null)
                    windows[i].Close();
            }
        }

        private static string KindLabel(Kind kind)
        {
            switch (kind)
            {
                case Kind.Sprite: return "Sprite";
                case Kind.Audio: return "音频";
                default: return "预制体";
            }
        }

        private int TotalCount
        {
            get
            {
                BuildingResourceIndex index = BuildingResourceIndex.Instance;
                switch (_kind)
                {
                    case Kind.Sprite: return index.SpriteKeyCount;
                    case Kind.Audio: return index.AudioKeyCount;
                    default: return index.PrefabKeys.Count;
                }
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginVertical();

            EditorGUILayout.LabelField(KindLabel(_kind) + "资源键（共 " + TotalCount + " 个）", EditorStyles.boldLabel);

            if (!string.IsNullOrEmpty(_targetLabel))
                EditorGUILayout.LabelField("正在填：" + _targetLabel, BuildingEditorUI.MiniLabel);

            _search = BuildingEditorUI.SearchField(_search,
                _kind == Kind.Prefab ? "输入关键字，比如 Barracks、Bullet、UPanime …" : "输入关键字，比如 Building、Arrow …");

            EditorGUILayout.BeginHorizontal();
            if (_kind == Kind.Audio)
                GUILayout.Space(4f);
            else
                _rowHeight = EditorGUILayout.Slider("行高", _rowHeight, 20f, 72f);

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("窗口可拉伸 / 最大化 / 停靠", BuildingEditorUI.MiniLabel, GUILayout.Width(180f));
            EditorGUILayout.EndHorizontal();

            if (!string.Equals(_search, _lastSearch, StringComparison.Ordinal) || _lastKind != (int)_kind)
            {
                _lastSearch = _search;
                _lastKind = (int)_kind;
                Rebuild();
            }

            EditorGUILayout.LabelField("命中 " + _results.Count + " 个" +
                (_results.Count >= MaxResults ? "（已截断，请加关键字缩小范围）" : string.Empty), BuildingEditorUI.MiniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            // 只有落在可视区里的行才去要缩略图：`AssetPreview.GetAssetPreview` 在预览生成期间
            // 每帧都会被重新请求一次，600 行全请求就是几百次调用 —— 选键窗口卡就是这么来的。
            float viewTop = _scroll.y;
            float viewBottom = _scroll.y + position.height - 60f;

            BuildingResourceIndex index = BuildingResourceIndex.Instance;

            for (int i = 0; i < _results.Count; i++)
            {
                string key = _results[i];
                Rect row = GUILayoutUtility.GetRect(0f, _rowHeight, GUILayout.ExpandWidth(true));
                bool hover = row.Contains(Event.current.mousePosition);

                if (hover)
                    BuildingEditorUI.FillRect(row, new Color(BuildingEditorUI.Accent.r, BuildingEditorUI.Accent.g, BuildingEditorUI.Accent.b, 0.25f));

                float thumb = Mathf.Min(_rowHeight - 2f, 40f);
                var thumbRect = new Rect(row.x + 2f, row.y + 1f, thumb, thumb);

                switch (_kind)
                {
                    case Kind.Sprite:
                        BuildingEditorUI.FillRect(thumbRect, new Color(0f, 0f, 0f, 0.2f));
                        BuildingEditorUI.DrawSpriteFit(thumbRect, index.GetSprite(key), Color.white, 1f);
                        break;

                    case Kind.Audio:
                        BuildingEditorUI.FillRect(thumbRect, new Color(BuildingEditorUI.Muted.r, BuildingEditorUI.Muted.g, BuildingEditorUI.Muted.b, 0.2f));
                        GUI.Label(thumbRect, "♪", BuildingEditorUI.CenteredMini);
                        break;

                    default:
                        BuildingEditorUI.FillRect(thumbRect, new Color(0f, 0f, 0f, 0.2f));
                        if (row.yMax > viewTop && row.y < viewBottom)
                        {
                            Texture preview = BuildingEditorUI.GetPrefabThumbnail(index.GetPrefab(key));
                            if (preview != null)
                                GUI.DrawTexture(new Rect(thumbRect.x + 1f, thumbRect.y + 1f, thumbRect.width - 2f, thumbRect.height - 2f),
                                    preview, ScaleMode.ScaleToFit);
                        }
                        break;
                }

                GUI.Label(new Rect(row.x + thumb + 7f, row.y + (row.height - 16f) * 0.5f, row.width - thumb - 12f, 16f),
                    key, EditorStyles.miniLabel);

                if (hover && Event.current.type == EventType.MouseDown)
                {
                    if (_onPick != null)
                        _onPick(key);
                    Repaint();
                }
            }

            if (_results.Count == 0)
                EditorGUILayout.LabelField("没有匹配的键。", BuildingEditorUI.MiniLabel);

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
            EditorGUILayout.LabelField("点一行即填入（可连续点多个）", BuildingEditorUI.MiniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        private void Rebuild()
        {
            _results.Clear();
            BuildingResourceIndex index = BuildingResourceIndex.Instance;

            List<string> hits;
            switch (_kind)
            {
                case Kind.Sprite: hits = index.SearchSprites(_search, MaxResults); break;
                case Kind.Audio: hits = index.SearchAudios(_search, MaxResults); break;
                default: hits = index.SearchPrefabs(_search, MaxResults); break;
            }

            for (int i = 0; i < hits.Count && _results.Count < MaxResults; i++)
                _results.Add(hits[i]);
        }
    }
}
