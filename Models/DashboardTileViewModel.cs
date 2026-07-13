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
    private string _automationVisualStateText = string.Empty;
    private string _automationFreshnessText = string.Empty;
    private string _accentHex = "#FF64748B";
    private string _valueHex = "#FF172033";
    private DashboardIconKind _iconKind = DashboardIconKind.GenericStatus;
    private DashboardVisualState _visualState = DashboardVisualState.Unavailable;
    private DashboardMotionKind _motionKind = DashboardMotionKind.None;
    private double _normalizedLevel;
    private double _motionPeriodSeconds;
    private bool _isMotionActive;
    private bool _isDataFresh;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Title
    {
        get => _title;
        set
        {
            if (SetField(ref _title, value))
            {
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            if (SetField(ref _value, value))
            {
                OnPropertyChanged(nameof(AutomationName));
            }
        }
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
        set
        {
            if (SetField(ref _unit, value))
            {
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    public string Subtitle
    {
        get => _subtitle;
        set => SetField(ref _subtitle, value);
    }

    public string Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    public string AutomationVisualStateText
    {
        get => _automationVisualStateText;
        set
        {
            if (SetField(ref _automationVisualStateText, value))
            {
                OnPropertyChanged(nameof(AutomationName));
            }
        }
    }

    public string AutomationFreshnessText
    {
        get => _automationFreshnessText;
        set
        {
            if (SetField(ref _automationFreshnessText, value))
            {
                OnPropertyChanged(nameof(AutomationName));
            }
        }
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

    public DashboardIconKind IconKind
    {
        get => _iconKind;
        set => SetField(ref _iconKind, value);
    }

    public DashboardVisualState VisualState
    {
        get => _visualState;
        set => SetField(ref _visualState, value);
    }

    public DashboardMotionKind MotionKind
    {
        get => _motionKind;
        set => SetField(ref _motionKind, value);
    }

    public double NormalizedLevel
    {
        get => _normalizedLevel;
        set => SetField(ref _normalizedLevel, value);
    }

    public double MotionPeriodSeconds
    {
        get => _motionPeriodSeconds;
        set => SetField(ref _motionPeriodSeconds, value);
    }

    public bool IsMotionActive
    {
        get => _isMotionActive;
        set => SetField(ref _isMotionActive, value);
    }

    public bool IsDataFresh
    {
        get => _isDataFresh;
        set => SetField(ref _isDataFresh, value);
    }

    public SolidColorBrush AccentBrush => ToBrush(AccentHex);

    public SolidColorBrush IconBackgroundBrush => ToBrush(WithAlpha(AccentHex, "20"));

    public SolidColorBrush ValueBrush => ToBrush(ValueHex);

    public string AutomationName => JoinNonEmpty(
        ", ",
        Title,
        JoinNonEmpty(" ", Value, Unit),
        Status,
        AutomationVisualStateText,
        AutomationFreshnessText);

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
        AutomationVisualStateText = next.AutomationVisualStateText;
        AutomationFreshnessText = next.AutomationFreshnessText;
        AccentHex = next.AccentHex;
        ValueHex = next.ValueHex;
        IconKind = next.IconKind;
        VisualState = next.VisualState;
        MotionKind = next.MotionKind;
        NormalizedLevel = next.NormalizedLevel;
        MotionPeriodSeconds = next.MotionPeriodSeconds;
        IsMotionActive = next.IsMotionActive;
        IsDataFresh = next.IsDataFresh;
    }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Title) ? Id : Title;
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

    private static string JoinNonEmpty(string separator, params string[] parts)
    {
        return string.Join(
            separator,
            parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part.Trim()));
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
