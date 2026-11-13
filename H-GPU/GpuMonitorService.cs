using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace HGPU
{
    /// <summary>
    /// Immutable snapshot of all GPU readings for one refresh cycle.
    /// float.NaN means "this sensor is not exposed by the driver".
    /// </summary>
    public class GpuSnapshot
    {
        public bool Found { get; init; }
        public string Name { get; init; } = "Not Detected";
        public float VramTotalBytes { get; init; } = float.NaN;
        public float TempC { get; init; } = float.NaN;
        public float CoreLoadPct { get; init; } = float.NaN;
        public float VramUsedBytes { get; init; } = float.NaN;
        public float FanRpm { get; init; } = float.NaN;
    }

    /// <summary>
    /// Hardware traversal layer: owns the LibreHardwareMonitor Computer instance
    /// and the IVisitor (UpdateVisitor). The UI layer only consumes GpuSnapshot.
    /// </summary>
    public class GpuMonitorService : IDisposable
    {
        private readonly Computer _computer;
        private bool _opened;

        public GpuMonitorService()
        {
            _computer = new Computer
            {
                IsCpuEnabled = false,
                IsGpuEnabled = true,
                IsMemoryEnabled = false,
                IsMotherboardEnabled = false,
                IsControllerEnabled = false,
                IsNetworkEnabled = false,
                IsStorageEnabled = false,
                IsPsuEnabled = false,
                IsBatteryEnabled = false
            };
        }

        /// <summary>
        /// Opens the kernel driver and starts sensor polling. Must be called once before Update().
        /// </summary>
        public void Open()
        {
            if (_opened) return;
            _computer.Open();                       // installs/starts the ring0 driver
            _opened = true;
        }

        /// <summary>
        /// Refreshes every hardware object via the IVisitor and builds a snapshot
        /// of the primary (dedicated) GPU. NVIDIA cards are preferred; if none is
        /// found it falls back to the first GPU reported by the system.
        /// </summary>
        public GpuSnapshot Update()
        {
            if (!_opened) Open();

            var visitor = new UpdateVisitor();
            _computer.Accept(visitor);

            // GPU hardware: NVIDIA/AMD entries are dedicated cards,
            // GpuIntel is the integrated GPU (filtered out).
            var gpus = _computer.Hardware
                .Where(h => h.HardwareType is HardwareType.GpuNvidia
                                            or HardwareType.GpuAmd
                                            or HardwareType.GpuIntel)
                .ToList();

            if (gpus.Count == 0)
                return new GpuSnapshot { Found = false };

            // Prefer the dedicated GPU (NVIDIA MX450) over the Intel iGPU.
            var target = gpus.FirstOrDefault(IsDiscrete) ?? gpus[0];

            // Flat sensor list (top-level + sub-hardware such as GPU RAM chips).
            var sensors = EnumerateSensors(target).ToList();

            float temp   = GetSensor(sensors, SensorType.Temperature, "GPU Core");
            float load   = GetSensor(sensors, SensorType.Load, "GPU Core");
            float memUse = GetSensor(sensors, SensorType.SmallData, "GPU Memory Used");
            float memTot = GetSensor(sensors, SensorType.SmallData, "GPU Memory Total");
            float fan    = GetSensor(sensors, SensorType.Fan);

            // Last-resort fallback: some NVIDIA laptop drivers only expose unnamed
            // temperature/load sensors. Pick the most plausible ones.
            if (float.IsNaN(temp)) temp = sensors
                .Where(s => s.SensorType == SensorType.Temperature)
                .OrderBy(s => s.Index)
                .FirstOrDefault()?.Value ?? float.NaN;

            if (float.IsNaN(load)) load = sensors
                .Where(s => s.SensorType == SensorType.Load && !s.Name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.Index)
                .FirstOrDefault()?.Value ?? float.NaN;

            return new GpuSnapshot
            {
                Found          = true,
                Name           = target.Name,
                TempC          = temp,
                CoreLoadPct    = load,
                VramUsedBytes  = memUse,
                VramTotalBytes = memTot,
                FanRpm         = fan
            };
        }

        private static bool IsDiscrete(IHardware hw)
        {
            return hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd;
        }

        private static IEnumerable<ISensor> EnumerateSensors(IHardware hw)
        {
            foreach (var s in hw.Sensors)
                yield return s;
            foreach (var sub in hw.SubHardware)
                foreach (var s in EnumerateSensors(sub))
                    yield return s;
        }

        private static float GetSensor(IEnumerable<ISensor> sensors, SensorType type, string exactName = null)
        {
            var query = sensors.Where(s => s.SensorType == type && s.Value.HasValue);

            ISensor hit = exactName != null
                ? query.FirstOrDefault(s => string.Equals(s.Name, exactName, StringComparison.OrdinalIgnoreCase))
                  ?? query.FirstOrDefault(s => s.Name.Contains(exactName, StringComparison.OrdinalIgnoreCase))
                : query.FirstOrDefault();

            return hit?.Value ?? float.NaN;
        }

        public void Dispose()
        {
            if (_opened) _computer.Close();
        }
    }

    /// <summary>
    /// Classic IVisitor implementation that triggers Hardware.Update() on every node.
    /// </summary>
    public class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) { computer.Traverse(this); }
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware)
                sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
