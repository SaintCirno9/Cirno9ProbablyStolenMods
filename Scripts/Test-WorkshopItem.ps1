<#
.SYNOPSIS
    回查模组在创意工坊上的当前状态，并与 Mods/<ModName>/Workshop/ 里的本地文案逐项对比。
.DESCRIPTION
    经本机已登录的 Steam 客户端（Steamworks.NET，ISteamUGC）读取条目，逐语言对比标题与描述，并对比标签；
    同时显示可见性、内容大小与最后更新时间。只读，不修改任何东西，不需要密码与令牌。
    有差异时以退出码 1 结束。

    注意：SteamCMD 登录会把桌面 Steam 挤下线，完整发布之后要先在客户端里重新连接，再运行本脚本。
    Steam 对没有对应语言文案的语言会回退到 english，所以只回查 workshop.json 里声明过的语言。
.EXAMPLE
    pwsh -NoProfile -File Scripts/Test-WorkshopItem.ps1 -ModName ProjectorShowcase
.NOTES
    Author: SaintCirno9
#>
param(
    [Parameter(Mandatory)][string]$ModName
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-GameRoot.ps1')
. (Join-Path $PSScriptRoot 'WorkshopText.ps1')

$AppId = '4348910'
if ($ModName -notmatch '^[A-Za-z0-9_]+$') { throw "模组名只能包含字母、数字和下划线：$ModName" }
$workshopDir = Join-Path (Split-Path -Parent $PSScriptRoot) "Mods\$ModName\Workshop"
$metadataPath = Join-Path $workshopDir 'workshop.json'
if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) { throw "缺少文件：$metadataPath" }

function Read-Bbcode
{
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "缺少文件：$Path" }
    return ((Get-Content -LiteralPath $Path -Raw -Encoding utf8) -replace "`r`n", "`n").Trim()
}

$metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding utf8 | ConvertFrom-Json
$publishedId = "$($metadata.publishedFileId)".Trim()
if ($publishedId -notmatch '^\d+$' -or $publishedId -eq '0') { throw 'workshop.json 还没有有效的 publishedFileId，条目尚未发布。' }

$expected = [ordered]@{ english = [pscustomobject]@{ Title = "$($metadata.title)".Trim(); Description = Read-Bbcode (Join-Path $workshopDir 'description.bbcode') } }
if ($metadata.PSObject.Properties['titles'])
{
    foreach ($property in $metadata.titles.PSObject.Properties)
    {
        $language = $property.Name
        $expected[$language] = [pscustomobject]@{
            Title       = "$($property.Value)".Trim()
            Description = Read-Bbcode (Join-Path $workshopDir "description.$language.bbcode")
        }
    }
}
$expectedTags = @()
if ($metadata.PSObject.Properties['tags']) { $expectedTags = @($metadata.tags | ForEach-Object { "$_".Trim() } | Where-Object { $_ }) }

Write-Host "回查条目 $publishedId（需桌面 Steam 已登录）..."
$states = @(Get-WorkshopItemState -GameRoot (Get-ProbablyStolenGameRoot) -AppId $AppId -PublishedFileId $publishedId -Languages @($expected.Keys))

$differences = 0
function Write-Check
{
    param([string]$Label, [bool]$Ok, [string]$Detail)
    if ($Ok) { Write-Host "  [一致] $Label" }
    else { Write-Host "  [差异] $Label  $Detail"; $script:differences++ }
}
foreach ($state in $states)
{
    $want = $expected[$state.Language]
    $remoteDescription = ("$($state.Description)" -replace "`r`n", "`n").Trim()
    Write-Check "$($state.Language) 标题" ($state.Title -ceq $want.Title) "本地「$($want.Title)」，工坊「$($state.Title)」"
    Write-Check "$($state.Language) 描述" ($remoteDescription -ceq $want.Description) "本地 $($want.Description.Length) 字符，工坊 $($remoteDescription.Length) 字符"
}
$remoteTags = @($states[0].Tags)
$tagsOk = (@($expectedTags | Sort-Object) -join '|') -ceq (@($remoteTags | Sort-Object) -join '|')
Write-Check '标签' $tagsOk "本地「$($expectedTags -join ', ')」，工坊「$($remoteTags -join ', ')」"

Write-Host ''
Write-Host ("可见性：{0}    内容大小：{1:N0} B    最后更新（UTC）：{2:yyyy-MM-dd HH:mm:ss}" -f $states[0].Visibility, $states[0].FileSize, $states[0].UpdatedUtc)
Write-Host "条目页面：https://steamcommunity.com/sharedfiles/filedetails/?id=$publishedId"
if ($differences -gt 0)
{
    Write-Host "共 $differences 项与本地不一致。"
    exit 1
}
Write-Host '工坊状态与本地文案一致。'
