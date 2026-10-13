# 对话可视化编辑器（Dialogue Visual Editor）

一个窗口、两页，对应游戏里**两套彼此无关**的对话系统：

| 页 | 管什么 | 数据在哪 | 谁在播 |
|---|---|---|---|
| **局外 · 选关界面** | 玩家在**选关界面点某一关**时弹出的「导游台词」（头像 + 文本 + 语音） | 每关 `StageConfig.guideDialogues`（`List<GuideDialogueEntry>`，3 个字段：`avatarSpriteKey` / `text` / `audioKey`） | `Assets/Runtime/UISystem/GameStart/GuideDialoguePlayer.cs` |
| **局内 · 对战文本** | 对战中弹出的对话框，点一下推进一句 | `DialogueConfigSO`（一条：立绘 Key + 文本）与 `DialogueSeriesSO`（按顺序引用多条） | `Assets/Runtime/TextSystem/DialogueManager.cs` + `DialogueView.cs` |

> 这两套**不是一回事**，别混：局外那套是"选关时的导游语音台词"，跟关卡内容无关；
> 局内那套才是对战中会暂停游戏、点一下一句的对话框。工具把它们分两页，就是怕改错地方。

**工具只改上面这三个资产**（`StageN.asset` 的 `guideDialogues` 字段、`DialogueConfigSO`、`DialogueSeriesSO`），
**游戏源码一行不改**，也不动场景 / 预制体。

---

## 一、怎么打开

| 方式 | 操作 |
|---|---|
| 菜单 | Unity 顶部菜单 **工具 → 对话可视化编辑器**（快捷键 `Ctrl + Alt + D`） |
| 切页 | 窗口工具栏最左边的「**局外 · 选关界面** ｜ **局内 · 对战文本**」 |

窗口布局：

```
┌──────────────────────── 工具栏 ────────────────────────┐
│ [局外·选关界面 | 局内·对战文本]  重新扫描 保存 撤销 预览 体检   N 错/N 警/N 提示 │
├──────────────┬─────────────────────────────────────────┤
│ 左栏          │ 右栏（当前选中对象）                      │
│ 搜索          │ 局外：这一关的台词，按播放顺序            │
│ 筛选          │ 局内：这个系列/这条散条                   │
│ 关卡 / 系列列表 │  每条一张卡片：头像/立绘 + Key + 文案 + 预览 │
├──────────────┴─────────────────────────────────────────┤
│ 状态栏：当前对象 · 是否已保存 | 最近一次操作结果 | 工具版本    │
└────────────────────────────────────────────────────────┘
```

- **预览**开关：每条卡片下面画一个气泡示意（左边立绘、右边黑字），用来目测长短；关掉能省地方。
- **体检**开关：把每条自己的问题直接挂在它那一行（错误红 / 警告黄 / 提示灰），不用去别处翻。
- 点卡片左边的**头像/立绘槽**可以直接开选图窗口；语音 Key 旁边有「**试听**」。

---

## 二、局外 · 选关界面

选中一个关卡 → 右边就是这条台词表，按**播放顺序**从上到下：

| 按钮 | 作用 |
|---|---|
| `＋ 加一条台词` | 追加一条空台词 |
| `▲ ▼` | 上移 / 下移（第一位 / 最后一位自动置灰） |
| `复制` | 复制出一条**新的条目**（改它不影响原来那条） |
| `删` | 删掉这一条 |
| `一次性清空本关台词` | 二次确认后清空（保存前自动备份，`Ctrl+Z` 也能撤销） |

**运行时到底怎么演的**（写规则之前逐条对过代码，出处 `GuideDialoguePlayer.cs`）：

