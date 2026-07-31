using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CompatBridge.Core
{
    internal static class SiteInputFileReader
    {
        private static readonly HashSet<string> HeaderNames =
            new HashSet<string>(
                new[]
                {
                    "url",
                    "site",
                    "website",
                    "address",
                    "domain",
                    "网址",
                    "网站",
                    "站点",
                    "地址",
                    "域名",
                    "url地址"
                },
                StringComparer.OrdinalIgnoreCase);

        public static List<string> ReadEntries(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("请选择要导入的 TXT 或 CSV 文件。", "path");
            }
            string extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("仅支持导入 TXT 或 CSV 文件。");
            }

            string content = File.ReadAllText(path);
            List<string> entries = string.Equals(
                extension,
                ".csv",
                StringComparison.OrdinalIgnoreCase)
                ? ReadCsvFirstColumn(content)
                : ReadTextLines(content);
            RemoveHeader(entries);
            return entries;
        }

        internal static List<string> ReadCsvFirstColumn(string content)
        {
            List<List<string>> rows = ParseCsv(content ?? string.Empty);
            List<string> entries = new List<string>();
            foreach (List<string> row in rows)
            {
                if (row.Count == 0)
                {
                    continue;
                }
                string value = row[0].Trim();
                if (value.Length > 0)
                {
                    entries.Add(value);
                }
            }
            return entries;
        }

        private static List<string> ReadTextLines(string content)
        {
            List<string> entries = new List<string>();
            foreach (string line in Regex.Split(content ?? string.Empty, @"\r\n|\n|\r"))
            {
                string value = line.Trim();
                if (value.Length > 0)
                {
                    entries.Add(value);
                }
            }
            return entries;
        }

        private static void RemoveHeader(List<string> entries)
        {
            if (entries.Count > 0 && HeaderNames.Contains(entries[0].Trim()))
            {
                entries.RemoveAt(0);
            }
        }

        private static List<List<string>> ParseCsv(string content)
        {
            List<List<string>> rows = new List<List<string>>();
            List<string> row = new List<string>();
            StringBuilder cell = new StringBuilder();
            bool inQuotes = false;

            for (int index = 0; index < content.Length; index++)
            {
                char current = content[index];
                if (current == '"')
                {
                    if (inQuotes &&
                        index + 1 < content.Length &&
                        content[index + 1] == '"')
                    {
                        cell.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (current == ',' && !inQuotes)
                {
                    row.Add(cell.ToString());
                    cell.Length = 0;
                }
                else if ((current == '\r' || current == '\n') && !inQuotes)
                {
                    row.Add(cell.ToString());
                    cell.Length = 0;
                    rows.Add(row);
                    row = new List<string>();
                    if (current == '\r' &&
                        index + 1 < content.Length &&
                        content[index + 1] == '\n')
                    {
                        index++;
                    }
                }
                else
                {
                    cell.Append(current);
                }
            }

            if (cell.Length > 0 || row.Count > 0)
            {
                row.Add(cell.ToString());
                rows.Add(row);
            }
            return rows;
        }
    }
}
