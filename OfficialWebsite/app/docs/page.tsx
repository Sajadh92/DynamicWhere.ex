import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Introduction",
  description:
    "DynamicWhere.ex — a library for building dynamic filter, sort, paginate, group, aggregate, and set-operation expressions in Entity Framework Core from JSON.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs">
      <h1>Introduction</h1>
      <p>
        <strong>DynamicWhere.ex</strong> is a free, MIT-style-licensed library for
        building <em>dynamic</em> filter, sort, paginate, group, aggregate, and
        set-operation queries in Entity Framework Core applications — driven by
        plain JSON objects from any front-end or API consumer.
      </p>

      <p>
        Version <strong>2.1.0</strong>. Target framework <strong>.NET 6+</strong>.
        License <strong>Free Forever</strong>.
      </p>

      <h2 id="what-it-does">What it does</h2>
      <p>
        Your front-end sends a JSON shape that describes <em>what</em> to query.
        DynamicWhere.ex turns that JSON into a safe, validated, EF Core‑native
        <code>IQueryable&lt;T&gt;</code> — including projection, ordering, paging, grouping,
        having clauses, and even <code>UNION</code> / <code>INTERSECT</code> /{" "}
        <code>EXCEPT</code> set operations.
      </p>

      <p>The library exposes three composable shapes:</p>

      <ul>
        <li>
          <Link href="/docs/classes/filter"><code>Filter</code></Link> — where + select +
          order + page.
        </li>
        <li>
          <Link href="/docs/classes/segment"><code>Segment</code></Link> — multiple
          condition sets joined by Union / Intersect / Except.
        </li>
        <li>
          <Link href="/docs/classes/summary"><code>Summary</code></Link> — where → group →
          having → order → page for aggregate reporting.
        </li>
      </ul>

      <h2 id="who-its-for">Who it's for</h2>
      <ul>
        <li>
          <strong>API teams</strong> exposing list / search / report endpoints with
          flexible filter UI on the client.
        </li>
        <li>
          <strong>Admin dashboards</strong> with dynamic query builders, saved
          searches, and exportable reports.
        </li>
        <li>
          <strong>Headless / multi-tenant systems</strong> where filter shapes vary
          per tenant or per page.
        </li>
        <li>
          <strong>Any EF Core consumer</strong> that wants to stop concatenating
          LINQ predicates by hand.
        </li>
      </ul>

      <h2 id="thirty-second-tour">30-second tour</h2>
      <p>Install the package:</p>
      <Code lang="bash">{`dotnet add package DynamicWhere.ex --version 2.1.0`}</Code>

      <p>Build a filter from a JSON body and apply it to a DbSet:</p>
      <Code lang="csharp">{`using DynamicWhere.ex.Source;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;

var filter = new Filter
{
    ConditionGroup = new ConditionGroup
    {
        Connector = Connector.And,
        Conditions = new List<Condition>
        {
            new Condition
            {
                Sort = 1,
                Field = "Name",
                DataType = DataType.Text,
                Operator = Operator.IContains,
                Values = new List<object> { "john" }
            }
        }
    },
    Page = new PageBy { PageNumber = 1, PageSize = 10 }
};

FilterResult<Customer> result = await dbContext.Customers.ToListAsync(filter);`}</Code>

      <Callout tone="success" title="That's the whole loop">
        Front-end sends a JSON shape. Back-end calls{" "}
        <code>.ToListAsync(filter)</code>. You get back a strongly-typed
        <code>FilterResult&lt;T&gt;</code> with pagination metadata — no string LINQ, no
        manual expression trees.
      </Callout>

      <h2 id="how-to-read-these-docs">How to read these docs</h2>
      <p>The sidebar is grouped by intent:</p>
      <ul>
        <li>
          <strong>Getting Started</strong> — install the package and run your first
          query in under two minutes.
        </li>
        <li>
          <strong>Enums</strong> — every supported{" "}
          <Link href="/docs/enums/data-type">DataType</Link> and{" "}
          <Link href="/docs/enums/operator">Operator</Link>.
        </li>
        <li>
          <strong>Classes</strong> — the JSON shapes you'll send from the
          front-end and the result shapes you'll receive.
        </li>
        <li>
          <strong>Extension Methods</strong> — every IQueryable extension, what it
          validates, and what it returns.
        </li>
        <li>
          <strong>JSON Cookbook</strong> — 13 copy-pasteable end-to-end examples.
        </li>
        <li>
          <strong>Cache & Optimization</strong> — how the internal reflection cache
          works and how to tune it for your environment.
        </li>
        <li>
          <strong>Reference</strong> — error codes and breaking-change guidance.
        </li>
      </ul>

      <h2 id="next">Next steps</h2>
      <ul>
        <li>
          <Link href="/docs/installation"><strong>Installation →</strong></Link>{" "}
          set up the package.
        </li>
        <li>
          <Link href="/docs/quick-start"><strong>Quick Start →</strong></Link>{" "}
          your first end-to-end query.
        </li>
        <li>
          <Link href="/docs/examples"><strong>JSON Cookbook →</strong></Link>{" "}
          if you learn fastest from copy-paste.
        </li>
      </ul>
    </DocPage>
  );
}
