# CompatBridge 真实机器验证记录（2026-07-30）

## 环境

- Windows：10.0.19044
- Microsoft Edge：150.0.4078.105
- 安装范围：系统级
- PowerShell：Windows PowerShell 5.1
- 权限：本地管理员
- 测试前策略冲突：无

## 已通过

1. 首次添加 `example.com`：
   - `InternetExplorerIntegrationLevel` 写入为 DWORD `1`；
   - `InternetExplorerIntegrationSiteList` 写入为
     `file:///C:/ProgramData/CompatBridge/sites.xml`；
   - XML、`state.json`、备份清单和日志均创建成功；
   - XML v1、状态 v1 和 SHA-256 一致。
2. 再添加 `example.org`：
   - XML 和状态版本递增至 v2；
   - 两个站点均存在；
   - SHA-256 校验通过。
3. 删除 `example.org`：
   - XML 和状态版本递增至 v3；
   - 仅保留 `example.com`；
   - SHA-256 校验通过。
4. 撤销删除：
   - 精确恢复 v2；
   - `example.com`、`example.org` 均恢复；
   - 撤销前安全备份创建成功。
5. 完整恢复初始状态：
   - 两个 CompatBridge 管理的 HKLM 策略值恢复为不存在；
   - `sites.xml` 和 `state.json` 恢复为不存在；
   - Edge 环境检查重新显示无阻止性冲突；
   - 备份与操作日志保留用于审计。
6. 恢复后重新运行自动化测试：21 项全部通过。

## 尚待人工确认

自动化环境无法可靠读取 Edge 的内部 `edge://` 页面：

- Windows UI 控制因不能确定内部页 URL 而停止；
- Headless `--dump-dom` 被 Edge 重定向到新标签页；
- 本地调试端口被执行环境安全策略禁止。

因此本次验证已经覆盖策略写入、XML、事务、版本、删除、撤销和恢复，但仍需人工完成：

1. 应用一个测试站点；
2. 启动 Edge；
3. 打开 `edge://policy`，确认两项策略无错误；
4. 打开 `edge://compat/enterprise`，确认列表版本和站点；
5. 访问测试站点，确认 IE 模式指示器；
6. 执行 `restore -Apply` 恢复基线。

当前机器在本次记录结束时已经恢复基线，没有保留测试站点策略。

## C# GUI 0.3 缓存与交互修复

用户使用 `www.baidu.com` 复测后发现：

- 删除站点后浏览器仍短暂使用 IE 模式；
- 撤销删除后再次添加，IE 模式不再启用。

定位结果：

1. 旧实现将已发布的 v2 列表撤销为 v1，导致 Edge 拒绝旧版本；
2. Edge 用户配置中的 `DualEngine\SiteList-Enterprise.json` 仍为空缓存；
3. Edge 110 及更高版本对同一站点列表地址的默认刷新周期可能很长，不能把
   “写完同一个 XML 文件”等同于“浏览器已经加载”。

修复与实测：

- 从状态、当前 XML、历史备份和日志恢复历史最高版本；
- 重复添加已有网址会重新发布，而不是静默返回；
- 添加、删除、撤销均只使用更高版本；
- 每个版本发布到独立文件，并同步更新策略地址；
- 真实事务链通过：
  - v10：旧状态迁移至版本化发布地址；
  - v11：删除后 XML 站点数为 0；
  - v12：再次添加后 XML 站点数为 1；
  - 删除与添加使用不同的策略地址，发布文件及哈希均有效；
- Edge 企业列表缓存随后成功读取 `www.baidu.com`；
- 主界面简化为输入网址后添加，以及在现有列表中直接删除。
