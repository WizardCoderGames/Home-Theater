// Checks GitHub Releases for a newer version and downloads its installer.
// Shared by the app and by Setup. A download is only used if its SHA-256 matches what GitHub
// reports for that release and the installer's own version number matches the release.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace Theater
{
    class ReleaseInfo
    {
        public Version Version;
        public string Tag = "", Notes = "", SetupUrl = "", Sha256 = "", Page = "";
        public long Size;
    }

    static class Updater
    {
        public const string AssetName = "EricsHomeTheater-Setup.exe";
        public const string InstalledExeName = "EricsHomeTheater.exe";

        // "owner/repository" on GitHub, set in update_repo.txt when the app is built.
        public static string Repo
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("THEATER_UPDATE_REPO");   // used by automated tests
                if (!string.IsNullOrEmpty(env)) return env.Trim();
                try
                {
                    using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("update_repo.txt"))
                    using (var rd = new StreamReader(s))
                        foreach (var line in rd.ReadToEnd().Split('\n'))
                        {
                            string l = line.Trim();
                            if (l.Length > 0 && !l.StartsWith("#")) return l;
                        }
                }
                catch { }
                return "";
            }
        }

        static string ApiOverride { get { return Environment.GetEnvironmentVariable("THEATER_UPDATE_API"); } }   // tests only
        static string Api { get { return string.IsNullOrEmpty(ApiOverride) ? "https://api.github.com" : ApiOverride.TrimEnd('/'); } }

        public static bool Configured { get { return Regex.IsMatch(Repo, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$"); } }

        public static Version Current
        {
            get { var v = (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version; return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); }
        }

        // The copy that Setup installed (as opposed to a development build) is the one that updates itself.
        public static bool IsInstalledCopy
        {
            get { return string.Equals(Path.GetFileName(System.Windows.Forms.Application.ExecutablePath), InstalledExeName, StringComparison.OrdinalIgnoreCase); }
        }

        public static Version ParseTag(string tag)
        {
            var m = Regex.Match(tag ?? "", @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (!m.Success) return null;
            return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        }

        static bool Allowed(string url)
        {
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return true;
            // Plain http only for a local test server.
            return !string.IsNullOrEmpty(ApiOverride) && (url.StartsWith("http://localhost:") || url.StartsWith("http://127.0.0.1:"));
        }

        static HttpWebRequest Request(string url)
        {
            if (!Allowed(url)) throw new InvalidOperationException("Refusing to use a non-HTTPS address: " + url);
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | (SecurityProtocolType)12288; }
            catch { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "EricsHomeTheater-Updater";   // GitHub requires a user agent
            r.Accept = "application/vnd.github+json";
            r.Timeout = 15000;
            r.ReadWriteTimeout = 30000;
            return r;
        }

        static string GetText(string url)
        {
            using (var resp = Request(url).GetResponse())
            using (var rd = new StreamReader(resp.GetResponseStream()))
                return rd.ReadToEnd();
        }

        static string S(Dictionary<string, object> d, string k) { object o; return d.TryGetValue(k, out o) && o != null ? o.ToString() : ""; }

        // The newest published release, or null if there is none with a verifiable installer.
        public static ReleaseInfo Latest()
        {
            if (!Configured) return null;
            string json;
            try { json = GetText(Api + "/repos/" + Repo + "/releases/latest"); }
            catch (WebException ex)
            {
                // 404: nothing has been published yet, which simply means there's no update.
                var resp = ex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.NotFound) return null;
                throw;
            }
            var d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (d == null) return null;
            var r = new ReleaseInfo { Tag = S(d, "tag_name"), Notes = S(d, "body"), Page = S(d, "html_url") };
            r.Version = ParseTag(r.Tag);
            if (r.Version == null) return null;
            string shaUrl = null;
            object assets;
            if (d.TryGetValue("assets", out assets) && assets is IList)
                foreach (var o in (IList)assets)
                {
                    var a = o as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = S(a, "name");
                    if (name == AssetName)
                    {
                        r.SetupUrl = S(a, "browser_download_url");
                        long size;
                        if (long.TryParse(S(a, "size"), out size)) r.Size = size;
                        string digest = S(a, "digest");
                        if (digest.StartsWith("sha256:")) r.Sha256 = digest.Substring(7);
                    }
                    else if (name == AssetName + ".sha256") shaUrl = S(a, "browser_download_url");
                }
            if (r.SetupUrl == "") return null;
            if (r.Sha256 == "" && shaUrl != null)
            {
                var m = Regex.Match(GetText(shaUrl), "[0-9a-fA-F]{64}");
                if (m.Success) r.Sha256 = m.Value;
            }
            if (!Regex.IsMatch(r.Sha256, "^[0-9a-fA-F]{64}$")) return null;   // can't verify it: don't use it
            return r;
        }

        public static bool IsNewer(ReleaseInfo r) { return r != null && r.Version > Current; }

        static string Sha256Of(string path)
        {
            using (var sha = SHA256.Create())
            using (var f = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
        }

        // Downloads (or reuses an already-downloaded) installer and checks it before returning its path.
        public static string Download(ReleaseInfo r, string dir, Action<int> progress)
        {
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "EricsHomeTheater-Setup-" + r.Version + ".exe"), tmp = path + ".part";
            if (File.Exists(path) && Sha256Of(path) == r.Sha256.ToLowerInvariant()) return path;

            using (var resp = Request(r.SetupUrl).GetResponse())
            using (var src = resp.GetResponseStream())
            using (var dst = File.Create(tmp))
            {
                long total = resp.ContentLength > 0 ? resp.ContentLength : r.Size, done = 0;
                var buf = new byte[81920];
                int n, last = -1;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    dst.Write(buf, 0, n);
                    done += n;
                    int pct = total > 0 ? (int)(done * 100 / total) : 0;
                    if (pct != last && progress != null) { progress(pct); last = pct; }
                }
            }
            if (Sha256Of(tmp) != r.Sha256.ToLowerInvariant())
            {
                File.Delete(tmp);
                throw new InvalidOperationException("The downloaded installer didn't match the release's checksum, so it wasn't used.");
            }
            var fv = ParseTag(FileVersionInfo.GetVersionInfo(tmp).FileVersion);
            if (fv == null || fv != r.Version)
            {
                File.Delete(tmp);
                throw new InvalidOperationException("The downloaded installer is version " + fv + ", not " + r.Version + ", so it wasn't used.");
            }
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
            return path;
        }
    }
}
