// Rendering helpers that keep the UI smooth on slow PCs: card art is rendered once at the exact
// on-screen pixel size and then copied to the screen without scaling.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Theater
{
    static class Gfx
    {
        // Draws a pre-sized bitmap 1:1 at a virtual-canvas position (the fastest path GDI+ has).
        public static void Blit(Graphics g, Bitmap b, float vx, float vy)
        {
            using (var m = g.Transform)
            {
                var pts = new[] { new PointF(vx, vy) };
                m.TransformPoints(pts);
                g.ResetTransform();
                g.DrawImageUnscaled(b, (int)Math.Round(pts[0].X), (int)Math.Round(pts[0].Y));
                g.Transform = m;
            }
        }
    }

    // Plain GDI scaling: much faster than GDI+ for enlarging a picture to full-screen size.
    static class Gdi
    {
        public static void StretchCover(Graphics g, Bitmap src, Rectangle dest)
        {
            float sr = (float)src.Width / src.Height, dr = (float)dest.Width / dest.Height;
            int sx = 0, sy = 0, sw = src.Width, sh = src.Height;
            if (sr > dr) { sw = (int)(src.Height * dr); sx = (src.Width - sw) / 2; }
            else { sh = (int)(src.Width / dr); sy = (src.Height - sh) / 2; }
            IntPtr hbm = src.GetHbitmap();
            IntPtr hdc = g.GetHdc();
            try
            {
                IntPtr mem = CreateCompatibleDC(hdc);
                IntPtr old = SelectObject(mem, hbm);
                SetStretchBltMode(hdc, 4);   // HALFTONE: smooth
                SetBrushOrgEx(hdc, 0, 0, IntPtr.Zero);
                StretchBlt(hdc, dest.X, dest.Y, dest.Width, dest.Height, mem, sx, sy, sw, sh, 0x00CC0020);
                SelectObject(mem, old);
                DeleteDC(mem);
            }
            finally
            {
                g.ReleaseHdc(hdc);
                DeleteObject(hbm);
            }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern int SetStretchBltMode(IntPtr hdc, int mode);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr old);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool StretchBlt(IntPtr dst, int dx, int dy, int dw, int dh, IntPtr src, int sx, int sy, int sw, int sh, int rop);
    }
    // Rounded card art (thumbnail, badges, progress) cached per item and pixel size, within a memory budget.
    class CardArt
    {
        class Entry { public Bitmap Bmp; public string Stamp; public long Used, Bytes; }

        readonly TheaterForm f;
        readonly Dictionary<string, Entry> map = new Dictionary<string, Entry>();
        long clock, bytes;
        public long Budget = 64L << 20;
        public int Builds;

        public CardArt(TheaterForm f) { this.f = f; }

        public Bitmap Get(MediaItem it, float vw, float vh)
        {
            float s = f.ViewScale;
            int w = Math.Max(1, (int)Math.Round(vw * s)), h = Math.Max(1, (int)Math.Round(vh * s));
            string key = it.Id + "@" + w + "x" + h;
            // Check the cache before touching the thumbnail (which may need a disk read).
            string stamp = Stamp(it, it.Thumb != null);
            Entry e;
            if (map.TryGetValue(key, out e) && e.Stamp == stamp) { e.Used = ++clock; return e.Bmp; }
            var thumb = f.Thumbs.Acquire(it);
            stamp = Stamp(it, thumb != null);

            var bmp = Build(it, thumb, w, h, vw, vh, s);
            Builds++;
            if (e != null) { bytes -= e.Bytes; e.Bmp.Dispose(); }
            long size = (long)w * h * 4;
            map[key] = new Entry { Bmp = bmp, Stamp = stamp, Used = ++clock, Bytes = size };
            bytes += size;
            Evict();
            return bmp;
        }

        string Stamp(MediaItem it, bool hasThumb)
        {
            var w = f.Hist.Get(it.Path);
            string watch = w == null ? "" : w.Watched ? "W" : ((int)(w.Fraction * 100)).ToString();
            return (hasThumb || it.ThumbReady ? "t" : "n") + it.Quality + (it.Hdr ? "H" : "") + watch;
        }

        void Evict()
        {
            if (bytes <= Budget) return;
            var list = new List<KeyValuePair<string, Entry>>(map);
            list.Sort((a, b) => a.Value.Used.CompareTo(b.Value.Used));
            foreach (var kv in list)
            {
                if (bytes <= Budget * 3 / 4) break;
                map.Remove(kv.Key);
                bytes -= kv.Value.Bytes;
                kv.Value.Bmp.Dispose();
            }
        }

        public void Clear()
        {
            foreach (var e in map.Values) e.Bmp.Dispose();
            map.Clear();
            bytes = 0;
        }

        Bitmap Build(MediaItem it, Bitmap thumb, int w, int h, float vw, float vh, float s)
        {
            using (var content = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(content))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                    g.ScaleTransform(w / vw, h / vh);
                    Ui.DrawArt(g, f, it, thumb, new RectangleF(0, 0, vw, vh));
                }
                // Cut the rounded corners with an anti-aliased fill.
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                using (var g = Graphics.FromImage(bmp))
                using (var tb = new TextureBrush(content))
                using (var path = Ui.Round(new RectangleF(0, 0, w, h), 10 * s))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillPath(tb, path);
                }
                return bmp;
            }
        }
    }
}
