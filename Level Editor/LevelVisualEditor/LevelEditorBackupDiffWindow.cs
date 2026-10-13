using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CageFightStageTool
{
    /// <summary>
    /// 「与备份对比」：把**当前关卡资产**和某一份备份做逐行 diff，回答"上次到底改了什么"。
    ///
    /// 为什么需要它：备份一直是"能恢复、但看不了差异" —— 不知道昨天改了什么，回退只能整体退，
    /// 改坏了也没法精确定位是哪一项。这里做的是一次性的文本 diff（关卡资产是 YAML，逐行看本来就很直观）。
    ///
    /// 说明：
    ///   · 备份在工程之外（`Level Editor\StageBackups`），**不能当资产加载**，所以直接读文件文本对比；
    ///   · 相同行的长段落会折叠（首尾各留 3 行），避免几千行里只有几行差异却要滚屏；
    ///   · 本窗口**只读**，不会改任何文件。
    /// </summary>
    public sealed class LevelEditorBackupDiffWindow : EditorWindow
    {
        /// <summary>一行 diff：0 = 相同，1 = 备份里有（删掉），2 = 当前有（新增）。</summary>
        private struct DiffLine
        {
            public int Kind;
            public string Text;
        }

        /// <summary>两侧超过这个行数就不做 O(n²) 的 LCS（避免卡死），只报"整体不同"。</summary>
        private const int MaxDiffLines = 4000;
        private const int CollapseThreshold = 8;   // 连续相同行超过这么多就折叠
        private const int CollapseKeep = 3;        // 折叠时首尾各保留几行

        private string _assetPath;
        private string _assetName;
        private readonly List<string> _backups = new List<string>();
        private readonly List<DiffLine> _lines = new List<DiffLine>();
        private int _backupIndex;
        private Vector2 _scroll;
        private string _summary = string.Empty;
        private int _added;
        private int _removed;

        public static void Open(string assetPath)
        {
            var window = GetWindow<LevelEditorBackupDiffWindow>(false, "与备份对比", true);
            window.minSize = new Vector2(560f, 380f);
            window._assetPath = assetPath;
            window.Reload();
            window.Show();
        }

        private void Reload()
        {
            _backups.Clear();
            _lines.Clear();
            _summary = string.Empty;
            _added = _removed = 0;

            if (string.IsNullOrEmpty(_assetPath) || !File.Exists(_assetPath))
            {
                _summary = "当前没有打开关卡（或资产文件不存在）。";
                return;
            }

            _assetName = Path.GetFileNameWithoutExtension(_assetPath);

            // 只列"这个关卡"的备份：命名是 <资产名>_yyyyMMdd_HHmmss_<原因>.asset
            List<string> all = StageEditorData.ListBackups();
            for (int i = 0; i < all.Count; i++)
            {
                string fileName = Path.GetFileNameWithoutExtension(all[i]);
                if (fileName.StartsWith(_assetName + "_", StringComparison.OrdinalIgnoreCase))
                    _backups.Add(all[i]);
            }

            if (_backups.Count == 0)
            {
                _summary = "没有 " + _assetName + " 的备份（备份在保存 / 批量 / 一键修复 / 删除前自动生成）。";
                return;
            }

            if (_backupIndex < 0 || _backupIndex >= _backups.Count)
                _backupIndex = 0;

            ComputeDiff();
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUILayout.LabelField("备份", GUILayout.Width(34f));
            using (new EditorGUI.DisabledScope(_backups.Count == 0))
            {
                string[] labels = new string[_backups.Count];
                for (int i = 0; i < _backups.Count; i++)
                    labels[i] = Path.GetFileName(_backups[i]);

                int picked = EditorGUILayout.Popup(_backupIndex, labels, EditorStyles.toolbarPopup, GUILayout.Width(320f));
                if (picked != _backupIndex)
                {
                    _backupIndex = picked;
                    ComputeDiff();
                }
            }

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(48f)))
                Reload();

            GUILayout.FlexibleSpace();

            if (!string.IsNullOrEmpty(_summary))
                EditorGUILayout.LabelField(_summary, EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();

            if (_lines.Count == 0)
            {
                EditorGUILayout.Space(8f);
                EditorGUILayout.HelpBox(string.IsNullOrEmpty(_summary) ? "没有可对比的内容。" : _summary, MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(
                "左列 - 是备份里的内容，右列 + 是当前内容（顺序：备份 → 当前）。" +
                "「改了多少行」= 删除 " + _removed + " 行、新增 " + _added + " 行。",
                LevelEditorUI.MiniLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            DrawLines();
            EditorGUILayout.EndScrollView();
        }

        /// <summary>绘制 diff，并把连续相同行折叠成一行提示（否则差异只有几行也要滚很久）。</summary>
        private void DrawLines()
        {
            var sameStyle = new GUIStyle(EditorStyles.miniLabel);
            sameStyle.normal.textColor = LevelEditorUI.Muted;

            var addStyle = new GUIStyle(EditorStyles.miniLabel);
            addStyle.normal.textColor = LevelEditorUI.Ok;

            var removeStyle = new GUIStyle(EditorStyles.miniLabel);
            removeStyle.normal.textColor = LevelEditorUI.Error;

            int i = 0;
            while (i < _lines.Count)
            {
                if (_lines[i].Kind != 0)
                {
                    DrawLine(_lines[i], _lines[i].Kind == 2 ? addStyle : removeStyle);
                    i++;
                    continue;
                }

                // 统计这一段连续相同行
                int start = i;
                while (i < _lines.Count && _lines[i].Kind == 0)
                    i++;
                int runLength = i - start;

                if (runLength <= CollapseThreshold)
                {
                    for (int k = start; k < i; k++)
                        DrawLine(_lines[k], sameStyle);
                    continue;
                }

                for (int k = start; k < start + CollapseKeep; k++)
                    DrawLine(_lines[k], sameStyle);
                EditorGUILayout.LabelField("　　… 中间 " + (runLength - CollapseKeep * 2) + " 行相同 …", sameStyle);
                for (int k = i - CollapseKeep; k < i; k++)
                    DrawLine(_lines[k], sameStyle);
            }
        }

        private static void DrawLine(DiffLine line, GUIStyle style)
        {
            EditorGUILayout.LabelField(line.Text, style);
        }

        private void ComputeDiff()
        {
            _lines.Clear();
            _added = _removed = 0;

            if (_backupIndex < 0 || _backupIndex >= _backups.Count)
                return;

            string backupPath = _backups[_backupIndex];
            string[] oldLines;
            string[] newLines;
            try
            {
                oldLines = File.ReadAllLines(backupPath);
                newLines = File.ReadAllLines(_assetPath);
            }
            catch (Exception exception)
            {
                _summary = "读取失败：" + exception.Message;
                return;
            }

            if (oldLines.Length > MaxDiffLines || newLines.Length > MaxDiffLines)
            {
                _summary = "文件太大（备份 " + oldLines.Length + " 行 / 当前 " + newLines.Length +
                           " 行），跳过了逐行对比。";
                return;
            }

            Diff(oldLines, newLines, _lines);

            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i].Kind == 1) _removed++;
                else if (_lines[i].Kind == 2) _added++;
            }

            _summary = Path.GetFileName(backupPath) + " → 当前：删 " + _removed + " 行 / 增 " + _added + " 行";
        }

        /// <summary>经典 LCS 逐行 diff：a = 备份（旧），b = 当前（新）。</summary>
        private static void Diff(string[] a, string[] b, List<DiffLine> output)
        {
            int n = a.Length;
            int m = b.Length;

            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    lcs[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                        ? lcs[i + 1, j + 1] + 1
                        : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                }
            }

            int x = 0;
            int y = 0;
            while (x < n && y < m)
            {
                if (string.Equals(a[x], b[y], StringComparison.Ordinal))
                {
                    output.Add(new DiffLine { Kind = 0, Text = "  " + a[x] });
                    x++;
                    y++;
                }
                else if (lcs[x + 1, y] >= lcs[x, y + 1])
                {
                    output.Add(new DiffLine { Kind = 1, Text = "- " + a[x] });
                    x++;
                }
                else
                {
                    output.Add(new DiffLine { Kind = 2, Text = "+ " + b[y] });
                    y++;
                }
            }

            while (x < n)
                output.Add(new DiffLine { Kind = 1, Text = "- " + a[x++] });
            while (y < m)
                output.Add(new DiffLine { Kind = 2, Text = "+ " + b[y++] });
        }
    }
}
