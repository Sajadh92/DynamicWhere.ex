import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";

export const metadata: Metadata = {
  title: ".Order<T>(...)",
  description:
    "Sort an IQueryable<T> by one or multiple OrderBy criteria — both single and list overloads.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/extensions/order/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/order">
      <h1>.Order&lt;T&gt;(...)</h1>
      <p>
        Sorts the query by one or multiple{" "}
        <Link href="/docs/classes/order-by"><code>OrderBy</code></Link>{" "}
        criteria. Two overloads are available — one for a single criterion,
        one for a list.
      </p>

      <h2 id="single">Single overload</h2>
      <Code lang="csharp">{`public static IQueryable<T> Order<T>(this IQueryable<T> query, OrderBy order)
    where T : class`}</Code>

      <h2 id="multiple">List overload</h2>
      <Code lang="csharp">{`public static IQueryable<T> Order<T>(this IQueryable<T> query, List<OrderBy> orders)
    where T : class`}</Code>

      <table>
        <thead>
          <tr>
            <th>Parameter</th>
            <th>Type</th>
            <th>Description</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>order</code> / <code>orders</code></td>
            <td>
              <code>OrderBy</code> / <code>List&lt;OrderBy&gt;</code>
            </td>
            <td>
              Sort criteria. Each entry's <code>Sort</code> determines priority
              (lower = first).
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="validations">Validations</h2>
      <ul>
        <li>
          <code>Field</code> must be non-empty and valid on <code>T</code>{" "}
          (case-insensitive, auto-normalized).
        </li>
      </ul>

      <h2 id="returns">Returns</h2>
      <p>
        <code>IQueryable&lt;T&gt;</code> — ordered query.
      </p>

      <h2 id="example-single">Example — single order</h2>
      <Code lang="csharp">{`var ordered = dbContext.Products.Order(new OrderBy
{
    Sort = 1,
    Field = "CreatedAt",
    Direction = Direction.Descending
});`}</Code>

      <Code lang="json">{`{
  "sort": 1,
  "field": "CreatedAt",
  "direction": "Descending"
}`}</Code>

      <h2 id="example-multi">Example — multiple orders</h2>
      <Code lang="csharp">{`var ordered = dbContext.Customers.Order(new List<OrderBy>
{
    new OrderBy { Sort = 1, Field = "LastName",  Direction = Direction.Ascending },
    new OrderBy { Sort = 2, Field = "FirstName", Direction = Direction.Ascending }
});`}</Code>

      <Code lang="json">{`[
  { "sort": 1, "field": "LastName",  "direction": "Ascending" },
  { "sort": 2, "field": "FirstName", "direction": "Ascending" }
]`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/order-by"><code>OrderBy</code></Link>{" "}
          property shape.
        </li>
        <li>
          <Link href="/docs/enums/direction"><code>Direction</code></Link>{" "}
          values.
        </li>
        <li>
          <Link href="/docs/examples/order">JSON Cookbook: Order</Link>.
        </li>
      </ul>
    </DocPage>
  );
}
