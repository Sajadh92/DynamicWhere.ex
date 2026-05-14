import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Summary",
  description:
    "Combines filtering, grouping, having, ordering, and pagination for aggregate reporting.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/classes/summary/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/classes/summary">
      <h1>Summary</h1>
      <p>
        A <code>Summary</code> is the shape for aggregate reporting. It runs the full pipeline
        <code> where → group → having → order → page</code> and returns dynamic rows of group keys
        plus aggregate alias values.
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
            <td>Optional where-clause (pre-grouping).</td>
          </tr>
          <tr>
            <td>
              <code>GroupBy</code>
            </td>
            <td>
              <Link href="/docs/classes/group-by"><code>GroupBy?</code></Link>
            </td>
            <td>
              <strong>Required.</strong> Grouping and aggregation config.
            </td>
          </tr>
          <tr>
            <td>
              <code>Having</code>
            </td>
            <td>
              <Link href="/docs/classes/condition-group"><code>ConditionGroup?</code></Link>
            </td>
            <td>
              Optional post-group filter. Each condition's <code>Field</code> must reference an{" "}
              <code>AggregateBy.Alias</code>.
            </td>
          </tr>
          <tr>
            <td>
              <code>Orders</code>
            </td>
            <td>
              <code>List&lt;<Link href="/docs/classes/order-by">OrderBy</Link>&gt;?</code>
            </td>
            <td>
              Sort on grouped result. Fields must be <code>GroupBy</code> fields or aggregate aliases.
            </td>
          </tr>
          <tr>
            <td>
              <code>Page</code>
            </td>
            <td>
              <Link href="/docs/classes/page-by"><code>PageBy?</code></Link>
            </td>
            <td>Optional pagination on grouped result.</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn" title="Having fields are aliases, not entity properties">
        Inside <code>Having</code> every <code>Condition.Field</code> refers to an{" "}
        <code>AggregateBy.Alias</code> — not to a property on the underlying entity. Trying to
        reference an entity property in <code>Having</code> is a validation error.
      </Callout>

      <h2 id="csharp-example">C# example</h2>
      <Code lang="csharp">{`var summary = new Summary
{
    ConditionGroup = new ConditionGroup
    {
        Connector = Connector.And,
        Conditions = new List<Condition>
        {
            new Condition
            {
                Sort = 1, Field = "IsActive",
                DataType = DataType.Boolean, Operator = Operator.Equal,
                Values = new List<object> { true }
            }
        }
    },
    GroupBy = new GroupBy
    {
        Fields = new List<string> { "Country" },
        AggregateBy = new List<AggregateBy>
        {
            new AggregateBy { Aggregator = Aggregator.Count, Alias = "Total" },
            new AggregateBy { Field = "Amount", Aggregator = Aggregator.Sum, Alias = "Revenue" }
        }
    },
    Having = new ConditionGroup
    {
        Connector = Connector.And,
        Conditions = new List<Condition>
        {
            new Condition
            {
                Sort = 1, Field = "Total",
                DataType = DataType.Number, Operator = Operator.GreaterThan,
                Values = new List<object> { 10 }
            }
        }
    },
    Orders = new List<OrderBy>
    {
        new OrderBy { Sort = 1, Field = "Revenue", Direction = Direction.Descending }
    },
    Page = new PageBy { PageNumber = 1, PageSize = 20 }
};

SummaryResult result = await dbContext.Orders.ToListAsync(summary);`}</Code>

      <h2 id="json-example">JSON example</h2>
      <Code lang="json">{`{
  "conditionGroup": {
    "connector": "And",
    "conditions": [
      { "sort": 1, "field": "IsActive", "dataType": "Boolean", "operator": "Equal", "values": [true] }
    ],
    "subConditionGroups": []
  },
  "groupBy": {
    "fields": ["Country"],
    "aggregateBy": [
      { "aggregator": "Count", "alias": "Total" },
      { "field": "Amount", "aggregator": "Sum", "alias": "Revenue" }
    ]
  },
  "having": {
    "connector": "And",
    "conditions": [
      { "sort": 1, "field": "Total", "dataType": "Number", "operator": "GreaterThan", "values": [10] }
    ],
    "subConditionGroups": []
  },
  "orders": [
    { "sort": 1, "field": "Revenue", "direction": "Descending" }
  ],
  "page": { "pageNumber": 1, "pageSize": 20 }
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/summary-result">SummaryResult →</Link>
        </li>
        <li>
          <Link href="/docs/extensions/to-list-async-summary">ToListAsyncSummary →</Link>
        </li>
        <li>
          <Link href="/docs/validation/summary">Summary validation →</Link>
        </li>
        <li>
          <Link href="/docs/examples/summary">Summary example →</Link>
        </li>
      </ul>
    </DocPage>
  );
}
