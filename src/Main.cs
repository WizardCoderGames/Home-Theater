// Theater - a TV-style movie player for Windows. Main window, input routing and playback.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Eric's Home Theater")]
[assembly: System.Reflection.AssemblyProduct("Eric's Home Theater")]
[assembly: System.Reflection.AssemblyVersion("1.6.2.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.6.2.0")]

namespace Theater
{
    static class AppInfo
    {
        public const string Name = "Eric's Home Theater";
    }

    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            try { SetProcessDPIAware(); } catch { }
            AppDomain.CurrentDomain.UnhandledException += (s, e) => { Log(e.ExceptionObject as Exception); DisplayModes.RestoreAll(); };
            AppDomain.CurrentDomain.ProcessExit += (s, e) => DisplayModes.RestoreAll();
            Application.ThreadException += (s, e) => { Log(e.Exception); MessageBox.Show("Something went wrong: " + e.Exception.Message, AppInfo.Name); };
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string name = "EricsHomeTheater" + (Environment.GetEnvironmentVariable("THEATER_TEST") == "1" ? ".test" : "");
            bool first;
            using (new System.Threading.Mutex(true, name + ".instance", out first))
            {
                if (!first)
                {
                    try { using (var ev = System.Threading.EventWaitHandle.OpenExisting(name + ".show")) ev.Set(); } catch { }
                    return;
                }
                ShowSignal = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, name + ".show");
                Application.Run(new TheaterForm());
            }
        }

        // Set by a second copy of the app asking this one to come back to the front.
        public static System.Threading.EventWaitHandle ShowSignal;

        public static void Log(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Settings.Dir, "error.log"), DateTime.Now + "  " + ex + "\r\n\r\n"); } catch { }
        }
    }

    class TheaterForm : Form
    {
        public Settings Cfg;
        public History Hist;
        public Library Lib = new Library();
        public Thumbs Thumbs;
        public CardArt Art;
        public float ViewScale { get { return scale; } }
        public List<MediaItem> DiscItems = new List<MediaItem>();
        public readonly Vlc Vlc = new Vlc();
        public Player Player;
        public string RemoteUrl = "";
        public readonly SystemStats Stats = new SystemStats();
        public string UpdateStatus = "Not checked yet";
        ReleaseInfo availableUpdate;
        string downloadedSetup;
        bool updateBusy, updatePostponed;
        readonly Timer updateTimer = new Timer();
        Dialog pairDialog, qrDialog;
        // Last time someone used the keyboard, mouse or a controller on this PC (not the phone).
        DateTime lastLocalInput = DateTime.MinValue;
        bool mouseSeen;
        Gamepad pad;

        readonly Timer padTimer = new Timer { Interval = 33 };
        bool phoneWasConnected, qrShownThisGap, startupQrDone;
        readonly DateTime startedAt = DateTime.Now;
        string displayDevice;
        bool serverOk;
        FormWindowState lastState = FormWindowState.Normal;
        public volatile string StateJson = "{}";

        readonly List<Screen> stack = new List<Screen>();
        readonly Panel video;
        readonly Timer anim = new Timer { Interval = 15 };
        readonly Timer slow = new Timer { Interval = 250 };
        readonly Timer discTimer = new Timer { Interval = 3000 };
        Dialog dialog;
        string toast;
        DateTime toastUntil;
        volatile bool thumbsDirty;
        bool scanning, discBusy, cursorHidden;
        // USB drives whose movies are in the library right now.
        public List<UsbDrive> Usb = new List<UsbDrive>();
        bool usbBusy, rescanAgain;
        string usbSig = "", usbRemovedNote;
        readonly List<UsbDrive> usbAnnounce = new List<UsbDrive>();
        string discSig = "";
        float scale = 1, ox, oy;
        DateTime lastMouse = DateTime.MinValue;        // real mouse use
        DateTime lastOtherInput = DateTime.MinValue;   // keyboard, controller or phone remote
        Point lastMousePos;
        RemoteServer server;
        int slowTicks;
        Rectangle windowedBounds = Rectangle.Empty;
        DateTime lastInput = DateTime.MinValue, lastThumbPaint = DateTime.MinValue;
        string lastClock = "";
        // Frame timing: if this PC can't animate smoothly, animations switch to instant moves.
        readonly Stopwatch paintClock = new Stopwatch();
        double avgPaintMs, maxPaintMs;
        int animFrames, slowFrames, spikes;

        public Screen Current { get { return stack[stack.Count - 1]; } }
        public IntPtr VideoHandle { get { return video.Handle; } }

        public TheaterForm()
        {
            Cfg = Settings.Load();
            string lastCode = Cfg.Code;
            do { Cfg.Code = Settings.NewCode(); } while (Cfg.Code == lastCode);
            Cfg.Save();
            Hist = History.Load();
            Thumbs = new Thumbs();
            Thumbs.Changed = () => thumbsDirty = true;
            Thumbs.VlcPath = () => Cfg.VlcPath;
            Thumbs.Busy = () => (DateTime.Now - lastInput).TotalSeconds < 0.8;
            Art = new CardArt(this);
            Ui.Smooth = Cfg.Anim != "off";
            Ui.SetTheme(Cfg.Theme);
            Sounds.Level = Cfg.MenuSounds;

            Text = AppInfo.Name;
            BackColor = Ui.Bg;
            KeyPreview = true;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(1280, 760);
            MinimumSize = new Size(640, 400);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            video = new Panel { BackColor = Color.Black, Dock = DockStyle.Fill, Visible = false };
            Controls.Add(video);
            Player = new Player(this);
            stack.Add(new HomeScreen(this));

            anim.Tick += (s, e) =>
            {
                bool moving = Current.Animate();
                Invalidate();
                if (!moving) anim.Stop();
            };
            slow.Tick += (s, e) => SlowTick();
            pad = new Gamepad(LocalAct);
            // Read controllers only while this app is in front.
            padTimer.Tick += (s, e) => { var a = Form.ActiveForm; if (a == this || a is PlayerOverlay) pad.Poll(); };
            discTimer.Tick += (s, e) => { PollDiscs(); PollUsb(); };
            Load += (s, e) => ApplyDisplayMode();
            Shown += (s, e) =>
            {
                // Show the library from last time instantly, then refresh it in the background.
                var cached = LibraryCache.Load(Cfg.Roots);
                if (cached != null) { Lib = cached; foreach (var sc in stack) sc.Refresh(); Kick(); }
                Rescan(cached != null);
                StartRemote();
                AutoWifi();
                PollDiscs();
                PollUsb();
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += (s2, e2) => { try { BeginInvoke((Action)UpdateRemoteUrl); } catch { } };
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (s2, e2) => { try { BeginInvoke((Action)FitScreen); } catch { } };
                slow.Start();
                discTimer.Start();
                padTimer.Start();
                // Another copy of the app being opened means "come back".
                if (Program.ShowSignal != null)
                    new System.Threading.Thread(() =>
                    {
                        while (true)
                        {
                            Program.ShowSignal.WaitOne();
                            try { BeginInvoke((Action)BringBack); } catch { return; }
                        }
                    }) { IsBackground = true, Name = "show-signal" }.Start();
                // First update check shortly after start, then every 6 hours.
                updateTimer.Interval = Environment.GetEnvironmentVariable("THEATER_TEST") == "1" ? 3000 : 30000;
                updateTimer.Tick += (s2, e2) => { updateTimer.Interval = 6 * 60 * 60 * 1000; CheckForUpdates(false); };
                updateTimer.Start();
            };
            LocationChanged += (s, e) => { if (Player.Active) Player.Reposition(); };
            SizeChanged += (s, e) =>
            {
                if (Player.Active) Player.Reposition();
                // Give the screen its normal resolution back while minimized; re-apply when restored.
                if (WindowState == FormWindowState.Minimized && lastState != FormWindowState.Minimized) DisplayModes.RestoreAll();
                else if (WindowState != FormWindowState.Minimized && lastState == FormWindowState.Minimized && !Cfg.Windowed) ApplyDisplayMode();
                lastState = WindowState;
            };
            FormClosing += (s, e) =>
            {

                Player.Stop();
                Vlc.Kill();
                Stats.Stop();
                if (server != null) server.Stop();
                DisplayModes.RestoreAll();
            };
        }

        // ---------- library, discs, remote ----------

        public void Rescan() { Rescan(false); }

        void Rescan(bool quiet)
        {
            if (scanning) { rescanAgain = true; return; }   // run once more when this scan finishes
            scanning = true;
            if (!quiet) ShowToast("Scanning your movies\u2026", 60);
            var folders = new List<string>(Cfg.Roots);
            var usb = new List<UsbDrive>(Usb);
            var roots = new List<string>(folders);
            roots.AddRange(usb.Select(d => d.Root));
            string before = Lib.Items.Count > 0 ? Lib.Signature() : "";
            Task.Run(() =>
            {
                var lib = Library.Scan(roots);
                string sig = lib.Signature();
                // USB drives come and go, so only a library without them is remembered for next time.
                if (sig != before && usb.Count == 0) LibraryCache.Save(lib, folders);
                return sig == before ? null : lib;   // null: nothing changed, keep what's on screen
            }).ContinueWith(t => BeginInvoke((Action)(() =>
            {
                scanning = false;
                if (t.IsFaulted) { if (!quiet) ShowToast("Couldn't scan your movie folders", 4); }
                else
                {
                    if (t.Result != null)
                    {
                        Lib = t.Result;
                        foreach (var s in stack) s.Refresh();
                    }
                    string usbNote = UsbAnnouncement() ?? usbRemovedNote;
                    usbRemovedNote = null;
                    if (usbNote != null) ShowToast(usbNote, 5);
                    else if (!quiet || t.Result != null) ShowToast(Lib.Items.Count + (Lib.Items.Count == 1 ? " title" : " titles") + " in your library", 3);
                    else toast = null;
                }
                Kick();
                if (!t.IsFaulted) Thumbs.Prefetch(Lib.Items);
                if (rescanAgain) { rescanAgain = false; Rescan(true); }
            })));
        }

        // ---------- USB drives ----------

        // Checks which USB drives are plugged in; when that changes, the library is rescanned with them.
        void PollUsb()
        {
            if (usbBusy) return;
            usbBusy = true;
            var folders = new List<string>(Cfg.Roots);
            bool auto = Cfg.UsbAuto;
            Task.Run(() => auto ? UsbDrives.Find(folders) : new List<UsbDrive>()).ContinueWith(t => BeginInvoke((Action)(() =>
            {
                usbBusy = false;
                if (t.IsFaulted) return;
                var found = t.Result;
                string sig = UsbDrives.Signature(found);
                if (sig == usbSig) return;
                var added = found.Where(d => !Usb.Any(o => o.Root == d.Root && o.Label == d.Label)).ToList();
                var removed = Usb.Where(o => !found.Any(d => d.Root == o.Root && d.Label == o.Label)).ToList();
                usbSig = sig;
                Usb = found;
                usbAnnounce.AddRange(added);
                if (removed.Count > 0)
                    usbRemovedNote = removed.Count == 1 ? "USB drive " + removed[0].Display + " removed \u2014 its movies are hidden until it's back" : removed.Count + " USB drives removed";
                if (added.Count > 0)
                    ShowToast("Looking for movies on " + (added.Count == 1 ? added[0].Display : "the USB drives") + "\u2026", 30);
                if (Current is SettingsScreen) Current.Refresh();
                Rescan(true);
            })));
        }

        // "USB drive "Movies" connected - 12 titles added", for drives that just came in.
        string UsbAnnouncement()
        {
            var still = usbAnnounce.Where(d => Usb.Any(o => o.Root == d.Root && o.Label == d.Label)).ToList();
            usbAnnounce.Clear();
            if (still.Count == 0) return null;
            int count = Lib.Items.Count(i => i.Path != null && still.Any(d => i.Path.StartsWith(d.Root, StringComparison.OrdinalIgnoreCase)));
            string which = still.Count == 1 ? "USB drive " + still[0].Display : still.Count + " USB drives";
            return count == 0 ? which + " connected \u2014 no movies found on it" : which + " connected \u2014 " + count + (count == 1 ? " title" : " titles") + " added";
        }

        public void ToggleUsbAuto()
        {
            Cfg.UsbAuto = !Cfg.UsbAuto;
            Cfg.Save();
            usbSig = "\u0000";   // force a fresh look
            PollUsb();
        }

        // Windows announces drives being plugged in or removed; check right away instead of waiting for the timer.
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            const int WM_DEVICECHANGE = 0x219, DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004, DBT_DEVTYP_VOLUME = 2;
            if (m.Msg != WM_DEVICECHANGE || m.LParam == IntPtr.Zero) return;
            long ev = m.WParam.ToInt64();
            if ((ev == DBT_DEVICEARRIVAL || ev == DBT_DEVICEREMOVECOMPLETE) && Marshal.ReadInt32(m.LParam, 4) == DBT_DEVTYP_VOLUME)
                BeginInvoke((Action)PollUsb);
        }

        void PollDiscs()
        {
            if (discBusy) return;
            discBusy = true;
            Task.Run(() => Discs.Scan()).ContinueWith(t => BeginInvoke((Action)(() =>
            {
                discBusy = false;
                if (t.IsFaulted) return;
                var list = t.Result;
                string sig = Discs.Signature(list);
                if (sig == discSig) return;
                bool first = discSig == "";
                discSig = sig;
                DiscItems = list;
                RefreshHome();
                if (!first)
                    foreach (var d in list)
                        if (d.Kind == Kind.Disc) ShowToast("Disc detected: " + d.Title + " (" + d.DiscType + ")", 4);
            })));
        }

        void StartRemote()
        {
            server = new RemoteServer(this);
            serverOk = server.Start(Cfg.Port);
            if (!serverOk) { RemoteUrl = "unavailable"; ShowToast("Phone remote couldn't start: " + server.Error, 6); }
            else UpdateRemoteUrl();
            RefreshHome();
        }

        // The phone remote's address follows the PC's current network (Wi-Fi can come up after the app starts).
        public void UpdateRemoteUrl()
        {
            if (!serverOk) return;
            string ip = RemoteServer.LanIp();
            string url = ip == "127.0.0.1" ? "waiting for network" : ip + ":" + Cfg.Port;
            if (url == RemoteUrl) return;
            RemoteUrl = url;
            UpdateState();
            if (Current is SettingsScreen) Current.Refresh();
            Kick();
        }

        // If the app started with Windows before Wi-Fi was up, connect to the chosen network.
        void AutoWifi()
        {
            string net = Cfg.WifiAuto;
            if (net == "") return;
            Task.Run(() =>
            {
                for (int i = 0; i < 12; i++)
                {
                    if (Wifi.HasNetwork()) return i > 0;
                    if (i % 3 == 0) Wifi.Connect(net);
                    System.Threading.Thread.Sleep(5000);
                }
                return Wifi.HasNetwork();
            }).ContinueWith(t => BeginInvoke((Action)(() =>
            {
                UpdateRemoteUrl();
                if (!t.IsFaulted && t.Result) ShowToast("Connected to Wi-Fi: " + net, 3);
            })));
        }

        // ---------- phone QR popup ----------

        public bool PhoneConnected { get { return serverOk && server != null && (DateTime.Now - server.LastPhone).TotalSeconds < 6; } }

        // Shows the QR code a few seconds after start if no phone is connected, and again when a
        // connected phone's remote has been closed for 30 seconds. Closes itself when a phone connects.
        void UpdatePhonePrompt()
        {
            if (!serverOk || !RemoteUrl.Contains(":")) return;
            if (PhoneConnected)
            {
                phoneWasConnected = true;
                startupQrDone = true;
                qrShownThisGap = false;
                if (qrDialog != null && dialog == qrDialog) { CloseQr(); dialog = null; Kick(); }
                return;
            }
            if (!Cfg.PhonePrompt || dialog != null || Player.Active || WindowState == FormWindowState.Minimized) return;
            bool usedHere = lastLocalInput != DateTime.MinValue;
            if (usedHere && !startupQrDone) startupQrDone = true;   // they're already using the keyboard, mouse or a controller
            bool startup = !startupQrDone && (DateTime.Now - startedAt).TotalSeconds > 6;
            bool lost = phoneWasConnected && !qrShownThisGap && (DateTime.Now - server.LastPhone).TotalSeconds > 30
                && (!usedHere || (DateTime.Now - lastLocalInput).TotalMinutes >= 5);
            if (startup || lost)
            {
                startupQrDone = true;
                qrShownThisGap = true;
                ShowPhoneQr(lost);
            }
        }

        void ShowPhoneQr(bool reconnect)
        {
            var modules = Qr.Encode("http://" + RemoteUrl + "/");
            if (modules == null || scale <= 0) return;
            int px = Math.Max(2, (int)(380 * scale / (modules.GetLength(0) + 8)));
            qrDialog = new Dialog
            {
                Title = reconnect ? "Phone remote disconnected" : "Use your phone as the remote",
                Message = "Point your phone's camera at this code (same Wi-Fi), or open " + RemoteUrl + " in its browser. A pairing code pops up here next.",
                Image = Qr.Render(modules, px, Color.Black, Color.White),
                ImageScale = scale,
                Options = new[] { "Not now" },
                Done = i => CloseQr()
            };
            dialog = qrDialog;
            Kick();
        }

        // Keyboard, mouse or controller use on this PC. Returns true if it only closed the QR popup.
        bool NoteLocalInput(bool dismissQr)
        {
            lastLocalInput = DateTime.Now;
            if (!dismissQr || qrDialog == null || dialog != qrDialog) return false;
            CloseQr();
            dialog = null;
            Kick();
            return true;
        }

        // A controller button: works like the same key on the keyboard.
        // True after a controller button; on-screen hints then show controller buttons instead of keys.
        public bool UsingController { get; private set; }
        public string OkButton { get { return UsingController ? "A" : "OK"; } }

        void SetController(bool on)
        {
            if (UsingController == on) return;
            UsingController = on;
            if (Player.Active) Player.Render(); else Kick();
        }

        void LocalAct(Act a)
        {
            SetController(true);
            NoteOtherInput();
            if (NoteLocalInput(true)) return;
            if (WindowState == FormWindowState.Minimized) return;
            if (Player.Active) Player.Act(a);
            else if (a == Act.Home && dialog == null) { GoHome(); Current.Act(Act.Home); Sounds.Play(Sfx.Back); }
            else Dispatch(a);
        }

        void CloseQr()
        {
            if (qrDialog != null && qrDialog.Image != null) qrDialog.Image.Dispose();
            qrDialog = null;
        }

        // ---------- phone pairing ----------

        // A phone opened the remote page: show the code it needs.
        public void PairRequest(string from)
        {
            if (WindowState == FormWindowState.Minimized) return;
            if (Player.Active) { Player.ShowCode(Cfg.Code); return; }
            if (dialog != null && dialog != pairDialog && dialog != qrDialog) return;   // don't cover a question that's waiting for an answer
            if (dialog != null && dialog == qrDialog) CloseQr();
            pairDialog = new Dialog
            {
                Title = "Connect your phone",
                Message = "A phone (" + from + ") opened the remote. Type this code on the phone:",
                Big = Cfg.Code,
                Options = new[] { "OK" },
                Expires = DateTime.Now.AddSeconds(90),
                Countdown = false
            };
            dialog = pairDialog;
            Kick();
        }

        public void PairDone()
        {
            if (dialog != null && dialog == pairDialog) dialog = null;
            pairDialog = null;
            if (Player.Active) Player.ShowCode(null);
            else ShowToast("Phone connected", 3);
            Kick();
        }

        public void ApplyTheme(string key)
        {
            Ui.SetTheme(key);
            Cfg.Theme = Ui.FindTheme(key).Key;
            Cfg.Save();
            BackColor = Ui.Bg;
            // Card art and the home backdrop are pre-rendered with the old colors.
            Art.Clear();
            stack[0] = new HomeScreen(this);
            foreach (var s in stack) s.Refresh();
            Kick();
        }

        public void RefreshHome()
        {
            stack[0].Refresh();
            if (Current is SettingsScreen) Current.Refresh();
            Kick();
        }

        // ---------- navigation ----------

        public void Push(Screen s) { stack.Add(s); Kick(); }

        public void Pop()
        {
            if (stack.Count > 1) { Current.Leave(); stack.RemoveAt(stack.Count - 1); }
            if (Current is HomeScreen) Current.Refresh();
            Kick();
        }

        public void GoHome()
        {
            while (stack.Count > 1) { Current.Leave(); stack.RemoveAt(stack.Count - 1); }
            Current.Refresh();
            Kick();
        }

        public void Kick()
        {
            if (!anim.Enabled) anim.Start();
            Invalidate();
        }

        public void ShowDialog(string title, string message, string[] options, Action<int> done)
        {
            dialog = new Dialog { Title = title, Message = message, Options = options, Done = done };
            Kick();
        }

        public void ShowToast(string text, double seconds)
        {
            toast = text;
            toastUntil = DateTime.Now.AddSeconds(seconds);
            Invalidate();
        }

        // Genre tiles for the Genres page; each opens a grid of its movies.
        public List<MediaItem> GenreItems()
        {
            var list = new List<MediaItem>();
            foreach (var g in Genres.Order)
            {
                var kids = Lib.Items.Where(i => i.Genres.Contains(g)).ToList();
                if (kids.Count > 0) list.Add(new MediaItem { Kind = Kind.Collection, IsGenre = true, Id = "genre-" + Library.Hash(g), Title = g, Children = kids });
            }
            var other = Lib.Items.Where(i => i.Genres.Count == 0).ToList();
            if (other.Count > 0) list.Add(new MediaItem { Kind = Kind.Collection, IsGenre = true, Id = "genre-other", Title = "Not sorted yet", Children = other });
            return list;
        }

        // Options for a movie or collection: the Menu button on a remote, F2, or the context-menu key.
        public void ItemMenu(MediaItem it)
        {
            if (it.IsGenre || it.Kind == Kind.Disc || it.Kind == Kind.NoDisc) { Open(it); return; }
            var labels = new List<string>();
            var actions = new List<Action>();
            labels.Add(it.Kind == Kind.Collection ? "Open" : "Play"); actions.Add(() => Open(it));
            labels.Add("Set genres\u2026"); actions.Add(() => EditGenres(it, 0));
            if (it.Kind == Kind.Movie)
            {
                var w = Hist.Get(it.Path);
                bool watched = w != null && w.Watched;
                labels.Add(watched ? "Mark as not watched" : "Mark as watched");
                actions.Add(() => { Hist.Set(it.Path, 0, w != null ? w.Len : it.Duration, !watched); RefreshHome(); });
            }
            labels.Add("Cancel"); actions.Add(() => { });
            string sub = it.Genres.Count > 0 ? "Genres: " + string.Join(", ", it.Genres) : "No genres yet";
            ShowDialog(it.FullTitle, sub, labels.ToArray(), i => { if (i >= 0 && i < actions.Count) actions[i](); });
        }

        void EditGenres(MediaItem it, int sel)
        {
            var opts = new List<string>();
            foreach (var g in Genres.Order) opts.Add((it.Genres.Contains(g) ? "\u2713   " : "      ") + g);
            opts.Add("Done");
            ShowDialog("Genres \u00B7 " + it.Title, "OK turns a genre on or off. Pick as many as fit.", opts.ToArray(), i =>
            {
                if (i >= 0 && i < Genres.Order.Length)
                {
                    var list = new List<string>(it.Genres);
                    string g = Genres.Order[i];
                    if (list.Contains(g)) list.Remove(g); else list.Add(g);
                    list.Sort((a, b) => Array.IndexOf(Genres.Order, a).CompareTo(Array.IndexOf(Genres.Order, b)));
                    Genres.Set(it, list);
                    EditGenres(it, i);
                }
                else RefreshHome();
            });
            dialog.Sel = sel;
        }

        public void AskExit()
        {
            ShowDialog("Exit " + AppInfo.Name + "?", "", new[] { "Stay", "Restart the PC", "Shut down the PC", "Exit to desktop" }, i =>
            {
                if (i == 1) ConfirmPower(true);
                else if (i == 2) ConfirmPower(false);
                else if (i == 3) Close();
            });
        }

        // Restarting or shutting down asks once more and counts down, so a stray press can be cancelled.
        void ConfirmPower(bool restart)
        {
            dialog = new Dialog
            {
                Title = (restart ? "Restart" : "Shut down") + " the PC?",
                Message = "Other open programs will close too, and unsaved work in them may be lost.",
                Options = new[] { restart ? "Restart now" : "Shut down now", "Cancel" },
                Expires = DateTime.Now.AddSeconds(15),
                CountdownFormat = (restart ? "Restarting" : "Shutting down") + " in {0} seconds.",
                Done = i => { if (i == 0 || i == -2) PowerOff(restart); }   // -2: the countdown ran out
            };
            Kick();
        }

        void PowerOff(bool restart)
        {
            try
            {
                Player.Stop();
                // A few seconds' grace lets this app close cleanly (saving settings, restoring the screen resolution).
                Process.Start(new ProcessStartInfo("shutdown.exe", (restart ? "/r" : "/s") + " /t 5") { UseShellExecute = false, CreateNoWindow = true });
                Close();
            }
            catch (Exception ex) { ShowToast("Couldn't " + (restart ? "restart" : "shut down") + " the PC: " + ex.Message, 5); }
        }

        // Opening the app while it's already running brings the running copy to the front instead.
        public void BringBack()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            if (!Cfg.Windowed) ApplyDisplayMode();
            // Windows only lets the app that's in use take the front; pressing Alt for a moment is the usual way around that.
            TopMost = true;
            TopMost = false;
            keybd_event(0x12, 0, 0, UIntPtr.Zero);
            keybd_event(0x12, 0, 2, UIntPtr.Zero);
            SetForegroundWindow(Handle);
            Activate();
            Kick();
        }

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        // ---------- opening things ----------

        public void Open(MediaItem it)
        {
            if (it == null) return;
            switch (it.Kind)
            {
                case Kind.Collection:
                    Push(new GridScreen(this, it.Title, it.Children));
                    return;
                case Kind.NoDisc:
                    ShowToast("Put a DVD or Blu-ray in drive " + it.Drive, 3);
                    return;
                case Kind.Disc:
                    PlayDisc(it);
                    return;
                case Kind.DiscFolder:
                    PlayDiscItem(it);
                    return;
                default:
                    var w = Hist.Get(it.Path);
                    if (w != null && !w.Watched && w.Pos > 30)
                        ShowDialog(it.FullTitle, "You stopped at " + Ui.Time(w.Pos) + " of " + Ui.Time(w.Len) + ".",
                            new[] { "Resume from " + Ui.Time(w.Pos), "Start from the beginning" },
                            i => { if (i == 0) Player.Start(it, w.Pos); else if (i == 1) Player.Start(it, 0); });
                    else Player.Start(it, 0);
                    return;
            }
        }

        void PlayDisc(MediaItem d)
        {
            if (d.DiscType == "Data disc")
            {
                ShowToast("Looking for videos on the disc…", 10);
                Task.Run(() => Library.ScanFolder(d.Path)).ContinueWith(t => BeginInvoke((Action)(() =>
                {
                    toast = null;
                    var found = t.IsFaulted ? null : t.Result;
                    if (found == null) ShowToast("No videos found on this disc", 3);
                    else if (found.Kind == Kind.Collection) Push(new GridScreen(this, d.Title, found.Children));
                    else Open(found);
                })));
                return;
            }
            if (Cfg.DiscPlayer != "" && File.Exists(Cfg.DiscPlayer)) { LaunchDiscPlayer(); return; }
            PlayDiscItem(d);
        }

        void PlayDiscItem(MediaItem d)
        {
            if (d.Encrypted)
                ShowDialog("This disc is copy-protected",
                    "Store-bought " + d.DiscType + " discs are encrypted. VLC can't decrypt them, so it will most likely show an error.\n\nTo play protected discs, install a licensed disc player program and choose it in Settings → Disc player.",
                    new[] { "Try with VLC anyway", "Open Settings", "Cancel" },
                    i => { if (i == 0) Player.Start(d, 0); else if (i == 1) Push(new SettingsScreen(this)); });
            else Player.Start(d, 0);
        }

        void LaunchDiscPlayer()
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo(Cfg.DiscPlayer) { UseShellExecute = true });
                DisplayModes.RestoreAll();
                WindowState = FormWindowState.Minimized;
                if (p != null)
                {
                    p.EnableRaisingEvents = true;
                    p.Exited += (s, e) => BeginInvoke((Action)(() => { WindowState = FormWindowState.Normal; ApplyDisplayMode(); Activate(); }));
                }
            }
            catch (Exception ex) { ShowToast("Couldn't open the disc player: " + ex.Message, 5); }
        }

        // ---------- updates ----------

        public void CheckForUpdates(bool userAsked)
        {
            if (updateBusy) return;
            if (!Updater.Configured)
            {
                UpdateStatus = "Not set up yet";
                if (userAsked) ShowToast("Updates aren't set up yet (no GitHub repository chosen)", 4);
                RefreshSettingsScreen();
                return;
            }
            if (downloadedSetup != null && userAsked) { updatePostponed = false; OfferInstall(); return; }
            updateBusy = true;
            UpdateStatus = "Checking\u2026";
            RefreshSettingsScreen();
            Task.Run(() => Updater.Latest()).ContinueWith(t => BeginInvoke((Action)(() =>
            {
                updateBusy = false;
                if (t.IsFaulted)
                {
                    UpdateStatus = "Couldn't check \u2014 no internet?";
                    if (userAsked) ShowToast("Couldn't reach GitHub to check for updates", 4);
                }
                else if (!Updater.IsNewer(t.Result))
                {
                    availableUpdate = null;
                    UpdateStatus = "Up to date";
                    if (userAsked) ShowToast("You have the latest version (" + Updater.Current + ")", 3);
                }
                else
                {
                    availableUpdate = t.Result;
                    UpdateStatus = "Version " + availableUpdate.Version + " available";
                    if (userAsked || Cfg.AutoUpdate) DownloadUpdate(availableUpdate, userAsked);
                }
                RefreshSettingsScreen();
            })));
        }

        void DownloadUpdate(ReleaseInfo r, bool userAsked)
        {
            updateBusy = true;
            UpdateStatus = "Downloading " + r.Version + "\u2026";
            RefreshSettingsScreen();
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Theater", "updates");
            Task.Run(() => Updater.Download(r, dir, pct => BeginInvoke((Action)(() => { UpdateStatus = "Downloading " + r.Version + "\u2026 " + pct + "%"; if (Current is SettingsScreen) Current.Refresh(); }))))
                .ContinueWith(t => BeginInvoke((Action)(() =>
                {
                    updateBusy = false;
                    if (t.IsFaulted)
                    {
                        UpdateStatus = "Update download failed";
                        Program.Log(t.Exception.GetBaseException());
                        if (userAsked) ShowToast("Couldn't download the update: " + t.Exception.GetBaseException().Message, 5);
                    }
                    else
                    {
                        downloadedSetup = t.Result;
                        UpdateStatus = "Version " + r.Version + " ready to install";
                        if (userAsked) updatePostponed = false;
                        OfferInstall();
                    }
                    RefreshSettingsScreen();
                })));
        }

        // Asks once (with a countdown that installs by default); waits while a movie plays or a question is open.
        void OfferInstall()
        {
            if (downloadedSetup == null || updatePostponed || Player.Active || dialog != null || WindowState == FormWindowState.Minimized) return;
            string notes = availableUpdate != null ? availableUpdate.Notes.Trim() : "";
            if (notes.Length > 280) notes = notes.Substring(0, 277) + "\u2026";
            dialog = new Dialog
            {
                Title = "Update ready: version " + (availableUpdate != null ? availableUpdate.Version.ToString() : "?"),
                Message = (notes.Length > 0 ? "What's new: " + notes + "\n\n" : "") + AppInfo.Name + " will close, update, and open again in a few seconds. Your library and settings stay as they are.",
                Options = new[] { "Update now", "Not now" },
                Expires = DateTime.Now.AddSeconds(20),
                Done = i => { if (i == 0 || i == -2) ApplyUpdate(); else updatePostponed = true; }   // -2: the countdown ran out
            };
            Kick();
        }

        void ApplyUpdate()
        {
            try
            {
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                string args = "/silent /update /relaunch /dir=\"" + dir + "\"";
                // A local copy (not installed by Setup) is updated in place: only its own file is replaced.
                if (!Updater.IsInstalledCopy) args += " /portable /exe=\"" + Path.GetFileName(Application.ExecutablePath) + "\"";
                Process.Start(new ProcessStartInfo(downloadedSetup, args) { UseShellExecute = true });
                ShowToast("Updating\u2026", 10);
                Close();
            }
            catch (Exception ex) { ShowToast("Couldn't start the update: " + ex.Message, 5); }
        }

        void RefreshSettingsScreen() { if (Current is SettingsScreen) { Current.Refresh(); Kick(); } }

        public void ToggleAutoUpdate()
        {
            Cfg.AutoUpdate = !Cfg.AutoUpdate;
            Cfg.Save();
            if (Cfg.AutoUpdate) CheckForUpdates(false);
        }

        public void AfterPlayback()
        {
            Current.Refresh();
            Activate();
            Kick();
        }

        public void ShowVideo(bool on)
        {
            video.Visible = on;
            if (on) video.BringToFront();
            Invalidate();
        }

        // ---------- display ----------

        public void ToggleWindowed()
        {
            Cfg.Windowed = !Cfg.Windowed;
            Cfg.Save();
            bool playing = Player.Active;
            MediaItem item = Player.Item;
            double pos = Player.Now();
            if (playing) Player.Suspend();
            ApplyDisplayMode();
            // Changing the window style recreates the video surface, so restart playback where it was.
            if (playing) Player.Start(item, pos);
            Kick();
        }

        string Device { get { return displayDevice ?? (displayDevice = System.Windows.Forms.Screen.FromControl(this).DeviceName); } }

        static Size ParseRes(string r)
        {
            var m = System.Text.RegularExpressions.Regex.Match(r ?? "", @"^(\d+)x(\d+)$");
            return m.Success ? new Size(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : Size.Empty;
        }

        public Size NativeResolution { get { return DisplayModes.Saved(Device); } }
        public Size CurrentResolution(out int hz) { return DisplayModes.Current(Device, out hz); }

        public string ResolutionLabel
        {
            get
            {
                var s = ParseRes(Cfg.Resolution);
                var n = NativeResolution;
                return s.IsEmpty ? "Native (" + n.Width + " \u00D7 " + n.Height + ")" : s.Width + " \u00D7 " + s.Height;
            }
        }

        // Size.Empty stands for "Native"; then every common resolution the screen supports below it.
        public List<Size> ResolutionChoices()
        {
            var list = new List<Size> { Size.Empty };
            var n = NativeResolution;
            foreach (var s in DisplayModes.Choices(Device))
                if (s != n && s.Width <= n.Width && s.Height <= n.Height) list.Add(s);
            return list;
        }

        public void SetResolution(Size s)
        {
            string old = Cfg.Resolution;
            Cfg.Resolution = s.IsEmpty ? "native" : s.Width + "x" + s.Height;
            Cfg.Save();
            if (old == Cfg.Resolution) return;
            if (Cfg.Windowed) { ShowToast("Saved \u2014 it applies when you switch to full screen", 4); return; }
            ApplyDisplayMode();
            // Like Windows does: if the screen can't show the new mode, it goes back by itself.
            dialog = new Dialog
            {
                Title = "Keep this resolution?",
                Message = "",
                Options = new[] { "Keep it", "Go back" },
                Expires = DateTime.Now.AddSeconds(15),
                Done = i => { if (i != 0) { Cfg.Resolution = old; Cfg.Save(); ApplyDisplayMode(); if (Current is SettingsScreen) Current.Refresh(); Kick(); } }
            };
            Kick();
        }

        void FitScreen()
        {
            if (Cfg.Windowed || WindowState == FormWindowState.Minimized) return;
            var b = System.Windows.Forms.Screen.FromControl(this).Bounds;
            if (Bounds != b) Bounds = b;
            Kick();
        }

        public void RestartEngine()
        {
            if (Player.Active) return;
            var v = Vlc;
            v.Post(() => v.Kill());
        }

        void ApplyDisplayMode()
        {
            displayDevice = System.Windows.Forms.Screen.FromControl(this).DeviceName;
            if (Cfg.Windowed)
            {
                DisplayModes.Restore(displayDevice);
                FormBorderStyle = FormBorderStyle.Sizable;
                if (windowedBounds != Rectangle.Empty) Bounds = windowedBounds;
                else
                {
                    var wa = System.Windows.Forms.Screen.FromControl(this).WorkingArea;
                    Bounds = new Rectangle(wa.X + (wa.Width - 1280) / 2, wa.Y + (wa.Height - 760) / 2, 1280, 760);
                }
            }
            else
            {
                if (FormBorderStyle != FormBorderStyle.None) windowedBounds = Bounds;
                FormBorderStyle = FormBorderStyle.None;
                var want = ParseRes(Cfg.Resolution);
                int hz;
                var cur = DisplayModes.Current(displayDevice, out hz);
                if (want.IsEmpty) DisplayModes.Restore(displayDevice);
                else if (cur != want && !DisplayModes.Apply(displayDevice, want)) ShowToast("This screen can't use " + want.Width + " \u00D7 " + want.Height, 4);
                Bounds = System.Windows.Forms.Screen.FromControl(this).Bounds;
            }
        }

        void SlowTick()
        {
            slowTicks++;

            if (Player.Active) return;
            if (WindowState == FormWindowState.Minimized) return;
            if (dialog != null && dialog.Expires.HasValue)
            {
                if (DateTime.Now > dialog.Expires.Value) { var d = dialog; dialog = null; if (d == pairDialog) pairDialog = null; if (d.Done != null) d.Done(-2); Kick(); }
                else if (slowTicks % 4 == 0 && dialog.Countdown) Invalidate();
            }
            if (Current is InfoScreen && slowTicks % 4 == 0) Invalidate();
            if (slowTicks % 4 == 0) UpdatePhonePrompt();
            if (downloadedSetup != null && !updatePostponed && dialog == null && slowTicks % 8 == 0) OfferInstall();
            // Repaint only what changes: new thumbnails, the clock (once a minute), the search caret, toasts.
            if (thumbsDirty && (DateTime.Now - lastThumbPaint).TotalMilliseconds > 400)
            {
                thumbsDirty = false;
                lastThumbPaint = DateTime.Now;
                Invalidate();
            }
            string clock = Ui.Clock();
            if (clock != lastClock) { lastClock = clock; InvalidateVirtual(new RectangleF(900, 20, 1000, 100)); }
            if (Current.WantsText && slowTicks % 2 == 0) InvalidateVirtual(new RectangleF(90, 40, 1200, 100));
            if (toast != null && DateTime.Now > toastUntil) { toast = null; InvalidateVirtual(new RectangleF(0, 960, 1920, 100)); }
            UpdateCursor();
            UpdateState();
        }

        public void SetCursorHidden(bool hide)
        {
            if (hide == cursorHidden) return;
            cursorHidden = hide;
            if (hide) Cursor.Hide(); else Cursor.Show();
        }

        public void NoteMouse() { lastMouse = DateTime.Now; lastLocalInput = DateTime.Now; UpdateCursor(); }

        // Keyboard, controller or phone: the mouse pointer gets out of the way.
        public void NoteOtherInput() { lastOtherInput = DateTime.Now; UpdateCursor(); }

        // The pointer shows only while the mouse is the thing being used (and, in full screen or during a movie,
        // only for a few seconds after it last moved).
        public bool MouseInUse
        {
            get
            {
                if (lastMouse <= lastOtherInput) return false;
                return (Cfg.Windowed && !Player.Active) || (DateTime.Now - lastMouse).TotalSeconds < 4;
            }
        }

        public void UpdateCursor() { SetCursorHidden(!MouseInUse); }

        void InvalidateVirtual(RectangleF v)
        {
            Invalidate(Rectangle.Inflate(Rectangle.Round(new RectangleF(ox + v.X * scale, oy + v.Y * scale, v.Width * scale, v.Height * scale)), 2, 2));
        }

        public void SetAnimations(string mode)
        {
            Cfg.Anim = mode;
            Cfg.Save();
            Ui.Smooth = mode != "off";
            slowFrames = 0;
            avgPaintMs = 0;
        }

        public string AnimationLabel
        {
            get
            {
                if (Cfg.Anim == "on") return "On";
                if (Cfg.Anim == "off") return "Off";
                return Ui.Smooth ? "Automatic (on)" : "Automatic (off \u2014 this PC is slow to draw)";
            }
        }

        public void UpdateState()
        {
            var d = new Dictionary<string, object>();
            d["screen"] = Player.Active ? "player" : Current.Name;
            d["focus"] = Current.FocusTitle;
            d["dialog"] = dialog != null ? dialog.Title : null;
            d["playing"] = Player.Active;
            if (Player.Active && Player.Item != null)
            {
                d["title"] = Player.Item.FullTitle;
                d["time"] = Math.Round(Player.Now(), 1);
                d["length"] = Player.St.Length;
                d["paused"] = Player.Paused;
                d["volume"] = (int)Math.Round(Player.St.Volume * 100 / 256.0);
                d["disc"] = Player.Item.IsDiscUri;
            }
            StateJson = new JavaScriptSerializer().Serialize(d);
        }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            if (Player.Active) { g.Clear(Color.Black); return; }
            paintClock.Restart();
            int buildsBefore = Art.Builds;
            g.Clear(Ui.Bg);
            scale = Math.Min(ClientSize.Width / 1920f, ClientSize.Height / 1080f);
            if (scale <= 0) return;
            ox = (ClientSize.Width - 1920 * scale) / 2;
            oy = (ClientSize.Height - 1080 * scale) / 2;
            g.TranslateTransform(ox, oy);
            g.ScaleTransform(scale, scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = anim.Enabled ? InterpolationMode.Bilinear : InterpolationMode.HighQualityBilinear;
            g.CompositingQuality = anim.Enabled ? CompositingQuality.HighSpeed : CompositingQuality.Default;
            g.PixelOffsetMode = anim.Enabled ? PixelOffsetMode.HighSpeed : PixelOffsetMode.Half;
            Current.Draw(g);
            if (dialog != null) dialog.Draw(g);
            if (toast != null && DateTime.Now < toastUntil)
            {
                var f = Ui.F(22, 1);
                float w = Ui.Width(g, toast, f) + 64;
                var r = new RectangleF((1920 - w) / 2, 978, w, 56);
                Ui.FillRound(g, Color.FromArgb(235, 38, 41, 51), r, 28);
                Ui.Text(g, toast, f, Ui.Ink, r, StringAlignment.Center, StringAlignment.Center);
            }
            // Frames that rendered new card art are one-off costs, not a sign of a slow PC.
            TrackFrame(paintClock.Elapsed.TotalMilliseconds, Art.Builds == buildsBefore && e.ClipRectangle.Width * e.ClipRectangle.Height >= ClientSize.Width * ClientSize.Height / 2);
        }

        void TrackFrame(double ms, bool fullFrame)
        {
            if (!anim.Enabled || !fullFrame) return;
            animFrames++;
            avgPaintMs = avgPaintMs == 0 ? ms : avgPaintMs * 0.9 + ms * 0.1;
            maxPaintMs = Math.Max(maxPaintMs, ms);
            if (ms > 33) spikes++;
            // Smooth motion needs roughly 30+ frames per second; below that, moves become instant.
            if (Cfg.Anim == "auto" && Ui.Smooth && ms > 34) slowFrames++;
            if (Cfg.Anim == "auto" && Ui.Smooth && animFrames > 30 && slowFrames > 15 && avgPaintMs > 30)
            {
                Ui.Smooth = false;
                ShowToast("Animations turned off to keep things snappy on this PC", 4);
            }
            if (animFrames % 60 == 0)
                try
                {
                    File.WriteAllText(Path.Combine(Settings.Dir, "perf.txt"), string.Format("avg {0:0.0} ms/frame, max {1:0.0} ms, frames over 33ms {6}, frames {2}, smooth {3}, card builds {4}, scale {5:0.00}",
                        avgPaintMs, maxPaintMs, animFrames, Ui.Smooth, Art.Builds, scale, spikes));
                }
                catch { }
        }

        PointF ToVirtual(Point p) { return new PointF((p.X - ox) / scale, (p.Y - oy) / scale); }

        // ---------- input ----------

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            SetController(false);
            NoteOtherInput();
            if (NoteLocalInput(keyData != Keys.F11)) return true;
            if (keyData == Keys.F11 || keyData == (Keys.Alt | Keys.Enter)) { ToggleWindowed(); return true; }
            if (Player.Active) return Player.Key(keyData) || base.ProcessCmdKey(ref msg, keyData);
            if (dialog == null && Current.WantsText)
            {
                if (keyData == Keys.Back && Current.Backspace()) return true;
                if (keyData == Keys.Space) return base.ProcessCmdKey(ref msg, keyData);
            }
            var a = MapKey(keyData);
            if (a != Act.None) { Dispatch(a); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        static Act MapKey(Keys k)
        {
            switch (k)
            {
                case Keys.Up: return Act.Up;
                case Keys.Down: return Act.Down;
                case Keys.Left: return Act.Left;
                case Keys.Right: return Act.Right;
                case Keys.Enter: return Act.Ok;
                case Keys.Escape: case Keys.Back: case Keys.BrowserBack: return Act.Back;
                case Keys.Home: case Keys.BrowserHome: return Act.Home;
                case Keys.Space: case Keys.MediaPlayPause: case Keys.Play: return Act.PlayPause;
                case Keys.Apps: case Keys.F2: case Keys.Shift | Keys.F10: return Act.Menu;
                case Keys.F1: return Act.Info;
                case Keys.BrowserSearch: case Keys.F3: case Keys.Control | Keys.F: return Act.Search;
            }
            return Act.None;
        }

        public void Dispatch(Act a)
        {
            if (a == Act.None) return;
            string before = NavKey();
            Route(a);
            switch (a)
            {
                // A direction that didn't move anything (end of a row, top of a list) gets a bump instead of a tick.
                case Act.Up: case Act.Down: case Act.Left: case Act.Right: Sounds.Play(NavKey() != before ? Sfx.Move : Sfx.Bump); break;
                case Act.Ok: case Act.Menu: case Act.Info: case Act.Search: Sounds.Play(Sfx.Select); break;
                case Act.Back: case Act.Home: Sounds.Play(Sfx.Back); break;
            }
        }

        // Where the focus is: changes whenever a move did something.
        string NavKey()
        {
            if (Player.Active) return "player";
            return dialog != null ? "dialog|" + dialog.Title + "|" + dialog.Sel : stack.Count + "|" + Current.Name + "|" + Current.FocusKey;
        }

        void Route(Act a)
        {
            lastInput = DateTime.Now;
            if (a == Act.Info && dialog == null && !(Current is InfoScreen)) { Push(new InfoScreen(this)); return; }
            if (dialog != null)
            {
                int r;
                if (dialog.Act(a, out r))
                {
                    var d = dialog;
                    dialog = null;
                    if (d.Done != null) d.Done(r);
                }
                Kick();
                return;
            }
            Current.Act(a);
            Kick();
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (Player.Active || dialog != null || char.IsControl(e.KeyChar)) return;
            Current.Char(e.KeyChar);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Location == lastMousePos) return;
            // Windows reports the pointer's position once when the window appears under it; that isn't someone using the mouse.
            bool firstReport = !mouseSeen;
            mouseSeen = true;
            lastMousePos = e.Location;
            if (firstReport) return;
            lastMouse = lastInput = DateTime.Now;
            SetController(false);
            NoteLocalInput(false);
            UpdateCursor();
            if (Player.Active) return;
            var p = ToVirtual(e.Location);
            if (dialog != null) { int i = dialog.Hit(p); if (i >= 0 && i != dialog.Sel) { dialog.Sel = i; Invalidate(); } return; }
            Current.Move(p);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            lastMouse = DateTime.Now;
            UpdateCursor();
            if (NoteLocalInput(true)) return;
            SetCursorHidden(false);
            if (Player.Active) return;
            if (e.Button == MouseButtons.Right || e.Button == MouseButtons.XButton1) { Dispatch(Act.Back); return; }
            if (e.Button != MouseButtons.Left) return;
            var p = ToVirtual(e.Location);
            if (dialog != null)
            {
                int i = dialog.Hit(p);
                if (i >= 0) { Sounds.Play(Sfx.Select); var d = dialog; dialog = null; if (d.Done != null) d.Done(i); Kick(); }
                return;
            }
            Sounds.Play(Sfx.Select);
            Current.Click(p);
            Kick();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            NoteLocalInput(false);
            if (Player.Active) { Player.Wheel(e.Delta); return; }
            if (dialog == null) Current.Wheel(e.Delta);
        }

        // ---------- phone remote ----------

        static readonly Dictionary<string, Act> RemoteActs = new Dictionary<string, Act> {
            { "up", Act.Up }, { "down", Act.Down }, { "left", Act.Left }, { "right", Act.Right }, { "ok", Act.Ok },
            { "back", Act.Back }, { "home", Act.Home }, { "playpause", Act.PlayPause }, { "stop", Act.Stop },
            { "volup", Act.VolUp }, { "voldown", Act.VolDown }, { "mute", Act.Mute }, { "audio", Act.Audio }, { "subs", Act.Subs },
            { "ff", Act.Forward }, { "rew", Act.Rewind }, { "info", Act.Info }, { "menu", Act.Menu },
            { "next", Act.NextChapter }, { "prev", Act.PrevChapter }, { "search", Act.Search }
        };

        public void RemoteKey(string k)
        {
            Act a;
            // Test builds only: "pad-ok" etc. acts like a controller button, for automated checks.
            if (k != null && k.StartsWith("pad-") && Environment.GetEnvironmentVariable("THEATER_TEST") == "1" && RemoteActs.TryGetValue(k.Substring(4), out a)) { LocalAct(a); return; }
            if (!RemoteActs.TryGetValue(k ?? "", out a)) return;
            NoteOtherInput();
            if (WindowState == FormWindowState.Minimized) return;
            if (Player.Active) Player.Act(a);
            else if (a == Act.Home && dialog == null) { GoHome(); Current.Act(Act.Home); Sounds.Play(Sfx.Back); }   // back to the home screen, top row
            else Dispatch(a);
        }

        public void RemoteText(string t)
        {
            NoteOtherInput();
            if (Player.Active || dialog != null) return;
            if (Current.WantsText) Current.SetText(t);
            else Push(GridScreen.Search(this, t));
        }

        public void RemotePlay(string id)
        {
            MediaItem it;
            if (!Lib.ById.TryGetValue(id, out it))
            {
                it = DiscItems.Find(d => d.Id == id);
                if (it == null) return;
            }
            NoteOtherInput();
            if (Player.Active) Player.Stop();
            dialog = null;
            Open(it);
        }
    }

    class Player
    {
        readonly TheaterForm F;
        readonly PlayerOverlay ov;
        readonly Timer timer = new Timer { Interval = 250 };
        public bool Active;
        public MediaItem Item;
        public VlcStatus St = new VlcStatus();
        double startAt, pendingSeek = -1;
        bool started, ended, polling;
        string lastFrame;
        string failure, flash;
        float flashLevel = -1;
        DateTime stAt, loadStart, osdUntil, flashUntil, nextAt, seekHold;
        MediaItem next;
        int muteVolume = -1, ticks, session;

        readonly List<KeyValuePair<RectangleF, Act>> buttons = new List<KeyValuePair<RectangleF, Act>>();
        RectangleF bar;
        Point lastMouse;
        bool mouseSeen;

        public Player(TheaterForm f)
        {
            F = f;
            ov = new PlayerOverlay(this);
            timer.Tick += (s, e) => Tick();
        }

        public bool Paused { get { return St.State == "paused"; } }
        bool IsDisc { get { return Item != null && Item.IsDiscUri; } }
        bool OsdVisible { get { return !started || Paused || failure != null || next != null || DateTime.Now < osdUntil; } }

        public double Now()
        {
            if (!Active) return 0;
            double t = St.Time;
            if (St.State == "playing" && started) t += (DateTime.Now - stAt).TotalSeconds;
            if (St.Length > 0) t = Math.Min(t, St.Length);
            return t;
        }

        public void Start(MediaItem item, double at)
        {
            if (Active) Halt(false);
            session++;
            Item = item;
            startAt = at;
            started = ended = false;
            failure = null;
            next = null;
            flash = null;
            pendingSeek = -1;
            muteVolume = -1;
            St = new VlcStatus { State = "opening", Volume = F.Cfg.Volume };
            stAt = loadStart = DateTime.Now;
            Active = true;
            F.Thumbs.Paused = true;
            F.ShowVideo(true);
            Reposition();
            if (!ov.Visible) ov.Show(F);
            ShowOsd(6);
            IntPtr hwnd = F.VideoHandle;
            string exe = F.Cfg.VlcPath, uri = item.Uri, extra = F.Cfg.VlcArgs();
            var vlc = F.Vlc;
            int my = session;
            vlc.Post(() =>
            {
                bool ok = vlc.Start(exe, hwnd, extra);
                if (ok) vlc.Play(uri);
                else F.BeginInvoke((Action)(() => { if (Active && session == my) Fail(vlc.Error); }));
            });
            timer.Start();
            Render();
            F.UpdateState();
        }

        public void Stop() { Halt(true); }

        // Stops VLC and saves progress but keeps the player surface (used while restarting).
        public void Suspend() { Halt(false); ov.Hide(); }

        void Halt(bool backToLibrary)
        {
            if (!Active) return;
            if (started && !ended && !IsDisc && Item.Path != null && St.Length > 0)
            {
                double pos = Now();
                bool watched = pos > St.Length * 0.93 || St.Length - pos < 180;
                if (watched) F.Hist.Set(Item.Path, 0, St.Length, true);
                else if (pos > 30) F.Hist.Set(Item.Path, pos, St.Length, false);
            }
            Active = false;
            F.Thumbs.Paused = false;
            timer.Stop();
            next = null;
            var vlc = F.Vlc;
            vlc.Post(() => { vlc.Request("command=pl_stop"); vlc.Request("command=pl_empty"); });
            if (backToLibrary)
            {
                ov.Hide();
                F.ShowVideo(false);
                F.UpdateCursor();
                F.AfterPlayback();
            }
            F.UpdateState();
        }

        public void Reposition()
        {
            var r = F.RectangleToScreen(F.ClientRectangle);
            if (ov.Bounds != r) { ov.Bounds = r; lastFrame = null; Render(); }
        }

        void Tick()
        {
            if (!Active) { timer.Stop(); return; }
            if (next != null && DateTime.Now >= nextAt) { PlayNext(); return; }
            if (pendingSeek >= 0)
            {
                int t = (int)pendingSeek;
                pendingSeek = -1;
                var v = F.Vlc;
                v.Post(() => v.Cmd("seek", t.ToString()));
            }
            if (!polling && ticks++ % 2 == 0)
            {
                polling = true;
                var vlc = F.Vlc;
                int my = session;
                Task.Run(() => vlc.Status()).ContinueWith(t => F.BeginInvoke((Action)(() =>
                {
                    polling = false;
                    if (session == my) OnStatus(t.IsFaulted ? null : t.Result);
                })));
            }
            F.UpdateCursor();
            Render();
        }

        void OnStatus(VlcStatus s)
        {
            if (!Active || s == null) return;
            if (DateTime.Now < seekHold) { s.Time = Now(); }
            if (muteVolume >= 0) s.Volume = 0;
            St = s;
            stAt = DateTime.Now;
            if (!started)
            {
                if (s.State == "playing" || s.State == "paused")
                {
                    started = true;
                    var vlc = F.Vlc;
                    int vol = F.Cfg.Volume;
                    St.Volume = vol;
                    if (startAt > 5)
                    {
                        int at = (int)startAt;
                        St.Time = at;
                        seekHold = DateTime.Now.AddSeconds(1.5);
                        vlc.Post(() => vlc.Cmd("seek", at.ToString()));
                    }
                    vlc.Post(() => vlc.Cmd("volume", vol.ToString()));
                    ShowOsd(4);
                }
                else if ((DateTime.Now - loadStart).TotalSeconds > 20 && failure == null)
                    Fail(IsDisc ? "VLC couldn't play this disc. Store-bought Blu-ray and 4K discs are usually copy-protected, which VLC can't play." : "VLC couldn't open this file.");
            }
            else if (s.State == "stopped" && !ended) Ended();
            F.UpdateState();
        }

        void Fail(string message)
        {
            failure = message;
            ShowOsd(0);
            Render();
        }

        void Ended()
        {
            ended = true;
            if (!IsDisc && Item.Path != null) F.Hist.Set(Item.Path, 0, St.Length > 0 ? St.Length : Item.Duration, true);
            next = null;
            if (Item.Parent != null)
            {
                int i = Item.Parent.Children.IndexOf(Item);
                if (i >= 0 && i + 1 < Item.Parent.Children.Count && Item.Parent.Children[i + 1].Kind == Kind.Movie) next = Item.Parent.Children[i + 1];
            }
            if (next != null) { nextAt = DateTime.Now.AddSeconds(10); Render(); }
            else Stop();
        }

        void PlayNext()
        {
            var n = next;
            next = null;
            if (n != null) Start(n, 0);
        }

        public void Act(Act a)
        {
            if (!Active) return;
            if (failure != null || !started)
            {
                if (a == Theater.Act.Back || a == Theater.Act.Stop || a == Theater.Act.Home || (failure != null && a == Theater.Act.Ok)) Stop();
                return;
            }
            if (next != null)
            {
                if (a == Theater.Act.Ok || a == Theater.Act.PlayPause) PlayNext();
                else if (a == Theater.Act.Back || a == Theater.Act.Stop || a == Theater.Act.Home) Stop();
                return;
            }
            switch (a)
            {
                case Theater.Act.PlayPause: TogglePause(); break;
                case Theater.Act.Ok: if (IsDisc) Hotkey("nav-activate"); else TogglePause(); break;
                case Theater.Act.Left: if (IsDisc) Hotkey("nav-left"); else Seek(-10); break;
                case Theater.Act.Right: if (IsDisc) Hotkey("nav-right"); else Seek(30); break;
                case Theater.Act.Up: if (IsDisc) Hotkey("nav-up"); else ShowOsd(5); break;
                case Theater.Act.Down: if (IsDisc) Hotkey("nav-down"); else ShowOsd(5); break;
                case Theater.Act.Rewind: Seek(-10); break;
                case Theater.Act.Forward: Seek(30); break;
                case Theater.Act.VolUp: Volume(13); break;
                case Theater.Act.VolDown: Volume(-13); break;
                case Theater.Act.Mute: ToggleMute(); break;
                case Theater.Act.Audio: CycleTrack("atrack"); break;
                case Theater.Act.Subs: CycleTrack("strack"); break;
                case Theater.Act.NextChapter: Hotkey("chapter-next"); Flash("Next chapter", -1); break;
                case Theater.Act.PrevChapter: Hotkey("chapter-prev"); Flash("Previous chapter", -1); break;
                case Theater.Act.Menu: if (IsDisc) { Hotkey("disc-menu"); Flash("Disc menu", -1); } else ShowOsd(6); break;
                case Theater.Act.Info: ShowOsd(8); break;
                case Theater.Act.Back:
                case Theater.Act.Stop:
                case Theater.Act.Home:
                    Stop();
                    return;
            }
            F.UpdateState();
            Render();
        }

        public bool Key(Keys k)
        {
            Act a = Theater.Act.None;
            switch (k)
            {
                case Keys.Escape: case Keys.Back: case Keys.BrowserBack: case Keys.MediaStop: case Keys.X: a = Theater.Act.Back; break;
                case Keys.Space: case Keys.MediaPlayPause: case Keys.Play: case Keys.P: a = Theater.Act.PlayPause; break;
                case Keys.Enter: a = Theater.Act.Ok; break;
                case Keys.Left: a = Theater.Act.Left; break;
                case Keys.Right: a = Theater.Act.Right; break;
                case Keys.Up: a = Theater.Act.Up; break;
                case Keys.Down: a = Theater.Act.Down; break;
                case Keys.OemOpenBrackets: case Keys.J: a = Theater.Act.Rewind; break;
                case Keys.OemCloseBrackets: case Keys.L: a = Theater.Act.Forward; break;
                case Keys.Oemplus: case Keys.Shift | Keys.Oemplus: case Keys.Add: a = Theater.Act.VolUp; break;
                case Keys.OemMinus: case Keys.Subtract: a = Theater.Act.VolDown; break;
                case Keys.M: a = Theater.Act.Mute; break;
                case Keys.A: a = Theater.Act.Audio; break;
                case Keys.S: case Keys.C: a = Theater.Act.Subs; break;
                case Keys.PageDown: case Keys.MediaNextTrack: a = Theater.Act.NextChapter; break;
                case Keys.PageUp: case Keys.MediaPreviousTrack: a = Theater.Act.PrevChapter; break;
                case Keys.Tab: case Keys.D: case Keys.Apps: a = Theater.Act.Menu; break;
                case Keys.I: a = Theater.Act.Info; break;
            }
            if (a == Theater.Act.None) return false;
            Act(a);
            return true;
        }

        public void Wheel(int delta) { if (Active && started) Volume(delta > 0 ? 13 : -13); }

        void Hotkey(string name)
        {
            var v = F.Vlc;
            v.Post(() => v.Action(name));
        }

        void TogglePause()
        {
            St.Time = Now();
            stAt = DateTime.Now;
            St.State = Paused ? "playing" : "paused";
            var v = F.Vlc;
            v.Post(() => v.Cmd("pl_pause"));
            ShowOsd(4);
        }

        void Seek(int delta)
        {
            if (IsDisc && St.Length <= 0) return;
            double t = Now() + delta;
            if (St.Length > 0) t = Math.Min(t, St.Length - 3);
            t = Math.Max(0, t);
            St.Time = t;
            stAt = DateTime.Now;
            seekHold = DateTime.Now.AddSeconds(1.2);
            pendingSeek = t;
            Flash(delta > 0 ? "+" + delta + " sec" : "−" + (-delta) + " sec", -1);
            ShowOsd(4);
        }

        void Volume(int delta)
        {
            int v = (muteVolume >= 0 ? muteVolume : St.Volume) + delta;
            muteVolume = -1;
            v = Math.Max(0, Math.Min(384, v));
            St.Volume = v;
            F.Cfg.Volume = Math.Max(26, v);
            F.Cfg.Save();
            var vlc = F.Vlc;
            vlc.Post(() => vlc.Cmd("volume", v.ToString()));
            Flash("Volume  " + (int)Math.Round(v * 100 / 256.0) + "%", v / 384f);
        }

        void ToggleMute()
        {
            var vlc = F.Vlc;
            if (muteVolume >= 0)
            {
                int v = muteVolume;
                muteVolume = -1;
                St.Volume = v;
                vlc.Post(() => vlc.Cmd("volume", v.ToString()));
                Flash("Volume  " + (int)Math.Round(v * 100 / 256.0) + "%", v / 384f);
            }
            else
            {
                muteVolume = Math.Max(26, St.Volume);
                St.Volume = 0;
                vlc.Post(() => vlc.Cmd("volume", "0"));
                Flash("Muted", 0);
            }
        }

        // ---------- audio & subtitles ----------

        static string TrackKind(string k) { return k == "sub" || k == "subs" || k == "strack" ? "strack" : "atrack"; }

        // The TV's A / S keys: go to the next track and say which one is playing now.
        void CycleTrack(string kind)
        {
            var v = F.Vlc;
            int my = session;
            v.Post(() =>
            {
                var list = v.Tracks(kind);
                if (kind == "atrack") list = list.Where(t => t.Id >= 0).ToList();   // audio never cycles to "off"
                string msg;
                if (list.Count <= 1)
                    msg = kind == "atrack" ? "This movie has one audio track" : "No subtitles in this movie";
                else
                {
                    int i = list.FindIndex(t => t.On);
                    var next = list[(i + 1) % list.Count];
                    v.SelectTrack(kind, next.Id);
                    msg = (kind == "atrack" ? "Audio: " : "Subtitles: ") + next.Name;
                }
                F.BeginInvoke((Action)(() => { if (Active && session == my) Flash(msg, -1); }));
            });
        }

        public void SetTrack(string kind, int id)
        {
            if (!Active || !started) return;
            kind = TrackKind(kind);
            var v = F.Vlc;
            int my = session;
            v.Post(() =>
            {
                v.SelectTrack(kind, id);
                var on = v.Tracks(kind).FirstOrDefault(t => t.On);
                string msg = (kind == "atrack" ? "Audio: " : "Subtitles: ") + (on != null ? on.Name : "Off");
                F.BeginInvoke((Action)(() => { if (Active && session == my) Flash(msg, -1); }));
            });
        }

        // Positive = later. Subtitles showing too early -> a positive subtitle delay.
        public void SetDelay(string kind, double seconds)
        {
            if (!Active || !started) return;
            bool sub = TrackKind(kind) == "strack";
            seconds = Math.Max(-30, Math.Min(30, Math.Round(seconds, 2)));
            if (sub) St.SubDelay = seconds; else St.AudioDelay = seconds;
            var v = F.Vlc;
            string val = seconds.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            v.Post(() => v.Cmd(sub ? "subdelay" : "audiodelay", val));
            Flash((sub ? "Subtitle timing  " : "Audio timing  ") + (seconds > 0 ? "+" : "") + seconds.ToString("0.0#") + " s", -1);
        }

        public void SubtitleSize(string direction)
        {
            if (!Active || !started) return;
            Hotkey(direction == "up" ? "subtitle-text-scale-up" : direction == "down" ? "subtitle-text-scale-down" : "subtitle-text-scale-normal");
            Flash(direction == "up" ? "Subtitles bigger" : direction == "down" ? "Subtitles smaller" : "Subtitles normal size", -1);
        }

        // For the phone remote: this movie's tracks and timing (called from the remote server's thread).
        public string TracksJson()
        {
            var ser = new JavaScriptSerializer();
            if (!Active || !started) return ser.Serialize(new Dictionary<string, object> { { "playing", false } });
            var v = F.Vlc;
            Func<List<Vlc.Track>, List<Dictionary<string, object>>> pack = l => l.Select(t => new Dictionary<string, object> { { "id", t.Id }, { "name", t.Name }, { "on", t.On } }).ToList();
            var subs = v.Tracks("strack");
            return ser.Serialize(new Dictionary<string, object> {
                { "playing", true },
                { "title", Item != null ? Item.FullTitle : "" },
                { "audio", pack(v.Tracks("atrack").Where(t => t.Id >= 0).ToList()) },
                { "subs", pack(subs.Count > 1 ? subs : new List<Vlc.Track>()) },   // just "Off" means the movie has none
                { "audioDelay", St.AudioDelay },
                { "subDelay", St.SubDelay }
            });
        }

        // Shows the phone pairing code over the movie (null hides it).
        public void ShowCode(string code)
        {
            if (code == null) { if (flash != null && flash.StartsWith("Phone code")) flashUntil = DateTime.Now; Flash("Phone connected", -1); return; }
            flash = "Phone code:  " + code;
            flashLevel = -1;
            flashUntil = DateTime.Now.AddSeconds(45);
            Render();
        }

        void Flash(string text, float level)
        {
            flash = text;
            flashLevel = level;
            flashUntil = DateTime.Now.AddSeconds(1.6);
            Render();
        }

        void ShowOsd(double seconds)
        {
            osdUntil = DateTime.Now.AddSeconds(seconds);
            Render();
        }

        // ---------- mouse on the video ----------

        public void Mouse(Point p, MouseButtons button, Size size)
        {
            if (button == MouseButtons.None)
            {
                if (p == lastMouse) return;
                // The overlay appearing under a still pointer reports its position once; that isn't mouse use.
                bool first = !mouseSeen;
                mouseSeen = true;
                lastMouse = p;
                if (first) return;
                F.NoteMouse();
                if (!OsdVisible) ShowOsd(3); else osdUntil = DateTime.Now.AddSeconds(3);
                return;
            }
            F.NoteMouse();
            if (button == MouseButtons.Right || button == MouseButtons.XButton1) { Act(Theater.Act.Back); return; }
            if (button != MouseButtons.Left) return;
            float s = Math.Min(size.Width / 1920f, size.Height / 1080f);
            var v = new PointF((p.X - (size.Width - 1920 * s) / 2) / s, (p.Y - (size.Height - 1080 * s) / 2) / s);
            if (OsdVisible)
            {
                foreach (var b in buttons) if (b.Key.Contains(v)) { Act(b.Value); return; }
                var hit = RectangleF.Inflate(bar, 0, 18);
                if (started && St.Length > 0 && hit.Contains(v))
                {
                    double t = Math.Max(0, Math.Min(1, (v.X - bar.X) / bar.Width)) * St.Length;
                    Seek((int)(t - Now()));
                    return;
                }
            }
            if (failure != null) { Stop(); return; }
            if (next != null) { PlayNext(); return; }
            if (started) TogglePause();
        }

        // ---------- on-screen display ----------

        public void Render()
        {
            if (!Active || !ov.Visible) return;
            var size = ov.ClientSize;
            if (size.Width < 10 || size.Height < 10) return;
            bool vis = OsdVisible, flashing = flash != null && DateTime.Now < flashUntil;
            // Only redraw when something visible changed (the clock text ticks once a second at most).
            string frame = !vis && !flashing ? "hidden" + size : string.Concat(
                size, vis, flashing, (int)Now(), St.State, started, failure, flash, flashLevel, St.Volume, St.Length,
                next != null ? (int)(nextAt - DateTime.Now).TotalSeconds : -1, Ui.Clock(), St.Resolution, St.Hdr, St.AudioInfo,
                started ? 0 : (int)(DateTime.Now.TimeOfDay.TotalMilliseconds / 120));
            if (frame == lastFrame) return;
            lastFrame = frame;
            using (var g = ov.BeginFrame(size))
            {
                g.Clear(Color.FromArgb(1, 0, 0, 0));   // alpha 1 keeps the whole surface clickable
                buttons.Clear();
                if (vis || flashing) DrawOsd(g, size, vis, flashing);
            }
            ov.Present();
        }

        void DrawOsd(Graphics g, Size size, bool vis, bool flashing)
        {
            float s = Math.Min(size.Width / 1920f, size.Height / 1080f);
            float ox = (size.Width - 1920 * s) / 2, oy = (size.Height - 1080 * s) / 2;
            g.TranslateTransform(ox, oy);
            g.ScaleTransform(s, s);
            float L = -ox / s - 2, R = (size.Width - ox) / s + 2, T = -oy / s - 2, B = (size.Height - oy) / s + 2;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;

            if (vis)
            {
                using (var lg = new LinearGradientBrush(new RectangleF(L, T, R - L, 262 - T), Color.FromArgb(200, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), 90f))
                    g.FillRectangle(lg, L, T, R - L, 260 - T);
                using (var lg = new LinearGradientBrush(new RectangleF(L, 690, R - L, B - 690), Color.FromArgb(0, 0, 0, 0), Color.FromArgb(225, 0, 0, 0), 90f))
                    g.FillRectangle(lg, L, 691, R - L, B - 691);

                Ui.Text(g, Item.FullTitle, Ui.F(44, 1), Color.White, new RectangleF(80, 52, 1300, 62));
                var parts = new List<string>();
                if (Item.Year != "") parts.Add(Item.Year);
                if (IsDisc) parts.Add(Item.DiscType);
                if (St.Resolution != "")
                {
                    int w = 0, h = 0;
                    var xs = St.Resolution.Split('x');
                    if (xs.Length == 2) { int.TryParse(xs[0], out w); int.TryParse(xs[1], out h); }
                    parts.Add(w >= 3200 || h >= 1600 ? "4K Ultra HD" : w >= 1800 || h >= 1000 ? "1080p" : w >= 1200 || h >= 700 ? "720p" : h > 0 ? "SD" : St.Resolution);
                }
                if (St.Hdr != "") parts.Add(St.Hdr);
                if (St.AudioInfo != "") parts.Add(St.AudioInfo);
                Ui.Text(g, string.Join("   ·   ", parts), Ui.F(22), Color.FromArgb(215, 220, 220, 228), new RectangleF(82, 116, 1300, 34));
                Ui.Text(g, Ui.Clock(), Ui.F(40, 3), Color.White, new RectangleF(1340, 52, 500, 56), StringAlignment.Far);

                if (failure != null) DrawFailure(g);
                else if (!started)
                {
                    float a = (float)(DateTime.Now.TimeOfDay.TotalMilliseconds / 2.5 % 360);
                    using (var pen = new Pen(Ui.Accent, 7) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(pen, 960 - 36, 500 - 36, 72, 72, a, 270);
                    Ui.Text(g, IsDisc ? "Starting the disc…" : "Loading…", Ui.F(26), Color.White, new RectangleF(560, 560, 800, 40), StringAlignment.Center);
                }
                else DrawTransport(g);
            }

            if (started && Paused && failure == null && next == null)
            {
                Ui.FillRound(g, Color.FromArgb(150, 0, 0, 0), new RectangleF(960 - 80, 540 - 80, 160, 160), 80);
                Ui.Text(g, "", Ui.F(64, 9), Color.White, new RectangleF(960 - 80, 540 - 80, 160, 160), StringAlignment.Center, StringAlignment.Center);
            }

            if (next != null) DrawUpNext(g);

            if (flashing)
            {
                var r = new RectangleF(1840 - 380, 150, 380, flashLevel >= 0 ? 92 : 64);
                Ui.FillRound(g, Color.FromArgb(215, 20, 21, 27), r, 16);
                Ui.Text(g, flash, Ui.F(26, 1), Color.White, new RectangleF(r.X + 26, r.Y + 6, r.Width - 52, 52), StringAlignment.Near, StringAlignment.Center);
                if (flashLevel >= 0)
                {
                    Ui.FillRound(g, Color.FromArgb(70, 255, 255, 255), new RectangleF(r.X + 26, r.Y + 66, r.Width - 52, 8), 4);
                    Ui.FillRound(g, Ui.Accent, new RectangleF(r.X + 26, r.Y + 66, (r.Width - 52) * Math.Min(1, flashLevel), 8), 4);
                }
            }
        }

        void DrawTransport(Graphics g)
        {
            double now = Now(), len = St.Length;
            bar = new RectangleF(80, 968, 1760, 8);
            if (len > 0)
            {
                float frac = (float)Math.Max(0, Math.Min(1, now / len));
                Ui.FillRound(g, Color.FromArgb(70, 255, 255, 255), bar, 4);
                Ui.FillRound(g, Ui.Accent, new RectangleF(bar.X, bar.Y, bar.Width * frac, bar.Height), 4);
                float kx = bar.X + bar.Width * frac;
                using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, kx - 11, bar.Y + 4 - 11, 22, 22);
                Ui.Text(g, Ui.Time(now), Ui.F(26, 1), Color.White, new RectangleF(80, 990, 400, 40));
                Ui.Text(g, "−" + Ui.Time(len - now), Ui.F(26, 1), Color.White, new RectangleF(1440, 990, 400, 40), StringAlignment.Far);
                Ui.Text(g, "Ends at " + DateTime.Now.AddSeconds(len - now).ToString("h:mm tt"), Ui.F(22), Color.FromArgb(200, 220, 220, 228), new RectangleF(560, 994, 800, 36), StringAlignment.Center);
            }
            else if (IsDisc)
            {
                Ui.Text(g, "Disc menu — use the arrows and OK to choose", Ui.F(24), Color.White, new RectangleF(80, 990, 1200, 40));
            }

            // Transport buttons (clickable)
            var defs = new List<Tuple<string, Act, bool>> {
                Tuple.Create("", Theater.Act.Rewind, true),
                Tuple.Create(Paused ? "" : "", Theater.Act.PlayPause, true),
                Tuple.Create("", Theater.Act.Forward, true),
            };
            float bw = 76, gap = 22, total = defs.Count * bw + (defs.Count - 1) * gap, x = 960 - total / 2, y = 870;
            foreach (var d in defs)
            {
                var r = new RectangleF(x, y, bw, bw);
                bool main = d.Item2 == Theater.Act.PlayPause;
                Ui.FillRound(g, main ? Color.FromArgb(235, 255, 255, 255) : Color.FromArgb(120, 40, 42, 52), r, bw / 2);
                Ui.Text(g, d.Item1, Ui.F(main ? 30 : 24, 9), main ? Ui.Bg : Color.White, r, StringAlignment.Center, StringAlignment.Center);
                buttons.Add(new KeyValuePair<RectangleF, Act>(r, d.Item2));
                x += bw + gap;
            }
            float sx = 80;
            foreach (var pair in new[] { Tuple.Create("Audio", Theater.Act.Audio), Tuple.Create("Subtitles", Theater.Act.Subs) })
            {
                float w = Ui.Width(g, pair.Item1, Ui.F(20, 1)) + 44;
                var r = new RectangleF(sx, 888, w, 44);
                Ui.FillRound(g, Color.FromArgb(120, 40, 42, 52), r, 22);
                Ui.Text(g, pair.Item1, Ui.F(20, 1), Color.White, r, StringAlignment.Center, StringAlignment.Center);
                buttons.Add(new KeyValuePair<RectangleF, Act>(r, pair.Item2));
                sx += w + 12;
            }
            var stop = new RectangleF(1840 - 120, 888, 120, 44);
            Ui.FillRound(g, Color.FromArgb(120, 40, 42, 52), stop, 22);
            Ui.Text(g, "Stop", Ui.F(20, 1), Color.White, stop, StringAlignment.Center, StringAlignment.Center);
            buttons.Add(new KeyValuePair<RectangleF, Act>(stop, Theater.Act.Stop));

            bool pad = F.UsingController;
            string hints = pad
                ? (IsDisc ? "D-pad~A:Disc menu|Start:Open disc menu|LB~RB:Skip|X:Pause|B:Exit"
                          : "A:Pause|LB~RB:Skip 10s / 30s|LT~RT:Volume|Y:Info|B:Exit")
                : (IsDisc ? "Arrows~Enter:Disc menu|Tab:Open disc menu|[~]:Skip|Space:Pause|Esc:Exit"
                          : "Enter:Pause|◀~▶:Skip 10s / 30s|A:Audio|S:Subtitles|+~−:Volume|Esc:Exit");
            Ui.Hints(g, new RectangleF(80, 1036, 1760, 34), hints, StringAlignment.Center, pad, Color.FromArgb(200, 220, 220, 228));
        }

        void DrawFailure(Graphics g)
        {
            var r = new RectangleF(510, 390, 900, 300);
            Ui.FillRound(g, Color.FromArgb(235, 24, 26, 33), r, 22);
            Ui.Text(g, "Can't play this", Ui.F(38, 2), Color.White, new RectangleF(r.X + 50, r.Y + 40, r.Width - 100, 56));
            Ui.Text(g, failure, Ui.F(23), Color.FromArgb(210, 214, 222), new RectangleF(r.X + 50, r.Y + 104, r.Width - 100, 120), StringAlignment.Near, StringAlignment.Near, true);
            Ui.Text(g, "Press Back to return", Ui.F(20), Ui.Accent, new RectangleF(r.X + 50, r.Bottom - 56, r.Width - 100, 30));
        }

        void DrawUpNext(Graphics g)
        {
            var r = new RectangleF(1840 - 560, 700, 560, 210);
            Ui.FillRound(g, Color.FromArgb(235, 24, 26, 33), r, 20);
            int secs = Math.Max(0, (int)Math.Ceiling((nextAt - DateTime.Now).TotalSeconds));
            Ui.Text(g, "UP NEXT", Ui.F(18, 2), Ui.Accent, new RectangleF(r.X + 34, r.Y + 28, 400, 26));
            Ui.Text(g, next.Title, Ui.F(30, 1), Color.White, new RectangleF(r.X + 34, r.Y + 58, r.Width - 68, 44));
            Ui.Text(g, "Starts in " + secs + " sec", Ui.F(22), Ui.Dim, new RectangleF(r.X + 34, r.Y + 104, 400, 32));
            Ui.Text(g, "OK  play now      Back  exit", Ui.F(19), Color.FromArgb(200, 220, 220, 228), new RectangleF(r.X + 34, r.Y + 150, 480, 30));
        }
    }

    // Transparent, click-catching window that floats over the video and shows the on-screen display.
    class PlayerOverlay : Form
    {
        readonly Player player;

        public PlayerOverlay(Player p)
        {
            player = p;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00080000 /*LAYERED*/ | 0x08000000 /*NOACTIVATE*/ | 0x00000080 /*TOOLWINDOW*/;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE -> MA_NOACTIVATE
            base.WndProc(ref m);
        }

        protected override void OnMouseMove(MouseEventArgs e) { player.Mouse(e.Location, MouseButtons.None, ClientSize); }
        protected override void OnMouseDown(MouseEventArgs e) { player.Mouse(e.Location, e.Button, ClientSize); }

        IntPtr memDc, dib, oldObj;
        Bitmap surface;
        Size surfaceSize;

        // Returns a Graphics that draws straight into the window's pixel buffer (premultiplied ARGB).
        public Graphics BeginFrame(Size size)
        {
            if (surface == null || surfaceSize != size)
            {
                FreeSurface();
                var bi = new BitmapInfoHeader { biSize = 40, biWidth = size.Width, biHeight = -size.Height, biPlanes = 1, biBitCount = 32 };
                IntPtr screen = GetDC(IntPtr.Zero);
                memDc = CreateCompatibleDC(screen);
                ReleaseDC(IntPtr.Zero, screen);
                IntPtr bits;
                dib = CreateDIBSection(memDc, ref bi, 0, out bits, IntPtr.Zero, 0);
                oldObj = SelectObject(memDc, dib);
                surface = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, bits);
                surfaceSize = size;
            }
            return Graphics.FromImage(surface);
        }

        public void Present()
        {
            if (surface == null) return;
            var size = new NSize { cx = surfaceSize.Width, cy = surfaceSize.Height };
            var src = new NPoint();
            var dst = new NPoint { x = Left, y = Top };
            var blend = new Blend { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
            UpdateLayeredWindow(Handle, IntPtr.Zero, ref dst, ref size, memDc, ref src, 0, ref blend, 2);
        }

        void FreeSurface()
        {
            if (surface != null) { surface.Dispose(); surface = null; }
            if (memDc != IntPtr.Zero)
            {
                SelectObject(memDc, oldObj);
                DeleteObject(dib);
                DeleteDC(memDc);
                memDc = dib = IntPtr.Zero;
            }
        }

        protected override void Dispose(bool disposing)
        {
            FreeSurface();
            base.Dispose(disposing);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BitmapInfoHeader
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }

        [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

        [StructLayout(LayoutKind.Sequential)] struct NPoint { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct NSize { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] struct Blend { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NPoint pptDst, ref NSize psize, IntPtr hdcSrc, ref NPoint pptSrc, int crKey, ref Blend pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);
    }
}
