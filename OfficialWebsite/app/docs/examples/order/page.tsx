import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";

export const metadata: Metadata = {
  title: "Example: Order — Sorting",
  description: "Single OrderBy and multi-key OrderBy lists.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/examples/order">
      <h1>Example 4: Order — Single / Multiple Ordering</h1>
      <p>
        An <Link href="/docs/classes/order-by"><code>OrderBy</code></Link> entry
        with <code>Sort</code> + <code>Field</code> +{" "}
        <Link href="/docs/enums/direction"><code>Direction</code></Link>{" "}
        feeds <Link href="/docs/extensions/order"><code>Order&lt;T&gt;</code></Link>.
      </p>

      <h2 id="single-order">Single order</h2>
      <Code lang="json">{`{
  "sort": 1,
  "field": "CreatedAt",
  "direction": "Descending"
}`}</Code>

      <h2 id="multiple-orders">Multiple orders</h2>
      <Code lang="json">{`[
  { "sort": 1, "field": "LastName", "direction": "Ascending" },
  { "sort": 2, "field": "FirstName", "direction": "Ascending" }
]`}</Code>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/order">Order extension</Link>
        </li>
        <li>
          <Link href="/docs/classes/order-by">OrderBy class</Link>
        </li>
      </ul>
    </DocPage>
  );
}
