# 法术可视化编辑器（Spell Visual Editor）

给 `SpellDefinition`（法术定义）和它们的**上架状态**做可视化编辑、体检与一键修复的 Unity 编辑器工具。

与另外四个编辑器（关卡 / 建筑 / 图鉴 / 兵种）是同一套做法：

* **工具源码放在游戏仓库之外**（`GitHub\Spell Editor`），靠一个目录联接挂进 CageFight；
* **游戏代码一行不改** —— 只读地引用运行时类型，写数据只写法术资产与注册表资产，保存前自动备份；
* 界面上每一个「数值 → 结论」都能追到运行时代码出处，体检里逐条写明。

---

## 一、怎么打开

Unity 菜单：**工具 → 法术可视化编辑器**（快捷键 `Ctrl+Alt+S`）

也可以在 Project 里选中一个 `SpellDefinition` 资产 → 右键 → **打开法术可视化编辑器**（会直接定位到它）。

窗口三栏：

| 栏 | 内容 |
|---|---|
| 左 | 法术列表（图标 + 资产名 + ID + 登记状态 + 错误/警告角标），顶部搜索框 |
| 中 | 选中法术的表单：身份 / 消耗 / 表现 / **伤害与效果** / 投递 / 瞄准 / 警示圈 / 注册表 |
| 右 | **体检**页（错误·警告·提示，逐条给依据）与**设置**页（备份、路径、注册表键数） |

工具栏：`重扫扫描`、`保存（Ctrl+S）`、`体检 错误/警告/提示`、`一键修复`、`备份 ▾`、`面板 ▶`（收起右栏）。

> `Ctrl+S` 是**保存全部已改动的法术资产**（Unity 编辑器窗口默认不吃这个快捷键，这里专门接管了）。

---

## 二、数据在哪

| 东西 | 位置 |
|---|---|
| 法术资产 | `Assets/RemoteResource/LoadoutDefinitions/Magic/*.asset`（`SpellDefinition`） |
| 上架注册表 | `Assets/RemoteResource/LoadoutDefinitionRegistry.asset` → `spellDefinitions` 列表 |
| 资源键 | `SpriteRegistry`（图标 / 警示圈）、`PrefabRegistry`（施法预制体） |
| 字段定义 | `Assets/Runtime/Gameplay/Loadout/SpellDefinition.cs` |
| **伤害与效果** | **施放预制体的执行器组件**上（`SpellDefinition` 里没有任何伤害字段）→ 见「伤害在哪」一节 |
| 施法链路 | `Assets/Runtime/Gameplay/MagicSystem/EngineerSpellCaster.cs` |

### 伤害在哪（为什么不在法术资产里）

`SpellDefinition` 只有 18 个字段（id / 图标 / 冷却 / 金币 / 施放预制体键 / 投递 / 弹道 / 警示圈…），**一个伤害字段都没有**。
真正的伤害写在**施放预制体的执行器组件**上 —— 运行时 `EngineerSpellCaster` 按 `deliveryType`
在预制体根节点上找三个接口之一并调用它（`EngineerSpellCaster.cs:136-169`）：

| 执行器 | 伤害/效果字段 | 出处 |
|---|---|---|
| `EngineerSpellDirectEffect` | `damage`（直接伤害）、`repel`、`damageType` | `EngineerSpellDirectEffect.cs:9-11` |
| `EngineerSpellProjectile` | `damage`、`repel`、`damageType` | `EngineerSpellProjectile.cs:7-10` |
| `MagicEnergySing`（大招） | `baseDamage`（**等级跟随**的基准）、`repel`、`damageType`、`radius` | `MagicEnergySing.cs:16-27` |
| `UnstableOreMagicProjectile` | `baseDamage`、`repel`、`damageType`、`explosionRadius` | `UnstableOreMagicProjectile.cs:19-32` |
| `HugeCheeseMagic` / `HundredYearsIce` / `DancingStatueMagic` / `StrangeMineMagic` | **没有直伤**：靠**施加 Buff**（巨化 / 冰冻 / 麻痹 / 创伤），组件上只有 `radius` / `duration` / `tickInterval` | 各文件 `[SerializeField]` 段 |

所以工具的做法是：扫施放预制体上的组件，按**白名单**列出伤害/效果字段就地编辑，并给出：
* 一行**摘要**（如同一法术「直接伤害 100（魔法）· 半径 2 · 击退 6」）；
* 没有直伤时明确写出「它的效果是施加状态，数值在它引用的 Buff 资产上」，并把 Buff 实例列出来（可定位/可换）；
* **共用预制体警告**：实测 `Bababoyi / Broom cat / Kende / Omniscient magic` 四个法术共用同一个施放预制体 `B-39`
  —— 改它等于一次改 4 个法术，工具会直接把这句话显示出来。

