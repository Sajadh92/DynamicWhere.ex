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
      <Code lang="bash">{`dotnet add package DynamicWhere.ex.Policies.AspNetCore --version 3.1.0`}</Code>
      <Code lang="csharp">{`app.MapDwPolicyAdmin(options =>
{
    options.RoutePrefix  = "/dw-policies";    // the default; mount it anywhere
    options.ReadPolicy   = "DwPolicyRead";    // both required, unless
    options.WritePolicy  = "DwPolicyWrite";   // AllowAnonymousAccess = true
});`}</Code>

      <Callout tone="danger" title="It refuses to mount without an authorization policy">
        There is deliberately no default. <code>POST /rules</code> changes what
        every caller may see, so there is nothing safe to fall back to.{" "}
        <code>ReadPolicy</code> and <code>WritePolicy</code> are two separate
        names and <em>both</em> are required: leave either blank — or blank the
        route prefix — and the application fails at <strong>startup</strong>{" "}
        rather than on the first request, because for an endpoint nobody is
        supposed to call, the first call is exactly the one that must not be the
        discovery.
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
          <tr><td><code>POST</code></td><td><code>/schema</code></td><td>Read</td><td>Fields for a filter UI: labels, groups, order, allowed values, cost. Takes <code>paths</code> and <code>depth</code>.</td></tr>
          <tr><td><code>GET</code></td><td><code>/rules?subject=</code></td><td>Read</td><td>List rules. The filter is <code>Kind[:Key]</code> — <code>Role:auditor</code>, not a bare key. Omitted, it lists every enabled broad rule; a user&apos;s rules need <code>User:{"{key}"}</code>.</td></tr>
          <tr><td><code>POST</code></td><td><code>/rules</code></td><td>Write</td><td>Upsert a rule. The body cannot carry a transform, operator lists, a forced predicate or facts — those go through <code>IDwPolicyWritableStore.UpsertAsync</code>.</td></tr>
          <tr><td><code>DELETE</code></td><td><code>/rules/{"{id}"}</code></td><td>Write</td><td>Delete a rule.</td></tr>
          <tr><td><code>POST</code></td><td><code>/explain</code></td><td>Read</td><td>The decision chain for one field, or for every field of the entity when none is named.</td></tr>
          <tr><td><code>POST</code></td><td><code>/simulate</code></td><td>Read</td><td>The sanitized clause, without executing it.</td></tr>
          <tr><td><code>GET</code></td><td><code>/health</code></td><td>Read</td><td>Snapshot version, age, degraded state, last error.</td></tr>
        </tbody>
      </table>

      <h2 id="schema-request">Asking for part of an entity</h2>
      <p>
        <code>POST /dw-policies/schema</code> takes a body rather than a query
        string, because the request carries a <strong>list</strong> of paths — and
        a list in a query string needs a separator. A comma is legal in a{" "}
        <code>[DwAlias]</code>, so the separator would eventually split a name in
        half and resolve neither piece.
      </p>
      <Code lang="json">{`{ "entity": "employee" }                            // 59 fields, two levels
{ "entity": "employee", "depth": 1 }                // 13 fields, the entity alone
{ "entity": "employee", "depth": 99 }               // 99 fields, as deep as a query may reach
{ "entity": "employee", "paths": ["Manager"] }      // 59 fields, rooted at the manager
{ "entity": "employee", "paths": ["Manager", "Address"], "depth": 1 }`}</Code>
      <p>
        <code>depth</code> is an integer and nothing else. A value beyond the
        query cap is clamped rather than refused, and the response reports both
        the depth it used and the ceiling, so a caller wanting everything sends a
        large number and learns the limit from the reply.
      </p>
      <p>
        The response is flat with a parent on every entry, which is a tree in
        adjacency form. <code>nodes</code> carries every navigation the walk
        touched, expanded or not, so a tree UI hangs each node and each field
        under its parent in one pass with no path parsing.
      </p>
      <Code lang="json">{`{
  "entity": "employee",
  "roots": ["Manager"], "depth": 2, "maxDepth": 4, "truncated": false,
  "fields": [ { "path": "Manager.FirstName", "parent": "Manager", ... } ],
  "nodes":  [ { "path": "Manager.Address", "parent": "Manager", "entity": "address",
                "depth": 3, "expanded": false, "remainingDepth": 0 } ]
}`}</Code>
      <p>
        <code>remainingDepth</code> says what asking for that path would return,
        so a node reporting zero has nothing to open. It accounts for{" "}
        <code>SchemaCycleLimit</code> as well as the query cap, and it is measured
        the way a request for that path would measure it — asking for a subtree
        resets the guard&apos;s count, which is what keeps drilling productive.
      </p>
      <Callout tone="note" title="A full-depth request returns 99, not 335">
        The cycle guard lets a type appear twice on one path, so{" "}
        <code>Manager.Email</code> is described and{" "}
        <code>Manager.Manager.Email</code> is not. Both remain queryable, and the
        second remains reachable by asking for the{" "}
        <code>Manager.Manager</code> subtree. Raising{" "}
        <code>SchemaCycleLimit</code> restores the exhaustive listing exactly.
      </Callout>

      <h2 id="schema">Schema, and the sealed-field rule</h2>
      <p>
        The schema is built from the entity rather than from a hand-maintained
        copy of it, so a field that becomes denied disappears from the UI without
        a front-end change. Expose the types you want reachable:
      </p>
      <Code lang="csharp">{`options.Entities.Expose<Employee>("Employee");`}</Code>
      <p>
        <strong>Sealed fields never appear</strong> — the schema omits them, and{" "}
        <code>POST /rules</code> rejects a rule aimed at one with a 400 whenever
        the body&apos;s <code>entityType</code> resolves through the exposed
        catalogue, so an operator is told at once rather than left to discover it.
        That check is the courtesy and not the guarantee: a rule naming a type
        nothing resolves is stored, and then loses at resolution time, where a
        sealed attribute outranks every dynamic level whatever any store did or
        did not check.
      </p>
      <Callout tone="note" title="Sealed is decided per feature">
        A field whose mask is sealed but whose <code>[DwDeny(Where)]</code> is
        overridable is absent for the masked feature and still writable for the
        overridable one. A field sealed on every feature is absent entirely.
      </Callout>

      <h2 id="explain">Explain</h2>
      <p>
        The whole chain, as JSON: an array of field entries, each carrying one
        record per feature saying what won, at which level, and what it overrode.
      </p>
      <Code lang="json">{`[
  {
    "field": "AccountNumber", "entityType": "MyApp.Models.Customer",
    "name": "AccountNumber", "isSealed": false,
    "features": [
      { "feature": "Select", "effect": "Mask",
        "decidedBy": "Rule a3f2 [Role:Finance]", "level": "DynamicRole",
        "attributionAmbiguous": false, "tiedWith": [],
        "overrode": ["DwMaskAttribute"] }
    ]
  }
]`}</Code>
      <p>
        <code>features</code> holds all six, so a feature nothing spoke about
        comes back as <code>Allow</code> with a null <code>decidedBy</code>.{" "}
        <code>isSealed</code> says an attribute decided at least one feature
        absolutely. A source renders as{" "}
        <code>Rule {"{id}"} [Kind:Key]</code>, or as the attribute&apos;s type
        name with <code>(sealed)</code> appended.
      </p>
      <p>
        Send <code>field</code> to explain one field; leave it out and every
        field of this caller&apos;s schema comes back, one entry each. There is
        no list of what was ignored.
      </p>
      <p>
        When two sources tie on level, specificity, priority and effect alike,
        the decided effect is still deterministic — only which of the equal
        sources <code>decidedBy</code> names is arbitrary, and{" "}
        <code>attributionAmbiguous</code> with <code>tiedWith</code> is how the
        endpoint says so rather than crediting one of them. See{" "}
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
        Drains whatever a request recorded against its context to an{" "}
        <code>IDwAuditSink</code>, once, at the end of the request. The sink is
        resolved from the request&apos;s own services, and the context it drains
        is the one stored in <code>HttpContext.Features</code> — a context built
        outside the pipeline records events nothing collects. Without the
        middleware the events are built and never written; with it and no sink
        registered, they are discarded with a warning.
      </p>
    </DocPage>
  );
}
