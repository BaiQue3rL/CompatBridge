[CmdletBinding()]
param()

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $repositoryRoot 'src\CompatBridge.PowerShell\CompatBridge.psd1') -Force

$script:Passed = 0
$script:Failed = 0

function Assert-Equal {
    param($Expected, $Actual, [string]$Message)
    if ($Expected -ne $Actual) {
        throw "$Message。期望：[$Expected]；实际：[$Actual]"
    }
}

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) {
        throw $Message
    }
}

function Test-Case {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        $script:Passed++
        Write-Host "[PASS] $Name" -ForegroundColor Green
    }
    catch {
        $script:Failed++
        Write-Host "[FAIL] $Name" -ForegroundColor Red
        Write-Host "       $($_.Exception.Message)" -ForegroundColor Red
    }
}

Test-Case '规范化裸域名' {
    $entry = ConvertTo-CompatBridgeSiteEntry 'OA.Example.COM'
    Assert-True $entry.IsValid '条目应有效'
    Assert-Equal 'oa.example.com' $entry.Url '域名应转小写'
}

Test-Case '保留显式端口和路径' {
    $entry = ConvertTo-CompatBridgeSiteEntry 'https://ERP.example.com:8443/legacy/login'
    Assert-Equal 'erp.example.com:8443/legacy/login' $entry.Url '规范化 URL 不正确'
    Assert-Equal 8443 $entry.Port '端口不正确'
}

Test-Case '移除查询字符串和片段并告警' {
    $entry = ConvertTo-CompatBridgeSiteEntry 'https://example.com/app?q=1#top'
    Assert-Equal 'example.com/app' $entry.Url '不应写入查询或片段'
    Assert-Equal 2 $entry.Warnings.Count '应返回两个警告'
}

Test-Case '接受 IPv4' {
    $entry = ConvertTo-CompatBridgeSiteEntry '192.168.10.20:8080/oa'
    Assert-True $entry.IsValid 'IPv4 条目应有效'
    Assert-Equal '192.168.10.20:8080/oa' $entry.Url 'IPv4 规范化错误'
}

Test-Case '拒绝看似 IPv4 的非法数字地址' {
    $entry = ConvertTo-CompatBridgeSiteEntry '999.999.999.999'
    Assert-True (-not $entry.IsValid) '非法数字地址不得当作 DNS 名接受'
}

Test-Case '拒绝通配符' {
    $entry = ConvertTo-CompatBridgeSiteEntry '*.example.com'
    Assert-True (-not $entry.IsValid) '通配符必须无效'
}

Test-Case '拒绝 FTP' {
    $entry = ConvertTo-CompatBridgeSiteEntry 'ftp://example.com'
    Assert-True (-not $entry.IsValid) 'FTP 必须无效'
}

Test-Case '拒绝凭据' {
    $entry = ConvertTo-CompatBridgeSiteEntry 'https://user:pass@example.com/app'
    Assert-True (-not $entry.IsValid) '带凭据 URL 必须无效'
}

Test-Case '识别批次重复和已有条目' {
    $preview = @(Get-CompatBridgeInputPreview -InputObject @(
        "a.example.com`na.example.com",
        "b.example.com`tC.example.com"
    ) -ExistingUrl @('b.example.com'))
    Assert-Equal 4 $preview.Count '预览数量不正确'
    Assert-Equal 'Ready' $preview[0].Classification '第一项应可添加'
    Assert-Equal 'DuplicateInput' $preview[1].Classification '第二项应为输入重复'
    Assert-Equal 'AlreadyExists' $preview[2].Classification '第三项应已存在'
    Assert-Equal 'Ready' $preview[3].Classification '第四项应可添加'
}

