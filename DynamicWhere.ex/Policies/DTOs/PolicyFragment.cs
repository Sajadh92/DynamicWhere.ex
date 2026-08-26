using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.DTOs;

/// <summary>
/// One contribution to one field's policy, from one source, at one precedence level. Attributes
/// and runtime rules both compile into fragments, so the resolver never learns where a policy
/// came from — only how authoritative it is.
/// </summary>
public sealed class PolicyFragment
{
    /// <summary>The path token meaning "every field of this type".</summary>
    public const string Wildcard = "*";

    /// <summary>
    /// Initializes a fragment.
    /// </summary>
    /// <param name="fieldPath">A field path, or <see cref="Wildcard"/>.</param>
    /// <param name="features">The features this fragment speaks to.</param>
    /// <param name="effect">What it does to those features.</param>
    /// <param name="level">How authoritative it is.</param>
    /// <param name="source">Where it came from, for tracing.</param>
    /// <param name="priority">Tiebreak within a level. Higher wins.</param>
    /// <param name="payload">Strategy detail, unused until masking arrives.</param>
    /// <param name="allowedOperators">
    /// The operators this fragment permits, or null when it says nothing about operators.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="fieldPath"/> is blank, or names no segment once normalized.
    /// </exception>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> is null.</exception>
    public PolicyFragment(
        string fieldPath,
        PolicyFeature features,
        PolicyEffect effect,
        PolicyLevel level,
        PolicySource source,
        int priority = 0,
        object? payload = null,
        IReadOnlyList<Operator>? allowedOperators = null)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            throw new ArgumentException("A fragment requires a field path.", nameof(fieldPath));
        }

        string normalized = NormalizePath(fieldPath);

        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                "A fragment requires a field path with at least one segment.", nameof(fieldPath));
        }

        FieldPath = normalized;
        Features = features;
        Effect = effect;
        Level = level;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Priority = priority;
        Payload = payload;
        AllowedOperators = allowedOperators;
    }

    /// <summary>The field path this fragment addresses, or <see cref="Wildcard"/>.</summary>
    public string FieldPath { get; }

    /// <summary>The features this fragment speaks to.</summary>
    public PolicyFeature Features { get; }

    /// <summary>What this fragment does to those features.</summary>
    public PolicyEffect Effect { get; }

    /// <summary>How authoritative this fragment is.</summary>
    public PolicyLevel Level { get; }

    /// <summary>Where this fragment came from.</summary>
    public PolicySource Source { get; }

    /// <summary>Tiebreak within a level. Higher wins.</summary>
    public int Priority { get; }

    /// <summary>Strategy detail carried opaquely through resolution.</summary>
    public object? Payload { get; }

    /// <summary>
    /// The operators this fragment permits, or null when it says nothing about operators.
    /// </summary>
    /// <remarks>
    /// Typed rather than carried in <see cref="Payload"/>. Reading a restriction back out of an
    /// <see cref="object"/> means an unchecked cast, and a cast that fails yields null — which here
    /// means "no restriction", so a mistake in the carrier type would silently permit every
    /// operator rather than fail. A restriction is also not an effect: it never competes in the
    /// per-feature election, because a restriction attached to a fragment that lost would simply
    /// disappear.
    /// </remarks>
    public IReadOnlyList<Operator>? AllowedOperators { get; }

    /// <summary>True when this fragment addresses every field.</summary>
    public bool IsWildcard => FieldPath == Wildcard;

    /// <summary>
    /// True when this fragment addresses the given path. Comparison is case-insensitive because
    /// field paths arrive from JSON written by hand.
    /// </summary>
    public bool Matches(string fieldPath) =>
        IsWildcard || string.Equals(FieldPath, fieldPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when this fragment speaks to the given feature.</summary>
    public bool Covers(PolicyFeature feature) => (Features & feature) == feature;

    /// <summary>
    /// Reduces a field path to the canonical form used for matching: outer whitespace removed,
    /// each segment trimmed, empty segments dropped.
    /// </summary>
    /// <remarks>
    /// This must agree with how the rest of the library normalizes a property path — see
    /// <c>CacheReflection.ValidatePropertyPathInternal</c>, which a query's own field names pass
    /// through. A fragment stored under one spelling and looked up under another simply does not
    /// match, and a fragment that does not match is access granted. Segment casing needs no
    /// handling here because <see cref="Matches"/> compares case-insensitively.
    /// </remarks>
    /// <param name="fieldPath">The raw path.</param>
    /// <returns>The canonical path, or <see cref="Wildcard"/> unchanged.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="fieldPath"/> is null.</exception>
    public static string NormalizePath(string fieldPath)
    {
        if (fieldPath is null)
        {
            throw new ArgumentNullException(nameof(fieldPath));
        }

        string trimmed = fieldPath.Trim();

        if (trimmed == Wildcard)
        {
            return Wildcard;
        }

        return string.Join('.', trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}
