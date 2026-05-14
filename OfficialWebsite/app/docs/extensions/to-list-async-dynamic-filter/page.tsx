import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".ToListAsyncDynamic<T>(Filter)",
  description:
    "Async EF Core entry — materialize a Filter using SelectDynamic and ToDynamicListAsync, returning Task<FilterResult<dynamic>>.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/extensions/to-list-async-dynamic-filter/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/to-list-async-dynamic-filter">
      <h1>.ToListAsyncDynamic&lt;T&gt;(Filter)</h1>
      <p>
        Async version of{" "}
        <Link href="/docs/extensions/to-list-dynamic-filter">
          <code>.ToListDynamic&lt;T&gt;(Filter)</code>
        </Link>
        . Uses EF Core's <code>CountAsync()</code> and{" "}
        <code>ToDynamicListAsync()</code> under the hood.
      </p>

      <h2 id="signature">Signature</h2>
      <Code lang="csharp">{`public static Task<FilterResult<dynamic>> ToListAsyncDynamic<T>(
    this IQueryable<T> query,
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
        <li>
          <code>CountAsync()</code> on the typed query → <code>TotalCount</code>.
        </li>
        <li><code>Order</code> applied on the typed query.</li>
        <li><code>Page</code> applied on the typed query.</li>
        <li><code>SelectDynamic</code> projection applied last.</li>
        <li><code>ToDynamicListAsync()</code> materializes the result.</li>
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
        rules — see that page for the full set of access patterns through
        nested dynamic objects and collections.
      </Callout>

      <h2 id="returns">Returns</h2>
      <p>
        <code>Task&lt;FilterResult&lt;dynamic&gt;&gt;</code>.
      </p>

      <h2 id="example">Example</h2>
      <Code lang="csharp">{`FilterResult<dynamic> result = await dbContext.Products.ToListAsyncDynamic(filter);

foreach (var p in result.Data)
{
    Console.WriteLine($"{p.Name} — {p.Category.Name}");
}`}</Code>

      <Code lang="csharp">{`var result = await dbContext.Products.ToListAsyncDynamic(filter, getQueryString: true);
Console.WriteLine(result.QueryString);`}</Code>

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
          <Link href="/docs/extensions/to-list-dynamic-filter">
            <code>.ToListDynamic&lt;T&gt;(Filter)</code>
          </Link>{" "}
          — synchronous variant (and in-memory overload).
        </li>
        <li>
          <Link href="/docs/extensions/to-list-async-filter">
            <code>.ToListAsync&lt;T&gt;(Filter)</code>
          </Link>{" "}
          — async typed variant.
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