Test-Case '识别同一站点的兼容设置冲突' {
    $existing = @(
        [pscustomobject]@{
            Url = 'conflict.example.com'
            CompatMode = 'IE8Enterprise'
            AllowRedirect = $false
        }
    )
    $preview = @(Get-CompatBridgeInputPreview -InputObject @('conflict.example.com') `
        -ExistingSite $existing -CompatMode Default)
    Assert-Equal 'ConflictSettings' $preview[0].Classification '应识别设置冲突'
}

Test-Case 'XML 默认使用 IE11 和 Default' {
    $xmlText = New-CompatBridgeSiteListDocument -Version 7 -Sites @(
        [pscustomobject]@{ Url = 'oa.example.com' }
    ) -CreatedAt ([datetime]'2026-07-30T12:34:56')
    [xml]$xml = $xmlText
    Assert-Equal '7' $xml.'site-list'.version '版本不正确'
    Assert-Equal 'Default' $xml.'site-list'.site.'compat-mode' '默认兼容模式不正确'
    Assert-Equal 'IE11' $xml.SelectSingleNode('/site-list/site/open-in').InnerText '默认打开方式不正确'
    Assert-Equal '20260730.123456' $xml.'site-list'.'created-by'.'date-created' '创建时间不正确'
}

Test-Case 'XML 正确转义属性' {
    $xmlText = New-CompatBridgeSiteListDocument -Version 1 -Sites @(
        [pscustomobject]@{ Url = 'example.com/a&b'; CompatMode = 'Default'; AllowRedirect = $true }
    )
    Assert-True ($xmlText.Contains('a&amp;b')) 'XML 属性必须转义'
    [xml]$xml = $xmlText
    Assert-Equal 'true' $xml.'site-list'.site.'open-in'.'allow-redirect' '重定向属性不正确'
}

Test-Case '原子导出后可以重新导入' {
    $testDirectory = Join-Path $repositoryRoot '.tmp\tests'
    if (-not (Test-Path -LiteralPath $testDirectory)) {
        [void](New-Item -ItemType Directory -Path $testDirectory)
    }
    $path = Join-Path $testDirectory 'sites.xml'
    $sites = @(
        [pscustomobject]@{ Url = 'oa.example.com'; CompatMode = 'Default'; AllowRedirect = $false },
        [pscustomobject]@{ Url = 'erp.example.com:8443/legacy'; CompatMode = 'IE8Enterprise'; AllowRedirect = $true }
    )
    [void](Export-CompatBridgeSiteList -Path $path -Version 9 -Sites $sites -Confirm:$false)
    $loaded = Import-CompatBridgeSiteList -Path $path
    Assert-Equal 9 $loaded.Version '导入版本不正确'
    Assert-Equal 2 $loaded.Sites.Count '导入站点数不正确'
    Assert-Equal 'IE8Enterprise' $loaded.Sites[1].CompatMode '高级兼容模式丢失'
}

Test-Case '只读环境检查返回结构化结果' {
    $status = Get-CompatBridgeEnvironmentStatus -DataRoot (Join-Path $repositoryRoot '.tmp\runtime')
    Assert-True ($null -ne $status.Edge) '应返回 Edge 状态'
    Assert-True ($null -ne $status.Policy.HKLM.SiteList) '应返回 HKLM 策略快照'
}

Test-Case '策略变更 WhatIf 不创建运行目录' {
    $whatIfRoot = Join-Path $repositoryRoot '.tmp\whatif-runtime'
    if (Test-Path -LiteralPath $whatIfRoot) {
        Remove-Item -LiteralPath $whatIfRoot -Recurse -Force
    }
    Add-CompatBridgeSites -InputObject @('whatif.example.com') -DataRoot $whatIfRoot -WhatIf
    Assert-True (-not (Test-Path -LiteralPath $whatIfRoot)) 'WhatIf 不得创建文件或目录'
}

Test-Case '空列表可以生成有效 v2 XML' {
    $xmlText = New-CompatBridgeSiteListDocument -Version 10 -Sites @()
    [xml]$xml = $xmlText
    Assert-Equal 'site-list' $xml.DocumentElement.Name '根元素不正确'
    Assert-Equal '10' $xml.DocumentElement.GetAttribute('version') '空列表版本不正确'
}

Test-Case '内部原子复制会完整替换目标文件' {
    $testDirectory = Join-Path $repositoryRoot '.tmp\atomic-copy'
    if (-not (Test-Path -LiteralPath $testDirectory)) {
        [void](New-Item -ItemType Directory -Path $testDirectory)
    }
    $source = Join-Path $testDirectory 'source.txt'
    $destination = Join-Path $testDirectory 'destination.txt'
    [System.IO.File]::WriteAllText($source, 'new-content')
    [System.IO.File]::WriteAllText($destination, 'old-content')
    & (Get-Module CompatBridge) {
        param($SourcePath, $DestinationPath)
        Copy-CompatBridgeFileAtomic -Source $SourcePath -Destination $DestinationPath
    } $source $destination
    Assert-Equal 'new-content' ([System.IO.File]::ReadAllText($destination)) '目标文件未被完整替换'
}

Test-Case '本地策略路径使用官方 file URI 格式' {
    $xmlPath = Join-Path $repositoryRoot '.tmp\runtime\sites.xml'
    $policyValue = & (Get-Module CompatBridge) {
        param($Path)
        Get-CompatBridgeSiteListPolicyValue -XmlPath $Path
    } $xmlPath
    Assert-True ($policyValue.StartsWith('file:///')) '本地策略值必须是 file URI'
    Assert-True ($policyValue.EndsWith('/sites.xml')) '策略 URI 应指向 sites.xml'
}

Test-Case '备份引用不能越出数据目录' {
    $runtimeRoot = Join-Path $repositoryRoot '.tmp\runtime-boundary'
    $outside = Join-Path $repositoryRoot '.tmp\outside-manifest.json'
    [System.IO.File]::WriteAllText($outside, '{}')
    $rejected = $false
    try {
        & (Get-Module CompatBridge) {
            param($Root, $Manifest)
            $paths = Get-CompatBridgeRuntimePaths -DataRoot $Root
            Assert-CompatBridgeBackupPath -Paths $paths -ManifestPath $Manifest
        } $runtimeRoot $outside
    }
    catch {
        $rejected = $true
    }
    Assert-True $rejected '必须拒绝数据目录外的备份清单'
}

Test-Case '状态检查会识别中断事务' {
    $interruptedRoot = Join-Path $repositoryRoot '.tmp\interrupted-status'
    if (-not (Test-Path -LiteralPath $interruptedRoot)) {
        [void](New-Item -ItemType Directory -Path $interruptedRoot)
    }
    $statePath = Join-Path $interruptedRoot 'state.json'
    [System.IO.File]::WriteAllText($statePath, '{"Phase":"Applying"}')
    $status = Get-CompatBridgeEnvironmentStatus -DataRoot $interruptedRoot
    Assert-True $status.HasBlockingConflict '中断事务必须成为阻止性冲突'
    Assert-True (($status.Conflicts -join '') -match '中断') '冲突说明应指出中断事务'
}

Test-Case '中断恢复 WhatIf 不创建运行目录' {
    $recoverWhatIfRoot = Join-Path $repositoryRoot '.tmp\recover-whatif'
    if (Test-Path -LiteralPath $recoverWhatIfRoot) {
        Remove-Item -LiteralPath $recoverWhatIfRoot -Recurse -Force
    }
    Repair-CompatBridgeInterruptedTransaction -DataRoot $recoverWhatIfRoot -WhatIf
    Assert-True (-not (Test-Path -LiteralPath $recoverWhatIfRoot)) '恢复 WhatIf 不得创建文件或目录'
}

Write-Host ''
Write-Host "通过：$script:Passed；失败：$script:Failed"
if ($script:Failed -gt 0) {
    exit 1
}
