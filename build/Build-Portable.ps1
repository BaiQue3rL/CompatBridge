[CmdletBinding()]
param(
    [switch]$SkipTests
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repositoryRoot 'src\CompatBridge.WinForms'
$testRoot = Join-Path $repositoryRoot 'tests\CompatBridge.WinForms.Tests'
$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$portableRoot = Join-Path $artifactRoot 'portable'
$testOutputRoot = Join-Path $artifactRoot 'tests'
$testRunRoot = Join-Path $artifactRoot (
    'test-runs\' + [guid]::NewGuid().ToString('N')
)
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $csc -PathType Leaf)) {
    throw '.NET Framework 4.8 C# compiler was not found.'
}

foreach ($directory in @($portableRoot, $testOutputRoot, $testRunRoot)) {
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        [void](New-Item -ItemType Directory -Path $directory)
    }
}

$coreSources = @(
    Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'Core') -Filter *.cs -File |
        Sort-Object Name |
        ForEach-Object FullName
)
$appSources = @(
    Get-ChildItem -LiteralPath $sourceRoot -Filter *.cs -File |
        Sort-Object Name |
        ForEach-Object FullName
) + @(
    (Join-Path $sourceRoot 'Properties\AssemblyInfo.cs')
) + $coreSources

$commonReferences = @(
    '/reference:System.dll',
    '/reference:System.Core.dll',
    '/reference:System.Xml.dll',
    '/reference:System.Runtime.Serialization.dll'
)

$appOutput = Join-Path $portableRoot 'CompatBridge.exe'
$appArguments = @(
    '/nologo',
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/debug-',
    '/warn:4',
    '/filealign:512',
    ('/win32icon:' + (Join-Path $repositoryRoot 'assets\CompatBridge.ico')),
    ('/win32manifest:' + (Join-Path $sourceRoot 'app.manifest')),
    ('/out:' + $appOutput)
) + $commonReferences + @(
    '/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll'
) + $appSources

Write-Host 'Building CompatBridge.exe...'
& $csc $appArguments
if ($LASTEXITCODE -ne 0) {
    throw "CompatBridge.exe compilation failed with exit code $LASTEXITCODE."
}

if (-not $SkipTests) {
    $testOutput = Join-Path $testOutputRoot 'CompatBridge.Tests.exe'
    $testArguments = @(
        '/nologo',
        '/target:exe',
        '/platform:anycpu',
        '/optimize+',
        '/debug-',
        '/warn:4',
        ('/out:' + $testOutput)
    ) + $commonReferences + $coreSources + @(
        (Join-Path $testRoot 'Program.cs')
    )

    Write-Host 'Building C# tests...'
    & $csc $testArguments
    if ($LASTEXITCODE -ne 0) {
        throw "C# test compilation failed with exit code $LASTEXITCODE."
    }

    Write-Host 'Running C# tests...'
    & $testOutput $testRunRoot
    if ($LASTEXITCODE -ne 0) {
        throw "C# tests failed with exit code $LASTEXITCODE."
    }
}

Copy-Item -LiteralPath (
    Join-Path $repositoryRoot 'docs\PORTABLE_README.txt'
) -Destination (
    Join-Path $portableRoot '使用说明.txt'
) -Force

$hash = (Get-FileHash -LiteralPath $appOutput -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = Join-Path $portableRoot 'SHA256.txt'
[System.IO.File]::WriteAllText(
    $checksumPath,
    "$hash  CompatBridge.exe`r`n",
    (New-Object System.Text.UTF8Encoding($false))
)

$zipPath = Join-Path $artifactRoot 'CompatBridge-portable-v0.3.5-preview.zip'
if (Test-Path -LiteralPath $zipPath -PathType Leaf) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (
    Join-Path $portableRoot '*'
) -DestinationPath $zipPath -CompressionLevel Optimal

$file = Get-Item -LiteralPath $appOutput
Write-Host ''
Write-Host "Built: $($file.FullName)"
Write-Host "Size:  $($file.Length) bytes"
Write-Host "SHA256: $hash"
Write-Host "ZIP:    $zipPath"
