using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Resolution;

/// <summary>
/// Merges the fragments supplied by every provider into one immutable <see cref="FieldPolicy"/>.
/// </summary>
/// <remarks>
/// Each feature is decided independently. For one feature, the most authoritative level that
/// supplies any fragment decides it outright; less authoritative levels are discarded rather than
/// merged, which is what makes a sealed attribute absolute and lets a role rule replace an
/// overridable attribute default.
/// </remarks>
public sealed class PolicyResolver
{
    private static readonly PolicyFeature[] Features =
    {
        PolicyFeature.Where, PolicyFeature.Select, PolicyFeature.Order,
        PolicyFeature.Group, PolicyFeature.Aggregate, PolicyFeature.Segment
    };

    private readonly IReadOnlyList<IDwPolicyProvider> _providers;

    /// <summary>
    /// Initializes the resolver.
    /// </summary>
    /// <param name="providers">The fragment sources, in any order.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="providers"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when any element of <paramref name="providers"/> is null.
    /// </exception>
    public PolicyResolver(IEnumerable<IDwPolicyProvider> providers)
    {
        if (providers is null)
        {
            throw new ArgumentNullException(nameof(providers));
        }

        List<IDwPolicyProvider> materialized = providers.ToList();

        // Reported here rather than at the first Resolve: a null element has no type to name, so
        // the position it sits at is the only thing that leads back to the mistake.
        for (int index = 0; index < materialized.Count; index++)
        {
            if (materialized[index] is null)
            {
                throw new ArgumentException(
                    $"The policy provider at index {index} is null. A provider that is not there " +
                    "supplies no fragments, and a field with no fragments is allowed.",
                    nameof(providers));
            }
        }

        _providers = materialized;
    }

