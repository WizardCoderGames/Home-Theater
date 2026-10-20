// A tiny web server for the phone remote. Any phone on the same Wi-Fi can open
// http://<this pc>:<port>/ ; every API call must carry the 4-digit code shown in Theater.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace Theater
{
    class RemoteServer
    {
        readonly TheaterForm form;
        TcpListener listener;
        byte[] page;
        public string Error;
        // Phones that asked to pair, and when each last asked (so refreshing the page doesn't spam the TV).
        readonly Dictionary<string, DateTime> pairAsked = new Dictionary<string, DateTime>();

        public RemoteServer(TheaterForm form) { this.form = form; }

        public bool Start(int port)
        {
            try
            {
                using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("remote.html"))
                using (var ms = new MemoryStream()) { s.CopyTo(ms); page = ms.ToArray(); }
                listener = new TcpListener(Environment.GetEnvironmentVariable("THEATER_TEST") == "1" ? IPAddress.Loopback : IPAddress.Any, port);
                listener.Start();
                // Keep child processes (VLC) from inheriting the listening socket.
                SetHandleInformation(listener.Server.Handle, 1, 0);
                new Thread(AcceptLoop) { IsBackground = true, Name = "remote" }.Start();
                return true;
            }
            catch (Exception ex) { Error = ex.Message; return false; }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);

        public void Stop() { try { if (listener != null) listener.Stop(); } catch { } }

        public static string LanIp()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    if (props.GatewayAddresses.Count == 0) continue;
                    foreach (var a in props.UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork) return a.Address.ToString();
                }
            }
            catch { }
            return "127.0.0.1";
        }

        void AcceptLoop()
        {
            while (true)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); }
                catch { return; }
                ThreadPool.QueueUserWorkItem(_ => Handle(c));
            }
        }

        void Handle(TcpClient c)
        {
            try
            {
                using (c)
                {
                    c.ReceiveTimeout = 5000;
                    var ns = c.GetStream();
                    string head = ReadHead(ns);
                    if (head == null) return;
                    string ip = ((IPEndPoint)c.Client.RemoteEndPoint).Address.ToString();
                    string first = head.Split('\n')[0].Trim();
                    var parts = first.Split(' ');
                    if (parts.Length < 2) return;
                    string target = parts[1], path = target, qs = "";
                    int q = target.IndexOf('?');
                    if (q >= 0) { path = target.Substring(0, q); qs = target.Substring(q + 1); }
                    var query = ParseQuery(qs);
                    Route(ns, path, query, ip, head);
                }
            }
            catch { }
        }

        static string ReadHead(NetworkStream ns)
        {
            var buf = new List<byte>();
            int b;
            while (buf.Count < 16384 && (b = ns.ReadByte()) >= 0)
            {
                buf.Add((byte)b);
                int n = buf.Count;
                if (n >= 4 && buf[n - 4] == 13 && buf[n - 3] == 10 && buf[n - 2] == 13 && buf[n - 1] == 10)
                    return Encoding.ASCII.GetString(buf.ToArray());
            }
            return null;
        }

        static Dictionary<string, string> ParseQuery(string qs)
        {
            var d = new Dictionary<string, string>();
            foreach (var pair in qs.Split('&'))
            {
                if (pair.Length == 0) continue;
                int eq = pair.IndexOf('=');
                string k = eq < 0 ? pair : pair.Substring(0, eq), v = eq < 0 ? "" : pair.Substring(eq + 1);
                d[Uri.UnescapeDataString(k.Replace('+', ' '))] = Uri.UnescapeDataString(v.Replace('+', ' '));
            }
            return d;
        }

        static string DeviceKind(string head)
        {
            var ua = System.Text.RegularExpressions.Regex.Match(head, @"^User-Agent:\s*(.+)$", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            string s = ua.Success ? ua.Groups[1].Value : "";
            if (s.Contains("Android")) return "Android";
            if (s.Contains("iPhone") || s.Contains("iPad")) return "iPhone";
            return "browser";
        }

        void Route(NetworkStream ns, string path, Dictionary<string, string> q, string ip, string head)
        {
            if (path == "/" || path == "/index.html") { Send(ns, 200, "text/html; charset=utf-8", page); return; }
            if (path == "/manifest.json")
            {
                Send(ns, 200, "application/manifest+json", "{\"name\":\"Eric's Home Theater Remote\",\"short_name\":\"Home Theater\",\"start_url\":\"/\",\"display\":\"standalone\",\"background_color\":\"#0b0c10\",\"theme_color\":\"#0b0c10\",\"icons\":[{\"src\":\"/icon.svg\",\"sizes\":\"any\",\"type\":\"image/svg+xml\"}]}");
                return;
            }
            if (path == "/icon.svg")
            {
                Send(ns, 200, "image/svg+xml", "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 96 96'><rect width='96' height='96' rx='22' fill='#ffb020'/><path d='M38 28 L70 48 L38 68 Z' fill='#0b0c10'/></svg>");
                return;
            }
            if (!path.StartsWith("/api/")) { Send(ns, 404, "text/plain", "Not found"); return; }

            // No code needed: a phone just opened the page and wants the code shown on the TV.
            if (path == "/api/pair")
            {
                bool show;
                lock (pairAsked)
                {
                    DateTime last;
                    show = !pairAsked.TryGetValue(ip, out last) || (DateTime.Now - last).TotalSeconds > 8;
                    pairAsked[ip] = DateTime.Now;
                }
                string who = DeviceKind(head) + ", " + ip;
                if (show) form.BeginInvoke((Action)(() => form.PairRequest(who)));
                Send(ns, 200, "application/json", "{\"ok\":true}");
                return;
            }

            string code;
            if (!q.TryGetValue("code", out code) || code != form.Cfg.Code)
            {
                Send(ns, 401, "application/json", "{\"error\":\"code\"}");
                return;
            }
            string v;
            switch (path)
            {
                case "/api/ping":
                    bool paired;
                    lock (pairAsked) paired = pairAsked.Remove(ip);
                    if (paired) form.BeginInvoke((Action)(() => form.PairDone()));
                    Send(ns, 200, "application/json", "{\"ok\":true,\"name\":\"" + AppInfo.Name + "\"}");
                    break;
                case "/api/state":
                    Send(ns, 200, "application/json", form.StateJson);
                    break;
                case "/api/key":
                    if (q.TryGetValue("k", out v)) form.BeginInvoke((Action)(() => form.RemoteKey(v)));
                    Send(ns, 200, "application/json", "{\"ok\":true}");
                    break;
                case "/api/text":
                    q.TryGetValue("t", out v);
                    form.BeginInvoke((Action)(() => form.RemoteText(v ?? "")));
                    Send(ns, 200, "application/json", "{\"ok\":true}");
                    break;
                case "/api/tracks":
                    Send(ns, 200, "application/json", form.Player.TracksJson());
                    break;
                case "/api/track":
                    {
                        string kind; int id;
                        if (q.TryGetValue("kind", out kind) && q.TryGetValue("id", out v) && int.TryParse(v, out id))
                            form.BeginInvoke((Action)(() => form.Player.SetTrack(kind, id)));
                        Send(ns, 200, "application/json", "{\"ok\":true}");
                    }
                    break;
                case "/api/delay":
                    {
                        string kind; double sec;
                        if (q.TryGetValue("kind", out kind) && q.TryGetValue("sec", out v)
                            && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out sec))
                            form.BeginInvoke((Action)(() => form.Player.SetDelay(kind, sec)));
                        Send(ns, 200, "application/json", "{\"ok\":true}");
                    }
                    break;
                case "/api/subsize":
                    if (q.TryGetValue("d", out v)) form.BeginInvoke((Action)(() => form.Player.SubtitleSize(v)));
                    Send(ns, 200, "application/json", "{\"ok\":true}");
                    break;
                case "/api/play":
                    if (q.TryGetValue("id", out v)) form.BeginInvoke((Action)(() => form.RemotePlay(v)));
                    Send(ns, 200, "application/json", "{\"ok\":true}");
                    break;
                case "/api/library":
                    q.TryGetValue("parent", out v);
                    Send(ns, 200, "application/json", LibraryJson(v));
                    break;
                case "/api/thumb":
                    if (q.TryGetValue("id", out v) && Regex(v))
                    {
                        string f = Thumbs.CachePath(v);
                        if (File.Exists(f)) { Send(ns, 200, "image/jpeg", File.ReadAllBytes(f), true); break; }
                        MediaItem it;
                        if (form.Lib.ById.TryGetValue(v, out it)) form.Thumbs.Request(it);
                    }
                    Send(ns, 404, "text/plain", "");
                    break;
                default:
                    Send(ns, 404, "application/json", "{\"error\":\"unknown\"}");
                    break;
            }
        }

        static bool Regex(string id)
        {
            foreach (char ch in id) if (!char.IsLetterOrDigit(ch)) return false;
            return id.Length > 0 && id.Length < 40;
        }

        string LibraryJson(string parent)
        {
            var lib = form.Lib;
            List<MediaItem> items = lib.Items;
            MediaItem p;
            if (!string.IsNullOrEmpty(parent) && lib.ById.TryGetValue(parent, out p) && p.Children != null) items = p.Children;
            var list = new List<Dictionary<string, object>>();
            foreach (var it in items)
            {
                var w = form.Hist.Get(it.Path);
                list.Add(new Dictionary<string, object> {
                    { "id", it.Id }, { "t", it.Title }, { "y", it.Year }, { "q", it.Quality }, { "h", it.Hdr },
                    { "k", it.Kind == Kind.Collection ? "c" : it.Kind == Kind.DiscFolder ? "d" : "m" },
                    { "n", it.Children != null ? it.Children.Count : 0 },
                    { "p", w != null && !w.Watched ? Math.Round(w.Fraction, 3) : 0 },
                    { "w", w != null && w.Watched }
                });
            }
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(list);
        }

        static void Send(NetworkStream ns, int status, string type, string body)
        {
            Send(ns, status, type, Encoding.UTF8.GetBytes(body));
        }

        static void Send(NetworkStream ns, int status, string type, byte[] body, bool cache = false)
        {
            string reason = status == 200 ? "OK" : status == 401 ? "Unauthorized" : "Not Found";
            string head = "HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length
                + "\r\nCache-Control: " + (cache ? "max-age=86400" : "no-store") + "\r\nConnection: close\r\n\r\n";
            var h = Encoding.ASCII.GetBytes(head);
            ns.Write(h, 0, h.Length);
            ns.Write(body, 0, body.Length);
        }
    }
}
