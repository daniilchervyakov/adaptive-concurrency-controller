// See https://aka.ms/new-console-template for more information

using System.Diagnostics;

var settings = new CpuLoadBasedConcurrencyControllerSettings()
{
    PidControllerSettings = new()
    {
        Proportional = 10,
        Derivative = 2
    },
    TargetCpuLoad = 7,
    DegreeOfParallelismSettings = new()
    {
        Min = 5,
        Max = 2000,
        Initial = 0
    }
};

Metrics.TargetCpuLoad = settings.TargetCpuLoad;

var controller = new CpuLoadBasedConcurrencyController(settings);

var iterations = 100;

for (var i = 1; i <= 3000; i++)
{
    new Thread(Work)
        {
            IsBackground = false
        }
        .Start();
}

Task.Run(async () =>
{
    while (true)
    {
        iterations = int.Parse(Console.ReadLine());
        Metrics.Iterations = iterations;
    }
});

void Work()
{
    while (true)
    {
        controller.Pass();
        Interlocked.Increment(ref Metrics.ThreadsInCriticalSection);
        
        Thread.Sleep(50);

        Thread.SpinWait(iterations);

        Interlocked.Decrement(ref Metrics.ThreadsInCriticalSection);
    }
}

while (true)
{
    Thread.Sleep(200);
    Metrics.Present();
}

static class Metrics
{
    public static int ThreadsInCriticalSection;
    public static int TargetDegreeOfParallelism;
    public static double CpuLoad;
    public static double TargetCpuLoad;
    public static int Iterations;

    public static void Present()
    {
        Console.Clear();

        Console.WriteLine(
            $"Load iterations: {Iterations} \n"    
                + $"Threads in critical section: {ThreadsInCriticalSection} \n"
                + $"Target degree of parallelism: {TargetDegreeOfParallelism} \n"
                + $"Cpu load (cores): {Math.Round(CpuLoad, 2):F} \n"
                + $"Target cpu load (cores): {Math.Round(TargetCpuLoad, 2):F}");
    }
}