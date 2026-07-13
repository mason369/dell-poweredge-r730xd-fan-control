namespace DellR730xdFanControlCenter;

public sealed class DashboardSnapshotFreshness
{
    public bool IsFresh { get; private set; }

    public void MarkFresh()
    {
        IsFresh = true;
    }

    public void MarkStale()
    {
        IsFresh = false;
    }
}
