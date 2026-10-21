// Wi-Fi settings: Ethernet status, nearby Wi-Fi networks, and a password screen that takes typing
// from a keyboard, the phone remote, or an on-screen keyboard driven with arrows or a controller.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;

namespace Theater
{
    class WifiScreen : Screen
    {
        class Row { public string Section, Label, Value; public Action Run; public Wifi.Network Net; }
        readonly List<Row> rows = new List<Row>();
        int sel;
        float scroll;
        List<Wifi.Network> nets;
        Wifi.Status wifi;
        Wifi.Wired wired;
        bool scanning;
        string busy;   // what's happening right now ("Connecting to …"), shown on the status card

        const float ListX = 104, ListW = 1160, ListTop = 150, ListBottom = 1000, ItemH = 70, ItemGap = 10, HeaderH = 52;

        // THEATER_TEST_WIFI=1 adds a pretend network so the password screen can be tried without changing networks.
        static readonly bool FakeNetwork = Environment.GetEnvironmentVariable("THEATER_TEST_WIFI") == "1";
        public const string FakeSsid = "Test network (pretend)";

        public WifiScreen(TheaterForm f) : base(f) { Load(true); }

        public override string Name { get { return "wifi"; } }
        public override string FocusTitle { get { return sel < rows.Count ? rows[sel].Label : ""; } }

        public override void Refresh() { Load(false); }

        void Load(bool rescan)
        {
            if (scanning) return;
            scanning = true;
            Build();
            Task.Run(() =>
            {
                var w = Wifi.Ethernet();
                var s = Wifi.Get();
                var n = Wifi.Nearby(rescan);
                return Tuple.Create(w, s, n);
            }).ContinueWith(t => F.BeginInvoke((Action)(() =>
            {
                scanning = false;
                if (!t.IsFaulted) { wired = t.Result.Item1; wifi = t.Result.Item2; nets = t.Result.Item3; }
                if (FakeNetwork && nets != null && !nets.Exists(x => x.Ssid == FakeSsid)) nets.Add(new Wifi.Network { Ssid = FakeSsid, Auth = "WPA2-Personal", Signal = 40 });
                Build();
            })));
        }

        void Build()
        {
            string selLabel = sel < rows.Count ? rows[sel].Label : null;
            rows.Clear();
            rows.Add(new Row
            {
                Section = "Ethernet",
                Label = "Ethernet (cable)",
                Value = wired == null ? "Checking…" : !wired.Present ? "This PC has no Ethernet port" : wired.Connected ? "Connected" + (wired.Speed != "" ? "  ·  " + wired.Speed : "") : "No cable plugged in",
                Run = ShowEthernet
            });
            if (nets == null) rows.Add(new Row { Section = "Wi-Fi networks", Label = "Looking for networks…", Value = "", Run = () => { } });
            else if (nets.Count == 0) rows.Add(new Row { Section = "Wi-Fi networks", Label = "No Wi-Fi networks found", Value = wifi != null && wifi.Adapter == "" ? "No Wi-Fi adapter" : "", Run = () => Load(true) });
            else
                foreach (var n in nets)
                {
                    var net = n;
                    string v = n.Current ? "Connected" : n.Saved ? "Saved" : n.Open ? "Open (no password)" : n.Enterprise ? "Work or school sign-in" : "Secured";
                    if (n.Signal >= 0) v += "  ·  " + n.Signal + "%";
                    rows.Add(new Row { Section = "Wi-Fi networks", Label = n.Ssid, Value = v, Net = n, Run = () => Choose(net) });
                }
            rows.Add(new Row { Section = "Options", Label = "Look for networks again", Value = scanning ? "Searching…" : "", Run = () => Load(true) });
            bool auto = F.Cfg.WifiAuto != "";
            rows.Add(new Row
            {
                Section = "Options",
                Label = "Auto-connect when the app starts",
                Value = auto ? "On  ·  " + F.Cfg.WifiAuto : "Off",
                Run = ToggleAuto
            });
            // Keep the same row selected after a refresh.
            if (selLabel != null) { int i = rows.FindIndex(r => r.Label == selLabel); if (i >= 0) sel = i; }
            sel = Math.Max(0, Math.Min(sel, rows.Count - 1));
            F.Kick();
        }

