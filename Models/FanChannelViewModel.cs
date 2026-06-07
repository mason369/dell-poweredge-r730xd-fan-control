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

    public string DisplayName => $"Fan {Index}";

    public string ChineseName => $"{Index} 号风扇";

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
}
