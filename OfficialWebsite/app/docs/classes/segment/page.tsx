import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Segment",
  description:
    "Combines multiple condition sets with Union / Intersect / Except, plus ordering and pagination.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/classes/segment/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/classes/segment">
      <h1>Segment</h1>
      <p>
        A <code>Segment</code> stitches together multiple{" "}
        <Link href="/docs/classes/condition-set"><code>ConditionSet</code></Link> objects with{" "}
        <code>Union</code> / <code>Intersect</code> / <code>Except</code> set operations, then
        applies optional projection, sort, and pagination.
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
              <code>ConditionSets</code>
            </td>
            <td>
              <code>List&lt;<Link href="/docs/classes/condition-set">ConditionSet</Link>&gt;</code>
            </td>
            <td>Ordered condition sets.</td>
          </tr>
          <tr>
            <td>
              <code>Selects</code>
            </td>
            <td>
              <code>List&lt;string&gt;?</code>
            </td>
            <td>Optional field projection.</td>
          </tr>
          <tr>
            <td>
              <code>Orders</code>
            </td>
            <td>
              <code>List&lt;<Link href="/docs/classes/order-by">OrderBy</Link>&gt;?</code>
            </td>
            <td>Optional sort criteria.</td>
          </tr>
          <tr>
            <td>
              <code>Page</code>
            </td>
            <td>
              <Link href="/docs/classes/page-by"><code>PageBy?</code></Link>
            </td>
            <td>Optional pagination.</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn" title="Async-only">
        Segments compile to set-operation SQL (<code>UNION</code> / <code>INTERSECT</code> /{" "}
        <code>EXCEPT</code>) and are executed exclusively through{" "}
        <Link href="/docs/extensions/to-list-async-segment"><code>ToListAsyncSegment</code></Link>.
        There is no synchronous counterpart.
      </Callout>

      <h2 id="csharp-example">C# example</h2>
      <Code lang="csharp">{`var segment = new Segment
{
    ConditionSets = new List<ConditionSet>
    {
        new ConditionSet
        {
            Sort = 0,
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
            Intersection = Intersection.Except,
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = new List<Condition>
                {
                    new Condition
                    {
                        Sort = 1, Field = "IsBlocked",
                        DataType = DataType.Boolean, Operator = Operator.Equal,
                        Values = new List<object> { true }
                    }
                }
            }
        }
    },
    Orders = new List<OrderBy>
    {
        new OrderBy { Sort = 1, Field = "Name" }
    },
    Page = new PageBy { PageNumber = 1, PageSize = 50 }
};

SegmentResult<Customer> result = await dbContext.Customers.ToListAsyncSegment(segment);`}</Code>

      <h2 id="json-example">JSON example</h2>
      <Code lang="json">{`{
  "conditionSets": [
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
      "intersection": "Except",
      "conditionGroup": {
        "connector": "And",
        "conditions": [
          { "sort": 1, "field": "IsBlocked", "dataType": "Boolean", "operator": "Equal", "values": [true] }
        ],
        "subConditionGroups": []
      }
    }
  ],
  "orders": [
    { "sort": 1, "field": "Name", "direction": "Ascending" }
  ],
  "page": { "pageNumber": 1, "pageSize": 50 }
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/segment-result">SegmentResult&lt;T&gt; →</Link>
        </li>
        <li>
          <Link href="/docs/extensions/to-list-async-segment">ToListAsyncSegment →</Link>
        </li>
        <li>
          <Link href="/docs/enums/intersection">Intersection enum →</Link>
        </li>
        <li>
          <Link href="/docs/examples/segment">Segment example →</Link>
        </li>
      </ul>
    </DocPage>
  );
}
