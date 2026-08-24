using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Types decorated with policy attributes, used by the attribute provider tests. Kept separate
/// from the sales fixture so the existing suite is unaffected by policy metadata.
/// </summary>
[DwEntity(RequirePolicy = true)]
internal class SecuredEmployee
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    [DwDenied]
    public string NationalId { get; set; } = string.Empty;

    [DwNoSelect(Overridable = true)]
    public decimal Salary { get; set; }

    [DwDeny(PolicyFeature.Order | PolicyFeature.Group)]
    public string InternalNotes { get; set; } = string.Empty;

    public SecuredContact? Contact { get; set; }
}

/// <summary>A nested reference navigation carrying its own policy attributes.</summary>
internal class SecuredContact
{
    [DwNoWhere]
    public string Email { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;
}

/// <summary>A type with no policy attributes at all.</summary>
internal class PlainProduct
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
}
