<#
.SYNOPSIS
    检测当前代理/网络出口区域与 SteamCMD 的 CellID 出口配置，并在区域不匹配或未固定时自动修正 config.vdf。
.DESCRIPTION
    由 Publish-WorkshopMod.ps1 通过点号引入，不单独运行。
    SteamCMD 登录成功后，服务端会在 CMsgClientLogonResponse 中向 config.vdf 回写 CellIDServerOverride；
    但 SteamCMD 的 GetCellID() 会优先使用 30 天（2592000 秒）内有效的 CurrentCellID（配合 TimeCellIDSet），
    仅当 CurrentCellID 缺失、为 -1 或过期时才回退读取 CellIDServerOverride。
    因此脚本按当前代理出口所在大区优先写入已验证的区域 CellID，同时更新 CurrentCellID、TimeCellIDSet 与
    CellIDServerOverride，并清理 CMWebSocket 缓存。
.NOTES
    Author: SaintCirno9
#>

$script:SteamDcMacroMap = @{
    lax = 'NorthAmerica'; sea = 'NorthAmerica'; sjc = 'NorthAmerica'; dfw = 'NorthAmerica'
    ord = 'NorthAmerica'; atl = 'NorthAmerica'; iad = 'NorthAmerica'; ewr = 'NorthAmerica'
    yyz = 'NorthAmerica'; slc = 'NorthAmerica'; den = 'NorthAmerica'; phx = 'NorthAmerica'
    lhr = 'Europe'; par = 'Europe'; ams = 'Europe'; fra = 'Europe'
    sto = 'Europe'; vie = 'Europe'; mad = 'Europe'; waw = 'Europe'; hel = 'Europe'
    hkg = 'EastAsia-CNHK'; sha = 'EastAsia-CNHK'; pek = 'EastAsia-CNHK'
    can = 'EastAsia-CNHK'; tsn = 'EastAsia-CNHK'; ctu = 'EastAsia-CNHK'
    tyo = 'EastAsia-JP'; osa = 'EastAsia-JP'
    seo = 'EastAsia-KR'
    sgp = 'SEAsia'
    syd = 'Oceania'; mel = 'Oceania'
    gru = 'SouthAmerica'; scl = 'SouthAmerica'; eze = 'SouthAmerica'; lim = 'SouthAmerica'
    maa = 'SouthAsia'; bom = 'SouthAsia'
    dxb = 'MiddleEast'
}

$script:CountryMacroMap = @{
    US = 'NorthAmerica'; CA = 'NorthAmerica'; MX = 'NorthAmerica'
    GB = 'Europe'; UK = 'Europe'; FR = 'Europe'; DE = 'Europe'; NL = 'Europe'
    SE = 'Europe'; FI = 'Europe'; NO = 'Europe'; DK = 'Europe'; PL = 'Europe'
    ES = 'Europe'; IT = 'Europe'; CH = 'Europe'; AT = 'Europe'; BE = 'Europe'
    IE = 'Europe'; CZ = 'Europe'; PT = 'Europe'
    CN = 'EastAsia-CNHK'; HK = 'EastAsia-CNHK'; MO = 'EastAsia-CNHK'; TW = 'EastAsia-CNHK'
    JP = 'EastAsia-JP'
    KR = 'EastAsia-KR'
    SG = 'SEAsia'; MY = 'SEAsia'; TH = 'SEAsia'; VN = 'SEAsia'; ID = 'SEAsia'; PH = 'SEAsia'
    AU = 'Oceania'; NZ = 'Oceania'
    BR = 'SouthAmerica'; AR = 'SouthAmerica'; CL = 'SouthAmerica'; PE = 'SouthAmerica'; CO = 'SouthAmerica'
    IN = 'SouthAsia'
    AE = 'MiddleEast'
}

# 已通过 ISteamDirectory/GetCMListForConnect 验证的各大区首选 CellID（未覆盖的大区再回退 cellid=0）。
$script:MacroRegionalCellIdMap = @{
    NorthAmerica    = '64'   # US - Los Angeles (lax1)
    Europe          = '103'  # fra1
    Oceania         = '51'   # syd1
    'EastAsia-CNHK' = '33'   # hkg1
    SouthAmerica    = '25'   # eze1/scl1/lim1
}

