import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".Group<T>(groupBy)",
  description:
    "Group an IQueryable<T> by one or more fields with optional aggregations — returns a dynamic IQueryable.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/group">
      <h1>.Group&lt;T&gt;(groupBy)</h1>
      <p>
        Groups the query by the specified fields and applies aggregations.
        Returns a dynamic <code>IQueryable</code> whose elements expose the
        group-by fields and aggregate aliases as properties.
      </p>

      <h2 id="signature">Signature</h2>
      <Code lang="csharp">{`public static IQueryable Group<T>(this IQueryable<T> query, GroupBy groupBy)
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
            <td><code>groupBy</code></td>
            <td>
              <Link href="/docs/classes/group-by"><code>GroupBy</code></Link>
            </td>
            <td>Grouping and aggregation config</td>
          </tr>
        </tbody>
      </table>

      <h2 id="validations">Validations</h2>
      <ul>
        <li>
          Must have at least one field —{" "}
          <code>GroupByMustHaveFields</code>.
        </li>
        <li>
          Fields must be unique (case-insensitive) —{" "}
          <code>GroupByFieldsMustBeUnique</code>.
        </li>
        <li>
          Fields cannot be complex/navigation types —{" "}
          <code>GroupByFieldCannotBeComplexType</code>.
        </li>
        <li>
          Fields cannot be collection types —{" "}
          <code>GroupByFieldCannotBeCollectionType</code>.
        </li>
        <li>
          Aggregation alias must not be empty and must not contain dots —{" "}
          <code>InvalidAlias</code>.
        </li>
        <li>
          Aggregation aliases must be unique —{" "}
          <code>AggregationAliasesMustBeUnique</code>.
        </li>
        <li>
          Aggregation alias cannot match a GroupBy field —{" "}
          <code>AggregationAliasCannotBeGroupByField({"{alias}"})</code>.
        </li>
        <li>
          Aggregation field must be a simple type —{" "}
          <code>AggregationFieldMustBeSimpleType</code>.
        </li>
        <li>
          Aggregation field cannot be a collection —{" "}
          <code>AggregationFieldCannotBeCollectionType</code>.
        </li>
        <li>
          <code>Sumation</code> / <code>Average</code> only work on numeric
          fields — <code>UnsupportedAggregatorForType</code>.
        </li>
        <li>
          <code>Minimum</code> / <code>Maximum</code> do not work on{" "}
          <code>Boolean</code> — <code>UnsupportedAggregatorForType</code>.
        </li>
      </ul>

      <Callout tone="warn">
        Dotted <code>GroupBy</code> fields (e.g., <code>Category.Name</code>)
        produce flattened alias keys in the dynamic result objects (e.g.,{" "}
        <code>CategoryName</code>). Order fields in <code>Summary.Orders</code>{" "}
        should use the dotted form; the library handles alias mapping
        internally.
      </Callout>

      <h2 id="returns">Returns</h2>
      <p>
        <code>IQueryable</code> — a dynamic query with grouped results. Each
        element exposes the group-by fields (flattened to alias keys) and the
        aggregation aliases.
      </p>

      <h2 id="example">Example</h2>
      <Code lang="csharp">{`var groupBy = new GroupBy
{
    Fields = new List<string> { "Category" },
    AggregateBy = new List<AggregateBy>
    {
        new AggregateBy { Field = null,    Alias = "TotalCount", Aggregator = Aggregator.Count },
        new AggregateBy { Field = "Price", Alias = "AvgPrice",   Aggregator = Aggregator.Average },
        new AggregateBy { Field = "Price", Alias = "MaxPrice",   Aggregator = Aggregator.Maximum }
    }
};

var grouped = dbContext.Products.Group(groupBy);`}</Code>

      <Code lang="json">{`{
  "fields": ["Category"],
  "aggregateBy": [
    { "field": null,    "alias": "TotalCount", "aggregator": "Count" },
    { "field": "Price", "alias": "AvgPrice",   "aggregator": "Average" },
    { "field": "Price", "alias": "MaxPrice",   "aggregator": "Maximum" }
  ]
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/group-by"><code>GroupBy</code></Link> /{" "}
          <Link href="/docs/classes/aggregate-by"><code>AggregateBy</code></Link>{" "}
          shapes.
        </li>
        <li>
          <Link href="/docs/enums/aggregator"><code>Aggregator</code></Link>{" "}
          values.
        </li>
        <li>
          <Link href="/docs/validation/group-by">GroupBy validation</Link>.
        </li>
        <li>
          <Link href="/docs/extensions/summary"><code>.Summary&lt;T&gt;</code></Link>{" "}
          for the full <code>where → group → having → order → page</code>{" "}
          pipeline.
        </li>
      </ul>
    </DocPage>
  );
}
