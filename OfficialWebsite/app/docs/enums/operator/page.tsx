import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Operator",
  description:
    "Operator enum — the 28 comparison operators DynamicWhere.ex supports, with the value count each one requires.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/enums/operator">
      <h1>Operator</h1>
      <p>
        <code>Operator</code> declares the comparison applied by a{" "}
        <Link href="/docs/classes/condition"><code>Condition</code></Link>.
        There are 28 operators total. Every operator that starts with{" "}
        <code>I</code> is the case-insensitive variant — both sides are
        normalized with <code>.ToLower()</code> before comparison.
      </p>

      <h2 id="value-counts">Required values at a glance</h2>
      <p>
        Each operator expects a specific number of entries in{" "}
        <code>Condition.Values</code>. Sending the wrong count throws a
        validation error (<code>RequiredOneValue</code>,{" "}
        <code>RequiredTwoValue</code>, <code>RequiredValues</code>, or{" "}
        <code>NotRequiredValues</code>).
      </p>
      <table>
        <thead>
          <tr>
            <th>Value count</th>
            <th>Operators</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><strong>0</strong></td>
            <td>
              <code>IsNull</code>, <code>IsNotNull</code>
            </td>
          </tr>
          <tr>
            <td><strong>1</strong></td>
            <td>
              All equality, contains, starts-with, ends-with, and ordered
              comparisons (24 operators total).
            </td>
          </tr>
          <tr>
            <td><strong>2</strong> (exactly)</td>
            <td>
              <code>Between</code>, <code>NotBetween</code>
            </td>
          </tr>
          <tr>
            <td><strong>1+</strong></td>
            <td>
              <code>In</code>, <code>IIn</code>, <code>NotIn</code>,{" "}
              <code>INotIn</code>
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="equality">Equality</h2>
      <table>
        <thead>
          <tr>
            <th>Operator</th>
            <th>Description</th>
            <th>Required Values</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Equal</code></td>
            <td>Equality (case-sensitive for text).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>IEqual</code></td>
            <td>Equality (case-insensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>NotEqual</code></td>
            <td>Inequality (case-sensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>INotEqual</code></td>
            <td>Inequality (case-insensitive).</td>
            <td>1</td>
          </tr>
        </tbody>
      </table>

      <h2 id="contains">Contains / StartsWith / EndsWith</h2>
      <table>
        <thead>
          <tr>
            <th>Operator</th>
            <th>Description</th>
            <th>Required Values</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Contains</code></td>
            <td>Text contains (case-sensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>IContains</code></td>
            <td>Text contains (case-insensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>NotContains</code></td>
            <td>Text does not contain (case-sensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>INotContains</code></td>
            <td>Text does not contain (case-insensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>StartsWith</code></td>
            <td>Starts with (case-sensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>IStartsWith</code></td>
            <td>Starts with (case-insensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>NotStartsWith</code></td>
            <td>Does not start with (case-sensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>INotStartsWith</code></td>
            <td>Does not start with (case-insensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>EndsWith</code></td>
            <td>Ends with (case-sensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>IEndsWith</code></td>
            <td>Ends with (case-insensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>NotEndsWith</code></td>
            <td>Does not end with (case-sensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>INotEndsWith</code></td>
            <td>Does not end with (case-insensitive).</td>
            <td>1</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="note">
        Case-insensitive <code>I*</code> operators emit <code>.ToLower()</code>{" "}
        on both sides of the comparison. On SQL Server this is typically free
        (default collations are case-insensitive). On case-sensitive collations
        (e.g. PostgreSQL with <code>C</code> locale) this still works but may
        sidestep an index.
      </Callout>

      <h2 id="in">In / NotIn (set membership)</h2>
      <table>
        <thead>
          <tr>
            <th>Operator</th>
            <th>Description</th>
            <th>Required Values</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>In</code></td>
            <td>Value is in the set (case-sensitive for text).</td>
            <td>1+</td>
          </tr>
          <tr>
            <td><code>IIn</code></td>
            <td>Value is in the set (case-insensitive).</td>
            <td>1+</td>
          </tr>
          <tr>
            <td><code>NotIn</code></td>
            <td>Value is not in the set (case-sensitive).</td>
            <td>1+</td>
          </tr>
          <tr>
            <td><code>INotIn</code></td>
            <td>Value is not in the set (case-insensitive).</td>
            <td>1+</td>
          </tr>
        </tbody>
      </table>

      <h2 id="ranges">Ordered comparisons &amp; ranges</h2>
      <table>
        <thead>
          <tr>
            <th>Operator</th>
            <th>Description</th>
            <th>Required Values</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>GreaterThan</code></td>
            <td>Greater than.</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>GreaterThanOrEqual</code></td>
            <td>Greater than or equal.</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>LessThan</code></td>
            <td>Less than.</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>LessThanOrEqual</code></td>
            <td>Less than or equal.</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>Between</code></td>
            <td>Inclusive range — first value is the lower bound, second is the upper bound.</td>
            <td>2 (exactly)</td>
          </tr>
          <tr>
            <td><code>NotBetween</code></td>
            <td>Outside the inclusive range defined by the two values.</td>
            <td>2 (exactly)</td>
          </tr>
        </tbody>
      </table>

      <h2 id="null">Null checks</h2>
      <table>
        <thead>
          <tr>
            <th>Operator</th>
            <th>Description</th>
            <th>Required Values</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>IsNull</code></td>
            <td>Property is NULL.</td>
            <td>0</td>
          </tr>
          <tr>
            <td><code>IsNotNull</code></td>
            <td>Property is NOT NULL.</td>
            <td>0</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn">
        Sending any value with <code>IsNull</code> / <code>IsNotNull</code>{" "}
        throws <code>NotRequiredValues</code>. Send an empty array:{" "}
        <code>"values": []</code>.
      </Callout>

      <h2 id="examples">JSON examples</h2>

      <p>Range (<code>Between</code>):</p>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Price",
  "dataType": "Number",
  "operator": "Between",
  "values": [100, 500]
}`}</Code>

      <p>Set membership (<code>IIn</code>):</p>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Country",
  "dataType": "Text",
  "operator": "IIn",
  "values": ["USA", "Canada", "UK"]
}`}</Code>

      <p>Null check (<code>IsNull</code>):</p>
      <Code lang="json">{`{
  "sort": 1,
  "field": "DeletedAt",
  "dataType": "DateTime",
  "operator": "IsNull",
  "values": []
}`}</Code>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/enums/data-type">DataType →</Link> which operators
          each logical type accepts.
        </li>
        <li>
          <Link href="/docs/validation/condition">Condition validation →</Link>{" "}
          the exact error codes thrown on count mismatches.
        </li>
        <li>
          <Link href="/docs/errors">Error codes →</Link> full reference.
        </li>
      </ul>
    </DocPage>
  );
}