function Get-MacroRegionFromCountry
{
    param([string]$CountryCode)
    if (-not $CountryCode) { return $null }
    $key = $CountryCode.Trim().ToUpperInvariant()
    if ($script:CountryMacroMap.ContainsKey($key)) { return $script:CountryMacroMap[$key] }
    return $null
}

function Get-MacroRegionFromDc
{
    param([string]$DcCode)
    if (-not $DcCode) { return $null }
    $prefix = ($DcCode.Trim().ToLowerInvariant() -replace '\d+$', '')
    if ($script:SteamDcMacroMap.ContainsKey($prefix)) { return $script:SteamDcMacroMap[$prefix] }
    return $null
}

function Get-EffectiveProxyEndpoint
{
    foreach ($varName in 'HTTPS_PROXY', 'https_proxy', 'HTTP_PROXY', 'http_proxy', 'ALL_PROXY', 'all_proxy')
    {
        $val = [Environment]::GetEnvironmentVariable($varName)
        if (-not [string]::IsNullOrWhiteSpace($val))
        {
            $trimmed = $val.Trim()
            if ($trimmed -notmatch '^[a-zA-Z][a-zA-Z0-9+.-]*://') { $trimmed = "http://$trimmed" }
            if ($trimmed -match '^socks')
            {
                return [pscustomobject]@{ Mode = 'SocksProxy'; Uri = $null; Label = "环境代理 ($trimmed)" }
            }
            return [pscustomobject]@{ Mode = 'Proxy'; Uri = $trimmed; Label = "环境代理 ($trimmed)" }
        }
    }

    $regPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
    if (Test-Path -LiteralPath $regPath)
    {
        $props = Get-ItemProperty -LiteralPath $regPath -ErrorAction SilentlyContinue
        if ($props -and $props.ProxyEnable -eq 1 -and -not [string]::IsNullOrWhiteSpace($props.ProxyServer))
        {
            $raw = "$($props.ProxyServer)".Trim()
            $selected = $null
            if ($raw -match '=')
            {
                foreach ($scheme in 'https', 'http')
                {
                    $m = [regex]::Match($raw, "(?:^|;)\s*$scheme=([^;]+)", 'IgnoreCase')
                    if ($m.Success) { $selected = $m.Groups[1].Value.Trim(); break }
                }
                if (-not $selected)
                {
                    return [pscustomobject]@{ Mode = 'SocksProxy'; Uri = $null; Label = "系统代理 ($raw)" }
                }
            }
            else
            {
                $selected = $raw
            }
            if ($selected -notmatch '^[a-zA-Z][a-zA-Z0-9+.-]*://') { $selected = "http://$selected" }
            return [pscustomobject]@{ Mode = 'Proxy'; Uri = $selected; Label = "系统代理 ($selected)" }
        }
    }

    return [pscustomobject]@{ Mode = 'Direct'; Uri = $null; Label = '直连（未检测到代理）' }
}

function Invoke-RegionProbeRequest
{
    param(
        [Parameter(Mandatory)][string]$Uri,
        [pscustomobject]$ProxyEndpoint,
        [int]$TimeoutSeconds = 4
    )
    $params = @{ Uri = $Uri; TimeoutSec = $TimeoutSeconds; ErrorAction = 'Stop' }
    if ($ProxyEndpoint -and $ProxyEndpoint.Uri)
    {
        $params.Proxy = $ProxyEndpoint.Uri
    }
    elseif ($ProxyEndpoint -and $ProxyEndpoint.Mode -eq 'Direct' -and $PSVersionTable.PSVersion.Major -ge 7)
    {
        $params.NoProxy = $true
    }
    return Invoke-RestMethod @params
}

