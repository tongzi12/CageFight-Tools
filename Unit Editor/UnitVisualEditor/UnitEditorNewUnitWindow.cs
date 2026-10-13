using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CageFightUnitTool
{
    /// <summary>
    /// 「新建兵种」向导：从现有兵种复制出一个新的**单位预制体**，并把训练配置、资源键、
    /// 兵营挂载一次接好（兵种编辑器的一条龙入口）。
    ///
    /// 设计上有意保留的一个按钮是 **「试运行」**：先算出"会写哪些文件、会动哪些已有资产、
    /// 建完还差什么"，确认无误再点「新建」。造预制体是不可逆操作里最重的一类，
    /// 让人先看到计划比事后道歉便宜。
    /// </summary>
    public sealed class UnitEditorNewUnitWindow : EditorWindow
    {
        private UnitCatalog _catalog;
        private UnitRecord _template;
        private UnitFactoryPlan _plan;
        private string _report = string.Empty;

        private string _race = string.Empty;
        private string _prefabName = string.Empty;
        private string _displayName = string.Empty;
        private string _trainingId = string.Empty;
        private int _unlockLevel = 1;
        private bool _darkBarracks;
        private bool _copyStats = true;
        private bool _reuseArt = true;
        private int _tag;

        private string _spriteKey = string.Empty;
        private string _animatorKey = string.Empty;
        private string _iconKey = string.Empty;
        private string _bulletKey = string.Empty;
        private string _audioKey = string.Empty;

        private int _cooldown = 5;
        private int _trainCount = 1;
        private int _upkeep;
        private float _threatScore = 1f;

        private bool _createDefinition = true;
        private bool _registerKey = true;
        private bool _attachBarracks = true;

        private Vector2 _scroll;
        private string[] _raceOptions = new string[0];
        private string[] _templateOptions = new string[0];
        private List<UnitRecord> _templateRecords = new List<UnitRecord>();
        private int _raceIndex;
        private int _templateIndex = -1;

        /// <summary>点「新建」之后回调主窗口（重扫 + 选中新兵种）。</summary>
        private Action<string> _onCreated;

        public static void Open(UnitCatalog catalog, UnitRecord template, Action<string> onCreated)
        {
            var window = GetWindow<UnitEditorNewUnitWindow>(true, "新建兵种", true);
            window.minSize = new Vector2(560f, 640f);
            window.titleContent = new GUIContent("新建兵种");
            window._catalog = catalog;
            window._onCreated = onCreated;
            window._plan = null;
            window._report = string.Empty;
            window.BuildRaceOptions();

            if (template != null)
            {
                window._race = template.Race;
                window.SelectTemplate(template.PrefabName);
            }

            window.Show();
        }

        private void BuildRaceOptions()
        {
            var races = new List<string>();
            if (_catalog != null)
            {
                for (int i = 0; i < _catalog.Races.Count; i++)
                {
                    string race = _catalog.Races[i];
                    if (!string.IsNullOrEmpty(race))
                        races.Add(race);
                }
            }

            if (races.Count == 0)
                races.Add("Cat");

            _raceOptions = races.ToArray();
            _raceIndex = Mathf.Max(0, Array.IndexOf(_raceOptions, _race));
            if (_raceIndex >= 0 && _raceIndex < _raceOptions.Length)
                _race = _raceOptions[_raceIndex];

            BuildTemplateOptions();
        }

        private void BuildTemplateOptions()
        {
            _templateRecords.Clear();
            if (_catalog != null)
            {
                for (int i = 0; i < _catalog.Count; i++)
                {
                    UnitRecord record = _catalog.Records[i];
                    if (record == null || !record.HasPrefab)
                        continue;
                    if (!string.IsNullOrEmpty(_race) &&
                        !string.Equals(record.Race, _race, StringComparison.OrdinalIgnoreCase))
                        continue;

                    _templateRecords.Add(record);
                }
            }

            _templateRecords.Sort((a, b) => string.CompareOrdinal(a.PrefabName, b.PrefabName));

            var names = new List<string>();
            for (int i = 0; i < _templateRecords.Count; i++)
                names.Add(_templateRecords[i].PrefabName);
            if (names.Count == 0)
                names.Add("（这个种族没有可用模板）");

            _templateOptions = names.ToArray();
            _templateIndex = _templateRecords.Count > 0 ? 0 : -1;
            if (_templateRecords.Count > 0)
                _template = _templateRecords[0];
        }

        private void SelectTemplate(string prefabName)
        {
            for (int i = 0; i < _templateRecords.Count; i++)
            {
                if (string.Equals(_templateRecords[i].PrefabName, prefabName, StringComparison.OrdinalIgnoreCase))
                {
                    _templateIndex = i;
                    _template = _templateRecords[i];
                    return;
                }
            }
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "从现有兵种**复制**出一个新的单位预制体（16 个组件、4 个共享行为子预制体、ShootPoint 一起带过来），\n" +
                "然后把训练配置、资源键、兵营挂载一次接好。\n\n" +
                "· 资源键 = **根节点名字**，所以复制出来的那份会按你填的名字改名（不改名会跟模板撞键）\n" +
                "· 战斗数值默认沿用模板，建好之后在兵种编辑器里调\n" +
                "· 随时可以点「试运行」先看会写哪些文件", MessageType.Info);

            DrawSourceSection();
            DrawIdentitySection();
            DrawArtSection();
            DrawTrainingSection();
            DrawWiringSection();

            EditorGUILayout.Space(6f);
            DrawPlanSection();

            EditorGUILayout.EndScrollView();
        }

        #region 各段
        private void DrawSourceSection()
        {
            EditorGUILayout.LabelField("① 从哪个兵种复制", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("种族", GUILayout.Width(64f));
            int race = EditorGUILayout.Popup(_raceIndex, _raceOptions);
            if (race != _raceIndex)
            {
                _raceIndex = race;
                _race = _raceOptions[Mathf.Clamp(_raceIndex, 0, _raceOptions.Length - 1)];
                BuildTemplateOptions();
                _plan = null;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("模板兵种", GUILayout.Width(64f));
            int index = EditorGUILayout.Popup(_templateIndex, _templateOptions);
            if (index != _templateIndex && index >= 0 && index < _templateRecords.Count)
            {
                _templateIndex = index;
                _template = _templateRecords[index];
                _plan = null;
            }
            EditorGUILayout.EndHorizontal();

            if (_template != null)
            {
                EditorGUILayout.LabelField("　　" + _template.PrefabPath, EditorGUIUtility.isProSkin
                    ? EditorStyles.miniLabel : EditorStyles.miniLabel);

                UnitStats stats = UnitStatsReader.Read(_template.PrefabPath);
                if (stats != null && stats.Valid)
                {
                    EditorGUILayout.LabelField(
                        "　　HP " + stats.MaxHp + "　攻击 " + stats.Atk + "　射程 " + stats.AtkRange +
                        "　移速 " + stats.MoveSpeed + "　贴图 " + (string.IsNullOrEmpty(stats.SpriteKey) ? "(空)" : stats.SpriteKey),
                        EditorStyles.miniLabel);
                }
            }
        }

        private void DrawIdentitySection()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("② 叫什么", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("名字", GUILayout.Width(64f));
            string name = EditorGUILayout.TextField(_prefabName);
            if (name != _prefabName)
            {
                _prefabName = name;
                _plan = null;
                if (string.IsNullOrEmpty(_displayName))
                    _displayName = name;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("　　预制体文件名 + 资源键 + 默认训练 ID 都取这个名字；建议英文或拼音（现有 id 都是英文）",
                EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("显示名", GUILayout.Width(64f));
            _displayName = EditorGUILayout.TextField(_displayName);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("训练 ID", GUILayout.Width(64f));
            string id = EditorGUILayout.TextField(_trainingId);
            if (id != _trainingId)
            {
                _trainingId = id;
                _plan = null;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("　　留空 = 按名字生成；重名会追加 _2、_3（与「批量补全训练 ID」同一套规则）",
                EditorStyles.miniLabel);
        }

        private void DrawArtSection()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("③ 美术 Key", EditorStyles.boldLabel);

            bool reuse = EditorGUILayout.ToggleLeft("沿用模板的贴图 / 动画 / 子弹 / 语音（同一个美术换个数值——最常见）",
                _reuseArt);
            if (reuse != _reuseArt)
            {
                _reuseArt = reuse;
                _plan = null;
            }

            if (_reuseArt)
                return;

            EditorGUI.indentLevel++;
            _spriteKey = KeyRow("基础贴图", _spriteKey, UnitResKind.Sprite, v => _spriteKey = v);
            _animatorKey = KeyRow("动画控制器", _animatorKey, UnitResKind.AnimatorController, v => _animatorKey = v);
            _iconKey = KeyRow("兵营图标", _iconKey, UnitResKind.Sprite, v => _iconKey = v);
            _bulletKey = KeyRow("子弹（远程才要）", _bulletKey, UnitResKind.Prefab, v => _bulletKey = v);
            _audioKey = KeyRow("语音", _audioKey, UnitResKind.Audio, v => _audioKey = v);
            EditorGUI.indentLevel--;

            EditorGUILayout.LabelField("　　留空 = 这一项不写（贴图/动画留空角色不会显示、不会动；子弹留空打不出子弹）",
                EditorStyles.miniLabel);
        }

        private string KeyRow(string label, string current, UnitResKind kind, Action<string> setter)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(96f));
            string value = EditorGUILayout.TextField(current);
            if (value != current)
            {
                setter(value);
                _plan = null;
            }

            if (GUILayout.Button("选", EditorStyles.miniButton, GUILayout.Width(26f)))
            {
                UnitKeyPickerWindow.Open(label, _prefabName, kind,
                    key => { setter(key); _plan = null; Repaint(); },
                    () => { setter(string.Empty); _plan = null; Repaint(); },
                    null, value);
            }
            EditorGUILayout.EndHorizontal();
            return value;
        }

        private void DrawTrainingSection()
        {
            if (!_createDefinition)
                return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("④ 训练规则", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("兵营", GUILayout.Width(64f));
            _darkBarracks = EditorGUILayout.ToggleLeft("黑暗兵营（不勾 = 普通兵营）", _darkBarracks, GUILayout.Width(200f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("阶级", GUILayout.Width(64f));
            _unlockLevel = EditorGUILayout.IntSlider(_unlockLevel, 1, 3);
            EditorGUILayout.LabelField("阶（= 解锁等级，兵营面板三排）", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("冷却", GUILayout.Width(64f));
            _cooldown = EditorGUILayout.IntField(_cooldown, GUILayout.Width(70f));
            EditorGUILayout.LabelField("秒", EditorStyles.miniLabel, GUILayout.Width(20f));
            EditorGUILayout.LabelField("单次产出", GUILayout.Width(64f));
            _trainCount = EditorGUILayout.IntField(_trainCount, GUILayout.Width(50f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("维护费", GUILayout.Width(64f));
            _upkeep = EditorGUILayout.IntField(_upkeep, GUILayout.Width(70f));
            EditorGUILayout.LabelField("威胁权重", GUILayout.Width(64f));
            _threatScore = EditorGUILayout.FloatField(_threatScore, GUILayout.Width(50f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("兵种标签", GUILayout.Width(64f));
            string[] tagNames = Enum.GetNames(typeof(TroopTag));
            _tag = EditorGUILayout.Popup(_tag, tagNames, GUILayout.Width(140f));
            EditorGUILayout.LabelField("出生时会注入到单位上（AI 威胁判定会读）", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawWiringSection()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("⑤ 接哪几条线", EditorStyles.boldLabel);

            KeyValueToggle(ref _createDefinition, "建训练配置（TroopDefinition）");
            KeyValueToggle(ref _registerKey, "登记 PrefabRegistry（资源键 → 新预制体）");
            KeyValueToggle(ref _attachBarracks, "挂进兵营的 BuildingTraining.troops（玩家才训练得到）");
            if (!_createDefinition && _attachBarracks)
            {
                _attachBarracks = false;
                EditorGUILayout.HelpBox("没有训练配置就挂不了兵营（兵营里挂的就是 TroopDefinition），已自动关掉。", MessageType.Info);
            }

            EditorGUILayout.LabelField("　　Addressables 分组会自动加（找「Remote Prefab <种族>」）；关卡预载与图鉴另外处理，试运行里会列出来。",
                EditorStyles.miniLabel);
        }

        private static void KeyValueToggle(ref bool value, string label)
        {
            value = EditorGUILayout.ToggleLeft(label, value);
        }

        private void DrawPlanSection()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("⑥ 试运行 / 执行", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("试运行（只算计划，不写盘）", GUILayout.Width(190f)))
            {
                _plan = UnitFactoryRunner.Plan(_catalog, BuildRequest());
                _report = string.Empty;
                Repaint();
            }

            using (new EditorGUI.DisabledScope(_plan == null || !_plan.CanRun))
            {
                GUI.backgroundColor = new Color(1f, 0.85f, 0.5f);
                if (GUILayout.Button("新建（会写盘）", GUILayout.Width(120f)))
                    Execute();
                GUI.backgroundColor = Color.white;
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (_plan == null)
            {
                EditorGUILayout.LabelField("还没试运行。", EditorStyles.miniLabel);
                return;
            }

            DrawList("拦人的问题", _plan.Errors, new Color(1f, 0.5f, 0.5f));
            DrawList("提醒", _plan.Warnings, new Color(1f, 0.8f, 0.4f));
            DrawList("会做这些事", _plan.Steps, Color.white);
            DrawList("建完还差什么", _plan.Todo, new Color(0.7f, 0.8f, 1f));

            if (!string.IsNullOrEmpty(_report))
            {
                EditorGUILayout.Space(4f);
                EditorGUILayout.LabelField("执行结果", EditorStyles.boldLabel);
                EditorGUILayout.TextArea(_report, EditorStyles.textArea, GUILayout.MinHeight(90f));
            }
        }

        private static void DrawList(string title, List<string> items, Color color)
        {
            if (items == null || items.Count == 0)
                return;

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField(title + "（" + items.Count + "）", EditorStyles.boldLabel);

            Color old = GUI.contentColor;
            GUI.contentColor = color;
            for (int i = 0; i < items.Count; i++)
                EditorGUILayout.LabelField("　· " + items[i], EditorStyles.wordWrappedMiniLabel);
            GUI.contentColor = old;
        }
        #endregion

        private UnitFactoryRequest BuildRequest()
        {
            return new UnitFactoryRequest
            {
                Race = _race,
                TemplatePrefabName = _template != null ? _template.PrefabName : string.Empty,
                TemplatePrefabPath = _template != null ? _template.PrefabPath : string.Empty,
                NewPrefabName = _prefabName,
                DisplayName = _displayName,
                TrainingId = _trainingId,
                UnlockLevel = _unlockLevel,
                DarkBarracks = _darkBarracks,
                CopyTemplateStats = _copyStats,
                ReuseTemplateArtKeys = _reuseArt,
                SpriteKey = _spriteKey,
                AnimatorControllerKey = _animatorKey,
                IconKey = _iconKey,
                BulletKey = _bulletKey,
                AudioKey = _audioKey,
                Tag = _tag,
                Cooldown = _cooldown,
                TrainCount = _trainCount,
                Upkeep = _upkeep,
                ThreatScore = _threatScore,
                CreateDefinition = _createDefinition,
                RegisterPrefabKey = _registerKey,
                AttachToBarracks = _attachBarracks
            };
        }

        private void Execute()
        {
            UnitFactoryPlan plan;
            string report = UnitFactoryRunner.Run(_catalog, BuildRequest(), out plan);
            _plan = plan;
            _report = report;

            string createdPath = plan != null ? plan.PrefabPath : null;
            bool ok = report != null && report.Contains("√ 预制体");

            EditorUtility.DisplayDialog(ok ? "新建兵种完成" : "新建兵种：有步骤没成功",
                report, "知道了");

            if (ok && _onCreated != null && !string.IsNullOrEmpty(createdPath))
                _onCreated(createdPath);

            Repaint();
        }
    }
}
