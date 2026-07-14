namespace DellR730xdFanControlCenter;

public sealed class VisualizationWheelScrollState
{
    public const long BurstTimeoutMilliseconds = 500;

    private double? _targetOffset;
    private long _lastInputTimestamp;

    public double Accumulate(
        double currentOffset,
        double deltaY,
        double scrollableHeight,
        long inputTimestamp)
    {
        if (!double.IsFinite(currentOffset) ||
            !double.IsFinite(deltaY) ||
            !double.IsFinite(scrollableHeight) ||
            scrollableHeight < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaY), "Scroll offsets and wheel distance must be finite, and the scrollable height cannot be negative.");
        }

        var startsNewBurst = !_targetOffset.HasValue ||
            inputTimestamp < _lastInputTimestamp ||
            inputTimestamp - _lastInputTimestamp > BurstTimeoutMilliseconds;
        var baseOffset = startsNewBurst
            ? Math.Clamp(currentOffset, 0, scrollableHeight)
            : _targetOffset.GetValueOrDefault();

        _targetOffset = Math.Clamp(baseOffset + deltaY, 0, scrollableHeight);
        _lastInputTimestamp = inputTimestamp;
        return _targetOffset.Value;
    }
}
