using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightEngineerTool
{
    /// <summary>
    /// 资源键选择窗口：列出某一种注册表里的全部键，支持搜索与预览，点一下就把键回填给调用方。
    /// 与图鉴/法术/兵种/建筑编辑器里的同名窗口是同一套做法（避免手输键导致的静默失效）。
    /// </summary>
    public sealed class EngineerKeyPickerWindow : EditorWindow
    {
        private EngineerResKind _kind;
        private Action<string> _onPick;
        private string _search = string.Empty;
        private Vector2 _scroll;
        private List<string> _keys = new List<string>();

        public static void Open(EngineerResKind kind, string currentKey, Action<string> onPick)
        {
            var window = GetWindow<EngineerKeyPickerWindow>(false, EngineerResourceIndex.KindName(kind) + " 键选择", true);
            window.minSize = new Vector2(380f, 420f);
            window._kind = kind;
            window._onPick = onPick;
            window._search = currentKey ?? string.Empty;
            window.Rebuild();
            window.Show();
        }

        private void Rebuild()
        {
            _keys = new List<string>(EngineerResourceIndex.Instance.Keys(_kind));
        }

        private void OnGUI()
        {
            EngineerResourceIndex index = EngineerResourceIndex.Instance;

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _search = GUILayout.TextField(_search ?? string.Empty, EditorStyles.toolbarSearchField ?? EditorStyles.textField, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("重新扫描", EditorStyles.toolbarButton, GUILayout.Width(64f)))
            {
                EngineerResourceIndex.Invalidate();
                Rebuild();
            }
            EditorGUILayout.EndHorizontal();

            if (!index.RegistryFound(_kind))
            {
                EditorGUILayout.HelpBox("没读到注册表资产：" + EngineerResourceIndex.RegistryPathOf(_kind),
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.LabelField(index.Keys(_kind).Count + " 个键，显示 " + VisibleCount() + " 个",
                EditorStyles.miniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _keys.Count; i++)
            {
                string key = _keys[i];
                if (!string.IsNullOrEmpty(_search) &&
                    key.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                EditorGUILayout.BeginHorizontal();

                var icon = new GUIContent(key);
                bool picked = GUILayout.Button(icon, EditorStyles.miniButton, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("用这个", EditorStyles.miniButton, GUILayout.Width(56f)))
                    picked = true;

                if (picked && _onPick != null)
                {
                    _onPick(key);
                    Close();
                    GUIUtility.ExitGUI();
                }

                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("注册表：" + EngineerResourceIndex.RegistryPathOf(_kind), EditorStyles.miniLabel);
            if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(48f)))
                Ping(EngineerResourceIndex.RegistryPathOf(_kind));
            EditorGUILayout.EndHorizontal();
        }

        private int VisibleCount()
        {
            if (string.IsNullOrEmpty(_search))
                return _keys.Count;

            int count = 0;
            for (int i = 0; i < _keys.Count; i++)
            {
                if (_keys[i].IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                    count++;
            }
            return count;
        }

        private static void Ping(string assetPath)
        {
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
            if (asset == null)
                return;

            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }
    }
}
