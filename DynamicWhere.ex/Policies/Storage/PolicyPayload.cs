using System.Text;
using System.Text.Json;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.ex.Policies.Storage;

/// <summary>
/// Converts between a persisted JSON payload and a typed <see cref="TransformStage"/>.
/// </summary>
/// <remarks>
/// One routine for every store, so that a database, a cache and the in-memory implementation cannot
/// be held to different standards — the same reason the alias validator is shared between
/// attributes and rules.
/// <para>
/// <b>Nothing here returns null on a document it cannot read.</b> Design section 5.1 gives a rule a
/// JSON payload, but Phase 4 moved transform detail onto typed carriers precisely because a failed
/// read yields null, and null in that position means "no transform" — so a malformed mask would
/// ship the value the database holds. Every failure throws, and the rule carrying it is rejected
/// rather than stored in a form that silently does nothing.
/// </para>
/// <para>
/// Enumerations are read by name and must be defined. A number is what an absent column produces,
/// and <see cref="MaskStrategy"/> has a member at zero.
/// </para>
/// </remarks>
public static class PolicyPayload
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = false };

    /// <summary>
    /// Reads a payload into the stage it describes.
    /// </summary>
    /// <param name="json">The payload, as a store holds it.</param>
    /// <returns>The stage, never null.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when the payload is blank, is not a JSON object, names no kind, names a kind this
    /// library does not ship, or omits a parameter its stage cannot work without.
    /// </exception>
    public static TransformStage ToStage(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException(
                "A transform payload cannot be blank. A rule that carries no readable transform " +
                "would emit the value exactly as stored.", nameof(json));
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
                $"The transform payload is not valid JSON: {error.Message}", nameof(json), error);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException(
                $"A transform payload must be a JSON object; this one is {root.ValueKind}.",
                nameof(json));
        }

        TransformKind kind = ReadEnum<TransformKind>(root, "kind", required: true)!.Value;

        return kind switch
        {
            TransformKind.Mask => ReadMask(root),
            TransformKind.Generalize => ReadGeneralize(root),
            TransformKind.Format => new FormatStage(ReadString(root, "format") ?? string.Empty),
            TransformKind.Truncate => new TruncateStage(
                ReadInt(root, "length") ?? throw Missing("length", "a truncation"),
                ReadString(root, "ellipsis")),
            TransformKind.Default => new DefaultStage(
                ReadString(root, "value"), root.TryGetProperty("value", out _)),

            // Refused in both directions. See ToJson.
            TransformKind.Mutate => throw MutateRefused(),
            _ => throw new ArgumentException(
                $"'{kind}' is not a transform kind this library can read from a payload.",
                nameof(json))
        };
    }

    /// <summary>
    /// Writes a stage as the payload a store persists.
    /// </summary>
    /// <param name="stage">The stage to write.</param>
    /// <returns>A JSON object <see cref="ToStage"/> reads back.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="stage"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown for a stage a store may not carry.
    /// </exception>
    public static string ToJson(TransformStage stage)
    {
        if (stage is null)
        {
            throw new ArgumentNullException(nameof(stage));
        }

        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", stage.Kind.ToString());

            switch (stage)
            {
                case MaskStage mask:
                    writer.WriteString("strategy", mask.Strategy.ToString());
                    writer.WriteNumber("keepStart", mask.KeepStart);
                    writer.WriteNumber("keepEnd", mask.KeepEnd);
                    writer.WriteString("maskChar", mask.MaskChar.ToString());
                    writer.WriteBoolean("preserveLength", mask.PreserveLength);

                    if (mask.Pattern is not null)
                    {
                        writer.WriteString("pattern", mask.Pattern);
                        writer.WriteString("replacement", mask.Replacement);
                    }

                    if (mask.Text is not null)
                    {
                        writer.WriteString("text", mask.Text);
                    }

                    break;

                case GeneralizeStage generalize:
                    writer.WriteString("mode", generalize.Mode.ToString());
                    writer.WriteNumber("step", generalize.Step);
                    writer.WriteString("part", generalize.Part.ToString());
                    writer.WriteNumber("decimals", generalize.Decimals);

                    break;

                case FormatStage format:
                    writer.WriteString("format", format.Format);

                    break;

                case TruncateStage truncate:
                    writer.WriteNumber("length", truncate.Length);

                    if (truncate.Ellipsis is not null)
                    {
                        writer.WriteString("ellipsis", truncate.Ellipsis);
                    }

                    break;

                case DefaultStage replacement:
                    // Written only when one was supplied, so that ToStage can tell [DwDefault] from
                    // [DwDefault(null)] by the property's presence rather than by its value.
                    if (replacement.HasValue)
                    {
                        if (replacement.Value is null)
                        {
                            writer.WriteNull("value");
                        }
                        else
                        {
                            writer.WriteString("value", replacement.Value);
                        }
                    }

                    break;

                case MutateStage:
                    throw MutateRefused();

                default:
                    throw new ArgumentException(
                        $"'{stage.Kind}' is not a transform kind this library can persist.",
                        nameof(stage));
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Builds a mask stage, letting the stage itself refuse an unworkable combination.</summary>
    private static MaskStage ReadMask(JsonElement root) =>
        new(ReadEnum<MaskStrategy>(root, "strategy", required: true)!.Value,
            ReadInt(root, "keepStart") ?? 0,
            ReadInt(root, "keepEnd") ?? 0,
            ReadChar(root, "maskChar") ?? '*',
            ReadBool(root, "preserveLength") ?? true,
            ReadString(root, "pattern"),
            ReadString(root, "replacement"),
            ReadString(root, "text"));

    /// <summary>Builds a generalization stage.</summary>
    private static GeneralizeStage ReadGeneralize(JsonElement root) =>
        new(ReadEnum<GeneralizeMode>(root, "mode", required: true)!.Value,
            ReadInt(root, "step") ?? 0,
            ReadEnum<DatePart>(root, "part", required: false) ?? DatePart.Year,
            ReadInt(root, "decimals") ?? 0);

    /// <summary>
    /// Reads an enumeration member by name.
    /// </summary>
    /// <remarks>
    /// By name, never by number, and the name must parse to a defined member. A numeric value is
    /// what an absent or defaulted column produces, and both <see cref="MaskStrategy"/> and
    /// <see cref="TransformKind"/> have a member at zero — so accepting a number would turn a
    /// missing field into a real instruction.
    /// </remarks>
    private static TEnum? ReadEnum<TEnum>(JsonElement root, string name, bool required)
        where TEnum : struct, Enum
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return required ? throw Missing(name, typeof(TEnum).Name) : null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ArgumentException(
                $"'{name}' must be the name of a {typeof(TEnum).Name}, not a " +
                $"{value.ValueKind}. A number is what an absent column produces, and zero is a " +
                "real member of these enumerations.");
        }

        string text = value.GetString() ?? string.Empty;

        if (!Enum.TryParse(text, ignoreCase: true, out TEnum parsed)
            || !Enum.IsDefined(typeof(TEnum), parsed))
        {
            throw new ArgumentException(
                $"'{text}' is not a {typeof(TEnum).Name} this library defines.");
        }

        return parsed;
    }

    /// <summary>Reads an optional string, treating an explicit null as absent.</summary>
    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Reads an optional integer, refusing a value that is present but not a number.</summary>
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

    /// <summary>Reads an optional single character.</summary>
    private static char? ReadChar(JsonElement root, string name)
    {
        string? text = ReadString(root, name);

        if (text is null)
        {
            return null;
        }

        return text.Length == 1
            ? text[0]
            : throw new ArgumentException($"'{name}' must be exactly one character.");
    }

    /// <summary>Reports a parameter a stage cannot be built without.</summary>
    private static ArgumentException Missing(string name, string stage) =>
        new($"A payload for {stage} requires '{name}'.");

    /// <summary>
    /// Refuses a transformer named by a store.
    /// </summary>
    /// <remarks>
    /// A payload naming a CLR type to construct escalates a store from "can change policy" to "can
    /// construct arbitrary types", which is a different thing from the disclosure the sealed
    /// ceiling is designed to bound — and design section 5.1 names only a mask spec, a default
    /// value and a bucket step as the payload's purpose. <c>[DwMutate]</c> is source code and stays
    /// available; a row in a table is not.
    /// <para>
    /// Refused when writing as well as when reading, so the asymmetry cannot be discovered by an
    /// operator whose rule saved and then would not load.
    /// </para>
    /// </remarks>
    private static ArgumentException MutateRefused() =>
        new("A transformer cannot be named by a stored payload: it would let whatever can write to " +
            "the store construct an arbitrary type. Declare it with the [DwMutate] attribute, " +
            "which is source code and is reviewed as such.");
}
