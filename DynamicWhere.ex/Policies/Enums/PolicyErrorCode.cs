namespace DynamicWhere.ex.Policies.Enums;

/// <summary>
/// The closed set of reasons a policy refuses part of a query.
/// </summary>
/// <remarks>
/// Typed rather than a bare string so a caller can switch exhaustively with compiler help:
/// <c>catch (PolicyException ex) when (ex.ErrorCode == PolicyErrorCode.FieldDeniedForWhere)</c>
/// binds to a symbol the library can rename safely, where a string literal would silently stop
/// matching. <c>PolicyException.Code</c> keeps the string form for logging and serialization.
/// <para>
/// The library's internal <c>ErrorCode</c> class is deliberately not used here. It holds around
/// thirty members, most of them validation strings unrelated to policy, and publishing all of them
/// to expose these ten would commit the rest as API surface permanently.
/// </para>
/// <para>
/// Members are appended, never renumbered: the numeric values are part of the contract once a
/// caller has serialized one.
/// </para>
/// </remarks>
public enum PolicyErrorCode
{
    /// <summary>Filtering on the requested field is refused.</summary>
    FieldDeniedForWhere = 1,

    /// <summary>Projecting the requested field is refused.</summary>
    FieldDeniedForSelect = 2,

    /// <summary>Sorting by the requested field is refused.</summary>
    FieldDeniedForOrder = 3,

    /// <summary>Grouping by the requested field is refused.</summary>
    FieldDeniedForGroup = 4,

    /// <summary>Aggregating the requested field is refused.</summary>
    FieldDeniedForAggregate = 5,

    /// <summary>The requested field is refused inside a set operation.</summary>
    FieldDeniedForSegment = 6,

    /// <summary>Every requested projection field was refused, leaving no projection.</summary>
    AllSelectsDenied = 7,

    /// <summary>The requested operator is not permitted on this field.</summary>
    OperatorNotAllowed = 8,

    /// <summary>The query exceeded a configured cap.</summary>
    CapExceeded = 9,

    /// <summary>The entity requires a policy context and the query supplied none.</summary>
    PolicyRequired = 10
}
