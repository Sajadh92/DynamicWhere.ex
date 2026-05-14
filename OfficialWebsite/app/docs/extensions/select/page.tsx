import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".Select<T>(fields)",
  description:
    "Project an IQueryable<T> into a new instance of T using a list of property paths — direct, whole-navigation, or dotted through reference and collection navigations.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/select">
      <h1>.Select&lt;T&gt;(fields)</h1>
      <p>
        Projects only the specified fields into a new instance of{" "}
        <code>T</code>. Supports direct properties, whole navigation
        objects/collections, and nested navigation paths — including paths that
        traverse collection properties.
      </p>

      <h2 id="signature">Signature</h2>
      <Code lang="csharp">{`public static IQueryable<T> Select<T>(this IQueryable<T> query, List<string> fields)
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
            <th>Effect on result</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>Direct scalar</td>
            <td>Bound directly</td>
            <td><code>"Name"</code></td>
            <td><code>Name: "Laptop"</code></td>
          </tr>
          <tr>
            <td>Whole navigation object (non-dotted)</td>
            <td>Bound as-is</td>
            <td><code>"Category"</code></td>
            <td>
              <code>{`Category: { Id: 5, Name: "Electronics", … }`}</code>
            </td>
          </tr>
          <tr>
            <td>Whole navigation collection (non-dotted)</td>
            <td>Bound as-is</td>
            <td><code>"Brands"</code></td>
            <td>
              <code>{`Brands: [{ Id: 1, … }, …]`}</code>
            </td>
          </tr>
          <tr>
            <td>Dotted through reference navigation</td>
            <td>Recursively projected</td>
            <td><code>"Category.Name"</code></td>
            <td>
              <code>{`Category: { Id: …, Name: "Electronics" }`}</code>
            </td>
          </tr>
          <tr>
            <td>Dotted through collection navigation</td>
            <td>
              Per-element <code>Select().ToList()</code>
            </td>
            <td><code>"Category.Vendors.Id"</code></td>
            <td>
              <code>{`Category: { Vendors: [{ Id: 1 }, …] }`}</code>
            </td>
          </tr>
          <tr>
            <td>Multi-level (reference + collection)</td>
            <td>Nested recursively</td>
            <td><code>"Category.Vendors.Product.Name"</code></td>
            <td>
              <code>{`Category: { Vendors: [{ Product: { Name: "…" } }] }`}</code>
            </td>
          </tr>
        </tbody>
      </table>

      <Callout tone="note">
        For nested entities (reference or collection), the <code>Id</code>{" "}
        property is always automatically included alongside any requested
        sub-fields.
      </Callout>

      <h2 id="validations">Validations</h2>
      <ul>
        <li><code>query</code> and <code>fields</code> cannot be null.</li>
        <li><code>fields</code> must have at least one entry.</li>
        <li>
          Every field must exist on <code>T</code> (case-insensitive,
          auto-normalized).
        </li>
        <li>
          <code>T</code> must have a parameterless constructor.
        </li>
      </ul>

      <Callout tone="warn">
        <strong>Parameterless constructor required.</strong>{" "}
        <code>.Select&lt;T&gt;(fields)</code> requires <code>T</code> to have a
        parameterless (default) constructor. If <code>T</code> does not have
        one, a <code>LogicException</code> is thrown. Most EF Core entity
        classes have parameterless constructors by default. If your type does
        not, use{" "}
        <Link href="/docs/extensions/select-dynamic">
          <code>.SelectDynamic&lt;T&gt;</code>
        </Link>{" "}
        instead.
      </Callout>

      <h2 id="returns">Returns</h2>
      <p>
        <code>IQueryable&lt;T&gt;</code> — a projected query. New instances of{" "}
        <code>T</code> are constructed via the parameterless constructor and
        member-bound from the requested fields.
      </p>

      <h2 id="examples">Examples</h2>

      <p><strong>Direct scalars.</strong></p>
      <Code lang="csharp">{`var projected = dbContext.Products
    .Select(new List<string> { "Id", "Name", "Price" });`}</Code>

      <p><strong>Dotted path through a reference navigation.</strong></p>
      <Code lang="csharp">{`var projected = dbContext.Products
    .Select(new List<string> { "Id", "Name", "Category.Name" });`}</Code>
      <p>
        <code>Category</code> is projected with only the requested{" "}
        <code>Name</code> sub-field (<code>Id</code> auto-included).
      </p>

      <p><strong>Dotted path through a collection navigation.</strong></p>
      <Code lang="csharp">{`var projected = dbContext.Products
    .Select(new List<string> { "Id", "Name", "Category.Vendors.Id" });`}</Code>
      <p>
        <code>Category.Vendors</code> is a collection — each <code>Vendor</code>{" "}
        element is projected with only its <code>Id</code> (<code>Id</code>{" "}
        auto-included).
      </p>

      <p><strong>Whole navigation object (non-dotted).</strong></p>
      <Code lang="csharp">{`var projected = dbContext.Products
    .Select(new List<string> { "Id", "Name", "Category" });`}</Code>
      <p>The entire <code>Category</code> object is bound as-is.</p>

      <p><strong>Whole collection (non-dotted).</strong></p>
      <Code lang="csharp">{`var projected = dbContext.Products
    .Select(new List<string> { "Id", "Name", "Brands" });`}</Code>
      <p>The entire <code>Brands</code> collection is bound as-is.</p>

      <p><strong>JSON-driven projection.</strong></p>
      <Code lang="json">{`{ "fields": ["Id", "Name", "Category.Name"] }`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/select-dynamic">
            <code>.SelectDynamic&lt;T&gt;</code>
          </Link>{" "}
          — non-generic dynamic variant (no parameterless constructor needed).
        </li>
        <li>
          <Link href="/docs/extensions/filter"><code>.Filter&lt;T&gt;</code></Link>{" "}
          — apply <code>where → order → page → select</code> in one call.
        </li>
        <li>
          <Link href="/docs/examples/select">JSON Cookbook: Select</Link>.
        </li>
      </ul>
    </DocPage>
  );
}
