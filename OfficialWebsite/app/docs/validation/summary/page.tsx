import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Summary Validation",
  description:
    "Rules enforced on a Summary — required GroupBy, valid Order / Having field references against the GroupBy fields and aggregate aliases.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/validation/summary">
      <h1>Summary Validation</h1>
      <p>
        A <Link href="/docs/classes/summary"><code>Summary</code></Link> is{" "}
        <em>where → group → having → order → page</em>. Its rules guarantee
        that every Order and Having field can actually be resolved against the
        grouped projection.
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
            <td><code>GroupBy</code> is required (not null)</td>
            <td><code>ArgumentNullException</code></td>
          </tr>
          <tr>
            <td>
              Order fields must exist in GroupBy fields or aggregate aliases
            </td>
            <td>
              <code>
                SummaryOrderFieldMustExistInGroupByOrAggregate(&#123;field&#125;)
              </code>
            </td>
          </tr>
          <tr>
            <td>
              Having condition fields must reference aggregate aliases
            </td>
            <td>
              <code>
                HavingFieldMustExistInAggregateByAlias(&#123;field&#125;)
              </code>
            </td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        After grouping, the only addressable members are the GroupBy keys and
        the aggregation aliases — that is what Order and Having can reference.
      </Callout>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/classes/summary">Summary class</Link>
        </li>
        <li>
          <Link href="/docs/validation/group-by">GroupBy rules</Link> — the
          nested <code>GroupBy</code> is also validated.
        </li>
        <li>
          <Link href="/docs/examples/summary">Summary JSON example</Link>
        </li>
      </ul>
    </DocPage>
  );
}
