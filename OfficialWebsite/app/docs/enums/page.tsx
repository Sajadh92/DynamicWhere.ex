import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Enums",
  description:
    "The eight enum types that drive DynamicWhere.ex — data types, operators, connectors, directions, intersections, aggregators, and cache tuning.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/enums/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/enums">
      <h1>Enums</h1>
      <p>
        DynamicWhere.ex is configured almost entirely through eight enums. They
        describe <em>what</em> a value is, <em>how</em> to compare it,{" "}
        <em>how</em> to combine conditions, and <em>how</em> to tune the
        internal reflection cache. Every JSON shape you send from the front-end
        is some combination of these.
      </p>

      <h2 id="overview">Overview</h2>
      <p>
        The eight enums fall into three families: <strong>query</strong>{" "}
        (describes the filter expression), <strong>shape</strong> (describes
        the result shape), and <strong>cache</strong> (tunes the reflection
        cache).
      </p>

      <table>
        <thead>
          <tr>
            <th>Enum</th>
            <th>Family</th>
            <th>Purpose</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              <Link href="/docs/enums/data-type"><code>DataType</code></Link>
            </td>
            <td>Query</td>
            <td>
              Logical type of a condition value — drives parsing, coercion, and
              the set of legal operators.
            </td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/enums/operator"><code>Operator</code></Link>
            </td>
            <td>Query</td>
            <td>
              The comparison applied by a condition — 28 operators covering
              equality, text matching, ranges, sets, and null checks.
            </td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/enums/connector"><code>Connector</code></Link>
            </td>
            <td>Query</td>
            <td>
              The logical glue (<code>And</code> / <code>Or</code>) that joins
              sibling conditions inside a <code>ConditionGroup</code>.
            </td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/enums/direction"><code>Direction</code></Link>
            </td>
            <td>Shape</td>
            <td>
              Sort direction (<code>Ascending</code> / <code>Descending</code>)
              for an <code>OrderBy</code>.
            </td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/enums/intersection"><code>Intersection</code></Link>
            </td>
            <td>Shape</td>
            <td>
              The set operation (<code>Union</code> / <code>Intersect</code> /{" "}
              <code>Except</code>) applied between condition sets in a{" "}
              <code>Segment</code>.
            </td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/enums/aggregator"><code>Aggregator</code></Link>
            </td>
            <td>Shape</td>
            <td>
              Aggregation function (<code>Count</code>, <code>Sumation</code>,{" "}
              <code>Average</code>, <code>Minimum</code>, <code>Maximum</code>,
              and friends) used inside a <code>GroupBy</code>.
            </td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/enums/cache-eviction-strategy">
                <code>CacheEvictionStrategy</code>
              </Link>
            </td>
            <td>Cache</td>
            <td>
              Eviction algorithm for the reflection cache —{" "}
              <code>FIFO</code>, <code>LRU</code> (default), or <code>LFU</code>.
            </td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/enums/cache-memory-type">
                <code>CacheMemoryType</code>
              </Link>
            </td>
            <td>Cache</td>
            <td>
              Identifies one of the three internal cache stores for monitoring
              and clearing operations.
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="namespace">Namespace</h2>
      <p>
        All query and shape enums live in <code>DynamicWhere.ex.Enums</code>.
        Cache enums live in <code>DynamicWhere.ex.Optimization.Cache.Config</code>.
      </p>

      <Callout tone="note">
        Every enum sub-page lists <strong>every value</strong> from the source
        of truth — no abbreviation. Use the table above as a quick map, then
        dive into the page you need.
      </Callout>

      <h2 id="next">Where to go next</h2>
      <ul>
        <li>
          <Link href="/docs/enums/data-type">DataType →</Link> what each logical
          type means and which operators it accepts.
        </li>
        <li>
          <Link href="/docs/enums/operator">Operator →</Link> the full operator
          matrix and the value count each one requires.
        </li>
        <li>
          <Link href="/docs/classes/condition">Condition class →</Link> how the
          enums fit together inside a single filter predicate.
        </li>
      </ul>
    </DocPage>
  );
}
