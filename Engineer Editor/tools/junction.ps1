# 工程师编辑器 —— 目录联接（junction）安装 / 修复 / 卸载
#
# 说明：本工具源码放在仓库的「Engineer Editor」目录，靠一个目录联接挂进 Unity 工程，
#       这样工程里不会多出实体脚本文件，也不改动任何原有文件（游戏源码一行不动）。
#
# 用法：
#   powershell -File junction.ps1 -Action status      # 看联接指向哪里、目标是否有效、git 是否已排除
#   powershell -File junction.ps1 -Action install     # 安装（已存在但目标失效/指向别处会自动重指）
#   powershell -File junction.ps1 -Action uninstall   # 只删联接，源码保留
#
# ── 关于 git（重要）────────────────────────────────────────────────────────
# git 会顺着联接把工具源码当成「本工程的新文件」提交上去，所以必须排除。
#
# 为什么不用 .git\info\exclude：工程根 .gitignore 里有 `!Assets/**` 这条**反排除**，
# 而 .gitignore 的优先级高于 .git\info\exclude（先匹配的源说了算），
# 于是 info/exclude 里的规则会被那条反排除压掉 —— 实测无效。
#
# 实际做法（照抄建筑编辑器已验证的做法）：在 **Assets\Editor\** 放一个「自己排除自己」的
# .gitignore（子目录 .gitignore 优先级高于根目录，压得过那条反排除）。install 会自动追加：
#     /EngineerSystem
#     /EngineerSystem.meta
#     /.gitignore
#
# 为什么规则放在 Assets\Editor 这一层、而不是 Assets\Editor\EngineerSystem 里面：
#   Unity 会给新建的 EngineerSystem 目录生成 **EngineerSystem.meta**，那个文件在**上一层**，
#   放在 EngineerSystem 里面的 .gitignore 管不到它（建筑编辑器踩过这个坑，注释留在那份文件里）。
# 这个文件本身也不进版本库，所以不会上传到 GitHub。
# ──────────────────────────────────────────────────────────────────────────
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

$linkPath   = Join-Path $ProjectPath "Assets\Editor\EngineerSystem\EngineerVisualEditor"
$parent     = Split-Path $linkPath -Parent
$metaPath   = "$linkPath.meta"
$mountMeta  = "$parent.meta"                                   # Unity 给挂载目录生成的 .meta（在上一层的目录里）
$ignorePath = Join-Path (Split-Path $parent -Parent) ".gitignore"   # = Assets\Editor\.gitignore
$utf8NoBom  = New-Object System.Text.UTF8Encoding($false)

$ignoreRules = @(
    "# 工程师编辑器：排除挂进来的目录联接（源码在仓库之外的 GitHub\Engineer Editor）",
    "/EngineerSystem",
    "/EngineerSystem.meta",
    "/.gitignore"
)

function Remove-ExistingLink {
    $item = Get-Item $linkPath -Force
    if ($item.LinkType -ne "Junction") {
        throw "$linkPath 存在但不是联接（可能是手动拷贝的实体目录），请先人工确认后再处理。"
    }
    # 只删联接本身（递归参数必须为 false，否则会把源码目录一起删掉），源码目录不受影响。
    [System.IO.Directory]::Delete($linkPath, $false)
}

function Write-GitIgnore {
    $lines = New-Object System.Collections.Generic.List[string]
    if (Test-Path $ignorePath) {
        foreach ($l in [System.IO.File]::ReadAllLines($ignorePath, [System.Text.Encoding]::UTF8)) { $lines.Add($l) }
    }

    $added = 0
    foreach ($rule in $ignoreRules) {
        if (-not $lines.Contains($rule)) { $lines.Add($rule); $added++ }
    }

    if ($added -eq 0) {
        Write-Host "git 排除已就绪：$ignorePath" -ForegroundColor Green
        return
    }

    [System.IO.File]::WriteAllLines($ignorePath, $lines, $utf8NoBom)
    Write-Host "已写入 $added 行 git 排除规则：$ignorePath" -ForegroundColor Green
}

function Test-GitIgnore {
    if (-not (Test-Path $ignorePath)) { return $false }
    return ([System.IO.File]::ReadAllLines($ignorePath, [System.Text.Encoding]::UTF8) -contains "/EngineerSystem")
}

