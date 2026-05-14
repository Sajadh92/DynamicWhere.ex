import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Condition",
  description:
    "The smallest filter unit in DynamicWhere.ex — one field, one operator, and a list of values that is normalized per DataType.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/classes/condition/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/classes/condition">
      <h1>Condition</h1>
      <p>
        A <code>Condition</code> is a single filter predicate — one field, one operator, and zero
        or more operand values. It is the smallest building block of every filter, segment, or
        summary you send.
      </p>

      <h2 id="properties">Properties</h2>
      <table>
        <thead>
          <tr>
            <th>Property</th>
            <th>Type</th>
            <th>Description</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              <code>Sort</code>
            </td>
            <td>
              <code>int</code>
            </td>
            <td>
              Evaluation order within a <Link href="/docs/classes/condition-group"><code>ConditionGroup</code></Link> (must be unique among siblings).
            </td>
          </tr>
          <tr>
            <td>
              <code>Field</code>
            </td>
            <td>
              <code>string?</code>
            </td>
            <td>
              Property path on the entity (supports dot notation, e.g. <code>"Order.Customer.Name"</code>).
            </td>
          </tr>
          <tr>
            <td>
              <code>DataType</code>
            </td>
            <td>
              <Link href="/docs/enums/data-type"><code>DataType</code></Link>
            </td>
            <td>Logical data type for value parsing.</td>
          </tr>
          <tr>
            <td>
              <code>Operator</code>
            </td>
            <td>
              <Link href="/docs/enums/operator"><code>Operator</code></Link>
            </td>
            <td>Comparison operator.</td>
          </tr>
          <tr>
            <td>
              <code>Values</code>
            </td>
            <td>
              <code>List&lt;object&gt;</code>
            </td>
            <td>
              Operand values (count depends on operator). Accepts raw JSON types — strings, numbers,
              booleans — and is normalized per <code>DataType</code>. See <a href="#value-coercion">Value Coercion</a> below.
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="csharp-example">C# example</h2>
      <Code lang="csharp">{`var condition = new Condition
{
    Sort = 1,
    Field = "Name",
    DataType = DataType.Text,
    Operator = Operator.IContains,
    Values = new List<object> { "john" }
};`}</Code>

      <h2 id="json-example">JSON example</h2>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Name",
  "dataType": "Text",
  "operator": "IContains",
  "values": ["john"]
}`}</Code>

      <h2 id="value-coercion">Value coercion</h2>
      <p>
        <code>Values</code> is <code>List&lt;object&gt;</code> so the front-end can send
        heterogeneous JSON shapes without quoting every primitive:
      </p>

      <Code lang="json">{`{
  "Field": "Price",
  "DataType": "Number",
  "Operator": "Between",
  "Values": [0, 1.569]
}`}</Code>

      <Code lang="json">{`{
  "Field": "IsActive",
  "DataType": "Boolean",
  "Operator": "Equal",
  "Values": [false]
}`}</Code>

      <p>The library normalizes every element before validation/build:</p>
      <table>
        <thead>
          <tr>
            <th>Incoming runtime type</th>
            <th>Normalized form</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              <code>string</code>
            </td>
            <td>as-is</td>
          </tr>
          <tr>
            <td>
              <code>bool</code>
            </td>
            <td>
              <code>"true"</code> / <code>"false"</code> (lowercase)
            </td>
          </tr>
          <tr>
            <td>
              <code>JsonElement</code> (System.Text.Json)
            </td>
            <td>
              unwrapped by <code>ValueKind</code> (<code>String</code> → text, <code>Number</code> → raw JSON token, <code>True</code>/<code>False</code> → lowercase)
            </td>
          </tr>
          <tr>
            <td>numeric / <code>IFormattable</code></td>
            <td>
              <code>InvariantCulture</code> formatting
            </td>
          </tr>
          <tr>
            <td>
              anything else (<code>JValue</code>, etc.)
            </td>
            <td>
              <code>value.ToString()</code>
            </td>
          </tr>
          <tr>
            <td>
              <code>null</code>
            </td>
            <td>
              <code>string.Empty</code>
            </td>
          </tr>
        </tbody>
      </table>

      <Callout tone="success" title="Backward compatibility">
        Callers previously sending <code>["abc"]</code> (quoted strings) keep working unchanged —
        strings deserialize into the <code>List&lt;object&gt;</code> as string elements. C# callers
        that previously used <code>Values = new List&lt;string&gt; &#123;...&#125;</code> must switch
        to <code>new List&lt;object&gt; &#123;...&#125;</code> (or <code>.Cast&lt;object&gt;().ToList()</code>).
      </Callout>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/condition-group">ConditionGroup →</Link> the parent shape.
        </li>
        <li>
          <Link href="/docs/enums/operator">Operator reference →</Link> how many values each operator expects.
        </li>
        <li>
          <Link href="/docs/enums/data-type">DataType reference →</Link> parsing rules per type.
        </li>
        <li>
          <Link href="/docs/validation/condition">Validation →</Link> error codes a malformed condition can throw.
        </li>
      </ul>
    </DocPage>
  );
}
