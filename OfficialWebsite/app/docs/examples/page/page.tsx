import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Example: Page — Pagination",
  description: "Pagination shape — 1-indexed PageNumber plus explicit PageSize.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/examples/page">
      <h1>Example 5: Page — Pagination</h1>
      <p>
        A <Link href="/docs/classes/page-by"><code>PageBy</code></Link>{" "}
        passed to <Link href="/docs/extensions/page"><code>Page&lt;T&gt;</code></Link>
        . Both fields are required and must be strictly positive.
      </p>

      <h2 id="payload">Payload</h2>
      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 25
}`}</Code>

      <Callout tone="info">
        <code>pageNumber</code> is 1-indexed. See{" "}
        <Link href="/docs/validation/page">Page validation rules</Link>.
      </Callout>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/page">Page extension</Link>
        </li>
        <li>
          <Link href="/docs/classes/page-by">PageBy class</Link>
        </li>
      </ul>
    </DocPage>
  );
}
