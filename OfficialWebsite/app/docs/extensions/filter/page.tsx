import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".Filter<T>(filter)",
  description:
    "Apply a complete Filter (where → order → page → select) to an IQueryable<T> in a single call.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/filter">
      <h1>.Filter&lt;T&gt;(filter)</h1>
      <p>
        Applies a complete{" "}
        <Link href="/docs/classes/filter"><code>Filter</code></Link> (
        <code>where → order → page → select</code>) to a query.
      </p>

      <h2 id="signature">Signature</h2>
      <Code lang="csharp">{`public static IQueryable<T> Filter<T>(this IQueryable<T> query, Filter filter)
    where T : class, new()`}</Code>

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
            <td><code>filter</code></td>
            <td>
              <Link href="/docs/classes/filter"><code>Filter</code></Link>
            </td>
            <td>
              Composition object — <code>ConditionGroup</code>,{" "}
              <code>Selects</code>, <code>Orders</code>, <code>Page</code>
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="composition">Composition order</h2>
      <p>
        The pipeline is applied in this order:{" "}
        <code>where → order → page → select</code>.
      </p>

      <Callout tone="warn">
        Ordering and pagination are applied on the strongly-typed{" "}
        <code>IQueryable&lt;T&gt;</code> <strong>before</strong> the select
        projection so that original field names referenced in{" "}
        <code>orders</code> always resolve against the original entity type{" "}
        <code>T</code>, regardless of which fields are projected.
      </Callout>

      <h2 id="validations">Validations</h2>
      <ul>
        <li>
          <code>ConditionGroup</code> (if provided) is validated as in{" "}
          <Link href="/docs/extensions/where"><code>.Where&lt;T&gt;</code></Link>
          .
        </li>
        <li>
          <code>Selects</code> (if provided) is validated as in{" "}
          <Link href="/docs/extensions/select"><code>.Select&lt;T&gt;</code></Link>
          .
        </li>
        <li>
          <code>Orders</code> (if provided): each <code>Field</code> must be
          non-empty and valid on <code>T</code>.
        </li>
        <li>
          <code>Page</code> (if provided): both <code>PageNumber</code> and{" "}
          <code>PageSize</code> must be &gt; 0.
        </li>
        <li>
          Because select uses{" "}
          <Link href="/docs/extensions/select"><code>.Select&lt;T&gt;</code></Link>
          , <code>T</code> must have a parameterless constructor.
        </li>
      </ul>

      <h2 id="returns">Returns</h2>
      <p>
        <code>IQueryable&lt;T&gt;</code> — composed query. You can chain
        further LINQ operators or call <code>ToList()</code> /{" "}
        <code>ToListAsync()</code> to materialize.
      </p>

      <h2 id="example">Example</h2>
      <Code lang="csharp">{`var filter = new Filter
{
    ConditionGroup = new ConditionGroup
    {
        Connector = Connector.And,
        Conditions = new List<Condition>
        {
            new Condition { Sort = 1, Field = "Price",         DataType = DataType.Number, Operator = Operator.GreaterThan, Values = new List<object> { 50 } },
            new Condition { Sort = 2, Field = "Category.Name", DataType = DataType.Text,   Operator = Operator.IEqual,      Values = new List<object> { "electronics" } }
        }
    },
    Selects = new List<string> { "Id", "Name", "Price", "Category.Name" },
    Orders  = new List<OrderBy> { new OrderBy { Sort = 1, Field = "Price", Direction = Direction.Descending } },
    Page    = new PageBy { PageNumber = 1, PageSize = 10 }
};

IQueryable<Product> query = dbContext.Products.Filter(filter);`}</Code>

      <Code lang="json">{`{
  "conditionGroup": {
    "connector": "And",
    "conditions": [
      { "sort": 1, "field": "Price",         "dataType": "Number", "operator": "GreaterThan", "values": ["50"] },
      { "sort": 2, "field": "Category.Name", "dataType": "Text",   "operator": "IEqual",      "values": ["electronics"] }
    ],
    "subConditionGroups": []
  },
  "selects": ["Id", "Name", "Price", "Category.Name"],
  "orders": [
    { "sort": 1, "field": "Price", "direction": "Descending" }
  ],
  "page": { "pageNumber": 1, "pageSize": 10 }
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/filter-dynamic">
            <code>.FilterDynamic&lt;T&gt;</code>
          </Link>{" "}
          — same composition but with dynamic projection.
        </li>
        <li>
          <Link href="/docs/extensions/to-list-filter">
            <code>.ToList&lt;T&gt;(Filter)</code>
          </Link>{" "}
          /{" "}
          <Link href="/docs/extensions/to-list-async-filter">
            <code>.ToListAsync&lt;T&gt;(Filter)</code>
          </Link>{" "}
          to materialize.
        </li>
        <li>
          <Link href="/docs/examples/filter">JSON Cookbook: Filter</Link>.
        </li>
      </ul>
    </DocPage>
  );
}
