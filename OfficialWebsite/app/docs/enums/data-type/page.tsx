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
        accept when paired with that type — using an unsupported operator
        throws a validation error.
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
            <td>Full timestamp (date + time).</td>
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
              underlying property, so the time component is ignored.
            </td>
            <td>
              Same as <code>DateTime</code> (the comparison strips the time
              component on both sides).
            </td>
          </tr>
          <tr>
            <td><code>Enum</code></td>
            <td>
              Enum stored as a <strong>string</strong> in the database (not as
              an integer).
            </td>
            <td>
              <code>Equal</code>, <code>NotEqual</code>, <code>Contains</code>,{" "}
              <code>NotContains</code>, <code>StartsWith</code>,{" "}
              <code>EndsWith</code>, <code>NotStartsWith</code>,{" "}
              <code>NotEndsWith</code>, <code>In</code>, <code>NotIn</code>,{" "}
              <code>IsNull</code>, <code>IsNotNull</code>.
            </td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn" title="Enum storage matters">
        <code>DataType.Enum</code> assumes enum values are stored as strings.
        If your EF Core configuration persists enums as integers (the default),
        filter with <code>DataType.Number</code> instead and send the numeric
        underlying value.
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
