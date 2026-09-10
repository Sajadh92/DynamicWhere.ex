import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Policy Admin API — schema, rules, explain, simulate, health",
  description: "Mount the DynamicWhere.ex policy administration endpoints in ASP.NET Core: schema discovery for filter UIs, rule management, the decision chain, dry-run simulation and store health.",
  keywords: ["policy admin API", "ASP.NET Core authorization endpoints", "schema discovery", "explain authorization decision"],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/policies/admin/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/policies/admin">
      <h1>Admin API</h1>
      <Code lang="bash">{`dotnet add package DynamicWhere.ex.Policies.AspNetCore --version 3.0.0`}</Code>
      <Code lang="csharp">{`app.MapDwPolicyAdmin(options =>
{
    options.RoutePrefix  = "/dw-policies";
    options.ReadPolicy   = "DwPolicyRead";    // both required
    options.WritePolicy  = "DwPolicyWrite";
});`}</Code>

      <Callout tone="danger" title="It refuses to mount without an authorization policy">
        There is deliberately no default. <code>POST /rules</code> changes what
        every caller may see, so there is nothing safe to fall back to. Omit
        either name and the application fails at <strong>startup</strong> rather
        than on the first request — because for an endpoint nobody is supposed to
        call, the first call is exactly the one that must not be the discovery.
      </Callout>
      <p>
        <code>AllowAnonymousAccess</code> exists for a deployment where something
        in front of the application authorizes. It is a deliberate choice, not a
        shortcut past registering a policy.
      </p>

      <h2 id="endpoints">Seven endpoints</h2>
      <table>
        <thead><tr><th>Method</th><th>Route</th><th>Auth</th><th>Purpose</th></tr></thead>
        <tbody>
          <tr><td><code>GET</code></td><td><code>/schema/{"{entity}"}</code></td><td>Read</td><td>Fields for a filter UI: labels, groups, order, allowed values, cost.</td></tr>
          <tr><td><code>GET</code></td><td><code>/rules?subject=</code></td><td>Read</td><td>List rules.</td></tr>
          <tr><td><code>POST</code></td><td><code>/rules</code></td><td>Write</td><td>Upsert a rule.</td></tr>
          <tr><td><code>DELETE</code></td><td><code>/rules/{"{id}"}</code></td><td>Write</td><td>Delete a rule.</td></tr>
          <tr><td><code>POST</code></td><td><code>/explain</code></td><td>Read</td><td>The decision chain for one field.</td></tr>
          <tr><td><code>POST</code></td><td><code>/simulate</code></td><td>Read</td><td>The sanitized clause, without executing it.</td></tr>
          <tr><td><code>GET</code></td><td><code>/health</code></td><td>Read</td><td>Snapshot version, age, degraded state, last error.</td></tr>
        </tbody>
      </table>

      <h2 id="schema">Schema, and the sealed-field rule</h2>
      <p>
        The schema is built from the entity rather than from a hand-maintained
        copy of it, so a field that becomes denied disappears from the UI without
        a front-end change. Expose the types you want reachable:
      </p>
      <Code lang="csharp">{`options.Entities.Expose<Employee>("Employee");`}</Code>
      <p>
        <strong>Sealed fields never appear</strong> — the schema omits them and{" "}
        <code>POST /rules</code> rejects them, so an operator cannot even attempt
        to grant one. Enforced at configuration time and again at resolution time.
      </p>
      <Callout tone="note" title="Sealed is decided per feature">
        A field whose mask is sealed but whose <code>[DwDeny(Where)]</code> is
        overridable is absent for the masked feature and still writable for the
        overridable one. A field sealed on every feature is absent entirely.
      </Callout>

      <h2 id="explain">Explain</h2>
      <p>
        The whole chain: what won, what it overrode, what was ignored and why.
      </p>
      <Code lang="text">{`Salary
  CanSelect : true (masked)
  Mask      : Partial(keepEnd 4)
  Decided by: Rule a3f2 - Role=Manager, Priority 10
  Overrode  : [DwMask(Full)] attribute (Overridable = true)
  Ignored   : Rule b21c - Global, lower precedence`}</Code>
      <p>
        When two sources tie on level, specificity, priority and effect alike,
        the decided effect is still deterministic — only which of the equal
        sources is named here is arbitrary. See{" "}
        <Link href="/docs/policies/precedence">Precedence</Link>.
      </p>

      <h2 id="simulate">Simulate</h2>
      <p>
        Send a clause, get back what it would become. Nothing executes and{" "}
        <strong>nothing is audited</strong>, so an operator checking a rule does
        not fill the audit trail with reads that never happened.
      </p>

      <h2 id="claims">From a ClaimsPrincipal</h2>
      <Code lang="csharp">{`var caller = await httpContext.GetPolicyContextAsync(claimsOptions);

// or explicitly
var caller = await DwClaimsAdapter.CreateContextAsync(User, claimsOptions, ct);`}</Code>
      <p>
        Claim types for user, role, tenant, custom subjects and context values are
        all configurable. <code>AllowAnonymous</code> is off by default: a
        coherent posture for a public read surface, and an accident everywhere
        else.
      </p>

      <h2 id="audit">Audit middleware</h2>
      <Code lang="csharp">{`app.UseDwPolicyAudit();`}</Code>
      <p>
        Drains whatever a request recorded against its context to the configured{" "}
        <code>IDwAuditSink</code>, once, at the end of the request. Without it the
        events are built and never written.
      </p>
    </DocPage>
  );
}
