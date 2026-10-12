namespace Clickra.Core.Presentation.Dashboard;

public enum DashboardSection
{
    Overview = 0,
    Convert = 1,
    History = 2,
    Settings = 3,
    About = 4
}

/// <summary>Semantic dashboard navigation shared by Native and Fluent renderers.</summary>
public sealed class DashboardPresentationController
{
    public DashboardSection ActiveSection { get; private set; } = DashboardSection.Overview;

    public void Select(DashboardSection section) => ActiveSection = section;

    public bool TrySelect(string? name)
    {
        if (!Enum.TryParse(name, ignoreCase: true, out DashboardSection section)) return false;
        Select(section);
        return true;
    }
}
