using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SshBeacon
{
    internal enum BeaconState
    {
        Idle,
        Connected,
        Linger,
        Error
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        internal struct BLENDFUNCTION { public byte BlendOp; public byte BlendFlags; public byte SourceConstantAlpha; public byte AlphaFormat; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; public int bmiColors; }

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst,
            ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(IntPtr h);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage,
            out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr dest, IntPtr src, IntPtr count);

        internal static void Blit(IntPtr dst, IntPtr src, int count)
        {
            CopyMemory(dst, src, (IntPtr)count);
        }

        internal const int WS_EX_LAYERED = 0x00080000;
        internal const int WS_EX_TRANSPARENT = 0x00000020;
        internal const int WS_EX_TOOLWINDOW = 0x00000080;
        internal const int WS_EX_NOACTIVATE = 0x08000000;
        internal const int GWL_EXSTYLE = -20;
        internal const int ULW_ALPHA = 2;
        internal const byte AC_SRC_ALPHA = 1;
    }

    internal abstract class LayeredWindow : Form
    {
        protected LayeredWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            MinimizeBox = false;
            MaximizeBox = false;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE;
                if (ClickThrough)
                {
                    cp.ExStyle |= Native.WS_EX_TRANSPARENT;
                }
                return cp;
            }
        }

        protected virtual bool ClickThrough { get { return false; } }

        // Draws a premultiplied-alpha bitmap onto the layered window at the form's Location.
        internal void Present(Bitmap premultiplied)
        {
            if (!IsHandleCreated || premultiplied == null) return;
            BitmapData data = null;
            IntPtr screenDc = IntPtr.Zero, memDc = IntPtr.Zero, hBmp = IntPtr.Zero, oldBmp = IntPtr.Zero;
            try
            {
                data = premultiplied.LockBits(new Rectangle(0, 0, premultiplied.Width, premultiplied.Height),
                    ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                int w = premultiplied.Width;
                int h = premultiplied.Height;
                screenDc = Native.GetDC(IntPtr.Zero);
                memDc = Native.CreateCompatibleDC(screenDc);
                Native.BITMAPINFO bi = new Native.BITMAPINFO();
                bi.bmiHeader.biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
                bi.bmiHeader.biWidth = w;
                bi.bmiHeader.biHeight = -h;
                bi.bmiHeader.biPlanes = 1;
                bi.bmiHeader.biBitCount = 32;
                bi.bmiHeader.biCompression = 0;
                IntPtr bits;
                hBmp = Native.CreateDIBSection(memDc, ref bi, 0, out bits, IntPtr.Zero, 0);
                if (hBmp == IntPtr.Zero) return;
                for (int y = 0; y < h; y++)
                {
                    Native.Blit(IntPtr.Add(bits, y * w * 4), IntPtr.Add(data.Scan0, y * data.Stride), w * 4);
                }
                oldBmp = Native.SelectObject(memDc, hBmp);
                Native.POINT dst = new Native.POINT();
                dst.X = Location.X;
                dst.Y = Location.Y;
                Native.POINT src = new Native.POINT();
                Native.SIZE size = new Native.SIZE();
                size.cx = w;
                size.cy = h;
                Native.BLENDFUNCTION blend = new Native.BLENDFUNCTION();
                blend.BlendOp = 0;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = 255;
                blend.AlphaFormat = Native.AC_SRC_ALPHA;
                Native.UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend,
                    Native.ULW_ALPHA);
            }
            finally
            {
                if (oldBmp != IntPtr.Zero) Native.SelectObject(memDc, oldBmp);
                if (hBmp != IntPtr.Zero) Native.DeleteObject(hBmp);
                if (memDc != IntPtr.Zero) Native.DeleteDC(memDc);
                if (screenDc != IntPtr.Zero) Native.ReleaseDC(IntPtr.Zero, screenDc);
                if (data != null) premultiplied.UnlockBits(data);
            }
        }

        internal static int AlphaOf(double fraction)
        {
            int v = (int)Math.Round(fraction * 255.0);
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return v;
        }
    }

    internal sealed class DotWindow : LayeredWindow
    {
        internal const int SizePx = 72;
        private readonly Bitmap _canvas;
        private bool _dragging;
        private Point _dragStart;
        private Point _windowStart;

        internal event EventHandler PositionMoved;
        internal event EventHandler DragFinished;

        internal DotWindow()
        {
            _canvas = new Bitmap(SizePx, SizePx, PixelFormat.Format32bppPArgb);
            Size = new Size(SizePx, SizePx);
            MinimumSize = new Size(SizePx, SizePx);
            MaximumSize = new Size(SizePx, SizePx);
            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
        }

        internal Bitmap Canvas { get { return _canvas; } }

        internal Point CenterOnScreen
        {
            get { return new Point(Left + SizePx / 2, Top + SizePx / 2); }
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragStart = Cursor.Position;
                _windowStart = Location;
                Capture = true;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            Point now = Cursor.Position;
            Location = new Point(_windowStart.X + (now.X - _dragStart.X), _windowStart.Y + (now.Y - _dragStart.Y));
            if (PositionMoved != null) PositionMoved(this, EventArgs.Empty);
        }

        private void OnMouseUp(object sender, MouseEventArgs e)
        {
            if (_dragging && e.Button == MouseButtons.Left)
            {
                _dragging = false;
                Capture = false;
                if (DragFinished != null) DragFinished(this, EventArgs.Empty);
            }
        }
    }

    internal sealed class PillWindow : LayeredWindow
    {
        private static readonly Font TitleFont = new Font("Microsoft YaHei UI", 9.0f, FontStyle.Bold, GraphicsUnit.Point);
        private static readonly Font SubFont = new Font("Microsoft YaHei UI", 8.25f, FontStyle.Regular, GraphicsUnit.Point);
        private static readonly StringFormat TextFormat = StringFormat.GenericTypographic;

        private Bitmap _canvas;
        private string _lastKey;

        protected override bool ClickThrough { get { return true; } }

        internal void ShowInfo(Point dotCenter, string title, string sub, Color accent)
        {
            string key = title + "\u0001" + sub + "\u0001" + dotCenter.X + "," + dotCenter.Y;
            if (!Visible || key != _lastKey)
            {
                Render(dotCenter, title, sub, accent);
                _lastKey = key;
            }
            else
            {
                Present(_canvas);
            }
        }

        private void Render(Point dotCenter, string title, string sub, Color accent)
        {
            int titleHeight, titleWidth, subHeight, subWidth;
            using (Bitmap probe = new Bitmap(1, 1, PixelFormat.Format32bppPArgb))
            using (Graphics g = Graphics.FromImage(probe))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                SizeF ts = g.MeasureString(title, TitleFont, 1000, TextFormat);
                SizeF ss = g.MeasureString(sub, SubFont, 1000, TextFormat);
                titleHeight = (int)Math.Ceiling(ts.Height);
                titleWidth = (int)Math.Ceiling(ts.Width);
                subHeight = (int)Math.Ceiling(ss.Height);
                subWidth = (int)Math.Ceiling(ss.Width);
            }

            int padX = 12, padY = 8;
            int w = Math.Max(titleWidth, subWidth) + padX * 2 + 9;
            int h = padY * 2 + titleHeight + 2 + subHeight;
            if (w < 150) w = 150;

            if (_canvas == null || _canvas.Width != w || _canvas.Height != h)
            {
                if (_canvas != null) _canvas.Dispose();
                _canvas = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                if (Size.Width != w || Size.Height != h) Size = new Size(w, h);
            }

            using (Graphics g = Graphics.FromImage(_canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                RectangleF rect = new RectangleF(0.5f, 0.5f, w - 1f, h - 1f);
                using (GraphicsPath path = RoundedRect(rect, 9f))
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(238, 28, 30, 34)))
                using (Pen border = new Pen(Color.FromArgb(90, 90, 96, 106), 1f))
                {
                    g.FillPath(bg, path);
                    g.DrawPath(border, path);
                }

                using (SolidBrush accentBrush = new SolidBrush(accent))
                {
                    g.FillEllipse(accentBrush, padX - 1, padY + titleHeight / 2f - 2.5f, 5f, 5f);
                }

                using (SolidBrush tb = new SolidBrush(Color.FromArgb(246, 248, 250)))
                using (SolidBrush sb = new SolidBrush(Color.FromArgb(178, 186, 196)))
                {
                    g.DrawString(title, TitleFont, tb, padX + 9, padY - 2, TextFormat);
                    g.DrawString(sub, SubFont, sb, padX + 9, padY + titleHeight - 1, TextFormat);
                }
            }

            Rectangle wa = Screen.FromPoint(dotCenter).WorkingArea;
            int x = dotCenter.X - DotWindow.SizePx / 2 - 12 - w;
            if (x < wa.Left + 4) x = dotCenter.X + DotWindow.SizePx / 2 + 12;
            if (x + w > wa.Right - 4) x = wa.Right - 4 - w;
            int y = dotCenter.Y - h / 2;
            if (y < wa.Top + 4) y = wa.Top + 4;
            if (y + h > wa.Bottom - 4) y = wa.Bottom - 4 - h;
            Location = new Point(x, y);
            if (!Visible) Visible = true;
            Present(_canvas);
        }

        internal void HidePill()
        {
            if (Visible) Visible = false;
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    internal sealed class App : ApplicationContext
    {
        private const string AppDirName = "SshBeacon";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunName = "SshBeacon";
        private const int PollIntervalMs = 800;
        private const int RenderIntervalMs = 40;
        private const int HoverIntervalMs = 120;
        private const int LingerSeconds = 6;

        private readonly DotWindow _dot;
        private readonly PillWindow _pill;
        private readonly NotifyIcon _tray;
        private readonly Icon _plainIcon, _unreadIcon;
        private readonly VisitHistory _history = new VisitHistory();
        private readonly System.ComponentModel.BackgroundWorker _logReader = new System.ComponentModel.BackgroundWorker();
        private DateTime _nextReadUtc;
        private long _latestRecord, _readTicks;
        private long _lastActivityTicks;
        private bool _loaded, _unread, _closing;
        private string _historyError;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _menuStatus;
        private readonly ToolStripMenuItem _menuAutoStart;
        private readonly System.Windows.Forms.Timer _pollTimer;
        private readonly System.Windows.Forms.Timer _renderTimer;
        private readonly System.Windows.Forms.Timer _hoverTimer;

        private readonly Dictionary<string, string> _peerNames = new Dictionary<string, string>();
        private readonly Dictionary<string, DateTime> _peerNamesAt = new Dictionary<string, DateTime>();

        private DateTime _connectedSinceUtc = DateTime.MinValue;
        private DateTime _lastConnUtc = DateTime.MinValue;
        private DateTime _flashUntilUtc = DateTime.MinValue;
        private int _connections;
        private List<string> _sources = new List<string>();
        private bool _error;
        private string _errorText = "";
        private BeaconState _state = BeaconState.Idle;
        private bool _dirty = true;
        private bool _menuOpen;
        private string _title = "";
        private string _sub = "";
        private Color _accent = Color.Gray;

        private readonly string _settingsPath;
        private readonly string _logPath;
        private readonly string _tsExe = @"C:\Program Files\Tailscale\tailscale.exe";
        private int _posX = int.MinValue;
        private int _posY = int.MinValue;
        private DateTime _lastLogUtc = DateTime.MinValue;

        internal App()
        {
            string baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppDirName);
            Directory.CreateDirectory(baseDir);
            _settingsPath = Path.Combine(baseDir, "settings.ini");
            _logPath = Path.Combine(baseDir, "beacon.log");
            Log("start " + Application.ExecutablePath);

            LoadSettings();
            long.TryParse(ReadSetting("read_ticks", "0"), out _readTicks);

            _dot = new DotWindow();
            _pill = new PillWindow();

            _menu = new ContextMenuStrip();
            _menuStatus = new ToolStripMenuItem("远程 SSH：未连接");
            _menuStatus.Enabled = false;
            ToolStripMenuItem openLog = new ToolStripMenuItem("查看访问日志");
            openLog.Click += delegate { OpenLog(); };
            ToolStripMenuItem resetPos = new ToolStripMenuItem("重置位置");
            resetPos.Click += delegate { ResetPosition(); };
            _menuAutoStart = new ToolStripMenuItem("开机自启");
            _menuAutoStart.Click += OnToggleAutoStart;
            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { Quit(); };
            _menu.Items.Add(_menuStatus);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_menuAutoStart);
            _menu.Items.Add(resetPos);
            _menu.Items.Add(openLog);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exitItem);
            _menu.Opened += delegate { _menuOpen = true; _pill.HidePill(); };
            _menu.Closed += delegate { _menuOpen = false; };
            _menuAutoStart.Checked = IsAutoStartEnabled();
            _dot.ContextMenuStrip = _menu;

            _dot.PositionMoved += delegate { _dirty = true; };
            _dot.DragFinished += delegate { SaveSettings(); };

            _tray = new NotifyIcon();
            _plainIcon = BuildTrayIcon(false);
            _unreadIcon = BuildTrayIcon(true);
            _tray.Icon = _plainIcon;
            _tray.Text = "SSH 提示";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _menu;
            _tray.DoubleClick += delegate { ResetPosition(); };
            _tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) MarkRead(); };
            _logReader.DoWork += delegate(object sender, System.ComponentModel.DoWorkEventArgs e)
            {
                e.Result = LoginBatch.Read((long)e.Argument);
            };
            _logReader.RunWorkerCompleted += OnHistoryRead;

            _pollTimer = new System.Windows.Forms.Timer();
            _pollTimer.Interval = PollIntervalMs;
            _pollTimer.Tick += delegate { Poll(); };
            _pollTimer.Start();

            _renderTimer = new System.Windows.Forms.Timer();
            _renderTimer.Interval = RenderIntervalMs;
            _renderTimer.Tick += delegate { RenderTick(); };
            _renderTimer.Start();

            _hoverTimer = new System.Windows.Forms.Timer();
            _hoverTimer.Interval = HoverIntervalMs;
            _hoverTimer.Tick += delegate { HoverTick(); };
            _hoverTimer.Start();

            Point p = (_posX == int.MinValue || _posY == int.MinValue) ? DefaultPosition() : new Point(_posX, _posY);
            _dot.Location = ClampPosition(p);
            EnsureAutoStart();
            Poll();
            RenderTick();
            if (!ReadSettingBool("hint_shown", false))
            {
                WriteSetting("hint_shown", "1");
                _tray.ShowBalloonTip(3500, "SSH 提示已就位", "有远程 SSH 连接时，屏幕角落的小点会闪黄色；悬停可看来源。", ToolTipIcon.None);
            }
            Application.ApplicationExit += delegate { Cleanup(); };
        }

        private Point DefaultPosition()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            return new Point(wa.Right - DotWindow.SizePx - 20, wa.Bottom - DotWindow.SizePx - 20);
        }

        private Point ClampPosition(Point p)
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            if (p.X < wa.Left) p.X = wa.Left;
            if (p.Y < wa.Top) p.Y = wa.Top;
            if (p.X + DotWindow.SizePx > wa.Right) p.X = wa.Right - DotWindow.SizePx;
            if (p.Y + DotWindow.SizePx > wa.Bottom) p.Y = wa.Bottom - DotWindow.SizePx;
            return p;
        }

        private void Poll()
        {
            if (!_logReader.IsBusy && DateTime.UtcNow >= _nextReadUtc)
            {
                _nextReadUtc = DateTime.UtcNow.AddSeconds(3);
                _logReader.RunWorkerAsync(_latestRecord);
            }
            try
            {
                List<string> sources = new List<string>();
                int connections = 0;
                TcpConnectionInformation[] conns = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections();
                for (int i = 0; i < conns.Length; i++)
                {
                    TcpConnectionInformation c = conns[i];
                    if (c.State != TcpState.Established) continue;
                    if (c.LocalEndPoint.Port != 22) continue;
                    connections++;
                    IPAddress ip = c.RemoteEndPoint.Address;
                    if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
                    string s = ip.ToString();
                    if (!sources.Contains(s)) sources.Add(s);
                }
                if (connections > 0)
                {
                    if (_connections == 0)
                    {
                        _connectedSinceUtc = DateTime.UtcNow;
                        _flashUntilUtc = DateTime.UtcNow.AddSeconds(1.0);
                    }
                    _lastConnUtc = DateTime.UtcNow;
                }
                _connections = connections;
                _sources = sources;
                if (_error)
                {
                    _error = false;
                    _errorText = "";
                    Log("monitor recovered");
                }
            }
            catch (Exception ex)
            {
                _connections = 0;
                _sources = new List<string>();
                if (!_error || _errorText != ex.Message) Log("monitor error: " + ex.Message);
                _error = true;
                _errorText = ex.Message;
            }
            _dirty = true;
            if (_connections > 0 || _error)
            {
                if (!_renderTimer.Enabled) _renderTimer.Start();
                if (!_hoverTimer.Enabled) _hoverTimer.Start();
            }
        }

        private BeaconState ComputeState(DateTime nowUtc)
        {
            if (_error) return BeaconState.Error;
            if (_connections > 0) return BeaconState.Connected;
            if (_lastConnUtc != DateTime.MinValue &&
                (nowUtc - _lastConnUtc).TotalSeconds < LingerSeconds) return BeaconState.Linger;
            return BeaconState.Idle;
        }

        private void RenderTick()
        {
            DateTime now = DateTime.UtcNow;
            BeaconState st = ComputeState(now);
            bool animated = st == BeaconState.Connected || st == BeaconState.Linger || now < _flashUntilUtc;
            bool dirty = _dirty;
            _dirty = false;

            if (st == BeaconState.Idle && !animated)
            {
                if (_dot.Visible)
                {
                    _dot.Hide();
                    _pill.HidePill();
                }
                if (_state != st)
                {
                    _state = st;
                    UpdateText();
                }
                _renderTimer.Stop();
                _hoverTimer.Stop();
                return;
            }

            if (!_dot.Visible) _dot.Show();
            if (!animated && !dirty && st == _state) return;
            _state = st;
            DrawDot(st, now);
            _dot.Present(_dot.Canvas);
            UpdateText();
        }

        private void DrawDot(BeaconState st, DateTime now)
        {
            BeaconVisuals.Draw(_dot.Canvas, st,
                (now - _connectedSinceUtc).TotalSeconds,
                (now - _lastConnUtc).TotalSeconds, LingerSeconds);
        }

        private void UpdateText()
        {
            DateTime now = DateTime.UtcNow;
            switch (_state)
            {
                case BeaconState.Connected:
                    {
                        string names = DescribeSources();
                        string duration = FormatDuration(now - _connectedSinceUtc);
                        _title = "远程 SSH 已连接";
                        _sub = names + " · " + (_connections > 1 ? _connections + " 个会话 · " : "") + duration;
                        _accent = Color.FromArgb(255, 196, 20);
                        break;
                    }
                case BeaconState.Linger:
                    {
                        double secs = (now - _lastConnUtc).TotalSeconds;
                        _title = "远程 SSH 刚刚断开";
                        _sub = DescribeSources() + " · " + (int)Math.Max(0, secs) + " 秒前";
                        _accent = Color.FromArgb(214, 168, 60);
                        break;
                    }
                case BeaconState.Error:
                    _title = "SSH 监控异常";
                    _sub = Truncate(_errorText, 56);
                    _accent = Color.FromArgb(255, 69, 58);
                    break;
                default:
                    _title = "远程 SSH 未连接";
                    _sub = "有入站 SSH 会话时这里会闪烁";
                    _accent = Color.FromArgb(150, 156, 166);
                    break;
            }
            _menuStatus.Text = "远程 SSH：" + StateWord(_state);
            string txt = _historyError != null ? "SSH 登录记录暂时无法读取" : "SSH 提示 · " + StateWord(_state);
            if (_history.Recent.Count > 0)
            {
                Visit last = _history.Recent[0];
                txt += (_unread ? " · 未读 " : " · 最近 ") + last.Last.ToLocalTime().ToString("MM-dd HH:mm") + " " + last.First.Address;
            }
            if (txt.Length > 62) txt = txt.Substring(0, 62);
            _tray.Text = txt;
        }

        private static string StateWord(BeaconState st)
        {
            switch (st)
            {
                case BeaconState.Connected: return "连接中";
                case BeaconState.Linger: return "刚断开";
                case BeaconState.Error: return "监控异常";
                default: return "未连接";
            }
        }

        private string DescribeSources()
        {
            if (_sources.Count == 0) return "";
            string ip = _sources[0];
            string name = PeerName(ip);
            if (_sources.Count == 1) return "来自 " + name;
            return "来自 " + _sources.Count + " 个来源";
        }

        private string PeerName(string ip)
        {
            string name;
            DateTime at;
            if (_peerNames.TryGetValue(ip, out name) && _peerNamesAt.TryGetValue(ip, out at) &&
                (DateTime.UtcNow - at).TotalMinutes < 5)
            {
                return name;
            }
            name = null;
            try
            {
                if (File.Exists(_tsExe))
                {
                    ProcessStartInfo psi = new ProcessStartInfo(_tsExe, "status");
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.CreateNoWindow = true;
                    using (Process p = Process.Start(psi))
                    {
                        string outp = p.StandardOutput.ReadToEnd();
                        if (!p.WaitForExit(1500)) { try { p.Kill(); } catch { } }
                        string[] lines = outp.Split('\n');
                        for (int i = 0; i < lines.Length; i++)
                        {
                            string[] f = lines[i].Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (f.Length >= 2 && f[0] == ip)
                            {
                                name = f[1];
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("peer lookup failed: " + ex.Message);
            }
            if (string.IsNullOrEmpty(name)) name = ip;
            _peerNames[ip] = name;
            _peerNamesAt[ip] = DateTime.UtcNow;
            return name;
        }

        private static string FormatDuration(TimeSpan span)
        {
            if (span.TotalHours >= 1)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}",
                    (int)span.TotalHours, span.Minutes, span.Seconds);
            }
            return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", span.Minutes, span.Seconds);
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max - 1) + "…";
        }

        private void HoverTick()
        {
            if (_menuOpen || !_dot.Visible)
            {
                _pill.HidePill();
                return;
            }
            Rectangle dotRect = new Rectangle(_dot.Location, new Size(DotWindow.SizePx, DotWindow.SizePx));
            if (dotRect.Contains(Cursor.Position))
            {
                _pill.ShowInfo(_dot.CenterOnScreen, _title, _sub, _accent);
            }
            else
            {
                _pill.HidePill();
            }
        }

        private void OnToggleAutoStart(object sender, EventArgs e)
        {
            bool enable = !IsAutoStartEnabled();
            SetAutoStart(enable);
            _menuAutoStart.Checked = enable;
            WriteSetting("autostart", enable ? "1" : "0");
        }

        private bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    if (k == null) return false;
                    string v = k.GetValue(RunName) as string;
                    return !string.IsNullOrEmpty(v);
                }
            }
            catch { return false; }
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (enable) k.SetValue(RunName, "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue(RunName, false);
                }
            }
            catch (Exception ex)
            {
                Log("autostart failed: " + ex.Message);
            }
        }

        private void EnsureAutoStart()
        {
            if (!ReadSettingBool("first_run_done", false))
            {
                SetAutoStart(true);
                WriteSetting("first_run_done", "1");
                WriteSetting("autostart", "1");
                return;
            }
            string v = ReadSetting("autostart", "");
            if (v == "1") SetAutoStart(true);
            else if (v == "0") SetAutoStart(false);
        }

        private void ResetPosition()
        {
            _dot.Location = ClampPosition(DefaultPosition());
            SaveSettings();
            _dirty = true;
            RenderTick();
        }

        private void OnHistoryRead(object sender, System.ComponentModel.RunWorkerCompletedEventArgs e)
        {
            if (_closing) return;
            if (e.Error != null)
            {
                if (_historyError == null) Log("读取 SSH 登录记录失败：" + e.Error.Message);
                _historyError = "暂时无法读取 Windows 的 SSH 登录记录。组件会自动重试。";
                _nextReadUtc = DateTime.UtcNow.AddSeconds(15);
                UpdateText();
                return;
            }
            LoginBatch batch = (LoginBatch)e.Result;
            bool refresh = !_loaded || _historyError != null || batch.Items.Count > 0 || batch.Transfers.Count > 0;
            _historyError = null;
            Login notify = null;
            foreach (Login login in batch.Items)
            {
                if (_history.Add(login) && _loaded) notify = login;
                _lastActivityTicks = Math.Max(_lastActivityTicks, login.Utc.Ticks);
            }
            Transfer transfer = batch.Transfers.Count == 0 ? null : batch.Transfers[0];
            if (transfer != null) _lastActivityTicks = Math.Max(_lastActivityTicks, transfer.Utc.Ticks);
            _unread = _lastActivityTicks > _readTicks;
            bool notifyTransfer = _loaded && transfer != null;
            _latestRecord = batch.LatestId;
            _loaded = true;
            if (!refresh) return;
            _tray.Icon = _unread ? _unreadIcon : _plainIcon;
            UpdateText();
            if (notifyTransfer)
                _tray.ShowBalloonTip(3500, transfer.Action, PeerName(transfer.Device) + " · " + transfer.Name +
                    (batch.Transfers.Count > 1 ? "（本次共 " + batch.Transfers.Count + " 条文件记录）" : ""), ToolTipIcon.None);
            else if (notify != null)
                _tray.ShowBalloonTip(3500, "有设备通过 SSH 登录了这台电脑", DeviceLabel(notify) + " · 账号 " + notify.User, ToolTipIcon.None);
        }

        private string DeviceLabel(Login login)
        {
            string name = PeerName(login.Address);
            return name == login.Address ? name : name + " (" + login.Address + ")";
        }

        private void MarkRead()
        {
            if (!_unread) return;
            _readTicks = _lastActivityTicks;
            _unread = false;
            _tray.Icon = _plainIcon;
            WriteSetting("read_ticks", _readTicks.ToString(CultureInfo.InvariantCulture));
            UpdateText();
        }

        private void OpenLog()
        {
            List<ActivityCard> cards = new List<ActivityCard>();
            try
            {
                foreach (Transfer t in Transfer.Read())
                {
                    string device = t.Device == "未知设备" ? t.Device : PeerName(t.Device);
                    cards.Add(new ActivityCard { Time = t.Utc, Action = t.Action, Title = t.Name, LocalPath = t.LocalPath,
                        Detail = "设备　" + device + (device == t.Device ? "" : "　" + t.Device) });
                }
            }
            catch (Exception ex)
            {
                cards.Add(new ActivityCard { Time = DateTime.UtcNow, Action = "记录读取异常", Title = "暂时无法读取文件收发记录", Detail = "稍后可重新打开查看。" });
                Log("读取文件收发记录失败：" + ex.Message);
            }
            foreach (Visit v in _history.Recent)
            {
                if (v.Last < DateTime.UtcNow.AddDays(-7)) continue;
                string count = v.Count > 1 ? "　连续 " + v.Count + " 次" : "";
                string period = v.Count > 1 ? "　从 " + v.First.Utc.ToLocalTime().ToString("MM-dd HH:mm:ss") + " 开始" : "";
                cards.Add(new ActivityCard { Time = v.Last, Action = "登录成功" + count,
                    Title = DeviceLabel(v.First), Detail = "账号　" + v.First.User + period });
            }
            if (_historyError != null || cards.Count == 0)
                cards.Add(new ActivityCard { Time = DateTime.MinValue, Action = "访问记录",
                    Title = _historyError ?? (_loaded ? "暂无访问记录" : "正在读取登录记录"), Detail = "" });
            using (ActivityWindow window = new ActivityWindow(cards))
            {
                window.Load += delegate { MarkRead(); };
                window.ShowDialog();
            }
        }

        private void Quit()
        {
            _pollTimer.Stop();
            _renderTimer.Stop();
            _hoverTimer.Stop();
            SaveSettings();
            _tray.Visible = false;
            Application.Exit();
        }

        private void Cleanup()
        {
            _closing = true;
            try
            {
                SaveSettings();
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                    _plainIcon.Dispose();
                    _unreadIcon.Dispose();
                }
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return;
                foreach (string line in File.ReadAllLines(_settingsPath))
                {
                    int idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    string key = line.Substring(0, idx).Trim();
                    string val = line.Substring(idx + 1).Trim();
                    if (key == "x") { int t; if (int.TryParse(val, out t)) _posX = t; }
                    else if (key == "y") { int t; if (int.TryParse(val, out t)) _posY = t; }
                }
            }
            catch (Exception ex)
            {
                Log("settings load failed: " + ex.Message);
            }
        }

        private void SaveSettings()
        {
            try
            {
                List<string> lines = new List<string>();
                lines.Add("x=" + _dot.Location.X);
                lines.Add("y=" + _dot.Location.Y);
                lines.Add("autostart=" + (IsAutoStartEnabled() ? "1" : "0"));
                lines.Add("hint_shown=" + ReadSetting("hint_shown", "1"));
                lines.Add("first_run_done=1");
                lines.Add("read_ticks=" + _readTicks.ToString(CultureInfo.InvariantCulture));
                File.WriteAllLines(_settingsPath, lines.ToArray());
            }
            catch (Exception ex)
            {
                Log("settings save failed: " + ex.Message);
            }
        }

        private string ReadSetting(string key, string def)
        {
            try
            {
                if (!File.Exists(_settingsPath)) return def;
                foreach (string line in File.ReadAllLines(_settingsPath))
                {
                    int idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    if (line.Substring(0, idx).Trim() == key) return line.Substring(idx + 1).Trim();
                }
            }
            catch { }
            return def;
        }

        private bool ReadSettingBool(string key, bool def)
        {
            return ReadSetting(key, def ? "1" : "0") == "1";
        }

        private void WriteSetting(string key, string value)
        {
            try
            {
                List<string> lines = new List<string>();
                bool replaced = false;
                if (File.Exists(_settingsPath))
                {
                    foreach (string line in File.ReadAllLines(_settingsPath))
                    {
                        int idx = line.IndexOf('=');
                        if (idx > 0 && line.Substring(0, idx).Trim() == key)
                        {
                            lines.Add(key + "=" + value);
                            replaced = true;
                        }
                        else
                        {
                            lines.Add(line);
                        }
                    }
                }
                if (!replaced) lines.Add(key + "=" + value);
                File.WriteAllLines(_settingsPath, lines.ToArray());
            }
            catch { }
        }

        private void Log(string message)
        {
            try
            {
                DateTime now = DateTime.UtcNow;
                if ((now - _lastLogUtc).TotalSeconds < 1 && message == "monitor error") return;
                _lastLogUtc = now;
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message;
                FileInfo fi = new FileInfo(_logPath);
                if (fi.Exists && fi.Length > 256 * 1024)
                {
                    File.Copy(_logPath, _logPath + ".old", true);
                    File.Delete(_logPath);
                }
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch { }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        internal static Icon BuildTrayIcon(bool unread)
        {
            using (Bitmap bmp = new Bitmap(32, 32, PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (Brush yellow = new SolidBrush(Color.FromArgb(255, 196, 20)))
                    using (Brush highlight = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                    {
                        g.FillEllipse(yellow, 6, 6, 20, 20);
                        g.FillEllipse(highlight, 10, 9, 8, 6);
                    }
                    if (unread)
                    {
                        g.FillEllipse(Brushes.SaddleBrown, 18, 0, 14, 14);
                        g.FillEllipse(Brushes.OrangeRed, 20, 2, 10, 10);
                    }
                }
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Preview is isolated: no polling, tray, settings, startup registration or singleton.
            if (args.Length == 1 && args[0] == "--preview")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new VisualPreview());
                return;
            }
            bool created;
            using (Mutex mutex = new Mutex(true, "SshBeacon_SingleInstance", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
                {
                    try
                    {
                        File.AppendAllText(Path.Combine(
                            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                            "SshBeacon", "beacon.log"),
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  crash: " + e.Exception + Environment.NewLine);
                    }
                    catch { }
                };
                Application.Run(new App());
            }
        }
    }
}