        void ShowEthernet()
        {
            if (wired == null) return;
            string msg = !wired.Present ? "This PC doesn't have an Ethernet port (or it's turned off in Windows)."
                : wired.Connected ? "Ethernet is connected" + (wired.Speed != "" ? " at " + wired.Speed : "") + (wired.Address != "" ? ".\nAddress on your network: " + wired.Address : "") + "\n\nA cable is faster and steadier than Wi-Fi for 4K movies and the phone remote."
                : "Plug a network cable from your router into this PC. Ethernet connects by itself; there's nothing to type.\n\nPort: " + wired.Adapter;
            F.ShowDialog("Ethernet", msg, new[] { "Done", "Check again" }, i => { if (i == 1) Load(false); });
        }

        void ToggleAuto()
        {
            if (F.Cfg.WifiAuto != "") F.Cfg.WifiAuto = "";
            else if (wifi != null && wifi.Connected) F.Cfg.WifiAuto = wifi.Ssid;
            else { F.ShowToast("Connect to a Wi-Fi network first", 3); return; }
            F.Cfg.Save();
            F.ShowToast(F.Cfg.WifiAuto != "" ? "Will connect to " + F.Cfg.WifiAuto + " when the app starts" : "Auto-connect is off", 3);
            Build();
        }

        void Choose(Wifi.Network n)
        {
            if (busy != null) { F.ShowToast(busy, 2); return; }
            if (n.Current)
            {
                F.ShowDialog(n.Ssid, "This PC is connected to " + n.Ssid + (n.Signal >= 0 ? " (" + n.Signal + "% signal)" : "") + ".", new[] { "Done", "Enter a new password" },
                    i => { if (i == 1) F.Push(new WifiPasswordScreen(F, n, this)); });
                return;
            }
            if (n.Enterprise) { F.ShowDialog(n.Ssid, "This network needs a work or school sign-in, which " + AppInfo.Name + " can't set up. Use Wi-Fi in Windows Settings for it.", new[] { "OK" }, i => { }); return; }
            string warn = wifi != null && wifi.Connected ? "\n\nThis PC will leave " + wifi.Ssid + ". If the phone remote is on " + wifi.Ssid + ", move the phone to the new network too." : "";
            if (n.Saved)
                F.ShowDialog("Connect to " + n.Ssid + "?", "This network is saved on this PC, so no password is needed." + warn, new[] { "Connect", "Enter a new password", "Cancel" }, i =>
                {
                    if (i == 0) Connect(n, null);
                    else if (i == 1) F.Push(new WifiPasswordScreen(F, n, this));
                });
            else if (n.Open)
                F.ShowDialog("Connect to " + n.Ssid + "?", "This is an open network: anyone nearby can see what's sent over it." + warn, new[] { "Connect", "Cancel" }, i => { if (i == 0) Connect(n, ""); });
            else F.Push(new WifiPasswordScreen(F, n, this));
        }

        // password: null for a saved network. Calls back with "" when connected, otherwise a reason.
        public void Connect(Wifi.Network n, string password, Action<string> done = null)
        {
            busy = "Connecting to " + n.Ssid + "…";
            F.ShowToast(busy, 25);
            F.Kick();
            Task.Run(() =>
            {
                if (n.Ssid == FakeSsid) { System.Threading.Thread.Sleep(1500); return "Test mode: pretend network, nothing was changed"; }
                return password == null ? (Wifi.ConnectSaved(n.Ssid) ? "" : "Couldn't connect to " + n.Ssid) : Wifi.ConnectNew(n, password);
            }).ContinueWith(t => F.BeginInvoke((Action)(() =>
            {
                busy = null;
                string err = t.IsFaulted ? "Couldn't connect to " + n.Ssid : t.Result;
                F.ShowToast(err == "" ? "Connected to " + n.Ssid : err, 4);
                F.UpdateRemoteUrl();
                if (done != null) done(err);
                Load(false);
            })));
        }

        public bool Busy { get { return busy != null; } }