1. 触发不是"进关"，而是**在选关界面点这个关卡的按钮**（`StageButton.Selected`，:39/:67-86）。
2. 按顺序一条条来，一条播完才下一条（:96-146）。
3. 每条的顺序是：**先出声**（语音 :104）→ 头像弹入（:107-123）→ 停顿 → 气泡 + 文本弹出（:131-137）→ 整体上飘淡出（:140）→ 间隔。
4. **文本是一次性整段显示，没有逐字打字机效果**（:131）。
5. 文本为空的条目会被**直接跳过**（:99 `continue`）—— 所以"空文案"是**警告不是错误**：玩家什么都看不到，但也不会报错。
6. 头像 Key 为空 → 这一条**只弹气泡、不显示头像**（:119-122）；头像 Key 填了但注册表里没有 → 头像不显示 + 编辑器报错日志。
7. 语音 Key 为空 → 这条**静音**（:292）；填了但 `AudioRegistry` 里没有 → 运行时 `Debug.LogError`「请确认该 Key 已登记到 AudioRegistry」（:311-314）。
8. 玩家**不能跳过**，也没法点掉；只有离开选关界面（切 FSM 状态）或点另一关才会中断（:56-65 / :83-84）。

---

## 三、局内 · 对战文本

| 区域 | 说明 |
|---|---|
| 左栏「对话系列」 | 每个 `DialogueSeriesSO` 一个条目，右边显示**引用了几条** |
| 左栏「散条」 | 没被任何系列引用的 `DialogueConfigSO`：它们现在不会出现在任何对话里 |
| 系列右栏 | 按顺序的条目卡片；`＋ 新建一条配置并加入`、`＋ 从现有配置里挑一条`（已在系列里的会置灰）、`▲▼`、`复制`（复制成**新资产**）、`删`（只从系列里移除，资产还在） |
| 「查这个系列被谁引用」 | 按需扫一遍工程里的 prefab / 场景，看谁引用了这个系列（可能要几秒）。**脚本里的字段引用扫不到**，那种要自己看代码 |

**运行时到底怎么演的**（出处 `DialogueManager.cs` / `DialogueView.cs`）：

1. 播系列是**模态**的：会把 `Time.timeScale` 置 0 暂停游戏，并临时拦掉底层 UI 的输入（:714-727 / :745-753）。
2. 点对话框任意位置推进一句（`DialogueView` 的点击 → `DialogueManager.Advance`）；系列里的空槽位会被跳过（`TryGetNextValid`）。
3. **立绘 Key 为空是允许的**：不查资源，`Image` 用 Unity UI 内建白图，对话照常播（:346-348）。
4. **立绘 Key 非空、但"当前关卡的预载缓存"里没有 → 整条对话被拒绝**，只会打 `OnDialogueFailed`（:378-386）。
   这就是为什么本工具要提醒：**光把 Key 登记进 `SpriteRegistry` 还不够，还得出现在相关关卡的 `StageConfig.sprites` 里**。
5. 文本是一次性整段显示，也没有打字机效果；文本框宽度不受字数限制，**多长都不会报错，只会画出去**。

---

## 四、数据体检（每条规则都对着运行时行为写）

### 局外 · 选关界面

| 级别 | 规则 | 依据 |
|---|---|---|
| 错误 | 头像 Key 不在 `SpriteRegistry` | 运行时取不到图，头像不显示 + 编辑器报错日志 |
| 错误 | 语音 Key 不在 `AudioRegistry` | 运行时 `LogError`，要求重新生成注册表 |
| 警告 | 文本为空 / 纯空白 | 运行时 `continue` 跳过这一条：白配了一条 |
| 警告 | 看起来像占位文案（`test` / `待填` / 纯数字…） | 最容易跟着版本发出去 |
| 提示 | 没填头像 Key | 只弹气泡、不显示头像（合法用法） |
| 提示 | 没填语音 Key | 这条静音（合法用法） |
| 提示 | 首尾有多余空白 / 超过 40 字 / 同一关里两句一模一样 | 画到气泡里会多一截空 / 偏长（气泡宽度没在代码里限制，得目测） |
| 提示 | 有 14 关还没有台词（汇总成一条，不刷屏） | 只是进度信息 |

### 局内 · 对战文本