function Get-EgressRegionInfo
{
    param([pscustomobject]$ProxyEndpoint)

    try
    {
        $info = Invoke-RegionProbeRequest -Uri 'https://ipinfo.io/json' -ProxyEndpoint $ProxyEndpoint
        if ($info -and $info.country)
        {
            $country = "$($info.country)".Trim().ToUpperInvariant()
            $parts = @($country, "$($info.region)".Trim(), "$($info.city)".Trim()) | Where-Object { $_ }
            return [pscustomobject]@{
                Country     = $country
                Display     = ($parts -join ' / ')
                MacroRegion = (Get-MacroRegionFromCountry $country)
            }
        }
    }
    catch
    {
        Write-Warning "ipinfo.io 出口区域探测失败（$($_.Exception.Message)），尝试备用探测..."
    }

    try
    {
        $trace = Invoke-RegionProbeRequest -Uri 'https://www.cloudflare.com/cdn-cgi/trace' -ProxyEndpoint $ProxyEndpoint
        $locMatch = [regex]::Match("$trace", '(?m)^loc=([A-Za-z]{2})\s*$')
        if ($locMatch.Success)
        {
            $country = $locMatch.Groups[1].Value.ToUpperInvariant()
            $coloMatch = [regex]::Match("$trace", '(?m)^colo=([A-Za-z0-9]+)\s*$')
            $display = if ($coloMatch.Success) { "$country / CF-$($coloMatch.Groups[1].Value)" } else { $country }
            return [pscustomobject]@{
                Country     = $country
                Display     = $display
                MacroRegion = (Get-MacroRegionFromCountry $country)
            }
        }
    }
    catch
    {
        Write-Warning "Cloudflare 出口区域探测失败：$($_.Exception.Message)"
    }

    return $null
}

function Get-SteamDirectoryPrimaryDc
{
    param(
        [Parameter(Mandatory)][string]$CellId,
        [pscustomobject]$ProxyEndpoint
    )
    try
    {
        $url = "https://api.steampowered.com/ISteamDirectory/GetCMListForConnect/v1/?cellid=$CellId&qoslevel=3"
        $resp = Invoke-RegionProbeRequest -Uri $url -ProxyEndpoint $ProxyEndpoint
        $topList = @($resp.response.serverlist | Select-Object -First 6 | Where-Object { $_.dc })
        if ($topList.Count -eq 0) { return $null }

        $dominant = $topList | Group-Object -Property dc | Sort-Object Count -Descending | Select-Object -First 1
        $dc = "$($dominant.Name)".Trim().ToLowerInvariant()
        return [pscustomobject]@{
            Dc          = $dc
            MacroRegion = (Get-MacroRegionFromDc $dc)
            TopDcs      = (@($topList | ForEach-Object { $_.dc } | Select-Object -Unique) -join ', ')
        }
    }
    catch
    {
        Write-Warning "查询 Steam CM 节点列表（cellid=$CellId）失败：$($_.Exception.Message)"
        return $null
    }
}

