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
    /// <exception cref="ArgumentException">Thrown when <paramref name="fieldPath"/> is blank.</exception>
    public PolicyFragment(
        string fieldPath,
        PolicyFeature features,
        PolicyEffect effect,
        PolicyLevel level,
        PolicySource source,
        int priority = 0,
        object? payload = null)
    {
        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            throw new ArgumentException("A fragment requires a field path.", nameof(fieldPath));
        }

        FieldPath = fieldPath.Trim();
        Features = features;
        Effect = effect;
        Level = level;
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Priority = priority;
        Payload = payload;
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
}
