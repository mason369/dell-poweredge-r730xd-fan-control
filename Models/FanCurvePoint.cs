using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DellR730xdFanControlCenter;

public sealed class FanCurvePoint : INotifyPropertyChanged
{
    private double _temperatureCelsius;
    private double _powerWatts;
    private double _fanPercent;

    public event PropertyChangedEventHandler? PropertyChanged;

    public double TemperatureCelsius
    {
        get => _temperatureCelsius;
        set => SetField(ref _temperatureCelsius, value);
    }

    public double PowerWatts
    {
        get => _powerWatts;
        set => SetField(ref _powerWatts, value);
    }

    public double FanPercent
    {
        get => _fanPercent;
        set => SetField(ref _fanPercent, value);
    }

    public FanCurvePoint Clone()
    {
        return new FanCurvePoint
        {
            TemperatureCelsius = TemperatureCelsius,
            PowerWatts = PowerWatts,
            FanPercent = FanPercent,
        };
    }

    private void SetField(ref double field, double value, [CallerMemberName] string? propertyName = null)
    {
        if (field.Equals(value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
