# 图鉴可视化编辑器（Book Visual Editor）

给**不懂技术的策划**用的图鉴内容编辑器：改文字、换图、调星级、看效果，全程不用碰代码、不用拖资产、不用记键名。

- 代码位置（唯一真源）：`GitHub\Illustration Editor\BookVisualEditor\`
- 接入方式：`CageFight\Assets\Editor\BookSystem\BookVisualEditor` 是一个**目录联接（junction）**，指向 `Illustration Editor\BookVisualEditor`。工程里没有多出任何实体脚本文件，也没有改动任何原有文件。
- 面向的游戏功能：`BookCatalog` 图鉴（派系页签 / 12 格格子 / Card 详情卡）

---

## 一、怎么打开

三种方式，任选其一：

| 方式 | 操作 |
|---|---|
| 菜单 | Unity 顶部菜单 **工具 → 图鉴可视化编辑器**（快捷键 `Ctrl + Alt + B`） |
| 资产右键 | Project 面板里右键 `BookCatalog.asset` → **打开图鉴可视化编辑器** |
| 自动 | 在 Project 面板点选 `BookCatalog.asset`，已打开的工具窗口会自动切到这个资产 |

默认资产路径：`Assets/Resource/LocalResource/Book/BookCatalog.asset`
（就是游戏里 `BookController.catalog` 引用的那个资产。）

---

## 二、界面导览

```
┌──────────────────────── 工具栏 ────────────────────────┐
│ 资产框  保存  撤销  全选显示项  整理顺序   体检计数  一键修复  表格  备份  设置 │
├──────────┬──────────────────────────┬──────────────────┤
│  左栏     │        中栏               │      右栏         │
│ 条目列表  │      点选即改的表单         │  预览 / 体检 / 设置 │
│ 搜索筛选  │  基本信息 / 图片 / 数值 / 简介 │  12格图鉴页 / Card  │
│ 勾选批量  │                          │                  │
├──────────┴──────────────────────────┴──────────────────┤
│ 状态栏：条目数 · 问题数 · 是否已保存 · 最近一次操作结果            │
└────────────────────────────────────────────────────────┘
```

- **右栏可以收起**：右栏页签行最右端有 **✕** —— 收起整条右栏（连分割条一起收，中栏立刻拿到全部宽度）；收起后工具栏会出现 **▸ 展开右栏**。收起状态记在 EditorPrefs 里，关掉重开还是收起的。

左右两栏的分隔条可以**拖动**调宽度。

---

## 三、常见任务怎么做

### 1. 改一个角色的名字 / 简介 / 卡面数值
1. 左栏找到这个角色（用搜索框打字最快，支持角色名 / ID / 资源键 / 简介）
2. 点中它
3. 中栏直接改「角色名」「Card 简介」「Card 数值文本」六行
4. 右栏 **Card 预览** 立刻能看到卡面文字
5. 点工具栏 **保存 (Ctrl+S)**

### 2. 换图鉴头像 / Card 立绘
- 中栏「图片资源」区域：
  - **搜索选图**：打开一个**可任意缩放 / 最大化 / 停靠的选键窗口**（标题栏写明正在填哪个字段、哪个条目）。输入关键字（例如 `Miner rat`、`Troop UI_0`）即可筛选，带缩略图，**点一行即填入**；窗口不关可以连续给多个字段选键，还能拖「行高」滑块把缩略图放大看清细节
  - **按单位键猜一个 / 智能匹配图片**：按优先级自动补全 ——
    1. **训练配置（`TroopDefinition`）里的 `iconKey` / `animationSpriteKey`**（最可靠：图鉴格子的头像本来就是兵营那个图标，这 44 个兵种全都填了）
    2. 按单位名前缀猜（`<单位名>-attack_0` → `<单位名>_0` → 同前缀任意帧）
    3. 头像兜底：从「`<种族> Troop UI` 图集」里挑一个**还没被其它条目用过**的图标
    补全后状态栏会写明每个键是**从哪来的**；一个都没匹配上也会说明原因，不会静默什么都不做
  - **清空**：把该字段清空
- 也可以**把 Sprite 直接从 Project 面板拖到左边的小图上**，工具会自动反查并填好资源键（键必须已在 `SpriteRegistry` 里）
- 键有效会显示绿色「键有效」，无效显示红色提示 —— 无效键出包后会加载失败，体检也会报错

### 3. 新增一个角色
1. 左栏点某个派系标题右边的 **＋**（会加到该派系），或用底部的「＋ 新增条目」
2. 填「角色名」
3. 选「所属派系」「种族图标」「星级」（都是下拉，不会填错）
4. 填「单位预制体键」→ 用右边 **搜索** 按钮选（**没有单位键的条目永远不会解锁**）
5. 点「按单位键智能匹配两张图」补图片，或手动搜索选图
6. 数一下六行数值 + 简介
7. 保存

### 4. 用 Excel 批量改 / 批量新增（推荐给量大的活）
- **导出全部条目到 CSV**：UTF-8 带 BOM，Excel / WPS 双击直接打开，中文不乱码
- **导出空白模板**：给不清楚列含义的人照着填
- **从 CSV 导入**：三种模式可选，都会先弹出预览（将更新几条、新增几条、哪些列缺失、有哪些需要注意的行），确认后才写入
  | 模式 | 行为 |
  |---|---|
  | 按 ID 更新已有条目 + 追加新行（推荐） | 表里 ID 已存在→更新；ID 是新的→新增 |
  | 只更新已有条目 | 表里的新 ID 会被忽略，适合只改文案 |
  | 清空后完全按表格重建 | 先删除当前全部条目再重建（额外弹一次确认，仅当表里包含全部内容时才用） |
  - 匹配键是**「唯一 ID」**，所以表格里的 ID 列不能乱改
  - **空单元格 = 保持原值**，不会把内容清空
  - 导入前自动备份，导入后可 `Ctrl+Z` 撤销

### 5. 批量改一批条目
1. 在左栏条目行前面**勾选**（或先筛选出目标集合，点工具栏「全选显示项」）
2. 点左栏底部 **批量操作 ▾**：
   - 移入派系 / 设置种族图标 / 设置星级 / 智能匹配图片
3. 批量前会自动备份

### 6. 上线前体检
右栏 **数据体检** 页签，或看工具栏的「体检 N 错 / N 警 / N 提示」。能查出：

| 级别 | 例子 |
|---|---|
| 错误 | 缺唯一 ID、ID 重复、派系不在页签列表、星级越界、头像/立绘/单位键在注册表里不存在 |
| 警告 | 缺角色名、缺单位键（永远不解锁）、同一单位被多条共用、缺图片键 |
| 提示 | 六行数值空、简介空、立绘用的是 move 帧、页签下没有条目 |

- **一键修复**：只做安全的确定性修复（补 ID、补角色名、修正派系/种族、修正能唯一确定的失效资源键、把星级夹到 1-7），不删任何条目，可 `Ctrl+Z`
- 每条问题都有「定位」按钮，直接跳到对应条目
- **导出报告**：把体检结果存成 txt，方便发给程序

### 7. 预览效果（右栏）
- **图鉴页预览**：完全复刻游戏内 Book 面板 —— 每页 12 格、页签切换、`◀ 上一页 / 下一页 ▶`、未解锁盖「未解锁」、已解锁但未拥有显示灰白
  - 「解锁状态模拟」可切换：全部已解锁 / 解锁但未拥有（灰白）/ 全部上锁 / 只解锁前一半
  - 点预览里的格子可以直接选中那个条目
- **Card 预览**：用条目数据画一张卡面示意图（立绘 / 名字 / 星级 / 种族 / 六行数值 / 简介），稳定可靠——游戏里用的就是这套数据
  - ⚠️ **「用真实 Card 预制体渲染」已收起**（实验性，暂未调通：在 URP + 预览场景里手动渲染 UI 得到的是一片空白）。入口已从预览页撤掉，只保留在「设置」页，且默认关闭；需要排查时可打开看诊断信息。

### 8. 撤销 / 备份 / 恢复
- 所有改动都在 Unity 的 Undo 里，`Ctrl+Z` 可回退（工具栏也有「撤销」）
- **保存前自动备份**（默认开）：每次保存 / 批量操作 / 导入 CSV / 一键修复前，都会把当前资产文件复制到
  `GitHub\Illustration Editor\BookBackups\BookCatalog_日期_时间_原因.asset`
- **备份 ▾ → 恢复：xxx** 可以回滚到任意一份备份（恢复前会先自动备份当前内容）
- 备份目录在**工程之外**，不会被 Unity 导入，也不会进包、进版本库
- 旧备份的清理：**每个资产**保留最新 N 份（N 在设置页改，默认 60）；只清理**认得出是备份**的文件
  （`资产名_yyyyMMdd_HHmmss_原因.asset`），其它 `.asset` 一律不碰 —— 备份目录被指到 `Assets`
  下面时也不会误删真正的资产文件

---

## 四、红线（会让线上出问题的操作）

1. **不要随便改「唯一 ID」**：玩家的解锁/拥有状态是按 ID 存的，改 ID = 老玩家这条图鉴变回未解锁。
2. **资源键必须来自注册表**：头像/立绘键必须在 `SpriteRegistry`、单位键必须在 `PrefabRegistry` 里。自己编的键编辑器里能填，但出包后加载不到（体检会报「键无效」）。
3. **单位预制体键要填**：没填的条目玩家永远解锁不了。
4. **一个单位只对应一条图鉴**：两条共用同一个单位键，遇见时两条会一起解锁。
5. 保存后记得让程序知道（改了资源键的话，可能需要重新生成资源注册表 / 重新构建；纯文字和星级改动不需要）。

---

## 五、给程序看的部分

### 文件清单（都在 `Illustration Editor\BookVisualEditor\`）
| 文件 | 作用 |
|---|---|
| `BookVisualEditorWindow.cs` | 主窗口：三栏布局、工具栏、列表、表单、批量操作、CSV/备份流程 |
| `BookEditorData.cs` | 数据层：加载 BookCatalog、条目增删改移排序、保存、备份恢复；`BookResourceIndex` 资源键索引 |
| `BookEditorValidation.cs` | 体检规则 + 一键修复 |
| `BookEditorCsv.cs` | CSV 导出/导入（RFC4180 解析、UTF-8 BOM / GBK 兼容、先预览后应用） |
| `BookEditorPreview.cs` | 图鉴页预览 + Card 示意图 +（已收起的）真实 Card 预制体渲染 `BookCardRenderer` |
| `BookEditorUI.cs` | 绘制工具箱（Sprite 图集切片绘制、分割条、分区、星级等） |
| `BookKeyPickerWindow.cs` | 资源键搜索选择窗口（独立可停靠、可任意缩放） |

编译归属：`Assets/Editor/CageFightEditor.asmdef`（该 asmdef 已引用 `CageFightRuntime`，所以能直接用 `BookCatalog` / `BookEntry` / `CardView` / `SpriteRegistry` / `PrefabRegistry`）。目录里**没有** asmdef，不会产生新的程序集。

### 与运行时代码的关系
- **只读**使用运行时类型，未修改 `BookCatalog.cs` / `BookController.cs` / `CardView.cs` 等任何既有文件
- 写数据只写 `BookCatalog.asset` **一个资产文件**（这是策划改内容的唯一落点，且保存前自动备份）
- 编辑器偏好存在 `EditorPrefs`（前缀 `CageFight.BookVisualEditor.`）

### 编译校验（不开 Unity 也能验）
```powershell