| 级别 | 规则 | 依据 |
|---|---|---|
| 错误 | 立绘 Key 不在 `SpriteRegistry` | 解析不到，对话会失败 |
| 错误 | 立绘 Key 在注册表里，但**没有任何一关的 `sprites` 预载清单**里有它 | 进关后 `GetSprite` 拿不到 → **整条对话被拒绝**（`OnDialogueFailed`） |
| 警告 | 文本为空 | 会弹一个空对话框 |
| 警告 | 系列一条都不引用 / 系列里有空槽位 | 播它没内容 / `TryGetNextValid` 会跳过，但位置留着容易看错 |
| 警告 | 看起来像占位文案 | 同上 |
| 提示 | 没填立绘 Key | 合法：用 Unity UI 内建白图 |
| 提示 | 同一条配置在一个系列里被引用多次 | 改文案会两处一起变（想各写各的用「复制」） |
| 提示 | 有散条（没被任何系列引用的配置） | 不会出现在任何对话里 |

---

## 五、保存 / 撤销 / 备份

- 改动**先落在内存里**，工具栏「保存」才写磁盘；有未保存改动时按钮变黄并显示 `●`。
- 保存前会把**这次改过的每个资产**各备份一份到 `<工具目录>/DialogueBackups/`（资产 + `.meta` 一起备份，将来还原 GUID 不变），每个资产最多保留 30 份（可在原文件里改 `DialoguePrefs.KeepBackupCount`）。
- 撤销走 Unity 自己的 `Undo`（`Ctrl+Z`），备注写的是中文（例如「修改导游台词」「调整对话顺序」）。
- 备份目录在**工程之外**（工具仓库里），Unity 不会导入它，也不会进版本库。

---

## 六、红线

| 别做 | 为什么 |
|---|---|
| 在选关界面给 14 个空关卡乱塞台词后不保存就关窗口 | 改动只在内存里，关窗口就没了（窗口标题不会变星号，看状态栏的「● 有未保存的修改」） |
| 手打一个不存在的立绘 / 语音 Key | 运行时必然报错；用「选图 / 选音」窗口从注册表里挑，那边只列真实存在的 Key |
| 局内对话只登记 `SpriteRegistry`、忘了加进关卡的 `sprites` | 进关后整条对话会被拒绝（工具会报错提醒） |
| 把 `DialogueConfigSO` 直接删掉 | 系列里会留下空槽位（工具会警告"空槽位"，能一键看出来是第几条） |
| 指望「复制」和「从现有配置里挑一条」是一回事 | 前者生成**新资产**（改文案互不影响），后者是**同一条被引用两次**（改一处两处都变） |

---

## 七、给程序看的部分

### 文件清单（都在 `Dialogue Editor\DialogueVisualEditor\`）

| 文件 | 作用 |
|---|---|
| `DialogueEditorWindow.cs` | 主窗口：两页（局外 / 局内）、左栏列表、卡片编辑、预览、状态栏 |
| `DialogueEditorData.cs` | 数据层：工具目录自定位、偏好、资产索引、注册表只读访问（Key / 立绘 / 语音）、私有 `[SerializeField]` 字段反射读写、脏标记与保存、备份与清理 |
| `DialogueEditorValidation.cs` | 体检规则（局外 / 局内两套），每条都注明运行时依据 |
| `DialogueEditorCore.cs` | **纯逻辑内核**：文案体检、移位边界、重复 / 占位识别、长度统计。只 `using System` + `System.Collections.Generic`，**不依赖 Unity**，可脱离编辑器单测 |
| `DialogueEditorUI.cs` | 绘制工具箱：配色、标签、右对齐小字、会换行的多行文本框、Sprite 等比绘制、气泡预览 |
| `DialogueEditorPickerWindow.cs` | Key 选择窗口：立绘按图挑（缩略图网格）、语音按列表挑（点一下试听） |

编译归属：`Assets/Editor/CageFightEditor.asmdef`（该 asmdef 已引用 `CageFightRuntime`）。工具目录里**没有** asmdef，不产生新程序集。

> 运行时代码全部在**全局命名空间**（`Assets/Runtime` 下没有 `namespace`），所以引用 `StageConfig` / `DialogueConfigSO` / `SpriteRegistry` 不需要 `using`。

### 与运行时代码的关系