    /// <summary>
    /// Resolves the policy for one field of one type, for one caller.
    /// </summary>
    /// <param name="entityType">The type being queried.</param>
    /// <param name="fieldPath">The field path, as it appears after alias resolution.</param>
    /// <param name="context">The caller.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="fieldPath"/> is blank, or names no segment once normalized.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="entityType"/> or <paramref name="context"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a provider returns null, or a null fragment, naming that provider.
    /// </exception>
    public FieldPolicy Resolve(Type entityType, string fieldPath, DwPolicyContext context)
    {
        // Both of these are handed straight to every provider. IDwPolicyProvider is public, and
        // returning empty for a null argument is a natural defensive style for an implementation
        // this library did not write — against such a provider a null here yields no fragments at
        // all, turning a sealed Deny into an Allow with nothing thrown.
        if (entityType is null)
        {
            throw new ArgumentNullException(nameof(entityType));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (string.IsNullOrWhiteSpace(fieldPath))
        {
            throw new ArgumentException("A policy lookup requires a field path.", nameof(fieldPath));
        }

        // Normalize once, here, because this is where paths enter the policy system, and normalize
        // through the same routine PolicyFragment uses at construction. Any difference between the
        // two spellings means a lookup fails to match a fragment that targets the same field — and
        // a Deny fragment that fails to match is access granted.
        string path = PolicyFragment.NormalizePath(fieldPath);

        if (path.Length == 0)
        {
            throw new ArgumentException(
                "A policy lookup requires a field path with at least one segment.", nameof(fieldPath));
        }

        List<PolicyFragment> candidates = new();

        foreach (PolicyFragment fragment in Sweep(entityType, context))
        {
            if (fragment.Matches(path))
            {
                candidates.Add(fragment);
            }
        }

        Dictionary<PolicyFeature, PolicyEffect> effects = new();
        List<PolicySource> sources = new();
        bool isSealed = false;

        foreach (PolicyFeature feature in Features)
        {
            PolicyFragment? winner = Decide(candidates, feature);

            if (winner is null)
            {
                continue;
            }

            effects[feature] = winner.Effect;

            if (!sources.Contains(winner.Source))
            {
                sources.Add(winner.Source);
            }

            isSealed |= winner.Level == PolicyLevel.SealedAttribute;
        }

        return new FieldPolicy(
            path,
            effects,
            sources,
            isSealed,
            IntersectOperators(candidates),
            ElectAlias(candidates),
            CollectForced(candidates),
            ElectRequired(candidates),
            ElectTransform(candidates),
            ElectFacts(candidates));
    }

    /// <summary>
    /// Resolves one field and reports the chain behind every feature of the decision.
    /// </summary>
    /// <param name="entityType">The type being queried.</param>
    /// <param name="fieldPath">The field path, as it appears after alias resolution.</param>
    /// <param name="context">The caller.</param>
    /// <returns>The decision, and why.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="fieldPath"/> is blank.</exception>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="entityType"/> or <paramref name="context"/> is null.
    /// </exception>
    /// <remarks>
    /// Here rather than in whatever displays it, so the chain and the decision come from one
    /// implementation of the four precedence rules. Two implementations would eventually disagree,
    /// and an explanation that contradicts the decision is worse than no explanation.
    /// <para>
    /// It resolves the field a second time rather than threading the candidate list out of
    /// <see cref="Resolve"/>. This is an administrative call, made once per field by an operator
    /// reading a screen, and the alternative is a resolution path that carries diagnostic state on
    /// every query for the benefit of the few that ask.
    /// </para>
    /// </remarks>
    public PolicyExplanation Explain(Type entityType, string fieldPath, DwPolicyContext context)
    {
        FieldPolicy policy = Resolve(entityType, fieldPath, context);

        string path = PolicyFragment.NormalizePath(fieldPath);

        List<PolicyFragment> candidates = new();

        foreach (PolicyFragment fragment in Sweep(entityType, context))
        {
            if (fragment.Matches(path))
            {
                candidates.Add(fragment);
            }
        }

        List<FeatureExplanation> features = new(Features.Length);

        foreach (PolicyFeature feature in Features)
        {
            features.Add(ExplainFeature(candidates, feature, policy));
        }

        return new PolicyExplanation(
            entityType.FullName ?? entityType.Name, policy.FieldPath, policy, features);
    }

    /// <summary>
    /// Sorts every fragment covering one feature into the winner, its equals, and what it outranked.
    /// </summary>
    /// <remarks>
    /// Nothing outranks the winner, so a covering fragment the winner does not outrank is equal to
    /// it on all four passes — the tie Phase 1 recorded as making attribution arbitrary. Fragments
    /// covering another feature appear in neither list: they never entered this contest, and
    /// reporting them would tell an operator a rule lost something it never ran in.
    /// </remarks>
    private static FeatureExplanation ExplainFeature(
        IReadOnlyList<PolicyFragment> candidates, PolicyFeature feature, FieldPolicy policy)
    {
        PolicyFragment? winner = Decide(candidates, feature);

        List<PolicySource> tied = new();
        List<PolicySource> overrode = new();

        if (winner is not null)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                PolicyFragment candidate = candidates[i];

                if (ReferenceEquals(candidate, winner) || !candidate.Covers(feature))
                {
                    continue;
                }

                if (Outranks(winner, candidate))
                {
                    overrode.Add(candidate.Source);
                }
                else
                {
                    tied.Add(candidate.Source);
                }
            }
        }