        float Layout(Dictionary<int, RectangleF> rects)
        {
            float y = 0;
            string section = null;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Section != section) { section = rows[i].Section; y += (i == 0 ? 0 : 14) + HeaderH; }
                rects[i] = new RectangleF(ListX, ListTop + y, ListW, ItemH);
                y += ItemH + ItemGap;
            }
            return y;
        }

        readonly Dictionary<int, RectangleF> rects = new Dictionary<int, RectangleF>();

        void KeepVisible()
        {
            rects.Clear();
            Layout(rects);
            RectangleF r;
            if (!rects.TryGetValue(sel, out r)) return;
            float top = r.Y - ListTop - (sel == 0 || rows[sel].Section != rows[sel - 1].Section ? HeaderH : 0), bottom = r.Bottom - ListTop, view = ListBottom - ListTop;
            if (top < scroll) scroll = top;
            if (bottom > scroll + view) scroll = bottom - view;
            scroll = Math.Max(0, scroll);
        }

        static void SignalBars(Graphics g, float x, float y, int signal, Color on, Color off)
        {
            for (int b = 0; b < 4; b++)
            {
                float h = 8 + b * 7;
                Ui.FillRound(g, signal >= 0 && signal >= b * 25 + 1 ? on : off, new RectangleF(x + b * 10, y + 30 - h, 7, h), 2);
            }
        }

        public override void Draw(Graphics g)
        {
            Ui.Text(g, "Wi-Fi settings", Ui.F(52, 2), Ui.Ink, new RectangleF(104, 44, 1000, 76), StringAlignment.Near, StringAlignment.Center);
            DrawClock(g);
            rects.Clear();
            Layout(rects);
            var state = g.Save();
            g.SetClip(new RectangleF(0, ListTop - 6, 1300, ListBottom - ListTop + 12));
            string section = null;
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rects[i];
                r.Y -= scroll;
                if (rows[i].Section != section)
                {
                    section = rows[i].Section;
                    Ui.Text(g, section.ToUpperInvariant(), Ui.F(17, 2), Ui.Accent, new RectangleF(r.X + 4, r.Y - 40, 600, 30));
                }
                if (r.Bottom < ListTop - 10 || r.Y > ListBottom + 10) continue;
                bool on = i == sel;
                var row = rows[i];
                Ui.FillRound(g, on ? Ui.Panel2 : Ui.Panel, r, 14);
                if (on) Ui.FillRound(g, Ui.Accent, new RectangleF(r.X, r.Y + 14, 5, r.Height - 28), 2);
                float lx = r.X + 30;
                if (row.Net != null)
                {
                    SignalBars(g, lx, r.Y + 18, row.Net.Signal >= 0 ? row.Net.Signal : 50, row.Net.Current ? Ui.Accent : Ui.Ink, Color.FromArgb(60, Ui.Ink));
                    lx += 56;
                    if (!row.Net.Open) Ui.Text(g, "", Ui.F(18, 9), Ui.Dim, new RectangleF(lx - 2, r.Y, 30, r.Height), StringAlignment.Near, StringAlignment.Center);
                    lx += 30;
                }
                Ui.Text(g, row.Label, Ui.F(23, on ? 1 : 0), Ui.Ink, new RectangleF(lx, r.Y, 560 - (lx - r.X), r.Height), StringAlignment.Near, StringAlignment.Center);
                Ui.Text(g, row.Value, Ui.F(20), row.Net != null && row.Net.Current ? Ui.Accent : on ? Ui.Ink : Ui.Dim, new RectangleF(r.X + 560, r.Y, r.Width - 590, r.Height), StringAlignment.Far, StringAlignment.Center);
            }
            g.Restore(state);
            DrawStatus(g);
            Ui.Hints(g, new RectangleF(104, 1022, 1700, 30), F.UsingController ? "D-pad~A:Choose|B:Back" : "Arrows~Enter:Choose|Esc:Back", StringAlignment.Near, F.UsingController);
        }

        void DrawStatus(Graphics g)
        {
            var card = new RectangleF(1310, 150, 520, 850);
            Ui.FillRound(g, Ui.Panel, card, 18);
            bool wifiOn = wifi != null && wifi.Connected, cable = wired != null && wired.Connected;
            Ui.Text(g, cable ? "" : "", Ui.F(84, 9), wifiOn || cable ? Ui.Accent : Ui.Dim, new RectangleF(card.X, card.Y + 60, card.Width, 110), StringAlignment.Center, StringAlignment.Center);
            string head = busy ?? (wifi == null && wired == null ? "Checking…" : cable && wifiOn ? "Ethernet and Wi-Fi" : cable ? "Connected by Ethernet" : wifiOn ? "Connected to Wi-Fi" : "Not connected");
            Ui.Text(g, head, Ui.F(30, 2), Ui.Ink, new RectangleF(card.X + 30, card.Y + 200, card.Width - 60, 84), StringAlignment.Center, StringAlignment.Near, true);
            float y = card.Y + 300;
            Action<string, string> line = (a, b) =>
            {
                Ui.Text(g, a, Ui.F(18), Ui.Dim, new RectangleF(card.X + 40, y, 440, 26), StringAlignment.Center);
                Ui.Text(g, b, Ui.F(24, 1), Ui.Ink, new RectangleF(card.X + 20, y + 26, 480, 36), StringAlignment.Center);
                y += 80;
            };
            if (wifiOn) line("Wi-Fi network", wifi.Ssid + (wifi.Signal != "" ? "  ·  " + wifi.Signal : ""));
            if (cable) line("Ethernet", "Connected" + (wired.Speed != "" ? "  ·  " + wired.Speed : ""));
            line("Phone remote", F.RemoteUrl);
            Ui.Text(g, "Typing a password: use a keyboard, the on-screen keyboard (arrows or a controller), or the text box in the phone remote.",
                Ui.F(19), Ui.Dim, new RectangleF(card.X + 40, card.Bottom - 150, 440, 120), StringAlignment.Center, StringAlignment.Near, true);
        }

        public override void Act(Act a)
        {
            switch (a)
            {
                case Theater.Act.Up: sel = Math.Max(0, sel - 1); break;
                case Theater.Act.Down: sel = Math.Min(rows.Count - 1, sel + 1); break;
                case Theater.Act.Ok: if (sel < rows.Count) rows[sel].Run(); return;
                case Theater.Act.Back: F.Pop(); return;
                case Theater.Act.Home: F.GoHome(); return;
                default: return;
            }
            KeepVisible();
            F.Kick();
        }

        int Hit(PointF p)
        {
            if (p.Y < ListTop || p.Y > ListBottom) return -1;
            foreach (var kv in rects) { var r = kv.Value; r.Y -= scroll; if (r.Contains(p)) return kv.Key; }
            return -1;
        }

        public override void Move(PointF p) { int i = Hit(p); if (i >= 0 && i != sel) { sel = i; F.Kick(); } }
        public override void Click(PointF p) { int i = Hit(p); if (i >= 0) { sel = i; rows[i].Run(); } }

        public override void Wheel(int delta)
        {
            var tmp = new Dictionary<int, RectangleF>();
            float max = Math.Max(0, Layout(tmp) - (ListBottom - ListTop));
            scroll = Math.Max(0, Math.Min(max, scroll - delta * 0.8f));
            F.Kick();
        }
    }

    // Password entry for one network.
    class WifiPasswordScreen : Screen
    {
        class Key { public string Text, Id; public int Row; public float X, W; public RectangleF Rect; }

        readonly Wifi.Network net;
        readonly WifiScreen owner;
        string pw = "", error = "";
        bool show, shift, symbols, typed;   // typed: the last input was real typing (keyboard or phone), so OK means Connect
        bool connecting;
        int focus;
        List<Key> keys;

        static readonly string[] Letters = { "1234567890", "qwertyuiop", "asdfghjkl-", "zxcvbnm_.@" };
        static readonly string[] Symbols = { "!@#$%^&*()", "~`+=[]{}\\|", ";:'\",<>/?-", "_.!?@#$%&*" };
        const float KeyW = 104, KeyH = 80, Gap = 12, KbX = (1920 - (10 * KeyW + 9 * Gap)) / 2, KbY = 420;

        public WifiPasswordScreen(TheaterForm f, Wifi.Network n, WifiScreen owner) : base(f)
        {
            net = n;
            this.owner = owner;
            BuildKeys();
            focus = keys.FindIndex(k => k.Text == "q");
        }

        public override string Name { get { return "wifipass"; } }
        public override string FocusTitle { get { return "Wi-Fi password for " + net.Ssid; } }   // never the password itself
        public override bool WantsText { get { return true; } }

        void BuildKeys()
        {
            keys = new List<Key>();
            var src = symbols ? Symbols : Letters;
            for (int r = 0; r < 4; r++)
                for (int c = 0; c < 10; c++)
                {
                    string ch = src[r][c].ToString();
                    if (shift && !symbols) ch = ch.ToUpperInvariant();
                    keys.Add(new Key { Text = ch, Id = "char", Row = r, X = c, W = 1 });
                }
            keys.Add(new Key { Text = shift ? "⇧ ABC" : "⇧ abc", Id = "shift", Row = 4, X = 0, W = 2 });
            keys.Add(new Key { Text = symbols ? "abc" : "&123", Id = "symbols", Row = 4, X = 2, W = 2 });
            keys.Add(new Key { Text = "Space", Id = "space", Row = 4, X = 4, W = 4 });
            keys.Add(new Key { Text = "⌫ Delete", Id = "delete", Row = 4, X = 8, W = 2 });
            keys.Add(new Key { Text = show ? "Hide password" : "Show password", Id = "show", Row = 5, X = 0, W = 3 });
            keys.Add(new Key { Text = "Connect", Id = "connect", Row = 5, X = 3, W = 7 });
            foreach (var k in keys)
                k.Rect = new RectangleF(KbX + k.X * (KeyW + Gap), KbY + k.Row * (KeyH + Gap) + (k.Row == 5 ? 14 : 0), k.W * KeyW + (k.W - 1) * Gap, KeyH);
        }

        void Press(Key k)
        {
            if (connecting) return;
            error = "";
            switch (k.Id)
            {
                case "char": Type(k.Text); break;
                case "space": Type(" "); break;
                case "delete": Backspace(); break;
                case "shift": shift = !shift; Rebuild(); break;
                case "symbols": symbols = !symbols; Rebuild(); break;
                case "show": show = !show; Rebuild(); break;
                case "connect": Connect(); break;
            }
            F.Kick();
        }

        void Rebuild() { string id = keys[focus].Id; int row = keys[focus].Row; float x = keys[focus].X; BuildKeys(); focus = keys.FindIndex(k => k.Row == row && k.X == x); if (focus < 0) focus = keys.FindIndex(k => k.Id == id); if (focus < 0) focus = 0; }

        void Type(string s) { if (pw.Length + s.Length <= 63) pw += s; }

        public override void Char(char c)
        {
            if (connecting || c < ' ') return;
            typed = true;
            error = "";
            Type(c.ToString());
            F.Kick();
        }

        public override bool Backspace()
        {
            if (connecting) return true;
            if (pw.Length > 0) pw = pw.Substring(0, pw.Length - 1);
            F.Kick();
            return true;   // never leaves the screen; Esc or B does
        }

        public override void SetText(string t)
        {
            if (connecting) return;
            typed = true;
            error = "";
            pw = (t ?? "").Length > 63 ? t.Substring(0, 63) : (t ?? "");
            F.Kick();
        }

        void Connect()
        {
            if (connecting) return;
            if (!net.Open && (pw.Length < 8 || pw.Length > 63)) { error = "Wi-Fi passwords are 8 to 63 characters"; F.Kick(); return; }
            if (owner.Busy) { error = "Already connecting, one moment…"; F.Kick(); return; }
            connecting = true;
            F.Kick();
            owner.Connect(net, pw, err =>
            {
                connecting = false;
                if (err == "")
                {
                    pw = "";
                    if (F.Current == this) F.Pop();
                }
                else { error = err; F.Kick(); }
            });
        }

        public override void Act(Act a)
        {
            switch (a)
            {
                case Theater.Act.Up: Nav(0, -1); break;
                case Theater.Act.Down: Nav(0, 1); break;
                case Theater.Act.Left: Nav(-1, 0); break;
                case Theater.Act.Right: Nav(1, 0); break;
                // After typing on a keyboard or the phone, Enter/OK connects; otherwise it presses the highlighted key.
                case Theater.Act.Ok: if (typed) Connect(); else Press(keys[focus]); break;
                case Theater.Act.PlayPause: Backspace(); break;   // X on a controller
                case Theater.Act.Menu: Connect(); break;          // Start on a controller
                case Theater.Act.Back: if (!connecting) { pw = ""; F.Pop(); } return;
                case Theater.Act.Home: if (!connecting) { pw = ""; F.GoHome(); } return;
            }
            F.Kick();
        }

        void Nav(int dx, int dy)
        {
            typed = false;
            var cur = keys[focus];
            if (dx != 0)
            {
                int best = -1;
                foreach (var k in keys)
                    if (k.Row == cur.Row && (dx > 0 ? k.X > cur.X : k.X < cur.X) && (best < 0 || Math.Abs(k.X - cur.X) < Math.Abs(keys[best].X - cur.X))) best = keys.IndexOf(k);
                if (best >= 0) focus = best;
                return;
            }
            int row = cur.Row + dy;
            if (row < 0 || row > 5) return;
            float cx = cur.X + cur.W / 2;
            int pick = -1;
            foreach (var k in keys)
            {
                if (k.Row != row) continue;
                if (cx >= k.X && cx < k.X + k.W) { pick = keys.IndexOf(k); break; }
                if (pick < 0 || Math.Abs(k.X + k.W / 2 - cx) < Math.Abs(keys[pick].X + keys[pick].W / 2 - cx)) pick = keys.IndexOf(k);
            }
            if (pick >= 0) focus = pick;
        }

        public override void Draw(Graphics g)
        {
            Ui.Text(g, "Wi-Fi password", Ui.F(52, 2), Ui.Ink, new RectangleF(104, 44, 1000, 76), StringAlignment.Near, StringAlignment.Center);
            DrawClock(g);
            Ui.Text(g, "for " + net.Ssid, Ui.F(26), Ui.Dim, new RectangleF(108, 118, 1200, 40));

            var box = new RectangleF(KbX, 190, 10 * KeyW + 9 * Gap, 100);
            Ui.FillRound(g, Ui.Panel, box, 16);
            using (var pen = new Pen(Ui.Accent, 3)) using (var path = Ui.Round(box, 16)) g.DrawPath(pen, path);
            string shown = show ? pw : new string('•', pw.Length);
            var font = show ? Ui.F(40, 1) : Ui.F(40, 2);
            if (pw.Length == 0) Ui.Text(g, "Type the password", Ui.F(32), Ui.Dim, new RectangleF(box.X + 34, box.Y, box.Width - 68, box.Height), StringAlignment.Near, StringAlignment.Center);
            else
            {
                float w = Ui.Width(g, shown, font), room = box.Width - 90;
                // Long passwords scroll so the end (where typing happens) stays visible.
                var clip = g.Save();
                g.SetClip(new RectangleF(box.X + 20, box.Y, box.Width - 40, box.Height));
                float tx = box.X + 34 - Math.Max(0, w - room);
                Ui.Text(g, shown, font, Ui.Ink, new RectangleF(tx, box.Y, w + 60, box.Height), StringAlignment.Near, StringAlignment.Center);
                g.Restore(clip);
                Ui.FillRound(g, Ui.Accent, new RectangleF(Math.Min(box.X + 40 + w, box.Right - 50), box.Y + 26, 3, box.Height - 52), 1);
            }
            Ui.Text(g, pw.Length + (pw.Length == 1 ? " character" : " characters"), Ui.F(18), Ui.Dim, new RectangleF(box.X, box.Bottom + 10, box.Width, 26), StringAlignment.Far);
            string status = connecting ? "Connecting to " + net.Ssid + "…" : error;
            Ui.Text(g, status, Ui.F(22, 1), connecting ? Ui.Accent : Color.FromArgb(240, 100, 90), new RectangleF(box.X, box.Bottom + 8, box.Width - 200, 34));

            for (int i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                bool on = i == focus && !typed;
                bool primary = k.Id == "connect";
                var bg = primary ? (on ? Ui.Accent : Color.FromArgb(150, Ui.Accent)) : on ? Ui.Ink : (k.Id == "char" || k.Id == "space") ? Ui.Panel2 : Ui.Panel;
                var fg = primary || on ? Ui.Bg : Ui.Ink;
                if ((k.Id == "shift" && shift) || (k.Id == "symbols" && symbols) || (k.Id == "show" && show)) { if (!on) { bg = Ui.Panel2; fg = Ui.Accent; } }
                Ui.FillRound(g, bg, k.Rect, 12);
                Ui.Text(g, k.Text, k.Id == "char" ? Ui.F(34, 1) : Ui.F(24, 1), fg, k.Rect, StringAlignment.Center, StringAlignment.Center);
            }
            Ui.Hints(g, new RectangleF(104, 1022, 1712, 30), F.UsingController
                ? "D-pad~A:Type the key|X:Delete|Start:Connect|B:Cancel"
                : "Type on your keyboard or phone, then|Enter:Connect|Backspace:Delete|Esc:Cancel", StringAlignment.Center, F.UsingController);
        }

        int HitKey(PointF p) { for (int i = 0; i < keys.Count; i++) if (keys[i].Rect.Contains(p)) return i; return -1; }

        public override void Move(PointF p) { int i = HitKey(p); if (i >= 0 && (i != focus || typed)) { focus = i; typed = false; F.Kick(); } }
        public override void Click(PointF p) { int i = HitKey(p); if (i >= 0) { focus = i; typed = false; Press(keys[i]); } }
        public override void Leave() { pw = ""; }
    }
}
