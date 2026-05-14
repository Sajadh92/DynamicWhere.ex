import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Quick Start",
  description:
    "Go from install to a working dynamic filter in under two minutes — JSON body in, paginated FilterResult<T> out.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/quick-start">
      <h1>Quick Start</h1>
      <p>
        From <em>install</em> to <em>working dynamic filter</em> in under two minutes.
        We'll send a JSON body from the front-end and apply it to an EF Core{" "}
        <code>DbSet&lt;Customer&gt;</code>.
      </p>

      <h2 id="step-1">1. Add the namespaces</h2>
      <Code lang="csharp">{`using DynamicWhere.ex.Source;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;`}</Code>

      <h2 id="step-2">2. Build a Filter</h2>
      <p>
        A <Link href="/docs/classes/filter"><code>Filter</code></Link> wraps four optional
        parts: where, select, order, and page.
      </p>
      <Code lang="csharp">{`var filter = new Filter
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
    Orders = new List<OrderBy>
    {
        new OrderBy
        {
            Sort = 1,
            Field = "CreatedAt",
            Direction = Direction.Descending
        }
    },
    Page = new PageBy { PageNumber = 1, PageSize = 10 }
};`}</Code>

      <h2 id="step-3">3. Apply it to a DbSet</h2>
      <Code lang="csharp">{`FilterResult<Customer> result = await dbContext.Customers.ToListAsync(filter);`}</Code>

      <p>You get back a strongly-typed <code>FilterResult&lt;T&gt;</code>:</p>
      <Code lang="csharp">{`Console.WriteLine($"page {result.PageNumber} of {result.PageCount}");
Console.WriteLine($"{result.TotalCount} total matches");
foreach (var c in result.Data)
{
    Console.WriteLine($"- {c.Name}");
}`}</Code>

      <h2 id="step-4">4. Drive it from JSON</h2>
      <p>
        Same filter, expressed as a JSON body your client can POST:
      </p>
      <Code lang="json">{`{
  "conditionGroup": {
    "connector": "And",
    "conditions": [
      {
        "sort": 1,
        "field": "Name",
        "dataType": "Text",
        "operator": "IContains",
        "values": ["john"]
      }
    ],
    "subConditionGroups": []
  },
  "orders": [
    { "sort": 1, "field": "CreatedAt", "direction": "Descending" }
  ],
  "page": { "pageNumber": 1, "pageSize": 10 }
}`}</Code>

      <p>And a minimal ASP.NET Core endpoint that consumes it:</p>
      <Code lang="csharp">{`app.MapPost("/customers/search", async (Filter filter, AppDbContext db) =>
{
    var result = await db.Customers.ToListAsync(filter);
    return Results.Ok(result);
});`}</Code>

      <Callout tone="success" title="That's the whole story">
        Front-end → JSON → <code>Filter</code> → <code>.ToListAsync(filter)</code> →
        paginated <code>FilterResult&lt;Customer&gt;</code>.
      </Callout>

      <h2 id="response-shape">Response shape</h2>
      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 10,
  "pageCount": 5,
  "totalCount": 42,
  "data": [
    { "id": 7, "name": "John Doe", "createdAt": "2025-09-14T12:31:00Z" }
  ],
  "queryString": null
}`}</Code>
      <p>
        Set <code>getQueryString: true</code> to also receive the generated SQL —
        useful in development.
      </p>
      <Code lang="csharp">{`var result = await db.Customers.ToListAsync(filter, getQueryString: true);
Console.WriteLine(result.QueryString);`}</Code>

      <h2 id="next">What's next?</h2>
      <ul>
        <li>
          <Link href="/docs/classes/filter">Filter →</Link> all four options in detail.
        </li>
        <li>
          <Link href="/docs/enums/operator">Operator reference →</Link> every supported comparison.
        </li>
        <li>
          <Link href="/docs/examples">JSON Cookbook →</Link> 13 copy-pasteable examples covering every extension method.
        </li>
        <li>
          <Link href="/docs/classes/segment">Segment →</Link> when you need UNION / INTERSECT / EXCEPT.
        </li>
        <li>
          <Link href="/docs/classes/summary">Summary →</Link> when you need GROUP BY + aggregations.
        </li>
      </ul>
    </DocPage>
  );
}
