import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "SegmentResult<T>",
  description:
    "The result wrapper returned by ToListAsync<T>(Segment) — inherits every property from FilterResult<T>.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/classes/segment-result/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/classes/segment-result">
      <h1>SegmentResult&lt;T&gt;</h1>
      <p>
        <code>SegmentResult&lt;T&gt;</code> is the return shape of{" "}
        <Link href="/docs/extensions/to-list-async-segment"><code>ToListAsync&lt;T&gt;(Segment)</code></Link>.
        It <em>inherits</em> every property from{" "}
        <Link href="/docs/classes/filter-result"><code>FilterResult&lt;T&gt;</code></Link> — there
        are no additional members. The separate type exists only to distinguish results that came
        from a segment query (Union / Intersect / Except, combined into one query) from those that
        came from a plain filter.
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
            <td>
              Total pages. <code>1</code> when no <code>Page</code> was sent
              (<code>0</code> with no rows).
            </td>
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
            <td>
              The requested page of the combined rows, each row once. With{" "}
              <code>Selects</code>, unselected members hold their defaults.
            </td>
          </tr>
          <tr>
            <td>
              <code>QueryString</code>
            </td>
            <td>
              <code>string?</code>
            </td>
            <td>
              Always <code>null</code> here: the <code>Segment</code> overload takes no{" "}
              <code>getQueryString</code> argument and never fills this in.
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
              <Link href="/docs/policies"><code>ApplyPolicy</code></Link>. A
              guarded query sets it as{" "}
              <Link href="/docs/policies/configuration#trace"><code>DwPolicyOptions.IncludeTraceInResult</code></Link>{" "}
              decides: by default it is present under the convenience tier and{" "}
              <code>null</code> under the strict tier (since 3.1.0 — see{" "}
              <Link href="/docs/breaking-changes#strict-trace-off-result">breaking changes</Link>).
              The trace is always on <code>PolicyQueryable&lt;T&gt;.LastTrace</code>.
            </td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn" title="Changed in 3.1.0: the database combines the sets">
        <code>TotalCount</code> counts rows, each once, whether the query tracks,
        runs <code>AsNoTracking()</code> or carries <code>Selects</code>. The sets
        used to be loaded one query each and combined in memory by object
        reference, so an untracked or projected segment counted a row once per
        set in a <code>Union</code>, found nothing to <code>Intersect</code> and
        nothing to <code>Except</code>. <code>Data</code> is now the one page the
        database returns.
      </Callout>

      <Callout tone="warn" title="Changed in 3.1.0: an unpaged segment reports one page">
        Send a <code>Page</code> and <code>PageCount</code> is{" "}
        <code>Ceiling(TotalCount / PageSize)</code>. Send none and it is{" "}
        <code>1</code> — the one page the whole result occupies — or{" "}
        <code>0</code> when nothing matched, the same answer a{" "}
        <Link href="/docs/classes/filter-result"><code>FilterResult&lt;T&gt;</code></Link>{" "}
        gives. Before 3.1.0 an unpaged segment with condition sets left{" "}
        <code>PageCount</code> at <code>0</code> beside a full page of rows, while
        the same request as a filter reported one page per row.{" "}
        <code>PageNumber</code> and <code>PageSize</code> still report{" "}
        <code>0</code> when no page was sent. The exception is a guarded query
        when the deployment sets{" "}
        <Link href="/docs/policies/configuration#caps"><code>DwCaps.DefaultPageSize</code></Link>:
        the query is given page <code>1</code> at that size, and reports it.
      </Callout>

      <Callout tone="info">
        Because <code>SegmentResult&lt;T&gt;</code> inherits <code>FilterResult&lt;T&gt;</code>,
        any helper or extension you write against the base type works for both.
      </Callout>

      <h2 id="csharp-example">C# usage</h2>
      <Code lang="csharp">{`SegmentResult<Customer> result = await dbContext.Customers.ToListAsync(segment);

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
  "queryString": null,
  "policy": null
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
          <Link href="/docs/extensions/to-list-async-segment">ToListAsync&lt;T&gt;(Segment) →</Link>
        </li>
      </ul>
    </DocPage>
  );
}
