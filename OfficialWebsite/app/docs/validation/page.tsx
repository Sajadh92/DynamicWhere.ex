import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Validation",
  description:
    "Every validation rule DynamicWhere.ex enforces before executing a query — fail fast with a LogicException carrying a stable error code.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/validation">
      <h1>Validation</h1>
      <p>
        Before DynamicWhere.ex translates your JSON into an{" "}
        <code>IQueryable&lt;T&gt;</code>, every shape is validated. A bad input
        throws a <code>LogicException</code> carrying a stable, machine-readable
        error code so your API can surface a precise 400 to the client.
      </p>

      <h2 id="how-validation-works">How validation works</h2>
      <p>
        Validation is performed inside each extension method on the input shape
        you pass. The library throws <code>LogicException(ErrorCode)</code>{" "}
        whenever a rule is broken — execution stops, no SQL is ever generated,
        and no data is touched.
      </p>

      <Callout tone="info">
        Catch <code>LogicException</code> in your controller pipeline and map its{" "}
        <code>ErrorCode</code> to a structured error response. See the full list
        of codes in <Link href="/docs/errors">Error Codes</Link>.
      </Callout>

      <h2 id="rule-categories">Rule categories</h2>
      <p>
        Rules are grouped by the shape they protect. Each page below documents
        every rule and the exact error code it raises.
      </p>

      <table>
        <thead>
          <tr>
            <th>Shape</th>
            <th>Page</th>
            <th>Protects</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Condition</code></td>
            <td>
              <Link href="/docs/validation/condition">Condition rules</Link>
            </td>
            <td>Field existence, operator/value arity, parseable values.</td>
          </tr>
          <tr>
            <td><code>ConditionGroup</code></td>
            <td>
              <Link href="/docs/validation/condition-group">
                ConditionGroup rules
              </Link>
            </td>
            <td>Unique sort indices across sibling conditions and groups.</td>
          </tr>
          <tr>
            <td><code>GroupBy</code> / <code>AggregateBy</code></td>
            <td>
              <Link href="/docs/validation/group-by">GroupBy rules</Link>
            </td>
            <td>Group field shape, aggregation aliases, aggregator/type compatibility.</td>
          </tr>
          <tr>
            <td><code>Segment</code></td>
            <td>
              <Link href="/docs/validation/segment">Segment rules</Link>
            </td>
            <td>Set-operation ordering and required intersection.</td>
          </tr>
          <tr>
            <td><code>Summary</code></td>
            <td>
              <Link href="/docs/validation/summary">Summary rules</Link>
            </td>
            <td>Required GroupBy, valid order / having field references.</td>
          </tr>
          <tr>
            <td><code>PageBy</code></td>
            <td>
              <Link href="/docs/validation/page">Page rules</Link>
            </td>
            <td>Positive page number and page size.</td>
          </tr>
        </tbody>
      </table>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/errors">Error Codes</Link> — the full enum of error
          codes raised by validation.
        </li>
        <li>
          <Link href="/docs/classes/condition">Condition</Link> — the shape most
          rules guard.
        </li>
      </ul>
    </DocPage>
  );
}
