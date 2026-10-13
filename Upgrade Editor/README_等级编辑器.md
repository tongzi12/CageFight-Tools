# 等级编辑器（Upgrade Visual Editor）

给策划/测试用的**玩家成长数据编辑器**：直接改游戏内玩家等级、货币、出战选择、图鉴与通关进度，
全程不用碰代码、不用记字段名、也不用去翻 AppData。

- 代码位置（唯一真源）：`GitHub\Upgrade Editor\UpgradeVisualEditor\`
- 接入方式：`CageFight\Assets\Editor\UpgradeSystem\UpgradeVisualEditor` 是一个**目录联接（junction）**，指向上面的源码目录。工程里没有多出任何实体脚本文件，**游戏源码一行不改**（接入/卸载见 `tools\junction.ps1`）。
- 管的数据：**玩家全局存档** `Application.persistentDataPath\SaveData\user_global_info.json`（Windows Editor = `%USERPROFILE%\AppData\LocalLow\JXX\CageFight\SaveData\`）—— 这一份文件在 **Unity 工程之外**，工具只读工程里的定义资产（下拉候选），只写这一份 JSON。

> 与**关卡编辑器 / 建筑编辑器 / 图鉴编辑器 / 兵种编辑器 / 法术编辑器**同一套做法与同一套视觉语言。

---

## 一、怎么打开

| 方式 | 操作 |
|---|---|
| 菜单 | Unity 顶部菜单 **工具 → 等级可视化编辑器**（快捷键 `Ctrl + Alt + P`） |

窗口打开时会自动读一次存档。**Unity 正在 Play 时写入按钮会被禁用**（原因见第五节）。

> 首次安装（或工具目录改过名/移动过）之后，**切回 Unity 让它获得焦点**：Unity 会自动导入并编译，
> 菜单里才会出现「等级可视化编辑器」。联接状态用 `powershell -File tools\junction.ps1 -Action status` 查。

---

## 二、界面导览

```
┌────────────────────────────── 工具栏 ──────────────────────────────┐
│ 重新读取 | 保存 (Ctrl+S) | 备份一份 | 打开目录 |  ● 有未保存的修改  │
├────────────────────┬───────────────────────────────────────────────┤
│ 快捷动作与体检      │ ① 等级与货币 │ ② 出战选择 │ ③ 图鉴与进度 │ ④ 备份与对比 │
│ （可收起 / 可拖宽） │                                               │
│  · 等级归零         │  存档信息（路径 / 状态 / 版本）                │
│  · 补货币           │  玩家等级：三个建筑 + 两个魔法 + 总等级换算     │
│  · 图鉴全解锁       │  货币 / 设置与进度                            │
│  · 清空通关进度     │                                               │
│  · 体检列表         │                                               │
├────────────────────┴───────────────────────────────────────────────┤
│ 状态栏：已保存 / 写入失败 / 体检提示 …                              │
└────────────────────────────────────────────────────────────────────┘
```

| 区块 | 内容 |
|---|---|
| **工具栏** | 重新读取（有改动会先问）、保存、立即备份、在资源管理器里打开存档目录；右侧实时显示「● 有未保存的修改」 |
| **快捷动作** | 测试常用的一键操作：三个建筑等级改成 N、**等级归零**、补货币、图鉴全解锁+全拥有、清空图鉴、清空通关进度、把 1~N 关标记为已通关、全部回默认值。全部**只改内存**，点「保存」才落盘（破坏性操作都有二次确认） |
| **体检** | 逐条列出发现的问题（错误 / 警告 / 提示），每条都能「定位」到对应页签，可修的给一个「修复：…」按钮；顶部「一键修复（N 处）」批量执行安全修复 |
| **① 等级与货币** | 存档信息（路径/时间/版本）、**三个建筑等级 + 两个魔法等级**（带 −/＋ 与合法范围）、货币、音量、伤害显示开关、已解锁关卡 |
| **② 出战选择** | 工程师 / 种族 / 法术槽 1 / 法术槽 2 / 大本营预制体键 —— 候选从工程资产里**现扫**（EngineerDefinition / RaceDefinition / SpellDefinition / 各部落 MainBase 键），找不到当前值时会标「← 不在工程里」 |
| **③ 图鉴与进度** | 按 BookCatalog 条目逐条勾选「解锁 / 拥有」（带搜索）+ 目录里没有的 ID 单独列出；通关进度两个列表（点编号移除、超出工程范围的编号标 ✗） |
| **④ 备份与对比** | 备份保留份数、写盘是否缩进、立即备份、备份列表（时间/文件名/大小）；选中一份可以**与当前逐字段对比**（变化的行高亮）或**恢复这份备份** |

---

## 三、数据体检（本工具最值钱的部分）

每条规则都对着运行时的同一条校验写的，出处写在问题详情里。

### 错误（**整份存档会被游戏丢弃**）

这是本工具最要紧的一类结论：`UserGlobalInfo.TryValidate` 是**整体校验**，
任何一条不过 → `TryDeserializeFromJson` 返回 false → 进游戏时这份存档**被丢弃**，
内存退回默认值（等级 1/1/1、货币 0、图鉴与通关进度全空），
之后任何一个变更还会把这些默认值**写回文件** —— 等于存档被清空。

| 规则 | 依据 |
|---|---|
| 三个建筑等级 < 1 | `barracksLevel` / `darkBarracksLevel` / `sentryTowerLevel` 要求 **≥ 1**（`UserGlobalInfo.TryValidate`）。**注意：写 0 不是"归零"，是"整份失效"** —— 想归零就写 1 |
| 两个魔法等级 < 0 | `attackMagicLevel` / `defenseMagicLevel` 要求 **≥ 0** |
| 货币 < 0 | `goldBarCount` / `diamondCount` 要求 **≥ 0** |
| 音量越界 / NaN | `volume` 必须是 0~1 的有效数值 |
| 已解锁关卡 < 1 | `unlockedStage` 要求 **≥ 1** |
| 选装 ID 超过 128 字符 | `AreLoadoutIdsValid`（`MaxLoadoutIdLength = 128`） |
| **版本高于当前支持版本** | `TryMigrate` 直接报「存档版本 N 高于当前支持版本 M」→ 拒绝加载 |

### 警告（配置与运行时行为不一致）

| 规则 | 依据 |
|---|---|
| **Unity 正在 Play** | 游戏进关时**只读一次**存档（`UserGlobalInfoPersistence.LoadRoutine`），而运行中任何数据变更都会在 0.5 秒后把**内存里的旧值**写回文件（`HandleChanged → SaveRoutine`）→ Play 中改文件既看不到效果，又会被覆盖。工具在 Play 中直接禁用写入 |
| 存档 60 秒内被写过 | 可能刚从 Play 退出、也可能游戏还在跑 —— 别在这时候改 |
| 选装 ID 在工程里找不到 | 工程师 / 种族 / 法术 ID 解析不到定义资产，选装这一项失效 |
| **大本营预制体键不在任何种族的建筑表里** | 运行时**先**按所选种族的 `Buildings` 表里 MainBase 条目解析，种族没配才回落这个键（`StageObjectInstantiator.ResolveRaceMainBaseKey:203-223`）；一旦走到回落那步、而该键又没被预载，就是 LogError → **整关实例化失败（进不去关）**（`:129-136`、`LoadingState.cs:87-91`） |
| 图鉴列表里有 BookCatalog 之外的 ID | 对应的图鉴格子永远不会亮；工具**只报告，不擅自删**（改名/条目删除都可能造成） |
| 通关列表里有工程里不存在的关卡编号 / 重复编号 | `IsStageCleared` 只判「在不在列表里」，重复不影响判定但会让统计虚高；工程外的编号说明关卡改过号或已删 |

### 提示

- 存档还不存在（没进过 Play 就没有这份文件）—— 可直接按默认值新建
- 魔法等级 0：注入建筑时是 `Mathf.Max(1, …)`（`StageObjectInstantiator.cs:164-165`），所以 0 与 1 在建筑那条路上没区别
- **已解锁关卡 `unlockedStage` 运行时没有任何读取方**：选关不是按它锁的（只按 Addressables 地址连续性），改它不改变可玩关卡
- 法术槽为空是**正常状态**（玩家没选那一格时就是空的）
- 图鉴「已拥有但未解锁」：解锁与拥有是两套独立记录，正常流程下获得即视为解锁

### 一键修复（只做安全确定性修复）

- 越界值**夹回合法范围**（等级夹到 ≥1、魔法/货币夹到 ≥0、音量夹到 0~1、已解锁关卡夹到 ≥1）
- 超长选装 ID **截断**到 128；存档版本号补齐到当前版本
- 通关列表**去重**（**不删**工程外的编号）
- 列表字段的空引用兜底
- **不会做**：删图鉴/通关条目、改「只报告」的那几条、动任何没报错的字段

---

## 四、保存 / 撤销 / 备份

- **写盘范围**：只有那一份存档 JSON，UTF-8 无 BOM，字段名与游戏一致（用的是运行时的 `UserGlobalInfoData`，不是自造镜像类 —— 游戏改字段这里会编译报错，不会悄悄写出一份缺字段的存档）
- **每次写盘前自动备份**到 `GitHub\Upgrade Editor\SaveBackups\user_global_info_yyyyMMdd_HHmmss_原因.json`，保留份数在「④ 备份与对比」里改
- **没有 Ctrl+Z**：改的是工程外的一份普通文件，Unity 的 Undo 栈管不到它。替代做法是**④ 里的备份**：选中一份 → 「与当前对比」看清差异 → 「恢复这份备份」（恢复前会把当前存档再备份一次，原因记为「恢复前」）
- 「全部回默认值」= 游戏默认存档（等级 1/1/1、货币 0、列表全空），同样是先改内存、点保存才落盘

---

## 五、已核实的运行时事实（界面上的换算都基于这些）

| 事实 | 出处 |
|---|---|
| 存档位置 = `Application.persistentDataPath/SaveData/user_global_info.json` | `LocalJsonSaveStorage.cs:11-29`（目录名 `SaveData` + `.json`）、`UserGlobalInfoPersistence.cs:15`（`saveFileName = "user_global_info"`） |
| 只在**进游戏时读一次**；数据一变 → 0.5 秒后回写 | `UserGlobalInfoPersistence.LoadRoutine`（`loadStarted` 只跑一次）、`HandleChanged → StartSaveDelayed(saveDelay = 0.5f)` |
| 写盘前会把 `schemaVersion` 钉到当前版本（现在是 6） | `UserGlobalInfo.SerializeToJson` / `UserGlobalInfoData.CurrentSchemaVersion` |
| 版本迁移：0→1→…→6，其中 **5→6 把三个建筑等级的 0 统一抬到 1** | `UserGlobalInfo.TryMigrate` |
| 版本 ≤1 的货币字段叫 `coinCount`（迁移时读进 `goldBarCount`） | `UserGlobalInfo.cs:447-454` |
| **总等级 = 三个建筑等级的最低值**，法术伤害按 ×1.1^(总等级-1) 结算 | `UserGlobalInfo.TotalLevel`、`ScaleDamageByTotalLevel`；调用方 `EngineerSpellCaster.cs:18`、`MagicEnergySing.cs:218`、`UnstableOreMagicProjectile.cs:258` |
| 局内建筑等级 = 存档等级（+ 全局规则 Strengthen2 的 +2），魔法等级注入时 `Mathf.Max(1, …)` | `StageObjectInstantiator.cs:164-173` |
| **升级一次的花费 = 当前等级 × 100 金条**（魔法用钻石同名公式） | `UpgradeLevelRules.BaseCostPerLevel / BaseDiamondCostPerLevel`、`GetUpgradeCost` |
| 升级只增不减：游戏里**没有任何降级/重置入口** | `UpgradeLevelRules.TryUpgrade` 只做 `level + 1`；`UserGlobalInfo.ResetToDefault()` 全工程无调用方 |
| 我方大本营预制体：**先查所选种族 Buildings 表的 MainBase 条目**，没有才用存档里的 `selectedRaceMainBasePrefabKey` | `StageObjectInstantiator.ResolveRaceMainBaseKey:203-223`、`LoadingState.cs:84-87` |
| 通关记录的唯一读取方是结算界面判重（防重复领奖） | `GameOverRewards.cs:121-122` → `UserGlobalInfo.IsStageCleared` |
| `unlockedStage` **没有读取方** | 全工程搜索：只有 getter/setter 与校验，没有消费方 |

---

## 六、红线（会让线上出问题的操作）

| 操作 | 后果 |
|---|---|
| 把三个建筑等级写成 **0** | 不是"归零"，是**整份存档校验失败** → 游戏丢弃存档、退回默认（等级 1/1/1 + 货币清零 + 图鉴与通关进度清空）。工具的最小值是 1 |
| Play 中写盘 | 写了不生效，且会被游戏回写覆盖 —— 工具已禁用该按钮 |
| 把大本营预制体键改成工程里不存在的键 | 种族没配 MainBase 条目时才会用到它；一旦用到且该键没预载 → **进不去关**。所以下拉只给工程里真实挂着的键 |
| 手改这份 JSON | 字段名/版本写错就会触发上面第一条。用工具改，或改完先点「重新读取」看体检 |

---

## 七、给程序看的部分

| 文件 | 作用 |
|---|---|
| `UpgradeEditorData.cs` | 存档定位 / 读 / 写 / 备份 / 恢复 / **逐字段对比**；路径与备份目录定位（`UpgradePaths`）；EditorPrefs（`CageFight.UpgradeVisualEditor.`）；下拉候选扫描（`UpgradeOptions`，只读 AssetDatabase） |
| `UpgradeEditorValidation.cs` | 体检规则（`UpgradeValidator.Validate`）+ 一键修复（`ApplySafeFixes`）+ 页签常量（`UpgradeTab`） |
| `UpgradeEditorWindow.cs` | 主窗口：工具栏 / 左栏（快捷动作 + 体检）/ 右栏四个页签 + 状态栏 |
| `UpgradeEditorUI.cs` | 绘制工具箱（与另外五个编辑器同一套配色与控件） |

> 路径**自动探测**：脚本会自己找 Unity 工程与 Unity 自带的 Roslyn（csc.dll），换机器、换目录都不用改脚本；也可显式传 `-ProjectPath` / `-CscPath` / `-SourceDir` 覆盖。
| `tools\compile-check.ps1` | **编译闸**：用 Unity 自带 Roslyn 对着工程真实引用编译源码，不启动 Unity、不改工程任何文件；同时检查源码卫生（`.cs` 行尾全 LF 无 BOM、`tools\*.ps1` 带 BOM），卫生不过退出码 2 |
| `tools\junction.ps1` | 目录联接的安装 / 状态 / 卸载；并在 `Assets\Editor\UpgradeSystem\` 写一份「自己排除自己」的 `.gitignore`，保证工具源码不会被提交进游戏仓库 |

---

## 八、故障排查

| 现象 | 处理 |
|---|---|
| Unity 菜单里没有「等级可视化编辑器」 | 联接断了：`powershell -File tools\junction.ps1 -Action status` 看目标是否有效，无效就跑 `-Action install` |
| 窗口说「还没读到存档」 | 这份文件是玩过才有：先在 Unity 里 Play 一次（进到选关/菜单即可），回到编辑器点「重新读取」 |
| 改了值但游戏里没变化 | ① 是不是没点保存？② 是不是 Play 中改的？存档只在**进入游戏时读一次** → 停止 Play、改完、再 Play |
| 存档被游戏改回去了 | Play 中游戏数据有变更 → 0.5 秒后回写。先停止 Play 再改 |
| 备份目录空了 | 「保留份数」被改小了（默认 40）。改大即可，旧备份已经按规则清理，无法找回 |

---

## 九、修订记录

### R1.1 — 初版：直接改玩家全局存档的成长数据

需求：**「主要自由修改游戏内玩家的等级」**，并且**不可动游戏源文件**。

查清的事实（都写进代码注释了）：

| 事实 | 出处 |
|---|---|
| 等级/货币/图鉴/进度**全部只存在一份 JSON 里**，而且在工程之外 | `LocalJsonSaveStorage.cs:11-29`、`UserGlobalInfoPersistence.cs:15` |
| 游戏里**没有任何**降级或重置入口 | `UpgradeLevelRules.TryUpgrade` 只 +1；`UserGlobalInfo.ResetToDefault()` 无调用方 |
| 等级是**真的会影响战斗**的：总等级（三项最低）→ 法术伤害 ×1.1^(总等级-1) | `UserGlobalInfo.ScaleDamageByTotalLevel` + `EngineerSpellCaster.cs:18` 等 |
| 三个建筑等级**不能写 0**：会让整份存档校验失败（等于全清） | `UserGlobalInfo.TryValidate` |

| 改动 | 说明 |
|---|---|
| 新工具 `Upgrade Editor`（第 6 个可视化编辑器） | 源码在仓库外，靠目录联接挂进 `Assets\Editor\UpgradeSystem\UpgradeVisualEditor`；**游戏源码一行不改、工程资产一个不写** |
| 序列化用运行时的 `UserGlobalInfoData` | 不造镜像类：游戏改字段这里会编译报错，不会悄悄写出一份缺字段的存档；同时按游戏同一套规则处理 `schemaVersion` 与版本 ≤1 的 `coinCount` 迁移 |
| 四页签编辑界面 | ① 等级与货币（**主角色：三个建筑 + 两个魔法**，带总等级/缩放倍率/升级花费换算）② 出战选择（候选从工程资产现扫）③ 图鉴与进度 ④ 备份与对比（逐字段 diff + 恢复） |
| 体检 16 条规则 + 一键修复 | 其中 7 条是「**整份存档会被游戏丢弃**」级别（等级<1、货币<0、音量越界、版本过高…），每条都写了运行时代码出处 |
| 写盘纪律 | 写前自动备份、Play 中禁用写入、文件刚被写过会提醒；**没有 Ctrl+Z**（改的是工程外文件），用备份对比/恢复替代 |

> **怎么验的**（编译通过≠逻辑对）：把 `UpgradeEditorData.cs` 与 `UpgradeEditorValidation.cs` 这两份**真文件**
> （不是副本）连同一套最小桩（含一个反射版 `JsonUtility`、可注入假资产的 `AssetDatabase`）
> 编进一个独立控制台程序，跑 6 个场景、**60+ 条断言全部通过**：
> ① 工具目录按标记文件定位、备份目录在工程之外；② 写入 → 读回后 20 个字段逐一相等、字段名与游戏一致（`goldBarCount`，不含旧名 `coinCount`）；
> ③ 备份/列表/恢复（含「恢复前」自动备份）都对；④ 版本 1 的 `coinCount` 正确迁移进 `goldBarCount`；
> ⑤ 16 条体检规则逐条命中、一键修复后 9 个非法值全部回到合法且**只报告的那几条仍在**、一份合法存档 0 错误；
> ⑥ 逐字段对比表 20 个字段、只有真正变化的 4 个被标出。
> 另外工具侧编译闸 ✔（4 个源文件）+ 源码卫生 ✔（行尾全 LF / 无 BOM / 脚本自带 BOM）。