        return new FeatureExplanation(
            feature,
            policy.EffectFor(feature),
            winner?.Source,
            winner?.Level,
            tied,
            overrode);
    }

    /// <summary>
    /// Resolves everything about a type that cannot be answered one field at a time: the names this
    /// caller may use, the predicates to inject, and the fields this caller must filter on.
    /// </summary>
    /// <param name="entityType">The type being queried.</param>
    /// <param name="context">The caller.</param>
    /// <returns>The type-wide policy, empty when nothing speaks to the type.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="entityType"/> or <paramref name="context"/> is null.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a provider returns null, or a null fragment, naming that provider.
    /// </exception>
    /// <remarks>
    /// One sweep per query, sharing its election helpers with <see cref="Resolve"/>. Two code paths
    /// that agree only by construction is the standing liability in a system where a fragment that
    /// fails to match means access granted, so the sweep and the per-field lookup answer through the
    /// same functions rather than through two implementations of the same rules.
    /// </remarks>
    public TypePolicy ResolveType(Type entityType, DwPolicyContext context)
    {
        if (entityType is null)
        {
            throw new ArgumentNullException(nameof(entityType));
        }

        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        // Grouped by the path each fragment names, because an alias and a requirement are elected
        // per field. Case-insensitive, matching PolicyFragment.Matches: a store spelling a path
        // differently from the attribute that also names it must land in the same contest, or the
        // two would each elect a winner and the more authoritative one would not necessarily apply.
        Dictionary<string, List<PolicyFragment>> byPath = new(StringComparer.OrdinalIgnoreCase);
        List<ForcedPredicate> forced = new();

        foreach (PolicyFragment fragment in Sweep(entityType, context))
        {
            if (fragment.Forced is not null)
            {
                forced.Add(fragment.Forced);
            }

            // A wildcard cannot carry any of the elected properties — PolicyFragment refuses it at
            // construction — so nothing is lost by keying this on the exact path.
            if (fragment.Alias is null && fragment.RequiredOperators is null && fragment.Transform is null)
            {
                continue;
            }

            if (!byPath.TryGetValue(fragment.FieldPath, out List<PolicyFragment>? group))
            {
                group = new List<PolicyFragment>();
                byPath[fragment.FieldPath] = group;
            }

            group.Add(fragment);
        }

        if (byPath.Count == 0 && forced.Count == 0)
        {
            return TypePolicy.Empty;
        }

        Dictionary<string, IReadOnlyList<string>> aliases = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, IReadOnlyList<Operator>> required = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ValueTransform> transforms = new(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, List<PolicyFragment>> entry in byPath)
        {
            string? alias = ElectAlias(entry.Value);

            if (alias is not null)
            {
                // A name accumulates every path it could mean. Two aliases can collide, and one
                // aliased type reached by two navigations gives one name two paths; keeping only
                // the first would resolve the collision by arrival order, which is no resolution.
                if (!aliases.TryGetValue(alias, out IReadOnlyList<string>? paths))
                {
                    aliases[alias] = new List<string> { entry.Key };
                }
                else
                {
                    ((List<string>)paths).Add(entry.Key);
                }
            }

            IReadOnlyList<Operator>? operators = ElectRequired(entry.Value);

            if (operators is not null)
            {
                required[entry.Key] = operators;
            }

            ValueTransform? transform = ElectTransform(entry.Value);

            if (transform is not null)
            {
                transforms[entry.Key] = transform;
            }
        }

        return new TypePolicy(aliases, forced, required, transforms);
    }

    /// <summary>
    /// Reads every fragment every provider has for a type, refusing a provider that misbehaves.
    /// </summary>
    /// <remarks>
    /// A provider returning null fails the lookup closed rather than quietly contributing nothing.
    /// The implementation is not necessarily one this library wrote, and an unattributed
    /// <see cref="NullReferenceException"/> from in here leads nowhere.
    /// </remarks>
    private IEnumerable<PolicyFragment> Sweep(Type entityType, DwPolicyContext context)
    {
        foreach (IDwPolicyProvider provider in _providers)
        {
            IReadOnlyList<PolicyFragment> fragments = provider.GetFragments(entityType, context)
                ?? throw new InvalidOperationException(
                    $"Policy provider '{provider.GetType().FullName}' returned null from " +
                    "GetFragments. Return an empty list instead: a null result cannot be told " +
                    "apart from a field no provider speaks to, and that field would be allowed.");

            foreach (PolicyFragment fragment in fragments)
            {
                if (fragment is null)
                {
                    throw new InvalidOperationException(
                        $"Policy provider '{provider.GetType().FullName}' returned a null " +
                        "fragment. Every element of the returned list must be a real fragment.");
                }

                yield return fragment;
            }
        }
    }

    /// <summary>
    /// Picks the alias the most authoritative fragment gives, or null when none names the field.
    /// </summary>
    /// <remarks>
    /// Elected rather than accumulated: a field has one name per caller. The ranking is the one the
    /// effects use, so a sealed <c>[DwAlias]</c> beats a runtime rule and an overridable one is
    /// replaceable — the same ceiling that governs every other compile-time decision.
    /// </remarks>
    private static string? ElectAlias(IReadOnlyList<PolicyFragment> candidates) =>
        Best(candidates, static f => f.Alias is not null)?.Alias;

    /// <summary>
    /// Picks the satisfying operator set the most authoritative fragment requires, or null when none
    /// requires a filter.
    /// </summary>
    /// <remarks>
    /// Elected for the same reason the alias is, and with the same consequence: a rule cannot lift a
    /// sealed requirement, but it can impose one where no attribute speaks.
    /// </remarks>
    private static IReadOnlyList<Operator>? ElectRequired(IReadOnlyList<PolicyFragment> candidates) =>
        Best(candidates, static f => f.RequiredOperators is not null)?.RequiredOperators;

    /// <summary>
    /// Assembles what is known about a field beyond its access decisions, electing each fact on its
    /// own, or null when no fragment states any of them.
    /// </summary>
    /// <remarks>
    /// Fact by fact rather than as a block, for the reason the transform chain is elected stage by
    /// stage: two attributes decorate one property — <c>[DwDescribe]</c> and
    /// <c>[DwAllowedValues]</c> — and a single winner-takes-all election between them would let
    /// whichever won erase the other. It also means a rule that renames a field for one role keeps
    /// the values the attribute declared instead of silently dropping them.
    /// <para>
    /// The ranking is the one the effects use, so a sealed attribute is a ceiling here as
    /// everywhere else. That is what stops a store from cheapening a field the source code called
    /// expensive, or switching off an audit it declared.
    /// </para>
    /// </remarks>
    private static FieldFacts? ElectFacts(IReadOnlyList<PolicyFragment> candidates)
    {
        string? label = Best(candidates, static f => f.Facts?.Label is not null)?.Facts!.Label;
        string? description =
            Best(candidates, static f => f.Facts?.Description is not null)?.Facts!.Description;
        string? group = Best(candidates, static f => f.Facts?.Group is not null)?.Facts!.Group;
        int? order = Best(candidates, static f => f.Facts?.Order is not null)?.Facts!.Order;
        IReadOnlyList<string>? values =
            Best(candidates, static f => f.Facts?.AllowedValues is not null)?.Facts!.AllowedValues;

        int? cost = ElectCost(candidates);
        PolicyFeature? audited = ElectAudited(candidates);

        if (label is null && description is null && group is null && order is null
            && values is null && cost is null && audited is null)
        {
            return null;
        }

        return new FieldFacts(label, description, group, order, values, cost, audited);
    }

    /// <summary>
    /// Elects a cost weight, taking the dearest of any that tie, or null when nothing weighs the
    /// field.
    /// </summary>
    /// <remarks>
    /// Rank decides first, so a sealed weight cannot be undercut. What rank cannot separate is two
    /// rules at one level disagreeing, and there the ordinary tiebreak is the effect — which means
    /// nothing on a fragment carrying only a weight, and would attribute the answer to whichever
    /// fragment happened to be swept first. The dearer weight wins instead: a caller holding two
    /// entitlements that disagree about cost is charged the higher, which is the same direction
    /// every other tie in this resolver breaks.
    /// </remarks>
    private static int? ElectCost(IReadOnlyList<PolicyFragment> candidates)
    {
        PolicyFragment? best = Best(candidates, static f => f.Facts?.CostWeight is not null);

        if (best is null)
        {
            return null;
        }

        int weight = best.Facts!.CostWeight!.Value;

        for (int i = 0; i < candidates.Count; i++)
        {
            PolicyFragment candidate = candidates[i];

            // Nothing outranks the winner, so a candidate the winner does not outrank is tied with
            // it. Anything the winner does outrank is a level that was discarded, not merged.
            if (candidate.Facts?.CostWeight is int other
                && !Outranks(best, candidate)
                && other > weight)
            {
                weight = other;
            }
        }

        return weight;
    }

    /// <summary>
    /// Elects the audited features, uniting any that tie, or null when nothing audits the field.
    /// </summary>
    /// <remarks>
    /// United rather than picked among ties, for the reason the cost takes the dearest: two
    /// entitlements that each record a different feature both meant that feature recorded, and
    /// dropping one because it was swept second loses a security record with nothing reporting it.
    /// </remarks>
    private static PolicyFeature? ElectAudited(IReadOnlyList<PolicyFragment> candidates)
    {
        PolicyFragment? best = Best(candidates, static f => f.Facts?.AuditedFeatures is not null);

        if (best is null)
        {
            return null;
        }

        PolicyFeature audited = best.Facts!.AuditedFeatures!.Value;

        for (int i = 0; i < candidates.Count; i++)
        {
            PolicyFragment candidate = candidates[i];

            if (candidate.Facts?.AuditedFeatures is PolicyFeature other && !Outranks(best, candidate))
            {
                audited |= other;
            }
        }

        return audited;
    }

    /// <summary>
    /// Assembles the transform chain by electing one winner for each stage, or null when nothing
    /// transforms the field.
    /// </summary>
    /// <remarks>
    /// Per stage, not per chain. A chain elected as a unit would let a runtime rule discard a sealed
    /// mask by supplying any transform at all; electing stage by stage means a rule can add a
    /// truncation on top of that mask and cannot take the mask away.
    /// <para>
    /// Nothing here refuses a chain that combines a replacement with another stage. That is a
    /// configuration error rather than a resolution question, and it is reported by the startup scan
    /// where it can name the type and the member rather than surfacing on a caller's query.
    /// </para>
    /// </remarks>
    private static ValueTransform? ElectTransform(IReadOnlyList<PolicyFragment> candidates)
    {
        MutateStage? mutate = Elect<MutateStage>(candidates, TransformKind.Mutate);
        GeneralizeStage? generalize = Elect<GeneralizeStage>(candidates, TransformKind.Generalize);
        FormatStage? format = Elect<FormatStage>(candidates, TransformKind.Format);
        MaskStage? mask = Elect<MaskStage>(candidates, TransformKind.Mask);
        TruncateStage? truncate = Elect<TruncateStage>(candidates, TransformKind.Truncate);
        DefaultStage? replacement = Elect<DefaultStage>(candidates, TransformKind.Default);

        if (mutate is null && generalize is null && format is null
            && mask is null && truncate is null && replacement is null)
        {
            return null;
        }

        return new ValueTransform(mutate, generalize, format, mask, truncate, replacement);
    }

    /// <summary>Picks the winning fragment for one stage and returns its stage.</summary>
    private static TStage? Elect<TStage>(IReadOnlyList<PolicyFragment> candidates, TransformKind kind)
        where TStage : TransformStage =>
        Best(candidates, f => f.Transform?.Kind == kind)?.Transform as TStage;

    /// <summary>
    /// Gathers every forced predicate that matched, in provider order.
    /// </summary>
    /// <remarks>
    /// Deliberately not an election. Injected predicates are joined by <c>And</c>, so an extra one
    /// can only narrow the result — the same argument that keeps operator restrictions out of the
    /// per-feature contest. Electing a winner here would let a rule at a lower level silently
    /// discard a sealed tenant scope, which is the one outcome the level ordering exists to prevent.
    /// </remarks>
    private static IReadOnlyList<ForcedPredicate>? CollectForced(IReadOnlyList<PolicyFragment> candidates)
    {
        List<ForcedPredicate>? forced = null;

        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].Forced is { } predicate)
            {
                forced ??= new List<ForcedPredicate>();
                forced.Add(predicate);
            }
        }

        return forced;
    }

    /// <summary>
    /// Picks the highest-ranked fragment satisfying a test, or null when none does.
    /// </summary>
    private static PolicyFragment? Best(IReadOnlyList<PolicyFragment> candidates, Func<PolicyFragment, bool> test)
    {
        PolicyFragment? best = null;

        for (int i = 0; i < candidates.Count; i++)
        {
            PolicyFragment candidate = candidates[i];

            if (test(candidate) && (best is null || Outranks(candidate, best)))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Narrows every operator restriction that matched into one permitted set, or null when none
    /// spoke to operators.
    /// </summary>
    /// <remarks>
    /// Deliberately outside the per-feature election above. A restriction is not an effect: the
    /// election keeps one winner per feature and discards the rest, so a restriction riding on a
    /// discarded fragment would silently vanish and the field would accept every operator again.
    /// Intersecting instead means an additional fragment can only ever narrow the set, which is the
    /// only direction that is safe to get wrong.
    /// <para>
    /// An intersection that empties is kept as empty rather than dropped to null. Null means "no
    /// restriction" and would re-permit everything; empty means the restrictions genuinely left no
    /// operator, and filtering on the field is refused.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<Operator>? IntersectOperators(IReadOnlyList<PolicyFragment> candidates)
    {
        List<Operator>? permitted = null;

        for (int i = 0; i < candidates.Count; i++)
        {
            IReadOnlyList<Operator>? restriction = candidates[i].AllowedOperators;

            if (restriction is null)
            {
                continue;
            }

            if (permitted is null)
            {
                permitted = new List<Operator>(restriction);

                continue;
            }

            permitted.RemoveAll(op => !restriction.Contains(op));
        }

        return permitted;
    }

    /// <summary>
    /// Picks the single fragment that decides one feature, or null when none speaks to it.
    /// </summary>
    /// <remarks>
    /// One pass, no allocation. Runs once per feature per field per query, so the staged filtering
    /// this replaced — four intermediate lists and as many closures — cost more than the work it did.
    /// <para>
    /// The ranking is unchanged: level, then specificity, then priority, then strongest effect. See
    /// <see cref="Outranks"/>.
    /// </para>
    /// </remarks>
    private static PolicyFragment? Decide(IReadOnlyList<PolicyFragment> candidates, PolicyFeature feature)
    {
        PolicyFragment? best = null;

        for (int i = 0; i < candidates.Count; i++)
        {
            PolicyFragment candidate = candidates[i];

            if (!candidate.Covers(feature))
            {
                continue;
            }

            if (best is null || Outranks(candidate, best))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> beats <paramref name="incumbent"/> under the four
    /// precedence rules, compared in order.
    /// </summary>
    /// <remarks>
    /// Level first: the most authoritative level wins outright, and weaker levels are discarded
    /// rather than merged — that is what makes a sealed attribute absolute. Then specificity, so a
    /// rule naming the field beats a wildcard and a broad denial can be relaxed field by field
    /// without deleting it. Then priority, highest first, so an operator can author a deliberate
    /// exception. Whatever still ties falls to the strictest effect, which is what makes a caller
    /// holding two conflicting roles land on the stricter of them.
    /// <para>
    /// A fragment tying on all four does not outrank the incumbent, so the earliest-encountered
    /// fragment wins. That matches the filtering this replaced.
    /// </para>
    /// </remarks>
    private static bool Outranks(PolicyFragment candidate, PolicyFragment incumbent)
    {
        if (candidate.Level != incumbent.Level)
        {
            return candidate.Level < incumbent.Level;
        }

        if (candidate.IsWildcard != incumbent.IsWildcard)
        {
            return !candidate.IsWildcard;
        }

        if (candidate.Priority != incumbent.Priority)
        {
            return candidate.Priority > incumbent.Priority;
        }

        return candidate.Effect > incumbent.Effect;
    }
}
