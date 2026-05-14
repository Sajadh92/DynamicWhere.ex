import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";

export const metadata: Metadata = {
  title: "JSON Cookbook",
  description:
    "Thirteen copy-pasteable end-to-end JSON examples — one for every extension method and every special projection shape DynamicWhere.ex supports.",
};

const EXAMPLES: { href: string; n: number; title: string; blurb: string }[] = [
  {
    href: "/docs/examples/select",
    n: 1,
    title: "Select — Field Projection",
    blurb:
      "Direct scalars, dotted reference paths, dotted collection paths, whole objects, whole collections.",
  },
  {
    href: "/docs/examples/where-single",
    n: 2,
    title: "Where (single Condition)",
    blurb:
      "Text IContains, Number Between, Date GreaterThan, DateTime Equal, Guid, Boolean, Enum In, IsNull, Text IIn.",
  },
  {
    href: "/docs/examples/where-group",
    n: 3,
    title: "Where (ConditionGroup)",
    blurb: "AND group plus an AND/OR nested example, including the equivalent SQL.",
  },
  {
    href: "/docs/examples/order",
    n: 4,
    title: "Order — Sorting",
    blurb: "Single OrderBy and multi-key OrderBy lists.",
  },
  {
    href: "/docs/examples/page",
    n: 5,
    title: "Page — Pagination",
    blurb: "1-indexed page number and explicit page size.",
  },
  {
    href: "/docs/examples/group",
    n: 6,
    title: "Group — GroupBy + Aggregations",
    blurb: "Group by a field plus Count, Average, Maximum aggregations.",
  },
  {
    href: "/docs/examples/filter",
    n: 7,
    title: "Filter — Typed",
    blurb: "Full Filter request and the FilterResult<T> response shape.",
  },
  {
    href: "/docs/examples/summary",
    n: 8,
    title: "Summary — Group + Aggregate + Having",
    blurb:
      "Full Summary request plus SummaryResult response with flattened dotted aliases.",
  },
  {
    href: "/docs/examples/segment",
    n: 9,
    title: "Segment — Set Operations",
    blurb: "Union + Except across three condition sets, with logic note and response.",
  },
  {
    href: "/docs/examples/select-dynamic",
    n: 11,
    title: "SelectDynamic — Dynamic Projection",
    blurb:
      "All eight path-style variants — direct, dotted reference, dotted collection, multi-level, merged, whole object, whole collection, deep reference.",
  },
  {
    href: "/docs/examples/filter-dynamic",
    n: 12,
    title: "FilterDynamic — Dynamic Filter",
    blurb:
      "Full Filter request returning FilterResult<dynamic>, plus the projection-rule note.",
  },
  {
    href: "/docs/examples/nested-collection",
    n: 13,
    title: "Nested Collection Navigation",
    blurb: "Field path through a collection — the library wraps it in .Any() lambdas.",
  },
];

export default function Page() {
  return (
    <DocPage pathname="/docs/examples">
      <h1>JSON Cookbook</h1>
      <p>
        Thirteen copy-pasteable examples covering every extension method, every
        projection style, and every set / aggregation shape DynamicWhere.ex
        supports. Each example is the exact JSON your front-end would send.
      </p>

      <h2 id="index">Index</h2>
      <table>
        <thead>
          <tr>
            <th>#</th>
            <th>Example</th>
            <th>What it shows</th>
          </tr>
        </thead>
        <tbody>
          {EXAMPLES.map((e) => (
            <tr key={e.href}>
              <td>{e.n}</td>
              <td>
                <Link href={e.href}>{e.title}</Link>
              </td>
              <td>{e.blurb}</td>
            </tr>
          ))}
        </tbody>
      </table>

      <h2 id="how-to-use">How to use these</h2>
      <p>
        Every JSON body on these pages is the literal payload an HTTP client
        would POST to your endpoint. Bind it to the matching shape (
        <Link href="/docs/classes/filter"><code>Filter</code></Link>,{" "}
        <Link href="/docs/classes/segment"><code>Segment</code></Link>,{" "}
        <Link href="/docs/classes/summary"><code>Summary</code></Link>, or a raw{" "}
        <Link href="/docs/classes/condition-group"><code>ConditionGroup</code></Link>
        ) and pass it to the corresponding{" "}
        <Link href="/docs/extensions/filter">extension method</Link>.
      </p>
    </DocPage>
  );
}
