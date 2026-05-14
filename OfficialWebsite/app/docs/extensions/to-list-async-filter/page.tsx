import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".ToListAsync<T>(Filter)",
  description:
    "Async EF Core entry point — materialize a Filter against an IQueryable<T> using CountAsync and ToListAsync.",
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
        <code>ToListAsync()</code> under the hood.
      </p>

      <h2 id="signature">Signature</h2>
      <Code lang="csharp">{`public static Task<FilterResult<T>> ToListAsync<T>(
    this IQueryable<T> query,
    Filter filter,
    bool getQueryString = false)
    where T : class, new()`}</Code>

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
        </tbody>
      </table>

      <h2 id="pipeline">Pipeline</h2>
      <ul>
        <li><code>Where</code> applied on the typed query.</li>
        <li>
          <code>CountAsync()</code> on the typed query → <code>TotalCount</code>.
        </li>
        <li><code>Order</code> applied on the typed query.</li>
        <li><code>Page</code> applied on the typed query.</li>
        <li><code>Select</code> projection applied last.</li>
        <li><code>ToListAsync()</code> materializes the result.</li>
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
