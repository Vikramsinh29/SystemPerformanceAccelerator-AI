namespace SystemPerformanceAccelerator.Desktop.ViewModels;

public sealed class SystemMonitorHistoryBuffer
{
    public const int DefaultCapacity = 60;

    private readonly int _capacity;
    private readonly List<double> _cpuValues = [];
    private readonly List<double> _memoryValues = [];

    public SystemMonitorHistoryBuffer(int capacity = DefaultCapacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 2);
        _capacity = capacity;
    }

    public IReadOnlyList<double> CpuValues => _cpuValues;
    public IReadOnlyList<double> MemoryValues => _memoryValues;
    public int Count => _cpuValues.Count;

    public void Add(double cpuUsagePercent, double memoryUsagePercent)
    {
        Append(_cpuValues, cpuUsagePercent);
        Append(_memoryValues, memoryUsagePercent);
    }

    public void Clear()
    {
        _cpuValues.Clear();
        _memoryValues.Clear();
    }

    private void Append(List<double> values, double value)
    {
        if (values.Count == _capacity)
        {
            values.RemoveAt(0);
        }

        values.Add(Math.Clamp(value, 0, 100));
    }
}
