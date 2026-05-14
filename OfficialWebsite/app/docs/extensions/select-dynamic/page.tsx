import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".SelectDynamic<T>(fields)",
  description:
    "Project an IQueryable<T> into a dynamic IQueryable using string-based Select — nested dynamic objects mirror the navigation hierarchy through reference and collection paths.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/extensions/select-dynamic/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/select-dynamic">
      <h1>.SelectDynamic&lt;T&gt;(fields)</h1>
      <p>
        Projects only the specified fields using{" "}
        <code>System.Linq.Dynamic.Core</code>'s string-based{" "}
        <code>Select</code>, returning a non-generic dynamic{" "}
        <code>IQueryable</code>. Dotted navigation paths are projected as{" "}
        <strong>nested dynamic objects</strong> that mirror the navigation
        hierarchy, including through collection properties.
      </p>

      <h2 id="signature">Signature</h2>
      <Code lang="csharp">{`public static IQueryable SelectDynamic<T>(this IQueryable<T> query, List<string> fields)
    where T : class`}</Code>

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
            <td><code>query</code></td>
            <td><code>IQueryable&lt;T&gt;</code></td>
            <td>Source query to project</td>
          </tr>
          <tr>
            <td><code>fields</code></td>
            <td><code>List&lt;string&gt;</code></td>
            <td>Property paths to include</td>
          </tr>
        </tbody>
      </table>

      <h2 id="projection-rules">Projection rules</h2>
      <table>
        <thead>
          <tr>
            <th>Path style</th>
            <th>Behaviour</th>
            <th>Example input</th>
            <th>Dynamic result</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>Non-dotted scalar</td>
            <td>Projected as-is</td>
            <td><code>"Name"</code></td>
            <td><code>Name: "Laptop"</code></td>
          </tr>
          <tr>
            <td>Non-dotted object</td>
            <td>Projected as-is (whole object)</td>
            <td><code>"Category"</code></td>
            <td>
              <code>{`Category: { Id: 5, Name: "Electronics" }`}</code>
            </td>
          </tr>
          <tr>
            <td>Non-dotted collection</td>
            <td>Projected as-is (whole collection)</td>
            <td><code>"Brands"</code></td>
            <td>
              <code>{`Brands: [{ Id: 1, … }]`}</code>
            </td>
          </tr>
          <tr>
            <td>Dotted through reference navigation</td>
            <td>Nested object per segment</td>
            <td><code>"Category.Name"</code></td>
            <td>
              <code>{`Category: { Name: "Electronics" }`}</code>
            </td>
          </tr>
          <tr>
            <td>Dotted through collection navigation</td>
            <td>
              <code>Select</code> lambda per collection segment
            </td>
            <td><code>"Category.Vendors.Id"</code></td>
            <td>
              <code>{`Category: { Vendors: [{ Id: 1 }] }`}</code>
            </td>
          </tr>
          <tr>
            <td>Multi-level dotted (reference + collection)</td>
            <td>
              Mixed nesting and <code>Select</code> lambdas
            </td>
            <td><code>"Category.Vendors.Product.Name"</code></td>
            <td>
              <code>{`Category: { Vendors: [{ Product: { Name: "…" } }] }`}</code>
            </td>
          </tr>
          <tr>
            <td>Nested collections (any depth)</td>
            <td>
              <code>Select</code> lambda at each collection level
            </td>
            <td><code>"A.ListB.ListC.Name"</code></td>
            <td>
              <code>{`A: { ListB: [{ ListC: [{ Name: "…" }] }] }`}</code>
            </td>
          </tr>
          <tr>
            <td>Multi-level dotted (deep reference)</td>
            <td>Deeply nested objects</td>
            <td><code>"Category.SubCategory.Name"</code></td>
            <td>
              <code>{`Category: { SubCategory: { Name: "Laptops" } }`}</code>
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="merging">Merging rule</h2>
      <p>
        Multiple dotted fields sharing the same root segment are merged into
        the same nested object:
      </p>
      <Code lang="json">{`["Category.Name", "Category.Id"]`}</Code>
      <p>
        produces <code>{`Category: { Name: "...", Id: 5 }`}</code>.
      </p>

      <h2 id="precedence">Whole-vs-sub-field precedence</h2>
      <Callout tone="warn">
        When both a whole-navigation field (e.g., <code>"Category"</code>) and
        sub-field paths sharing the same root segment (e.g.,{" "}
        <code>"Category.Name"</code>) are requested, the{" "}
        <strong>sub-field projection takes precedence</strong> and the
        whole-navigation entry is silently dropped.
      </Callout>

      <h2 id="validations">Validations</h2>
      <ul>
        <li><code>query</code> and <code>fields</code> cannot be null.</li>
        <li><code>fields</code> must have at least one entry.</li>
        <li>
          Every field must exist on <code>T</code> (case-insensitive,
          auto-normalized).
        </li>
      </ul>

      <Callout tone="note">
        Unlike{" "}
        <Link href="/docs/extensions/select"><code>.Select&lt;T&gt;</code></Link>
        , this method does <strong>not</strong> require a parameterless
        constructor on <code>T</code>.
      </Callout>

      <h2 id="returns">Returns</h2>
      <p>
        <code>IQueryable</code> — a dynamic projected query where each element
        is an anonymous object.
      </p>

      <h2 id="examples">Examples</h2>

      <p><strong>Direct scalars.</strong></p>
      <Code lang="csharp">{`var dynQuery = dbContext.Products
    .SelectDynamic(new List<string> { "Id", "Name", "Price" });`}</Code>
      <Code lang="json">{`{ "Id": 7, "Name": "Laptop Pro", "Price": 1299.99 }`}</Code>

      <p>
        <strong>Dotted path through reference navigation (nested object).</strong>
      </p>
      <Code lang="csharp">{`var dynQuery = dbContext.Products
    .SelectDynamic(new List<string> { "Id", "Name", "Price", "Category.Name" });`}</Code>
      <Code lang="json">{`{ "Id": 7, "Name": "Laptop Pro", "Price": 1299.99, "Category": { "Name": "Electronics" } }`}</Code>

      <p>
        <strong>Dotted path through collection navigation (Select lambda).</strong>
      </p>
      <Code lang="csharp">{`var dynQuery = dbContext.Products
    .SelectDynamic(new List<string> { "Id", "Name", "Category.Vendors.Id" });`}</Code>
      <Code lang="json">{`{ "Id": 7, "Name": "Laptop Pro", "Category": { "Vendors": [{ "Id": 3 }, { "Id": 7 }] } }`}</Code>

      <p>
        <strong>Multi-level dotted (reference → collection → reference).</strong>
      </p>
      <Code lang="csharp">{`var dynQuery = dbContext.Products
    .SelectDynamic(new List<string> { "Id", "Category.Vendors.Product.Name" });`}</Code>
      <Code lang="json">{`{ "Id": 7, "Category": { "Vendors": [{ "Product": { "Name": "Laptop Pro" } }] } }`}</Code>

      <p><strong>Merged sub-fields.</strong></p>
      <Code lang="csharp">{`var dynQuery = dbContext.Products
    .SelectDynamic(new List<string> { "Id", "Category.Name", "Category.Id" });`}</Code>
      <Code lang="json">{`{ "Id": 7, "Category": { "Name": "Electronics", "Id": 5 } }`}</Code>

      <p><strong>Whole navigation object.</strong></p>
      <Code lang="csharp">{`var dynQuery = dbContext.Products
    .SelectDynamic(new List<string> { "Id", "Name", "Category" });`}</Code>
      <Code lang="json">{`{ "Id": 7, "Name": "Laptop Pro", "Category": { "Id": 5, "Name": "Electronics" } }`}</Code>

      <p><strong>Whole collection.</strong></p>
      <Code lang="csharp">{`var dynQuery = dbContext.Products
    .SelectDynamic(new List<string> { "Id", "Name", "OrderItems" });`}</Code>
      <Code lang="json">{`{ "Id": 7, "Name": "Laptop Pro", "OrderItems": [ { "Id": 1, "Quantity": 2 } ] }`}</Code>

      <p><strong>Deep reference nesting.</strong></p>
      <Code lang="csharp">{`var dynQuery = dbContext.Products
    .SelectDynamic(new List<string> { "Id", "Category.SubCategory.Name" });`}</Code>
      <Code lang="json">{`{ "Id": 7, "Category": { "SubCategory": { "Name": "Laptops" } } }`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/select"><code>.Select&lt;T&gt;</code></Link>{" "}
          — strongly-typed variant returning <code>IQueryable&lt;T&gt;</code>.
        </li>
        <li>
          <Link href="/docs/extensions/filter-dynamic">
            <code>.FilterDynamic&lt;T&gt;</code>
          </Link>{" "}
          — apply <code>where → order → page → dynamic select</code>.
        </li>
        <li>
          <Link href="/docs/examples/select-dynamic">
            JSON Cookbook: SelectDynamic
          </Link>
          .
        </li>
      </ul>
    </DocPage>
  );
}
