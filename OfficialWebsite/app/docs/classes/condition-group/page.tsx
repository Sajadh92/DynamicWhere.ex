import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "ConditionGroup",
  description:
    "A logical And/Or grouping of conditions and nested sub-groups — unlimited depth.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/classes/condition-group/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/classes/condition-group">
      <h1>ConditionGroup</h1>
      <p>
        A <code>ConditionGroup</code> joins zero or more{" "}
        <Link href="/docs/classes/condition"><code>Condition</code></Link> objects and zero or more
        nested <code>ConditionGroup</code> objects with a single logical{" "}
        <Link href="/docs/enums/connector"><code>Connector</code></Link> (<code>And</code> or{" "}
        <code>Or</code>). Groups can be nested to unlimited depth.
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
            <td>Evaluation order among sibling sub-groups.</td>
          </tr>
          <tr>
            <td>
              <code>Connector</code>
            </td>
            <td>
              <Link href="/docs/enums/connector"><code>Connector</code></Link>
            </td>
            <td>
              Logical operator joining children (<code>And</code> / <code>Or</code>).
            </td>
          </tr>
          <tr>
            <td>
              <code>Conditions</code>
            </td>
            <td>
              <code>List&lt;Condition&gt;</code>
            </td>
            <td>Flat conditions in this group.</td>
          </tr>
          <tr>
            <td>
              <code>SubConditionGroups</code>
            </td>
            <td>
              <code>List&lt;ConditionGroup&gt;</code>
            </td>
            <td>Nested condition groups (unlimited depth).</td>
          </tr>
        </tbody>
      </table>

      <h2 id="csharp-example">C# example</h2>
      <Code lang="csharp">{`var group = new ConditionGroup
{
    Connector = Connector.And,
    Conditions = new List<Condition>
    {
        new Condition
        {
            Sort = 1,
            Field = "IsActive",
            DataType = DataType.Boolean,
            Operator = Operator.Equal,
            Values = new List<object> { true }
        }
    },
    SubConditionGroups = new List<ConditionGroup>
    {
        new ConditionGroup
        {
            Sort = 1,
            Connector = Connector.Or,
            Conditions = new List<Condition>
            {
                new Condition
                {
                    Sort = 1, Field = "Country",
                    DataType = DataType.Text, Operator = Operator.Equal,
                    Values = new List<object> { "IQ" }
                },
                new Condition
                {
                    Sort = 2, Field = "Country",
                    DataType = DataType.Text, Operator = Operator.Equal,
                    Values = new List<object> { "SA" }
                }
            }
        }
    }
};`}</Code>

      <h2 id="json-example">JSON example with nested groups</h2>
      <p>
        Reads as <code>IsActive = true AND (Country = "IQ" OR Country = "SA")</code>:
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
        { "sort": 1, "field": "Country", "dataType": "Text", "operator": "Equal", "values": ["IQ"] },
        { "sort": 2, "field": "Country", "dataType": "Text", "operator": "Equal", "values": ["SA"] }
      ],
      "subConditionGroups": []
    }
  ]
}`}</Code>

      <Callout tone="info" title="Sort matters">
        Sort values must be unique per level. They determine evaluation order — useful when readers
        of the saved JSON care about predicate order.
      </Callout>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/condition">Condition →</Link>
        </li>
        <li>
          <Link href="/docs/classes/filter">Filter →</Link> wraps a ConditionGroup as its where-clause.
        </li>
        <li>
          <Link href="/docs/classes/summary">Summary →</Link> uses a ConditionGroup for both <code>where</code> and <code>having</code>.
        </li>
        <li>
          <Link href="/docs/validation/condition-group">Validation →</Link>
        </li>
      </ul>
    </DocPage>
  );
}
