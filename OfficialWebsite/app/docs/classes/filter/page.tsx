import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Filter",
  description:
    "The most common top-level shape — combines a where-clause, projection, sort, and pagination in a single object.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/classes/filter/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/classes/filter">
      <h1>Filter</h1>
      <p>
        A <code>Filter</code> is the most common top-level shape. It combines filtering,
        projection, ordering, and pagination in a single object, and is the input to{" "}
        <Link href="/docs/extensions/to-list-async-filter"><code>ToListAsyncFilter</code></Link>{" "}
        and its dynamic / sync siblings.
      </p>

      <h2 id="properties">Properties</h2>
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
              <code>ConditionGroup</code>
            </td>
            <td>
              <Link href="/docs/classes/condition-group"><code>ConditionGroup?</code></Link>
            </td>
            <td>Optional where-clause.</td>
          </tr>
          <tr>
            <td>
              <code>Selects</code>
            </td>
            <td>
              <code>List&lt;string&gt;?</code>
            </td>
            <td>Optional field projection (like SQL <code>SELECT col1, col2</code>).</td>
          </tr>
          <tr>
            <td>
              <code>Orders</code>
            </td>
            <td>
              <code>List&lt;<Link href="/docs/classes/order-by">OrderBy</Link>&gt;?</code>
            </td>
            <td>Optional sort criteria.</td>
          </tr>
          <tr>
            <td>
              <code>Page</code>
            </td>
            <td>
              <Link href="/docs/classes/page-by"><code>PageBy?</code></Link>
            </td>
            <td>Optional pagination.</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info" title="Order of operations">
        Internally the pipeline is <code>where → order → page → select</code>. Ordering and paging
        run on the full entity, then projection is applied to the trimmed page — so you can sort by
        a field you don't include in <code>Selects</code>.
      </Callout>

      <h2 id="csharp-example">C# example</h2>
      <Code lang="csharp">{`var filter = new Filter
{
    ConditionGroup = new ConditionGroup
    {
        Connector = Connector.And,
        Conditions = new List<Condition>
        {
            new Condition
            {
                Sort = 1,
                Field = "IsActive",
                DataType = DataType.Boolean,
                Operator = Operator.Equal,
                Values = new List<object> { true }
            }
        }
    },
    Selects = new List<string> { "Id", "Name", "Email" },
    Orders = new List<OrderBy>
    {
        new OrderBy { Sort = 1, Field = "CreatedAt", Direction = Direction.Descending }
    },
    Page = new PageBy { PageNumber = 1, PageSize = 25 }
};

FilterResult<Customer> result = await dbContext.Customers.ToListAsync(filter);`}</Code>

      <h2 id="json-example">JSON example</h2>
      <Code lang="json">{`{
  "conditionGroup": {
    "connector": "And",
    "conditions": [
      { "sort": 1, "field": "IsActive", "dataType": "Boolean", "operator": "Equal", "values": [true] }
    ],
    "subConditionGroups": []
  },
  "selects": ["Id", "Name", "Email"],
  "orders": [
    { "sort": 1, "field": "CreatedAt", "direction": "Descending" }
  ],
  "page": { "pageNumber": 1, "pageSize": 25 }
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/filter-result">FilterResult&lt;T&gt; →</Link> the return shape.
        </li>
        <li>
          <Link href="/docs/extensions/to-list-async-filter">ToListAsyncFilter →</Link>
        </li>
        <li>
          <Link href="/docs/extensions/to-list-async-dynamic-filter">ToListAsyncDynamicFilter →</Link> for projection to <code>dynamic</code>.
        </li>
        <li>
          <Link href="/docs/examples/filter">Filter example →</Link>
        </li>
      </ul>
    </DocPage>
  );
}
