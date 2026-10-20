// Drives VLC as the playback engine. VLC renders into a window we own (--drawable-hwnd)
// and is controlled through its local web interface, bound to 127.0.0.1 only.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Theater
{
    class VlcStatus
    {
        public string State = "stopped";
        public double Time, Length, Position;
        public int Volume = 256;
        public int Chapter = -1, Chapters;
        public string Resolution = "", Hdr = "", AudioInfo = "";
        public double AudioDelay, SubDelay;   // seconds
        public int AudioCount, SubCount;
    }

    class Vlc
    {
        Process proc;
        int port;
        string pw;
        public string Error = "";
        IntPtr surface;
        readonly object startLock = new object();
        readonly BlockingCollection<System.Action> queue = new BlockingCollection<System.Action>();

        public Vlc()
        {
            // All commands run one at a time, in order, off the UI thread.
            new Thread(() => { foreach (var a in queue.GetConsumingEnumerable()) { try { a(); } catch { } } }) { IsBackground = true, Name = "vlc" }.Start();
        }

        public void Post(System.Action a) { queue.Add(a); }

        public bool Running
        {
            get { try { return proc != null && !proc.HasExited; } catch { return false; } }
        }

        string extraArgs = "";

        public bool Start(string exe, IntPtr hwnd, string extra)
        {
            lock (startLock)
            {
                if (Running && surface == hwnd && extra == extraArgs) return true;
                if (Running) { Kill(); try { proc.WaitForExit(3000); } catch { } }
                surface = hwnd;
                extraArgs = extra ?? "";
                if (!File.Exists(exe)) { Error = "VLC wasn't found at " + exe + ". Set its location in Settings."; return false; }
                port = FreePort();
                do { rcPort = FreePort(); } while (rcPort == port);
                pw = Guid.NewGuid().ToString("N");
                string args = string.Join(" ", new[] {
                    "--intf=dummy", "--dummy-quiet", "--extraintf=http:rc",
                    "--http-host=127.0.0.1", "--http-port=" + port, "--http-password=" + pw,
                    "--rc-host=127.0.0.1:" + rcPort, "--rc-quiet",
                    "--drawable-hwnd=" + hwnd.ToInt64(),
                    "--no-video-title-show", "--no-mouse-events", "--no-keyboard-events",
                    "--no-one-instance", "--play-and-stop", "--no-loop", "--no-repeat", "--no-random",
                    "--no-snapshot-preview", "--no-metadata-network-access", "--osd"
                }) + (extraArgs.Length > 0 ? " " + extraArgs : "");
                try
                {
                    proc = Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true });
                    Jobs.Track(proc);
                }
                catch (Exception ex) { Error = "Couldn't start VLC: " + ex.Message; return false; }
                for (int i = 0; i < 80; i++)
                {
                    if (Request("") != null) return true;
                    if (proc.HasExited) break;
                    Thread.Sleep(100);
                }
                Error = "VLC didn't start.";
                Kill();
                return false;
            }
        }

        public void Kill()
        {
            try { if (Running) proc.Kill(); } catch { }
        }

        static int FreePort()
        {
            var l = new TcpListener(IPAddress.Loopback, 0);
            l.Start();
            int p = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return p;
        }

        public string Request(string query)
        {
            if (port == 0) return null;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/requests/status.json" + (query.Length > 0 ? "?" + query : ""));
                req.Proxy = null;
                req.Timeout = 3000;
                req.Headers["Authorization"] = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pw));
                using (var resp = req.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return rd.ReadToEnd();
            }
            catch { return null; }
        }

        public void Cmd(string command, string val = null)
        {
            Request("command=" + command + (val != null ? "&val=" + Uri.EscapeDataString(val) : ""));
        }

        // Hotkey actions such as "nav-up", "disc-menu", "subtitle-text-scale-up", "chapter-next".
        public void Action(string name) { Cmd("key", name); }

        // ---------- audio and subtitle tracks ----------
        // VLC's web interface can't list tracks, so these use its text control channel (also 127.0.0.1 only).

        int rcPort;
        readonly object rcLock = new object();

        public class Track { public int Id; public string Name = ""; public bool On; }

        string Rc(string command)
        {
            if (rcPort == 0 || !Running) return "";
            lock (rcLock)
            {
                try
                {
                    using (var c = new TcpClient())
                    {
                        if (!c.ConnectAsync("127.0.0.1", rcPort).Wait(1000)) return "";
                        var s = c.GetStream();
                        var cmd = Encoding.UTF8.GetBytes(command + "\n");
                        s.Write(cmd, 0, cmd.Length);
                        var sb = new StringBuilder();
                        var buf = new byte[16384];
                        var until = DateTime.Now.AddMilliseconds(1500);
                        // Read until VLC confirms the command ("<command>: returned ...").
                        while (DateTime.Now < until && sb.ToString().IndexOf("returned", StringComparison.Ordinal) < 0)
                        {
                            if (s.DataAvailable) { int n = s.Read(buf, 0, buf.Length); if (n <= 0) break; sb.Append(Encoding.UTF8.GetString(buf, 0, n)); }
                            else Thread.Sleep(20);
                        }
                        return sb.ToString();
                    }
                }
                catch { return ""; }
            }
        }

        // kind: "atrack" (audio) or "strack" (subtitles). Includes "Off" (id -1) when VLC offers it.
        public List<Track> Tracks(string kind)
        {
            var list = new List<Track>();
            if (kind != "atrack" && kind != "strack") return list;
            foreach (var line in Rc(kind).Split('\n'))
            {
                var m = Regex.Match(line.TrimEnd('\r'), @"^\|\s*(-?\d+) - (.*?)( \*)?\s*$");
                if (!m.Success) continue;
                list.Add(new Track { Id = int.Parse(m.Groups[1].Value), Name = TrackName(m.Groups[2].Value), On = m.Groups[3].Success });
            }
            // Same names (two "English" tracks): number them so they can be told apart.
            foreach (var g in list.GroupBy(t => t.Name).Where(g => g.Count() > 1))
            {
                int i = 1;
                foreach (var t in g) t.Name += " (" + i++ + ")";
            }
            return list;
        }

        // "Track 2 - [English]" -> "English";  "Ita Ac3 5.1 - [Italian]" -> "Italian Â· Ita Ac3 5.1";  "Disable" -> "Off"
        static string TrackName(string raw)
        {
            if (raw == "Disable") return "Off";
            var m = Regex.Match(raw, @"^(.*?)\s*-\s*\[(.+)\]$");
            if (!m.Success) return raw;
            string desc = m.Groups[1].Value.Trim(), lang = m.Groups[2].Value.Trim();
            return Regex.IsMatch(desc, @"^Track \d+$") || desc.Length == 0 ? lang : lang + " \u00B7 " + desc;
        }

        public void SelectTrack(string kind, int id)
        {
            if (kind == "atrack" || kind == "strack") Rc(kind + " " + id);
        }

        public void Play(string input)
        {
            Request("command=pl_stop");
            Request("command=pl_empty");
            Request("command=in_play&input=" + Uri.EscapeDataString(input));
        }

        public VlcStatus Status()
        {
            string s = Request("");
            if (s == null) return null;
            try
            {
                var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(s) as Dictionary<string, object>;
                if (d == null) return null;
                var st = new VlcStatus
                {
                    State = Str(d, "state"),
                    Time = Num(d, "time"),
                    Length = Num(d, "length"),
                    Position = Num(d, "position"),
                    Volume = (int)Num(d, "volume"),
                    AudioDelay = Num(d, "audiodelay"),
                    SubDelay = Num(d, "subtitledelay")
                };
                object o;
                var info = d.TryGetValue("information", out o) ? o as Dictionary<string, object> : null;
                if (info != null)
                {
                    st.Chapter = info.ContainsKey("chapter") ? (int)Num(info, "chapter") : -1;
                    var ch = info.TryGetValue("chapters", out o) ? o as IList : null;
                    st.Chapters = ch != null ? ch.Count : 0;
                    var cat = info.TryGetValue("category", out o) ? o as Dictionary<string, object> : null;
                    if (cat != null)
                        foreach (var kv in cat)
                        {
                            var stream = kv.Value as Dictionary<string, object>;
                            if (stream == null || !kv.Key.StartsWith("Stream")) continue;
                            string type = Str(stream, "Type");
                            if (type == "Video")
                            {
                                string res = Str(stream, "Video_resolution");
                                if (res == "") res = Str(stream, "Resolution");
                                if (res != "") st.Resolution = res;
                                string tf = Str(stream, "Color_transfer_function");
                                if (tf.Contains("2084") || tf.Contains("PQ")) st.Hdr = "HDR10";
                                else if (tf.Contains("HLG") || tf.Contains("B67")) st.Hdr = "HLG";
                            }
                            else if (type == "Audio")
                            {
                                st.AudioCount++;
                                if (st.AudioInfo == "") st.AudioInfo = (Str(stream, "Language") + " " + Channels(Str(stream, "Channels"))).Trim();
                            }
                            else if (type == "Subtitle") st.SubCount++;
                        }
                }
                return st;
            }
            catch { return null; }
        }

        static string Channels(string c)
        {
            if (c.Contains("3F2R/LFE")) return "5.1";
            if (c.Contains("3F4R/LFE") || c.Contains("7.1")) return "7.1";
            if (c.Contains("Stereo")) return "Stereo";
            if (c.Contains("Mono")) return "Mono";
            return "";
        }

        static string Str(Dictionary<string, object> d, string k)
        {
            object o;
            return d.TryGetValue(k, out o) && o != null ? o.ToString() : "";
        }

        static double Num(Dictionary<string, object> d, string k)
        {
            object o;
            if (!d.TryGetValue(k, out o) || o == null) return 0;
            try { return Convert.ToDouble(o, System.Globalization.CultureInfo.InvariantCulture); } catch { return 0; }
        }
    }
}


