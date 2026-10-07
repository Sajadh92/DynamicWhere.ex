using DynamicWhere.ex.Policies.Attributes;

namespace DynamicWhere.ex.Policies.Source;

/// <summary>
/// Reads whether a type declares <c>[DwEntity(RefuseSelects = true)]</c>.
/// </summary>
/// <remarks>
/// Read from the attribute the type carries or inherits, as <c>RequirePolicy</c> and <c>DefaultOrder</c>
/// are: a <c>[DwEntity]</c> on a derived type replaces the base type's.
/// </remarks>
internal static class SelectsRefusal
{
    /// <summary>What a refusal, and the decision recorded for it, names as its source.</summary>
    internal const string Origin = "DwEntityAttribute(RefuseSelects = true)";

    /// <summary>True when <paramref name="type"/> refuses a caller's projection.</summary>
    internal static bool Declared(Type type) =>
        Attribute.GetCustomAttribute(type, typeof(DwEntityAttribute)) is DwEntityAttribute { RefuseSelects: true };

    /// <summary>
    /// The answer for one type, read once.
    /// </summary>
    /// <remarks>
    /// A static generic holds it for the life of the process without a dictionary lookup, since every
    /// guarded filter and segment asks.
    /// </remarks>
    internal static class Of<T>
    {
        internal static readonly bool Declared = SelectsRefusal.Declared(typeof(T));
    }
}
