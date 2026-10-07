using DynamicWhere.ex.Policies.Attributes;

namespace DynamicWhere.Tests.Policies;

// The two row types PolicyBootstrap exposes for PolicyEndpoint350Tests. Declared apart from that suite,
// because the floor leg compiles the bootstrap and drops the suite with the rest of the ASP.NET Core graph.

/// <summary>A row read whole: a caller's Selects for it is refused (3.5.0).</summary>
[DwEntity(RefuseSelects = true)]
public class EpCardRow
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;
}

/// <summary>A queue whose default ends a caller's orders, less a field no caller may order by (3.5.0).</summary>
[DwEntity(DefaultOrder = "Rank desc, Code desc, Id", DefaultOrderAsTiebreak = true)]
public class EpQueueRow
{
    public int Id { get; set; }

    public int Status { get; set; }

    public string Code { get; set; } = string.Empty;

    [DwNoOrder]
    public int Rank { get; set; }
}
