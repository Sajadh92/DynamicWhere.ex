import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Extension Methods — IQueryable & IEnumerable LINQ extensions",
  description:
    "Every IQueryable<T> and IEnumerable<T> extension method DynamicWhere.ex adds — Select, SelectDynamic, Where, Group, Order, Page, Filter, Summary, ToList, ToListAsync, and the dynamic variants. Build LINQ pipelines on EF Core from JSON.",
  keywords: [
    "DynamicWhere.ex extension methods",
    "IQueryable extension EF Core",
    "dynamic LINQ extensions",
    "ToListAsync dynamic filter",
    ".Where IQueryable JSON",
  ],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/extensions/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions">
      <h1>Extension Methods</h1>
      <p>
        Every extension method lives in{" "}
        <code>DynamicWhere.ex.Source.Extension</code> and operates on{" "}
        <code>IQueryable&lt;T&gt;</code> — with <code>IEnumerable&lt;T&gt;</code>{" "}
        overloads for in-memory variants. The methods fall into four intents:
        projection, filtering, composition, and materialization.
      </p>

      <h2 id="projection">Projection</h2>
      <p>
        Project a query into a typed shape or a dynamic shape. Both support
        direct properties, whole navigation objects, whole collections, and
        dotted paths through reference and collection navigations.
      </p>
      <table>
        <thead>
          <tr>
            <th>Method</th>
            <th>Signature</th>
            <th>Returns</th>
            <th>Async</th>
            <th>Link</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Select</code></td>
            <td><code>.Select&lt;T&gt;(List&lt;string&gt; fields)</code></td>
            <td><code>IQueryable&lt;T&gt;</code></td>
            <td>No</td>
            <td><Link href="/docs/extensions/select">Docs</Link></td>
          </tr>
          <tr>
            <td><code>SelectDynamic</code></td>
            <td><code>.SelectDynamic&lt;T&gt;(List&lt;string&gt; fields)</code></td>
            <td><code>IQueryable</code></td>
            <td>No</td>
            <td><Link href="/docs/extensions/select-dynamic">Docs</Link></td>
          </tr>
        </tbody>
      </table>

      <h2 id="filtering">Filtering</h2>
      <p>
        Build the where-clause, group, order, or paginate the query — one
        building block at a time.
      </p>
      <table>
        <thead>
          <tr>
            <th>Method</th>
            <th>Signature</th>
            <th>Returns</th>
            <th>Async</th>
            <th>Link</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Where</code></td>
            <td><code>.Where&lt;T&gt;(Condition condition)</code></td>
            <td><code>IQueryable&lt;T&gt;</code></td>
            <td>No</td>
            <td><Link href="/docs/extensions/where">Docs</Link></td>
          </tr>
          <tr>
            <td><code>Where</code></td>
            <td><code>.Where&lt;T&gt;(ConditionGroup group)</code></td>
            <td><code>IQueryable&lt;T&gt;</code></td>
            <td>No</td>
            <td><Link href="/docs/extensions/where">Docs</Link></td>
          </tr>
          <tr>
            <td><code>Group</code></td>
            <td><code>.Group&lt;T&gt;(GroupBy groupBy)</code></td>
            <td><code>IQueryable</code></td>
            <td>No</td>
            <td><Link href="/docs/extensions/group">Docs</Link></td>
          </tr>
          <tr>
            <td><code>Order</code></td>
            <td>
              <code>.Order&lt;T&gt;(OrderBy)</code> /{" "}
              <code>.Order&lt;T&gt;(List&lt;OrderBy&gt;)</code>
            </td>
            <td><code>IQueryable&lt;T&gt;</code></td>
            <td>No</td>
            <td><Link href="/docs/extensions/order">Docs</Link></td>
          </tr>
          <tr>
            <td><code>Page</code></td>
            <td><code>.Page&lt;T&gt;(PageBy page)</code></td>
            <td><code>IQueryable&lt;T&gt;</code></td>
            <td>No</td>
            <td><Link href="/docs/extensions/page">Docs</Link></td>
          </tr>
        </tbody>
      </table>

      <h2 id="composition">Composition</h2>
      <p>
        Apply a complete <Link href="/docs/classes/filter"><code>Filter</code></Link>{" "}
        or <Link href="/docs/classes/summary"><code>Summary</code></Link>{" "}
        composition to the query. Each returns an <code>IQueryable</code> so you
        can chain further operations before materializing.
      </p>
      <table>
        <thead>
          <tr>
            <th>Method</th>
            <th>Signature</th>
            <th>Returns</th>
            <th>Async</th>
            <th>Link</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Filter</code></td>
            <td><code>.Filter&lt;T&gt;(Filter filter)</code></td>
            <td><code>IQueryable&lt;T&gt;</code></td>
            <td>No</td>
            <td><Link href="/docs/extensions/filter">Docs</Link></td>
          </tr>
          <tr>
            <td><code>FilterDynamic</code></td>
            <td><code>.FilterDynamic&lt;T&gt;(Filter filter)</code></td>
            <td><code>IQueryable</code></td>
            <td>No</td>
            <td><Link href="/docs/extensions/filter-dynamic">Docs</Link></td>
          </tr>
          <tr>
            <td><code>Summary</code></td>
            <td><code>.Summary&lt;T&gt;(Summary summary)</code></td>
            <td><code>IQueryable</code></td>
            <td>No</td>
            <td><Link href="/docs/extensions/summary">Docs</Link></td>
          </tr>
        </tbody>
      </table>

      <h2 id="materialization">Materialization</h2>
      <p>
        Execute the composed query and return a paginated result — typed,
        dynamic, summary, or segment. The async overloads call EF Core's{" "}
        <code>CountAsync</code> and <code>ToListAsync</code> /{" "}
        <code>ToDynamicListAsync</code> under the hood.
      </p>
      <table>
        <thead>
          <tr>
            <th>Method</th>
            <th>Signature</th>
            <th>Returns</th>
            <th>Async</th>
            <th>Link</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>ToList</code> (Filter)</td>
            <td><code>.ToList&lt;T&gt;(Filter, bool getQueryString = false)</code></td>
            <td><code>FilterResult&lt;T&gt;</code></td>
            <td>No</td>
            <td><Link href="/docs/extensions/to-list-filter">Docs</Link></td>
          </tr>
          <tr>
            <td><code>ToListAsync</code> (Filter)</td>
            <td><code>.ToListAsync&lt;T&gt;(Filter, bool getQueryString = false)</code></td>
            <td><code>Task&lt;FilterResult&lt;T&gt;&gt;</code></td>
            <td>Yes</td>
            <td><Link href="/docs/extensions/to-list-async-filter">Docs</Link></td>
          </tr>
          <tr>
            <td><code>ToListDynamic</code> (Filter)</td>
            <td><code>.ToListDynamic&lt;T&gt;(Filter, bool getQueryString = false)</code></td>
            <td><code>FilterResult&lt;dynamic&gt;</code></td>
            <td>No</td>
            <td><Link href="/docs/extensions/to-list-dynamic-filter">Docs</Link></td>
          </tr>
          <tr>
            <td><code>ToListAsyncDynamic</code> (Filter)</td>
            <td><code>.ToListAsyncDynamic&lt;T&gt;(Filter, bool getQueryString = false)</code></td>
            <td><code>Task&lt;FilterResult&lt;dynamic&gt;&gt;</code></td>
            <td>Yes</td>
            <td><Link href="/docs/extensions/to-list-async-dynamic-filter">Docs</Link></td>
          </tr>
          <tr>
            <td><code>ToList</code> (Summary)</td>
            <td><code>.ToList&lt;T&gt;(Summary, bool getQueryString = false)</code></td>
            <td><code>SummaryResult</code></td>
            <td>No</td>
            <td><Link href="/docs/extensions/to-list-summary">Docs</Link></td>
          </tr>
          <tr>
            <td><code>ToListAsync</code> (Summary)</td>
            <td><code>.ToListAsync&lt;T&gt;(Summary, bool getQueryString = false)</code></td>
            <td><code>Task&lt;SummaryResult&gt;</code></td>
            <td>Yes</td>
            <td><Link href="/docs/extensions/to-list-async-summary">Docs</Link></td>
          </tr>
          <tr>
            <td><code>ToListAsync</code> (Segment)</td>
            <td><code>.ToListAsync&lt;T&gt;(Segment segment)</code></td>
            <td><code>Task&lt;SegmentResult&lt;T&gt;&gt;</code></td>
            <td>Yes (only)</td>
            <td><Link href="/docs/extensions/to-list-async-segment">Docs</Link></td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn">
        Segment operations are <strong>async-only</strong>. There is no
        synchronous <code>ToList&lt;T&gt;(Segment)</code> variant. Each{" "}
        <code>ConditionSet</code> is materialized independently into memory,
        then set operations (<code>Union</code> / <code>Intersect</code> /{" "}
        <code>Except</code>) execute in-memory before ordering and pagination.
      </Callout>

      <h2 id="in-memory">In-memory overloads</h2>
      <p>
        The <code>ToList</code> / <code>ToListDynamic</code> (Filter and
        Summary) extensions also expose <code>IEnumerable&lt;T&gt;</code>{" "}
        overloads. These wrap the collection with{" "}
        <code>AsQueryable()</code> and delegate to the typed overload — handy
        for unit tests and in-process pipelines.
      </p>

      <Callout tone="note">
        Pass <code>getQueryString: true</code> to any materialization call to
        capture the generated SQL on the result. This requires an active EF
        Core provider; pure <code>IEnumerable&lt;T&gt;</code> use may not
        support <code>.ToQueryString()</code>.
      </Callout>

      <h2 id="next">Next steps</h2>
      <ul>
        <li>
          <Link href="/docs/classes/filter"><code>Filter</code></Link> — the JSON
          shape that drives the typed pipeline.
        </li>
        <li>
          <Link href="/docs/classes/summary"><code>Summary</code></Link> — the
          aggregate-reporting pipeline.
        </li>
        <li>
          <Link href="/docs/classes/segment"><code>Segment</code></Link> — set
          operations across multiple condition sets.
        </li>
      </ul>
    </DocPage>
  );
}
