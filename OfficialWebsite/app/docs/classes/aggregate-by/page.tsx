import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "AggregateBy",
  description:
    "A single aggregation within a GroupBy — Count / Sum / Min / Max / Avg with an output alias.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/classes/aggregate-by/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/classes/aggregate-by">
      <h1>AggregateBy</h1>
      <p>
        An <code>AggregateBy</code> represents one aggregation inside a{" "}
        <Link href="/docs/classes/group-by"><code>GroupBy</code></Link>. It produces a single output
        column named by its <code>Alias</code>.
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
              <code>Field</code>
            </td>
            <td>
              <code>string?</code>
            </td>
            <td>Property to aggregate (optional for <code>Count</code>).</td>
          </tr>
          <tr>
            <td>
              <code>Alias</code>
            </td>
            <td>
              <code>string?</code>
            </td>
            <td>
              Name of the result column (must not conflict with <code>GroupBy.Fields</code>, no dots).
            </td>
          </tr>
          <tr>
            <td>
              <code>Aggregator</code>
            </td>
            <td>
              <Link href="/docs/enums/aggregator"><code>Aggregator</code></Link>
            </td>
            <td>Aggregation function.</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn" title="Alias rules">
        <ul>
          <li>
            <code>Alias</code> must not contain dots — it becomes a top-level property on the
            resulting <code>dynamic</code> objects in <code>SummaryResult.Data</code>.
          </li>
          <li>
            <code>Alias</code> must not collide with any field name in <code>GroupBy.Fields</code>.
          </li>
          <li>
            <code>Field</code> is required for every aggregator <em>except</em> <code>Count</code>.
          </li>
        </ul>
      </Callout>

      <h2 id="csharp-example">C# example</h2>
      <Code lang="csharp">{`var aggregations = new List<AggregateBy>
{
    new AggregateBy { Aggregator = Aggregator.Count, Alias = "OrderCount" },
    new AggregateBy { Field = "Amount", Aggregator = Aggregator.Sum, Alias = "Revenue" },
    new AggregateBy { Field = "Amount", Aggregator = Aggregator.Avg, Alias = "AverageAmount" }
};`}</Code>

      <h2 id="json-example">JSON example</h2>
      <Code lang="json">{`[
  { "aggregator": "Count", "alias": "OrderCount" },
  { "field": "Amount", "aggregator": "Sum", "alias": "Revenue" },
  { "field": "Amount", "aggregator": "Avg", "alias": "AverageAmount" }
]`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/group-by">GroupBy →</Link>
        </li>
        <li>
          <Link href="/docs/enums/aggregator">Aggregator enum →</Link>
        </li>
        <li>
          <Link href="/docs/classes/summary-result">SummaryResult →</Link> the shape that surfaces aliases.
        </li>
      </ul>
    </DocPage>
  );
}
