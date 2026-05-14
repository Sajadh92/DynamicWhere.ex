import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Example: FilterDynamic",
  description:
    "Same Filter shape as the typed version — but returns FilterResult<dynamic> whose shape mirrors the selects.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/examples/filter-dynamic">
      <h1>Example 12: FilterDynamic — Full Dynamic Filter</h1>
      <p>
        Uses the same <Link href="/docs/classes/filter">
          <code>Filter</code>
        </Link>{" "}
        JSON shape as{" "}
        <Link href="/docs/examples/filter">example 7</Link>. The difference is
        the return type: <code>IQueryable</code> /{" "}
        <code>FilterResult&lt;dynamic&gt;</code> instead of{" "}
        <code>IQueryable&lt;T&gt;</code> / <code>FilterResult&lt;T&gt;</code>.
        Flowed through{" "}
        <Link href="/docs/extensions/filter-dynamic">
          <code>FilterDynamic&lt;T&gt;</code>
        </Link>
        ,{" "}
        <Link href="/docs/extensions/to-list-dynamic-filter">
          <code>ToListDynamic&lt;T&gt;(Filter)</code>
        </Link>
        , or{" "}
        <Link href="/docs/extensions/to-list-async-dynamic-filter">
          <code>ToListAsyncDynamic&lt;T&gt;(Filter)</code>
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

      <h2 id="response">
        Response shape (<code>FilterResult&lt;dynamic&gt;</code>)
      </h2>
      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 10,
  "pageCount": 5,
  "totalCount": 42,
  "data": [
    { "Id": 7, "Name": "Laptop Pro", "Price": 1299.99, "Category": { "Name": "Electronics" } }
  ],
  "queryString": null
}`}</Code>

      <Callout tone="info">
        <strong>Note:</strong> For <code>selects</code>, the same projection
        rules as <Link href="/docs/examples/select-dynamic">
          <code>SelectDynamic</code>
        </Link>{" "}
        apply: non-dotted paths are projected as-is (whole object or
        collection); dotted paths through reference navigations produce nested
        dynamic objects (<code>Category: &#123; Name: &quot;...&quot; &#125;</code>); dotted paths
        through collection navigations use <code>Select</code> lambdas to
        project individual element fields (
        <code>Category: &#123; Vendors: [&#123; Id: … &#125;] &#125;</code>).
      </Callout>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/filter-dynamic">
            FilterDynamic extension
          </Link>
        </li>
        <li>
          <Link href="/docs/examples/filter">Filter example (typed)</Link>
        </li>
        <li>
          <Link href="/docs/examples/select-dynamic">
            SelectDynamic projection variants
          </Link>
        </li>
      </ul>
    </DocPage>
  );
}
