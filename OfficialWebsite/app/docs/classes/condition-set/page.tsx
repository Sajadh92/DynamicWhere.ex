import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "ConditionSet",
  description:
    "One member of a Segment — a filter plus the set operation that joins it with the previous set's result.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/classes/condition-set/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/classes/condition-set">
      <h1>ConditionSet</h1>
      <p>
        A <code>ConditionSet</code> is one member of a{" "}
        <Link href="/docs/classes/segment"><code>Segment</code></Link>. Each set carries its own
        <code> ConditionGroup</code> filter, and every set after the first specifies an{" "}
        <Link href="/docs/enums/intersection"><code>Intersection</code></Link>{" "}
        (<code>Union</code> / <code>Intersect</code> / <code>Except</code>) that joins it with the
        previous set's result.
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
              Execution order (must be unique). The first set's <code>Intersection</code> is ignored.
            </td>
          </tr>
          <tr>
            <td>
              <code>Intersection</code>
            </td>
            <td>
              <Link href="/docs/enums/intersection"><code>Intersection?</code></Link>
            </td>
            <td>
              Set operation to apply with the previous set's result. <strong>Required for index 1+</strong>.
            </td>
          </tr>
          <tr>
            <td>
              <code>ConditionGroup</code>
            </td>
            <td>
              <Link href="/docs/classes/condition-group"><code>ConditionGroup</code></Link>
            </td>
            <td>The filter for this set.</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn" title="Intersection rules">
        <ul>
          <li>The set with the lowest <code>Sort</code> is the seed — its <code>Intersection</code> is ignored.</li>
          <li>Every other set <strong>must</strong> provide an <code>Intersection</code>. Omitting it triggers a validation error.</li>
        </ul>
      </Callout>

      <h2 id="csharp-example">C# example</h2>
      <Code lang="csharp">{`var sets = new List<ConditionSet>
{
    new ConditionSet
    {
        Sort = 0,
        // Intersection ignored for the first set
        ConditionGroup = new ConditionGroup
        {
            Connector = Connector.And,
            Conditions = new List<Condition>
            {
                new Condition
                {
                    Sort = 1, Field = "Country",
                    DataType = DataType.Text, Operator = Operator.Equal,
                    Values = new List<object> { "IQ" }
                }
            }
        }
    },
    new ConditionSet
    {
        Sort = 1,
        Intersection = Intersection.Union,
        ConditionGroup = new ConditionGroup
        {
            Connector = Connector.And,
            Conditions = new List<Condition>
            {
                new Condition
                {
                    Sort = 1, Field = "IsVip",
                    DataType = DataType.Boolean, Operator = Operator.Equal,
                    Values = new List<object> { true }
                }
            }
        }
    }
};`}</Code>

      <h2 id="json-example">JSON example</h2>
      <Code lang="json">{`[
  {
    "sort": 0,
    "intersection": null,
    "conditionGroup": {
      "connector": "And",
      "conditions": [
        { "sort": 1, "field": "Country", "dataType": "Text", "operator": "Equal", "values": ["IQ"] }
      ],
      "subConditionGroups": []
    }
  },
  {
    "sort": 1,
    "intersection": "Union",
    "conditionGroup": {
      "connector": "And",
      "conditions": [
        { "sort": 1, "field": "IsVip", "dataType": "Boolean", "operator": "Equal", "values": [true] }
      ],
      "subConditionGroups": []
    }
  }
]`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/segment">Segment →</Link>
        </li>
        <li>
          <Link href="/docs/enums/intersection">Intersection enum →</Link>
        </li>
        <li>
          <Link href="/docs/extensions/to-list-async-segment">ToListAsyncSegment →</Link>
        </li>
      </ul>
    </DocPage>
  );
}
