Set-StrictMode -Version 2.0

$script:ModuleVersion = '0.1.0'
$script:EdgePolicySubKey = 'SOFTWARE\Policies\Microsoft\Edge'
$script:LegacyIePolicySubKey = 'SOFTWARE\Policies\Microsoft\Internet Explorer\Main\EnterpriseMode'

function New-CompatBridgeInvalidEntry {
    param(
        [Parameter(Mandatory = $true)][string]$Raw,
        [Parameter(Mandatory = $true)][string]$ErrorMessage
    )

    [pscustomobject]@{
        Raw = $Raw
        IsValid = $false
        Url = $null
        Host = $null
        Port = $null
        Path = $null
        Warnings = @()
        Error = $ErrorMessage
    }
}

function ConvertTo-CompatBridgeAsciiHost {
    param([Parameter(Mandatory = $true)][string]$HostName)

    $trimmedHost = $HostName.Trim().TrimEnd('.').ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($trimmedHost)) {
        throw '主机名为空。'
    }

    $ipAddress = $null
    if ([System.Net.IPAddress]::TryParse($trimmedHost, [ref]$ipAddress)) {
        if ($ipAddress.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetworkV6) {
            throw '当前原型暂不接受 IPv6；需先在真实 Edge Site List 中验证格式。'
        }
        if ($ipAddress.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
            throw '不支持的 IP 地址类型。'
        }
        return $ipAddress.ToString()
    }
    if ($trimmedHost -match '^[0-9.]+$') {
        throw 'IPv4 地址格式无效。'
    }

    try {
        $idn = New-Object System.Globalization.IdnMapping
        $asciiHost = $idn.GetAscii($trimmedHost).ToLowerInvariant()
    }
    catch {
        throw '主机名包含无法转换的国际化字符。'
    }

    if ($asciiHost.Length -gt 253) {
        throw '主机名超过 253 个字符。'
    }

    foreach ($label in $asciiHost.Split('.')) {
        if ($label.Length -lt 1 -or $label.Length -gt 63) {
            throw '主机名标签长度必须为 1 到 63 个字符。'
        }
        if ($label -notmatch '^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$') {
            throw '主机名只能包含字母、数字和标签内部的连字符。'
        }
    }

    return $asciiHost
}

function ConvertTo-CompatBridgeSiteEntry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [AllowEmptyString()]
        [string]$InputObject
    )

    process {
        $raw = $InputObject
        $value = $raw.Trim()
        if ([string]::IsNullOrWhiteSpace($value)) {
            return New-CompatBridgeInvalidEntry -Raw $raw -ErrorMessage '输入为空。'
        }

        if ($value.Contains('*')) {
            return New-CompatBridgeInvalidEntry -Raw $raw -ErrorMessage 'Enterprise Mode Site List 不接受通配符。'
        }

        if ($value -match '^[a-zA-Z][a-zA-Z0-9+.-]*://' -and
            $value -notmatch '^(?i:https?)://') {
            return New-CompatBridgeInvalidEntry -Raw $raw -ErrorMessage '仅接受 HTTP 或 HTTPS URL。'
        }

        $candidate = $value
        if ($candidate -notmatch '^[a-zA-Z][a-zA-Z0-9+.-]*://') {
            $candidate = 'http://' + $candidate
        }

        $uri = $null
        if (-not [System.Uri]::TryCreate($candidate, [System.UriKind]::Absolute, [ref]$uri)) {
            return New-CompatBridgeInvalidEntry -Raw $raw -ErrorMessage '无法解析为有效的域名、IP 或 URL。'
        }

        if ($uri.Scheme -notin @('http', 'https')) {
            return New-CompatBridgeInvalidEntry -Raw $raw -ErrorMessage '仅接受 HTTP 或 HTTPS URL。'
        }
        if (-not [string]::IsNullOrEmpty($uri.UserInfo)) {
            return New-CompatBridgeInvalidEntry -Raw $raw -ErrorMessage 'URL 不得包含用户名或密码。'
        }

        $withoutScheme = $candidate -replace '^[a-zA-Z][a-zA-Z0-9+.-]*://', ''
        $authority = ($withoutScheme -split '[/#?]', 2)[0]
        if ([string]::IsNullOrWhiteSpace($authority)) {
            return New-CompatBridgeInvalidEntry -Raw $raw -ErrorMessage '缺少主机名。'
        }
        if ($authority.StartsWith('[')) {
            return New-CompatBridgeInvalidEntry -Raw $raw -ErrorMessage '当前原型暂不接受 IPv6；需先在真实 Edge Site List 中验证格式。'
        }

        $authorityMatch = [regex]::Match($authority, '^(?<host>[^:]+)(?::(?<port>[0-9]+))?$')
        if (-not $authorityMatch.Success) {
            return New-CompatBridgeInvalidEntry -Raw $raw -ErrorMessage '主机或端口格式无效。'
        }

        $explicitPort = $null
        if ($authorityMatch.Groups['port'].Success) {
            $portNumber = 0
            if (-not [int]::TryParse($authorityMatch.Groups['port'].Value, [ref]$portNumber) -or
                $portNumber -lt 1 -or $portNumber -gt 65535) {
                return New-CompatBridgeInvalidEntry -Raw $raw -ErrorMessage '端口必须介于 1 和 65535 之间。'
            }
            $explicitPort = $portNumber
        }

        try {
            $hostName = ConvertTo-CompatBridgeAsciiHost -HostName $authorityMatch.Groups['host'].Value
        }
        catch {
            return New-CompatBridgeInvalidEntry -Raw $raw -ErrorMessage $_.Exception.Message
        }

        $warnings = New-Object System.Collections.Generic.List[string]
        if (-not [string]::IsNullOrEmpty($uri.Query)) {
            $warnings.Add('已移除查询字符串。')
        }
        if (-not [string]::IsNullOrEmpty($uri.Fragment)) {
            $warnings.Add('已移除 URL 片段。')
        }

        $escapedPath = $uri.GetComponents([System.UriComponents]::Path, [System.UriFormat]::UriEscaped)
        $normalizedPath = ''
        if (-not [string]::IsNullOrEmpty($escapedPath)) {
            $normalizedPath = '/' + $escapedPath.TrimStart('/')
            if ($normalizedPath -eq '/') {
                $normalizedPath = ''
            }
        }

        $siteUrl = $hostName
        if ($null -ne $explicitPort) {
            $siteUrl += ':' + $explicitPort
        }
        $siteUrl += $normalizedPath

        [pscustomobject]@{
            Raw = $raw
            IsValid = $true
            Url = $siteUrl
            Host = $hostName
            Port = $explicitPort
            Path = $normalizedPath
            Warnings = @($warnings)
            Error = $null
        }
    }
}

function Split-CompatBridgeInput {
    param([Parameter(Mandatory = $true)][string[]]$InputObject)

    foreach ($item in $InputObject) {
        foreach ($line in ($item -split '\r?\n')) {
            foreach ($cell in ($line -split "`t")) {
                $trimmed = $cell.Trim()
                if (-not [string]::IsNullOrWhiteSpace($trimmed)) {
                    $trimmed
                }
            }
        }
    }
}

