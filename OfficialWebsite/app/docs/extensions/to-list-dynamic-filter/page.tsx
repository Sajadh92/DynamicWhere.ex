import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".ToListDynamic<T>(Filter)",
  description:
    "Materialize a Filter using SelectDynamic and return a FilterResult<dynamic>. Includes an IEnumerable<T> overload.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/to-list-dynamic-filter">
      <h1>.ToListDynamic&lt;T&gt;(Filter)</h1>
      <p>
        Materializes a{" "}
        <Link href="/docs/classes/filter"><code>Filter</code></Link> using{" "}
        <Link href="/docs/extensions/select-dynamic">
          <code>SelectDynamic&lt;T&gt;</code>
        </Link>{" "}
        and returns a <code>FilterResult&lt;dynamic&gt;</code> with pagination
        metadata. Where and count run on the typed query; ordering, pagination,
        and projection all happen before materialisation.
      </p>

      <h2 id="queryable">IQueryable&lt;T&gt; overload</h2>
      <Code lang="csharp">{`public static FilterResult<dynamic> ToListDynamic<T>(
    this IQueryable<T> query,
    Filter filter,
    bool getQueryString = false)
    where T : class`}</Code>

      <h2 id="enumerable">IEnumerable&lt;T&gt; overload</h2>
      <p>
        In-memory variant — wraps the collection with <code>AsQueryable()</code>{" "}
        then delegates to the <code>IQueryable&lt;T&gt;</code> overload.
      </p>
      <Code lang="csharp">{`public static FilterResult<dynamic> ToListDynamic<T>(
    this IEnumerable<T> source,
    Filter filter,
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
              <code>QueryString</code>
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="pipeline">Pipeline</h2>
      <ul>
        <li><code>Where</code> applied on the typed query.</li>
        <li><code>Count</code> on the typed query → <code>TotalCount</code>.</li>
        <li><code>Order</code> applied on the typed query.</li>
        <li><code>Page</code> applied on the typed query.</li>
        <li>
          <code>SelectDynamic</code> projection applied last → produces a
          dynamic <code>IQueryable</code>.
        </li>
        <li>Materialized as <code>List&lt;dynamic&gt;</code>.</li>
      </ul>

      <Callout tone="warn">
        Ordering and pagination are applied on the strongly-typed{" "}
        <code>IQueryable&lt;T&gt;</code> <strong>before</strong> the dynamic
        projection so that field names referenced in <code>orders</code> always
        resolve against the original entity type <code>T</code>.
      </Callout>

      <Callout tone="note">
        Property names in the dynamic result follow{" "}
        <Link href="/docs/extensions/select-dynamic">
          <code>SelectDynamic</code>
        </Link>{" "}
        rules: non-dotted paths are projected as-is; dotted paths through
        reference navigations produce nested dynamic objects (e.g.,{" "}
        <code>result.Category.Name</code>); dotted paths through collection
        navigations produce nested collections (
        <code>result.Category.Vendors[0].Id</code>).
      </Callout>

      <h2 id="returns">Returns</h2>
      <p>
        <code>FilterResult&lt;dynamic&gt;</code> — same shape as{" "}
        <Link href="/docs/classes/filter-result">
          <code>FilterResult&lt;T&gt;</code>
        </Link>{" "}
        but <code>Data</code> is <code>List&lt;dynamic&gt;</code>.
      </p>

      <h2 id="example">Example</h2>
      <Code lang="csharp">{`FilterResult<dynamic> result = dbContext.Products.ToListDynamic(filter);

foreach (var p in result.Data)
{
    // Access nested dynamic object
    Console.WriteLine($"{p.Name} — {p.Category.Name}");
}`}</Code>

      <p><strong>In-memory variant.</strong></p>
      <Code lang="csharp">{`List<Product> source = LoadFromCsv();
FilterResult<dynamic> result = source.ToListDynamic(filter);`}</Code>

      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 10,
  "pageCount": 5,
  "totalCount": 42,
  "data": [
    { "Id": 7, "Name": "Laptop Pro", "Price": 1299.99, "Category": { "Name": "Electronics" } }
  ],
  "queryString": null
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/to-list-async-dynamic-filter">
            <code>.ToListAsyncDynamic&lt;T&gt;(Filter)</code>
          </Link>{" "}
          — async EF Core variant.
        </li>
        <li>
          <Link href="/docs/extensions/to-list-filter">
            <code>.ToList&lt;T&gt;(Filter)</code>
          </Link>{" "}
          — strongly-typed variant.
        </li>
        <li>
          <Link href="/docs/extensions/filter-dynamic">
            <code>.FilterDynamic&lt;T&gt;</code>
          </Link>{" "}
          — non-materializing composition.
        </li>
      </ul>
    </DocPage>
  );
}
