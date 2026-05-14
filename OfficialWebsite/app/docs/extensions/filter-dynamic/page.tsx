import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".FilterDynamic<T>(filter)",
  description:
    "Apply a complete Filter (where → order → page → dynamic select) and return a non-generic dynamic IQueryable.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/extensions/filter-dynamic/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/filter-dynamic">
      <h1>.FilterDynamic&lt;T&gt;(filter)</h1>
      <p>
        Applies a complete{" "}
        <Link href="/docs/classes/filter"><code>Filter</code></Link> (
        <code>where → order → page → dynamic select</code>) to a query and
        returns a dynamic <code>IQueryable</code>.
      </p>

      <h2 id="signature">Signature</h2>
      <Code lang="csharp">{`public static IQueryable FilterDynamic<T>(this IQueryable<T> query, Filter filter)
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
        <code>where → order → page → dynamic select</code>.
      </p>

      <Callout tone="warn">
        Ordering and pagination are applied on the strongly-typed{" "}
        <code>IQueryable&lt;T&gt;</code> <strong>before</strong> the dynamic
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
          <Link href="/docs/extensions/select-dynamic">
            <code>.SelectDynamic&lt;T&gt;</code>
          </Link>
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
      </ul>

      <Callout tone="note">
        Unlike{" "}
        <Link href="/docs/extensions/filter"><code>.Filter&lt;T&gt;</code></Link>
        , this method does <strong>not</strong> require a parameterless
        constructor on <code>T</code>.
      </Callout>

      <h2 id="returns">Returns</h2>
      <p>
        <code>IQueryable</code> — dynamic composed query whose elements are
        anonymous objects. Projection rules follow{" "}
        <Link href="/docs/extensions/select-dynamic">
          <code>SelectDynamic</code>
        </Link>{" "}
        — non-dotted paths are projected as-is (whole object or collection);
        dotted paths through reference navigations produce nested dynamic
        objects (<code>Category: {`{ Name: "..." }`}</code>); dotted paths
        through collection navigations use <code>Select</code> lambdas to
        project individual element fields (
        <code>Category: {`{ Vendors: [{ Id: … }] }`}</code>).
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

IQueryable query = dbContext.Products.FilterDynamic(filter);`}</Code>

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
          <Link href="/docs/extensions/filter"><code>.Filter&lt;T&gt;</code></Link>{" "}
          — strongly-typed variant.
        </li>
        <li>
          <Link href="/docs/extensions/to-list-dynamic-filter">
            <code>.ToListDynamic&lt;T&gt;(Filter)</code>
          </Link>{" "}
          /{" "}
          <Link href="/docs/extensions/to-list-async-dynamic-filter">
            <code>.ToListAsyncDynamic&lt;T&gt;(Filter)</code>
          </Link>{" "}
          to materialize.
        </li>
        <li>
          <Link href="/docs/examples/filter-dynamic">
            JSON Cookbook: FilterDynamic
          </Link>
          .
        </li>
      </ul>
    </DocPage>
  );
}
