# CompatBridge（兼容桥）— Microsoft Edge IE 模式站点管理工具

<p align="center">
  <img src="assets/compatbridge-logo-final.png" width="112" alt="CompatBridge Logo">
</p>

<p align="center">
  <a href="https://github.com/BaiQue3rL/CompatBridge/releases/tag/v0.3.5-preview"><img src="https://img.shields.io/badge/release-v0.3.5--preview-0067B8" alt="Release v0.3.5-preview"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10/11">
  <img src="https://img.shields.io/badge/.NET%20Framework-4.8-512BD4" alt=".NET Framework 4.8">
  <a href="LICENSE"><img src="https://img.shields.io/github/license/BaiQue3rL/CompatBridge" alt="MIT License"></a>
</p>

<p align="center"><strong>批量管理 Edge IE 模式站点，一键添加，随时撤销。</strong></p>

CompatBridge 是面向 Windows 的 Microsoft Edge IE 模式兼容工具，使用官方
Enterprise Mode Site List v2 和 Edge 策略管理需要 IE11 内核的 OA、ERP、CRM
等旧系统。它不会修改 Edge 用户数据，也不会模拟浏览器点击。

关键词：Microsoft Edge IE mode、Edge 兼容模式、Enterprise Mode Site List v2、
IE11、旧版 OA/ERP/CRM、Windows 浏览器兼容性。

