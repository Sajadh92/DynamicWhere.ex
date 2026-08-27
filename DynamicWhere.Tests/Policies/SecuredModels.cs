using System.Collections;
using DynamicWhere.ex.Enums;
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

/// <summary>
/// A DTO whose navigations are wrapped in more than one collection layer. Each of these holds
/// <see cref="SecuredLineDto"/> at the bottom, so a walker that stops after peeling a single layer
/// lands on collection plumbing — or on nothing — and never reaches the denied field.
/// </summary>
internal class SecuredJaggedInvoiceDto
{
    public int Id { get; set; }

    /// <summary>An array whose element is the application's own collection type.</summary>
    public SecuredPagedList<SecuredLineDto>[] Lines { get; set; } =
        Array.Empty<SecuredPagedList<SecuredLineDto>>();

    /// <summary>A BCL collection nested inside another: not an array at any level.</summary>
    public List<List<SecuredLineDto>> Batches { get; set; } = new();

    /// <summary>A jagged array: two array layers rather than an array over a collection.</summary>
    public SecuredLineDto[][] Grid { get; set; } = Array.Empty<SecuredLineDto[]>();

    /// <summary>An array of a collection of simple values: still a value, not a navigation.</summary>
    public SecuredNameList[] Reviewers { get; set; } = Array.Empty<SecuredNameList>();

    /// <summary>A jagged array of a primitive: peeling both layers must still reach a value.</summary>
    public byte[][] Signatures { get; set; } = Array.Empty<byte[]>();

    /// <summary>A jagged array of text: peeling both layers must not walk into <see cref="char"/>.</summary>
    public string[][] Labels { get; set; } = Array.Empty<string[]>();
}

/// <summary>
/// A tree node that implements its own child collection. Ordinary in application code, and the
/// shape that makes an unbounded unwrap non-terminating: its element type is itself.
/// </summary>
internal class SecuredTreeNode : IEnumerable<SecuredTreeNode>
{
    [DwDenied]
    public string Secret { get; set; } = string.Empty;

    /// <inheritdoc />
    public IEnumerator<SecuredTreeNode> GetEnumerator() =>
        Enumerable.Empty<SecuredTreeNode>().GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Half of a two-step collection cycle. Its element type is never itself, so a guard that only
/// compares one unwrap against the next would follow this pair forever.
/// </summary>
internal class SecuredCycleA : IEnumerable<SecuredCycleB>
{
    [DwDenied]
    public string SecretA { get; set; } = string.Empty;

    /// <inheritdoc />
    public IEnumerator<SecuredCycleB> GetEnumerator() =>
        Enumerable.Empty<SecuredCycleB>().GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>The other half of the two-step collection cycle.</summary>
internal class SecuredCycleB : IEnumerable<SecuredCycleA>
{
    [DwDenied]
    public string SecretB { get; set; } = string.Empty;

    /// <inheritdoc />
    public IEnumerator<SecuredCycleA> GetEnumerator() =>
        Enumerable.Empty<SecuredCycleA>().GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A DTO holding the two collection shapes that have no bottom to unwrap to.</summary>
internal class SecuredRecursiveDto
{
    /// <summary>A type whose own element type is itself.</summary>
    public SecuredTreeNode? Children { get; set; }

    /// <summary>The entry point into a two-step collection cycle.</summary>
    public SecuredCycleA? Cycle { get; set; }
}

/// <summary>
/// A type restricting which operators may be used on its fields. Kept apart from
/// <see cref="SecuredEmployee"/> so the fragment counts the provider tests assert stay unchanged.
/// </summary>
internal class SecuredAccount
{
    public int Id { get; set; }

    [DwOperators(Allow = new[] { Operator.Equal, Operator.In })]
    public string NationalId { get; set; } = string.Empty;

    [DwOperators(Deny = new[] { Operator.Contains, Operator.StartsWith })]
    public string Iban { get; set; } = string.Empty;

    public decimal Balance { get; set; }
}

/// <summary>
/// A self-referencing type with no policy attributes at all, so a depth cap can be exercised
/// without a denial reaching the assertion first.
/// </summary>
internal class PlainNode
{
    public string Label { get; set; } = string.Empty;

    public PlainNode? Next { get; set; }
}

/// <summary>
/// Carries a denial but no policy requirement, so a guarded run and an unguarded one can be
/// compared directly. A type marked RequirePolicy cannot stand in: the unguarded half refuses.
/// </summary>
internal class OpenLedger
{
    public int Id { get; set; }

