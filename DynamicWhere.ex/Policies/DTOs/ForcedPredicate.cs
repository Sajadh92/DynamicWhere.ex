using DynamicWhere.ex.Enums;

namespace DynamicWhere.ex.Policies.DTOs;

/// <summary>
/// One predicate the library adds to a query on the caller's behalf: the resolved form of a
/// <c>[DwForceWhere]</c> attribute or of the equivalent runtime rule.
/// </summary>
/// <remarks>
/// Immutable, and deliberately not a <see cref="Classes.Core.Condition"/>. A condition holds a
/// literal value, and a predicate reading <see cref="ContextValue"/> does not have one until a
/// caller is known — the same predicate means a different tenant for every request. The conversion
/// happens at injection time, against a context.
/// <para>
/// The <see cref="DataType"/> is settled here rather than at injection because it is a property of
/// the field, not of the request: it is read from the decorated member's own CLR type, which is the
/// type the pipeline is about to validate the value against.
/// </para>
/// </remarks>
public sealed class ForcedPredicate
{
    private ForcedPredicate(
        string fieldPath,
        Operator op,
        DataType dataType,
        string? value,
        string? contextValue,
        bool allowNull = false)
    {
        FieldPath = fieldPath;
        Operator = op;
        DataType = dataType;
        Value = value;
        ContextValue = contextValue;
        AllowNull = allowNull;
    }

    /// <summary>The field the predicate filters on.</summary>
    public string FieldPath { get; }

    /// <summary>The operator the injected condition uses.</summary>
    public Operator Operator { get; }

    /// <summary>The data type of the value, taken from the field's own CLR type.</summary>
    public DataType DataType { get; }

    /// <summary>The constant to filter by, or null when the value comes from the context.</summary>
    public string? Value { get; }

    /// <summary>The context key to read the value from, or null when the value is a constant.</summary>
    public string? ContextValue { get; }

    /// <summary>True when this predicate needs an ambient value from the caller's context.</summary>
    public bool ReadsContext => ContextValue is not null;

    /// <summary>
    /// True when a row whose field is null also passes: the predicate is injected as
    /// <c>(field op value OR field IS NULL)</c>.
    /// </summary>
    /// <remarks>
    /// The shape of a record that belongs to one tenant or to none, such as a system role no
    /// institution owns. Two forced predicates on one member are joined by <c>And</c>, so no pair of
    /// them can say "or null"; this is the only way to. Never true for a null check, which compares
    /// against nothing and has no comparison to widen.
    /// </remarks>
    public bool AllowNull { get; }

    /// <summary>
    /// Creates a predicate filtering by a constant.
    /// </summary>
    /// <param name="fieldPath">The field to filter on.</param>
    /// <param name="op">The operator to use.</param>
    /// <param name="dataType">The field's data type.</param>
    /// <param name="value">The constant, in string form.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="fieldPath"/> or <paramref name="value"/> is blank.
    /// </exception>
    public static ForcedPredicate FromConstant(string fieldPath, Operator op, DataType dataType, string value) =>
        FromConstant(fieldPath, op, dataType, value, allowNull: false);

    /// <summary>
    /// Creates a predicate filtering by a constant, optionally letting a null field pass as well.
    /// </summary>
    /// <param name="fieldPath">The field to filter on.</param>
    /// <param name="op">The operator to use.</param>
    /// <param name="dataType">The field's data type.</param>
    /// <param name="value">The constant, in string form.</param>
    /// <param name="allowNull">True to inject <c>(field op value OR field IS NULL)</c>.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="fieldPath"/> or <paramref name="value"/> is blank.
    /// </exception>
    public static ForcedPredicate FromConstant(
        string fieldPath, Operator op, DataType dataType, string value, bool allowNull)
    {
        Require(fieldPath, nameof(fieldPath), "A forced predicate requires a field path.");
        Require(value, nameof(value), "A forced predicate built from a constant requires a value.");

        return new ForcedPredicate(fieldPath.Trim(), op, dataType, value, contextValue: null, allowNull);
    }

    /// <summary>
    /// Creates a predicate filtering by a value read from the caller's context.
    /// </summary>
    /// <param name="fieldPath">The field to filter on.</param>
    /// <param name="op">The operator to use.</param>
    /// <param name="dataType">The field's data type.</param>
    /// <param name="contextValue">The context key holding the value.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="fieldPath"/> or <paramref name="contextValue"/> is blank.
    /// </exception>
    public static ForcedPredicate FromContext(
        string fieldPath, Operator op, DataType dataType, string contextValue) =>
        FromContext(fieldPath, op, dataType, contextValue, allowNull: false);

    /// <summary>
    /// Creates a predicate filtering by a value read from the caller's context, optionally letting a
    /// null field pass as well.
    /// </summary>
    /// <param name="fieldPath">The field to filter on.</param>
    /// <param name="op">The operator to use.</param>
    /// <param name="dataType">The field's data type.</param>
    /// <param name="contextValue">The context key holding the value.</param>
    /// <param name="allowNull">True to inject <c>(field op value OR field IS NULL)</c>.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="fieldPath"/> or <paramref name="contextValue"/> is blank.
    /// </exception>
    /// <remarks>
    /// The context value itself must still be supplied. Allowing a null field says which rows pass;
    /// it does not let a caller with no tenant through as though they had one.
    /// </remarks>
    public static ForcedPredicate FromContext(
        string fieldPath, Operator op, DataType dataType, string contextValue, bool allowNull)
    {
        Require(fieldPath, nameof(fieldPath), "A forced predicate requires a field path.");
        Require(
            contextValue,
            nameof(contextValue),
            "A forced predicate reading the context requires a key to read.");

        return new ForcedPredicate(fieldPath.Trim(), op, dataType, value: null, contextValue.Trim(), allowNull);
    }

    /// <summary>
    /// Creates a predicate testing a field for null, which needs no value at all.
    /// </summary>
    /// <param name="fieldPath">The field to test.</param>
    /// <param name="op">Either <see cref="Operator.IsNull"/> or <see cref="Operator.IsNotNull"/>.</param>
    /// <param name="dataType">The field's data type.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="fieldPath"/> is blank, or <paramref name="op"/> is an operator
    /// that needs a value.
    /// </exception>
    /// <remarks>
    /// Soft deletion is usually spelled this way — <c>DeletedAt IS NULL</c> — and without this the
    /// commonest forced predicate of all would have to be written as a comparison against a
    /// sentinel date.
    /// </remarks>
    public static ForcedPredicate FromNullCheck(string fieldPath, Operator op, DataType dataType)
    {
        Require(fieldPath, nameof(fieldPath), "A forced predicate requires a field path.");

        if (op is not (Operator.IsNull or Operator.IsNotNull))
        {
            throw new ArgumentException(
                $"'{op}' compares against a value, so it cannot be built as a null check.", nameof(op));
        }

        return new ForcedPredicate(fieldPath.Trim(), op, dataType, value: null, contextValue: null);
    }

    /// <summary>True when this predicate compares against nothing, because it tests for null.</summary>
    public bool IsNullCheck => Operator is Operator.IsNull or Operator.IsNotNull;

    /// <summary>Refuses a blank argument.</summary>
    private static void Require(string value, string parameter, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, parameter);
        }
    }
}
