// Windows helpers: display resolution, start-with-Windows, Wi-Fi, and live hardware readings.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace Theater
{
    // Temporary full-screen resolution changes (like a game does). Windows keeps the user's
    // saved resolution; Theater switches back on exit, when windowed, or when minimized.
    static class DisplayModes
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DevMode
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string device, int mode, ref DevMode dm);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string device, ref DevMode dm, IntPtr hwnd, int flags, IntPtr param);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string device, IntPtr dm, IntPtr hwnd, int flags, IntPtr param);

        const int CurrentSettings = -1, RegistrySettings = -2, CdsFullscreen = 4;
        static readonly HashSet<string> changed = new HashSet<string>();

        static DevMode New() { var d = new DevMode(); d.dmSize = (short)Marshal.SizeOf(typeof(DevMode)); return d; }

        public static Size Current(string device, out int hz)
        {
            var d = New();
            hz = 0;
            if (!EnumDisplaySettings(device, CurrentSettings, ref d)) return Size.Empty;
            hz = d.dmDisplayFrequency;
            return new Size(d.dmPelsWidth, d.dmPelsHeight);
        }

        // The resolution Windows itself is set to (what "Native" means here).
        public static Size Saved(string device)
        {
            var d = New();
            return EnumDisplaySettings(device, RegistrySettings, ref d) ? new Size(d.dmPelsWidth, d.dmPelsHeight) : Size.Empty;
        }

        // Common TV-style resolutions this monitor supports, largest first.
        public static List<Size> Choices(string device)
        {
            var all = new HashSet<Size>();
            var d = New();
            for (int i = 0; EnumDisplaySettings(device, i, ref d); i++)
                if (d.dmBitsPerPel == 32) all.Add(new Size(d.dmPelsWidth, d.dmPelsHeight));
            var wanted = new[] { new Size(3840, 2160), new Size(2560, 1440), new Size(1920, 1080), new Size(1600, 900), new Size(1280, 720) };
            return wanted.Where(all.Contains).ToList();
        }

        public static bool Apply(string device, Size size)
        {
            int hz;
            Current(device, out hz);
            DevMode best = New();
            bool found = false;
            var d = New();
            for (int i = 0; EnumDisplaySettings(device, i, ref d); i++)
            {
                if (d.dmPelsWidth != size.Width || d.dmPelsHeight != size.Height || d.dmBitsPerPel != 32) continue;
                // Prefer the current refresh rate, otherwise the highest available.
                if (!found || d.dmDisplayFrequency == hz || (best.dmDisplayFrequency != hz && d.dmDisplayFrequency > best.dmDisplayFrequency)) { best = d; found = true; }
            }
            if (!found) return false;
            best.dmFields = 0x80000 | 0x100000 | 0x400000;   // width, height, frequency
            bool ok = ChangeDisplaySettingsEx(device, ref best, IntPtr.Zero, CdsFullscreen, IntPtr.Zero) == 0;
            if (ok) changed.Add(device);
            return ok;
        }

        public static void Restore(string device)
        {
            if (!changed.Remove(device)) return;
            ChangeDisplaySettingsEx(device, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        }

        public static void RestoreAll()
        {
            foreach (var d in changed.ToList()) Restore(d);
        }
    }

    static class StartWithWindows
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool Enabled
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key))
                    return k != null && k.GetValue(AppInfo.Name) != null;
            }
        }

        public static void Set(bool on)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (on) k.SetValue(AppInfo.Name, "\"" + System.Windows.Forms.Application.ExecutablePath + "\" --startup");
                else k.DeleteValue(AppInfo.Name, false);
            }
        }
    }

    // Wi-Fi through Windows' own netsh tool: status, saved networks, connect to a saved network.
    static class Wifi
    {
        public class Status { public bool Connected; public string Ssid = "", Signal = "", Adapter = ""; }

        static string Netsh(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (var p = Process.Start(psi))
                {
                    string s = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(8000);
                    return s;
                }
            }
            catch { return ""; }
        }

        public static Status Get()
        {
            var st = new Status();
            string s = Netsh("wlan show interfaces");
            var state = Regex.Match(s, @"^\s*State\s*:\s*(.+)$", RegexOptions.Multiline);
            var ssid = Regex.Match(s, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline);
            var sig = Regex.Match(s, @"^\s*Signal\s*:\s*(.+)$", RegexOptions.Multiline);
            var name = Regex.Match(s, @"^\s*Name\s*:\s*(.+)$", RegexOptions.Multiline);
            st.Connected = state.Success && state.Groups[1].Value.Trim().Equals("connected", StringComparison.OrdinalIgnoreCase);
            if (ssid.Success) st.Ssid = ssid.Groups[1].Value.Trim();
            if (sig.Success) st.Signal = sig.Groups[1].Value.Trim();
            if (name.Success) st.Adapter = name.Groups[1].Value.Trim();
            return st;
        }

        public static List<string> SavedNetworks()
        {
            return Regex.Matches(Netsh("wlan show profiles"), @"All User Profile\s*:\s*(.+)$", RegexOptions.Multiline)
                .Cast<Match>().Select(m => m.Groups[1].Value.Trim()).Where(n => n.Length > 0).Distinct().ToList();
        }

        public static bool Connect(string profile)
        {
            if (profile.IndexOf('"') >= 0) return false;
            string s = Netsh("wlan connect name=\"" + profile + "\"");
            return s.IndexOf("completed successfully", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public class Network
        {
            public string Ssid = "", Auth = "";
            public int Signal = -1;          // percent, when Windows reports it
            public bool Saved, Current;
            public bool Open { get { return Auth.Equals("Open", StringComparison.OrdinalIgnoreCase); } }
            public bool Enterprise { get { return Auth.IndexOf("Enterprise", StringComparison.OrdinalIgnoreCase) >= 0 || Auth.Contains("802.1X"); } }
        }

        // Networks in range, strongest first (the connected one at the top).
        public static List<Network> Nearby(bool rescan)
        {
            if (rescan && Scan()) Thread.Sleep(3500);   // give the adapter time to listen on every channel
            var saved = new HashSet<string>(SavedNetworks());
            var cur = Get();
            var list = new List<Network>();
            Network n = null;
            foreach (var raw in Netsh("wlan show networks mode=bssid").Split('\n'))
            {
                string line = raw.TrimEnd('\r', ' ');
                var m = Regex.Match(line, @"^SSID \d+\s*:\s*(.*)$");
                if (m.Success)
                {
                    n = new Network { Ssid = m.Groups[1].Value.Trim() };
                    if (n.Ssid.Length > 0 && !list.Any(x => x.Ssid == n.Ssid)) list.Add(n);
                    continue;
                }
                if (n == null) continue;
                m = Regex.Match(line, @"^\s+Authentication\s*:\s*(.+)$");
                if (m.Success && n.Auth == "") n.Auth = m.Groups[1].Value.Trim();
                m = Regex.Match(line, @"^\s+Signal\s*:\s*(\d+)%");
                if (m.Success) n.Signal = Math.Max(n.Signal, int.Parse(m.Groups[1].Value));
            }
            foreach (var x in list)
            {
                x.Saved = saved.Contains(x.Ssid);
                x.Current = cur.Connected && cur.Ssid == x.Ssid;
                int sig;
                if (x.Current && x.Signal < 0 && int.TryParse(cur.Signal.TrimEnd('%', ' '), out sig)) x.Signal = sig;
            }
            if (cur.Connected && cur.Ssid != "" && !list.Any(x => x.Current))
            {
                int sig;
                list.Add(new Network { Ssid = cur.Ssid, Saved = true, Current = true, Signal = int.TryParse(cur.Signal.TrimEnd('%', ' '), out sig) ? sig : -1 });
            }
            return list.OrderByDescending(x => x.Current).ThenByDescending(x => x.Signal).ThenByDescending(x => x.Saved).ThenBy(x => x.Ssid).ToList();
        }

        // Joins a network that isn't saved yet: saves a Windows Wi-Fi profile with the password, then connects.
        // Returns "" on success or a short reason. A wrong password removes the profile again.
        public static string ConnectNew(Network net, string password)
        {
            if (net.Enterprise) return "This network needs a work or school sign-in. Use Windows Settings for it.";
            string auth, enc;
            string a = net.Auth.ToUpperInvariant();
            if (net.Open) { auth = "open"; enc = "none"; }
            else if (a.Contains("WPA3")) { auth = "WPA3SAE"; enc = "AES"; }
            else if (a.Contains("WPA2")) { auth = "WPA2PSK"; enc = "AES"; }
            else if (a.Contains("WPA")) { auth = "WPAPSK"; enc = "TKIP"; }
            else return "This network uses old security (" + net.Auth + ") that " + AppInfo.Name + " can't set up.";
            if (!net.Open && (password.Length < 8 || password.Length > 63)) return "Wi-Fi passwords are 8 to 63 characters";

            string hex = BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(net.Ssid)).Replace("-", "");
            string name = System.Security.SecurityElement.Escape(net.Ssid);
            string xml = "<?xml version=\"1.0\"?>\r\n<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">"
                + "<name>" + name + "</name><SSIDConfig><SSID><hex>" + hex + "</hex><name>" + name + "</name></SSID></SSIDConfig>"
                + "<connectionType>ESS</connectionType><connectionMode>auto</connectionMode><MSM><security>"
                + "<authEncryption><authentication>" + auth + "</authentication><encryption>" + enc + "</encryption><useOneX>false</useOneX></authEncryption>"
                + (net.Open ? "" : "<sharedKey><keyType>passPhrase</keyType><protected>false</protected><keyMaterial>" + System.Security.SecurityElement.Escape(password) + "</keyMaterial></sharedKey>")
                + "</security></MSM></WLANProfile>";
            // Windows encrypts the password as it imports the profile; the file only exists for that moment.
            string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "theater-wifi-" + Guid.NewGuid().ToString("N") + ".xml");
            string added;
            try
            {
                System.IO.File.WriteAllText(file, xml);
                added = Netsh("wlan add profile filename=\"" + file + "\" user=current");
            }
            finally { try { System.IO.File.Delete(file); } catch { } }
            if (added.IndexOf("is added", StringComparison.OrdinalIgnoreCase) < 0) return "Windows wouldn't save this network";
            if (WaitFor(net.Ssid, Connect(net.Ssid))) return "";
            if (net.Ssid.IndexOf('"') < 0) Netsh("wlan delete profile name=\"" + net.Ssid + "\"");
            return net.Open ? "Couldn't connect to " + net.Ssid : "Couldn't connect. Check the password and try again.";
        }

        // Connects to a saved network and waits to see whether it worked.
        public static bool ConnectSaved(string ssid) { return WaitFor(ssid, Connect(ssid)); }

        static bool WaitFor(string ssid, bool started)
        {
            if (!started) return false;
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(1000);
                var st = Get();
                if (st.Connected && st.Ssid == ssid) return true;
            }
            return false;
        }

        public class Wired { public bool Present, Connected; public string Adapter = "", Speed = "", Address = ""; }

        // The PC's Ethernet port (Bluetooth and virtual adapters don't count).
        public static Wired Ethernet()
        {
            var w = new Wired();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.NetworkInterfaceType != NetworkInterfaceType.Ethernet && ni.NetworkInterfaceType != NetworkInterfaceType.GigabitEthernet) continue;
                    string d = ni.Description;
                    if (Regex.IsMatch(d, "Bluetooth|Virtual|Hyper-V|VPN|TAP-|Loopback|WAN Miniport|VMware|VirtualBox|Tunnel", RegexOptions.IgnoreCase)) continue;
                    bool up = ni.OperationalStatus == OperationalStatus.Up;
                    if (w.Present && !up) continue;   // prefer a port that has a cable in it
                    w.Present = true;
                    w.Connected = up;
                    w.Adapter = d;
                    w.Speed = up && ni.Speed > 0 ? (ni.Speed >= 1000000000 ? (ni.Speed / 1e9).ToString("0.#") + " Gbps" : (ni.Speed / 1000000) + " Mbps") : "";
                    w.Address = "";
                    if (up)
                        foreach (var u in ni.GetIPProperties().UnicastAddresses)
                            if (u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) { w.Address = u.Address.ToString(); break; }
                    if (up) break;
                }
            }
            catch { }
            return w;
        }

        // Asks every Wi-Fi adapter to look for networks now (otherwise Windows only scans every minute or so).
        [DllImport("wlanapi.dll")] static extern int WlanOpenHandle(uint version, IntPtr reserved, out uint negotiated, out IntPtr handle);
        [DllImport("wlanapi.dll")] static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);
        [DllImport("wlanapi.dll")] static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
        [DllImport("wlanapi.dll")] static extern int WlanScan(IntPtr handle, ref Guid iface, IntPtr ssid, IntPtr ie, IntPtr reserved);
        [DllImport("wlanapi.dll")] static extern void WlanFreeMemory(IntPtr p);

        static bool Scan()
        {
            IntPtr h = IntPtr.Zero, list = IntPtr.Zero;
            bool any = false;
            try
            {
                uint v;
                if (WlanOpenHandle(2, IntPtr.Zero, out v, out h) != 0) return false;
                if (WlanEnumInterfaces(h, IntPtr.Zero, out list) != 0) return false;
                int count = Marshal.ReadInt32(list);
                for (int i = 0; i < count; i++)
                {
                    // WLAN_INTERFACE_INFO: a GUID, a 256-character description, then the state (532 bytes).
                    var guid = (Guid)Marshal.PtrToStructure(new IntPtr(list.ToInt64() + 8 + i * 532), typeof(Guid));
                    if (WlanScan(h, ref guid, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) == 0) any = true;
                }
            }
            catch { }
            finally
            {
                if (list != IntPtr.Zero) WlanFreeMemory(list);
                if (h != IntPtr.Zero) WlanCloseHandle(h, IntPtr.Zero);
            }
            return any;
        }

        public static bool HasNetwork()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == OperationalStatus.Up
                    && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.GetIPProperties().GatewayAddresses.Count > 0);
            }
            catch { return false; }
        }
    }

    // Live hardware readings for the System info screen, gathered on a background thread.
    class SystemStats
    {
        public string CpuName = "", GpuText = "";
        public int Cores, Threads;
        public volatile float CpuPercent = -1, Gpu3dPercent = -1, GpuVideoPercent = -1, AppCpuPercent = -1;
        public long RamTotal, RamUsed, AppMemory, VlcMemory;
        public List<Tuple<string, long>> Gpus = new List<Tuple<string, long>>();
        public volatile List<Drives.Drive> DriveList = new List<Drives.Drive>();

        Thread thread;
        volatile bool running;

        public void Start()
        {
            if (running) return;
            running = true;
            thread = new Thread(Loop) { IsBackground = true, Name = "stats", Priority = ThreadPriority.BelowNormal };
            thread.Start();
        }

        public void Stop() { running = false; }

        void Loop()
        {
            try
            {
                using (var s = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
                    foreach (ManagementObject o in s.Get())
                    {
                        CpuName = Regex.Replace(Convert.ToString(o["Name"]).Trim(), @"\s+", " ");
                        Cores += Convert.ToInt32(o["NumberOfCores"]);
                        Threads += Convert.ToInt32(o["NumberOfLogicalProcessors"]);
                    }
            }
            catch { }
            Gpus = ReadGpus();

            PerformanceCounter cpu = null;
            try { cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); cpu.NextValue(); } catch { cpu = null; }
            var gpuCounters = new List<PerformanceCounter>();
            DateTime gpuListAt = DateTime.MinValue, drivesAt = DateTime.MinValue;
            var me = Process.GetCurrentProcess();
            TimeSpan lastCpu = me.TotalProcessorTime;
            DateTime lastAt = DateTime.Now;

            while (running)
            {
                try
                {
                    if (cpu != null) CpuPercent = cpu.NextValue();
                    var mem = new MemStatus();
                    mem.dwLength = (uint)Marshal.SizeOf(typeof(MemStatus));
                    if (GlobalMemoryStatusEx(ref mem)) { RamTotal = (long)mem.ullTotalPhys; RamUsed = (long)(mem.ullTotalPhys - mem.ullAvailPhys); }

                    me.Refresh();
                    AppMemory = me.WorkingSet64;
                    var now = DateTime.Now;
                    AppCpuPercent = (float)((me.TotalProcessorTime - lastCpu).TotalMilliseconds / (now - lastAt).TotalMilliseconds / Math.Max(1, Environment.ProcessorCount) * 100);
                    lastCpu = me.TotalProcessorTime;
                    lastAt = now;
                    long vlc = 0;
                    foreach (var p in Process.GetProcessesByName("vlc")) { try { vlc += p.WorkingSet64; } catch { } p.Dispose(); }
                    VlcMemory = vlc;
                    if ((DateTime.Now - drivesAt).TotalSeconds >= 5) { DriveList = Drives.Read(); drivesAt = DateTime.Now; }

                    // GPU load from Windows' "GPU Engine" counters (Windows 10 and later).
                    if ((DateTime.Now - gpuListAt).TotalSeconds > 10)
                    {
                        foreach (var c in gpuCounters) c.Dispose();
                        gpuCounters.Clear();
                        try
                        {
                            var cat = new PerformanceCounterCategory("GPU Engine");
                            foreach (var inst in cat.GetInstanceNames())
                                if (inst.Contains("engtype_3D") || inst.Contains("engtype_VideoDecode"))
                                {
                                    var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, true);
                                    c.NextValue();
                                    gpuCounters.Add(c);
                                }
                        }
                        catch { }
                        gpuListAt = DateTime.Now;
                    }
                    float g3d = 0, gvid = 0;
                    bool any = false;
                    foreach (var c in gpuCounters)
                    {
                        try
                        {
                            float v = c.NextValue();
                            any = true;
                            if (c.InstanceName.Contains("engtype_3D")) g3d += v; else gvid += v;
                        }
                        catch { }
                    }
                    Gpu3dPercent = any ? Math.Min(100, g3d) : -1;
                    GpuVideoPercent = any ? Math.Min(100, gvid) : -1;
                }
                catch { }
                for (int i = 0; i < 10 && running; i++) Thread.Sleep(100);
            }
            if (cpu != null) cpu.Dispose();
            foreach (var c in gpuCounters) c.Dispose();
        }

        // Graphics cards with their real video memory (WMI caps it at 4 GB, the driver registry doesn't).
        static List<Tuple<string, long>> ReadGpus()
        {
            var list = new List<Tuple<string, long>>();
            try
            {
                using (var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                    if (cls != null)
                        foreach (var sub in cls.GetSubKeyNames())
                        {
                            if (!Regex.IsMatch(sub, @"^\d{4}$")) continue;
                            using (var k = cls.OpenSubKey(sub))
                            {
                                if (k == null) continue;
                                string name = Convert.ToString(k.GetValue("DriverDesc") ?? "");
                                object mem = k.GetValue("HardwareInformation.qwMemorySize") ?? k.GetValue("HardwareInformation.MemorySize");
                                long bytes = 0;
                                if (mem is long) bytes = (long)mem;
                                else if (mem is int) bytes = (uint)(int)mem;
                                else if (mem is byte[] && ((byte[])mem).Length >= 4) bytes = ((byte[])mem).Length >= 8 ? BitConverter.ToInt64((byte[])mem, 0) : BitConverter.ToUInt32((byte[])mem, 0);
                                if (name.Length > 0 && !name.Contains("Virtual") && !name.Contains("Basic Display")) list.Add(Tuple.Create(name, bytes));
                            }
                        }
            }
            catch { }
            return list;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MemStatus
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemStatus m);
    }

    // Internal drives and USB drives with their size and free space.
    static class Drives
    {
        public class Drive { public string Letter = "", Label = "", Format = ""; public bool Usb; public long Total, Free; public long Used { get { return Total - Free; } } }

        public static List<Drive> Read()
        {
            var list = new List<Drive>();
            try
            {
                // Disc drives are skipped: asking them can spin up a disc.
                foreach (var d in System.IO.DriveInfo.GetDrives())
                {
                    if (d.DriveType != System.IO.DriveType.Fixed && d.DriveType != System.IO.DriveType.Removable) continue;
                    try
                    {
                        if (!d.IsReady || d.TotalSize <= 0) continue;
                        list.Add(new Drive { Letter = d.Name.TrimEnd('\\'), Label = d.VolumeLabel, Format = d.DriveFormat, Usb = d.DriveType == System.IO.DriveType.Removable, Total = d.TotalSize, Free = d.TotalFreeSpace });
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }
    }
}