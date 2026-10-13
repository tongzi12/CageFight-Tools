# 法术可视化编辑器 —— 编译校验脚本
#
# 作用：用 Unity 自带的 Roslyn，把 Spell Editor\SpellVisualEditor 里的源码
#       对着 CageFight 工程真实用到的程序集（含已编译好的 CageFightRuntime.dll）编一遍，
#       不启动 Unity、不改动工程任何文件，确认代码能编译通过。
#
# 用法：powershell -File compile-check.ps1
#       （不要用管道过滤输出，受限环境下管道会让编译器进程起不来）
#
# 产物：Spell Editor\.compile-check\SpellVisualEditor.dll（临时校验产物，可随时删）
#
# 注意：本文件必须保存为「UTF-8 带 BOM」，否则 Windows PowerShell 5.1 会读错中文。

param(
    [string]$ProjectPath = "",
    [string]$SourceDir = "",
    [string]$OutDir = "",
    [string]$CscPath = "",
    [string]$Csproj      = "CageFightEditor.csproj"
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
if (-not $OutDir) { $OutDir = Join-Path (Get-ToolRoot) ".compile-check" }
if (-not $CscPath) { $CscPath = Get-CscPath }

if (-not $SourceDir) { throw "找不到工具源码目录（期望 $PSScriptRoot\.. 下有一个 *VisualEditor* 目录）。可用 -SourceDir 显式指定。" }
if (-not $ProjectPath) { throw "找不到 Unity 工程。请在工程同级目录下运行，或用 -ProjectPath ""D:\path\to\CageFight"" 显式指定。" }
if (-not $CscPath) { throw "找不到 Unity 自带的 Roslyn（csc.dll）。请装 Unity 或用 -CscPath ""<Unity 安装目录>\Editor\Data\DotNetSdkRoslyn\csc.dll"" 显式指定。" }
$ErrorActionPreference = "Stop"

# ---------- 0. 源码卫生检查（行尾 / BOM）----------
# 为什么要有这一步：Windows 上 File.WriteAllLines 与 Set-Content 默认写 CRLF，
# 一次"用脚本改代码"就能把整份 .cs 转成 CRLF —— Unity 里看不出问题，
# 但 git diff 会变成整文件重写，之后多行补丁也匹配不上（本工程已经踩过两次：
# 建筑编辑器 R1.5、关卡编辑器 R1.19）。
# 断言的都是实测成立、且 README 里写明过的约定：
#   · 工具源码 .cs：行尾必须是 LF，且**不带** BOM
#   · tools\*.ps1 ：**必须带** UTF-8 BOM（否则 PowerShell 5.1 读错中文）
$hygiene = New-Object System.Collections.Generic.List[string]

$selfPath = $MyInvocation.MyCommand.Path
if ($selfPath -and (Test-Path $selfPath)) {
    $selfBytes = [System.IO.File]::ReadAllBytes($selfPath)
    if (-not ($selfBytes.Length -ge 3 -and $selfBytes[0] -eq 0xEF -and $selfBytes[1] -eq 0xBB -and $selfBytes[2] -eq 0xBF)) {
        $hygiene.Add("本脚本自己没有 UTF-8 BOM：$selfPath")
    }
}

foreach ($srcFile in (Get-ChildItem -Path $SourceDir -Filter *.cs -Recurse)) {
    $bytes = [System.IO.File]::ReadAllBytes($srcFile.FullName)
    $crCount = 0
    for ($i = 0; $i -lt $bytes.Length; $i++) { if ($bytes[$i] -eq 0x0D) { $crCount++ } }
    if ($crCount -gt 0) { $hygiene.Add("行尾含 CR（应为纯 LF，共 $crCount 处）：$($srcFile.Name)") }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        $hygiene.Add("带了 UTF-8 BOM（源码约定是不带）：$($srcFile.Name)")
    }
}

$toolDir = Split-Path $selfPath -Parent
foreach ($ps1 in (Get-ChildItem -Path $toolDir -Filter *.ps1 -File)) {
    $bytes = [System.IO.File]::ReadAllBytes($ps1.FullName)
    if (-not ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)) {
        $hygiene.Add("这个 .ps1 没有 UTF-8 BOM：$($ps1.Name)")
    }
    $ps1Cr = 0
    for ($i = 0; $i -lt $bytes.Length; $i++) { if ($bytes[$i] -eq 0x0D) { $ps1Cr++ } }
    if ($ps1Cr -gt 0) { $hygiene.Add("脚本行尾含 CR（应为纯 LF，共 $ps1Cr 处）：$($ps1.Name)") }
}