> 路径**自动探测**：脚本会自己找 Unity 工程与 Unity 自带的 Roslyn（csc.dll），换机器、换目录都不用改脚本；也可显式传 `-ProjectPath` / `-CscPath` / `-SourceDir` 覆盖。
powershell -File tools\compile-check.ps1
```
用 Unity 自带的 Roslyn（`Editor\Data\DotNetSdkRoslyn\csc.dll`）+ `CageFightEditor.csproj` 里真实的引用列表 + `Library\ScriptAssemblies\CageFightRuntime.dll` 编译一遍，产物落在 `Illustration Editor\.compile-check\`。**不启动 Unity、不写工程任何文件。**

> 两个小坑：
> 1. 不要用管道过滤这个脚本的输出（例如 `| Select-Object -Last 5`），受限环境下管道会让编译器进程起不来，脚本会误报失败。直接运行即可。
> 2. `Illustration Editor\tools\*.ps1` 必须保存为 **UTF-8 带 BOM**，否则 Windows PowerShell 5.1 会按本地代码页解析中文而报语法错。

### 安装 / 卸载目录联接
```powershell
# 查看状态 / 安装 / 卸载（脚本已备好）
powershell -File tools\junction.ps1 -Action status
powershell -File tools\junction.ps1 -Action install
powershell -File tools\junction.ps1 -Action uninstall

