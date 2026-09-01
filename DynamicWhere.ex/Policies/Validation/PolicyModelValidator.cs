using System.Reflection;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Masking;

namespace DynamicWhere.ex.Policies.Validation;

/// <summary>
/// Checks a policy model at startup, so a misconfiguration fails a deployment rather than a query.
/// </summary>
/// <remarks>
/// Every rule here is one whose violation would otherwise surface on a caller's request, at whatever
/// hour that request happens — a mask on a decimal, a transformer that is not one, two attributes
/// that contradict each other. None of them can be wrong in a way that grants access, because the
/// run-time checks fail closed; they can be wrong in a way that fails the query, which is what this
/// moves forward in time.
/// <para>
/// Reported as a list rather than thrown one at a time. Fixing a model one exception per run is the
/// slowest possible way to learn what is wrong with it.
/// </para>
/// </remarks>
public static class PolicyModelValidator
{
    /// <summary>
    /// Inspects the given types and returns everything wrong with their policy attributes.
    /// </summary>
    /// <param name="types">The entity and DTO types to inspect.</param>
    /// <returns>The problems found, empty when the model is sound.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="types"/> is null.</exception>
    public static PolicyModelReport Inspect(IEnumerable<Type> types)
    {
        if (types is null)
        {
            throw new ArgumentNullException(nameof(types));
        }

        List<string> errors = new();
        List<string> warnings = new();

        foreach (Type type in types)
        {
            Inspect(type, errors, warnings);
        }

        return new PolicyModelReport(errors, warnings);
    }

