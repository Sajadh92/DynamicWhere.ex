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
        The twenty-nine points below cover constraints, surprises, and corner cases —
        read them before designing an API around the library so you can pick the
        right entry points and avoid runtime exceptions in production.
      </p>
      <Callout tone="danger" title="Behaviour changes in 3.2.0">
        Points&nbsp;25 to 29 changed in <strong>3.2.0</strong>, and each is
        visible to code written for 3.1.0. A guarded query that sends no{" "}
        <code>Selects</code> synthesizes its projection whenever a denied value
        can reach the result, and the projection keeps what the source carries:
        the assigned members of a projected row, and an entity&apos;s columns,
        owned and complex members, while an entity&apos;s navigations and the
        objects of a row in memory are left out (point&nbsp;25).{" "}
        <code>Selects</code> naming a member is gated against every denial
        beneath it, and a narrowing that cannot be built is refused
        (point&nbsp;26). A declared default order reaches a projection that
        builds <code>T</code> and assigns every field the default names
        (point&nbsp;27). Every async terminal gains overloads that take a{" "}
        <code>CancellationToken</code>, so <code>ToListAsync(filter, default)</code>{" "}
        no longer compiles, and the async dynamic <code>Filter</code> and the
        async <code>Summary</code> read through EF Core&apos;s asynchronous
        operators (point&nbsp;28). And a type in an application namespace that
        starts with <code>System</code> is policed (point&nbsp;29).
      </Callout>
      <Callout tone="danger" title="Behaviour changes in 3.1.0">
        Eleven behaviours changed in <strong>3.1.0</strong>. Each one fixes a defect,
        and each one is visible to a caller that depended on the old shape. Date
        comparisons now read the member&apos;s type before building
        the predicate (point&nbsp;14) and accept a value only in ISO&nbsp;8601, a
        year-first form, or a format the deployment declares (point&nbsp;15); an
        unpaged <code>PageCount</code> is now <code>1</code>{" "}
        rather than <code>TotalCount</code> (point&nbsp;16); the{" "}
        <code>Select</code> constructor refusal carries a stable code instead of an
        English sentence (point&nbsp;17); a guarded query is refused unless its
        context was prepared (point&nbsp;18); a segment&apos;s condition sets
        are combined, ordered and paged in the database (point&nbsp;19); a member
        named <code>Root</code>, <code>It</code> or <code>Parent</code> is read as
        that member rather than as the row, and{" "}
        <code>ParsingConfig.Default</code> is no longer read (point&nbsp;20); a
        guarded request whose condition groups nest deeper than{" "}
        <code>MaxConditionDepth</code>, or a guarded segment with more sets than{" "}
        <code>MaxConditionSets</code>, is refused (point&nbsp;21); under the
        strict tier a result no longer carries the policy trace (point&nbsp;22),
        and a field that does not exist is refused exactly as a denied one is, with
        no field named (point&nbsp;23); and a guarded condition carrying more
        values than <code>MaxConditionValues</code>, or a guarded summary computing
        more aggregates than <code>MaxAggregates</code>, is refused, while a{" "}
        <code>Count</code> is now charged to the query budget (point&nbsp;24).
      </Callout>
      <Callout tone="danger" title="Upgrade to 2.1.4">
        Releases before <strong>2.1.4</strong> did not escape condition values before
        embedding them in the generated expression. A value carrying a{" "}
        <code>\</code> or a <code>&quot;</code> ended its string literal early — a search
        term ending in <code>\</code> threw{" "}
        <code>ParseException: &apos;)&apos; or &apos;,&apos; expected</code>, and a crafted value
        could close the literal and append predicate logic of its own, returning rows
        the filter should never have matched. See point&nbsp;12.
      </Callout>

      <h2 id="parameterless-constructor">1. Parameterless Constructor Required for Select Projection</h2>
      <p>
        <code>Select&lt;T&gt;(fields)</code> requires <code>T</code> to have a
        parameterless (default) constructor. If <code>T</code> does not have one, a{" "}
        <code>LogicException</code> is thrown, with{" "}
        <code>SelectTypeMustHaveParameterlessConstructor</code> as its{" "}
        <code>Message</code> and the type&apos;s name on{" "}
        <code>Subject</code> — see point&nbsp;17 for what that message used to be.
        Most EF Core entity classes have parameterless constructors by default.
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
        variant. The condition sets are combined into one query that the database
        orders and pages (point&nbsp;19). Under <code>ApplyPolicy</code>,{" "}
        <Link href="/docs/policies/configuration#caps"><code>DwCaps.MaxConditionSets</code></Link>{" "}
        (default 10) bounds how many sets one request may carry.
      </p>
      <Callout tone="danger" title="No synchronous overload">
        If you need to compose UNION / INTERSECT / EXCEPT across multiple condition
        sets you <em>must</em> use the async pipeline. See{" "}
        <Link href="/docs/classes/segment"><code>Segment</code></Link> and{" "}
        <Link href="/docs/extensions/to-list-async-segment"><code>ToListAsync&lt;T&gt;(Segment)</code></Link>.
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

      <h2 id="enum-string-storage">4. Enum Filtering Matches the Member Name, Whatever the Column Stores</h2>
      <p>
        <Link href="/docs/enums/data-type"><code>DataType.Enum</code></Link>{" "}
        compares the member <em>name</em> you send (<code>&quot;Pending&quot;</code>),
        and it works whether the column stores names or integers: the dynamic LINQ
        parser converts the name to the enum value before EF Core translates the
        comparison. <code>DataType.Enum</code> has no value-format check at all, so
        nothing is rejected at validation time.
      </p>
      <Callout tone="danger" title="Substring operators need a string member">
        <code>Contains</code>, <code>StartsWith</code> and <code>EndsWith</code>{" "}
        (and their <code>Not</code> forms) only bind on a member that really is a{" "}
        <code>string</code>. On an enum-typed member the parser has no such method
        to bind and throws <code>ParseException</code> from{" "}
        <code>System.Linq.Dynamic.Core</code> — as does a name that is not a member
        of the enum. For enums use <code>Equal</code>, <code>NotEqual</code>,{" "}
        <code>In</code>, <code>NotIn</code>, <code>IsNull</code> or{" "}
        <code>IsNotNull</code>.
      </Callout>

      <h2 id="having-aliases">5. Having Clause Fields Reference Aliases, Not Entity Properties</h2>
      <p>
        In a <Link href="/docs/classes/summary"><code>Summary</code></Link>,{" "}
        <code>Having</code> is itself a{" "}
        <Link href="/docs/classes/condition-group"><code>ConditionGroup</code></Link>,
        so the path is <code>Having.Conditions[].Field</code> — and each field in
        its nested <code>SubConditionGroups</code> as well. Every one of them must
        match an <code>AggregateBy.Alias</code>, not an entity property path.
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
        There is no negated <code>.Any()</code> either. The <code>Not</code>{" "}
        operators are emitted <em>inside</em> the <code>Any</code> lambda, so{" "}
        <code>NotEqual</code> on a path through a collection means &quot;some
        element does not match&quot;, not &quot;no element matches&quot;.
        Universal quantification has to be expressed outside the library — split
        it into two queries, or apply it in memory on the materialized result. See{" "}
        <Link href="/docs/examples/nested-collection">the nested‑collection
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
      <Callout tone="warn" title="Fixed in 3.1.0: an invented field name stayed in memory">
        Validating a field path recorded an access for eviction before the path
        was validated. A path that fails adds no cache entry for eviction to
        remove, so under <code>LRU</code> — the default — or <code>LFU</code>{" "}
        every distinct invalid name a caller sent kept its record for the life of
        the process, and a caller sending unique invented names grew the process
        without limit, fastest under a strict policy, which resolves every unknown
        name in a request. A path is now tracked only once it has validated.
      </Callout>

      <h2 id="get-query-string-ef-core">9. <code>getQueryString</code> Parameter Requires EF Core Provider</h2>
      <p>
        Passing <code>getQueryString: true</code> to <code>ToList</code> /{" "}
        <code>ToListAsync</code> calls <code>.ToQueryString()</code>, which needs
        an active EF Core database provider to produce SQL. On an in‑memory{" "}
        <code>IEnumerable&lt;T&gt;</code> it does not fail:{" "}
        <code>QueryString</code> holds a placeholder sentence where the SQL would
        be.
      </p>
      <Callout tone="warn" title="EF Core only">
        Only enable <code>getQueryString</code> when the source is a real{" "}
        <code>DbSet&lt;T&gt;</code> or an EF Core‑backed{" "}
        <code>IQueryable&lt;T&gt;</code>. On an in‑memory collection you get the
        placeholder sentence rather than SQL. Use it as a development aid, not as
        a production feature.
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
        <code>ToListAsync&lt;T&gt;(Filter)</code>, and{" "}
        <code>ToListAsync&lt;T&gt;(Segment)</code> since 3.1.0) and dynamic (
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

      <h2 id="values-are-literals">12. Condition Values Become Escaped Literals, Not Query Parameters</h2>
      <p>
        A condition&apos;s <code>Values</code> are written into the generated dynamic
        LINQ expression as string literals. Since <strong>2.1.4</strong> they are
        escaped first — a backslash is doubled and a double quote is backslash‑escaped
        — so any value matches literally, <code>\</code> and <code>&quot;</code>{" "}
        included, and a value can no longer break out of its literal to alter the
        predicate.
      </p>
      <p>
        The literal then reaches the provider as a constant, so EF Core inlines it
        into the SQL rather than binding a parameter — a{" "}
        <code>Contains</code> on <code>{`"الثانية\\"`}</code> renders as{" "}
        <code>{`instr(lower("p"."Name"), 'الثانية\\') > 0`}</code>. EF Core escapes
        that literal for SQL itself, so this is not a SQL injection path.
      </p>
      <Callout tone="warn" title="No plan-cache reuse across distinct values">
        Because values are inlined rather than parameterized, each distinct search
        term produces a distinct SQL statement. On SQL Server that means a separate
        plan‑cache entry per term. If a high‑cardinality free‑text filter is on a hot
        path, consider enabling forced parameterization at the database level.
      </Callout>
      <Callout tone="danger" title="Fixed in 3.1.0: a long In list ended the process">
        <code>In</code>, <code>NotIn</code>, <code>IIn</code> and{" "}
        <code>INotIn</code> on <code>Text</code>, and <code>In</code> and{" "}
        <code>NotIn</code> on <code>Guid</code>, <code>Number</code> and{" "}
        <code>Enum</code>, joined their values into one flat chain —{" "}
        <code>{`f == "a" || f == "b" || …`}</code> — which the expression parser
        reads as one level of nesting per value. EF Core and the expression
        compiler walk that tree recursively, so a single condition carrying about
        seven hundred values overflowed the request thread&apos;s stack, guarded or
        not, and a stack overflow ends the process: no <code>catch</code> can stop
        it. A list longer than 32 values is now nested as a balanced tree of flat
        chains of at most 32 terms, all joined by the same operator. A list of 32
        or fewer is written exactly as before, so its predicate and its SQL do not
        change, and a longer list returns the same rows. Under{" "}
        <code>ApplyPolicy</code>, <code>MaxConditionValues</code> also bounds the
        values of one condition (point&nbsp;24).
      </Callout>

      <h2 id="alias-identifier">13. <code>AggregateBy.Alias</code> Must Be a Plain Identifier</h2>
      <p>
        The alias is emitted verbatim into the generated <code>Select</code>{" "}
        projection, so since <strong>2.1.4</strong> it must be a leading letter or
        underscore followed by letters, digits, or underscores. Letters are matched by
        Unicode category, so a non‑Latin alias such as <code>&quot;المجموع&quot;</code>{" "}
        stays valid.
      </p>
      <Callout tone="warn" title="Tightened in 2.1.4">
        Earlier releases only rejected aliases containing a dot, which let an alias
        holding a comma — <code>&quot;Total, 1 as Leaked&quot;</code> — append terms of
        its own to the projection. Aliases carrying any other separator (a space, a
        dash) never parsed in the first place, so nothing that previously worked is
        rejected; a malformed alias now throws{" "}
        <code>AggregationMustHasValidAlias</code> at validation time instead of failing
        later. See <Link href="/docs/classes/aggregate-by"><code>AggregateBy</code></Link>.
      </Callout>

      <h2 id="date-member-type">14. Date Comparisons Resolve the Member&apos;s Type</h2>
      <p>
        Before <strong>3.1.0</strong> every{" "}
        <Link href="/docs/enums/data-type"><code>DataType.Date</code></Link> and{" "}
        <code>DataType.DateTime</code> condition produced the same string per
        operator, whatever the member actually was — for{" "}
        <code>GreaterThanOrEqual</code>,{" "}
        <code>{`{field} != null && {field} >= DateTime.Parse("…")`}</code>. The
        builder now reads the member&apos;s CLR type first and emits the null guard,
        the literal, and the <code>.Date</code> access that type can actually take.
      </p>
      <ul>
        <li>
          The null guard is emitted <strong>only for a member that can be null</strong>.
          A non-nullable member reached through a navigation —{" "}
          <code>Approval.ApprovedAt</code> — guards each navigation instead:{" "}
          <code>{`Approval != null && …`}</code>.
        </li>
        <li>
          A <code>DateTimeOffset</code> member is compared against a{" "}
          <code>DateTimeOffset</code> literal; a <code>DateTime</code> member
          against a <code>DateTime</code> literal; a <code>DateOnly</code> member
          against a <code>DateOnly</code>, as a day under both data types.
        </li>
        <li>
          A nullable member is unwrapped with <code>.Value</code> under its guard,
          so <code>DataType.Date</code> emits <code>{`{field}.Value.Date`}</code> —
          or <code>{`{field}.Value`}</code> on a <code>DateOnly?</code>, which is
          already a day.
        </li>
        <li>
          A <code>Having</code> condition names an aggregate alias rather than a
          member, so the type comes from what the alias stands for: a{" "}
          <code>Minimum</code>, <code>Maximum</code>, <code>FirstOrDefault</code> or{" "}
          <code>LastOrDefault</code> returns one of the values it read, so it has that
          member&apos;s type — nullable if the member is. On PostgreSQL a{" "}
          <code>Having</code> on <code>Maximum</code> of a <code>timestamptz</code>{" "}
          column becomes{" "}
          <code>{`HAVING max(col) > TIMESTAMPTZ '…'`}</code>.
        </li>
      </ul>
      <Callout tone="danger" title="Fixed: DateTimeOffset members were unusable">
        Until 3.1.0 <em>every</em> comparison on a <code>DateTimeOffset</code>{" "}
        member threw. On a non-nullable one the null guard compared a struct against{" "}
        <code>null</code> and failed with{" "}
        <code>
          InvalidOperationException: The binary operator NotEqual is not defined
          for the types &apos;System.DateTimeOffset&apos; and
          &apos;System.Object&apos;
        </code>
        . On a nullable one the literal was built as the wrong type — for{" "}
        <code>GreaterThanOrEqual</code>,{" "}
        <code>
          ParseException: Operator &apos;&gt;=&apos; incompatible with operand types
          &apos;DateTimeOffset?&apos; and &apos;DateTime&apos;
        </code>
        . And <code>DataType.Date</code> on <em>any</em> nullable date member threw,
        because a nullable has no <code>.Date</code> — on a <code>DateTime?</code>,{" "}
        <code>
          ParseException: No property or field &apos;Date&apos; exists in type
          &apos;DateTime?&apos;
        </code>
        . All of these now work.
      </Callout>
      <Callout tone="danger" title="Fixed: DateOnly members could not be compared">
        Until 3.1.0 no comparison on a <code>DateOnly</code> member worked. Only{" "}
        <code>IsNull</code> and <code>IsNotNull</code> did.{" "}
        <code>DataType.Date</code> asked it for a <code>.Date</code> it does not
        have —{" "}
        <code>
          ParseException: No property or field &apos;Date&apos; exists in type
          &apos;DateOnly&apos;
        </code>{" "}
        — and <code>DataType.DateTime</code> compared it against a{" "}
        <code>DateTime</code> literal —{" "}
        <code>
          ParseException: Operator &apos;==&apos; incompatible with operand types
          &apos;DateOnly&apos; and &apos;DateTime&apos;
        </code>
        . Both data types now compare a <code>DateOnly</code> as a day, and a
        nullable <code>DateOnly</code> is guarded like any other nullable date. On
        PostgreSQL an <code>Equal</code> becomes{" "}
        <code>{`WHERE "Day" = DATE '2026-09-01'`}</code>.
      </Callout>
      <Callout tone="warn" title="IsNull answers a constant on a non-nullable member of the entity itself">
        With the guard gone, there is nothing left for{" "}
        <Link href="/docs/enums/operator"><code>IsNull</code></Link> and{" "}
        <code>IsNotNull</code> to test on a non-nullable date member of the entity
        itself, so they answer with the constant the guard already implied:{" "}
        <code>IsNull</code> is <code>false</code> and <code>IsNotNull</code> is{" "}
        <code>true</code>. On PostgreSQL that reaches the database as{" "}
        <code>WHERE FALSE</code> and, for <code>IsNotNull</code>, as no predicate at
        all. On a non-nullable <code>DateTimeOffset</code>, where both used to throw
        like every other operator, they now answer. Reached through a navigation,
        as in <code>Approval.ApprovedAt</code>, they test the navigation instead:{" "}
        <code>IsNull</code> matches the rows with no approval, because a provider
        reads the member of a missing approval as NULL.
      </Callout>
      <Callout tone="note" title="Unchanged: the guard sits outside the comparison">
        On a nullable member the guard still wraps the <em>whole</em> comparison,
        so a null row fails every comparison — including the negative ones. A row
        whose date is unset does not match <code>NotEqual</code> and does not match{" "}
        <code>NotBetween</code>. Combine with <code>IsNull</code> under an{" "}
        <code>Or</code> if you want the unset rows back.
      </Callout>

      <h2 id="date-value-formats">15. Date Values Are ISO 8601, Year-First, or a Declared Format</h2>
      <p>
        Condition values for the two date types are now read against an explicit
        list of formats, never the lenient .NET parser, and re-emitted in
        round-trip form — at validation and in the builder alike, as the
        member&apos;s own date type. Every deployment accepts ISO&nbsp;8601
        extended calendar dates (
        <code>2026-09-01</code>, optionally with a time after a <code>T</code> or
        a space, a fraction, and <code>Z</code> or an offset) and year-first dates
        with <code>/</code> or <code>.</code> (<code>2026/09/01</code>,{" "}
        <code>2026.09.01</code>), plus any format the deployment declares. The
        other ISO&nbsp;8601 forms are <code>InvalidFormat</code>: basic (
        <code>20260901</code>), week (<code>2026-W36-2</code>), ordinal (
        <code>2026-244</code>) and reduced precision (<code>2026-09</code>,{" "}
        <code>2026-09-01T12</code>). The
        shipped predicate used to carry your raw text into a{" "}
        <code>DateTime.Parse</code> that the runtime evaluated in the host&apos;s
        culture, so the same filter meant different days on two servers; the
        host&apos;s culture and calendar now play no part.
      </p>
      <Callout tone="danger" title="Day-first and month-first values are now refused">
        A numeric date that leads with a day or a month —{" "}
        <code>&quot;01/09/2026&quot;</code>, <code>&quot;15/09/2026&quot;</code>,{" "}
        <code>&quot;09/15/2026&quot;</code>, <code>&quot;01.09.2026&quot;</code>,{" "}
        <code>&quot;1/9/26&quot;</code>, with or without a time — is refused with
        the new code{" "}
        <Link href="/docs/errors"><code>AmbiguousDateFormat</code></Link>, whatever
        its numbers, with the field on <code>LogicException.Subject</code>. A server
        whose culture used to read such values refuses them now, unless it
        declares the form. The refusal goes by shape on purpose: refusing only values with
        two valid readings would fail on the 5th of the month and pass on the 15th,
        so a client would find out in production instead of on its first request.
        Send ISO&nbsp;8601 — <code>&quot;2026-09-15&quot;</code>,{" "}
        <code>&quot;2026-09-15T12:00:00Z&quot;</code> — or declare the form your
        clients send once at startup, with{" "}
        <code>{`DwDates.Configure(o => o.Formats.Add("dd/MM/yyyy"))`}</code>; see{" "}
        <Link href="/docs/enums/data-type#date-formats">DataType → Date formats</Link>.
      </Callout>
      <Callout tone="warn" title="Values the lenient parser guessed at are now InvalidFormat">
        Anything that is neither an accepted form nor a day-first or month-first
        date is refused with{" "}
        <Link href="/docs/errors"><code>InvalidFormat</code></Link>. That includes
        values the lenient parser used to accept without a word:{" "}
        <code>&quot;12:00&quot;</code> was today at noon,{" "}
        <code>&quot;1/9&quot;</code> a day of the current year, and{" "}
        <code>&quot;Sep 2026&quot;</code> and{" "}
        <code>&quot;1 September 2026&quot;</code> were 1 September.
      </Callout>
      <Callout tone="warn" title="Zones: DateTimeOffset normalizes to UTC, DateTime does not">
        On a <code>DateTimeOffset</code> member the value is normalized to UTC, and
        a value carrying no zone is <em>read</em> as UTC — which is what keeps{" "}
        <code>DataType.Date</code> comparing the calendar day you wrote rather than
        the day it happens to be on the server. On PostgreSQL{" "}
        <code>DataType.Date</code> translates to{" "}
        <code>{`date_trunc('day', col AT TIME ZONE 'UTC')`}</code>.{" "}
        <code>DateTime</code> members keep the previous behaviour: a value carrying
        a zone is converted to the host&apos;s local time, which is the reading a{" "}
        <code>timestamp without time zone</code> column is compared against.
      </Callout>
      <Callout tone="warn" title="C# date objects in Values are written year-first">
        A <code>DateTime</code>, <code>DateTimeOffset</code> or{" "}
        <code>DateOnly</code> placed in <code>Condition.Values</code> from C# is
        now written as year-first text —{" "}
        <code>&quot;2026-09-01T12:30:00&quot;</code>,{" "}
        <code>&quot;2026-09-01T12:30:00+03:00&quot;</code>,{" "}
        <code>&quot;2026-09-01&quot;</code> — instead of the month-first invariant
        form <code>&quot;09/01/2026 12:30:00&quot;</code>, so a C# caller is never
        refused for sending an unambiguous value. It also ends a silent misreading:
        that month-first text used to be parsed back in the host&apos;s culture, so
        on a day-first server <code>new DateTime(2026, 9, 1)</code> filtered on
        9 January.
      </Callout>
      <Callout tone="warn" title="A local DateTime keeps its offset on a DateTimeOffset member">
        A C# <code>DateTime</code> whose <code>Kind</code> is <code>Local</code>{" "}
        — <code>DateTime.Now</code>, or the value Newtonsoft.Json produces from a
        string carrying an offset — placed in <code>Values</code> with{" "}
        <code>DataType.DateTime</code> against a <code>DateTimeOffset</code> or{" "}
        <code>DateTimeOffset?</code> member, or against a <code>Having</code> alias
        over such a member&apos;s aggregate, is written with its offset:{" "}
        <code>&quot;2026-09-17T15:00:00+03:00&quot;</code>. It filters on the
        moment it holds. Written with no zone it would be read as UTC, which on a
        host at UTC+3 names a moment three hours later, with no error. Everything
        else keeps no zone, on purpose. Under <code>DataType.Date</code> a local{" "}
        <code>DateTime</code> is written without one, so{" "}
        <code>DateTime.Today</code> compares the day it was written for — on a host
        ahead of UTC, local midnight on the 17th is still the 16th in UTC. A{" "}
        <code>DateTime</code> or <code>DateOnly</code> member gets none. A{" "}
        <code>DateTime</code> of <code>Kind</code> <code>Utc</code> or{" "}
        <code>Unspecified</code> gets none, and on a <code>DateTimeOffset</code>{" "}
        member it is read as UTC. Text values — every JSON string System.Text.Json
        binds — are never touched.
      </Callout>

      <h2 id="unpaged-page-count">16. <code>PageCount</code> on an Unpaged Result Is <code>1</code></h2>
      <p>
        When a <code>Filter</code> or <code>Summary</code> carries no{" "}
        <code>Page</code>, <code>PageCount</code> is now <code>1</code> — the one
        page the whole result occupies — and <code>0</code> when nothing matched.
        It used to equal <code>TotalCount</code>: the calculation divided by a page
        size of <code>1</code> whenever none was sent, so a 5,000-row result
        reported 5,000 pages of one row each.
      </p>
      <Callout tone="danger" title="Check anything that renders a pager">
        A client that draws page links straight from <code>PageCount</code> drew one
        link per row on every unpaged endpoint and now draws a single link. Applies
        to <Link href="/docs/classes/filter-result"><code>FilterResult&lt;T&gt;</code></Link>{" "}
        — typed and dynamic, sync and async — to{" "}
        <Link href="/docs/classes/summary-result"><code>SummaryResult</code></Link>,
        and to{" "}
        <Link href="/docs/classes/segment-result"><code>SegmentResult&lt;T&gt;</code></Link>,
        which used to report <code>0</code> for an unpaged request with condition
        sets.{" "}
        <code>PageNumber</code> and{" "}
        <code>PageSize</code> are unchanged — both still report <code>0</code> when
        no page was sent. The exception is a guarded query when the deployment
        sets{" "}
        <Link href="/docs/policies/configuration#caps"><code>DwCaps.DefaultPageSize</code></Link>:
        the query is given page <code>1</code> at that size, and reports it.
      </Callout>

      <h2 id="select-code">17. <code>Select</code>&apos;s Constructor Refusal Is Now a Stable Code</h2>
      <p>
        The refusal in point&nbsp;1 used to arrive as an English sentence —{" "}
        <code>{`Select projection requires a parameterless constructor on type '{T}'.`}</code>{" "}
        — which a caller could not match on, because the type name was
        interpolated into it. The <code>Message</code> is now the fixed
        code <code>SelectTypeMustHaveParameterlessConstructor</code>, and the
        type&apos;s name rides on a new property,{" "}
        <code>LogicException.Subject</code> (<code>string?</code>).{" "}
        <code>LogicException</code> gained a second constructor for it,{" "}
        <code>LogicException(string message, string? subject)</code>.
      </p>
      <Callout tone="danger" title="Two things to update">
        Middleware that string-matched the old sentence stops matching, and anything
        that scraped the type name out of the message must read{" "}
        <code>Subject</code> instead. The count of stable codes went from 27 to 28
        (30 with point&nbsp;15&apos;s <code>AmbiguousDateFormat</code> and
        point&nbsp;20&apos;s{" "}
        <code>{`FieldPath[{path}]StartsWithReservedName`}</code>), leaving one
        validation failure whose message is a sentence rather than a code —{" "}
        <code>{`Unsupported combination of DataType '{type}' and Operator '{op}'.`}</code>{" "}
        See <Link href="/docs/errors">the error code reference</Link>.
      </Callout>
      <Callout tone="note" title="Guarded queries reach it too">
        A member carrying <code>[DwNoSelect]</code> makes the policy layer
        synthesize a projection for a query that sent none — since 3.2.0
        whatever the member holds, and beneath another member when its value
        can reach the result (point&nbsp;25) — so a typed guarded query on a
        type with no parameterless constructor raises
        the same code, even though the caller never asked for a{" "}
        <code>Select</code>. The dynamic terminals project through{" "}
        <code>SelectDynamic</code> and are not affected.
      </Callout>

      <h2 id="policy-prepared">18. A Guarded Query Requires a Prepared Context</h2>
      <p>
        A query guarded through{" "}
        <Link href="/docs/policies"><code>ApplyPolicy</code></Link> whose{" "}
        <code>DwPolicyContext</code> never went through{" "}
        <code>DwPolicy.PrepareAsync</code> is refused with a{" "}
        <code>PolicyException</code> carrying{" "}
        <code>PolicyContextNotPrepared</code> — whether or not a policy store is
        configured, and at the <code>ApplyPolicy</code> call itself, before any
        terminal runs. A store provider already refused one, because it had no pinned
        snapshot to answer from; with attributes alone nothing refused it, so the
        same missing call was a failure in one deployment and silence in another.{" "}
        <code>DwPolicyContext.IsPrepared</code> is public, so you can assert it
        yourself.
      </p>
      <Callout tone="warn" title="The explicit-options overload does not check">
        The <code>ApplyPolicy</code> overload that takes explicit options and a
        resolver is exempt: a host composing its own options owns preparation. The
        check applies to the overloads that read the ambient{" "}
        <code>DwPolicy</code> configuration, because that is where{" "}
        <code>PrepareAsync</code> is the documented ceremony. A store handed to the
        explicit overload still refuses an unprepared context on its own.
      </Callout>

      <h2 id="segment-in-database">19. A Segment&apos;s Sets Are Combined in the Database</h2>
      <p>
        <Link href="/docs/extensions/to-list-async-segment"><code>ToListAsync(Segment)</code></Link>{" "}
        turns its condition sets into one query. <code>Union</code> and{" "}
        <code>Intersect</code> join the sets&apos; conditions with{" "}
        <code>OR</code> and <code>AND</code>; <code>Except</code> removes its
        set&apos;s rows with <code>NOT EXISTS</code> on the primary key; and a type
        with no primary key uses SQL <code>UNION</code> /{" "}
        <code>INTERSECT</code> / <code>EXCEPT</code>. The combined query is then
        ordered, paged, projected and counted exactly like a{" "}
        <code>Filter</code>.
      </p>
      <p>
        Until 3.1.0 each set was loaded into a list, and the lists were combined
        in memory by object reference. That was right only for a tracking query
        with no <code>Selects</code>. With <code>AsNoTracking()</code>, with{" "}
        <code>Selects</code>, and under <code>ApplyPolicy</code>, which always runs
        untracked, <code>Intersect</code> returned nothing, <code>Except</code>{" "}
        removed nothing and <code>Union</code> counted a row once for every set
        that matched it. Every row of every set was read before the page was cut.
      </p>
      <ul>
        <li>
          <strong>Results.</strong> Untracked, projected and guarded segments
          return the rows their sets describe. A tracking query without{" "}
          <code>Selects</code> returns the same rows it did.
        </li>
        <li>
          <strong>Ordering.</strong> Sorting runs in the database, so text follows
          its collation rather than .NET&apos;s string comparison, and NULLs fall
          where the provider puts them. <code>Orders</code> apply before{" "}
          <code>Selects</code>, so an order field no longer has to be selected. A
          segment with no <code>Orders</code> comes back in whatever order the
          database chooses, as a filter does.
        </li>
        <li>
          <strong>Reads.</strong> Only the requested page is read, plus one{" "}
          <code>COUNT</code> for <code>TotalCount</code>.
        </li>
        <li>
          <strong>Providers.</strong> On a type with a primary key, only{" "}
          <code>Except</code> needs the provider to translate a correlated{" "}
          <code>EXISTS</code>. A type with no primary key needs every column to be
          comparable — not PostgreSQL <code>json</code> or SQL Server{" "}
          <code>xml</code> — and support for the SQL set operators its sets use.
        </li>
      </ul>

      <h2 id="root-it-parent-members">20. Members Named <code>Root</code>, <code>It</code> or <code>Parent</code> Are Read as Members</h2>
      <p>
        <code>System.Linq.Dynamic.Core</code> reads <code>it</code>,{" "}
        <code>root</code> and <code>parent</code> as keywords, in any letter case,
        wherever an identifier can stand, and the library writes member paths into
        its expressions as they are named. Before <strong>3.1.0</strong> it parsed
        with those keywords on:
      </p>
      <ul>
        <li>
          A navigation named <code>Root</code> or <code>It</code> was read as the
          row itself. <code>Root.Name</code> filtered, sorted, grouped and
          aggregated — and through <code>SelectDynamic</code> projected — the
          row&apos;s own <code>Name</code>.
        </li>
        <li>
          A navigation named <code>Parent</code> threw <code>ParseException</code>.
        </li>
        <li>
          An <code>AggregateBy.Alias</code> named <code>root</code>,{" "}
          <code>it</code> or <code>parent</code> failed in <code>Having</code> and
          in <code>Summary.Orders</code>.
        </li>
      </ul>
      <p>
        Every expression is now parsed with a <code>ParsingConfig</code> the
        library owns: the parser&apos;s defaults with{" "}
        <code>AreContextKeywordsEnabled = false</code>, so <code>it</code>,{" "}
        <code>root</code> and <code>parent</code> name members like any other
        identifier. No setting restores the keyword reading.
      </p>
      <Callout tone="danger" title="Fixed: a policy decided on one column while the query read another">
        Under <code>ApplyPolicy</code> the gate decided on the path the caller
        named while the database read the row&apos;s own column. A dynamic
        projection of <code>Root.Name</code> returned the values of a{" "}
        <code>[DwDenied]</code> <code>Name</code>, a filter on{" "}
        <code>Root.Name</code> tested the denied column, and a{" "}
        <code>[DwForceWhere]</code> scope reached through a navigation named{" "}
        <code>Root</code> filtered the row&apos;s own column instead of the linked
        record&apos;s.
      </Callout>
      <Callout tone="warn" title="ParsingConfig.Default is no longer read">
        The library used to parse through the shared{" "}
        <code>ParsingConfig.Default</code>. It no longer reads that instance, so a
        change a host makes to it does not reach DynamicWhere queries, and the
        library&apos;s own configuration does not reach the host&apos;s dynamic
        LINQ. No setting carries a host&apos;s changes to{" "}
        <code>ParsingConfig.Default</code> into the library&apos;s parsing.
      </Callout>
      <Callout tone="danger" title="A path that starts with one of the parser's own words is now refused by name">
        The parser reads its functions and literals before it looks for a member,
        and it still does: <code>new</code>, <code>iif</code>, <code>np</code>,{" "}
        <code>isnull</code>, <code>is</code>, <code>as</code>, <code>cast</code>,{" "}
        <code>true</code>, <code>false</code> and <code>null</code>, in any letter
        case, shadow the <em>first</em> segment of a field path. So{" "}
        <strong>3.1.0</strong> refuses such a path itself, with a{" "}
        <code>LogicException</code> whose message is{" "}
        <code>{`FieldPath[{path}]StartsWithReservedName`}</code> and whose{" "}
        <Link href="/docs/errors#subject"><code>Subject</code></Link> carries that
        first segment, trimmed. It is raised where a path is validated, so a
        condition <code>Field</code>, an <code>Orders</code> entry, a{" "}
        <code>Selects</code> entry, a <code>GroupBy.Fields</code> entry, an{" "}
        <code>AggregateBy.Field</code> and the member a <code>[DwAlias]</code>{" "}
        stands for all answer alike — guarded or not. A <code>DefaultOrder</code>{" "}
        entry naming one is skipped instead, as an unreadable entry is, because a
        default order never refuses a query; the startup scan reports it. A member that cannot be reached cannot be filtered, sorted, grouped,
        aggregated or projected: rename the CLR property and keep the column with{" "}
        <code>[Column(&quot;New&quot;)]</code>.
      </Callout>
      <Callout tone="danger" title="What a member with one of those names did before 3.1.0">
        Nothing announced itself. <code>New</code>, <code>Iif</code>,{" "}
        <code>Np</code>, <code>IsNull</code>, <code>Is</code>, <code>As</code> and{" "}
        <code>Cast</code> raised the parser&apos;s <code>ParseException</code>,{" "}
        <code>True</code> and <code>False</code> an{" "}
        <code>InvalidOperationException</code>, and <code>Null</code> was read as
        the null literal — so the predicate compared null with the caller&apos;s
        value and the query returned <strong>no rows and no error</strong>. A typed{" "}
        <code>Selects</code> entry naming such a member used to work, because a
        typed projection is built without the parser; it is refused now too, so one
        rule covers every clause. Under <code>ApplyPolicy</code> the{" "}
        <code>Convenience</code> tier and any dry run give the new code, while the{" "}
        <code>Strict</code> tier outside a dry run answers with the clause&apos;s{" "}
        <code>FieldDeniedFor*</code> code and <code>FieldPath</code>{" "}
        <code>&quot;*&quot;</code>, as it answers for every name it cannot use. The{" "}
        <Link href="/docs/policies/configuration#validate">startup scan</Link>{" "}
        reports a <code>DefaultOrder</code> entry naming one as an error.
      </Callout>
      <Callout tone="note" title="Names that only look reserved">
        Only the first segment is the parser&apos;s: <code>Owner.New</code> names
        the member, because the parser looks for a member after a dot. An alias
        named after one of these words still works — only the path it stands for is
        checked. And <code>it</code>, <code>root</code>, <code>parent</code> and{" "}
        <code>outerIt</code>, whose keywords this point turns off, are members like
        any other identifier, as is every predefined type name the parser
        knows: <code>String</code>, <code>Boolean</code>, <code>Char</code>,{" "}
        <code>Byte</code>, <code>SByte</code>, <code>Int16</code>,{" "}
        <code>Int32</code>, <code>Int64</code>, <code>UInt16</code>,{" "}
        <code>UInt32</code>, <code>UInt64</code>, <code>Single</code>,{" "}
        <code>Double</code>, <code>Decimal</code>, <code>DateTime</code>,{" "}
        <code>DateTimeOffset</code>, <code>TimeSpan</code>, <code>Guid</code>,{" "}
        <code>Math</code>, <code>Convert</code>, <code>Uri</code>,{" "}
        <code>Object</code> and <code>Enum</code>.
      </Callout>

      <h2 id="condition-depth-and-set-caps">21. <code>MaxConditionDepth</code> and <code>MaxConditionSets</code> Refuse Guarded Requests 3.0 Ran</h2>
      <p>
        Two caps are new in <strong>3.1.0</strong>. Only a query guarded through{" "}
        <Link href="/docs/policies/configuration#caps"><code>ApplyPolicy</code></Link>{" "}
        enforces them; an unguarded call is not affected. Both default to{" "}
        <code>10</code>, refuse a value below <code>1</code>, freeze with the rest
        of the posture, and bind from configuration as{" "}
        <code>Caps:MaxConditionDepth</code> and <code>Caps:MaxConditionSets</code>.
      </p>
      <table>
        <thead>
          <tr><th>Cap</th><th>What it counts</th><th>Refusal</th></tr>
        </thead>
        <tbody>
          <tr>
            <td><code>DwCaps.MaxConditionDepth</code></td>
            <td>
              How deeply condition groups nest. The top group counts as{" "}
              <code>1</code> and each level of <code>SubConditionGroups</code> adds{" "}
              <code>1</code>, counted on the caller&apos;s groups before any forced
              predicate is injected — on a <code>Filter</code>&apos;s group, on the
              deeper of a <code>Summary</code>&apos;s conditions and its{" "}
              <code>Having</code>, and on each <code>Segment</code> set separately.
            </td>
            <td>
              <code>PolicyException</code> with <code>CapExceeded</code> and{" "}
              <code>SourceOrigin</code>{" "}
              <code>&quot;MaxConditionDepth cap (10), request had 11&quot;</code>
            </td>
          </tr>
          <tr>
            <td><code>DwCaps.MaxConditionSets</code></td>
            <td>
              How many condition sets one <code>Segment</code> sends, empty sets
              included.
            </td>
            <td>
              <code>PolicyException</code> with <code>CapExceeded</code> and{" "}
              <code>SourceOrigin</code>{" "}
              <code>&quot;MaxConditionSets cap (10), request had 11&quot;</code>
            </td>
          </tr>
        </tbody>
      </table>
      <p>
        Nothing bounded either shape before. <code>MaxConditions</code> counts
        conditions and says nothing about how deeply their groups nest, and a set
        with no conditions passes every other cap while still adding to the one
        statement a segment becomes.
      </p>
      <Callout tone="danger" title="A request 3.0 ran can be refused">
        A guarded filter nested eleven groups deep, or a guarded segment carrying
        eleven or more condition sets, ran on 3.0.0 and is refused on 3.1.0. A
        deployment whose clients send such requests raises the cap, in code or
        from configuration:
        <Code lang="csharp">{`DwPolicy.Configure(new DwPolicyOptions
{
    Caps = { MaxConditionDepth = 20, MaxConditionSets = 25 },
}, providers);`}</Code>
        <Code lang="json">{`{
  "DynamicWhere": {
    "Policies": {
      "Caps": { "MaxConditionDepth": 20, "MaxConditionSets": 25 }
    }
  }
}`}</Code>
      </Callout>

      <h2 id="strict-trace-off-result">22. The Strict Tier Keeps the Policy Trace Off the Result</h2>
      <p>
        On 3.0.0 every guarded terminal put its <code>PolicyTrace</code> on the
        result, in both tiers:{" "}
        <Link href="/docs/classes/filter-result"><code>FilterResult&lt;T&gt;.Policy</code></Link>{" "}
        from <code>ToList</code>, <code>ToListAsync</code>,{" "}
        <code>ToListDynamic</code> and <code>ToListAsyncDynamic</code> with a{" "}
        <code>Filter</code>,{" "}
        <Link href="/docs/classes/summary-result"><code>SummaryResult.Policy</code></Link>{" "}
        from <code>ToList</code> and <code>ToListAsync</code> with a{" "}
        <code>Summary</code>, and{" "}
        <Link href="/docs/classes/segment-result"><code>SegmentResult&lt;T&gt;.Policy</code></Link>{" "}
        from <code>ToListAsync</code> with a <code>Segment</code>. The trace names
        every field a policy dropped, the attribute or rule that sealed each one,
        and every predicate injected on the caller&apos;s behalf — the detail the
        strict tier already refuses to hand over through{" "}
        <code>getQueryString</code> — and an API that serializes a result sends it
        to the caller.
      </p>
      <p>
        <Link href="/docs/policies/configuration#trace"><code>DwPolicyOptions.IncludeTraceInResult</code></Link>{" "}
        (<code>bool?</code>, default <code>null</code>) now decides.{" "}
        <code>null</code> follows the tier: off under <code>DwTier.Strict</code>, on
        under <code>DwTier.Convenience</code>. <code>true</code> or{" "}
        <code>false</code> overrides the tier in either one. It freezes with the
        posture and binds from the configuration key{" "}
        <code>IncludeTraceInResult</code>.
      </p>
      <Callout tone="danger" title="Under the strict tier result.Policy is null">
        Code that reads <code>Policy</code> from a strict-tier guarded result now
        reads <code>null</code>. The trace is still recorded, on the{" "}
        <code>PolicyQueryable&lt;T&gt;.LastTrace</code> of the handle that ran the
        query, and audit events are written exactly as before. Set{" "}
        <code>IncludeTraceInResult = true</code> to put the trace back on the result.
        <Code lang="csharp">{`var guarded = db.Employees.ApplyPolicy(caller);
var result  = await guarded.ToListAsync(filter);

PolicyTrace? sent     = result.Policy;      // null under DwTier.Strict, unless IncludeTraceInResult = true
PolicyTrace? recorded = guarded.LastTrace;  // recorded whatever the setting says`}</Code>
      </Callout>

      <h2 id="strict-unknown-field">23. The Strict Tier Answers an Unknown Field and a Denied Field Alike</h2>
      <p>
        On 3.0.0 a guarded query told a field that does not exist from one the
        caller may not use. A name that matched nothing on <code>T</code> failed
        validation with <code>LogicException</code>{" "}
        <code>ConditionMustHasValidFieldName</code> before any policy decision was
        made, and a denied field was refused with a <code>PolicyException</code>{" "}
        naming the field — and, where one source decided, that rule or attribute on{" "}
        <code>RuleId</code> and <code>SourceOrigin</code>. A caller probing the
        strict tier learned which columns exist, including the ones they may never
        read, one guess at a time, and each refusal confirmed the guess.
      </p>
      <p>
        Under <code>DwTier.Strict</code>, outside a dry run, the two now answer
        alike:
      </p>
      <ul>
        <li>
          A name that matches nothing is gated as a field denied for every feature,
          at the step where a denial is raised — after the caps — so it gets the
          code a <code>[DwDenied]</code> field gets in that clause:{" "}
          <code>FieldDeniedForWhere</code>, <code>FieldDeniedForSelect</code>,{" "}
          <code>FieldDeniedForOrder</code>, <code>FieldDeniedForGroup</code> or{" "}
          <code>FieldDeniedForAggregate</code>. A name padded with dots or blank
          segments — <code>NoSuchColumn....</code>,{" "}
          <code>. . . . X</code> — is normalized the way a real path is, so it
          gets the refusal a padded real field gets rather than failing{" "}
          <code>MaxNavigationDepth</code>.
        </li>
        <li>
          Inside a segment every field refusal is{" "}
          <code>FieldDeniedForSegment</code>, with <code>Feature</code>{" "}
          <code>Segment</code>, whichever clause refused it: a condition in any set,
          an order, a select, or the field taking part at all. Answered by clause, a
          field denied for every clause but not for segments would say{" "}
          <code>FieldDeniedForOrder</code> where a name that matches nothing says{" "}
          <code>FieldDeniedForSegment</code>. Filters and summaries keep their
          per-clause codes.
        </li>
        <li>
          Every refusal with one of those six codes carries{" "}
          <code>FieldPath = &quot;*&quot;</code>, <code>RuleId = null</code> and{" "}
          <code>SourceOrigin = null</code>, whatever the field — a real denied field
          and an alias included — so the message is the same too:{" "}
          <code>{`FieldDeniedForWhere: field '*', feature 'Where', tier 'Strict'.`}</code>
        </li>
        <li>
          A <code>CapExceeded</code> refusal names no path either.{" "}
          <code>MaxNavigationDepth</code> and <code>MaxAuditEvents</code>, the two
          caps that named a field, used to report its canonical path, which
          confirmed that the path exists. They report <code>&quot;*&quot;</code>,
          and <code>SourceOrigin</code> still names the cap.
        </li>
        <li>
          <code>MaxQueryCost</code> is checked after every field has passed its
          gate, not before. A field weighted by <code>[DwCost]</code> that the
          caller may not use is refused as denied before its weight can count,
          exactly as a name that does not exist is, so the budget cannot tell the
          two apart. An allowed weighted field is still refused with{" "}
          <code>QueryCostExceeded</code>.
        </li>
        <li>
          <code>MissingContextValue</code> has <code>FieldPath</code>{" "}
          <code>&quot;*&quot;</code> and a <code>null</code>{" "}
          <code>SourceOrigin</code>, so it names neither the scope&apos;s column nor
          the context key it reads — together they describe how rows are
          partitioned.
        </li>
        <li>
          The trace keeps the real path and reason: an unknown name is recorded as{" "}
          <code>Denied</code>, with the reason <code>names nothing on</code>{" "}
          followed by the type&apos;s name. With{" "}
          <Link href="/docs/policies/configuration#audit-refusals"><code>AuditRefusals</code></Link>{" "}
          on, the audit event names the field, the scoped field of a{" "}
          <code>MissingContextValue</code>, or the unknown name the caller sent.
        </li>
      </ul>
      <Callout tone="danger" title="Check anything that reads FieldPath or matches the validation code">
        Under the strict tier an error response built from{" "}
        <code>PolicyException.FieldPath</code> now says <code>*</code>, and a
        handler that matched <code>ConditionMustHasValidFieldName</code> for a
        misspelt field receives a <code>PolicyException</code> instead. It still
        derives from <code>LogicException</code>, so an existing{" "}
        <code>catch</code> still catches it. Inside a segment, a handler that
        matched a clause&apos;s code receives <code>FieldDeniedForSegment</code>,
        and a <code>MissingContextValue</code> carries neither the column nor its
        key. No setting restores the old answer under the strict tier. The
        convenience tier is unchanged — an unknown field fails validation, a
        refusal names the field with its <code>RuleId</code> and{" "}
        <code>SourceOrigin</code>, and the cost budget is checked before gating —
        and a dry run refuses nothing, so an unknown name fails validation there
        too.
      </Callout>

      <h2 id="values-and-aggregates-caps">24. <code>MaxConditionValues</code> and <code>MaxAggregates</code> Refuse Guarded Requests 3.0 Ran</h2>
      <p>
        Two more caps are new in <strong>3.1.0</strong>. As with point&nbsp;21,
        only a query guarded through{" "}
        <Link href="/docs/policies/configuration#caps"><code>ApplyPolicy</code></Link>{" "}
        enforces them; an unguarded call is not affected.{" "}
        <code>MaxConditionValues</code> defaults to <code>1000</code> and{" "}
        <code>MaxAggregates</code> to <code>50</code>. Both refuse a value below{" "}
        <code>1</code>, freeze with the rest of the posture, and bind from
        configuration as <code>Caps:MaxConditionValues</code> and{" "}
        <code>Caps:MaxAggregates</code>.
      </p>
      <table>
        <thead>
          <tr><th>Cap</th><th>What it counts</th><th>Refusal</th></tr>
        </thead>
        <tbody>
          <tr>
            <td><code>DwCaps.MaxConditionValues</code></td>
            <td>
              The values one condition carries. The condition carrying the most is
              the one compared, wherever it sits: a <code>Filter</code>&apos;s
              conditions, a <code>Summary</code>&apos;s conditions and its{" "}
              <code>Having</code>, and every set of a <code>Segment</code>.
            </td>
            <td>
              <code>PolicyException</code> with <code>CapExceeded</code>,{" "}
              <code>FieldPath</code> <code>&quot;*&quot;</code> and{" "}
              <code>SourceOrigin</code>{" "}
              <code>&quot;MaxConditionValues cap (1000), request had 1001&quot;</code>
            </td>
          </tr>
          <tr>
            <td><code>DwCaps.MaxAggregates</code></td>
            <td>
              The <code>AggregateBy</code> entries one summary sends, through the{" "}
              <code>Summary</code> terminals and the composable <code>Group</code>{" "}
              and <code>Summary</code>. The count the group-size floor adds for
              itself is not the caller&apos;s and is not counted.
            </td>
            <td>
              <code>PolicyException</code> with <code>CapExceeded</code>,{" "}
              <code>FieldPath</code> <code>&quot;*&quot;</code> and{" "}
              <code>SourceOrigin</code>{" "}
              <code>&quot;MaxAggregates cap (50), request had 51&quot;</code>
            </td>
          </tr>
        </tbody>
      </table>
      <p>
        Nothing bounded either shape before. An <code>In</code> or a{" "}
        <code>NotIn</code> is one comparison per value, so a single condition
        could hand the database a predicate of any size while spending one
        condition from <code>MaxConditions</code> and one field from the cost
        budget. Every aggregate is a column of every group, and one with no field
        — a <code>Count</code> — named nothing a <code>[DwCost]</code> weight
        could be set on, so any number of them cost nothing.
      </p>
      <p>
        That <code>Count</code> is now charged as well: an aggregate with no{" "}
        <code>Field</code> costs <code>DwCaps.DefaultFieldCost</code> toward{" "}
        <code>MaxQueryCost</code>, where it used to cost nothing. A guarded summary
        that sat just under its budget can now go over it and be refused with{" "}
        <code>QueryCostExceeded</code>.
      </p>
      <p>
        Every count cap — <code>MaxConditions</code>,{" "}
        <code>MaxConditionDepth</code>, <code>MaxConditionSets</code>,{" "}
        <code>MaxConditionValues</code>, <code>MaxAggregates</code>,{" "}
        <code>MaxOrderFields</code> and <code>MaxPageSize</code> — is now checked
        before any field name is resolved, because resolving every name of an
        oversized request is the work the caps exist to refuse;{" "}
        <code>MaxNavigationDepth</code> still runs once names are resolved. A
        request that is too large <em>and</em> names a field that does not exist
        is refused with <code>CapExceeded</code> in both tiers, where 3.0.0
        resolved names first and answered{" "}
        <code>ConditionMustHasValidFieldName</code>.
      </p>
      <Callout tone="danger" title="A request 3.0 ran can be refused">
        A guarded summary computing more than fifty aggregates, or one whose{" "}
        <code>Count</code> aggregates now take it over <code>MaxQueryCost</code>,
        ran on 3.0.0 and is refused on 3.1.0. So is a guarded condition carrying
        more than a thousand values — which on 3.0.0 could end the process instead
        (point&nbsp;12). A deployment whose clients send such requests raises the
        cap, in code or from configuration:
        <Code lang="csharp">{`DwPolicy.Configure(new DwPolicyOptions
{
    Caps = { MaxConditionValues = 5000, MaxAggregates = 100, MaxQueryCost = 2000 },
}, providers);`}</Code>
        <Code lang="json">{`{
  "DynamicWhere": {
    "Policies": {
      "Caps": { "MaxConditionValues": 5000, "MaxAggregates": 100, "MaxQueryCost": 2000 }
    }
  }
}`}</Code>
      </Callout>

      <h2 id="synthesized-projection">25. A Guarded Query That Sends No <code>Selects</code> Keeps What the Source Carries</h2>
      <p>
        A request with no <code>Selects</code> returns whole rows, denied fields
        included, so a guarded query synthesizes a projection when a denied
        field could reach the result. <strong>3.2.0</strong> changed when it
        does so and what the projection keeps. The rules are on{" "}
        <Link href="/docs/policies/configuration#no-selects">Policy configuration</Link>.
      </p>
      <ul>
        <li>
          <strong>When.</strong> A field denied at the top of <code>T</code>{" "}
          asks for it whatever the field holds. A field denied beneath a member
          asks for it when its value can reach the result: on an entity, beneath
          a column, an owned or complex member, or a navigation the query loads
          through an <code>Include</code>, an automatic include or a lazy
          loader; on a projected row, beneath a member the initializer assigns;
          in memory, beneath any member. A chain that reaches its rows through
          a navigation, a <code>SelectMany</code>, a <code>Join</code> or a
          projection behind another <code>Select</code> counts every navigation
          as loaded. A field a subtype of <code>T</code> declares, and one a
          subtype of a member&apos;s type declares, count too. It does so in both
          tiers, typed and dynamic, for a <code>Filter</code> and a{" "}
          <code>Segment</code>. Until 3.2.0 only a simple field denied at the top
          of <code>T</code> asked for one. A denial beneath a navigation nothing
          loads never leaves the database, and a member EF Core does not map
          holds nothing it read, so an entity whose only denials sit there is
          read exactly as in 3.1.0.
        </li>
        <li>
          <strong>What.</strong> The allowed members, which replace the allowed
          scalars. A row a projection builds keeps the members its initializer
          assigns. An entity keeps its mapped columns, converted and JSON ones
          included, its owned and complex members, and every collection of
          simple values such as <code>byte[]</code> or{" "}
          <code>List&lt;string&gt;</code>. Rows in memory keep their values. A
          member holding an object is kept whole when nothing it can hold is
          denied, narrowed to the allowed fields where the core&apos;s narrowing
          translates, and otherwise left out whole.
        </li>
      </ul>
      <Callout tone="danger" title="Fixed (security): a denial beneath a member was not enforced">
        With every denied field beneath a member and none at the top of{" "}
        <code>T</code>, nothing was synthesized, and the whole row came back
        with the denied value in it: in a list or nested object of a row
        projected before <code>ApplyPolicy</code>, in a row held in memory, and
        in an entity&apos;s included, automatically included, lazily loaded or
        owned member — typed and dynamic, in both tiers, for a{" "}
        <code>Filter</code> and a <code>Segment</code>.
      </Callout>
      <Callout tone="danger" title="Fixed (security): what a query loads was read too narrowly">
        An include named from the root and reached through{" "}
        <code>{`Select(o => o.Customer)`}</code>, <code>SelectMany</code> or{" "}
        <code>Join</code>, a projection behind another <code>Select</code>, an
        initializer after a constructor with arguments, and a lazy loader the
        constructor takes and keeps in a field or a property of any name each
        loaded a denied value the gate read as unloaded. A field a subtype
        declares — a derived entity&apos;s, or a subclass&apos;s held by a
        base-typed member — was not read at all, and under a{" "}
        <code>&quot;*&quot;</code> deny a path the walk never asked about was
        allowed. Each came back.
      </Callout>
      <Callout tone="danger" title="Rows of a derived type come back as T">
        When a type the model derives from <code>T</code>, or a loaded subclass
        of a row in memory, declares a denied field, the rows are projected to{" "}
        <code>T</code>, so a derived type&apos;s allowed fields are dropped too,
        and a member declared as a base type is narrowed to it. Over an abstract{" "}
        <code>T</code> the typed terminals fail with{" "}
        <code>SelectTypeMustHaveParameterlessConstructor</code>; the dynamic ones
        return its members. Query the derived type,{" "}
        <code>{`OfType<Company>()`}</code>, to keep its fields.
      </Callout>
      <Callout tone="danger" title="Fixed (security): a denied member that holds no simple value came back">
        A field denied at the top of <code>T</code> whose own type is not a
        simple value — a byte array, a list, an owned object, a JSON column —
        synthesized no projection either, so with nothing else denied the whole
        row came back with it.
      </Callout>
      <Callout tone="danger" title="Nested objects and lists come back">
        In 3.1.0, as soon as any field was denied, every nested object and list
        of a row projected before <code>ApplyPolicy</code> came back null or
        empty, and so did an entity&apos;s columns holding an object, its owned
        and complex members and its collections of simple values. They are
        returned now, whole or narrowed. A member that cannot be narrowed is
        left out whole, and the trace records a <code>Dropped</code> decision
        whose reason starts <code>left out whole</code>.
      </Callout>
      <Callout tone="danger" title="What a projection leaves out">
        Once a projection is needed it leaves out an entity&apos;s navigations,
        included ones too, since projecting one would load it: under{" "}
        <code>Convenience</code> name the navigation in <code>Selects</code> to
        get it narrowed, and under <code>Strict</code> name its allowed fields.
        It leaves out
        the objects a row in memory holds, since a kept object is the
        caller&apos;s own and a transform would change it in place, and a value
        EF Core does not map, which EF Core could compute only by reading the
        whole entity, the denied columns included. A member with no setter and
        a member named with one of the parser&apos;s words are left out too. A
        typed query projects into <code>T</code>, so <code>T</code> needs a
        public parameterless constructor for it, as it already did
        (point&nbsp;1).
      </Callout>
      <Callout tone="warn" title="A forced scope on a list's element type filters rows, not elements">
        A forced scope declared on a list&apos;s element type asks for no
        projection on its own. It filters the rows that hold the list, never its
        elements, so <code>Selects</code> naming the list returns every element,
        those the scope excludes included, as in every release. A projection
        needed for another reason leaves such a list out whole. Scope the
        elements where the row is built.
      </Callout>

      <h2 id="selects-beneath">26. <code>Selects</code> Naming a Member Is Gated Against Every Denial Beneath It</h2>
      <p>
        When <code>Selects</code> names a navigation with a denied field beneath
        it, the <code>Convenience</code> tier replaces the entry with the
        allowed fields beneath it, and the <code>Strict</code> tier refuses it.
        Since <strong>3.2.0</strong> the gate finds every denial beneath the
        member, and refuses, with <code>FieldDeniedForSelect</code>, a narrowing
        it cannot build as gated. See{" "}
        <Link href="/docs/policies/configuration#navigation-selects">A navigation named in Selects</Link>.
      </p>
      <table>
        <thead>
          <tr><th><code>Selects</code> names</th><th>Until 3.1.0</th><th>Since 3.2.0</th></tr>
        </thead>
        <tbody>
          <tr>
            <td>A navigation whose key, <code>Id</code>, is denied</td>
            <td>
              The convenience tier narrowed the key away, and the core&apos;s
              typed projection, which adds the key of every nested node it
              builds, put it back.
            </td>
            <td>
              Refused in both tiers, as naming a sibling of the key already
              was.
            </td>
          </tr>
          <tr>
            <td>A navigation named through another, <code>Main.Lead</code>, when <code>Main.Id</code> is denied</td>
            <td>Kept, and the projection added <code>Main</code>&apos;s key.</td>
            <td>Refused: the key of every node the path passes through is gated.</td>
          </tr>
          <tr>
            <td>
              A member typed as a collection the core does not unwrap —{" "}
              <code>IReadOnlyList&lt;T&gt;</code>,{" "}
              <code>IReadOnlyCollection&lt;T&gt;</code>,{" "}
              <code>Collection&lt;T&gt;</code> or an application&apos;s own —
              with a denied field beneath it
            </td>
            <td>
              Every field beneath it came back, the denied ones included, in both
              tiers: the projection gate read collections through a narrower list
              than the attribute walker, and found nothing beneath the member.
            </td>
            <td>
              The gate reads collections the way the walker does. The strict
              tier refuses the denied field, and the convenience tier&apos;s
              narrowing, which the core cannot project, is refused too.
            </td>
          </tr>
          <tr>
            <td>
              A member that carries a field denied where no path reaches it:
              deeper than four segments, inside a framework generic such as{" "}
              <code>Dictionary&lt;string, T&gt;</code>, declared by a subtype of
              its type, in an entity navigation&apos;s owned chain or converted
              column, or, under a <code>&quot;*&quot;</code> deny, on a path the
              walk never asks about
            </td>
            <td>Returned, the denied field included.</td>
            <td>
              Refused under <code>Strict</code>. Under <code>Convenience</code>{" "}
              narrowed where the core can narrow it, which builds the declared
              type, and refused where it cannot.
            </td>
          </tr>
          <tr>
            <td>A member with a denied property that has no setter beneath it, or a rule on a path reached through a cycle</td>
            <td>Not found, so the member came back with it.</td>
            <td>Found: the gate reads the providers&apos; rules as well as the walk.</td>
          </tr>
        </tbody>
      </table>
      <p>
        A member that cannot be narrowed at all — a column, a complex property
        or a member stored as JSON, a member of a row in memory, or one a
        projection builds some way the core cannot narrow — is refused in both
        tiers when something beneath it is denied.
      </p>
      <Callout tone="danger" title="Fixed (security): named members carried denied values out">
        A request that ran on 3.1.0 can now be refused. Under the{" "}
        <code>Convenience</code> tier the refusal names the denied key, the
        first denied field beneath the member, or, for a denial no path names,
        the member itself; under <code>Strict</code> its <code>FieldPath</code>{" "}
        is <code>&quot;*&quot;</code>. A request that sends no{" "}
        <code>Selects</code> is not refused for such a member: its synthesized
        projection narrows the member or leaves it out whole (point&nbsp;25).
      </Callout>
      <Callout tone="warn" title="What the policy cannot see into">
        A member typed <code>object</code>, a framework interface or a
        collection that is not generic, such as <code>IEnumerable</code>,{" "}
        <code>ArrayList</code> or <code>Array</code>, is opaque to the policy: a
        synthesized projection leaves it out, and naming it returns whatever it
        holds. A framework generic holding a policed type, such as{" "}
        <code>Dictionary&lt;string, LineDto&gt;</code>, has no paths beneath it:
        naming it is refused in both tiers where the core cannot narrow it,
        narrowed away under <code>Convenience</code> beneath a navigation, and a
        synthesized projection leaves it out. Hold such values in a list of the
        policed type instead.
      </Callout>

      <h2 id="default-order-projection">27. <code>DefaultOrder</code> Reaches a Projection That Builds <code>T</code></h2>
      <p>
        In 3.1.0 a <code>Select</code> anywhere in the chain kept a guarded query
        in its own order, because a default applied after a projection could
        name a field the projection left out, which EF Core cannot translate.
        Since <strong>3.2.0</strong> only the outermost <code>Select</code>{" "}
        counts, because it makes the rows the default orders. When it builds{" "}
        <code>T</code> in an object initializer and assigns every field the{" "}
        <Link href="/docs/policies/attributes#default-order"><code>[DwEntity(DefaultOrder)]</code></Link>{" "}
        names a column, at every level of a nested path, the default applies. A
        column is a member the EF Core model maps on the entity the{" "}
        <code>Select</code> reads, read directly, through reference navigations
        or through <code>EF.Property</code>; in memory any assigned field is
        one. A value the projection computes, by any method or operator, even
        one EF Core could translate, a member the model does not map, a
        constructor with arguments, a default field the initializer does not
        assign, or a nested path through anything but an initializer still
        leaves the query in its own order: ordering by it could fail where the
        unguarded query ran.
      </p>
      <Code lang="csharp">{`[DwEntity(DefaultOrder = "CreatedAt desc, Id")]
public class TicketRow
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Title { get; set; } = string.Empty;
}

// 3.1.0: unordered. 3.2.0: ordered by CreatedAt desc, Id.
var rows = db.Tickets
    .Select(t => new TicketRow { Id = t.Id, CreatedAt = t.CreatedAt, Title = t.Title })
    .ApplyPolicy(caller)
    .ToList(new Filter());`}</Code>
      <ul>
        <li>
          A projection composed on the guarded handle — the guarded{" "}
          <code>Select</code>, or a guarded <code>Filter</code> whose{" "}
          <code>Selects</code> is set — keeps the rest of the chain unordered,
          even when it keeps every default field. So{" "}
          <code>{`guarded.Select(["Id", "Title"]).Page(page)`}</code> pages as it
          did in 3.0.0, unordered.
        </li>
        <li>
          A <code>Filter</code> composed on the handle that sent orders gets no
          default later in the chain, even when the policy dropped every one of
          them, as a composed <code>Order</code> already did not.
        </li>
      </ul>
      <Callout tone="danger" title="A projected query that ran unordered can now be ordered">
        A guarded query over such a projection that sends no orders now comes
        back in the declared order, where it used to come back in the
        database&apos;s. Paging through it is stable if the default ends with a
        unique field.
      </Callout>

      <h2 id="cancellation-token">28. Every Async Terminal Takes a <code>CancellationToken</code></h2>
      <p>
        Since <strong>3.2.0</strong> every asynchronous terminal, guarded and
        unguarded, has overloads that take a <code>CancellationToken</code>:{" "}
        <Link href="/docs/extensions/to-list-async-filter"><code>ToListAsync</code></Link>{" "}
        and{" "}
        <Link href="/docs/extensions/to-list-async-dynamic-filter"><code>ToListAsyncDynamic</code></Link>{" "}
        with a <code>Filter</code>,{" "}
        <Link href="/docs/extensions/to-list-async-summary"><code>ToListAsync</code></Link>{" "}
        with a <code>Summary</code>, and{" "}
        <Link href="/docs/extensions/to-list-async-segment"><code>ToListAsync</code></Link>{" "}
        with a <code>Segment</code>. The token reaches the count and the read. The
        overloads sit beside the 3.1 signatures, which are unchanged, so code
        compiled against 3.1 still binds. That brings the extension methods to
        28. A reflection lookup by name alone finds more overloads than it did,
        and where it found one — <code>ToListAsyncDynamic</code>, on the
        extension class and on the guarded handle — it now finds several, so{" "}
        <code>Type.GetMethod</code> given only the name throws{" "}
        <code>AmbiguousMatchException</code>; pass the parameter types.
      </p>
      <Callout tone="danger" title="ToListAsync(filter, default) no longer compiles">
        <code>default</code> fits both <code>bool getQueryString</code> and the
        new <code>CancellationToken</code> overload, so the call is ambiguous
        (CS0121). So are <code>ToListAsyncDynamic(filter, default)</code> and{" "}
        <code>ToListAsync(summary, default)</code>, on a query and on the guarded
        handle alike. Write <code>false</code>, a token, or a named argument.
        <Code lang="csharp">{`await query.ToListAsync(filter, default);              // CS0121 since 3.2.0
await query.ToListAsync(filter, false);                // as 3.1 read it
await query.ToListAsync(filter, cancellationToken);    // the new overload`}</Code>
      </Callout>
      <Callout tone="warn" title="The dynamic and summary reads go through EF Core">
        <code>ToListAsyncDynamic</code> and the async <code>Summary</code> read
        through EF Core&apos;s <code>ToListAsync</code> instead of Dynamic
        LINQ&apos;s <code>ToDynamicListAsync</code>, which had no token to pass
        on, and the async <code>Summary</code> counts through{" "}
        <code>CountAsync</code> where it counted synchronously. So on an EF Core
        query a canceled token now reaches the database. The rows and the counts
        are the same. A provider that is not EF Core&apos;s keeps Dynamic
        LINQ&apos;s read, on the calling thread.
      </Callout>

      <h2 id="system-namespace">29. A Type in a Namespace That Starts with <code>System</code> Is Policed</h2>
      <p>
        The attribute walker does not descend into the framework&apos;s own
        types, which carry no policy attributes. Until <strong>3.2.0</strong>{" "}
        it took any namespace whose name started with <code>System</code> for
        the framework&apos;s, so an application namespace such as{" "}
        <code>SystemsCorp.Payroll</code> or <code>SystemX.Domain</code> got no
        policy beneath its types. A <code>[DwDenied]</code> field on such a type,
        reached through a member, was returned, filterable and sortable. Only{" "}
        <code>System</code> and the namespaces beneath it are the
        framework&apos;s now.
      </p>
      <Callout tone="danger" title="Fixed (security): an application namespace was read as the framework's">
        A guarded request that filtered on, sorted by or selected such a field
        ran on 3.1.0. It is now refused or dropped, as for any denied field.
      </Callout>

      <h2 id="next">See also</h2>
      <ul>
        <li>
          <Link href="/docs/errors">Error Codes Reference →</Link> the 30 stable
          validation messages.
        </li>
        <li>
          <Link href="/docs/enums/data-type"><code>DataType</code> →</Link>{" "}
          context for points 14 and 15, and how a deployment{" "}
          <Link href="/docs/enums/data-type#date-formats">declares its date formats</Link>.
        </li>
        <li>
          <Link href="/docs/classes/filter-result"><code>FilterResult&lt;T&gt;</code> →</Link>{" "}
          context for point 16.
        </li>
        <li>
          <Link href="/docs/cache/configuration">Cache configuration →</Link>{" "}
          context for point 8.
        </li>
        <li>
          <Link href="/docs/extensions/select-dynamic"><code>SelectDynamic</code> →</Link>{" "}
          context for points 1 and 10.
        </li>
        <li>
          <Link href="/docs/classes/aggregate-by"><code>AggregateBy</code> →</Link>{" "}
          context for point 13.
        </li>
        <li>
          <Link href="/docs/enums/operator#in"><code>Operator</code> →</Link>{" "}
          context for the long-list fix in point 12.
        </li>
        <li>
          <Link href="/docs/policies/configuration">Policy configuration →</Link>{" "}
          context for points 21 to 26: the caps, the trace on a result, what a
          strict refusal carries, and what a denied field does to a projection.
        </li>
        <li>
          <Link href="/docs/policies/security#probing">Security &amp; k-anonymity →</Link>{" "}
          why the strict tier hides which fields exist (point 23), and{" "}
          <Link href="/docs/policies/security#beneath">the denials the gate could not see</Link>{" "}
          (points 25, 26 and 29).
        </li>
        <li>
          <Link href="/docs/policies/attributes#default-order">Default order →</Link>{" "}
          context for point 27.
        </li>
        <li>
          <Link href="/docs/extensions#materialization">Materialization →</Link>{" "}
          context for point 28: every async terminal and its overloads.
        </li>
      </ul>
    </DocPage>
  );
}
