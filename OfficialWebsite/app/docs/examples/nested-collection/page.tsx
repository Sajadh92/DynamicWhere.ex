import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Example: Nested Collection Navigation",
  description:
    "Field paths that traverse a collection are automatically wrapped in .Any() lambdas — including the generated expression.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/examples/nested-collection/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/examples/nested-collection">
      <h1>Example 13: Nested Collection Navigation</h1>
      <p>
        When a field path traverses a collection property (e.g.{" "}
        <code>Orders.OrderItems.ProductName</code>), the library automatically
        wraps the inner segment in a <code>.Any()</code> lambda — so the
        predicate evaluates per-element.
      </p>

      <h2 id="condition">Condition</h2>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Orders.OrderItems.ProductName",
  "dataType": "Text",
  "operator": "IContains",
  "values": ["laptop"]
}`}</Code>

      <h2 id="generated">Generated expression</h2>
      <Callout tone="info">
        <strong>Generated expression:</strong>
      </Callout>
      <Code lang="csharp">{`Orders.Any(i1 => i1.OrderItems.Any(i2 => i2.ProductName != null && i2.ProductName.ToLower().Contains("laptop")))`}</Code>

      <p>
        Each collection segment in the path becomes another nested{" "}
        <code>.Any()</code> level — so deep paths through collections compose
        cleanly into a single boolean expression EF Core can translate.
      </p>

      <h2 id="ordering">Ordering across the same paths</h2>
      <p>
        Sorting accepts the same collection paths, but <code>.Any()</code>{" "}
        yields a boolean — useless for ordering. Each collection segment is
        reduced to one comparable value instead:{" "}
        <strong>
          <code>Min</code> ascending, <code>Max</code> descending
        </strong>
        .
      </p>

      <Code lang="json">{`{
  "sort": 1,
  "field": "Orders.OrderItems.ProductName",
  "direction": "Ascending"
}`}</Code>

      <Callout tone="info">
        <strong>Generated expression:</strong>
      </Callout>
      <Code lang="csharp">{`Orders.Min(OrderItems.Min(ProductName)) asc`}</Code>

      <p>
        Rows whose collection is empty sort as <code>null</code> — or as the
        type default for non-nullable value types, where{" "}
        <code>DefaultIfEmpty()</code> is inserted to keep in-memory sorting from
        throwing. A path may not <em>end</em> on a collection of entities; sort
        by a scalar inside it instead (<code>Tags</code> ✗ →{" "}
        <code>Tags.Value</code> ✓).
      </p>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/classes/condition">Condition class</Link>
        </li>
        <li>
          <Link href="/docs/extensions/where">Where extension</Link>
        </li>
        <li>
          <Link href="/docs/extensions/order">Order extension</Link>
        </li>
        <li>
          <Link href="/docs/examples/where-single">
            Single-Condition variants
          </Link>
        </li>
      </ul>
    </DocPage>
  );
}
