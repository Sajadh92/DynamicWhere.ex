import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".ToListAsyncDynamic<T>(Filter)",
  description:
    "Async EF Core entry — materialize a Filter using SelectDynamic, EF Core's CountAsync and ToListAsync, returning Task<FilterResult<dynamic>>, with overloads that take a CancellationToken.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/extensions/to-list-async-dynamic-filter/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/to-list-async-dynamic-filter">
      <h1>.ToListAsyncDynamic&lt;T&gt;(Filter)</h1>
      <p>
        Async version of{" "}
        <Link href="/docs/extensions/to-list-dynamic-filter">
          <code>.ToListDynamic&lt;T&gt;(Filter)</code>
        </Link>
        . Counts with EF Core's <code>CountAsync()</code> and reads with EF
        Core's own <code>ToListAsync()</code>. Since 3.2.0 two more overloads
        take a <code>CancellationToken</code>, which reaches both.
      </p>

      <h2 id="signature">Signature</h2>
      <Code lang="csharp">{`public static Task<FilterResult<dynamic>> ToListAsyncDynamic<T>(
    this IQueryable<T> query,
    Filter filter,
    bool getQueryString = false)
    where T : class

// 3.2.0
public static Task<FilterResult<dynamic>> ToListAsyncDynamic<T>(
    this IQueryable<T> query,
    Filter filter,
    CancellationToken cancellationToken)
    where T : class

public static Task<FilterResult<dynamic>> ToListAsyncDynamic<T>(
    this IQueryable<T> query,
    Filter filter,
    bool getQueryString,
    CancellationToken cancellationToken)
    where T : class`}</Code>

      <table>
        <thead>
          <tr>
            <th>Parameter</th>
            <th>Type</th>
            <th>Default</th>
            <th>Description</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>filter</code></td>
            <td>
              <Link href="/docs/classes/filter"><code>Filter</code></Link>
            </td>
            <td>–</td>
            <td>Composition object</td>
          </tr>
          <tr>
            <td><code>getQueryString</code></td>
            <td><code>bool</code></td>
            <td><code>false</code></td>
            <td>
              When <code>true</code>, captures the generated SQL on{" "}
              <code>QueryString</code>
            </td>
          </tr>
          <tr>
            <td><code>cancellationToken</code></td>
            <td><code>CancellationToken</code></td>
            <td>–</td>
            <td>
              Cancels the count and the read. The overload without it passes{" "}
              <code>CancellationToken.None</code>
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="pipeline">Pipeline</h2>
      <ul>
        <li><code>Where</code> applied on the typed query.</li>
        <li>
          <code>CountAsync(cancellationToken)</code> on the typed query →{" "}
          <code>TotalCount</code>.
        </li>
        <li><code>Order</code> applied on the typed query.</li>
        <li><code>Page</code> applied on the typed query.</li>
        <li><code>SelectDynamic</code> projection applied last.</li>
        <li>
          EF Core&apos;s <code>ToListAsync(cancellationToken)</code> materializes
          the result, called for the query&apos;s element type: the class the
          projection generates, or <code>T</code> when <code>Selects</code> is
          null.
        </li>
      </ul>

      <Callout tone="warn" title="Changed in 3.2.0: the read goes through EF Core">
        The read used to run Dynamic LINQ&apos;s{" "}
        <code>ToDynamicListAsync()</code>, asynchronous as well but with no token
        to pass on. On an EF Core query it now runs through EF Core&apos;s{" "}
        <code>ToListAsync()</code>, so a canceled token reaches the database. The
        rows are the same. Only the count needs an EF Core async provider; on
        any other provider the read falls back to Dynamic LINQ&apos;s.
      </Callout>

      <Callout tone="warn">
        Ordering and pagination are applied on the strongly-typed{" "}
        <code>IQueryable&lt;T&gt;</code> <strong>before</strong> the dynamic
        projection so that field names referenced in <code>orders</code> always
        resolve against the original entity type <code>T</code>.
      </Callout>

      <Callout tone="note">
        Property names in the dynamic result follow{" "}
        <Link href="/docs/extensions/select-dynamic">
          <code>SelectDynamic</code>
        </Link>{" "}
        rules — see that page for the full set of access patterns through
        nested dynamic objects and collections.
      </Callout>

      <h2 id="cancellation">Cancellation</h2>
      <p>
        The two overloads that take a <code>CancellationToken</code> are new in
        3.2.0. The token reaches the count and the read, so a canceled token
        stops whichever of the two is running, and the call throws{" "}
        <code>OperationCanceledException</code>. EF Core&apos;s{" "}
        <code>TaskCanceledException</code> derives from it. The overload without
        a token passes <code>CancellationToken.None</code>.
      </p>
      <p>
        They are overloads, not an optional parameter added to the old
        signature. The 3.1 signature is unchanged, so code compiled against 3.1
        still binds. The guarded handle that{" "}
        <Link href="/docs/policies#handle"><code>ApplyPolicy</code></Link>{" "}
        returns has the same overloads.
      </p>
      <Callout tone="warn" title="ToListAsyncDynamic(filter, default) does not compile">
        <code>default</code> fits both <code>bool getQueryString</code> and{" "}
        <code>CancellationToken</code>, so the compiler reports the call as
        ambiguous (CS0121). Write <code>false</code>, a token, or a named
        argument.
      </Callout>
      <Code lang="csharp">{`await db.Products.ToListAsyncDynamic(filter, default);                   // CS0121: ambiguous
await db.Products.ToListAsyncDynamic(filter, cancellationToken);         // the token overload
await db.Products.ToListAsyncDynamic(filter, true, cancellationToken);   // the SQL and a token`}</Code>

      <h2 id="returns">Returns</h2>
      <p>
        <code>Task&lt;FilterResult&lt;dynamic&gt;&gt;</code>.
      </p>

      <h2 id="example">Example</h2>
      <Code lang="csharp">{`FilterResult<dynamic> result = await dbContext.Products.ToListAsyncDynamic(filter);

foreach (var p in result.Data)
{
    Console.WriteLine($"{p.Name} — {p.Category.Name}");
}`}</Code>

      <Code lang="csharp">{`var result = await dbContext.Products.ToListAsyncDynamic(filter, getQueryString: true);
Console.WriteLine(result.QueryString);`}</Code>

      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 10,
  "pageCount": 5,
  "totalCount": 42,
  "data": [
    { "Id": 7, "Name": "Laptop Pro", "Price": 1299.99, "Category": { "Name": "Electronics" } }
  ],
  "queryString": null
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/to-list-dynamic-filter">
            <code>.ToListDynamic&lt;T&gt;(Filter)</code>
          </Link>{" "}
          — synchronous variant (and in-memory overload).
        </li>
        <li>
          <Link href="/docs/extensions/to-list-async-filter">
            <code>.ToListAsync&lt;T&gt;(Filter)</code>
          </Link>{" "}
          — async typed variant.
        </li>
        <li>
          <Link href="/docs/extensions/filter-dynamic">
            <code>.FilterDynamic&lt;T&gt;</code>
          </Link>{" "}
          — non-materializing composition.
        </li>
      </ul>
    </DocPage>
  );
}
