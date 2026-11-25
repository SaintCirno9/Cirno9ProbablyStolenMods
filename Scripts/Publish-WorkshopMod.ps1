<#
.SYNOPSIS
    构建模组、整理上传目录，并通过 SteamCMD 创建或更新 Steam 创意工坊条目。
.DESCRIPTION
    流程依据官方模组指南 ModdingGuide.md 的
    “Steam Workshop / Publishing with steamcmd”与“Capabilities and the Scanner”章节。

    工坊元数据随模组提交，位于 Mods/<ModName>/Workshop/：
      workshop.json                条目 ID（首次发布前为 "0"）、英文标题 title、其他语言标题 titles、可选标签 tags、上次发布的 ModVersion
      description.bbcode           英文描述（默认语言 english，Steam BBCode）
      description.<语言>.bbcode    其他语言描述，如 description.schinese.bbcode
    每次发布都会用这些文件覆盖工坊页面上对应语言的标题和描述；Steam 按玩家客户端语言显示，缺失的语言回退英文。
    语言代码用 Steam API 语言名（schinese、tchinese、japanese 等）；同一语言的标题与描述文件必须成对出现。
    可见性只在首次创建或显式传 -Visibility 时设置；取值按官方指南：Public=0、FriendsOnly=1、Private=2（隐藏）。

    SteamCMD 的 workshop_build_item 没有语言参数，只能写英文。因此：
      完整发布：通过本机 Steam 客户端（Scripts/WorkshopText.ps1，Steamworks.NET 的 ISteamUGC）逐语言提交其他语言的
                标题与描述，最后再由 SteamCMD 上传内容并写英文标题与描述。SteamCMD 登录会挤下线桌面 Steam，
                所以放在最后；仅新条目因 ID 要等 SteamCMD 创建，只能事后提交，此时需桌面 Steam 已重新连接。
      -MetadataOnly：不使用 SteamCMD，全部经 ISteamUGC 提交（含 english），只需桌面 Steam 客户端已登录，不需要密码与令牌。
                只更新已有条目的标题、描述（及显式传入的可见性），不构建、不上传内容和预览图，也不检查或记录版本。
      -PreviewOnly：通过 SteamCMD 仅提交 previewfile（及可选的可见性、更新说明），省略 contentfolder 与文案字段；
                不构建、不上传模组内容、不经 ISteamUGC 改动标题与描述，也不检查或记录版本（仍使用 SteamCMD 登录缓存）。

    上传内容整理到 Artifacts/Workshop/<ModName>/content/，SteamCMD 配置写到 Artifacts/Workshop/<ModName>/item.vdf。
    工坊条目会被游戏整体扫描，脚本会拦下未声明的 DLL、脚本/可执行文件、压缩包、源美术文件和点开头的条目。
    游戏内 F8 控制台的 mod-scan 无法由脚本执行，上传前会列出发布检查并要求确认；传 -Yes 跳过确认。

    SteamCMD 查找顺序：-SteamCmdPath、.env 的 STEAMCMD_PATH、仓库 Tools/SteamCMD/steamcmd.exe、PATH。
    仓库内的 SteamCMD 由 Scripts/Install-SteamCmd.ps1 安装。
    发布时使用 -nobootstrapupdate -skipinitialbootstrap 跳过自身更新检查；需更新时直接运行 steamcmd +quit，不带这两个参数。
    SteamCMD 账号取值顺序：-SteamUser、.env 的 STEAM_USERNAME、SteamCMD 登录缓存（Tools/SteamCMD/config/config.vdf）。
    密码与 Steam 令牌验证码由 SteamCMD 交互输入，脚本不保存任何凭据。
    -Yes 为非交互模式：要求登录缓存已存在（否则在做任何提交之前就报错），并隐去 SteamCMD 输出里的账号名。
    发布前会预检：需要经 ISteamUGC 提交时检查桌面 Steam 是否运行，缺失时在改动任何东西之前报错。
    更新说明只写入 english；其他语言需要在条目“改动说明”页手动补，脚本结束时会提示。
.EXAMPLE
    pwsh -NoProfile -File Scripts/Publish-WorkshopMod.ps1 -ModName ReputationInterfaceRemake -DryRun
.EXAMPLE
    pwsh -NoProfile -File Scripts/Publish-WorkshopMod.ps1 -ModName ReputationInterfaceRemake -ChangeNote "修复滚动条位置"
