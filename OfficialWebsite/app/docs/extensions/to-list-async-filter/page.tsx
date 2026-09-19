import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".ToListAsync<T>(Filter)",
  description:
    "Async EF Core entry point — materialize a Filter against an IQueryable<T> using CountAsync and ToListAsync, with overloads that take a CancellationToken.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/extensions/to-list-async-filter/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/to-list-async-filter">
      <h1>.ToListAsync&lt;T&gt;(Filter)</h1>
      <p>
        Async version of{" "}
        <Link href="/docs/extensions/to-list-filter">
          <code>.ToList&lt;T&gt;(Filter)</code>
        </Link>
        . Uses EF Core's <code>CountAsync()</code> and{" "}
        <code>ToListAsync()</code> under the hood. Since 3.2.0 two more
        overloads take a <code>CancellationToken</code>, which reaches both.
      </p>

      <h2 id="signature">Signature</h2>
      <Code lang="csharp">{`public static Task<FilterResult<T>> ToListAsync<T>(
    this IQueryable<T> query,
    Filter filter,
    bool getQueryString = false)
    where T : class

// 3.2.0
public static Task<FilterResult<T>> ToListAsync<T>(
    this IQueryable<T> query,
    Filter filter,
    CancellationToken cancellationToken)
    where T : class

public static Task<FilterResult<T>> ToListAsync<T>(
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
              <code>FilterResult.QueryString</code>
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
        <li><code>Select</code> projection applied last.</li>
        <li><code>ToListAsync(cancellationToken)</code> materializes the result.</li>
      </ul>

      <Callout tone="warn">
        Ordering and pagination are applied on the strongly-typed{" "}
        <code>IQueryable&lt;T&gt;</code> <strong>before</strong> the select
        projection so that field names referenced in <code>orders</code> always
        resolve against the original entity type <code>T</code>.
      </Callout>

      <Callout tone="note">
        <code>getQueryString: true</code> calls{" "}
        <code>.ToQueryString()</code> which requires an active EF Core
        database provider.
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
      <Callout tone="warn" title="ToListAsync(filter, default) does not compile">
        <code>default</code> fits both <code>bool getQueryString</code> and{" "}
        <code>CancellationToken</code>, so the compiler reports the call as
        ambiguous (CS0121). Write <code>false</code>, a token, or a named
        argument.
      </Callout>
      <Code lang="csharp">{`await db.Customers.ToListAsync(filter, default);                   // CS0121: ambiguous
await db.Customers.ToListAsync(filter, cancellationToken);         // the token overload
await db.Customers.ToListAsync(filter, getQueryString: false);     // no token
await db.Customers.ToListAsync(filter, true, cancellationToken);   // the SQL and a token`}</Code>

      <h2 id="returns">Returns</h2>
      <p>
        <code>Task&lt;</code>
        <Link href="/docs/classes/filter-result">
          <code>FilterResult&lt;T&gt;</code>
        </Link>
        <code>&gt;</code>.
      </p>

      <h2 id="example">Example</h2>
      <Code lang="csharp">{`FilterResult<Customer> result = await dbContext.Customers.ToListAsync(filter);

Console.WriteLine($"{result.TotalCount} customers");
foreach (var c in result.Data)
{
    Console.WriteLine($"- {c.Name}");
}`}</Code>

      <p><strong>Capture generated SQL.</strong></p>
      <Code lang="csharp">{`var result = await dbContext.Customers.ToListAsync(filter, getQueryString: true);
Console.WriteLine(result.QueryString);`}</Code>

      <p><strong>Minimal ASP.NET Core endpoint.</strong></p>
      <Code lang="csharp">{`app.MapPost("/customers/search", async (Filter filter, AppDbContext db) =>
{
    var result = await db.Customers.ToListAsync(filter);
    return Results.Ok(result);
});`}</Code>

      <p>
        <strong>Cancel with the request.</strong> A minimal API binds a{" "}
        <code>CancellationToken</code> parameter to{" "}
        <code>HttpContext.RequestAborted</code>, so a client that disconnects
        cancels the count or the read.
      </p>
      <Code lang="csharp">{`app.MapPost("/customers/search", async (Filter filter, AppDbContext db, CancellationToken cancellationToken) =>
{
    var result = await db.Customers.ToListAsync(filter, cancellationToken);
    return Results.Ok(result);
});`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/to-list-filter">
            <code>.ToList&lt;T&gt;(Filter)</code>
          </Link>{" "}
          — synchronous variant (and in-memory overload).
        </li>
        <li>
          <Link href="/docs/extensions/to-list-async-dynamic-filter">
            <code>.ToListAsyncDynamic&lt;T&gt;(Filter)</code>
          </Link>{" "}
          — async dynamic projection.
        </li>
        <li>
          <Link href="/docs/classes/filter-result">
            <code>FilterResult&lt;T&gt;</code>
          </Link>{" "}
          shape.
        </li>
      </ul>
    </DocPage>
  );
}