    public string Reference { get; set; } = string.Empty;

    [DwDenied]
    public decimal Amount { get; set; }
}

/// <summary>
/// The shape the injection feature exists for: rows belong to a tenant, and a caller must never see
/// another tenant's. Deliberately not <c>RequirePolicy</c>, so a guarded run and an unguarded one
/// can be compared directly.
/// </summary>
internal class ScopedInvoice
{
    public int Id { get; set; }

    [DwForceWhere(Operator.Equal, ContextValue = "TenantId")]
    public int TenantId { get; set; }

    [DwForceWhere(Operator.Equal, Value = "false")]
    public bool IsDeleted { get; set; }

    [DwAlias("reference")]
    public string Number { get; set; } = string.Empty;

    public decimal Amount { get; set; }
}

/// <summary>
/// A type that demands the caller supply the scope rather than supplying it for them.
/// </summary>
internal class RequiredScopeLedger
{
    public int Id { get; set; }

    [DwRequireWhere]
    public int TenantId { get; set; }

    [DwRequireWhere(Operators = new[] { Operator.GreaterThanOrEqual, Operator.Between })]
    public DateTime OccurredAt { get; set; }

    public decimal Amount { get; set; }
}

/// <summary>
/// Both attributes on one member: the pairing that proves an injected predicate satisfies the
/// requirement it would otherwise fall foul of.
/// </summary>
internal class ForcedAndRequiredLedger
{
    public int Id { get; set; }

    [DwRequireWhere]
    [DwForceWhere(Operator.Equal, ContextValue = "TenantId")]
    public int TenantId { get; set; }

    public decimal Amount { get; set; }
}

/// <summary>Two forced predicates on one member, which compose into a range by conjunction.</summary>
internal class BoundedWindow
{
    [DwForceWhere(Operator.GreaterThanOrEqual, Value = "18")]
    [DwForceWhere(Operator.LessThanOrEqual, Value = "65")]
    public int Age { get; set; }

    [DwAlias("label", Overridable = true)]
    public string Label { get; set; } = string.Empty;
}

/// <summary>Aliases on a root field and through a reference navigation.</summary>
internal class AliasedCustomer
{
    public int Id { get; set; }

    [DwAlias("customer_name")]
    public string Name { get; set; } = string.Empty;

    public AliasedContact? Contact { get; set; }
}

/// <summary>The nested type behind <see cref="AliasedCustomer.Contact"/>.</summary>
internal class AliasedContact
{
    [DwAlias("email_address")]
    public string Email { get; set; } = string.Empty;

    [DwNoOrder]
    [DwAlias("phone_number")]
    public string Phone { get; set; } = string.Empty;
}

/// <summary>
/// An alias colliding with a real property name on the same type. One caller-supplied name, two
/// fields it could mean.
/// </summary>
internal class CollidingAliasDto
{
    public string Salary { get; set; } = string.Empty;

    [DwAlias("Salary")]
    public string Notes { get; set; } = string.Empty;
}

/// <summary>
/// One aliased type reached by two navigations, so its alias stands for two paths and neither is
/// the obvious one.
/// </summary>
internal class TwoContactCustomer
{
    public AliasedContact? Home { get; set; }

    public AliasedContact? Work { get; set; }
}

/// <summary>
/// A forced predicate on a member whose CLR type has no <see cref="DataType"/> counterpart. Refused
/// rather than guessed: the pipeline is about to validate the value against this type.
/// </summary>
internal class UnmappableForce
{
    [DwForceWhere(Operator.Equal, Value = "01:00:00")]
    public TimeSpan Window { get; set; }
}

/// <summary>
/// A field the caller may not filter on that the library filters on anyway. The usual pairing: a
/// caller who may not name a tenant column is exactly the caller who must be confined to one.
/// </summary>
internal class HiddenTenantLedger
{
    public int Id { get; set; }

    [DwNoWhere]
    [DwForceWhere(Operator.Equal, ContextValue = "TenantId")]
    public int TenantId { get; set; }

    public decimal Amount { get; set; }
}

/// <summary>
/// Soft deletion expressed as a null check, which needs no value at all.
/// </summary>
internal class SoftDeletedLedger
{
    public int Id { get; set; }

    [DwForceWhere(Operator.IsNull)]
    public DateTime? DeletedAt { get; set; }

    public decimal Amount { get; set; }
}
