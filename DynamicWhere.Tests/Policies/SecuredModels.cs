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

/// <summary>A self-referencing type, used to prove the nested walk terminates.</summary>
internal class SecuredNode
{
    [DwDenied]
    public string Secret { get; set; } = string.Empty;

    public SecuredNode? Next { get; set; }
}

/// <summary>
/// A DTO holding an array-typed collection navigation. EF Core would not map this shape, but the
/// policy attributes are supported on DTOs as well as entities, where an array is ordinary.
/// </summary>
internal class SecuredInvoiceDto
{
    public int Id { get; set; }

    public SecuredLineDto[] Lines { get; set; } = Array.Empty<SecuredLineDto>();

    /// <summary>An array of a primitive: a value, not a navigation into <see cref="byte"/>.</summary>
    public byte[] Signature { get; set; } = Array.Empty<byte>();
}

/// <summary>The element type behind an array-typed navigation.</summary>
internal class SecuredLineDto
{
    [DwDenied]
    public decimal Cost { get; set; }

    public string Sku { get; set; } = string.Empty;
}

/// <summary>A contact reference expressed as an interface, an ordinary shape on a DTO.</summary>
internal interface ISecuredContact
{
    /// <summary>Carries a policy attribute the walker must reach through the interface.</summary>
    [DwDenied]
    string Email { get; }

    /// <summary>Undecorated, so it must produce no fragment.</summary>
    string Phone { get; }
}

/// <summary>
/// A DTO holding an interface-typed reference navigation and a user-defined struct held by value.
/// </summary>
internal class SecuredCustomerDto
{
    public string Name { get; set; } = string.Empty;

    public ISecuredContact? Contact { get; set; }

    public SecuredAuditStamp Audit { get; set; }
}

/// <summary>A user-defined struct carrying its own policy attributes.</summary>
internal struct SecuredAuditStamp
{
    [DwDenied]
    public string? ChangedBy { get; set; }

    public DateTime ChangedAt { get; set; }
}
