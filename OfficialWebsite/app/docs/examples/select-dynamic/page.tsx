import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";

export const metadata: Metadata = {
  title: "Example: SelectDynamic — Dynamic Projection",
  description:
    "All eight projection variants for SelectDynamic — direct, dotted reference, dotted collection, multi-level, merged, whole object, whole collection, deep reference.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/examples/select-dynamic/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/examples/select-dynamic">
      <h1>Example 11: SelectDynamic — Dynamic Field Projection</h1>
      <p>
        <Link href="/docs/extensions/select-dynamic">
          <code>SelectDynamic&lt;T&gt;</code>
        </Link>{" "}
        accepts the same field paths as{" "}
        <Link href="/docs/extensions/select"><code>Select</code></Link> but
        projects into a dynamic result whose shape mirrors the path structure.
      </p>

      <h2 id="direct-scalars">Direct scalars</h2>
      <Code lang="json">{`{ "fields": ["Id", "Name", "Price"] }`}</Code>
      <p><strong>Response shape:</strong></p>
      <Code lang="json">{`{ "Id": 7, "Name": "Laptop Pro", "Price": 1299.99 }`}</Code>

      <h2 id="dotted-reference">
        Dotted path through reference navigation (nested object)
      </h2>
      <Code lang="json">{`{
  "fields": ["Id", "Name", "Price", "Category.Name"]
}`}</Code>
      <p>
        <code>Category.Name</code> produces a nested <code>Category</code>{" "}
        object in the result.
      </p>
      <p><strong>Response shape:</strong></p>
      <Code lang="json">{`{ "Id": 7, "Name": "Laptop Pro", "Price": 1299.99, "Category": { "Name": "Electronics" } }`}</Code>

      <h2 id="dotted-collection">
        Dotted path through collection navigation (Select lambda)
      </h2>
      <Code lang="json">{`{
  "fields": ["Id", "Name", "Category.Vendors.Id"]
}`}</Code>
      <p>
        <code>Category.Vendors</code> is a collection — each element is
        projected via a <code>Select</code> lambda so only <code>Id</code> is
        extracted.
      </p>
      <p><strong>Response shape:</strong></p>
      <Code lang="json">{`{ "Id": 7, "Name": "Laptop Pro", "Category": { "Vendors": [{ "Id": 3 }, { "Id": 7 }] } }`}</Code>

      <h2 id="multi-level">
        Multi-level dotted path (reference → collection → reference)
      </h2>
      <Code lang="json">{`{
  "fields": ["Id", "Category.Vendors.Product.Name"]
}`}</Code>
      <p><strong>Response shape:</strong></p>
      <Code lang="json">{`{ "Id": 7, "Category": { "Vendors": [{ "Product": { "Name": "Laptop Pro" } }] } }`}</Code>

      <h2 id="merged">Multiple dotted fields merged under the same root segment</h2>
      <Code lang="json">{`{
  "fields": ["Id", "Category.Name", "Category.Id"]
}`}</Code>
      <p>
        <code>Category.Name</code> and <code>Category.Id</code> are merged into
        a single nested <code>Category</code> object.
      </p>
      <p><strong>Response shape:</strong></p>
      <Code lang="json">{`{ "Id": 7, "Category": { "Name": "Electronics", "Id": 5 } }`}</Code>

      <h2 id="whole-object">Whole navigation object (non-dotted)</h2>
      <Code lang="json">{`{
  "fields": ["Id", "Name", "Category"]
}`}</Code>
      <p>
        <code>Category</code> has no dot → projected as the whole object.
      </p>
      <p><strong>Response shape:</strong></p>
      <Code lang="json">{`{ "Id": 7, "Name": "Laptop Pro", "Category": { "Id": 5, "Name": "Electronics" } }`}</Code>

      <h2 id="whole-collection">Whole collection (non-dotted)</h2>
      <Code lang="json">{`{
  "fields": ["Id", "Name", "OrderItems"]
}`}</Code>
      <p><strong>Response shape:</strong></p>
      <Code lang="json">{`{ "Id": 7, "Name": "Laptop Pro", "OrderItems": [ { "Id": 1, "Quantity": 2 } ] }`}</Code>

      <h2 id="deep-reference">Deep nesting through reference navigations</h2>
      <Code lang="json">{`{
  "fields": ["Id", "Category.SubCategory.Name"]
}`}</Code>
      <p><strong>Response shape:</strong></p>
      <Code lang="json">{`{ "Id": 7, "Category": { "SubCategory": { "Name": "Laptops" } } }`}</Code>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/select-dynamic">
            SelectDynamic extension
          </Link>
        </li>
        <li>
          <Link href="/docs/examples/select">Select example (typed)</Link>
        </li>
        <li>
          <Link href="/docs/examples/filter-dynamic">
            FilterDynamic example
          </Link>
        </li>
      </ul>
    </DocPage>
  );
}
