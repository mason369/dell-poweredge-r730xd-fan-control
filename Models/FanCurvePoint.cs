namespace DellR730xdFanControlCenter;

public sealed class FanCurvePoint
{
    public double TemperatureCelsius { get; set; }

    public double FanPercent { get; set; }

    public FanCurvePoint Clone()
    {
        return new FanCurvePoint
        {
            TemperatureCelsius = TemperatureCelsius,
            FanPercent = FanPercent,
        };
    }
}
