import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Segment Validation",
  description:
    "Rules enforced on a Segment — unique Sort values across sets and required Intersection on every set after the first.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/validation/segment/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/validation/segment">
      <h1>Segment Validation</h1>
      <p>
        A <Link href="/docs/classes/segment"><code>Segment</code></Link> is a
        list of <Link href="/docs/classes/condition-set">
          <code>ConditionSet</code>
        </Link>{" "}
        entries joined by <code>UNION</code> / <code>INTERSECT</code> /{" "}
        <code>EXCEPT</code>. Two rules guarantee a deterministic, well-formed
        set expression.
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
            <td><code>ConditionSets</code> Sort values must be unique</td>
            <td><code>SetsUniqueSort</code></td>
          </tr>
          <tr>
            <td>
              Sets at index 1+ must have <code>Intersection</code> specified
            </td>
            <td><code>RequiredIntersection</code></td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        The first set (after <code>Sort</code> ordering) defines the base
        collection and has <code>Intersection = null</code>. Every subsequent
        set combines into the running result, so its <code>Intersection</code>{" "}
        is required.
      </Callout>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/classes/segment">Segment class</Link>
        </li>
        <li>
          <Link href="/docs/classes/condition-set">ConditionSet class</Link>
        </li>
        <li>
          <Link href="/docs/enums/intersection">Intersection enum</Link>
        </li>
        <li>
          <Link href="/docs/examples/segment">Segment JSON example</Link>
        </li>
      </ul>
    </DocPage>
  );
}
