using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Windows.Forms;

namespace SshBeacon
{
    // A light field, not a shaded object: every layer decays continuously to transparency.
    // No rings, borders, specular marks, hard disk or perpetual travelling decoration.
    internal static class BeaconVisuals
    {
        private static double Clamp(double x) { return Math.Max(0, Math.Min(1, x)); }
        private static double Smooth(double x) { x = Clamp(x); return x * x * (3 - 2 * x); }

        internal static void Draw(Bitmap canvas, BeaconState state, double connectedSeconds,
            double disconnectedSeconds, double lingerSeconds)
        {
            double age = Math.Max(0, connectedSeconds);
            double fade = state == BeaconState.Linger
                ? 1 - Smooth(disconnectedSeconds / Math.Max(0.001, lingerSeconds)) : 1;
            double breathAge = state == BeaconState.Linger
                ? Math.Max(0, age - Math.Max(0, disconnectedSeconds)) : age;
            // Independent, slow optical drift: core and diffusion do not pulse in lockstep.
            double breath = 0.5 - 0.5 * Math.Cos(breathAge * Math.PI * 2 / 5.8);
            bool error = state == BeaconState.Error;
            double opacity = state == BeaconState.Idle ? 0 : fade * (error ? 1 : Smooth(age / 0.8));
            if (error) breath = 0.5;
            double energy = 0.93 + 0.07 * breath;
            double t = error ? 0 : breathAge;
            double coreX = 0.24 * Math.Sin(t * 0.63), coreY = 0.18 * Math.Sin(t * 0.47 + 1.2);
            double haloX = 0.7 + 0.9 * Math.Sin(t * 0.37), haloY = -0.6 + 0.7 * Math.Sin(t * 0.29 + 0.7);
            double angle = 0.4 + 0.28 * Math.Sin(t * 0.22);
            double co = Math.Cos(angle), si = Math.Sin(angle);
            double spreadX = 1.12 + 0.06 * Math.Sin(t * 0.51 + 0.4);
            double spreadY = 0.89 + 0.04 * Math.Sin(t * 0.43 + 1.6);
            double haloEnergy = 0.93 + 0.07 * Math.Sin(t * 0.71 + 0.9);
            double outerEnergy = 0.94 + 0.06 * Math.Sin(t * 0.33 + 2.1);
            int w = canvas.Width, h = canvas.Height;
            byte[] pixels = new byte[w * h * 4];
            double cx = (w - 1) / 2.0, cy = (h - 1) / 2.0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double dx = (x - cx) / 1.06, dy = (y - cy) / 1.06, r2 = dx * dx + dy * dy;
                // A resolved warm emitter, with a gently offset, unequal light field.
                double nx = dx - coreX, ny = dy - coreY;
                double coreRadius = nx * nx + ny * ny;
                double hx = dx - haloX, hy = dy - haloY;
                double u = (hx * co + hy * si) / spreadX;
                double v = (-hx * si + hy * co) / spreadY;
                double ox = (dx + 0.7 * Math.Sin(t * 0.26 + 1)) / 0.97;
                double oy = (dy - 0.6 * Math.Cos(t * 0.31)) / 1.03;
                double nucleus = Math.Exp(-coreRadius / 11.0);
                double diffusion = Math.Exp(-(u * u + v * v) / 69.0);
                double atmosphere = Math.Exp(-(ox * ox + oy * oy) / 230.0);
                double a = 1.05 * nucleus * energy + 0.22 * diffusion * haloEnergy + 0.052 * atmosphere * outerEnergy;
                a = Clamp(a) * opacity;
                // A little more golden, without turning the core into an orange disk.
                double white = Math.Exp(-coreRadius / 24.0);
                double red = 255;
                double green = error ? 75 + 74 * white : 177 + 70 * white;
                double blue = error ? 66 + 65 * white : 77 + 147 * white;
                int i = (y * w + x) * 4;
                byte alpha = (byte)LayeredWindow.AlphaOf(a);
                // Format32bppPArgb expects RGB multiplied by the stored alpha.
                pixels[i] = (byte)Math.Round(blue * alpha / 255.0);
                pixels[i + 1] = (byte)Math.Round(green * alpha / 255.0);
                pixels[i + 2] = (byte)Math.Round(red * alpha / 255.0);
                pixels[i + 3] = alpha;
            }
            BitmapData data = canvas.LockBits(new Rectangle(0, 0, w, h),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try
            {
                for (int y = 0; y < h; y++)
                    System.Runtime.InteropServices.Marshal.Copy(pixels, y * w * 4,
                        IntPtr.Add(data.Scan0, y * data.Stride), w * 4);
            }
            finally { canvas.UnlockBits(data); }
        }
    }

    internal sealed class VisualPreview : Form
    {
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Bitmap _frame = new Bitmap(DotWindow.SizePx, DotWindow.SizePx, PixelFormat.Format32bppPArgb);
        private readonly Font _title = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
        private readonly Font _body = new Font("Microsoft YaHei UI", 9f);
        private bool _light;

        internal VisualPreview()
        {
            Text = "SSH Beacon · 动效预览";
            ClientSize = new Size(640, 360);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space) { _clock.Restart(); Invalidate(); }
                if (e.KeyCode == Keys.L) { _light = !_light; Invalidate(); }
                if (e.KeyCode == Keys.Escape) Close();
            };
            _timer.Interval = 40;
            _timer.Tick += delegate { Invalidate(); };
            _timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.Clear(_light ? Color.FromArgb(244, 244, 240) : Color.FromArgb(20, 24, 30));
            double t = _clock.Elapsed.TotalSeconds % 16;
            BeaconState state = t < 7 ? BeaconState.Connected : t < 13 ? BeaconState.Linger : BeaconState.Idle;
            BeaconVisuals.Draw(_frame, state, t, Math.Max(0, t - 7), 6);
            using (SolidBrush text = new SolidBrush(_light ? Color.FromArgb(40, 44, 50) : Color.FromArgb(226, 231, 239)))
            using (SolidBrush muted = new SolidBrush(_light ? Color.FromArgb(100, 106, 114) : Color.FromArgb(144, 154, 168)))
            {
                g.DrawString("暖光 · 静息", _title, text, 32, 26);
                g.DrawString("柔和光核 · 连续泛光 · 极轻呼吸 · 无连接完全隐藏", _body, muted, 32, 65);
                g.DrawImageUnscaled(_frame, 284, 108);
                string label = state == BeaconState.Connected ? "SSH 已连接" : state == BeaconState.Linger ? "断开后 6 秒柔和淡出" : "静默：没有任何光点";
                g.DrawString(label, _body, text, 230, 206);
                g.DrawString("原尺寸预览   /   Space 重播   /   L 切换背景   /   Esc 关闭", _body, muted, 32, 285);
                g.DrawString("仅模拟动效，不检测网络、不改设置、不影响正在运行的小点。", _body, muted, 32, 312);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _timer.Dispose(); _frame.Dispose(); _title.Dispose(); _body.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
