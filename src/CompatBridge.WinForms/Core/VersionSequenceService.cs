using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace CompatBridge.Core
{
    internal sealed class VersionSequenceService
    {
        private static readonly Regex LogVersionPattern = new Regex(
            "(?:\\\"Version\\\"\\s*:\\s*|Version=)(\\d+)",
            RegexOptions.CultureInvariant);

        private readonly RuntimePaths _paths;
        private readonly SiteListService _siteLists;

        public VersionSequenceService(
            RuntimePaths paths,
            SiteListService siteLists)
        {
            _paths = paths;
            _siteLists = siteLists;
        }

        public int ReserveNext(AppState state)
        {
            int highWater = GetHighWater(state);
            if (highWater >= 999999999)
            {
                throw new InvalidOperationException(
                    "Site List 版本号已达到上限，无法继续递增。");
            }

            int next = highWater + 1;
            JsonFileStore.WriteTextAtomic(
                _paths.VersionSequence,
                next.ToString(CultureInfo.InvariantCulture));
            return next;
        }

        public int GetHighWater(AppState state)
        {
            int highWater = state == null ? 0 : state.CurrentVersion;
            highWater = Math.Max(highWater, ReadVersionFile(_paths.Xml));
            highWater = Math.Max(highWater, ReadSequence());

            if (!File.Exists(_paths.VersionSequence))
            {
                highWater = Math.Max(highWater, ScanBackups());
                highWater = Math.Max(highWater, ScanLog());
            }
            return highWater;
        }

        private int ReadSequence()
        {
            if (!File.Exists(_paths.VersionSequence))
            {
                return 0;
            }
            int version;
            return int.TryParse(
                File.ReadAllText(_paths.VersionSequence).Trim(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out version)
                ? version
                : 0;
        }

        private int ScanBackups()
        {
            if (!Directory.Exists(_paths.Backups))
            {
                return 0;
            }
            int highWater = 0;
            foreach (string path in Directory.EnumerateFiles(
                _paths.Backups,
                "sites.xml",
                SearchOption.AllDirectories))
            {
                highWater = Math.Max(highWater, ReadVersionFile(path));
            }
            return highWater;
        }

        private int ScanLog()
        {
            if (!File.Exists(_paths.Log))
            {
                return 0;
            }
            int highWater = 0;
            foreach (string line in File.ReadLines(_paths.Log))
            {
                MatchCollection matches = LogVersionPattern.Matches(line);
                foreach (Match match in matches)
                {
                    int version;
                    if (int.TryParse(
                        match.Groups[1].Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out version))
                    {
                        highWater = Math.Max(highWater, version);
                    }
                }
            }
            return highWater;
        }

        private int ReadVersionFile(string path)
        {
            if (!File.Exists(path))
            {
                return 0;
            }
            try
            {
                return _siteLists.Load(path).Version;
            }
            catch
            {
                return 0;
            }
        }
    }
}