# 等价的手工命令
New-Item -ItemType Junction -Path "<CageFight 工程目录>\Assets\Editor\BookSystem\BookVisualEditor" `
         -Target "BookVisualEditor"
Remove-Item "<CageFight 工程目录>\Assets\Editor\BookSystem\BookVisualEditor" -Force   # 只删联接，不删源码
```

**再次改名了怎么办（重要）**

`Illustration Editor` 这个文件夹名可以随便改，但**改完必须重新指向联接**，否则 Unity 里这批脚本会整体消失（菜单也会跟着没了）：

```powershell
# 会自己修正联接目标（把路径换成改名后的工具目录）
powershell -File "tools\junction.ps1" -Action install
# 或者在任意位置显式指定源码目录：
powershell -File "<工具目录>\tools\junction.ps1" -Action install -SourceDir "<新的工具目录>\BookVisualEditor"
```

> 源码、`.cs.meta`（GUID）都不会丢，重新指向后 Unity 一获得焦点就会重新导入、编译。
> 工具的**备份目录会自动跟随工具目录**（改名后自动找到新位置），不需要手工改配置。

**联接已实测可用**（Unity 2022.3.58f1c1）：
- Unity 会把联接看成普通目录，正常导入全部脚本并生成 `.cs.meta`（这些 meta 写在 `Illustration Editor\BookVisualEditor\` 里）
- Console 会有一条**黄色提示**：`Assets/Editor/BookSystem/BookVisualEditor is a symbolic link. Using symlinks in Unity projects may cause your project to become corrupted...` —— 这是 Unity 对所有链接目录的统一警告。本项目只有一个引用、没有递归链接，可以忽略；如果看着不舒服，改用「一键复制」方式（把 `Illustration Editor\BookVisualEditor` 整个拷到 `Assets\Editor\BookSystem\BookVisualEditor`，删掉联接）即可，功能完全一样，代价是以后每次改源码要重新拷一次。
- Unity 只在**窗口获得焦点**时会刷新资源库：改完源码后切回 Unity 窗口，它才会重新编译。

### 改动范围核对（本次会话）
- 新增：`Illustration Editor\BookVisualEditor\*.cs`（7 个源文件）、`Illustration Editor\tools\*.ps1`（2 个脚本）、`Illustration Editor\README_图鉴编辑器.md`
- 新增：目录联接 `CageFight\Assets\Editor\BookSystem\BookVisualEditor` + Unity 生成的 `BookVisualEditor.meta`
- `git status` 里唯一与本工具相关的条目就是上面这个联接目录；工程里其它 `M` 状态的文件（`ProjectVersion.txt`、`packages-lock.json`、`PackageManagerSettings.asset`）是 **11:17 打开 Unity 时 Unity 自己改的**，与本工具无关

---

## 六、故障排查

| 现象 | 原因 / 处理 |
|---|---|
| **编辑器菜单突然消失了** | 多半是工具文件夹被改名/移动，联接目标失效。跑一次 `tools\junction.ps1 -Action install`（或 `-Action status` 看目标是否存在），然后切回 Unity 窗口 |
| 菜单里没有「图鉴可视化编辑器」 | 工程还没编译完 / 有编译错误。看 Console，或先跑一次 `tools\compile-check.ps1` |
| 打开后左边是空的 | 没加载资产。把 `BookCatalog.asset` 拖到工具栏的资产框，或点欢迎页的「加载默认图鉴资产」 |
| 头像/立绘在游戏里不显示，但预览有 | 预览用的是编辑器直引（`avatarSprite`/`roleSprite`），运行时只认资源键。体检会报「键无效」，按提示改成注册表里的真实键 |
| 体检说「找不到 SpriteRegistry」 | 资源注册表还没生成，先执行工程里的一键生成资源注册表流程 |
| 打包后图鉴没变 | 确认保存过（工具栏保存按钮、状态栏不再显示「有未保存的修改」） |
| 真实 Card 渲染黑屏/空白 | 该功能已收起（默认关闭），用示意图即可；若要排查，去「设置」页打开实验开关，诊断行会给出渲染管线自检结果 |

---

## 七、当前会话的改动范围声明

本次工作**只新增**，没有修改 `CageFight` 工程里的任何既有文件：

- 新增：`Illustration Editor\BookVisualEditor\*.cs`（7 个源文件）
- 新增：`Illustration Editor\tools\compile-check.ps1`
- 新增：目录联接 `CageFight\Assets\Editor\BookSystem\BookVisualEditor`（原项目里只多一个链接节点）
- 运行时/编辑器脚本、场景、预制体、ProjectSettings、Packages **均未改动**

## 八、修订记录

### R2 — 修「旧备份被错误淘汰」（会悄悄吃掉你的回退点）

**问题**：`TrimBackups()` 是"整个目录按文件名排序、删掉前 N 个"。而文件名是
`资产名_yyyyMMdd_HHmmss_原因.asset` —— **资产名前缀主导排序，时间戳根本轮不到**。
所以删掉的不是最旧的备份，而是"资产名靠前的那个资产"的备份，并且会一路删到它一份不剩，
另一个资产更老的备份反而留着。备份是这工具唯一的回退点（批量操作前也会自动备份），
它被悄悄吃掉意味着"出事时想回滚，发现没得回"。

**第二个洞**：清理用 `Directory.GetFiles(folder, "*.asset")` 通配，**不校验文件名**。
而备份目录可以在设置页改成任意文件夹（无二次确认）—— 指到 `Assets\RemoteResource\Stages`
之类的资产目录再触发一次备份，就会按份数上限**删掉那里的真正资产文件**。

**修法**（与建筑 / 兵种编辑器同一套做法，那两个工具早有这个修复）：

| 改动 | 说明 |
|---|---|
| 按**资产名**分组，各自保留最新 N 份 | 分组内前缀相同、时间戳定宽 → 名字序就是时间序 |
| 只清理**认得出是备份**的文件 | `资产名_yyyyMMdd_HHmmss_原因.asset`；认不出的一律不碰，真资产文件天然认不出 |
| 设置项文案改成「**每个资产**保留份数」 | 语义从"全目录 N 份"变成"每个资产 N 份"，文案必须跟上，否则这个选项是误导的 |
| `ListBackups()` 的注释改成实话 | 它本来就是按文件名倒序、不是按时间倒序（只改文档，没改行为 —— 改排序有可能让用户原本能翻到的备份从菜单前 25 项里掉出去） |

> 教训和「同源工具只修了一半」那条一样：这个修复在建筑 / 兵种编辑器里早就有了，
> **图鉴和关卡编辑器漏了**。四个工具是四份复制粘贴、没有共享层，任何修复都得手工同步四遍。

### R1 — 右栏可以收起了

反馈：**体检面板挡住视野。**

| 位置 | 操作 |
|---|---|
| 右栏页签行最右端的 **✕** | 收起整条右栏（连分割条一起收），中栏立刻拿到全部宽度 |
| 工具栏 **▸ 展开右栏**（只在收起时出现） | 展开回来 |
| 工具栏 **设置**（`ShowSettingsMenu`） | 自动展开右栏 |

收起状态记在 `EditorPrefs`（`BookEditorPrefs.RightCollapsed`），关掉重开还是收起的。

> 展开按钮放在 `DisabledScope(!_data.IsLoaded)` **外面** —— 没加载图鉴时它不该变灰，否则收起之后就没有回去的路了。