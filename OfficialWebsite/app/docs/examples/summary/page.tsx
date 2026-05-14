import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Example: Summary",
  description:
    "Full Summary request — where → group → having → order → page — plus the SummaryResult response and flattened-alias note.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/examples/summary">
      <h1>Example 8: Summary — Group + Aggregate + Having</h1>
      <p>
        A <Link href="/docs/classes/summary"><code>Summary</code></Link>{" "}
        runs <em>where → group → having → order → page</em> in one shot through{" "}
        <Link href="/docs/extensions/summary">
          <code>Summary&lt;T&gt;</code>
        </Link>,{" "}
        <Link href="/docs/extensions/to-list-summary">
          <code>ToList&lt;T&gt;(Summary)</code>
        </Link>{" "}
        or{" "}
        <Link href="/docs/extensions/to-list-async-summary">
          <code>ToListAsync&lt;T&gt;(Summary)</code>
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
        "field": "IsActive",
        "dataType": "Boolean",
        "operator": "Equal",
        "values": ["true"]
      }
    ],
    "subConditionGroups": []
  },
  "groupBy": {
    "fields": ["Category.Name"],
    "aggregateBy": [
      { "field": null, "alias": "ProductCount", "aggregator": "Count" },
      { "field": "Price", "alias": "AvgPrice", "aggregator": "Average" },
      { "field": "Price", "alias": "TotalRevenue", "aggregator": "Sumation" }
    ]
  },
  "having": {
    "connector": "And",
    "conditions": [
      {
        "sort": 1,
        "field": "ProductCount",
        "dataType": "Number",
        "operator": "GreaterThan",
        "values": ["5"]
      }
    ],
    "subConditionGroups": []
  },
  "orders": [
    { "sort": 1, "field": "TotalRevenue", "direction": "Descending" }
  ],
  "page": {
    "pageNumber": 1,
    "pageSize": 10
  }
}`}</Code>

      <h2 id="response">Response shape (<code>SummaryResult</code>)</h2>
      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 10,
  "pageCount": 1,
  "totalCount": 3,
  "data": [
    { "CategoryName": "Electronics", "ProductCount": 15, "AvgPrice": 349.99, "TotalRevenue": 5249.85 },
    { "CategoryName": "Clothing", "ProductCount": 12, "AvgPrice": 45.00, "TotalRevenue": 540.00 }
  ],
  "queryString": null
}`}</Code>

      <Callout tone="info">
        <strong>Note:</strong> Dotted GroupBy fields like{" "}
        <code>Category.Name</code> become flattened aliases in the result
        (e.g. <code>CategoryName</code>).
      </Callout>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/classes/summary">Summary class</Link>
        </li>
        <li>
          <Link href="/docs/classes/summary-result">SummaryResult class</Link>
        </li>
        <li>
          <Link href="/docs/validation/summary">Summary validation rules</Link>
        </li>
      </ul>
    </DocPage>
  );
}
