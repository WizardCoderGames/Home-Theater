// Settings, watch history, the movie library scanner, disc detection and thumbnails.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Theater
{
    class Settings
    {
        public List<string> Roots = new List<string>();
        public string VlcPath = @"C:\Program Files\VideoLAN\VLC\vlc.exe";
        public string DiscPlayer = "";
        public string Code = "";
        public int Port = 8765;
        public int Volume = 256;
        public bool Windowed;
        public string Anim = "auto";        // auto | on | off
        public string Resolution = "native"; // native | WIDTHxHEIGHT (full screen only)
        public string HwDecode = "auto";     // auto | d3d11 | dxva2 | off
        public string Renderer = "auto";     // auto | d3d11 | d3d9 | opengl | gdi
        public string WifiAuto = "";         // saved Wi-Fi network to connect to at startup
        public bool AutoUpdate = true;       // download and install new versions from GitHub
        public bool UsbAuto = true;          // add movies from USB drives while they're plugged in
        public bool PhonePrompt = true;      // show the phone QR code at start and when the phone disconnects
        public string Theme = "gold";        // a Ui.Themes key

        // Extra VLC options for the hardware acceleration and renderer choices.
        public string VlcArgs()
        {
            string hw = HwDecode == "d3d11" ? "d3d11va" : HwDecode == "dxva2" ? "dxva2" : HwDecode == "off" ? "none" : "any";
            string vout = Renderer == "d3d11" ? "direct3d11" : Renderer == "d3d9" ? "direct3d9" : Renderer == "opengl" ? "gl" : Renderer == "gdi" ? "wingdi" : "";
            return "--avcodec-hw=" + hw + (vout != "" ? " --vout=" + vout : "");
        }

        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Theater"); } }
        static string FilePath { get { return Path.Combine(Dir, "settings.ini"); } }

        public static Settings Load()
        {
            var s = new Settings();
            bool existed = false;
            try
            {
                if (File.Exists(FilePath))
                {
                    existed = true;
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq < 0) continue;
                        string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                        switch (k)
                        {
                            case "folder": if (v.Length > 0) s.Roots.Add(v); break;
                            case "vlc": s.VlcPath = v; break;
                            case "discplayer": s.DiscPlayer = v; break;
                            case "code": s.Code = v; break;
                            case "port": int.TryParse(v, out s.Port); break;
                            case "volume": int.TryParse(v, out s.Volume); break;
                            case "windowed": s.Windowed = v == "1"; break;
                            case "anim": if (v == "on" || v == "off" || v == "auto") s.Anim = v; break;
                            case "resolution": s.Resolution = v; break;
                            case "hwdecode": s.HwDecode = v; break;
                            case "renderer": s.Renderer = v; break;
                            case "wifiauto": s.WifiAuto = v; break;
                            case "autoupdate": s.AutoUpdate = v != "0"; break;
                            case "usbauto": s.UsbAuto = v != "0"; break;
                            case "phoneprompt": s.PhonePrompt = v != "0"; break;
                            case "theme": s.Theme = v; break;
                        }
                    }
                }
            }
            catch { }
            if (!existed)
            {
                foreach (var d in new[] { @"F:\Movies", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) })
                    if (Directory.Exists(d)) { s.Roots.Add(d); break; }
            }
            if (!File.Exists(s.VlcPath)) { string found = FindVlc(); if (found != null) s.VlcPath = found; }
            if (s.Code.Length != 4) s.Code = NewCode();
            if (s.Volume <= 0 || s.Volume > 512) s.Volume = 256;
            if (s.Port <= 0) s.Port = 8765;
            s.Save();
            return s;
        }

        // VLC's own registry entry (works wherever it was installed), then the usual folders.
        public static string FindVlc()
        {
            foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
                try
                {
                    using (var hk = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view))
                    using (var k = hk.OpenSubKey(@"SOFTWARE\VideoLAN\VLC"))
                    {
                        var dir = k == null ? null : k.GetValue("InstallDir") as string;
                        if (dir != null && File.Exists(Path.Combine(dir, "vlc.exe"))) return Path.Combine(dir, "vlc.exe");
                    }
                }
                catch { }
            foreach (var p in new[] { @"C:\Program Files\VideoLAN\VLC\vlc.exe", @"C:\Program Files (x86)\VideoLAN\VLC\vlc.exe" })
                if (File.Exists(p)) return p;
            return null;
        }

        public static string NewCode()
        {
            return new Random().Next(1000, 10000).ToString();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var lines = new List<string>();
                foreach (var r in Roots) lines.Add("folder=" + r);
                lines.Add("vlc=" + VlcPath);
                lines.Add("discplayer=" + DiscPlayer);
                lines.Add("code=" + Code);
                lines.Add("port=" + Port);
                lines.Add("volume=" + Volume);
                lines.Add("windowed=" + (Windowed ? "1" : "0"));
                lines.Add("anim=" + Anim);
                lines.Add("resolution=" + Resolution);
                lines.Add("hwdecode=" + HwDecode);
                lines.Add("renderer=" + Renderer);
                lines.Add("wifiauto=" + WifiAuto);
                lines.Add("autoupdate=" + (AutoUpdate ? "1" : "0"));
                lines.Add("usbauto=" + (UsbAuto ? "1" : "0"));
                lines.Add("phoneprompt=" + (PhonePrompt ? "1" : "0"));
                lines.Add("theme=" + Theme);
                File.WriteAllLines(FilePath, lines);
            }
            catch { }
        }
    }

    class WatchState
    {
        public double Pos, Len;
        public DateTime When;
        public bool Watched;
        public double Fraction { get { return Len > 0 ? Math.Min(1, Pos / Len) : 0; } }
    }

    class History
    {
        readonly Dictionary<string, WatchState> map = new Dictionary<string, WatchState>(StringComparer.OrdinalIgnoreCase);
        static string FilePath { get { return Path.Combine(Settings.Dir, "history.txt"); } }

        public static History Load()
        {
            var h = new History();
            try
            {
                if (File.Exists(FilePath))
                    foreach (var line in File.ReadAllLines(FilePath))
                    {
                        var p = line.Split('\t');
                        if (p.Length < 5) continue;
                        var w = new WatchState();
                        double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out w.Pos);
                        double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out w.Len);
                        long ticks;
                        if (long.TryParse(p[3], out ticks)) w.When = new DateTime(ticks);
                        w.Watched = p[4] == "1";
                        h.map[p[0]] = w;
                    }
            }
            catch { }
            return h;
        }

        public WatchState Get(string path)
        {
            WatchState w;
            return path != null && map.TryGetValue(path, out w) ? w : null;
        }

        public void Set(string path, double pos, double len, bool watched)
        {
            map[path] = new WatchState { Pos = pos, Len = len, When = DateTime.Now, Watched = watched };
            Save();
        }

        public List<KeyValuePair<string, WatchState>> All()
        {
            return new List<KeyValuePair<string, WatchState>>(map);
        }

        void Save()
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                var lines = new List<string>();
                foreach (var kv in map)
                    lines.Add(kv.Key + "\t" + kv.Value.Pos.ToString(CultureInfo.InvariantCulture) + "\t" + kv.Value.Len.ToString(CultureInfo.InvariantCulture)
                        + "\t" + kv.Value.When.Ticks + "\t" + (kv.Value.Watched ? "1" : "0"));
                File.WriteAllLines(FilePath, lines);
            }
            catch { }
        }
    }

    enum Kind { Movie, Collection, DiscFolder, Disc, NoDisc }

    class MediaItem
    {
        public string Id, Path, Uri, Title = "", Year = "", Quality = "", PosterPath, DiscType = "", Drive;
        public bool Hdr, Encrypted;
        public Kind Kind;
        public long Size;
        public DateTime Added;
        public double Duration;
        public List<MediaItem> Children;
        public MediaItem Parent;
        public List<string> Genres = new List<string>();
        public bool IsGenre;   // a genre tile (its Children are the movies in that genre)
        public volatile Bitmap Thumb;
        public volatile bool ThumbQueued, ThumbReady;   // ThumbReady: a card image exists in the disk cache
        public LinkedListNode<MediaItem> LruNode;       // UI thread only

        public bool Playable { get { return Kind == Kind.Movie || Kind == Kind.DiscFolder || Kind == Kind.Disc; } }
        public bool IsDiscUri { get { return Uri != null && (Uri.StartsWith("bluray:") || Uri.StartsWith("dvd:")); } }

        public string SortKey
        {
            get { return Regex.Replace(Title, "^(the|a|an) ", "", RegexOptions.IgnoreCase); }
        }

        // Title as shown outside its collection ("Show · S01E02").
        public string FullTitle
        {
            get { return Parent != null ? Parent.Title + " \u00B7 " + Title : Title; }
        }
    }

    class ParsedName
    {
        public string Title = "", Year = "", Quality = "";
        public bool Hdr;
    }

    class Library
    {
        public List<MediaItem> Items = new List<MediaItem>();
        public Dictionary<string, MediaItem> ById = new Dictionary<string, MediaItem>();
        public Dictionary<string, MediaItem> ByPath = new Dictionary<string, MediaItem>(StringComparer.OrdinalIgnoreCase);

        static readonly HashSet<string> VideoExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".mkv", ".mp4", ".m4v", ".avi", ".mov", ".wmv", ".ts", ".m2ts", ".mts", ".webm", ".mpg", ".mpeg", ".flv", ".iso", ".divx", ".ogv"
        };
        static readonly HashSet<string> ImageExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };

        public IEnumerable<MediaItem> Flatten()
        {
            foreach (var it in Items)
            {
                if (it.Children == null) yield return it;
                else foreach (var c in it.Children) yield return c;
            }
        }

        public static Library Scan(List<string> roots)
        {
            var lib = new Library();
            foreach (var root in roots)
            {
                if (!Directory.Exists(root)) continue;
                var disc = DiscFolder(root);
                if (disc != null) { lib.Items.Add(disc); continue; }
                foreach (var f in SafeFiles(root))
                    if (IsVideo(f)) lib.Items.Add(MakeMovie(f, null, null));
                foreach (var d in SafeDirs(root))
                {
                    var it = ScanFolder(d);
                    if (it != null) lib.Items.Add(it);
                }
            }
            lib.Items.Sort((a, b) => string.Compare(a.SortKey, b.SortKey, StringComparison.OrdinalIgnoreCase));
            lib.BuildIndex();
            return lib;
        }

        public void BuildIndex()
        {
            ById.Clear();
            ByPath.Clear();
            foreach (var it in Items) Index(it);
            Theater.Genres.Apply(this);
        }

        // Changes whenever files are added, removed or replaced.
        public string Signature()
        {
            var sb = new StringBuilder();
            foreach (var it in Flatten()) sb.Append(it.Path).Append('|').Append(it.Size).Append('\n');
            return Hash(sb.ToString()) + ":" + Items.Count;
        }

        void Index(MediaItem it)
        {
            ById[it.Id] = it;
            if (it.Path != null) ByPath[it.Path] = it;
            if (it.Children != null) foreach (var c in it.Children) Index(c);
        }

        public static MediaItem ScanFolder(string dir)
        {
            var disc = DiscFolder(dir);
            if (disc != null) return disc;
            var vids = new List<string>();
            var discs = new List<MediaItem>();
            Collect(dir, 0, vids, discs);
            int n = vids.Count + discs.Count;
            if (n == 0) return null;
            string poster = FindPoster(dir);
            if (n == 1)
            {
                if (discs.Count == 1) return discs[0];
                var m = MakeMovie(vids[0], null, Path.GetFileName(dir));
                m.PosterPath = poster ?? FindPoster(Path.GetDirectoryName(vids[0]));
                return m;
            }
            var c = new MediaItem { Kind = Kind.Collection, Path = dir, Id = Hash(dir), PosterPath = poster, Children = new List<MediaItem>() };
            Apply(c, Parse(Path.GetFileName(dir)));
            foreach (var v in vids) c.Children.Add(MakeMovie(v, c, null));
            foreach (var d in discs) { d.Parent = c; c.Children.Add(d); }
            c.Children.Sort((a, b) => StrCmpLogicalW(a.Path, b.Path));
            foreach (var ch in c.Children)
            {
                c.Size += ch.Size;
                if (ch.Added > c.Added) c.Added = ch.Added;
            }
            return c;
        }

        static void Collect(string dir, int depth, List<string> vids, List<MediaItem> discs)
        {
            foreach (var f in SafeFiles(dir))
                if (IsVideo(f) && !Regex.IsMatch(Path.GetFileNameWithoutExtension(f), @"\b(sample|trailer)\b", RegexOptions.IgnoreCase))
                    vids.Add(f);
            if (depth >= 3) return;
            foreach (var d in SafeDirs(dir))
            {
                string name = Path.GetFileName(d).ToUpperInvariant();
                if (name == "BDMV" || name == "VIDEO_TS" || name == "AACS" || name == "CERTIFICATE" || name == "SAMPLE" || name == "SAMPLES") continue;
                var disc = DiscFolder(d);
                if (disc != null) discs.Add(disc);
                else Collect(d, depth + 1, vids, discs);
            }
        }

        // A ripped Blu-ray (BDMV) or DVD (VIDEO_TS) folder.
        public static MediaItem DiscFolder(string dir)
        {
            try
            {
                string bd = Path.Combine(dir, @"BDMV\index.bdmv");
                bool isBd = File.Exists(bd), isDvd = !isBd && Directory.Exists(Path.Combine(dir, "VIDEO_TS"));
                if (!isBd && !isDvd) return null;
                var m = new MediaItem { Kind = Kind.DiscFolder, Path = dir, Id = Hash(dir) };
                Apply(m, Parse(Path.GetFileName(dir)));
                string mrlPath = dir.Replace('\\', '/').TrimEnd('/') + "/";
                if (isBd)
                {
                    bool uhd = IsUhd(bd);
                    m.DiscType = uhd ? "4K Ultra HD Blu-ray" : "Blu-ray";
                    m.Encrypted = Directory.Exists(Path.Combine(dir, "AACS"));
                    m.Uri = "bluray:///" + mrlPath;
                    if (m.Quality == "") m.Quality = uhd ? "4K" : "1080p";
                    m.PosterPath = DiscCover(dir) ?? FindPoster(dir);
                }
                else
                {
                    m.DiscType = "DVD";
                    m.Uri = "dvd:///" + mrlPath;
                    m.PosterPath = FindPoster(dir);
                }
                try { m.Added = Directory.GetLastWriteTime(dir); } catch { }
                return m;
            }
            catch { return null; }
        }

        public static bool IsUhd(string indexBdmv)
        {
            try
            {
                using (var fs = File.OpenRead(indexBdmv))
                {
                    var b = new byte[8];
                    fs.Read(b, 0, 8);
                    return Encoding.ASCII.GetString(b) == "INDX0300";
                }
            }
            catch { return false; }
        }

        // Blu-ray discs carry their own cover art in BDMV\META\DL.
        public static string DiscCover(string dir)
        {
            try
            {
                string dl = Path.Combine(dir, @"BDMV\META\DL");
                if (!Directory.Exists(dl)) return null;
                string best = null;
                long size = 0;
                foreach (var f in Directory.GetFiles(dl, "*.jpg"))
                {
                    long s = new FileInfo(f).Length;
                    if (s > size) { size = s; best = f; }
                }
                return best;
            }
            catch { return null; }
        }

        static MediaItem MakeMovie(string path, MediaItem parent, string folderName)
        {
            var m = new MediaItem { Kind = Kind.Movie, Path = path, Uri = path, Id = Hash(path), Parent = parent };
            try
            {
                var fi = new FileInfo(path);
                m.Size = fi.Length;
                m.Added = fi.CreationTime > fi.LastWriteTime ? fi.CreationTime : fi.LastWriteTime;
            }
            catch { }
            var fromFile = Parse(Path.GetFileNameWithoutExtension(path));
            if (folderName != null)
            {
                var fromFolder = Parse(folderName);
                var best = (fromFolder.Year != "" || fromFile.Year == "") ? fromFolder : fromFile;
                Apply(m, best);
                if (fromFile.Quality != "") m.Quality = fromFile.Quality;
                m.Hdr = fromFile.Hdr || fromFolder.Hdr;
            }
            else Apply(m, fromFile);

            if (parent != null)
            {
                // Episodes: "Show.S01E03.Name" -> "S01E03 · Name"
                var ep = Regex.Match(Path.GetFileNameWithoutExtension(path), @"[Ss](\d{1,2})[ ._-]?[Ee](\d{1,3})(.*)$");
                if (ep.Success)
                {
                    string rest = Parse(ep.Groups[3].Value).Title;
                    m.Title = "S" + ep.Groups[1].Value.PadLeft(2, '0') + "E" + ep.Groups[2].Value.PadLeft(2, '0') + (rest.Length > 1 ? " \u00B7 " + rest : "");
                    m.Year = "";
                }
            }
            return m;
        }

        static void Apply(MediaItem m, ParsedName p)
        {
            m.Title = p.Title;
            m.Year = p.Year;
            if (p.Quality != "") m.Quality = p.Quality;
            m.Hdr |= p.Hdr;
        }

        static readonly string Junk = @"2160p|1080p|1080i|720p|576p|480p|4k|uhd|bluray|blu-ray|brrip|bdrip|bdremux|remux|webrip|web-dl|webdl|web|hdrip|dvdrip|dvdscr|hdtv|hdcam|cam|x264|x265|h264|h265|h\.264|h\.265|hevc|avc|xvid|divx|aac|ac3|dts|remastered|extended|unrated|proper|repack|yify|yts|10bit|hdr|hdr10|dv";

        public static ParsedName Parse(string raw)
        {
            var p = new ParsedName();
            string s = Regex.Replace(raw ?? "", @"(\.(mkv|mp4|avi|m4v|mov|wmv))+$", "", RegexOptions.IgnoreCase);
            if (Regex.IsMatch(s, @"2160p|\b4k\b|\buhd\b", RegexOptions.IgnoreCase)) p.Quality = "4K";
            else if (Regex.IsMatch(s, @"1080[pi]", RegexOptions.IgnoreCase)) p.Quality = "1080p";
            else if (Regex.IsMatch(s, @"720p", RegexOptions.IgnoreCase)) p.Quality = "720p";
            p.Hdr = Regex.IsMatch(s, @"\b(hdr|hdr10|hdr10\+|dv|dovi|dolby[ .]?vision)\b", RegexOptions.IgnoreCase);

            string t = Regex.Replace(s.Replace('.', ' ').Replace('_', ' '), @"\s+", " ").Trim();
            // Drop download-site prefixes like "[ www.Torrenting.com ] - ".
            t = Regex.Replace(t, @"^\[?\s*www\s+\S+\s+(com|org|net|to|me|cc|io)\s*\]?\s*-?\s*", "", RegexOptions.IgnoreCase);
            var m = Regex.Match(t, @"^(.+)[\s\(\[]+((?:19|20)\d{2})(?:[\s\)\]]|$)");
            if (m.Success && m.Groups[1].Value.Trim().Length > 0)
            {
                p.Title = m.Groups[1].Value;
                p.Year = m.Groups[2].Value;
            }
            else
            {
                var j = Regex.Match(t, @"^(.+?)[\s\(\[-]+\b(" + Junk + @")\b", RegexOptions.IgnoreCase);
                p.Title = j.Success ? j.Groups[1].Value : t;
            }
            string title = p.Title;
            // Drop trailing bracketed tags like "(Eng Sub - CAM)" or "[720p]"
            string prev;
            do { prev = title; title = Regex.Replace(title, @"\s*[\[\(][^\]\)]*[\]\)]?\s*$", ""); } while (title != prev && title.Length > 0);
            title = Regex.Replace(title, @"\s*\b(" + Junk + @")\b.*$", "", RegexOptions.IgnoreCase);
            title = title.Trim(' ', '-', '(', '[', '_', ',');
            p.Title = title.Length > 0 ? title : t;
            return p;
        }

        // Prefers files named poster/cover/folder; otherwise a portrait image (most downloads ship the poster that way).
        public static string FindPoster(string dir)
        {
            try
            {
                string portrait = null;
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    if (!ImageExt.Contains(Path.GetExtension(f))) continue;
                    string n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    if (n.Contains("poster") || n.Contains("cover") || n == "folder" || n.Contains("front")) return f;
                    // Download-site banners (www.YTS.LT.jpg and friends) are ads, not posters.
                    if (Regex.IsMatch(n, @"yts|yify|www\.|torrent|rarbg|\.com|\.org")) continue;
                    if (portrait == null && IsPortrait(f)) portrait = f;
                }
                return portrait;
            }
            catch { return null; }
        }

        static bool IsPortrait(string file)
        {
            try
            {
                using (var fs = File.OpenRead(file))
                using (var img = Image.FromStream(fs, false, false))
                    return img.Height > img.Width * 1.15 && img.Width >= 150;
            }
            catch { return false; }
        }

        static bool IsVideo(string f) { return VideoExt.Contains(Path.GetExtension(f)); }

        static IEnumerable<string> SafeFiles(string dir)
        {
            try { return Directory.GetFiles(dir); } catch { return new string[0]; }
        }

        static IEnumerable<string> SafeDirs(string dir)
        {
            try
            {
                var list = new List<string>();
                foreach (var d in Directory.GetDirectories(dir))
                {
                    var attr = File.GetAttributes(d);
                    if ((attr & (FileAttributes.Hidden | FileAttributes.System)) == 0) list.Add(d);
                }
                return list;
            }
            catch { return new string[0]; }
        }

        public static string Hash(string s)
        {
            using (var md5 = MD5.Create())
            {
                var b = md5.ComputeHash(Encoding.UTF8.GetBytes(s.ToLowerInvariant()));
                var sb = new StringBuilder();
                for (int i = 0; i < 8; i++) sb.Append(b[i].ToString("x2"));
                return sb.ToString();
            }
        }

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int StrCmpLogicalW(string a, string b);
    }

    static class Discs
    {
        // One entry per optical drive: the disc in it, or a "no disc" placeholder.
        public static List<MediaItem> Scan()
        {
            var list = new List<MediaItem>();
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); } catch { return list; }
            foreach (var d in drives)
            {
                if (d.DriveType != DriveType.CDRom) continue;
                string root = d.Name, letter = root.Substring(0, 2);
                bool ready = false;
                try { ready = d.IsReady; } catch { }
                if (!ready)
                {
                    list.Add(new MediaItem { Kind = Kind.NoDisc, Title = "No disc in " + letter, Drive = letter, Path = root, Id = "nodisc" + letter[0] });
                    continue;
                }
                string label = "";
                long size = 0;
                try { label = d.VolumeLabel; size = d.TotalSize; } catch { }
                var it = new MediaItem { Kind = Kind.Disc, Drive = letter, Path = root, Id = "disc" + Library.Hash(letter + label + size) };
                it.Title = Pretty(label);
                string bd = root + @"BDMV\index.bdmv";
                if (File.Exists(bd))
                {
                    bool uhd = Library.IsUhd(bd);
                    it.DiscType = uhd ? "4K Ultra HD Blu-ray" : "Blu-ray";
                    it.Quality = uhd ? "4K" : "1080p";
                    it.Encrypted = Directory.Exists(root + "AACS");
                    it.Uri = "bluray:///" + letter + "/";
                    it.PosterPath = Library.DiscCover(root);
                }
                else if (Directory.Exists(root + "VIDEO_TS"))
                {
                    it.DiscType = "DVD";
                    it.Uri = "dvd:///" + letter + "/";
                }
                else it.DiscType = "Data disc";
                list.Add(it);
            }
            return list;
        }

        public static string Signature(List<MediaItem> discs)
        {
            var sb = new StringBuilder();
            foreach (var d in discs) sb.Append(d.Id).Append('|');
            return sb.ToString();
        }

        static string Pretty(string label)
        {
            if (string.IsNullOrEmpty(label)) return "Disc";
            string s = label.Replace('_', ' ').Trim();
            if (s == s.ToUpperInvariant()) s = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(s.ToLowerInvariant());
            return s;
        }
    }

    // Builds card images in the background: poster art if the folder has it, otherwise a frame from Windows' thumbnailer.
    class Thumbs
    {
        public static string CacheDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Theater", "cache"); } }
        public const int W = 480, H = 270;

        readonly List<MediaItem> queue = new List<MediaItem>();
        readonly Dictionary<string, string> meta = new Dictionary<string, string>();
        public Action Changed;
        public Func<string> VlcPath = () => "";
        public volatile bool Paused;   // set while a movie plays so frame grabs don't compete with it

        public Thumbs()
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                string mf = Path.Combine(CacheDir, "meta.txt");
                if (File.Exists(mf))
                    foreach (var line in File.ReadAllLines(mf))
                    {
                        int tab = line.IndexOf('\t');
                        if (tab > 0) meta[line.Substring(0, tab)] = line.Substring(tab + 1);
                    }
            }
            catch { }
            // One worker only loads saved thumbnails (instant); the others capture new frames side by side.
            int grabbers = Environment.ProcessorCount >= 8 ? 3 : Environment.ProcessorCount >= 4 ? 2 : 1;
            for (int i = 0; i <= grabbers; i++)
            {
                bool cheapOnly = i == 0;
                var t = new Thread(() => Run(cheapOnly)) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "thumbs" + i };
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            }
        }

        public static string CachePath(string id) { return Path.Combine(CacheDir, id + ".jpg"); }

        // Decoded thumbnails held in memory are capped; older ones reload from the disk cache when needed.
        readonly LinkedList<MediaItem> loaded = new LinkedList<MediaItem>();
        public int MaxLoaded = 40;
        public Func<bool> Busy = () => false;

        // UI thread: returns the thumbnail if available (loading it from the disk cache if necessary),
        // otherwise queues it for generation and returns null.
        public Bitmap Acquire(MediaItem it)
        {
            if (it == null || it.Kind == Kind.NoDisc || it.IsGenre) return null;
            var t = it.Thumb;
            if (t == null)
            {
                if (!it.ThumbReady) { Request(it); return null; }
                t = LoadBitmap(CachePath(it.Id));
                if (t == null) { it.ThumbReady = false; return null; }
                it.Thumb = t;
            }
            if (it.LruNode == null) it.LruNode = loaded.AddFirst(it);
            else if (loaded.First != it.LruNode) { loaded.Remove(it.LruNode); loaded.AddFirst(it.LruNode); }
            while (loaded.Count > MaxLoaded)
            {
                var old = loaded.Last.Value;
                loaded.RemoveLast();
                old.LruNode = null;
                var b = old.Thumb;
                old.Thumb = null;
                if (b != null) b.Dispose();
            }
            return t;
        }

        // Movies whose thumbnails are made ahead of time, before they're on screen.
        readonly List<MediaItem> later = new List<MediaItem>();
        readonly HashSet<MediaItem> laterSet = new HashSet<MediaItem>();

        readonly HashSet<MediaItem> aheadNow = new HashSet<MediaItem>();
        bool aheadWanted;   // something being made ahead came on screen meanwhile, so repaint when it's done

        public void Request(MediaItem it)
        {
            if (it == null) return;
            lock (queue)
            {
                if (laterSet.Remove(it)) later.Remove(it);   // on screen now: move it to the front of the line
                else if (it.ThumbQueued) { if (aheadNow.Contains(it)) aheadWanted = true; return; }
                it.ThumbQueued = true;
                queue.Add(it);
                Monitor.PulseAll(queue);
            }
        }

        // Queues thumbnails for every movie that doesn't have one yet; they're made while the app sits idle.
        public void Prefetch(IEnumerable<MediaItem> items)
        {
            var todo = new List<MediaItem>();
            var seen = new HashSet<MediaItem>();
            var stack = new Stack<MediaItem>(items);
            while (stack.Count > 0)
            {
                var it = stack.Pop();
                if (it == null || !seen.Add(it)) continue;
                if (it.Children != null) foreach (var c in it.Children) stack.Push(c);
                if (it.IsGenre || it.Kind == Kind.NoDisc || it.ThumbQueued || it.ThumbReady) continue;
                if (it.Kind != Kind.Movie && it.Kind != Kind.Collection) continue;
                todo.Add(it);
            }
            if (todo.Count == 0) return;
            Task.Run(() =>
            {
                // Checking the disk cache is quick but not free, so it happens off the UI thread.
                var missing = todo.FindAll(it => !File.Exists(CachePath(it.Id)));
                lock (queue)
                {
                    foreach (var it in missing)
                        if (!it.ThumbQueued) { it.ThumbQueued = true; later.Insert(0, it); laterSet.Add(it); }
                    Monitor.PulseAll(queue);
                }
            });
        }

        void Run(bool cheapOnly)
        {
            while (true)
            {
                MediaItem it = null;
                bool ahead = false;
                lock (queue)
                {
                    while (it == null)
                    {
                        // Already-saved thumbnails load first (newest request first); new frame captures,
                        // which are slow, wait while the user is scrolling or a movie is playing.
                        for (int i = queue.Count - 1; i >= 0 && it == null; i--)
                            if (IsCheap(queue[i])) { it = queue[i]; queue.RemoveAt(i); }
                        if (it != null) break;
                        if (cheapOnly || queue.Count + later.Count == 0) { Monitor.Wait(queue); continue; }
                        if (Busy() || Paused) { Monitor.Wait(queue, 250); continue; }
                        if (queue.Count > 0) { it = queue[queue.Count - 1]; queue.RemoveAt(queue.Count - 1); }
                        else { it = later[later.Count - 1]; later.RemoveAt(later.Count - 1); laterSet.Remove(it); aheadNow.Add(it); ahead = true; }
                    }
                }
                try { Load(it, ahead); } catch { }
                if (ahead)
                {
                    bool wanted;
                    lock (queue) { aheadNow.Remove(it); wanted = aheadWanted; aheadWanted = false; }
                    if (!wanted) continue;   // not on screen, so nothing to repaint
                }
                var h = Changed;
                if (h != null) h();
            }
        }

        static bool IsCheap(MediaItem it) { return it.Kind == Kind.NoDisc || it.IsGenre || File.Exists(CachePath(it.Id)); }

        // ahead: made in advance, so it's only saved to disk (memory holds just the thumbnails on screen).
        void Load(MediaItem it, bool ahead)
        {
            if (it.Kind == Kind.Movie) LoadMeta(it);
            if (it.Kind == Kind.NoDisc) return;
            string cache = CachePath(it.Id);
            if (File.Exists(cache)) { it.Thumb = LoadBitmap(cache); it.ThumbReady = it.Thumb != null; return; }

            Bitmap src = null;
            bool poster = false;
            if (it.PosterPath != null)
            {
                src = LoadBitmap(it.PosterPath);
                poster = src != null && src.Height > src.Width;
            }
            if (src == null && it.Kind == Kind.Collection && it.Children.Count > 0)
            {
                var first = it.Children[0];
                if (first.PosterPath != null) { src = LoadBitmap(first.PosterPath); poster = src != null && src.Height > src.Width; }
                if (src == null && first.Kind == Kind.Movie) { LoadMeta(first); src = Frame(first); }
            }
            if (src == null && it.Kind == Kind.Movie) src = Frame(it);
            if (src == null) return;
            var card = MakeCard(src, poster);
            src.Dispose();
            bool saved = false;
            try { card.Save(cache, ImageFormat.Jpeg); saved = true; } catch { }
            if (ahead && saved) { card.Dispose(); it.ThumbReady = true; return; }
            it.Thumb = card;
            it.ThumbReady = true;
        }

        void LoadMeta(MediaItem it)
        {
            string m;
            bool known;
            lock (meta) known = meta.TryGetValue(it.Id, out m);
            if (!known)
            {
                int width, height;
                double dur;
                ReadProps(it.Path, out width, out height, out dur);
                // Classify by width as well: widescreen movies are often cropped to e.g. 1920x800.
                string q = width >= 3200 || height >= 1600 ? "4K" : width >= 1800 || height >= 1000 ? "1080p" : width >= 1200 || height >= 700 ? "720p" : width > 0 ? "SD" : "";
                m = q + "\t" + dur.ToString(CultureInfo.InvariantCulture);
                lock (meta)
                {
                    meta[it.Id] = m;
                    try { File.AppendAllText(Path.Combine(CacheDir, "meta.txt"), it.Id + "\t" + m + "\r\n"); } catch { }
                }
            }
            var p = m.Split('\t');
            double d;
            if (p.Length >= 2 && double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out d))
            {
                it.Duration = d;
                if (p[0] != "") it.Quality = p[0];
            }
        }

        // A frame from well into the movie (opening frames are usually studio logos).
        Bitmap Frame(MediaItem it)
        {
            double at = it.Duration > 120 ? it.Duration * 0.18 : 300;
            var bmp = VlcFrame(it, at);
            if (bmp != null && Brightness(bmp) < 22)
            {
                var second = VlcFrame(it, it.Duration > 120 ? it.Duration * 0.42 : 900);
                if (second != null) { bmp.Dispose(); bmp = second; }
            }
            return bmp ?? ShellThumb(it.Path);
        }

        Bitmap VlcFrame(MediaItem it, double at)
        {
            string vlc = VlcPath();
            if (string.IsNullOrEmpty(vlc) || !File.Exists(vlc) || it.Path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase)) return null;
            string dir = Path.Combine(CacheDir, "tmp"), prefix = "f" + it.Id, outFile = Path.Combine(dir, prefix + ".jpg");
            try
            {
                Directory.CreateDirectory(dir);
                if (File.Exists(outFile)) File.Delete(outFile);
                // Saves the first decoded frame and stops after half a second (2.6 s per movie before, ~1.1 s now).
                string args = "-I dummy --dummy-quiet --no-audio --vout=dummy --avcodec-hw=none --no-osd --no-video-title-show"
                    + " --video-filter=scene --scene-format=jpg --scene-ratio=1 --scene-replace --scene-width=640"
                    + " --scene-prefix=" + prefix + " --scene-path=\"" + dir + "\""
                    + " --start-time=" + ((int)at) + " --run-time=0.5 --play-and-exit --no-one-instance \"" + it.Path + "\"";
                using (var p = Process.Start(new ProcessStartInfo(vlc, args) { UseShellExecute = false, CreateNoWindow = true }))
                {
                    Jobs.Track(p);
                    try { p.PriorityClass = ProcessPriorityClass.Idle; } catch { }
                    if (!p.WaitForExit(25000)) { try { p.Kill(); } catch { } }
                }
                if (!File.Exists(outFile)) return null;
                var bmp = LoadBitmap(outFile);
                try { File.Delete(outFile); } catch { }
                return bmp;
            }
            catch { return null; }
        }

        static int Brightness(Bitmap b)
        {
            long sum = 0;
            int n = 0;
            for (int y = b.Height / 8; y < b.Height; y += Math.Max(1, b.Height / 12))
                for (int x = b.Width / 8; x < b.Width; x += Math.Max(1, b.Width / 12))
                {
                    var c = b.GetPixel(x, y);
                    sum += (c.R * 3 + c.G * 6 + c.B) / 10;
                    n++;
                }
            return n > 0 ? (int)(sum / n) : 0;
        }

        static Bitmap MakeCard(Bitmap src, bool poster)
        {
            var bmp = new Bitmap(W, H, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                if (!poster)
                {
                    Ui.DrawCover(g, src, new RectangleF(0, 0, W, H));
                }
                else
                {
                    // Blurred, dimmed poster behind a sharp centered poster.
                    using (var tiny = new Bitmap(20, 12))
                    {
                        using (var tg = Graphics.FromImage(tiny))
                        {
                            tg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            Ui.DrawCover(tg, src, new RectangleF(0, 0, 20, 12));
                        }
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.DrawImage(tiny, new Rectangle(-24, -24, W + 48, H + 48));
                    }
                    using (var dim = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillRectangle(dim, 0, 0, W, H);
                    int ph = H - 28, pw = ph * src.Width / src.Height;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    using (var sh = new SolidBrush(Color.FromArgb(90, 0, 0, 0))) g.FillRectangle(sh, (W - pw) / 2 + 4, 18, pw, ph);
                    g.DrawImage(src, new Rectangle((W - pw) / 2, 14, pw, ph));
                }
            }
            return bmp;
        }

        public static Bitmap LoadBitmap(string path)
        {
            try
            {
                var bytes = File.ReadAllBytes(path);
                using (var ms = new MemoryStream(bytes))
                using (var img = Image.FromStream(ms))
                    return new Bitmap(img);
            }
            catch { return null; }
        }

        // ---- Windows Shell interop ----

        [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemImageFactory
        {
            [PreserveSig] int GetImage(NativeSize size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct NativeSize { public int Width, Height; }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        struct PropertyKey
        {
            public Guid Fmtid;
            public uint Pid;
            public PropertyKey(Guid g, uint pid) { Fmtid = g; Pid = pid; }
        }

        [StructLayout(LayoutKind.Explicit)]
        struct PropVariant
        {
            [FieldOffset(0)] public ushort vt;
            [FieldOffset(8)] public long val;
            [FieldOffset(16)] public long pad;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr pbc, int flags, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        [DllImport("ole32.dll")]
        static extern int PropVariantClear(ref PropVariant pv);

        [DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr h);

        static Bitmap ShellThumb(string path)
        {
            object o = null;
            try
            {
                var iid = typeof(IShellItemImageFactory).GUID;
                if (SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out o) != 0 || o == null) return null;
                IntPtr hbm;
                // SIIGBF_BIGGERSIZEOK | SIIGBF_THUMBNAILONLY: a real frame, never the generic file icon.
                if (((IShellItemImageFactory)o).GetImage(new NativeSize { Width = 640, Height = 640 }, 0x1 | 0x8, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                try { return Image.FromHbitmap(hbm); }
                finally { DeleteObject(hbm); }
            }
            catch { return null; }
            finally { if (o != null) Marshal.ReleaseComObject(o); }
        }

        static void ReadProps(string path, out int width, out int height, out double duration)
        {
            width = height = 0;
            duration = 0;
            IPropertyStore ps = null;
            try
            {
                var iid = typeof(IPropertyStore).GUID;
                if (SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0, ref iid, out ps) != 0 || ps == null) return;
                PropVariant v;
                var k = new PropertyKey(new Guid("64440491-4C8B-11D1-8B70-080036B11A03"), 4);   // System.Video.FrameHeight
                if (ps.GetValue(ref k, out v) == 0) { if (v.vt == 19) height = (int)(uint)v.val; PropVariantClear(ref v); }
                k = new PropertyKey(new Guid("64440491-4C8B-11D1-8B70-080036B11A03"), 3);       // System.Video.FrameWidth
                if (ps.GetValue(ref k, out v) == 0) { if (v.vt == 19) width = (int)(uint)v.val; PropVariantClear(ref v); }
                k = new PropertyKey(new Guid("64440490-4C8B-11D1-8B70-080036B11A03"), 3);       // System.Media.Duration (100ns)
                if (ps.GetValue(ref k, out v) == 0) { if (v.vt == 21) duration = (ulong)v.val / 1e7; PropVariantClear(ref v); }
            }
            catch { }
            finally { if (ps != null) Marshal.ReleaseComObject(ps); }
        }
    }

    // Saves the scanned library so the next launch shows it instantly; a fresh scan then runs in the background.
    static class LibraryCache
    {
        static string FilePath { get { return Path.Combine(Settings.Dir, "library.json"); } }

        // Bump the version when title parsing changes so the cache gets rebuilt once.
        static string CacheKey(List<string> roots) { return "v2|" + string.Join("|", roots); }

        public static void Save(Library lib, List<string> roots)
        {
            try
            {
                var d = new Dictionary<string, object> { { "roots", CacheKey(roots) }, { "items", lib.Items.ConvertAll(ToDict) } };
                File.WriteAllText(FilePath, new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(d));
            }
            catch { }
        }

        public static Library Load(List<string> roots)
        {
            try
            {
                if (!File.Exists(FilePath)) return null;
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var d = ser.DeserializeObject(File.ReadAllText(FilePath)) as Dictionary<string, object>;
                if (d == null || (d["roots"] as string) != CacheKey(roots)) return null;
                var lib = new Library();
                foreach (var o in (System.Collections.IList)d["items"]) lib.Items.Add(FromDict((Dictionary<string, object>)o, null));
                lib.BuildIndex();
                return lib;
            }
            catch { return null; }
        }

        static Dictionary<string, object> ToDict(MediaItem m)
        {
            var d = new Dictionary<string, object> {
                { "id", m.Id }, { "path", m.Path }, { "uri", m.Uri }, { "title", m.Title }, { "year", m.Year }, { "q", m.Quality },
                { "poster", m.PosterPath }, { "disc", m.DiscType }, { "hdr", m.Hdr }, { "enc", m.Encrypted }, { "kind", (int)m.Kind },
                { "size", m.Size }, { "added", m.Added.Ticks }
            };
            if (m.Children != null) d["children"] = m.Children.ConvertAll(ToDict);
            return d;
        }

        static MediaItem FromDict(Dictionary<string, object> d, MediaItem parent)
        {
            Func<string, string> s = k => { object o; return d.TryGetValue(k, out o) && o != null ? o.ToString() : null; };
            var m = new MediaItem
            {
                Id = s("id"), Path = s("path"), Uri = s("uri"), Title = s("title") ?? "", Year = s("year") ?? "", Quality = s("q") ?? "",
                PosterPath = s("poster"), DiscType = s("disc") ?? "", Hdr = s("hdr") == "True", Encrypted = s("enc") == "True",
                Kind = (Kind)int.Parse(s("kind")), Size = long.Parse(s("size")), Added = new DateTime(long.Parse(s("added"))), Parent = parent
            };
            object c;
            if (d.TryGetValue("children", out c) && c != null)
            {
                m.Children = new List<MediaItem>();
                foreach (var o in (System.Collections.IList)c) m.Children.Add(FromDict((Dictionary<string, object>)o, m));
            }
            return m;
        }
    }

    // Genres: a built-in list for the library (genres.txt, embedded) plus the user's own choices,
    // which are saved by item and always win.
    static class Genres
    {
        public static readonly string[] Order = {
            "Horror", "Comedy", "Action", "Superhero", "Sci-Fi", "Horror Comedy", "Slashers", "Zombies",
            "Creature Features", "Thriller", "Family & Animation", "TV Series", "Fantasy", "Crime", "Drama"
        };

        static Dictionary<string, List<string>> builtIn, mine;
        static readonly object gate = new object();
        static string MinePath { get { return Path.Combine(Settings.Dir, "my-genres.txt"); } }

        public static string Norm(string title)
        {
            string t = Regex.Replace((title ?? "").ToLowerInvariant(), "^(the|a|an) ", "");
            return Regex.Replace(t, "[^a-z0-9]", "");
        }

        static List<string> Split(string s)
        {
            var list = new List<string>();
            foreach (var part in s.Split(',')) { string g = part.Trim(); if (g.Length > 0 && !list.Contains(g)) list.Add(g); }
            return list;
        }

        static void EnsureLoaded()
        {
            if (builtIn != null) return;
            builtIn = new Dictionary<string, List<string>>();
            mine = new Dictionary<string, List<string>>();
            try
            {
                using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("genres.txt"))
                using (var rd = new StreamReader(s, Encoding.UTF8))
                {
                    string line;
                    while ((line = rd.ReadLine()) != null)
                    {
                        line = line.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        var p = line.Split('|');
                        if (p.Length < 3) continue;
                        string title = Norm(p[0]), year = p[1].Trim();
                        var g = Split(p[2]);
                        if (year.Length > 0) builtIn[title + "|" + year] = g;
                        if (!builtIn.ContainsKey(title)) builtIn[title] = g;
                    }
                }
            }
            catch { }
            try
            {
                if (File.Exists(MinePath))
                    foreach (var line in File.ReadAllLines(MinePath))
                    {
                        var p = line.Split('|');
                        if (p.Length >= 2 && p[0].Trim().Length > 0) mine[p[0].Trim()] = Split(p[1]);
                    }
            }
            catch { }
        }

        public static void Apply(Library lib)
        {
            lock (gate)
            {
                EnsureLoaded();
                foreach (var it in lib.Items) it.Genres = Lookup(it);
            }
        }

        static List<string> Lookup(MediaItem it)
        {
            List<string> g;
            if (mine.TryGetValue(it.Id, out g)) return new List<string>(g);
            string t = Norm(it.Title);
            if (it.Year != "" && builtIn.TryGetValue(t + "|" + it.Year, out g)) return new List<string>(g);
            if (builtIn.TryGetValue(t, out g)) return new List<string>(g);
            return new List<string>();
        }

        public static void Set(MediaItem it, List<string> genres)
        {
            lock (gate)
            {
                EnsureLoaded();
                mine[it.Id] = new List<string>(genres);
                it.Genres = new List<string>(genres);
                try
                {
                    Directory.CreateDirectory(Settings.Dir);
                    var lines = new List<string> { "# Genres you chose in Eric's Home Theater:  item id | genres | title (for reference)" };
                    foreach (var kv in mine) lines.Add(kv.Key + " | " + string.Join(", ", kv.Value) + (kv.Key == it.Id ? " | " + it.Title : ""));
                    File.WriteAllLines(MinePath, lines);
                }
                catch { }
            }
        }
    }
}