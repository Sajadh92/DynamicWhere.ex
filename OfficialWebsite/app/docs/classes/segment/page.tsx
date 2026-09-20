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
        applies optional sort and pagination. A projection, if you give one, is applied{" "}
        <em>after</em> they are combined, to the ordered and paged rows.
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
            <td>
              Optional field projection, applied last to the combined, ordered and
              paged rows, as for a <code>Filter</code>.
            </td>
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
        The condition sets are combined into <strong>one query</strong> that the
        database answers: <code>Union</code> and <code>Intersect</code> join the
        sets&apos; conditions, and <code>Except</code> removes its set&apos;s rows
        by primary key. A type with no primary key uses SQL <code>UNION</code> /{" "}
        <code>INTERSECT</code> / <code>EXCEPT</code>. Segments are executed
        exclusively through{" "}
        <Link href="/docs/extensions/to-list-async-segment"><code>ToListAsync&lt;T&gt;(Segment)</code></Link>.
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

SegmentResult<Customer> result = await dbContext.Customers.ToListAsync(segment);`}</Code>

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

      <h2 id="clone">Clone</h2>
      <p>
        <code>Segment.Clone()</code> is public since <strong>3.3.0</strong>. It
        returns a deep copy — every condition set with its own condition group, the <code>Selects</code> list, each order and the page — so nothing either request is given afterwards reaches the other.
      </p>
      <p>
        Every node is new. The values a condition carries stay the caller&apos;s
        own objects, in a new list: they are scalars decoded from JSON and
        nothing in the pipeline writes to them.
      </p>
      <Code lang="csharp">{`Segment page2 = caller.Clone();
page2.Page!.PageNumber = 2;          // the caller's own segment is untouched`}</Code>
      <p>
        Reading one request again with a part changed, the next page or another
        order, used to mean rebuilding it around the caller&apos;s own clauses,
        which leaves both requests holding one condition tree: a rewrite of
        either reaches both. The library has cloned before rewriting anything
        since 3.0; callers could not until now. A branch the caller left null
        stays null.
      </p>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/segment-result">SegmentResult&lt;T&gt; →</Link>
        </li>
        <li>
          <Link href="/docs/extensions/to-list-async-segment">ToListAsync&lt;T&gt;(Segment) →</Link>
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
