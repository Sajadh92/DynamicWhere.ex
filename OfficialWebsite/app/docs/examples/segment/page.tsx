import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Example: Segment — Set Operations",
  description:
    "Three ConditionSets joined by Union + Except into one query, ordered and paged in the database, plus the SegmentResult shape.",
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
        . The sets are combined into one query, which the database orders and
        pages. Run it through{" "}
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
        {" "}→ order → paginate, as one query. The Union joins the first two
        sets&apos; conditions with <code>OR</code>; the Except removes the
        inactive rows with <code>NOT EXISTS</code> on the primary key.
      </Callout>

      <Callout tone="info">
        <strong><code>selects</code> can be added.</strong> As for a filter, it
        projects the ordered page, so ordering by <code>Name</code> works whether
        or not <code>Name</code> is selected.
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
      "category": null
    }
  ],
  "queryString": null
}`}</Code>
      <p>
        With no <code>selects</code> the rows are whole entities, loaded as the
        source query loads them. A navigation is not loaded unless that query
        includes it, so <code>category</code> is <code>null</code> here.{" "}
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