function Show-GitStatus {
    # 看 Assets/Editor 整层：联接在 EngineerSystem 里，而 Unity 生成的 EngineerSystem.meta 在上一层。
    $status = & git -C $ProjectPath status --porcelain=v1 --untracked-files=all -- "Assets/Editor"
    if ($status) {
        Write-Host "注意：Assets/Editor 下仍有 git 可见的内容：" -ForegroundColor Yellow
        $status | ForEach-Object { Write-Host "  $_" }
    } else {
        Write-Host "核对通过：Assets/Editor 在 git 眼里是干净的（联接与它的 .meta 都没进版本库）。" -ForegroundColor Green
    }
}

switch ($Action) {

    "install" {
        $resolved = Resolve-Path $SourceDir -ErrorAction SilentlyContinue
        if (-not $resolved) { throw "源码目录不存在：$SourceDir（工具改过名的话，用 -SourceDir 指定新路径）" }
        $resolvedSource = $resolved.Path

        if (-not (Test-Path $parent)) {
            # 另外五个编辑器是挂在工程里**已经有**的编辑器目录下（StageSystem / BookSystem / …）；
            # 工程师编辑器没有现成的同系统目录，所以这里顺手把挂载目录建出来。
            # 它里面只会出现两样东西：本联接、以及下面那份「自己排除自己」的 .gitignore —— 都不会进版本库。
            New-Item -ItemType Directory -Force -Path $parent | Out-Null
            Write-Host "已创建工程内的挂载目录：$parent" -ForegroundColor Cyan
        }

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

        Write-GitIgnore
        Show-GitStatus

        Write-Host "切回 Unity 窗口（让它获得焦点），Unity 会自动重新导入并编译。" -ForegroundColor Cyan
    }

    "uninstall" {
        if (-not (Test-Path $linkPath)) {
            Write-Host "没有找到联接：$linkPath" -ForegroundColor Yellow
        } else {
            Remove-ExistingLink

            # Unity 生成的 meta 一起清掉，避免留下孤儿 meta（联接自己的、以及挂载目录的）。
            if (Test-Path $metaPath) {
                Remove-Item $metaPath -Force
                Write-Host "已删除 Unity 生成的 EngineerVisualEditor.meta" -ForegroundColor Yellow
            }
            if (Test-Path $mountMeta) {
                Remove-Item $mountMeta -Force
                Write-Host "已删除 Unity 生成的 EngineerSystem.meta" -ForegroundColor Yellow
            }
            Write-Host "已移除联接（源码仍保留在 $SourceDir）" -ForegroundColor Green
            Write-Host "提示：空的挂载目录 $parent 也可以一并删掉。" -ForegroundColor Yellow
        }

        if (Test-Path $ignorePath) {
            Write-Host "提示：$ignorePath 里仍有本工具的排除规则（/EngineerSystem 那两行），确认不再需要后可手工删除。" -ForegroundColor Yellow
        }
    }

    "status" {
        if (-not (Test-Path $linkPath)) {
            Write-Host "未安装：$linkPath 不存在" -ForegroundColor Yellow
            Write-Host "  可用 -Action install 安装，源码目录：$SourceDir"
        } else {
            $item = Get-Item $linkPath -Force
            if ($item.LinkType -ne "Junction") {
                Write-Host "$linkPath 存在，但不是联接（可能是手动拷贝的实体目录）" -ForegroundColor Yellow
            } else {
                $target = $item.Target
                $targetOk = $target -and (Test-Path $target)

                if ($targetOk) {
                    Write-Host "已安装且目标有效（Junction）" -ForegroundColor Green
                } else {
                    Write-Host "【问题】联接目标已失效 —— Unity 里脚本会消失、编辑器菜单也会不见" -ForegroundColor Red
                    Write-Host "  处理：powershell -File junction.ps1 -Action install -SourceDir <新的工具目录>\EngineerVisualEditor" -ForegroundColor Yellow
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

        Write-Host ""
        if (Test-GitIgnore) {
            Write-Host "git 排除：已就绪（$ignorePath）" -ForegroundColor Green
        } else {
            Write-Host "git 排除：未设置 —— 现在提交会把工具源码带进仓库，请跑 -Action install" -ForegroundColor Red
        }
    }
}
