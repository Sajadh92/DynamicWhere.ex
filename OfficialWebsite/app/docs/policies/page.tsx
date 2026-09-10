import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Field-Level Policies — decide what each caller may query and see",
  description: "Field-level access control for DynamicWhere.ex. Deny, restrict, alias, force, mask and generalize any field per caller, with sealed attributes and optional runtime rules. New in 3.0.",
  keywords: ["EF Core field level security", "dynamic query authorization", "data masking EF Core", "row level security .NET", "k-anonymity .NET"],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/policies/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/policies">
      <h1>Field-Level Policies</h1>
      <p>
        DynamicWhere.ex accepts a <code>Filter</code> from any caller and turns
        it into an EF Core query. The caller chooses which fields to filter on,
        sort by, select, group by and aggregate. Policies add the question the
        library could not previously answer: <strong>who is asking, and what
        are they allowed to see?</strong>
      </p>

      <Callout tone="success" title="New in 3.0, and entirely opt-in">
        A project with no policy attributes and no <code>DwPolicy.Configure</code>{" "}
        call behaves exactly as 2.1.5. There is no break in the 2.x API — see{" "}
        <Link href="/docs/breaking-changes">Breaking Changes</Link>.
      </Callout>

      <h2 id="shape">A sandwich, not a rewrite</h2>
      <p>
        Requests are sanitized <em>before</em> the query is built. Results are
        transformed <em>after</em> they materialize. The query engine in between
        is unchanged, which is why turning policies on cannot alter the SQL of
        an unguarded query.
      </p>
      <Code lang="text">{`caller request
    |
    v
query.ApplyPolicy(ctx)     the only entry point; returns a guarded handle
    |
    v
sanitize                   drop or refuse denied fields, restrict operators,
                           enforce caps, inject aliases and forced predicates
    |
    v
existing DynamicWhere      untouched
    |
    v
transform                  mask, mutate, default, generalize, truncate, format
    |
    v
FilterResult.Policy        a PolicyTrace saying what the policy did`}</Code>

      <h2 id="start">The whole thing in one screen</h2>
      <Code lang="csharp">{`// Once, at startup. A second call is refused: the tier is read by every
// request thread, and a posture that can change mid-flight can be relaxed.
DwPolicy.Configure(new DwPolicyOptions
{
    Tier = DwTier.Convenience,
    HashSalt = secret,
});

// Once per request, never once per query.
var caller = await DwPolicy.PrepareAsync(
    new DwPolicyContext()
        .WithSubject(DwSubjectKind.User, userId)
        .WithSubject(DwSubjectKind.Role, "Support")
        .WithSubject(DwSubjectKind.Tenant, tenantId)
        .WithValue("TenantId", tenantId));

// Then query through the guarded handle instead of the raw IQueryable.
var result = await db.Employees.ApplyPolicy(caller).ToListAsync(filter);`}</Code>

      <Code lang="csharp">{`[DwEntity(RequirePolicy = true)]        // an unguarded read throws
public class Employee
{
    [DwMask(MaskStrategy.Email), DwNoOrder]
    public string Email { get; set; }    // s*************@c******.com on the way out

    [DwForceWhere(Operator.Equal, Value = "true")]
    public bool IsActive { get; set; }   // ANDed into every guarded query

    [DwGeneralize(GeneralizeMode.Round, Step = 5000,
                  AllowAggregate = true, MinGroupSize = 5)]
    [DwNoOrder, DwAudit, DwCost(10)]
    public decimal Salary { get; set; }  // rounded; aggregatable over groups of 5+

    [DwDenied]
    public JsonDocument? WorkSchedule { get; set; }
}`}</Code>

      <h2 id="features">Six features, per field</h2>
      <p>
        Every decision is made for one field and one feature:{" "}
        <code>Where</code>, <code>Select</code>, <code>Order</code>,{" "}
        <code>Group</code>, <code>Aggregate</code>, <code>Segment</code>.
      </p>
      <p>
        <code>Segment</code> is its own flag rather than a combination of{" "}
        <code>Where</code> and <code>Select</code>, because set operations can
        reconstruct a hidden field from membership alone — see{" "}
        <Link href="/docs/policies/security">Security</Link>.
      </p>

      <h2 id="where">Where to go next</h2>
      <ul>
        <li><Link href="/docs/policies/attributes">Attributes</Link> — all eighteen, with what each one does</li>
        <li><Link href="/docs/policies/precedence">Precedence</Link> — six levels, and why attributes are sealed by default</li>
        <li><Link href="/docs/policies/transforms">Transforms &amp; masking</Link> — eight mask strategies and five other transforms</li>
        <li><Link href="/docs/policies/store">Dynamic store</Link> — rules without a redeploy</li>
        <li><Link href="/docs/policies/providers">Store providers</Link> — Redis and Entity Framework Core</li>
        <li><Link href="/docs/policies/admin">Admin API</Link> — schema, rules, explain, simulate, health</li>
        <li><Link href="/docs/policies/security">Security &amp; k-anonymity</Link> — the seven inference channels</li>
        <li><Link href="/docs/policies/configuration">Configuration</Link> — options, caps and defaults</li>
      </ul>

      <h2 id="packages">The four packages</h2>
      <table>
        <thead><tr><th>Package</th><th>What it adds</th></tr></thead>
        <tbody>
          <tr><td><code>DynamicWhere.ex</code></td><td>Attributes, resolver, sanitizer, transforms, in-memory store, discovery, audit</td></tr>
          <tr><td><code>DynamicWhere.ex.Policies.Redis</code></td><td>Runtime rules in Redis</td></tr>
          <tr><td><code>DynamicWhere.ex.Policies.EntityFrameworkCore</code></td><td>Runtime rules in any EF Core provider</td></tr>
          <tr><td><code>DynamicWhere.ex.Policies.AspNetCore</code></td><td>Admin API, claims adapter, audit middleware</td></tr>
        </tbody>
      </table>
      <p>All four ship at the same version, and the build refuses to let them drift apart.</p>
    </DocPage>
  );
}
