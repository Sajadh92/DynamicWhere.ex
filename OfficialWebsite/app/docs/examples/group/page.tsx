import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";

export const metadata: Metadata = {
  title: "Example: Group — GroupBy + Aggregations",
  description:
    "GroupBy with Count, Average, and Maximum aggregations over a category key.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/examples/group">
      <h1>Example 6: Group — GroupBy with Aggregations</h1>
      <p>
        A <Link href="/docs/classes/group-by"><code>GroupBy</code></Link>{" "}
        carrying one or more <Link href="/docs/classes/aggregate-by">
          <code>AggregateBy</code>
        </Link>{" "}
        entries, passed to{" "}
        <Link href="/docs/extensions/group"><code>Group&lt;T&gt;</code></Link>.
      </p>

      <h2 id="payload">Payload</h2>
      <Code lang="json">{`{
  "fields": ["Category"],
  "aggregateBy": [
    { "field": null, "alias": "TotalCount", "aggregator": "Count" },
    { "field": "Price", "alias": "AvgPrice", "aggregator": "Average" },
    { "field": "Price", "alias": "MaxPrice", "aggregator": "Maximum" }
  ]
}`}</Code>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/group">Group extension</Link>
        </li>
        <li>
          <Link href="/docs/enums/aggregator">Aggregator enum</Link>
        </li>
        <li>
          <Link href="/docs/validation/group-by">GroupBy validation rules</Link>
        </li>
        <li>
          <Link href="/docs/examples/summary">Summary example</Link> — same
          shape inside a full where → group → having pipeline.
        </li>
      </ul>
    </DocPage>
  );
}
