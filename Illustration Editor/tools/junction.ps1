# 图鉴可视化编辑器 —— 目录联接（junction）安装 / 修复 / 卸载
#
# 说明：本工具源码放在仓库的 Illustration Editor，靠一个目录联接挂进 Unity 工程，
#       这样工程里不会多出实体脚本文件，也不改动任何原有文件。
#
# 用法：
#   powershell -File junction.ps1 -Action status      # 看联接指向哪里、目标是否有效
#   powershell -File junction.ps1 -Action install     # 安装；已存在但目标失效/指向别处会自动重指
#   powershell -File junction.ps1 -Action uninstall   # 只删联接，源码保留
#
# 工具文件夹改名 / 移动之后，跑一次 -Action install 即可恢复
#（Unity 里脚本消失、编辑器菜单不见了，基本都是这个原因）。
#
# 注意：本文件必须保存为「UTF-8 带 BOM」，否则 Windows PowerShell 5.1 会读错中文。

param(
    [ValidateSet("install", "uninstall", "status")]
    [string]$Action = "status",
    [string]$ProjectPath = "",
    [string]$SourceDir = ""
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
if (-not $SourceDir) { $SourceDir = Get-ToolSourceDir }
if (-not $ProjectPath) { $ProjectPath = Get-ProjectPath }

# 显式传进来的路径也要校验：否则错误会拖到后面才以"找不到 csproj"的形式暴露出来
if ($SourceDir -and -not (Test-Path $SourceDir)) { throw "-SourceDir 指向的目录不存在：$SourceDir" }
if ($ProjectPath -and -not (Test-UnityProject $ProjectPath)) { throw "指定的 -ProjectPath 不是 Unity 工程（缺少 Assets 或 ProjectSettings\ProjectVersion.txt）：$ProjectPath" }

if (-not $SourceDir) { throw "找不到工具源码目录（期望 $PSScriptRoot\.. 下有一个 *VisualEditor* 目录）。可用 -SourceDir 显式指定。" }
if (-not $ProjectPath) { throw "找不到 Unity 工程。请在工程同级目录下运行，或用 -ProjectPath ""D:\path\to\CageFight"" 显式指定。" }
$ErrorActionPreference = "Stop"

$linkPath = Join-Path $ProjectPath "Assets\Editor\BookSystem\BookVisualEditor"
$parent   = Split-Path $linkPath -Parent
$metaPath = "$linkPath.meta"

function Remove-ExistingLink {
    $item = Get-Item $linkPath -Force
    if ($item.LinkType -ne "Junction") {
        throw "$linkPath 存在但不是联接（可能是手动拷贝的实体目录），请先人工确认后再处理。"
    }
    # 只删联接本身（递归参数必须为 false，否则会把源码目录一起删掉），源码目录不受影响。
    [System.IO.Directory]::Delete($linkPath, $false)
}

switch ($Action) {

    "install" {
        $resolved = Resolve-Path $SourceDir -ErrorAction SilentlyContinue
        if (-not $resolved) { throw "源码目录不存在：$SourceDir（工具改过名的话，用 -SourceDir 指定新路径）" }
        $resolvedSource = $resolved.Path

        if (-not (Test-Path $parent)) { throw "工程目录不存在：$parent" }

        $needCreate = $true
        if (Test-Path $linkPath) {
            $item = Get-Item $linkPath -Force
            if ($item.LinkType -ne "Junction") {
                throw "$linkPath 存在但不是联接（可能是手动拷贝的实体目录），请先人工确认后再处理。"
            }

            $target = $item.Target
            $targetOk = $target -and (Test-Path $target)

            if ($targetOk -and ($target.TrimEnd('\') -ieq $resolvedSource.TrimEnd('\'))) {
                Write-Host "联接已正确，无需处理：$linkPath" -ForegroundColor Green
                Write-Host "  -> $target"
                $needCreate = $false
            } else {
                Write-Host "联接目标失效或指向别处，正在重指…" -ForegroundColor Yellow
                Write-Host ("  旧目标：" + $target + "（存在：" + $(if ($targetOk) { "是" } else { "否" }) + "）")
                Remove-ExistingLink
            }
        }

        if ($needCreate) {
            New-Item -ItemType Junction -Path $linkPath -Target $resolvedSource | Out-Null
            Write-Host "已建立联接：$linkPath" -ForegroundColor Green
            Write-Host "  -> $resolvedSource"
        }

        $count = (Get-ChildItem $linkPath -Filter *.cs -ErrorAction SilentlyContinue | Measure-Object).Count
        Write-Host "通过联接可见 $count 个 .cs 文件" -ForegroundColor Cyan
        Write-Host "切回 Unity 窗口（让它获得焦点），Unity 会自动重新导入并编译。" -ForegroundColor Cyan
    }

    "uninstall" {
        if (-not (Test-Path $linkPath)) {
            Write-Host "没有找到联接：$linkPath" -ForegroundColor Yellow
            break
        }

        Remove-ExistingLink

        # Unity 生成的目录 meta 一起清掉，避免留下孤儿 meta。
        if (Test-Path $metaPath) {
            Remove-Item $metaPath -Force
            Write-Host "已删除 Unity 生成的 BookVisualEditor.meta" -ForegroundColor Yellow
        }

        Write-Host "已移除联接（源码仍保留在 $SourceDir）" -ForegroundColor Green
    }

    "status" {
        if (-not (Test-Path $linkPath)) {
            Write-Host "未安装：$linkPath 不存在" -ForegroundColor Yellow
            Write-Host "  可用 -Action install 安装，源码目录：$SourceDir"
            break
        }

        $item = Get-Item $linkPath -Force
        if ($item.LinkType -ne "Junction") {
            Write-Host "$linkPath 存在，但不是联接（可能是手动拷贝的实体目录）" -ForegroundColor Yellow
            break
        }

        $target = $item.Target
        $targetOk = $target -and (Test-Path $target)

        if ($targetOk) {
            Write-Host "已安装且目标有效（Junction）" -ForegroundColor Green
        } else {
            Write-Host "【问题】联接目标已失效 —— Unity 里脚本会消失、编辑器菜单也会不见" -ForegroundColor Red
            Write-Host "  处理：powershell -File junction.ps1 -Action install -SourceDir <新的工具目录>\BookVisualEditor" -ForegroundColor Yellow
        }

        Write-Host "  链接：$linkPath"
        Write-Host "  目标：$target"

        if ($targetOk) {
            $count = (Get-ChildItem $linkPath -Filter *.cs -ErrorAction SilentlyContinue | Measure-Object).Count
            Write-Host "  源码：$count 个 .cs 文件"
            if ($target.TrimEnd('\') -ine $SourceDir.TrimEnd('\')) {
                Write-Host "  提示：与脚本默认的 -SourceDir（$SourceDir）不一致，如需对齐可跑 -Action install" -ForegroundColor Yellow
            }
        }
    }
}
