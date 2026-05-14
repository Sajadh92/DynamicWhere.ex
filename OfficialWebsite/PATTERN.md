# Page Pattern — read first

Every doc page MUST follow this template. Use the `DocPage` wrapper, the `Code` component for code blocks, and `Callout` for asides. **Do NOT use markdown** — every page is a `.tsx` file under `app/docs/...`.

## Template

```tsx
import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "EXACT TITLE",                  // becomes "TITLE · DynamicWhere.ex"
  description: "ONE SENTENCE DESCRIPTION",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/PATH/HERE">  {/* MUST exactly match the route */}
      <h1>EXACT H1</h1>
      <p>Lead paragraph — one sentence orientation.</p>

      <h2 id="section-slug">Section</h2>
      <p>...</p>

      <Code lang="csharp">{`code...`}</Code>
      <Code lang="json">{`json...`}</Code>

      <Callout tone="note">Aside text.</Callout>
      {/* tones: info | note | warn | success | danger */}

      <table>
        <thead>
          <tr><th>Col</th><th>Col</th></tr>
        </thead>
        <tbody>
          <tr><td>...</td><td>...</td></tr>
        </tbody>
      </table>
    </DocPage>
  );
}
```

## Rules

1. **Always set `pathname` prop on `<DocPage>` to the EXACT route** (e.g. `/docs/enums/operator`). Drives prev/next nav.
2. Use `<Code lang="csharp">` / `<Code lang="json">` / `<Code lang="bash">` / `<Code lang="xml">` — NEVER raw `<pre>` or backticks.
3. Use `<Link href="/docs/...">` for internal cross-refs, NOT `<a>`.
4. Use `<a target="_blank" rel="noreferrer">` for external links.
5. Always include at least one `<h2>` after the lead `<p>`.
6. **Cover every detail** from the DOC.md section in the agent's brief — no abbreviation, no skipping rows, no "etc."
7. Tables for any tabular data. Use `<code>` for type / property / enum value names.
8. Use the JSX entity for `<` and `>` in generics: write `IQueryable&lt;T&gt;` not `IQueryable<T>` (the latter breaks JSX).
9. `Callout` tones: `info` (blue), `note` (purple, default), `warn` (amber), `success` (green), `danger` (red — for breaking changes).
10. Keep import lines exactly as shown above.

## Cross-reference map (for internal links)

- Enums: `/docs/enums/data-type` | `/operator` | `/connector` | `/direction` | `/intersection` | `/aggregator` | `/cache-eviction-strategy` | `/cache-memory-type`
- Classes: `/docs/classes/condition` | `/condition-group` | `/condition-set` | `/order-by` | `/group-by` | `/aggregate-by` | `/page-by` | `/filter` | `/segment` | `/summary` | `/filter-result` | `/segment-result` | `/summary-result`
- Extensions: `/docs/extensions/select` | `/select-dynamic` | `/where` | `/group` | `/order` | `/page` | `/filter` | `/filter-dynamic` | `/to-list-filter` | `/to-list-async-filter` | `/to-list-dynamic-filter` | `/to-list-async-dynamic-filter` | `/summary` | `/to-list-summary` | `/to-list-async-summary` | `/to-list-async-segment`
- Validation: `/docs/validation/condition` | `/condition-group` | `/group-by` | `/segment` | `/summary` | `/page`
- Examples: `/docs/examples/select` | `/where-single` | `/where-group` | `/order` | `/page` | `/group` | `/filter` | `/summary` | `/segment` | `/select-dynamic` | `/filter-dynamic` | `/nested-collection`
- Cache: `/docs/cache/architecture` | `/stores` | `/options` | `/configuration` | `/warmup` | `/monitoring` | `/presets`
- Reference: `/docs/errors` | `/docs/breaking-changes`
