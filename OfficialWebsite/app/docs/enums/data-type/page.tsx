import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "DataType",
  description:
    "DataType enum — the logical type of a condition value. Drives parsing, coercion, and the set of legal operators per type.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/enums/data-type/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/enums/data-type">
      <h1>DataType</h1>
      <p>
        <code>DataType</code> declares the <em>logical</em> type of a{" "}
        <Link href="/docs/classes/condition"><code>Condition</code></Link>'s
        values. The library uses it to pick the right comparison expression,
        parse incoming JSON values, and validate that the chosen{" "}
        <Link href="/docs/enums/operator"><code>Operator</code></Link> is legal
        for that type.
      </p>

      <h2 id="values">Values</h2>
      <p>
        Seven logical types. Each row lists every operator the library will
        accept when paired with that type. An unsupported pairing is not caught
        by validation: the predicate builder throws a <code>LogicException</code>{" "}
        reading{" "}
        <code>
          Unsupported combination of DataType &apos;&lt;type&gt;&apos; and Operator
          &apos;&lt;op&gt;&apos;.
        </code>
      </p>

      <table>
        <thead>
          <tr>
            <th>Value</th>
            <th>Description</th>
            <th>Supported Operators</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Text</code></td>
            <td>String data.</td>
            <td>
              All text operators including the case-insensitive{" "}
              <code>I*</code> variants (<code>IEqual</code>,{" "}
              <code>IContains</code>, <code>IStartsWith</code>,{" "}
              <code>IEndsWith</code>, <code>IIn</code>, etc.),{" "}
              <code>In</code> / <code>NotIn</code>, <code>IsNull</code> /{" "}
              <code>IsNotNull</code>.
            </td>
          </tr>
          <tr>
            <td><code>Guid</code></td>
            <td>GUID stored as a string.</td>
            <td>
              <code>Equal</code>, <code>NotEqual</code>, <code>In</code>,{" "}
              <code>NotIn</code>, <code>IsNull</code>, <code>IsNotNull</code>.
            </td>
          </tr>
          <tr>
            <td><code>Number</code></td>
            <td>
              Any numeric value — <code>byte</code> through <code>decimal</code>{" "}
              (including <code>short</code>, <code>int</code>,{" "}
              <code>long</code>, <code>float</code>, <code>double</code>).
            </td>
            <td>
              <code>Equal</code>, <code>NotEqual</code>,{" "}
              <code>GreaterThan</code>, <code>GreaterThanOrEqual</code>,{" "}
              <code>LessThan</code>, <code>LessThanOrEqual</code>,{" "}
              <code>Between</code>, <code>NotBetween</code>, <code>In</code>,{" "}
              <code>NotIn</code>, <code>IsNull</code>, <code>IsNotNull</code>.
            </td>
          </tr>
          <tr>
            <td><code>Boolean</code></td>
            <td>
              <code>true</code> or <code>false</code>.
            </td>
            <td>
              <code>Equal</code>, <code>NotEqual</code>, <code>IsNull</code>,{" "}
              <code>IsNotNull</code>.
            </td>
          </tr>
          <tr>
            <td><code>DateTime</code></td>
            <td>
              Full timestamp (date + time), compared as the member&apos;s own type
              — <code>DateTime</code> or <code>DateTimeOffset</code>, nullable or
              not.
            </td>
            <td>
              <code>Equal</code>, <code>NotEqual</code>,{" "}
              <code>GreaterThan</code>, <code>GreaterThanOrEqual</code>,{" "}
              <code>LessThan</code>, <code>LessThanOrEqual</code>,{" "}
              <code>Between</code>, <code>NotBetween</code>,{" "}
              <code>IsNull</code>, <code>IsNotNull</code>.
            </td>
          </tr>
          <tr>
            <td><code>Date</code></td>
            <td>
              Date-only — compared via the <code>.Date</code> part of the
              underlying property (<code>.Value.Date</code> on a nullable one),
              so the time component is ignored.
            </td>
            <td>
              Same as <code>DateTime</code> (the comparison strips the time
              component on both sides).
            </td>
          </tr>
          <tr>
            <td><code>Enum</code></td>
            <td>
              An enum member, matched by name (any case) or by number. The column
              may store either.
            </td>
            <td>
              <code>Equal</code>, <code>NotEqual</code>, <code>In</code>,{" "}
              <code>NotIn</code>, <code>IsNull</code>, <code>IsNotNull</code>. The
              string operators pass validation but throw{" "}
              <code>ParseException</code> on an enum-typed member.
            </td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn" title="How the two date types build their predicate">
        <code>DateTime</code> and <code>Date</code> read the member&apos;s CLR type
        before building the predicate. The null guard is emitted only for a member
        that can be null, so on a non-nullable member <code>IsNull</code> answers{" "}
        <code>false</code> and <code>IsNotNull</code> answers <code>true</code>. On a
        nullable one the guard wraps the whole comparison, so a null row fails{" "}
        <code>NotEqual</code> and <code>NotBetween</code> too. A{" "}
        <code>DateTimeOffset</code> member is compared against a{" "}
        <code>DateTimeOffset</code> literal and a <code>DateTime</code> member
        against a <code>DateTime</code> literal. Before 3.1.0 every comparison on a{" "}
        <code>DateTimeOffset</code> member threw, and so did <code>Date</code> on any
        nullable date member — see{" "}
        <Link href="/docs/breaking-changes#date-member-type">breaking changes</Link>.
      </Callout>

      <Callout tone="danger" title="Send dates as ISO 8601">
        Values are parsed with the <strong>invariant</strong> culture, not the
        host&apos;s. One it cannot read — a day-first{" "}
        <code>&quot;15/09/2026&quot;</code> — is refused with{" "}
        <code>InvalidFormat</code>, and a day-first value whose day is 12 or less is
        read month-first without complaint: <code>&quot;01/09/2026&quot;</code> is
        9 January. On a <code>DateTimeOffset</code> member the value is normalized
        to UTC, and one carrying no zone is read as UTC, so <code>Date</code>{" "}
        compares the calendar day you wrote. On a <code>DateTime</code> member a
        value carrying a zone is converted to the host&apos;s local time. Send{" "}
        <code>&quot;2026-09-15&quot;</code> or{" "}
        <code>&quot;2026-09-15T12:00:00Z&quot;</code> and none of this bites — see{" "}
        <Link href="/docs/breaking-changes#date-invariant-culture">breaking changes</Link>.
      </Callout>

      <Callout tone="warn" title="Enum storage does not matter; the operator list does">
        <code>DataType.Enum</code> matches a member by name or by number, and EF
        Core translates it for an integer column as readily as for a string one.
        What the type decides is which operators work: <code>Contains</code>,{" "}
        <code>StartsWith</code>, <code>EndsWith</code> and their negations are
        accepted by validation and then throw <code>ParseException</code>{" "}
        (&ldquo;No applicable method &apos;Contains&apos; exists in type&rdquo;)
        against an enum-typed member, whatever the storage. For a{" "}
        <code>string</code> column that merely holds enum names and needs those
        operators, use <code>DataType.Text</code>.
      </Callout>

      <h2 id="json-examples">JSON examples per type</h2>

      <h3 id="text">Text</h3>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Name",
  "dataType": "Text",
  "operator": "IContains",
  "values": ["phone"]
}`}</Code>

      <h3 id="guid">Guid</h3>
      <Code lang="json">{`{
  "sort": 1,
  "field": "CustomerId",
  "dataType": "Guid",
  "operator": "Equal",
  "values": ["a1b2c3d4-e5f6-7890-abcd-ef1234567890"]
}`}</Code>

      <h3 id="number">Number</h3>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Price",
  "dataType": "Number",
  "operator": "Between",
  "values": [0, 1.569]
}`}</Code>

      <h3 id="boolean">Boolean</h3>
      <Code lang="json">{`{
  "sort": 1,
  "field": "IsActive",
  "dataType": "Boolean",
  "operator": "Equal",
  "values": [true]
}`}</Code>

      <h3 id="datetime">DateTime</h3>
      <Code lang="json">{`{
  "sort": 1,
  "field": "CreatedAt",
  "dataType": "DateTime",
  "operator": "Equal",
  "values": ["2024-06-15T14:30:00"]
}`}</Code>

      <h3 id="date">Date</h3>
      <Code lang="json">{`{
  "sort": 1,
  "field": "CreatedAt",
  "dataType": "Date",
  "operator": "GreaterThan",
  "values": ["2024-01-01"]
}`}</Code>

      <h3 id="enum">Enum</h3>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Status",
  "dataType": "Enum",
  "operator": "In",
  "values": ["Active", "Pending"]
}`}</Code>

      <h2 id="value-coercion">Value coercion</h2>
      <p>
        <code>Values</code> is <code>List&lt;object&gt;</code>. Whatever the
        front-end sends, the library normalizes each element before validating
        and building the expression.
      </p>
      <table>
        <thead>
          <tr>
            <th>Incoming runtime type</th>
            <th>Normalized form</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>string</code></td>
            <td>as-is</td>
          </tr>
          <tr>
            <td><code>bool</code></td>
            <td><code>"true"</code> / <code>"false"</code> (lowercase)</td>
          </tr>
          <tr>
            <td><code>JsonElement</code></td>
            <td>
              Unwrapped by <code>ValueKind</code>: string → text, number → raw
              JSON token, <code>True</code> / <code>False</code> → lowercase
              string.
            </td>
          </tr>
          <tr>
            <td>numeric / <code>IFormattable</code></td>
            <td><code>InvariantCulture</code> formatting</td>
          </tr>
          <tr>
            <td>anything else (e.g. <code>JValue</code>)</td>
            <td><code>value.ToString()</code></td>
          </tr>
          <tr>
            <td><code>null</code></td>
            <td><code>string.Empty</code></td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        Old clients that send <code>["true"]</code> or <code>["100"]</code>{" "}
        (quoted strings) keep working unchanged — strings deserialize into the{" "}
        <code>List&lt;object&gt;</code> as string elements and the normalizer
        passes them through.
      </Callout>

      <h2 id="csharp">C# usage</h2>
      <Code lang="csharp">{`using DynamicWhere.ex.Enums;

var condition = new Condition
{
    Sort = 1,
    Field = "Price",
    DataType = DataType.Number,
    Operator = Operator.GreaterThan,
    Values = new List<object> { 50 }
};`}</Code>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/enums/operator">Operator →</Link> all 28 operators
          and their value-count requirements.
        </li>
        <li>
          <Link href="/docs/classes/condition">Condition →</Link> the class that
          carries the <code>DataType</code>.
        </li>
        <li>
          <Link href="/docs/validation/condition">Condition validation →</Link>{" "}
          the rules used when parsing each <code>DataType</code>.
        </li>
      </ul>
    </DocPage>
  );
}
