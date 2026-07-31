# CompatBridge 原型设计

## 安全原则

1. 先预览、后应用。
2. 任何策略修改前都保存 XML、状态和精确的注册表值快照。
3. XML 使用同目录临时文件替换，避免留下半写文件。
4. 只管理能够由 `state.json` 证明属于 CompatBridge 的策略。
5. Cloud Site List 或外部 GPO/Intune 策略优先于本地列表，检测到冲突即停止。
6. Edge 只在用户确认后重启。

## 数据目录

正式运行：

```text
C:\ProgramData\CompatBridge\
├── sites.xml
├── state.json
├── backups\
└── logs\
```

开发时请将 SDK、包缓存和临时工具集中放在仓库之外的单一开发环境目录，例如：

```text
<development-root>\CompatBridge.DevEnv\
```

## 输入规范化

- 接受 DNS 主机名、IPv4、带 `http://` 或 `https://` 的 URL；
- 保留显式端口和非根路径；
- DNS 名转为小写 ASCII（IDN 使用 Punycode）；
- 根路径 `/` 省略；
- 查询字符串和片段不进入 XML，同时返回警告；
- 拒绝通配符、用户凭据、非 HTTP(S) 协议、非法主机和非法端口；
- 当前原型明确拒绝 IPv6，待真实 Edge Site List 验证后再决定其规范格式。

## 策略归属

正式版写入 HKLM：

```text
HKLM\SOFTWARE\Policies\Microsoft\Edge
  InternetExplorerIntegrationLevel    REG_DWORD 1
  InternetExplorerIntegrationSiteList REG_SZ    <本地 sites.xml URI>
```

HKCU 同名策略、`InternetExplorerIntegrationCloudSiteList`、IE 旧策略列表以及不属于
CompatBridge 的 HKLM 站点列表都属于冲突信号。原型会只读诊断这些信号；只有
`state.json` 能证明策略归属、预检无冲突且用户显式指定 `-Apply` 时，事务应用功能
才会写入策略。
