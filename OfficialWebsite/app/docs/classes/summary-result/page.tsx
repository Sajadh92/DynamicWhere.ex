import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "SummaryResult",
  description:
    "The result wrapper returned by ToListSummary / ToListAsyncSummary — same pagination shape as FilterResult, but Data is List<dynamic>.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/classes/summary-result/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/classes/summary-result">
      <h1>SummaryResult</h1>
      <p>
        <code>SummaryResult</code> is the return shape of{" "}
        <Link href="/docs/extensions/to-list-summary"><code>ToListSummary</code></Link> and{" "}
        <Link href="/docs/extensions/to-list-async-summary"><code>ToListAsyncSummary</code></Link>.
        It mirrors <Link href="/docs/classes/filter-result"><code>FilterResult&lt;T&gt;</code></Link>{" "}
        except <code>Data</code> is a list of dynamic objects whose properties are the{" "}
        <code>GroupBy.Fields</code> plus every <code>AggregateBy.Alias</code>.
      </p>

      <h2 id="properties">Properties</h2>
      <table>
        <thead>
          <tr>
            <th>Property</th>
            <th>Type</th>
            <th>Description</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              <code>PageNumber</code>
            </td>
            <td>
              <code>int</code>
            </td>
            <td>Current page (0 when no pagination).</td>
          </tr>
          <tr>
            <td>
              <code>PageSize</code>
            </td>
            <td>
              <code>int</code>
            </td>
            <td>Page size (0 when no pagination).</td>
          </tr>
          <tr>
            <td>
              <code>PageCount</code>
            </td>
            <td>
              <code>int</code>
            </td>
            <td>Total pages.</td>
          </tr>
          <tr>
            <td>
              <code>TotalCount</code>
            </td>
            <td>
              <code>int</code>
            </td>
            <td>Total grouped records.</td>
          </tr>
          <tr>
            <td>
              <code>Data</code>
            </td>
            <td>
              <code>List&lt;dynamic&gt;</code>
            </td>
            <td>Dynamic objects with group keys + aggregation values.</td>
          </tr>
          <tr>
            <td>
              <code>QueryString</code>
            </td>
            <td>
              <code>string?</code>
            </td>
            <td>
              Generated SQL (when <code>getQueryString: true</code> is passed).
            </td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info" title="Flattened alias keys">
        Each row in <code>Data</code> is a dynamic object whose top-level properties are the
        <code> GroupBy.Fields</code> names plus the <code>AggregateBy.Alias</code> names. There is
        no nesting under "group" / "aggregates" — everything is flat.
      </Callout>

      <h2 id="csharp-example">C# usage</h2>
      <Code lang="csharp">{`SummaryResult result = await dbContext.Orders.ToListAsync(summary);

foreach (dynamic row in result.Data)
{
    Console.WriteLine($"{row.Country}: {row.Total} orders, revenue {row.Revenue}");
}`}</Code>

      <h2 id="json-response">JSON response example</h2>
      <p>
        Given a <code>Summary</code> grouping by <code>Country</code> with aliases{" "}
        <code>Total</code> (Count) and <code>Revenue</code> (Sum of <code>Amount</code>):
      </p>
      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 20,
  "pageCount": 1,
  "totalCount": 3,
  "data": [
    { "Country": "IQ", "Total": 142, "Revenue": 28450.75 },
    { "Country": "SA", "Total": 98,  "Revenue": 19120.00 },
    { "Country": "AE", "Total": 31,  "Revenue":  6940.50 }
  ],
  "queryString": null
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/summary">Summary →</Link>
        </li>
        <li>
          <Link href="/docs/classes/group-by">GroupBy →</Link>
        </li>
        <li>
          <Link href="/docs/classes/aggregate-by">AggregateBy →</Link> defines the alias keys you see here.
        </li>
        <li>
          <Link href="/docs/extensions/to-list-async-summary">ToListAsyncSummary →</Link>
        </li>
      </ul>
    </DocPage>
  );
}
