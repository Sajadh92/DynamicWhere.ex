import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".ToListAsync<T>(Summary)",
  description:
    "Async EF Core entry — materialize a Summary against an IQueryable<T> and return Task<SummaryResult>.",
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
        . Uses EF Core's async count/materialization under the hood.
      </p>

      <h2 id="signature">Signature</h2>
      <Code lang="csharp">{`public static Task<SummaryResult> ToListAsync<T>(
    this IQueryable<T> query,
    Summary summary,
    bool getQueryString = false)
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
        </tbody>
      </table>

      <h2 id="pipeline">Pipeline</h2>
      <ul>
        <li><code>Where</code> applied on the typed query.</li>
        <li><code>Group</code> applied — produces grouped dynamic intermediate.</li>
        <li><code>Having</code> applied — fields must reference aggregate aliases.</li>
        <li>Async count on the grouped query → <code>TotalCount</code>.</li>
        <li><code>Order</code> applied on the grouped query.</li>
        <li><code>Page</code> applied on the grouped query.</li>
        <li>Async materialization as <code>List&lt;dynamic&gt;</code>.</li>
      </ul>

      <Callout tone="note">
        Dotted <code>GroupBy</code> fields like <code>Category.Name</code>{" "}
        become flattened aliases in the result (e.g.,{" "}
        <code>CategoryName</code>). Order fields in <code>Summary.Orders</code>{" "}
        use the dotted form — the library handles alias mapping internally.
      </Callout>

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
