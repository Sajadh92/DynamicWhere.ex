using System.Text;
using System.Text.Json;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Storage;

/// <summary>
/// Converts a <see cref="PolicyRule"/> to the document a store persists, and back.
/// </summary>
/// <remarks>
/// The one place a whole rule is read or written, for the same reason <see cref="PolicyPayload"/> is
/// the one place a transform is: three stores each inventing a format is three standards, and the
/// one that quietly omits a field is the one nobody notices.
/// <para>
/// <b>Design section 5.1 is incomplete, and the gap fails open.</b> It gives a rule a single
/// <c>Payload</c> column and describes its purpose as a mask spec, a default value or a bucket step.
/// The rule this library actually holds carries four more things — <see cref="PolicyRule.Forced"/>,
/// <see cref="PolicyRule.RequiredOperators"/>, <see cref="PolicyRule.AllowedOperators"/> and
/// <see cref="PolicyRule.Alias"/> — and three of those four are controls that vanish silently when
/// a serializer drops them. A forced predicate that does not survive the round trip is a tenant
/// scope that has stopped applying, with a rule still listed and a snapshot still counting it.
/// </para>
/// <para>
/// Nothing here returns a partial rule. Every failure throws, so an unreadable row fails the load
/// rather than being skipped — a skipped row is a denial that is no longer enforced and that nothing
/// reports. Failing the load routes into machinery that already exists: fatal at startup, and a
/// refresh failure afterwards, governed by <c>StoreFailure</c> and bounded by the staleness ceiling.
/// </para>
/// <para>
/// Every enumeration is written by name and read by name. Four of the six involved have a member at
/// zero — <see cref="PolicyEffect.Allow"/>, <see cref="DwSubjectKind.Global"/>,
/// <see cref="Operator.Equal"/> and <see cref="DataType.Text"/> — so a numeric value read from a
/// column that was absent, defaulted or added with <c>NOT NULL DEFAULT 0</c> would be a real,
/// plausible-looking instruction that nobody wrote.
/// </para>
/// </remarks>
public static class PolicyRuleDocument
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false };

    /// <summary>
    /// Writes a rule as the document a store persists.
    /// </summary>
    /// <param name="rule">The rule to write.</param>
    /// <returns>A JSON object <see cref="ToRule"/> reads back.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when the rule carries a transform a store may not hold.
    /// </exception>
    public static string ToJson(PolicyRule rule)
    {
        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, WriterOptions))
        {
            writer.WriteStartObject();

            writer.WriteString("id", rule.Id);
            writer.WriteString("subjectKind", rule.SubjectKind.ToString());

            if (rule.SubjectKey is not null)
            {
                writer.WriteString("subjectKey", rule.SubjectKey);
            }

            writer.WriteString("entityType", rule.EntityType);
            writer.WriteString("fieldPath", rule.FieldPath);
            writer.WriteString("features", rule.Features.ToString());
            writer.WriteString("effect", rule.Effect.ToString());
            writer.WriteNumber("priority", rule.Priority);
            writer.WriteBoolean("enabled", rule.Enabled);

            WriteInstant(writer, "validFrom", rule.ValidFrom);
            WriteInstant(writer, "validTo", rule.ValidTo);

            if (rule.Purpose is not null)
            {
                writer.WriteString("purpose", rule.Purpose);
            }

            if (rule.CreatedBy is not null)
            {
                writer.WriteString("createdBy", rule.CreatedBy);
            }

            WriteInstant(writer, "createdAt", rule.CreatedAt);

            if (rule.UpdatedBy is not null)
            {
                writer.WriteString("updatedBy", rule.UpdatedBy);
            }

            WriteInstant(writer, "updatedAt", rule.UpdatedAt);

            // Written by the same routine the relational store's detail column uses, so the two
            // cannot describe a carrier differently.
            if (HasDetail(rule))
            {
                writer.WritePropertyName("detail");
                WriteDetail(writer, rule);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Reads a document into the rule it describes.
    /// </summary>
    /// <param name="json">The document, as a store holds it.</param>
    /// <returns>The rule, never null.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the document is blank, is not a JSON object, omits something a rule cannot be
    /// built without, names an enumeration member by number or by a name this library does not
    /// define, or describes a rule the boundary refuses.
    /// </exception>
    /// <remarks>
    /// The rule is built through <see cref="PolicyRule"/>'s constructor rather than around it, so
    /// every refusal that boundary makes still holds for a rule arriving from a store — which is
    /// the only place those refusals were ever going to matter.
    /// </remarks>
    public static PolicyRule ToRule(string json)
    {
        JsonElement root = Parse(json, "rule document");

        RuleDetail detail = root.TryGetProperty("detail", out JsonElement element)
            && element.ValueKind != JsonValueKind.Null
            ? ReadDetail(element)
            : RuleDetail.None;

        return new PolicyRule(
            ReadEnum<DwSubjectKind>(root, "subjectKind", required: true)!.Value,
            ReadString(root, "subjectKey"),
            ReadString(root, "entityType") ?? throw Missing("entityType"),
            ReadString(root, "fieldPath") ?? throw Missing("fieldPath"),
            ReadFeatures(root),
            ReadEnum<PolicyEffect>(root, "effect", required: true)!.Value,
            ReadInt(root, "priority") ?? 0,
            ReadBool(root, "enabled") ?? true,
            ReadInstant(root, "validFrom"),
            ReadInstant(root, "validTo"),
            ReadString(root, "purpose"),
            detail.Transform,
            detail.AllowedOperators,
            detail.Alias,
            detail.Forced,
            detail.RequiredOperators,
            detail.Facts,
            ReadGuid(root, "id"),
            ReadString(root, "createdBy"),
            ReadInstant(root, "createdAt"),
            ReadString(root, "updatedBy"),
            ReadInstant(root, "updatedAt"));
    }

    /// <summary>
    /// Writes only the carriers a relational schema has no column for.
    /// </summary>
    /// <param name="rule">The rule whose detail to write.</param>
    /// <returns>
    /// A JSON object, or null when the rule carries none of them — which is the common case and
    /// should not cost a column full of <c>{}</c>.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is null.</exception>
    /// <remarks>
    /// Design section 5.6 indexes a rule by <c>(SubjectKind, SubjectKey, EntityType)</c> and by
    /// <c>(EntityType, FieldPath)</c>, so those stay real columns and only what nothing queries by
    /// lives here.
    /// </remarks>
    public static string? DetailToJson(PolicyRule rule)
    {
        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        if (!HasDetail(rule))
        {
            return null;
        }

        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, WriterOptions))
        {
            WriteDetail(writer, rule);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Reads a detail column.
    /// </summary>
    /// <param name="json">The column's contents, or null when the rule carries no detail.</param>
    /// <returns>The carriers, never null.</returns>
    /// <exception cref="ArgumentException">Thrown when the column cannot be read.</exception>
    public static RuleDetail ReadDetail(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? RuleDetail.None
            : ReadDetail(Parse(json!, "rule detail"));

    /// <summary>
    /// Reads an enumeration member from the name a store holds.
    /// </summary>
    /// <typeparam name="TEnum">The enumeration to read.</typeparam>
    /// <param name="name">The member's name, as stored.</param>
    /// <param name="what">What is being read, for the message.</param>
    /// <returns>The member.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the name is blank, is written as digits, or is not a member this library
    /// defines.
    /// </exception>
    /// <remarks>
    /// Public because a store provider outside this library needs the same reader — design section
    /// 8.4 expects future providers, and a provider that parses <c>"0"</c> as
    /// <see cref="PolicyEffect.Allow"/> would be the second standard this type exists to prevent.
    /// <para>
    /// Digits are refused as firmly as a blank is. Four of the enumerations a rule carries have a
    /// member at zero — <see cref="PolicyEffect.Allow"/>, <see cref="DwSubjectKind.Global"/>,
    /// <see cref="Operator.Equal"/> and <see cref="DataType.Text"/> — so a column that was
    /// defaulted, added with <c>NOT NULL DEFAULT 0</c>, or written by something that stored the
    /// underlying number would otherwise read as a grant, a global audience, an equality
    /// restriction, or the wrong data type to validate against.
    /// </para>
    /// </remarks>
    public static TEnum ToEnum<TEnum>(string? name, string what)
        where TEnum : struct, Enum
    {
        RefuseNonName(name, what, typeof(TEnum));

        return Enum.TryParse(name, ignoreCase: true, out TEnum parsed)
            && Enum.IsDefined(typeof(TEnum), parsed)
            ? parsed
            : throw new ArgumentException(
                $"'{name}' is not a {typeof(TEnum).Name} this library defines, reading {what}.");
    }

    /// <summary>
    /// Reads a feature set from the name a store holds.
    /// </summary>
    /// <param name="name">The flags, as stored — for instance <c>"Where, Select"</c>.</param>
    /// <returns>The features.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the name is blank, is written as digits, or names no combination this library
    /// defines.
    /// </exception>
    /// <remarks>
    /// Separate from <see cref="ToEnum{TEnum}"/> because <see cref="PolicyFeature"/> is a flags
    /// enumeration and <c>Enum.IsDefined</c> is false for every combination — <c>Where | Select</c>
    /// is 3 and is a member of nothing. The unknown-bit mask and the refusal of
    /// <see cref="PolicyFeature.None"/> stay in <see cref="PolicyRule"/>'s constructor, so the two
    /// checks remain independent of one another.
    /// </remarks>
    public static PolicyFeature ToFeatures(string? name)
    {
        RefuseNonName(name, "features", typeof(PolicyFeature));

        return Enum.TryParse(name, ignoreCase: true, out PolicyFeature parsed)
            ? parsed
            : throw new ArgumentException(
                $"'{name}' does not name any combination of PolicyFeature this library defines.");
    }

    /// <summary>Refuses a stored enumeration that is blank or written as a number.</summary>
    private static void RefuseNonName(string? name, string what, Type type)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                $"A {type.Name} is required for {what}, and none was stored. An absent value would " +
                "be read as zero, which is a real member of this enumeration.");
        }

        foreach (char character in name!)
        {
            if (!char.IsDigit(character) && character != '-' && character != '+')
            {
                return;
            }
        }

        throw NotAName(what, type);
    }

    /// <summary>True when the rule carries anything a relational schema has no column for.</summary>
    private static bool HasDetail(PolicyRule rule) =>
        rule.Transform is not null
        || rule.AllowedOperators is not null
        || rule.Alias is not null
        || rule.Forced is not null
        || rule.RequiredOperators is not null
        || rule.Facts is not null;

    /// <summary>Writes the six carriers as one object.</summary>
    private static void WriteDetail(Utf8JsonWriter writer, PolicyRule rule)
    {
        writer.WriteStartObject();

        if (rule.Transform is not null)
        {
            // Through PolicyPayload, which stays the one place a transform is read or written — and
            // which refuses a named transformer in both directions, so that refusal is inherited
            // here rather than restated.
            writer.WritePropertyName("transform");
            writer.WriteRawValue(PolicyPayload.ToJson(rule.Transform));
        }

        WriteOperators(writer, "allowedOperators", rule.AllowedOperators);
        WriteOperators(writer, "requiredOperators", rule.RequiredOperators);

        if (rule.Alias is not null)
        {
            writer.WriteString("alias", rule.Alias);
        }

        if (rule.Forced is not null)
        {
            writer.WritePropertyName("forced");
            writer.WriteStartObject();
            writer.WriteString("fieldPath", rule.Forced.FieldPath);
            writer.WriteString("operator", rule.Forced.Operator.ToString());
            writer.WriteString("dataType", rule.Forced.DataType.ToString());

            // Exactly one of these, or neither for a null check. Written only when present so the
            // reader can tell the three shapes apart by which properties exist.
            if (rule.Forced.Value is not null)
            {
                writer.WriteString("value", rule.Forced.Value);
            }

            if (rule.Forced.ContextValue is not null)
            {
                writer.WriteString("contextValue", rule.Forced.ContextValue);
            }

            writer.WriteEndObject();
        }

        WriteFacts(writer, rule.Facts);

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes what a rule says about a field beyond its access decisions.
    /// </summary>
    /// <remarks>
    /// Each fact is written only when it is set, so that absent and set-to-a-default stay
    /// distinguishable on the way back in. Two of them make that distinction matter rather than
    /// merely tidy: an order of zero is a real position and a cost weight of zero is a field the
    /// budget does not charge for, and both would otherwise read back as "nobody said".
    /// <para>
    /// The audit is written by name, like every other enumeration a rule carries and for the same
    /// reason — <see cref="PolicyFeature.None"/> is zero, so a numeric value that failed to parse
    /// would read as an audit of nothing, which is an access happening with nothing written down.
    /// </para>
    /// </remarks>
    private static void WriteFacts(Utf8JsonWriter writer, FieldFacts? facts)
    {
        if (facts is null)
        {
            return;
        }

        writer.WritePropertyName("facts");
        writer.WriteStartObject();

        if (facts.Label is not null)
        {
            writer.WriteString("label", facts.Label);
        }

        if (facts.Description is not null)
        {
            writer.WriteString("description", facts.Description);
        }

        if (facts.Group is not null)
        {
            writer.WriteString("group", facts.Group);
        }

        if (facts.Order is int order)
        {
            writer.WriteNumber("order", order);
        }

        if (facts.AllowedValues is { } values)
        {
            writer.WriteStartArray("allowedValues");

            for (int i = 0; i < values.Count; i++)
            {
                writer.WriteStringValue(values[i]);
            }

            writer.WriteEndArray();
        }

        if (facts.CostWeight is int weight)
        {
            writer.WriteNumber("cost", weight);
        }

        if (facts.AuditedFeatures is PolicyFeature audited)
        {
            writer.WriteString("audit", audited.ToString());
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes an operator list, keeping absent and empty distinguishable.
    /// </summary>
    /// <remarks>
    /// For a requirement, null means "this rule demands no filter" and empty means "it demands one
    /// that nothing satisfies". Writing an absent property for both reverses the decision on the way
    /// back in, and reverses it towards the grant.
    /// </remarks>
    private static void WriteOperators(
        Utf8JsonWriter writer, string name, IReadOnlyList<Operator>? operators)
    {
        if (operators is null)
        {
            return;
        }

        writer.WriteStartArray(name);

        for (int i = 0; i < operators.Count; i++)
        {
            writer.WriteStringValue(operators[i].ToString());
        }

        writer.WriteEndArray();
    }

    /// <summary>Reads the five carriers from an object.</summary>
    /// <remarks>
    /// The shape is checked before anything is read from it. <c>TryGetProperty</c> throws
    /// <see cref="InvalidOperationException"/> on a JSON scalar rather than reporting it, so a
    /// document whose detail was a number reached a store's load as an exception type the contract
    /// never mentions and no caller could catch for.
    /// </remarks>
    private static RuleDetail ReadDetail(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                $"A rule's detail must be a JSON object; this one is {root.ValueKind}.");
        }

        return new RuleDetail(
            root.TryGetProperty("transform", out JsonElement transform)
                && transform.ValueKind != JsonValueKind.Null
                ? PolicyPayload.ToStage(transform.GetRawText())
                : null,
            ReadOperators(root, "allowedOperators"),
            ReadString(root, "alias"),
            ReadForced(root),
            ReadOperators(root, "requiredOperators"),
            ReadFacts(root));
    }

    /// <summary>Reads the facts block, or null when the rule states none.</summary>
    /// <remarks>
    /// The shape is checked before anything is read from it, for the reason the detail's own shape
    /// is: <c>TryGetProperty</c> throws <see cref="InvalidOperationException"/> on a scalar rather
    /// than reporting it, and a store's load would surface an exception type the contract never
    /// mentions.
    /// </remarks>
    private static FieldFacts? ReadFacts(JsonElement root)
    {
        if (!root.TryGetProperty("facts", out JsonElement facts)
            || facts.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (facts.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                $"A rule's facts must be a JSON object; this one is {facts.ValueKind}.");
        }

        return new FieldFacts(
            ReadString(facts, "label"),
            ReadString(facts, "description"),
            ReadString(facts, "group"),
            ReadInt(facts, "order"),
            ReadValues(facts),
            ReadInt(facts, "cost"),
            ReadAudited(facts));
    }

    /// <summary>Reads the audited features, or null when the rule audits nothing.</summary>
    /// <remarks>
    /// Through <see cref="ToFeatures"/> rather than the single-member reader, because this is a
    /// flags enumeration and an audit of two features is stored as "Where, Select" — a name no
    /// single member carries. Reading it the other way refuses a rule that is perfectly valid,
    /// which fails the load and is loud; the reverse mistake would have been silent.
    /// </remarks>
    private static PolicyFeature? ReadAudited(JsonElement facts)
    {
        string? name = ReadName(facts, "audit", required: false);

        return name is null ? null : ToFeatures(name);
    }

    /// <summary>Reads an allowed-value list, or null when the property is absent.</summary>
    private static IReadOnlyList<string>? ReadValues(JsonElement facts)
    {
        if (!facts.TryGetProperty("allowedValues", out JsonElement value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException("'allowedValues' must be an array of strings.");
        }

        List<string> values = new();

        foreach (JsonElement element in value.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException(
                    $"An allowed value must be a string; this one is {element.ValueKind}.");
            }

            values.Add(element.GetString()!);
        }

        return values;
    }

    /// <summary>Reads an operator list, or null when the property is absent.</summary>
    private static IReadOnlyList<Operator>? ReadOperators(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new ArgumentException($"'{name}' must be an array of operator names.");
        }

        List<Operator> operators = new();

        foreach (JsonElement element in value.EnumerateArray())
        {
            operators.Add(ParseEnum<Operator>(element, name));
        }

        return operators;
    }

    /// <summary>
    /// Rebuilds a forced predicate through its own factories.
    /// </summary>
    /// <remarks>
    /// The three shapes — a constant, a value read from the caller's context, and a null check — are
    /// mutually exclusive, and the factories are where that exclusivity is enforced. Setting fields
    /// directly would let a document naming both a value and a context value resolve to whichever
    /// the reader happened to check first, which is a predicate nobody wrote filtering rows nobody
    /// intended.
    /// </remarks>
    private static ForcedPredicate? ReadForced(JsonElement root)
    {
        if (!root.TryGetProperty("forced", out JsonElement forced)
            || forced.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (forced.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("'forced' must be a JSON object.");
        }

        string field = ReadString(forced, "fieldPath") ?? throw Missing("forced.fieldPath");
        Operator op = ReadEnum<Operator>(forced, "operator", required: true)!.Value;
        DataType type = ReadEnum<DataType>(forced, "dataType", required: true)!.Value;
        string? value = ReadString(forced, "value");
        string? contextValue = ReadString(forced, "contextValue");

        if (value is not null && contextValue is not null)
        {
            throw new ArgumentException(
                "A forced predicate names either a constant or a context key, never both. A " +
                "document carrying both describes two different predicates, and honouring one of " +
                "them would filter on a value the operator did not write.");
        }

        if (value is not null)
        {
            return ForcedPredicate.FromConstant(field, op, type, value);
        }

        return contextValue is not null
            ? ForcedPredicate.FromContext(field, op, type, contextValue)
            : ForcedPredicate.FromNullCheck(field, op, type);
    }

    /// <summary>Parses a document, refusing anything that is not a JSON object.</summary>
    private static JsonElement Parse(string json, string what)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException(
                $"A {what} cannot be blank. A rule that cannot be read is a control that is no " +
                "longer enforced, so it is refused rather than skipped.", nameof(json));
        }

        JsonElement root;

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            root = document.RootElement.Clone();
        }
        catch (JsonException error)
        {
            throw new ArgumentException(
                $"The {what} is not valid JSON: {error.Message}", nameof(json), error);
        }

        return root.ValueKind == JsonValueKind.Object
            ? root
            : throw new ArgumentException(
                $"A {what} must be a JSON object; this one is {root.ValueKind}.", nameof(json));
    }

    /// <summary>Reads the feature flags, through the same parser a store column goes through.</summary>
    private static PolicyFeature ReadFeatures(JsonElement root) =>
        ToFeatures(ReadName(root, "features", required: true));

    /// <summary>Reads an enumeration member by name.</summary>
    private static TEnum? ReadEnum<TEnum>(JsonElement root, string name, bool required)
        where TEnum : struct, Enum
    {
        string? text = ReadName(root, name, required);

        return text is null ? null : ToEnum<TEnum>(text, name);
    }

    /// <summary>Parses one array element as an enumeration member.</summary>
    private static TEnum ParseEnum<TEnum>(JsonElement element, string name)
        where TEnum : struct, Enum =>
        element.ValueKind == JsonValueKind.String
            ? ToEnum<TEnum>(element.GetString(), name)
            : throw NotAName(name, typeof(TEnum));

    /// <summary>
    /// Reads the text of a property that must name an enumeration member.
    /// </summary>
    /// <remarks>
    /// A JSON number is refused here; a string of digits is refused by <see cref="ToEnum{TEnum}"/>,
    /// which is where a store column arrives too, so both paths refuse the same things.
    /// </remarks>
    private static string? ReadName(JsonElement root, string name, bool required)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return required ? throw Missing(name) : null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : throw NotAName(name, null);
    }

    /// <summary>Reads an optional string, treating an explicit null as absent.</summary>
    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : throw new ArgumentException($"'{name}' must be a string.");
    }

    /// <summary>Reads an optional whole number.</summary>
    private static int? ReadInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parsed)
            ? parsed
            : throw new ArgumentException($"'{name}' must be a whole number.");
    }

    /// <summary>Reads an optional boolean.</summary>
    private static bool? ReadBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ArgumentException($"'{name}' must be true or false.")
        };
    }

    /// <summary>Reads an optional identifier.</summary>
    private static Guid? ReadGuid(JsonElement root, string name)
    {
        string? text = ReadString(root, name);

        if (text is null)
        {
            return null;
        }

        return Guid.TryParse(text, out Guid parsed)
            ? parsed
            : throw new ArgumentException($"'{name}' must be a GUID; it was '{text}'.");
    }

    /// <summary>Reads an optional instant.</summary>
    private static DateTimeOffset? ReadInstant(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            && value.TryGetDateTimeOffset(out DateTimeOffset parsed)
            ? parsed
            : throw new ArgumentException($"'{name}' must be an ISO 8601 instant.");
    }

    /// <summary>Writes an instant only when there is one, so absent stays absent.</summary>
    private static void WriteInstant(Utf8JsonWriter writer, string name, DateTimeOffset? value)
    {
        if (value is not null)
        {
            writer.WriteString(name, value.Value);
        }
    }

    /// <summary>Reports a property a rule cannot be built without.</summary>
    private static ArgumentException Missing(string name) =>
        new($"A rule document requires '{name}'.");

    /// <summary>Reports an enumeration that was not written as a name.</summary>
    private static ArgumentException NotAName(string name, Type? type) =>
        new($"'{name}' must be the name of a {type?.Name ?? "value"}, written as text. A number is " +
            "what an absent or defaulted column produces, and Allow, Global, Equal and Text are " +
            "all zero — so a numeric member would be a real instruction nobody wrote.");
}