function Find-VdfObjectBlock
{
    param([Parameter(Mandatory)][string]$Content, [Parameter(Mandatory)][string]$KeyName)

    $escapedKey = [regex]::Escape($KeyName)
    $match = [regex]::Match($Content, "(?m)^([ \t]*)`"$escapedKey`"[ \t]*\r?\n?[ \t]*\{")
    if (-not $match.Success) { return $null }

    $openBrace = $match.Index + $match.Length - 1
    $depth = 0
    $inString = $false
    $escaped = $false
    for ($i = $openBrace; $i -lt $Content.Length; $i++)
    {
        $ch = $Content[$i]
        if ($escaped) { $escaped = $false; continue }
        if ($inString)
        {
            if ($ch -eq '\') { $escaped = $true }
            elseif ($ch -eq '"') { $inString = $false }
            continue
        }
        if ($ch -eq '"') { $inString = $true; continue }
        if ($ch -eq '{') { $depth++ }
        elseif ($ch -eq '}')
        {
            $depth--
            if ($depth -eq 0)
            {
                return [pscustomobject]@{
                    Indent     = $match.Groups[1].Value
                    OpenBrace  = $openBrace
                    CloseBrace = $i
                    Body       = $Content.Substring($openBrace + 1, $i - $openBrace - 1)
                }
            }
        }
    }
    return $null
}

function Read-SteamCmdVdfState
{
    param([Parameter(Mandatory)][string]$Content)

    $getVal = {
        param([string]$Key)
        $m = [regex]::Match($Content, "(?m)^[ \t]*`"$([regex]::Escape($Key))`"[ \t]+`"([^`"]*)`"")
        if ($m.Success) { return $m.Groups[1].Value.Trim() }
        return $null
    }

    $currentCellId = & $getVal 'CurrentCellID'
    $timeSetRaw = & $getVal 'TimeCellIDSet'
    $overrideCellId = & $getVal 'CellIDServerOverride'

    # 对齐 SteamCMD (steamclient64.dll): TimeCellIDSet 缺失或超过 30 天 (2592000s) 时 CurrentCellID 视为失效。
    $validCurrentCellId = $null
    $timeSet = 0L
    if ($currentCellId -and $currentCellId -ne '-1' -and [long]::TryParse($timeSetRaw, [ref]$timeSet) -and $timeSet -gt 0)
    {
        $nowUnix = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        if (($nowUnix - $timeSet) -le 2592000L)
        {
            $validCurrentCellId = $currentCellId
        }
    }

    $effectiveCellId = if ($null -ne $validCurrentCellId) { $validCurrentCellId } else { $overrideCellId }

    $cachedDcs = @()
    $cmBlock = Find-VdfObjectBlock -Content $Content -KeyName 'CMWebSocket'
    if ($cmBlock)
    {
        $hostMatches = [regex]::Matches($cmBlock.Body, '"(?:cmp|ext)\d+-([a-z0-9]+)\.steamserver\.net(?::\d+)?"')
        $cachedDcs = @($hostMatches | Select-Object -First 6 | ForEach-Object { $_.Groups[1].Value.ToLowerInvariant() })
    }

    $cachedPrimaryDc = $null
    if ($cachedDcs.Count -gt 0)
    {
        $dom = $cachedDcs | Group-Object | Sort-Object Count -Descending | Select-Object -First 1
        $cachedPrimaryDc = "$($dom.Name)"
    }

    return [pscustomobject]@{
        CurrentCellId        = $currentCellId
        ValidCurrentCellId   = $validCurrentCellId
        CellIdServerOverride = $overrideCellId
        EffectiveCellId      = $effectiveCellId
        CachedPrimaryDc      = $cachedPrimaryDc
        CachedPrimaryMacro   = (Get-MacroRegionFromDc $cachedPrimaryDc)
        CachedTopDcs         = (@($cachedDcs | Select-Object -Unique) -join ', ')
    }
}

function Set-SteamCmdVdfValue
{
    param(
        [Parameter(Mandatory)][string]$Content,
        [Parameter(Mandatory)][string]$Key,
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$Newline
    )

    $keyRegex = [regex]"(?m)^([ \t]*`"$([regex]::Escape($Key))`"[ \t]+)`"[^`"]*`""
    if ($keyRegex.IsMatch($Content))
    {
        return $keyRegex.Replace($Content, { param($m) "$($m.Groups[1].Value)`"$Value`"" }, 1)
    }

    $anchorMatch = [regex]::Match($Content, '(?m)^([ \t]*)"(?:CellIDServerOverride|cip|Accounts)"[ \t]*')
    if (-not $anchorMatch.Success)
    {
        throw "无法在 SteamCMD config.vdf 中定位写入 $Key 的位置。"
    }
    $indent = $anchorMatch.Groups[1].Value
    return $Content.Insert($anchorMatch.Index, "$indent`"$Key`"`t`t`"$Value`"$Newline")
}

