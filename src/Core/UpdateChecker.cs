using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ContextMenuCleaner.Core
{
    public class UpdateInfo
    {
        public bool Available;
        public string LatestVersion = "";
        public string ReleaseUrl = "";
        public string Notes = "";
        public string Error = "";
    }

    /// <summary>
    /// Read-only check of the latest GitHub Release. Designed to run on a background
    /// thread: it never throws, times out in six seconds, and sends nothing about the
    /// machine except a User-Agent string. No telemetry, no upload of any kind.
    /// </summary>
    public static class UpdateChecker
    {
        public const string Owner = "KyuuKyuuStone";
        public const string Repo = "whack-a-menu";
        public const string ApiUrl = "https://api.github.com/repos/" + Owner +
                                    "/" + Repo + "/releases/latest";
        public const string ReleasesUrl = "https://github.com/" + Owner +
                                         "/" + Repo + "/releases";

        public static UpdateInfo Check(string currentVersion)
        {
            UpdateInfo info = new UpdateInfo();
            info.ReleaseUrl = ReleasesUrl;
            try
            {
                // GitHub requires TLS 1.2; older .NET Framework defaults may be TLS 1.0.
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; }
                catch (Exception) { /* keep the process default */ }

                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(ApiUrl);
                request.Method = "GET";
                request.UserAgent = "whack-a-menu/" + currentVersion;
                request.Accept = "application/vnd.github+json";
                request.Timeout = 6000;
                request.ReadWriteTimeout = 6000;
                IWebProxy proxy = EnvironmentProxy();
                if (proxy != null) request.Proxy = proxy;

                string json;
                using (WebResponse response = request.GetResponse())
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                {
                    json = reader.ReadToEnd();
                }

                string tag = Field(json, "tag_name");
                string url = Field(json, "html_url");
                string body = Field(json, "body");
                if (string.IsNullOrEmpty(tag))
                {
                    info.Error = "发布信息里没有版本号";
                    return info;
                }
                info.LatestVersion = Normalize(tag);
                if (!string.IsNullOrEmpty(url)) info.ReleaseUrl = url;
                if (!string.IsNullOrEmpty(body))
                    info.Notes = body.Length > 240 ? body.Substring(0, 240) + "…" : body;
                info.Available = IsNewer(info.LatestVersion, currentVersion);
            }
            catch (WebException ex)
            {
                HttpWebResponse response = ex.Response as HttpWebResponse;
                info.Error = response != null && (int)response.StatusCode == 404
                    ? "还没有发布任何 Release"
                    : "连不上 GitHub（" + ex.Message + "）";
            }
            catch (Exception ex)
            {
                info.Error = ex.Message;
            }
            return info;
        }

        /// <summary>
        /// Honour HTTPS_PROXY / HTTP_PROXY explicitly. On locked-down desktops the
        /// WinINET default proxy is often empty while a local proxy is exported in
        /// the environment; without this, api.github.com returns 403 even though
        /// git reaches GitHub through that same proxy.
        /// </summary>
        private static IWebProxy EnvironmentProxy()
        {
            string[] names = { "HTTPS_PROXY", "https_proxy", "HTTP_PROXY", "http_proxy" };
            foreach (string name in names)
            {
                string value = Environment.GetEnvironmentVariable(name);
                if (string.IsNullOrEmpty(value)) continue;
                try { return new WebProxy(value); }
                catch (Exception) { /* try the next candidate */ }
            }
            return null;
        }

        /// <summary>
        /// Tiny JSON string reader. The GitHub response is huge; pulling just three
        /// quoted fields avoids adding a JSON library to a zero-dependency build.
        /// </summary>
        private static string Field(string json, string name)
        {
            Match match = Regex.Match(json,
                "\"" + name + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"",
                RegexOptions.Singleline);
            return match.Success ? Unescape(match.Groups[1].Value) : "";
        }

        private static string Unescape(string value)
        {
            return value.Replace("\\n", "\n").Replace("\\r", "")
                        .Replace("\\t", "  ").Replace("\\\"", "\"")
                        .Replace("\\/", "/").Replace("\\\\", "\\");
        }

        public static string Normalize(string tag)
        {
            string value = (tag ?? "").Trim();
            if (value.StartsWith("v") || value.StartsWith("V"))
                value = value.Substring(1);
            return value;
        }

        public static bool IsNewer(string latest, string current)
        {
            Version a, b;
            if (!Version.TryParse(Normalize(latest), out a)) return false;
            if (!Version.TryParse(Normalize(current), out b)) return false;
            return a.CompareTo(b) > 0;
        }
    }
}
