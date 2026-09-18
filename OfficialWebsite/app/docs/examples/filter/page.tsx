import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Example: Filter (Typed)",
  description:
    "A full Filter request — where + select + order + page — and the typed FilterResult<T> response shape.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/examples/filter/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/examples/filter">
      <h1>Example 7: Filter — Typed</h1>
      <p>
        A <Link href="/docs/classes/filter"><code>Filter</code></Link>{" "}
        bundles a where group, a select list, ordering, and pagination — then
        flows through <Link href="/docs/extensions/filter">
          <code>Filter&lt;T&gt;</code>
        </Link>,{" "}
        <Link href="/docs/extensions/to-list-filter">
          <code>ToList&lt;T&gt;(Filter)</code>
        </Link>{" "}
        or{" "}
        <Link href="/docs/extensions/to-list-async-filter">
          <code>ToListAsync&lt;T&gt;(Filter)</code>
        </Link>
        .
      </p>

      <h2 id="request">Request</h2>
      <Code lang="json">{`{
  "conditionGroup": {
    "connector": "And",
    "conditions": [
      {
        "sort": 1,
        "field": "Price",
        "dataType": "Number",
        "operator": "GreaterThan",
        "values": ["50"]
      },
      {
        "sort": 2,
        "field": "Category.Name",
        "dataType": "Text",
        "operator": "IEqual",
        "values": ["electronics"]
      }
    ],
    "subConditionGroups": []
  },
  "selects": ["Id", "Name", "Price", "Category.Name"],
  "orders": [
    { "sort": 1, "field": "Price", "direction": "Descending" }
  ],
  "page": {
    "pageNumber": 1,
    "pageSize": 10
  }
}`}</Code>

      <h2 id="response">Response shape (<code>FilterResult&lt;Product&gt;</code>)</h2>
      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 10,
  "pageCount": 5,
  "totalCount": 42,
  "data": [
    {
      "id": 7,
      "name": "Laptop Pro",
      "price": 1299.99,
      "isActive": false,
      "createdAt": "0001-01-01T00:00:00",
      "category": { "id": 5, "name": "Electronics" }
    }
  ],
  "queryString": null
}`}</Code>

      <Callout tone="warn">
        A typed row is a whole <code>Product</code>, not a trimmed object.{" "}
        <code>selects</code> decides which members are <em>read</em>; the rest
        are still present, holding their defaults. A selected reference
        navigation also carries its <code>Id</code> when the nested type has
        one, and a row with no category comes back as an empty{" "}
        <code>category</code> object rather than <code>null</code>. Use{" "}
        <Link href="/docs/examples/filter-dynamic">FilterDynamic</Link> when you
        want a payload containing only the requested members.
      </Callout>

      <Callout tone="info">
        Set <code>getQueryString: true</code> on the extension method to
        populate <code>queryString</code> with the generated SQL — useful in
        development.
      </Callout>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/classes/filter">Filter class</Link>
        </li>
        <li>
          <Link href="/docs/classes/filter-result">FilterResult class</Link>
        </li>
        <li>
          <Link href="/docs/examples/filter-dynamic">FilterDynamic example</Link>{" "}
          — same shape returning <code>FilterResult&lt;dynamic&gt;</code>.
        </li>
      </ul>
    </DocPage>
  );
}
