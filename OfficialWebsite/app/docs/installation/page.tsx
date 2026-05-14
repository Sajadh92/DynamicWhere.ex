import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Installation",
  description:
    "Install DynamicWhere.ex into your .NET 6+ project via dotnet CLI, Package Manager, or PackageReference.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/installation">
      <h1>Installation</h1>
      <p>
        DynamicWhere.ex is published on NuGet. It targets <strong>.NET 6+</strong> and
        plugs into any project that already uses Entity Framework Core.
      </p>

      <h2 id="cli">dotnet CLI</h2>
      <Code lang="bash">{`dotnet add package DynamicWhere.ex --version 2.1.0`}</Code>

      <h2 id="package-manager">Package Manager (Visual Studio)</h2>
      <Code lang="powershell">{`Install-Package DynamicWhere.ex -Version 2.1.0`}</Code>

      <h2 id="package-reference">PackageReference (csproj)</h2>
      <Code lang="xml">{`<ItemGroup>
  <PackageReference Include="DynamicWhere.ex" Version="2.1.0" />
</ItemGroup>`}</Code>

      <h2 id="dependencies">Dependencies</h2>
      <p>
        DynamicWhere.ex depends on the following packages — restore will pull them
        automatically. Versions are pinned to the floor; newer compatible releases
        are picked up by default unless your project locks them down.
      </p>

      <table>
        <thead>
          <tr>
            <th>Package</th>
            <th>Version</th>
            <th>Why</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Microsoft.EntityFrameworkCore</code></td>
            <td><code>6.0.22</code></td>
            <td>Provides <code>IQueryable&lt;T&gt;</code>, <code>CountAsync</code>, <code>ToListAsync</code>, <code>ToQueryString</code>.</td>
          </tr>
          <tr>
            <td><code>System.Linq.Dynamic.Core</code></td>
            <td><code>1.6.7</code></td>
            <td>Enables string-based <code>Select</code> / <code>OrderBy</code> / <code>GroupBy</code> used by the dynamic variants.</td>
          </tr>
        </tbody>
      </table>

      <h2 id="namespaces">Namespaces you'll import</h2>
      <p>The most common usings:</p>
      <Code lang="csharp">{`using DynamicWhere.ex.Source;            // Extension methods
using DynamicWhere.ex.Classes.Core;      // Condition, ConditionGroup, OrderBy, PageBy, ...
using DynamicWhere.ex.Classes.Complex;   // Filter, Segment, Summary
using DynamicWhere.ex.Enums;             // DataType, Operator, Connector, ...
using DynamicWhere.ex.Optimization.Cache.Source; // CacheExpose
using DynamicWhere.ex.Optimization.Cache.Config; // CacheOptions, CacheEvictionStrategy`}</Code>

      <h2 id="verify">Verify the install</h2>
      <p>The fastest sanity check is a one-liner against an existing DbSet:</p>
      <Code lang="csharp">{`var count = await db.Customers
    .Where(new Condition
    {
        Sort = 1,
        Field = "Id",
        DataType = DataType.Number,
        Operator = Operator.GreaterThan,
        Values = new List<object> { 0 }
    })
    .CountAsync();`}</Code>

      <Callout tone="info">
        Once that compiles and runs, you're good. Move on to{" "}
        <Link href="/docs/quick-start">Quick Start</Link> for a full filter end-to-end.
      </Callout>

      <h2 id="compatibility">Compatibility notes</h2>
      <ul>
        <li><strong>Target framework:</strong> .NET 6, .NET 7, .NET 8, .NET 9.</li>
        <li>
          <strong>EF Core provider:</strong> any provider that supports <code>ToQueryString()</code> works for the optional <code>getQueryString: true</code> flag. SQL Server, PostgreSQL (Npgsql), MySQL (Pomelo), and SQLite are all known to work.
        </li>
        <li>
          <strong>Database collation:</strong> case-insensitive <code>I*</code> operators emit <code>.ToLower()</code> on both sides. See{" "}
          <Link href="/docs/breaking-changes">Breaking Changes</Link> for performance notes.
        </li>
        <li>
          <strong>Enum storage:</strong> the <code>Enum</code> data type assumes enums are stored as <em>strings</em>. If you store them as integers, filter using <code>DataType.Number</code> instead.
        </li>
      </ul>

      <h2 id="links">Links</h2>
      <ul>
        <li>
          <a href="https://www.nuget.org/packages/DynamicWhere.ex" target="_blank" rel="noreferrer">
            NuGet package page →
          </a>
        </li>
        <li>
          <a href="https://github.com/Sajadh92/DynamicWhere.ex" target="_blank" rel="noreferrer">
            GitHub source repository →
          </a>
        </li>
      </ul>
    </DocPage>
  );
}
