import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";

export const metadata: Metadata = {
  title: ".Page<T>(page)",
  description:
    "Paginate an IQueryable<T> by PageNumber and PageSize — both must be greater than zero.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/extensions/page/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/page">
      <h1>.Page&lt;T&gt;(page)</h1>
      <p>
        Paginates the query using a{" "}
        <Link href="/docs/classes/page-by"><code>PageBy</code></Link>{" "}
        descriptor. Internally this skips{" "}
        <code>(PageNumber - 1) * PageSize</code> records and takes{" "}
        <code>PageSize</code>.
      </p>

      <h2 id="signature">Signature</h2>
      <Code lang="csharp">{`public static IQueryable<T> Page<T>(this IQueryable<T> query, PageBy page)
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
            <td><code>page</code></td>
            <td>
              <Link href="/docs/classes/page-by"><code>PageBy</code></Link>
            </td>
            <td>Page number and size</td>
          </tr>
        </tbody>
      </table>

      <h2 id="validations">Validations</h2>
      <ul>
        <li>
          <code>PageNumber</code> must be &gt; 0 — error code{" "}
          <code>InvalidPageNumber</code>.
        </li>
        <li>
          <code>PageSize</code> must be &gt; 0 — error code{" "}
          <code>InvalidPageSize</code>.
        </li>
      </ul>

      <h2 id="returns">Returns</h2>
      <p>
        <code>IQueryable&lt;T&gt;</code> — paged query.
      </p>

      <h2 id="example">Example</h2>
      <Code lang="csharp">{`var paged = dbContext.Products.Page(new PageBy
{
    PageNumber = 1,
    PageSize   = 25
});`}</Code>

      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 25
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/page-by"><code>PageBy</code></Link> shape.
        </li>
        <li>
          <Link href="/docs/validation/page">Page validation rules</Link>.
        </li>
        <li>
          <Link href="/docs/examples/page">JSON Cookbook: Page</Link>.
        </li>
      </ul>
    </DocPage>
  );
}