.EXAMPLE
    pwsh -NoProfile -File Scripts/Publish-WorkshopMod.ps1 -ModName ReputationInterfaceRemake -MetadataOnly
.EXAMPLE
    pwsh -NoProfile -File Scripts/Publish-WorkshopMod.ps1 -ModName ReputationInterfaceRemake -PreviewOnly
.NOTES
    Author: SaintCirno9
#>
param(
    [Parameter(Mandatory)][string]$ModName,
    [string]$ChangeNote,
    [ValidateSet('Public', 'FriendsOnly', 'Private')][string]$Visibility,
    [string]$SteamUser,
    [string]$SteamCmdPath,
    [switch]$AllowSameVersion,
    [string]$SteamCellId,
    [switch]$SkipRegionSync,
    [switch]$MetadataOnly,
    [switch]$PreviewOnly,
    [switch]$Yes,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Get-GameRoot.ps1')
. (Join-Path $PSScriptRoot 'WorkshopText.ps1')
. (Join-Path $PSScriptRoot 'SteamCmdRegion.ps1')

$AppId = '4348910'
# 与 Directory.Build.targets 的部署资源保持一致。
$ResourceDirs = 'Textures', 'Localization', 'Sounds', 'Books', 'LootTables'
$VisibilityCodes = @{ Public = '0'; FriendsOnly = '1'; Private = '2' }
# Steam 的 API 语言代码，见 https://partner.steamgames.com/doc/store/localization/languages
$SteamLanguages = 'arabic', 'bulgarian', 'schinese', 'tchinese', 'czech', 'danish', 'dutch', 'english', 'finnish', 'french',
    'german', 'greek', 'hungarian', 'indonesian', 'italian', 'japanese', 'koreana', 'norwegian', 'polish', 'portuguese',
    'brazilian', 'romanian', 'russian', 'spanish', 'latam', 'swedish', 'thai', 'turkish', 'ukrainian', 'vietnamese', 'malay'
# 游戏扫描器会拒绝或官方指南要求排除的文件类型。
$ForbiddenExtensions = '.exe', '.bat', '.cmd', '.ps1', '.js', '.vbs', '.lnk', '.msi', '.zip', '.7z', '.rar', '.psd', '.aseprite'
$Utf8NoBom = [Text.UTF8Encoding]::new($false)

function Assert-VdfText
{
    param([string]$Label, [string]$Text, [int]$MaxLength)
    # SteamCMD 的 VDF 会处理反斜杠转义，直接拒绝双引号与反斜杠，避免内容被截断或改写。
    if ($Text -match '["\\]') { throw "$Label 不能包含双引号或反斜杠（VDF 转义）。" }
    if ($Text.Length -gt $MaxLength) { throw "$Label 超过 Steam 上限 $MaxLength 字符（当前 $($Text.Length)）。" }
}

function Assert-TextLimit
{
    param([string]$Label, [string]$Text, [int]$MaxLength)
    if ($Text.Length -gt $MaxLength) { throw "$Label 超过 Steam 上限 $MaxLength 字符（当前 $($Text.Length)）。" }
}

function Get-NormalizedVersion
{
    # 与游戏 ModUIManager.NormalizeVersion 一致：取开头的数字与点。
    param([string]$Version)
    return ([regex]::Match("$Version".Trim(), '^[\d.]*').Value).TrimEnd('.')
}

function Test-GameVersionMatch
{
    # 与游戏 ModUIManager.VersionMatchesGame 一致。
    param([string]$Declared, [string]$Game)
    $a = Get-NormalizedVersion $Declared
    $b = Get-NormalizedVersion $Game
    if (-not $a -or -not $b) { return $false }
    return $a -eq $b -or $a.StartsWith("$b.") -or $b.StartsWith("$a.")
}

function Resolve-SteamCmdExecutable
{
    param([string]$Root, [string]$OverridePath)
    $candidate = Get-ProbablyStolenSetting -Name 'STEAMCMD_PATH' -Value $OverridePath -Optional
    if (-not $candidate)
    {
        $bundled = Join-Path $Root 'Tools\SteamCMD\steamcmd.exe'
        if (Test-Path -LiteralPath $bundled -PathType Leaf) { $candidate = $bundled }
    }
    if (-not $candidate)
    {
        $command = Get-Command steamcmd -ErrorAction SilentlyContinue
        if ($null -ne $command) { $candidate = $command.Source }
    }
    return $candidate
}

function Get-SteamCmdConfigVdfPath
{
    param([string]$Root, [string]$SteamCmdExePath)
    if ($SteamCmdExePath)
    {
        return Join-Path (Split-Path -Parent $SteamCmdExePath) 'config\config.vdf'
    }
    return Join-Path $Root 'Tools\SteamCMD\config\config.vdf'
}

function Get-SteamCmdCachedAccount
{
    # SteamCMD 登录后会把账号缓存在自带的 config.vdf 里；读不到时返回 $null。
    param([string]$ConfigPath)
    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { return $null }
    $match = [regex]::Match((Get-Content -LiteralPath $ConfigPath -Raw), '"Accounts"\s*\{\s*"([^"]+)"')
    if ($match.Success) { return $match.Groups[1].Value }
    return $null
}

function Assert-DesktopSteamRunning
{
    param([string]$Reason)
    if (-not (Get-Process -Name steam -ErrorAction SilentlyContinue))
    {
        throw "桌面 Steam 未运行：$Reason。请启动并登录 Steam 后重试。"
    }
}

function Format-VdfPath
{
    param([string]$Path)
    return [IO.Path]::GetFullPath($Path) -replace '\\', '/'
}

if ($ModName -notmatch '^[A-Za-z0-9_]+$') { throw "模组名只能包含字母、数字和下划线：$ModName" }
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$modDir = Join-Path $repositoryRoot "Mods\$ModName"
$projectPath = Join-Path $modDir "$ModName.csproj"
$manifestPath = Join-Path $modDir 'manifest.xml'
$previewPath = Join-Path $modDir 'preview.png'
$workshopPreviewGifPath = Join-Path $modDir 'Workshop\preview.gif'
$metadataPath = Join-Path $modDir 'Workshop\workshop.json'
$descriptionPath = Join-Path $modDir 'Workshop\description.bbcode'
foreach ($path in $projectPath, $manifestPath, $previewPath, $metadataPath, $descriptionPath)
{
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "缺少文件：$path" }
}
if ((Get-Item -LiteralPath $previewPath).Length -ge 1MB) { throw 'preview.png 需小于 1 MB（官方指南要求）。' }
$hasWorkshopPreviewGif = Test-Path -LiteralPath $workshopPreviewGifPath -PathType Leaf
if ($hasWorkshopPreviewGif -and (Get-Item -LiteralPath $workshopPreviewGifPath).Length -ge 1MB)
{
    throw 'Workshop\preview.gif 需小于 1 MB（官方指南要求）。'
}
$workshopPreviewSourcePath = if ($hasWorkshopPreviewGif) { $workshopPreviewGifPath } else { $previewPath }

