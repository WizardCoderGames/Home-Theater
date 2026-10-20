// Setup for Eric's Home Theater: installs per user (no admin needed), creates shortcuts,
// registers an uninstaller in Settings > Apps, and can remove everything again.
//
//   Setup.exe                       interactive install / update
//   Setup.exe /uninstall            remove (asks first); this is what Settings > Apps runs
//   Setup.exe /silent [/dir="..."] [/noshortcuts] [/nodesktop] [/nostartmenu] [/noreg] [/startup] [/nostartup] [/relaunch]
//   Setup.exe /silent /update /dir="..." /relaunch    what the app runs to update itself: keeps your
//                                                      shortcuts and Start-with-Windows choice as they are
//   ... /portable /exe="Theater.exe"                   update a local (not installed) copy in place: just
//                                                      replaces that one file - no shortcuts, uninstaller or registry

using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using Theater;

[assembly: AssemblyTitle("Eric's Home Theater Setup")]
[assembly: AssemblyProduct("Eric's Home Theater")]
[assembly: AssemblyVersion("1.5.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]

namespace TheaterSetup
{
    class Options
    {
        public string Dir = Setup.DefaultDir;
        public bool Desktop = true, StartMenu = true, StartWithWindows, Registry = true, Launch = true;
        public bool TouchShortcuts = true, TouchRun = true, Relaunch, Portable;
        public string ExeFile = Setup.ExeName;   // file name of the app inside Dir
        public string ExePath { get { return Path.Combine(Dir, ExeFile); } }
    }

    static class Setup
    {
        public const string Name = "Eric's Home Theater";
        public const string Version = "1.5.0";
        public const string ExeName = "EricsHomeTheater.exe";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\EricsHomeTheater";
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static string DefaultDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Name); }
        }

        static string DesktopLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Name + ".lnk"); } }
        static string StartMenuLink { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Name + ".lnk"); } }

        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool uninstall = false, silent = false, update = false;
            var o = new Options { Dir = InstalledDir() ?? DefaultDir, StartWithWindows = RunEntry() != null };
            foreach (var a in args)
            {
                string s = a.Trim();
                if (s.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)) uninstall = true;
                else if (s.Equals("/silent", StringComparison.OrdinalIgnoreCase)) silent = true;
                else if (s.Equals("/noshortcuts", StringComparison.OrdinalIgnoreCase)) o.Desktop = o.StartMenu = false;
                else if (s.Equals("/noreg", StringComparison.OrdinalIgnoreCase)) o.Registry = false;
                else if (s.Equals("/startup", StringComparison.OrdinalIgnoreCase)) o.StartWithWindows = true;
                else if (s.Equals("/nostartup", StringComparison.OrdinalIgnoreCase)) o.StartWithWindows = false;
                else if (s.Equals("/nodesktop", StringComparison.OrdinalIgnoreCase)) o.Desktop = false;
                else if (s.Equals("/nostartmenu", StringComparison.OrdinalIgnoreCase)) o.StartMenu = false;
                else if (s.Equals("/update", StringComparison.OrdinalIgnoreCase)) update = true;
                else if (s.Equals("/relaunch", StringComparison.OrdinalIgnoreCase)) o.Relaunch = true;
                else if (s.Equals("/portable", StringComparison.OrdinalIgnoreCase)) o.Portable = true;
                else if (s.StartsWith("/exe=", StringComparison.OrdinalIgnoreCase))
                {
                    // Only a plain file name ending in .exe; never a path.
                    string name = s.Substring(5).Trim('"');
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && name == Path.GetFileName(name)) o.ExeFile = name;
                }
                else if (s.StartsWith("/dir=", StringComparison.OrdinalIgnoreCase)) o.Dir = s.Substring(5).Trim('"');
            }

            if (uninstall)
            {
                // Uninstall.exe lives in the install folder; use that folder.
                string dir = Path.GetDirectoryName(Application.ExecutablePath);
                if (!File.Exists(Path.Combine(dir, ExeName))) dir = o.Dir;
                if (silent) { Uninstall(dir, false, o.Registry); return 0; }
                if (MessageBox.Show("Remove " + Name + " from this PC?", Name + " Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 1;
                bool data = MessageBox.Show("Also delete your settings, watch history, genres you chose and thumbnails?\n\nChoose No to keep them in case you install again. Your movie files are never touched.",
                    Name + " Setup", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
                try
                {
                    Uninstall(dir, data, true);
                    MessageBox.Show(Name + " was removed.", Name + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Couldn't finish removing it: " + ex.Message, Name + " Setup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return 2;
                }
            }

            if (o.Portable)
            {
                // A local copy: replace just the app file and leave everything else on the PC alone.
                o.TouchShortcuts = false;
                o.TouchRun = false;
                o.Registry = false;
            }
            else if (update)
            {
                // Updating in place: only refresh what already belongs to this install.
                bool desk = LinkPointsInto(DesktopLink, o.Dir), menu = LinkPointsInto(StartMenuLink, o.Dir);
                o.Desktop = desk;
                o.StartMenu = menu;
                o.TouchShortcuts = desk || menu;
                string run = RunEntry();
                o.TouchRun = run != null && run.IndexOf(o.Dir, StringComparison.OrdinalIgnoreCase) >= 0;
                o.StartWithWindows = o.TouchRun;
                string registered = InstalledDir();
                o.Registry = registered != null && string.Equals(Path.GetFullPath(registered).TrimEnd('\\'), Path.GetFullPath(o.Dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }

            if (silent)
            {
                try
                {
                    Install(o, null);
                    if (o.Relaunch) Process.Start(new ProcessStartInfo(o.ExePath) { UseShellExecute = true, WorkingDirectory = o.Dir });
                    return 0;
                }
                catch (Exception ex)
                {
                    try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "EricsHomeTheater-setup.log"), DateTime.Now + "  " + ex + "\r\n"); } catch { }
                    if (o.Relaunch && File.Exists(o.ExePath)) Process.Start(o.ExePath);   // don't leave the user without the app
                    return 2;
                }
            }

            Application.Run(new SetupForm(o));
            return 0;
        }

        public static string InstalledDir()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(UninstallKey))
            {
                var dir = k == null ? null : k.GetValue("InstallLocation") as string;
                return dir != null && File.Exists(Path.Combine(dir, ExeName)) ? dir : null;
            }
        }

        public static string RunEntry()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k == null ? null : k.GetValue(Name) as string;
        }

        static bool LinkPointsInto(string link, string dir)
        {
            if (!File.Exists(link)) return false;
            try
            {
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                object shell = Activator.CreateInstance(shellType);
                try
                {
                    object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
                    string target = (string)sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null);
                    Marshal.ReleaseComObject(sc);
                    return target != null && target.StartsWith(Path.GetFullPath(dir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
                }
                finally { Marshal.ReleaseComObject(shell); }
            }
            catch { return false; }
        }

        public static string FindVlc()
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                try
                {
                    using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
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

        // Closes the installed app, and the app file being replaced if it has a different name.
        static void CloseRunning(string exePath)
        {
            var names = new List<string> { Path.GetFileNameWithoutExtension(ExeName) };
            string own = Path.GetFileNameWithoutExtension(exePath);
            if (!names.Contains(own, StringComparer.OrdinalIgnoreCase)) names.Add(own);
            foreach (var name in names)
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    // Other programs that happen to share a name are left alone.
                    if (!string.Equals(name, Path.GetFileNameWithoutExtension(ExeName), StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(p.MainModule.FileName, exePath, StringComparison.OrdinalIgnoreCase)) { p.Dispose(); continue; }
                    p.CloseMainWindow();
                    if (!p.WaitForExit(6000)) { p.Kill(); p.WaitForExit(3000); }
                }
                catch { }
                p.Dispose();
            }
        }

        public static void Install(Options o, Action<int, string> progress)
        {
            Action<int, string> report = (pct, text) => { if (progress != null) progress(pct, text); };

            report(5, "Closing " + Name + " if it's open\u2026");
            string exe = o.ExePath, tmp = exe + ".new";
            CloseRunning(exe);

            report(20, "Copying files\u2026");
            Directory.CreateDirectory(o.Dir);
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.exe"))
            using (var f = File.Create(tmp))
                s.CopyTo(f);
            if (File.Exists(exe)) File.Delete(exe);
            File.Move(tmp, exe);

            // The setup program doubles as the uninstaller (not for local copies).
            string uninstaller = Path.Combine(o.Dir, "Uninstall.exe");
            if (!o.Portable && !string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase))
                File.Copy(Application.ExecutablePath, uninstaller, true);

            report(55, "Creating shortcuts\u2026");
            if (o.TouchShortcuts)
            {
                if (o.Desktop) Shortcut(DesktopLink, exe); else TryDelete(DesktopLink);
                if (o.StartMenu) Shortcut(StartMenuLink, exe); else TryDelete(StartMenuLink);
            }

            report(75, "Registering with Windows\u2026");
            if (o.Registry)
            {
                using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    k.SetValue("DisplayName", Name);
                    k.SetValue("DisplayVersion", Version);
                    k.SetValue("Publisher", "Eric");
                    k.SetValue("DisplayIcon", exe + ",0");
                    k.SetValue("InstallLocation", o.Dir);
                    k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                    k.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall");
                    k.SetValue("QuietUninstallString", "\"" + uninstaller + "\" /uninstall /silent");
                    k.SetValue("EstimatedSize", (int)((new FileInfo(exe).Length + new FileInfo(uninstaller).Length) / 1024), RegistryValueKind.DWord);
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }
                if (o.TouchRun)
                    using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                    {
                        if (o.StartWithWindows) k.SetValue(Name, "\"" + exe + "\" --startup");
                        else k.DeleteValue(Name, false);
                    }
            }
            report(100, "Done");
        }

        public static void Uninstall(string dir, bool removeData, bool registry)
        {
            CloseRunning(Path.Combine(dir, ExeName));
            TryDelete(DesktopLink);
            TryDelete(StartMenuLink);
            if (registry)
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    var v = k == null ? null : k.GetValue(Name) as string;
                    if (v != null && v.IndexOf(dir, StringComparison.OrdinalIgnoreCase) >= 0) k.DeleteValue(Name, false);
                }
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
            }
            TryDelete(Path.Combine(dir, ExeName));
            if (removeData)
            {
                TryDeleteDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Theater"));
                TryDeleteDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Theater"));
            }
            // Uninstall.exe can't delete itself while running: remove it (and the then-empty folder) right after exit.
            // Only these two known items are removed; rmdir without /s won't delete a folder that has anything else in it.
            string un = Path.Combine(dir, "Uninstall.exe");
            if (File.Exists(un) && string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(un), StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" + un + "\" & rmdir \"" + dir + "\"")
                { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
            else
            {
                TryDelete(un);
                try { if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir); } catch { }
            }
        }

        static void Shortcut(string link, string target)
        {
            // WScript.Shell creates .lnk files; called through reflection so no extra references are needed.
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            try
            {
                object sc = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
                var t = sc.GetType();
                t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
                t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
                t.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { Name + " \u2014 your movies, TV-style" });
                t.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                Marshal.ReleaseComObject(sc);
            }
            finally { Marshal.ReleaseComObject(shell); }
        }

        static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
        static void TryDeleteDir(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
    }

    // The setup window, styled like the app.
    class SetupForm : Form
    {
        static readonly Color Bg = Color.FromArgb(11, 12, 16), Panel = Color.FromArgb(24, 26, 33), Panel2 = Color.FromArgb(38, 41, 51);
        static readonly Color Ink = Color.FromArgb(242, 242, 246), Dim = Color.FromArgb(150, 154, 168), Accent = Color.FromArgb(255, 176, 32);

        readonly Options o;
        readonly TextBox dirBox;
        readonly CheckBox desktop, startMenu, startup, launch;
        readonly Button installBtn, cancelBtn, browseBtn;
        readonly ProgressBar bar;
        readonly Label status, sub;
        bool done;
        ReleaseInfo online;   // a newer release than this installer carries

        public SetupForm(Options options)
        {
            o = options;
            bool update = Setup.InstalledDir() != null;
            Text = Setup.Name + " Setup";
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(640, 560);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ForeColor = Ink;
            Font = new Font("Segoe UI", 10f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var title = new Label { Text = Setup.Name, Font = new Font("Segoe UI", 20f, FontStyle.Bold), ForeColor = Ink, AutoSize = true, Location = new Point(104, 28), BackColor = Bg };
            sub = new Label { Text = (update ? "Update to version " : "Version ") + Setup.Version + "  \u00B7  your movies, TV-style", ForeColor = Dim, AutoSize = true, Location = new Point(107, 72), BackColor = Bg };
            Controls.Add(title);
            Controls.Add(sub);

            Controls.Add(new Label { Text = "Install to", ForeColor = Dim, AutoSize = true, Location = new Point(40, 128), BackColor = Bg });
            dirBox = new TextBox { Text = o.Dir, Location = new Point(40, 152), Width = 452, BackColor = Panel2, ForeColor = Ink, BorderStyle = BorderStyle.FixedSingle };
            browseBtn = MakeButton("Browse\u2026", new Rectangle(504, 150, 96, 30), Panel2, Ink);
            browseBtn.Click += (s, e) =>
            {
                using (var d = new FolderBrowserDialog { Description = "Choose where to install " + Setup.Name, SelectedPath = Directory.Exists(dirBox.Text) ? dirBox.Text : "" })
                    if (d.ShowDialog(this) == DialogResult.OK) dirBox.Text = Path.Combine(d.SelectedPath, Setup.Name);
            };
            Controls.Add(dirBox);

            desktop = MakeCheck("Create a desktop shortcut", 206, o.Desktop);
            startMenu = MakeCheck("Add to the Start menu", 238, o.StartMenu);
            startup = MakeCheck("Start " + Setup.Name + " when Windows starts", 270, o.StartWithWindows);
            launch = MakeCheck("Open it when setup finishes", 302, o.Launch);

            // VLC is the playback engine.
            var vlcPanel = new Panel { Location = new Point(40, 348), Size = new Size(560, 74), BackColor = Panel };
            string vlc = Setup.FindVlc();
            var vlcText = new Label { AutoSize = false, Location = new Point(16, 12), Size = new Size(530, 52), BackColor = Panel, ForeColor = Ink };
            if (vlc != null) vlcText.Text = "\u2713  VLC media player found \u2014 movies will play.\n" + vlc;
            else
            {
                vlcText.Text = "\u26A0  VLC media player isn't installed. It's free and needed to play movies.";
                vlcText.ForeColor = Accent;
                var link = new LinkLabel { Text = "Get VLC from videolan.org", Location = new Point(16, 42), AutoSize = true, BackColor = Panel, LinkColor = Accent, ActiveLinkColor = Ink };
                link.LinkClicked += (s, e) => Process.Start(new ProcessStartInfo("https://www.videolan.org/vlc/") { UseShellExecute = true });
                vlcPanel.Controls.Add(link);
            }
            vlcPanel.Controls.Add(vlcText);
            Controls.Add(vlcPanel);

            bar = new ProgressBar { Location = new Point(40, 446), Size = new Size(560, 8), Visible = false, Style = ProgressBarStyle.Continuous };
            status = new Label { Location = new Point(40, 462), Size = new Size(560, 24), ForeColor = Dim, BackColor = Bg, Text = update ? "An earlier version is installed. Your settings and library stay as they are." : "Installs for your Windows account only \u2014 no administrator password needed." };
            Controls.Add(bar);
            Controls.Add(status);

            cancelBtn = MakeButton("Cancel", new Rectangle(352, 500, 120, 40), Panel2, Ink);
            cancelBtn.Click += (s, e) => Close();
            installBtn = MakeButton(update ? "Update" : "Install", new Rectangle(480, 500, 120, 40), Accent, Bg);
            installBtn.Font = new Font("Segoe UI Semibold", 11f);
            installBtn.Click += (s, e) => OnInstall();
            AcceptButton = installBtn;
            CancelButton = cancelBtn;

            // If a newer version has been published, install that instead of the one inside this file.
            if (Updater.Configured)
                Task.Run(() => { try { return Updater.Latest(); } catch { return null; } }).ContinueWith(t =>
                {
                    if (IsDisposed || !IsHandleCreated || !Updater.IsNewer(t.Result)) return;
                    BeginInvoke((Action)(() =>
                    {
                        if (done || !installBtn.Enabled) return;
                        online = t.Result;
                        sub.Text = "Version " + online.Version + " (newest, from GitHub)  \u00B7  your movies, TV-style";
                        status.Text = "A newer version is online \u2014 Setup will download and install " + online.Version + ".";
                        status.ForeColor = Ink;
                        installBtn.Text = "Install " + online.Version;
                    }));
                });
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = DeviceDpi / 96f;
            var r = new RectangleF(40 * s, 30 * s, 52 * s, 52 * s);
            using (var path = Round(r, 13 * s))
            using (var b = new SolidBrush(Accent)) g.FillPath(b, path);
            using (var b = new SolidBrush(Bg))
                g.FillPolygon(b, new[] { new PointF(r.X + r.Width * 0.38f, r.Y + r.Height * 0.27f), new PointF(r.X + r.Width * 0.76f, r.Y + r.Height * 0.5f), new PointF(r.X + r.Width * 0.38f, r.Y + r.Height * 0.73f) });
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        CheckBox MakeCheck(string text, int y, bool on)
        {
            var c = new CheckBox { Text = text, Checked = on, Location = new Point(40, y), AutoSize = true, BackColor = Bg, ForeColor = Ink, FlatStyle = FlatStyle.Standard };
            Controls.Add(c);
            return c;
        }

        Button MakeButton(string text, Rectangle r, Color back, Color fore)
        {
            var b = new Button { Text = text, Bounds = r, BackColor = back, ForeColor = fore, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back, 0.2f);
            Controls.Add(b);
            return b;
        }

        void OnInstall()
        {
            if (done)
            {
                if (launch.Checked) Process.Start(new ProcessStartInfo(Path.Combine(o.Dir, Setup.ExeName)) { UseShellExecute = true, WorkingDirectory = o.Dir });
                Close();
                return;
            }
            string dir = dirBox.Text.Trim();
            if (dir.Length == 0 || !Path.IsPathRooted(dir)) { status.Text = "Choose a full folder path to install to."; status.ForeColor = Accent; return; }
            o.Dir = dir;
            o.Desktop = desktop.Checked;
            o.StartMenu = startMenu.Checked;
            o.StartWithWindows = startup.Checked;
            o.Launch = launch.Checked;
            foreach (Control c in Controls) if (c is CheckBox || c is TextBox || c == browseBtn) c.Enabled = false;
            installBtn.Enabled = cancelBtn.Enabled = false;
            bar.Visible = true;
            status.ForeColor = Dim;
            var latest = online;
            Action<int, string> report = (pct, text) => BeginInvoke((Action)(() => { bar.Value = Math.Max(0, Math.Min(100, pct)); status.Text = text; }));
            Task.Run(() =>
            {
                if (latest != null)
                {
                    try
                    {
                        // Download the newest installer (checked against GitHub's checksum) and let it do the install.
                        string file = Updater.Download(latest, Path.Combine(Path.GetTempPath(), "EricsHomeTheater-update"), pct => report(pct * 8 / 10, "Downloading version " + latest.Version + "\u2026 " + pct + "%"));
                        report(85, "Installing version " + latest.Version + "\u2026");
                        string args = "/silent /dir=\"" + o.Dir + "\"" + (o.Desktop ? "" : " /nodesktop") + (o.StartMenu ? "" : " /nostartmenu") + (o.StartWithWindows ? " /startup" : " /nostartup");
                        using (var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true }))
                        {
                            p.WaitForExit();
                            if (p.ExitCode == 0) { report(100, "Done"); return; }
                        }
                        report(20, "The newest version couldn't be installed; installing " + Setup.Version + " instead\u2026");
                    }
                    catch (Exception)
                    {
                        report(20, "Couldn't download the newest version; installing " + Setup.Version + " instead\u2026");
                    }
                }
                Setup.Install(o, report);
            })
                .ContinueWith(t => BeginInvoke((Action)(() =>
                {
                    if (t.IsFaulted)
                    {
                        var ex = t.Exception.GetBaseException();
                        status.Text = "Setup couldn't finish: " + ex.Message;
                        status.ForeColor = Accent;
                        installBtn.Enabled = cancelBtn.Enabled = true;
                        foreach (Control c in Controls) if (c is CheckBox || c is TextBox || c == browseBtn) c.Enabled = true;
                        return;
                    }
                    done = true;
                    status.Text = Setup.Name + " is installed. Find it on your desktop and in the Start menu.";
                    status.ForeColor = Ink;
                    installBtn.Text = "Finish";
                    installBtn.Enabled = true;
                    cancelBtn.Visible = false;
                    launch.Enabled = true;
                })));
        }
    }
}
