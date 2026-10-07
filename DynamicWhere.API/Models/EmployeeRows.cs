using DynamicWhere.ex.Policies.Attributes;

namespace DynamicWhere.API.Models;

/// <summary>
/// An employee card, projected from <see cref="Employee"/> and read whole.
/// </summary>
/// <remarks>
/// New in 3.5.0: <see cref="DwEntityAttribute.RefuseSelects"/>. A card is shown as one unit, and a caller
/// narrowing it would get a row reporting an empty string for every member they did not name, which reads
/// as data. The policy refuses a non-empty <c>Selects</c> for it with <c>SelectsRefused</c>, and because the
/// refusal is the policy's own it is traced, audited and simulated. Paired with <c>RequirePolicy</c>,
/// because an unguarded query never reads the flag.
/// </remarks>
[DwEntity(RequirePolicy = true, RefuseSelects = true)]
public class EmployeeCardRow
{
    public Guid Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    public string Position { get; set; } = string.Empty;
}

/// <summary>
/// An employee work queue, projected from <see cref="Employee"/> and paged by a field many rows share.
/// </summary>
/// <remarks>
/// New in 3.5.0: <see cref="DwEntityAttribute.DefaultOrderAsTiebreak"/>. A caller paging by
/// <c>Department</c>, which every member of a team shares, leaves the order of a team's rows to the
/// database, and PostgreSQL may break those ties differently on every page, so a row can show on two pages
/// and another on none. The declared default follows the caller's orders and ends on the key, so the order
/// is total.
/// </remarks>
[DwEntity(RequirePolicy = true, DefaultOrder = "LastName, Id", DefaultOrderAsTiebreak = true)]
public class EmployeeQueueRow
{
    public Guid Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    public string Position { get; set; } = string.Empty;
}
