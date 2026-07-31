[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('preview', 'build', 'status', 'list', 'add', 'remove', 'undo', 'restore', 'recover', 'help')]
    [string]$Command = 'help',

    [string]$InputText,
    [string]$File,
    [string]$OutputPath,
    [ValidateRange(1, 999999999)]
    [int]$Version = 1,
    [ValidateSet('Default', 'IE8Enterprise', 'IE7Enterprise')]
    [string]$CompatMode = 'Default',
    [switch]$AllowRedirect,
    [switch]$Apply,
    [string]$DataRoot = 'C:\ProgramData\CompatBridge'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot 'CompatBridge.psd1') -Force

function Read-CompatBridgeCliInput {
    if (-not [string]::IsNullOrWhiteSpace($InputText)) {
        return @($InputText)
    }
    if (-not [string]::IsNullOrWhiteSpace($File)) {
        if (-not (Test-Path -LiteralPath $File -PathType Leaf)) {
            throw "找不到输入文件：$File"
        }
        $extension = [System.IO.Path]::GetExtension($File)
        if ($extension -ieq '.csv') {
            $lines = @(Get-Content -LiteralPath $File -Encoding UTF8 | Where-Object {
                -not [string]::IsNullOrWhiteSpace($_)
            })
            if ($lines.Count -eq 0) {
                return @()
            }
            if ($lines[0].Trim().Trim('"') -match '^(?i:url|site|domain|address|站点|网址)$') {
                $lines = @($lines | Select-Object -Skip 1)
            }
            if ($lines.Count -eq 0) {
                return @()
            }
            $rows = @($lines | ConvertFrom-Csv -Header 'Value')
            return @($rows | ForEach-Object { [string]$_.Value })
        }
        return @(Get-Content -LiteralPath $File -Encoding UTF8)
    }
    throw '请使用 -InputText 或 -File 提供站点。'
}

