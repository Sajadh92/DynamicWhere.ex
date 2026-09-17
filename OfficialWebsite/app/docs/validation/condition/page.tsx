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
              <code>Field</code>&apos;s first segment must not be a name the
              expression parser keeps for itself — <code>new</code>,{" "}
              <code>iif</code>, <code>np</code>, <code>isnull</code>,{" "}
              <code>is</code>, <code>as</code>, <code>cast</code>,{" "}
              <code>true</code>, <code>false</code>, <code>null</code>, in any
              letter case
            </td>
            <td><code>{`FieldPath[{path}]StartsWithReservedName`}</code></td>
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

      <Callout tone="warn" title="A field the parser would read as its own is refused before the lookup">
        The expression parser reads its own functions and literals before it looks
        for a member, so a path whose first segment is one of them never reaches
        the member. New in <strong>3.1.0</strong>: the library refuses it by name,
        with the first segment — trimmed — on{" "}
        <Link href="/docs/errors#subject"><code>LogicException.Subject</code></Link>.
        The check sits where any path is validated, so <code>Orders</code>,{" "}
        <code>Selects</code>, <code>GroupBy.Fields</code>,{" "}
        <code>AggregateBy.Field</code> and a <code>[DwEntity(DefaultOrder)]</code>{" "}
        entry answer the same way, guarded or not. Only the first segment counts —{" "}
        <code>Owner.New</code> names the member — and <code>it</code>,{" "}
        <code>root</code>, <code>parent</code> and every predefined type name such
        as <code>String</code> or <code>Guid</code> are ordinary members. Rename the
        CLR property and map the column with <code>[Column]</code>. See{" "}
        <Link href="/docs/breaking-changes#root-it-parent-members">breaking changes</Link>.
      </Callout>

      <Callout tone="warn" title="A date value is read the way the predicate will read it">
        Validation reads a date value exactly as the predicate builder does, with
        the member&apos;s own <code>DateTime</code>, <code>DateTimeOffset</code> or{" "}
        <code>DateOnly</code> type. The server&apos;s culture plays no part, so a
        value is accepted or refused the same way on every host, and a value that
        passes validation is one the builder can use. A deployment that{" "}
        <Link href="/docs/enums/data-type#declaring-date-formats">declares a local format</Link>{" "}
        is no different: a format whose own text the built-in readers also read is
        refused when it is configured, so no declaration can make a value depend on
        the host&apos;s time zone. ISO&nbsp;8601 (
        <code>&quot;2026-09-15&quot;</code>,{" "}
        <code>&quot;2026-09-15T12:00:00Z&quot;</code>) and year-first dates are
        accepted everywhere; <code>&quot;01/09/2026&quot;</code> is refused with{" "}
        <code>AmbiguousDateFormat</code> unless the deployment declares its order.
        See <Link href="/docs/enums/data-type">DataType</Link> and{" "}
        <Link href="/docs/breaking-changes#date-value-formats">breaking changes</Link>.
      </Callout>

      <Callout tone="warn" title="Under a strict policy, an unknown field is a policy refusal">
        On a query guarded by <code>ApplyPolicy</code> under the{" "}
        <code>Strict</code> tier, outside a dry run, a <code>Field</code> that
        names nothing on <code>T</code> does not raise <code>InvalidField</code>.
        It is refused like a field denied for every feature — a{" "}
        <code>PolicyException</code> with <code>FieldDeniedForWhere</code>, or{" "}
        <code>FieldDeniedForSegment</code> inside a segment, and{" "}
        <code>FieldPath</code> <code>&quot;*&quot;</code> — so the answer does not
        say whether the field exists. Unguarded queries, the convenience tier and a
        dry run raise <code>InvalidField</code> as before. See{" "}
        <Link href="/docs/policies/configuration#strict-refusals">what a strict refusal says</Link>.
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
