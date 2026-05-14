import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".ToList<T>(Summary)",
  description:
    "Materialize a Summary against an IQueryable<T> or IEnumerable<T> and return a paginated SummaryResult.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/to-list-summary">
      <h1>.ToList&lt;T&gt;(Summary)</h1>
      <p>
        Materializes a{" "}
        <Link href="/docs/classes/summary"><code>Summary</code></Link> and
        returns a{" "}
        <Link href="/docs/classes/summary-result">
          <code>SummaryResult</code>
        </Link>{" "}
        with pagination metadata.
      </p>

      <h2 id="queryable">IQueryable&lt;T&gt; overload</h2>
      <Code lang="csharp">{`public static SummaryResult ToList<T>(
    this IQueryable<T> query,
    Summary summary,
    bool getQueryString = false)
    where T : class`}</Code>

      <h2 id="enumerable">IEnumerable&lt;T&gt; overload</h2>
      <p>In-memory variant for summary operations.</p>
      <Code lang="csharp">{`public static SummaryResult ToList<T>(
    this IEnumerable<T> source,
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
        <li>
          <code>Group</code> applied — produces grouped dynamic intermediate.
        </li>
        <li>
          <code>Having</code> applied — fields must reference aggregate aliases.
        </li>
        <li><code>Count</code> on the grouped query → <code>TotalCount</code>.</li>
        <li><code>Order</code> applied on the grouped query.</li>
        <li><code>Page</code> applied on the grouped query.</li>
        <li>Materialized as <code>List&lt;dynamic&gt;</code>.</li>
      </ul>

      <Callout tone="note">
        Passing <code>getQueryString: true</code> calls{" "}
        <code>.ToQueryString()</code>, which requires an active EF Core
        provider; pure in-memory usage may not support it.
      </Callout>

      <h2 id="returns">Returns</h2>
      <p>
        <Link href="/docs/classes/summary-result">
          <code>SummaryResult</code>
        </Link>{" "}
        with <code>PageNumber</code>, <code>PageSize</code>,{" "}
        <code>PageCount</code>, <code>TotalCount</code>,{" "}
        <code>Data</code> (<code>List&lt;dynamic&gt;</code> with group keys +
        aggregation values), and optional <code>QueryString</code>.
      </p>

      <h2 id="example">Example</h2>
      <Code lang="csharp">{`SummaryResult result = dbContext.Products.ToList(summary);

foreach (var row in result.Data)
{
    Console.WriteLine($"{row.CategoryName}: {row.ProductCount} products, avg {row.AvgPrice}");
}`}</Code>

      <p><strong>In-memory variant.</strong></p>
      <Code lang="csharp">{`List<Product> source = LoadFromCsv();
SummaryResult result = source.ToList(summary);`}</Code>

      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 10,
  "pageCount": 1,
  "totalCount": 3,
  "data": [
    { "CategoryName": "Electronics", "ProductCount": 15, "AvgPrice": 349.99, "TotalRevenue": 5249.85 },
    { "CategoryName": "Clothing",    "ProductCount": 12, "AvgPrice": 45.00,  "TotalRevenue": 540.00 }
  ],
  "queryString": null
}`}</Code>

      <Callout tone="warn">
        Dotted <code>GroupBy</code> fields like <code>Category.Name</code>{" "}
        become flattened aliases in the result (e.g.,{" "}
        <code>CategoryName</code>).
      </Callout>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/to-list-async-summary">
            <code>.ToListAsync&lt;T&gt;(Summary)</code>
          </Link>{" "}
          — async EF Core variant.
        </li>
        <li>
          <Link href="/docs/extensions/summary"><code>.Summary&lt;T&gt;</code></Link>{" "}
          — non-materializing composition.
        </li>
        <li>
          <Link href="/docs/classes/summary-result">
            <code>SummaryResult</code>
          </Link>{" "}
          shape.
        </li>
      </ul>
    </DocPage>
  );
}
