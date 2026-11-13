using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using LibreHardwareMonitor.Hardware;

namespace HGPU
{
    public class Form1 : Form
    {
        // ---- palette -------------------------------------------------------
        private static readonly Color Bg        = Color.FromArgb(18, 18, 20);
        private static readonly Color Card      = Color.FromArgb(28, 28, 32);
        private static readonly Color CardLine  = Color.FromArgb(44, 44, 50);
        private static readonly Color TextDim   = Color.FromArgb(140, 140, 148);
        private static readonly Color TextMain  = Color.FromArgb(232, 232, 236);
        private static readonly Color Accent    = Color.FromArgb(76, 217, 100);   // green
        private static readonly Color AccentBlue= Color.FromArgb(80, 170, 255);
        private static readonly Color AccentVio = Color.FromArgb(170, 120, 255);
        private static readonly Color AccentDim = Color.FromArgb(48, 48, 54);
        private static readonly Color Alarm     = Color.FromArgb(255, 72, 72);    // > 80 C
        private const float TempAlarmC = 80f;

        // ---- services / controls ------------------------------------------
        private readonly GpuMonitorService _service = new();
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

        private PictureBox _logo;
        private Label _titleLabel, _subtitleLabel, _statusLabel;
        private Label _infoGpuName, _infoVram;
        private Gauge _gaugeTemp, _gaugeLoad, _gaugeVram;
        private Label _fanValue;
        private bool _alarmOn;

        public Form1()
        {
            BuildUi();
            _timer.Tick += (_, _) => RefreshSensors();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            try { _service.Open(); } catch { /* driver may need a reboot after install */ }
            RefreshSensors();
            _timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _service.Dispose();
            base.OnFormClosed(e);
        }

        // ====================================================================
        //  UI CONSTRUCTION
        // ====================================================================
        private void BuildUi()
        {
            Text = "H-GPU";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;
            ClientSize = new Size(460, 560);
            Font = new Font("Segoe UI", 9F);
            DoubleBuffered = true;
            Icon = MakeIcon(LoadEmbeddedIcon());

            // ---- header ----------------------------------------------------
            var header = new Panel { Location = new Point(16, 14), Size = new Size(428, 64), BackColor = Color.Transparent, Parent = this };

            _logo = new PictureBox { Size = new Size(64, 48), SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(0, 6), Image = LoadEmbeddedIcon(), Parent = header };

            _titleLabel = new Label { Text = "H-GPU", Font = new Font("Segoe UI Semibold", 16F), ForeColor = TextMain, Location = new Point(78, 2), AutoSize = true, Parent = header };
            _subtitleLabel = new Label { Text = "DEDICATED GPU MONITOR", Font = new Font("Consolas", 7.5F), ForeColor = TextDim, Location = new Point(81, 34), AutoSize = true, Parent = header };

            _statusLabel = new Label { Text = "● INIT", Font = new Font("Consolas", 8F), ForeColor = TextDim, Location = new Point(356, 8), AutoSize = true, Parent = header };

            // ---- static info card -------------------------------------------
            var info = MakeCard(16, 92, 428, 78);
            AddCaption("GPU NAME", info, 14, 12);
            _infoGpuName = AddValue("Detecting...", info, 14, 30, new Font("Segoe UI Semibold", 10.5F));
            AddCaption("VIDEO MEMORY", info, 224, 12);
            _infoVram = AddValue("N/A", info, 224, 30, new Font("Segoe UI Semibold", 10.5F));

            // ---- gauges ------------------------------------------------------
            _gaugeTemp  = MakeGauge(16, 186, "TEMPERATURE", "°C", 100f, Accent);
            _gaugeLoad  = MakeGauge(16, 286, "GPU UTILIZATION", "%", 100f, AccentBlue);
            _gaugeVram  = MakeGauge(16, 386, "VRAM USAGE", "GB", 2f, AccentVio);

            // ---- fan card ----------------------------------------------------
            var fan = MakeCard(16, 486, 428, 58);
            AddCaption("GPU FAN", fan, 14, 12);
            _fanValue = AddValue("N/A", fan, 14, 28, new Font("Segoe UI Semibold", 12F));
            _fanValue.ForeColor = AccentBlue;
        }

        private Panel MakeCard(int x, int y, int w, int h)
        {
            var p = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = Card,
                Parent = this
            };
            p.Paint += (s, e) =>
            {
                using var pen = new Pen(CardLine);
                e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
            };
            return p;
        }

        private static void AddCaption(string text, Panel parent, int x, int y)
        {
            _ = new Label
            {
                Text = text,
                Font = new Font("Consolas", 7.5F),
                ForeColor = TextDim,
                Location = new Point(x, y),
                AutoSize = true,
                BackColor = Color.Transparent,
                Parent = parent
            };
        }

        private static Label AddValue(string text, Panel parent, int x, int y, Font font)
        {
            return new Label
            {
                Text = text,
                Font = font,
                ForeColor = TextMain,
                Location = new Point(x, y),
                AutoSize = true,
                BackColor = Color.Transparent,
                Parent = parent
            };
        }

