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

/// <summary>
/// An application's own collection type. It is declared outside the <c>System</c> namespace, so it
/// is recognizable as a collection only through the <see cref="IEnumerable{T}"/> it inherits.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
internal class SecuredPagedList<T> : List<T>
{
}

/// <summary>
/// An application's own collection of simple values: a collection by contract, but its element is
/// a value rather than a navigation.
/// </summary>
internal class SecuredNameList : List<string>
{
}

/// <summary>
/// A DTO holding navigations typed as the application's own collection types rather than an array
/// or a BCL collection.
/// </summary>
internal class SecuredPagedInvoiceDto
{
    public int Id { get; set; }

    /// <summary>A custom collection whose element type carries a policy attribute.</summary>
    public SecuredPagedList<SecuredLineDto> Lines { get; set; } = new();

    /// <summary>A custom collection of strings: a value, not a navigation into <see cref="char"/>.</summary>
    public SecuredNameList Reviewers { get; set; } = new();

    /// <summary>A keyed collection whose element is a <see cref="KeyValuePair{TKey,TValue}"/>.</summary>
    public Dictionary<string, SecuredLineDto> LinesBySku { get; set; } = new();
}
