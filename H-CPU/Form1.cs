using HCPU.Services;

namespace HCPU.UI;

/// <summary>
/// 主窗口：极简深色主题。UI 层只负责展示 HardwareMonitorService 返回的快照数据，
/// 不包含任何传感器访问逻辑。
/// </summary>
public sealed class Form1 : Form
{
    // ============ 报警阈值 ============
    private const double AlarmTemp = 85.0;      // 超过 85°C 触发红色高亮 + 非阻塞横幅
    private const double AlarmResetTemp = 80.0; // 回落到 80°C 以下解除（滞回，避免闪烁）

    // ============ 深色主题配色 ============
    private static readonly Color Bg = Color.FromArgb(22, 22, 26);
    private static readonly Color CardBg = Color.FromArgb(32, 32, 38);
    private static readonly Color TextMain = Color.FromArgb(235, 235, 240);
    private static readonly Color TextDim = Color.FromArgb(140, 142, 152);
    private static readonly Color AccentRed = Color.FromArgb(229, 57, 53);     // 与图标同色系
    private static readonly Color AlarmRed = Color.FromArgb(255, 82, 82);
    private static readonly Color WarnOrange = Color.FromArgb(255, 167, 38);
    private static readonly Color GridLine = Color.FromArgb(44, 44, 52);
    private static readonly Color BannerBg = Color.FromArgb(74, 20, 22);

    private static readonly Font FontTitle = new("Microsoft YaHei UI", 16F, FontStyle.Bold);
    private static readonly Font FontBody = new("Microsoft YaHei UI", 9.75F);
    private static readonly Font FontSmall = new("Microsoft YaHei UI", 8.5F);
    private static readonly Font FontMono = new("Consolas", 9.75F, FontStyle.Bold);

    private readonly HardwareMonitorService _monitor = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    // 控件引用
    private readonly Label _lblCpuName = new();
    private readonly Label _lblCores = new();
    private readonly Label _lblStatus = new();
    private readonly Panel _banner = new();
    private readonly Label _bannerText = new();
    private readonly DarkDataGridView _grid = new();

    // 预创建的固定行
    private DataGridViewRow _rowLoad = null!;
    private DataGridViewRow _rowClock = null!;
    private DataGridViewRow _rowMaxTemp = null!;

    // 逐核温度行（按传感器名缓存，避免每秒重建整表造成闪烁）
    private readonly Dictionary<string, DataGridViewRow> _tempRows = new(StringComparer.OrdinalIgnoreCase);
    private bool _alarmActive;

    public Form1()
    {
        // ---- 窗体 ----
        Text = "H-CPU · Hardware Monitor";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(520, 560);
        BackColor = Bg;
        ForeColor = TextMain;
        Font = FontBody;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildUi();

        // ---- 管理员权限兜底检查 ----
        // app.manifest 已要求提权；若被手动去掉 manifest，这里给出非阻塞提示而不是崩溃
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        bool isAdmin = new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        if (!isAdmin)
            ShowBanner("Administrator privileges required - right-click the exe and choose 'Run as administrator', otherwise temperature sensors cannot be read.", isAlarm: false);

        // ---- 初始化监控服务 ----
        try
        {
            _monitor.Open();
            var info = _monitor.GetStaticInfo();
            _lblCpuName.Text = info.Name;
            _lblCores.Text = $"{info.PhysicalCores} Physical Cores · {info.LogicalCores} Threads";
        }
        catch (Exception ex)
        {
            _lblCpuName.Text = "Failed to get CPU info";
            _lblCores.Text = ex.Message;
        }

        _timer.Tick += (_, _) => RefreshTick();
        _timer.Start();
        RefreshTick(); // 先立即刷一次，避免首屏空白
    }

    // =====================================================================
    //  UI 构建
    // =====================================================================
    private void BuildUi()
    {
        int margin = 20;

        // ---- 标题 ----
        var lblTitle = new Label
        {
            Text = "H-CPU",
            Font = FontTitle,
            ForeColor = AccentRed,
            AutoSize = true,
            Location = new Point(margin, 14),
        };
        Controls.Add(lblTitle);

        // ---- 静态信息卡片 ----
        var cardStatic = new Panel
        {
            BackColor = CardBg,
            Location = new Point(margin, 62),
            Size = new Size(ClientSize.Width - margin * 2, 66),
        };
        _lblCpuName.SetBounds(14, 10, cardStatic.Width - 28, 24);
        _lblCpuName.ForeColor = TextMain;
        _lblCpuName.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
        _lblCores.SetBounds(14, 36, cardStatic.Width - 28, 20);
        _lblCores.ForeColor = TextDim;
        _lblCores.Font = FontSmall;
        cardStatic.Controls.Add(_lblCpuName);
        cardStatic.Controls.Add(_lblCores);
        Controls.Add(cardStatic);

        // ---- 报警横幅（非阻塞提示，默认隐藏）----
        _banner.BackColor = BannerBg;
        _banner.Visible = false;
        _banner.SetBounds(margin, 138, ClientSize.Width - margin * 2, 34);
        _bannerText.Dock = DockStyle.Fill;
        _bannerText.ForeColor = AlarmRed;
        _bannerText.Font = new Font("Microsoft YaHei UI", 9.75F, FontStyle.Bold);
        _bannerText.TextAlign = ContentAlignment.MiddleLeft;
        _bannerText.Padding = new Padding(10, 0, 10, 0);
        _banner.Controls.Add(_bannerText);
        Controls.Add(_banner);

        // ---- 数据表格 ----
        _grid.SetBounds(margin, _banner.Visible ? 180 : 138,
                        ClientSize.Width - margin * 2, 352);
        SetupGrid();
        Controls.Add(_grid);

        // ---- 底部状态栏 ----
        _lblStatus.SetBounds(margin, ClientSize.Height - 30, ClientSize.Width - margin * 2, 20);
        _lblStatus.ForeColor = TextDim;
        _lblStatus.Font = FontSmall;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(_lblStatus);
    }