if ($hygiene.Count -gt 0) {
    Write-Host "`n源码卫生检查有问题：" -ForegroundColor Yellow
    foreach ($msg in $hygiene) { Write-Host "  · $msg" -ForegroundColor Yellow }
}

$csprojPath = Join-Path $ProjectPath $Csproj
if (-not (Test-Path $csprojPath)) { throw "找不到工程 csproj：$csprojPath" }
if (-not (Test-Path $CscPath))    { throw "找不到 Unity 的 Roslyn 编译器：$CscPath" }

# ---------- 1. 从 Unity 生成的 csproj 里提取引用与宏 ----------
[xml]$xml = Get-Content $csprojPath

$references = New-Object System.Collections.Generic.List[string]
foreach ($ref in $xml.SelectNodes("//*[local-name()='Reference']")) {
    $hint = $ref.SelectSingleNode("*[local-name()='HintPath']")
    if ($hint -and $hint.InnerText) {
        $path = $hint.InnerText.Trim()
        if (-not [System.IO.Path]::IsPathRooted($path)) {
            $path = Join-Path $ProjectPath $path
        }
        if (Test-Path $path) { $references.Add($path) }
    }
}

$defineNode = $xml.SelectSingleNode("//*[local-name()='DefineConstants']")
$defines = if ($defineNode) { $defineNode.InnerText.Trim() } else { "UNITY_EDITOR;UNITY_2022_3_OR_NEWER" }
if ($defines -notmatch "UNITY_EDITOR") { $defines = "UNITY_EDITOR;$defines" }

$runtimeDll = Join-Path $ProjectPath "Library\ScriptAssemblies\CageFightRuntime.dll"
if (Test-Path $runtimeDll) { $references.Add($runtimeDll) } else { Write-Warning "找不到 CageFightRuntime.dll，请先在 Unity 里让工程编译一次。" }

# ---------- 2. 收集源码 ----------
$sources = Get-ChildItem -Path $SourceDir -Filter *.cs -Recurse | Sort-Object FullName
if ($sources.Count -eq 0) { throw "源码目录里没有 .cs 文件：$SourceDir" }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$outDll = Join-Path $OutDir "SpellVisualEditor.dll"

# ---------- 3. 写响应文件并调用 csc ----------
$rspLines = New-Object System.Collections.Generic.List[string]
$rspLines.Add("-target:library")
$rspLines.Add("-nologo")
$rspLines.Add("-nostdlib+")
$rspLines.Add("-langversion:9.0")
$rspLines.Add("-nowarn:0169,0649,0414,0067")
$rspLines.Add("-unsafe-")
$rspLines.Add("-out:`"$outDll`"")
$rspLines.Add("-define:`"$defines`"")
foreach ($r in $references) { $rspLines.Add("-r:`"$r`"") }
foreach ($s in $sources)    { $rspLines.Add("`"$($s.FullName)`"") }

$rspPath = Join-Path $OutDir "compile.rsp"
Set-Content -Path $rspPath -Value $rspLines -Encoding UTF8

Write-Host "引用程序集 $($references.Count) 个，待编译文件 $($sources.Count) 个" -ForegroundColor Cyan
foreach ($s in $sources) { Write-Host "  · $($s.Name)" }

& dotnet exec "$CscPath" "@$rspPath"
$exit = $LASTEXITCODE
# csc 有输出时 $LASTEXITCODE 可能为 $null（PowerShell 对原生命令的行为），
# 此时按「产物是否生成」兜底，避免把失败当成功（原本缺这一句，会「假通过」）。
if ($null -eq $exit) { $exit = if (Test-Path $outDll) { 0 } else { 1 } }

if ($exit -eq 0) {
    Write-Host "`n编译通过 OK  产物：$outDll" -ForegroundColor Green
} else {
    Write-Host "`n编译失败  退出码 $exit" -ForegroundColor Red
}

# 卫生检查也计入结果：编译过了但源码卫生有问题，这次校验同样算没过（用退出码 2 区分开）
if ($hygiene.Count -gt 0) {
    Write-Host "源码卫生 ✘  共 $($hygiene.Count) 项（见上）" -ForegroundColor Red
    if ($exit -eq 0) { $exit = 2 }
} else {
    Write-Host "源码卫生 ✔  行尾全 LF / 无 BOM / 脚本自带 BOM" -ForegroundColor Green
}
exit $exit