> 当前预览版：`v0.3.5-preview`<br>
> [下载 v0.3.5-preview 绿色版](https://github.com/BaiQue3rL/CompatBridge/releases/tag/v0.3.5-preview)

## 软件界面

主界面只保留最直接的操作：输入网址后添加，或在现有列表中找到网址并删除。

![CompatBridge Edge IE 模式站点管理主界面](docs/images/main-window.png)

批量添加支持多行粘贴、Excel 一列以及 TXT/CSV 导入，并在写入前区分有效、
已存在、重复、非法和冲突项。

![CompatBridge 批量添加与 Enterprise Mode Site List 预览](docs/images/batch-add.png)

> 截图中的域名和 IP 仅为界面展示示例。

## 使用方法

1. 从 [Releases](https://github.com/BaiQue3rL/CompatBridge/releases/tag/v0.3.5-preview)
   下载绿色版 ZIP 并解压；
2. 双击 `CompatBridge.exe`，接受 Windows 管理员权限提示；
3. 输入需要 IE 模式的网址并点击“添加”；
4. 删除时在列表中找到对应网址，点击“删除”；
5. Edge 正在运行时，按提示保存网页内容并确认是否重启。

## 功能特性

仓库目前包含 C# WinForms 绿色版和 PowerShell 验证原型：

- `CompatBridge.exe` 为独立 C# 程序，不封装 PowerShell；
- AnyCPU 单 EXE，目标 .NET Framework 4.8；
- 双击后申请管理员权限，无需安装服务或写入程序目录；
- 主界面采用“输入网址 → 添加”和“找到网址 → 删除”的直接流程；
- 次级“批量添加”入口支持多行/Excel 一列粘贴、TXT/CSV 导入、
  分类预览及只添加有效的新网址；
- 诊断、撤销和初始状态恢复收纳在独立的“诊断与恢复”窗口；

- 将域名、IPv4、带协议/端口/路径的 URL 规范化为 Site List v2 条目；
- 批量预览有效项、重复项和非法项；
- 生成及读取 Enterprise Mode Site List v2 XML；
- 原子写入 XML；
- 只读检查管理员权限、Edge 版本及 HKLM/HKCU 策略冲突；
- 每次策略变更前保存 XML、状态和精确注册表快照，失败时自动回滚；
- 支持撤销上一次操作和完整恢复工具运行前状态；
- 断电或进程终止后可识别并恢复未完成事务；
- 所有变更使用单调递增版本，并用新的本地发布地址规避 Edge 旧列表缓存；
- `add`/`remove` 默认只预览，只有显式增加 `-Apply` 才会修改策略；
- 永远不会自动强杀或重启 Edge。

PowerShell 原型已在 Windows 10 与系统级 Edge 150 上完成注册表、XML、版本递增、
删除、撤销和基线恢复演练。C# WinForms 0.3 预览版已完成真实的重复添加、删除、
再次添加、撤销和版本化策略地址迁移，并通过核心测试与主窗口启动、布局检查。

## PowerShell 验证原型（开发者）

要求：Windows PowerShell 5.1。

```powershell
# 批量预览（支持换行或从 Excel 复制的一列）
.\src\CompatBridge.PowerShell\CompatBridge.ps1 preview -InputText @'
oa.example.com
https://erp.example.com:8443/legacy/login?from=edge
192.168.1.20
'@

# 从 TXT/CSV 预览
.\src\CompatBridge.PowerShell\CompatBridge.ps1 preview -File .\sites.txt

# 生成 XML（仅生成文件，不修改注册表）
.\src\CompatBridge.PowerShell\CompatBridge.ps1 build `
  -File .\sites.txt `
  -OutputPath .\artifacts\sites.xml

# 只读环境检查
.\src\CompatBridge.PowerShell\CompatBridge.ps1 status

# 添加与删除：先预览，再显式应用
.\src\CompatBridge.PowerShell\CompatBridge.ps1 add -File .\sites.txt
.\src\CompatBridge.PowerShell\CompatBridge.ps1 add -File .\sites.txt -Apply
.\src\CompatBridge.PowerShell\CompatBridge.ps1 remove -File .\remove.txt -Apply

# 撤销最近一次操作，或完整恢复首次运行前状态
.\src\CompatBridge.PowerShell\CompatBridge.ps1 undo -Apply
.\src\CompatBridge.PowerShell\CompatBridge.ps1 restore -Apply

# status 提示存在中断事务时
.\src\CompatBridge.PowerShell\CompatBridge.ps1 recover -Apply

# 运行测试
.\tests\Run-Tests.ps1

# 如果本机执行策略禁止脚本，只对这个子进程临时绕过（不修改系统设置）
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-Tests.ps1
```

## 安全边界

- 正式运行数据默认位于 `C:\ProgramData\CompatBridge`。
- 构建输出集中位于仓库的 `artifacts` 目录。
- 查询字符串和片段会从 XML 条目中剔除并产生警告；通配符、凭据、非 HTTP(S)
  协议和非法端口会被拒绝。
- 检测到 M365 Cloud Site List、HKCU 冲突策略或无法证明由 CompatBridge 管理的
  现有 HKLM 站点列表时，后续策略应用必须停止并要求人工处理。

真实机器验证步骤见 [docs/REAL_MACHINE_TEST.md](docs/REAL_MACHINE_TEST.md)。
本次实测记录见
[docs/VALIDATION_2026-07-30.md](docs/VALIDATION_2026-07-30.md)。

## 构建绿色版

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  .\build\Build-Portable.ps1
```

输出：

```text
artifacts\portable\CompatBridge.exe
artifacts\portable\使用说明.txt
artifacts\portable\SHA256.txt
artifacts\CompatBridge-portable-v0.3.5-preview.zip
```

## 品牌资源

- `assets/compatbridge-logo-final.png`：透明背景主 Logo；
- `assets/compatbridge-logo.svg`：严格左右镜像的矢量源文件；
- `assets/CompatBridge.ico`：Windows 多尺寸应用图标；
- `assets/compatbridge-icon-sizes.png`：小尺寸辨识度预览；
- `assets/LOGO_PROMPT.md`：生成提示词和生产处理记录。
- `build/Build-Logo.ps1`：从精确几何坐标生成 PNG 和多尺寸 ICO。

## 许可证

本项目使用 [MIT License](LICENSE)。

## 官方依据

- [Configure Internet Explorer integration](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-browser-policies/internetexplorerintegrationlevel)
- [Configure the Enterprise Mode Site List](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-browser-policies/internetexplorerintegrationsitelist)
- [Enterprise Mode schema v.2 guidance](https://learn.microsoft.com/en-us/previous-versions/windows/internet-explorer/ie-it-pro/internet-explorer-11/enterprise-mode/enterprise-mode-schema-version-2-guidance)
- [Local site list for IE mode](https://learn.microsoft.com/en-us/deployedge/edge-ie-mode-local-site-list)
