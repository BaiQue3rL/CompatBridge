CompatBridge（兼容桥）绿色版 0.3.4-preview
============================================================

用途：
  批量管理 Microsoft Edge IE 模式站点。

运行：
  1. 将 CompatBridge.exe 复制到任意本地目录。
  2. 双击运行。
  3. 接受 Windows 管理员权限提示。
  4. 输入网址并点击“添加”。
  5. 如需批量添加，点击“批量添加”：
     - 可粘贴多行网址或从 Excel 复制一列；
     - 也可导入 TXT 或 CSV 文件（CSV 读取第一列）；
     - 预览会区分可添加、已存在、重复、格式错误和冲突项；
     - 点击“添加有效网址”只会写入有效的新网址。
  6. 如果 Edge 正在运行，按提示保存网页内容并重新启动 Edge。
  7. 删除时在列表中找到网址，点击该行的“删除”。
     删除最后一个网址时，CompatBridge 会同时停用自己创建的 IE 模式策略。

系统要求：
  - Windows 10/11 或相应 Windows Server；
  - 支持 IE 模式的系统级 Microsoft Edge；
  - .NET Framework 4.8；
  - 本地管理员权限；
  - 不存在冲突的 GPO、Intune、M365 Cloud Site List 或旧 IE 策略。

运行数据：
  C:\ProgramData\CompatBridge
    sites.xml
    state.json
    published\
    backups\
    logs\

安全说明：
  - 不修改 Edge 用户数据。
  - 不模拟浏览器点击。
  - 不自动结束或强杀 Edge。
  - 每次策略变更前备份 XML、状态和注册表值。
  - 每次添加、删除和撤销都使用更高的列表版本和新的发布地址。
  - 发现外部策略或完整性异常时停止，不静默覆盖。

提示：
  此预览版尚未进行商业代码签名。从网络下载时，Windows SmartScreen
  可能显示“未知发布者”。请只从可信来源获取，并核对 SHA256.txt。
