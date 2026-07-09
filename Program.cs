using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Drawing.Imaging;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace VoidAutoClicker
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            try { Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    static class Native
    {
        [DllImport("user32.dll")]
        public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;
        public const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        public const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        public const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }

        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);
        [DllImport("ntdll.dll")] public static extern int NtQueryTimerResolution(out uint Min, out uint Max, out uint Current);

        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        public const int WM_NCLBUTTONDOWN = 0xA1;
        public const int HT_CAPTION = 0x2;
        public const int WM_EXITSIZEMOVE = 0x0232;

        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)] public struct FILETIME { public uint Low; public uint High; }
        [DllImport("kernel32.dll", SetLastError = true)] public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
        public static ulong FT(FILETIME f) { return ((ulong)f.High << 32) | f.Low; }

        [StructLayout(LayoutKind.Sequential)]
        public class MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
            public MEMORYSTATUSEX() { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)); }
        }
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX m);
        [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);
        public const int VREFRESH = 116;
    }

    // A single saved preset.
    class Profile
    {
        public string Name { get; set; } = "Default";
        public int Cps { get; set; } = 14;
        public int RangePlus { get; set; } = 2;
        public bool Randomize { get; set; } = false;
        public int Action { get; set; } = 0;        // 0 = click, 1 = hold
        public int Output { get; set; } = 0;        // 0 = mouse, 1 = keyboard
        public int MouseButton { get; set; } = 0;   // 0 = left, 1 = right
        public int KeyVk { get; set; } = 0x46;      // keyboard output key ('F')
        public int Mode { get; set; } = 0;          // trigger: 0 = hold, 1 = toggle
        public int BindVk { get; set; } = 0x06;     // trigger key (Mouse 5)
        public bool UseFixedPos { get; set; } = false;
        public int PosX { get; set; } = 0;
        public int PosY { get; set; } = 0;
        public bool ReturnCursor { get; set; } = false;
    }

    class Settings
    {
        public List<Profile> Profiles { get; set; } = new List<Profile>();
        public int ActiveProfile { get; set; } = 0;
        public bool Humanize { get; set; } = false;
        public bool RobloxOnly { get; set; } = false;
        public bool AlwaysOnTop { get; set; } = false;
        public bool MinimizeToTray { get; set; } = true;
        public bool ShowSplash { get; set; } = true;
        public int PanicVk { get; set; } = 0x77;     // F8
        public int AccentArgb { get; set; } = 0;
        public bool ShowOverlay { get; set; } = false;
        public int OverlayX { get; set; } = 0;
        public int OverlayY { get; set; } = 0;
        public bool BedwarsSeeded { get; set; } = false;
        public double UiScale { get; set; } = 1.0;
        public bool SoundEnabled { get; set; } = true;
        public bool AutoOptimize { get; set; } = false;
        public bool BoostPriority { get; set; } = true;
        public bool BoostPower { get; set; } = true;
        public bool FpsCapEnabled { get; set; } = false;
        public int FpsCap { get; set; } = 240;
        public bool FpsUncapped { get; set; } = false;
        // trim tracker
        public List<int> TrimGames { get; set; } = new List<int>();
        public int TrimBase { get; set; } = 0;
        public int TrimWins { get; set; } = 0;
        public int TrimGoal { get; set; } = 10;
        public int TrimTotalWins { get; set; } = 0;
        // updates
        public bool CheckUpdates { get; set; } = true;
        public string SkippedVersion { get; set; } = "";
    }

    static class App
    {
        public const string Version = "1.3.0";
        public const string Owner = "Garfieldking45";
        public const string Repo = "VoidAutoClicker";
        public static string LatestApi { get { return "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest"; } }
        public static string ReleasesPage { get { return "https://github.com/" + Owner + "/" + Repo + "/releases/latest"; } }
    }

    static class Theme
    {
        public static readonly Color Bg        = Color.FromArgb(11, 11, 16);
        public static readonly Color Panel     = Color.FromArgb(21, 21, 30);
        public static readonly Color PanelHi   = Color.FromArgb(29, 29, 41);
        public static readonly Color Border    = Color.FromArgb(38, 38, 53);
        public static Color Accent             = Color.FromArgb(139, 92, 246);
        public static Color AccentSoft         = Color.FromArgb(167, 139, 250);
        public static readonly Color Green     = Color.FromArgb(52, 211, 153);
        public static readonly Color Red       = Color.FromArgb(244, 63, 94);
        public static readonly Color Amber     = Color.FromArgb(251, 191, 36);
        public static readonly Color Text      = Color.FromArgb(243, 243, 248);
        public static readonly Color TextDim   = Color.FromArgb(132, 132, 152);
        public static readonly Color TextFaint = Color.FromArgb(78, 78, 96);
        public static readonly Color[] Presets =
        {
            Color.FromArgb(139, 92, 246), Color.FromArgb(34, 211, 238), Color.FromArgb(52, 211, 153),
            Color.FromArgb(244, 63, 94),  Color.FromArgb(251, 146, 60), Color.FromArgb(96, 165, 250),
        };
        public static void SetAccent(Color c) { Accent = c; AccentSoft = Fx.Lighten(c, 1.22f); }
    }

    static class Fx
    {
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
        public static Color Lighten(Color c, float f) { return Color.FromArgb(c.A, Math.Min(255, (int)(c.R*f)), Math.Min(255, (int)(c.G*f)), Math.Min(255, (int)(c.B*f))); }
        public static Color Darken(Color c, float f) { return Color.FromArgb(c.A, (int)(c.R*f), (int)(c.G*f), (int)(c.B*f)); }
        public static Color Lerp(Color a, Color b, float t) { if (t<0) t=0; if (t>1) t=1; return Color.FromArgb((int)(a.A+(b.A-a.A)*t),(int)(a.R+(b.R-a.R)*t),(int)(a.G+(b.G-a.G)*t),(int)(a.B+(b.B-a.B)*t)); }
        public static Font F(float size, FontStyle s = FontStyle.Regular) { return new Font("Segoe UI", size, s); }
    }

    abstract class DpiControl : Control
    {
        protected DpiControl() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); }
        protected float S { get { return DeviceDpi / 96f; } }
        protected int Sc(int v) { return (int)Math.Round(v * S); }
        protected float Scf(float v) { return v * S; }

        System.Windows.Forms.Timer _anim;
        protected void StartAnim()
        {
            if (_anim == null) { _anim = new System.Windows.Forms.Timer { Interval = 16 }; _anim.Tick += (s, e) => { bool go = AnimStep(); Invalidate(); if (!go) _anim.Stop(); }; }
            if (!_anim.Enabled) _anim.Start();
        }
        // ease 'cur' toward 'target'; returns true while still moving
        protected static bool Ease(ref float cur, float target, float k)
        {
            cur += (target - cur) * k;
            if (Math.Abs(cur - target) < 0.004f) { cur = target; return false; }
            return true;
        }
        protected virtual bool AnimStep() { return false; }
        protected override void Dispose(bool disposing) { if (disposing && _anim != null) { _anim.Stop(); _anim.Dispose(); _anim = null; } base.Dispose(disposing); }
    }

    // Lightweight rounded container used to group sections inside a panel.
    class SubCard : Panel
    {
        bool _hero;
        public bool Hero { get { return _hero; } set { _hero = value; BackColor = value ? Theme.PanelHi : Theme.Panel; } }
        float S { get { return DeviceDpi / 96f; } }
        int Sc(int v) { return (int)Math.Round(v * S); }
        public SubCard() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); BackColor = Theme.Panel; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Fx.Round(r, Sc(10)))
            {
                using (var b = new SolidBrush(_hero ? Theme.PanelHi : Theme.Panel)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Border, S)) g.DrawPath(p, path);
            }
        }
    }

    class Card : Panel
    {
        public string Caption;
        float S { get { return DeviceDpi / 96f; } }
        int Sc(int v) { return (int)Math.Round(v * S); }
        public Card() { SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true); BackColor = Theme.Panel; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Fx.Round(r, Sc(14)))
            {
                using (var b = new SolidBrush(Theme.Panel)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Border, S)) g.DrawPath(p, path);
            }
            if (!string.IsNullOrEmpty(Caption))
            {
                using (var f = Fx.F(9f, FontStyle.Bold)) TextRenderer.DrawText(g, Caption.ToUpper(), f, new Point(Sc(20), Sc(16)), Theme.TextDim, TextFormatFlags.NoPadding);
                using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, Sc(20), Sc(34), Sc(24), Sc(2));
            }
        }
    }

    class Slider : DpiControl
    {
        public int Min = 1, Max = 40;
        int _val = 14; bool _drag; float _vp = -1f;
        public event EventHandler ValueChanged;
        public int Value { get { return _val; } set { int v = Math.Max(Min, Math.Min(Max, value)); if (v != _val) { _val = v; if (!_drag) StartAnim(); Invalidate(); if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); } } }
        public Slider() { Cursor = Cursors.Hand; }
        int Pad { get { return Sc(11); } }
        float Ratio { get { return (float)(_val - Min) / (Max - Min); } }
        float VPRatio { get { if (_vp < 0) _vp = Ratio; return _vp; } }
        int KnobX { get { return Pad + (int)(VPRatio * (Width - 2 * Pad)); } }
        void SetFromX(int x) { float r = (float)(x - Pad) / (Width - 2 * Pad); r = Math.Max(0, Math.Min(1, r)); int nv = Min + (int)Math.Round(r * (Max - Min)); _vp = r; Value = nv; }
        protected override bool AnimStep() { return Ease(ref _vp, Ratio, 0.3f); }
        protected override void OnMouseDown(MouseEventArgs e) { if (!Enabled) return; _drag = true; SetFromX(e.X); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { if (_drag) SetFromX(e.X); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _drag = false; _vp = Ratio; base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int ty = Height / 2;
            Color fill = Enabled ? Theme.Accent : Theme.TextFaint;
            using (var p = new Pen(Theme.Border, Scf(6f))) { p.StartCap = p.EndCap = LineCap.Round; g.DrawLine(p, Pad, ty, Width - Pad, ty); }
            int kx = KnobX;
            using (var p = new Pen(fill, Scf(6f))) { p.StartCap = p.EndCap = LineCap.Round; g.DrawLine(p, Pad, ty, kx, ty); }
            if (Enabled) using (var b = new SolidBrush(Color.FromArgb(55, Theme.Accent))) g.FillEllipse(b, kx - Sc(13), ty - Sc(13), Sc(26), Sc(26));
            using (var b = new SolidBrush(Enabled ? Theme.Text : Theme.TextDim)) g.FillEllipse(b, kx - Sc(8), ty - Sc(8), Sc(16), Sc(16));
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, kx - Sc(4), ty - Sc(4), Sc(8), Sc(8));
        }
    }

    class Toggle : DpiControl
    {
        bool _on; float _p = -1f;
        public event EventHandler Toggled;
        public bool On { get { return _on; } set { if (_on != value) { _on = value; StartAnim(); Invalidate(); if (Toggled != null) Toggled(this, EventArgs.Empty); } } }
        public Toggle() { Cursor = Cursors.Hand; }
        protected override void OnClick(EventArgs e) { On = !On; base.OnClick(e); }
        protected override bool AnimStep() { return Ease(ref _p, _on ? 1f : 0f, 0.3f); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (_p < 0) _p = _on ? 1f : 0f;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Fx.Round(r, Height / 2)) using (var b = new SolidBrush(Fx.Lerp(Theme.Border, Theme.Accent, _p))) g.FillPath(b, path);
            int d = Height - Sc(8);
            int x0 = Sc(4), x1 = Width - d - Sc(4);
            int x = (int)(x0 + (x1 - x0) * _p);
            if (_p > 0.04f) using (var b = new SolidBrush(Color.FromArgb((int)(70 * _p), Theme.AccentSoft))) g.FillEllipse(b, x - Sc(2), Sc(2), d + Sc(4), d + Sc(4));
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x, Sc(4), d, d);
        }
    }

    class Segment : DpiControl
    {
        readonly string[] _items; int _sel; float _pillX = -1f;
        public float FontSize = 9.5f;
        public event EventHandler SelectedChanged;
        public int Selected { get { return _sel; } set { if (_sel != value) { _sel = value; StartAnim(); Invalidate(); if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty); } } }
        public Segment(params string[] items) { _items = items; Cursor = Cursors.Hand; }
        float TargetX { get { return (Width / _items.Length) * _sel + Sc(3); } }
        protected override bool AnimStep() { return Ease(ref _pillX, TargetX, 0.32f); }
        protected override void OnMouseDown(MouseEventArgs e) { int w = Width / _items.Length; Selected = Math.Min(_items.Length - 1, Math.Max(0, e.X / w)); base.OnMouseDown(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (_pillX < 0) _pillX = TargetX;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var outer = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Fx.Round(outer, Sc(9)))
            {
                using (var b = new SolidBrush(Theme.Bg)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Border, S)) g.DrawPath(p, path);
            }
            int w = Width / _items.Length;
            var sel = new Rectangle((int)_pillX, Sc(3), w - Sc(6), Height - Sc(7));
            using (var path = Fx.Round(sel, Sc(7))) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
            using (var f = Fx.F(FontSize, FontStyle.Bold))
                for (int i = 0; i < _items.Length; i++)
                    TextRenderer.DrawText(g, _items[i], f, new Rectangle(i * w, 0, w, Height), i == _sel ? Color.White : Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    class GButton : DpiControl
    {
        bool _hover, _down; float _ha, _pa;
        public Color Fill = Theme.Accent;
        public Color Fg = Color.White;
        public bool Outline = false;
        public float FontSize = 10.5f;
        public GButton() { Cursor = Cursors.Hand; }
        protected override bool AnimStep() { bool a = Ease(ref _ha, _hover ? 1f : 0f, 0.28f); bool b = Ease(ref _pa, _down ? 1f : 0f, 0.4f); return a || b; }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; StartAnim(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; StartAnim(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; StartAnim(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; StartAnim(); base.OnMouseUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int inset = (int)Math.Round(_pa * Scf(1.5f));
            var r = new Rectangle(inset, inset, Width - 1 - inset * 2, Height - 1 - inset * 2);
            Color c = Fx.Lerp(Fx.Lerp(Fill, Fx.Lighten(Fill, 1.12f), _ha), Fx.Darken(Fill, 0.85f), _pa);
            using (var path = Fx.Round(r, Sc(10)))
            {
                using (var b = new SolidBrush(c)) g.FillPath(b, path);
                if (Outline) using (var p = new Pen(Fx.Lerp(Theme.Border, Theme.Accent, _ha), S)) g.DrawPath(p, path);
            }
            using (var f = Fx.F(FontSize, FontStyle.Bold)) TextRenderer.DrawText(g, Text, f, r, Fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    class Swatch : DpiControl
    {
        public Color Color { get; set; } public bool Selected { get; set; }
        public event EventHandler Picked;
        public Swatch(Color c) { Color = c; Cursor = Cursors.Hand; }
        protected override void OnClick(EventArgs e) { if (Picked != null) Picked(this, e); base.OnClick(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(Sc(2), Sc(2), Width - Sc(5), Height - Sc(5));
            if (Selected) using (var b = new SolidBrush(Color.FromArgb(70, Color))) using (var gp = Fx.Round(new Rectangle(0, 0, Width - 1, Height - 1), Sc(11))) g.FillPath(b, gp);
            using (var path = Fx.Round(r, Sc(9)))
            {
                using (var b = new SolidBrush(Color)) g.FillPath(b, path);
                using (var p = new Pen(Selected ? Color.White : Fx.Darken(Color, 0.7f), Selected ? Scf(2.2f) : S)) g.DrawPath(p, path);
            }
        }
    }

    class CpsGraph : DpiControl
    {
        readonly Queue<float> _data = new Queue<float>();
        const int Cap = 150;
        public int Target = 14;
        public void Push(float v) { _data.Enqueue(v); while (_data.Count > Cap) _data.Dequeue(); Invalidate(); }
        public void Reset() { _data.Clear(); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            int W = Width, H = Height;
            if (W <= 2 || H <= 2) return;
            float top = Math.Max(Target * 1.4f, 10f);
            using (var gp = new Pen(Color.FromArgb(28, 28, 40), S)) for (int i = 1; i < 4; i++) { int yy = H * i / 4; g.DrawLine(gp, 0, yy, W, yy); }
            int ty = (int)(H - (Target / top) * H);
            using (var p = new Pen(Color.FromArgb(110, Theme.AccentSoft), S) { DashStyle = DashStyle.Dash }) g.DrawLine(p, 0, ty, W, ty);
            using (var f = Fx.F(8f, FontStyle.Bold)) TextRenderer.DrawText(g, "TARGET " + Target, f, new Point(Sc(8), Math.Max(Sc(2), ty - Sc(16))), Color.FromArgb(150, Theme.AccentSoft), TextFormatFlags.NoPadding);
            if (_data.Count < 2) return;
            var arr = _data.ToArray();
            float dx = (float)W / (Cap - 1);
            var line = new PointF[arr.Length];
            int start = Cap - arr.Length;
            for (int i = 0; i < arr.Length; i++) { float val = Math.Min(arr[i], top); line[i] = new PointF((start + i) * dx, H - (val / top) * H); }
            var area = new PointF[line.Length + 2];
            Array.Copy(line, area, line.Length);
            area[line.Length] = new PointF(line[line.Length - 1].X, H);
            area[line.Length + 1] = new PointF(line[0].X, H);
            using (var lg = new LinearGradientBrush(new Rectangle(0, 0, W, H), Color.FromArgb(72, Theme.Accent), Color.FromArgb(0, Theme.Accent), LinearGradientMode.Vertical)) g.FillPolygon(lg, area);
            using (var p = new Pen(Theme.Accent, Scf(2.2f))) { p.LineJoin = LineJoin.Round; g.DrawLines(p, line); }
            var lp = line[line.Length - 1];
            using (var b = new SolidBrush(Color.FromArgb(60, Theme.AccentSoft))) g.FillEllipse(b, lp.X - Scf(6), lp.Y - Scf(6), Scf(12), Scf(12));
            using (var b = new SolidBrush(Theme.AccentSoft)) g.FillEllipse(b, lp.X - Scf(3.5f), lp.Y - Scf(3.5f), Scf(7), Scf(7));
        }
    }

    class HeroCard : DpiControl
    {
        public int Target = 14;
        public float DisplayActual = 0f;
        public bool Armed = false, Active = false, Waiting = false, Hold = false;
        public float Pulse = 0f;
        public string BindName = "MOUSE 5";
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Fx.Round(r, Sc(14)))
            {
                using (var lg = new LinearGradientBrush(r, Theme.PanelHi, Theme.Panel, LinearGradientMode.Vertical)) g.FillPath(lg, path);
                using (var p = new Pen(Theme.Border, S)) g.DrawPath(p, path);
            }
            int pad = Sc(24), split = (int)(Width * 0.52f);
            using (var f = Fx.F(9f, FontStyle.Bold)) TextRenderer.DrawText(g, "TARGET RATE", f, new Point(pad, Sc(22)), Theme.TextDim, TextFormatFlags.NoPadding);
            string tnum = Target.ToString();
            using (var big = Fx.F(50f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, tnum, big, new Point(pad - Sc(3), Sc(40)), Theme.Text, TextFormatFlags.NoPadding);
                Size ns = TextRenderer.MeasureText(g, tnum, big, Size.Empty, TextFormatFlags.NoPadding);
                using (var unit = Fx.F(14f, FontStyle.Bold)) TextRenderer.DrawText(g, "CPS", unit, new Point(pad + ns.Width, Sc(40) + ns.Height - Sc(36)), Theme.AccentSoft, TextFormatFlags.NoPadding);
            }
            double ms = 1000.0 / Math.Max(1, Target);
            using (var f = Fx.F(10.5f)) TextRenderer.DrawText(g, ms.ToString("0.0") + " ms between clicks", f, new Point(pad, Height - Sc(46)), Theme.TextDim, TextFormatFlags.NoPadding);

            using (var p = new Pen(Theme.Border, S)) g.DrawLine(p, split, Sc(30), split, Height - Sc(30));
            int rx = split + pad;
            using (var f = Fx.F(9f, FontStyle.Bold)) TextRenderer.DrawText(g, "LIVE OUTPUT", f, new Point(rx, Sc(22)), Theme.TextDim, TextFormatFlags.NoPadding);
            string anum = Active && !Hold ? DisplayActual.ToString("0.0") : (Hold ? "—" : "0.0");
            Color aCol = Active ? Theme.Green : Theme.TextFaint;
            using (var big = Fx.F(34f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, anum, big, new Point(rx - Sc(2), Sc(44)), aCol, TextFormatFlags.NoPadding);
                Size ns = TextRenderer.MeasureText(g, anum, big, Size.Empty, TextFormatFlags.NoPadding);
                using (var unit = Fx.F(11f, FontStyle.Bold)) TextRenderer.DrawText(g, Hold ? "" : "CPS", unit, new Point(rx + ns.Width, Sc(44) + ns.Height - Sc(24)), Theme.TextDim, TextFormatFlags.NoPadding);
            }
            Color dot; string status;
            if (!Armed) { dot = Theme.TextFaint; status = "DISABLED"; }
            else if (Waiting) { dot = Theme.Amber; status = "WAITING FOR ROBLOX"; }
            else if (Active && Hold) { dot = Theme.Green; status = "HOLDING"; }
            else if (Active) { dot = Theme.Green; status = "CLICKING"; }
            else { dot = Theme.Amber; status = "ARMED  •  " + BindName; }
            int dyc = Height - Sc(44);
            float cx = rx + Sc(7), cy = dyc + Sc(7);
            float glowR = Active ? Sc(8) + Pulse * Sc(5) : Sc(8);
            int glowA = Active ? (int)(40 + Pulse * 70) : 60;
            using (var b = new SolidBrush(Color.FromArgb(glowA, dot))) g.FillEllipse(b, cx - glowR, cy - glowR, glowR * 2, glowR * 2);
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, cx - Sc(4), cy - Sc(4), Sc(8), Sc(8));
            using (var f = Fx.F(9.5f, FontStyle.Bold)) TextRenderer.DrawText(g, status, f, new Point(rx + Sc(22), dyc + Sc(1)), Theme.Text, TextFormatFlags.NoPadding);
        }
    }

    class TitleBar : DpiControl
    {
        readonly Form _form;
        Rectangle _min, _close; bool _hMin, _hClose;
        public TitleBar(Form f) { _form = f; }
        void Relayout() { int s = Sc(30), y = (Height - s) / 2; _close = new Rectangle(Width - s - Sc(12), y, s, s); _min = new Rectangle(Width - 2 * s - Sc(18), y, s, s); }
        protected override void OnSizeChanged(EventArgs e) { Relayout(); base.OnSizeChanged(e); Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e) { bool hm = _min.Contains(e.Location), hc = _close.Contains(e.Location); if (hm != _hMin || hc != _hClose) { _hMin = hm; _hClose = hc; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { _hMin = _hClose = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_close.Contains(e.Location)) { _form.Close(); return; }
            if (_min.Contains(e.Location)) { _form.WindowState = FormWindowState.Minimized; return; }
            Native.ReleaseCapture(); Native.SendMessage(_form.Handle, Native.WM_NCLBUTTONDOWN, Native.HT_CAPTION, 0);
            base.OnMouseDown(e);
        }
        void Glyph(Graphics g, Rectangle r, string s, bool hover, bool danger)
        {
            if (hover) using (var path = Fx.Round(r, Sc(7))) using (var b = new SolidBrush(danger ? Theme.Red : Theme.PanelHi)) g.FillPath(b, path);
            using (var f = Fx.F(11f, FontStyle.Regular)) TextRenderer.DrawText(g, s, f, r, hover ? Color.White : Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (_close.IsEmpty) Relayout();
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, ClientRectangle);
            int cy = Height / 2;
            var dia = new Point[] { new Point(Sc(20), cy), new Point(Sc(28), cy - Sc(8)), new Point(Sc(36), cy), new Point(Sc(28), cy + Sc(8)) };
            using (var lg = new LinearGradientBrush(new Rectangle(Sc(18), cy - Sc(9), Sc(20), Sc(18)), Theme.Accent, Theme.AccentSoft, 45f)) g.FillPolygon(lg, dia);
            using (var fb = Fx.F(13f, FontStyle.Bold))
            {
                int tx = Sc(46), tyy = cy - Sc(10);
                TextRenderer.DrawText(g, "VOID", fb, new Point(tx, tyy), Theme.Text, TextFormatFlags.NoPadding);
                Size vw = TextRenderer.MeasureText(g, "VOID", fb, Size.Empty, TextFormatFlags.NoPadding);
                using (var fl = Fx.F(13f, FontStyle.Regular)) TextRenderer.DrawText(g, " AUTOCLICKER", fl, new Point(tx + vw.Width, tyy), Theme.TextDim, TextFormatFlags.NoPadding);
            }
            using (var p = new Pen(Theme.Border, S)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
            using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, Height - Sc(2), Sc(120), Sc(2));
            Glyph(g, _min, "—", _hMin, false);
            Glyph(g, _close, "✕", _hClose, true);
        }
    }

    class OverlayForm : Form
    {
        float _s = 1f; int Sc(int v) { return (int)Math.Round(v * _s); }
        public float Cps; public bool Active, Waiting, Armed, Hold; public float Pulse;
        public Action Moved;
        public OverlayForm() { FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; BackColor = Theme.Bg; DoubleBuffered = true; }
        protected override void OnLoad(EventArgs e) { base.OnLoad(e); _s = DeviceDpi / 96f; ClientSize = new Size(Sc(170), Sc(62)); }
        protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); _s = DeviceDpi / 96f; ClientSize = new Size(Sc(170), Sc(62)); Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { Native.ReleaseCapture(); Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, Native.HT_CAPTION, 0); base.OnMouseDown(e); }
        protected override void WndProc(ref Message m) { base.WndProc(ref m); if (m.Msg == Native.WM_EXITSIZEMOVE && Moved != null) Moved(); }
        public void UpdateState(float cps, bool active, bool waiting, bool armed, bool hold, float pulse) { Cps = cps; Active = active; Waiting = waiting; Armed = armed; Hold = hold; Pulse = pulse; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = Fx.Round(r, Sc(14)))
            {
                using (var lg = new LinearGradientBrush(r, Theme.PanelHi, Theme.Bg, LinearGradientMode.Vertical)) g.FillPath(lg, path);
                using (var p = new Pen(Theme.Border, _s)) g.DrawPath(p, path);
            }
            using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, Sc(2), Sc(8), Sc(3), Height - Sc(16));
            Color dot; string label;
            if (!Armed) { dot = Theme.TextFaint; label = "OFF"; }
            else if (Waiting) { dot = Theme.Amber; label = "WAIT"; }
            else if (Active && Hold) { dot = Theme.Green; label = "HOLD"; }
            else if (Active) { dot = Theme.Green; label = "LIVE"; }
            else { dot = Theme.Amber; label = "ARMED"; }
            float cx = Sc(22), cy = Height / 2f;
            float glowR = Active ? Sc(7) + Pulse * Sc(4) : Sc(7);
            int glowA = Active ? (int)(50 + Pulse * 80) : 70;
            using (var b = new SolidBrush(Color.FromArgb(glowA, dot))) g.FillEllipse(b, cx - glowR, cy - glowR, glowR * 2, glowR * 2);
            using (var b = new SolidBrush(dot)) g.FillEllipse(b, cx - Sc(4), cy - Sc(4), Sc(8), Sc(8));
            string num = Active && !Hold ? Cps.ToString("0.0") : (Hold ? "—" : "0.0");
            using (var big = Fx.F(22f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, num, big, new Point(Sc(38), Sc(8)), Active ? Theme.Text : Theme.TextDim, TextFormatFlags.NoPadding);
                Size ns = TextRenderer.MeasureText(g, num, big, Size.Empty, TextFormatFlags.NoPadding);
                using (var u = Fx.F(9f, FontStyle.Bold)) TextRenderer.DrawText(g, Hold ? "" : "CPS", u, new Point(Sc(40) + ns.Width, Sc(24)), Theme.AccentSoft, TextFormatFlags.NoPadding);
            }
            using (var f = Fx.F(7.5f, FontStyle.Bold)) TextRenderer.DrawText(g, label, f, new Point(Sc(40), Height - Sc(18)), Theme.TextDim, TextFormatFlags.NoPadding);
            using (var f = Fx.F(7.5f, FontStyle.Bold)) TextRenderer.DrawText(g, "VOID", f, new Rectangle(0, Height - Sc(18), Width - Sc(10), Sc(14)), Color.FromArgb(120, Theme.AccentSoft), TextFormatFlags.Right);
        }
    }

    class SplashForm : Form
    {
        float _s = 1f; int Sc(int v) { return (int)Math.Round(v * _s); }
        float _progress = 0, _phase = 0; string _status = "Starting…";
        public SplashForm() { FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual; BackColor = Color.FromArgb(7, 7, 11); DoubleBuffered = true; }
        protected override void OnLoad(EventArgs e) { base.OnLoad(e); _s = DeviceDpi / 96f; ClientSize = new Size(Sc(460), Sc(320)); var wa = Screen.PrimaryScreen.WorkingArea; Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2); }
        public void SetProgress(float p) { _progress = p; Invalidate(); }
        public void SetStatus(string s) { if (_status != s) { _status = s; Invalidate(); } }
        public void Pulse(float ph) { _phase = ph; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int W = Width, H = Height;
            var rr = new Rectangle(0, 0, W - 1, H - 1);
            using (var path = Fx.Round(rr, Sc(18)))
            {
                using (var lg = new LinearGradientBrush(rr, Color.FromArgb(26, 19, 48), Color.FromArgb(7, 7, 11), 90f)) g.FillPath(lg, path);
                using (var p = new Pen(Theme.Border, _s)) g.DrawPath(p, path);
            }
            using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, 0, Sc(120), Sc(2));
            float cx = W / 2f, cy = Sc(118);
            float pulse = (float)((Math.Sin(_phase * 3) + 1) / 2);
            float glowR = Sc(34) + pulse * Sc(10);
            using (var gp = new GraphicsPath()) { gp.AddEllipse(cx - glowR, cy - glowR, glowR * 2, glowR * 2); using (var pgb = new PathGradientBrush(gp) { CenterColor = Color.FromArgb((int)(90 + pulse * 70), Theme.Accent), SurroundColors = new[] { Color.FromArgb(0, Theme.Accent) } }) g.FillPath(pgb, gp); }
            float hd = Sc(26);
            var pts = new[] { new PointF(cx, cy - hd), new PointF(cx + hd, cy), new PointF(cx, cy + hd), new PointF(cx - hd, cy) };
            using (var dpath = new GraphicsPath())
            {
                dpath.AddPolygon(pts);
                using (var dg = new LinearGradientBrush(new RectangleF(cx - hd, cy - hd, hd * 2, hd * 2), Theme.Accent, Theme.AccentSoft, 45f)) g.FillPath(dg, dpath);
                var oc = g.Clip; g.SetClip(dpath);
                using (var hb = new SolidBrush(Color.FromArgb(55, 255, 255, 255))) g.FillPolygon(hb, new[] { new PointF(cx, cy - hd), new PointF(cx, cy), new PointF(cx - hd, cy) });
                g.Clip = oc;
            }
            using (var fb = Fx.F(20f, FontStyle.Bold))
            {
                string a = "VOID ", b2 = "AUTOCLICKER";
                Size sa = TextRenderer.MeasureText(g, a, fb, Size.Empty, TextFormatFlags.NoPadding);
                Size sb = TextRenderer.MeasureText(g, b2, fb, Size.Empty, TextFormatFlags.NoPadding);
                int tx = (W - (sa.Width + sb.Width)) / 2, tyy = Sc(178);
                TextRenderer.DrawText(g, a, fb, new Point(tx, tyy), Theme.Text, TextFormatFlags.NoPadding);
                using (var fl = Fx.F(20f, FontStyle.Regular)) TextRenderer.DrawText(g, b2, fl, new Point(tx + sa.Width, tyy), Theme.TextDim, TextFormatFlags.NoPadding);
            }
            using (var f = Fx.F(10f)) TextRenderer.DrawText(g, "Precision auto clicker for Roblox", f, new Rectangle(0, Sc(210), W, Sc(16)), Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            int pw = Sc(240), px = (W - pw) / 2, py = Sc(248);
            using (var b = new SolidBrush(Theme.PanelHi)) using (var tp = Fx.Round(new Rectangle(px, py, pw, Sc(4)), Sc(2))) g.FillPath(b, tp);
            int fwd = (int)(pw * Math.Max(0, Math.Min(1, _progress)));
            if (fwd > 0) using (var lg = new LinearGradientBrush(new Rectangle(px, py, Math.Max(1, fwd), Sc(4)), Theme.Accent, Theme.AccentSoft, 0f)) using (var fp = Fx.Round(new Rectangle(px, py, Math.Max(Sc(4), fwd), Sc(4)), Sc(2))) g.FillPath(lg, fp);
            using (var f = Fx.F(9f)) TextRenderer.DrawText(g, _status, f, new Rectangle(0, Sc(262), W, Sc(14)), Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            using (var f = Fx.F(9f)) { TextRenderer.DrawText(g, "v" + App.Version, f, new Point(Sc(16), H - Sc(24)), Theme.TextFaint, TextFormatFlags.NoPadding); TextRenderer.DrawText(g, "made by Ethan", f, new Rectangle(0, H - Sc(24), W - Sc(16), Sc(14)), Theme.TextFaint, TextFormatFlags.Right | TextFormatFlags.NoPadding); }
        }
    }

    // Small themed text-input dialog (for naming profiles).
    // Shown when a newer release exists on GitHub.
    class UpdateForm : Form
    {
        public bool Skip;
        public UpdateForm(string current, string latest, string notes)
        {
            FormBorderStyle = FormBorderStyle.FixedDialog; ControlBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Panel; ForeColor = Theme.Text; AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(380, 224); Text = "Update available";

            Controls.Add(new Label { Text = "Update available", AutoSize = false, Bounds = new Rectangle(20, 18, 340, 24), ForeColor = Theme.Text, Font = Fx.F(12f, FontStyle.Bold), BackColor = Color.Transparent });
            Controls.Add(new Label { Text = "v" + current + "   \u2192   v" + latest, AutoSize = false, Bounds = new Rectangle(20, 44, 340, 20), ForeColor = Theme.AccentSoft, Font = Fx.F(10f, FontStyle.Bold), BackColor = Color.Transparent });

            var box = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(notes) ? "No release notes." : notes.Replace("\n", "\r\n"),
                Bounds = new Rectangle(20, 72, 340, 88), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = Theme.Bg, ForeColor = Theme.TextDim, BorderStyle = BorderStyle.FixedSingle, Font = Fx.F(9f), TabStop = false
            };
            Controls.Add(box);

            var dl = new Button { Text = "Download", Bounds = new Rectangle(20, 172, 110, 34), DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Color.White, Font = Fx.F(9.5f, FontStyle.Bold) };
            var later = new Button { Text = "Later", Bounds = new Rectangle(140, 172, 100, 34), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Theme.PanelHi, ForeColor = Theme.Text, Font = Fx.F(9.5f) };
            var skip = new Button { Text = "Skip this version", Bounds = new Rectangle(250, 172, 110, 34), FlatStyle = FlatStyle.Flat, BackColor = Theme.PanelHi, ForeColor = Theme.TextDim, Font = Fx.F(8.5f) };
            dl.FlatAppearance.BorderSize = 0; later.FlatAppearance.BorderSize = 0; skip.FlatAppearance.BorderSize = 0;
            skip.Click += (s, e) => { Skip = true; DialogResult = DialogResult.Ignore; };
            AcceptButton = dl; CancelButton = later;
            Controls.AddRange(new Control[] { dl, later, skip });
        }
    }

    class PromptForm : Form
    {
        public string Value = "";
        public PromptForm(string prompt, string initial)
        {
            FormBorderStyle = FormBorderStyle.FixedDialog; ControlBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Panel; ForeColor = Theme.Text; AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96f, 96f);
            ClientSize = new Size(320, 144);
            var lbl = new Label { Text = prompt, AutoSize = false, Bounds = new Rectangle(16, 16, 288, 20), ForeColor = Theme.TextDim, Font = Fx.F(9.5f, FontStyle.Bold), BackColor = Color.Transparent };
            var tb = new TextBox { Text = initial, Bounds = new Rectangle(16, 44, 288, 26), BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Fx.F(11f) };
            var ok = new Button { Text = "OK", Bounds = new Rectangle(150, 90, 72, 34), DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent, ForeColor = Color.White, Font = Fx.F(9.5f, FontStyle.Bold) };
            var cancel = new Button { Text = "Cancel", Bounds = new Rectangle(232, 90, 72, 34), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Theme.PanelHi, ForeColor = Theme.Text, Font = Fx.F(9.5f) };
            ok.FlatAppearance.BorderSize = 0; cancel.FlatAppearance.BorderSize = 0;
            AcceptButton = ok; CancelButton = cancel;
            ok.Click += (s, e) => Value = tb.Text.Trim();
            Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
            Shown += (s, e) => tb.Focus();
        }
    }

    // Animated XP progress bar with accent gradient.
    class XpBar : DpiControl
    {
        float _p, _cur;
        public float Progress { get { return _p; } set { float v = Math.Max(0f, Math.Min(1f, value)); if (Math.Abs(v - _p) > 0.0001f) { _p = v; StartAnim(); Invalidate(); } } }
        protected override bool AnimStep() { return Ease(ref _cur, _p, 0.16f); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            int rad = Height / 2;
            using (var path = Fx.Round(r, rad))
            {
                using (var b = new SolidBrush(Theme.Bg)) g.FillPath(b, path);
                using (var p = new Pen(Theme.Border, S)) g.DrawPath(p, path);
            }
            int w = (int)Math.Round(_cur * (Width - 2));
            if (w > rad)
            {
                var fr = new Rectangle(1, 1, w, Height - 3);
                using (var path = Fx.Round(fr, (Height - 3) / 2))
                using (var b = new LinearGradientBrush(new Rectangle(0, 0, Math.Max(2, Width), Height), Fx.Darken(Theme.Accent, 0.75f), Theme.AccentSoft, LinearGradientMode.Horizontal))
                    g.FillPath(b, path);
            }
        }
    }

    // Vertical icon sidebar that replaces the tab strip.
    class IconRail : DpiControl
    {
        public string[] Names = { "Click", "Target", "Boost", "Stats", "Trim", "Style", "Settings" };
        public int BottomCount = 2;
        int _sel = 0, _hover = -1; float _hlY = -1f;
        Rectangle[] _rects;
        readonly ToolTip _tip = new ToolTip { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 4000 };
        public event EventHandler<int> Picked;
        public int Selected { get { return _sel; } set { if (_sel != value) { _sel = value; StartAnim(); Invalidate(); } } }

        int Box { get { return Sc(40); } }
        protected override bool AnimStep() { if (_rects == null) Relayout(); return Ease(ref _hlY, _rects[_sel].Y, 0.3f); }
        void Relayout()
        {
            int n = Names.Length, box = Box, x = (Width - box) / 2;
            _rects = new Rectangle[n];
            int topN = n - BottomCount, gap = Sc(6), y = Sc(12);
            for (int i = 0; i < topN; i++) { _rects[i] = new Rectangle(x, y, box, box); y += box + gap; }
            int by = Height - Sc(12) - box;
            for (int i = n - 1; i >= topN; i--) { _rects[i] = new Rectangle(x, by, box, box); by -= box + gap; }
        }
        protected override void OnSizeChanged(EventArgs e) { Relayout(); base.OnSizeChanged(e); Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_rects == null) Relayout();
            int h = -1;
            for (int i = 0; i < _rects.Length; i++) if (_rects[i].Contains(e.Location)) { h = i; break; }
            if (h != _hover) { _hover = h; _tip.SetToolTip(this, h >= 0 ? Names[h] : ""); Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { _hover = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_rects == null) Relayout();
            for (int i = 0; i < _rects.Length; i++) if (_rects[i].Contains(e.Location)) { Selected = i; if (Picked != null) Picked(this, i); break; }
            base.OnMouseDown(e);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            if (_rects == null) Relayout();
            if (_hlY < 0) _hlY = _rects[_sel].Y;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Color.FromArgb(14, 14, 20))) g.FillRectangle(b, ClientRectangle);
            using (var p = new Pen(Theme.Border, S)) g.DrawLine(p, Width - 1, 0, Width - 1, Height);
            int box = Box, hx = (Width - box) / 2, hy = (int)Math.Round(_hlY);
            var hr = new Rectangle(hx, hy, box, box);
            using (var path = Fx.Round(hr, Sc(10))) using (var b = new SolidBrush(Theme.Accent)) g.FillPath(b, path);
            using (var b = new SolidBrush(Theme.AccentSoft)) g.FillRectangle(b, hr.X - Sc(10), hr.Y + Sc(9), Sc(3), hr.Height - Sc(18));
            for (int i = 0; i < _rects.Length; i++)
            {
                var r = _rects[i]; bool sel = i == _sel, hov = i == _hover;
                if (!sel && hov) { using (var path = Fx.Round(r, Sc(10))) using (var b = new SolidBrush(Theme.PanelHi)) g.FillPath(b, path); }
                DrawIcon(g, i, r, sel ? Color.White : (hov ? Theme.Text : Theme.TextDim));
            }
        }
        void DrawIcon(Graphics g, int idx, Rectangle box, Color c)
        {
            var r = Rectangle.Inflate(box, -Sc(11), -Sc(11));
            using (var pen = new Pen(c, Scf(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var br = new SolidBrush(c))
            {
                switch (idx)
                {
                    case 0:
                        g.FillPolygon(br, new[] {
                            new PointF(r.X+r.Width*0.16f, r.Y+r.Height*0.04f), new PointF(r.X+r.Width*0.16f, r.Y+r.Height*0.90f),
                            new PointF(r.X+r.Width*0.40f, r.Y+r.Height*0.66f), new PointF(r.X+r.Width*0.56f, r.Y+r.Height*0.99f),
                            new PointF(r.X+r.Width*0.72f, r.Y+r.Height*0.90f), new PointF(r.X+r.Width*0.54f, r.Y+r.Height*0.58f),
                            new PointF(r.X+r.Width*0.84f, r.Y+r.Height*0.50f) });
                        break;
                    case 1:
                        g.DrawEllipse(pen, r.X, r.Y, r.Width, r.Height);
                        { float in1 = r.Width*0.28f; g.DrawEllipse(pen, r.X+in1, r.Y+in1, r.Width-2*in1, r.Height-2*in1); }
                        { float d = r.Width*0.18f; g.FillEllipse(br, r.X+r.Width/2-d/2, r.Y+r.Height/2-d/2, d, d); }
                        break;
                    case 2:
                        g.FillPolygon(br, new[] {
                            new PointF(r.X+r.Width*0.56f, r.Y), new PointF(r.X+r.Width*0.16f, r.Y+r.Height*0.56f),
                            new PointF(r.X+r.Width*0.46f, r.Y+r.Height*0.56f), new PointF(r.X+r.Width*0.40f, r.Y+r.Height),
                            new PointF(r.X+r.Width*0.84f, r.Y+r.Height*0.40f), new PointF(r.X+r.Width*0.52f, r.Y+r.Height*0.40f) });
                        break;
                    case 3:
                        { float bw = r.Width*0.22f, gp = r.Width*0.17f, x0 = r.X + (r.Width-(3*bw+2*gp))/2f; float[] hs = { 0.45f, 0.72f, 1.0f };
                          for (int k = 0; k < 3; k++) { float bh = r.Height*hs[k], bx = x0 + k*(bw+gp); using (var p2 = Fx.Round(new Rectangle((int)bx,(int)(r.Bottom-bh),(int)Math.Max(2,bw),(int)bh), Sc(2))) g.FillPath(br, p2); } }
                        break;
                    case 4:
                        { // shield (armor trim)
                          float w = r.Width * 0.78f, x0 = r.X + (r.Width - w) / 2f, top = r.Y + r.Height * 0.04f, bot = r.Y + r.Height * 0.98f;
                          using (var path = new GraphicsPath()) {
                            path.AddLine(x0 + w / 2f, top, x0 + w, top + r.Height * 0.16f);
                            path.AddLine(x0 + w, top + r.Height * 0.16f, x0 + w, r.Y + r.Height * 0.52f);
                            path.AddBezier(x0 + w, r.Y + r.Height * 0.52f, x0 + w, r.Y + r.Height * 0.82f, x0 + w * 0.68f, bot - r.Height * 0.04f, x0 + w / 2f, bot);
                            path.AddBezier(x0 + w / 2f, bot, x0 + w * 0.32f, bot - r.Height * 0.04f, x0, r.Y + r.Height * 0.82f, x0, r.Y + r.Height * 0.52f);
                            path.AddLine(x0, r.Y + r.Height * 0.52f, x0, top + r.Height * 0.16f);
                            path.CloseFigure(); g.FillPath(br, path); } }
                        break;
                    case 5:
                        { float cx = r.X+r.Width/2f; using (var path = new GraphicsPath()) {
                            path.AddLine(cx, r.Y, r.X+r.Width*0.86f, r.Y+r.Height*0.60f);
                            path.AddArc(r.X+r.Width*0.14f, r.Y+r.Height*0.38f, r.Width*0.72f, r.Height*0.62f, 0, 180);
                            path.CloseFigure(); g.FillPath(br, path); } }
                        break;
                    case 6:
                        for (int k = 0; k < 3; k++) { float yy = r.Y+r.Height*(0.16f+k*0.34f); g.DrawLine(pen, r.X, yy, r.Right, yy);
                            float kx = r.X+r.Width*(k == 1 ? 0.32f : 0.66f); g.FillEllipse(br, kx-Sc(3), yy-Sc(3), Sc(6), Sc(6)); }
                        break;
                }
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    //  MAIN FORM
    // ────────────────────────────────────────────────────────────────────────
    enum Mode { Hold, Toggle }
    enum Btn { Left, Right }

    class MainForm : Form
    {
        float _s = 1f;
        int Sc(int v) { return (int)Math.Round(v * _s); }

        // engine state
        volatile bool _engineRunning, _armed, _active, _humanize, _randomize, _robloxOnly, _gateWait, _holding;
        volatile int _targetCps = 14, _rangePlus = 2, _bindVk = 0x06, _pendingBind, _panicVk = 0x77;
        volatile int _action = 0, _output = 0, _keyVk = 0x46;     // action: click/hold, output: mouse/keyboard
        volatile bool _useFixedPos = false, _returnCursor = false;
        volatile int _posX = 0, _posY = 0;
        volatile Mode _mode = Mode.Hold;
        volatile Btn _button = Btn.Left;
        volatile float _actualCps;
        bool _binding, _bindArmed; int _bindTarget = 0;           // 0 trigger, 1 panic, 2 keyboard key
        volatile bool _capturingPos; volatile bool _posCaptured; volatile int _capX, _capY;
        Thread _engine; Stopwatch _hrt;
        const double Jitter = 2.5;

        IntPtr _fgH; bool _fgRoblox;
        long _clicks; DateTime _sessionStart = DateTime.Now; float _peakCps;

        Settings _set = new Settings();
        bool _loading = true;

        float _animActual, _pulsePhase;
        Color _startFillCur = Theme.Accent;

        NotifyIcon _tray; ToolStripMenuItem _trayToggle;
        Icon _winIcon, _trayIcon; IntPtr _winHicon = IntPtr.Zero, _trayHicon = IntPtr.Zero;
        System.Windows.Forms.Timer _ui, _anim, _splashTimer;
        OverlayForm _overlay; SplashForm _splash; double _splashT;

        HeroCard _hero; CpsGraph _graph; Slider _cps, _range; GButton _start, _bindBtn, _tuneBtn, _keyBtn, _panicBtn, _posBtn, _profBtn;
        Label _cpsVal, _rangeVal, _sysCpu, _sysTimer, _tuneResult, _stClicks, _stTime, _stPeak, _stAvg, _posLabel;
        Segment _mouseSeg; Label _mouseLbl, _keyLbl;
        Panel _panClick, _panTarget, _panBoost, _panStats, _panStyle, _panSettings, _panTrim; IconRail _rail; int _curTab;
        double _uiFactor = 1.0;
        SoundPlayer _sndArm, _sndDisarm; bool _prevArmedSound;
        Panel _animPanel; float _panelEase; int _panelTargetTop, _phy;
        readonly List<Swatch> _swatches = new List<Swatch>();

        // boost / performance
        Label _bCpu, _bRam, _bRoblox, _bTimer, _boostStatus; GButton _bElevate;
        bool _boostPriorityOn, _boostPowerOn; string _savedScheme;
        ulong _pIdle, _pKern, _pUsr; double _lastCpu;
        const string HighPerfGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        // auto-optimize + fps cap
        bool _autoOptimize, _powerActive, _fpsCapEnabled, _fpsUncapped, _fpsApplied, _robloxWasRunning, _suppressBoostToggle;
        int _fpsCap = 240;
        Label _fpsSuggest, _fpsValLbl; Toggle _autoTog; GButton _fpsApply, _fpsRestore, _fpsMinus, _fpsPlus;
        // trim tracker
        const int TrimMax = 396900;
        XpBar _trimBar, _trimWinBar;
        Label _trimWinPct;
        Label _trimNow, _trimPct, _trimAvg, _trimEst, _trimWinNum, _trimWinLeft;
        Label _heroWins, _heroXpLeft;
        TextBox _trimXpIn, _trimSetIn, _trimGoalIn;

        static string SettingsPath { get { string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoidAutoClicker"); try { Directory.CreateDirectory(dir); } catch { } return Path.Combine(dir, "settings.json"); } }

        public MainForm()
        {
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual; AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg; Text = "Void AutoClicker"; DoubleBuffered = true; ClientSize = new Size(420, 300); ShowInTaskbar = true;
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _loading = true;
            LoadSettings(); SetupTray(); ApplyAccentIcons();
            _sndArm = MakeTone(880, 70, 0.30); _sndDisarm = MakeTone(440, 95, 0.26); _prevArmedSound = _armed;
            RecomputeScale();
            ClientSize = new Size(Sc(980), Sc(700));
            _startFillCur = _armed ? Theme.Red : Theme.Accent;
            if (_set.ShowSplash) Opacity = 0;
            BuildUi(); CenterToScreen(); StartTimers();
            TopMost = _set.AlwaysOnTop;
            Native.timeBeginPeriod(1); UpdateTimerLabel();
            _hrt = Stopwatch.StartNew();
            _engineRunning = true;
            _engine = new Thread(EngineLoop) { IsBackground = true, Priority = ThreadPriority.Highest };
            _engine.Start();
            _loading = false;
            if (_set.ShowSplash) RunSplash();     // splash calls CheckForUpdates when it finishes
            else { if (_set.ShowOverlay) SetOverlay(true); CheckForUpdates(false); }
        }

        void RunSplash()
        {
            _splash = new SplashForm(); _splash.Show(); _splash.BringToFront();
            _splashT = 0;
            _splashTimer = new System.Windows.Forms.Timer { Interval = 30 };
            _splashTimer.Tick += (s, e) => SplashTick();
            _splashTimer.Start();
        }
        void SplashTick()
        {
            _splashT += 0.030; const double total = 2.4;
            if (_splash != null && !_splash.IsDisposed)
            {
                _splash.SetProgress((float)Math.Min(1, _splashT / (total - 0.35)));
                _splash.Pulse((float)_splashT);
                string st = _splashT < 0.5 ? "Starting…" : _splashT < 1.0 ? "Setting 1 ms timer resolution…" : _splashT < 1.5 ? "Starting click engine…" : _splashT < 1.95 ? "Loading your settings…" : "Ready";
                _splash.SetStatus(st);
            }
            if (_splashT >= total)
            {
                if (Opacity < 1) Opacity = Math.Min(1, Opacity + 0.12);
                if (_splash != null && !_splash.IsDisposed) _splash.Opacity = Math.Max(0, _splash.Opacity - 0.14);
                if (Opacity >= 1)
                {
                    _splashTimer.Stop(); _splashTimer.Dispose(); _splashTimer = null;
                    if (_splash != null && !_splash.IsDisposed) { _splash.Close(); _splash.Dispose(); _splash = null; }
                    Activate();
                    if (_set.ShowOverlay) SetOverlay(true);
                    CheckForUpdates(false);
                }
            }
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); _loading = true; RecomputeScale(); Rebuild(); _loading = false; }

        // ── UPDATE CHECK (GitHub Releases) ──
        // Compares dotted numeric versions: returns >0 if a is newer than b.
        static int CompareVersions(string a, string b)
        {
            Func<string, int[]> parse = v =>
            {
                v = (v ?? "").Trim();
                if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase)) v = v.Substring(1);
                var parts = v.Split('.');
                var nums = new int[3];
                for (int i = 0; i < 3 && i < parts.Length; i++)
                {
                    string digits = "";
                    foreach (char c in parts[i]) { if (char.IsDigit(c)) digits += c; else break; }
                    int n; nums[i] = int.TryParse(digits, out n) ? n : 0;
                }
                return nums;
            };
            var x = parse(a); var y = parse(b);
            for (int i = 0; i < 3; i++) { if (x[i] != y[i]) return x[i] - y[i]; }
            return 0;
        }

        // Minimal extraction so we don't depend on the JSON shape beyond two fields.
        static string JsonField(string json, string name)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    JsonElement el;
                    if (doc.RootElement.TryGetProperty(name, out el) && el.ValueKind == JsonValueKind.String)
                        return el.GetString();
                }
            }
            catch { }
            return null;
        }

        void CheckForUpdates(bool manual)
        {
            if (!manual && !_set.CheckUpdates) return;
            var t = new Thread(() =>
            {
                string tag = null, notes = null;
                try
                {
                    using (var http = new HttpClient())
                    {
                        http.Timeout = TimeSpan.FromSeconds(8);
                        // GitHub requires a User-Agent on API requests.
                        http.DefaultRequestHeaders.Add("User-Agent", App.Repo);
                        string json = http.GetStringAsync(App.LatestApi).GetAwaiter().GetResult();
                        tag = JsonField(json, "tag_name");
                        notes = JsonField(json, "body");
                    }
                }
                catch { tag = null; }   // offline / rate-limited / no releases yet: stay quiet

                if (IsDisposed || Disposing || !IsHandleCreated) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (string.IsNullOrEmpty(tag))
                        {
                            if (manual) MessageBox.Show(this, "Couldn't reach GitHub. Check your connection and try again.", "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                        if (CompareVersions(tag, App.Version) <= 0)
                        {
                            if (manual) MessageBox.Show(this, "You're on the latest version (v" + App.Version + ").", "Check for updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            return;
                        }
                        if (!manual && string.Equals(tag.TrimStart('v', 'V'), (_set.SkippedVersion ?? "").TrimStart('v', 'V'), StringComparison.OrdinalIgnoreCase)) return;

                        using (var f = new UpdateForm(App.Version, tag.TrimStart('v', 'V'), notes))
                        {
                            var r = f.ShowDialog(this);
                            if (f.Skip) { _set.SkippedVersion = tag; SaveSettings(); }
                            else if (r == DialogResult.OK) OpenUrl(App.ReleasesPage);
                        }
                    }));
                }
                catch { }
            });
            t.IsBackground = true; t.SetApartmentState(ApartmentState.STA); t.Start();
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
        }

        void RecomputeScale() { _s = (DeviceDpi / 96f) * (float)_uiFactor; }

        static SoundPlayer MakeTone(int freq, int ms, double amp)
        {
            try
            {
                int sr = 44100, n = sr * ms / 1000, fade = sr * 5 / 1000, dataLen = n * 2;
                var stream = new MemoryStream();
                var bw = new BinaryWriter(stream);
                Action<string> tag = t => { foreach (char ch in t) bw.Write((byte)ch); };
                tag("RIFF"); bw.Write(36 + dataLen); tag("WAVE"); tag("fmt "); bw.Write(16);
                bw.Write((short)1); bw.Write((short)1); bw.Write(sr); bw.Write(sr * 2); bw.Write((short)2); bw.Write((short)16);
                tag("data"); bw.Write(dataLen);
                for (int i = 0; i < n; i++)
                {
                    double env = 1.0;
                    if (i < fade) env = (double)i / fade; else if (i > n - fade) env = (double)(n - i) / fade;
                    double s = Math.Sin(2 * Math.PI * freq * i / sr) * amp * env;
                    bw.Write((short)(s * short.MaxValue));
                }
                bw.Flush(); stream.Position = 0;
                var sp = new SoundPlayer(stream); sp.Load(); return sp;
            }
            catch { return null; }
        }
        void ApplyScale(double f)
        {
            _uiFactor = Math.Max(0.7, Math.Min(1.5, f));
            _set.UiScale = _uiFactor;
            _loading = true; RecomputeScale(); Rebuild(); _loading = false;
            CenterToScreen(); SaveSettings();
        }

        void Rebuild()
        {
            SuspendLayout();
            for (int i = Controls.Count - 1; i >= 0; i--) Controls[i].Dispose();
            Controls.Clear(); _swatches.Clear();
            ClientSize = new Size(Sc(980), Sc(700));
            _startFillCur = _armed ? Theme.Red : Theme.Accent;
            BuildUi(); ResumeLayout(); SyncUiToState(); ShowTab(_curTab);
        }

        // ── ICON ──
        static Icon BuildIcon(Color accent, Color soft, int px, out IntPtr hicon)
        {
            var bmp = new Bitmap(px, px, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.Transparent);
                int r = (int)(px * 0.22f);
                var rect = new Rectangle(0, 0, px - 1, px - 1);
                using (var path = Fx.Round(rect, r)) using (var lg = new LinearGradientBrush(rect, Color.FromArgb(26, 26, 37), Color.FromArgb(9, 9, 14), 90f)) g.FillPath(lg, path);
                float gr = px * 0.34f, cx = px / 2f, cy = px / 2f;
                using (var gp = new GraphicsPath()) { gp.AddEllipse(cx - gr, cy - gr, gr * 2, gr * 2); using (var pgb = new PathGradientBrush(gp) { CenterColor = Color.FromArgb(150, accent), SurroundColors = new[] { Color.FromArgb(0, accent) } }) g.FillPath(pgb, gp); }
                float hd = px * 0.30f;
                var pts = new[] { new PointF(cx, cy - hd), new PointF(cx + hd, cy), new PointF(cx, cy + hd), new PointF(cx - hd, cy) };
                using (var dpath = new GraphicsPath())
                {
                    dpath.AddPolygon(pts);
                    using (var dg = new LinearGradientBrush(new RectangleF(cx - hd, cy - hd, hd * 2, hd * 2), accent, soft, 45f)) g.FillPath(dg, dpath);
                    var oc = g.Clip; g.SetClip(dpath);
                    using (var hb = new SolidBrush(Color.FromArgb(55, 255, 255, 255))) g.FillPolygon(hb, new[] { new PointF(cx, cy - hd), new PointF(cx, cy), new PointF(cx - hd, cy) });
                    using (var sb = new SolidBrush(Color.FromArgb(45, 0, 0, 0))) g.FillPolygon(sb, new[] { new PointF(cx, cy + hd), new PointF(cx, cy), new PointF(cx + hd, cy) });
                    g.Clip = oc;
                }
                float bw = Math.Max(1f, px * 0.012f);
                using (var bpath = Fx.Round(new Rectangle((int)bw, (int)bw, (int)(px - 1 - 2 * bw), (int)(px - 1 - 2 * bw)), (int)(r - bw))) using (var pen = new Pen(Color.FromArgb(58, 56, 84), bw)) g.DrawPath(pen, bpath);
            }
            hicon = bmp.GetHicon(); bmp.Dispose();
            return Icon.FromHandle(hicon);
        }
        void ApplyAccentIcons()
        {
            var nw = BuildIcon(Theme.Accent, Theme.AccentSoft, 256, out var h1);
            var nt = BuildIcon(Theme.Accent, Theme.AccentSoft, 32, out var h2);
            Icon = nw; if (_tray != null) _tray.Icon = nt;
            IntPtr oldW = _winHicon, oldT = _trayHicon; Icon ow = _winIcon, ot = _trayIcon;
            _winHicon = h1; _trayHicon = h2; _winIcon = nw; _trayIcon = nt;
            if (oldW != IntPtr.Zero) Native.DestroyIcon(oldW);
            if (oldT != IntPtr.Zero) Native.DestroyIcon(oldT);
            if (ow != null) ow.Dispose(); if (ot != null) ot.Dispose();
        }
        void SetupTray()
        {
            _tray = new NotifyIcon { Text = "Void AutoClicker", Icon = SystemIcons.Application, Visible = false };
            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Show", null, (s, e) => RestoreFromTray()));
            _trayToggle = new ToolStripMenuItem("Enable", null, (s, e) => SetArmed(!_armed));
            menu.Items.Add(_trayToggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) => Close()));
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += (s, e) => RestoreFromTray();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (WindowState == FormWindowState.Minimized && _set.MinimizeToTray && _tray != null) { Hide(); _tray.Visible = true; } }
        void RestoreFromTray() { Show(); WindowState = FormWindowState.Normal; Activate(); if (_tray != null) _tray.Visible = false; }

        // ── SETTINGS / PROFILES ──
        Profile Active { get { return _set.Profiles[_set.ActiveProfile]; } }
        void ApplyProfileToFields(Profile p)
        {
            _targetCps = Math.Max(1, Math.Min(40, p.Cps)); _rangePlus = Math.Max(0, Math.Min(8, p.RangePlus)); _randomize = p.Randomize;
            _action = p.Action; _output = p.Output; _button = (Btn)(p.MouseButton == 1 ? 1 : 0); _keyVk = p.KeyVk == 0 ? 0x46 : p.KeyVk;
            _mode = (Mode)(p.Mode == 1 ? 1 : 0); _bindVk = p.BindVk == 0 ? 0x06 : p.BindVk;
            _useFixedPos = p.UseFixedPos; _posX = p.PosX; _posY = p.PosY; _returnCursor = p.ReturnCursor;
        }
        void CaptureFieldsToProfile(Profile p)
        {
            p.Cps = _targetCps; p.RangePlus = _rangePlus; p.Randomize = _randomize;
            p.Action = _action; p.Output = _output; p.MouseButton = (int)_button; p.KeyVk = _keyVk;
            p.Mode = (int)_mode; p.BindVk = _bindVk; p.UseFixedPos = _useFixedPos; p.PosX = _posX; p.PosY = _posY; p.ReturnCursor = _returnCursor;
        }
        void LoadSettings()
        {
            try { if (File.Exists(SettingsPath)) { var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath)); if (s != null) _set = s; } } catch { }
            if (_set.Profiles == null) _set.Profiles = new List<Profile>();
            if (_set.Profiles.Count == 0)
            {
                _set.Profiles.Add(new Profile { Name = "Default" });
                _set.Profiles.Add(new Profile { Name = "Bedwars", Cps = 34, RangePlus = 1, Randomize = true, MouseButton = 0, Mode = 0, BindVk = 0x06 });
                _set.ActiveProfile = 1; // start on Bedwars
                _set.BedwarsSeeded = true;
            }
            else if (!_set.BedwarsSeeded && !_set.Profiles.Exists(p => p.Name == "Bedwars"))
            {
                _set.Profiles.Add(new Profile { Name = "Bedwars", Cps = 34, RangePlus = 1, Randomize = true, MouseButton = 0, Mode = 0, BindVk = 0x06 });
                _set.BedwarsSeeded = true;
            }
            if (_set.ActiveProfile < 0 || _set.ActiveProfile >= _set.Profiles.Count) _set.ActiveProfile = 0;
            ApplyProfileToFields(Active);
            _humanize = _set.Humanize; _robloxOnly = _set.RobloxOnly; _panicVk = _set.PanicVk == 0 ? 0x77 : _set.PanicVk;
            _uiFactor = _set.UiScale <= 0 ? 1.0 : Math.Max(0.7, Math.Min(1.5, _set.UiScale));
            _boostPriorityOn = _set.BoostPriority; _boostPowerOn = _set.BoostPower; _autoOptimize = _set.AutoOptimize;
            _fpsCapEnabled = _set.FpsCapEnabled; _fpsUncapped = _set.FpsUncapped; _fpsCap = _set.FpsCap < 30 ? 240 : Math.Min(1000, _set.FpsCap);
            if (_set.TrimGames == null) _set.TrimGames = new List<int>();
            if (_set.TrimGoal < 1) _set.TrimGoal = 10;
            if (_set.AccentArgb != 0) Theme.SetAccent(Color.FromArgb(_set.AccentArgb));
        }
        void SaveSettings()
        {
            if (_loading) return;
            CaptureFieldsToProfile(Active);
            _set.Humanize = _humanize; _set.RobloxOnly = _robloxOnly; _set.AlwaysOnTop = TopMost; _set.PanicVk = _panicVk; _set.AccentArgb = Theme.Accent.ToArgb();
            _set.AutoOptimize = _autoOptimize; _set.BoostPriority = _boostPriorityOn; _set.BoostPower = _boostPowerOn;
            _set.FpsCapEnabled = _fpsCapEnabled; _set.FpsCap = _fpsCap; _set.FpsUncapped = _fpsUncapped;
            if (_overlay != null && !_overlay.IsDisposed && _overlay.Visible) { _set.OverlayX = _overlay.Location.X; _set.OverlayY = _overlay.Location.Y; }
            try { File.WriteAllText(SettingsPath, JsonSerializer.Serialize(_set, new JsonSerializerOptions { WriteIndented = true })); } catch { }
        }
        void SwitchProfile(int i)
        {
            if (i < 0 || i >= _set.Profiles.Count) return;
            CaptureFieldsToProfile(Active);
            _set.ActiveProfile = i;
            ApplyProfileToFields(Active);
            _loading = true; Rebuild(); _loading = false;
            SaveSettings();
        }
        void NewProfile()
        {
            string name = AskName("Name your new profile", "Profile " + (_set.Profiles.Count + 1));
            if (string.IsNullOrEmpty(name)) return;
            CaptureFieldsToProfile(Active);
            var np = new Profile { Name = name };
            CaptureFieldsToProfile(np);
            _set.Profiles.Add(np); _set.ActiveProfile = _set.Profiles.Count - 1;
            _loading = true; Rebuild(); _loading = false; SaveSettings();
        }
        void RenameProfile()
        {
            string name = AskName("Rename profile", Active.Name);
            if (string.IsNullOrEmpty(name)) return;
            Active.Name = name; if (_profBtn != null) _profBtn.Text = "▾  " + name; SaveSettings();
        }
        void DeleteProfile()
        {
            if (_set.Profiles.Count <= 1) return;
            _set.Profiles.RemoveAt(_set.ActiveProfile);
            _set.ActiveProfile = 0; ApplyProfileToFields(Active);
            _loading = true; Rebuild(); _loading = false; SaveSettings();
        }
        string AskName(string title, string initial) { using (var p = new PromptForm(title, initial)) { return p.ShowDialog(this) == DialogResult.OK && p.Value.Length > 0 ? p.Value : null; } }
        void ShowProfileMenu(Control anchor)
        {
            var m = new ContextMenuStrip();
            for (int i = 0; i < _set.Profiles.Count; i++) { int idx = i; var it = new ToolStripMenuItem(_set.Profiles[i].Name) { Checked = (i == _set.ActiveProfile) }; it.Click += (s, e) => SwitchProfile(idx); m.Items.Add(it); }
            m.Show(anchor, new Point(0, anchor.Height));
        }

        // ── LAYOUT ──
        void BuildUi()
        {
            var title = new TitleBar(this) { Dock = DockStyle.Top, Height = Sc(46) };
            Controls.Add(title);
            int LX = Sc(16), LW = Sc(580);
            int RX = Sc(16 + 580 + 16), RW = Sc(352);
            int top = Sc(60);

            _hero = new HeroCard { Location = new Point(LX, top), Size = new Size(LW, Sc(196)) };
            Controls.Add(_hero);
            _start = new GButton { Location = new Point(LX, Sc(268)), Size = new Size(LW, Sc(56)), Text = "ENABLE CLICKER", FontSize = 11.5f, Fill = _startFillCur };
            _start.Click += (s, e) => SetArmed(!_armed);
            Controls.Add(_start);

            // profiles bar
            var pbar = new Card { Location = new Point(LX, Sc(336)), Size = new Size(LW, Sc(40)) };
            _profBtn = new GButton { Location = new Point(Sc(10), Sc(6)), Size = new Size(Sc(300), Sc(28)), Text = "▾  " + Active.Name, Fill = Theme.Bg, Fg = Theme.AccentSoft, Outline = true, FontSize = 9.5f };
            _profBtn.Click += (s, e) => ShowProfileMenu(_profBtn);
            pbar.Controls.Add(_profBtn);
            var pNew = new GButton { Location = new Point(Sc(318), Sc(6)), Size = new Size(Sc(74), Sc(28)), Text = "NEW", Fill = Theme.Accent, Fg = Color.White, FontSize = 9f };
            pNew.Click += (s, e) => NewProfile(); pbar.Controls.Add(pNew);
            var pRen = new GButton { Location = new Point(Sc(396), Sc(6)), Size = new Size(Sc(86), Sc(28)), Text = "RENAME", Fill = Theme.PanelHi, Fg = Theme.Text, Outline = true, FontSize = 9f };
            pRen.Click += (s, e) => RenameProfile(); pbar.Controls.Add(pRen);
            var pDel = new GButton { Location = new Point(Sc(486), Sc(6)), Size = new Size(Sc(84), Sc(28)), Text = "DELETE", Fill = Theme.PanelHi, Fg = Theme.Red, Outline = true, FontSize = 9f };
            pDel.Click += (s, e) => DeleteProfile(); pbar.Controls.Add(pDel);
            Controls.Add(pbar);

            var gcard = new Card { Caption = "Live Output", Location = new Point(LX, Sc(384)), Size = new Size(LW, Sc(300)) };
            _graph = new CpsGraph { Location = new Point(Sc(16), Sc(50)), Size = new Size(LW - Sc(32), Sc(300) - Sc(66)) };
            _graph.Target = _targetCps;
            gcard.Controls.Add(_graph); Controls.Add(gcard);

            // right column: icon sidebar + swapping panel
            int cardH = Sc(624), railW = Sc(54);
            var rightCard = new Card { Location = new Point(RX, top), Size = new Size(RW, cardH) };
            Controls.Add(rightCard);
            _rail = new IconRail { Location = new Point(Sc(1), Sc(1)), Size = new Size(railW, cardH - Sc(2)), Selected = _curTab };
            _rail.Picked += (s, i) => ShowTab(i);
            rightCard.Controls.Add(_rail);

            int phx = railW + Sc(8), phy = Sc(10), phw = RW - railW - Sc(16), phh = cardH - Sc(20);
            _phy = phy;
            _panClick = MakePanel(phx, phy, phw, phh); rightCard.Controls.Add(_panClick);
            _panTarget = MakePanel(phx, phy, phw, phh); rightCard.Controls.Add(_panTarget);
            _panBoost = MakePanel(phx, phy, phw, phh); rightCard.Controls.Add(_panBoost);
            _panStats = MakePanel(phx, phy, phw, phh); rightCard.Controls.Add(_panStats);
            _panTrim = MakePanel(phx, phy, phw, phh); rightCard.Controls.Add(_panTrim);
            _panStyle = MakePanel(phx, phy, phw, phh); rightCard.Controls.Add(_panStyle);
            _panSettings = MakePanel(phx, phy, phw, phh); rightCard.Controls.Add(_panSettings);
            BuildClickPanel(_panClick, phw);
            BuildTargetPanel(_panTarget, phw);
            BuildBoostPanel(_panBoost, phw);
            BuildStatsPanel(_panStats, phw);
            BuildTrimPanel(_panTrim, phw);
            BuildStylePanel(_panStyle, phw);
            BuildSettingsPanel(_panSettings, phw);

            ShowTab(_curTab); SyncUiToState();
        }

        Panel MakePanel(int x, int y, int w, int h) { return new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = Theme.Panel }; }

        void BuildClickPanel(Panel p, int pw)
        {
            int y = 16;
            p.Controls.Add(MiniLabel("CLICK RATE", 16, y));
            _cpsVal = RightLabel(_targetCps + " CPS", pw - Sc(104), Sc(y), 90, Theme.AccentSoft); p.Controls.Add(_cpsVal); y += 22;
            _cps = new Slider { Location = new Point(Sc(10), Sc(y)), Size = new Size(pw - Sc(20), Sc(38)), Value = _targetCps };
            _cps.ValueChanged += (s, e) => { ApplyCps(_cps.Value); SaveSettings(); };
            p.Controls.Add(_cps); y += 50;

            p.Controls.Add(MiniLabel("RANDOMIZE ± RANGE", 16, y + 4));
            var rtog = new Toggle { Location = new Point(pw - Sc(58), Sc(y)), Size = new Size(Sc(48), Sc(26)), On = _randomize };
            rtog.Toggled += (s, e) => { _randomize = rtog.On; if (_range != null) { _range.Enabled = _randomize; _range.Invalidate(); } UpdateRangeLabel(); SaveSettings(); };
            p.Controls.Add(rtog); y += 32;
            _rangeVal = RightLabel("± " + _rangePlus + " CPS", pw - Sc(104), Sc(y), 90, Theme.TextDim); p.Controls.Add(_rangeVal); y += 18;
            _range = new Slider { Min = 0, Max = 8, Location = new Point(Sc(10), Sc(y)), Size = new Size(pw - Sc(20), Sc(38)), Value = _rangePlus, Enabled = _randomize };
            _range.ValueChanged += (s, e) => { _rangePlus = _range.Value; UpdateRangeLabel(); SaveSettings(); };
            p.Controls.Add(_range); y += 52;

            p.Controls.Add(MiniLabel("ACTIVATION MODE", 16, y)); y += 20;
            var segMode = new Segment("HOLD", "TOGGLE") { Location = new Point(Sc(10), Sc(y)), Size = new Size(pw - Sc(20), Sc(34)), Selected = (_mode == Mode.Hold ? 0 : 1) };
            segMode.SelectedChanged += (s, e) => { _mode = segMode.Selected == 0 ? Mode.Hold : Mode.Toggle; SaveSettings(); };
            p.Controls.Add(segMode); y += 48;

            p.Controls.Add(MiniLabel("TRIGGER KEY", 16, y)); y += 20;
            _bindBtn = new GButton { Location = new Point(Sc(10), Sc(y)), Size = new Size(pw - Sc(20), Sc(40)), Text = VkPretty(_bindVk), Fill = Theme.Bg, Fg = Theme.AccentSoft, Outline = true, FontSize = 10f };
            _bindBtn.Click += (s, e) => BeginBind(0);
            p.Controls.Add(_bindBtn);
        }

        void BuildTargetPanel(Panel p, int pw)
        {
            int y = 16;
            p.Controls.Add(MiniLabel("ACTION", 16, y)); y += 20;
            var actSeg = new Segment("CLICK", "HOLD") { Location = new Point(Sc(10), Sc(y)), Size = new Size(pw - Sc(20), Sc(34)), Selected = _action };
            actSeg.SelectedChanged += (s, e) => { _action = actSeg.Selected; SaveSettings(); };
            p.Controls.Add(actSeg); y += 48;

            p.Controls.Add(MiniLabel("OUTPUT", 16, y)); y += 20;
            var outSeg = new Segment("MOUSE", "KEYBOARD") { Location = new Point(Sc(10), Sc(y)), Size = new Size(pw - Sc(20), Sc(34)), Selected = _output };
            outSeg.SelectedChanged += (s, e) => { _output = outSeg.Selected; UpdateTargetVisibility(); SaveSettings(); };
            p.Controls.Add(outSeg); y += 48;

            // mouse button (output = mouse)
            _mouseLbl = MiniLabel("MOUSE BUTTON", 16, y);
            p.Controls.Add(_mouseLbl);
            _keyLbl = MiniLabel("KEYBOARD KEY", 16, y);
            p.Controls.Add(_keyLbl);
            y += 20;
            _mouseSeg = new Segment("LEFT", "RIGHT") { Location = new Point(Sc(10), Sc(y)), Size = new Size(pw - Sc(20), Sc(34)), Selected = (_button == Btn.Left ? 0 : 1) };
            _mouseSeg.SelectedChanged += (s, e) => { _button = _mouseSeg.Selected == 0 ? Btn.Left : Btn.Right; SaveSettings(); };
            p.Controls.Add(_mouseSeg);
            _keyBtn = new GButton { Location = new Point(Sc(10), Sc(y)), Size = new Size(pw - Sc(20), Sc(34)), Text = VkPretty(_keyVk), Fill = Theme.Bg, Fg = Theme.AccentSoft, Outline = true, FontSize = 10f };
            _keyBtn.Click += (s, e) => BeginBind(2);
            p.Controls.Add(_keyBtn);
            y += 48;

            p.Controls.Add(MiniLabel("FIXED CLICK POSITION  (mouse only)", 16, y)); y += 22;
            p.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(16), Sc(y + 4)), Text = "USE FIXED POSITION", Font = Fx.F(9f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Color.Transparent });
            var posTog = new Toggle { Location = new Point(pw - Sc(58), Sc(y)), Size = new Size(Sc(48), Sc(26)), On = _useFixedPos };
            posTog.Toggled += (s, e) => { _useFixedPos = posTog.On; SaveSettings(); };
            p.Controls.Add(posTog); y += 36;
            _posBtn = new GButton { Location = new Point(Sc(10), Sc(y)), Size = new Size(Sc(150), Sc(32)), Text = "SET POSITION", Fill = Theme.PanelHi, Fg = Theme.Text, Outline = true, FontSize = 9f };
            _posBtn.Click += (s, e) => BeginPosCapture();
            p.Controls.Add(_posBtn);
            _posLabel = new Label { AutoSize = false, Size = new Size(Sc(106), Sc(32)), Location = new Point(Sc(166), Sc(y)), Text = (_posX == 0 && _posY == 0) ? "not set" : _posX + ", " + _posY, Font = Fx.F(10f, FontStyle.Bold), ForeColor = Theme.AccentSoft, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
            p.Controls.Add(_posLabel); y += 40;
            p.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(16), Sc(y + 4)), Text = "RETURN CURSOR AFTER CLICK", Font = Fx.F(9f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Color.Transparent });
            var retTog = new Toggle { Location = new Point(pw - Sc(58), Sc(y)), Size = new Size(Sc(48), Sc(26)), On = _returnCursor };
            retTog.Toggled += (s, e) => { _returnCursor = retTog.On; SaveSettings(); };
            p.Controls.Add(retTog);

            UpdateTargetVisibility();
        }
        void UpdateTargetVisibility()
        {
            bool mouse = _output == 0;
            if (_mouseLbl != null) _mouseLbl.Visible = mouse;
            if (_mouseSeg != null) _mouseSeg.Visible = mouse;
            if (_keyLbl != null) _keyLbl.Visible = !mouse;
            if (_keyBtn != null) _keyBtn.Visible = !mouse;
        }

        void BuildSettingsPanel(Panel p, int pw)
        {
            p.Controls.Add(MiniLabel("WINDOW SIZE", 16, 16));
            int sel = _uiFactor < 0.93 ? 0 : (_uiFactor > 1.1 ? 2 : 1);
            var sizeSeg = new Segment("SMALL", "MEDIUM", "LARGE") { Location = new Point(Sc(10), Sc(36)), Size = new Size(pw - Sc(20), Sc(34)), Selected = sel };
            sizeSeg.SelectedChanged += (s, e) => ApplyScale(sizeSeg.Selected == 0 ? 0.85 : sizeSeg.Selected == 2 ? 1.2 : 1.0);
            p.Controls.Add(sizeSeg);

            AddBehaviorRow(p, pw, 84, "SOUND CUES", "Soft tick when armed / disarmed", _set.SoundEnabled, v => { _set.SoundEnabled = v; SaveSettings(); });
            AddBehaviorRow(p, pw, 132, "ROBLOX ONLY", "Click only when Roblox is focused", _robloxOnly, v => { _robloxOnly = v; SaveSettings(); });
            AddBehaviorRow(p, pw, 180, "HUMANIZE CLICKS", "Vary timing slightly", _humanize, v => { _humanize = v; SaveSettings(); });
            AddBehaviorRow(p, pw, 228, "ALWAYS ON TOP", "Keep window above your game", TopMost, v => { TopMost = v; SaveSettings(); });
            AddBehaviorRow(p, pw, 276, "MINIMIZE TO TRAY", "Hide to tray when minimized", _set.MinimizeToTray, v => { _set.MinimizeToTray = v; SaveSettings(); });
            AddBehaviorRow(p, pw, 324, "WELCOME SCREEN", "Show splash on launch", _set.ShowSplash, v => { _set.ShowSplash = v; SaveSettings(); });

            p.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(16), Sc(380)), Text = "PANIC KEY", Font = Fx.F(10f, FontStyle.Bold), ForeColor = Theme.Red, BackColor = Color.Transparent });
            p.Controls.Add(new Label { AutoSize = false, Size = new Size(pw - Sc(40), Sc(16)), Location = new Point(Sc(16), Sc(400)), Text = "Instantly stops everything, any mode", Font = Fx.F(8.5f), ForeColor = Theme.TextDim, BackColor = Color.Transparent });
            _panicBtn = new GButton { Location = new Point(Sc(10), Sc(420)), Size = new Size(pw - Sc(20), Sc(34)), Text = VkPretty(_panicVk), Fill = Theme.Bg, Fg = Theme.Red, Outline = true, FontSize = 10f };
            _panicBtn.Click += (s, e) => BeginBind(1);
            p.Controls.Add(_panicBtn);

            AddBehaviorRow(p, pw, 472, "CHECK FOR UPDATES", "Look for a new version on launch", _set.CheckUpdates, v => { _set.CheckUpdates = v; SaveSettings(); });
            var chk = new GButton { Location = new Point(Sc(10), Sc(516)), Size = new Size(pw - Sc(20), Sc(30)), Text = "CHECK NOW", Fill = Theme.PanelHi, Fg = Theme.Text, Outline = true, FontSize = 9f };
            chk.Click += (s, e) => { _set.SkippedVersion = ""; SaveSettings(); CheckForUpdates(true); };
            p.Controls.Add(chk);
            p.Controls.Add(new Label { AutoSize = false, Size = new Size(pw - Sc(20), Sc(16)), Location = new Point(Sc(10), Sc(552)), Text = "Version " + App.Version, Font = Fx.F(8f), ForeColor = Theme.TextFaint, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter });
        }
        void AddBehaviorRow(Panel p, int pw, int y, string title, string desc, bool on, Action<bool> onChange)
        {
            p.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(16), Sc(y)), Text = title, Font = Fx.F(10f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Color.Transparent });
            p.Controls.Add(new Label { AutoSize = false, Size = new Size(pw - Sc(80), Sc(16)), Location = new Point(Sc(16), Sc(y + 18)), Text = desc, Font = Fx.F(8.5f), ForeColor = Theme.TextDim, BackColor = Color.Transparent });
            var tog = new Toggle { Location = new Point(pw - Sc(58), Sc(y + 4)), Size = new Size(Sc(48), Sc(26)), On = on };
            tog.Toggled += (s, e) => onChange(tog.On);
            p.Controls.Add(tog);
        }

        void BuildStatsPanel(Panel p, int pw)
        {
            p.Controls.Add(MiniLabel("TOTAL OUTPUTS", 16, 18));
            _stClicks = new Label { AutoSize = true, Location = new Point(Sc(14), Sc(36)), Text = "0", Font = Fx.F(30f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Color.Transparent };
            p.Controls.Add(_stClicks);
            p.Controls.Add(MiniLabel("SESSION TIME", 16, 104));
            _stTime = ValueLabel("0:00", 16, 124, 140); _stTime.Font = Fx.F(13f, FontStyle.Bold); p.Controls.Add(_stTime);
            p.Controls.Add(MiniLabel("PEAK CPS", 141, 104));
            _stPeak = ValueLabel("0.0", 141, 124, 120); _stPeak.Font = Fx.F(13f, FontStyle.Bold); _stPeak.ForeColor = Theme.AccentSoft; p.Controls.Add(_stPeak);
            p.Controls.Add(MiniLabel("AVERAGE CPS", 16, 170));
            _stAvg = ValueLabel("0.0", 16, 190, 140); _stAvg.Font = Fx.F(13f, FontStyle.Bold); _stAvg.ForeColor = Theme.Green; p.Controls.Add(_stAvg);
            var reset = new GButton { Location = new Point(Sc(10), Sc(296)), Size = new Size(pw - Sc(20), Sc(38)), Text = "RESET STATS", Fill = Theme.PanelHi, Fg = Theme.Text, Outline = true, FontSize = 9.5f };
            reset.Click += (s, e) => { Interlocked.Exchange(ref _clicks, 0); _sessionStart = DateTime.Now; _peakCps = 0; };
            p.Controls.Add(reset);

            int yb = 350;
            p.Controls.Add(MiniLabel("PROCESSOR", 16, yb)); yb += 18;
            _sysCpu = ValueLabel(GetCpuName(), 16, yb, 262); p.Controls.Add(_sysCpu); yb += 26;
            p.Controls.Add(MiniLabel("TIMER RESOLUTION", 16, yb));
            _sysTimer = ValueLabel("—", 132, yb, 136, ContentAlignment.MiddleRight); _sysTimer.ForeColor = Theme.Green; p.Controls.Add(_sysTimer); yb += 26;
            int halfW = (pw - Sc(28)) / 2;
            var cal = new GButton { Location = new Point(Sc(10), Sc(yb)), Size = new Size(halfW, Sc(34)), Text = "CALIBRATE", Fill = Theme.PanelHi, Fg = Theme.Text, Outline = true, FontSize = 9.5f };
            cal.Click += (s, e) => RunBenchmark(false); p.Controls.Add(cal);
            _tuneBtn = new GButton { Location = new Point(Sc(10) + halfW + Sc(8), Sc(yb)), Size = new Size(halfW, Sc(34)), Text = "AUTO-TUNE", Fill = Theme.Accent, Fg = Color.White, FontSize = 9.5f };
            _tuneBtn.Click += (s, e) => RunBenchmark(true); p.Controls.Add(_tuneBtn); yb += 42;
            _tuneResult = new Label { AutoSize = false, Size = new Size(pw - Sc(16), Sc(30)), Location = new Point(Sc(10), Sc(yb)), Font = Fx.F(8.5f, FontStyle.Bold), ForeColor = Theme.TextDim, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, Text = "Run a test to verify timing stability." };
            p.Controls.Add(_tuneResult);
        }

        // ── STATS TRACKER (wins + armor trim) ──
        TextBox TrimInput(int x, int y, int w, string hint)
        {
            var tb = new TextBox { Bounds = new Rectangle(Sc(x), Sc(y), Sc(w), Sc(26)), BackColor = Theme.Bg, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = Fx.F(10f), PlaceholderText = hint };
            tb.KeyPress += (s, e) => { if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true; };
            return tb;
        }
        static int ParseInt(TextBox tb) { int v; return int.TryParse(tb.Text.Trim(), out v) ? v : -1; }
        int TrimTotal() { int s = _set.TrimBase; foreach (var g in _set.TrimGames) s += g; return s; }

        Label Cap(string t, int x, int y) { return new Label { AutoSize = true, Location = new Point(Sc(x), Sc(y)), Text = t, Font = Fx.F(8f), ForeColor = Theme.TextDim, BackColor = Color.Transparent }; }
        Label Val(string t, int x, int y, int w, float size, Color c) { return new Label { AutoSize = false, Size = new Size(Sc(w), Sc((int)(size * 1.9f))), Location = new Point(Sc(x), Sc(y)), Text = t, Font = Fx.F(size, FontStyle.Bold), ForeColor = c, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft };
        }

        void BuildTrimPanel(Panel p, int pw)
        {
            int cw = pw - Sc(20);          // card width
            int inx = Sc(12);              // inner x padding inside a card
            int y = 0;

            // header
            p.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(12), Sc(y)), Text = "STATS TRACKER", Font = Fx.F(10f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Color.Transparent });
            y += 26;

            // ── HERO: wins all time + xp left ──
            var hero = new SubCard { Hero = true, Location = new Point(Sc(10), Sc(y)), Size = new Size(cw, Sc(74)) };
            hero.Controls.Add(Cap("WINS ALL TIME", 12, 12));
            _heroWins = Val("0", 12, 30, 88, 20f, Theme.Text); hero.Controls.Add(_heroWins);
            var editWins = new GButton { Location = new Point(Sc(100), Sc(34)), Size = new Size(Sc(30), Sc(20)), Text = "SET", Fill = Theme.Bg, Fg = Theme.TextDim, Outline = true, FontSize = 7f };
            editWins.Click += (s, e) =>
            {
                string r = AskName("Set all-time wins", _set.TrimTotalWins.ToString());
                int v; if (r != null && int.TryParse(r.Trim(), out v) && v >= 0) { _set.TrimTotalWins = v; TrimCommit(); }
            };
            hero.Controls.Add(editWins);
            hero.Controls.Add(Cap("XP LEFT", 140, 12));
            _heroXpLeft = Val("396,900", 140, 30, 140, 20f, Theme.AccentSoft); hero.Controls.Add(_heroXpLeft);
            p.Controls.Add(hero); y += 84;

            // ── WINS TODAY ──
            var wc = new SubCard { Location = new Point(Sc(10), Sc(y)), Size = new Size(cw, Sc(156)) };
            wc.Controls.Add(Cap("WINS TODAY", 12, 12));
            wc.Controls.Add(Cap("GOAL", 178, 12));
            _trimGoalIn = TrimInput(214, 8, 46, ""); _trimGoalIn.Text = _set.TrimGoal.ToString();
            Action commitGoal = () => { int v = ParseInt(_trimGoalIn); if (v >= 1) { _set.TrimGoal = v; TrimCommit(); } else _trimGoalIn.Text = _set.TrimGoal.ToString(); };
            _trimGoalIn.Leave += (s, e) => commitGoal();
            _trimGoalIn.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; commitGoal(); } };
            wc.Controls.Add(_trimGoalIn);

            int cy = 42, cxc = cw / 2;
            var wMinus = new GButton { Location = new Point(cxc - Sc(74), Sc(cy)), Size = new Size(Sc(32), Sc(32)), Text = "\u2212", Fill = Theme.Bg, Fg = Theme.TextDim, Outline = true, FontSize = 12f };
            wMinus.Click += (s, e) => { if (_set.TrimWins > 0) { _set.TrimWins--; _set.TrimTotalWins = Math.Max(0, _set.TrimTotalWins - 1); TrimCommit(); } };
            wc.Controls.Add(wMinus);
            _trimWinNum = new Label { AutoSize = false, Size = new Size(Sc(80), Sc(32)), Location = new Point(cxc - Sc(40), Sc(cy)), Text = "0", Font = Fx.F(17f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter };
            wc.Controls.Add(_trimWinNum);
            var wPlus = new GButton { Location = new Point(cxc + Sc(42), Sc(cy)), Size = new Size(Sc(32), Sc(32)), Text = "+", Fill = Theme.Accent, Fg = Color.White, FontSize = 12f };
            wPlus.Click += (s, e) => { _set.TrimWins++; _set.TrimTotalWins++; TrimCommit(); };
            wc.Controls.Add(wPlus);

            _trimWinBar = new XpBar { Location = new Point(inx, Sc(90)), Size = new Size(cw - Sc(24), Sc(9)) };
            wc.Controls.Add(_trimWinBar);
            _trimWinPct = new Label { AutoSize = false, Size = new Size(cw - Sc(24), Sc(16)), Location = new Point(inx, Sc(104)), Text = "0 / 10  \u00b7  0%", Font = Fx.F(8f, FontStyle.Bold), ForeColor = Theme.TextDim, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter };
            wc.Controls.Add(_trimWinPct);
            wc.Controls.Add(Cap("LEFT TODAY", 12, 130));
            var setToday = new GButton { Location = new Point(Sc(88), Sc(126)), Size = new Size(Sc(30), Sc(20)), Text = "SET", Fill = Theme.Bg, Fg = Theme.TextDim, Outline = true, FontSize = 7f };
            setToday.Click += (s, e) =>
            {
                string r = AskName("Set wins today", _set.TrimWins.ToString());
                int v; if (r != null && int.TryParse(r.Trim(), out v) && v >= 0)
                {
                    _set.TrimTotalWins = Math.Max(0, _set.TrimTotalWins + (v - _set.TrimWins));
                    _set.TrimWins = v; TrimCommit();
                }
            };
            wc.Controls.Add(setToday);
            _trimWinLeft = new Label { AutoSize = false, Size = new Size(Sc(70), Sc(18)), Location = new Point(cw - Sc(84), Sc(128)), Text = "10", Font = Fx.F(10.5f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleRight };
            wc.Controls.Add(_trimWinLeft);
            p.Controls.Add(wc); y += 166;

            // ── ARMOR TRIM PROGRESS ──
            var tc = new SubCard { Location = new Point(Sc(10), Sc(y)), Size = new Size(cw, Sc(128)) };
            tc.Controls.Add(Cap("ARMOR TRIM PROGRESS", 12, 12));
            _trimNow = Val("0", 12, 30, 100, 14f, Theme.Text); tc.Controls.Add(_trimNow);
            tc.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(74), Sc(38)), Text = "/ 396,900 xp", Font = Fx.F(8f), ForeColor = Theme.TextDim, BackColor = Color.Transparent });
            _trimBar = new XpBar { Location = new Point(inx, Sc(62)), Size = new Size(cw - Sc(24), Sc(9)) };
            tc.Controls.Add(_trimBar);
            int c3 = (cw - Sc(24)) / 3;
            tc.Controls.Add(Cap("COMPLETE", 12, 84));
            tc.Controls.Add(Cap("AVG / GAME", 12 + 90, 84));
            tc.Controls.Add(Cap("GAMES LEFT", 12 + 180, 84));
            _trimPct = Val("0.0%", 12, 100, 84, 10.5f, Theme.AccentSoft); tc.Controls.Add(_trimPct);
            _trimAvg = Val("\u2014", 12 + 90, 100, 84, 10.5f, Theme.Text); tc.Controls.Add(_trimAvg);
            _trimEst = Val("\u2014", 12 + 180, 100, 84, 10.5f, Theme.Text); tc.Controls.Add(_trimEst);
            p.Controls.Add(tc); y += 136;

            // ── LOG A GAME ──
            var lc = new SubCard { Location = new Point(Sc(10), Sc(y)), Size = new Size(cw, Sc(142)) };
            lc.Controls.Add(Cap("LOG A GAME", 12, 12));
            _trimXpIn = TrimInput(12, 30, 176, "XP earned"); lc.Controls.Add(_trimXpIn);
            var addBtn = new GButton { Location = new Point(Sc(194), Sc(30)), Size = new Size(cw - Sc(206), Sc(26)), Text = "ADD", Fill = Theme.Accent, Fg = Color.White, FontSize = 8.5f };
            Action doAdd = () => { int v = ParseInt(_trimXpIn); if (v > 0) { _set.TrimGames.Add(v); _trimXpIn.Text = ""; TrimCommit(); } };
            addBtn.Click += (s, e) => doAdd();
            _trimXpIn.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; doAdd(); } };
            lc.Controls.Add(addBtn);

            int[] quick = { 100, 250, 500, 1000 };
            int qw = (cw - Sc(24) - Sc(18)) / 4;
            for (int i = 0; i < 4; i++)
            {
                int amt = quick[i];
                var qb = new GButton { Location = new Point(inx + i * (qw + Sc(6)), Sc(66)), Size = new Size(qw, Sc(26)), Text = "+" + (amt >= 1000 ? "1k" : amt.ToString()), Fill = Theme.Bg, Fg = Theme.TextDim, Outline = true, FontSize = 8f };
                qb.Click += (s, e) => { _set.TrimGames.Add(amt); TrimCommit(); };
                lc.Controls.Add(qb);
            }

            _trimSetIn = TrimInput(12, 104, 130, "Set XP total"); lc.Controls.Add(_trimSetIn);
            var setBtn = new GButton { Location = new Point(Sc(148), Sc(104)), Size = new Size(Sc(60), Sc(26)), Text = "SET", Fill = Theme.Bg, Fg = Theme.Text, Outline = true, FontSize = 8.5f };
            Action doSet = () =>
            {
                int v = ParseInt(_trimSetIn); if (v < 0) return;
                int logged = 0; foreach (var gm in _set.TrimGames) logged += gm;
                if (v < logged) { _set.TrimGames.Clear(); _set.TrimBase = v; } else _set.TrimBase = v - logged;
                _trimSetIn.Text = ""; TrimCommit();
            };
            setBtn.Click += (s, e) => doSet();
            _trimSetIn.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; doSet(); } };
            lc.Controls.Add(setBtn);
            var undo = new GButton { Location = new Point(Sc(214), Sc(104)), Size = new Size(cw - Sc(226), Sc(26)), Text = "UNDO", Fill = Theme.Bg, Fg = Theme.TextDim, Outline = true, FontSize = 8f };
            undo.Click += (s, e) => { if (_set.TrimGames.Count > 0) { _set.TrimGames.RemoveAt(_set.TrimGames.Count - 1); TrimCommit(); } };
            lc.Controls.Add(undo);
            p.Controls.Add(lc); y += 150;

            // ── footer actions ──
            int hw = (cw - Sc(8)) / 2;
            var newDay = new GButton { Location = new Point(Sc(10), Sc(y)), Size = new Size(hw, Sc(28)), Text = "NEW DAY", Fill = Theme.Panel, Fg = Theme.TextDim, Outline = true, FontSize = 8f };
            newDay.Click += (s, e) => { _set.TrimWins = 0; TrimCommit(); };
            p.Controls.Add(newDay);
            var resetAll = new GButton { Location = new Point(Sc(10) + hw + Sc(8), Sc(y)), Size = new Size(hw, Sc(28)), Text = "RESET ALL", Fill = Theme.Panel, Fg = Theme.Red, Outline = true, FontSize = 8f };
            resetAll.Click += (s, e) =>
            {
                if (MessageBox.Show("Clear all XP, games, and wins?", "Reset Stats Tracker", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                { _set.TrimGames.Clear(); _set.TrimBase = 0; _set.TrimWins = 0; _set.TrimTotalWins = 0; TrimCommit(); }
            };
            p.Controls.Add(resetAll);

            TrimRender();
        }

        void TrimCommit() { TrimRender(); SaveSettings(); }

        void TrimRender()
        {
            if (_trimNow == null) return;
            int total = TrimTotal();
            float pct = Math.Min(1f, (float)total / TrimMax);
            int xpLeft = Math.Max(0, TrimMax - total);

            _heroWins.Text = _set.TrimTotalWins.ToString("N0");
            _heroXpLeft.Text = xpLeft.ToString("N0");

            _trimNow.Text = total.ToString("N0");
            _trimBar.Progress = pct;
            _trimPct.Text = (pct * 100f).ToString("0.0") + "%";
            if (_set.TrimGames.Count > 0)
            {
                int sum = 0; foreach (var gm in _set.TrimGames) sum += gm;
                double avg = (double)sum / _set.TrimGames.Count;
                _trimAvg.Text = Math.Round(avg).ToString("N0");
                _trimEst.Text = total >= TrimMax ? "0" : Math.Ceiling(xpLeft / avg).ToString("N0");
                _trimAvg.ForeColor = Theme.Text; _trimEst.ForeColor = Theme.Text;
            }
            else { _trimAvg.Text = "\u2014"; _trimEst.Text = "\u2014"; _trimAvg.ForeColor = Theme.TextFaint; _trimEst.ForeColor = Theme.TextFaint; }

            _trimWinNum.Text = _set.TrimWins.ToString();
            _trimWinLeft.Text = Math.Max(0, _set.TrimGoal - _set.TrimWins).ToString();
            int goal = Math.Max(1, _set.TrimGoal);
            float wpct = (float)_set.TrimWins / goal;
            _trimWinBar.Progress = Math.Min(1f, wpct);
            _trimWinPct.Text = _set.TrimWins + " / " + _set.TrimGoal + "  \u00b7  " + Math.Round(wpct * 100f) + "%";
            _trimWinPct.ForeColor = _set.TrimWins >= _set.TrimGoal ? Theme.AccentSoft : Theme.TextDim;
        }


        void BuildStylePanel(Panel p, int pw)
        {
            p.Controls.Add(MiniLabel("ACCENT COLOR", 16, 18));
            int sw = 40, gap = 8, x0 = 10;
            for (int i = 0; i < Theme.Presets.Length; i++)
            {
                var c = Theme.Presets[i];
                var s = new Swatch(c) { Location = new Point(Sc(x0 + i * (sw + gap)), Sc(40)), Size = new Size(Sc(sw), Sc(sw)), Selected = (c.ToArgb() == Theme.Accent.ToArgb()) };
                s.Picked += (a, e) => ChangeAccent(((Swatch)a).Color);
                _swatches.Add(s); p.Controls.Add(s);
            }
            p.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(16), Sc(102)), Text = "IN-GAME OVERLAY", Font = Fx.F(10f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Color.Transparent });
            p.Controls.Add(new Label { AutoSize = false, Size = new Size(pw - Sc(80), Sc(16)), Location = new Point(Sc(16), Sc(122)), Text = "Floating CPS readout you can place over your game", Font = Fx.F(8.5f), ForeColor = Theme.TextDim, BackColor = Color.Transparent });
            var ovTog = new Toggle { Location = new Point(pw - Sc(58), Sc(106)), Size = new Size(Sc(48), Sc(26)), On = _set.ShowOverlay };
            ovTog.Toggled += (s, e) => SetOverlay(ovTog.On);
            p.Controls.Add(ovTog);
            var resetPos = new GButton { Location = new Point(Sc(10), Sc(168)), Size = new Size(pw - Sc(20), Sc(36)), Text = "RESET OVERLAY POSITION", Fill = Theme.PanelHi, Fg = Theme.Text, Outline = true, FontSize = 9f };
            resetPos.Click += (s, e) => { _set.OverlayX = 0; _set.OverlayY = 0; if (_overlay != null && _overlay.Visible) PlaceOverlayDefault(); SaveSettings(); };
            p.Controls.Add(resetPos);
            p.Controls.Add(new Label { AutoSize = false, Size = new Size(pw - Sc(20), Sc(30)), Location = new Point(Sc(12), Sc(214)), Text = "Tip: drag the overlay anywhere — it remembers where you put it.", Font = Fx.F(8f), ForeColor = Theme.TextFaint, BackColor = Color.Transparent });
        }

        void ShowTab(int i)
        {
            _curTab = i;
            if (_rail != null) _rail.Selected = i;
            if (_panClick != null) _panClick.Visible = i == 0;
            if (_panTarget != null) _panTarget.Visible = i == 1;
            if (_panBoost != null) _panBoost.Visible = i == 2;
            if (_panStats != null) _panStats.Visible = i == 3;
            if (_panTrim != null) _panTrim.Visible = i == 4;
            if (_panStyle != null) _panStyle.Visible = i == 5;
            if (_panSettings != null) _panSettings.Visible = i == 6;
            Panel cur = i == 0 ? _panClick : i == 1 ? _panTarget : i == 2 ? _panBoost : i == 3 ? _panStats : i == 4 ? _panTrim : i == 5 ? _panStyle : _panSettings;
            if (cur != null && !_loading) { _panelTargetTop = _phy; _animPanel = cur; _panelEase = 0f; cur.Top = _phy + Sc(16); }
            else if (cur != null) cur.Top = _phy;
        }

        void BuildBoostPanel(Panel p, int pw)
        {
            p.Controls.Add(MiniLabel("LIVE MONITOR", 16, 12));
            int y = 32;
            p.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(16), Sc(y)), Text = "CPU", Font = Fx.F(9.5f, FontStyle.Bold), ForeColor = Theme.TextDim, BackColor = Color.Transparent });
            _bCpu = RightLabel("0%", pw - Sc(110), Sc(y), 96, Theme.Text); p.Controls.Add(_bCpu); y += 24;
            p.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(16), Sc(y)), Text = "MEMORY", Font = Fx.F(9.5f, FontStyle.Bold), ForeColor = Theme.TextDim, BackColor = Color.Transparent });
            _bRam = RightLabel("0%", pw - Sc(110), Sc(y), 96, Theme.Text); p.Controls.Add(_bRam); y += 24;
            p.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(16), Sc(y)), Text = "ROBLOX", Font = Fx.F(9.5f, FontStyle.Bold), ForeColor = Theme.TextDim, BackColor = Color.Transparent });
            _bRoblox = RightLabel("Not running", pw - Sc(190), Sc(y), 176, Theme.TextDim); p.Controls.Add(_bRoblox); y += 24;
            p.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(16), Sc(y)), Text = "TIMER", Font = Fx.F(9.5f, FontStyle.Bold), ForeColor = Theme.TextDim, BackColor = Color.Transparent });
            _bTimer = RightLabel("—", pw - Sc(150), Sc(y), 136, Theme.Green); p.Controls.Add(_bTimer); y += 32;

            // AUTO-OPTIMIZE master
            p.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(16), Sc(y)), Text = "AUTO-OPTIMIZE", Font = Fx.F(10f, FontStyle.Bold), ForeColor = Theme.AccentSoft, BackColor = Color.Transparent });
            p.Controls.Add(new Label { AutoSize = false, Size = new Size(pw - Sc(80), Sc(14)), Location = new Point(Sc(16), Sc(y + 18)), Text = "Apply enabled boosts when Roblox launches", Font = Fx.F(8f), ForeColor = Theme.TextDim, BackColor = Color.Transparent });
            _autoTog = new Toggle { Location = new Point(pw - Sc(58), Sc(y + 3)), Size = new Size(Sc(48), Sc(26)), On = _autoOptimize };
            _autoTog.Toggled += (s, e) => { _autoOptimize = _autoTog.On; SaveSettings(); BoostMsg(_autoOptimize ? "Auto-optimize on — runs when Roblox starts." : "Auto-optimize off.", _autoOptimize ? Theme.Green : Theme.TextDim); };
            p.Controls.Add(_autoTog); y += 42;

            p.Controls.Add(MiniLabel("BOOSTS  (reversible)", 16, y)); y += 20;
            p.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(16), Sc(y + 4)), Text = "BOOST ROBLOX PRIORITY", Font = Fx.F(9f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Color.Transparent });
            var prTog = new Toggle { Location = new Point(pw - Sc(58), Sc(y)), Size = new Size(Sc(48), Sc(26)), On = _boostPriorityOn };
            prTog.Toggled += (s, e) => { if (_suppressBoostToggle) return; _boostPriorityOn = prTog.On; ApplyPriority(prTog.On); SaveSettings(); };
            p.Controls.Add(prTog); y += 34;
            p.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(16), Sc(y + 4)), Text = "PERFORMANCE POWER PLAN", Font = Fx.F(9f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Color.Transparent });
            var pwTog = new Toggle { Location = new Point(pw - Sc(58), Sc(y)), Size = new Size(Sc(48), Sc(26)), On = _boostPowerOn };
            pwTog.Toggled += (s, e) => { if (_suppressBoostToggle) return; _boostPowerOn = pwTog.On; ApplyPower(pwTog.On); SaveSettings(); };
            p.Controls.Add(pwTog); y += 38;

            int halfW = (pw - Sc(20) - Sc(8)) / 2;
            var opt = new GButton { Location = new Point(Sc(10), Sc(y)), Size = new Size(halfW, Sc(34)), Text = "OPTIMIZE", Fill = Theme.Accent, Fg = Color.White, FontSize = 9.5f };
            opt.Click += (s, e) => { _suppressBoostToggle = true; prTog.On = true; pwTog.On = true; _suppressBoostToggle = false; _boostPriorityOn = true; _boostPowerOn = true; ApplyPriority(true); ApplyPower(true); if (_fpsCapEnabled) ApplyFpsNow(); SaveSettings(); };
            p.Controls.Add(opt);
            var rest = new GButton { Location = new Point(Sc(10) + halfW + Sc(8), Sc(y)), Size = new Size(halfW, Sc(34)), Text = "RESTORE", Fill = Theme.PanelHi, Fg = Theme.Text, Outline = true, FontSize = 9.5f };
            rest.Click += (s, e) => { _suppressBoostToggle = true; prTog.On = false; pwTog.On = false; _suppressBoostToggle = false; _boostPriorityOn = false; _boostPowerOn = false; ApplyPriority(false); ApplyPower(false); SaveSettings(); };
            p.Controls.Add(rest); y += 42;

            // FPS CAP
            p.Controls.Add(MiniLabel("FPS CAP  (writes Roblox setting)", 16, y)); y += 18;
            string why; int sug = SuggestFps(out why);
            _fpsSuggest = new Label { AutoSize = false, Size = new Size(pw - Sc(20), Sc(28)), Location = new Point(Sc(16), Sc(y)), Text = "Suggested " + sug + " — " + why, Font = Fx.F(8f), ForeColor = Theme.TextDim, BackColor = Color.Transparent }; p.Controls.Add(_fpsSuggest); y += 32;
            _fpsMinus = new GButton { Location = new Point(Sc(10), Sc(y)), Size = new Size(Sc(38), Sc(30)), Text = "\u2212", Fill = Theme.PanelHi, Fg = Theme.Text, Outline = true, FontSize = 12f };
            _fpsMinus.Click += (s, e) => { if (_fpsUncapped) return; _fpsCap = Math.Max(30, _fpsCap - 30); RefreshFpsUi(); SaveSettings(); };
            p.Controls.Add(_fpsMinus);
            _fpsValLbl = new Label { AutoSize = false, Size = new Size(Sc(96), Sc(30)), Location = new Point(Sc(50), Sc(y)), Text = _fpsUncapped ? "Uncapped" : _fpsCap.ToString(), Font = Fx.F(13f, FontStyle.Bold), ForeColor = Theme.AccentSoft, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleCenter }; p.Controls.Add(_fpsValLbl);
            _fpsPlus = new GButton { Location = new Point(Sc(148), Sc(y)), Size = new Size(Sc(38), Sc(30)), Text = "+", Fill = Theme.PanelHi, Fg = Theme.Text, Outline = true, FontSize = 12f };
            _fpsPlus.Click += (s, e) => { if (_fpsUncapped) return; _fpsCap = Math.Min(1000, _fpsCap + 30); RefreshFpsUi(); SaveSettings(); };
            p.Controls.Add(_fpsPlus);
            var useSug = new GButton { Location = new Point(Sc(192), Sc(y)), Size = new Size(pw - Sc(202), Sc(30)), Text = "USE " + sug, Fill = Theme.PanelHi, Fg = Theme.AccentSoft, Outline = true, FontSize = 8.5f };
            useSug.Click += (s, e) => { _fpsCap = sug; RefreshFpsUi(); SaveSettings(); };
            p.Controls.Add(useSug); y += 36;

            p.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(16), Sc(y + 3)), Text = "UNCAPPED", Font = Fx.F(9f, FontStyle.Bold), ForeColor = Theme.Text, BackColor = Color.Transparent });
            p.Controls.Add(new Label { AutoSize = true, Location = new Point(Sc(16), Sc(y + 19)), Text = "Max frames (higher GPU use)", Font = Fx.F(8f), ForeColor = Theme.TextDim, BackColor = Color.Transparent });
            var uncapTog = new Toggle { Location = new Point(pw - Sc(58), Sc(y + 2)), Size = new Size(Sc(48), Sc(26)), On = _fpsUncapped };
            uncapTog.Toggled += (s, e) => { _fpsUncapped = uncapTog.On; RefreshFpsUi(); SaveSettings(); };
            p.Controls.Add(uncapTog); y += 40;

            var fpsOn = new GButton { Location = new Point(Sc(10), Sc(y)), Size = new Size(halfW, Sc(34)), Text = "APPLY CAP", Fill = Theme.Accent, Fg = Color.White, FontSize = 9.5f };
            fpsOn.Click += (s, e) => { _fpsCapEnabled = true; ApplyFpsNow(); SaveSettings(); };
            p.Controls.Add(fpsOn); _fpsApply = fpsOn;
            var fpsOff = new GButton { Location = new Point(Sc(10) + halfW + Sc(8), Sc(y)), Size = new Size(halfW, Sc(34)), Text = "REMOVE", Fill = Theme.PanelHi, Fg = Theme.Text, Outline = true, FontSize = 9.5f };
            fpsOff.Click += (s, e) => { _fpsCapEnabled = false; RestoreFpsNow(); SaveSettings(); };
            p.Controls.Add(fpsOff); _fpsRestore = fpsOff; y += 40;

            _boostStatus = new Label { AutoSize = false, Size = new Size(pw - Sc(24), Sc(28)), Location = new Point(Sc(14), Sc(y)), Font = Fx.F(8.5f, FontStyle.Bold), ForeColor = Theme.TextDim, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft, Text = IsAdmin() ? "Boosts auto-revert when you close the app." : "Tip: some boosts need admin to apply." };
            p.Controls.Add(_boostStatus); y += 30;
            _bElevate = new GButton { Location = new Point(Sc(10), Sc(y)), Size = new Size(pw - Sc(20), Sc(28)), Text = "RUN AS ADMIN", Fill = Theme.PanelHi, Fg = Theme.Amber, Outline = true, FontSize = 9f, Visible = !IsAdmin() };
            _bElevate.Click += (s, e) => RelaunchAsAdmin();
            p.Controls.Add(_bElevate);
        }
        void RefreshFpsUi() { if (_fpsValLbl != null) _fpsValLbl.Text = _fpsUncapped ? "Uncapped" : _fpsCap.ToString(); if (_fpsMinus != null) _fpsMinus.Fg = _fpsUncapped ? Theme.TextFaint : Theme.Text; if (_fpsPlus != null) _fpsPlus.Fg = _fpsUncapped ? Theme.TextFaint : Theme.Text; }

        // ── FPS CAP DETECTION + APPLY ──
        static string GetGpuName()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"))
                    if (k != null)
                        foreach (var sub in k.GetSubKeyNames())
                        {
                            if (sub.Length != 4) continue;
                            using (var g = k.OpenSubKey(sub)) { var name = g != null ? g.GetValue("DriverDesc") as string : null; if (!string.IsNullOrEmpty(name)) return name.Trim(); }
                        }
            }
            catch { }
            return "Unknown GPU";
        }
        int GetRefreshRate()
        {
            try { using (var g = CreateGraphics()) { IntPtr hdc = g.GetHdc(); int hz = Native.GetDeviceCaps(hdc, Native.VREFRESH); g.ReleaseHdc(hdc); if (hz > 1) return hz; } } catch { }
            return 60;
        }
        int SuggestFps(out string why)
        {
            string gpu = GetGpuName(), gl = gpu.ToLowerInvariant();
            int ram = 0; try { var m = new Native.MEMORYSTATUSEX(); if (Native.GlobalMemoryStatusEx(m)) ram = (int)Math.Round(m.ullTotalPhys / 1073741824.0); } catch { }
            int hz = GetRefreshRate();
            int tier;
            if (gl.Contains("rtx 30") || gl.Contains("rtx 40") || gl.Contains("rtx 50") || gl.Contains("rx 6") || gl.Contains("rx 7") || gl.Contains("rx 9")) tier = 2;
            else if (gl.Contains("gtx 16") || gl.Contains("rtx 20") || gl.Contains("gtx 10") || gl.Contains("rx 5")) tier = 1;
            else if (gl.Contains("intel") || gl.Contains("uhd") || gl.Contains("iris") || gl.Contains("vega") || gl.Contains("radeon graphics")) tier = 0;
            else tier = 1;
            if (ram > 0 && ram < 8) tier = Math.Min(tier, 0);
            int fps = tier == 2 ? 360 : tier == 1 ? 240 : 120;
            string tn = tier == 2 ? "strong" : tier == 1 ? "solid" : "modest";
            why = tn + " hardware" + (ram > 0 ? " (" + ram + "GB)" : "") + "; " + hz + "Hz screen but Roblox can render higher for lower input lag.";
            return fps;
        }
        static List<string> RobloxVersionDirs()
        {
            var list = new List<string>();
            try
            {
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roblox", "Versions");
                if (Directory.Exists(root))
                    foreach (var d in Directory.GetDirectories(root))
                        if (File.Exists(Path.Combine(d, "RobloxPlayerBeta.exe"))) list.Add(d);
            }
            catch { }
            return list;
        }
        static bool WriteFpsFile(int fps)
        {
            var dirs = RobloxVersionDirs(); if (dirs.Count == 0) return false;
            int ok = 0;
            foreach (var d in dirs)
            {
                try { string cs = Path.Combine(d, "ClientSettings"); Directory.CreateDirectory(cs); File.WriteAllText(Path.Combine(cs, "ClientAppSettings.json"), "{\"DFIntTaskSchedulerTargetFps\": " + fps + "}"); ok++; } catch { }
            }
            return ok > 0;
        }
        static void DeleteFpsFile()
        {
            foreach (var d in RobloxVersionDirs())
                try { string f = Path.Combine(d, "ClientSettings", "ClientAppSettings.json"); if (File.Exists(f)) File.Delete(f); } catch { }
        }
        void ApplyFpsNow()
        {
            int fps = _fpsUncapped ? 9999 : _fpsCap;
            bool ok = WriteFpsFile(fps); _fpsApplied = ok;
            BoostMsg(ok ? ("FPS cap set to " + (_fpsUncapped ? "Uncapped" : _fpsCap.ToString()) + " (restart Roblox to apply).") : "Roblox folder not found — launch Roblox once, then retry.", ok ? Theme.Green : Theme.Amber);
        }
        void RestoreFpsNow() { DeleteFpsFile(); _fpsApplied = false; BoostMsg("FPS cap removed — back to Roblox default.", Theme.TextDim); }

        static bool IsAdmin() { try { using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); } catch { return false; } }
        void RelaunchAsAdmin()
        {
            try
            {
                SaveSettings();
                string exe = Process.GetCurrentProcess().MainModule.FileName;
                var psi = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" };
                Process.Start(psi);
                Application.Exit();
            }
            catch { if (_boostStatus != null) _boostStatus.Text = "Admin relaunch was cancelled."; }
        }
        void BoostMsg(string t, Color c) { if (_boostStatus != null) { _boostStatus.Text = t; _boostStatus.ForeColor = c; } }

        void ApplyPriority(bool on)
        {
            int hit = 0;
            try { foreach (var pr in Process.GetProcessesByName("RobloxPlayerBeta")) { try { pr.PriorityClass = on ? ProcessPriorityClass.High : ProcessPriorityClass.Normal; hit++; } catch { } pr.Dispose(); } } catch { }
            if (on) BoostMsg(hit > 0 ? "Roblox set to High priority." : (IsAdmin() ? "Roblox isn't running yet — will apply when it starts." : "Couldn't set priority. Try Run as Admin."), hit > 0 ? Theme.Green : Theme.Amber);
            else BoostMsg("Roblox priority restored to Normal.", Theme.TextDim);
        }
        void ApplyPower(bool on)
        {
            if (on)
            {
                if (!_powerActive) _savedScheme = GetActiveScheme();
                bool ok = SetScheme(HighPerfGuid); if (ok) _powerActive = true;
                BoostMsg(ok ? "Performance power plan active." : (IsAdmin() ? "Couldn't switch power plan on this PC." : "Power plan needs admin. Try Run as Admin."), ok ? Theme.Green : Theme.Amber);
            }
            else
            {
                if (_powerActive && _savedScheme != null) { SetScheme(_savedScheme); _savedScheme = null; }
                _powerActive = false; BoostMsg("Original power plan restored.", Theme.TextDim);
            }
        }
        string GetActiveScheme()
        {
            try
            {
                var psi = new ProcessStartInfo("powercfg", "/getactivescheme") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using (var pr = Process.Start(psi)) { string o = pr.StandardOutput.ReadToEnd(); pr.WaitForExit(2000); var m = System.Text.RegularExpressions.Regex.Match(o, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}"); if (m.Success) return m.Value; }
            }
            catch { }
            return null;
        }
        bool SetScheme(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return false;
            try { var psi = new ProcessStartInfo("powercfg", "/setactive " + guid) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true }; using (var pr = Process.Start(psi)) { string err = pr.StandardError.ReadToEnd(); pr.WaitForExit(2000); return string.IsNullOrWhiteSpace(err); } }
            catch { return false; }
        }
        void RevertBoostsOnExit()
        {
            try { if (_powerActive && _savedScheme != null) SetScheme(_savedScheme); } catch { }
            try { if (_boostPriorityOn) foreach (var pr in Process.GetProcessesByName("RobloxPlayerBeta")) { try { pr.PriorityClass = ProcessPriorityClass.Normal; } catch { } pr.Dispose(); } } catch { }
        }

        double ReadCpu()
        {
            try
            {
                if (!Native.GetSystemTimes(out var i, out var k, out var u)) return _lastCpu;
                ulong idle = Native.FT(i), kern = Native.FT(k), usr = Native.FT(u);
                ulong dIdle = idle - _pIdle, dKern = kern - _pKern, dUsr = usr - _pUsr;
                _pIdle = idle; _pKern = kern; _pUsr = usr;
                ulong sys = dKern + dUsr;
                if (sys > 0 && (dKern + dUsr) >= dIdle) _lastCpu = (1.0 - (double)dIdle / sys) * 100.0;
                if (_lastCpu < 0) _lastCpu = 0; if (_lastCpu > 100) _lastCpu = 100;
            }
            catch { }
            return _lastCpu;
        }
        int ReadRam() { try { var m = new Native.MEMORYSTATUSEX(); if (Native.GlobalMemoryStatusEx(m)) return (int)m.dwMemoryLoad; } catch { } return 0; }
        void UpdateMonitor()
        {
            double cpu = ReadCpu();
            if (_panBoost == null || !_panBoost.Visible) return;
            if (_bCpu != null) _bCpu.Text = cpu.ToString("0") + "%";
            if (_bRam != null) _bRam.Text = ReadRam() + "%";
            if (_bRoblox != null)
            {
                string st = "Not running"; Color c = Theme.TextDim;
                try { var rp = Process.GetProcessesByName("RobloxPlayerBeta"); if (rp.Length > 0) { ProcessPriorityClass pc = ProcessPriorityClass.Normal; try { pc = rp[0].PriorityClass; } catch { } st = "Running · " + pc; c = Theme.Green; } foreach (var pr in rp) pr.Dispose(); } catch { }
                _bRoblox.Text = st; _bRoblox.ForeColor = c;
            }
            if (_bTimer != null) { try { uint mn, mx, cur; if (Native.NtQueryTimerResolution(out mn, out mx, out cur) == 0) _bTimer.Text = (cur / 10000.0).ToString("0.00") + " ms"; else _bTimer.Text = "~1 ms"; } catch { _bTimer.Text = "~1 ms"; } }
        }

        Label MiniLabel(string t, int x, int y) { return new Label { AutoSize = true, Location = new Point(Sc(x), Sc(y)), Text = t, Font = Fx.F(8.5f, FontStyle.Bold), ForeColor = Theme.TextDim, BackColor = Color.Transparent }; }
        Label ValueLabel(string t, int x, int y, int w, ContentAlignment a = ContentAlignment.MiddleLeft) { return new Label { AutoSize = false, Size = new Size(Sc(w), Sc(20)), Location = new Point(Sc(x), Sc(y)), Text = t, Font = Fx.F(9.5f), ForeColor = Theme.Text, BackColor = Color.Transparent, TextAlign = a, AutoEllipsis = true }; }
        Label RightLabel(string t, int x, int y, int w, Color c) { return new Label { AutoSize = false, Size = new Size(Sc(w), Sc(18)), Location = new Point(x, y), Text = t, Font = Fx.F(9.5f, FontStyle.Bold), ForeColor = c, BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleRight }; }

        void ChangeAccent(Color c)
        {
            Theme.SetAccent(c);
            if (_tuneBtn != null) _tuneBtn.Fill = Theme.Accent;
            if (_cpsVal != null) _cpsVal.ForeColor = Theme.AccentSoft;
            if (_stPeak != null) _stPeak.ForeColor = Theme.AccentSoft;
            if (_bindBtn != null && !_binding) _bindBtn.Fg = Theme.AccentSoft;
            if (_keyBtn != null) _keyBtn.Fg = Theme.AccentSoft;
            if (_profBtn != null) _profBtn.Fg = Theme.AccentSoft;
            if (_posLabel != null) _posLabel.ForeColor = Theme.AccentSoft;
            if (_trimPct != null) _trimPct.ForeColor = Theme.AccentSoft;
            if (_heroXpLeft != null) _heroXpLeft.ForeColor = Theme.AccentSoft;
            if (_fpsValLbl != null) _fpsValLbl.ForeColor = Theme.AccentSoft;
            foreach (var s in _swatches) { s.Selected = s.Color.ToArgb() == c.ToArgb(); s.Invalidate(); }
            _startFillCur = _armed ? Theme.Red : Theme.Accent;
            ApplyAccentIcons();
            Invalidate(true);
            if (_overlay != null && !_overlay.IsDisposed) _overlay.Invalidate();
            SaveSettings();
        }

        void SetOverlay(bool show)
        {
            _set.ShowOverlay = show;
            if (show)
            {
                if (_overlay == null || _overlay.IsDisposed) { _overlay = new OverlayForm(); _overlay.Moved = () => SaveSettings(); }
                _overlay.Show();
                if (_set.OverlayX == 0 && _set.OverlayY == 0) PlaceOverlayDefault();
                else _overlay.Location = new Point(_set.OverlayX, _set.OverlayY);
            }
            else if (_overlay != null) _overlay.Hide();
            SaveSettings();
        }
        void PlaceOverlayDefault() { var wa = Screen.FromControl(this).WorkingArea; _overlay.Location = new Point(wa.Right - _overlay.Width - 24, wa.Bottom - _overlay.Height - 24); }

        void StartTimers()
        {
            if (_ui == null) { _ui = new System.Windows.Forms.Timer { Interval = 55 }; _ui.Tick += (s, e) => SlowTick(); _ui.Start(); }
            if (_anim == null) { _anim = new System.Windows.Forms.Timer { Interval = 16 }; _anim.Tick += (s, e) => AnimTick(); _anim.Start(); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveSettings();
            RevertBoostsOnExit();
            _engineRunning = false;
            try { if (_engine != null) _engine.Join(300); } catch { }
            if (_holding) ReleaseUp();
            Native.timeEndPeriod(1);
            if (_overlay != null && !_overlay.IsDisposed) { _overlay.Close(); _overlay.Dispose(); }
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            if (_winHicon != IntPtr.Zero) Native.DestroyIcon(_winHicon);
            if (_trayHicon != IntPtr.Zero) Native.DestroyIcon(_trayHicon);
            try { if (_sndArm != null) _sndArm.Dispose(); if (_sndDisarm != null) _sndDisarm.Dispose(); } catch { }
            base.OnFormClosing(e);
        }
        protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); using (var p = new Pen(Theme.Border, _s)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1); }

        // ── ENGINE ──
        double NowMs() { return _hrt.Elapsed.TotalMilliseconds; }
        bool RobloxForeground()
        {
            IntPtr h = Native.GetForegroundWindow();
            if (h == _fgH) return _fgRoblox;
            _fgH = h; bool r = false;
            try { int pid; Native.GetWindowThreadProcessId(h, out pid); if (pid != 0) using (var pr = Process.GetProcessById(pid)) r = pr.ProcessName.IndexOf("Roblox", StringComparison.OrdinalIgnoreCase) >= 0; } catch { }
            _fgRoblox = r; return r;
        }
        void PressDown()
        {
            if (_output == 1) Native.keybd_event((byte)_keyVk, 0, 0, UIntPtr.Zero);
            else { uint d = _button == Btn.Right ? Native.MOUSEEVENTF_RIGHTDOWN : Native.MOUSEEVENTF_LEFTDOWN; Native.mouse_event(d, 0, 0, 0, UIntPtr.Zero); }
        }
        void ReleaseUp()
        {
            if (_output == 1) Native.keybd_event((byte)_keyVk, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            else { uint u = _button == Btn.Right ? Native.MOUSEEVENTF_RIGHTUP : Native.MOUSEEVENTF_LEFTUP; Native.mouse_event(u, 0, 0, 0, UIntPtr.Zero); }
        }
        void DoClickOnce()
        {
            if (_output == 0 && _useFixedPos)
            {
                Native.POINT prev = default; bool got = _returnCursor && Native.GetCursorPos(out prev);
                Native.SetCursorPos(_posX, _posY);
                PressDown(); ReleaseUp();
                if (got) Native.SetCursorPos(prev.X, prev.Y);
            }
            else { PressDown(); ReleaseUp(); }
            Interlocked.Increment(ref _clicks);
        }
        void HoldStart() { if (_output == 0 && _useFixedPos) Native.SetCursorPos(_posX, _posY); PressDown(); _holding = true; Interlocked.Increment(ref _clicks); }
        void HoldStop() { if (_holding) { ReleaseUp(); _holding = false; } }

        void EngineLoop()
        {
            bool prevKey = false, prevPanic = false; int count = 0;
            double winStart = NowMs(), nextClick = 0;
            var rng = new Random();
            while (_engineRunning)
            {
                // rebinding capture
                if (_binding)
                {
                    if ((Native.GetAsyncKeyState(0x1B) & 0x8000) != 0) { _binding = false; _pendingBind = -1; }
                    else if (!_bindArmed) { if ((Native.GetAsyncKeyState(0x01) & 0x8000) == 0) _bindArmed = true; }
                    else { for (int vk = 0x02; vk <= 0xFE; vk++) if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0) { _pendingBind = vk; _binding = false; break; } }
                    HoldStop(); _actualCps = 0; Thread.Sleep(4); continue;
                }
                // position capture
                if (_capturingPos)
                {
                    if ((Native.GetAsyncKeyState(0x1B) & 0x8000) != 0) { _capturingPos = false; }
                    else if ((Native.GetAsyncKeyState(0x0D) & 0x8000) != 0) { Native.POINT pt; if (Native.GetCursorPos(out pt)) { _capX = pt.X; _capY = pt.Y; _posCaptured = true; } _capturingPos = false; }
                    HoldStop(); _actualCps = 0; Thread.Sleep(5); continue;
                }
                // panic key — instant disarm
                bool panic = _panicVk != 0 && (Native.GetAsyncKeyState(_panicVk) & 0x8000) != 0;
                if (panic && !prevPanic) { _armed = false; _active = false; HoldStop(); }
                prevPanic = panic;

                bool keyDown = _bindVk != 0 && (Native.GetAsyncKeyState(_bindVk) & 0x8000) != 0;
                if (!_armed) { _active = false; _gateWait = false; _actualCps = 0; HoldStop(); prevKey = keyDown; Thread.Sleep(1); continue; }

                if (_mode == Mode.Hold) { if (keyDown != _active) { _active = keyDown; if (_active) { nextClick = NowMs(); count = 0; winStart = NowMs(); } } }
                else { if (keyDown && !prevKey) { _active = !_active; if (_active) { nextClick = NowMs(); count = 0; winStart = NowMs(); } } }
                prevKey = keyDown;

                bool gateOpen = !_robloxOnly || RobloxForeground();
                bool go = _active && gateOpen;

                if (_action == 1) // HOLD action
                {
                    if (go && !_holding) HoldStart();
                    else if (!go && _holding) HoldStop();
                    _gateWait = _active && !gateOpen;
                    _actualCps = 0;
                    Thread.Sleep(3);
                    continue;
                }

                // CLICK action
                if (go)
                {
                    _gateWait = false;
                    double now = NowMs();
                    if (now >= nextClick)
                    {
                        DoClickOnce(); count++;
                        int cps = _targetCps;
                        if (_randomize && _rangePlus > 0) cps = _targetCps + rng.Next(-_rangePlus, _rangePlus + 1);
                        if (cps < 1) cps = 1;
                        double iv = 1000.0 / cps;
                        if (_humanize) { iv += (rng.NextDouble() * 2 - 1) * Jitter; if (iv < 1) iv = 1; }
                        nextClick += iv; if (now - nextClick > iv) nextClick = now + iv;
                    }
                    double el = NowMs() - winStart;
                    if (el >= 300) { _actualCps = (float)(count * 1000.0 / el); count = 0; winStart = NowMs(); }
                    double rem = nextClick - NowMs();
                    if (rem > 1.5) Thread.Sleep(1); else Thread.SpinWait(40);
                }
                else if (_active && !gateOpen) { _gateWait = true; _actualCps = 0; nextClick = NowMs(); count = 0; winStart = NowMs(); Thread.Sleep(3); }
                else { _gateWait = false; _actualCps = 0; Thread.Sleep(1); }
            }
        }

        // ── HANDLERS ──
        void SetArmed(bool on) { _armed = on; if (_start != null) { _start.Text = on ? "DISABLE CLICKER" : "ENABLE CLICKER"; if (!on && _graph != null) _graph.Reset(); } }
        void ApplyCps(int v) { _targetCps = v; if (_cpsVal != null) _cpsVal.Text = v + " CPS"; if (_hero != null) { _hero.Target = v; _hero.Invalidate(); } if (_graph != null) _graph.Target = v; }
        void UpdateRangeLabel() { if (_rangeVal != null) _rangeVal.Text = "± " + _rangePlus + " CPS"; }
        void BeginBind(int target) { _bindTarget = target; _bindArmed = false; _pendingBind = 0; _binding = true; var b = target == 1 ? _panicBtn : target == 2 ? _keyBtn : _bindBtn; if (b != null) { b.Text = "PRESS ANY KEY…"; b.Fg = Theme.Amber; b.Invalidate(); } }
        void BeginPosCapture() { _posCaptured = false; _capturingPos = true; if (_posBtn != null) { _posBtn.Text = "MOVE • PRESS ENTER"; _posBtn.Fg = Theme.Amber; _posBtn.Invalidate(); } }

        void SyncUiToState()
        {
            if (_hero != null) { _hero.Target = _targetCps; _hero.Armed = _armed; _hero.BindName = VkPretty(_bindVk); _hero.Invalidate(); }
            if (_graph != null) _graph.Target = _targetCps;
            if (_start != null) { _start.Text = _armed ? "DISABLE CLICKER" : "ENABLE CLICKER"; _start.Invalidate(); }
            UpdateRangeLabel();
        }

        void SlowTick()
        {
            int pb = _pendingBind;
            if (pb != 0)
            {
                _pendingBind = 0;
                int t = _bindTarget;
                if (pb > 0)
                {
                    if (t == 1) _panicVk = pb; else if (t == 2) _keyVk = pb; else _bindVk = pb;
                    SaveSettings();
                }
                var b = t == 1 ? _panicBtn : t == 2 ? _keyBtn : _bindBtn;
                if (b != null) { b.Text = VkPretty(t == 1 ? _panicVk : t == 2 ? _keyVk : _bindVk); b.Fg = t == 1 ? Theme.Red : Theme.AccentSoft; b.Invalidate(); }
            }
            if (_posCaptured) { _posCaptured = false; _posX = _capX; _posY = _capY; if (_posLabel != null) _posLabel.Text = _posX + ", " + _posY; if (_posBtn != null) { _posBtn.Text = "SET POSITION"; _posBtn.Fg = Theme.Text; _posBtn.Invalidate(); } SaveSettings(); }
            else if (!_capturingPos && _posBtn != null && _posBtn.Text.StartsWith("MOVE")) { _posBtn.Text = "SET POSITION"; _posBtn.Fg = Theme.Text; _posBtn.Invalidate(); }

            bool hold = _action == 1;
            bool dispActive = hold ? _holding : (_active && !_gateWait);
            if (_armed != _prevArmedSound) { if (_set.SoundEnabled) { try { var sp = _armed ? _sndArm : _sndDisarm; if (sp != null) sp.Play(); } catch { } } _prevArmedSound = _armed; }
            if (_hero != null) { _hero.Target = _targetCps; _hero.Armed = _armed; _hero.Waiting = _gateWait; _hero.Hold = hold; _hero.BindName = VkPretty(_bindVk); }
            if (_start != null && (_start.Text == "DISABLE CLICKER") != _armed) { _start.Text = _armed ? "DISABLE CLICKER" : "ENABLE CLICKER"; _start.Invalidate(); }
            if (_graph != null) _graph.Push(dispActive && !hold ? _actualCps : 0f);
            if (_actualCps > _peakCps) _peakCps = _actualCps;
            if (_trayToggle != null) _trayToggle.Text = _armed ? "Disable" : "Enable";

            if (_panStats != null && _panStats.Visible)
            {
                long c = Interlocked.Read(ref _clicks);
                double secs = (DateTime.Now - _sessionStart).TotalSeconds;
                if (_stClicks != null) _stClicks.Text = c.ToString("N0");
                if (_stTime != null) { var ts = TimeSpan.FromSeconds(secs); _stTime.Text = ts.Hours > 0 ? string.Format("{0}:{1:00}:{2:00}", ts.Hours, ts.Minutes, ts.Seconds) : string.Format("{0}:{1:00}", ts.Minutes, ts.Seconds); }
                if (_stPeak != null) _stPeak.Text = _peakCps.ToString("0.0");
                if (_stAvg != null) _stAvg.Text = (secs > 1 ? c / secs : 0).ToString("0.0");
            }

            UpdateMonitor();
            bool rbx = false;
            try { var a = Process.GetProcessesByName("RobloxPlayerBeta"); rbx = a.Length > 0; foreach (var pr in a) pr.Dispose(); } catch { }
            if (rbx && !_robloxWasRunning && _autoOptimize)
            {
                if (_boostPowerOn) ApplyPower(true);
                if (_fpsCapEnabled && !_fpsApplied) ApplyFpsNow();
            }
            if (rbx && _boostPriorityOn)
            {
                try { foreach (var pr in Process.GetProcessesByName("RobloxPlayerBeta")) { try { if (pr.PriorityClass != ProcessPriorityClass.High) pr.PriorityClass = ProcessPriorityClass.High; } catch { } pr.Dispose(); } } catch { }
            }
            if (rbx && _autoOptimize && _fpsCapEnabled && !_fpsApplied) ApplyFpsNow();
            if (!rbx && _robloxWasRunning)
            {
                if (_autoOptimize && _powerActive) ApplyPower(false);
                _fpsApplied = false;
            }
            _robloxWasRunning = rbx;
        }

        void AnimTick()
        {
            _animActual += (_actualCps - _animActual) * 0.25f;
            if (Math.Abs(_animActual - _actualCps) < 0.02f) _animActual = _actualCps;
            _pulsePhase += 0.10f; if (_pulsePhase > Math.PI * 2) _pulsePhase -= (float)(Math.PI * 2);
            float pulse = (float)((Math.Sin(_pulsePhase) + 1) / 2);
            Color target = _armed ? Theme.Red : Theme.Accent;
            _startFillCur = Fx.Lerp(_startFillCur, target, 0.18f);
            if (_start != null) { _start.Fill = _startFillCur; _start.Invalidate(); }

            bool hold = _action == 1;
            bool dispActive = hold ? _holding : (_active && !_gateWait);
            if (_hero != null) { _hero.DisplayActual = _animActual; _hero.Pulse = pulse; _hero.Active = dispActive; _hero.Hold = hold; _hero.Waiting = _gateWait; _hero.Armed = _armed; _hero.Invalidate(); }
            if (_overlay != null && !_overlay.IsDisposed && _overlay.Visible) _overlay.UpdateState(_animActual, dispActive, _gateWait, _armed, hold, pulse);

            if (_animPanel != null && !_animPanel.IsDisposed)
            {
                _panelEase += 0.16f;
                float e = 1f - (float)Math.Pow(1 - Math.Min(1f, _panelEase), 3); // easeOutCubic
                int off = (int)Math.Round((1 - e) * Sc(16));
                _animPanel.Top = _panelTargetTop + off;
                if (_panelEase >= 1f) { _animPanel.Top = _panelTargetTop; _animPanel = null; }
            }
        }

        // ── BENCHMARK ──
        void RunBenchmark(bool autoTune)
        {
            _tuneResult.ForeColor = Theme.Amber; _tuneResult.Text = autoTune ? "Auto-tuning…" : "Calibrating…";
            var th = new Thread(() =>
            {
                if (autoTune)
                {
                    int best = 30; double bestStab = 0;
                    for (int cps = 30; cps <= 35; cps++) { double stab; MeasureStability(cps, 500, out stab); if (stab >= 97) { best = cps; bestStab = stab; } }
                    int pick = best; double pstab = bestStab;
                    try { BeginInvoke((Action)(() => { _cps.Value = pick; _tuneResult.ForeColor = Theme.Green; _tuneResult.Text = "Locked " + pick + " CPS  •  " + pstab.ToString("0.0") + "% stability"; })); } catch { }
                }
                else
                {
                    double stab; double achieved = MeasureStability(_targetCps, 1500, out stab);
                    string verdict = stab >= 97 ? "rock solid" : stab >= 90 ? "good" : "inconsistent — try lowering";
                    try { BeginInvoke((Action)(() => { _tuneResult.ForeColor = stab >= 90 ? Theme.Green : Theme.Amber; _tuneResult.Text = achieved.ToString("0.0") + " CPS  •  " + stab.ToString("0.0") + "% stable  •  " + verdict; })); } catch { }
                }
            });
            th.IsBackground = true; th.Priority = ThreadPriority.Highest; th.Start();
        }
        double MeasureStability(int cps, int durationMs, out double stability)
        {
            double iv = 1000.0 / cps; var sw = Stopwatch.StartNew();
            double next = 0, last = -1; int count = 0; var gaps = new List<double>();
            while (sw.Elapsed.TotalMilliseconds < durationMs)
            {
                double now = sw.Elapsed.TotalMilliseconds;
                if (now >= next) { if (last >= 0) gaps.Add(now - last); last = now; count++; next += iv; if (now - next > iv) next = now + iv; }
                double rem = next - sw.Elapsed.TotalMilliseconds;
                if (rem > 1.5) Thread.Sleep(1); else Thread.SpinWait(40);
            }
            double achieved = count * 1000.0 / durationMs;
            double variance = 0; foreach (var x in gaps) { double d = x - iv; variance += d * d; }
            if (gaps.Count > 0) variance /= gaps.Count;
            stability = Math.Max(0, Math.Min(100, 100.0 - (Math.Sqrt(variance) / iv * 100.0)));
            return achieved;
        }
        void UpdateTimerLabel()
        {
            try { uint mn, mx, cur; if (Native.NtQueryTimerResolution(out mn, out mx, out cur) == 0) { double ms = cur / 10000.0; _sysTimer.Text = ms.ToString("0.00") + " ms · Optimized"; return; } } catch { }
            _sysTimer.Text = "~1 ms · Optimized";
        }
        static string GetCpuName()
        {
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")) { var v = k != null ? k.GetValue("ProcessorNameString") as string : null; if (!string.IsNullOrEmpty(v)) return v.Trim(); } } catch { }
            return "Unknown processor";
        }
        static string VkPretty(int vk)
        {
            switch (vk)
            {
                case 0x01: return "MOUSE 1"; case 0x02: return "MOUSE 2"; case 0x04: return "MOUSE 3";
                case 0x05: return "MOUSE 4"; case 0x06: return "MOUSE 5"; case 0x20: return "SPACE";
                case 0x10: return "SHIFT"; case 0x11: return "CTRL"; case 0x12: return "ALT";
                case 0x09: return "TAB"; case 0x0D: return "ENTER"; case 0x1B: return "ESC";
            }
            if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
            if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
            if (vk >= 0x70 && vk <= 0x7B) return "F" + (vk - 0x6F);
            return ((Keys)vk).ToString().ToUpper();
        }
    }
}
