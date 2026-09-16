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
            <td>
              A null value normalizes to an empty string — accepted by{" "}
              <code>Text</code> and <code>Enum</code>, rejected by every other{" "}
              <code>DataType</code>
            </td>
            <td><code>InvalidFormat</code></td>
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
              <code>Date</code> / <code>DateTime</code> values must be ISO&nbsp;8601,
              year-first, or a format declared with <code>DwDates.Configure</code>
            </td>
            <td>
              <code>AmbiguousDateFormat</code> for a day/month-first date,
              otherwise <code>InvalidFormat</code>
            </td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn" title="A date value is read the way the predicate will read it">
        Validation reads a date value exactly as the predicate builder does, with
        the member&apos;s own <code>DateTime</code>, <code>DateTimeOffset</code> or{" "}
        <code>DateOnly</code> type. The server&apos;s culture plays no part, so a
        value is accepted or refused the same way on every host, and a value that
        passes validation is one the builder can use. ISO&nbsp;8601 (
        <code>&quot;2026-09-15&quot;</code>,{" "}
        <code>&quot;2026-09-15T12:00:00Z&quot;</code>) and year-first dates are
        accepted everywhere; <code>&quot;01/09/2026&quot;</code> is refused with{" "}
        <code>AmbiguousDateFormat</code> unless the deployment declares its order.
        See <Link href="/docs/enums/data-type">DataType</Link> and{" "}
        <Link href="/docs/breaking-changes#date-value-formats">breaking changes</Link>.
      </Callout>

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
        regardless of whether the (missing) value would have parsed. There is no
        separate &quot;value is null or whitespace&quot; failure:{" "}
        <code>InvalidValue</code> (
        <code>ConditionValuesAreNullOrWhiteSpace</code>) is defined but never
        thrown.
      </Callout>
    </DocPage>
  );
}
