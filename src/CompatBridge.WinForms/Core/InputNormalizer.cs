using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace CompatBridge.Core
{
    internal static class InputNormalizer
    {
        private static readonly Regex SchemeRegex =
            new Regex(@"^[a-zA-Z][a-zA-Z0-9+.-]*://", RegexOptions.Compiled);

        private static readonly Regex HttpSchemeRegex =
            new Regex(@"^https?://", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex AuthorityRegex =
            new Regex(@"^(?<host>[^:]+)(?::(?<port>[0-9]+))?$", RegexOptions.Compiled);

        private static readonly Regex HostLabelRegex =
            new Regex(@"^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$", RegexOptions.Compiled);

        public static IEnumerable<string> SplitInput(IEnumerable<string> input)
        {
            foreach (string item in input)
            {
                if (item == null)
                {
                    continue;
                }
                string[] lines = Regex.Split(item, @"\r?\n");
                foreach (string line in lines)
                {
                    string[] cells = line.Split('\t');
                    foreach (string cell in cells)
                    {
                        string value = cell.Trim();
                        if (value.Length > 0)
                        {
                            yield return value;
                        }
                    }
                }
            }
        }

        public static NormalizedSite Normalize(string input)
        {
            string raw = input ?? string.Empty;
            string value = raw.Trim();
            if (value.Length == 0)
            {
                return Invalid(raw, "输入为空。");
            }
            if (value.IndexOf('*') >= 0)
            {
                return Invalid(raw, "Enterprise Mode Site List 不接受通配符。");
            }
            if (SchemeRegex.IsMatch(value) && !HttpSchemeRegex.IsMatch(value))
            {
                return Invalid(raw, "仅接受 HTTP 或 HTTPS URL。");
            }

            string candidate = SchemeRegex.IsMatch(value) ? value : "http://" + value;
            Uri uri;
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out uri))
            {
                return Invalid(raw, "无法解析为有效的域名、IP 或 URL。");
            }
            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
            {
                return Invalid(raw, "仅接受 HTTP 或 HTTPS URL。");
            }
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                return Invalid(raw, "URL 不得包含用户名或密码。");
            }

            string withoutScheme = SchemeRegex.Replace(candidate, string.Empty, 1);
            int separator = withoutScheme.IndexOfAny(new[] { '/', '#', '?' });
            string authority = separator < 0 ? withoutScheme : withoutScheme.Substring(0, separator);
            if (authority.Length == 0)
            {
                return Invalid(raw, "缺少主机名。");
            }
            if (authority.StartsWith("[", StringComparison.Ordinal))
            {
                return Invalid(raw, "当前版本暂不接受 IPv6。");
            }

            Match authorityMatch = AuthorityRegex.Match(authority);
            if (!authorityMatch.Success)
            {
                return Invalid(raw, "主机或端口格式无效。");
            }

            int? explicitPort = null;
            Group portGroup = authorityMatch.Groups["port"];
            if (portGroup.Success)
            {
                int port;
                if (!int.TryParse(portGroup.Value, out port) || port < 1 || port > 65535)
                {
                    return Invalid(raw, "端口必须介于 1 和 65535 之间。");
                }
                explicitPort = port;
            }

            string host;
            string hostError;
            if (!TryNormalizeHost(authorityMatch.Groups["host"].Value, out host, out hostError))
            {
                return Invalid(raw, hostError);
            }

            List<string> warnings = new List<string>();
            if (!string.IsNullOrEmpty(uri.Query))
            {
                warnings.Add("已移除查询字符串。");
            }
            if (!string.IsNullOrEmpty(uri.Fragment))
            {
                warnings.Add("已移除 URL 片段。");
            }

            string escapedPath = uri.GetComponents(UriComponents.Path, UriFormat.UriEscaped);
            string normalizedPath = string.Empty;
            if (!string.IsNullOrEmpty(escapedPath))
            {
                normalizedPath = "/" + escapedPath.TrimStart('/');
                if (normalizedPath == "/")
                {
                    normalizedPath = string.Empty;
                }
            }

            string siteUrl = host;
            if (explicitPort.HasValue)
            {
                siteUrl += ":" + explicitPort.Value.ToString(CultureInfo.InvariantCulture);
            }
            siteUrl += normalizedPath;

            return new NormalizedSite
            {
                Raw = raw,
                IsValid = true,
                Url = siteUrl,
                Host = host,
                Port = explicitPort,
                Path = normalizedPath,
                Warnings = warnings,
                Error = null
            };
        }

        public static List<PreviewItem> PreviewAdd(
            IEnumerable<string> input,
            IEnumerable<ManagedSite> existingSites,
            string compatMode,
            bool allowRedirect)
        {
            Dictionary<string, ManagedSite> existing =
                new Dictionary<string, ManagedSite>(StringComparer.OrdinalIgnoreCase);
            foreach (ManagedSite site in existingSites)
            {
                existing[site.Url] = site;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<PreviewItem> result = new List<PreviewItem>();
            foreach (string value in SplitInput(input))
            {
                NormalizedSite normalized = Normalize(value);
                PreviewClassification classification = PreviewClassification.Invalid;
                if (normalized.IsValid)
                {
                    if (seen.Contains(normalized.Url))
                    {
                        classification = PreviewClassification.DuplicateInput;
                    }
                    else if (existing.ContainsKey(normalized.Url))
                    {
                        ManagedSite current = existing[normalized.Url];
                        classification =
                            string.Equals(current.CompatMode, compatMode, StringComparison.Ordinal) &&
                            current.AllowRedirect == allowRedirect
                                ? PreviewClassification.AlreadyExists
                                : PreviewClassification.ConflictSettings;
                        seen.Add(normalized.Url);
                    }
                    else
                    {
                        classification = PreviewClassification.Ready;
                        seen.Add(normalized.Url);
                    }
                }

                result.Add(ToPreview(normalized, classification));
            }
            return result;
        }

        public static List<PreviewItem> PreviewRemove(
            IEnumerable<string> input,
            IEnumerable<ManagedSite> existingSites)
        {
            HashSet<string> existing = new HashSet<string>(
                existingSites.Select(delegate(ManagedSite site) { return site.Url; }),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<PreviewItem> result = new List<PreviewItem>();
            foreach (string value in SplitInput(input))
            {
                NormalizedSite normalized = Normalize(value);
                PreviewClassification classification = PreviewClassification.Invalid;
                if (normalized.IsValid)
                {
                    if (seen.Contains(normalized.Url))
                    {
                        classification = PreviewClassification.DuplicateInput;
                    }
                    else
                    {
                        classification = existing.Contains(normalized.Url)
                            ? PreviewClassification.Matched
                            : PreviewClassification.NotFound;
                        seen.Add(normalized.Url);
                    }
                }
                result.Add(ToPreview(normalized, classification));
            }
            return result;
        }

        private static PreviewItem ToPreview(
            NormalizedSite normalized,
            PreviewClassification classification)
        {
            return new PreviewItem
            {
                Raw = normalized.Raw,
                Url = normalized.Url,
                Classification = classification,
                Warnings = normalized.Warnings ?? new List<string>(),
                Error = normalized.Error
            };
        }

        private static bool TryNormalizeHost(string input, out string host, out string error)
        {
            host = null;
            error = null;
            string value = input.Trim().TrimEnd('.').ToLowerInvariant();
            if (value.Length == 0)
            {
                error = "主机名为空。";
                return false;
            }

            IPAddress address;
            if (IPAddress.TryParse(value, out address))
            {
                if (address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    error = "当前版本暂不接受 IPv6。";
                    return false;
                }
                if (address.AddressFamily != AddressFamily.InterNetwork)
                {
                    error = "不支持的 IP 地址类型。";
                    return false;
                }
                host = address.ToString();
                return true;
            }
            if (Regex.IsMatch(value, @"^[0-9.]+$"))
            {
                error = "IPv4 地址格式无效。";
                return false;
            }

            string ascii;
            try
            {
                ascii = new IdnMapping().GetAscii(value).ToLowerInvariant();
            }
            catch
            {
                error = "主机名包含无法转换的国际化字符。";
                return false;
            }
            if (ascii.Length > 253)
            {
                error = "主机名超过 253 个字符。";
                return false;
            }
            string[] labels = ascii.Split('.');
            foreach (string label in labels)
            {
                if (label.Length < 1 || label.Length > 63)
                {
                    error = "主机名标签长度必须为 1 到 63 个字符。";
                    return false;
                }
                if (!HostLabelRegex.IsMatch(label))
                {
                    error = "主机名只能包含字母、数字和标签内部的连字符。";
                    return false;
                }
            }
            host = ascii;
            return true;
        }

        private static NormalizedSite Invalid(string raw, string error)
        {
            return new NormalizedSite
            {
                Raw = raw,
                IsValid = false,
                Url = null,
                Host = null,
                Port = null,
                Path = null,
                Warnings = new List<string>(),
                Error = error
            };
        }
    }
}
