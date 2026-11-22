<#
.SYNOPSIS
    通过本机已登录的 Steam 客户端（游戏自带的 Steamworks.NET，ISteamUGC）按语言更新创意工坊条目的标题与描述。
.DESCRIPTION
    SteamCMD 的 workshop_build_item 只识别 title、description 等固定键，没有语言参数，只能写入默认语言 english。
    这里改用 ISteamUGC：每种语言单独调用一次 StartItemUpdate、SetItemUpdateLanguage、SetItemTitle、
    SetItemDescription、SubmitItemUpdate，不上传内容和预览图，也不附带更新说明。

    前提：桌面 Steam 客户端正在运行且已登录拥有游戏的账号。不需要密码与令牌，也不会挤下线客户端。
    语言代码使用 Steam API 语言名，如 english、schinese、tchinese、japanese。
    由 Publish-WorkshopMod.ps1 通过点号引入，不单独运行。
.NOTES
    Author: SaintCirno9
#>

function Assert-SteamCall
{
    param([bool]$Ok, [string]$Action)
    if (-not $Ok) { throw "Steam 拒绝了调用：$Action。" }
}

function Initialize-Steamworks
{
    # 加载游戏自带的 Steamworks.NET 并初始化；调用方负责在 finally 里 [Steamworks.SteamAPI]::Shutdown()。
    param([Parameter(Mandatory)][string]$GameRoot, [Parameter(Mandatory)][string]$AppId)
    $dataDir = Join-Path $GameRoot 'Probably Stolen_Data'
    $nativeDll = Join-Path $dataDir 'Plugins\x86_64\steam_api64.dll'
    $managedDll = Join-Path $dataDir 'Managed\com.rlabrecque.steamworks.net.dll'
    foreach ($path in $nativeDll, $managedDll)
    {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "找不到 Steamworks 文件：$path" }
    }
    if (-not ('Steamworks.SteamAPI' -as [type]))
    {
        [Runtime.InteropServices.NativeLibrary]::Load($nativeDll) | Out-Null
        [Reflection.Assembly]::LoadFrom($managedDll) | Out-Null
    }
    $env:SteamAppId = $AppId
    $message = ''
    $initResult = [Steamworks.SteamAPI]::InitEx([ref]$message)
    if ($initResult -ne [Steamworks.ESteamAPIInitResult]::k_ESteamAPIInitResult_OK)
    {
        throw "Steamworks 初始化失败：$initResult $message。请确认桌面 Steam 客户端已运行并登录。"
    }
}

