using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CompatBridge.Core
{
    internal sealed class TransactionService
    {
        private readonly RuntimePaths _paths;
        private readonly SiteListService _siteLists;
        private readonly RegistryPolicyService _policies;
        private readonly VersionSequenceService _versions;

        public TransactionService()
            : this(AppInfo.DefaultDataRoot)
        {
        }

        public TransactionService(string dataRoot)
        {
            _paths = new RuntimePaths(dataRoot);
            _siteLists = new SiteListService();
            _policies = new RegistryPolicyService();
            _versions = new VersionSequenceService(_paths, _siteLists);
        }

        public RuntimePaths Paths
        {
            get { return _paths; }
        }

        public EnvironmentStatus GetEnvironmentStatus()
        {
            return _policies.GetEnvironmentStatus(_paths);
        }

        public List<ManagedSite> GetSites()
        {
            if (!File.Exists(_paths.Xml))
            {
                return new List<ManagedSite>();
            }
            return _siteLists.Load(_paths.Xml).Sites;
        }

        public AppState GetState()
        {
            return LoadState();
        }

        public List<PreviewItem> PreviewAdd(
            IEnumerable<string> input,
            string compatMode,
            bool allowRedirect)
        {
            return InputNormalizer.PreviewAdd(
                input,
                GetSites(),
                compatMode,
                allowRedirect);
        }

        public List<PreviewItem> PreviewRemove(IEnumerable<string> input)
        {
            return InputNormalizer.PreviewRemove(input, GetSites());
        }

        public OperationResult AddSites(
            IEnumerable<string> input,
            string compatMode,
            bool allowRedirect)
        {
            using (FileStream operationLock = EnterOperationLock())
            {
                List<ManagedSite> existing = GetSites();
                List<PreviewItem> preview = InputNormalizer.PreviewAdd(
                    input,
                    existing,
                    compatMode,
                    allowRedirect);
                ThrowForInvalid(preview);
                List<PreviewItem> conflicts = preview.Where(
                    delegate(PreviewItem item)
                    {
                        return item.Classification ==
                            PreviewClassification.ConflictSettings;
                    }).ToList();
                if (conflicts.Count > 0)
                {
                    throw new InvalidOperationException(
                        "现有站点使用不同的兼容设置，程序不会静默覆盖：" +
                        string.Join(
                            "；",
                            conflicts.Select(
                                delegate(PreviewItem item) { return item.Url; }).ToArray()));
                }

                List<ManagedSite> additions = preview.Where(
                    delegate(PreviewItem item)
                    {
                        return item.Classification == PreviewClassification.Ready;
                    }).Select(
                    delegate(PreviewItem item)
                    {
                        return new ManagedSite
                        {
                            Url = item.Url,
                            CompatMode = compatMode,
                            AllowRedirect = allowRedirect
                        };
                    }).ToList();
                if (additions.Count == 0)
                {
                    AppState existingState = LoadState();
                    if (existingState != null &&
                        preview.Any(
                            delegate(PreviewItem item)
                            {
                                return item.Classification ==
                                    PreviewClassification.AlreadyExists;
                            }))
                    {
                        return ExecuteMutation(
                            "Refresh",
                            existing,
                            _versions.ReserveNext(existingState));
                    }
                    return new OperationResult
                    {
                        Changed = false,
                        Operation = "Add",
                        Message = "没有可添加的新站点。"
                    };
                }

                AppState state = LoadState();
                int nextVersion = _versions.ReserveNext(state);
                List<ManagedSite> combined = new List<ManagedSite>(existing);
                combined.AddRange(additions);
                return ExecuteMutation("Add", combined, nextVersion);
            }
        }

        public OperationResult RemoveSites(IEnumerable<string> input)
        {
            using (FileStream operationLock = EnterOperationLock())
            {
                List<ManagedSite> existing = GetSites();
                List<PreviewItem> preview =
                    InputNormalizer.PreviewRemove(input, existing);
                ThrowForInvalid(preview);
                HashSet<string> requested = new HashSet<string>(
                    preview.Where(
                        delegate(PreviewItem item)
                        {
                            return item.Classification ==
                                PreviewClassification.Matched;
                        }).Select(
                        delegate(PreviewItem item) { return item.Url; }),
                    StringComparer.OrdinalIgnoreCase);
                List<string> notFound = preview.Where(
                    delegate(PreviewItem item)
                    {
                        return item.Classification ==
                            PreviewClassification.NotFound;
                    }).Select(
                    delegate(PreviewItem item) { return item.Url; }).ToList();

                List<ManagedSite> matched = existing.Where(
                    delegate(ManagedSite site)
                    {
                        return requested.Contains(site.Url);
                    }).ToList();
                if (matched.Count == 0)
                {
                    return new OperationResult
                    {
                        Changed = false,
                        Operation = "Remove",
                        Matched = new List<string>(),
                        NotFound = notFound,
                        Message = "没有匹配到可删除的站点。"
                    };
                }

                AppState state = LoadState();
                if (state == null)
                {
                    throw new InvalidOperationException(
                        "缺少 CompatBridge 状态文件，拒绝修改无法证明归属的站点列表。");
                }
                int nextVersion = _versions.ReserveNext(state);
                List<ManagedSite> remaining = existing.Where(
                    delegate(ManagedSite site)
                    {
                        return !requested.Contains(site.Url);
                    }).ToList();
                OperationResult result = remaining.Count == 0
                    ? ExecuteLastRemoval(state, nextVersion)
                    : ExecuteMutation("Remove", remaining, nextVersion);
                result.Matched = matched.Select(
                    delegate(ManagedSite site) { return site.Url; }).ToList();
                result.NotFound = notFound;
                return result;
            }
        }

        private OperationResult ExecuteLastRemoval(
            AppState state,
            int version)
        {
            _policies.AssertMutationPreflight(_paths, state, _siteLists);
            string manifestPath = CreateBackup("Remove");
            string baselinePath =
                AssertBackupPath(state.BaselineManifest);
            string publishedPath = _paths.GetPublishedXml(version);
            try
            {
                state.PendingTransaction = manifestPath;
                state.PendingSiteListValue = null;
                state.Phase = "Applying";
                state.UpdatedAtUtc = UtcNow();
                JsonFileStore.WriteAtomic(_paths.State, state);

                _siteLists.SaveAtomic(
                    _paths.Xml,
                    version,
                    new ManagedSite[0]);
                JsonFileStore.CopyAtomic(_paths.Xml, publishedPath);

                RestoreBackup(baselinePath);
                AppendLog(
                    "Remove",
                    "Success",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Version={0}; SiteCount=0; Deactivated=True; Backup={1}",
                        version,
                        manifestPath));
            }
            catch (Exception operationError)
            {
                try
                {
                    RestoreBackup(manifestPath);
                    AppendLog(
                        "Remove",
                        "RolledBack",
                        "Error=" + operationError.Message +
                        "; Backup=" + manifestPath);
                }
                catch (Exception rollbackError)
                {
                    throw new InvalidOperationException(
                        string.Format(
                            CultureInfo.CurrentCulture,
                            "删除最后一个网址失败，且自动回滚也失败。原始错误：{0}；回滚错误：{1}；备份：{2}",
                            operationError.Message,
                            rollbackError.Message,
                            manifestPath),
                        rollbackError);
                }
                throw new InvalidOperationException(
                    "删除失败，已自动回滚：" + operationError.Message,
                    operationError);
            }

            return new OperationResult
            {
                Changed = true,
                Operation = "Remove",
                Version = version,
                SiteCount = 0,
                BackupManifest = manifestPath,
                RequiresEdgeRestart = true,
                Message = "已删除最后一个网址，并停用 CompatBridge IE 模式策略。"
            };
        }

        public OperationResult UndoLastChange()
        {
            using (FileStream operationLock = EnterOperationLock())
            {
                AppState state = LoadState();
                if (state == null ||
                    string.IsNullOrWhiteSpace(state.LastTransaction))
                {
                    throw new InvalidOperationException(
                        "没有可撤销的 CompatBridge 变更。");
                }
                _policies.AssertMutationPreflight(_paths, state, _siteLists);
                string target = AssertBackupPath(state.LastTransaction);
                BackupManifest manifest =
                    JsonFileStore.Read<BackupManifest>(target);
                ValidateManifest(manifest);
                List<ManagedSite> restoredSites = new List<ManagedSite>();
                if (manifest.XmlExists)
                {
                    string backupXml = Path.Combine(
                        Path.GetDirectoryName(target),
                        "sites.xml");
                    restoredSites = _siteLists.Load(backupXml).Sites;
                }

                OperationResult result = ExecuteMutation(
                    "Undo",
                    restoredSites,
                    _versions.ReserveNext(state));
                result.Message = "已恢复上次变更前的站点内容。";
                return result;
            }
        }

        public OperationResult RestoreBaseline()
        {
            using (FileStream operationLock = EnterOperationLock())
            {
                AppState state = LoadState();
                if (state == null ||
                    string.IsNullOrWhiteSpace(state.BaselineManifest))
                {
                    throw new InvalidOperationException(
                        "没有可恢复的 CompatBridge 初始状态。");
                }
                _policies.AssertMutationPreflight(_paths, state, _siteLists);
                string target = AssertBackupPath(state.BaselineManifest);
                string safety = CreateBackup("RestoreBaselineSafety");
                try
                {
                    RestoreBackup(target);
                }
                catch (Exception restoreError)
                {
                    try
                    {
                        RestoreBackup(safety);
                    }
                    catch (Exception safetyError)
                    {
                        throw new InvalidOperationException(
                            string.Format(
                                CultureInfo.CurrentCulture,
                                "恢复初始状态失败，且无法恢复操作前状态。恢复错误：{0}；二次恢复错误：{1}；安全备份：{2}",
                                restoreError.Message,
                                safetyError.Message,
                                safety),
                            safetyError);
                    }
                    throw new InvalidOperationException(
                        "恢复初始状态失败，已恢复到操作前状态：" +
                        restoreError.Message,
                        restoreError);
                }

                AppendLog(
                    "RestoreBaseline",
                    "Success",
                    "Restored=" + target + "; Safety=" + safety);
                return new OperationResult
                {
                    Changed = true,
                    Operation = "RestoreBaseline",
                    BackupManifest = target,
                    RequiresEdgeRestart = true
                };
            }
        }

        public OperationResult RecoverInterruptedTransaction()
        {
            using (FileStream operationLock = EnterOperationLock())
            {
                AppState state = LoadState();
                if (state == null ||
                    !string.Equals(state.Phase, "Applying", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(state.PendingTransaction))
                {
                    throw new InvalidOperationException(
                        "没有可自动恢复的中断事务。");
                }
                if (state.SchemaVersion != 1 ||
                    !string.Equals(
                        state.DataRoot,
                        _paths.Root,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "中断事务的状态文件版本或数据目录不匹配。");
                }
                string expectedPolicy =
                    string.IsNullOrWhiteSpace(state.PendingSiteListValue)
                        ? state.ManagedSiteListValue
                        : state.PendingSiteListValue;
                if (!_policies.IsManagedSiteListPolicyValue(
                    _paths,
                    expectedPolicy))
                {
                    throw new InvalidOperationException(
                        "中断事务记录的站点列表路径与当前目录不一致。");
                }

                string pending = AssertBackupPath(state.PendingTransaction);
                BackupManifest manifest =
                    JsonFileStore.Read<BackupManifest>(pending);
                ValidateManifest(manifest);

                RegistryValueSnapshot currentLevel = _policies.GetSnapshot(
                    RegistryHive.LocalMachine,
                    RegistryPolicyService.EdgePolicySubKey,
                    RegistryPolicyService.IntegrationLevelName);
                RegistryValueSnapshot currentList = _policies.GetSnapshot(
                    RegistryHive.LocalMachine,
                    RegistryPolicyService.EdgePolicySubKey,
                    RegistryPolicyService.SiteListName);
                RegistryValueSnapshot intendedLevel = new RegistryValueSnapshot
                {
                    Exists = true,
                    Kind = (int)RegistryValueKind.DWord,
                    DwordValue = 1
                };
                RegistryValueSnapshot intendedList = new RegistryValueSnapshot
                {
                    Exists = true,
                    Kind = (int)RegistryValueKind.String,
                    StringValue = expectedPolicy
                };
                bool levelExpected =
                    RegistryPolicyService.SnapshotEquals(
                        currentLevel,
                        manifest.Registry.InternetExplorerIntegrationLevel) ||
                    RegistryPolicyService.SnapshotEquals(
                        currentLevel,
                        intendedLevel);
                bool listExpected =
                    RegistryPolicyService.SnapshotEquals(
                        currentList,
                        manifest.Registry.InternetExplorerIntegrationSiteList) ||
                    RegistryPolicyService.SnapshotEquals(
                        currentList,
                        intendedList);
                if (!levelExpected || !listExpected)
                {
                    throw new InvalidOperationException(
                        "中断后策略又被外部修改；为避免覆盖组织策略，拒绝自动恢复。");
                }

                string safety = CreateBackup("InterruptedRecoverySafety");
                try
                {
                    RestoreBackup(pending);
                }
                catch (Exception restoreError)
                {
                    try
                    {
                        RestoreBackup(safety);
                    }
                    catch (Exception safetyError)
                    {
                        throw new InvalidOperationException(
                            string.Format(
                                CultureInfo.CurrentCulture,
                                "中断恢复失败，且无法恢复操作前状态。恢复错误：{0}；二次恢复错误：{1}；安全备份：{2}",
                                restoreError.Message,
                                safetyError.Message,
                                safety),
                            safetyError);
                    }
                    throw new InvalidOperationException(
                        "中断恢复失败，已恢复到操作前状态：" +
                        restoreError.Message,
                        restoreError);
                }

                AppendLog(
                    "RecoverInterrupted",
                    "Success",
                    "Restored=" + pending + "; Safety=" + safety);
                return new OperationResult
                {
                    Changed = true,
                    Operation = "RecoverInterrupted",
                    BackupManifest = pending,
                    RequiresEdgeRestart = true
                };
            }
        }

        private OperationResult ExecuteMutation(
            string operation,
            List<ManagedSite> sites,
            int version)
        {
            AppState state = LoadState();
            _policies.AssertMutationPreflight(_paths, state, _siteLists);
            string manifestPath = CreateBackup(operation);
            string publishedPath = _paths.GetPublishedXml(version);
            string publishedPolicy =
                RegistryPolicyService.GetSiteListPolicyValue(publishedPath);
            if (state == null)
            {
                state = new AppState
                {
                    SchemaVersion = 1,
                    ToolVersion = AppInfo.Version,
                    InstallId = Guid.NewGuid().ToString(),
                    Phase = "Preparing",
                    DataRoot = _paths.Root,
                    ManagedSiteListValue = publishedPolicy,
                    CurrentVersion = 0,
                    XmlSha256 = null,
                    BaselineManifest = manifestPath,
                    LastTransaction = null,
                    PendingTransaction = manifestPath,
                    UpdatedAtUtc = UtcNow()
                };
            }

            try
            {
                state.PendingTransaction = manifestPath;
                state.PendingSiteListValue = publishedPolicy;
                state.Phase = "Applying";
                state.UpdatedAtUtc = UtcNow();
                JsonFileStore.WriteAtomic(_paths.State, state);

                _siteLists.SaveAtomic(_paths.Xml, version, sites);
                JsonFileStore.CopyAtomic(_paths.Xml, publishedPath);
                _policies.SetManagedPolicies(publishedPolicy);
                _policies.VerifyManagedPolicies(publishedPolicy);

                state.Phase = "Active";
                state.ToolVersion = AppInfo.Version;
                state.ManagedSiteListValue = publishedPolicy;
                state.CurrentVersion = version;
                state.XmlSha256 = JsonFileStore.Sha256(_paths.Xml);
                state.LastTransaction = manifestPath;
                state.PendingTransaction = null;
                state.PendingSiteListValue = null;
                state.UpdatedAtUtc = UtcNow();
                JsonFileStore.WriteAtomic(_paths.State, state);
                AppendLog(
                    operation,
                    "Success",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Version={0}; SiteCount={1}; Backup={2}",
                        version,
                        sites.Count,
                        manifestPath));
            }
            catch (Exception operationError)
            {
                try
                {
                    RestoreBackup(manifestPath);
                    AppendLog(
                        operation,
                        "RolledBack",
                        "Error=" + operationError.Message +
                        "; Backup=" + manifestPath);
                }
                catch (Exception rollbackError)
                {
                    throw new InvalidOperationException(
                        string.Format(
                            CultureInfo.CurrentCulture,
                            "操作失败，且自动回滚也失败。原始错误：{0}；回滚错误：{1}；备份：{2}",
                            operationError.Message,
                            rollbackError.Message,
                            manifestPath),
                        rollbackError);
                }
                throw new InvalidOperationException(
                    "操作失败，已自动回滚：" + operationError.Message,
                    operationError);
            }

            return new OperationResult
            {
                Changed = true,
                Operation = operation,
                Version = version,
                SiteCount = sites.Count,
                BackupManifest = manifestPath,
                RequiresEdgeRestart = true
            };
        }

        private FileStream EnterOperationLock()
        {
            EnsureDirectories();
            try
            {
                return File.Open(
                    _paths.Lock,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch (IOException error)
            {
                throw new InvalidOperationException(
                    "另一个 CompatBridge 操作正在进行，请稍后重试。",
                    error);
            }
        }

        private void EnsureDirectories()
        {
            Directory.CreateDirectory(_paths.Root);
            Directory.CreateDirectory(_paths.Backups);
            Directory.CreateDirectory(_paths.Logs);
            Directory.CreateDirectory(_paths.Published);
        }

        private AppState LoadState()
        {
            if (!File.Exists(_paths.State))
            {
                return null;
            }
            try
            {
                return JsonFileStore.Read<AppState>(_paths.State);
            }
            catch (Exception error)
            {
                throw new InvalidDataException(
                    "无法读取状态文件 " + _paths.State + "：" + error.Message,
                    error);
            }
        }

        private string CreateBackup(string operation)
        {
            EnsureDirectories();
            RegistryValueSnapshot level = _policies.GetSnapshot(
                RegistryHive.LocalMachine,
                RegistryPolicyService.EdgePolicySubKey,
                RegistryPolicyService.IntegrationLevelName);
            RegistryValueSnapshot list = _policies.GetSnapshot(
                RegistryHive.LocalMachine,
                RegistryPolicyService.EdgePolicySubKey,
                RegistryPolicyService.SiteListName);

            string id =
                DateTime.UtcNow.ToString(
                    "yyyyMMddTHHmmssfffffffZ",
                    CultureInfo.InvariantCulture) +
                "-" +
                Guid.NewGuid().ToString("N");
            string directory = Path.Combine(_paths.Backups, id);
            Directory.CreateDirectory(directory);
            bool xmlExists = File.Exists(_paths.Xml);
            bool stateExists = File.Exists(_paths.State);
            if (xmlExists)
            {
                File.Copy(
                    _paths.Xml,
                    Path.Combine(directory, "sites.xml"),
                    true);
            }
            if (stateExists)
            {
                File.Copy(
                    _paths.State,
                    Path.Combine(directory, "state.json"),
                    true);
            }

            BackupManifest manifest = new BackupManifest
            {
                SchemaVersion = 1,
                Id = id,
                CreatedAtUtc = UtcNow(),
                Operation = operation,
                XmlExists = xmlExists,
                StateExists = stateExists,
                Registry = new RegistryBackup
                {
                    InternetExplorerIntegrationLevel = level,
                    InternetExplorerIntegrationSiteList = list
                }
            };
            string manifestPath = Path.Combine(directory, "manifest.json");
            JsonFileStore.WriteAtomic(manifestPath, manifest);
            return manifestPath;
        }

        private string AssertBackupPath(string manifestPath)
        {
            string fullManifest = Path.GetFullPath(manifestPath);
            string fullRoot = Path.GetFullPath(_paths.Backups)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            if (!fullManifest.StartsWith(
                fullRoot,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "状态文件引用了数据目录之外的备份，已拒绝恢复。");
            }
            if (!File.Exists(fullManifest))
            {
                throw new FileNotFoundException(
                    "找不到备份清单。",
                    fullManifest);
            }
            return fullManifest;
        }

        private void RestoreBackup(string manifestPath)
        {
            string safePath = AssertBackupPath(manifestPath);
            BackupManifest manifest =
                JsonFileStore.Read<BackupManifest>(safePath);
            ValidateManifest(manifest);
            string directory = Path.GetDirectoryName(safePath);

            if (manifest.XmlExists)
            {
                string xmlBackup = Path.Combine(directory, "sites.xml");
                if (!File.Exists(xmlBackup))
                {
                    throw new InvalidDataException(
                        "备份清单声明存在 XML，但备份文件缺失。");
                }
                _siteLists.Load(xmlBackup);
                JsonFileStore.CopyAtomic(xmlBackup, _paths.Xml);
            }
            else if (File.Exists(_paths.Xml))
            {
                File.Delete(_paths.Xml);
            }

            _policies.Restore(
                RegistryPolicyService.IntegrationLevelName,
                manifest.Registry.InternetExplorerIntegrationLevel);
            _policies.Restore(
                RegistryPolicyService.SiteListName,
                manifest.Registry.InternetExplorerIntegrationSiteList);

            if (manifest.StateExists)
            {
                string stateBackup = Path.Combine(directory, "state.json");
                if (!File.Exists(stateBackup))
                {
                    throw new InvalidDataException(
                        "备份清单声明存在状态文件，但备份文件缺失。");
                }
                JsonFileStore.Read<AppState>(stateBackup);
                JsonFileStore.CopyAtomic(stateBackup, _paths.State);
            }
            else if (File.Exists(_paths.State))
            {
                File.Delete(_paths.State);
            }
        }

        private static void ValidateManifest(BackupManifest manifest)
        {
            if (manifest == null ||
                manifest.SchemaVersion != 1 ||
                manifest.Registry == null ||
                manifest.Registry.InternetExplorerIntegrationLevel == null ||
                manifest.Registry.InternetExplorerIntegrationSiteList == null)
            {
                throw new InvalidDataException(
                    "备份清单版本无效或缺少注册表快照。");
            }
        }

        private void AppendLog(
            string operation,
            string result,
            string details)
        {
            EnsureDirectories();
            OperationLogRecord record = new OperationLogRecord
            {
                TimestampUtc = UtcNow(),
                Operation = operation,
                Result = result,
                Details = details
            };
            File.AppendAllText(
                _paths.Log,
                JsonFileStore.Serialize(record) + Environment.NewLine,
                new UTF8Encoding(false));
        }

        private static void ThrowForInvalid(List<PreviewItem> preview)
        {
            List<PreviewItem> invalid = preview.Where(
                delegate(PreviewItem item)
                {
                    return item.Classification == PreviewClassification.Invalid;
                }).ToList();
            if (invalid.Count == 0)
            {
                return;
            }
            throw new InvalidOperationException(
                "存在非法输入，未做任何修改：" +
                string.Join(
                    "；",
                    invalid.Select(
                        delegate(PreviewItem item)
                        {
                            return item.Raw + "（" + item.Error + "）";
                        }).ToArray()));
        }

        private static string UtcNow()
        {
            return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }
    }
}