[xml]$manifestXml = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8
$manifest = $manifestXml.Manifest
$modId = "$($manifest.ID)".Trim()
$modVersion = "$($manifest.ModVersion)".Trim()
if (-not $modId -or -not $modVersion) { throw 'manifest.xml 缺少 ID 或 ModVersion。' }
$assemblies = @($manifest.Assemblies.Assembly | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
if ($assemblies.Count -eq 0) { throw 'manifest.xml 未声明 Assemblies。' }
$gameVersions = @($manifest.GameVersions.Version | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
if ($gameVersions.Count -eq 0) { throw 'manifest.xml 未声明 GameVersions，模组菜单无法判断兼容性。' }

$metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding utf8 | ConvertFrom-Json
$publishedId = "$($metadata.publishedFileId)".Trim()
if ($publishedId -notmatch '^\d+$') { throw 'workshop.json 的 publishedFileId 必须是数字字符串，首次发布填 "0"。' }
$isNew = $publishedId -eq '0'
$title = "$($metadata.title)".Trim()
if (-not $title) { throw 'workshop.json 缺少 title。' }
$description = ((Get-Content -LiteralPath $descriptionPath -Raw -Encoding utf8) -replace "`r`n", "`n").Trim()
if (-not $description) { throw 'description.bbcode 为空。' }

$titles = [ordered]@{}
if ($metadata.PSObject.Properties['titles'])
{
    foreach ($property in $metadata.titles.PSObject.Properties) { $titles[$property.Name] = "$($property.Value)".Trim() }
}
$tags = @()
if ($metadata.PSObject.Properties['tags'])
{
    $tags = @($metadata.tags | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
    foreach ($tag in $tags) { Assert-TextLimit "tags 项 $tag" $tag 255 }
}
$localizedDescriptions = [ordered]@{}
$descriptionFiles = @(Get-ChildItem -LiteralPath (Join-Path $modDir 'Workshop') -Filter 'description.*.bbcode' -File)
foreach ($file in $descriptionFiles)
{
    $language = $file.Name.Substring('description.'.Length, $file.Name.Length - 'description.'.Length - '.bbcode'.Length)
    $localizedDescriptions[$language] = ((Get-Content -LiteralPath $file.FullName -Raw -Encoding utf8) -replace "`r`n", "`n").Trim()
}
$localizedTexts = @()
foreach ($language in @($titles.Keys) + @($localizedDescriptions.Keys) | Select-Object -Unique)
{
    if ($language -notin $SteamLanguages -or $language -eq 'english')
    {
        throw "不支持的语言代码 ${language}：须为 Steam API 语言名（如 schinese、tchinese、japanese），且不能是 english（英文用 title 与 description.bbcode）。"
    }
    if (-not $titles[$language]) { throw "语言 $language 缺少 workshop.json 的 titles.$language。" }
    if (-not $localizedDescriptions[$language]) { throw "语言 $language 缺少或为空的 description.$language.bbcode。" }
    Assert-TextLimit "titles.$language" $titles[$language] 128
    Assert-TextLimit "description.$language.bbcode" $localizedDescriptions[$language] 8000
    $localizedTexts += [pscustomobject]@{ Language = $language; Title = $titles[$language]; Description = $localizedDescriptions[$language] }
}
# 完整发布时经 ISteamUGC 额外提交的文案：其他语言；没有其他语言但设置了标签时，补一条 english 文案来携带标签。
$extraTexts = @($localizedTexts)
if ($extraTexts.Count -eq 0 -and $tags.Count -gt 0)
{
    $extraTexts = @([pscustomobject]@{ Language = 'english'; Title = $title; Description = $description })
}
if ($MetadataOnly -and $PreviewOnly) { throw '-MetadataOnly 与 -PreviewOnly 不能同时使用。' }
if ($MetadataOnly)
{
    if ($isNew) { throw '-MetadataOnly 只能更新已有条目；首次发布需要上传内容。' }
    if ($ChangeNote -or $AllowSameVersion) { throw '-MetadataOnly 不上传内容，不能与 -ChangeNote、-AllowSameVersion 同时使用。' }
}
elseif ($PreviewOnly)
{
    if ($isNew) { throw '-PreviewOnly 只能更新已有条目；首次发布需要上传内容。' }
    if ($AllowSameVersion) { throw '-PreviewOnly 不上传内容或记录版本，不能与 -AllowSameVersion 同时使用。' }
    if ($ChangeNote) { Assert-VdfText 'ChangeNote' $ChangeNote 8000 }
}
else
{
    if (-not $isNew -and "$($metadata.lastPublishedVersion)" -eq $modVersion -and -not $AllowSameVersion)
    {
        throw "ModVersion $modVersion 已发布过。请先提高 manifest.xml 的 ModVersion，或传 -AllowSameVersion。"
    }
    if (-not $ChangeNote) { $ChangeNote = "v$modVersion" }
    Assert-VdfText 'ChangeNote' $ChangeNote 8000
    # 英文标题与描述经 SteamCMD 的 VDF 提交，需满足 VDF 转义限制；其他语言走 ISteamUGC，不受限。
    Assert-VdfText 'title' $title 128
    Assert-VdfText 'description.bbcode' $description 8000
}
if (-not $Visibility -and $isNew) { $Visibility = 'Private' }
Assert-TextLimit 'title' $title 128
Assert-TextLimit 'description.bbcode' $description 8000

$stageRoot = Join-Path $repositoryRoot "Artifacts\Workshop\$ModName"
$contentDir = Join-Path $stageRoot 'content'
$vdfPath = Join-Path $stageRoot 'item.vdf'
$previewSize = (Get-Item -LiteralPath $workshopPreviewSourcePath).Length

function Write-WorkshopVdf
{
    param([string]$Path, [System.Collections.IDictionary]$Fields)
    $vdfLines = @('"workshopitem"', '{')
    foreach ($entry in $Fields.GetEnumerator()) { $vdfLines += "`t`"$($entry.Key)`"`t`t`"$($entry.Value)`"" }
    $vdfLines += '}'
    [IO.File]::WriteAllText($Path, ($vdfLines -join "`n") + "`n", $Utf8NoBom)
}

if ($PreviewOnly)
{
    New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null
    $fields = [ordered]@{
        appid           = $AppId
        publishedfileid = $publishedId
        previewfile     = Format-VdfPath $workshopPreviewSourcePath
    }
    if ($Visibility) { $fields.visibility = $VisibilityCodes[$Visibility] }
    if ($ChangeNote) { $fields.changenote = $ChangeNote }
    Write-WorkshopVdf -Path $vdfPath -Fields $fields
}
elseif (-not $MetadataOnly)
{
    Write-Host "正在构建 $ModName（Release，不部署到游戏目录）..."
    & dotnet build $projectPath -c Release -p:DeployMods=false -nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "构建失败（退出码 $LASTEXITCODE）。" }
    $outputDir = Join-Path $repositoryRoot "Artifacts\bin\$ModName\Release"

    # 上传目录由本脚本独占管理，每次重建，避免旧文件混入工坊条目。
    if (Test-Path -LiteralPath $contentDir) { Remove-Item -LiteralPath $contentDir -Recurse -Force }
    New-Item -ItemType Directory -Path $contentDir -Force | Out-Null
    foreach ($name in $assemblies)
    {
        if ([IO.Path]::GetFileName($name) -ne $name) { throw "manifest.xml 的 Assembly 只能填写文件名：$name" }
        $dllPath = Join-Path $outputDir $name
        if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) { throw "找不到 manifest.xml 声明的程序集：$dllPath" }
        Copy-Item -LiteralPath $dllPath -Destination $contentDir
    }
    Copy-Item -LiteralPath $manifestPath, $previewPath -Destination $contentDir
    foreach ($dir in $ResourceDirs)
    {
        $source = Join-Path $modDir $dir
        if (Test-Path -LiteralPath $source -PathType Container) { Copy-Item -LiteralPath $source -Destination $contentDir -Recurse }
    }

    # 工坊条目会被游戏整体扫描，提前拦下会被拒绝的内容。
    $contentItems = @(Get-ChildItem -LiteralPath $contentDir -Recurse -Force)
    if ($contentItems.Count -gt 50000) { throw '上传目录超过 50000 个文件，游戏会拒绝该模组。' }
    foreach ($item in $contentItems)
    {
        $relative = $item.FullName.Substring($contentDir.Length + 1)
        if ($item.Name.StartsWith('.')) { throw "上传目录不能包含点开头的文件或目录：$relative" }
        if ($item.PSIsContainer) { continue }
        $extension = $item.Extension.ToLowerInvariant()
        if ($ForbiddenExtensions -contains $extension) { throw "上传目录包含会被拒绝或不应发布的文件：$relative" }
        if ($extension -eq '.dll' -and ($relative -ne $item.Name -or $assemblies -notcontains $item.Name))
        {
            throw "上传目录包含 manifest.xml 未声明的 DLL：$relative"
        }
    }

    $playerLog = Join-Path $env:USERPROFILE 'AppData\LocalLow\Questing Goose Studio\Probably Stolen\Player.log'
    $gameVersion = $null
    if (Test-Path -LiteralPath $playerLog -PathType Leaf)
    {
        $auditLine = Select-String -LiteralPath $playerLog -Pattern '\[ModLoader\] audit: game (\S+),' | Select-Object -Last 1
        if ($auditLine) { $gameVersion = $auditLine.Matches[0].Groups[1].Value }
    }

    $previewUploadPath = if ($hasWorkshopPreviewGif) { $workshopPreviewSourcePath } else { Join-Path $contentDir 'preview.png' }
    $fields = [ordered]@{
        appid           = $AppId
        publishedfileid = $publishedId
        contentfolder   = Format-VdfPath $contentDir
        previewfile     = Format-VdfPath $previewUploadPath
    }
    if ($Visibility) { $fields.visibility = $VisibilityCodes[$Visibility] }
    $fields.title = $title
    $fields.description = $description
    $fields.changenote = $ChangeNote
    Write-WorkshopVdf -Path $vdfPath -Fields $fields
}

Write-Host ''
Write-Host "模组：$modId v$modVersion"
Write-Host ("条目：" + $(if ($isNew) { '新建' } else { "更新 $publishedId" }))
Write-Host ("可见性：" + $(if ($Visibility) { $Visibility } else { '保持不变' }))
if (-not $PreviewOnly)
{
    Write-Host "标题（english）：$title"
    foreach ($text in $localizedTexts)
    {
        Write-Host ("标题（{0}）：{1}  描述 {2} 字符" -f $text.Language, $text.Title, $text.Description.Length)
    }
    Write-Host ("描述（english）：{0} 字符" -f $description.Length)
    if ($tags.Count -gt 0)
    {
        Write-Host ("标签：" + ($tags -join ', '))
    }
}
if ($MetadataOnly)
{
    Write-Host '模式：仅更新标题与描述（经本机 Steam 客户端 ISteamUGC），不上传内容和预览图，不修改 workshop.json。'
}
elseif ($PreviewOnly)
{
    Write-Host '模式：仅更新主封面图（经 SteamCMD previewfile），不上传内容，不改动标题与描述，不修改 workshop.json。'
    Write-Host ("封面图：{0}  {1:N0} B" -f $workshopPreviewSourcePath, $previewSize)
    Write-Host ("更新说明：" + $(if ($ChangeNote) { $ChangeNote } else { '（省略）' }))
    Write-Host "SteamCMD 配置：$vdfPath"
}
else
{
    Write-Host "更新说明：$ChangeNote"
    Write-Host ("工坊封面：{0}  {1:N0} B" -f $workshopPreviewSourcePath, $previewSize)
    Write-Host "上传目录：$contentDir"
    Get-ChildItem -LiteralPath $contentDir -Recurse -File | ForEach-Object {
        Write-Host ("  {0}  {1:N0} B" -f $_.FullName.Substring($contentDir.Length + 1), $_.Length)
    }
    Write-Host "SteamCMD 配置：$vdfPath"
    Write-Host ("GameVersions：" + ($gameVersions -join ', '))
    if (-not $gameVersion)
    {
        Write-Warning '未从 Player.log 读到游戏版本，无法核对 GameVersions；请确认已按当前游戏版本填写。'
    }
    elseif (-not ($gameVersions | Where-Object { Test-GameVersionMatch $_ $gameVersion }))
    {
        Write-Warning "manifest.xml 的 GameVersions 与本机游戏版本 $gameVersion 不匹配，模组菜单会标红。"
    }
    else
    {
        Write-Host "本机游戏版本 $gameVersion 与 GameVersions 匹配。"
    }
}

$steamCmd = if (-not $MetadataOnly) { Resolve-SteamCmdExecutable -Root $repositoryRoot -OverridePath $SteamCmdPath } else { $null }
$steamCmdConfigPath = if (-not $MetadataOnly) { Get-SteamCmdConfigVdfPath -Root $repositoryRoot -SteamCmdExePath $steamCmd } else { $null }

$resolvedSteamCellId = Get-ProbablyStolenSetting -Name 'STEAMCMD_CELL_ID' -Value $SteamCellId -Optional

if ($DryRun)
{
    Write-Host ''
    if ($MetadataOnly)
    {
        Write-Host 'DryRun：未提交。实际更新时经本机 Steam 客户端逐语言调用 ISteamUGC。'
    }
    else
    {
        Sync-SteamCmdRegion -ConfigVdfPath $steamCmdConfigPath -OverrideCellId $resolvedSteamCellId -SkipRegionSync:$SkipRegionSync
        Write-Host ''
        Write-Host "DryRun：未上传。实际发布时执行：steamcmd -nobootstrapupdate -skipinitialbootstrap +login <账号> +workshop_build_item `"$vdfPath`" +quit"
        if (-not $PreviewOnly -and $extraTexts.Count -gt 0)
        {
            Write-Host ($(if ($isNew) { '创建条目后' } else { '运行 SteamCMD 之前' }) + '，经本机 Steam 客户端逐语言提交其他语言的标题与描述（以及标签）。')
        }
    }
    return
}

if (-not $MetadataOnly)
{
    if (-not $steamCmd -or -not (Test-Path -LiteralPath $steamCmd -PathType Leaf))
    {
        throw '找不到 steamcmd.exe。请先运行 Scripts/Install-SteamCmd.ps1，或在 .env 配置 STEAMCMD_PATH、传 -SteamCmdPath。'
    }

    # 账号：-SteamUser、.env 的 STEAM_USERNAME、SteamCMD 登录缓存，依次取第一个。
    # -Yes 不能交互输入密码与令牌，所以要求登录缓存存在，避免做完其他语言提交后才卡在密码提示。
    $cachedAccount = Get-SteamCmdCachedAccount $steamCmdConfigPath
    if ($Yes -and -not $cachedAccount)
    {
        throw '-Yes 需要 SteamCMD 登录缓存（Tools\SteamCMD\config\config.vdf 的 Accounts）。请先在终端交互登录一次：Tools\SteamCMD\steamcmd.exe +login <账号> +quit'
    }
    $user = Get-ProbablyStolenSetting -Name 'STEAM_USERNAME' -Value $SteamUser -Optional
    if (-not $user) { $user = $cachedAccount }
    if (-not $user) { $user = Read-Host 'Steam 账号' }
    if (-not $user) { throw '未提供 Steam 账号。' }
    if (-not $isNew -and -not $PreviewOnly -and $extraTexts.Count -gt 0)
    {
        Assert-DesktopSteamRunning '已有条目要先经 ISteamUGC 提交其他语言文案，再由 SteamCMD 上传'
    }
}
else
{
    Assert-DesktopSteamRunning '-MetadataOnly 经 ISteamUGC 提交'
}

if ($MetadataOnly -and -not $Yes)
{
    if ((Read-Host '确认用本地标题与描述覆盖工坊页面？输入 y 继续') -ne 'y')
    {
        Write-Host '已取消，未上传。'
        return
    }
}
elseif ($PreviewOnly -and -not $Yes)
{
    if ((Read-Host '确认仅上传新封面图覆盖工坊主预览图（不改动内容与文案）？输入 y 继续') -ne 'y')
    {
        Write-Host '已取消，未上传。'
        return
    }
}
elseif (-not $Yes)
{
    Write-Host ''
    Write-Host '发布前检查（官方指南 Before a release）：'
    Write-Host "  1. 游戏内 F8 控制台执行 mod-scan $modId：declared 覆盖 detected，无 hard flag 与 ERROR。"
    Write-Host '  2. <Capabilities> 不多于实际需要；之后新增能力会让所有玩家重新审核。'
    Write-Host '  3. 依赖的其他模组已写入 <Prerequisites>，并在工坊描述中说明。'
    if ((Read-Host '确认上传？输入 y 继续') -ne 'y')
    {
        Write-Host '已取消，未上传。'
        return
    }
}

$gameRoot = if ($MetadataOnly -or (-not $PreviewOnly -and $extraTexts.Count -gt 0)) { Get-ProbablyStolenGameRoot } else { $null }
if ($MetadataOnly)
{
    $visibilityCode = if ($Visibility) { $VisibilityCodes[$Visibility] } else { $null }
    $texts = @([pscustomobject]@{ Language = 'english'; Title = $title; Description = $description }) + $localizedTexts
    Write-Host ''
    Write-Host '经本机 Steam 客户端提交（需桌面 Steam 已登录）...'
    Set-WorkshopItemText -GameRoot $gameRoot -AppId $AppId -PublishedFileId $publishedId -Texts $texts -VisibilityCode $visibilityCode -Tags $tags
    Write-Host ''
    Write-Host '已更新工坊标题与描述。'
    Write-Host "条目页面：https://steamcommunity.com/sharedfiles/filedetails/?id=$publishedId"
    return
}


# SteamCMD 登录会把同账号的桌面 Steam 挤下线，之后 ISteamUGC 无法使用，所以已有条目先提交其他语言，SteamCMD 放最后。
# 新条目的 ID 要等 SteamCMD 创建后才有，只能事后提交。
if (-not $isNew -and -not $PreviewOnly -and $extraTexts.Count -gt 0)
{
    Write-Host ''
    Write-Host '先经本机 Steam 客户端提交其他语言的标题与描述、标签（需桌面 Steam 已登录）...'
    Set-WorkshopItemText -GameRoot $gameRoot -AppId $AppId -PublishedFileId $publishedId -Texts $extraTexts -Tags $tags
}

Write-Host ''
Sync-SteamCmdRegion -ConfigVdfPath $steamCmdConfigPath -BackupDir (Join-Path $repositoryRoot '.cache') -OverrideCellId $resolvedSteamCellId -SkipRegionSync:$SkipRegionSync -Apply
Write-Host ''
if ($Yes)
{
    # 非交互模式（多为 AI 代跑）：用登录缓存登录，并隐去输出里的账号名，避免被原样贴出。
    Write-Host "启动 SteamCMD（$steamCmd），使用登录缓存..."
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & $steamCmd -nobootstrapupdate -skipinitialbootstrap +login $user +workshop_build_item $vdfPath +quit 2>&1 | ForEach-Object { "$_" -replace [regex]::Escape($user), '<账号>' }
    $ErrorActionPreference = $previousPreference
}
else
{
    Write-Host "启动 SteamCMD（$steamCmd），按提示输入密码与 Steam 令牌验证码..."
    & $steamCmd -nobootstrapupdate -skipinitialbootstrap +login $user +workshop_build_item $vdfPath +quit
}
if ($LASTEXITCODE -ne 0) { throw "SteamCMD 退出码 $LASTEXITCODE，上传未完成；workshop.json 未修改。" }

if ($PreviewOnly)
{
    Write-Host ''
    Write-Host '已更新工坊主封面图（workshop.json 未修改）。'
    Write-Host "条目页面：https://steamcommunity.com/sharedfiles/filedetails/?id=$publishedId"
    return
}

if ($isNew)
{
    # 官方指南：SteamCMD 创建条目后会把新 ID 回写到 VDF 的 publishedfileid。
    $writtenId = $null
    if ((Get-Content -LiteralPath $vdfPath -Raw -Encoding utf8) -match '"publishedfileid"\s+"(\d+)"') { $writtenId = $Matches[1] }
    if (-not $writtenId -or $writtenId -eq '0')
    {
        $writtenId = (Read-Host 'SteamCMD 未回写条目 ID，请从上方输出或条目网址复制 ID').Trim()
    }
    if ($writtenId -notmatch '^\d+$' -or $writtenId -eq '0')
    {
        throw '未取得有效条目 ID，workshop.json 未修改。请手动填写 publishedFileId，避免下次发布重复创建条目。'
    }
    $publishedId = $writtenId
}

$updated = [ordered]@{
    publishedFileId = $publishedId
    title           = $title
}
if ($titles.Count -gt 0) { $updated.titles = $titles }
if ($tags.Count -gt 0) { $updated.tags = $tags }
$updated.lastPublishedVersion = $modVersion
[IO.File]::WriteAllText($metadataPath, ($updated | ConvertTo-Json) + "`n", $Utf8NoBom)
Write-Host ''
Write-Host "已记录到 $metadataPath，请提交该文件。"
Write-Host "条目页面：https://steamcommunity.com/sharedfiles/filedetails/?id=$publishedId"
if ($isNew)
{
    Write-Host '新条目默认隐藏：首次上传需在 Steam 网站接受一次创意工坊法律协议，确认内容后再改为公开。'
}
if ($localizedTexts.Count -gt 0)
{
    # SteamCMD 只写 english；ISteamUGC 的更新说明只在带内容变化的提交里生效，且一条记录只带一种语言，无法给同一版本补其他语言。
    $languageList = ($localizedTexts | ForEach-Object { $_.Language }) -join '、'
    Write-Host ''
    Write-Host "改动说明只写入了 english。其他语言（$languageList）请手动补：打开 https://steamcommunity.com/sharedfiles/filedetails/changelog/$publishedId ，点最新一条的“编辑”，切换语言后粘贴。"
}

if ($isNew -and $extraTexts.Count -gt 0)
{
    Write-Host ''
    Write-Host '经本机 Steam 客户端提交其他语言的标题与描述（SteamCMD 登录后需桌面 Steam 已重新连接）...'
    try
    {
        Set-WorkshopItemText -GameRoot $gameRoot -AppId $AppId -PublishedFileId $publishedId -Texts $extraTexts -Tags $tags
    }
    catch
    {
        throw "内容已上传且 workshop.json 已记录，但其他语言文案提交失败：$($_.Exception.Message) 重新连接桌面 Steam 后用 -MetadataOnly 重试。"
    }
    Write-Host '已更新其他语言的标题与描述。'
}
