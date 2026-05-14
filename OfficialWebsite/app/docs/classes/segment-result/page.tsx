import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "SegmentResult<T>",
  description:
    "The result wrapper returned by ToListAsyncSegment — inherits every property from FilterResult<T>.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/classes/segment-result">
      <h1>SegmentResult&lt;T&gt;</h1>
      <p>
        <code>SegmentResult&lt;T&gt;</code> is the return shape of{" "}
        <Link href="/docs/extensions/to-list-async-segment"><code>ToListAsyncSegment</code></Link>.
        It <em>inherits</em> every property from{" "}
        <Link href="/docs/classes/filter-result"><code>FilterResult&lt;T&gt;</code></Link> — there
        are no additional members. The separate type exists only to distinguish results that came
        from a segment query (UNION / INTERSECT / EXCEPT) from those that came from a plain filter.
      </p>

      <h2 id="properties">Inherited properties</h2>
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
            <td>Total matching records (across all combined sets).</td>
          </tr>
          <tr>
            <td>
              <code>Data</code>
            </td>
            <td>
              <code>List&lt;T&gt;</code>
            </td>
            <td>The result entities — already de-duplicated by the set operations.</td>
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

      <Callout tone="info">
        Because <code>SegmentResult&lt;T&gt;</code> inherits <code>FilterResult&lt;T&gt;</code>,
        any helper or extension you write against the base type works for both.
      </Callout>

      <h2 id="csharp-example">C# usage</h2>
      <Code lang="csharp">{`SegmentResult<Customer> result = await dbContext.Customers.ToListAsyncSegment(segment);

Console.WriteLine($"page {result.PageNumber} of {result.PageCount}");
Console.WriteLine($"{result.TotalCount} total matches");

foreach (var customer in result.Data)
{
    Console.WriteLine($"- {customer.Name}");
}`}</Code>

      <h2 id="json-response">JSON response example</h2>
      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 50,
  "pageCount": 3,
  "totalCount": 124,
  "data": [
    { "id": 7, "name": "John Doe" },
    { "id": 12, "name": "Aisha Khan" }
  ],
  "queryString": null
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/segment">Segment →</Link>
        </li>
        <li>
          <Link href="/docs/classes/filter-result">FilterResult&lt;T&gt; →</Link> the base type.
        </li>
        <li>
          <Link href="/docs/extensions/to-list-async-segment">ToListAsyncSegment →</Link>
        </li>
      </ul>
    </DocPage>
  );
}