function Update-SteamCmdVdfRegion
{
    param(
        [Parameter(Mandatory)][string]$ConfigVdfPath,
        [Parameter(Mandatory)][string]$TargetCellId,
        [string]$BackupPath
    )
    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    $content = Get-Content -LiteralPath $ConfigVdfPath -Raw -Encoding utf8
    $newline = if ($content.Contains("`r`n")) { "`r`n" } else { "`n" }

    if ($BackupPath)
    {
        $backupDir = Split-Path -Parent $BackupPath
        if ($backupDir -and -not (Test-Path -LiteralPath $backupDir -PathType Container))
        {
            New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        }
        [IO.File]::WriteAllText($BackupPath, $content, $utf8NoBom)
    }

    # 1. 清空 CMWebSocket 缓存，迫使 SteamCMD 重新按目标 CellID 拉取节点列表。
    $cmBlock = Find-VdfObjectBlock -Content $content -KeyName 'CMWebSocket'
    if ($cmBlock)
    {
        $emptyBlock = "{$newline$($cmBlock.Indent)}"
        $content = $content.Substring(0, $cmBlock.OpenBrace) + $emptyBlock + $content.Substring($cmBlock.CloseBrace + 1)
    }

    # 2. 同步写入 CellIDServerOverride、CurrentCellID 与 TimeCellIDSet，避免登录回包将出口重置回旧区域。
    $unixNow = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
    $content = Set-SteamCmdVdfValue -Content $content -Key 'CellIDServerOverride' -Value $TargetCellId -Newline $newline
    $content = Set-SteamCmdVdfValue -Content $content -Key 'CurrentCellID' -Value $TargetCellId -Newline $newline
    $content = Set-SteamCmdVdfValue -Content $content -Key 'TimeCellIDSet' -Value $unixNow -Newline $newline

    [IO.File]::WriteAllText($ConfigVdfPath, $content, $utf8NoBom)
}

