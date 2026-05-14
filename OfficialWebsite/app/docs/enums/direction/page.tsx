import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Direction",
  description:
    "Direction enum — the sort direction (Ascending / Descending) used by an OrderBy.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/enums/direction/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/enums/direction">
      <h1>Direction</h1>
      <p>
        <code>Direction</code> is the sort direction for an{" "}
        <Link href="/docs/classes/order-by"><code>OrderBy</code></Link>. It maps
        directly onto LINQ's <code>OrderBy</code> /{" "}
        <code>OrderByDescending</code> and SQL's <code>ASC</code> /{" "}
        <code>DESC</code>.
      </p>

      <h2 id="values">Values</h2>
      <table>
        <thead>
          <tr>
            <th>Value</th>
            <th>Description</th>
            <th>LINQ equivalent</th>
            <th>SQL equivalent</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Ascending</code></td>
            <td>Sort A → Z, 0 → 9, oldest → newest.</td>
            <td><code>OrderBy(x =&gt; x.Field)</code></td>
            <td><code>ORDER BY Field ASC</code></td>
          </tr>
          <tr>
            <td><code>Descending</code></td>
            <td>Sort Z → A, 9 → 0, newest → oldest.</td>
            <td><code>OrderByDescending(x =&gt; x.Field)</code></td>
            <td><code>ORDER BY Field DESC</code></td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        <code>Ascending</code> is the default if you build an{" "}
        <code>OrderBy</code> in C# without setting <code>Direction</code>{" "}
        explicitly.
      </Callout>

      <h2 id="json-single">JSON — single order</h2>
      <Code lang="json">{`{
  "sort": 1,
  "field": "CreatedAt",
  "direction": "Descending"
}`}</Code>

      <h2 id="json-multiple">JSON — multiple orders</h2>
      <p>
        Multiple <code>OrderBy</code> entries are applied in <code>Sort</code>{" "}
        order (lower runs first, becoming the primary sort key).
      </p>
      <Code lang="json">{`[
  { "sort": 1, "field": "LastName",  "direction": "Ascending" },
  { "sort": 2, "field": "FirstName", "direction": "Ascending" },
  { "sort": 3, "field": "CreatedAt", "direction": "Descending" }
]`}</Code>

      <p>Equivalent SQL (illustrative):</p>
      <Code lang="bash">{`ORDER BY LastName ASC, FirstName ASC, CreatedAt DESC`}</Code>

      <h2 id="csharp">C# usage</h2>
      <Code lang="csharp">{`using DynamicWhere.ex.Enums;

var orders = new List<OrderBy>
{
    new OrderBy { Sort = 1, Field = "LastName",  Direction = Direction.Ascending },
    new OrderBy { Sort = 2, Field = "FirstName", Direction = Direction.Ascending },
    new OrderBy { Sort = 3, Field = "CreatedAt", Direction = Direction.Descending },
};

var query = db.Customers.Order(orders);`}</Code>

      <h2 id="order-extension">Used by</h2>
      <ul>
        <li>
          <Link href="/docs/classes/order-by"><code>OrderBy</code></Link>{" "}
          — the class that carries <code>Direction</code>.
        </li>
        <li>
          <Link href="/docs/extensions/order">.Order&lt;T&gt; →</Link> the
          extension method that applies an <code>OrderBy</code> (or a list of
          them) to an <code>IQueryable</code>.
        </li>
        <li>
          <Link href="/docs/classes/filter"><code>Filter</code></Link>,{" "}
          <Link href="/docs/classes/segment"><code>Segment</code></Link>, and{" "}
          <Link href="/docs/classes/summary"><code>Summary</code></Link> — all
          expose an <code>Orders</code> list.
        </li>
      </ul>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/examples/order">Order example →</Link> end-to-end
          JSON cookbook.
        </li>
        <li>
          <Link href="/docs/classes/page-by">PageBy →</Link> paginate the
          ordered result.
        </li>
      </ul>
    </DocPage>
  );
}
