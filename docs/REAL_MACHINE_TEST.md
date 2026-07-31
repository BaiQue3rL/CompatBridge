# 真实机器验证清单

策略应用必须先在可回滚的测试机或虚拟机完成本清单。不要直接使用承载正式业务、
GPO、Intune 或 M365 Cloud Site List 的生产电脑。

## 前置条件

- Windows 10/11 或相应的受支持 Windows Server；
- 系统级 Microsoft Edge 78 或更高版本；
- 本地管理员权限；
- 一个确定需要 IE 模式的测试站点；
- Edge 中没有未保存的工作。

测试前运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\src\CompatBridge.PowerShell\CompatBridge.ps1 status
```

预期 `Supported=True`、`BlockingConflict=False`。如检测到 Cloud Site List、HKCU
策略、IE 旧站点列表或现有 HKLM 策略，停止测试，不要手工删除单位策略来通过检查。

## 测试步骤

1. 保存只读基线：

   ```powershell
   reg query "HKLM\SOFTWARE\Policies\Microsoft\Edge" /v InternetExplorerIntegrationLevel
   reg query "HKLM\SOFTWARE\Policies\Microsoft\Edge" /v InternetExplorerIntegrationSiteList
   ```

   “找不到”也是有效的基线结果。

2. 创建只包含测试站点的 UTF-8 `sites.txt`，先预览：

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
     .\src\CompatBridge.PowerShell\CompatBridge.ps1 add -File .\sites.txt
   ```

3. 确认预览无非法项后显式应用：

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
     .\src\CompatBridge.PowerShell\CompatBridge.ps1 add -File .\sites.txt -Apply
   ```

4. 检查以下文件均已生成：

   ```text
   C:\ProgramData\CompatBridge\sites.xml
   C:\ProgramData\CompatBridge\state.json
   C:\ProgramData\CompatBridge\backups\<事务编号>\manifest.json
   C:\ProgramData\CompatBridge\logs\operations.jsonl
   ```

5. 在 Edge 打开 `edge://policy`，确认：

   - `InternetExplorerIntegrationLevel` 为 `1`；
   - `InternetExplorerIntegrationSiteList` 指向
     `file:///C:/ProgramData/CompatBridge/sites.xml`；
   - 两项策略没有错误。

6. 由测试人员保存工作并正常关闭所有 Edge 窗口，然后重新打开 Edge。CompatBridge
   原型不会强杀 Edge。

7. 打开 `edge://compat/enterprise`，确认列表已加载、版本号和 XML 一致；访问测试
   站点，确认地址栏显示 IE 模式指示器。

8. 再添加一个站点，确认 `site-list version` 递增 1；删除该站点，再确认版本递增
   1 且其他站点不变。

9. 执行一次撤销并复查 XML、注册表和 Edge：

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
     .\src\CompatBridge.PowerShell\CompatBridge.ps1 undo -Apply
   ```

10. 最后完整恢复初始状态：

    ```powershell
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
      .\src\CompatBridge.PowerShell\CompatBridge.ps1 restore -Apply
    ```

    对照第 1 步确认两个注册表值精确恢复；如果最初不存在，恢复后也应不存在。再次
    正常重启 Edge 并确认测试站点不再由 CompatBridge 强制进入 IE 模式。

## 必测失败场景

在虚拟机快照中分别制造以下情况，确认工具停止且不覆盖：

- HKLM 已有其他站点列表；
- HKCU 存在同名 Edge 策略；
- 配置了 `InternetExplorerIntegrationCloudSiteList`；
- `sites.xml` 被外部编辑或删除；
- `state.json` 缺失、损坏或记录的目录不一致；
- XML 写入期间目标目录变为只读。
- 在虚拟机快照中于事务中途终止 PowerShell，确认 `status` 报告中断事务，并用
  `recover -Apply` 恢复事务前状态。

每个失败场景都应检查备份和原值仍可恢复。完成这些演练前，不将原型用于生产电脑。
