using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace CompatBridge.Core
{
    internal sealed class SiteListService
    {
        private static readonly string[] SupportedCompatModes =
        {
            "Default",
            "IE8Enterprise",
            "IE7Enterprise"
        };

        public SiteListDocument Load(string path)
        {
            XmlDocument document = new XmlDocument();
            document.PreserveWhitespace = false;
            document.Load(path);
            if (document.DocumentElement == null ||
                document.DocumentElement.Name != "site-list")
            {
                throw new InvalidDataException(
                    "XML 根元素必须是 site-list（Enterprise Mode schema v2）。");
            }

            int version;
            if (!int.TryParse(
                    document.DocumentElement.GetAttribute("version"),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out version) ||
                version < 1)
            {
                throw new InvalidDataException("site-list version 必须为正整数。");
            }

            List<ManagedSite> sites = new List<ManagedSite>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            XmlNodeList nodes = document.SelectNodes("/site-list/site");
            if (nodes != null)
            {
                foreach (XmlNode node in nodes)
                {
                    XmlElement siteElement = node as XmlElement;
                    if (siteElement == null)
                    {
                        continue;
                    }
                    string url = siteElement.GetAttribute("url");
                    NormalizedSite normalized = InputNormalizer.Normalize(url);
                    if (!normalized.IsValid ||
                        !string.Equals(normalized.Url, url, StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "XML 包含未规范化或无效的站点条目：" + url);
                    }
                    if (!seen.Add(url))
                    {
                        throw new InvalidDataException("XML 包含重复站点条目：" + url);
                    }

                    XmlElement compat = siteElement.SelectSingleNode("compat-mode") as XmlElement;
                    XmlElement openIn = siteElement.SelectSingleNode("open-in") as XmlElement;
                    if (compat == null || openIn == null)
                    {
                        throw new InvalidDataException(
                            "站点条目缺少 compat-mode 或 open-in：" + url);
                    }
                    if (Array.IndexOf(SupportedCompatModes, compat.InnerText) < 0)
                    {
                        throw new InvalidDataException(
                            "站点条目使用不支持的 compat-mode：" + url);
                    }
                    if (!string.Equals(
                            openIn.InnerText.Trim(),
                            "IE11",
                            StringComparison.Ordinal))
                    {
                        throw new InvalidDataException(
                            "站点条目必须使用 open-in=IE11：" + url);
                    }

                    sites.Add(new ManagedSite
                    {
                        Url = url,
                        CompatMode = compat.InnerText,
                        AllowRedirect = string.Equals(
                            openIn.GetAttribute("allow-redirect"),
                            "true",
                            StringComparison.Ordinal)
                    });
                }
            }

            return new SiteListDocument { Version = version, Sites = sites };
        }

        public string Build(int version, IEnumerable<ManagedSite> sites, DateTime createdAt)
        {
            if (version < 1)
            {
                throw new ArgumentOutOfRangeException("version");
            }

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\r\n",
                NewLineHandling = NewLineHandling.Replace,
                Encoding = new UTF8Encoding(false),
                OmitXmlDeclaration = false
            };

            using (MemoryStream stream = new MemoryStream())
            {
                using (XmlWriter writer = XmlWriter.Create(stream, settings))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("site-list");
                    writer.WriteAttributeString(
                        "version",
                        version.ToString(CultureInfo.InvariantCulture));

                    writer.WriteStartElement("created-by");
                    writer.WriteElementString("tool", AppInfo.ProductName);
                    writer.WriteElementString("version", AppInfo.Version);
                    writer.WriteElementString(
                        "date-created",
                        createdAt.ToString("yyyyMMdd.HHmmss", CultureInfo.InvariantCulture));
                    writer.WriteEndElement();

                    HashSet<string> unique =
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (ManagedSite site in sites)
                    {
                        ValidateSite(site);
                        if (!unique.Add(site.Url))
                        {
                            throw new InvalidDataException(
                                "站点列表包含重复条目：" + site.Url);
                        }

                        writer.WriteStartElement("site");
                        writer.WriteAttributeString("url", site.Url);
                        writer.WriteElementString("compat-mode", site.CompatMode);
                        writer.WriteStartElement("open-in");
                        if (site.AllowRedirect)
                        {
                            writer.WriteAttributeString("allow-redirect", "true");
                        }
                        writer.WriteString("IE11");
                        writer.WriteEndElement();
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
                return settings.Encoding.GetString(stream.ToArray());
            }
        }

        public void SaveAtomic(
            string path,
            int version,
            IEnumerable<ManagedSite> sites)
        {
            string xml = Build(version, sites, DateTime.Now);
            JsonFileStore.WriteTextAtomic(path, xml);
            Load(path);
        }

        private static void ValidateSite(ManagedSite site)
        {
            if (site == null || string.IsNullOrWhiteSpace(site.Url))
            {
                throw new InvalidDataException("站点条目缺少 Url。");
            }
            NormalizedSite normalized = InputNormalizer.Normalize(site.Url);
            if (!normalized.IsValid ||
                !string.Equals(normalized.Url, site.Url, StringComparison.Ordinal))
            {
                throw new InvalidDataException("站点条目未规范化：" + site.Url);
            }
            if (Array.IndexOf(SupportedCompatModes, site.CompatMode) < 0)
            {
                throw new InvalidDataException(
                    "不支持的兼容模式：" + site.CompatMode);
            }
        }
    }
}
