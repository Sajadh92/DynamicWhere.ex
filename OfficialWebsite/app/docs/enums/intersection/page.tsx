import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Intersection",
  description:
    "Intersection enum — the set operation (Union / Intersect / Except) applied between condition sets in a Segment.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/enums/intersection/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/enums/intersection">
      <h1>Intersection</h1>
      <p>
        <code>Intersection</code> is the set operation applied between
        consecutive{" "}
        <Link href="/docs/classes/condition-set"><code>ConditionSet</code></Link>{" "}
        results inside a{" "}
        <Link href="/docs/classes/segment"><code>Segment</code></Link>. The sets
        are combined into <strong>one query</strong> the database answers.{" "}
        <code>Union</code> and <code>Intersect</code> combine the sets&apos; own
        conditions, and only <code>Except</code> matches rows, by primary key.
      </p>

      <h2 id="values">Values</h2>
      <table>
        <thead>
          <tr>
            <th>Value</th>
            <th>Description</th>
            <th>Generated as</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Union</code></td>
            <td>Combines both sets — every row from either side, once.</td>
            <td><code>OR</code> of the sets&apos; conditions</td>
          </tr>
          <tr>
            <td><code>Intersect</code></td>
            <td>Keeps only rows present in both sets.</td>
            <td><code>AND</code> of the sets&apos; conditions</td>
          </tr>
          <tr>
            <td><code>Except</code></td>
            <td>Removes rows found in the second set from the first.</td>
            <td><code>NOT EXISTS</code> on the primary key</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn">
        A type with no primary key has no row identity to match on, so its sets
        use SQL <code>UNION</code> / <code>INTERSECT</code> /{" "}
        <code>EXCEPT</code> instead. Those compare whole rows: identical rows
        collapse into one, and a column the database cannot compare, such as
        PostgreSQL <code>json</code>, fails the query even when it is not
        selected.
      </Callout>

      <h2 id="ordering">Ordering and the first set</h2>
      <p>
        Sets execute in <code>Sort</code> order. The <code>Intersection</code>{" "}
        on the <strong>first</strong> set (lowest <code>Sort</code>) is{" "}
        <em>ignored</em> — there is nothing to combine it with. Sets at index{" "}
        <code>1+</code> <strong>must</strong> have an{" "}
        <code>Intersection</code>, otherwise validation throws{" "}
        <code>RequiredIntersection</code>.
      </p>

      <Callout tone="warn">
        Set operations are <strong>async-only</strong>. The only entry point is{" "}
        <code>.ToListAsync&lt;T&gt;(Segment)</code>. Sets combine left to right:{" "}
        <code>((set1 op2 set2) op3 set3)</code>.
      </Callout>

      <h2 id="json">JSON example — Union then Except</h2>
      <p>
        Three sets: <code>Electronics</code> ∪ <code>Price &lt; 20</code> ∖{" "}
        <code>Inactive</code>.
      </p>
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
          { "sort": 1, "field": "Price", "dataType": "Number", "operator": "LessThan", "values": [20] }
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
          { "sort": 1, "field": "IsActive", "dataType": "Boolean", "operator": "Equal", "values": [false] }
        ],
        "subConditionGroups": []
      }
    }
  ],
  "orders": [ { "sort": 1, "field": "Name", "direction": "Ascending" } ],
  "page": { "pageNumber": 1, "pageSize": 20 }
}`}</Code>

      <p>Logic:</p>
      <Code lang="bash">{`(Electronics) UNION (Price < 20) EXCEPT (Inactive)
  → order by Name ASC
  → page 1, size 20`}</Code>

      <h2 id="csharp">C# usage</h2>
      <Code lang="csharp">{`using DynamicWhere.ex.Enums;

var segment = new Segment
{
    ConditionSets = new List<ConditionSet>
    {
        new ConditionSet { Sort = 1, Intersection = null, ConditionGroup = setA },
        new ConditionSet { Sort = 2, Intersection = Intersection.Union,  ConditionGroup = setB },
        new ConditionSet { Sort = 3, Intersection = Intersection.Except, ConditionGroup = setC },
    }
};

SegmentResult<Product> result = await db.Products.ToListAsync(segment);`}</Code>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/classes/segment">Segment →</Link> the wrapper class
          that holds the condition sets.
        </li>
        <li>
          <Link href="/docs/classes/condition-set">ConditionSet →</Link> a
          single member of the set.
        </li>
        <li>
          <Link href="/docs/extensions/to-list-async-segment">
            .ToListAsync(Segment) →
          </Link>{" "}
          the only extension that executes segments.
        </li>
        <li>
          <Link href="/docs/examples/segment">Segment example →</Link>{" "}
          end-to-end JSON cookbook.
        </li>
      </ul>
    </DocPage>
  );
}
