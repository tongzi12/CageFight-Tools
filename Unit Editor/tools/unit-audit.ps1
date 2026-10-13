# 兵种数据体检（离线版）
#
# 作用：不打开 Unity，直接读 TroopDefinitions 与「兵种预制体」，
#       把数值/资源键问题列出来。规则与 UnitVisualEditor/UnitEditorValidation.cs 保持一致。
#
# 用法：powershell -File unit-audit.ps1
#
# 注意：本文件必须保存为「UTF-8 带 BOM」，否则 Windows PowerShell 5.1 会读错中文。

param(
    [string]$ProjectPath = ""
)


# ── 路径自动探测（换机器 / 换目录都不用改脚本）──────────────────────────────
# 命令行显式传参优先；探测不到会报错并给出示例，绝不静默用一个不存在的路径继续跑。
function Get-ToolRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Get-ToolSourceDir {
    $root = Get-ToolRoot
    $hit = Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue |
           Where-Object { $_.Name -like "*VisualEditor*" } | Select-Object -First 1
    if ($hit) { return $hit.FullName }
    return $null
}

function Test-UnityProject {
    param([string]$Path)
    if (-not $Path -or -not (Test-Path $Path)) { return $false }
    return ((Test-Path (Join-Path $Path "Assets")) -and (Test-Path (Join-Path $Path "ProjectSettings\ProjectVersion.txt")))
}

# 找 Unity 工程：当前目录 → 工具目录 → 工具目录的上一级（通常是 GitHub 根），各看一眼子目录
function Get-ProjectPath {
    $toolRoot = Get-ToolRoot
    # 依次看：当前目录 → 工具目录 → 工具目录的上一级 → 再上一级
    # （最后一级是为了"所有工具放在一个仓库里"的布局：<GitHub>\CageFight-Tools\<工具>\tools）
    $up1 = Split-Path $toolRoot -Parent
    $up2 = if ($up1) { Split-Path $up1 -Parent } else { $null }
    $roots = @((Get-Location).Path, $toolRoot, $up1, $up2)
    foreach ($root in $roots) {
        if (Test-UnityProject $root) { return (Resolve-Path $root).Path }
        if (-not (Test-Path $root)) { continue }
        foreach ($child in (Get-ChildItem -Path $root -Directory -ErrorAction SilentlyContinue)) {
            if (Test-UnityProject $child.FullName) { return $child.FullName }
        }
    }
    return $null
}

# 找 Unity 自带的 Roslyn：扫 Unity Hub 各版本目录，取版本号最大的一个
function Get-CscPath {
    $found = @()
    $hubs = @("$env:ProgramFiles\Unity\Hub\Editor",
              "${env:ProgramFiles(x86)}\Unity\Hub\Editor",
              "$env:ProgramFiles\Unity\Editor")
    foreach ($hub in $hubs) {
        if (-not (Test-Path $hub)) { continue }
        foreach ($ver in (Get-ChildItem -Path $hub -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending)) {
            $csc = Join-Path $ver.FullName "Editor\Data\DotNetSdkRoslyn\csc.dll"
            if (Test-Path $csc) { $found += $csc }
        }
    }
    if ($found.Count -gt 0) { return $found[0] }
    return $null
}

# ── 解析（显式传参优先）──
if (-not $ProjectPath) { $ProjectPath = Get-ProjectPath }

# 显式传进来的路径也要校验：否则错误会拖到后面才以"找不到 csproj"的形式暴露出来
if ($SourceDir -and -not (Test-Path $SourceDir)) { throw "-SourceDir 指向的目录不存在：$SourceDir" }
if ($ProjectPath -and -not (Test-UnityProject $ProjectPath)) { throw "指定的 -ProjectPath 不是 Unity 工程（缺少 Assets 或 ProjectSettings\ProjectVersion.txt）：$ProjectPath" }

if (-not $ProjectPath) { throw "找不到 Unity 工程。请在工程同级目录下运行，或用 -ProjectPath ""D:\path\to\CageFight"" 显式指定。" }
$ErrorActionPreference = "Stop"