function Get-CompatBridgeInputPreview {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, ValueFromPipeline = $true)]
        [string[]]$InputObject,

        [AllowEmptyCollection()]
        [string[]]$ExistingUrl = @(),

        [AllowEmptyCollection()]
        [object[]]$ExistingSite = @(),

        [ValidateSet('Default', 'IE8Enterprise', 'IE7Enterprise')]
        [string]$CompatMode = 'Default',

        [switch]$AllowRedirect
    )

    begin {
        $buffer = New-Object System.Collections.Generic.List[string]
    }
    process {
        foreach ($item in $InputObject) {
            $buffer.Add($item)
        }
    }
    end {
        $seen = @{}
        $existing = @{}
        foreach ($url in $ExistingUrl) {
            if (-not [string]::IsNullOrWhiteSpace($url)) {
                $existing[$url.ToLowerInvariant()] = $null
            }
        }
        foreach ($site in $ExistingSite) {
            if ($site.PSObject.Properties['Url'] -and
                -not [string]::IsNullOrWhiteSpace([string]$site.Url)) {
                $existing[[string]$site.Url.ToLowerInvariant()] = $site
            }
        }

        foreach ($value in (Split-CompatBridgeInput -InputObject $buffer.ToArray())) {
            $entry = ConvertTo-CompatBridgeSiteEntry -InputObject $value
            $classification = 'Invalid'
            if ($entry.IsValid) {
                $key = $entry.Url.ToLowerInvariant()
                if ($seen.ContainsKey($key)) {
                    $classification = 'DuplicateInput'
                }
                elseif ($existing.ContainsKey($key)) {
                    $existingEntry = $existing[$key]
                    $classification = 'AlreadyExists'
                    if ($null -ne $existingEntry) {
                        $existingCompatMode = 'Default'
                        if ($existingEntry.PSObject.Properties['CompatMode']) {
                            $existingCompatMode = [string]$existingEntry.CompatMode
                        }
                        $existingAllowRedirect = $false
                        if ($existingEntry.PSObject.Properties['AllowRedirect']) {
                            $existingAllowRedirect = [bool]$existingEntry.AllowRedirect
                        }
                        if ($existingCompatMode -ne $CompatMode -or
                            $existingAllowRedirect -ne $AllowRedirect.IsPresent) {
                            $classification = 'ConflictSettings'
                        }
                    }
                    $seen[$key] = $true
                }
                else {
                    $classification = 'Ready'
                    $seen[$key] = $true
                }
            }

            [pscustomobject]@{
                Raw = $entry.Raw
                Url = $entry.Url
                Classification = $classification
                Warnings = $entry.Warnings
                Error = $entry.Error
            }
        }
    }
}

function New-CompatBridgeSiteListDocument {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateRange(1, 999999999)]
        [int]$Version,

        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Sites,

        [datetime]$CreatedAt = (Get-Date)
    )

    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.IndentChars = '  '
    $settings.NewLineChars = "`r`n"
    $settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $settings.OmitXmlDeclaration = $false

    $stream = New-Object System.IO.MemoryStream
    $writer = [System.Xml.XmlWriter]::Create($stream, $settings)
    try {
        $writer.WriteStartDocument()
        $writer.WriteStartElement('site-list')
        $writer.WriteAttributeString('version', $Version.ToString([System.Globalization.CultureInfo]::InvariantCulture))

        $writer.WriteStartElement('created-by')
        $writer.WriteElementString('tool', 'CompatBridge')
        $writer.WriteElementString('version', $script:ModuleVersion)
        $writer.WriteElementString('date-created', $CreatedAt.ToString('yyyyMMdd.HHmmss'))
        $writer.WriteEndElement()

        $unique = @{}
        foreach ($site in $Sites) {
            $url = [string]$site.Url
            if ([string]::IsNullOrWhiteSpace($url)) {
                throw '站点条目缺少 Url。'
            }
            $key = $url.ToLowerInvariant()
            if ($unique.ContainsKey($key)) {
                throw "站点列表包含重复条目：$url"
            }
            $unique[$key] = $true

            $compatMode = 'Default'
            if ($site.PSObject.Properties['CompatMode'] -and
                -not [string]::IsNullOrWhiteSpace([string]$site.CompatMode)) {
                $compatMode = [string]$site.CompatMode
            }
            if ($compatMode -notin @('Default', 'IE8Enterprise', 'IE7Enterprise')) {
                throw "不支持的兼容模式：$compatMode"
            }

            $allowRedirect = $false
            if ($site.PSObject.Properties['AllowRedirect']) {
                $allowRedirect = [bool]$site.AllowRedirect
            }

            $writer.WriteStartElement('site')
            $writer.WriteAttributeString('url', $url)
            $writer.WriteElementString('compat-mode', $compatMode)
            $writer.WriteStartElement('open-in')
            if ($allowRedirect) {
                $writer.WriteAttributeString('allow-redirect', 'true')
            }
            $writer.WriteString('IE11')
            $writer.WriteEndElement()
            $writer.WriteEndElement()
        }

        $writer.WriteEndElement()
        $writer.WriteEndDocument()
    }
    finally {
        $writer.Dispose()
    }

    $xmlBytes = $stream.ToArray()
    $stream.Dispose()
    return $settings.Encoding.GetString($xmlBytes)
}

function Import-CompatBridgeSiteList {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
        [string]$Path
    )

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $document = New-Object System.Xml.XmlDocument
    $document.PreserveWhitespace = $false
    $document.Load($resolvedPath)

    if ($document.DocumentElement.Name -ne 'site-list') {
        throw 'XML 根元素必须是 site-list（Enterprise Mode schema v2）。'
    }

    $versionNumber = 0
    if (-not [int]::TryParse($document.DocumentElement.GetAttribute('version'), [ref]$versionNumber) -or
        $versionNumber -lt 1) {
        throw 'site-list version 必须为正整数。'
    }

    $sites = New-Object System.Collections.Generic.List[object]
    $seen = @{}
    foreach ($siteNode in $document.SelectNodes('/site-list/site')) {
        $url = $siteNode.GetAttribute('url')
        $normalized = ConvertTo-CompatBridgeSiteEntry -InputObject $url
        if (-not $normalized.IsValid -or $normalized.Url -cne $url) {
            throw "XML 包含未规范化或无效的站点条目：$url"
        }
        $key = $url.ToLowerInvariant()
        if ($seen.ContainsKey($key)) {
            throw "XML 包含重复站点条目：$url"
        }
        $seen[$key] = $true

        $compatNode = $siteNode.SelectSingleNode('compat-mode')
        $openInNode = $siteNode.SelectSingleNode('open-in')
        if ($null -eq $compatNode -or $null -eq $openInNode) {
            throw "站点条目缺少 compat-mode 或 open-in：$url"
        }
        if ($compatNode.InnerText -notin @('Default', 'IE8Enterprise', 'IE7Enterprise')) {
            throw "站点条目使用不支持的 compat-mode：$url"
        }
        if ($openInNode.InnerText.Trim() -ne 'IE11') {
            throw "站点条目必须使用 open-in=IE11：$url"
        }

        $sites.Add([pscustomobject]@{
            Url = $url
            CompatMode = $compatNode.InnerText
            AllowRedirect = ($openInNode.GetAttribute('allow-redirect') -eq 'true')
        })
    }

    [pscustomobject]@{
        Version = $versionNumber
        Sites = $sites.ToArray()
        Path = $resolvedPath
    }
}