> 改的是**施放预制体资产**（不是关卡局部数据），会影响所有用到这个法术的地方；
> `Ctrl+S` 保存前会把该预制体的旧内容备份到 `Spell Editor\SpellBackups\`。

### 法术字段一览（表单顺序）

| 分组 | 字段 | 说明 |
|---|---|---|
| 身份 | `id` | **稳定 ID**：注册表按它建字典，装备与存档也按它引用。改 ID 等于换了一个法术 |
| | `displayName` | 界面文案（空则退回资产名） |
| 消耗 | `goldCost` | **每次施放扣除的金币**；0 = 免费。法术栏按钮右下角的角标显示的就是它 |
| | `cooldown` | 两次施放之间的最短间隔（秒）；0 = 不限制 |
| 表现 | `iconKey` | 法术栏按钮 / 选装界面的图标（Sprite 键） |
| | `castPrefabKey` | 施法预制体（Prefab 键）；**根节点上必须有执行器接口** |
| 投递 | `deliveryType` | `Projectile`（弹道/瞄准）或 `DirectSpawn`（原地生成） |
| | `directCastTime` | 仅 `DirectSpawn`：播「Cast spell」前摇后等待的秒数 |
| | `releaseMode` | `Custom` 或 `FromEngineer`（从工程兵发出） |
| | `spawnOffset` | 仅 `FromEngineer` + `Projectile` 时叠加到施法点 |
| | `faceEngineerDirection` | 仅 `FromEngineer` 时生效 |
| 瞄准 | `dragAim`、`maxDistance`、`arcHeight`、`flightTime` | 拖拽瞄准与弹道参数 |
| 警示圈 | `showWarningCircle`、`warningCircleKey`、`warningCircleScale` | 拖拽时显示的落点范围圈 |

---

## 三、数据体检（本工具最值钱的部分）

每条结论都能在右栏点「定位」跳到对应法术。**规则全部照抄运行时行为**，出处写在每条详情里。

### 错误（会让法术在游戏里彻底不可用）

| 规则 | 依据 |
|---|---|
| **ID 为空** | `LoadoutDefinitionRegistry.AddSpells` 会跳过 ID 为空的定义（`LoadoutDefinitionRegistry.cs:142`）→ `TryGetSpell(id)` 永远查不到这个法术 |
| **ID 与另一个法术重复** | 注册表用 `ContainsKey` 拦了重复，只打一条「重复法术 ID」警告，**后登记的那条作废**（`:143-147`）→ 其中一条游戏里永远选不到 |
| **没有登记进注册表** | `ResourceManager.TryGetSpell / SpellDefinitions` 只遍历注册表列表（`ResourceManager.cs:404-436`）→ 没登记的资产在游戏里等于不存在 |
| **施法预制体键为空** | `EngineerSpellCaster.Cast` 第一步就检查它，为空直接 `return false`（`EngineerSpellCaster.cs:41-42`）→ 点了没反应 |
| **施法预制体键不在 PrefabRegistry** | 运行时 `GetGameObject(key)` 取不到 → `LogError`「法术预制体未预加载」并放弃施法（`:52-59`） |
| **预制体根节点没有任何执行器** | `LogError`「法术预制体根节点缺少施法执行器」并回收实例（`:151-169`）→ 法术无任何效果 |
| **投递方式与预制体不匹配** | `DirectSpawn` 只找 `IEngineerDirectSpellInstance`（`:136-148`）、`Projectile` 找 `IEngineerAimedSpellInstance` 或 `IEngineerSpellInstance`；找不到就 `LogError` 并回收 |
| **注册表资产加载不到** | 上架状态与登记功能都不可用 |

### 警告（配置与运行时行为不一致）

| 规则 | 依据 |
|---|---|
| ID 与资产文件名不一致 | 运行时只认 ID；不一致以后很难对上号 |
| 显示名为空 | 界面文案退回资产名 |
| 图标键为空 / 不在 SpriteRegistry | `LoadRegisteredSprite(IconKey)` 加载不出来 → 法术栏空白图标（`PlayerLoadoutManager.cs:226`） |
| 开了警示圈但键为空 / 不在注册表 | 只在 `showWarningCircle = true` 时预载该键（`PlayerLoadoutManager.cs:207-208`）→ 缺了就没有圈 |
| 数字越界（冷却 / 金币 / 前摇 / 最远距离 / 飞行时间 / 警示圈缩放 / 抛物线高度） | 字段上都有 `[Min]`（`SpellDefinition.cs:23-44`）；Inspector 会拦，但**数据里可以直接写非法值** |
| 注册表里有重复条目 | 运行时不报错（字典忽略重复），但列表长度虚高，排查容易看错 |

### 提示

DirectSpawn 法术的弹道三项（`arcHeight`/`flightTime`/`maxDistance`）不参与运行时、`releaseMode = Custom` 时朝向跟随不生效、配了警示圈贴图却没开显示、**默认出装的两个法术槽都是空的**。

### 一键修复（只做确定性、数据层面的修复）

* 把**没登记**的法术补进 `LoadoutDefinitionRegistry.spellDefinitions`（**上架**）
* 删掉注册表里**重复**的条目
* 数字**夹到**字段约束范围
* 键名**大小写 / 分隔符校正** —— 仅当注册表里存在唯一同名键时才改
* `deliveryType` 与预制体上的执行器**同步**（能唯一判断时才改）
* **ID 为空**时用资产名生成一个

**不会做**：改已有 ID（ID 是引用的依据）、删法术资产、改预制体、改游戏代码。修复前对**被改动的资产**逐个备份。

---

## 四、保存 / 撤销 / 备份

* 表单改动即时写进资产的 `SerializedObject`（`Ctrl+Z` 可撤销）
* `Ctrl+S` 保存全部改动的资产；**保存前把当前磁盘内容备份**到 `Spell Editor\SpellBackups\`
* 备份命名 `<资产名>_yyyyMMdd_HHmmss_<原因>.asset`；每个资产默认保留最近 20 份
* 淘汰只按「同名资产」分组、且只有文件名符合上述格式的才参与 —— **真实资产不会被误删**
* `备份 ▾` 菜单可打开备份目录、在 Console 打印路径、定位最近 15 份备份

---

## 五、已核实的运行时事实

1. **法术按 ID 注册**：`LoadoutDefinitionRegistry.Initialize()` 建 `spellById`；重复 ID 打警告并忽略后来者。
2. **施法要找执行器**：`EngineerSpellCaster` 按 `deliveryType` 在**施法预制体的根节点**上找三个接口之一 ——
   `IEngineerDirectSpellInstance`（原地）、`IEngineerAimedSpellInstance`（瞄准）、`IEngineerSpellInstance`（不需要目标点）。
3. **`DirectSpawn` 还需要全局的「Cast spell」预制体**做前摇（`:69-79`）—— 它由 `ResourceManager.commonPrefabs` 全局预载，不是法术自己的字段。
4. **预载**：`PlayerLoadoutManager` 按当前出装预载 `CastPrefabKey`、`IconKey`，以及（仅 `showWarningCircle` 时）`WarningCircleKey`。
5. **默认出装**：`TryGetDefaultLoadout` 只强制校验工程师与种族；默认法术槽填了但查不到时会被 `NormalizeDefaults` 清空。

---

## 六、文件清单（都在 `Spell Editor\SpellVisualEditor\`）

| 文件 | 作用 |
|---|---|
| `SpellEditorWindow.cs` | 主窗口：三栏布局、工具栏、表单、体检面板、备份流程 |
| `SpellEditorModel.cs` | 数据层：偏好、注册表只读索引（6 类键）、法术扫描、登记（上架）、备份 |
| `SpellEditorValidation.cs` | 体检规则 + 一键修复 |
| `SpellEditorUI.cs` | 绘制工具箱（配色、分区、分割条、标签、Sprite 绘制、表格单元） |
| `SpellKeyPickerWindow.cs` | 通用资源键选择窗口（独立可停靠、可缩放） |

编译归属：`Assets/Editor/CageFightEditor.asmdef`（该 asmdef 引用了 `CageFightRuntime`，所以能直接用
`SpellDefinition` / `SpellDeliveryType` / `LoadoutDefinitionRegistry` / `IEngineer*SpellInstance`）。
工具目录里**没有** asmdef，不会产生新的程序集。

> 运行时代码全部在**全局命名空间**（`Assets/Runtime` 下没有任何 `namespace`），所以工具里引用游戏类型不需要 `using`。

---

## 七、编译校验（不开 Unity 也能验）

```powershell

