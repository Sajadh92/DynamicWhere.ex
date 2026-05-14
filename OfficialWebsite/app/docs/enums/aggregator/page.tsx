import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Aggregator",
  description:
    "Aggregator enum — the 8 aggregation functions (Count, CountDistinct, Sumation, Average, Minimum, Maximum, FirstOrDefault, LastOrDefault) used inside a GroupBy.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/enums/aggregator">
      <h1>Aggregator</h1>
      <p>
        <code>Aggregator</code> is the aggregation function applied inside a{" "}
        <Link href="/docs/classes/group-by"><code>GroupBy</code></Link>. Each
        entry in <code>GroupBy.AggregateBy</code> picks one of the eight
        functions and a target field — and the result column is named by{" "}
        <code>AggregateBy.Alias</code>.
      </p>

      <h2 id="values">Values</h2>
      <p>
        The two columns at the end tell you whether the function accepts a
        field at all, and whether that field is constrained to numeric types.
      </p>

      <table>
        <thead>
          <tr>
            <th>Value</th>
            <th>Description</th>
            <th>Supports Field?</th>
            <th>Numeric Only?</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Count</code></td>
            <td>Count items in the group.</td>
            <td>Optional — when no field, counts all items in the group.</td>
            <td>No</td>
          </tr>
          <tr>
            <td><code>CountDistinct</code></td>
            <td>Count distinct values of the field.</td>
            <td>Required</td>
            <td>No</td>
          </tr>
          <tr>
            <td><code>Sumation</code></td>
            <td>Sum of values.</td>
            <td>Required</td>
            <td><strong>Yes</strong></td>
          </tr>
          <tr>
            <td><code>Average</code></td>
            <td>Average of values.</td>
            <td>Required</td>
            <td><strong>Yes</strong></td>
          </tr>
          <tr>
            <td><code>Minimum</code></td>
            <td>Minimum value.</td>
            <td>Required</td>
            <td>No (except <code>Boolean</code>)</td>
          </tr>
          <tr>
            <td><code>Maximum</code></td>
            <td>Maximum value.</td>
            <td>Required</td>
            <td>No (except <code>Boolean</code>)</td>
          </tr>
          <tr>
            <td><code>FirstOrDefault</code></td>
            <td>First value in the group.</td>
            <td>Required</td>
            <td>No</td>
          </tr>
          <tr>
            <td><code>LastOrDefault</code></td>
            <td>Last value in the group.</td>
            <td>Required</td>
            <td>No</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn" title="Numeric-only constraints">
        <code>Sumation</code> and <code>Average</code> only accept numeric
        fields. <code>Minimum</code> and <code>Maximum</code> reject{" "}
        <code>Boolean</code> fields specifically. Violations throw{" "}
        <code>UnsupportedAggregatorForType(agg, type)</code>.
      </Callout>

      <h2 id="validation">Validation rules</h2>
      <table>
        <thead>
          <tr>
            <th>Rule</th>
            <th>Error code</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Alias</code> must be non-empty and contain no dots.</td>
            <td><code>InvalidAlias</code></td>
          </tr>
          <tr>
            <td>Aliases must be unique within an <code>AggregateBy</code> list.</td>
            <td><code>AggregationAliasesMustBeUnique</code></td>
          </tr>
          <tr>
            <td>An alias cannot match any <code>GroupBy.Fields</code> entry.</td>
            <td><code>AggregationAliasCannotBeGroupByField(alias)</code></td>
          </tr>
          <tr>
            <td>Aggregation field must be a simple type (not complex / navigation).</td>
            <td><code>AggregationFieldMustBeSimpleType</code></td>
          </tr>
          <tr>
            <td>Aggregation field cannot be a collection.</td>
            <td><code>AggregationFieldCannotBeCollectionType</code></td>
          </tr>
          <tr>
            <td><code>Sumation</code> / <code>Average</code> on a non-numeric field.</td>
            <td><code>UnsupportedAggregatorForType(agg, type)</code></td>
          </tr>
          <tr>
            <td><code>Minimum</code> / <code>Maximum</code> on a <code>Boolean</code> field.</td>
            <td><code>UnsupportedAggregatorForType(agg, type)</code></td>
          </tr>
        </tbody>
      </table>

      <h2 id="json">JSON example</h2>
      <Code lang="json">{`{
  "fields": ["Category"],
  "aggregateBy": [
    { "field": null,    "alias": "TotalCount",    "aggregator": "Count" },
    { "field": "Id",    "alias": "DistinctIds",   "aggregator": "CountDistinct" },
    { "field": "Price", "alias": "TotalRevenue",  "aggregator": "Sumation" },
    { "field": "Price", "alias": "AvgPrice",      "aggregator": "Average" },
    { "field": "Price", "alias": "MinPrice",      "aggregator": "Minimum" },
    { "field": "Price", "alias": "MaxPrice",      "aggregator": "Maximum" },
    { "field": "Name",  "alias": "FirstName",     "aggregator": "FirstOrDefault" },
    { "field": "Name",  "alias": "LastName",      "aggregator": "LastOrDefault" }
  ]
}`}</Code>

      <p>Result (illustrative single group row):</p>
      <Code lang="json">{`{
  "Category": "Electronics",
  "TotalCount": 15,
  "DistinctIds": 15,
  "TotalRevenue": 5249.85,
  "AvgPrice": 349.99,
  "MinPrice": 9.99,
  "MaxPrice": 1299.99,
  "FirstName": "Adapter Cable",
  "LastName": "Wireless Mouse"
}`}</Code>

      <h2 id="csharp">C# usage</h2>
      <Code lang="csharp">{`using DynamicWhere.ex.Enums;

var groupBy = new GroupBy
{
    Fields = new List<string> { "Category" },
    AggregateBy = new List<AggregateBy>
    {
        new AggregateBy { Field = null,    Alias = "TotalCount",   Aggregator = Aggregator.Count },
        new AggregateBy { Field = "Price", Alias = "TotalRevenue", Aggregator = Aggregator.Sumation },
        new AggregateBy { Field = "Price", Alias = "AvgPrice",     Aggregator = Aggregator.Average },
    }
};`}</Code>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/classes/group-by">GroupBy →</Link> the grouping
          container that owns the <code>AggregateBy</code> list.
        </li>
        <li>
          <Link href="/docs/classes/aggregate-by">AggregateBy →</Link> a single
          aggregation entry.
        </li>
        <li>
          <Link href="/docs/classes/summary">Summary →</Link> wraps grouping
          with optional having, order, and pagination.
        </li>
        <li>
          <Link href="/docs/validation/group-by">GroupBy validation →</Link>{" "}
          full rule set.
        </li>
      </ul>
    </DocPage>
  );
}
