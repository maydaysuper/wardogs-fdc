using System.Diagnostics;

namespace WardogsNavigator.Services;

public sealed class PerformanceMonitorService
{
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _lastCpu = Process.GetCurrentProcess().TotalProcessorTime;
    private DateTime _lastUtc = DateTime.UtcNow;

    public PerformanceSnapshot Sample()
    {
        _process.Refresh();

        var now = DateTime.UtcNow;
        var cpu = _process.TotalProcessorTime;
        var wallMs = Math.Max(1, (now - _lastUtc).TotalMilliseconds);
        var cpuMs = Math.Max(0, (cpu - _lastCpu).TotalMilliseconds);

        _lastUtc = now;
        _lastCpu = cpu;

        var cpuPercent = Math.Clamp(
            cpuMs / wallMs /
            Math.Max(1, Environment.ProcessorCount) *
            100.0,
            0,
            100);

        return new PerformanceSnapshot
        {
            CpuPercent = cpuPercent,
            WorkingSetMb = _process.WorkingSet64 / 1024.0 / 1024.0,
            PrivateMemoryMb = _process.PrivateMemorySize64 / 1024.0 / 1024.0,
            ThreadCount = _process.Threads.Count
        };
    }
}

public sealed class PerformanceSnapshot
{
    public double CpuPercent { get; init; }
    public double WorkingSetMb { get; init; }
    public double PrivateMemoryMb { get; init; }
    public int ThreadCount { get; init; }
}
