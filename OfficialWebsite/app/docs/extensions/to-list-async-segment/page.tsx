import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".ToListAsync<T>(Segment)",
  description:
    "Async-only segment entry — materialize each ConditionSet independently then apply Union / Intersect / Except in-memory before ordering and pagination.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/to-list-async-segment">
      <h1>.ToListAsync&lt;T&gt;(Segment)</h1>
      <p>
        Async-only segment operation. Executes each{" "}
        <Link href="/docs/classes/condition-set">
          <code>ConditionSet</code>
        </Link>{" "}
        independently, then applies set operations (<code>Union</code> /{" "}
        <code>Intersect</code> / <code>Except</code>), followed by ordering and
        pagination.
      </p>

      <Callout tone="warn">
        <strong>Async-only.</strong>{" "}
        <code>.ToListAsync&lt;T&gt;(Segment)</code> is the only entry point for
        segment queries — there is no synchronous{" "}
        <code>.ToList&lt;T&gt;(Segment)</code> variant. Each{" "}
        <code>ConditionSet</code> is materialized independently into memory,
        then set operations are performed in-memory.
      </Callout>

      <h2 id="signature">Signature</h2>
      <Code lang="csharp">{`public static Task<SegmentResult<T>> ToListAsync<T>(
    this IQueryable<T> query,
    Segment segment)
    where T : class, new()`}</Code>

      <table>
        <thead>
          <tr>
            <th>Parameter</th>
            <th>Type</th>
            <th>Description</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>segment</code></td>
            <td>
              <Link href="/docs/classes/segment"><code>Segment</code></Link>
            </td>
            <td>
              Composition object — <code>ConditionSets</code>,{" "}
              <code>Selects</code>, <code>Orders</code>, <code>Page</code>
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="pipeline">Pipeline</h2>
      <ul>
        <li>
          For each <code>ConditionSet</code> (in <code>Sort</code> order):
          apply its <code>ConditionGroup</code> as a <code>Where</code>, then
          materialize that set into memory.
        </li>
        <li>
          Apply set operations between consecutive results using the next set's{" "}
          <code>Intersection</code> (<code>Union</code>, <code>Intersect</code>,
          or <code>Except</code>). The first set's <code>Intersection</code> is
          ignored.
        </li>
        <li>
          Apply <code>Orders</code> on the combined in-memory result.
        </li>
        <li>
          Apply <code>Page</code> on the combined in-memory result.
        </li>
        <li>
          Optionally project via <code>Selects</code> before returning.
        </li>
      </ul>

      <h2 id="validations">Validations</h2>
      <ul>
        <li>
          <code>ConditionSets</code> <code>Sort</code> values must be unique —{" "}
          <code>SetsUniqueSort</code>.
        </li>
        <li>
          Sets at index 1+ must have <code>Intersection</code> specified —{" "}
          <code>RequiredIntersection</code>.
        </li>
        <li>
          Each <code>ConditionSet.ConditionGroup</code> is validated as in{" "}
          <Link href="/docs/extensions/where"><code>.Where&lt;T&gt;</code></Link>
          .
        </li>
        <li>
          <code>Orders</code> (if provided): each <code>Field</code> must be
          non-empty and valid on <code>T</code>.
        </li>
        <li>
          <code>Page</code> (if provided): both <code>PageNumber</code> and{" "}
          <code>PageSize</code> must be &gt; 0.
        </li>
      </ul>

      <h2 id="returns">Returns</h2>
      <p>
        <code>Task&lt;</code>
        <Link href="/docs/classes/segment-result">
          <code>SegmentResult&lt;T&gt;</code>
        </Link>
        <code>&gt;</code> — inherits all properties from{" "}
        <Link href="/docs/classes/filter-result">
          <code>FilterResult&lt;T&gt;</code>
        </Link>
        .
      </p>

      <h2 id="example">Example</h2>
      <Code lang="csharp">{`var segment = new Segment
{
    ConditionSets = new List<ConditionSet>
    {
        new ConditionSet
        {
            Sort = 1,
            Intersection = null,
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = new List<Condition>
                {
                    new Condition { Sort = 1, Field = "Category.Name", DataType = DataType.Text, Operator = Operator.Equal, Values = new List<object> { "Electronics" } }
                }
            }
        },
        new ConditionSet
        {
            Sort = 2,
            Intersection = Intersection.Union,
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = new List<Condition>
                {
                    new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.LessThan, Values = new List<object> { 20 } }
                }
            }
        },
        new ConditionSet
        {
            Sort = 3,
            Intersection = Intersection.Except,
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = new List<Condition>
                {
                    new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = new List<object> { false } }
                }
            }
        }
    },
    Selects = new List<string> { "Id", "Name", "Price" },
    Orders  = new List<OrderBy> { new OrderBy { Sort = 1, Field = "Name", Direction = Direction.Ascending } },
    Page    = new PageBy { PageNumber = 1, PageSize = 20 }
};

SegmentResult<Product> result = await dbContext.Products.ToListAsync(segment);`}</Code>

      <Code lang="json">{`{
  "conditionSets": [
    {
      "sort": 1,
      "intersection": null,
      "conditionGroup": {
        "connector": "And",
        "conditions": [
          { "sort": 1, "field": "Category.Name", "dataType": "Text", "operator": "Equal", "values": ["Electronics"] }
        ],
        "subConditionGroups": []
      }
    },
    {
      "sort": 2,
      "intersection": "Union",
      "conditionGroup": {
        "connector": "And",
        "conditions": [
          { "sort": 1, "field": "Price", "dataType": "Number", "operator": "LessThan", "values": ["20"] }
        ],
        "subConditionGroups": []
      }
    },
    {
      "sort": 3,
      "intersection": "Except",
      "conditionGroup": {
        "connector": "And",
        "conditions": [
          { "sort": 1, "field": "IsActive", "dataType": "Boolean", "operator": "Equal", "values": ["false"] }
        ],
        "subConditionGroups": []
      }
    }
  ],
  "selects": ["Id", "Name", "Price"],
  "orders": [
    { "sort": 1, "field": "Name", "direction": "Ascending" }
  ],
  "page": { "pageNumber": 1, "pageSize": 20 }
}`}</Code>

      <p>
        <strong>Logic:</strong>{" "}
        <code>(Electronics) UNION (Price &lt; 20) EXCEPT (Inactive)</code> →
        order → paginate.
      </p>

      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 20,
  "pageCount": 2,
  "totalCount": 35,
  "data": [
    { "id": 1, "name": "Adapter Cable", "price": 9.99 }
  ],
  "queryString": null
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/segment"><code>Segment</code></Link> shape.
        </li>
        <li>
          <Link href="/docs/classes/condition-set">
            <code>ConditionSet</code>
          </Link>{" "}
          shape and{" "}
          <Link href="/docs/enums/intersection">
            <code>Intersection</code>
          </Link>{" "}
          values.
        </li>
        <li>
          <Link href="/docs/validation/segment">Segment validation</Link>.
        </li>
        <li>
          <Link href="/docs/examples/segment">JSON Cookbook: Segment</Link>.
        </li>
      </ul>
    </DocPage>
  );
}
