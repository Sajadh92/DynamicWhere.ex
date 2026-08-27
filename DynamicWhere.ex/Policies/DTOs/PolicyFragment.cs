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
    /// <param name="alias">
    /// The public name this fragment gives the field, or null when it says nothing about naming.
    /// </param>
    /// <param name="forced">
    /// A predicate to add to every query on this type, or null when this fragment forces none.
    /// </param>
    /// <param name="requiredOperators">
    /// The operators that satisfy a filtering requirement on this field, or null when this fragment
    /// requires no filter. An empty list is a requirement nothing satisfies, which is not the same
    /// thing as no requirement.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="fieldPath"/> is blank, or names no segment once normalized, or
    /// when <paramref name="alias"/> is supplied and is blank, dotted, or the wildcard.
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
        IReadOnlyList<Operator>? allowedOperators = null,
        string? alias = null,
        ForcedPredicate? forced = null,
        IReadOnlyList<Operator>? requiredOperators = null)
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
        Alias = NormalizeAlias(alias);
        Forced = forced;
        RequiredOperators = requiredOperators;

        // Refused at the source rather than ignored at the point of use. Ignoring a nonsensical
        // fragment is how a misconfigured rule becomes invisible, and both of these are nonsense on
        // the wildcard: one name cannot stand for every field, and a demand that the caller filter
        // on every field refuses every query ever written against the type.
        if (IsWildcard && Alias is not null)
        {
            throw new ArgumentException(
                "An alias cannot be attached to the wildcard path: one name cannot stand for every " +
                "field.", nameof(alias));
        }

        if (IsWildcard && RequiredOperators is not null)
        {
            throw new ArgumentException(
                "A filtering requirement cannot be attached to the wildcard path: it would demand a " +
                "filter on every field of the type.", nameof(requiredOperators));
        }

        // A forced predicate names its own field, which is what lets a wildcard fragment carry one.
        // A field-specific fragment carrying a predicate aimed elsewhere is a confusion, and the
        // wrong reading of it injects a filter on a column nobody named.
        if (Forced is not null
            && !IsWildcard
            && !string.Equals(FieldPath, NormalizePath(Forced.FieldPath), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The fragment for '{FieldPath}' carries a forced predicate on " +
                $"'{Forced.FieldPath}'. A field-specific fragment may only force a predicate on its " +
                "own field.", nameof(forced));
        }
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

    /// <summary>
    /// The public name this fragment gives the field, or null when it says nothing about naming.
    /// </summary>
    /// <remarks>
    /// Elected rather than accumulated: a field has one name per caller, decided by the same
    /// ranking the effects use, so a sealed attribute beats a runtime rule and an overridable one
    /// does not. Two names for one field is not a question with an answer.
    /// </remarks>
    public string? Alias { get; }

    /// <summary>
    /// A predicate to add to every query on this type, or null when this fragment forces none.
    /// </summary>
    /// <remarks>
    /// Collected rather than elected, and that difference is deliberate. A conjunction of forced
    /// predicates can only narrow the result, so an additional one is always safe; electing a single
    /// winner would let a low-authority rule silently discard a sealed tenant scope, which is the
    /// one outcome the level ordering exists to prevent.
    /// </remarks>
    public ForcedPredicate? Forced { get; }

    /// <summary>
    /// The operators that satisfy a filtering requirement on this field, or null when this fragment
    /// requires no filter.
    /// </summary>
    /// <remarks>
    /// Null and empty differ. Null is "no requirement"; empty is "a requirement nothing satisfies",
    /// which refuses every query on the type rather than quietly permitting one.
    /// </remarks>
    public IReadOnlyList<Operator>? RequiredOperators { get; }

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

    /// <summary>
    /// Reduces an alias to the form it is matched in, refusing the spellings that could shadow
    /// something else.
    /// </summary>
    /// <remarks>
    /// One validator for every alias, whether it came from an attribute or from a runtime rule, so
    /// the two cannot be held to different standards. Three spellings are refused rather than
    /// normalized away:
    /// <list type="bullet">
    /// <item><description>
    /// Blank. Null already means "this fragment says nothing about naming", so accepting a blank
    /// string as the same thing would make a misconfigured rule invisible.
    /// </description></item>
    /// <item><description>
    /// Dotted. A caller's name is resolved before it reaches <c>Validate&lt;T&gt;()</c>, at which
    /// point a dotted alias is indistinguishable from a real navigation path and could shadow one.
    /// </description></item>
    /// <item><description>
    /// The wildcard. One name cannot stand for every field.
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <param name="alias">The raw alias, or null.</param>
    /// <returns>The trimmed alias, or null when none was supplied.</returns>
    /// <exception cref="ArgumentException">Thrown when the alias is blank, dotted, or the wildcard.</exception>
    private static string? NormalizeAlias(string? alias)
    {
        if (alias is null)
        {
            return null;
        }

        string trimmed = alias.Trim();

        if (trimmed.Length == 0)
        {
            throw new ArgumentException(
                "An alias cannot be blank. Leave it null to say nothing about naming.", nameof(alias));
        }

        if (trimmed.Contains('.', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The alias '{trimmed}' contains a path separator. An alias is resolved before a " +
                "field path is validated, so a dotted one cannot be told apart from a navigation " +
                "path and could shadow a field the caller meant.",
                nameof(alias));
        }

        if (trimmed == Wildcard)
        {
            throw new ArgumentException(
                "An alias cannot be the wildcard: one name cannot stand for every field.",
                nameof(alias));
        }

        return trimmed;
    }
}
