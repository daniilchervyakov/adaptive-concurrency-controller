internal record CpuLoadBasedConcurrencyControllerSettings
{
    public DegreeOfParallelismSettings DegreeOfParallelismSettings { get; init; }
    
    public PidControllerSettings PidControllerSettings { get; init; }
    
    public double TargetCpuLoad { get; init; }
}