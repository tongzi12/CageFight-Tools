# 工程师可视化编辑器（Engineer Visual Editor）

给策划用的**工程师编辑器**：改玩家可选的工程师（定义资产 + 选装登记 + 局内数值），
看得见图标/立绘/待机帧，也看得见「为什么进游戏没有工程师」。

- 代码位置（唯一真源）：`GitHub\Engineer Editor\EngineerVisualEditor\`
- 接入方式：`CageFight\Assets\Editor\EngineerSystem\EngineerVisualEditor` 是一个**目录联接（junction）**，指向上面的源码目录。工程里没有多出任何实体脚本文件，**游戏源码一行不改**（接入/卸载见 `tools\junction.ps1`）。
- 管的数据：`Assets\RemoteResource\LoadoutDefinitions\*.asset`（`EngineerDefinition`）+ `Assets\RemoteResource\LoadoutDefinitionRegistry.asset` 里的一条登记 + 它 `prefabKey` 指到的**单位预制体**上的局内数值。

> 与**关卡 / 建筑 / 图鉴 / 兵种 / 法术 / 等级**六个编辑器同一套做法与同一套视觉语言。

---

## 一、怎么打开

| 方式 | 操作 |
|---|---|
| 菜单 | Unity 顶部菜单 **工具 → 工程师可视化编辑器**（快捷键 `Ctrl + Alt + E`） |

首次安装（或工具目录改过名/移动过）之后，**切回 Unity 让它获得焦点**才会编译出菜单项；
联接状态用 `powershell -File tools\junction.ps1 -Action status` 查。

---

## 二、界面导览

```
┌───────────────────────────── 工具栏 ─────────────────────────────┐
│ 重新扫描 | 保存 (Ctrl+S) | 备份一份 | 定位资产 | ● 未保存 | N 错误 N 警告 │
├──────────────┬───────────────────────────────────────────────────┤
│ 工程师列表    │ 标识与资源键 │ 立绘与待机帧 │ 局内数值（预制体） │ 选装注册表 │ 体检 │
│ 图标+名字+id  │                                                   │
│ 已登记/未登记 │  每个页签都是「字段 + 校验角标 + 出处说明」        │
├──────────────┴───────────────────────────────────────────────────┤
│ 状态栏：已保存 / 备份文件名 / 修复结果 …                          │
└──────────────────────────────────────────────────────────────────┘
```

| 页签 | 内容 |
|---|---|
| **标识与资源键** | `id` / `displayName`、`iconKey`（Sprite）、`prefabKey`（预制体，**这条为空/无效 = 进游戏没有工程师**）、自带法术下拉（来自注册表法术表） |
| **立绘与待机帧** | `portraitFrameKey` 静态立绘、`idlePortraitFrameKeys[]`（增删 / 改键 / 上下移 / 逐个键选择器）、`idlePortraitFrameRate` 滑条 + **立绘预览**（自动回退：待机帧 → 静态立绘 → 小图标） |
| **局内数值（预制体）** | `EngineerController` 的生命/移速/击退抗性/相机视野/濒死击飞/修复建筑/移动参数名/施法锚点 + `GameObjectProperty` 的等级上下文。**改的是预制体数据，且在预制体上统一保存（写前备份整个 .prefab）** |
| **选装注册表** | 该资产是否已登记、一键加入/移出、默认工程师/种族/法术槽的现状与「设为默认」 |
| **体检** | 逐条结论 + 「定位」跳页签 + 单条修复 + 一键修复 |

---

## 三、数据体检（本工具最值钱的部分）

每条规则都对着运行时的同一条链路写的，出处写在问题详情里。

### 错误（会导致「进游戏没有工程师」或「界面里没有它」）

| 规则 | 依据 |
|---|---|
| **没有登记进选装注册表** | 选装界面遍历的就是注册表列表（`LoadoutSelectionPanel.cs:230/241` → `ResourceManager.EngineerDefinitions`）→ 界面里看不到它；存档按 ID 解析，解析失败时 `PlayerLoadoutSpawner` 报「未能解析当前工程师」并返回 false（`PlayerLoadoutSpawner.cs:35-39`）→ **这一局没有工程师**。可一键修复（加进注册表） |
| **prefabKey 为空 / 不在 PrefabRegistry** | 进游戏时工程师预制体按注册表预载（`PlayerLoadoutManager.cs:183` `LoadRegisteredGameObject`）；取不到时 `GetGameObject` 返回 null → `PlayerLoadoutSpawner` 打「工程师预制体未预载或对象池未就绪」并返回 false（`:43-48`）→ **这一局没有工程师** |
| **prefabKey 指向的资产取不到** | 键在注册表里但资产被删/改名 → 预载失败，同上 |
| **预制体上没有 EngineerController** | `PlayerLoadoutSpawner.cs:83-90` 拿到预制体立刻要它 → 缺了就 LogError + 把实例还给对象池 → **这一局没有工程师** |
| **预制体上没有 GameObjectProperty** | `EngineerController` 声明了 `[RequireComponent(GameObjectProperty)]`（`EngineerController.cs:7`），但那只在编辑器加组件时生效 |
| **id 为空 / 与其它工程师重复** | 注册表 `AddEngineers` 跳过空 ID、重复 ID 只留第一个并打警告（`LoadoutDefinitionRegistry.cs:110-115`）→ 定义选不到，存档也存不住 |

### 警告（配置与运行时行为不一致）

| 规则 | 依据 |
|---|---|
| `iconKey` 为空 / 不在 SpriteRegistry | 列表与界面图标空白（`GetSprite` 返回 null） |
| `portraitFrameKey` 为空 / 不在 SpriteRegistry | 菜单立绘为空 —— 为空时会**回退到 iconKey**（`EngineerMenuPortrait.cs:135-140`） |
| **自带法术为空 / ID 不在注册表法术表里** | 局内槽位 0 = 工程师自带法术（`PlayerLoadoutManager.cs:151/161/184`）；解析不到就没有这一格，施法预制体也不会被预载（`LoadoutSelectionPanel.cs:242-243`） |
| 待机帧里有**空项** / 键不在 SpriteRegistry | 空项那一帧不显示；键无效那一帧丢图。空项可一键修复（清掉），无效键**只报告**（可能只是刚改名还没登记） |
| `idlePortraitFrameRate` 超出 1~20 | 字段上的 `[Range(1,20)]` 只夹 Inspector；运行时按原值算（`EngineerMenuPortrait.cs:107`） |
| **移动参数名与动画控制器对不上** | `EngineerController` 用 `SetBool(moveParameter)`（`:113/174`），控制器里没有同名 bool/trigger → **移动动画不播**。工具会读控制器参数列表并逐个列出来 |
| 注册表默认工程师为空 / ID 无效 | `TryGetDefaultLoadout` 要求工程师与种族都有效（`LoadoutDefinitionRegistry.cs:81-84`）→ 新存档没有默认选择 |

### 提示

- 没配待机帧（只显示静态立绘）、没配 `spellAnchor`（回退自身 transform，法术从脚底发出）、没有 `Shoot point` 子节点（抛射法术起点回退到施法锚点）、没有 Animator/控制器（完全没有动画）
- `EngineerController.maxHp` 与 `GameObjectProperty.maxHp` 不一致：**工程师的血量池是前者**（`EngineerController.cs:117/125/183`），后者在这个单位上不参与结算 —— 两个数不一样容易看错
- **prefabKey 不在图鉴目录里**：出战工程师走的是 `BookProgress.MarkOwned(engineer.PrefabKey)`（`PlayerLoadoutSpawner.cs:51`），图鉴按 `BookEntry.prefabKey` 匹配（`BookCatalog.cs:100-112`）→ 这个键不在目录里，出战它**不会点亮任何图鉴条目**

### 一键修复（只做安全确定性修复）

- 把该工程师**补进选装注册表**（只动工程师那条列表，不碰种族/法术表）
- 清掉待机帧数组里的**空项**、把帧率**夹回 1~20**
- 默认工程师为空/无效时**设为当前这个**

**不会做**：删任何资源键、改 id、删工程师、动没报错的字段。

---

## 四、保存 / 备份 / 撤销

- 改动先进 Unity 的内存（`SerializedObject` 会话 / 预制体字段），**点「保存 (Ctrl+S)」才写盘**
- 写盘前**自动备份**：定义资产与/或整个 `.prefab` 各一份 → `GitHub\Engineer Editor\EngineerBackups\`（保留份数在 `EngineerEditorPrefs.BackupKeep`，默认 20，按资产名分组保留）
- **没有整窗口的 Ctrl+Z**：改的是资产文件，用备份回退（备份文件名带时间戳与原因，一眼能认出）
- 切换工程师时有未保存改动会先问

---

## 五、已核实的运行时事实（界面上的判断都基于这些）

| 事实 | 出处 |
|---|---|
| 工程师 = 定义资产（id/名字/图标键/预制体键/自带法术/立绘与待机帧）+ 注册表里的一条登记 | `EngineerDefinition.cs`、`LoadoutDefinitionRegistry.cs:11/26` |
| 生成流程：`PlayerLoadoutSpawner` 解析当前工程师 → 按 `PrefabKey` 取预制体 → 要求 `EngineerController` → 出生在我方大本营正面外沿 | `PlayerLoadoutSpawner.cs:31-94` |
| 默认选择由注册表决定（工程师 + 种族都要有效） | `LoadoutDefinitionRegistry.TryGetDefaultLoadout:70-85` |
| 局内法术槽位：0 = 工程师自带，1/2 = 玩家选的两个 | `PlayerLoadoutManager.cs:151-161`、`LoadoutSelectionPanel.cs:83`（写入槽位） |
| 菜单立绘优先级：待机帧动画 → 静态立绘 → 小图标 | `EngineerMenuPortrait.cs:84-140` |
| 血量池是 `EngineerController.maxHp`（不是 GameObjectProperty 的） | `EngineerController.cs:117/125/183` |
| 移动动画靠 `SetBool(moveParameter)`，默认参数名 `IsMoving` | `EngineerController.cs:36/113/174` |
| 射击点取名为 `Shoot point` 的子节点，取不到回退施法锚点；施法锚点为空回退自身 | `EngineerController.cs:63-82/116` |
| 工程师会治疗附近友方建筑（半径 + 每秒百分比 + 结算间隔） | `EngineerController.cs:252-286` |

> **打开工具时的实测现状（5 个工程师）**：`Bababoyi` / `Broom cat` / `Kende` / `MouseEngineer` / `Omniscient`
> 都已登记、图标/立绘/预制体键**全部有效**；
> **3 个没有配自带法术**（Broom cat / Kende / Omniscient 的 `innateSpellId` 为空）；
> **5 个都没有配待机帧**（菜单里只显示静态立绘）；
> 注册表默认工程师 = `mouse_engineer_1`（有效）。

---

## 六、红线

| 操作 | 后果 |
|---|---|
| 改 `id` | 存档里的 `selectedEngineerId` 是按 id 记的 → 老存档选不中这个人（进关没有工程师）。要改就先想好存档兼容 |
| 把工程师移出注册表 | 选装界面里没有了；正选着它的存档进关就没有工程师 |
| 把 `prefabKey` 改成没登记进 PrefabRegistry 的键 | 工程师预制体预载失败 → 这一局没有工程师 |
| 直接改预制体上的 `EngineerController.maxHp` 却不改 `GameObjectProperty.maxHp`（或反之） | 不影响运行，但两处数字不一致会误导后来的人（工具会提示） |

---

## 七、给程序看的部分

| 文件 | 作用 |
|---|---|
| `EngineerEditorData.cs` | 只读索引（六张资源注册表 + 选装注册表 + 图鉴目录）、工程师目录（`EngineerCatalog`/`EngineerRecord`）、备份（`EngineerWriter`）、预制体访问（`EngineerPrefabAccess`，含读取动画控制器 bool 参数）、EditorPrefs、工具目录定位 |
| `EngineerEditorValidation.cs` | 体检规则（`EngineerValidator.Validate`）+ 一键修复（`ApplySafeFixes`） |
| `EngineerEditorWriters.cs` | 选装注册表写入（`EngineerRegistryWriter`：加入/移出/设默认，**只动工程师那条列表**）与定义资产写入（`EngineerAssetWriter`：走 `SerializedObject`，字段都是 private `[SerializeField]`） |
| `EngineerEditorWindow.cs` | 主窗口：工具栏 / 左栏列表 / 五个页签 / 状态栏 |
| `EngineerEditorKeyPickerWindow.cs` | 资源键选择窗口（搜索 + 定位注册表） |
| `EngineerEditorUI.cs` | 绘制工具箱（与另外六个编辑器同一套配色与控件，含 Sprite 绘制） |

> 路径**自动探测**：脚本会自己找 Unity 工程与 Unity 自带的 Roslyn（csc.dll），换机器、换目录都不用改脚本；也可显式传 `-ProjectPath` / `-CscPath` / `-SourceDir` 覆盖。
| `tools\compile-check.ps1` | 编译闸：用 Unity 自带 Roslyn 对着工程真实引用编译；同时检查源码卫生（`.cs` 行尾全 LF 无 BOM、`tools\*.ps1` 带 BOM），卫生不过退出码 2 |
| `tools\junction.ps1` | 目录联接安装/状态/卸载，并在 `Assets\Editor\` 写一份「自己排除自己」的 `.gitignore`，保证工具源码不进游戏仓库 |

---

## 八、故障排查

| 现象 | 处理 |
|---|---|
| 菜单里没有「工程师可视化编辑器」 | 联接断了：`tools\junction.ps1 -Action status`，无效就跑 `-Action install` |
| 列表是空的 | 工程里没有 `EngineerDefinition` 资产，或者资产没被 Unity 导入完 |
| 「键无效」但资产明明在 | 注册表还没重新生成：点工具栏「重新扫描」；仍旧无效就是**键确实没登记进注册表** |
| 改了没生效 | 没点「保存 (Ctrl+S)」；或者改的是另一个工程师（看左栏高亮） |
| 进游戏还是没有工程师 | 看「体检」页的两条**错误**：注册表登记 + prefabKey 预载链；再确认玩家存档里的 `selectedEngineerId` 与这里的 `id` 一致（用「等级编辑器」看存档） |

---

## 九、修订记录

### R1.1 — 初版：工程师定义 / 选装登记 / 局内数值三处一屏管完

需求：**做一个工程师编辑器，不动游戏源码**。

查清的运行时链路（都写进代码注释与体检详情了）：

| 事实 | 出处 |
|---|---|
| 工程师能不能在界面上出现，取决于**选装注册表**里有没有登记 | `LoadoutSelectionPanel.cs:230/241` → `ResourceManager.EngineerDefinitions` |
| 工程师能不能生成，取决于 `prefabKey` **在 PrefabRegistry 且预载成功** + 预制体上有 `EngineerController` | `PlayerLoadoutManager.cs:183`、`PlayerLoadoutSpawner.cs:43-48/83-90` |
| 自带法术是局内槽位 0，走注册表的法术表 | `PlayerLoadoutManager.cs:151-161/184` |
| 菜单立绘是「待机帧 → 静态立绘 → 小图标」三级回退 | `EngineerMenuPortrait.cs:84-140` |
| 工程师的血量池是 `EngineerController.maxHp`，不是 GameObjectProperty 的 | `EngineerController.cs:117/125/183` |

| 改动 | 说明 |
|---|---|
| 新工具 `Engineer Editor`（第 7 个可视化编辑器） | 源码在仓库外，靠目录联接挂进 `Assets\Editor\EngineerSystem\EngineerVisualEditor`；**游戏源码一行不改** |
| 五页签字表 + 左栏工程师列表（带图标与登记角标） | 标识与资源键 / 立绘与待机帧（含预览与帧序列编辑）/ 局内数值（预制体）/ 选装注册表 / 体检 |
| 体检 15 条规则（6 条错误 + 8 条警告 + 提示） | 其中「未登记」「prefabKey 无效」「缺 EngineerController」三条直指**进游戏没有工程师**这个静默失效 |
| 安全写入 | 定义走 `SerializedObject`（字段 private）、预制体走组件公开字段；两者写盘前都备份；注册表只动工程师那条列表（不整表替换） |
| 资源键选择窗口 | 避免手输键导致静默失效；带搜索与「定位注册表」 |

> **怎么验的**：编译闸 ✔（6 个源文件、238 个引用、0 error）+ 源码卫生 ✔（行尾全 LF / 无 BOM / 脚本自带 BOM）；
> 打开工具时的体检结论已用**工程真实数据**预演过：5 个工程师全部已登记、键全部有效，**3 个没配自带法术、5 个没配待机帧**（详见第五节）。
> 过程中踩到一个坑并已修：**用 Windows PowerShell 的 `Get-Content` 读无 BOM 的 UTF-8 源码会按 ANSI(GBK) 解码**，
> 一次「读改写」就能把中文源码变成非法 UTF-8（编译报一堆「常量中有换行符」）——
> 本工具的所有脚本与源码在写入后都做了 **UTF-8 合法性 + 行尾**校验，方法见 `tools\compile-check.ps1`。
