# 建筑可视化编辑器（Building Visual Editor）

给**不懂技术的策划**用的建筑调整工具：三级生命/攻击/射程/造价、每级外观图、占地与施工、兵营兵种表、哨塔射击与维护费，一屏看完，并且能一眼对比两个种族的同一类建筑。

- 代码位置（唯一真源）：`GitHub\Building Editor\BuildingVisualEditor\`
- 接入方式：`CageFight\Assets\Editor\BuildingSystem\BuildingVisualEditor` 是一个**目录联接（junction）**，指向上面的源码目录。工程里没有多出任何实体脚本文件，也没有改动任何既有文件。
- 姊妹工具：图鉴编辑器（`Illustration Editor`）、兵种编辑器（`Unit Editor`）、关卡编辑器（`Level Editor`），四者互不依赖。

---

## 一、怎么打开

| 方式 | 操作 |
|---|---|
| 菜单 | Unity 顶部菜单 **工具 → 建筑可视化编辑器**（快捷键 `Ctrl + Alt + G`） |
| 资产右键 | Project 面板里右键建筑预制体 → **打开建筑可视化编辑器**（只有挂了 `BuildUP` / `BuildingBase` 的预制体才显示这一项） |
| 自动 | 在 Project 面板点选建筑预制体，已打开的工具窗口会自动切过去 |

工具扫描的是**全部预制体**，判定「挂了 `BuildUP` 或 `BuildingBase` 就算建筑」。实测工程里命中 **9 个**：

```
Assets\RemoteResource\Prefabricated body\Build\
    Cat Barracks1 / Cat Base1 / Cat Dark barracks1 / Cat Sentry tower1
    Mouse Barracks1 / Mouse Base1 / Mouse Dark barracks1 / Mouse Sentry tower1
