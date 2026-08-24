using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Context;

/// <summary>
/// One principal dimension of the caller — a role they hold, the tenant they belong to, or their
/// own user identity. A caller is described by several of these at once.
/// </summary>
/// <remarks>
/// Identity comparison is deliberately case-insensitive. Identities arrive from JWT claims and
/// other token sources whose casing this library does not control, so comparing them ordinally
/// would fail open: a rule targeting <c>Role:Manager</c> would not match a caller whose token
/// says <c>manager</c>, and a rule written to deny access would silently not apply. Field paths
/// in <c>PolicyFragment</c> are matched the same way, for the same reason. The accepted tradeoff
/// is that two identities differing only in case are treated as the same subject.
/// <para>
/// Only comparison is case-insensitive. <see cref="Identity"/> is stored exactly as supplied —
/// trimmed, but with its original casing — so <see cref="ToString"/> and any future explain
/// output still show the operator what they actually typed.
/// </para>
/// </remarks>
public sealed class DwSubject : IEquatable<DwSubject>
{
    /// <summary>
    /// Initializes a subject.
    /// </summary>
    /// <param name="kind">The dimension this subject describes.</param>
    /// <param name="identity">
    /// The identity within that dimension. Ignored and stored as an empty string when
    /// <paramref name="kind"/> is <see cref="DwSubjectKind.Global"/>.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="identity"/> is null or whitespace for any kind other than
    /// <see cref="DwSubjectKind.Global"/>.
    /// </exception>
    public DwSubject(DwSubjectKind kind, string identity)
    {
        if (kind != DwSubjectKind.Global && string.IsNullOrWhiteSpace(identity))
        {
            throw new ArgumentException($"A subject of kind '{kind}' requires an identity.", nameof(identity));
        }

        Kind = kind;
        Identity = kind == DwSubjectKind.Global ? string.Empty : identity.Trim();
    }

    /// <summary>The dimension this subject describes.</summary>
    public DwSubjectKind Kind { get; }

    /// <summary>The identity within that dimension, or an empty string for Global.</summary>
    public string Identity { get; }

    /// <inheritdoc />
    public bool Equals(DwSubject? other) =>
        other is not null && other.Kind == Kind &&
        string.Equals(other.Identity, Identity, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DwSubject);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(Identity));

    /// <inheritdoc />
    public override string ToString() =>
        Kind == DwSubjectKind.Global ? "Global" : $"{Kind}:{Identity}";
}
