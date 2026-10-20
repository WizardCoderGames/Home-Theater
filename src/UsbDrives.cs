// Finds USB drives (flash drives, SD cards, external USB hard drives) so their movies can be
// added to the library while they're plugged in. Internal drives are never included.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;

namespace Theater
{
    class UsbDrive
    {
        public string Root, Label;   // Root like "E:\"; Label is the drive's name, or "" if it has none

        // How the drive is named in messages: its name in quotes, or just its letter.
        public string Display { get { return Label.Length > 0 ? "\u201C" + Label + "\u201D" : Root.Substring(0, 2); } }
        public string RowName { get { return Label.Length > 0 ? Label : Root.Substring(0, 2); } }
    }

    static class UsbDrives
    {
        // Whether a drive is on USB rarely changes, and asking Windows is slow, so remember the answer.
        static readonly Dictionary<string, bool> isUsbCache = new Dictionary<string, bool>();
        static readonly object gate = new object();

        // USB drives that are plugged in and readable, except the Windows drive and drives that
        // already hold one of the library's movie folders.
        public static List<UsbDrive> Find(IEnumerable<string> libraryFolders)
        {
            var list = new List<UsbDrive>();
            string system = Path.GetPathRoot(Environment.SystemDirectory);
            string testDrive = Environment.GetEnvironmentVariable("THEATER_TEST_USB");   // automated tests only
            DriveInfo[] drives;
            try { drives = DriveInfo.GetDrives(); } catch { return list; }
            foreach (var d in drives)
            {
                try
                {
                    if (d.DriveType != DriveType.Removable && d.DriveType != DriveType.Fixed) continue;
                    if (!d.IsReady) continue;
                    string root = d.Name;
                    if (string.Equals(root, system, StringComparison.OrdinalIgnoreCase)) continue;
                    if (libraryFolders.Any(f => f.StartsWith(root, StringComparison.OrdinalIgnoreCase))) continue;
                    string letter = root.Substring(0, 2);
                    bool usb;
                    if (!string.IsNullOrEmpty(testDrive) && string.Equals(testDrive.TrimEnd('\\'), letter, StringComparison.OrdinalIgnoreCase)) usb = true;
                    else
                    {
                        string key = root + "|" + d.VolumeLabel + "|" + d.TotalSize;
                        lock (gate)
                        {
                            if (!isUsbCache.TryGetValue(key, out usb))
                            {
                                usb = d.DriveType == DriveType.Removable || IsUsbDisk(letter);
                                isUsbCache[key] = usb;
                            }
                        }
                    }
                    if (usb) list.Add(new UsbDrive { Root = root, Label = d.VolumeLabel ?? "" });
                }
                catch { }
            }
            return list;
        }

        public static string Signature(List<UsbDrive> drives)
        {
            return string.Join("|", drives.Select(d => d.Root + d.Label));
        }

        // Follows drive letter -> partition -> physical disk and checks how the disk is connected.
        static bool IsUsbDisk(string letter)
        {
            try
            {
                using (var parts = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_LogicalDisk.DeviceID='" + letter + "'} WHERE AssocClass = Win32_LogicalDiskToPartition"))
                    foreach (ManagementObject part in parts.Get())
                        using (var disks = new ManagementObjectSearcher("ASSOCIATORS OF {Win32_DiskPartition.DeviceID='" + part["DeviceID"] + "'} WHERE AssocClass = Win32_DiskDriveToDiskPartition"))
                            foreach (ManagementObject disk in disks.Get())
                            {
                                string iface = Convert.ToString(disk["InterfaceType"]) ?? "";
                                string pnp = Convert.ToString(disk["PNPDeviceID"]) ?? "";
                                string media = Convert.ToString(disk["MediaType"]) ?? "";
                                if (iface.Equals("USB", StringComparison.OrdinalIgnoreCase)
                                    || pnp.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase)
                                    || pnp.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0
                                    || media.IndexOf("External", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                            }
            }
            catch { }
            return false;
        }
    }
}
