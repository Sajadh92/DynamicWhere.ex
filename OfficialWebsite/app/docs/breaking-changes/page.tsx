import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Breaking Changes & Known Limitations",
  description:
    "Behaviour, constraints, and edge cases in DynamicWhere.ex that you should know about before shipping to production.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/breaking-changes/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/breaking-changes">
      <h1>Breaking Changes & Known Limitations</h1>
      <p>
        DynamicWhere.ex is intentionally opinionated about how queries are shaped.
        The eleven points below cover constraints, surprises, and corner cases —
        read them before designing an API around the library so you can pick the
        right entry points and avoid runtime exceptions in production.
      </p>

      <h2 id="parameterless-constructor">1. Parameterless Constructor Required for Select Projection</h2>
      <p>
        <code>Select&lt;T&gt;(fields)</code> requires <code>T</code> to have a
        parameterless (default) constructor. If <code>T</code> does not have one, a{" "}
        <code>LogicException</code> is thrown. Most EF Core entity classes have
        parameterless constructors by default.
      </p>
      <Callout tone="danger" title="Hard requirement">
        Records with positional parameters and classes whose only constructor takes
        required arguments are <strong>not</strong> usable as the target type for{" "}
        <Link href="/docs/extensions/select"><code>Select&lt;T&gt;</code></Link> or
        for the typed <Link href="/docs/classes/filter"><code>Filter</code></Link>{" "}
        projection. Use <Link href="/docs/extensions/select-dynamic"><code>SelectDynamic</code></Link>{" "}
        instead, or add an explicit parameterless constructor to your DTO.
      </Callout>

      <h2 id="async-only-segment">2. Segment Operations are Async-Only</h2>
      <p>
        <code>ToListAsync&lt;T&gt;(Segment)</code> is the only entry point for
        segment queries. There is no synchronous <code>ToList&lt;T&gt;(Segment)</code>{" "}
        variant. Each <code>ConditionSet</code> is materialized independently into
        memory, then set operations are performed in‑memory.
      </p>
      <Callout tone="danger" title="No synchronous overload">
        If you need to compose UNION / INTERSECT / EXCEPT across multiple condition
        sets you <em>must</em> use the async pipeline. See{" "}
        <Link href="/docs/classes/segment"><code>Segment</code></Link> and{" "}
        <Link href="/docs/extensions/to-list-async-segment"><code>ToListAsyncSegment</code></Link>.
      </Callout>

      <h2 id="case-insensitive-tolower">3. Case-Insensitive Operators use <code>.ToLower()</code></h2>
      <p>
        All <code>I*</code> operators (e.g., <code>IContains</code>,{" "}
        <code>IEqual</code>) normalize both sides via <code>.ToLower()</code>. This
        works correctly with SQL Server (<code>COLLATE</code> is typically
        case‑insensitive), but be aware of potential performance or behavior
        differences on case‑sensitive database collations (e.g., PostgreSQL with{" "}
        <code>C</code> locale).
      </p>
      <Callout tone="warn" title="Mind your collation">
        On case‑sensitive collations the provider may not be able to use an index
        for a <code>LOWER(column)</code> predicate, which can turn a fast seek into
        a table scan. If you target PostgreSQL with <code>C</code> locale, consider
        a functional index on <code>LOWER(column)</code> or use the case‑sensitive
        operator variants.
      </Callout>

      <h2 id="enum-string-storage">4. Enum Filtering Requires String Storage</h2>
      <p>
        The <code>Enum</code> data type assumes enum values are stored as strings
        (not integers) in the database. If your database stores enums as integers,
        use <code>DataType.Number</code> instead.
      </p>
      <Callout tone="danger" title="Pick the right DataType">
        Configure your EF Core conversion accordingly:{" "}
        <code>.HasConversion&lt;string&gt;()</code> if you want to filter with{" "}
        <Link href="/docs/enums/data-type"><code>DataType.Enum</code></Link>, or
        leave it as the default integer mapping and filter with{" "}
        <code>DataType.Number</code>. Mismatched configurations either throw{" "}
        <code>InvalidFormat</code> at validation time or silently return zero rows.
      </Callout>

      <h2 id="having-aliases">5. Having Clause Fields Reference Aliases, Not Entity Properties</h2>
      <p>
        In a <Link href="/docs/classes/summary"><code>Summary</code></Link>, the{" "}
        <code>Having.ConditionGroup.Conditions[].Field</code> must match an{" "}
        <code>AggregateBy.Alias</code>, not an entity property path.
      </p>
      <Callout tone="warn" title="Aliases only">
        A <code>Having</code> condition that references an entity property directly
        (e.g., <code>"UnitPrice"</code> instead of the alias{" "}
        <code>"AvgPrice"</code>) throws{" "}
        <code>HavingFieldMustExistInAggregateByAlias</code>. See{" "}
        <Link href="/docs/errors">the error code reference</Link>.
      </Callout>

      <h2 id="groupby-flattens-dotted">6. GroupBy Flattens Dotted Field Names in Results</h2>
      <p>
        Dotted <code>GroupBy</code> fields (e.g., <code>Category.Name</code>)
        produce flattened alias keys in the dynamic result objects (e.g.,{" "}
        <code>CategoryName</code>). Order fields in <code>Summary.Orders</code>{" "}
        should use the dotted form; the library handles alias mapping internally.
      </p>
      <Callout tone="warn" title="Dotted in → flattened out">
        Inside the result, access the grouped column as{" "}
        <code>row.CategoryName</code> — not <code>row.Category.Name</code>. When
        writing <code>Summary.Orders</code> entries, keep the dotted form (
        <code>"Category.Name"</code>) and the library will map it to the flattened
        alias for you.
      </Callout>

      <h2 id="collection-any">7. Collection Navigation Auto-Wraps with <code>.Any()</code></h2>
      <p>
        When a condition's <code>Field</code> path traverses a collection property,
        the library automatically inserts <code>.Any()</code> lambdas. This means
        the filter checks if <strong>any</strong> item in the collection matches —
        there is no built‑in <code>.All()</code> support.
      </p>
      <Callout tone="warn" title="No .All() support">
        If you need universal quantification (every child must match), express it
        with a negated <code>.Any()</code> condition or split into two filters.
        See <Link href="/docs/examples/nested-collection">the nested‑collection
        example</Link>.
      </Callout>

      <h2 id="cache-eventually-consistent">8. Thread-Safe Cache, But Configuration Changes are Eventually Consistent</h2>
      <p>
        <code>CacheExpose.Configure()</code> is thread‑safe, but already‑in‑progress
        operations may use the previous configuration until they complete.
      </p>
      <Callout tone="warn" title="In-flight calls keep the old config">
        Treat configuration as a startup concern when possible. Hot‑swapping the
        cache strategy at peak traffic is safe but won't retroactively re‑classify
        ongoing operations. See{" "}
        <Link href="/docs/cache/configuration">cache configuration</Link>.
      </Callout>

      <h2 id="get-query-string-ef-core">9. <code>getQueryString</code> Parameter Requires EF Core Provider</h2>
      <p>
        Passing <code>getQueryString: true</code> to <code>ToList</code> /{" "}
        <code>ToListAsync</code> calls <code>.ToQueryString()</code> which requires
        an active EF Core database provider. It will fail on pure in‑memory{" "}
        <code>IEnumerable&lt;T&gt;</code> calls (use the <code>IEnumerable</code>{" "}
        overloads which internally call <code>AsQueryable()</code> first, but{" "}
        <code>ToQueryString()</code> may not be supported).
      </p>
      <Callout tone="warn" title="EF Core only">
        Only enable <code>getQueryString</code> when the source is a real{" "}
        <code>DbSet&lt;T&gt;</code> or an EF Core‑backed{" "}
        <code>IQueryable&lt;T&gt;</code>. On in‑memory collections you'll get a
        provider exception. Use it as a development aid, not as a production
        feature.
      </Callout>

      <h2 id="dynamic-return-types">10. <code>SelectDynamic</code> / <code>FilterDynamic</code> / <code>ToListDynamic</code> / <code>ToListAsyncDynamic</code> Return Non-Generic Types</h2>
      <p>
        These methods return <code>IQueryable</code> or{" "}
        <code>FilterResult&lt;dynamic&gt;</code> instead of the strongly‑typed
        equivalents. Downstream code must work with <code>dynamic</code> objects.
        Property names in the dynamic result follow these rules:
      </p>
      <ul>
        <li>
          <strong>Non‑dotted paths</strong> (<code>Name</code>,{" "}
          <code>Category</code>, <code>OrderItems</code>, …) are projected as‑is —
          access them by their exact field name at runtime.
        </li>
        <li>
          <strong>Dotted paths through reference navigations</strong> (e.g.,{" "}
          <code>Category.Name</code>) produce <strong>nested dynamic objects</strong>{" "}
          reflecting the navigation hierarchy — access them as{" "}
          <code>result.Category.Name</code>, not as a flat{" "}
          <code>CategoryName</code>.
        </li>
        <li>
          <strong>Dotted paths through collection navigations</strong> (e.g.,{" "}
          <code>Category.Vendors.Id</code>) generate a <code>Select</code> lambda
          per collection segment — the result is a nested collection of dynamic
          objects accessible as <code>result.Category.Vendors[0].Id</code>.
        </li>
        <li>
          <strong>Multiple dotted fields</strong> sharing the same root segment
          (e.g., <code>Category.Name</code> + <code>Category.Id</code>) are merged
          into a single nested object: <code>result.Category.Name</code> and{" "}
          <code>result.Category.Id</code>.
        </li>
        <li>
          <strong>Mixed whole‑navigation + sub‑field paths</strong>: when both{" "}
          <code>"Category"</code> and <code>"Category.Name"</code> are requested,
          the sub‑field projection takes precedence and <code>"Category"</code> is
          silently dropped.
        </li>
      </ul>
      <Callout tone="warn" title="Two different shapes — typed vs. dynamic">
        Note that <Link href="/docs/extensions/select-dynamic"><code>SelectDynamic</code></Link>{" "}
        preserves the navigation hierarchy (nested), whereas the typed{" "}
        <code>GroupBy</code> result flattens dotted names (point&nbsp;6). They are
        different on purpose — pick the extension method that matches the shape
        your client expects.
      </Callout>

      <h2 id="order-page-before-projection">11. All Filter Extensions Apply Order and Page Before the Select Projection</h2>
      <p>
        All Filter extensions — both typed (<code>Filter&lt;T&gt;</code>,{" "}
        <code>ToList&lt;T&gt;(Filter)</code>,{" "}
        <code>ToListAsync&lt;T&gt;(Filter)</code>) and dynamic (
        <code>FilterDynamic&lt;T&gt;</code>, <code>ToListDynamic&lt;T&gt;</code>,{" "}
        <code>ToListAsyncDynamic&lt;T&gt;</code>) — apply ordering and pagination
        on the typed <code>IQueryable&lt;T&gt;</code> <strong>before</strong> the
        select projection. This ensures that field names referenced in{" "}
        <code>orders</code> always resolve against the original entity type{" "}
        <code>T</code>, regardless of which fields are projected.
      </p>
      <Callout tone="warn" title="Order on the entity, project after">
        You can sort by a column that is not in your <code>Select.Fields</code>{" "}
        list. The library resolves <code>orders[].field</code> against{" "}
        <code>T</code>'s original property graph, then projects to the requested
        subset. This is the right behaviour for almost every list endpoint — it
        just surprises people who expected the order field to also need to be in
        the projection.
      </Callout>

      <h2 id="next">See also</h2>
      <ul>
        <li>
          <Link href="/docs/errors">Error Codes Reference →</Link> the 22 stable
          validation messages.
        </li>
        <li>
          <Link href="/docs/cache/configuration">Cache configuration →</Link>{" "}
          context for point 8.
        </li>
        <li>
          <Link href="/docs/extensions/select-dynamic"><code>SelectDynamic</code> →</Link>{" "}
          context for points 1 and 10.
        </li>
      </ul>
    </DocPage>
  );
}
