import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "OrderBy",
  description: "A single sort criterion — field + direction, with a priority order.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/classes/order-by">
      <h1>OrderBy</h1>
      <p>
        An <code>OrderBy</code> is a single sort criterion. Pass a list of them to <code>Filter</code>,{" "}
        <code>Segment</code>, or <code>Summary</code> — they apply in <code>Sort</code> order (lower first).
      </p>

      <h2 id="properties">Properties</h2>
      <table>
        <thead>
          <tr>
            <th>Property</th>
            <th>Type</th>
            <th>Default</th>
            <th>Description</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              <code>Sort</code>
            </td>
            <td>
              <code>int</code>
            </td>
            <td>–</td>
            <td>Priority order (lower = first).</td>
          </tr>
          <tr>
            <td>
              <code>Field</code>
            </td>
            <td>
              <code>string?</code>
            </td>
            <td>–</td>
            <td>Property path to sort by.</td>
          </tr>
          <tr>
            <td>
              <code>Direction</code>
            </td>
            <td>
              <Link href="/docs/enums/direction"><code>Direction</code></Link>
            </td>
            <td>
              <code>Ascending</code>
            </td>
            <td>Sort direction.</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="note">
        Default direction is <code>Ascending</code> — you can omit it for ascending sorts.
      </Callout>

      <h2 id="csharp-example">C# example</h2>
      <Code lang="csharp">{`var orders = new List<OrderBy>
{
    new OrderBy { Sort = 1, Field = "CreatedAt", Direction = Direction.Descending },
    new OrderBy { Sort = 2, Field = "Name" } // ascending by default
};`}</Code>

      <h2 id="json-example">JSON example</h2>
      <Code lang="json">{`[
  { "sort": 1, "field": "CreatedAt", "direction": "Descending" },
  { "sort": 2, "field": "Name", "direction": "Ascending" }
]`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/enums/direction">Direction enum →</Link>
        </li>
        <li>
          <Link href="/docs/extensions/order">Order extension →</Link>
        </li>
        <li>
          <Link href="/docs/examples/order">Order example →</Link>
        </li>
      </ul>
    </DocPage>
  );
}
