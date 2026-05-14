import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "GroupBy",
  description:
    "Grouping configuration — the fields to group by and the aggregations to compute per group.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/classes/group-by/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/classes/group-by">
      <h1>GroupBy</h1>
      <p>
        A <code>GroupBy</code> defines the group-key fields and the list of aggregations to compute
        for each group. It is the required member of every <Link href="/docs/classes/summary"><code>Summary</code></Link>.
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
              <code>Fields</code>
            </td>
            <td>
              <code>List&lt;string&gt;</code>
            </td>
            <td>
              Properties to group by (must be simple types — no collections, no complex types).
            </td>
          </tr>
          <tr>
            <td>
              <code>AggregateBy</code>
            </td>
            <td>
              <code>List&lt;<Link href="/docs/classes/aggregate-by">AggregateBy</Link>&gt;</code>
            </td>
            <td>Aggregations to compute per group.</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn" title="Field restrictions">
        Group keys must be simple value types — <code>string</code>, numbers, booleans, enums,
        dates, etc. Collections and complex/navigation types cannot be grouped on.
      </Callout>

      <h2 id="csharp-example">C# example</h2>
      <Code lang="csharp">{`var groupBy = new GroupBy
{
    Fields = new List<string> { "Country", "Status" },
    AggregateBy = new List<AggregateBy>
    {
        new AggregateBy { Aggregator = Aggregator.Count, Alias = "Total" },
        new AggregateBy { Field = "Amount", Aggregator = Aggregator.Sum, Alias = "Revenue" }
    }
};`}</Code>

      <h2 id="json-example">JSON example</h2>
      <Code lang="json">{`{
  "fields": ["Country", "Status"],
  "aggregateBy": [
    { "aggregator": "Count", "alias": "Total" },
    { "field": "Amount", "aggregator": "Sum", "alias": "Revenue" }
  ]
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/aggregate-by">AggregateBy →</Link>
        </li>
        <li>
          <Link href="/docs/classes/summary">Summary →</Link>
        </li>
        <li>
          <Link href="/docs/extensions/group">Group extension →</Link>
        </li>
        <li>
          <Link href="/docs/validation/group-by">Validation →</Link>
        </li>
      </ul>
    </DocPage>
  );
}