Assets\RemoteResource\Prefab\Camp.prefab      ← 有 BuildingBase、没有 BuildUP（中立营地）
```

---

## 二、界面导览

```
┌────────────────────────── 工具栏 ──────────────────────────┐
│ 重新扫描建筑 | 保存● | 撤销 |   体检 N 错 / N 警 / N 提示 | 一键修复 | 备份▾ | 设置 │
├──────────┬───────────────────────────────┬────────────────┤
│  左栏     │            中栏                │      右栏       │
│ 建筑列表  │  三级外观对比 + 三级数值编辑表    │  体检 / 设置     │
│ 搜索筛选  │  占地与施工 / 兵营 / 哨塔 /      │                │
│ 分组方式  │  升级与拆除 / 谁在用这个建筑      │                │
├──────────┴───────────────────────────────┴────────────────┤
│ 状态栏：当前建筑 · 类型 · 是否已保存 | 工具版本 | 最近一次操作结果      │
└───────────────────────────────────────────────────────────┘
```

左右两条分隔条都可以**拖动**调宽度。

### 左栏：建筑列表

- 分组方式：**按种族分组**（默认）/ **按建筑类型分组**
- 每行：一级外观缩略图 + 名字 + 类型 + 归属种族 + 右上角「N 级 / 无等级表」角标 + 问题数，左侧竖条是这一行最严重的体检级别
- 筛选：搜索（名字 / 种族 / 类型 / 路径）、只看有问题的、只看能建造的、只看中立/没等级表的

### 中栏：三级外观对比 + 三级数值编辑表

这是本工具的核心，也是和其它三个编辑器最大的不同：

**① 三级外观对比条**（每级一张卡）：该级的外观图（**强引用**，直接画）、生命、攻击、攻击范围、该级造价、到这一级累计花了多少、拆了退多少；会攻击的建筑还会显示 `× 1.1^(等级-1)` 换算后的**本关实际值**。

**② 三级数值编辑表**：逐级改 `外观图 / 生命 / 攻击 / 攻击范围 / 造价`，其中第 1 级是**建造费**、第 2/3 级是**升级费**。

> ⚠️ **必须改这张表**：运行时 `BuildUP.ApplyLevel()` 会用 `levels[]` 覆盖预制体上的 `GameObjectProperty.maxHp / atk / atkRange` —— 改 prop 上那几个字段**没有用**。
> 缩放只作用于**生命 / 攻击**（`× 1.1^(等级-1)`）；**攻击范围不缩放**，三级要分别填。

**③ 其它分区**（都可折叠）：占地与施工、兵营（兵种表增删）、哨塔（射击与维护费）、升级与拆除表现、谁在用这个建筑。

- **右栏可以收起**：右栏页签行最右端有 **✕** —— 收起整条右栏（连分割条一起收，中栏立刻拿到全部宽度）；收起后工具栏会出现 **▸ 展开右栏**。收起状态记在 EditorPrefs 里，关掉重开还是收起的。

### 右栏

**体检**：按级别过滤（错误 / 警告 / 提示），每条带出处说明，「定位」可直接跳到对应建筑。

**设置**：注册表读取情况、备份目录与份数、当前建筑信息、从磁盘重新加载。

---

## 三、数据体检

体检覆盖**全部建筑**，并额外做**跨建筑**与**种族建筑表**的检查。

### 错误

| 规则 | 依据 |
|---|---|
| 没有 `BuildingBase` | 占地登记、合法性检查、施工流程、朝向翻转都在它上面 |
| 没有 `GameObjectProperty` | 占地、施工时长、血量都取不到；`ApplyLevel` 写数值时会直接跳过 |
| `objectType` 没勾 `Building` | 放置系统按 `(objectType & GameObjectType.Building) != 0` 判断（`BuildingPlace.cs:82`） |
| 等级表为空 | 建造费/血量/攻击/升级全都取不到值 |
| 第 1 级没有外观图 | 第 1 级是初始外观；sprite 为空时升级**不换图**（`BuildUP.cs:442-443`） |
| 某级生命 ≤ 0 | 会退化成 1 血（`SetMaxHp(max(1, round(...)))`） |
| 某级造价是负数 | 会被 `max(0, cost)` 夹成 0 |
| **哨塔某级攻击范围为 0** | 哨塔靠以自身为中心的矩形索敌，任一维为 0 就永远选不到目标 |
| 占地尺寸有 0 或负 | 占用登记形同虚设，建筑可以互相叠放 |
| 资源键不在注册表 | 施工特效键 / 面板键 / 弹幕键 / 各类音效键 / 升级特效键 / 拆除动画键 |
| 种族建筑表缺某个类型 | `BuildingButton.ResolvePrefab()` 查不到就**回落成建造按钮上的默认预制体**（通常是别的种族的建筑） |
| 种族建筑表里的键不在注册表 / 是空键 | 同上 |
| 哨塔维护费是负数 | 维护费以 `isDefense:true` 注册进队伍账本，负数等于倒贴收入 |
| 兵种表里有空条目 | `BuildingTraining.PreloadResources()` 遍历时会抛 NullReferenceException |

### 警告

| 规则 | 依据 |
|---|---|
| 等级表不是 3 级 | 语义就是三级（`new LevelData[3]`，显示等级 = 索引 + 1） |
| **填了施工时长但施工特效键为空** | `BuildingBase.StartBuild()` 里 `buildAnime == null` 直接 return → **不进施工流程、`isCompleted` 恒为 false**（兵营面板打不开、哨塔不开火、拆除表现不显示）。实测两个大本营正是这个情况 |
| 某级生命没比上一级高 / 攻击更低 | 花了升级费却更弱，通常是漏改或填反了 |
| **建造费是 0** | `GetBuildCost` 取 `levels[0].cost`，为 0 时 `cost > 0` 判断直接跳过扣费 → 可以无限免费摆。实测 Cat 的三个建筑造价全是 0 |
| 预制体上的 `side` 不是 1 | 玩家的建造/升级/拆除只对 `side == 1` 开放（`BuildUP.cs:73`） |
| `isDarkBarracks` 与预制体名字对不上 | 这个标记决定按 `darkBarracksLevel` 还是 `barracksLevel` 结算，配错会让敌方黑暗兵营等级作用在普通兵营上 |
| 不在任何种族建筑表里 | 玩家永远造不出它（没有 BuildUP 的中立建筑降级为提示） |
| 兵种表里有重复兵种 | `GetTroop(row, index)` 按解锁等级逐排取，重复会让面板出现两个一样的格子 |
| **有兵种的解锁等级超出 1..3** | 兵营面板只填 `row = 1..3`，越界的兵种在任何一排都不会出现 —— 要在**兵种编辑器**里改它的兵营阶级 |
| 哨塔开火音效键不在 AudioRegistry | 出包后这一声会静音 |
| 哨塔维护费表不足 3 级 | 缺的级取不到值，按 0 算 |
| 兵营没配头像渲染器 / 面板键为空 | 头像不显示 / 面板打不开 |
| **有兵种没有挂在任何兵营上** | 玩家在兵营界面永远看不到它们 |

### 提示

没有 BuildUP（中立建筑）、某级造价递减、兵种同时挂在普通与黑暗兵营、哨塔没指定射击点、兵营没配冷却遮罩、非哨塔建筑的攻击/攻击范围为 0（**这是正常的**，见下文）等。

### 一键修复（只做安全确定性修复）

- 等级表补齐到 3 级 / 截断到 3 级（用最后一级的值补齐）
- 负数造价、负数时长、负数维护费 **归零**
- 占地尺寸夹到至少 1×1；`side` 夹回 1（玩家）
- 删掉兵种表里的空条目；`isDarkBarracks` 按名字纠正
- **三级数值（生命/攻击/射程/造价）一律不动** —— 那是平衡决策；**种族建筑表也不动**（该用哪个预制体只有人知道）

---

## 四、保存 / 撤销 / 备份

- 所有字段写入都走 **`SerializedObject` + `ApplyModifiedProperties`** —— 与 Inspector 完全一致，`Ctrl+Z` 原生可用，而且**不受私有 `[SerializeField]` 限制**（建筑的绝大多数参数都是私有的：`removable`、6 个拆除表现参数、`panelPrefabKey`、`avatarRenderer`…）
- 工具栏「保存 (Ctrl+S)」才写盘：先把**原文件**备份到 `Building Editor\BuildingBackups\`，再 `AssetDatabase.SaveAssets()`
- 备份按**资产名分组**各自保留最新 N 份（默认 20），不会出现"刚生成的备份被当场删掉"
- 「从磁盘重新加载」丢弃内存改动；切换建筑前会先问

---

## 五、已核实的运行时事实（界面上的换算都基于这些）

| 事实 | 出处 |
|---|---|
| 三级数据 = `BuildUP.levels[0..2]`，每级 `{ sprite, maxHp, attack, attackRange, cost }`；`level` **0 起**，显示等级 = level+1 | `BuildUP.cs:8-19`、`:448` |
| **每级外观图是强引用 Sprite（图集切片）**，不是资源键 | `BuildUP.cs:11` |
| 建造费 = `levels[0].cost`；升级费 = `levels[level+1].cost`；总花费 = 建造费 + 已付升级费；**返还 = 总花费 / 2 向下取整** | `BuildingPlace.cs:355-363`、`BuildUP.cs:78-86` |
| **`ApplyLevel()` 是唯一写数值入口**：`生命/攻击 × 1.1^(等级-1)`（`health.SetMaxHp` 走受控 API），**攻击范围直接赋值、不缩放**，**每级换图** | `BuildUP.cs:424-449` |
| 等级缩放的等级来源：**哨塔取 `sentryTowerLevel`、兵营取 `barracksLevel`、其它恒为 1** → 大本营不吃等级缩放 | `BuildUP.cs:451-465` |
| **预制体上的 `prop.maxHp / atk / atkRange` 是"没填的默认值"，运行时被 `levels[]` 覆盖** | 实测：`Mouse Barracks1` 的 prop.maxHp=100 而 levels[0].maxHp=2000 |
| **非哨塔建筑的 `levels[].attack / attackRange` 全是 0**（兵营/大本营不攻击），而 `ApplyLevel` 会无条件把 `prop.atkRange` 写成它 | 实测 |
| 占地 = `GameObjectProperty.occupySpace`（格）；左下基准格 = `(int)(pos - size/2 + 0.5)`；占用是**挡路**的 | `BuildingBase.cs:165-190`、`:225` |
| `ChechValid()` 三条判据：全格在地图内 + 全格在**己方半场** + 无他人占用；不合法时 `spr.color = red` | `BuildingBase.cs:80-123` |
| 半场中点 = `(friendlyX + friendlyX + max(1,enemyBaseDistance)) / 2`，**奇左偶右、闭区间** | `BuildingBase.cs:147-159` |
| **敌方建筑（偶数队）会 `spr.flipX = true` 左右翻转** | `BuildingBase.cs:387-394` |
| **施工**：`buildAnime` 为空则根本不进施工流程（`StartBuild` 直接 return）；施工期间 `spr.enabled=false`、血量按进度涨 | `BuildingBase.cs:129-140`、`:260-319` |
| 玩家的建造/升级/拆除/训练只对 **`side == 1`** 开放；敌方建筑走队伍账本 `TryUpgradeByTeam()` | `BuildUP.cs:73`、`:175-189` |
| 兵营面板按**兵种解锁等级**分三排（`row = 1..3` ↔ `UnlockLevel`） | `BuildingTraining.cs:68-79`、`TroopTrainingPanel.Refresh()` |
| 兵营归**只读展示**：`troops[]` 与 `isDarkBarracks` 在建筑预制体上 | `BuildingTraining.cs:14-17` |
| 哨塔索敌是以自身为中心的**矩形** `atkRange`（half = range/2），取最近目标；**施工中不开火** | `BuildingTowerAI.cs:164-216`、`:124-158` |
| 哨塔维护费 `upkeepPerLevel` 按 **0 起等级**索引，以 `isDefense:true` 注册 | `BuildingUpkeep.cs:15,48-56,78` |
| `BuildingType`：`None=0 / Barracks=1 / DarkBarracks=2 / Sentry=3 / MainBase=4`；`None` 是"不用种族变体"的哨兵值 | `BuildingButton.cs:9-16` |
| 种族→建筑：`RaceDefinition.buildings`（`RaceBuildingEntry { buildingType, prefabKey }`）；**查不到会回落成建造按钮上的默认预制体** | `BuildingButton.cs:38-85` |
| **三套"等级"别混**：`BuildUP.CurrentLevel`（0 起，局内升级）/ `BuildingBase.Level`（1 起，框架显示）/ `UserGlobalInfo.*Level`（局外存档，花金条升） | `BuildUP.cs:76`、`BuildingBase.cs:18`、`UpgradeLevelRules.cs` |
| **黑暗兵营的建造前置**：本方大本营的局内等级 `BuildingBase.Level >= 2` | `UpgradeLevelRules.cs:51-62` |

---

## 六、实测发现的数据问题（不修，只报告）

打开工具就能在体检里看到，这里记一份快照：

| 建筑 | 现象 |
|---|---|
| **Cat Barracks1 / Cat Dark barracks1 / Cat Sentry tower1** | `cost` **全是 0** —— 玩家造它们免费（Mouse 是 2000 / 2500 / 1200）。两族造价明显还没调完 |
| **Cat Base1 / Mouse Base1** | `prop.buildAnime` 为空 → `StartBuild()` 直接 return → **`isCompleted` 恒为 false** |
| 两个种族的兵营/黑暗兵营/大本营 | `levels[].attack = 0`、`attackRange = {0,0}`（**正常**：它们不攻击；工具只对哨塔报这两项） |
| **Chicken 种族** | `RaceDefinition.buildings` **完全为空** → 4 个类型全缺；如果 Chicken 会作为玩家/敌方种族，建造与敌方大本营生成都会回落到按钮默认预制体 |
| `Camp` | 有 `BuildingBase`、**没有 `BuildUP`** → 中立营地，造不了也升不了级（正常） |

---

## 七、红线

1. **改 `levels[]`，不要改 `prop.maxHp / atk / atkRange`** —— 运行时会被覆盖，改了没效果（工具里已经把后者标注为"会被覆盖"）。
2. **建筑预制体是全局资产**：所有关卡、所有种族共用同一份，改一处影响所有地方。
3. **资源键必须来自注册表**，自己编的键出包加载不到。而**每级外观图是强引用**，不需要注册表。
4. **改了资源键要重新构建 Addressables**。
5. **大本营不要勾 `removable`**：它是拆除逻辑的前置，勾错会让玩家拆掉自己的大本营。
6. **`isDarkBarracks` 配错**会让敌方的黑暗兵营等级作用在普通兵营上。

---

## 八、给程序看的部分

| 文件 | 作用 |
|---|---|
| `BuildingEditorWindow.cs` | 主窗口：三栏布局、工具栏、建筑列表、三级外观对比 + 数值编辑表、各组件分区、体检面板 |
| `BuildingEditorData.cs` | 数据层：偏好、工具目录自定位、建筑扫描与归属反查、注册表索引（预制体/Sprite/音频）、`SerializedObject` 字段读取器、三级语义换算、会话与备份 |
| `BuildingEditorValidation.cs` | 体检规则 + 一键修复（单个建筑 + 种族建筑表 + 兵营跨建筑） |
| `BuildingEditorUI.cs` | 绘制工具箱（配色、Sprite 切片绘制、分区、折叠头、分割条、标签、网格/标记等） |
| `BuildingEditorKeyPickerWindow.cs` | 资源键选择窗口（预制体 / Sprite / 音频，独立可停靠、可缩放、行高可调） |

- 编译归属：`Assets/Editor/CageFightEditor.asmdef`（已引用 `CageFightRuntime`，可直接用 `BuildUP` / `BuildingBase` / `BuildingTraining` / `BuildingTowerAI` / `BuildingUpkeep` / `BuildingRemoveButton` / `BuildingHealth` / `GameObjectProperty` / `RaceDefinition` / `TroopDefinition`）。目录里**没有** asmdef，不会新增程序集。
- **写入范围**：建筑预制体（`.prefab`）。**不碰任何运行时代码，也不写种族建筑表**（那张表目前只读）。
- 运行时代码全部在**全局命名空间**（`Assets/Runtime` 下没有任何 `namespace`），所以引用游戏类型不需要 `using`。

> **为什么体检和写入全部走 `SerializedObject`**：建筑的绝大多数参数是私有 `[SerializeField]`
> （`BuildUP.removable` + 6 个拆除表现参数、`BuildingTraining.panelPrefabKey` / `avatarRenderer`、
> `BuildingTowerAI` 的那些…），直接读字段编译不过；`SerializedObject` 既不受访问级别限制，
> 又和 Inspector 的写入路径完全一致（`Ctrl+Z` 原生可用）。

### 编译校验（不开 Unity 也能验）

```powershell

