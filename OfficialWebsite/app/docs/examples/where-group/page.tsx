import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Example: Where (ConditionGroup)",
  description:
    "A flat AND group plus an AND group with a nested OR sub-group, including the equivalent SQL.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/examples/where-group/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/examples/where-group">
      <h1>Example 3: Where (ConditionGroup)</h1>
      <p>
        A <Link href="/docs/classes/condition-group">
          <code>ConditionGroup</code>
        </Link>{" "}
        passed to{" "}
        <Link href="/docs/extensions/where"><code>Where&lt;T&gt;</code></Link>.
        Groups can nest indefinitely via <code>SubConditionGroups</code>.
      </p>

      <h2 id="and-group">AND group</h2>
      <Code lang="json">{`{
  "connector": "And",
  "conditions": [
    {
      "sort": 1,
      "field": "Name",
      "dataType": "Text",
      "operator": "IContains",
      "values": ["john"]
    },
    {
      "sort": 2,
      "field": "Age",
      "dataType": "Number",
      "operator": "GreaterThanOrEqual",
      "values": ["18"]
    }
  ],
  "subConditionGroups": []
}`}</Code>

      <h2 id="nested">Nested groups (AND with nested OR)</h2>
      <Code lang="json">{`{
  "connector": "And",
  "conditions": [
    {
      "sort": 1,
      "field": "IsActive",
      "dataType": "Boolean",
      "operator": "Equal",
      "values": ["true"]
    }
  ],
  "subConditionGroups": [
    {
      "sort": 1,
      "connector": "Or",
      "conditions": [
        {
          "sort": 1,
          "field": "Role",
          "dataType": "Text",
          "operator": "Equal",
          "values": ["Admin"]
        },
        {
          "sort": 2,
          "field": "Role",
          "dataType": "Text",
          "operator": "Equal",
          "values": ["Manager"]
        }
      ],
      "subConditionGroups": []
    }
  ]
}`}</Code>

      <Callout tone="info">
        <strong>Equivalent SQL:</strong>
      </Callout>
      <Code lang="sql">{`WHERE IsActive = true AND (Role = 'Admin' OR Role = 'Manager')`}</Code>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/classes/condition-group">ConditionGroup class</Link>
        </li>
        <li>
          <Link href="/docs/extensions/where">Where extension</Link>
        </li>
        <li>
          <Link href="/docs/validation/condition-group">
            ConditionGroup validation rules
          </Link>
        </li>
      </ul>
    </DocPage>
  );
}
