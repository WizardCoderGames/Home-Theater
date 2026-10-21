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
            DateTime gpuListAt = DateTime.MinValue;
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

    // What a phone's Wake-on-LAN app needs to wake this PC: its network card's MAC address and the broadcast address.
    static class WakeOnLan
    {
        public static bool Find(out string mac, out string broadcast, out string adapter)
        {
            mac = broadcast = adapter = "";
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    if (props.GatewayAddresses.Count == 0) continue;
                    foreach (var u in props.UnicastAddresses)
                    {
                        if (u.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork || u.IPv4Mask == null) continue;
                        byte[] ip = u.Address.GetAddressBytes(), mask = u.IPv4Mask.GetAddressBytes(), b = new byte[4];
                        for (int i = 0; i < 4; i++) b[i] = (byte)(ip[i] | ~mask[i]);
                        broadcast = new System.Net.IPAddress(b).ToString();
                        mac = string.Join(":", ni.GetPhysicalAddress().GetAddressBytes().Select(x => x.ToString("X2")));
                        adapter = ni.Description;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }
    }
}