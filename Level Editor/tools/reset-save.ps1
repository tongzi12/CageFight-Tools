# CageFight 玩家全局存档 —— 等级归零 / 补测试用货币（开发用脚本，随时可删）
#
# 为什么需要它：
#   · 升级面板的等级**只增不减**（UpgradeLevelRules.TryUpgrade 只做 +1，没有降级入口）；
#   · UserGlobalInfo.ResetToDefault() 虽然存在，但**全工程没有任何调用方**（没有重置按钮）；
#   所以想反复测"从 1 级开始升级"的流程，只能改存档文件。
#
# 存档在哪（Editor / Device Simulator 都一样）：
#   Application.persistentDataPath/SaveData/user_global_info.json
#   Windows Editor = %USERPROFILE%\AppData\LocalLow\JXX\CageFight\SaveData\user_global_info.json
#   出处：LocalJsonSaveStorage.cs:11-29（目录 + .json 后缀）、UserGlobalInfoPersistence.cs:15（文件名 user_global_info）
#
# 用法（默认只预览，不写盘）：
#   powershell -File reset-save.ps1                          # 预览：三个建筑等级 → 1
#   powershell -File reset-save.ps1 -Apply                    # 写盘（写前自动备份）
#   powershell -File reset-save.ps1 -Apply -GoldBars 100000 -Diamonds 100000
#   powershell -File reset-save.ps1 -Apply -MagicLevel 0      # 顺带把两个魔法等级归零（它们本来就允许 0）
#   powershell -File reset-save.ps1 -Apply -Wipe              # 删掉整份存档 = 全部回默认
#
# ⚠ 写盘前先在 Unity 里**停止 Play**：游戏每有数据变更就会在 0.5 秒后回写这个文件
#   （UserGlobalInfoPersistence.HandleChanged → SaveRoutine），Play 中改会被它覆盖掉。
#
# ⚠ 三个建筑等级**不能写 0**：UserGlobalInfo.TryValidate 要求 barracksLevel / darkBarracksLevel /
#   sentryTowerLevel >= 1，写 0 会让**整份存档校验失败**（TryDeserializeFromJson 返回 false）
#   → 游戏内存里保留默认值 = 等级 1/1/1 **且货币清零、通关进度清空**。所以本脚本把最小值钉在 1。
#   （真想要"全清"，用 -Wipe。）

param(
    [switch]$Apply,
    [switch]$Wipe,
    [ValidateRange(1, 999)][int]$Level = 1,
    [ValidateRange(-1, 999)][int]$MagicLevel = -1,
    [int]$GoldBars = -1,
    [int]$Diamonds = -1,
    [string]$Path
)

$ErrorActionPreference = 'Stop'

if (-not $Path) {
    $Path = Join-Path $env:USERPROFILE 'AppData\LocalLow\JXX\CageFight\SaveData\user_global_info.json'
}

function Get-Field([string]$json, [string]$name) {
    $m = [regex]::Match($json, '"' + $name + '"\s*:\s*(-?\d+)')
    if ($m.Success) { return $m.Groups[1].Value }
    return '(缺)'
}

if ($Wipe) {
    if (-not (Test-Path $Path)) {
        Write-Host "存档本来就不存在：$Path" -ForegroundColor Yellow
        exit 0
    }

    Write-Host "整份存档将被删除（等价于全部回默认：等级 1/1/1、货币 0、图鉴与通关进度清空）：" -ForegroundColor Yellow
    Write-Host "  $Path" -ForegroundColor Yellow
    if (-not $Apply) {
        Write-Host "预览模式：没有删任何东西。加 -Apply 才会真删。" -ForegroundColor Cyan
        exit 0
    }

    $backup = [System.IO.Path]::ChangeExtension($Path, $null).TrimEnd('.') + '.backup_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.json'
    Copy-Item $Path $backup
    Remove-Item $Path
    Write-Host "已删除。备份：$backup" -ForegroundColor Green
    exit 0
}

if (-not (Test-Path $Path)) {
    Write-Host "找不到存档：$Path" -ForegroundColor Red
    Write-Host "（还没进过 Play 就会没有这份文件 —— 先跑一次游戏再改。）" -ForegroundColor Red
    exit 1
}

$before = Get-Content $Path -Raw
$age = [int]((Get-Date) - (Get-Item $Path).LastWriteTime).TotalSeconds
if ($age -lt 60) {
    Write-Host "⚠ 这个文件 $age 秒前刚被写过：很可能 Unity 正在 Play。" -ForegroundColor Yellow
    Write-Host "  请先停止 Play 再改，否则游戏会在 0.5 秒后把旧值写回来。" -ForegroundColor Yellow
}

$after = $before
$after = [regex]::Replace($after, '"barracksLevel"\s*:\s*-?\d+', '"barracksLevel":' + $Level)
$after = [regex]::Replace($after, '"darkBarracksLevel"\s*:\s*-?\d+', '"darkBarracksLevel":' + $Level)
$after = [regex]::Replace($after, '"sentryTowerLevel"\s*:\s*-?\d+', '"sentryTowerLevel":' + $Level)
if ($MagicLevel -ge 0) {
    $after = [regex]::Replace($after, '"defenseMagicLevel"\s*:\s*-?\d+', '"defenseMagicLevel":' + $MagicLevel)
    $after = [regex]::Replace($after, '"attackMagicLevel"\s*:\s*-?\d+', '"attackMagicLevel":' + $MagicLevel)
}
if ($GoldBars -ge 0) { $after = [regex]::Replace($after, '"goldBarCount"\s*:\s*-?\d+', '"goldBarCount":' + $GoldBars) }
if ($Diamonds -ge 0) { $after = [regex]::Replace($after, '"diamondCount"\s*:\s*-?\d+', '"diamondCount":' + $Diamonds) }

Write-Host "存档：$Path" -ForegroundColor Cyan
$fields = @('barracksLevel', 'darkBarracksLevel', 'sentryTowerLevel', 'defenseMagicLevel', 'attackMagicLevel', 'goldBarCount', 'diamondCount')
foreach ($f in $fields) {
    $b = Get-Field $before $f
    $a = Get-Field $after $f
    $mark = '  '
    if ($b -ne $a) { $mark = '<=' }
    Write-Host ("  {0,-20} {1,8}  ->  {2,8}  {3}" -f $f, $b, $a, $mark)
}

if ($after -eq $before) {
    Write-Host "没有需要改的（等级已经是 $Level）。" -ForegroundColor Green
    exit 0
}

if (-not $Apply) {
    Write-Host "预览模式：文件没有被修改。加 -Apply 才会写盘（写前自动备份）。" -ForegroundColor Cyan
    exit 0
}

$backup = [System.IO.Path]::ChangeExtension($Path, $null).TrimEnd('.') + '.backup_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.json'
Copy-Item $Path $backup
[System.IO.File]::WriteAllText($Path, $after, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "已写盘。备份：$backup" -ForegroundColor Green
Write-Host "现在进 Play 测试即可：三个建筑等级都是 $Level（升级一次 = 100 金条 × 当前等级）。" -ForegroundColor Green