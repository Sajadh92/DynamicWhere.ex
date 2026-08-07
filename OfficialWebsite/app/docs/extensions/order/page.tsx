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
        <li>
          <code>Field</code> may not end on a collection of entities or other
          complex types — there is no single value to compare.
        </li>
      </ul>

      <h2 id="returns">Returns</h2>
      <p>
        <code>IQueryable&lt;T&gt;</code> — ordered query.
      </p>

      <h2 id="collection-paths">Collection paths</h2>
      <p>
        A sort needs one comparable value per row, so a path that crosses a
        collection navigation cannot be emitted verbatim —{" "}
        <code>List&lt;Tag&gt;</code> has no <code>Value</code> member. Each
        collection segment is reduced with an aggregate instead:{" "}
        <strong>
          <code>Min</code> when sorting ascending, <code>Max</code> when sorting
          descending
        </strong>{" "}
        — rows are ordered by their best matching element in the requested
        direction.
      </p>

      <table>
        <thead>
          <tr>
            <th>Field</th>
            <th>Direction</th>
            <th>Generated expression</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Category.Name</code></td>
            <td>Ascending</td>
            <td><code>Category.Name asc</code></td>
          </tr>
          <tr>
            <td><code>Tags.Value</code></td>
            <td>Ascending</td>
            <td><code>Tags.Min(Value) asc</code></td>
          </tr>
          <tr>
            <td><code>Tags.Value</code></td>
            <td>Descending</td>
            <td><code>Tags.Max(Value) desc</code></td>
          </tr>
          <tr>
            <td><code>OrderItems.Product.Name</code></td>
            <td>Ascending</td>
            <td><code>OrderItems.Min(Product.Name) asc</code></td>
          </tr>
          <tr>
            <td><code>OrderItems.UnitPrice</code></td>
            <td>Ascending</td>
            <td>
              <code>
                OrderItems.Select(UnitPrice).DefaultIfEmpty().Min() asc
              </code>
            </td>
          </tr>
        </tbody>
      </table>

      <p>
        Rows whose collection is empty have nothing to sort by. Reference and
        nullable types yield <code>null</code>; non-nullable value types
        (<code>int</code>, <code>decimal</code>, <code>DateTime</code>, …) use{" "}
        <code>DefaultIfEmpty()</code> and yield the type default, which keeps
        in-memory sorting from throwing{" "}
        <code>Sequence contains no elements</code>.
      </p>

      <Code lang="csharp">{`// Products sorted by their cheapest line item, cheapest product first
var ordered = dbContext.Orders.Order(new OrderBy
{
    Sort = 1,
    Field = "OrderItems.UnitPrice",
    Direction = Direction.Ascending
});`}</Code>

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
