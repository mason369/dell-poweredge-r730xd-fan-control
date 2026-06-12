using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace DellR730xdFanControlCenter;

public sealed class DashboardTileViewModel : INotifyPropertyChanged
{
    private string _id = string.Empty;
    private string _title = string.Empty;
    private string _value = "--";
    private double _valueFontSize = 26;
    private int _valueMaxLines = 1;
    private string _unit = string.Empty;
    private string _subtitle = string.Empty;
    private string _status = string.Empty;
    private string _accentHex = "#FF64748B";
    private string _valueHex = "#FF172033";
    private double _temperatureIconOpacity;
    private double _fanIconOpacity;
    private double _powerIconOpacity;
    private double _voltageIconOpacity;
    private double _currentIconOpacity;
    private double _healthIconOpacity;
    private bool _isFanAnimated;
    private double _fanRotationSeconds = 2;
    private double _electricalIconOpacity;
    private bool _isElectricalAnimated;
    private double _electricalPulseSeconds = 0.9;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Title
    {
        get => _title;
        set => SetField(ref _title, value);
    }

    public string Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    public double ValueFontSize
    {
        get => _valueFontSize;
        set => SetField(ref _valueFontSize, value);
    }

    public int ValueMaxLines
    {
        get => _valueMaxLines;
        set => SetField(ref _valueMaxLines, value);
    }

    public string Unit
    {
        get => _unit;
        set => SetField(ref _unit, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        set => SetField(ref _subtitle, value);
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string AccentHex
    {
        get => _accentHex;
        set
        {
            if (!SetField(ref _accentHex, value))
            {
                return;
            }

            OnPropertyChanged(nameof(AccentBrush));
            OnPropertyChanged(nameof(IconBackgroundBrush));
        }
    }

    public string ValueHex
    {
        get => _valueHex;
        set
        {
            if (!SetField(ref _valueHex, value))
            {
                return;
            }

            OnPropertyChanged(nameof(ValueBrush));
        }
    }

    public double TemperatureIconOpacity
    {
        get => _temperatureIconOpacity;
        set => SetField(ref _temperatureIconOpacity, value);
    }

    public double FanIconOpacity
    {
        get => _fanIconOpacity;
        set => SetField(ref _fanIconOpacity, value);
    }

    public double PowerIconOpacity
    {
        get => _powerIconOpacity;
        set => SetField(ref _powerIconOpacity, value);
    }

    public double VoltageIconOpacity
    {
        get => _voltageIconOpacity;
        set => SetField(ref _voltageIconOpacity, value);
    }

    public double CurrentIconOpacity
    {
        get => _currentIconOpacity;
        set => SetField(ref _currentIconOpacity, value);
    }

    public double HealthIconOpacity
    {
        get => _healthIconOpacity;
        set => SetField(ref _healthIconOpacity, value);
    }

    public bool IsFanAnimated
    {
        get => _isFanAnimated;
        set => SetField(ref _isFanAnimated, value);
    }

    public double FanRotationSeconds
    {
        get => _fanRotationSeconds;
        set => SetField(ref _fanRotationSeconds, value);
    }

    public double ElectricalIconOpacity
    {
        get => _electricalIconOpacity;
        set => SetField(ref _electricalIconOpacity, value);
    }

    public bool IsElectricalAnimated
    {
        get => _isElectricalAnimated;
        set => SetField(ref _isElectricalAnimated, value);
    }

    public double ElectricalPulseSeconds
    {
        get => _electricalPulseSeconds;
        set => SetField(ref _electricalPulseSeconds, value);
    }

    public SolidColorBrush AccentBrush => ToBrush(AccentHex);

    public SolidColorBrush IconBackgroundBrush => ToBrush(WithAlpha(AccentHex, "20"));

    public SolidColorBrush ValueBrush => ToBrush(ValueHex);

    public void UpdateFrom(DashboardTileViewModel next)
    {
        if (!string.Equals(Id, next.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(LocalizationService.Format("Dashboard.TileUpdateMismatch", next.Id, Id));
        }

        Title = next.Title;
        Value = next.Value;
        ValueFontSize = next.ValueFontSize;
        ValueMaxLines = next.ValueMaxLines;
        Unit = next.Unit;
        Subtitle = next.Subtitle;
        Status = next.Status;
        AccentHex = next.AccentHex;
        ValueHex = next.ValueHex;
        TemperatureIconOpacity = next.TemperatureIconOpacity;
        FanIconOpacity = next.FanIconOpacity;
        PowerIconOpacity = next.PowerIconOpacity;
        VoltageIconOpacity = next.VoltageIconOpacity;
        CurrentIconOpacity = next.CurrentIconOpacity;
        HealthIconOpacity = next.HealthIconOpacity;
        IsFanAnimated = next.IsFanAnimated;
        FanRotationSeconds = next.FanRotationSeconds;
        ElectricalIconOpacity = next.ElectricalIconOpacity;
        IsElectricalAnimated = next.IsElectricalAnimated;
        ElectricalPulseSeconds = next.ElectricalPulseSeconds;
    }

    private static string WithAlpha(string hex, string alpha)
    {
        if (hex.Length != 9 || hex[0] != '#')
        {
            throw new InvalidOperationException(LocalizationService.Format("Dashboard.MetricColorInvalid", hex));
        }

        return $"#{alpha}{hex[3..]}";
    }

    private static SolidColorBrush ToBrush(string hex)
    {
        if (hex.Length != 9 || hex[0] != '#')
        {
            throw new InvalidOperationException(LocalizationService.Format("Dashboard.MetricColorInvalid", hex));
        }

        var alpha = Convert.ToByte(hex.Substring(1, 2), 16);
        var red = Convert.ToByte(hex.Substring(3, 2), 16);
        var green = Convert.ToByte(hex.Substring(5, 2), 16);
        var blue = Convert.ToByte(hex.Substring(7, 2), 16);
        return new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
