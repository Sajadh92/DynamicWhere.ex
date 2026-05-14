import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".ToList<T>(Filter)",
  description:
    "Materialize a Filter against an IQueryable<T> or IEnumerable<T> and return a paginated FilterResult<T>.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/extensions/to-list-filter/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/to-list-filter">
      <h1>.ToList&lt;T&gt;(Filter)</h1>
      <p>
        Materializes a{" "}
        <Link href="/docs/classes/filter"><code>Filter</code></Link> and returns
        a{" "}
        <Link href="/docs/classes/filter-result"><code>FilterResult&lt;T&gt;</code></Link>{" "}
        with pagination metadata. Two overloads are available — one for{" "}
        <code>IQueryable&lt;T&gt;</code> (typical EF Core usage) and one for{" "}
        <code>IEnumerable&lt;T&gt;</code> (in-memory pipelines and tests).
      </p>

      <h2 id="queryable">IQueryable&lt;T&gt; overload</h2>
      <Code lang="csharp">{`public static FilterResult<T> ToList<T>(
    this IQueryable<T> query,
    Filter filter,
    bool getQueryString = false)
    where T : class, new()`}</Code>

      <h2 id="enumerable">IEnumerable&lt;T&gt; overload</h2>
      <p>
        In-memory variant — wraps the collection with <code>AsQueryable()</code>{" "}
        then delegates to the typed overload.
      </p>
      <Code lang="csharp">{`public static FilterResult<T> ToList<T>(
    this IEnumerable<T> source,
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
        <li><code>CountAsync</code>-equivalent on the typed query → <code>TotalCount</code>.</li>
        <li><code>Order</code> applied on the typed query.</li>
        <li><code>Page</code> applied on the typed query.</li>
        <li><code>Select</code> projection applied last.</li>
        <li><code>ToList()</code> materializes the result.</li>
      </ul>

      <Callout tone="warn">
        Ordering and pagination are applied on the strongly-typed{" "}
        <code>IQueryable&lt;T&gt;</code> <strong>before</strong> the select
        projection so that field names referenced in <code>orders</code> always
        resolve against the original entity type <code>T</code>.
      </Callout>

      <Callout tone="note">
        Passing <code>getQueryString: true</code> calls{" "}
        <code>.ToQueryString()</code>, which requires an active EF Core
        database provider. Pure in-memory <code>IEnumerable&lt;T&gt;</code>{" "}
        usage may not support this.
      </Callout>

      <h2 id="returns">Returns</h2>
      <p>
        <Link href="/docs/classes/filter-result">
          <code>FilterResult&lt;T&gt;</code>
        </Link>{" "}
        with <code>PageNumber</code>, <code>PageSize</code>,{" "}
        <code>PageCount</code>, <code>TotalCount</code>, <code>Data</code>, and
        optional <code>QueryString</code>.
      </p>

      <h2 id="example">Example</h2>
      <Code lang="csharp">{`FilterResult<Product> result = dbContext.Products.ToList(filter);

Console.WriteLine($"page {result.PageNumber} of {result.PageCount}");
Console.WriteLine($"{result.TotalCount} total matches");
foreach (var p in result.Data)
{
    Console.WriteLine($"- {p.Name}");
}`}</Code>

      <p><strong>In-memory variant.</strong></p>
      <Code lang="csharp">{`List<Product> source = LoadFromCsv();
FilterResult<Product> result = source.ToList(filter);`}</Code>

      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 10,
  "pageCount": 5,
  "totalCount": 42,
  "data": [
    { "id": 7, "name": "Laptop Pro", "price": 1299.99, "category": { "name": "Electronics" } }
  ],
  "queryString": null
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/to-list-async-filter">
            <code>.ToListAsync&lt;T&gt;(Filter)</code>
          </Link>{" "}
          — async EF Core variant.
        </li>
        <li>
          <Link href="/docs/extensions/to-list-dynamic-filter">
            <code>.ToListDynamic&lt;T&gt;(Filter)</code>
          </Link>{" "}
          — dynamic projection variant.
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
