using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DellR730xdFanControlCenter;

public sealed class FanChannelViewModel : INotifyPropertyChanged
{
    private double _percent;

    public FanChannelViewModel(int index, double percent)
    {
        Index = index;
        _percent = percent;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Index { get; }

    public string DisplayName => LocalizationService.Format("Control.FanDisplayName", Index);

    public string ChineseName => LocalizationService.Format("Control.FanSubtitle", Index);

    public string SetButtonLabel => LocalizationService.T("Control.Set");

    public double Percent
    {
        get => _percent;
        set
        {
            if (_percent == value)
            {
                return;
            }

            _percent = value;
            OnPropertyChanged();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(ChineseName));
        OnPropertyChanged(nameof(SetButtonLabel));
    }
}