function Export-CompatBridgeSiteList {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][ValidateRange(1, 999999999)][int]$Version,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Sites
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $directory = [System.IO.Path]::GetDirectoryName($fullPath)
    if (-not (Test-Path -LiteralPath $directory)) {
        [void][System.IO.Directory]::CreateDirectory($directory)
    }

    $xml = New-CompatBridgeSiteListDocument -Version $Version -Sites $Sites
    if (-not $PSCmdlet.ShouldProcess($fullPath, '原子写入 Enterprise Mode Site List v2 XML')) {
        return
    }

    $temporaryPath = Join-Path $directory ([System.IO.Path]::GetRandomFileName())
    try {
        $encoding = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($temporaryPath, $xml, $encoding)

        # Parse the temporary file before replacing the destination.
        [void](Import-CompatBridgeSiteList -Path $temporaryPath)

        if (Test-Path -LiteralPath $fullPath) {
            $replaceBackup = $fullPath + '.replace-backup'
            try {
                [System.IO.File]::Replace($temporaryPath, $fullPath, $replaceBackup, $true)
            }
            finally {
                if (Test-Path -LiteralPath $replaceBackup) {
                    Remove-Item -LiteralPath $replaceBackup -Force
                }
            }
        }
        else {
            [System.IO.File]::Move($temporaryPath, $fullPath)
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }

    Get-Item -LiteralPath $fullPath
}

function Get-CompatBridgeRegistryValue {
    param(
        [Parameter(Mandatory = $true)]
        [Microsoft.Win32.RegistryHive]$Hive,
        [Parameter(Mandatory = $true)]
        [string]$SubKey,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    try {
        $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
            $Hive,
            [Microsoft.Win32.RegistryView]::Registry64
        )
        try {
            $key = $baseKey.OpenSubKey($SubKey, $false)
            if ($null -eq $key) {
                return [pscustomobject]@{ Exists = $false; Value = $null; Kind = $null }
            }
            try {
                if ($key.GetValueNames() -notcontains $Name) {
                    return [pscustomobject]@{ Exists = $false; Value = $null; Kind = $null }
                }
                return [pscustomobject]@{
                    Exists = $true
                    Value = $key.GetValue($Name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                    Kind = $key.GetValueKind($Name).ToString()
                }
            }
            finally {
                $key.Dispose()
            }
        }
        finally {
            $baseKey.Dispose()
        }
    }
    catch {
        [pscustomobject]@{
            Exists = $false
            Value = $null
            Kind = $null
            ReadError = $_.Exception.Message
        }
    }
}

function Get-CompatBridgeEdgeVersion {
    $candidates = New-Object System.Collections.Generic.List[object]
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $candidates.Add([pscustomobject]@{
            Path = Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'
            Scope = 'System'
        })
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $candidates.Add([pscustomobject]@{
            Path = Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe'
            Scope = 'System'
        })
    }
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $candidates.Add([pscustomobject]@{
            Path = Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\Application\msedge.exe'
            Scope = 'User'
        })
    }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate.Path -PathType Leaf) {
            return [pscustomobject]@{
                Installed = $true
                SystemInstalled = ($candidate.Scope -eq 'System')
                Scope = $candidate.Scope
                Path = $candidate.Path
                Version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($candidate.Path).ProductVersion
            }
        }
    }
    [pscustomobject]@{
        Installed = $false
        SystemInstalled = $false
        Scope = $null
        Path = $null
        Version = $null
    }
}

function Get-CompatBridgeEnvironmentStatus {
    [CmdletBinding()]
    param(
        [string]$DataRoot = 'C:\ProgramData\CompatBridge'
    )

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    $isAdministrator = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    $hklmLevel = Get-CompatBridgeRegistryValue -Hive LocalMachine -SubKey $script:EdgePolicySubKey -Name 'InternetExplorerIntegrationLevel'
    $hklmList = Get-CompatBridgeRegistryValue -Hive LocalMachine -SubKey $script:EdgePolicySubKey -Name 'InternetExplorerIntegrationSiteList'
    $hklmCloud = Get-CompatBridgeRegistryValue -Hive LocalMachine -SubKey $script:EdgePolicySubKey -Name 'InternetExplorerIntegrationCloudSiteList'
    $hkcuLevel = Get-CompatBridgeRegistryValue -Hive CurrentUser -SubKey $script:EdgePolicySubKey -Name 'InternetExplorerIntegrationLevel'
    $hkcuList = Get-CompatBridgeRegistryValue -Hive CurrentUser -SubKey $script:EdgePolicySubKey -Name 'InternetExplorerIntegrationSiteList'
    $hkcuCloud = Get-CompatBridgeRegistryValue -Hive CurrentUser -SubKey $script:EdgePolicySubKey -Name 'InternetExplorerIntegrationCloudSiteList'
    $legacyMachine = Get-CompatBridgeRegistryValue -Hive LocalMachine -SubKey $script:LegacyIePolicySubKey -Name 'SiteList'
    $legacyUser = Get-CompatBridgeRegistryValue -Hive CurrentUser -SubKey $script:LegacyIePolicySubKey -Name 'SiteList'

    $conflicts = New-Object System.Collections.Generic.List[string]
    if ($hklmCloud.Exists -or $hkcuCloud.Exists) {
        $conflicts.Add('检测到 M365 Cloud Site List；该策略优先于本地 Enterprise Mode Site List。')
    }
    if ($hkcuLevel.Exists -or $hkcuList.Exists) {
        $conflicts.Add('检测到 HKCU Edge IE 模式策略；必须先确认组织策略归属和优先级。')
    }
    if ($legacyMachine.Exists -or $legacyUser.Exists) {
        $conflicts.Add('检测到 IE 旧版 Enterprise Mode Site List 策略；不得直接覆盖或删除。')
    }

    $expectedXml = Join-Path ([System.IO.Path]::GetFullPath($DataRoot)) 'sites.xml'
    $statePath = Join-Path ([System.IO.Path]::GetFullPath($DataRoot)) 'state.json'
    if (Test-Path -LiteralPath $statePath -PathType Leaf) {
        try {
            $statusState = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
            if ([string]$statusState.Phase -eq 'Applying') {
                $conflicts.Add('检测到中断的 CompatBridge 事务；请先运行 recover -Apply。')
            }
            elseif ([string]$statusState.Phase -ne 'Active') {
                $conflicts.Add('CompatBridge 状态文件不是可用的 Active 状态。')
            }
        }
        catch {
            $conflicts.Add('CompatBridge 状态文件无法解析，必须先人工检查。')
        }
    }
    if ($hklmLevel.Exists -and -not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
        $conflicts.Add('HKLM 已配置 IE 模式集成级别，但不存在 CompatBridge 状态文件；不得假定该策略归本工具所有。')
    }
    if ($hklmList.Exists) {
        $listValue = [string]$hklmList.Value
        $ownsExpectedPath = $listValue -eq $expectedXml -or
            $listValue -eq ([System.Uri]$expectedXml).AbsoluteUri
        if (-not $ownsExpectedPath) {
            $conflicts.Add('HKLM 已配置其他 Enterprise Mode Site List；不得静默接管。')
        }
        elseif (-not (Test-Path -LiteralPath $statePath -PathType Leaf)) {
            $conflicts.Add('HKLM 指向 CompatBridge 数据目录，但缺少 state.json，无法证明策略归属。')
        }
    }

    $isWindows = ($env:OS -eq 'Windows_NT')
    $windowsVersion = [Environment]::OSVersion.Version
    $edge = Get-CompatBridgeEdgeVersion
    $supportIssues = New-Object System.Collections.Generic.List[string]
    if (-not $isWindows -or $windowsVersion.Major -lt 10) {
        $supportIssues.Add('需要受支持的 Windows 10、Windows 11 或相应 Windows Server 版本。')
    }
    if (-not $edge.Installed) {
        $supportIssues.Add('未检测到 Microsoft Edge。')
    }
    elseif (-not $edge.SystemInstalled) {
        $supportIssues.Add('检测到的 Edge 是每用户安装；IE 模式要求系统级安装。')
    }
    else {
        $edgeVersion = $null
        if (-not [version]::TryParse([string]$edge.Version, [ref]$edgeVersion) -or
            $edgeVersion.Major -lt 78) {
            $supportIssues.Add('Microsoft Edge 版本低于 IE 模式策略要求的 78。')
        }
    }

    [pscustomobject]@{
        IsWindows = $isWindows
        WindowsVersion = $windowsVersion.ToString()
        IsAdministrator = $isAdministrator
        Edge = $edge
        IsSupported = ($supportIssues.Count -eq 0)
        SupportIssues = $supportIssues.ToArray()
        DataRoot = [System.IO.Path]::GetFullPath($DataRoot)
        Policy = [pscustomobject]@{
            HKLM = [pscustomobject]@{
                IntegrationLevel = $hklmLevel
                SiteList = $hklmList
                CloudSiteList = $hklmCloud
            }
            HKCU = [pscustomobject]@{
                IntegrationLevel = $hkcuLevel
                SiteList = $hkcuList
                CloudSiteList = $hkcuCloud
            }
            LegacyIE = [pscustomobject]@{
                HKLM = $legacyMachine
                HKCU = $legacyUser
            }
        }
        HasBlockingConflict = ($conflicts.Count -gt 0)
        Conflicts = $conflicts.ToArray()
    }
}

