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
        The nineteen points below cover constraints, surprises, and corner cases —
        read them before designing an API around the library so you can pick the
        right entry points and avoid runtime exceptions in production.
      </p>
      <Callout tone="danger" title="Behaviour changes in 3.1.0">
        Six behaviours changed in <strong>3.1.0</strong>. Each one fixes a defect,
        and each one is visible to a caller that depended on the old shape. Date
        comparisons now read the member&apos;s type before building
        the predicate (point&nbsp;14) and accept a value only in ISO&nbsp;8601, a
        year-first form, or a format the deployment declares (point&nbsp;15); an
        unpaged <code>PageCount</code> is now <code>1</code>{" "}
        rather than <code>TotalCount</code> (point&nbsp;16); the{" "}
        <code>Select</code> constructor refusal carries a stable code instead of an
        English sentence (point&nbsp;17); a guarded query is refused unless its
        context was prepared (point&nbsp;18); and a segment&apos;s condition sets
        are combined, ordered and paged in the database (point&nbsp;19).
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
      <Callout tone="danger" title="Fixed: DateOnly members could not be filtered">
        Until 3.1.0 no condition on a <code>DateOnly</code> member worked.{" "}
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
      <Callout tone="warn" title="IsNull answers a constant on a non-nullable member">
        With the guard gone, there is nothing left for{" "}
        <Link href="/docs/enums/operator"><code>IsNull</code></Link> and{" "}
        <code>IsNotNull</code> to test on a non-nullable date member, so they answer
        with the constant the guard already implied: <code>IsNull</code> is{" "}
        <code>false</code> and <code>IsNotNull</code> is <code>true</code>. On
        PostgreSQL that reaches the database as <code>WHERE FALSE</code> and, for{" "}
        <code>IsNotNull</code>, as no predicate at all. On a non-nullable{" "}
        <code>DateTimeOffset</code>, where both used to throw like every other
        operator, they now answer.
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
        member&apos;s own date type. Every deployment accepts ISO&nbsp;8601 (
        <code>2026-09-01</code>, optionally with a time after a <code>T</code> or
        a space, a fraction, and <code>Z</code> or an offset) and year-first dates
        with <code>/</code> or <code>.</code> (<code>2026/09/01</code>,{" "}
        <code>2026.09.01</code>), plus any format the deployment declares. The
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
        no page was sent.
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
        (29 with point&nbsp;15&apos;s <code>AmbiguousDateFormat</code>), leaving one
        validation failure whose message is a sentence rather than a code —{" "}
        <code>{`Unsupported combination of DataType '{type}' and Operator '{op}'.`}</code>{" "}
        See <Link href="/docs/errors">the error code reference</Link>.
      </Callout>
      <Callout tone="note" title="Guarded queries reach it too">
        A member carrying <code>[DwNoSelect]</code> makes the policy layer
        synthesize a projection for a query that sent none, so a typed guarded
        query on a type with no parameterless constructor raises the same code —
        even though the caller never asked for a <code>Select</code>. The dynamic
        terminals project through <code>SelectDynamic</code> and are not affected.
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
          <strong>Providers.</strong> The provider has to translate a correlated{" "}
          <code>EXISTS</code>. A type with no primary key also needs every column
          to be comparable — not PostgreSQL <code>json</code> or SQL Server{" "}
          <code>xml</code> — and support for all three SQL set operators.
        </li>
      </ul>

      <h2 id="next">See also</h2>
      <ul>
        <li>
          <Link href="/docs/errors">Error Codes Reference →</Link> the 29 stable
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
      </ul>
    </DocPage>
  );
}