switch ($Command) {
    'preview' {
        $values = Read-CompatBridgeCliInput
        $preview = @(Get-CompatBridgeInputPreview -InputObject $values)
        $preview | Select-Object Raw, Url, Classification,
            @{ Name = 'Warnings'; Expression = { $_.Warnings -join '；' } },
            Error | Format-Table -AutoSize -Wrap

        $ready = @($preview | Where-Object Classification -eq 'Ready').Count
        $invalid = @($preview | Where-Object Classification -eq 'Invalid').Count
        $duplicates = $preview.Count - $ready - $invalid
        Write-Host "可添加：$ready；非法：$invalid；重复/已存在：$duplicates"
    }
    'build' {
        if ([string]::IsNullOrWhiteSpace($OutputPath)) {
            throw 'build 命令需要 -OutputPath。'
        }
        $values = Read-CompatBridgeCliInput
        $preview = @(Get-CompatBridgeInputPreview -InputObject $values)
        $invalid = @($preview | Where-Object Classification -eq 'Invalid')
        if ($invalid.Count -gt 0) {
            $invalid | Select-Object Raw, Error | Format-Table -AutoSize -Wrap
            throw '存在非法输入，未生成 XML。请先修正或移除。'
        }
        $sites = @(
            $preview |
                Where-Object Classification -eq 'Ready' |
                ForEach-Object {
                    [pscustomobject]@{
                        Url = $_.Url
                        CompatMode = $CompatMode
                        AllowRedirect = $AllowRedirect.IsPresent
                    }
                }
        )
        if ($sites.Count -eq 0) {
            throw '没有可写入的站点。'
        }
        $result = Export-CompatBridgeSiteList -Path $OutputPath -Version $Version -Sites $sites -Confirm:$false
        Write-Host "已生成：$($result.FullName)"
        Write-Host "版本：$Version；站点数：$($sites.Count)"
    }
    'status' {
        $status = Get-CompatBridgeEnvironmentStatus -DataRoot $DataRoot
        [pscustomobject]@{
            Windows = $status.IsWindows
            WindowsVersion = $status.WindowsVersion
            Administrator = $status.IsAdministrator
            Supported = $status.IsSupported
            EdgeInstalled = $status.Edge.Installed
            EdgeScope = $status.Edge.Scope
            EdgeVersion = $status.Edge.Version
            DataRoot = $status.DataRoot
            BlockingConflict = $status.HasBlockingConflict
        } | Format-List
        if ($status.Conflicts.Count -gt 0) {
            Write-Host '冲突：'
            $status.Conflicts | ForEach-Object { Write-Host "  - $_" }
        }
        if ($status.SupportIssues.Count -gt 0) {
            Write-Host '环境问题：'
            $status.SupportIssues | ForEach-Object { Write-Host "  - $_" }
        }
    }
    'list' {
        $sites = @(Get-CompatBridgeSites -DataRoot $DataRoot)
        $sites | Format-Table Url, CompatMode, AllowRedirect -AutoSize
        Write-Host "站点数：$($sites.Count)"
    }
    'add' {
        $values = Read-CompatBridgeCliInput
        $existing = @(Get-CompatBridgeSites -DataRoot $DataRoot)
        $preview = @(Get-CompatBridgeInputPreview -InputObject $values `
            -ExistingSite $existing -CompatMode $CompatMode -AllowRedirect:$AllowRedirect)
        $preview | Select-Object Raw, Url, Classification,
            @{ Name = 'Warnings'; Expression = { $_.Warnings -join '；' } },
            Error | Format-Table -AutoSize -Wrap
        if (-not $Apply) {
            Write-Host '当前仅预览。确认无误后增加 -Apply 执行策略修改。'
            break
        }
        $result = Add-CompatBridgeSites -InputObject $values -CompatMode $CompatMode `
            -AllowRedirect:$AllowRedirect -DataRoot $DataRoot -Confirm:$false
        $result | Format-List
        if ($result.RequiresEdgeRestart) {
            Write-Host '策略已变更。请在确认工作已保存后手动重启 Edge。'
        }
    }
    'remove' {
        $values = Read-CompatBridgeCliInput
        $existing = @(Get-CompatBridgeSites -DataRoot $DataRoot)
        $preview = @(Get-CompatBridgeInputPreview -InputObject $values)
        $existingKeys = @{}
        foreach ($site in $existing) {
            $existingKeys[$site.Url.ToLowerInvariant()] = $true
        }
        $preview |
            Select-Object Raw, Url,
                @{ Name = 'Match'; Expression = {
                    if ($_.Classification -eq 'Invalid') { 'Invalid' }
                    elseif ($existingKeys.ContainsKey($_.Url.ToLowerInvariant())) { 'Matched' }
                    else { 'NotFound' }
                }},
                Error |
            Format-Table -AutoSize -Wrap
        if (-not $Apply) {
            Write-Host '当前仅预览。确认无误后增加 -Apply 执行策略修改。'
            break
        }
        $result = Remove-CompatBridgeSites -InputObject $values -DataRoot $DataRoot -Confirm:$false
        $result | Format-List
        if ($result.RequiresEdgeRestart) {
            Write-Host '策略已变更。请在确认工作已保存后手动重启 Edge。'
        }
    }
    'undo' {
        if (-not $Apply) {
            Write-Host '撤销会修改 XML 和注册表。增加 -Apply 后执行。'
            break
        }
        Undo-CompatBridgeLastChange -DataRoot $DataRoot -Confirm:$false | Format-List
        Write-Host '已撤销。请在确认工作已保存后手动重启 Edge。'
    }
    'restore' {
        if (-not $Apply) {
            Write-Host '恢复会还原 CompatBridge 运行前的 XML 和注册表值。增加 -Apply 后执行。'
            break
        }
        Restore-CompatBridgeBaseline -DataRoot $DataRoot -Confirm:$false | Format-List
        Write-Host '已恢复初始状态。请在确认工作已保存后手动重启 Edge。'
    }
    'recover' {
        if (-not $Apply) {
            Write-Host '恢复中断事务会还原 XML 和注册表。增加 -Apply 后执行。'
            break
        }
        Repair-CompatBridgeInterruptedTransaction -DataRoot $DataRoot -Confirm:$false | Format-List
        Write-Host '中断事务已恢复。请在确认工作已保存后手动重启 Edge。'
    }
    default {
        @'
CompatBridge PowerShell 原型

用法：
  CompatBridge.ps1 preview -InputText <多行站点>
  CompatBridge.ps1 preview -File <sites.txt|sites.csv>
  CompatBridge.ps1 build -File <sites.txt|sites.csv> -OutputPath <sites.xml> [-Version 1]
  CompatBridge.ps1 status [-DataRoot C:\ProgramData\CompatBridge]
  CompatBridge.ps1 list [-DataRoot C:\ProgramData\CompatBridge]
  CompatBridge.ps1 add -File <sites.txt|sites.csv> [-Apply]
  CompatBridge.ps1 remove -File <sites.txt|sites.csv> [-Apply]
  CompatBridge.ps1 undo -Apply
  CompatBridge.ps1 restore -Apply
  CompatBridge.ps1 recover -Apply

add/remove 不带 -Apply 时只预览；build 永远只生成 XML。
'@ | Write-Host
    }
}