> 路径**自动探测**：脚本会自己找 Unity 工程与 Unity 自带的 Roslyn（csc.dll），换机器、换目录都不用改脚本；也可显式传 `-ProjectPath` / `-CscPath` / `-SourceDir` 覆盖。
powershell -File "tools\compile-check.ps1"
```

> 不要用管道过滤这个脚本的输出；`tools\*.ps1` 必须保存为 **UTF-8 带 BOM**。

### 安装 / 卸载目录联接

```powershell
powershell -File "tools\junction.ps1" -Action status
powershell -File "tools\junction.ps1" -Action install
powershell -File "tools\junction.ps1" -Action uninstall
```

**再次改名了怎么办**：`Building Editor` 文件夹名可以随便改，但改完必须重指联接（否则 Unity 里这批脚本会整体消失、菜单也没了）：

```powershell
powershell -File "<新的工具目录>\tools\junction.ps1" -Action install -SourceDir "<新的工具目录>\BuildingVisualEditor"
```

工具的**备份目录会自动跟随工具目录**（靠 `BuildingVisualEditor\BuildingEditorData.cs` 这个标记文件识别）。

### git 排除（重要）

git 会顺着联接把工具源码当成"本工程的新文件"。工程根 `.gitignore` 里有 `!Assets/**` 这条**反排除**，
而 `.git\info\exclude` 的优先级**低于** `.gitignore`，压不过它（实测无效）。因此排除规则放在：

```
CageFight\Assets\Editor\.gitignore
    /BuildingSystem          ← 工具联接所在目录（本工具新建）
    /BuildingSystem.meta     ← Unity 会给新目录生成它，在上一层，所以规则必须放这里
    /.gitignore              ← 自己也排除自己，所以这个文件也不会进版本库
```

> 注意这一层和另外三个工具不同：兵种/图鉴/关卡编辑器的目录是**早就存在**的，而
> `Assets\Editor\BuildingSystem` 是本工具新建的，Unity 会为它生成 `BuildingSystem.meta` ——
> 那个文件在 `Assets\Editor\` 下，放在 `BuildingSystem` 里面的 `.gitignore` 管不到它，
> 所以规则必须放在上一层。`-Action install` 会自动写好，并当场用 `git status` 核对。

> 如果你希望"**别人克隆下来也自动排除**"，把下面两行手工加进工程根的 `.gitignore`：
> ```
> /Assets/Editor/BuildingSystem
> /Assets/Editor/BuildingSystem.meta
> ```

**联接已实测可用**（Unity 2022.3.58f1c1）：

- Unity 会把联接看成普通目录，正常导入全部脚本并生成 `.cs.meta`（meta 写在 `Building Editor\BuildingVisualEditor\` 里）
- Console 会有一条**黄色提示**：`Assets/Editor/BuildingSystem/BuildingVisualEditor is a symbolic link…` —— 这是 Unity 对所有链接目录的统一警告，可以忽略
- Unity **只在窗口获得焦点时**刷新资源库：改完源码后切回 Unity 窗口才会重新编译

---

## 九、故障排查

| 现象 | 原因 / 处理 |
|---|---|
| **菜单里没有「建筑可视化编辑器」** | 联接失效（工具文件夹被改名/移动）→ 跑 `tools\junction.ps1 -Action install`，然后切回 Unity 窗口 |
| 打开后列表是空的 | 点「重新扫描建筑」；确认工程里有挂了 `BuildUP` / `BuildingBase` 的预制体 |
| 改了生命/攻击，游戏里没变 | 你是不是改了 `GameObjectProperty` 上的那几个字段？**真值在 `BuildUP.levels[]`**（工具里已标注"会被覆盖"） |
| 升级后外观没变 | 该级的 `sprite` 为空 —— `ApplyLevel` 里 `if (body && data.sprite)` 才换图 |
| 建筑瞬间完工 / 兵营点不开 | 该预制体的 `buildAnime` 为空 → 不进施工流程 → `isCompleted` 恒为 false |
| 某条体检说"攻击范围为 0" | 只有**哨塔**才该报这条；兵营/大本营不攻击，范围 0 是正常的（工具已按组件区分） |
| 面板上某个兵种不出现 | 它的解锁等级不在 1..3 —— 去**兵种编辑器**改它的兵营阶级 |
| 保存后想回滚 | 工具栏「备份 ▾」→ 恢复任意一份；备份在工具目录的 `BuildingBackups` |

---

## 十、修订记录

### R1.8 — 修「一键修复修不掉空条目」与三处删元素缺陷

反馈：检查各编辑器的体检与一键修复是否正常。

发现（都是「报了修不掉」这一类的确定性缺陷）：

| 问题 | 证据 | 后果 |
|---|---|---|
| **`troops` 是对象引用数组，`DeleteArrayElementAtIndex` 第一次只把元素置空、不缩短数组** | 一键修复里删空兵种条目那处、列表里那个「删」按钮，都只调了一次 | 报「已修复 N 处」但空条目还在 → 体检继续报同一条 → **再修再报，永远修不掉**；「删」按钮点一下那一行不消失，变成 `None (Troop Definition)` |
| 「删」按钮 `break` 跳过了 `EndHorizontal()`，IMGUI 组不配对 | 同上 | 该帧组栈不平衡 |
| **`B-TROOP-NULL` 是 Error 级误报** | 文案写「运行时会抛 NullReferenceException」，但 `troops` 的每个运行时消费者都判了 null（`TroopDefinition.cs:100-103` 的 Preload、`BuildingTraining.cs:74` 的 GetTroop、`TroopDefinition.cs:191/207`、`TeamCommander.cs:1086`）| 一个无害的数据状态被报成错误，把体检计数和一键修复的 N 一起带偏 |

修法：

- 两处删元素都补上「删完看 `arraySize` 变没变，没变就再删一次」的守卫 —— 图鉴编辑器在
  `BookEditorData.cs:1035 / 1062 / 1133` 三处都写了这个守卫，本工具漏了。
  （注意 `BuildUP.levels` 是**结构体**数组，单删就是有效的，不要照抄过去。）
- 「删」按钮改成「配平 `EndHorizontal()` 后 `break`」，**刻意不用 `GUIUtility.ExitGUI()`**：
  它会把本帧直接抛出去，连带跳过外面 `Edit()` 里的 `ApplyModifiedProperties()`，删除就白做了。
  少画后面几行在 IMGUI 里是安全的（只有「多画」才会报 `Getting control N's position`）。
- `B-TROOP-NULL` 降为**警告**，文案改成真实后果（面板占一格 / 条目被跳过）。

> 通用检查办法：把每条 `Fixable = true` 的规则和修复实现**逐条对一遍** ——
> 点完一键修复之后那条规则必须不再触发，否则就是死循环。

### R1.7 — 第三轮卡顿排查：真凶是「每次读字段都 `SerializedObject.Update()`」

反馈：**其它编辑器不卡，就建筑编辑器卡。**

这次先做了一件前两轮没做的事：**横向比四个工具**。结论是这不关内容多少 —— 建筑编辑器只有 9 个预制体，
关卡编辑器有 11 个关卡却更流畅。差在**读数据的方式**：

| 工具 | 每帧路径上的 `SerializedObject.Update()` | 字段读法 |
|---|---|---|
| 关卡编辑器 | **0** | 直接读 `public` 字段（`StageConfig` / `StageObjectData` 的字段是公开的）|
| 兵种编辑器 | **0** | 只在写的时候用 SerializedObject |
| 图鉴编辑器 | 0（那 9 处全在用户操作的写路径）| 同上 |
| **建筑编辑器** | **2 个调用点，但都落在每帧 / 每次体检的读路径上** | **每读一个字段就 `Update()` 一次** |

建筑组件用 SerializedObject 是**必须的**（字段基本是私有的，工具自己的注释写了这点），
所以问题不在"用不用"，在"用法"。

**量级**（数出来的，不是估的）：

- **绘制一帧**：7 个 `Edit()` 各 Update **两次**（缓存命中时 `GetSerialized` 一次 + `Edit` 里又一次）
  = 14 次；`CurrentInjectedLevel()` 三张预览卡 3~6 次；`DrawOccupancyBlock` 2 次 → **约 19~22 次/帧**
- **一次体检**：体检函数里 **19 处** `BuildingFields.Get*` × **9 个建筑** = **171 次 `Update()` + 171 次 `FindProperty()`**；
  而编辑时每敲一个键都把体检标脏 → **每 0.2 秒重来一遍**（R1.1 加的节流只降了频率，没降单次成本）

**为什么前两轮都没抓到**（这条比修复本身更值得记）：

R1.3 自己写下了排查方法 —— 把 `AssetDatabase.*` / `File.*` / `EditorPrefs.Get*` / `FindObjectsOfType` /
`new SerializedObject` / `AssetPreview.*` 的调用点全列出来逐个查。**`SerializedObject.Update()` 不在这张清单里。**
R1.3 修的是"每帧 `new SerializedObject`"（**构造**成本），而"构造一次、每次读都重新序列化"是另一件事。
R1.3 那行改动的注释恰好露了马脚：*「复用缓存的那一份（开头 `Update()`、改动后 `ApplyModifiedProperties()`）」*
—— 当时认为"缓存 = 只构造一次"，没注意到缓存命中的 `else` 分支里还留着 `Update()`。

| 位置 | 处理 |
|---|---|
| `BuildingFields.GetSerialized()` | 缓存命中时原来无条件 `Update()`。改成**每趟 UI 对同一个对象最多刷新一次**，用新增的 `BeginPass()` 划趟 |
| `BuildingFields.BeginPass()` | 新增。`OnGUI` 开头调一次，必须排在 `DrawToolbar` / `EnsureIssues` / 三栏绘制之前 —— 它们全都要读字段 |
| `BuildingFields.GetSerializedForWrite()` | 新增。**写路径每次强制 `Update()`** —— `ApplyModifiedProperties()` 是"把整棵属性树写回对象"，树旧了会连别处刚做的改动一起覆盖。写是用户操作、频率低，这点成本无所谓 |
| `Edit()` 里那次 `serialized.Update()` | **删掉**（`GetSerialized` 刚刷过；现在走 `GetSerializedForWrite` 也只刷一次）|

效果：每帧 **约 20 → 约 5** 次 `Update()`；每次体检 **171 → 约 45**（9 个建筑各自涉及的约 5 个组件，各刷一次）。
"别处改过也准"的语义没破 —— 每趟开头刷新一次，跨窗口改动照样当场可见。

> 教训：**"构造贵"和"每次读都重新序列化"是两件事**，grep 关键词只能覆盖你想到的那个。
> 更进一步：**同一个病在四个工具里的表现可能完全不同**（这边是每帧 `Update()`，图鉴那边是 `Invalidate()` 零调用点），
> 所以"别的工具卡不卡"本身就是一个有效的诊断信号 —— 这次就是靠它定位的。

### R1.6 — 右栏可以收起了

反馈：**体检面板挡住视野。**

| 位置 | 操作 |
|---|---|
| 右栏页签行最右端的 **✕** | 收起整条右栏（连分割条一起收），中栏立刻拿到全部宽度 |
| 工具栏 **▸ 展开右栏**（只在收起时出现） | 展开回来 |
| 工具栏 **设置** 按钮、状态栏「体检 N 错」跳转 | 自动展开右栏（否则点了看不到东西） |

收起状态记在 `EditorPrefs`（`CageFight.BuildingVisualEditor.RightCollapsed`），关掉重开还是收起的。

> 展开按钮刻意放在 `DisabledScope(!_data.IsLoaded)` **外面** —— 没加载建筑时它不该变灰，否则收起之后就没有回去的路了。
### R1.5 — 修三级表格的三处排版错

反馈：显示错误。数值已经能看见了，但排版有三处不对：

| 现象 | 原因 | 处理 |
|---|---|---|
| **「范围」的 X / Y 被折到下一行** | `Vector2Int` 字段有自己的**最小宽度**（两个子框各要几十像素），挤在「生命 / 攻击」同一行时 Unity 会把子框换行 | 「攻击范围」**单独占一行**，宽度放到 200 |
| **「拆退」数值末位被切掉**（`拆退 600` 显示成 `拆退 60`，`850`→`85`，`1250`→`125`） | 那一行末尾放了 `GUILayout.FlexibleSpace()`，它和 `LabelField` **抢同一份剩余宽度**，标签只拿到一半就被裁剪 | 去掉那个 `FlexibleSpace()`，让标签拿到整段剩余宽度 |
| **第三张预览卡被裁掉一半** | 卡片里的 `LabelField` 会把**文字宽度当成最小宽度**，三张卡的文字加起来撑破了中栏 | 卡片文字改用 `GUI.Label` + `GetRect`（**不参与布局宽度**，超出就裁在卡片内），新增 `CardLine()` 统一画 |

**顺带**：把两个工具所有 `.cs` 的行尾统一成 LF —— 之前用 `WriteAllLines` 删代码时把建筑编辑器窗口转成了 CRLF，
这正是这一次多行补丁又匹配失败的原因（同一个坑第二次踩，现在做了全量体检）。

### R1.4 — 修「数值显示为空」与「看不明白」

反馈：这个页面不好理解。

**① 数值根本显示不出来（真 bug，不是文案问题）** ← 主因

截图里「生命 / 攻击 / 建造费」后面**输入框是空的**，「外观图」只剩一个小圆圈。这是 Unity 的一个坑：

> `PropertyField(..., GUILayout.Width(80))` 里的 **80 是「标签 + 输入框」的总宽**，
> 而标签宽度取的是 `EditorGUIUtility.labelWidth`（默认约 **150**）。
> 窄栏下标签会把宽度全吃掉 → **输入框宽度变成 0，数值看起来就是空的**；`外观图` 只剩缩略图那一小块。

处理：三级数值表与兵营兵种表里把 `labelWidth` 压到 **40 / 22**（画完立刻还原，绝不外泄），
字段宽度也调开让「范围」不再折行。全工程 7 处「带标签 + 固定宽度」的字段都过了一遍。

**② 文案里的 markdown 记号被原样显示**

`HelpBox` 与 `DisplayDialog` **不支持富文本**，而我写成了 `代码` / 加粗记号 —— 它们在界面上原样显示，看着像坏掉了。
处理：体检详情（走 `RichLabel`，支持富文本）统一换成 `<b>…</b>`；HelpBox 与对话框里的记号全部去掉。
三级那段的说明顺便重写成人话：

> 这一屏就是建筑的真值 —— 游戏里升级用的就是这张表。
> · 改这里的「生命 / 攻击 / 范围 / 造价」才有效；预制体上同名的那几个字段，运行时会被这张表覆盖。
> · 生命与攻击在游戏里按 ×1.1^(等级-1) 放大；攻击范围不放大，所以三级要分别填。

**③ `1423→1423` 看不出信息**：缩放倍率正好是 ×1 时不再画箭头，只在真会放大时才显示「基础→实际」。

**④ 顺手**：删掉一个没用到的 `FieldNarrow` 帮助函数（它带着同一个 labelWidth 陷阱，留着以后必踩）。

### R1.3 — 又一轮卡顿排查：找到真凶「状态栏每帧打磁盘」

反馈：建筑编辑器还是卡。

上一轮修的都是"每帧算得多"，但**真凶是每帧在做 I/O**：

| 位置 | 问题 | 处理 |
|---|---|---|
| **`SourceStamp()`（状态栏）** ← **主因** | 状态栏每帧都画「工具 2026-10-03 21:24」，而这个字符串的实现是 `AssetDatabase.FindAssets`（**资源库查询**）+ 每个脚本两次文件系统调用（`File.Exists` / `File.GetLastWriteTime`）。**每帧一次资源库查询 + 十几次磁盘 stat**；拖动、悬停时每秒几十次重绘，全在打磁盘 | **缓存**：算一次存起来，重扫/聚焦时再清。兵种编辑器早就踩过这个坑（README 原文：「状态栏版本标识、设置页偏好不再每帧查询/写盘」），这次把教训找回来了 |
| **折叠状态** | `IsCollapsed()` 每帧对每个折叠头读一次 `EditorPrefs` —— 那背后是**注册表读取** | 改成**内存缓存**，只有真正切换折叠时才写回 `EditorPrefs` |
| **`Edit()` 每帧 `new SerializedObject`** | 本窗口一帧要画 4~5 个组件，每次构造都要重建整棵属性树 | 复用 `BuildingFields` 里缓存的那一份（开头 `Update()`、改动后 `ApplyModifiedProperties()` —— Unity 自己写 Inspector 的标准做法） |
| **`GroupByRace` 每帧读 EditorPrefs** | 左栏每帧问一次分组方式 | 缓存在字段里，`OnEnable` 读、`OnDisable` 写回 |
| **选键窗口按全量请求缩略图** | `AssetPreview.GetAssetPreview` 在预览生成期间**每帧都会被重新请求**，600 行全请求就是几百次调用 | 只给**落在可视区里的行**取缩略图（关卡选键窗口只画 Sprite，本来没这个热点） |

> 排查方法：把两个工具里所有 `AssetDatabase.*` / `File.*` / `EditorPrefs.Get*` / `FindObjectsOfType` / `new SerializedObject` / `AssetPreview.*` 的调用点全部列出来逐个判断是否落在每帧路径上，而不是靠猜。

### R1.2 — 修「拖动看不到数值」

反馈：无法正常拖动导致看不到数值。

两个原因叠在一起：

| 原因 | 说明 | 处理 |
|---|---|---|
| **三级数值表一行太宽** | 原来是「级号 + 外观图 + 生命 + 攻击 + 攻击范围 + 造价」一行 ≈ **800px**，而中栏窄的时候只有 300px 出头 → 右边（也就是数值）整个被裁掉 | 每级改成**三行小组**（级号+外观图 / 生命+攻击+范围 / 造价+累计拆退），最宽一行约 340px；中栏最小宽度同时提到 **360** |
| **详情区的横向滚动被锁死** | 之前为了防止列表被横向滚走，给详情滚动视图也加了 `x = 0` —— 内容超宽时底部会出现横向滚动条，但**拖不动**，超出的部分就永远看不见 | 详情区**解除 x 锁定**（列表仍然锁，那里横向滚动确实没意义）；万一还有内容超宽，至少能拖出来看 |
| 顺带 | 三级预览卡只有三分之一栏宽，长文字会被裁；兵种行的固定宽度同理 | 预览卡文字压短（累计花费/拆退移到编辑表，那里宽度够）；兵种行固定宽度收窄，超范围提示改成小角标 |

### R1.1 — 修「用起来有点卡顿」和「拉伸很别扭」

反馈：使用起来有点卡顿，而且拉伸操作变扭。

**卡顿的三个来源（都已修）**

| 来源 | 原因 | 处理 |
|---|---|---|
| **体检里读字段的开销**（最要紧的一处） | `BuildingFields.Get*` 每次读字段都 `new SerializedObject(...)` —— 每次构造都要重建整棵属性树；而体检要对 9 个建筑 × 十几个字段逐个读，**一次体检就是几百次重建** | 按目标对象**缓存 `SerializedObject`**，读之前 `Update()` 保证拿到最新值；重扫/聚焦时清缓存 |
| **每次编辑都跑全量体检** | 编辑时每个按键都会把体检标脏，于是打字、拖数值时每一帧都在重算 | 体检**节流 0.2 秒**（`ValidateInterval`），期间只约一次稍后再算 |
| **切回窗口就全量重扫** | `OnFocus` 里做了 `Rescan()`（遍历 161 个预制体逐个取组件）+ `BuildingResourceIndex.Invalidate()`（重建 2500+ 条 Sprite 映射） | `OnFocus` **不再全量重扫**，只清实例缓存并做 3 秒防抖；真正的重扫交给工具栏「重新扫描建筑」与设置页「重新读取全部注册表」 |

**拉伸变扭的两个来源（都已修）**

| 来源 | 原因 | 处理 |
|---|---|---|
| **中栏会被挤成一条缝** | 左栏最大 460 + 右栏最大 700 = **1160，超过窗口宽度** —— 两条都拖大就没中栏了 | 新增 `ComputeColumnWidths()`：两条栏宽之和超过「窗口宽 − 中栏最小 320」时，**按各自的余量比例压缩**；拖分割条时的上限也跟着收敛 |
| **分割条难抓、没有反馈** | 命中区只有 5px，光标只在里面才变，而且永远是一条暗线 | 命中区左右各外扩 3px（画出来还是细线，但好抓）；悬停/拖动时**线变亮加粗**；拖动时置 `GUI.changed` 让画面跟上 |

> **同样的两处修复也应用到了关卡编辑器**（`LevelEditorUI.Splitter` + `LevelEditorWindow` 的 `ComputeColumnWidths` / 体检节流），因为两个工具是同一套代码，问题一模一样。

### R1 — 建筑可视化编辑器（首版）

- **三级外观对比 + 三级数值编辑表**（本工具的核心）：每级的外观图、生命、攻击、攻击范围、造价、累计花费、拆除返还；会攻击的建筑带 `× 1.1^(等级-1)` 换算
- **建筑列表**：按种族或按类型分组，行内一级外观缩略图 + 类型/种族 + 「N 级 / 无等级表」角标 + 问题角标
- **分区编辑**：占地与施工、兵营（兵种表可增删）、哨塔（射击与维护费）、升级与拆除表现、谁在用这个建筑
- **体检**：40+ 条规则，覆盖单个建筑 / 种族建筑表 / 兵营跨建筑三类；一键修复只做安全确定性修正
- **资源键选择窗口**：预制体 / Sprite / 音频三类，可停靠、可缩放、行高可调
- 工程外备份（按资产名分组保留）、`EditorPrefs` 偏好、离机编译校验、目录联接安装脚本
- 全部字段读写走 `SerializedObject`，`Ctrl+Z` 原生可用、不受私有 `[SerializeField]` 限制

**关键设计判断（都有实测依据）**：

1. **攻击/攻击范围只对哨塔报错** —— 实扫发现兵营、黑暗兵营、大本营的 `levels[].attack` 与 `attackRange` **全是 0**，而 `ApplyLevel` 会无条件把 `prop.atkRange` 写成它。对这些建筑 0 是正常值，一律报就是满屏误报。
2. **界面明确标注"prop 上的 maxHp/atk/atkRange 会被覆盖"** —— 实测 `Mouse Barracks1` 的 prop.maxHp=100 而 `levels[0].maxHp=2000`，策划很容易改错地方。
3. **施工特效键为空单独给告警** —— 它不是"少个特效"，而是 `StartBuild()` 直接 return、`isCompleted` 恒为 false，会连带兵营面板打不开、哨塔不开火；实测两个大本营正是这个情况。
4. **种族建筑表只读** —— 表缺条目（如 Chicken 全缺）会报错，但"该用哪个预制体"只有人知道，工具不替你做这个决定。