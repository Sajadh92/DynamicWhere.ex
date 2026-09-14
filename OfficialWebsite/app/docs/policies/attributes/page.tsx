import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Policy Attributes — the complete reference",
  description: "Every DynamicWhere.ex policy attribute: DwDeny, DwOperators, DwAlias, DwForceWhere, DwRequireWhere, DwMask, DwGeneralize, DwCost, DwAudit and the rest, with what each one does.",
  keywords: ["DwMask", "DwDeny attribute", "DwForceWhere", "EF Core attribute security", "field level attributes .NET"],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/policies/attributes/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/policies/attributes">
      <h1>Policy Attributes</h1>
      <p>
        The compile-time half of the feature. Every attribute below is{" "}
        <strong>sealed by default</strong> — no runtime rule can lift it unless
        you write <code>Overridable = true</code>. See{" "}
        <Link href="/docs/policies/precedence">Precedence</Link>.
      </p>

      <h2 id="entity">Type level</h2>
      <table>
        <thead><tr><th>Attribute</th><th>Effect</th></tr></thead>
        <tbody>
          <tr>
            <td><code>[DwEntity(RequirePolicy = true)]</code></td>
            <td>Querying this type without a policy context throws <code>PolicyRequired</code> instead of returning rows.</td>
          </tr>
        </tbody>
      </table>
      <Callout tone="warn" title="This is the one that catches a forgotten guard">
        Without it, a code path that never calls <code>ApplyPolicy</code> returns
        everything, and nothing complains. With it, the omission is a startup-
        loud failure on the first call rather than a silent disclosure.
      </Callout>

      <h2 id="access">Access control</h2>
      <table>
        <thead><tr><th>Attribute</th><th>Effect</th></tr></thead>
        <tbody>
          <tr><td><code>[DwDeny(features)]</code></td><td>The composable primitive. Refuse any combination of the six features.</td></tr>
          <tr><td><code>[DwDenied]</code></td><td>Refuse all six.</td></tr>
          <tr><td><code>[DwNoWhere]</code></td><td>Refuse filtering.</td></tr>
          <tr><td><code>[DwNoSelect]</code></td><td>Refuse projection. The field stays filterable and countable.</td></tr>
          <tr><td><code>[DwNoOrder]</code></td><td>Refuse sorting. The fix startup validation names for a masked field.</td></tr>
          <tr><td><code>[DwNoGroup]</code></td><td>Refuse grouping.</td></tr>
          <tr><td><code>[DwNoAggregate]</code></td><td>Refuse aggregation.</td></tr>
          <tr><td><code>[DwOperators(Allow = ..., Deny = ...)]</code></td><td>Restrict which operators may target the field. Restrictions from several sources intersect.</td></tr>
        </tbody>
      </table>
      <Code lang="csharp">{`// Confirmable, not searchable: a caller can check a code it already knows
// and cannot sweep for one it does not.
[DwOperators(Allow = new[] { Operator.Equal, Operator.In })]
public string EmployeeCode { get; set; }`}</Code>

      <h2 id="injection">Injection</h2>
      <table>
        <thead><tr><th>Attribute</th><th>Effect</th></tr></thead>
        <tbody>
          <tr><td><code>[DwAlias("name")]</code></td><td>A public name, accepted anywhere a field path is. Renamed back on the way out, after materialization.</td></tr>
          <tr><td><code>[DwForceWhere(op, Value =, ContextValue =)]</code></td><td>A predicate ANDed into every guarded query, whether the caller asked or not.</td></tr>
          <tr><td><code>[DwRequireWhere(Operators =)]</code></td><td>The caller must filter on this field. Throws in both tiers.</td></tr>
        </tbody>
      </table>
      <Code lang="csharp">{`// The row-level boundary. ContextValue reads from DwPolicyContext.Values,
// so the tenant comes from the request rather than from the source.
[DwForceWhere(Operator.Equal, ContextValue = "TenantId")]
public int TenantId { get; set; }

// Not a denial: an unscoped read of every department is the query worth
// refusing, and a requirement refuses it without blocking the scoped one.
[DwRequireWhere]
public string Department { get; set; }`}</Code>
      <Callout tone="note" title="Forced predicates are ANDed, never elected">
        Several sources can each force a predicate on the same field, and all of
        them apply. A conjunction can only narrow, so a low-authority rule can
        tighten a tenant scope and can never discard one.
      </Callout>

      <h2 id="transforms">Transformation</h2>
      <p>
        Covered in full on <Link href="/docs/policies/transforms">Transforms &amp; masking</Link>.
      </p>
      <table>
        <thead><tr><th>Attribute</th><th>Effect</th></tr></thead>
        <tbody>
          <tr><td><code>[DwMask(strategy)]</code></td><td>Obscure the value. Eight strategies.</td></tr>
          <tr><td><code>[DwMutate(typeof(T))]</code></td><td>Hand the value to your own <code>IValueTransformer</code>.</td></tr>
          <tr><td><code>[DwDefault]</code> / <code>[DwDefault("v")]</code></td><td>Replace with the type default or a constant.</td></tr>
          <tr><td><code>[DwGeneralize(mode)]</code></td><td>Reduce precision, keeping the type.</td></tr>
          <tr><td><code>[DwTruncate(n)]</code></td><td>Shorten text.</td></tr>
          <tr><td><code>[DwFormat("fmt")]</code></td><td>Render through a .NET format string.</td></tr>
        </tbody>
      </table>
      <p>
        All six also carry <code>AllowAggregate</code> and{" "}
        <code>MinGroupSize</code> — see{" "}
        <Link href="/docs/policies/security">Security</Link>, because those two
        are the k-anonymity control and are easy to miss.
      </p>

      <h2 id="metadata">Discovery, cost and audit</h2>
      <table>
        <thead><tr><th>Attribute</th><th>Effect</th></tr></thead>
        <tbody>
          <tr><td><code>[DwDescribe(Label =, Description =, Group =, Order =)]</code></td><td>Describes the field for the schema endpoint, so a front end builds its filter UI from the entity rather than a hand-maintained copy.</td></tr>
          <tr><td><code>[DwAllowedValues(...)]</code></td><td>Offer a list rather than a free-text box.</td></tr>
          <tr><td><code>[DwCost(weight)]</code></td><td>Charge the field against the query budget, so an expensive field costs more of a caller allowance.</td></tr>
          <tr><td><code>[DwAudit(features)]</code></td><td>Record every use to <code>IDwAuditSink</code>.</td></tr>
        </tbody>
      </table>

      <h2 id="overridable">Overridable</h2>
      <p>
        Every policy attribute carries <code>Overridable</code>, which defaults
        to <strong>false</strong>. One attribute can be sealed while another on
        the same member is replaceable.
      </p>
      <Code lang="csharp">{`// The mask is absolute; the description is a suggestion an operator may change.
[DwMask(MaskStrategy.Full)]
[DwDescribe(Label = "National ID", Overridable = true)]
public string NationalId { get; set; }`}</Code>

      <Callout tone="warn" title="A carrier on a self-referencing type">
        <code>[DwAlias]</code>, <code>[DwRequireWhere]</code> and{" "}
        <code>[DwForceWhere]</code> describe the entity being queried, so they
        are not replicated onto reflections of a type reached from itself —{" "}
        <code>Employee.Manager</code>, <code>Category.Parent</code>. A scope
        declared one navigation away on a <em>different</em> type, such as{" "}
        <code>Order.Buyer.TenantId</code>, still applies.
      </Callout>
    </DocPage>
  );
}