namespace Theater
{
    // Puts helper processes (VLC) in a job object so Windows closes them if Theater exits or crashes.
    static class Jobs
    {
        static IntPtr job = IntPtr.Zero;
        static readonly object gate = new object();

        public static void Track(System.Diagnostics.Process p)
        {
            try
            {
                lock (gate)
                {
                    if (job == IntPtr.Zero)
                    {
                        job = CreateJobObject(IntPtr.Zero, null);
                        var info = new ExtLimit();
                        info.Basic.LimitFlags = 0x2000;   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                        int len = System.Runtime.InteropServices.Marshal.SizeOf(typeof(ExtLimit));
                        IntPtr ptr = System.Runtime.InteropServices.Marshal.AllocHGlobal(len);
                        try
                        {
                            System.Runtime.InteropServices.Marshal.StructureToPtr(info, ptr, false);
                            SetInformationJobObject(job, 9, ptr, (uint)len);
                        }
                        finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(ptr); }
                    }
                    AssignProcessToJobObject(job, p.Handle);
                }
            }
            catch { }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct BasicLimit
        {
            public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct IoCounters { public ulong a, b, c, d, e, f; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct ExtLimit
        {
            public BasicLimit Basic;
            public IoCounters Io;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr CreateJobObject(IntPtr attrs, string name);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint len);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    }
}