- **只读**使用运行时类型；未修改 `StageConfig.cs` / `GuideDialoguePlayer.cs` / `DialogueManager.cs` / `DialogueView.cs` / `DialogueConfigSO.cs` / `DialogueSeriesSO.cs`
- 写数据只写 `StageN.asset`（`guideDialogues` 字段，public 字段直接改）、`DialogueConfigSO`、`DialogueSeriesSO`（后两个是 private `[SerializeField]`，用反射写 + `Undo.RecordObject` + `EditorUtility.SetDirty`）
- 编辑器偏好存在 `EditorPrefs`（前缀 `CageFight.DialogueVisualEditor.`）

### 编译校验（不开 Unity 也能验）

```

> 路径**自动探测**：脚本会自己找 Unity 工程与 Unity 自带的 Roslyn（csc.dll），换机器、换目录都不用改脚本；也可显式传 `-ProjectPath` / `-CscPath` / `-SourceDir` 覆盖。
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\compile-check.ps1"
```

用 Unity 自带的 Roslyn 对着工程真实用到的引用编一遍（含 `Library\ScriptAssemblies\CageFightRuntime.dll`），
并检查**源码卫生**：工具 `.cs` 行尾必须 LF、不带 BOM；`tools\*.ps1` 必须带 UTF-8 BOM。

### 安装 / 卸载目录联接

```
powershell -File tools\junction.ps1 -Action status
powershell -File tools\junction.ps1 -Action install
powershell -File tools\junction.ps1 -Action uninstall
```

挂载点：`CageFight\Assets\Editor\DialogueSystem\DialogueVisualEditor` → `Dialogue Editor\DialogueVisualEditor`。

### git 排除（重要）

联接会让 git 把工具源码当成"工程里的新文件"，所以 install 会在 `CageFight\Assets\Editor\.gitignore`
里追加 `/DialogueSystem`、`/DialogueSystem.meta`、`/.gitignore` 三行（**子目录 .gitignore 压得过根目录的 `!Assets/**` 反排除**；
规则放在上一层是因为 Unity 生成的 `DialogueSystem.meta` 在上一层）。装完会自动核对 `Assets/Editor` 在 git 眼里是否干净。

---

## 八、故障排查

| 现象 | 原因与处理 |
|---|---|
| Unity 里菜单没有「对话可视化编辑器」 | 联接失效（工具目录改名/挪走了）→ `tools\junction.ps1 -Action install` |
| 左栏是空的 | 点了「重新扫描」还是空 → 工程里没有 `StageConfig` / 对话资产 |
| 立绘一直显示"无立绘 / Key 无效" | Key 不在 `SpriteRegistry`，或者注册表资产被改名挪走了（体检会报"读不到 SpriteRegistry.asset"） |
| 选了图但预览还是旧图 | 注册表里的编辑器强引用没刷新 → 点「重新扫描」 |
| 保存后 Unity 还在转圈 | 正常：保存会写资产并让 Unity 重新导入 |

---

## 九、修订记录

### R1 — 首版：一个窗口两页，管住工程里两套对话

起因：游戏里其实有**两套互不相干**的对话系统（选关界面的导游台词、对战中的对话框），
而在这之前**只有**关卡编辑器里一个很朴素的"导游对话"列表（`LevelEditorWindow.DrawGuideDialogues`），
局内那套（`DialogueConfigSO` / `DialogueSeriesSO`）连一个编辑入口都没有 —— 只能在 Inspector 里点默认列表，
既看不到"立绘 Key 到底能不能解析"，也不知道"这条配置有没有被任何系列引用"。

| 改动 | 说明 |
|---|---|
| 新工具「对话可视化编辑器」（第 8 个工具） | 菜单 **工具 → 对话可视化编辑器**（`Ctrl + Alt + D`），目录联接挂在 `Assets/Editor/DialogueSystem/` |
| 两页 | **局外 · 选关界面**（每关 `guideDialogues`）／**局内 · 对战文本**（系列 + 散条） |
| 卡片式编辑 | 每条一张卡：头像/立绘槽（点一下选图）、Key 输入框 + 选图/选音/试听/清空、会换行的多行文案、`▲▼`/复制/删 |
| 气泡预览 | 每条下面按运行时的样子画一个示意（左立绘、右黑字），可整体开关 |
| 逐条体检 | 把每条自己的问题挂在它那一行；工具栏显示「N 错 / N 警 / N 提示」 |
| Key 选择窗口 | 立绘**按图挑**（缩略图网格，只列注册表里真实存在的 Key）、语音**按列表挑 + 点一下试听** |
| 引用查询 | 局内页可扫工程探"这个系列被谁引用"（prefab / 场景；脚本字段扫不到，会如实说明） |
| 安全网 | 保存前逐资产备份到 `DialogueBackups/`（含 `.meta`）、全流程 `Undo` 支持、改动未保存时状态栏与按钮都提示 |

