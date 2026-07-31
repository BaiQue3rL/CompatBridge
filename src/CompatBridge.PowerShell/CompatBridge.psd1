@{
    RootModule = 'CompatBridge.psm1'
    ModuleVersion = '0.1.0'
    GUID = '876b4d2b-76b9-4be2-98c7-1a2096becc78'
    Author = 'CompatBridge'
    CompanyName = 'CompatBridge'
    Copyright = '(c) CompatBridge contributors'
    Description = 'PowerShell prototype core for CompatBridge Edge IE mode site management.'
    PowerShellVersion = '5.1'
    FunctionsToExport = @(
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
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
}