function Get-CompatBridgeRuntimePaths {
    param([Parameter(Mandatory = $true)][string]$DataRoot)

    $root = [System.IO.Path]::GetFullPath($DataRoot)
    [pscustomobject]@{
        Root = $root
        Xml = Join-Path $root 'sites.xml'
        State = Join-Path $root 'state.json'
        Lock = Join-Path $root 'operation.lock'
        Backups = Join-Path $root 'backups'
        Logs = Join-Path $root 'logs'
        Log = Join-Path (Join-Path $root 'logs') 'operations.jsonl'
    }
}

function Initialize-CompatBridgeDirectories {
    param([Parameter(Mandatory = $true)]$Paths)

    foreach ($directory in @($Paths.Root, $Paths.Backups, $Paths.Logs)) {
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
            [void][System.IO.Directory]::CreateDirectory($directory)
        }
    }
}

function Enter-CompatBridgeOperationLock {
    param([Parameter(Mandatory = $true)]$Paths)

    Initialize-CompatBridgeDirectories -Paths $Paths
    try {
        return [System.IO.File]::Open(
            $Paths.Lock,
            [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite,
            [System.IO.FileShare]::None
        )
    }
    catch {
        throw '另一个 CompatBridge 操作正在进行，请稍后重试。'
    }
}

function Write-CompatBridgeTextAtomic {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $directory = [System.IO.Path]::GetDirectoryName($fullPath)
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        [void][System.IO.Directory]::CreateDirectory($directory)
    }

    $temporaryPath = Join-Path $directory ([System.IO.Path]::GetRandomFileName())
    try {
        [System.IO.File]::WriteAllText(
            $temporaryPath,
            $Text,
            (New-Object System.Text.UTF8Encoding($false))
        )
        if (Test-Path -LiteralPath $fullPath -PathType Leaf) {
            $replaceBackup = $fullPath + '.replace-backup'
            try {
                [System.IO.File]::Replace($temporaryPath, $fullPath, $replaceBackup, $true)
            }
            finally {
                if (Test-Path -LiteralPath $replaceBackup -PathType Leaf) {
                    Remove-Item -LiteralPath $replaceBackup -Force
                }
            }
        }
        else {
            [System.IO.File]::Move($temporaryPath, $fullPath)
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Copy-CompatBridgeFileAtomic {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $fullDestination = [System.IO.Path]::GetFullPath($Destination)
    $directory = [System.IO.Path]::GetDirectoryName($fullDestination)
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        [void][System.IO.Directory]::CreateDirectory($directory)
    }

    $temporaryPath = Join-Path $directory ([System.IO.Path]::GetRandomFileName())
    try {
        [System.IO.File]::Copy($Source, $temporaryPath, $true)
        if (Test-Path -LiteralPath $fullDestination -PathType Leaf) {
            $replaceBackup = $fullDestination + '.replace-backup'
            try {
                [System.IO.File]::Replace($temporaryPath, $fullDestination, $replaceBackup, $true)
            }
            finally {
                if (Test-Path -LiteralPath $replaceBackup -PathType Leaf) {
                    Remove-Item -LiteralPath $replaceBackup -Force
                }
            }
        }
        else {
            [System.IO.File]::Move($temporaryPath, $fullDestination)
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Read-CompatBridgeState {
    param([Parameter(Mandatory = $true)]$Paths)

    if (-not (Test-Path -LiteralPath $Paths.State -PathType Leaf)) {
        return $null
    }
    try {
        return (Get-Content -LiteralPath $Paths.State -Raw -Encoding UTF8 | ConvertFrom-Json)
    }
    catch {
        throw "无法读取状态文件 $($Paths.State)：$($_.Exception.Message)"
    }
}

function Write-CompatBridgeState {
    param(
        [Parameter(Mandatory = $true)]$Paths,
        [Parameter(Mandatory = $true)]$State
    )

    $json = $State | ConvertTo-Json -Depth 20
    Write-CompatBridgeTextAtomic -Path $Paths.State -Text $json
}

function Set-CompatBridgeRegistryValue {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][Microsoft.Win32.RegistryValueKind]$Kind
    )

    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::LocalMachine,
        [Microsoft.Win32.RegistryView]::Registry64
    )
    try {
        $key = $baseKey.CreateSubKey($script:EdgePolicySubKey, $true)
        try {
            $key.SetValue($Name, $Value, $Kind)
        }
        finally {
            $key.Dispose()
        }
    }
    finally {
        $baseKey.Dispose()
    }
}

function Restore-CompatBridgeRegistryValue {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$Snapshot
    )

    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::LocalMachine,
        [Microsoft.Win32.RegistryView]::Registry64
    )
    try {
        $key = $baseKey.CreateSubKey($script:EdgePolicySubKey, $true)
        try {
            if (-not [bool]$Snapshot.Exists) {
                $key.DeleteValue($Name, $false)
                return
            }

            $kind = [Microsoft.Win32.RegistryValueKind][System.Enum]::Parse(
                [Microsoft.Win32.RegistryValueKind],
                [string]$Snapshot.Kind
            )
            $value = $Snapshot.Value
            switch ($kind) {
                'DWord' { $value = [int]$value }
                'QWord' { $value = [long]$value }
                'MultiString' { $value = [string[]]$value }
                'Binary' { $value = [byte[]]$value }
                default { $value = [string]$value }
            }
            $key.SetValue($Name, $value, $kind)
        }
        finally {
            $key.Dispose()
        }
    }
    finally {
        $baseKey.Dispose()
    }
}

