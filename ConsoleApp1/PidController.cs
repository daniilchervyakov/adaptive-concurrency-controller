internal class PidController
{
    private readonly PidControllerSettings _settings;
    
    private double _integral;
    private double _lastDeviation;

    public PidController(PidControllerSettings settings)
    {
        _settings = settings;
    }

    public double Update(double value, double target, double dt)
    {
        var deviation = target - value;
        var derivative = (deviation - _lastDeviation) / dt;
        
        _integral += deviation * dt;
        _lastDeviation = deviation;

        return _settings.Proportional * deviation 
               + _settings.Integral * _integral 
               + _settings.Derivative * derivative;
    }
}