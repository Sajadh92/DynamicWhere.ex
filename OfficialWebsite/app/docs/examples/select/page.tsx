import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Example: Select — Field Projection",
  description:
    "Every Select shape — direct scalars, dotted reference paths, dotted collection paths, whole navigation objects, and whole collections.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/examples/select/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/examples/select">
      <h1>Example 1: Select — Field Projection</h1>
      <p>
        The <Link href="/docs/extensions/select"><code>Select&lt;T&gt;</code></Link>{" "}
        extension takes a list of field paths. Each variant below shows a
        different projection style.
      </p>

      <h2 id="direct-scalars">Direct scalars</h2>
      <Code lang="json">{`{ "fields": ["Id", "Name", "Price"] }`}</Code>

      <h2 id="dotted-reference">Dotted path through reference navigation</h2>
      <Code lang="json">{`{ "fields": ["Id", "Name", "Category.Name"] }`}</Code>
      <p>
        <code>Category</code> is projected with only the requested{" "}
        <code>Name</code> sub-field (<code>Id</code> auto-included).
      </p>

      <h2 id="dotted-collection">Dotted path through collection navigation</h2>
      <Code lang="json">{`{ "fields": ["Id", "Name", "Category.Vendors.Id"] }`}</Code>
      <p>
        <code>Category.Vendors</code> is a collection — each <code>Vendor</code>{" "}
        element is projected with only its <code>Id</code> (<code>Id</code>{" "}
        auto-included).
      </p>

      <h2 id="whole-object">Whole navigation object (non-dotted)</h2>
      <Code lang="json">{`{ "fields": ["Id", "Name", "Category"] }`}</Code>
      <p>
        The entire <code>Category</code> object is bound as-is.
      </p>

      <h2 id="whole-collection">Whole collection (non-dotted)</h2>
      <Code lang="json">{`{ "fields": ["Id", "Name", "Brands"] }`}</Code>
      <p>
        The entire <code>Brands</code> collection is bound as-is.
      </p>

      <Callout tone="info">
        <strong>Backend:</strong> <code>query.Select(fields)</code>
      </Callout>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/extensions/select">Select extension</Link>
        </li>
        <li>
          <Link href="/docs/examples/select-dynamic">SelectDynamic example</Link>{" "}
          — same paths, dynamic return type.
        </li>
      </ul>
    </DocPage>
  );
}
