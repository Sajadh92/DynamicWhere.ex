import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "GroupBy Validation",
  description:
    "Every rule enforced on a GroupBy / AggregateBy shape — field shape, aliases, aggregator/type compatibility.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/validation/group-by/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/validation/group-by">
      <h1>GroupBy Validation</h1>
      <p>
        A <Link href="/docs/classes/group-by"><code>GroupBy</code></Link> shape
        is heavily validated because both the group keys and the aggregations
        feed the generated SQL <code>GROUP BY</code> / <code>SELECT</code>{" "}
        clauses.
      </p>

      <h2 id="rules">Rules</h2>
      <table>
        <thead>
          <tr>
            <th>Rule</th>
            <th>Error Code</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>Must have at least one field</td>
            <td><code>GroupByMustHaveFields</code></td>
          </tr>
          <tr>
            <td>Fields must be unique (case-insensitive)</td>
            <td><code>GroupByFieldsMustBeUnique</code></td>
          </tr>
          <tr>
            <td>Fields cannot be complex/navigation types</td>
            <td><code>GroupByFieldCannotBeComplexType</code></td>
          </tr>
          <tr>
            <td>Fields cannot be collection types</td>
            <td><code>GroupByFieldCannotBeCollectionType</code></td>
          </tr>
          <tr>
            <td>
              Aggregation alias must not be empty and must not contain dots
            </td>
            <td><code>InvalidAlias</code></td>
          </tr>
          <tr>
            <td>Aggregation aliases must be unique</td>
            <td><code>AggregationAliasesMustBeUnique</code></td>
          </tr>
          <tr>
            <td>Aggregation alias cannot match a GroupBy field</td>
            <td>
              <code>AggregationAliasCannotBeGroupByField(&#123;alias&#125;)</code>
            </td>
          </tr>
          <tr>
            <td>Aggregation field must be a simple type</td>
            <td><code>AggregationFieldMustBeSimpleType</code></td>
          </tr>
          <tr>
            <td>Aggregation field cannot be a collection</td>
            <td><code>AggregationFieldCannotBeCollectionType</code></td>
          </tr>
          <tr>
            <td>
              <code>Sumation</code> / <code>Average</code> only work on numeric
              fields
            </td>
            <td>
              <code>
                UnsupportedAggregatorForType(&#123;agg&#125;,&#123;type&#125;)
              </code>
            </td>
          </tr>
          <tr>
            <td>
              <code>Minimum</code> / <code>Maximum</code> do not work on{" "}
              <code>Boolean</code>
            </td>
            <td>
              <code>
                UnsupportedAggregatorForType(&#123;agg&#125;,&#123;type&#125;)
              </code>
            </td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        Dotted GroupBy fields like <code>Category.Name</code> are allowed if the
        leaf is a simple type. Only the leaf is the actual key; the dotted path
        is flattened into an alias such as <code>CategoryName</code> in the
        result. See{" "}
        <Link href="/docs/examples/summary">the Summary example</Link>.
      </Callout>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/classes/group-by">GroupBy class</Link>
        </li>
        <li>
          <Link href="/docs/classes/aggregate-by">AggregateBy class</Link>
        </li>
        <li>
          <Link href="/docs/enums/aggregator">Aggregator enum</Link>
        </li>
      </ul>
    </DocPage>
  );
}
