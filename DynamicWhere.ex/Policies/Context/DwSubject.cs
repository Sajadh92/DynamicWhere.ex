using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Context;

/// <summary>
/// One principal dimension of the caller — a role they hold, the tenant they belong to, or their
/// own user identity. A caller is described by several of these at once.
/// </summary>
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
        string.Equals(other.Identity, Identity, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DwSubject);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Kind, Identity);

    /// <inheritdoc />
    public override string ToString() =>
        Kind == DwSubjectKind.Global ? "Global" : $"{Kind}:{Identity}";
}
