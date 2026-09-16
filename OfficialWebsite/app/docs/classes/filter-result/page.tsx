import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "FilterResult<T>",
  description:
    "The strongly-typed result wrapper returned by ToList<T>(Filter) / ToListAsync<T>(Filter) and their dynamic siblings — data plus pagination metadata.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/classes/filter-result/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/classes/filter-result">
      <h1>FilterResult&lt;T&gt;</h1>
      <p>
        <code>FilterResult&lt;T&gt;</code> is the strongly-typed wrapper returned by every terminal
        filter extension —{" "}
        <Link href="/docs/extensions/to-list-filter"><code>ToList&lt;T&gt;(Filter)</code></Link>,{" "}
        <Link href="/docs/extensions/to-list-async-filter"><code>ToListAsync&lt;T&gt;(Filter)</code></Link>,
        and the dynamic variants. It contains the result page plus pagination metadata.
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
            <td>Total matching records.</td>
          </tr>
          <tr>
            <td>
              <code>Data</code>
            </td>
            <td>
              <code>List&lt;T&gt;</code>
            </td>
            <td>The result entities.</td>
          </tr>
          <tr>
            <td>
              <code>QueryString</code>
            </td>
            <td>
              <code>string?</code>
            </td>
            <td>
              Generated SQL (when <code>getQueryString: true</code> is passed to the extension).
            </td>
          </tr>
          <tr>
            <td>
              <code>Policy</code>
            </td>
            <td>
              <code>PolicyTrace?</code>
            </td>
            <td>
              What the policy layer did to this query.{" "}
              <code>null</code> unless the query went through{" "}
              <Link href="/docs/policies"><code>ApplyPolicy</code></Link>.
            </td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        When the input <code>Filter</code> has no <code>Page</code>, <code>PageNumber</code>{" "}
        and <code>PageSize</code> come back as <code>0</code> and the full result sits in{" "}
        <code>Data</code>. <code>PageCount</code> is{" "}
        <code>Ceiling(TotalCount / (PageSize == 0 ? 1 : PageSize))</code>, so with no paging
        it equals <code>TotalCount</code> — not <code>1</code>.
      </Callout>

      <h2 id="csharp-example">C# usage</h2>
      <Code lang="csharp">{`FilterResult<Customer> result = await dbContext.Customers.ToListAsync(filter);

Console.WriteLine($"page {result.PageNumber} of {result.PageCount}");
Console.WriteLine($"{result.TotalCount} total matches");

foreach (var customer in result.Data)
{
    Console.WriteLine($"- {customer.Name}");
}`}</Code>

      <h2 id="json-response">JSON response example</h2>
      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 10,
  "pageCount": 5,
  "totalCount": 42,
  "data": [
    { "id": 7, "name": "John Doe", "createdAt": "2025-09-14T12:31:00Z" },
    { "id": 8, "name": "Jane Roe", "createdAt": "2025-09-13T08:11:00Z" }
  ],
  "queryString": null,
  "policy": null
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/filter">Filter →</Link>
        </li>
        <li>
          <Link href="/docs/extensions/to-list-async-filter">ToListAsync&lt;T&gt;(Filter) →</Link>
        </li>
        <li>
          <Link href="/docs/classes/segment-result">SegmentResult&lt;T&gt; →</Link> inherits this shape.
        </li>
      </ul>
    </DocPage>
  );
}
