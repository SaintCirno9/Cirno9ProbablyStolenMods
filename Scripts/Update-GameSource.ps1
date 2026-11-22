<#
.SYNOPSIS
    使用 ilspycmd 将游戏程序集反编译到仓库 GameSource\，旧结果先移入 .cache\GameSource-backups\。
.EXAMPLE
    pwsh -NoProfile -File Scripts/Update-GameSource.ps1
.NOTES
    Author: SaintCirno9
#>
param(
    [string]$GameRootDir,
    [string[]]$Assembly = @('Assembly-CSharp', 'Assembly-CSharp-firstpass')
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-GameRoot.ps1')
$gameRoot = Get-ProbablyStolenGameRoot $GameRootDir
$managedDir = Join-Path $gameRoot 'Probably Stolen_Data\Managed'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $repositoryRoot 'GameSource'
$ilspy = Get-Command ilspycmd -ErrorAction SilentlyContinue
if ($null -eq $ilspy) { throw '找不到 ilspycmd。请先执行 dotnet tool install -g ilspycmd。' }

foreach ($name in $Assembly)
{
    $dllPath = Join-Path $managedDir "$name.dll"
    if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) { throw "找不到游戏程序集：$dllPath" }
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$backupPath = Join-Path $repositoryRoot (".cache\GameSource-backups\" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
foreach ($name in $Assembly)
{
    $target = Join-Path $outputRoot $name
    if (Test-Path -LiteralPath $target)
    {
        New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
        Move-Item -LiteralPath $target -Destination (Join-Path $backupPath $name)
        Write-Host "旧结果已备份：$(Join-Path $backupPath $name)"
    }
    Write-Host "正在反编译 $name ..."
    & $ilspy.Source -p --disable-updatecheck -r $managedDir -o $target (Join-Path $managedDir "$name.dll")
    if ($LASTEXITCODE -ne 0) { throw "ilspycmd 反编译失败：$name（退出码 $LASTEXITCODE）" }
}
Write-Host "GameSource 刷新完成：$outputRoot"