function Set-WorkshopItemText
{
    <#
    .PARAMETER Texts
        每项含 Language、Title、Description 的对象或哈希表，按顺序逐个提交。
    .PARAMETER VisibilityCode
        可选，官方取值 '0' 公开、'1' 好友、'2' 隐藏；只随第一次提交设置。
    .PARAMETER Tags
        可选，条目标签（如 Mod、Gameplay）；只随第一次提交设置，会替换条目现有的全部标签。省略或为空时不改动标签。
    #>
    param(
        [Parameter(Mandatory)][string]$GameRoot,
        [Parameter(Mandatory)][string]$AppId,
        [Parameter(Mandatory)][string]$PublishedFileId,
        [Parameter(Mandatory)][object[]]$Texts,
        [string]$VisibilityCode,
        [string[]]$Tags,
        [int]$TimeoutSeconds = 120
    )
    Initialize-Steamworks -GameRoot $GameRoot -AppId $AppId
    try
    {
        $appIdValue = [Steamworks.AppId_t]::new([uint32]$AppId)
        $fileIdValue = [Steamworks.PublishedFileId_t]::new([uint64]$PublishedFileId)
        $first = $true
        foreach ($text in $Texts)
        {
            $language = "$($text.Language)"
            Write-Host "  提交 $language 标题与描述..."
            $handle = [Steamworks.SteamUGC]::StartItemUpdate($appIdValue, $fileIdValue)
            Assert-SteamCall ([Steamworks.SteamUGC]::SetItemUpdateLanguage($handle, $language)) "SetItemUpdateLanguage($language)"
            Assert-SteamCall ([Steamworks.SteamUGC]::SetItemTitle($handle, "$($text.Title)")) "SetItemTitle($language)"
            Assert-SteamCall ([Steamworks.SteamUGC]::SetItemDescription($handle, "$($text.Description)")) "SetItemDescription($language)"
            if ($first -and $VisibilityCode)
            {
                $visibility = [Steamworks.ERemoteStoragePublishedFileVisibility][int]$VisibilityCode
                Assert-SteamCall ([Steamworks.SteamUGC]::SetItemVisibility($handle, $visibility)) 'SetItemVisibility'
            }
            if ($first -and $Tags -and $Tags.Count -gt 0)
            {
                Write-Host "  设置标签：$($Tags -join ', ')"
                Assert-SteamCall ([Steamworks.SteamUGC]::SetItemTags($handle, [string[]]$Tags, $false)) 'SetItemTags'
            }
            $first = $false

            $call = [Steamworks.SteamUGC]::SubmitItemUpdate($handle, $null)
            $failed = $false
            $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
            while (-not [Steamworks.SteamUtils]::IsAPICallCompleted($call, [ref]$failed))
            {
                if ([datetime]::UtcNow -gt $deadline) { throw "提交 $language 超时（$TimeoutSeconds 秒）。" }
                [Steamworks.SteamAPI]::RunCallbacks()
                Start-Sleep -Milliseconds 100
            }
            if ($failed)
            {
                throw "提交 $language 失败：$([Steamworks.SteamUtils]::GetAPICallFailureReason($call))。"
            }
            $size = [Runtime.InteropServices.Marshal]::SizeOf[Steamworks.SubmitItemUpdateResult_t]()
            $buffer = [Runtime.InteropServices.Marshal]::AllocHGlobal($size)
            try
            {
                $callbackId = [Steamworks.SubmitItemUpdateResult_t]::k_iCallback
                if (-not [Steamworks.SteamUtils]::GetAPICallResult($call, $buffer, $size, $callbackId, [ref]$failed))
                {
                    throw "读取 $language 的提交结果失败。"
                }
                $result = [Runtime.InteropServices.Marshal]::PtrToStructure[Steamworks.SubmitItemUpdateResult_t]($buffer)
            }
            finally
            {
                [Runtime.InteropServices.Marshal]::FreeHGlobal($buffer)
            }
            if ($result.m_eResult -ne [Steamworks.EResult]::k_EResultOK)
            {
                throw "提交 $language 失败：$($result.m_eResult)。"
            }
            if ($result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
            {
                Write-Warning '需要在 Steam 网站接受创意工坊法律协议：https://steamcommunity.com/sharedfiles/workshoplegalagreement'
            }
        }
    }
    finally
    {
        [Steamworks.SteamAPI]::Shutdown()
    }
}
function Get-WorkshopItemState
{
    <#
    .SYNOPSIS
        经本机 Steam 客户端回查条目当前在工坊上的状态，每种语言一项。
    .OUTPUTS
        每项含 Language、Title、Description、Tags、Visibility、FileSize、UpdatedUtc。
        Steam 对没有对应语言文案的语言会回退到 english，所以回查某语言时看到的是回退后的文本。
    #>
    param(
        [Parameter(Mandatory)][string]$GameRoot,
        [Parameter(Mandatory)][string]$AppId,
        [Parameter(Mandatory)][string]$PublishedFileId,
        [string[]]$Languages = @('english'),
        [int]$TimeoutSeconds = 60
    )
    Initialize-Steamworks -GameRoot $GameRoot -AppId $AppId
    try
    {
        $ids = [Steamworks.PublishedFileId_t[]]@([Steamworks.PublishedFileId_t]::new([uint64]$PublishedFileId))
        foreach ($language in $Languages)
        {
            $query = [Steamworks.SteamUGC]::CreateQueryUGCDetailsRequest($ids, [uint32]1)
            Assert-SteamCall ([Steamworks.SteamUGC]::SetLanguage($query, $language)) "SetLanguage($language)"
            [Steamworks.SteamUGC]::SetReturnLongDescription($query, $true) | Out-Null
            [Steamworks.SteamUGC]::SetAllowCachedResponse($query, 0) | Out-Null
            $call = [Steamworks.SteamUGC]::SendQueryUGCRequest($query)
            $failed = $false
            $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
            while (-not [Steamworks.SteamUtils]::IsAPICallCompleted($call, [ref]$failed))
            {
                if ([datetime]::UtcNow -gt $deadline) { throw "查询 $language 超时（$TimeoutSeconds 秒）。" }
                [Steamworks.SteamAPI]::RunCallbacks()
                Start-Sleep -Milliseconds 100
            }
            if ($failed) { throw "查询 $language 失败：$([Steamworks.SteamUtils]::GetAPICallFailureReason($call))。" }
            $size = [Runtime.InteropServices.Marshal]::SizeOf[Steamworks.SteamUGCQueryCompleted_t]()
            $buffer = [Runtime.InteropServices.Marshal]::AllocHGlobal($size)
            try
            {
                $callbackId = [Steamworks.SteamUGCQueryCompleted_t]::k_iCallback
                if (-not [Steamworks.SteamUtils]::GetAPICallResult($call, $buffer, $size, $callbackId, [ref]$failed))
                {
                    throw "读取 $language 的查询结果失败。"
                }
                $completed = [Runtime.InteropServices.Marshal]::PtrToStructure[Steamworks.SteamUGCQueryCompleted_t]($buffer)
            }
            finally
            {
                [Runtime.InteropServices.Marshal]::FreeHGlobal($buffer)
            }
            if ($completed.m_eResult -ne [Steamworks.EResult]::k_EResultOK -or $completed.m_unNumResultsReturned -lt 1)
            {
                throw "查询 $language 没有返回条目：$($completed.m_eResult)。"
            }
            $details = [Steamworks.SteamUGCDetails_t]::new()
            Assert-SteamCall ([Steamworks.SteamUGC]::GetQueryUGCResult($completed.m_handle, [uint32]0, [ref]$details)) 'GetQueryUGCResult'
            [pscustomobject]@{
                Language    = $language
                Title       = $details.m_rgchTitle
                Description = $details.m_rgchDescription
                Tags        = @("$($details.m_rgchTags)".Split(',', [StringSplitOptions]::RemoveEmptyEntries) | ForEach-Object { $_.Trim() })
                Visibility  = "$($details.m_eVisibility)" -replace '^k_ERemoteStoragePublishedFileVisibility', ''
                FileSize    = $details.m_nFileSize
                UpdatedUtc  = [DateTimeOffset]::FromUnixTimeSeconds([int64]$details.m_rtimeUpdated).UtcDateTime
            }
            [Steamworks.SteamUGC]::ReleaseQueryUGCRequest($completed.m_handle) | Out-Null
        }
    }
    finally
    {
        [Steamworks.SteamAPI]::Shutdown()
    }
}
