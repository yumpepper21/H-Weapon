using System.Management;
using LibreHardwareMonitor.Hardware;

namespace HCPU.Services;

/// <summary>一次刷新得到的 CPU 快照数据（纯数据，UI 层只管展示）。</summary>
public sealed class CpuSnapshot
{
    /// <summary>CPU 名称，如 "Intel(R) Core(TM) i7-11390H"。</summary>
    public string Name { get; init; } = "未知设备";

    /// <summary>物理核心数。</summary>
    public int PhysicalCores { get; init; }

    /// <summary>逻辑线程数。</summary>
    public int LogicalCores { get; init; }

    /// <summary>CPU 总负载率（%）。0~100，-1 表示传感器不可用。</summary>
    public float TotalLoad { get; init; } = -1;

    /// <summary>当前平均核心频率（MHz）。-1 表示传感器不可用。</summary>
    public float ClockMhz { get; init; } = -1;

    /// <summary>全部温度传感器中的最高温度（°C）。-1 表示无温度数据。</summary>
    public float MaxTemp { get; init; } = -1;

    /// <summary>CPU Package（整体封装）温度，可能为空。</summary>
    public float? PackageTemp { get; init; }

    /// <summary>逐传感器温度列表（Intel 为 Core #1..#n，AMD 为 Tctl/Tdie 等）。</summary>
    public IReadOnlyList<CpuTempSensor> Temps { get; init; } = Array.Empty<CpuTempSensor>();

    public bool HasLoad => TotalLoad >= 0;
    public bool HasTempData => Temps.Count > 0;
}

/// <summary>单个温度传感器读数。</summary>
public sealed record CpuTempSensor(string Name, float Value);

/// <summary>
/// 硬件监控服务：封装 LibreHardwareMonitorLib 的全部底层调用。
/// UI 层（Form1）只与 CpuSnapshot 打交道，不接触任何传感器 API。
/// 注意：本类的 Open() 需要管理员权限才能加载内核驱动，否则读不到温度。
/// </summary>
public sealed class HardwareMonitorService : IDisposable
{
    private readonly Computer _computer;
    private bool _opened;

    public HardwareMonitorService()
    {
        _computer = new Computer
        {
            // 只启用 CPU，保持"极简"：不加载 GPU/主板/硬盘等驱动，启动更快、更干净
            IsCpuEnabled = true,
            IsGpuEnabled = false,
            IsMemoryEnabled = false,
            IsMotherboardEnabled = false,
            IsControllerEnabled = false,
            IsStorageEnabled = false,
            IsNetworkEnabled = false,
            IsPsuEnabled = false,
        };
    }

    /// <summary>打开传感器会话并加载内核驱动（必须以管理员身份运行）。</summary>
    public void Open()
    {
        if (_opened) return;
        _computer.Open();
        _opened = true;
    }

    /// <summary>
    /// 读取一次静态信息（CPU 名称 / 物理核心数 / 逻辑线程数）。
    /// 核心数来自 WMI（Win32_Processor），CPU 名称优先用 WMI，失败时回退到 LHMs。
    /// </summary>
    public CpuStaticInfo GetStaticInfo()
    {
        string name = "";
        int cores = 0, logical = 0;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor");
            foreach (var obj in searcher.Get())
            {
                name = obj["Name"]?.ToString() ?? "";
                cores = Convert.ToInt32(obj["NumberOfCores"]);
                logical = Convert.ToInt32(obj["NumberOfLogicalProcessors"]);
                break; // 只取第一颗物理 CPU（消费级场景足够）
            }
        }
        catch
        {
            // WMI 不可用时走兜底逻辑
        }

        if (logical <= 0) logical = Environment.ProcessorCount;

        // CPU 名称兜底：LibreHardwareMonitor 也能给出 CPU 型号
        if (string.IsNullOrWhiteSpace(name))
        {
            var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            name = cpu?.Name ?? "未知 CPU";
        }

        return new CpuStaticInfo(name, cores, logical);
    }

    /// <summary>刷新并返回当前 CPU 快照（在 UI 线程的 Timer 里每秒调用）。</summary>
    public CpuSnapshot GetSnapshot()
    {
        Open();

        var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (cpu is null)
        {
            return new CpuSnapshot(); // 没识别到 CPU，返回全默认值
        }

        cpu.Update(); // 拉取最新传感器读数

        float totalLoad = -1;
        var coreLoads = new List<float>();
        var clocks = new List<float>();
        var temps = new List<CpuTempSensor>();
        float? packageTemp = null;

        foreach (var sensor in cpu.Sensors)
        {
            if (sensor.Value is null) continue;

            switch (sensor.SensorType)
            {
                case SensorType.Load:
                    // Intel: "CPU Total"；AMD 部分版本只有 "CPU Core #n"，则取均值
                    if (sensor.Name.Equals("CPU Total", StringComparison.OrdinalIgnoreCase))
                        totalLoad = sensor.Value.Value;
                    else if (sensor.Name.StartsWith("CPU Core #", StringComparison.OrdinalIgnoreCase)
                             || sensor.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase))
                        coreLoads.Add(sensor.Value.Value);
                    break;

                case SensorType.Clock:
                    // 只统计核心频率（"Core #1"），排除 Bus Speed 等总线频率
                    if (sensor.Name.StartsWith("Core #", StringComparison.OrdinalIgnoreCase))
                        clocks.Add(sensor.Value.Value);
                    break;

                case SensorType.Temperature:
                    // Intel: "Core #n"、"CPU Package"；AMD: "Core (Tctl/Tdie)"、"CCD1" 等
                    if (sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                        packageTemp = sensor.Value.Value;
                    else
                        temps.Add(new CpuTempSensor(sensor.Name, sensor.Value.Value));
                    break;
            }
        }

        // 总负载兜底：没有 "CPU Total" 时取各核心负载均值
        if (totalLoad < 0 && coreLoads.Count > 0)
            totalLoad = coreLoads.Average();

        // 频率取各核心均值（若个别核心睡眠导致读数为 0，剔除后再平均）
        float clock = -1;
        var validClocks = clocks.Where(c => c > 0).ToList();
        if (validClocks.Count > 0)
            clock = validClocks.Average();

        // 温度兜底：若所有逐核温度都读不到但有 Package 温度，用 Package 填充
        float maxTemp = temps.Count > 0 ? temps.Max(t => t.Value) : (packageTemp ?? -1);
        if (temps.Count == 0 && packageTemp.HasValue)
            temps.Add(new CpuTempSensor("CPU Package", packageTemp.Value));

        return new CpuSnapshot
        {
            Name = cpu.Name ?? "未知 CPU",
            TotalLoad = totalLoad,
            ClockMhz = clock,
            MaxTemp = maxTemp,
            PackageTemp = packageTemp,
            Temps = temps,
        };
    }

    public void Dispose()
    {
        if (_opened)
        {
            _computer.Close(); // 卸载内核驱动，释放资源
            _opened = false;
        }
    }
}

/// <summary>CPU 静态信息（开机后不会变化，只查询一次）。</summary>
public sealed record CpuStaticInfo(string Name, int PhysicalCores, int LogicalCores);
