import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Connector",
  description:
    "Connector enum — the logical glue (And / Or) that joins sibling conditions inside a ConditionGroup.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/enums/connector">
      <h1>Connector</h1>
      <p>
        <code>Connector</code> is the logical operator that joins the children
        of a{" "}
        <Link href="/docs/classes/condition-group"><code>ConditionGroup</code></Link>.
        It applies to both flat <code>Conditions</code> and nested{" "}
        <code>SubConditionGroups</code> within the same group.
      </p>

      <h2 id="values">Values</h2>
      <table>
        <thead>
          <tr>
            <th>Value</th>
            <th>Description</th>
            <th>C# equivalent</th>
            <th>SQL equivalent</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>And</code></td>
            <td>All children must evaluate to true.</td>
            <td><code>&amp;&amp;</code></td>
            <td><code>AND</code></td>
          </tr>
          <tr>
            <td><code>Or</code></td>
            <td>At least one child must evaluate to true.</td>
            <td><code>||</code></td>
            <td><code>OR</code></td>
          </tr>
        </tbody>
      </table>

      <h2 id="json-and">JSON example — And</h2>
      <p>An <code>And</code> group where both predicates must hold.</p>
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
      "values": [18]
    }
  ],
  "subConditionGroups": []
}`}</Code>

      <p>Generated SQL (illustrative):</p>
      <Code lang="bash">{`WHERE LOWER(Name) LIKE '%john%' AND Age >= 18`}</Code>

      <h2 id="json-or">JSON example — Or</h2>
      <p>An <code>Or</code> group — either predicate is enough.</p>
      <Code lang="json">{`{
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
}`}</Code>

      <p>Generated SQL (illustrative):</p>
      <Code lang="bash">{`WHERE Role = 'Admin' OR Role = 'Manager'`}</Code>

      <h2 id="nesting">Mixing connectors via nesting</h2>
      <p>
        A single <code>ConditionGroup</code> has exactly one{" "}
        <code>Connector</code>. To express compound logic like{" "}
        <code>A AND (B OR C)</code>, put the <code>Or</code> branch in a{" "}
        <code>SubConditionGroup</code>.
      </p>
      <Code lang="json">{`{
  "connector": "And",
  "conditions": [
    {
      "sort": 1,
      "field": "IsActive",
      "dataType": "Boolean",
      "operator": "Equal",
      "values": [true]
    }
  ],
  "subConditionGroups": [
    {
      "sort": 1,
      "connector": "Or",
      "conditions": [
        { "sort": 1, "field": "Role", "dataType": "Text", "operator": "Equal", "values": ["Admin"] },
        { "sort": 2, "field": "Role", "dataType": "Text", "operator": "Equal", "values": ["Manager"] }
      ],
      "subConditionGroups": []
    }
  ]
}`}</Code>

      <p>Equivalent SQL:</p>
      <Code lang="bash">{`WHERE IsActive = 1 AND (Role = 'Admin' OR Role = 'Manager')`}</Code>

      <Callout tone="note">
        Sub-groups can nest to unlimited depth. The library validates that{" "}
        <code>Sort</code> values are unique within each level of conditions and
        sub-groups.
      </Callout>

      <h2 id="csharp">C# usage</h2>
      <Code lang="csharp">{`using DynamicWhere.ex.Enums;

var group = new ConditionGroup
{
    Connector = Connector.And,
    Conditions = new List<Condition>
    {
        new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = new List<object> { true } }
    }
};`}</Code>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/classes/condition-group">ConditionGroup →</Link> the
          class that carries the connector.
        </li>
        <li>
          <Link href="/docs/examples/where-group">Where (group) example →</Link>{" "}
          end-to-end JSON cookbook.
        </li>
      </ul>
    </DocPage>
  );
}
