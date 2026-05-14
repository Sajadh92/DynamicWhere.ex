import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Condition Validation",
  description:
    "Every rule enforced on a Condition before it can become a WHERE predicate — field existence, operator arity, value parsing.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/validation/condition/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/validation/condition">
      <h1>Condition Validation</h1>
      <p>
        A <Link href="/docs/classes/condition"><code>Condition</code></Link> is
        validated before its predicate is generated. Any broken rule throws a{" "}
        <code>LogicException</code> with the listed error code.
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
            <td>
              <code>Field</code> must be non-empty and exist on <code>T</code>
            </td>
            <td><code>InvalidField</code></td>
          </tr>
          <tr>
            <td>
              <code>Between</code> / <code>NotBetween</code> require exactly 2
              values
            </td>
            <td><code>RequiredTwoValue</code></td>
          </tr>
          <tr>
            <td>
              <code>In</code> / <code>IIn</code> / <code>NotIn</code> /{" "}
              <code>INotIn</code> require 1+ values
            </td>
            <td><code>RequiredValues</code></td>
          </tr>
          <tr>
            <td>
              <code>IsNull</code> / <code>IsNotNull</code> require 0 values
            </td>
            <td><code>NotRequiredValues</code></td>
          </tr>
          <tr>
            <td>All other operators require exactly 1 value</td>
            <td><code>RequiredOneValue(&#123;Operator&#125;)</code></td>
          </tr>
          <tr>
            <td>Values must not be null/whitespace</td>
            <td><code>InvalidValue</code></td>
          </tr>
          <tr>
            <td><code>Guid</code> values must parse as <code>Guid</code></td>
            <td><code>InvalidFormat</code></td>
          </tr>
          <tr>
            <td><code>Number</code> values must parse as a numeric type</td>
            <td><code>InvalidFormat</code></td>
          </tr>
          <tr>
            <td><code>Boolean</code> values must parse as <code>bool</code></td>
            <td><code>InvalidFormat</code></td>
          </tr>
          <tr>
            <td>
              <code>Date</code> / <code>DateTime</code> values must parse as{" "}
              <code>DateTime</code>
            </td>
            <td><code>InvalidFormat</code></td>
          </tr>
        </tbody>
      </table>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/classes/condition">Condition class</Link>
        </li>
        <li>
          <Link href="/docs/enums/operator">Operator enum</Link>
        </li>
        <li>
          <Link href="/docs/enums/data-type">DataType enum</Link>
        </li>
        <li>
          <Link href="/docs/errors">Error Codes</Link>
        </li>
      </ul>

      <Callout tone="info">
        Operator/value arity is enforced before any value is parsed. A missing
        value for <code>Between</code> raises <code>RequiredTwoValue</code>{" "}
        regardless of whether the (missing) value would have parsed.
      </Callout>
    </DocPage>
  );
}
