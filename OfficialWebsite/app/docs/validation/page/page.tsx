import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Page Validation",
  description:
    "Rules enforced on PageBy — PageNumber > 0 and PageSize > 0.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/validation/page">
      <h1>Page Validation</h1>
      <p>
        A <Link href="/docs/classes/page-by"><code>PageBy</code></Link>{" "}
        instance is rejected unless both <code>PageNumber</code> and{" "}
        <code>PageSize</code> are strictly positive.
      </p>

      <h2 id="rules">Rules</h2>
      <table>
        <thead>
          <tr>
            <th>Rule</th>
            <th>Error Code</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              <code>PageNumber</code> must be &gt; 0
            </td>
            <td><code>InvalidPageNumber</code></td>
          </tr>
          <tr>
            <td>
              <code>PageSize</code> must be &gt; 0
            </td>
            <td><code>InvalidPageSize</code></td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        Pages are 1-indexed. A request for page 0 — or a zero-row page — is
        always rejected.
      </Callout>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/classes/page-by">PageBy class</Link>
        </li>
        <li>
          <Link href="/docs/extensions/page">Page extension</Link>
        </li>
        <li>
          <Link href="/docs/examples/page">Page JSON example</Link>
        </li>
      </ul>
    </DocPage>
  );
}