/// <summary>
/// The parts of a rule a relational schema has no column for.
/// </summary>
/// <remarks>
/// Kept together so the store that holds them in one column and the store that holds the whole rule
/// in one document are reading the same six things through the same routine.
/// </remarks>
public sealed class RuleDetail
{
    /// <summary>Initializes the carriers.</summary>
    /// <param name="transform">One stage of the field's transform chain, or null.</param>
    /// <param name="allowedOperators">The operators the rule permits, or null to say nothing.</param>
    /// <param name="alias">The public name the rule gives the field, or null.</param>
    /// <param name="forced">A predicate to add to every query on the type, or null.</param>
    /// <param name="requiredOperators">
    /// The operators satisfying a filtering requirement, or null to require none.
    /// </param>
    /// <param name="facts">
    /// What the rule says about the field that is not an access decision, or null.
    /// </param>
    public RuleDetail(
        TransformStage? transform,
        IReadOnlyList<Operator>? allowedOperators,
        string? alias,
        ForcedPredicate? forced,
        IReadOnlyList<Operator>? requiredOperators,
        FieldFacts? facts = null)
    {
        Transform = transform;
        AllowedOperators = allowedOperators;
        Alias = alias;
        Forced = forced;
        RequiredOperators = requiredOperators;
        Facts = facts;
    }

    /// <summary>One stage of the field's transform chain, or null.</summary>
    public TransformStage? Transform { get; }

    /// <summary>The operators the rule permits, or null when it says nothing about operators.</summary>
    public IReadOnlyList<Operator>? AllowedOperators { get; }

    /// <summary>The public name the rule gives the field, or null.</summary>
    public string? Alias { get; }

    /// <summary>A predicate to add to every query on the type, or null.</summary>
    public ForcedPredicate? Forced { get; }

    /// <summary>The operators satisfying a filtering requirement, or null.</summary>
    public IReadOnlyList<Operator>? RequiredOperators { get; }

    /// <summary>
    /// What the rule says about the field that is not an access decision, or null.
    /// </summary>
    public FieldFacts? Facts { get; }

    /// <summary>A rule carrying none of them, which is the common case.</summary>
    public static RuleDetail None { get; } = new(null, null, null, null, null, null);
}
