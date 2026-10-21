// Menu sound effects. The sounds are synthesized when the app starts (no sound files) and played
// with Windows' PlaySound, which mixes with other audio and never blocks the menus.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Theater
{
    enum Sfx { Move, Select, Back, Bump }

    static class Sounds
    {
        [DllImport("winmm.dll")] static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);
        const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_MEMORY = 0x4;
        const int Rate = 44100;

        // Indexed by [volume level][sound]; level 0 is Normal, 1 is Quiet. Kept in unmanaged memory because
        // PlaySound reads it after returning.
        static readonly IntPtr[,] clips = new IntPtr[2, 4];
        static string level = "normal";
        static bool ready, failed;

        public static string Level { get { return level; } set { level = value == "quiet" || value == "off" ? value : "normal"; } }

        public static string Label(string v) { return v == "off" ? "Off" : v == "quiet" ? "Quiet" : "On (normal volume)"; }

        public static void Play(Sfx s)
        {
            if (level == "off" || failed) return;
            try
            {
                if (!ready) Build();
                PlaySound(clips[level == "quiet" ? 1 : 0, (int)s], IntPtr.Zero, SND_ASYNC | SND_MEMORY | SND_NODEFAULT);
            }
            catch { failed = true; }   // no sound device or winmm: stay silent
        }

        static void Build()
        {
            for (int v = 0; v < 2; v++)
            {
                float g = v == 0 ? 1f : 0.38f;
                Store(v, Sfx.Move, Mix(30, Tone(0, 30, 2300, 1900, 5, 0.16f * g)));
                Store(v, Sfx.Select, Mix(110, Tone(0, 70, 660, 680, 22, 0.20f * g), Tone(34, 110, 990, 1000, 26, 0.18f * g)));
                Store(v, Sfx.Back, Mix(110, Tone(0, 70, 990, 960, 22, 0.17f * g), Tone(34, 110, 660, 640, 26, 0.17f * g)));
                Store(v, Sfx.Bump, Mix(80, Tone(0, 80, 170, 120, 18, 0.30f * g), Tone(0, 40, 340, 260, 8, 0.10f * g)));
            }
            ready = true;
        }

        // A sine that glides from f0 to f1 (Hz), starting at startMs, with a 2 ms fade-in and a decay of tauMs.
        static Func<int, double> Tone(double startMs, double endMs, double f0, double f1, double tauMs, float gain)
        {
            int a = (int)(startMs * Rate / 1000), b = (int)(endMs * Rate / 1000), len = Math.Max(1, b - a);
            double phase = 0;
            int last = -1;
            return i =>
            {
                if (i < a || i >= b) return 0;
                int k = i - a;
                if (k != last + 1) phase = 0;   // samples are asked for in order
                last = k;
                double t = (double)k / len, f = f0 + (f1 - f0) * t;
                phase += 2 * Math.PI * f / Rate;
                double ms = k * 1000.0 / Rate;
                double env = Math.Min(1, ms / 2) * Math.Exp(-ms / tauMs) * Math.Min(1, (len - k) * 1000.0 / Rate / 3);   // fade out the last 3 ms
                return Math.Sin(phase) * env * gain;
            };
        }

        static short[] Mix(double ms, params Func<int, double>[] tones)
        {
            var s = new short[(int)(ms * Rate / 1000)];
            for (int i = 0; i < s.Length; i++)
            {
                double v = 0;
                foreach (var t in tones) v += t(i);
                s[i] = (short)Math.Max(-32767, Math.Min(32767, v * 32767));
            }
            return s;
        }

        static void Store(int v, Sfx s, short[] samples)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            int bytes = samples.Length * 2;
            w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes); w.Write(new[] { 'W', 'A', 'V', 'E' });
            w.Write(new[] { 'f', 'm', 't', ' ' }); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
            foreach (var x in samples) w.Write(x);
            var data = ms.ToArray();
            var p = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, p, data.Length);
            clips[v, (int)s] = p;
        }
    }
}
