import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Classes",
  description:
    "Overview of every class shape DynamicWhere.ex exposes — Core building blocks, Complex top-level shapes, and Result wrappers.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/classes">
      <h1>Classes</h1>
      <p>
        DynamicWhere.ex is shaped by a small set of plain C# classes that map 1:1 to JSON. Mix them
        together to express any filter, projection, sort, page, group, aggregate, or set operation.
      </p>

      <Callout tone="info" title="Three categories">
        <strong>Core</strong> classes are the building blocks. <strong>Complex</strong> classes are
        the top-level shapes you send from a client. <strong>Result</strong> classes are the
        strongly-typed wrappers you get back.
      </Callout>

      <h2 id="core">Core classes</h2>
      <p>Small composable pieces. You rarely send these on their own.</p>
      <table>
        <thead>
          <tr>
            <th>Class</th>
            <th>What it represents</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              <Link href="/docs/classes/condition">
                <code>Condition</code>
              </Link>
            </td>
            <td>A single filter predicate (field + operator + values).</td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/classes/condition-group">
                <code>ConditionGroup</code>
              </Link>
            </td>
            <td>An <code>And</code> / <code>Or</code> grouping of conditions and nested groups.</td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/classes/condition-set">
                <code>ConditionSet</code>
              </Link>
            </td>
            <td>One member of a <code>Segment</code> joined by set operations.</td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/classes/order-by">
                <code>OrderBy</code>
              </Link>
            </td>
            <td>A single sort criterion (field + direction).</td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/classes/group-by">
                <code>GroupBy</code>
              </Link>
            </td>
            <td>Group-key fields plus a list of aggregations.</td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/classes/aggregate-by">
                <code>AggregateBy</code>
              </Link>
            </td>
            <td>One aggregation (Count / Sum / Min / Max / Avg) with an alias.</td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/classes/page-by">
                <code>PageBy</code>
              </Link>
            </td>
            <td>Pagination — page number and page size.</td>
          </tr>
        </tbody>
      </table>

      <h2 id="complex">Complex classes</h2>
      <p>Top-level shapes you serialize from the front-end and pass to extension methods.</p>
      <table>
        <thead>
          <tr>
            <th>Class</th>
            <th>What it represents</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              <Link href="/docs/classes/filter">
                <code>Filter</code>
              </Link>
            </td>
            <td>Where + select + order + page in one object.</td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/classes/segment">
                <code>Segment</code>
              </Link>
            </td>
            <td>Multiple condition sets combined with <code>Union</code> / <code>Intersect</code> / <code>Except</code>.</td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/classes/summary">
                <code>Summary</code>
              </Link>
            </td>
            <td>Where → group → having → order → page for aggregate reporting.</td>
          </tr>
        </tbody>
      </table>

      <h2 id="results">Result classes</h2>
      <p>Returned by the terminal extension methods. Always contain pagination metadata.</p>
      <table>
        <thead>
          <tr>
            <th>Class</th>
            <th>Returned by</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              <Link href="/docs/classes/filter-result">
                <code>FilterResult&lt;T&gt;</code>
              </Link>
            </td>
            <td>
              <code>ToListFilter</code> / <code>ToListAsyncFilter</code> / dynamic variants.
            </td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/classes/segment-result">
                <code>SegmentResult&lt;T&gt;</code>
              </Link>
            </td>
            <td>
              <code>ToListAsyncSegment</code>.
            </td>
          </tr>
          <tr>
            <td>
              <Link href="/docs/classes/summary-result">
                <code>SummaryResult</code>
              </Link>
            </td>
            <td>
              <code>ToListSummary</code> / <code>ToListAsyncSummary</code>.
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="next">Next</h2>
      <ul>
        <li>
          <Link href="/docs/classes/condition">Condition →</Link> the smallest filter unit.
        </li>
        <li>
          <Link href="/docs/classes/filter">Filter →</Link> the most common top-level shape.
        </li>
        <li>
          <Link href="/docs/examples">JSON Cookbook →</Link> copy-pasteable end-to-end examples.
        </li>
      </ul>
    </DocPage>
  );
}