function Test-CompatBridgeRegistrySnapshotEqual {
    param(
        [Parameter(Mandatory = $true)]$Left,
        [Parameter(Mandatory = $true)]$Right
    )

    if ([bool]$Left.Exists -ne [bool]$Right.Exists) {
        return $false
    }
    if (-not [bool]$Left.Exists) {
        return $true
    }
    return (
        [string]$Left.Kind -eq [string]$Right.Kind -and
        [string]$Left.Value -eq [string]$Right.Value
    )
}

function Get-CompatBridgeSiteListPolicyValue {
    param([Parameter(Mandatory = $true)][string]$XmlPath)

    $fullPath = [System.IO.Path]::GetFullPath($XmlPath)
    return (New-Object System.Uri($fullPath)).AbsoluteUri
}

function Get-CompatBridgeFileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
        try {
            return (($algorithm.ComputeHash($stream) | ForEach-Object { $_.ToString('x2') }) -join '')
        }
        finally {
            $algorithm.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function New-CompatBridgeBackup {
    param(
        [Parameter(Mandatory = $true)]$Paths,
        [Parameter(Mandatory = $true)][string]$Operation
    )

    $id = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssfffffffZ') + '-' + [guid]::NewGuid().ToString('N')
    $backupDirectory = Join-Path $Paths.Backups $id
    [void][System.IO.Directory]::CreateDirectory($backupDirectory)

    $xmlExists = Test-Path -LiteralPath $Paths.Xml -PathType Leaf
    $stateExists = Test-Path -LiteralPath $Paths.State -PathType Leaf
    if ($xmlExists) {
        Copy-Item -LiteralPath $Paths.Xml -Destination (Join-Path $backupDirectory 'sites.xml') -Force
    }
    if ($stateExists) {
        Copy-Item -LiteralPath $Paths.State -Destination (Join-Path $backupDirectory 'state.json') -Force
    }

    $levelSnapshot = Get-CompatBridgeRegistryValue -Hive LocalMachine -SubKey $script:EdgePolicySubKey -Name 'InternetExplorerIntegrationLevel'
    $siteListSnapshot = Get-CompatBridgeRegistryValue -Hive LocalMachine -SubKey $script:EdgePolicySubKey -Name 'InternetExplorerIntegrationSiteList'
    foreach ($snapshot in @($levelSnapshot, $siteListSnapshot)) {
        if ($snapshot.PSObject.Properties['ReadError']) {
            throw "无法可靠读取注册表策略，未创建事务备份：$($snapshot.ReadError)"
        }
    }

    $manifest = [pscustomobject]@{
        SchemaVersion = 1
        Id = $id
        CreatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        Operation = $Operation
        XmlExists = $xmlExists
        StateExists = $stateExists
        Registry = [pscustomobject]@{
            InternetExplorerIntegrationLevel = $levelSnapshot
            InternetExplorerIntegrationSiteList = $siteListSnapshot
        }
    }
    $manifestPath = Join-Path $backupDirectory 'manifest.json'
    Write-CompatBridgeTextAtomic -Path $manifestPath -Text ($manifest | ConvertTo-Json -Depth 20)
    return $manifestPath
}

function Assert-CompatBridgeBackupPath {
    param(
        [Parameter(Mandatory = $true)]$Paths,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    $fullManifest = [System.IO.Path]::GetFullPath($ManifestPath)
    $fullBackupRoot = [System.IO.Path]::GetFullPath($Paths.Backups).TrimEnd('\') + '\'
    if (-not $fullManifest.StartsWith($fullBackupRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw '状态文件引用了数据目录之外的备份，已拒绝恢复。'
    }
    if (-not (Test-Path -LiteralPath $fullManifest -PathType Leaf)) {
        throw "找不到备份清单：$fullManifest"
    }
    return $fullManifest
}

function Restore-CompatBridgeBackup {
    param(
        [Parameter(Mandatory = $true)]$Paths,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    $safeManifestPath = Assert-CompatBridgeBackupPath -Paths $Paths -ManifestPath $ManifestPath
    $manifest = Get-Content -LiteralPath $safeManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $backupDirectory = Split-Path -Parent $safeManifestPath
    if ([int]$manifest.SchemaVersion -ne 1 -or $null -eq $manifest.Registry) {
        throw '备份清单版本无效或缺少注册表快照。'
    }

    if ([bool]$manifest.XmlExists) {
        $xmlBackup = Join-Path $backupDirectory 'sites.xml'
        if (-not (Test-Path -LiteralPath $xmlBackup -PathType Leaf)) {
            throw '备份清单声明存在 XML，但备份文件缺失。'
        }
        [void](Import-CompatBridgeSiteList -Path $xmlBackup)
        Copy-CompatBridgeFileAtomic -Source $xmlBackup -Destination $Paths.Xml
    }
    elseif (Test-Path -LiteralPath $Paths.Xml -PathType Leaf) {
        Remove-Item -LiteralPath $Paths.Xml -Force
    }

    Restore-CompatBridgeRegistryValue -Name 'InternetExplorerIntegrationLevel' `
        -Snapshot $manifest.Registry.InternetExplorerIntegrationLevel
    Restore-CompatBridgeRegistryValue -Name 'InternetExplorerIntegrationSiteList' `
        -Snapshot $manifest.Registry.InternetExplorerIntegrationSiteList

    if ([bool]$manifest.StateExists) {
        $stateBackup = Join-Path $backupDirectory 'state.json'
        if (-not (Test-Path -LiteralPath $stateBackup -PathType Leaf)) {
            throw '备份清单声明存在状态文件，但备份文件缺失。'
        }
        Copy-CompatBridgeFileAtomic -Source $stateBackup -Destination $Paths.State
    }
    elseif (Test-Path -LiteralPath $Paths.State -PathType Leaf) {
        Remove-Item -LiteralPath $Paths.State -Force
    }
}

function Add-CompatBridgeLog {
    param(
        [Parameter(Mandatory = $true)]$Paths,
        [Parameter(Mandatory = $true)][string]$Operation,
        [Parameter(Mandatory = $true)][string]$Result,
        [hashtable]$Details = @{}
    )

    $record = [ordered]@{
        TimestampUtc = (Get-Date).ToUniversalTime().ToString('o')
        Operation = $Operation
        Result = $Result
        Details = $Details
    }
    $line = ($record | ConvertTo-Json -Compress -Depth 10) + [Environment]::NewLine
    [System.IO.File]::AppendAllText($Paths.Log, $line, (New-Object System.Text.UTF8Encoding($false)))
}

function Assert-CompatBridgeMutationPreflight {
    param(
        [Parameter(Mandatory = $true)]$Paths,
        $State
    )

    $status = Get-CompatBridgeEnvironmentStatus -DataRoot $Paths.Root
    if (-not $status.IsWindows) {
        throw 'CompatBridge 策略原型只能在 Windows 上运行。'
    }
    if (-not $status.IsAdministrator) {
        throw '修改 HKLM Edge 策略需要管理员权限。'
    }
    if (-not $status.IsSupported) {
        throw ('当前环境不支持安全应用 IE 模式策略：' + ($status.SupportIssues -join '；'))
    }
    if ($status.HasBlockingConflict) {
        throw ('检测到阻止性策略冲突：' + ($status.Conflicts -join '；'))
    }

    if ($null -eq $State) {
        if (Test-Path -LiteralPath $Paths.Xml -PathType Leaf) {
            throw '数据目录中已存在无法证明归属的 sites.xml；请先人工确认并导入，不得静默接管。'
        }
    }
    else {
        if ([int]$State.SchemaVersion -ne 1 -or [string]$State.Phase -ne 'Active') {
            throw 'CompatBridge 状态文件不完整或上次事务未正常完成，请先人工恢复。'
        }
        $expectedPolicyValue = Get-CompatBridgeSiteListPolicyValue -XmlPath $Paths.Xml
        if ([string]$State.ManagedSiteListValue -ne $expectedPolicyValue) {
            throw '状态文件中的受管站点列表路径与当前数据目录不一致。'
        }
        if ([string]$State.DataRoot -ne $Paths.Root) {
            throw '状态文件记录的数据目录与当前数据目录不一致。'
        }
        if (-not (Test-Path -LiteralPath $Paths.Xml -PathType Leaf)) {
            throw 'CompatBridge 管理的 sites.xml 已丢失，拒绝以空列表继续。'
        }
        if (-not $State.PSObject.Properties['XmlSha256'] -or
            [string]::IsNullOrWhiteSpace([string]$State.XmlSha256)) {
            throw '状态文件缺少 XML 完整性摘要。'
        }
        $managedList = Import-CompatBridgeSiteList -Path $Paths.Xml
        if ([int]$managedList.Version -ne [int]$State.CurrentVersion) {
            throw 'sites.xml 版本与状态文件不一致，可能被外部修改。'
        }
        if ((Get-CompatBridgeFileSha256 -Path $Paths.Xml) -ne [string]$State.XmlSha256) {
            throw 'sites.xml 完整性校验失败，可能被外部修改。'
        }
        if (-not $status.Policy.HKLM.IntegrationLevel.Exists -or
            [string]$status.Policy.HKLM.IntegrationLevel.Kind -ne 'DWord' -or
            [int]$status.Policy.HKLM.IntegrationLevel.Value -ne 1) {
            throw 'CompatBridge 管理的 IE 模式集成策略已被外部修改或删除。'
        }
        if (-not $status.Policy.HKLM.SiteList.Exists -or
            [string]$status.Policy.HKLM.SiteList.Kind -ne 'String' -or
            [string]$status.Policy.HKLM.SiteList.Value -ne $expectedPolicyValue) {
            throw 'CompatBridge 管理的站点列表策略已被外部修改或删除。'
        }
    }
}

function Invoke-CompatBridgeSiteMutation {
    param(
        [Parameter(Mandatory = $true)]$Paths,
        [Parameter(Mandatory = $true)][string]$Operation,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Sites,
        [Parameter(Mandatory = $true)][int]$Version
    )

    $state = Read-CompatBridgeState -Paths $Paths
    Assert-CompatBridgeMutationPreflight -Paths $Paths -State $state
    $manifestPath = New-CompatBridgeBackup -Paths $Paths -Operation $Operation
    $isInitial = ($null -eq $state)

    if ($isInitial) {
        $state = [pscustomobject]@{
            SchemaVersion = 1
            ToolVersion = $script:ModuleVersion
            InstallId = [guid]::NewGuid().ToString()
            Phase = 'Preparing'
            DataRoot = $Paths.Root
            ManagedSiteListValue = Get-CompatBridgeSiteListPolicyValue -XmlPath $Paths.Xml
            CurrentVersion = 0
            XmlSha256 = $null
            BaselineManifest = $manifestPath
            LastTransaction = $null
            PendingTransaction = $manifestPath
            UpdatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        }
    }

    try {
        if (-not $state.PSObject.Properties['PendingTransaction']) {
            $state | Add-Member -NotePropertyName PendingTransaction -NotePropertyValue $manifestPath
        }
        else {
            $state.PendingTransaction = $manifestPath
        }
        $state.Phase = 'Applying'
        $state.UpdatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        Write-CompatBridgeState -Paths $Paths -State $state

        [void](Export-CompatBridgeSiteList -Path $Paths.Xml -Version $Version -Sites $Sites -Confirm:$false)
        Set-CompatBridgeRegistryValue -Name 'InternetExplorerIntegrationLevel' -Value ([int]1) -Kind DWord
        Set-CompatBridgeRegistryValue -Name 'InternetExplorerIntegrationSiteList' `
            -Value ([string]$state.ManagedSiteListValue) -Kind String

        $writtenLevel = Get-CompatBridgeRegistryValue -Hive LocalMachine -SubKey $script:EdgePolicySubKey -Name 'InternetExplorerIntegrationLevel'
        $writtenList = Get-CompatBridgeRegistryValue -Hive LocalMachine -SubKey $script:EdgePolicySubKey -Name 'InternetExplorerIntegrationSiteList'
        if (-not $writtenLevel.Exists -or [string]$writtenLevel.Kind -ne 'DWord' -or
            [int]$writtenLevel.Value -ne 1 -or -not $writtenList.Exists -or
            [string]$writtenList.Kind -ne 'String' -or
            [string]$writtenList.Value -ne [string]$state.ManagedSiteListValue) {
            throw '策略写入后的回读验证失败。'
        }

        $state.Phase = 'Active'
        $state.CurrentVersion = $Version
        $state.XmlSha256 = Get-CompatBridgeFileSha256 -Path $Paths.Xml
        $state.LastTransaction = $manifestPath
        $state.PendingTransaction = $null
        $state.UpdatedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
        Write-CompatBridgeState -Paths $Paths -State $state
        Add-CompatBridgeLog -Paths $Paths -Operation $Operation -Result 'Success' -Details @{
            Version = $Version
            SiteCount = $Sites.Count
            BackupManifest = $manifestPath
        }
    }
    catch {
        $failure = $_
        try {
            Restore-CompatBridgeBackup -Paths $Paths -ManifestPath $manifestPath
            Add-CompatBridgeLog -Paths $Paths -Operation $Operation -Result 'RolledBack' -Details @{
                Error = $failure.Exception.Message
                BackupManifest = $manifestPath
            }
        }
        catch {
            throw "操作失败，且自动回滚也失败。原始错误：$($failure.Exception.Message)；回滚错误：$($_.Exception.Message)；备份：$manifestPath"
        }
        throw "操作失败，已自动回滚：$($failure.Exception.Message)"
    }

    [pscustomobject]@{
        Operation = $Operation
        Version = $Version
        SiteCount = $Sites.Count
        BackupManifest = $manifestPath
        RequiresEdgeRestart = $true
    }
}

function Get-CompatBridgeSites {
    [CmdletBinding()]
    param([string]$DataRoot = 'C:\ProgramData\CompatBridge')

    $paths = Get-CompatBridgeRuntimePaths -DataRoot $DataRoot
    if (-not (Test-Path -LiteralPath $paths.Xml -PathType Leaf)) {
        return @()
    }
    return (Import-CompatBridgeSiteList -Path $paths.Xml).Sites
}

function Add-CompatBridgeSites {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory = $true)][string[]]$InputObject,
        [ValidateSet('Default', 'IE8Enterprise', 'IE7Enterprise')]
        [string]$CompatMode = 'Default',
        [switch]$AllowRedirect,
        [string]$DataRoot = 'C:\ProgramData\CompatBridge'
    )

    $paths = Get-CompatBridgeRuntimePaths -DataRoot $DataRoot
    if (-not $PSCmdlet.ShouldProcess($paths.Root, '添加站点并应用 Edge IE 模式策略')) {
        return
    }

    $lock = Enter-CompatBridgeOperationLock -Paths $paths
    try {
        $existingSites = @(Get-CompatBridgeSites -DataRoot $paths.Root)
        $preview = @(Get-CompatBridgeInputPreview -InputObject $InputObject `
            -ExistingSite $existingSites -CompatMode $CompatMode -AllowRedirect:$AllowRedirect)
        $invalid = @($preview | Where-Object Classification -eq 'Invalid')
        if ($invalid.Count -gt 0) {
            throw ('存在非法输入，未做任何修改：' + (($invalid | ForEach-Object { "$($_.Raw) ($($_.Error))" }) -join '；'))
        }
        $conflicting = @($preview | Where-Object Classification -eq 'ConflictSettings')
        if ($conflicting.Count -gt 0) {
            throw ('现有站点使用不同的兼容设置，原型不会静默覆盖：' +
                (($conflicting | ForEach-Object Url) -join '；'))
        }

        $newSites = @(
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
        if ($newSites.Count -eq 0) {
            return [pscustomobject]@{
                Changed = $false
                Preview = $preview
                Message = '没有可添加的新站点。'
            }
        }

        $state = Read-CompatBridgeState -Paths $paths
        $nextVersion = 1
        if ($null -ne $state) {
            if ([int]$state.CurrentVersion -ge 999999999) {
                throw 'Site List 版本号已达到原型上限，无法继续递增。'
            }
            $nextVersion = [int]$state.CurrentVersion + 1
        }
        $result = Invoke-CompatBridgeSiteMutation -Paths $paths -Operation 'Add' `
            -Sites @($existingSites + $newSites) -Version $nextVersion
        $result | Add-Member -NotePropertyName Changed -NotePropertyValue $true
        $result | Add-Member -NotePropertyName Preview -NotePropertyValue $preview
        return $result
    }
    finally {
        $lock.Dispose()
    }
}

function Remove-CompatBridgeSites {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param(
        [Parameter(Mandatory = $true)][string[]]$InputObject,
        [string]$DataRoot = 'C:\ProgramData\CompatBridge'
    )

    $paths = Get-CompatBridgeRuntimePaths -DataRoot $DataRoot
    if (-not $PSCmdlet.ShouldProcess($paths.Root, '删除站点并应用 Edge IE 模式策略')) {
        return
    }

    $lock = Enter-CompatBridgeOperationLock -Paths $paths
    try {
        $existingSites = @(Get-CompatBridgeSites -DataRoot $paths.Root)
        $preview = @(Get-CompatBridgeInputPreview -InputObject $InputObject)
        $invalid = @($preview | Where-Object Classification -eq 'Invalid')
        if ($invalid.Count -gt 0) {
            throw ('存在非法输入，未做任何修改：' + (($invalid | ForEach-Object { "$($_.Raw) ($($_.Error))" }) -join '；'))
        }

        $requested = @{}
        foreach ($item in ($preview | Where-Object Classification -eq 'Ready')) {
            $requested[$item.Url.ToLowerInvariant()] = $true
        }
        $matched = @($existingSites | Where-Object { $requested.ContainsKey($_.Url.ToLowerInvariant()) })
        $remaining = @($existingSites | Where-Object { -not $requested.ContainsKey($_.Url.ToLowerInvariant()) })
        $notFound = @(
            $requested.Keys |
                Where-Object {
                    $key = $_
                    -not ($existingSites | Where-Object { $_.Url.ToLowerInvariant() -eq $key })
                }
        )
        if ($matched.Count -eq 0) {
            return [pscustomobject]@{
                Changed = $false
                Matched = @()
                NotFound = $notFound
                Preview = $preview
                Message = '没有匹配到可删除的站点。'
            }
        }

        $state = Read-CompatBridgeState -Paths $paths
        if ($null -eq $state) {
            throw '缺少 CompatBridge 状态文件，拒绝修改无法证明归属的站点列表。'
        }
        if ([int]$state.CurrentVersion -ge 999999999) {
            throw 'Site List 版本号已达到原型上限，无法继续递增。'
        }
        $nextVersion = [int]$state.CurrentVersion + 1
        $result = Invoke-CompatBridgeSiteMutation -Paths $paths -Operation 'Remove' `
            -Sites $remaining -Version $nextVersion
        $result | Add-Member -NotePropertyName Changed -NotePropertyValue $true
        $result | Add-Member -NotePropertyName Matched -NotePropertyValue @($matched | ForEach-Object Url)
        $result | Add-Member -NotePropertyName NotFound -NotePropertyValue $notFound
        $result | Add-Member -NotePropertyName Preview -NotePropertyValue $preview
        return $result
    }
    finally {
        $lock.Dispose()
    }
}

function Undo-CompatBridgeLastChange {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param([string]$DataRoot = 'C:\ProgramData\CompatBridge')

    $paths = Get-CompatBridgeRuntimePaths -DataRoot $DataRoot
    if (-not $PSCmdlet.ShouldProcess($paths.Root, '撤销 CompatBridge 上一次变更')) {
        return
    }
    $lock = Enter-CompatBridgeOperationLock -Paths $paths
    try {
        $state = Read-CompatBridgeState -Paths $paths
        if ($null -eq $state -or [string]::IsNullOrWhiteSpace([string]$state.LastTransaction)) {
            throw '没有可撤销的 CompatBridge 变更。'
        }
        Assert-CompatBridgeMutationPreflight -Paths $paths -State $state
        $manifestPath = [string]$state.LastTransaction
        $safetyManifest = New-CompatBridgeBackup -Paths $paths -Operation 'UndoSafety'
        try {
            Restore-CompatBridgeBackup -Paths $paths -ManifestPath $manifestPath
        }
        catch {
            $restoreFailure = $_
            try {
                Restore-CompatBridgeBackup -Paths $paths -ManifestPath $safetyManifest
            }
            catch {
                throw "撤销失败，且无法恢复撤销前状态。撤销错误：$($restoreFailure.Exception.Message)；恢复错误：$($_.Exception.Message)；安全备份：$safetyManifest"
            }
            throw "撤销失败，已恢复到撤销前状态：$($restoreFailure.Exception.Message)"
        }
        Add-CompatBridgeLog -Paths $paths -Operation 'Undo' -Result 'Success' -Details @{
            RestoredManifest = $manifestPath
            SafetyManifest = $safetyManifest
        }
        [pscustomobject]@{
            Changed = $true
            RestoredManifest = $manifestPath
            RequiresEdgeRestart = $true
        }
    }
    finally {
        $lock.Dispose()
    }
}

function Restore-CompatBridgeBaseline {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param([string]$DataRoot = 'C:\ProgramData\CompatBridge')

    $paths = Get-CompatBridgeRuntimePaths -DataRoot $DataRoot
    if (-not $PSCmdlet.ShouldProcess($paths.Root, '完整恢复 CompatBridge 运行前状态')) {
        return
    }
    $lock = Enter-CompatBridgeOperationLock -Paths $paths
    try {
        $state = Read-CompatBridgeState -Paths $paths
        if ($null -eq $state -or [string]::IsNullOrWhiteSpace([string]$state.BaselineManifest)) {
            throw '没有可恢复的 CompatBridge 初始状态。'
        }
        Assert-CompatBridgeMutationPreflight -Paths $paths -State $state
        $manifestPath = [string]$state.BaselineManifest
        $safetyManifest = New-CompatBridgeBackup -Paths $paths -Operation 'RestoreBaselineSafety'
        try {
            Restore-CompatBridgeBackup -Paths $paths -ManifestPath $manifestPath
        }
        catch {
            $restoreFailure = $_
            try {
                Restore-CompatBridgeBackup -Paths $paths -ManifestPath $safetyManifest
            }
            catch {
                throw "恢复初始状态失败，且无法恢复操作前状态。恢复错误：$($restoreFailure.Exception.Message)；二次恢复错误：$($_.Exception.Message)；安全备份：$safetyManifest"
            }
            throw "恢复初始状态失败，已恢复到操作前状态：$($restoreFailure.Exception.Message)"
        }
        Add-CompatBridgeLog -Paths $paths -Operation 'RestoreBaseline' -Result 'Success' -Details @{
            RestoredManifest = $manifestPath
            SafetyManifest = $safetyManifest
        }
        [pscustomobject]@{
            Changed = $true
            RestoredManifest = $manifestPath
            RequiresEdgeRestart = $true
        }
    }
    finally {
        $lock.Dispose()
    }
}

function Repair-CompatBridgeInterruptedTransaction {
    [CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
    param([string]$DataRoot = 'C:\ProgramData\CompatBridge')

    $paths = Get-CompatBridgeRuntimePaths -DataRoot $DataRoot
    if (-not $PSCmdlet.ShouldProcess($paths.Root, '恢复中断的 CompatBridge 事务')) {
        return
    }
    $lock = Enter-CompatBridgeOperationLock -Paths $paths
    try {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw '恢复 HKLM Edge 策略需要管理员权限。'
        }

        $state = Read-CompatBridgeState -Paths $paths
        if ($null -eq $state -or [string]$state.Phase -ne 'Applying' -or
            -not $state.PSObject.Properties['PendingTransaction'] -or
            [string]::IsNullOrWhiteSpace([string]$state.PendingTransaction)) {
            throw '没有可自动恢复的中断事务。'
        }
        if ([int]$state.SchemaVersion -ne 1 -or [string]$state.DataRoot -ne $paths.Root) {
            throw '中断事务的状态文件版本或数据目录不匹配。'
        }

        $pendingManifestPath = Assert-CompatBridgeBackupPath -Paths $paths `
            -ManifestPath ([string]$state.PendingTransaction)
        $pendingManifest = Get-Content -LiteralPath $pendingManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([int]$pendingManifest.SchemaVersion -ne 1 -or $null -eq $pendingManifest.Registry) {
            throw '中断事务引用的备份清单无效。'
        }
        $expectedManagedValue = Get-CompatBridgeSiteListPolicyValue -XmlPath $paths.Xml
        if ([string]$state.ManagedSiteListValue -ne $expectedManagedValue) {
            throw '中断事务记录的站点列表路径与当前数据目录不一致。'
        }

        $currentLevel = Get-CompatBridgeRegistryValue -Hive LocalMachine `
            -SubKey $script:EdgePolicySubKey -Name 'InternetExplorerIntegrationLevel'
        $currentList = Get-CompatBridgeRegistryValue -Hive LocalMachine `
            -SubKey $script:EdgePolicySubKey -Name 'InternetExplorerIntegrationSiteList'
        if ($currentLevel.PSObject.Properties['ReadError'] -or
            $currentList.PSObject.Properties['ReadError']) {
            throw '无法可靠读取当前 Edge 策略，拒绝自动恢复。'
        }
        $intendedLevel = [pscustomobject]@{ Exists = $true; Kind = 'DWord'; Value = 1 }
        $intendedList = [pscustomobject]@{
            Exists = $true
            Kind = 'String'
            Value = [string]$state.ManagedSiteListValue
        }

        $levelIsExpected = (Test-CompatBridgeRegistrySnapshotEqual -Left $currentLevel `
            -Right $pendingManifest.Registry.InternetExplorerIntegrationLevel) -or
            (Test-CompatBridgeRegistrySnapshotEqual -Left $currentLevel -Right $intendedLevel)
        $listIsExpected = (Test-CompatBridgeRegistrySnapshotEqual -Left $currentList `
            -Right $pendingManifest.Registry.InternetExplorerIntegrationSiteList) -or
            (Test-CompatBridgeRegistrySnapshotEqual -Left $currentList -Right $intendedList)
        if (-not $levelIsExpected -or -not $listIsExpected) {
            throw '中断后策略又被外部修改；为避免覆盖组织策略，拒绝自动恢复。'
        }

        $safetyManifest = New-CompatBridgeBackup -Paths $paths -Operation 'InterruptedRecoverySafety'
        try {
            Restore-CompatBridgeBackup -Paths $paths -ManifestPath $pendingManifestPath
        }
        catch {
            $restoreFailure = $_
            try {
                Restore-CompatBridgeBackup -Paths $paths -ManifestPath $safetyManifest
            }
            catch {
                throw "中断恢复失败，且无法恢复操作前状态。恢复错误：$($restoreFailure.Exception.Message)；二次恢复错误：$($_.Exception.Message)；安全备份：$safetyManifest"
            }
            throw "中断恢复失败，已恢复到操作前状态：$($restoreFailure.Exception.Message)"
        }

        Add-CompatBridgeLog -Paths $paths -Operation 'RecoverInterrupted' -Result 'Success' -Details @{
            RestoredManifest = $pendingManifestPath
            SafetyManifest = $safetyManifest
        }
        [pscustomobject]@{
            Changed = $true
            RestoredManifest = $pendingManifestPath
            SafetyManifest = $safetyManifest
            RequiresEdgeRestart = $true
        }
    }
    finally {
        $lock.Dispose()
    }
}

Export-ModuleMember -Function @(
    'ConvertTo-CompatBridgeSiteEntry',
    'Get-CompatBridgeInputPreview',
    'New-CompatBridgeSiteListDocument',
    'Import-CompatBridgeSiteList',
    'Export-CompatBridgeSiteList',
    'Get-CompatBridgeEnvironmentStatus',
    'Get-CompatBridgeSites',
    'Add-CompatBridgeSites',
    'Remove-CompatBridgeSites',
    'Undo-CompatBridgeLastChange',
    'Restore-CompatBridgeBaseline',
    'Repair-CompatBridgeInterruptedTransaction'
)
