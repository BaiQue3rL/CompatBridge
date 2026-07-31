using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace CompatBridge.Core
{
    internal sealed class RegistryPolicyService
    {
        public const string EdgePolicySubKey = @"SOFTWARE\Policies\Microsoft\Edge";
        public const string LegacyIePolicySubKey =
            @"SOFTWARE\Policies\Microsoft\Internet Explorer\Main\EnterpriseMode";
        public const string IntegrationLevelName = "InternetExplorerIntegrationLevel";
        public const string SiteListName = "InternetExplorerIntegrationSiteList";
        public const string CloudSiteListName = "InternetExplorerIntegrationCloudSiteList";

        public EnvironmentStatus GetEnvironmentStatus(RuntimePaths paths)
        {
            EnvironmentStatus status = new EnvironmentStatus
            {
                IsWindows = Environment.OSVersion.Platform == PlatformID.Win32NT,
                WindowsVersion = Environment.OSVersion.Version.ToString(),
                SupportIssues = new List<string>(),
                Conflicts = new List<string>()
            };

            WindowsPrincipal principal =
                new WindowsPrincipal(WindowsIdentity.GetCurrent());
            status.IsAdministrator = principal.IsInRole(
                WindowsBuiltInRole.Administrator);

            DetectEdge(status);
            if (!status.IsWindows || Environment.OSVersion.Version.Major < 10)
            {
                status.SupportIssues.Add(
                    "需要受支持的 Windows 10、Windows 11 或相应 Windows Server。");
            }
            if (!status.EdgeInstalled)
            {
                status.SupportIssues.Add("未检测到 Microsoft Edge。");
            }
            else if (!status.EdgeSystemInstalled)
            {
                status.SupportIssues.Add(
                    "检测到的 Edge 是每用户安装；IE 模式要求系统级安装。");
            }
            else
            {
                Version version;
                if (!Version.TryParse(status.EdgeVersion, out version) ||
                    version.Major < 78)
                {
                    status.SupportIssues.Add(
                        "Microsoft Edge 版本低于 IE 模式策略要求的 78。");
                }
            }
            status.IsSupported = status.SupportIssues.Count == 0;

            RegistryValueSnapshot machineLevel =
                GetSnapshot(RegistryHive.LocalMachine, EdgePolicySubKey, IntegrationLevelName);
            RegistryValueSnapshot machineList =
                GetSnapshot(RegistryHive.LocalMachine, EdgePolicySubKey, SiteListName);
            RegistryValueSnapshot machineCloud =
                GetSnapshot(RegistryHive.LocalMachine, EdgePolicySubKey, CloudSiteListName);
            RegistryValueSnapshot userLevel =
                GetSnapshot(RegistryHive.CurrentUser, EdgePolicySubKey, IntegrationLevelName);
            RegistryValueSnapshot userList =
                GetSnapshot(RegistryHive.CurrentUser, EdgePolicySubKey, SiteListName);
            RegistryValueSnapshot userCloud =
                GetSnapshot(RegistryHive.CurrentUser, EdgePolicySubKey, CloudSiteListName);
            RegistryValueSnapshot legacyMachine =
                GetSnapshot(RegistryHive.LocalMachine, LegacyIePolicySubKey, "SiteList");
            RegistryValueSnapshot legacyUser =
                GetSnapshot(RegistryHive.CurrentUser, LegacyIePolicySubKey, "SiteList");

            bool stateExists = File.Exists(paths.State);
            AppState state = null;
            if (stateExists)
            {
                try
                {
                    state = JsonFileStore.Read<AppState>(paths.State);
                    status.StatePhase = state.Phase;
                    status.IsManaged = string.Equals(
                        state.Phase,
                        "Active",
                        StringComparison.Ordinal);
                    status.ManagedVersion = state.CurrentVersion;
                    if (string.Equals(state.Phase, "Applying", StringComparison.Ordinal))
                    {
                        status.Conflicts.Add(
                            "检测到中断的 CompatBridge 事务，请先执行“恢复中断事务”。");
                    }
                    else if (!string.Equals(
                        state.Phase,
                        "Active",
                        StringComparison.Ordinal))
                    {
                        status.Conflicts.Add(
                            "CompatBridge 状态文件不是可用的 Active 状态。");
                    }
                }
                catch
                {
                    status.Conflicts.Add(
                        "CompatBridge 状态文件无法解析，必须先人工检查。");
                }
            }

            if (machineCloud.Exists || userCloud.Exists)
            {
                status.Conflicts.Add(
                    "检测到 M365 Cloud Site List；该策略优先于本地站点列表。");
            }
            if (userLevel.Exists || userList.Exists)
            {
                status.Conflicts.Add(
                    "检测到 HKCU Edge IE 模式策略，必须先确认组织策略归属。");
            }
            if (legacyMachine.Exists || legacyUser.Exists)
            {
                status.Conflicts.Add(
                    "检测到 IE 旧版 Enterprise Mode Site List 策略，不得直接覆盖。");
            }
            if (machineLevel.Exists && !stateExists)
            {
                status.Conflicts.Add(
                    "HKLM 已配置 IE 模式集成级别，但不存在 CompatBridge 状态文件。");
            }
            if (machineList.Exists)
            {
                string value = SnapshotAsString(machineList);
                bool expectedPath =
                    state != null &&
                    string.Equals(
                        value,
                        state.ManagedSiteListValue,
                        StringComparison.Ordinal);
                if (!expectedPath)
                {
                    if (!stateExists &&
                        IsManagedSiteListPolicyValue(paths, value))
                    {
                        status.Conflicts.Add(
                            "HKLM 指向 CompatBridge 数据目录，但缺少 state.json。");
                    }
                    else
                    {
                        status.Conflicts.Add(
                            "HKLM 已配置其他 Enterprise Mode Site List，不得静默接管。");
                    }
                }
            }

            status.HasBlockingConflict = status.Conflicts.Count > 0;
            return status;
        }

        public void AssertMutationPreflight(
            RuntimePaths paths,
            AppState state,
            SiteListService siteLists)
        {
            EnvironmentStatus status = GetEnvironmentStatus(paths);
            if (!status.IsAdministrator)
            {
                throw new InvalidOperationException("修改 HKLM Edge 策略需要管理员权限。");
            }
            if (!status.IsSupported)
            {
                throw new InvalidOperationException(
                    "当前环境不支持安全应用 IE 模式策略：" +
                    string.Join("；", status.SupportIssues.ToArray()));
            }
            if (status.HasBlockingConflict)
            {
                throw new InvalidOperationException(
                    "检测到阻止性策略冲突：" +
                    string.Join("；", status.Conflicts.ToArray()));
            }

            if (state == null)
            {
                if (File.Exists(paths.Xml))
                {
                    throw new InvalidOperationException(
                        "数据目录中已存在无法证明归属的 sites.xml，不得静默接管。");
                }
                return;
            }

            if (state.SchemaVersion != 1 ||
                !string.Equals(state.Phase, "Active", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "CompatBridge 状态文件不完整或上次事务未正常完成。");
            }
            if (!IsManagedSiteListPolicyValue(
                paths,
                state.ManagedSiteListValue))
            {
                throw new InvalidOperationException(
                    "状态文件中的受管站点列表路径与当前数据目录不一致。");
            }
            if (!string.Equals(
                state.DataRoot,
                paths.Root,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "状态文件记录的数据目录与当前数据目录不一致。");
            }
            if (!File.Exists(paths.Xml))
            {
                throw new InvalidOperationException(
                    "CompatBridge 管理的 sites.xml 已丢失。");
            }
            SiteListDocument document = siteLists.Load(paths.Xml);
            if (document.Version != state.CurrentVersion)
            {
                throw new InvalidOperationException(
                    "sites.xml 版本与状态文件不一致，可能被外部修改。");
            }
            if (string.IsNullOrEmpty(state.XmlSha256) ||
                !string.Equals(
                    JsonFileStore.Sha256(paths.Xml),
                    state.XmlSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "sites.xml 完整性校验失败，可能被外部修改。");
            }
            string publishedPath =
                GetLocalPathFromPolicyValue(state.ManagedSiteListValue);
            if (!File.Exists(publishedPath) ||
                !string.Equals(
                    JsonFileStore.Sha256(publishedPath),
                    state.XmlSha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Edge 当前使用的已发布站点列表缺失或完整性校验失败。");
            }

            RegistryValueSnapshot level =
                GetSnapshot(RegistryHive.LocalMachine, EdgePolicySubKey, IntegrationLevelName);
            RegistryValueSnapshot list =
                GetSnapshot(RegistryHive.LocalMachine, EdgePolicySubKey, SiteListName);
            if (!level.Exists ||
                level.Kind != (int)RegistryValueKind.DWord ||
                level.DwordValue != 1)
            {
                throw new InvalidOperationException(
                    "CompatBridge 管理的 IE 模式集成策略已被外部修改或删除。");
            }
            if (!list.Exists ||
                list.Kind != (int)RegistryValueKind.String ||
                !string.Equals(
                    list.StringValue,
                    state.ManagedSiteListValue,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "CompatBridge 管理的站点列表策略已被外部修改或删除。");
            }
        }

        public RegistryValueSnapshot GetSnapshot(
            RegistryHive hive,
            string subKey,
            string name)
        {
            using (RegistryKey baseKey =
                RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
            using (RegistryKey key = baseKey.OpenSubKey(subKey, false))
            {
                if (key == null || Array.IndexOf(key.GetValueNames(), name) < 0)
                {
                    return new RegistryValueSnapshot { Exists = false };
                }

                RegistryValueKind kind = key.GetValueKind(name);
                object value = key.GetValue(
                    name,
                    null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames);
                RegistryValueSnapshot snapshot = new RegistryValueSnapshot
                {
                    Exists = true,
                    Kind = (int)kind
                };
                switch (kind)
                {
                    case RegistryValueKind.DWord:
                        snapshot.DwordValue = Convert.ToInt32(value);
                        break;
                    case RegistryValueKind.QWord:
                        snapshot.QwordValue = Convert.ToInt64(value);
                        break;
                    case RegistryValueKind.Binary:
                        snapshot.BinaryBase64 = Convert.ToBase64String((byte[])value);
                        break;
                    case RegistryValueKind.MultiString:
                        snapshot.MultiStringValue = (string[])value;
                        break;
                    default:
                        snapshot.StringValue = Convert.ToString(value);
                        break;
                }
                return snapshot;
            }
        }

        public void SetManagedPolicies(string siteListValue)
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                RegistryView.Registry64))
            using (RegistryKey key = baseKey.CreateSubKey(EdgePolicySubKey))
            {
                key.SetValue(IntegrationLevelName, 1, RegistryValueKind.DWord);
                key.SetValue(SiteListName, siteListValue, RegistryValueKind.String);
            }
        }

        public void VerifyManagedPolicies(string siteListValue)
        {
            RegistryValueSnapshot level =
                GetSnapshot(RegistryHive.LocalMachine, EdgePolicySubKey, IntegrationLevelName);
            RegistryValueSnapshot list =
                GetSnapshot(RegistryHive.LocalMachine, EdgePolicySubKey, SiteListName);
            if (!level.Exists ||
                level.Kind != (int)RegistryValueKind.DWord ||
                level.DwordValue != 1 ||
                !list.Exists ||
                list.Kind != (int)RegistryValueKind.String ||
                !string.Equals(
                    list.StringValue,
                    siteListValue,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("策略写入后的回读验证失败。");
            }
        }

        public void Restore(
            string name,
            RegistryValueSnapshot snapshot)
        {
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                RegistryView.Registry64))
            using (RegistryKey key = baseKey.CreateSubKey(EdgePolicySubKey))
            {
                if (!snapshot.Exists)
                {
                    key.DeleteValue(name, false);
                    return;
                }

                RegistryValueKind kind = (RegistryValueKind)snapshot.Kind;
                object value;
                switch (kind)
                {
                    case RegistryValueKind.DWord:
                        value = snapshot.DwordValue;
                        break;
                    case RegistryValueKind.QWord:
                        value = snapshot.QwordValue;
                        break;
                    case RegistryValueKind.Binary:
                        value = Convert.FromBase64String(snapshot.BinaryBase64);
                        break;
                    case RegistryValueKind.MultiString:
                        value = snapshot.MultiStringValue;
                        break;
                    default:
                        value = snapshot.StringValue;
                        break;
                }
                key.SetValue(name, value, kind);
            }
        }

        public static string GetSiteListPolicyValue(string xmlPath)
        {
            return new Uri(Path.GetFullPath(xmlPath)).AbsoluteUri;
        }

        public bool IsManagedSiteListPolicyValue(
            RuntimePaths paths,
            string value)
        {
            string localPath;
            try
            {
                localPath = GetLocalPathFromPolicyValue(value);
            }
            catch
            {
                return false;
            }

            string canonical = Path.GetFullPath(paths.Xml);
            if (string.Equals(
                localPath,
                canonical,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            string publishedRoot =
                Path.GetFullPath(paths.Published)
                    .TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            return localPath.StartsWith(
                publishedRoot,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string GetLocalPathFromPolicyValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidDataException("站点列表策略值为空。");
            }
            Uri uri;
            if (Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                uri.IsFile)
            {
                return Path.GetFullPath(uri.LocalPath);
            }
            return Path.GetFullPath(value);
        }

        public static bool SnapshotEquals(
            RegistryValueSnapshot left,
            RegistryValueSnapshot right)
        {
            if (left.Exists != right.Exists)
            {
                return false;
            }
            if (!left.Exists)
            {
                return true;
            }
            if (left.Kind != right.Kind)
            {
                return false;
            }
            RegistryValueKind kind = (RegistryValueKind)left.Kind;
            switch (kind)
            {
                case RegistryValueKind.DWord:
                    return left.DwordValue == right.DwordValue;
                case RegistryValueKind.QWord:
                    return left.QwordValue == right.QwordValue;
                case RegistryValueKind.Binary:
                    return string.Equals(
                        left.BinaryBase64,
                        right.BinaryBase64,
                        StringComparison.Ordinal);
                case RegistryValueKind.MultiString:
                    return ArraysEqual(left.MultiStringValue, right.MultiStringValue);
                default:
                    return string.Equals(
                        left.StringValue,
                        right.StringValue,
                        StringComparison.Ordinal);
            }
        }

        private static bool ArraysEqual(string[] left, string[] right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }
            if (left == null || right == null || left.Length != right.Length)
            {
                return false;
            }
            for (int i = 0; i < left.Length; i++)
            {
                if (!string.Equals(left[i], right[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static string SnapshotAsString(RegistryValueSnapshot snapshot)
        {
            return snapshot.StringValue ?? string.Empty;
        }

        private static void DetectEdge(EnvironmentStatus status)
        {
            List<Tuple<string, bool>> candidates = new List<Tuple<string, bool>>();
            string programFilesX86 = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFilesX86);
            string programFiles = Environment.GetFolderPath(
                Environment.SpecialFolder.ProgramFiles);
            string localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(programFilesX86))
            {
                candidates.Add(Tuple.Create(
                    Path.Combine(
                        programFilesX86,
                        @"Microsoft\Edge\Application\msedge.exe"),
                    true));
            }
            if (!string.IsNullOrEmpty(programFiles))
            {
                candidates.Add(Tuple.Create(
                    Path.Combine(
                        programFiles,
                        @"Microsoft\Edge\Application\msedge.exe"),
                    true));
            }
            if (!string.IsNullOrEmpty(localAppData))
            {
                candidates.Add(Tuple.Create(
                    Path.Combine(
                        localAppData,
                        @"Microsoft\Edge\Application\msedge.exe"),
                    false));
            }

            foreach (Tuple<string, bool> candidate in candidates)
            {
                if (!File.Exists(candidate.Item1))
                {
                    continue;
                }
                status.EdgeInstalled = true;
                status.EdgeSystemInstalled = candidate.Item2;
                status.EdgePath = candidate.Item1;
                status.EdgeVersion =
                    FileVersionInfo.GetVersionInfo(candidate.Item1).ProductVersion;
                return;
            }
        }
    }
}
