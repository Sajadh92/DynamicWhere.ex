import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".ToListAsync<T>(Summary)",
  description:
    "Async EF Core entry — materialize a Summary against an IQueryable<T> and return Task<SummaryResult>, with overloads that take a CancellationToken.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/extensions/to-list-async-summary/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/to-list-async-summary">
      <h1>.ToListAsync&lt;T&gt;(Summary)</h1>
      <p>
        Async version of{" "}
        <Link href="/docs/extensions/to-list-summary">
          <code>.ToList&lt;T&gt;(Summary)</code>
        </Link>
        . On an EF Core query it counts the groups with EF Core&apos;s{" "}
        <code>CountAsync()</code> and reads them with EF Core&apos;s{" "}
        <code>ToListAsync()</code>. Since 3.2.0 two more overloads take a{" "}
        <code>CancellationToken</code>, which reaches both.
      </p>

      <Callout tone="warn" title="Guarded, small groups are dropped by default">
        <code>DwCaps.MinGroupSize</code> ships <strong>on, at 5</strong>, so a
        guarded summary removes every group with fewer than five rows — not
        refused, and nothing in the answer says a group was dropped. That is
        right for anonymised reporting and surprising for an operational count.
        Set <code>Caps.MinGroupSize = 1</code> to switch the floor off,
        deliberately. An unguarded call is never floored. See{" "}
        <Link href="/docs/policies/security#aggregates">k-anonymity</Link>.
      </Callout>

      <h2 id="signature">Signature</h2>
      <Code lang="csharp">{`public static Task<SummaryResult> ToListAsync<T>(
    this IQueryable<T> query,
    Summary summary,
    bool getQueryString = false)
    where T : class

// 3.2.0
public static Task<SummaryResult> ToListAsync<T>(
    this IQueryable<T> query,
    Summary summary,
    CancellationToken cancellationToken)
    where T : class

public static Task<SummaryResult> ToListAsync<T>(
    this IQueryable<T> query,
    Summary summary,
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
            <td><code>summary</code></td>
            <td>
              <Link href="/docs/classes/summary"><code>Summary</code></Link>
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
              <code>SummaryResult.QueryString</code>
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
        <li><code>Group</code> applied — produces grouped dynamic intermediate.</li>
        <li><code>Having</code> applied — fields must reference aggregate aliases.</li>
        <li>
          The grouped query is counted → <code>TotalCount</code>. On an EF Core
          query this is EF Core&apos;s <code>CountAsync(cancellationToken)</code>.
        </li>
        <li><code>Order</code> applied on the grouped query.</li>
        <li><code>Page</code> applied on the grouped query.</li>
        <li>
          Async materialization as <code>List&lt;dynamic&gt;</code>. On an EF
          Core query this is EF Core&apos;s{" "}
          <code>ToListAsync(cancellationToken)</code>.
        </li>
      </ul>
      <p>
        A source whose provider is not EF Core&apos;s — rows in memory through{" "}
        <code>AsQueryable()</code>, for one — keeps the reads it had in 3.1: a
        synchronous <code>Count()</code>, then Dynamic LINQ&apos;s{" "}
        <code>ToDynamicListAsync()</code>, which reads on the calling thread. A
        token that is already canceled still stops it before the count.
      </p>

      <Callout tone="warn" title="Changed in 3.2.0: the count and the read are asynchronous on EF Core">
        Until 3.2.0 the group count ran synchronously, and the read went through
        Dynamic LINQ&apos;s <code>ToDynamicListAsync()</code>, which had no token
        to pass on, on every provider. On an EF Core query both now run through
        EF Core&apos;s asynchronous operators, so a canceled token reaches the
        database. The count and the rows are the same.
      </Callout>

      <Callout tone="note">
        Dotted <code>GroupBy</code> fields like <code>Category.Name</code>{" "}
        become flattened aliases in the result (e.g.,{" "}
        <code>CategoryName</code>). Order fields in <code>Summary.Orders</code>{" "}
        use the dotted form — the library handles alias mapping internally.
      </Callout>

      <h2 id="cancellation">Cancellation</h2>
      <p>
        The two overloads that take a <code>CancellationToken</code> are new in
        3.2.0. The token reaches the count and the read. On an EF Core query a
        canceled token stops whichever of the two is running, and the call
        throws <code>OperationCanceledException</code>. EF Core&apos;s{" "}
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
      <Callout tone="warn" title="ToListAsync(summary, default) does not compile">
        <code>default</code> fits both <code>bool getQueryString</code> and{" "}
        <code>CancellationToken</code>, so the compiler reports the call as
        ambiguous (CS0121). Write <code>false</code>, a token, or a named
        argument.
      </Callout>
      <Code lang="csharp">{`await db.Products.ToListAsync(summary, default);                   // CS0121: ambiguous
await db.Products.ToListAsync(summary, cancellationToken);         // the token overload
await db.Products.ToListAsync(summary, true, cancellationToken);   // the SQL and a token`}</Code>

      <h2 id="returns">Returns</h2>
      <p>
        <code>Task&lt;</code>
        <Link href="/docs/classes/summary-result">
          <code>SummaryResult</code>
        </Link>
        <code>&gt;</code>.
      </p>

      <h2 id="example">Example</h2>
      <Code lang="csharp">{`SummaryResult result = await dbContext.Products.ToListAsync(summary);

foreach (var row in result.Data)
{
    Console.WriteLine($"{row.CategoryName}: {row.ProductCount} products");
}`}</Code>

      <p><strong>ASP.NET Core endpoint.</strong></p>
      <Code lang="csharp">{`app.MapPost("/products/summary", async (Summary summary, AppDbContext db) =>
{
    var result = await db.Products.ToListAsync(summary);
    return Results.Ok(result);
});`}</Code>

      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 10,
  "pageCount": 1,
  "totalCount": 3,
  "data": [
    { "CategoryName": "Electronics", "ProductCount": 15, "AvgPrice": 349.99, "TotalRevenue": 5249.85 }
  ],
  "queryString": null
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/to-list-summary">
            <code>.ToList&lt;T&gt;(Summary)</code>
          </Link>{" "}
          — synchronous variant (and in-memory overload).
        </li>
        <li>
          <Link href="/docs/extensions/summary">
            <code>.Summary&lt;T&gt;</code>
          </Link>{" "}
          — non-materializing composition.
        </li>
        <li>
          <Link href="/docs/examples/summary">JSON Cookbook: Summary</Link>.
        </li>
      </ul>
    </DocPage>
  );
}
