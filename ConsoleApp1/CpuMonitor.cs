using System.Diagnostics;
using System.Runtime.InteropServices;

internal class CpuMonitor
{
    private const int HistorySize = 10;
    
    private static readonly Process CurrentProcess = Process.GetCurrentProcess();
    private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();
    
    private static readonly LinkedList<double> History = new();

    private static TimeSpan _lastTimestamp;
    private static long _lastCycles;

    public static event Action<CpuLoadData>? ValueUpdated;
    
    static CpuMonitor()
    {
        new Thread(UpdateValueLoop)
        {
            Priority = ThreadPriority.Highest
        }.Start();
    }

    private static void UpdateValueLoop()
    {
        while (true)
        {
            var currentTimestamp = Stopwatch.Elapsed;
            var cycles = QueryProcessCycleTime();
            
            var wallclockTime = currentTimestamp - _lastTimestamp;
            var cyclesDelta = cycles - _lastCycles;

            var cpuTime = cyclesDelta / 4.3E9;
            var cpuLoad = cpuTime / wallclockTime.TotalSeconds;

            History.AddLast(cpuLoad);

            if(History.Count > HistorySize)
                History.RemoveFirst();
            
            var averageLoad = History.Average();
            
            Metrics.CpuLoad = averageLoad;
            
            _lastCycles = cycles;
            _lastTimestamp = currentTimestamp;
            
            ValueUpdated?.Invoke(new CpuLoadData(currentTimestamp, averageLoad));
            
            Thread.Sleep(100);
        }
    }
        
    private static unsafe long QueryProcessCycleTime()
    {
        long res = 0;
        var b = QueryProcessCycleTime(CurrentProcess.Handle, &res);

        return res;
    }
    
    [DllImport("kernel32.dll")]
    private static extern unsafe bool QueryProcessCycleTime(IntPtr handle, long* cycleTime);
    
    public readonly record struct CpuLoadData(TimeSpan Timestamp, double CpuLoad);
}