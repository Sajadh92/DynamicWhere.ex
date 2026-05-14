import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";

export const metadata: Metadata = {
  title: "Example: Where (single Condition)",
  description:
    "Nine single-Condition examples covering every DataType and the most common operators — Text, Number, Date, DateTime, Guid, Boolean, Enum, IsNull, In.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/examples/where-single">
      <h1>Example 2: Where (single Condition)</h1>
      <p>
        A single{" "}
        <Link href="/docs/classes/condition"><code>Condition</code></Link>{" "}
        passed to <Link href="/docs/extensions/where"><code>Where&lt;T&gt;</code></Link>
        . Each variant below shows a different{" "}
        <Link href="/docs/enums/data-type">DataType</Link> +{" "}
        <Link href="/docs/enums/operator">Operator</Link> combination.
      </p>

      <h2 id="text-icontains">Text — case-insensitive contains</h2>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Name",
  "dataType": "Text",
  "operator": "IContains",
  "values": ["phone"]
}`}</Code>

      <h2 id="number-between">Number — between range</h2>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Price",
  "dataType": "Number",
  "operator": "Between",
  "values": ["100", "500"]
}`}</Code>

      <h2 id="date-greater-than">Date — greater than</h2>
      <Code lang="json">{`{
  "sort": 1,
  "field": "CreatedAt",
  "dataType": "Date",
  "operator": "GreaterThan",
  "values": ["2024-01-01"]
}`}</Code>

      <h2 id="datetime-equal">DateTime — exact match</h2>
      <Code lang="json">{`{
  "sort": 1,
  "field": "CreatedAt",
  "dataType": "DateTime",
  "operator": "Equal",
  "values": ["2024-06-15T14:30:00"]
}`}</Code>

      <h2 id="guid-equal">Guid — equality</h2>
      <Code lang="json">{`{
  "sort": 1,
  "field": "CustomerId",
  "dataType": "Guid",
  "operator": "Equal",
  "values": ["a1b2c3d4-e5f6-7890-abcd-ef1234567890"]
}`}</Code>

      <h2 id="boolean-equal">Boolean — exact match</h2>
      <Code lang="json">{`{
  "sort": 1,
  "field": "IsActive",
  "dataType": "Boolean",
  "operator": "Equal",
  "values": ["true"]
}`}</Code>

      <h2 id="enum-in">Enum — in set</h2>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Status",
  "dataType": "Enum",
  "operator": "In",
  "values": ["Active", "Pending"]
}`}</Code>

      <h2 id="is-null">Null check</h2>
      <Code lang="json">{`{
  "sort": 1,
  "field": "DeletedAt",
  "dataType": "DateTime",
  "operator": "IsNull",
  "values": []
}`}</Code>

      <h2 id="text-iin">Text — In (multiple values)</h2>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Country",
  "dataType": "Text",
  "operator": "IIn",
  "values": ["USA", "Canada", "UK"]
}`}</Code>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/where">Where extension</Link>
        </li>
        <li>
          <Link href="/docs/examples/where-group">Where (group) example</Link>
        </li>
        <li>
          <Link href="/docs/validation/condition">Condition validation rules</Link>
        </li>
      </ul>
    </DocPage>
  );
}
