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
    PolicyRequired = 10,

    /// <summary>
    /// A field the policy requires the caller to filter on was not filtered on in a way that
    /// narrows the result.
    /// </summary>
    RequiredFilterMissing = 11,

    /// <summary>
    /// A forced predicate reads an ambient value the caller's context does not supply.
    /// </summary>
    MissingContextValue = 12,

    /// <summary>
    /// A field name the caller used could mean more than one field, so the query was refused rather
    /// than resolved in favour of one.
    /// </summary>
    /// <remarks>
    /// Reachable because an alias may come from a runtime rule as well as an attribute, so an alias
    /// can be introduced that collides with a real property path, or with another alias. Choosing
    /// either reading silently sends a filter somewhere the caller did not mean.
    /// </remarks>
    AmbiguousFieldName = 13,

    /// <summary>
    /// The caller asked for the generated SQL, which the strict tier does not return.
    /// </summary>
    /// <remarks>
    /// The query text names the columns of denied fields and spells out every injected predicate,
    /// so handing it to a semi-trusted caller discloses both the schema and the shape of the scope
    /// confining them.
    /// </remarks>
    QueryStringDenied = 14,

    /// <summary>
    /// Two groups of a summary share a key once their key values were transformed.
    /// </summary>
    /// <remarks>
    /// Rounding two salaries into one band, or masking two identifiers into one run of stars, leaves
    /// rows that look like duplicates and whose aggregates cannot be added together without
    /// inventing a figure the database never computed. Refused rather than merged.
    /// </remarks>
    AmbiguousGroupKey = 15,

    /// <summary>
    /// A method that returns an unmaterialized query was called on a type whose values are
    /// transformed on output.
    /// </summary>
    /// <remarks>
    /// Transformation happens on materialized objects, so a query the caller materializes themselves
    /// is one the library never sees and cannot transform. The terminal methods return transformed
    /// data; <c>AsUnguardedQueryable</c> is the explicit way out.
    /// </remarks>
    TransformRequiresMaterialization = 16,

    /// <summary>
    /// The policy store cannot be trusted to describe the policy in force, so every guarded query
    /// is refused.
    /// </summary>
    /// <remarks>
    /// Raised when the last successful load is older than <c>MaxSnapshotAge</c>, and immediately on
    /// a refresh failure under <c>StoreFailureMode.FailClosed</c>.
    /// <para>
    /// It is an exception rather than a blanket denial for a specific reason: a denial competes in
    /// the ordinary election, and a sealed <c>[DwOperators]</c> allowance outranks a dynamic one —
    /// so every field carrying an operator restriction would have stayed filterable in exactly the
    /// state this refusal exists to produce. Nothing outranks an exception.
    /// </para>
    /// </remarks>
    StoreUnavailable = 17,

    /// <summary>
    /// A guarded query used a context that was never prepared against the policy store.
    /// </summary>
    /// <remarks>
    /// A store is read asynchronously and the query path is synchronous, so a caller's user-level
    /// rules are fetched once when the context is built and pinned to it. A context that skipped
    /// that step could only be served from the broad zone, where a denial written for one user
    /// silently does not appear — so it is refused instead. Build the context through
    /// <c>DwPolicy.PrepareAsync</c>, once per request.
    /// </remarks>
    PolicyContextNotPrepared = 18
}
