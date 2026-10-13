# 图鉴数据体检（离线版）
#
# 作用：不打开 Unity，直接读 BookCatalog.asset + 两份资源注册表，
#       按「图鉴可视化编辑器」里那套规则把问题列出来。
#       规则与 BookVisualEditor/BookEditorValidation.cs 保持一致。
#
# 用法：powershell -File data-audit.ps1
#
# 注意：本文件必须保存为「UTF-8 带 BOM」，否则 Windows PowerShell 5.1 会读错中文。

param(
    [string]$CatalogPath = "",
    [string]$SpriteRegistry = "",
    [string]$PrefabRegistry = ""
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
if (-not $CatalogPath) { $CatalogPath = Join-Path $ProjectPath "Assets\Resource\LocalResource\Book\BookCatalog.asset" }
if (-not $SpriteRegistry) { $SpriteRegistry = Join-Path $ProjectPath "Assets\RemoteResource\SpriteRegistry.asset" }
if (-not $PrefabRegistry) { $PrefabRegistry = Join-Path $ProjectPath "Assets\RemoteResource\PrefabRegistry.asset" }

$ErrorActionPreference = "Stop"

# 仓库根（Unity 工程的上一级）：由注册表路径反推 <repo>\CageFight\Assets\RemoteResource\xxx.asset
$projectRoot = Split-Path (Split-Path (Split-Path $SpriteRegistry -Parent) -Parent) -Parent
$repoRoot    = Split-Path $projectRoot -Parent

function Convert-UnicodeEscapes([string]$text) {
    if ([string]::IsNullOrEmpty($text)) { return "" }
    $t = $text.Trim()
    if ($t.Length -ge 2 -and $t.StartsWith('"') -and $t.EndsWith('"')) { $t = $t.Substring(1, $t.Length - 2) }
    return [System.Text.RegularExpressions.Regex]::Unescape($t)
}

function ConvertTo-NormalizedKey([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return "" }
    return ($s -replace '[^A-Za-z0-9]', '').ToLowerInvariant()
}

# 与 BookEditorValidation.SuggestUnitKeyFromId 同规则：完全相同 / 忽略大小写 / 去掉符号后相同，且唯一
function Get-ExactUnitKey([string]$entryId, $keys) {
    if ([string]::IsNullOrEmpty($entryId)) { return $null }
    $id = $entryId.Trim()
    $n = ConvertTo-NormalizedKey $id
    $hits = @($keys | Where-Object { $_ -ieq $id -or (ConvertTo-NormalizedKey $_) -eq $n })
    if ($hits.Count -eq 1) { return $hits[0] }
    return $null
}

# 与 BookEditorValidation.GuessUnitKeyFromRoleKey 同规则：从立绘键推断单位名
function Get-UnitKeyFromRoleKey([string]$roleKey, $keys) {
    if ([string]::IsNullOrEmpty($roleKey)) { return $null }
    $core = [regex]::Replace($roleKey.Trim(), '[-_ ](attack|move|idle|free|MOVE)?[-_ ]?\d+$', '', 'IgnoreCase')
    $core = [regex]::Replace($core, '[-_ ](attack|attck|move|idle|free)$', '', 'IgnoreCase')
    $core = $core.Trim(' ', '-', '_')
    if ($core.Length -lt 2 -or $core -ieq $roleKey.Trim()) { return $null }
    $hit = @($keys | Where-Object { $_ -ieq $core })
    if ($hit.Count -eq 1) { return $hit[0] }
    return $null
}
# 模糊候选（用于提示，不自动改）
function Get-FuzzyUnitKeys([string]$entryId, $keys, [int]$max = 3) {
    if ([string]::IsNullOrEmpty($entryId)) { return @() }
    $id = $entryId.Trim()
    return @($keys | Where-Object { $_.IndexOf($id, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or $id.IndexOf($_, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 } | Select-Object -First $max)
}
function Get-RegistryKeys([string]$path) {
    $keys = New-Object System.Collections.Generic.HashSet[string]
    if (-not (Test-Path $path)) { return $keys }
    foreach ($line in [System.IO.File]::ReadAllLines($path)) {
        if ($line -match '^\s*-\s*key:\s*(.*)$') { [void]$keys.Add($Matches[1].Trim()) }
    }
    return $keys
}

# ---------- 把「编辑器预览直引」（{fileID: X, guid: G}）解析成切片名 ----------
$script:MetaCache = @{}          # guid -> @{ fileID = 切片名 }
$script:GuidMetaPath = @{}       # guid -> meta 路径

function Get-SpriteSheetMap([string]$guid) {
    if ($script:MetaCache.ContainsKey($guid)) { return $script:MetaCache[$guid] }

    $path = $null
    if ($script:GuidMetaPath.ContainsKey($guid)) { $path = $script:GuidMetaPath[$guid] }
    if (-not $path) {
        foreach ($dir in @("$repoRoot\CageFight\Assets\Resource", "$repoRoot\CageFight\Assets\RemoteResource")) {
            if (-not (Test-Path $dir)) { continue }
            $hit = Get-ChildItem $dir -Recurse -Filter *.png.meta -ErrorAction SilentlyContinue |
                   Where-Object { ([System.IO.File]::ReadAllText($_.FullName)).Contains($guid) } | Select-Object -First 1
            if ($hit) { $path = $hit.FullName; break }
        }
        $script:GuidMetaPath[$guid] = $path
    }

    $map = @{}
    if ($path) {
        $text = [System.IO.File]::ReadAllText($path)
        foreach ($m in [regex]::Matches($text, 'name:\s*([^\r\n]+)\r?\n[\s\S]{0,700}?internalID:\s*(-?\d+)')) {
            $name = $m.Groups[1].Value.Trim()
            $fid = $m.Groups[2].Value
            if (-not $map.ContainsKey($fid)) { $map[$fid] = $name }
        }
    }

    $script:MetaCache[$guid] = $map
    return $map
}

function Get-SpriteRefName([string]$ref) {
    if (-not $ref) { return $null }
    $m = [regex]::Match($ref, 'fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]{32})')
    if (-not $m.Success) { return $null }
    $fid = $m.Groups[1].Value
    $map = Get-SpriteSheetMap $m.Groups[2].Value
    if ($map.ContainsKey($fid)) { return $map[$fid] }
    return "fileID $fid"
}

# ---------- 读资产 ----------
if (-not (Test-Path $CatalogPath)) { throw "找不到图鉴资产：$CatalogPath" }
$lines = [System.IO.File]::ReadAllLines($CatalogPath)

$categories = New-Object System.Collections.Generic.List[string]
$raceOptions = New-Object System.Collections.Generic.List[string]
$entries = New-Object System.Collections.Generic.List[hashtable]

$section = ""
$inEntries = $false
$cur = $null

foreach ($line in $lines) {
    if ($line -match '^  categories:') { $section = "categories"; $inEntries = $false; continue }
    if ($line -match '^  raceOptions:') { $section = "raceOptions"; $inEntries = $false; continue }
    if ($line -match '^  entries:') { $section = "entries"; $inEntries = $true; continue }
    if ($line -match '^  [A-Za-z]+:') { $section = ""; $inEntries = $false; continue }

    if ($inEntries) {
        if ($line -match '^  - id:\s?(.*)$') {
            if ($cur) { $entries.Add($cur) }
            $cur = @{ id = $Matches[1].Trim() }
            continue
        }
        if ($cur -and $line -match '^    ([A-Za-z][A-Za-z0-9]*):\s?(.*)$') {
            $cur[$Matches[1]] = $Matches[2].Trim()
            continue
        }
        continue
    }

    if ($section -eq "categories" -and $line -match '^  - (.*)$') { $categories.Add($Matches[1].Trim()); continue }
    if ($section -eq "raceOptions" -and $line -match '^  - (.*)$') { $raceOptions.Add($Matches[1].Trim()); continue }
}
if ($cur) { $entries.Add($cur) }

$spriteKeys = Get-RegistryKeys $SpriteRegistry
$prefabKeys = Get-RegistryKeys $PrefabRegistry

$errors = New-Object System.Collections.Generic.List[string]
$warns  = New-Object System.Collections.Generic.List[string]
$infos  = New-Object System.Collections.Generic.List[string]

function Add-Error($m) { $errors.Add($m) }
function Add-Warn($m)  { $warns.Add($m) }
function Add-Info($m)  { $infos.Add($m) }

# ---------- 逐条体检 ----------
$idOwner = @{}
$prefabOwner = @{}
$usedCategories = New-Object System.Collections.Generic.HashSet[string]

for ($i = 0; $i -lt $entries.Count; $i++) {
    $e = $entries[$i]
    $name = Convert-UnicodeEscapes $e["displayName"]
    $label = if ($name) { $name } elseif ($e["id"]) { $e["id"] } else { "条目 $($i+1)" }
    $tag = "[$($i+1)] $label"

    $id = $e["id"]
    if (-not $id) {
        Add-Error "$tag 缺唯一 ID（永远无法解锁）"
    } else {
        if ($id -ne $id.Trim()) { Add-Warn "$tag ID 首尾有空格：`"$id`"" }
        if ($idOwner.ContainsKey($id)) { Add-Error "$tag ID 重复（与第 $($idOwner[$id] + 1) 条相同：`"$id`"）" }
        else { $idOwner[$id] = $i }
    }

    if (-not $name) { Add-Error "$tag 缺角色名" }

    $cat = $e["categoryId"]
    if (-not $cat) { Add-Error "$tag 缺所属派系（不会出现在任何页签）" }
    elseif ($categories -notcontains $cat) { Add-Error "$tag 派系 `"$cat`" 不在 categories 列表里（会掉进未分类）" }
    else { [void]$usedCategories.Add($cat) }

    $race = $e["raceId"]
    if (-not $race) { Add-Warn "$tag 缺种族图标（Card 上没有种族图标亮起）" }
    elseif ($raceOptions -notcontains $race) { Add-Warn "$tag 种族 `"$race`" 不在 raceOptions 里" }

    $avatar = $e["avatarSpriteKey"]
    if (-not $avatar) { Add-Warn "$tag 缺头像资源键（图鉴格子空白）" }
    elseif (-not $spriteKeys.Contains($avatar)) { Add-Error "$tag 头像资源键无效：`"$avatar`" 不在 SpriteRegistry" }

    $role = $e["roleSpriteKey"]
    if (-not $role) { Add-Warn "$tag 缺立绘资源键（Card 立绘空白）" }
    elseif (-not $spriteKeys.Contains($role)) { Add-Error "$tag 立绘资源键无效：`"$role`" 不在 SpriteRegistry" }
    elseif ($role -match '-move') { Add-Info "$tag 立绘用的是 move 帧：`"$role`"（卡面通常用 -attack_0）" }

    if ($avatar -and $avatar -eq $role) { Add-Info "$tag 头像与立绘用了同一张图" }

    # --- 编辑器预览直引 vs 资源键 ---
    # 游戏只认资源键；资产里还存着给编辑器看的直引（avatarSprite/roleSprite）。
    # 两者不一致时，编辑器里显示的和游戏里不是同一张图（改键后没同步直引就会出现）。
    $avatarRef = Get-SpriteRefName $e["avatarSprite"]
    if ($avatarRef -and $avatar -and $avatarRef -ne $avatar) {
        Add-Warn "$tag 编辑器预览图与头像键不一致：键=`"$avatar`"，预览直引=`"$avatarRef`"（游戏只认键，编辑器会显示成另一张图；可用工具「一键修复」同步）"
    }
    $roleRef = Get-SpriteRefName $e["roleSprite"]
    if ($roleRef -and $role -and $roleRef -ne $role) {
        Add-Warn "$tag 编辑器预览图与立绘键不一致：键=`"$role`"，预览直引=`"$roleRef`"（可用工具「一键修复」同步）"
    }

    $prefab = $e["prefabKey"]
    if (-not $prefab) {
        $exact = Get-ExactUnitKey $id $prefabKeys
        if ($exact) {
            Add-Warn "$tag 缺单位预制体键 -> 可自动填：$exact"
        } else {
            $fromRole = Get-UnitKeyFromRoleKey $e["roleSpriteKey"] $prefabKeys
            if ($fromRole) { Add-Warn "$tag 缺单位预制体键（永远解锁不了；按立绘键疑似：$fromRole）"; continue }
            $fuzzy = Get-FuzzyUnitKeys $id $prefabKeys
            if ($fuzzy.Count -gt 0) { Add-Warn "$tag 缺单位预制体键（永远解锁不了；疑似：$($fuzzy -join ' / ')）" }
            else { Add-Warn "$tag 缺单位预制体键（永远解锁不了；注册表里没有相近的名字，需手动选）" }
        }
    }
    elseif (-not $prefabKeys.Contains($prefab)) { Add-Error "$tag 单位预制体键无效：`"$prefab`" 不在 PrefabRegistry" }
    else {
        if ($prefabOwner.ContainsKey($prefab)) { Add-Warn "$tag 单位键重复（与第 $($prefabOwner[$prefab] + 1) 条共用 `"$prefab`"）" }
        else { $prefabOwner[$prefab] = $i }
    }

    $star = 0
    [void][int]::TryParse($e["star"], [ref]$star)
    if ($star -lt 1 -or $star -gt 7) { Add-Error "$tag 星级越界：$star" }

    $sixEmpty = -not ($e["repelText"] -or $e["quantityText"] -or $e["scopeText"] -or $e["timeText"] -or $e["weightText"] -or $e["coinText"])
    if ($sixEmpty) { Add-Info "$tag 六项数值全为空" }

    if (-not (Convert-UnicodeEscapes $e["explainText"])) { Add-Info "$tag 简介为空" }
}

foreach ($c in $categories) {
    if (-not $usedCategories.Contains($c)) { Add-Info "页签 `"$c`" 下没有任何条目（游戏内是一页空格子）" }
}

# ---------- 报告 ----------
Write-Host ""
Write-Host "图鉴数据体检 —— $(Split-Path $CatalogPath -Leaf)" -ForegroundColor Cyan
Write-Host "条目 $($entries.Count) 条　错误 $($errors.Count)　警告 $($warns.Count)　提示 $($infos.Count)"
Write-Host ("错误 = 上线会出问题（必改）；警告 = 多半要补；提示 = 看一眼就行").PadRight(10)
Write-Host ("-" * 78)

if ($errors.Count) {
    Write-Host "`n【错误 $($errors.Count)】" -ForegroundColor Red
    $errors | ForEach-Object { Write-Host ("  · " + $_) -ForegroundColor Red }
}
if ($warns.Count) {
    Write-Host "`n【警告 $($warns.Count)】" -ForegroundColor Yellow
    $warns | Group-Object { ($_ -replace '^\[\d+\]\s*\S+\s*', '') } | Sort-Object Count -Descending | ForEach-Object {
        Write-Host ("  · x{0}  {1}" -f $_.Count, $_.Name) -ForegroundColor Yellow
        $_.Group | ForEach-Object { Write-Host ("      - " + $_) -ForegroundColor DarkGray }
    }
}
if ($infos.Count) {
    Write-Host "`n【提示 $($infos.Count)】" -ForegroundColor Gray
    $infos | ForEach-Object { Write-Host ("  · " + $_) }
}
Write-Host ""
