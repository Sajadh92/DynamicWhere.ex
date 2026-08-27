using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// A provider that returns exactly the fragments a test hands it. Lets the precedence matrix be
/// exercised at every level before a real policy store exists, and keeps resolver tests free of
/// reflection and storage concerns.
/// </summary>
internal sealed class FakePolicyProvider : IDwPolicyProvider
{
    private readonly List<PolicyFragment> _fragments = new();

    private string? _onlyForUser;

    /// <summary>
    /// Adds a fragment.
    /// </summary>
    /// <param name="fieldPath">A field path, or <c>"*"</c> for every field.</param>
    /// <param name="features">The features the fragment speaks to.</param>
    /// <param name="effect">What it does to them.</param>
    /// <param name="level">How authoritative it is.</param>
    /// <param name="priority">Tiebreak within the level. Higher wins.</param>
    /// <returns>This provider, for chaining.</returns>
    public FakePolicyProvider Add(
        string fieldPath,
        PolicyFeature features,
        PolicyEffect effect,
        PolicyLevel level,
        int priority = 0)
    {
        PolicySource source = level is PolicyLevel.SealedAttribute or PolicyLevel.OverridableAttribute
            ? PolicySource.FromAttribute($"Fake{effect}Attribute", level == PolicyLevel.SealedAttribute)
            : PolicySource.FromRule($"{level}-{_fragments.Count}", level.ToString());

        _fragments.Add(new PolicyFragment(fieldPath, features, effect, level, source, priority));

        return this;
    }

    /// <summary>
    /// Adds a fragment that restricts operators without refusing anything.
    /// </summary>
    /// <param name="fieldPath">A field path, or <c>"*"</c> for every field.</param>
    /// <param name="level">How authoritative it is.</param>
    /// <param name="operators">The operators it permits.</param>
    /// <returns>This provider, for chaining.</returns>
    public FakePolicyProvider AddOperators(string fieldPath, PolicyLevel level, params Operator[] operators)
    {
        PolicySource source = PolicySource.FromAttribute($"FakeOperators{_fragments.Count}", isSealed: false);

        _fragments.Add(new PolicyFragment(
            fieldPath,
            PolicyFeature.Where,
            PolicyEffect.Allow,
            level,
            source,
            allowedOperators: operators));

        return this;
    }

    /// <inheritdoc />
    public IReadOnlyList<PolicyFragment> GetFragments(Type entityType, DwPolicyContext context) =>
        _onlyForUser is null
        || context.Identities(DwSubjectKind.User).Contains(_onlyForUser, StringComparer.OrdinalIgnoreCase)
            ? _fragments
            : Array.Empty<PolicyFragment>();

    /// <summary>
    /// Restricts every fragment on this provider to one user, so a test can prove the public field
    /// vocabulary varies by caller.
    /// </summary>
    /// <param name="userIdentity">The user whose context sees these fragments.</param>
    /// <returns>This provider, for chaining.</returns>
    public FakePolicyProvider OnlyFor(string userIdentity)
    {
        _onlyForUser = userIdentity;

        return this;
    }

    /// <summary>
    /// Adds a fragment giving a field a public name.
    /// </summary>
    /// <param name="fieldPath">The field being named.</param>
    /// <param name="alias">The public name.</param>
    /// <param name="level">How authoritative it is.</param>
    /// <param name="priority">Tiebreak within the level. Higher wins.</param>
    /// <returns>This provider, for chaining.</returns>
    public FakePolicyProvider AddAlias(
        string fieldPath, string alias, PolicyLevel level, int priority = 0)
    {
        _fragments.Add(new PolicyFragment(
            fieldPath, PolicyFeature.Where, PolicyEffect.Allow, level, SourceFor(level), priority,
            alias: alias));

        return this;
    }

    /// <summary>
    /// Adds a fragment forcing a predicate onto every query for the type.
    /// </summary>
    /// <param name="forced">The predicate to inject.</param>
    /// <param name="level">How authoritative it is.</param>
    /// <returns>This provider, for chaining.</returns>
    public FakePolicyProvider AddForced(ForcedPredicate forced, PolicyLevel level)
    {
        _fragments.Add(new PolicyFragment(
            forced.FieldPath, PolicyFeature.Where, PolicyEffect.Allow, level, SourceFor(level),
            forced: forced));

        return this;
    }

    /// <summary>
    /// Adds a fragment demanding the caller filter on a field.
    /// </summary>
    /// <param name="fieldPath">The field the caller must filter on.</param>
    /// <param name="level">How authoritative it is.</param>
    /// <param name="operators">The operators that satisfy the requirement.</param>
    /// <returns>This provider, for chaining.</returns>
    public FakePolicyProvider AddRequired(
        string fieldPath, PolicyLevel level, params Operator[] operators)
    {
        _fragments.Add(new PolicyFragment(
            fieldPath, PolicyFeature.Where, PolicyEffect.Allow, level, SourceFor(level),
            requiredOperators: operators));

        return this;
    }

    /// <summary>Builds a source of the shape the level implies.</summary>
    private PolicySource SourceFor(PolicyLevel level) =>
        level is PolicyLevel.SealedAttribute or PolicyLevel.OverridableAttribute
            ? PolicySource.FromAttribute($"Fake{level}Attribute", level == PolicyLevel.SealedAttribute)
            : PolicySource.FromRule($"{level}-{_fragments.Count}", level.ToString());
}