$prefabRoot = Join-Path $ProjectPath "Assets\RemoteResource\Prefabricated body"
$defRoot    = Join-Path $ProjectPath "Assets\RemoteResource\TroopDefinitions"
$remote     = Join-Path $ProjectPath "Assets\RemoteResource"

$excludedFolderNames = @("Build", "UI", "Vfx", "Magic")   # 子弹目录按「名字含 Bullet」判断
$barracksRaces = @("Mouse", "Cat", "Chicken")

$errors = New-Object System.Collections.Generic.List[string]
$warns  = New-Object System.Collections.Generic.List[string]
$infos  = New-Object System.Collections.Generic.List[string]

# ---------- 1. 由 .cs.meta 解析组件脚本 guid ----------
function Get-ScriptGuid([string]$scriptName) {
    $meta = Get-ChildItem (Join-Path $ProjectPath "Assets\Runtime") -Recurse -Filter "$scriptName.cs.meta" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $meta) { return $null }
    foreach ($line in [System.IO.File]::ReadAllLines($meta.FullName)) {
        if ($line -match '^guid:\s*([0-9a-f]{32})') { return $Matches[1] }
    }
    return $null
}

$guidProp     = Get-ScriptGuid "GameObjectProperty"
$guidTexture  = Get-ScriptGuid "StageTexture"
$guidAnimator = Get-ScriptGuid "StageAnimatorController"
$guidAudio    = Get-ScriptGuid "StageAudio"

# ---------- 2. 注册表键集合 ----------
function Get-RegistryKeys([string]$file) {
    $keys = New-Object System.Collections.Generic.HashSet[string]
    $path = Join-Path $remote $file
    if (-not (Test-Path $path)) { return $keys }
    foreach ($line in [System.IO.File]::ReadAllLines($path)) {
        if ($line -match '^\s*-\s*key:\s*(.*)$') { [void]$keys.Add($Matches[1].Trim()) }
    }
    return $keys
}

$spriteKeys = Get-RegistryKeys "SpriteRegistry.asset"
$prefabKeys = Get-RegistryKeys "PrefabRegistry.asset"
$audioKeys  = Get-RegistryKeys "AudioRegistry.asset"
$acKeys     = Get-RegistryKeys "AnimatorControllerRegistry.asset"

# ---------- 3. 解析预制体组件 ----------
function Get-PrefabComponents([string]$path) {
    $result = @{}
    $curGuid = $null
    $fields = @{}
    foreach ($l in [System.IO.File]::ReadAllLines($path)) {
        if ($l -match '^--- ') {
            if ($curGuid -and -not $result.ContainsKey($curGuid)) { $result[$curGuid] = $fields }
            $curGuid = $null; $fields = @{}; continue
        }
        if ($l -match 'm_Script: \{fileID: 11500000, guid: ([0-9a-f]{32})') {
            $curGuid = $Matches[1]; $fields = @{}; continue
        }
        if ($curGuid -and $l -match '^  ([A-Za-z_][A-Za-z0-9_]*):\s?(.*)$') {
            $fields[$Matches[1]] = $Matches[2].Trim()
        }
    }
    if ($curGuid -and -not $result.ContainsKey($curGuid)) { $result[$curGuid] = $fields }
    return $result
}

function Get-Num([hashtable]$fields, [string]$key, [double]$fallback = 0) {
    if (-not $fields -or -not $fields.ContainsKey($key)) { return $fallback }
    $v = 0.0
    if ([double]::TryParse($fields[$key], [ref]$v)) { return $v }
    return $fallback
}

