using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Resolution;

/// <summary>
/// Produces policy fragments by reflecting over the attributes on a type.
/// </summary>
/// <remarks>
/// The result is identical for every caller, so it is computed once per type and cached. An
/// attribute with <c>Overridable = false</c> lands at <see cref="PolicyLevel.SealedAttribute"/> and
/// nothing at runtime can replace it; one with <c>Overridable = true</c> lands at
/// <see cref="PolicyLevel.OverridableAttribute"/>, the least authoritative level, and acts only as
/// a default.
/// </remarks>
public sealed class AttributePolicyProvider : IDwPolicyProvider
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<PolicyFragment>> Cache = new();

    /// <summary>
    /// How many navigation segments a generated field path may contain. Matches the default
    /// navigation-depth cap, so the provider never produces a path the sanitizer would reject.
    /// </summary>
    public const int MaxDepth = 4;

    /// <inheritdoc />
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entityType"/> is null.</exception>
    public IReadOnlyList<PolicyFragment> GetFragments(Type entityType, DwPolicyContext context)
    {
        if (entityType == null)
        {
            throw new ArgumentNullException(nameof(entityType));
        }

        return Cache.GetOrAdd(entityType, Build);
    }

    /// <summary>
    /// Reflects over one type, turning every policy attribute into a fragment. Reference
    /// navigations and collection element types are walked so that dotted paths such as
    /// <c>Contact.Email</c> carry their own policy.
    /// </summary>
    /// <remarks>
    /// The result is handed to every caller and lives in a process-wide cache, so it is wrapped
    /// before it leaves: a caller who cast it back to <see cref="List{T}"/> and removed a fragment
    /// would be granting access to a denied field for the lifetime of the process.
    /// </remarks>
    private static IReadOnlyList<PolicyFragment> Build(Type entityType)
    {
        List<PolicyFragment> fragments = new();

        Walk(entityType, prefix: string.Empty, depth: 0, fragments);

        return new ReadOnlyCollection<PolicyFragment>(fragments);
    }

    /// <summary>
    /// Adds fragments for one type, then descends into its navigations.
    /// </summary>
    /// <param name="type">The type being walked.</param>
    /// <param name="prefix">The dotted path leading to this type, empty at the root.</param>
    /// <param name="depth">How many navigations deep the walk currently is.</param>
    /// <param name="fragments">The accumulator.</param>
    /// <remarks>
    /// Depth alone terminates the walk. A visited-type guard would be cheaper on a graph with many
    /// cycles, but it would also suppress legitimate paths: a self-referencing type would never
    /// yield <c>Next.Secret</c>, even though a caller can filter on exactly that path. Bidirectional
    /// navigations are cyclic by nature, so the depth cap is doing the real work either way, and the
    /// whole walk is computed once per type and cached.
    /// </remarks>
    private static void Walk(Type type, string prefix, int depth, List<PolicyFragment> fragments)
    {
        if (depth >= MaxDepth)
        {
            return;
        }

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            string path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";

            foreach (DwDenyAttribute attribute in property.GetCustomAttributes<DwDenyAttribute>(inherit: true))
            {
                fragments.Add(ToFragment(path, attribute));
            }

            foreach (DwOperatorsAttribute attribute in property.GetCustomAttributes<DwOperatorsAttribute>(inherit: true))
            {
                fragments.Add(ToFragment(path, attribute));
            }

            DwAliasAttribute? alias = property.GetCustomAttribute<DwAliasAttribute>(inherit: true);

            if (alias is not null)
            {
                fragments.Add(ToFragment(path, alias));
            }

            DwRequireWhereAttribute? required = property.GetCustomAttribute<DwRequireWhereAttribute>(inherit: true);

            if (required is not null)
            {
                fragments.Add(ToFragment(path, required));
            }

            foreach (DwForceWhereAttribute attribute in property.GetCustomAttributes<DwForceWhereAttribute>(inherit: true))
            {
                fragments.Add(ToFragment(path, property, attribute));
            }

            foreach (TransformStage stage in TransformStagesOn(property))
            {
                fragments.Add(ToFragment(path, stage, StageAttributeOn(property, stage.Kind)));
            }

            Type? navigation = NavigationTypeOf(property.PropertyType);

            if (navigation is not null)
            {
                Walk(navigation, path, depth + 1, fragments);
            }
        }
    }

    /// <summary>
    /// How many collection layers <see cref="NavigationTypeOf"/> peels off a single property type
    /// before it stops and walks whatever it has reached.
    /// </summary>
    /// <remarks>
    /// Two layers cover every shape seen in practice — an array of a collection, a jagged array, a
    /// collection of collections — so four leaves headroom without the count ever mattering to a
    /// real model. The limit exists for the types that have no bottom to reach: a tree node
    /// declared as <c>class Node : IEnumerable&lt;Node&gt;</c> unwraps to itself forever, and a pair
    /// declared as <c>A : IEnumerable&lt;B&gt;</c> and <c>B : IEnumerable&lt;A&gt;</c> alternates
    /// forever without ever unwrapping to the type it just came from, so a guard comparing one
    /// layer against the next would not stop it. Any fixed count stops both, whatever the length of
    /// the cycle. Reaching the limit is not treated as "no navigation": the type reached is still
    /// walked, because skipping it would suppress every attribute beneath it.
    /// </remarks>
    private const int MaxCollectionLayers = 4;

    /// <summary>
    /// Returns the type to descend into for a property, or null when the property holds a value
    /// rather than a navigation. Collections yield their element type, so <c>Orders.Total</c>
    /// resolves the same way a reference navigation does.
    /// </summary>
    /// <remarks>
    /// A navigation the walker fails to recognize produces no fragments for anything beneath it,
    /// and a field with no fragment is allowed — so this errs toward descending. Element types are
    /// taken from the <see cref="IEnumerable{T}"/> a type implements rather than from where that
    /// type is declared, which covers arrays, the BCL collections, and an application's own
    /// <c>PagedList&lt;T&gt;</c> alike. Collection layers are peeled until a non-collection is
    /// reached, so <c>PagedList&lt;LineDto&gt;[]</c>, <c>LineDto[][]</c> and
    /// <c>List&lt;List&lt;LineDto&gt;&gt;</c> all resolve to <c>LineDto</c> rather than to the
    /// collection in between, whose own members (<c>Count</c>, <c>Length</c>, <c>Item</c>) carry no
    /// policy and would end the walk short of the decorated fields. The value tests are applied
    /// once, to whatever is left at the bottom, so peeling extra layers never turns
    /// <c>byte[][]</c> into a walk over <see cref="byte"/>. Interfaces and user-defined structs are
    /// followed as well as classes, because these attributes are supported on DTOs, where an
    /// <c>IContact</c> reference or a record struct is ordinary.
    /// </remarks>
    private static Type? NavigationTypeOf(Type propertyType)
    {
        Type current = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

        for (int layer = 0; layer < MaxCollectionLayers; layer++)
        {
            Type? element = ElementTypeOf(current);

            if (element is null)
            {
                break;
            }

            current = Nullable.GetUnderlyingType(element) ?? element;
        }

        return AsNavigation(current);
    }

    /// <summary>
    /// Returns the element type of one collection layer — the element of an array, or the <c>T</c>
    /// of the first <see cref="IEnumerable{T}"/> a type implements — or null when the type is not a
    /// collection.
    /// </summary>
    /// <remarks>
    /// Arrays are read through <see cref="Type.GetElementType"/> rather than through their
    /// interfaces, because a multidimensional array implements only the non-generic
    /// <see cref="System.Collections.IEnumerable"/> and would otherwise be mistaken for a value.
    /// <see cref="string"/> is excluded explicitly: it implements <c>IEnumerable&lt;char&gt;</c>,
    /// and treating text as a collection of characters would send the walker into
    /// <see cref="char"/> on every string property in the model.
    /// </remarks>
    private static Type? ElementTypeOf(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }

        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return type.GetGenericArguments()[0];
        }

        foreach (Type contract in type.GetInterfaces())
        {
            if (contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return contract.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the type when it is one the walker should descend into, otherwise null.
    /// </summary>
    /// <remarks>
    /// This is the only place a type is rejected as a value, and it runs once, on what is left
    /// after every collection layer has been peeled — which is what keeps <c>byte[][]</c>, and an
    /// array of a <c>List&lt;string&gt;</c> subclass, from being walked as navigations. The
    /// namespace test stands in for "declared by the framework rather than by the application": a
    /// BCL type reached at the bottom of a property, such as <see cref="DateTime"/> or a
    /// <see cref="KeyValuePair{TKey,TValue}"/> out of a dictionary, holds no policy attributes and
    /// is a value as far as a filter is concerned.
    /// </remarks>
    private static Type? AsNavigation(Type? type)
    {
        if (type is null || type == typeof(string) || type.IsPrimitive || type.IsEnum)
        {
            return null;
        }

        return type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true ? null : type;
    }

    /// <summary>
    /// Converts one attribute into a fragment at the level its <c>Overridable</c> flag implies.
    /// </summary>
    private static PolicyFragment ToFragment(string fieldPath, DwDenyAttribute attribute)
    {
        PolicyLevel level = attribute.Overridable
            ? PolicyLevel.OverridableAttribute
            : PolicyLevel.SealedAttribute;

        PolicySource source = PolicySource.FromAttribute(
            attribute.GetType().Name,
            isSealed: !attribute.Overridable);

        return new PolicyFragment(fieldPath, attribute.Features, PolicyEffect.Deny, level, source);
    }

    /// <summary>
    /// Converts an operator restriction into a fragment.
    /// </summary>
    /// <remarks>
    /// The effect is <see cref="PolicyEffect.Allow"/> and the features are
    /// <see cref="PolicyFeature.Where"/>, because a restriction refuses nothing on its own — it
    /// only narrows what a permitted filter may do. The restriction itself rides on the fragment's
    /// typed operator list, which the resolver intersects rather than elects, so this fragment
    /// losing the election for <c>Where</c> does not discard it.
    /// </remarks>
    private static PolicyFragment ToFragment(string fieldPath, DwOperatorsAttribute attribute)
    {
        PolicyLevel level = attribute.Overridable
            ? PolicyLevel.OverridableAttribute
            : PolicyLevel.SealedAttribute;

        PolicySource source = PolicySource.FromAttribute(
            attribute.GetType().Name,
            isSealed: !attribute.Overridable);

        return new PolicyFragment(
            fieldPath,
            PolicyFeature.Where,
            PolicyEffect.Allow,
            level,
            source,
            allowedOperators: attribute.Resolve());
    }

    /// <summary>
    /// Converts a public name into a fragment.
    /// </summary>
    /// <remarks>
    /// The effect is <see cref="PolicyEffect.Allow"/> and the feature is
    /// <see cref="PolicyFeature.Where"/> because naming a field refuses nothing. The alias rides on
    /// the fragment's typed field and is elected outside the per-feature contest, so this fragment
    /// losing the contest for <c>Where</c> does not discard the name.
    /// </remarks>
    private static PolicyFragment ToFragment(string fieldPath, DwAliasAttribute attribute) =>
        new(fieldPath,
            PolicyFeature.Where,
            PolicyEffect.Allow,
            LevelOf(attribute),
            SourceOf(attribute),
            alias: attribute.Name);

    /// <summary>
    /// Converts a filtering requirement into a fragment.
    /// </summary>
    private static PolicyFragment ToFragment(string fieldPath, DwRequireWhereAttribute attribute) =>
        new(fieldPath,
            PolicyFeature.Where,
            PolicyEffect.Allow,
            LevelOf(attribute),
            SourceOf(attribute),
            requiredOperators: attribute.Resolve());

    /// <summary>
    /// Converts a forced predicate into a fragment, resolving the value's data type from the member
    /// it decorates.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// Thrown when the attribute names neither a constant nor a context key, names both, or
    /// decorates a member whose CLR type has no <see cref="DataType"/> counterpart.
    /// </exception>
    private static PolicyFragment ToFragment(
        string fieldPath, PropertyInfo property, DwForceWhereAttribute attribute)
    {
        bool hasValue = attribute.Value is not null;
        bool hasContextValue = attribute.ContextValue is not null;
        bool isNullCheck = attribute.Operator is Operator.IsNull or Operator.IsNotNull;

        // Refused rather than resolved in favour of one. Neither leaves nothing to inject, and both
        // leaves no way to choose -- and the wrong choice here is a tenant scope filtering on a
        // constant somebody left behind. A null check is the exception: it compares against
        // nothing, which is how soft deletion is usually spelled.
        if (isNullCheck && (hasValue || hasContextValue))
        {
            throw new ArgumentException(
                $"[DwForceWhere({attribute.Operator})] on '{fieldPath}' compares against nothing, " +
                "so it must set neither Value nor ContextValue.");
        }

        if (!isNullCheck && hasValue == hasContextValue)
        {
            throw new ArgumentException(
                $"[DwForceWhere] on '{fieldPath}' must set exactly one of Value or ContextValue; " +
                (hasValue ? "it sets both." : "it sets neither."));
        }

        DataType dataType = DataTypeOf(property, fieldPath);

        ForcedPredicate forced = isNullCheck
            ? ForcedPredicate.FromNullCheck(fieldPath, attribute.Operator, dataType)
            : hasContextValue
                ? ForcedPredicate.FromContext(fieldPath, attribute.Operator, dataType, attribute.ContextValue!)
                : ForcedPredicate.FromConstant(fieldPath, attribute.Operator, dataType, attribute.Value!);

        return new PolicyFragment(
            fieldPath,
            PolicyFeature.Where,
            PolicyEffect.Allow,
            LevelOf(attribute),
            SourceOf(attribute),
            forced: forced);
    }

    /// <summary>
    /// Reads every transform attribute on a member and yields the stage each one describes.
    /// </summary>
    /// <remarks>
    /// A member carries at most one of each, so the six are read independently and a member with a
    /// mask and a truncation yields two stages that the resolver elects separately.
    /// </remarks>
    private static IEnumerable<TransformStage> TransformStagesOn(PropertyInfo property)
    {
        if (property.GetCustomAttribute<DwMutateAttribute>(inherit: true) is { } mutate)
        {
            yield return new MutateStage(mutate.Transformer);
        }

        if (property.GetCustomAttribute<DwGeneralizeAttribute>(inherit: true) is { } generalize)
        {
            yield return new GeneralizeStage(
                generalize.Mode, generalize.Step, generalize.Part, generalize.Decimals);
        }

        if (property.GetCustomAttribute<DwFormatAttribute>(inherit: true) is { } format)
        {
            yield return new FormatStage(format.Format);
        }

        if (property.GetCustomAttribute<DwMaskAttribute>(inherit: true) is { } mask)
        {
            yield return new MaskStage(
                mask.Strategy, mask.KeepStart, mask.KeepEnd, mask.MaskChar, mask.PreserveLength,
                mask.Pattern, mask.Replacement, mask.Text);
        }

        if (property.GetCustomAttribute<DwTruncateAttribute>(inherit: true) is { } truncate)
        {
            yield return new TruncateStage(truncate.Length, truncate.Ellipsis);
        }

        if (property.GetCustomAttribute<DwDefaultAttribute>(inherit: true) is { } replacement)
        {
            yield return new DefaultStage(replacement.Value, replacement.HasValue);
        }
    }

    /// <summary>Finds the attribute a stage came from, so its level and source are its own.</summary>
    private static DwPolicyAttribute StageAttributeOn(PropertyInfo property, TransformKind kind) =>
        kind switch
        {
            TransformKind.Mutate => property.GetCustomAttribute<DwMutateAttribute>(inherit: true)!,
            TransformKind.Generalize => property.GetCustomAttribute<DwGeneralizeAttribute>(inherit: true)!,
            TransformKind.Format => property.GetCustomAttribute<DwFormatAttribute>(inherit: true)!,
            TransformKind.Mask => property.GetCustomAttribute<DwMaskAttribute>(inherit: true)!,
            TransformKind.Truncate => property.GetCustomAttribute<DwTruncateAttribute>(inherit: true)!,
            _ => property.GetCustomAttribute<DwDefaultAttribute>(inherit: true)!
        };

    /// <summary>
    /// Converts one transform stage into a fragment.
    /// </summary>
    /// <remarks>
    /// The effect is <see cref="PolicyEffect.Mask"/> on <see cref="PolicyFeature.Select"/>, which is
    /// what that effect has always meant: the feature proceeds and the value is transformed on
    /// output. It makes <c>FieldPolicy.IsMasked(Select)</c> real for the first time — nothing could
    /// produce a Mask effect until now — and it keeps a masked field filterable and sortable, since
    /// <c>Allows</c> refuses only a denial.
    /// <para>
    /// Because the effect competes in the per-feature election, a <see cref="DwDenyAttribute"/> on
    /// the same field and level still wins: Deny outranks Mask. A field both denied and masked is
    /// dropped from the projection rather than masked in it, which is the stricter reading.
    /// </para>
    /// </remarks>
    private static PolicyFragment ToFragment(
        string fieldPath, TransformStage stage, DwPolicyAttribute attribute) =>
        new(fieldPath,
            PolicyFeature.Select,
            PolicyEffect.Mask,
            LevelOf(attribute),
            SourceOf(attribute),
            transform: stage);

    /// <summary>The level an attribute's <c>Overridable</c> flag places it at.</summary>
    private static PolicyLevel LevelOf(DwPolicyAttribute attribute) =>
        attribute.Overridable ? PolicyLevel.OverridableAttribute : PolicyLevel.SealedAttribute;

    /// <summary>The source describing one attribute.</summary>
    private static PolicySource SourceOf(DwPolicyAttribute attribute) =>
        PolicySource.FromAttribute(attribute.GetType().Name, isSealed: !attribute.Overridable);

    /// <summary>
    /// Maps a member's CLR type onto the <see cref="DataType"/> the pipeline will validate the
    /// injected value against.
    /// </summary>
    /// <remarks>
    /// Read from the member rather than declared on the attribute. C# forbids a nullable enum as an
    /// attribute argument, so an override would need a sentinel or a paired flag — and it could only
    /// ever disagree with the type the value is about to be parsed as.
    /// <para>
    /// A type with no counterpart is refused here, at the misconfiguration, rather than guessed into
    /// a downstream parse failure that names neither the field nor the attribute.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">Thrown when the CLR type has no counterpart.</exception>
    private static DataType DataTypeOf(PropertyInfo property, string fieldPath)
    {
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (type.IsEnum)
        {
            return DataType.Enum;
        }

        if (type == typeof(string) || type == typeof(char))
        {
            return DataType.Text;
        }

        if (type == typeof(Guid))
        {
            return DataType.Guid;
        }

        if (type == typeof(bool))
        {
            return DataType.Boolean;
        }

        if (type == typeof(DateOnly))
        {
            return DataType.Date;
        }

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            return DataType.DateTime;
        }

        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short)
            || type == typeof(ushort) || type == typeof(int) || type == typeof(uint)
            || type == typeof(long) || type == typeof(ulong) || type == typeof(float)
            || type == typeof(double) || type == typeof(decimal))
        {
            return DataType.Number;
        }

        throw new ArgumentException(
            $"[DwForceWhere] on '{fieldPath}' decorates a {type.Name}, which has no DataType " +
            "counterpart, so the injected value has no form the pipeline could parse it into.");
    }
}