        private Gauge MakeGauge(int x, int y, string caption, string unit, float max, Color color)
        {
            var g = new Gauge
            {
                Location = new Point(x, y),
                Size = new Size(428, 88),
                Caption = caption,
                Unit = unit,
                Maximum = max,
                BarColor = color,
                BackColor = Card,
                Parent = this
            };
            return g;
        }

        // ====================================================================
        //  DATA REFRESH (Timer @ 1000 ms)
        // ====================================================================
        private void RefreshSensors()
        {
            GpuSnapshot s;
            try { s = _service.Update(); }
            catch { return; }

            if (!s.Found)
            {
                _statusLabel.Text = "● NO GPU";
                _statusLabel.ForeColor = Alarm;
                _infoGpuName.Text = "Not Detected";
                return;
            }

            _statusLabel.Text = _alarmOn ? "● HOT" : "● LIVE";
            _statusLabel.ForeColor = _alarmOn ? Alarm : Accent;

            // static info
            _infoGpuName.Text = s.Name;
            _infoVram.Text = float.IsNaN(s.VramTotalBytes)
                ? "N/A"
                : $"{s.VramTotalBytes / 1024f / 1024f / 1024f:0.0} GB";

            // temperature
            float temp = s.TempC;
            bool hasTemp = !float.IsNaN(temp);
            _alarmOn = hasTemp && temp >= TempAlarmC;
            _gaugeTemp.Set(hasTemp ? temp : 0,
                           hasTemp ? $"{temp:0} °C" : "N/A",
                           _alarmOn ? Alarm : Accent);
            _gaugeTemp.Warning = _alarmOn;

            // utilization
            float load = s.CoreLoadPct;
            bool hasLoad = !float.IsNaN(load);
            _gaugeLoad.Set(hasLoad ? load : 0, hasLoad ? $"{load:0} %" : "N/A", AccentBlue);

            // vram
            bool hasMem = !float.IsNaN(s.VramUsedBytes) && !float.IsNaN(s.VramTotalBytes) && s.VramTotalBytes > 0;
            if (hasMem)
            {
                double used = s.VramUsedBytes / 1073741824.0;
                double tot  = s.VramTotalBytes / 1073741824.0;
                _gaugeVram.Maximum = (float)tot;
                _gaugeVram.Set((float)used, $"{used:0.0} / {tot:0.0} GB", AccentVio);
            }
            else
            {
                _gaugeVram.Set(0, "N/A", AccentVio);
            }

            // fan
            _fanValue.Text = float.IsNaN(s.FanRpm) ? "N/A (not reported)" : $"{s.FanRpm:0} RPM";
        }

        // ====================================================================
        //  CUSTOM GAUGE CONTROL (segmented bar, task-manager style)
        // ====================================================================
        private class Gauge : Control
        {
            public string Caption = "";
            public string Unit = "";
            public float Maximum = 100f;
            public Color BarColor = Accent;
            public bool Warning;

            private float _value;
            private string _text = "N/A";

            public Gauge()
            {
                DoubleBuffered = true;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            }

            public void Set(float value, string text, Color color)
            {
                _value = Math.Clamp(value, 0f, Maximum <= 0 ? 1f : Maximum);
                _text = text;
                BarColor = color;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;

                // caption
                using (var f = new Font("Consolas", 7.5F))
                using (var b = new SolidBrush(TextDim))
                    g.DrawString(Caption, f, b, 14, 10);

                // value
                using (var f = new Font("Segoe UI Semibold", 12F))
                using (var b = new SolidBrush(Warning ? Alarm : TextMain))
                {
                    var size = g.MeasureString(_text, f);
                    g.DrawString(_text, f, b, Width - size.Width - 14, 6);
                }

                // segmented bar
                int segW = 9, gap = 4, count = 32;
                int totalW = segW * count + gap * (count - 1);
                int x0 = (Width - totalW) / 2;
                int y0 = Height - 30;
                int lit = Maximum <= 0 ? 0 : (int)Math.Round(_value / Maximum * count);

                for (int i = 0; i < count; i++)
                {
                    Color c = i < lit
                        ? (Warning && i >= count * 0.8f ? Alarm : BarColor)
                        : AccentDim;
                    using var b = new SolidBrush(c);
                    g.FillRectangle(b, x0 + i * (segW + gap), y0, segW, 14);
                }
            }
        }

        // ====================================================================
        //  EMBEDDED ICON HELPERS
        // ====================================================================
        private static Bitmap LoadEmbeddedIcon()
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("HGPU.gpu-icon.png");
            return stream != null ? new Bitmap(stream) : new Bitmap(1, 1);
        }

        private static Icon MakeIcon(Bitmap bmp)
        {
            try { return Icon.FromHandle(bmp.GetHicon()); }
            catch { return SystemIcons.Application; }
        }
    }
}