function Test-Excluded([string]$fullPath) {
    $rel = $fullPath.Substring($prefabRoot.Length).TrimStart('\', '/')
    $segments = $rel -split '[\\/]'
    for ($i = 0; $i -lt $segments.Count - 1; $i++) {
        $seg = $segments[$i]
        if ($seg -match 'Bullet') { return $true }
        foreach ($name in $excludedFolderNames) { if ($seg -ieq $name) { return $true } }
    }
    return $false
}

# ---------- 4. 扫兵种预制体 ----------
$units = @{}
$skipped = 0

foreach ($file in (Get-ChildItem $prefabRoot -Recurse -Filter *.prefab)) {
    if (Test-Excluded $file.FullName) { $skipped++; continue }

    $comps = Get-PrefabComponents $file.FullName
    $prop = if ($guidProp -and $comps.ContainsKey($guidProp)) { $comps[$guidProp] } else { $null }
    if (-not $prop) { $skipped++; continue }   # 没有核心属性组件的不是兵种

    $name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    $race = $file.Directory.Name
    $barracks = $barracksRaces -contains $race
    $tag = "[$race/$name]"

    $hp       = Get-Num $prop "maxHp"
    $atkRate  = Get-Num $prop "atkRate"
    $speed    = Get-Num $prop "moveSpeed"
    $atk      = Get-Num $prop "atk"
    $magicAtk = Get-Num $prop "magicAtk"
    $remote   = $prop["isRemoteAtk"] -eq "1"
    $atkObj   = $prop["atkObj"]
    $range    = $prop["atkRange"]

    $spriteKey = if ($guidTexture -and $comps.ContainsKey($guidTexture)) { $comps[$guidTexture]["spriteKey"] } else { "" }
    $animKey   = if ($guidAnimator -and $comps.ContainsKey($guidAnimator)) { $comps[$guidAnimator]["animatorControllerKey"] } else { "" }
    $audioList = if ($guidAudio -and $comps.ContainsKey($guidAudio)) { @(1..8 | ForEach-Object { $comps[$guidAudio]["audioKey$_"] }) } else { @() }

    $units[$name] = @{ Race = $race; Hp = $hp; Atk = $atk; Path = $file.FullName }

    if ($hp -le 0)      { $errors.Add("$tag 生命值为 0") }
    if ($speed -le 0)   { $errors.Add("$tag 移动速度为 0") }
    if ($range -match 'x:\s*0' -or $range -match 'y:\s*0') { $errors.Add("$tag 攻击范围含 0：$range") }
    if ($atkRate -le 0) { $warns.Add("$tag 攻速系数为 0（不影响角色出手，但攻速类 Buff 会完全失效，建议填 1）") }
    if ($atk -le 0 -and $magicAtk -le 0) { $warns.Add("$tag 攻击与魔攻都是 0") }
    if ($remote -and -not $atkObj) { $warns.Add("$tag 远程单位但投射物键为空") }
    if ($atkObj -and -not $prefabKeys.Contains($atkObj)) { $errors.Add("$tag 投射物键无效：$atkObj") }

    if ($spriteKey -and -not $spriteKeys.Contains($spriteKey)) { $errors.Add("$tag 动画贴图键无效：$spriteKey") }
    elseif (-not $spriteKey) {
        if ($barracks) { $warns.Add("$tag 缺动画贴图键") } else { $infos.Add("$tag 缺动画贴图键（非兵营种族，可能由其它系统驱动）") }
    }

    if ($animKey -and -not $acKeys.Contains($animKey)) { $errors.Add("$tag 动画控制器键无效：$animKey") }
    elseif (-not $animKey) {
        if ($barracks) { $warns.Add("$tag 缺动画控制器键") } else { $infos.Add("$tag 缺动画控制器键（非兵营种族）") }
    }

    foreach ($k in $audioList) {
        if ($k -and -not $audioKeys.Contains($k)) { $errors.Add("$tag 音效键无效：$k") }
    }
}

# ---------- 5. 扫训练配置 ----------
$defPrefabKeys = @{}
$defCount = 0

foreach ($file in Get-ChildItem $defRoot -Recurse -Filter *.asset) {
    $defCount++
    $f = @{}
    foreach ($l in [System.IO.File]::ReadAllLines($file.FullName)) {
        if ($l -match '^  ([A-Za-z_][A-Za-z0-9_]*):\s?(.*)$') { $f[$Matches[1]] = $Matches[2].Trim() }
    }

    $name = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    $tag = "[配置/$($file.Directory.Name)/$name]"
    $prefabKey = $f["prefabKey"]

    if (-not $prefabKey) {
        $errors.Add("$tag 缺单位预制体键")
    }
    elseif (-not $prefabKeys.Contains($prefabKey)) {
        $errors.Add("$tag 单位预制体键无效：$prefabKey")
    }
    else {
        if ($defPrefabKeys.ContainsKey($prefabKey)) { $warns.Add("$tag 与 $($defPrefabKeys[$prefabKey]) 共用单位键 $prefabKey（数值其实是同一份预制体）") }
        else { $defPrefabKeys[$prefabKey] = $file.Name }
    }

    if ($f["iconKey"] -and -not $spriteKeys.Contains($f["iconKey"])) { $errors.Add("$tag 图标键无效：$($f['iconKey'])") }
    elseif (-not $f["iconKey"]) { $warns.Add("$tag 缺图标键") }

    if ($f["animatorControllerKey"] -and -not $acKeys.Contains($f["animatorControllerKey"])) { $errors.Add("$tag 动画控制器键无效：$($f['animatorControllerKey'])") }
    if ($f["animationSpriteKey"] -and -not $spriteKeys.Contains($f["animationSpriteKey"])) { $errors.Add("$tag 基础动画贴图键无效：$($f['animationSpriteKey'])") }

    $cd = Get-Num $f "cooldown"
    if ($cd -le 0) { $errors.Add("$tag 训练冷却为 0") } elseif ($cd -lt 1.5) { $warns.Add("$tag 训练冷却过短：$cd") }
    if ((Get-Num $f "trainCount") -le 0) { $errors.Add("$tag 单次产出为 0") }
    if (-not $f["displayName"]) { $warns.Add("$tag 缺显示名（可用工具一键修复）") }
    if (-not $f["id"]) { $warns.Add("$tag 缺 id（可用工具一键修复）") }
}

# ---------- 6. 交叉检查 ----------
foreach ($name in $units.Keys) {
    if ($defPrefabKeys.ContainsKey($name)) { continue }
    $race = $units[$name].Race
    if ($barracksRaces -contains $race) {
        $warns.Add("[预制体/$race/$name] 有预制体但没有训练配置（玩家在兵营里训练不到）")
    } else {
        $infos.Add("[预制体/$race/$name] 没有训练配置（非兵营种族，通常正常）")
    }
}
foreach ($key in $defPrefabKeys.Keys) {
    if (-not $units.ContainsKey($key)) { $errors.Add("[配置] $key 找不到对应预制体") }
}

# ---------- 报告 ----------
Write-Host ""
Write-Host "兵种数据体检 —— CageFight" -ForegroundColor Cyan
Write-Host ("兵种 {0} 个　训练配置 {1} 个　错误 {2}　警告 {3}　提示 {4}　(跳过非兵种预制体 {5} 个)" -f $units.Count, $defCount, $errors.Count, $warns.Count, $infos.Count, $skipped)
Write-Host "错误 = 上线会出问题；警告 = 多半要补；提示 = 看一眼就行"
Write-Host ("-" * 78)

if ($errors.Count) {
    Write-Host "`n【错误 $($errors.Count)】" -ForegroundColor Red
    $errors | Sort-Object -Unique | ForEach-Object { Write-Host ("  · " + $_) -ForegroundColor Red }
}
if ($warns.Count) {
    Write-Host "`n【警告 $($warns.Count)】" -ForegroundColor Yellow
    $warns | Group-Object { ($_ -replace '^\[[^\]]+\]\s*', '') } | Sort-Object Count -Descending | ForEach-Object {
        Write-Host ("  · x{0}  {1}" -f $_.Count, $_.Name) -ForegroundColor Yellow
        if ($_.Count -le 30) { $_.Group | ForEach-Object { Write-Host ("      - " + $_) -ForegroundColor DarkGray } }
    }
}
if ($infos.Count) {
    Write-Host "`n【提示 $($infos.Count)】" -ForegroundColor Gray
    $infos | Group-Object { ($_ -replace '^\[[^\]]+\]\s*', '') } | Sort-Object Count -Descending | ForEach-Object {
        Write-Host ("  · x{0}  {1}" -f $_.Count, $_.Name) -ForegroundColor Gray
        if ($_.Count -le 20) { $_.Group | ForEach-Object { Write-Host ("      - " + $_) -ForegroundColor DarkGray } }
    }
}
Write-Host ""
