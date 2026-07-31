using CompatBridge.Core;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CompatBridge.Tests
{
    internal static class Program
    {
        private static int _passed;
        private static int _failed;

        private static int Main(string[] args)
        {
            string root = args.Length > 0
                ? Path.GetFullPath(args[0])
                : Path.Combine(
                    Path.GetTempPath(),
                    "CompatBridge.Tests." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);

            Run("规范化裸域名", delegate
            {
                NormalizedSite site = InputNormalizer.Normalize("OA.Example.COM");
                True(site.IsValid, "条目应有效");
                Equal("oa.example.com", site.Url, "域名规范化");
            });
            Run("保留端口和路径", delegate
            {
                NormalizedSite site = InputNormalizer.Normalize(
                    "https://ERP.example.com:8443/legacy/login");
                Equal(
                    "erp.example.com:8443/legacy/login",
                    site.Url,
                    "URL 规范化");
                Equal(8443, site.Port.Value, "端口");
            });
            Run("移除查询和片段", delegate
            {
                NormalizedSite site = InputNormalizer.Normalize(
                    "https://example.com/app?q=1#top");
                Equal("example.com/app", site.Url, "查询片段移除");
                Equal(2, site.Warnings.Count, "警告数");
            });
            Run("接受 IPv4", delegate
            {
                NormalizedSite site = InputNormalizer.Normalize(
                    "192.168.10.20:8080/oa");
                True(site.IsValid, "IPv4 应有效");
                Equal(
                    "192.168.10.20:8080/oa",
                    site.Url,
                    "IPv4 规范化");
            });
            Run("拒绝非法数字地址", delegate
            {
                True(
                    !InputNormalizer.Normalize("999.999.999.999").IsValid,
                    "非法 IPv4 必须拒绝");
            });
            Run("拒绝通配符和凭据", delegate
            {
                True(
                    !InputNormalizer.Normalize("*.example.com").IsValid,
                    "通配符必须拒绝");
                True(
                    !InputNormalizer.Normalize(
                        "https://user:pass@example.com").IsValid,
                    "凭据必须拒绝");
            });
            Run("识别重复和已有站点", delegate
            {
                List<PreviewItem> preview = InputNormalizer.PreviewAdd(
                    new[] { "a.example.com\na.example.com\tb.example.com" },
                    new[]
                    {
                        new ManagedSite
                        {
                            Url = "b.example.com",
                            CompatMode = "Default",
                            AllowRedirect = false
                        }
                    },
                    "Default",
                    false);
                Equal(3, preview.Count, "预览数");
                Equal(
                    PreviewClassification.Ready,
                    preview[0].Classification,
                    "第一项");
                Equal(
                    PreviewClassification.DuplicateInput,
                    preview[1].Classification,
                    "重复项");
                Equal(
                    PreviewClassification.AlreadyExists,
                    preview[2].Classification,
                    "已有项");
            });
            Run("识别兼容设置冲突", delegate
            {
                List<PreviewItem> preview = InputNormalizer.PreviewAdd(
                    new[] { "conflict.example.com" },
                    new[]
                    {
                        new ManagedSite
                        {
                            Url = "conflict.example.com",
                            CompatMode = "IE8Enterprise",
                            AllowRedirect = false
                        }
                    },
                    "Default",
                    false);
                Equal(
                    PreviewClassification.ConflictSettings,
                    preview[0].Classification,
                    "设置冲突");
            });
            Run("读取 TXT 单列并跳过表头", delegate
            {
                string path = Path.Combine(root, "sites.txt");
                File.WriteAllText(
                    path,
                    "网址\r\nOA.example.com\r\n192.168.1.10/oa\r\n");
                List<string> entries =
                    SiteInputFileReader.ReadEntries(path);
                Equal(2, entries.Count, "TXT 条目数");
                Equal("OA.example.com", entries[0], "TXT 第一项");
            });
            Run("读取 CSV 第一列和引号", delegate
            {
                string path = Path.Combine(root, "sites.csv");
                File.WriteAllText(
                    path,
                    "URL,备注\r\n\"oa.example.com/app,legacy\",\"旧系统\"\r\n" +
                    "erp.example.com,ERP\r\n");
                List<string> entries =
                    SiteInputFileReader.ReadEntries(path);
                Equal(2, entries.Count, "CSV 条目数");
                Equal(
                    "oa.example.com/app,legacy",
                    entries[0],
                    "CSV 引号字段");
                Equal("erp.example.com", entries[1], "CSV 第二项");
            });
            Run("生成并读取 Site List v2", delegate
            {
                SiteListService service = new SiteListService();
                string path = Path.Combine(root, "sites.xml");
                service.SaveAtomic(
                    path,
                    7,
                    new[]
                    {
                        new ManagedSite
                        {
                            Url = "oa.example.com",
                            CompatMode = "Default",
                            AllowRedirect = false
                        },
                        new ManagedSite
                        {
                            Url = "erp.example.com:8443/legacy",
                            CompatMode = "IE8Enterprise",
                            AllowRedirect = true
                        }
                    });
                SiteListDocument loaded = service.Load(path);
                Equal(7, loaded.Version, "XML 版本");
                Equal(2, loaded.Sites.Count, "站点数");
                Equal(
                    "IE8Enterprise",
                    loaded.Sites[1].CompatMode,
                    "兼容模式");
                True(loaded.Sites[1].AllowRedirect, "重定向属性");
            });
            Run("空列表 XML 有效", delegate
            {
                SiteListService service = new SiteListService();
                string path = Path.Combine(root, "empty-sites.xml");
                service.SaveAtomic(path, 8, new ManagedSite[0]);
                Equal(0, service.Load(path).Sites.Count, "空列表站点数");
            });
            Run("状态 JSON 往返", delegate
            {
                AppState state = new AppState
                {
                    SchemaVersion = 1,
                    ToolVersion = AppInfo.Version,
                    InstallId = Guid.NewGuid().ToString(),
                    Phase = "Active",
                    DataRoot = root,
                    ManagedSiteListValue = "file:///C:/ProgramData/CompatBridge/sites.xml",
                    CurrentVersion = 9,
                    XmlSha256 = "abc",
                    BaselineManifest = "baseline",
                    LastTransaction = "last",
                    PendingTransaction = null,
                    UpdatedAtUtc = DateTime.UtcNow.ToString("o")
                };
                string path = Path.Combine(root, "state.json");
                JsonFileStore.WriteAtomic(path, state);
                AppState loaded = JsonFileStore.Read<AppState>(path);
                Equal("Active", loaded.Phase, "状态阶段");
                Equal(9, loaded.CurrentVersion, "状态版本");
            });
            Run("本地策略使用 file URI", delegate
            {
                string value = RegistryPolicyService.GetSiteListPolicyValue(
                    Path.Combine(root, "sites.xml"));
                True(
                    value.StartsWith("file:///", StringComparison.OrdinalIgnoreCase),
                    "必须使用 file URI");
                True(
                    value.EndsWith("/sites.xml", StringComparison.OrdinalIgnoreCase),
                    "必须指向 sites.xml");
            });
            Run("仅接受 CompatBridge 自有发布目录", delegate
            {
                RuntimePaths paths = new RuntimePaths(
                    Path.Combine(root, "owned-policy"));
                RegistryPolicyService policies = new RegistryPolicyService();
                True(
                    policies.IsManagedSiteListPolicyValue(
                        paths,
                        RegistryPolicyService.GetSiteListPolicyValue(paths.Xml)),
                    "兼容旧版主列表路径");
                True(
                    policies.IsManagedSiteListPolicyValue(
                        paths,
                        RegistryPolicyService.GetSiteListPolicyValue(
                            paths.GetPublishedXml(12))),
                    "接受版本化发布路径");
                True(
                    !policies.IsManagedSiteListPolicyValue(
                        paths,
                        RegistryPolicyService.GetSiteListPolicyValue(
                            Path.Combine(root, "outside.xml"))),
                    "拒绝数据目录之外的路径");
            });
            Run("注册表快照比较", delegate
            {
                RegistryValueSnapshot left = new RegistryValueSnapshot
                {
                    Exists = true,
                    Kind = (int)RegistryValueKind.DWord,
                    DwordValue = 1
                };
                RegistryValueSnapshot right = new RegistryValueSnapshot
                {
                    Exists = true,
                    Kind = (int)RegistryValueKind.DWord,
                    DwordValue = 1
                };
                True(
                    RegistryPolicyService.SnapshotEquals(left, right),
                    "相同快照应相等");
            });
            Run("站点列表版本跨撤销和恢复保持递增", delegate
            {
                string versionRoot = Path.Combine(root, "version-sequence");
                RuntimePaths paths = new RuntimePaths(versionRoot);
                Directory.CreateDirectory(paths.Backups);
                Directory.CreateDirectory(paths.Logs);
                SiteListService siteLists = new SiteListService();
                siteLists.SaveAtomic(
                    paths.Xml,
                    4,
                    new ManagedSite[0]);
                string backupDirectory = Path.Combine(paths.Backups, "older");
                Directory.CreateDirectory(backupDirectory);
                siteLists.SaveAtomic(
                    Path.Combine(backupDirectory, "sites.xml"),
                    8,
                    new ManagedSite[0]);
                File.WriteAllText(
                    paths.Log,
                    "{\"Version\":11}\r\nVersion=10");

                VersionSequenceService versions =
                    new VersionSequenceService(paths, siteLists);
                AppState state = new AppState { CurrentVersion = 7 };
                Equal(11, versions.GetHighWater(state), "历史最高版本");
                Equal(12, versions.ReserveNext(state), "首次保留版本");

                state.CurrentVersion = 1;
                siteLists.SaveAtomic(paths.Xml, 1, new ManagedSite[0]);
                Equal(13, versions.ReserveNext(state), "恢复旧内容后的后续版本");
            });

            Console.WriteLine();
            Console.WriteLine(
                "CompatBridge C# tests: passed={0}, failed={1}",
                _passed,
                _failed);
            Console.WriteLine("Artifacts: " + root);
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action body)
        {
            try
            {
                body();
                _passed++;
                Console.WriteLine("[PASS] " + name);
            }
            catch (Exception error)
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name);
                Console.WriteLine("       " + error.Message);
            }
        }

        private static void True(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void Equal<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    string.Format(
                        "{0}。期望：[{1}]；实际：[{2}]",
                        message,
                        expected,
                        actual));
            }
        }
    }
}
