namespace DynamicWhere.ex.Policies.Attributes;

/// <summary>
/// Declares type-level policy behaviour.
/// </summary>
/// <remarks>
/// <see cref="RequirePolicy"/> closes the hole in an opt-in model: without it, every field policy
/// on a type is bypassed simply by not applying a policy context to the query. It is enforced by
/// every one of this library's extension methods, on the <c>IQueryable</c> and <c>IEnumerable</c>
/// paths alike: reaching such a type through any of them without <c>ApplyPolicy</c> throws
/// <c>PolicyException</c> with <c>PolicyRequired</c>.
/// <para>
/// What it does not cover is plain LINQ. The guard lives in this library's methods, so a
/// <c>DbSet</c> queried directly is not intercepted — the flag protects the paths that read a
/// filter, which is the shape an API exposes.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class DwEntityAttribute : Attribute
{
    /// <summary>
    /// When true, querying this type without a policy context throws instead of returning rows.
    /// </summary>
    public bool RequirePolicy { get; set; }

    /// <summary>
    /// The order a guarded query takes when its caller sends none, such as <c>"CreatedAt desc, Id"</c>.
    /// </summary>
    /// <remarks>
    /// Comma-separated fields, each optionally followed by <c>asc</c> or <c>desc</c> in any letter case;
    /// ascending when neither is written. It applies only to a query guarded by <c>ApplyPolicy</c>:
    /// <c>ToList</c>, <c>ToListAsync</c>, <c>ToListDynamic</c> and <c>ToListAsyncDynamic</c> with a
    /// <c>Filter</c>, <c>ToListAsync</c> with a <c>Segment</c>, the composable <c>Filter</c> and
    /// <c>FilterDynamic</c>, and <c>Page</c>. An unguarded query never reads it, and is ordered only as
    /// its caller asks. A caller who sends orders gets exactly those, unless <see cref="DefaultOrderAsTiebreak"/>
    /// or the posture's <c>DefaultOrderAsTiebreak</c> appends the default to them. A query already ordered keeps that order, whether an <c>OrderBy</c> ordered it before
    /// <c>ApplyPolicy</c> or a composed <c>Order</c> did, even one whose every order the policy dropped. A
    /// projected query takes the default when its outermost <c>Select</c> builds the type in an object
    /// initializer and assigns every field the default names a column — a member the model maps, read
    /// directly, through reference navigations or through <c>EF.Property</c> — at every level of a
    /// nested path. That is the shape a caller writes who projects a row type before guarding it. A
    /// projection with no initializer at all, <c>new TicketRow(t.Id, t.Code)</c>, a field the
    /// initializer leaves unassigned, and a field the projection computes each leave the query in its
    /// own order, because a default must never be the reason a query that ran unguarded fails. A
    /// constructor with arguments <i>and</i> an initializer still takes the default, since the
    /// initializer is what says which member holds which column. A <c>Select</c> composed on the
    /// guarded handle leaves the rest of the chain unordered; a <c>Filter</c> carrying
    /// <c>Selects</c> is ordered as any other filter is. An in-memory sequence sorted before
    /// <c>ApplyPolicy</c> is not recognised as ordered, because it reaches the policy as a query with no
    /// <c>OrderBy</c> in it, so send its order with the filter. End the default with a unique field, such
    /// as the key, or rows sharing the leading values can still change places between pages.
    /// <para>
    /// Nothing is ordered that this property does not name. An entry naming a field the type does not
    /// have, one that is not a field and a direction, or one no query can order by, such as a
    /// collection of entities, is skipped rather than refused, and <c>PolicyModelValidator</c> reports
    /// it. A field the caller may not order by is left out as well, and in a <c>Segment</c> so is a field
    /// the caller may not use in a segment; the trace records each one. Ordering by it would rank rows by
    /// a value the caller is not allowed to see. A field its own attributes seal against ordering fails
    /// the startup scan; one denied for ordering only by overridable attributes, or denied for segments,
    /// is reported as a warning.
    /// </para>
    /// <para>
    /// A field the default keeps that is audited for ordering is recorded as a use, as a caller's own
    /// order is; a field left out is not. A <c>[DwEntity]</c> on a derived type replaces this one, so
    /// repeat <c>DefaultOrder</c> and <c>RequirePolicy</c> there.
    /// </para>
    /// </remarks>
    public string? DefaultOrder { get; set; }

    /// <summary>
    /// When true, a guarded query whose caller sends orders ends them with every field of
    /// <see cref="DefaultOrder"/> the caller did not name, so rows tied on the caller's fields keep one
    /// order from page to page.
    /// </summary>
    /// <remarks>
    /// A caller sorting by a field many rows share, such as a status or a timestamp, leaves the order of
    /// those rows to the database, which may return them differently on every query: paging then shows one
    /// row on two pages and another on none, while the total stays right. With this set, the default's
    /// fields follow the caller's last <c>Sort</c>, in their declared direction. A field the caller already
    /// orders by keeps the caller's place and direction, and is not added again.
    /// <para>
    /// The order is total only when the default ends with a unique field, such as the key. The default's
    /// own rules hold: a field this caller may not order by is left out, and in a <c>Segment</c> so is a
    /// field the caller may not use there. A caller whose every order was dropped gets the whole default.
    /// It applies wherever a guarded query takes a caller's orders: the <c>Filter</c> terminals,
    /// <c>ToListAsync</c> with a <c>Segment</c>, the composable <c>Filter</c>, <c>FilterDynamic</c> and
    /// <c>Order</c>, and the simulator. A projection that leaves out a field the default names, or a
    /// <c>Select</c> composed on the guarded handle, leaves the caller's orders as they are, as it leaves
    /// the default out. A summary's orders rank groups and never take it. A query whose source was ordered
    /// before <c>ApplyPolicy</c> still takes it after the caller's orders, which replace that order.
    /// </para>
    /// <para>
    /// The posture's <c>DwPolicyOptions.DefaultOrderAsTiebreak</c> turns it on for every type that
    /// declares a default; this property turns it on for one. A type without a usable
    /// <see cref="DefaultOrder"/> has nothing to append, and <c>PolicyModelValidator</c> warns when this
    /// property is set on one. A <c>[DwEntity]</c> on a derived type replaces this one, so repeat it there.
    /// </para>
    /// </remarks>
    public bool DefaultOrderAsTiebreak { get; set; }

    /// <summary>
    /// When true, a guarded query that sends a non-empty <c>Selects</c> for this type is refused with
    /// <c>PolicyException</c> and <c>SelectsRefused</c>.
    /// </summary>
    /// <remarks>
    /// For a row type that is meant to be read whole, such as a read-only projection, where a partial row
    /// would report default values for every member the caller did not name. The refusal is the
    /// policy's own, so it is traced, audited when the posture audits refusals, and reported by the
    /// simulator; a host refusing the list in its endpoint before the gate runs would hide a probe from
    /// all three.
    /// <para>
    /// The names in the list are gated first, exactly as on any other type. Under the strict tier a name
    /// the caller may not select is refused as <c>FieldDeniedForSelect</c> and audited under its own path,
    /// and under either tier a list whose every name was dropped is refused as <c>AllSelectsDenied</c>;
    /// only a list of names the caller may select reaches <c>SelectsRefused</c>. Both tiers refuse, since
    /// dropping the list would hand back whole rows the caller did not ask for. A dry run records the
    /// decision and runs the query as written.
    /// </para>
    /// <para>
    /// It applies wherever a guarded query takes a caller's projection: the <c>Filter</c> terminals,
    /// <c>ToListAsync</c> with a <c>Segment</c>, the composable <c>Filter</c>, <c>FilterDynamic</c>,
    /// <c>Select</c> and <c>SelectDynamic</c>, and the simulator. It never refuses a <c>Selects</c> that is
    /// null or empty, which means what it means on any type, and a projection the policy synthesizes to
    /// withhold denied fields is the library's, not the caller's. An unguarded query never reads it, so pair it with
    /// <see cref="RequirePolicy"/>; <c>PolicyModelValidator</c> warns when it stands alone. A
    /// <c>[DwEntity]</c> on a derived type replaces this one, so repeat it there.
    /// </para>
    /// </remarks>
    public bool RefuseSelects { get; set; }
}
