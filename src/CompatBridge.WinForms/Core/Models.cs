using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CompatBridge.Core
{
    internal static class AppInfo
    {
        public const string ProductName = "CompatBridge";
        public const string ChineseName = "兼容桥";
        public const string Version = "0.3.5-preview";
        public const string DefaultDataRoot = @"C:\ProgramData\CompatBridge";
    }

    internal enum PreviewClassification
    {
        Ready,
        Invalid,
        DuplicateInput,
        AlreadyExists,
        ConflictSettings,
        Matched,
        NotFound
    }

    internal sealed class NormalizedSite
    {
        public string Raw { get; set; }
        public bool IsValid { get; set; }
        public string Url { get; set; }
        public string Host { get; set; }
        public int? Port { get; set; }
        public string Path { get; set; }
        public List<string> Warnings { get; set; }
        public string Error { get; set; }
    }

    [DataContract]
    internal sealed class ManagedSite
    {
        [DataMember(Order = 1)]
        public string Url { get; set; }

        [DataMember(Order = 2)]
        public string CompatMode { get; set; }

        [DataMember(Order = 3)]
        public bool AllowRedirect { get; set; }
    }

    internal sealed class PreviewItem
    {
        public string Raw { get; set; }
        public string Url { get; set; }
        public PreviewClassification Classification { get; set; }
        public List<string> Warnings { get; set; }
        public string Error { get; set; }

        public string ClassificationText
        {
            get
            {
                switch (Classification)
                {
                    case PreviewClassification.Ready: return "可添加";
                    case PreviewClassification.Invalid: return "非法";
                    case PreviewClassification.DuplicateInput: return "输入重复";
                    case PreviewClassification.AlreadyExists: return "已存在";
                    case PreviewClassification.ConflictSettings: return "设置冲突";
                    case PreviewClassification.Matched: return "已匹配";
                    case PreviewClassification.NotFound: return "未找到";
                    default: return Classification.ToString();
                }
            }
        }

        public string WarningText
        {
            get { return Warnings == null ? string.Empty : string.Join("；", Warnings.ToArray()); }
        }
    }

    internal sealed class SiteListDocument
    {
        public int Version { get; set; }
        public List<ManagedSite> Sites { get; set; }
    }

    [DataContract]
    internal sealed class AppState
    {
        [DataMember(Order = 1)]
        public int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        public string ToolVersion { get; set; }

        [DataMember(Order = 3)]
        public string InstallId { get; set; }

        [DataMember(Order = 4)]
        public string Phase { get; set; }

        [DataMember(Order = 5)]
        public string DataRoot { get; set; }

        [DataMember(Order = 6)]
        public string ManagedSiteListValue { get; set; }

        [DataMember(Order = 7)]
        public int CurrentVersion { get; set; }

        [DataMember(Order = 8)]
        public string XmlSha256 { get; set; }

        [DataMember(Order = 9)]
        public string BaselineManifest { get; set; }

        [DataMember(Order = 10)]
        public string LastTransaction { get; set; }

        [DataMember(Order = 11)]
        public string PendingTransaction { get; set; }

        [DataMember(Order = 12)]
        public string UpdatedAtUtc { get; set; }

        [DataMember(Order = 13, EmitDefaultValue = false)]
        public string PendingSiteListValue { get; set; }
    }

    [DataContract]
    internal sealed class RegistryValueSnapshot
    {
        [DataMember(Order = 1)]
        public bool Exists { get; set; }

        [DataMember(Order = 2)]
        public int Kind { get; set; }

        [DataMember(Order = 3)]
        public string StringValue { get; set; }

        [DataMember(Order = 4)]
        public int DwordValue { get; set; }

        [DataMember(Order = 5)]
        public long QwordValue { get; set; }

        [DataMember(Order = 6)]
        public string BinaryBase64 { get; set; }

        [DataMember(Order = 7)]
        public string[] MultiStringValue { get; set; }
    }

    [DataContract]
    internal sealed class RegistryBackup
    {
        [DataMember(Order = 1)]
        public RegistryValueSnapshot InternetExplorerIntegrationLevel { get; set; }

        [DataMember(Order = 2)]
        public RegistryValueSnapshot InternetExplorerIntegrationSiteList { get; set; }
    }

    [DataContract]
    internal sealed class BackupManifest
    {
        [DataMember(Order = 1)]
        public int SchemaVersion { get; set; }

        [DataMember(Order = 2)]
        public string Id { get; set; }

        [DataMember(Order = 3)]
        public string CreatedAtUtc { get; set; }

        [DataMember(Order = 4)]
        public string Operation { get; set; }

        [DataMember(Order = 5)]
        public bool XmlExists { get; set; }

        [DataMember(Order = 6)]
        public bool StateExists { get; set; }

        [DataMember(Order = 7)]
        public RegistryBackup Registry { get; set; }
    }

    [DataContract]
    internal sealed class OperationLogRecord
    {
        [DataMember(Order = 1)]
        public string TimestampUtc { get; set; }

        [DataMember(Order = 2)]
        public string Operation { get; set; }

        [DataMember(Order = 3)]
        public string Result { get; set; }

        [DataMember(Order = 4)]
        public string Details { get; set; }
    }

    internal sealed class EnvironmentStatus
    {
        public bool IsWindows { get; set; }
        public string WindowsVersion { get; set; }
        public bool IsAdministrator { get; set; }
        public bool EdgeInstalled { get; set; }
        public bool EdgeSystemInstalled { get; set; }
        public string EdgePath { get; set; }
        public string EdgeVersion { get; set; }
        public bool IsSupported { get; set; }
        public List<string> SupportIssues { get; set; }
        public bool HasBlockingConflict { get; set; }
        public List<string> Conflicts { get; set; }
        public bool IsManaged { get; set; }
        public int ManagedVersion { get; set; }
        public string StatePhase { get; set; }
    }

    internal sealed class OperationResult
    {
        public bool Changed { get; set; }
        public string Operation { get; set; }
        public int Version { get; set; }
        public int SiteCount { get; set; }
        public string BackupManifest { get; set; }
        public bool RequiresEdgeRestart { get; set; }
        public List<string> Matched { get; set; }
        public List<string> NotFound { get; set; }
        public string Message { get; set; }
    }

    internal sealed class RuntimePaths
    {
        public RuntimePaths(string dataRoot)
        {
            Root = System.IO.Path.GetFullPath(dataRoot);
            Xml = System.IO.Path.Combine(Root, "sites.xml");
            State = System.IO.Path.Combine(Root, "state.json");
            Lock = System.IO.Path.Combine(Root, "operation.lock");
            Backups = System.IO.Path.Combine(Root, "backups");
            Logs = System.IO.Path.Combine(Root, "logs");
            Log = System.IO.Path.Combine(Logs, "operations.jsonl");
            VersionSequence = System.IO.Path.Combine(Logs, "version-sequence.txt");
            Published = System.IO.Path.Combine(Root, "published");
        }

        public string Root { get; private set; }
        public string Xml { get; private set; }
        public string State { get; private set; }
        public string Lock { get; private set; }
        public string Backups { get; private set; }
        public string Logs { get; private set; }
        public string Log { get; private set; }
        public string VersionSequence { get; private set; }
        public string Published { get; private set; }

        public string GetPublishedXml(int version)
        {
            return System.IO.Path.Combine(
                Published,
                "sites-v" +
                version.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ".xml");
        }
    }
}
