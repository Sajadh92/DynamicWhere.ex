import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".Summary<T>(summary)",
  description:
    "Apply where → group → having → order → page to an IQueryable<T> for aggregate reporting.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/extensions/summary/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/summary">
      <h1>.Summary&lt;T&gt;(summary)</h1>
      <p>
        Applies the full aggregate-reporting pipeline —{" "}
        <code>where → group → having → order → page</code> — to a query and
        returns a dynamic <code>IQueryable</code>.
      </p>

      <h2 id="signature">Signature</h2>
      <Code lang="csharp">{`public static IQueryable Summary<T>(this IQueryable<T> query, Summary summary)
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
            <td><code>summary</code></td>
            <td>
              <Link href="/docs/classes/summary"><code>Summary</code></Link>
            </td>
            <td>
              Composition object — <code>ConditionGroup</code>,{" "}
              <code>GroupBy</code> (required), <code>Having</code>,{" "}
              <code>Orders</code>, <code>Page</code>
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="pipeline">Pipeline</h2>
      <ul>
        <li>
          <code>Where</code> — optional pre-grouping filter via{" "}
          <code>ConditionGroup</code>.
        </li>
        <li>
          <code>Group</code> — <strong>required</strong>; groups by{" "}
          <code>GroupBy.Fields</code> and computes{" "}
          <code>GroupBy.AggregateBy</code> aggregations.
        </li>
        <li>
          <code>Having</code> — optional post-group filter. Each condition's{" "}
          <code>Field</code> must reference an <code>AggregateBy.Alias</code>.
        </li>
        <li>
          <code>Order</code> — optional sort. Fields must be GroupBy fields or
          aggregate aliases.
        </li>
        <li><code>Page</code> — optional pagination.</li>
      </ul>

      <h2 id="validations">Validations</h2>
      <ul>
        <li>
          <code>GroupBy</code> is required (not null) —{" "}
          <code>ArgumentNullException</code>.
        </li>
        <li>
          GroupBy validation — see{" "}
          <Link href="/docs/extensions/group"><code>.Group&lt;T&gt;</code></Link>
          .
        </li>
        <li>
          Order fields must exist in GroupBy fields or aggregate aliases —{" "}
          <code>SummaryOrderFieldMustExistInGroupByOrAggregate({"{field}"})</code>.
        </li>
        <li>
          Having condition fields must reference aggregate aliases —{" "}
          <code>HavingFieldMustExistInAggregateByAlias({"{field}"})</code>.
        </li>
        <li>
          Page validation — both <code>PageNumber</code> and{" "}
          <code>PageSize</code> must be &gt; 0.
        </li>
      </ul>

      <Callout tone="warn">
        In a <code>Summary</code>, the{" "}
        <code>Having.ConditionGroup.Conditions[].Field</code> must match an{" "}
        <code>AggregateBy.Alias</code>, <strong>not</strong> an entity property
        path.
      </Callout>

      <Callout tone="note">
        Dotted GroupBy fields (e.g., <code>Category.Name</code>) produce
        flattened alias keys in the dynamic result objects (e.g.,{" "}
        <code>CategoryName</code>). Order fields in <code>Summary.Orders</code>{" "}
        should use the dotted form; the library handles alias mapping
        internally.
      </Callout>

      <h2 id="returns">Returns</h2>
      <p>
        <code>IQueryable</code> — dynamic grouped query. Each element exposes
        the GroupBy fields (flattened to alias keys) plus all{" "}
        <code>AggregateBy.Alias</code> values.
      </p>

      <h2 id="example">Example</h2>
      <Code lang="csharp">{`var summary = new Summary
{
    ConditionGroup = new ConditionGroup
    {
        Connector = Connector.And,
        Conditions = new List<Condition>
        {
            new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = new List<object> { true } }
        }
    },
    GroupBy = new GroupBy
    {
        Fields = new List<string> { "Category.Name" },
        AggregateBy = new List<AggregateBy>
        {
            new AggregateBy { Field = null,    Alias = "ProductCount", Aggregator = Aggregator.Count },
            new AggregateBy { Field = "Price", Alias = "AvgPrice",     Aggregator = Aggregator.Average },
            new AggregateBy { Field = "Price", Alias = "TotalRevenue", Aggregator = Aggregator.Sumation }
        }
    },
    Having = new ConditionGroup
    {
        Connector = Connector.And,
        Conditions = new List<Condition>
        {
            new Condition { Sort = 1, Field = "ProductCount", DataType = DataType.Number, Operator = Operator.GreaterThan, Values = new List<object> { 5 } }
        }
    },
    Orders = new List<OrderBy>
    {
        new OrderBy { Sort = 1, Field = "TotalRevenue", Direction = Direction.Descending }
    },
    Page = new PageBy { PageNumber = 1, PageSize = 10 }
};

IQueryable grouped = dbContext.Products.Summary(summary);`}</Code>

      <Code lang="json">{`{
  "conditionGroup": {
    "connector": "And",
    "conditions": [
      { "sort": 1, "field": "IsActive", "dataType": "Boolean", "operator": "Equal", "values": ["true"] }
    ],
    "subConditionGroups": []
  },
  "groupBy": {
    "fields": ["Category.Name"],
    "aggregateBy": [
      { "field": null,    "alias": "ProductCount", "aggregator": "Count" },
      { "field": "Price", "alias": "AvgPrice",     "aggregator": "Average" },
      { "field": "Price", "alias": "TotalRevenue", "aggregator": "Sumation" }
    ]
  },
  "having": {
    "connector": "And",
    "conditions": [
      { "sort": 1, "field": "ProductCount", "dataType": "Number", "operator": "GreaterThan", "values": ["5"] }
    ],
    "subConditionGroups": []
  },
  "orders": [
    { "sort": 1, "field": "TotalRevenue", "direction": "Descending" }
  ],
  "page": { "pageNumber": 1, "pageSize": 10 }
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/summary"><code>Summary</code></Link> shape.
        </li>
        <li>
          <Link href="/docs/extensions/to-list-summary">
            <code>.ToList&lt;T&gt;(Summary)</code>
          </Link>{" "}
          /{" "}
          <Link href="/docs/extensions/to-list-async-summary">
            <code>.ToListAsync&lt;T&gt;(Summary)</code>
          </Link>{" "}
          to materialize.
        </li>
        <li>
          <Link href="/docs/validation/summary">Summary validation</Link>.
        </li>
      </ul>
    </DocPage>
  );
}