> 路径**自动探测**：脚本会自己找 Unity 工程与 Unity 自带的 Roslyn（csc.dll），换机器、换目录都不用改脚本；也可显式传 `-ProjectPath` / `-CscPath` / `-SourceDir` 覆盖。
powershell -File "tools\compile-check.ps1"
```

用 Unity 自带的 Roslyn + `CageFightEditor.csproj` 里真实的引用列表 + `Library\ScriptAssemblies\CageFightRuntime.dll`
编译一遍，产物落在 `Spell Editor\.compile-check\`。**不启动 Unity、不写工程任何文件。**

脚本同时做**源码卫生检查**：工具源码 `.cs` 必须是 **LF 行尾、不带 BOM**；`tools\*.ps1` 必须**带** UTF-8 BOM
（Windows PowerShell 5.1 读中文要靠它）。卫生有问题时退出码是 **2**，与编译失败的 1 区分开。

---

## 八、安装 / 卸载目录联接

```powershell
powershell -File "tools\junction.ps1" -Action install
powershell -File "tools\junction.ps1" -Action status
powershell -File "tools\junction.ps1" -Action uninstall
```

安装后联接位置：`CageFight\Assets\Editor\MagicSystem\SpellVisualEditor` → `GitHub\Spell Editor\SpellVisualEditor`。

---

## 九、git 排除（重要）

工具源码**不能进游戏仓库**。CageFight 的根 `.gitignore` 里有一条专门的规则把这个联接路径排除掉：

```
/Assets/Editor/MagicSystem/SpellVisualEditor
/Assets/Editor/MagicSystem/SpellVisualEditor.meta
```

原因：git 会**顺着目录联接**把工具源码当成"本工程的新文件"，一旦提交就会出现两份代码、以后必然对不上；
而且别人克隆后在那个路径生成真实文件，就再也建不了联接。

工具源码自己的版本管理在 `GitHub\Spell Editor` 这个**独立仓库**里（无远端，只在本机）。

---

## 十、故障排查

| 现象 | 原因与处理 |
|---|---|
| Unity 菜单里没有「法术可视化编辑器」 | 联接没装或指向失效：`junction.ps1 -Action status` 看一眼，再 `-Action install` |
| 列表是空的 | `Assets\RemoteResource\LoadoutDefinitions\Magic` 不存在，或里面没有 `SpellDefinition` 资产 |
| 体检说「找不到注册表资产」 | `Assets\RemoteResource\LoadoutDefinitionRegistry.asset` 被移动/改名了 |
| 改完字段游戏里没变化 | 记得 `Ctrl+S` 保存；另外运行时是按**出装**预载的，没被任何出装用到的法术不会预载 |
| 键在注册表里却显示「不在注册表」 | 点工具栏「重扫扫描」（注册表索引有缓存） |

---

## 十一、修订记录

### R1.3 — 修「注册表资产引用读不到」（会把好数据误报成错）+ 预制体全部字段可就地改

起因：实战截图里 `Dancing statue Magic` 被红字报成「键不在 PrefabRegistry」——
但它**明明在**（`PrefabRegistry.asset` 第 1003 行）。查下来是工具的读取 bug：

| 问题 | 真相 | 修法 |
|---|---|---|
| 六张注册表的资产字段名**各不相同** —— `prefab` / `sprite` / `audioClip` / `texture` / `animationClip` / `animatorController`，而且每条都还带一个 AssetReference（`prefabReference` / `spriteReference` / … 里的 `m_AssetGUID`）。**没有一张表有 `m_Asset` 字段** | 工具只找 `m_Asset` → 所有资产引用都取不到（键的集合是对的，所以没有「不在注册表」的红字，但资产全是空的）→ 于是执行器检查与「伤害与效果」都报成"不在注册表/取不到预制体" | 改成**不猜字段名**：扫这条目的子属性 —— ① 任何 `ObjectReference` 且有值就用它（这一路能拿到 SpriteRegistry 的**子精灵**）②都没有就找 `m_AssetGUID`，用 guid 反查路径加载 |

连带修的：

* **失败提示分成三种**（以前一律说「不在 PrefabRegistry」，是误报）：
  ① 键为空；② 键**不在**注册表；③ 键在注册表但**那条登记没指向资产**（引用为空 / 资产被删）——三者的修法完全不同
* 新增体检规则 `spell.castPrefab.assetMissing`（对应上面第 ③ 种）
* **副作用（好的一面）**：这个 bug 之前让**所有**注册表取值都是 null —— 包括法术图标；修好后左侧列表与详情里的图标预览也会开始显示

顺手加的：

| 改动 | 说明 |
|---|---|
| **「显示预制体全部字段」开关** | 打开后除伤害白名单外，还能就地改施放预制体组件上的其它**直观字段**（数字 / 开关 / 文本 / 枚举 / 向量 / 颜色 / 对象引用）。数组与嵌套结构跳过 —— 那些在预制体 Inspector 里改更安全 |

### R1.2 — 「伤害与效果」：把伤害从施放预制体上读出来改

起因：想改法术伤害，但在法术资产上**找不到伤害字段** —— `SpellDefinition` 只有 18 个字段，全是
id / 图标 / 冷却 / 金币 / 施放预制体键 / 投递 / 弹道 / 警示圈。查下来伤害写在**施放预制体的执行器组件**上。

| 改动 | 说明 |
|---|---|
| 中栏新增**伤害与效果**分组（放在「表现」之后） | 扫施放预制体上的组件，按白名单列出伤害/效果字段：**直接伤害 / 基础伤害（等级跟随）/ 伤害类型 / 击退强度 / 半径 / 爆炸半径 / 持续 / 施加间隔 / 攻魔攻 / 伤害倍率**，就地编辑（数字夹到字段约束范围、`Ctrl+Z` 可撤销） |
| 一行**摘要** | 如「直接伤害 100（魔法）· 半径 2 · 击退 6」；只显示可识别的关键数字，便于横向对比法术强度 |
| **共用预制体警告** | 同一个施放预制体被多个法术共用时直接写明（实测 `B-39` 被 4 个法术共用）—— 改它等于一次改 4 个 |
| **没有直伤时如实说明** | 范围型法术（巨化/冰冻/麻痹/创伤）没有直伤字段 → 提示「效果是施加状态，数值在 Buff 资产上」，并把 Buff 实例列出来（可换引用、可定位） |
| 保存一起备份预制体 | `Ctrl+S` 除了备份法术资产，还会备份被改过伤害的**施放预制体**（旧内容 = 回退点） |

> **实测各法术会露出什么**（工具外面按同一套白名单核的）：`Crowbar` → `damage=20 / initialDamage=10 / damageType=魔法`；
> `Magic energy sing Magic` → `baseDamage=150 / 半径 4 / 击退 10`；`Unstable ore Magic` → `baseDamage=100 / 爆炸半径 2`；
> `Hundred years ice` → `半径 4.5 / 持续 3~5 / 每 0.3s`；`Dancing statue Magic` / `Strange mine Magic` / `Huge cheese Magic` → 只有半径与节奏（无直伤）；
> `Bababoyi / Broom cat / Kende / Omniscient magic` → 共用预制体 `B-39` 的 `atk=220 / magicAtk=20`。

### R1.1 — 补上「消耗」分组（施法金币 / 冷却）

首版表单漏了 `goldCost` 与 `cooldown` 这两个字段（体检里有对应规则，表单里却没有输入框 —— 实际打开一看就发现了）。

* 中栏新增**消耗**分组：`施法消耗（金币）` + `冷却（秒）`，紧跟「身份」之后；
* 两个字段的输入都**夹了范围**（金币 ≥ 0、冷却 ≥ 0）：`goldCost` 是负数会让扣费逻辑倒着走，`cooldown` 是负数没有意义；
* 附带一行参考：满冷却连放时的**金币/分钟**（次数 × 单发金币），方便判断一发法术有多贵；
* 字段覆盖核对：`SpellDefinition` 的 **18 个玩法字段**现在与表单一一对应（15 个普通字段 + 3 个资源键）。

> 扣费链路（写进 tooltip）：`goldCost` 由 `GameplaySpellBar` 在按下时先 `CanAfford` 判断、再 `ChargeGold` 扣除
> （`GameplaySpellBar.cs:384-403`）；0 = 免费。

### R1 — 法术可视化编辑器（首版）

* **法术列表 + 表单**：18 个玩法字段全部可视化（含 3 个资源键的键选择器 + 图标/预制体预览）
* **体检**：错误 8 条 / 警告 6 类 / 提示 4 类，每条都写明运行时代码出处
* **一键修复**：登记上架、注册表去重、数字夹范围、键名校正、`deliveryType` 与执行器同步、空 ID 生成
* **注册表面板**：显示每个法术的「已登记 / 未登记」，可一键登记（写的是**数据资产**，不是代码）
* **备份**：保存 / 一键修复前逐资产备份，按资产分组淘汰，绝不误删真实资产
* **编译校验 + 源码卫生**脚本、**目录联接**安装脚本、CageFight 侧的 git 排除规则

**没改游戏代码**：工具只读地引用 `SpellDefinition` / `LoadoutDefinitionRegistry` / `IEngineer*SpellInstance` 等运行时类型。