    private void SetupGrid()
    {
        _grid.BackgroundColor = CardBg;
        _grid.BorderStyle = BorderStyle.None;
        _grid.EnableHeadersVisualStyles = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.AllowUserToResizeColumns = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _grid.GridColor = GridLine;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(40, 40, 48);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = TextDim;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(40, 40, 48);
        _grid.ColumnHeadersDefaultCellStyle.Font = FontSmall;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = 30;
        _grid.DefaultCellStyle.BackColor = CardBg;
        _grid.DefaultCellStyle.ForeColor = TextMain;
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(48, 48, 58);
        _grid.DefaultCellStyle.SelectionForeColor = TextMain;
        _grid.DefaultCellStyle.Font = FontBody;
        _grid.RowTemplate.Height = 30;

        _grid.Columns.Add("item", "Item");
        _grid.Columns.Add("value", "Current Value");
        _grid.Columns[0].FillWeight = 55;
        _grid.Columns[1].FillWeight = 45;
        _grid.Columns[1].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        _grid.Columns[1].DefaultCellStyle.Font = FontMono;

        // 固定三行：总负载 / 频率 / 最高温度
        _rowLoad = AddRow("CPU Total Load");
        _rowClock = AddRow("CPU Frequency");
        _rowMaxTemp = AddRow("Peak Core Temperature");
        _rowMaxTemp.DefaultCellStyle.Font = new Font("Consolas", 9.75F, FontStyle.Bold);
    }

    private DataGridViewRow AddRow(string name)
    {
        var row = new DataGridViewRow();
        row.Cells.Add(new DataGridViewTextBoxCell { Value = name });
        row.Cells.Add(new DataGridViewTextBoxCell { Value = "—" });
        _grid.Rows.Add(row);
        return row;
    }

    // =====================================================================
    //  每秒刷新
    // =====================================================================
    private void RefreshTick()
    {
        CpuSnapshot snap;
        try
        {
            snap = _monitor.GetSnapshot();
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"Failed to read sensors: {ex.Message}";
            return;
        }

        // ---- 静态/动态总览 ----
        _rowLoad.Cells[1].Value = snap.HasLoad ? $"{snap.TotalLoad:0} %" : "—";
        _rowClock.Cells[1].Value = snap.ClockMhz > 0 ? $"{snap.ClockMhz:0} MHz" : "—";

        // ---- 逐核温度行（首次出现的新传感器自动补行）----
        foreach (var t in snap.Temps)
        {
            if (!_tempRows.TryGetValue(t.Name, out var row))
            {
                row = AddRow($"Temp · {t.Name}");
                _tempRows[t.Name] = row;
            }

            row.Cells[1].Value = $"{t.Value:0.0} °C";

            // 单核超 85°C 即红色高亮该行，75~85°C 橙色预警
            if (t.Value >= AlarmTemp)
            {
                row.DefaultCellStyle.ForeColor = AlarmRed;
                row.Cells[1].Style.ForeColor = AlarmRed;
            }
            else if (t.Value >= 75)
            {
                row.DefaultCellStyle.ForeColor = WarnOrange;
                row.Cells[1].Style.ForeColor = WarnOrange;
            }
            else
            {
                row.DefaultCellStyle.ForeColor = TextMain;
                row.Cells[1].Style.ForeColor = TextMain;
            }
        }

        // ---- 最高温度汇总行 + 报警逻辑（带 5°C 滞回，避免在 85°C 附近闪烁）----
        _rowMaxTemp.Cells[1].Value = snap.HasTempData ? $"{snap.MaxTemp:0.0} °C" : "—";
        _rowMaxTemp.Cells[1].Style.ForeColor = snap.MaxTemp >= AlarmTemp ? AlarmRed : TextMain;

        if (snap.HasTempData && snap.MaxTemp >= AlarmTemp && !_alarmActive)
        {
            _alarmActive = true;
            ShowBanner($"CPU temperature too high: {snap.MaxTemp:0.0} °C (threshold {AlarmTemp:0} °C) - please check your cooling!", isAlarm: true);
        }
        else if ((!snap.HasTempData || snap.MaxTemp <= AlarmResetTemp) && _alarmActive)
        {
            _alarmActive = false;
            _banner.Visible = false;
            _grid.Top = 138; // 横幅隐藏后表格上移补位
            _grid.Height = 352;
        }

        _lblStatus.Text = $"Updated at {DateTime.Now:HH:mm:ss}";
    }

    private void ShowBanner(string message, bool isAlarm)
    {
        _bannerText.Text = isAlarm ? "[!] " + message : message;
        _bannerText.ForeColor = isAlarm ? AlarmRed : WarnOrange;
        _banner.BackColor = isAlarm ? BannerBg : Color.FromArgb(60, 44, 12);
        _banner.Visible = true;
        _grid.Top = _banner.Bottom + 8;
        _grid.Height = 352;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _monitor.Dispose(); // 关闭时卸载内核驱动
        base.OnFormClosed(e);
    }
}

/// <summary>开启双缓冲的 DataGridView，杜绝每秒刷新时的闪烁。</summary>
internal sealed class DarkDataGridView : DataGridView
{
    public DarkDataGridView() => DoubleBuffered = true;
}
