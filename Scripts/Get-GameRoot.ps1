<#
.SYNOPSIS
    读取仓库 .env 中的本机配置，解析 Probably Stolen 游戏目录。
    Get-ProbablyStolenSetting 传 -Optional 时，缺少 .env 或该键返回 $null 而不报错。
.NOTES
    Author: SaintCirno9
#>
function Get-ProbablyStolenSetting
{
    param([string]$Name, [string]$Value, [switch]$Optional)
    if (-not [string]::IsNullOrWhiteSpace($Value)) { return $Value }
    $envPath = Join-Path (Split-Path -Parent $PSScriptRoot) '.env'
    if (-not (Test-Path -LiteralPath $envPath -PathType Leaf))
    {
        if ($Optional) { return $null }
        throw "找不到 .env。请复制 .env.example 并填写 $Name。"
    }
    $settings = @{}
    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $envPath -Encoding utf8)
    {
        $lineNumber++
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#')) { continue }
        if ($line -notmatch '^\s*([A-Z_][A-Z0-9_]*)\s*=\s*(.*?)\s*$')
        {
            throw ".env 第 $lineNumber 行格式错误，应使用 KEY=VALUE。"
        }
        $key = $Matches[1]
        $settingValue = $Matches[2]
        if ($settings.ContainsKey($key)) { throw ".env 第 $lineNumber 行重复配置：$key。" }
        if ($settingValue.StartsWith('"') -or $settingValue.StartsWith("'"))
        {
            if ($settingValue.Length -lt 2 -or $settingValue[-1] -ne $settingValue[0])
            {
                throw ".env 第 $lineNumber 行引号不匹配：$key。"
            }
            $settingValue = $settingValue.Substring(1, $settingValue.Length - 2)
        }
        $settings[$key] = $settingValue
    }
    if ([string]::IsNullOrWhiteSpace($settings[$Name]))
    {
        if ($Optional) { return $null }
        throw ".env 中未配置 $Name。"
    }
    return $settings[$Name]
}

function Get-ProbablyStolenGameRoot
{
    param([string]$GameRootDir)
    $value = Get-ProbablyStolenSetting -Name 'PROBABLY_STOLEN_GAME_ROOT' -Value $GameRootDir
    $gameRoot = [IO.Path]::GetFullPath($value)
    if (-not (Test-Path -LiteralPath (Join-Path $gameRoot 'Probably Stolen.exe') -PathType Leaf))
    {
        throw "找不到游戏可执行文件：$gameRoot"
    }
    return $gameRoot
}
