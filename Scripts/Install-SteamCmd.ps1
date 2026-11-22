<#
.SYNOPSIS
    在仓库 Tools/SteamCMD/ 安装 Valve 官方 SteamCMD，供 Scripts/Publish-WorkshopMod.ps1 使用。
.DESCRIPTION
    从 Valve 官方地址下载 steamcmd.zip，校验 steamcmd.exe 的 Valve 数字签名后解压，
    再运行一次 +quit 完成首次自更新。已安装时只做自更新。
    Tools/SteamCMD/ 不提交：登录后其中的 config/ 会缓存 Steam 登录凭据。
.EXAMPLE
    pwsh -NoProfile -File Scripts/Install-SteamCmd.ps1
.NOTES
    Author: SaintCirno9
#>
param()
$ErrorActionPreference = 'Stop'

$DownloadUrl = 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$installDir = Join-Path $repositoryRoot 'Tools\SteamCMD'
$steamCmd = Join-Path $installDir 'steamcmd.exe'

function Assert-ValveSignature
{
    param([string]$Path)
    $signature = Get-AuthenticodeSignature -LiteralPath $Path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Valve')
    {
        throw "steamcmd.exe 签名校验失败：$($signature.Status) $($signature.SignerCertificate.Subject)"
    }
    Write-Host "签名有效：$($signature.SignerCertificate.Subject)"
}

if (-not (Test-Path -LiteralPath $steamCmd -PathType Leaf))
{
    $tempDir = Join-Path ([IO.Path]::GetTempPath()) ("steamcmd-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $tempDir | Out-Null
    try
    {
        $zipPath = Join-Path $tempDir 'steamcmd.zip'
        Write-Host "正在下载 $DownloadUrl ..."
        Invoke-WebRequest -Uri $DownloadUrl -OutFile $zipPath
        Expand-Archive -LiteralPath $zipPath -DestinationPath $tempDir
        Assert-ValveSignature (Join-Path $tempDir 'steamcmd.exe')
        New-Item -ItemType Directory -Path $installDir -Force | Out-Null
        Move-Item -LiteralPath (Join-Path $tempDir 'steamcmd.exe') -Destination $steamCmd
    }
    finally
    {
        Remove-Item -LiteralPath $tempDir -Recurse -Force
    }
    Write-Host "已安装到 $installDir"
}
else
{
    Write-Host "已安装：$steamCmd"
}

# 首次运行会下载 SteamCMD 自身的运行文件；之后每次运行都会自动更新。
Write-Host '正在运行 SteamCMD 自更新...'
& $steamCmd +quit
# SteamCMD 首次自更新后常以退出码 7 结束，属于正常现象。
if ($LASTEXITCODE -notin 0, 7) { throw "SteamCMD 自更新失败（退出码 $LASTEXITCODE）。" }
Assert-ValveSignature $steamCmd
Write-Host "SteamCMD 就绪：$steamCmd"