function Sync-SteamCmdRegion
{
    <#
    .SYNOPSIS
        核对代理出口区域与 SteamCMD 的有效 CellID / CMWebSocket 出口；不一致或未固定时更新 config.vdf。
    #>
    param(
        [Parameter(Mandatory)][string]$ConfigVdfPath,
        [string]$BackupDir,
        [switch]$Apply
    )

    $proxy = Get-EffectiveProxyEndpoint
    if ($proxy.Mode -eq 'SocksProxy')
    {
        Write-Warning "检测到 $($proxy.Label)（无 HTTP/HTTPS 代理端口），跳过 SteamCMD 出口区域自动探测。"
        return
    }

    $egress = Get-EgressRegionInfo -ProxyEndpoint $proxy
    if (-not $egress)
    {
        Write-Warning '未能识别当前网络出口区域，跳过 SteamCMD 出口核对。'
        return
    }

    $proxyRegionLabel = if ($egress.MacroRegion) { "$($egress.Display) [$($egress.MacroRegion)]" } else { $egress.Display }
    Write-Host "网络出口（$($proxy.Label)）：$proxyRegionLabel"

    if (-not (Test-Path -LiteralPath $ConfigVdfPath -PathType Leaf))
    {
        Write-Host "SteamCMD 出口：未找到 $ConfigVdfPath（首次登录前正常），跳过修改。"
        return
    }

    $rawVdf = Get-Content -LiteralPath $ConfigVdfPath -Raw -Encoding utf8
    $vdfState = Read-SteamCmdVdfState -Content $rawVdf
    $effectiveCellId = if ($null -ne $vdfState.EffectiveCellId) { $vdfState.EffectiveCellId } else { '0' }
    $probeCellId = if ($effectiveCellId -eq '') { '0' } else { $effectiveCellId }

    $currentDirInfo = Get-SteamDirectoryPrimaryDc -CellId $probeCellId -ProxyEndpoint $proxy
    $cmdDc = if ($currentDirInfo) { $currentDirInfo.Dc } else { $vdfState.CachedPrimaryDc }
    $cmdMacro = if ($currentDirInfo -and $currentDirInfo.MacroRegion) { $currentDirInfo.MacroRegion } else { $vdfState.CachedPrimaryMacro }
    $cellLabel = if ($null -ne $vdfState.ValidCurrentCellId)
    {
        "CurrentCellID=$($vdfState.ValidCurrentCellId)"
    }
    elseif ($null -ne $vdfState.CellIdServerOverride)
    {
        "CellIDServerOverride=$($vdfState.CellIdServerOverride)（未固定 CurrentCellID）"
    }
    else
    {
        '未设置(默认0)'
    }
    $dcDesc = if ($cmdDc) { "主节点 $cmdDc [$cmdMacro]" } else { '未知节点' }
    Write-Host "SteamCMD 出口配置：$cellLabel（$dcDesc）"
    if ($vdfState.CachedTopDcs)
    {
        Write-Host "SteamCMD CMWebSocket 缓存首部节点：$($vdfState.CachedTopDcs)"
    }

    if (-not $egress.MacroRegion)
    {
        Write-Warning "代理国家代码 $($egress.Country) 暂未映射大区，保留 SteamCMD 当前配置。"
        return
    }

    $preferredRegionalId = if ($script:MacroRegionalCellIdMap.ContainsKey($egress.MacroRegion)) { $script:MacroRegionalCellIdMap[$egress.MacroRegion] } else { $null }
    $cellMismatch = ($null -ne $cmdMacro) -and ($cmdMacro -ne $egress.MacroRegion)
    $cacheMismatch = ($null -ne $vdfState.CachedPrimaryMacro) -and ($vdfState.CachedPrimaryMacro -ne $egress.MacroRegion)
    $unpinned = $preferredRegionalId -and ((-not $vdfState.ValidCurrentCellId) -or ($vdfState.ValidCurrentCellId -eq '0'))
    if (-not $cellMismatch -and -not $cacheMismatch -and -not $unpinned)
    {
        Write-Host 'SteamCMD 出口与当前代理节点区域一致。'
        return
    }

    # 优先使用已验证的大区 CellID 并通过 CurrentCellID + TimeCellIDSet 固定，未覆盖大区再回退 "0"。
    $targetCellId = $null
    $targetDcDesc = $null
    if ($preferredRegionalId)
    {
        $regInfo = Get-SteamDirectoryPrimaryDc -CellId $preferredRegionalId -ProxyEndpoint $proxy
        if ($regInfo -and $regInfo.MacroRegion -eq $egress.MacroRegion)
        {
            $targetCellId = $preferredRegionalId
            $targetDcDesc = "$($regInfo.Dc) [$($regInfo.MacroRegion)]"
        }
    }
    if (-not $targetCellId)
    {
        $zeroInfo = Get-SteamDirectoryPrimaryDc -CellId '0' -ProxyEndpoint $proxy
        if ($zeroInfo -and $zeroInfo.MacroRegion -eq $egress.MacroRegion)
        {
            $targetCellId = '0'
            $targetDcDesc = "$($zeroInfo.Dc) [$($zeroInfo.MacroRegion)]"
        }
        elseif ($preferredRegionalId)
        {
            $targetCellId = $preferredRegionalId
            $targetDcDesc = $egress.MacroRegion
        }
    }

    if (-not $targetCellId)
    {
        Write-Warning "检测到 SteamCMD 出口 [$cmdMacro] 与代理区域 [$($egress.MacroRegion)] 不一致，但未能验证目标 CellID，保留原配置。"
        return
    }

    if (-not $Apply)
    {
        Write-Warning "SteamCMD 出口需要更新（当前 $dcDesc，代理 [$($egress.MacroRegion)]）；实际发布前会自动设为 CurrentCellID/CellIDServerOverride=`"$targetCellId`"（$targetDcDesc）并清理 CMWebSocket 缓存。"
        return
    }

    $backupPath = if ($BackupDir) { Join-Path $BackupDir 'steamcmd_config.vdf.last.bak' } else { $null }
    Update-SteamCmdVdfRegion -ConfigVdfPath $ConfigVdfPath -TargetCellId $targetCellId -BackupPath $backupPath
    Write-Host "已自动调整 SteamCMD 出口：CurrentCellID / CellIDServerOverride -> `"$targetCellId`"（$targetDcDesc），并清理 CMWebSocket 缓存。"
}
