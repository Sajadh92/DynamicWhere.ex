import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "PageBy",
  description: "Pagination — 1-based page number and a positive page size.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/classes/page-by">
      <h1>PageBy</h1>
      <p>
        A <code>PageBy</code> describes pagination for a <code>Filter</code>, <code>Segment</code>,
        or <code>Summary</code>. Page numbers are 1-based.
      </p>

      <h2 id="properties">Properties</h2>
      <table>
        <thead>
          <tr>
            <th>Property</th>
            <th>Type</th>
            <th>Description</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              <code>PageNumber</code>
            </td>
            <td>
              <code>int</code>
            </td>
            <td>1-based page index (must be &gt; 0).</td>
          </tr>
          <tr>
            <td>
              <code>PageSize</code>
            </td>
            <td>
              <code>int</code>
            </td>
            <td>Items per page (must be &gt; 0).</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn">
        Both values <strong>must be greater than zero</strong>. A <code>PageBy</code> with{" "}
        <code>PageNumber = 0</code> or <code>PageSize = 0</code> fails validation.
      </Callout>

      <h2 id="csharp-example">C# example</h2>
      <Code lang="csharp">{`var page = new PageBy { PageNumber = 1, PageSize = 25 };`}</Code>

      <h2 id="json-example">JSON example</h2>
      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 25
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/page">Page extension →</Link>
        </li>
        <li>
          <Link href="/docs/validation/page">Page validation →</Link>
        </li>
        <li>
          <Link href="/docs/examples/page">Pagination example →</Link>
        </li>
      </ul>
    </DocPage>
  );
}