**为什么局内那半只做"资产作者工具"**：现有运行时（`DialogueManager` + `DialogueView`）已经完整，
但工程里唯一调用它的是测试脚本 `Assets/Runtime/TestTool/TextTest.cs`（挂在 `TextTest.prefab` 上，没有任何关卡引用它）。
也就是说**现在改游戏代码去"接进关卡"是另一件事**（要新增组件 / 给 `StageConfig` 加字段），
本次按"不动游戏源码"的约定只做工具侧；真要把对话接进关卡，另开一轮再说。

> **怎么验的**（编译过 ≠ 逻辑对，这次验了两层）：
>
> **① 纯逻辑内核单测**：把 `DialogueEditorCore.cs`（含 `DialogueCore` / `DialogueIssue` / `DialogueSummary`）
> **从真文件里原样抽出**（不是手抄）编进独立控制台程序，跑 **61 条断言全部通过**：
> 空 / 纯空白（含全角空格）识别、首尾空白、一行摘要（换行压缩、超长截断）、
> **移位的全部边界**（第一位不能上移、最后一位不能下移、越界 / 负数 / 原位 / `null` 列表一律返回 false 且不动列表）、
> 重复文案定位（只找"前面出现过"的、空白不算重复）、占位文案识别（**用真实数据当样本**：`12312399` 算占位、`正式文本` / `上吧！击败这些小卒子` 不算）、
> 统计（条数 / 空条数 / 最长那条 / 总字数 / `null` 输入）、文案体检的四个码与级别、40 字阈值边界（40 不提示、41 提示）。
>
> **② 真实数据干跑**：另写脚本直接从 `CageFight` 的 YAML 资产抠数据（2544 个 Sprite Key、121 个 Audio Key、
> 22 关的 `guideDialogues` + `sprites` 预载清单、8 条 `DialogueConfigSO`、2 个 `DialogueSeriesSO` 的引用顺序），
> 再喂回**同一个** `DialogueCore` + 与体检相同的 Key 判定，得到"工具打开后会报什么"：
>
> | 页 | 结果 |
> |---|---|
> | 局外 | **0 错 / 0 警 / 2 提示** —— 9 条台词（Stage1–8）的头像 / 语音 Key 全都能解析；提示是 Stage8 那条没填语音（静音，合法）+ 14 关还没台词 |
> | 局内 | **4 错 / 7 警 / 7 提示** —— 错误全是现有测试资产：`1111_0`、`BabaDoctor Believer_14` 没进任何关卡的预载清单，`Audience-Chicken_0`、`Audience-Rat_6` 不在 `SpriteRegistry`；警告是 `TextTest1…5` 与 `12312399` / `12455555` 这类占位文案 |
>
> **干跑顺手挖出来两件值得知道的事**（工具只报告，不自动改）：
> - `Assets/Resource/LocalResource/Text/Dialogue1/Series.asset` 的引用顺序是 **`Config1 → Config1 → Config3`**：
>   第 1、2 条是**同一条资产**（不是复制出来的新条目），改文案会两处一起变 —— 工具会给一条提示。
> - **`Config2` 是一条散条**（没有任何系列引用它），`TestDialogueSeries` 也没有被任何 prefab / 场景引用（`Dialogue1/Series` 被 `TextTest.prefab` 引用）。
>
> 工具侧编译闸 ✔（6 个源文件）+ 源码卫生 ✔；目录联接已安装并核对 `Assets/Editor` 在 git 眼里干净。
>
> 未验的部分说清楚：**窗口本身没在 Unity 里打开过**（需要切回 Unity 触发一次编译）——
> 两页切换、卡片排版、气泡预览、选图网格的实际观感只能打开后目测；
> 数字与判定口径由上面两层兜住。
