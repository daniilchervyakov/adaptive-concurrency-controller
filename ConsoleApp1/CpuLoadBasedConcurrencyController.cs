internal class CpuLoadBasedConcurrencyController
{
    private readonly CpuLoadBasedConcurrencyControllerSettings _settings; 

    private readonly ConcurrencyController _concurrencyController;
    private readonly PidController _pidController;
    
    private TimeSpan _lastTimestamp;

    private double _targetDop = 0;
    
    public CpuLoadBasedConcurrencyController(CpuLoadBasedConcurrencyControllerSettings settings)
    {
        _settings = settings;

        _concurrencyController = new ConcurrencyController(settings.DegreeOfParallelismSettings.Initial);
        _pidController = new PidController(settings.PidControllerSettings);
        
        CpuMonitor.ValueUpdated += CpuMonitorOnValueUpdated;
    }

    private void CpuMonitorOnValueUpdated(CpuMonitor.CpuLoadData cpuLoadData)
    {
        var dt = cpuLoadData.Timestamp - _lastTimestamp;
        
        var pidOutput = _pidController.Update(cpuLoadData.CpuLoad, _settings.TargetCpuLoad, dt.TotalSeconds);
        
        var dopSettings = _settings.DegreeOfParallelismSettings;

        var limits = (dopSettings.Min, dopSettings.Max);

        _targetDop += pidOutput * dt.TotalSeconds;

        var newTargetDop = (int)Math.Round(Math.Clamp(_targetDop, limits.Min, limits.Max));

        Metrics.TargetDegreeOfParallelism = newTargetDop;
        
        _concurrencyController.SetTargetDegreeOfParallelism(newTargetDop);

        _lastTimestamp = cpuLoadData.Timestamp;
    }

    public void Pass()
        => _concurrencyController.Pass();
}