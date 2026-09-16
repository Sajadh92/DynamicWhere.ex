import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Example: Segment — Set Operations",
  description:
    "Three ConditionSets joined by Union + Except, with order / page applied in memory to the combined result, plus the SegmentResult shape.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/examples/segment/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/examples/segment">
      <h1>Example 9: Segment — Set Operations</h1>
      <p>
        A <Link href="/docs/classes/segment"><code>Segment</code></Link>{" "}
        combines multiple <Link href="/docs/classes/condition-set">
          <code>ConditionSet</code>
        </Link>{" "}
        sources via{" "}
        <Link href="/docs/enums/intersection">
          <code>UNION</code> / <code>INTERSECT</code> / <code>EXCEPT</code>
        </Link>
        . Each set runs as its own query; the results are combined in memory,
        then ordered and paged. Run it through{" "}
        <Link href="/docs/extensions/to-list-async-segment">
          <code>ToListAsync&lt;T&gt;(Segment)</code>
        </Link>
        .
      </p>

      <h2 id="request">Request</h2>
      <Code lang="json">{`{
  "conditionSets": [
    {
      "sort": 1,
      "intersection": null,
      "conditionGroup": {
        "connector": "And",
        "conditions": [
          {
            "sort": 1,
            "field": "Category.Name",
            "dataType": "Text",
            "operator": "Equal",
            "values": ["Electronics"]
          }
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
          {
            "sort": 1,
            "field": "Price",
            "dataType": "Number",
            "operator": "LessThan",
            "values": ["20"]
          }
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
          {
            "sort": 1,
            "field": "IsActive",
            "dataType": "Boolean",
            "operator": "Equal",
            "values": ["false"]
          }
        ],
        "subConditionGroups": []
      }
    }
  ],
  "orders": [
    { "sort": 1, "field": "Name", "direction": "Ascending" }
  ],
  "page": {
    "pageNumber": 1,
    "pageSize": 20
  }
}`}</Code>

      <Callout tone="info">
        <strong>Logic:</strong> <code>(Electronics) UNION (Price &lt; 20) EXCEPT (Inactive)</code>
        {" "}→ order → paginate. The three sets are loaded separately and
        combined in memory, not by a SQL set operator.
      </Callout>

      <Callout tone="warn">
        <strong>Do not add <code>selects</code> here.</strong> A{" "}
        <code>Segment</code> projects each set <em>before</em> combining them,
        and the combination compares rows by reference. Fresh projected objects
        never match across sets, so <code>Intersect</code> returns nothing,{" "}
        <code>Except</code> removes nothing, and <code>Union</code> stops
        de-duplicating. Project after the segment instead.
      </Callout>

      <h2 id="response">Response shape (<code>SegmentResult&lt;Product&gt;</code>)</h2>
      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 20,
  "pageCount": 2,
  "totalCount": 35,
  "data": [
    {
      "id": 1,
      "name": "Adapter Cable",
      "price": 9.99,
      "isActive": true,
      "createdAt": "2024-03-02T09:15:00",
      "category": { "id": 5, "name": "Electronics" }
    }
  ],
  "queryString": null
}`}</Code>
      <p>
        With no <code>selects</code> the rows are whole entities.{" "}
        <code>queryString</code> is always <code>null</code> on a segment: the
        overload takes no <code>getQueryString</code> argument.
      </p>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/classes/segment">Segment class</Link>
        </li>
        <li>
          <Link href="/docs/classes/segment-result">SegmentResult class</Link>
        </li>
        <li>
          <Link href="/docs/validation/segment">Segment validation rules</Link>
        </li>
      </ul>
    </DocPage>
  );
}