    /// <summary>Inspects one type.</summary>
    private static void Inspect(Type type, List<string> errors, List<string> warnings)
    {
        Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            string member = $"{type.Name}.{property.Name}";

            CheckAlias(property, member, aliases, errors);

            ValueTransform chain = ChainOn(property);

            if (chain.IsEmpty)
            {
                continue;
            }

            CheckConflict(chain, member, errors);
            CheckMutator(chain, member, errors);
            CheckOutputType(chain, property, member, errors);
            CheckMaskedButOrderable(chain, property, member, warnings);
        }
    }

    /// <summary>Refuses two members of one type answering to the same public name.</summary>
    private static void CheckAlias(
        PropertyInfo property, string member, Dictionary<string, string> aliases, List<string> errors)
    {
        if (property.GetCustomAttribute<DwAliasAttribute>(inherit: true) is not { } alias)
        {
            return;
        }

        if (aliases.TryGetValue(alias.Name, out string? existing))
        {
            errors.Add(
                $"{member}: the alias '{alias.Name}' is already used by {existing}. A name that " +
                "could mean two fields is refused at query time, so neither member can be filtered " +
                "on by it.");

            return;
        }

        aliases[alias.Name] = member;
    }

    /// <summary>Refuses a replacement declared alongside a stage it would silently discard.</summary>
    private static void CheckConflict(ValueTransform chain, string member, List<string> errors)
    {
        if (chain.HasConflictingDefault)
        {
            errors.Add(
                $"{member}: [DwDefault] replaces the value outright and short-circuits every other " +
                "stage, so the other transforms on this member would never run. Remove one.");
        }
    }

    /// <summary>Refuses a transformer type that cannot transform anything.</summary>
    private static void CheckMutator(ValueTransform chain, string member, List<string> errors)
    {
        if (chain.Mutate is { } mutate && !typeof(IValueTransformer).IsAssignableFrom(mutate.Transformer))
        {
            errors.Add(
                $"{member}: [DwMutate] names {mutate.Transformer.Name}, which does not implement " +
                "IValueTransformer, so there is nothing to run.");
        }
    }

    /// <summary>
    /// Refuses a chain whose output the member cannot hold.
    /// </summary>
    /// <remarks>
    /// The whole applicability question, checked once. Masking, formatting, truncation and bucketing
    /// all emit text; a member that is not text cannot hold their result, so the pairing is a
    /// configuration error however sensible each attribute looks alone. Rounding and date reduction
    /// keep the value's kind and are therefore valid wherever the member already is.
    /// <para>
    /// A chain containing <c>[DwMutate]</c> is not checked, because a transformer returns whatever
    /// it likes and only its author knows what that is. The run-time check still catches it.
    /// </para>
    /// </remarks>
    private static void CheckOutputType(
        ValueTransform chain, PropertyInfo property, string member, List<string> errors)
    {
        Type declared = property.PropertyType;
        Type target = Nullable.GetUnderlyingType(declared) ?? declared;

        if (chain.Default is { } replacement)
        {
            CheckReplacement(replacement, declared, member, errors);

            return;
        }

        if (chain.Mutate is not null)
        {
            return;
        }

        if (chain.Mask?.Strategy == MaskStrategy.Null
            && declared.IsValueType
            && Nullable.GetUnderlyingType(declared) is null)
        {
            errors.Add(
                $"{member}: MaskStrategy.Null removes the value, which a non-nullable " +
                $"{declared.Name} cannot hold. Emitting the type's default instead would turn a " +
                "removed value into a real-looking zero, so make the member nullable or choose " +
                "another strategy.");

            return;
        }

        if (!EmitsText(chain) || target == typeof(string))
        {
            return;
        }

        errors.Add(
            $"{member}: {Naming(chain)} emits text, which cannot be assigned to " +
            $"{declared.Name}. Use [DwGeneralize] to reduce a number or a date while keeping its " +
            "type, or project into a type whose member is a string.");
    }

    /// <summary>Refuses a constant that cannot be read as the member's type.</summary>
    private static void CheckReplacement(
        DefaultStage replacement, Type declared, string member, List<string> errors)
    {
        if (!replacement.HasValue)
        {
            return;
        }

        try
        {
            TransformPipeline.Coerce(replacement.Value, declared);
        }
        catch (Exception error) when (error is FormatException or InvalidCastException
                                          or OverflowException or ArgumentException)
        {
            errors.Add(
                $"{member}: [DwDefault(\"{replacement.Value}\")] cannot be read as " +
                $"{declared.Name}.");
        }
    }

    /// <summary>True when the last stage that changes the value's kind produces text.</summary>
    private static bool EmitsText(ValueTransform chain) =>
        chain.Truncate is not null
        || chain.Mask is not null
        || chain.Format is not null
        || chain.Generalize?.Mode == GeneralizeMode.Bucket;

    /// <summary>Names the text-emitting stages, for the message.</summary>
    private static string Naming(ValueTransform chain) =>
        string.Join(
            " and ",
            chain.Stages
                .Where(s => s.Kind is TransformKind.Mask or TransformKind.Format or TransformKind.Truncate
                            || (s is GeneralizeStage g && g.Mode == GeneralizeMode.Bucket))
                .Select(s => $"[Dw{s.Kind}]"));

    /// <summary>
    /// Warns when a member is transformed but nothing stops a caller sorting by it.
    /// </summary>
    /// <remarks>
    /// Design section 7.4. Sorting runs in SQL against the real value, so paging through a masked
    /// column ranks the true order and, combined with range filters, converges on the values the
    /// mask hides. A warning rather than an error because the engine does not get to decide: the fix
    /// is <c>[DwNoOrder]</c>, and there are models where the ordering is the point and the mask is
    /// only cosmetic.
    /// </remarks>
    private static void CheckMaskedButOrderable(
        ValueTransform chain, PropertyInfo property, string member, List<string> warnings)
    {
        bool denied = property
            .GetCustomAttributes<DwDenyAttribute>(inherit: true)
            .Any(a => (a.Features & PolicyFeature.Order) == PolicyFeature.Order);

        if (!denied && chain.Default is null)
        {
            warnings.Add(
                $"{member}: the value is transformed on output but the field can still be sorted " +
                "on, and sorting runs against the real value. Paging through it ranks the true " +
                "order. Add [DwNoOrder] unless that is intended.");
        }
    }

    /// <summary>Reads the chain a member's attributes describe.</summary>
    private static ValueTransform ChainOn(PropertyInfo property)
    {
        DwGeneralizeAttribute? generalize = property.GetCustomAttribute<DwGeneralizeAttribute>(inherit: true);
        DwMaskAttribute? mask = property.GetCustomAttribute<DwMaskAttribute>(inherit: true);
        DwTruncateAttribute? truncate = property.GetCustomAttribute<DwTruncateAttribute>(inherit: true);
        DwFormatAttribute? format = property.GetCustomAttribute<DwFormatAttribute>(inherit: true);
        DwMutateAttribute? mutate = property.GetCustomAttribute<DwMutateAttribute>(inherit: true);
        DwDefaultAttribute? replacement = property.GetCustomAttribute<DwDefaultAttribute>(inherit: true);

        return new ValueTransform(
            mutate is null ? null : new MutateStage(mutate.Transformer),
            generalize is null
                ? null
                : new GeneralizeStage(generalize.Mode, generalize.Step, generalize.Part, generalize.Decimals),
            format is null ? null : new FormatStage(format.Format),
            mask is null
                ? null
                : new MaskStage(
                    mask.Strategy, mask.KeepStart, mask.KeepEnd, mask.MaskChar, mask.PreserveLength,
                    mask.Pattern, mask.Replacement, mask.Text),
            truncate is null ? null : new TruncateStage(truncate.Length, truncate.Ellipsis),
            replacement is null ? null : new DefaultStage(replacement.Value, replacement.HasValue));
    }
}
