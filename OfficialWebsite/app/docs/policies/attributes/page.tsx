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
        The compile-time half of the feature. Every field-level attribute below
        is <strong>sealed by default</strong> — no runtime rule can lift it unless
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
          <tr>
            <td><code>[DwEntity(DefaultOrder = &quot;CreatedAt desc, Id&quot;)]</code></td>
            <td>The order a guarded query takes when its caller sends none. See <Link href="/docs/policies/attributes#default-order">Default order</Link>.</td>
          </tr>
        </tbody>
      </table>
      <Callout tone="warn" title="RequirePolicy is the one that catches a forgotten guard">
        Without it, a code path that never calls <code>ApplyPolicy</code> returns
        everything, and nothing complains. With it, the omission is a startup-
        loud failure on the first call rather than a silent disclosure.
        <strong>Only this library&apos;s own extension methods run the check</strong>,
        so plain EF Core or LINQ against the <code>DbSet</code> is not intercepted
        and returns rows as it always did.
      </Callout>

      <h2 id="default-order">Default order</h2>
      <p>
        A caller who pages a query without ordering it gets whichever rows the
        database returns first, so two pages can repeat or miss a row.{" "}
        <code>DefaultOrder</code> names the order a guarded query takes when its
        caller sends none — <code>Orders</code> null or empty.
      </p>
      <Code lang="csharp">{`[DwEntity(RequirePolicy = true, DefaultOrder = "CreatedAt desc, Id")]
public class Ticket
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Title { get; set; } = string.Empty;
}`}</Code>
      <ul>
        <li>
          Entries are separated by commas. Each is a field path, optionally
          followed by <code>asc</code> or <code>desc</code> in any letter case, and
          ascending when neither is written. A path may cross a navigation, and a
          blank entry — the one a trailing comma leaves — is ignored.
        </li>
        <li>
          It applies only through <code>ApplyPolicy</code>: to{" "}
          <code>ToList</code>, <code>ToListAsync</code>, <code>ToListDynamic</code>{" "}
          and <code>ToListAsyncDynamic</code> with a <code>Filter</code>, to{" "}
          <code>ToListAsync</code> with a <code>Segment</code>, to the composable{" "}
          <code>Filter</code> and <code>FilterDynamic</code>, and to the composable{" "}
          <code>Page</code> on a source nothing has ordered.
        </li>
        <li>
          It never applies outside the guarded handle. A core method on a plain{" "}
          <code>IQueryable&lt;T&gt;</code> or <code>IEnumerable&lt;T&gt;</code> —
          including one called on what <code>AsUnguardedQueryable()</code>{" "}
          returns — does not read it, and orders only as its caller asks, as in
          3.0.0.
        </li>
        <li>
          The caller&apos;s own orders win, and the default is not appended to
          them as a tiebreak. A query that is already ordered keeps its order,
          whether an <code>IQueryable&lt;T&gt;</code> was ordered before it was
          guarded —{" "}
          <code>{`db.Tickets.OrderBy(t => t.Title).ApplyPolicy(caller)`}</code> — or
          an <code>Order</code> was composed on the guarded handle first, as in{" "}
          <code>{`guarded.Order(order).Page(page)`}</code>. An in-memory
          sequence sorted before <code>ApplyPolicy</code> is not recognised as
          ordered, because it reaches the policy as a query with no{" "}
          <code>OrderBy</code> in it, so it takes the default; send that order with
          the filter instead. A <code>Summary</code> never takes the default, and
          neither do the composable <code>Where</code>, <code>Select</code> and{" "}
          <code>Order</code>.
        </li>
      </ul>
      <p>
        The default is gated like any order. A field in it that this caller may not
        order by is left out — never refused, because the caller did not send it —
        and the trace records a <code>Dropped</code> decision for{" "}
        <code>Order</code> whose reason starts{" "}
        <code>left out of the default order</code>. Ordering by that field would
        rank rows by a value the caller may not see. A dry run keeps the field and
        still records the decision, and a caller whose own orders were all dropped
        under the <code>Convenience</code> tier gets no default in their place.
      </p>
      <p>
        A default is never a reason for the library to refuse a query. An entry
        naming a field the type does not have is skipped, so is an entry that is not
        a field and a direction, and so is one no query can order by, such as a
        collection of entities; a field named twice is ordered by once. Nothing is ordered that the
        declaration does not name, so a type without a <code>DefaultOrder</code> is
        ordered only as its caller asks.{" "}
        <Link href="/docs/policies/configuration#validate">Startup validation</Link>{" "}
        reports an unreadable entry, a field no query can order by and a field the
        type&apos;s own attributes deny for ordering as errors, and a field the type
        does not have as a warning.
      </p>
      <Callout tone="warn" title="End it with a unique field">
        Rows that share every value the default names can still change places
        between pages. End the default with the key —{" "}
        <code>&quot;CreatedAt desc, Id&quot;</code> — so that no two rows tie.
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
          <tr><td><code>[DwForceWhere(op, Value =, ContextValue =, AllowNull =)]</code></td><td>A predicate ANDed into every guarded query, whether the caller asked or not. With <code>AllowNull = true</code> a row whose member is null passes as well — see <Link href="/docs/policies/attributes#allow-null">below</Link>.</td></tr>
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

      <h2 id="allow-null">A forced predicate that lets null through</h2>
      <p>
        A record can belong to one tenant or to none — a system role no
        institution owns. An equality scope never matches the row whose column is
        null, and several forced predicates on one member are joined by{" "}
        <code>And</code>, so no combination of them can say &quot;or null&quot;.{" "}
        <code>AllowNull = true</code> widens one predicate to{" "}
        <code>(field op value OR field IS NULL)</code>.
      </p>
      <Code lang="csharp">{`// Tenant 5 sees its own roles, and the roles no institution owns.
[DwForceWhere(Operator.Equal, ContextValue = "TenantId", AllowNull = true)]
public int? InstitutionId { get; set; }`}</Code>
      <ul>
        <li>
          The widened term is placed in a group of its own and joined by{" "}
          <code>And</code> to the caller&apos;s group and to every other forced
          predicate, so a caller&apos;s <code>Or</code> cannot merge with it:{" "}
          <code>(Name = A OR Name = B) AND (InstitutionId = 5 OR InstitutionId IS NULL)</code>.
        </li>
        <li>
          It works with every operator that takes a value. It is refused with{" "}
          <code>ArgumentException</code> at resolution, and reported by{" "}
          <Link href="/docs/policies/configuration#validate">startup validation</Link>,
          on <code>Operator.IsNull</code> or <code>Operator.IsNotNull</code>, which
          already decide about null, and on a member that can never be null, such
          as an <code>int</code>.
        </li>
        <li>
          The context value is still required. A context that does not supply{" "}
          <code>TenantId</code> is refused with <code>MissingContextValue</code>:
          the flag widens which rows pass, not which callers are scoped.
        </li>
        <li>
          The widened term is a disjunction, so it does not satisfy a{" "}
          <code>[DwRequireWhere]</code> on the same member. The caller still has to
          filter on it.
        </li>
        <li>
          The trace records the injection as{" "}
          <code>forced predicate (Equal, or null)</code>, with the operator&apos;s
          name. A dry run injects nothing, as for every forced predicate.
        </li>
      </ul>
      <p>
        A runtime rule sets the same flag on its <code>ForcedPredicate</code> — see{" "}
        <Link href="/docs/policies/store#forced">Dynamic store</Link>.
      </p>

      <h2 id="transforms">Transformation</h2>
      <p>
        Covered in full on <Link href="/docs/policies/transforms">Transforms &amp; masking</Link>.
      </p>
      <table>
        <thead><tr><th>Attribute</th><th>Effect</th></tr></thead>
        <tbody>
          <tr><td><code>[DwMask(strategy)]</code></td><td>Obscure the value. Nine strategies.</td></tr>
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
          <tr><td><code>[DwAudit(features)]</code></td><td>Record every use to <code>IDwAuditSink</code>. Refused queries are recorded separately, whether or not a field carries this attribute, by <Link href="/docs/policies/configuration#audit-refusals"><code>DwPolicyOptions.AuditRefusals</code></Link>.</td></tr>
        </tbody>
      </table>

      <h2 id="overridable">Overridable</h2>
      <p>
        Every field-level policy attribute carries <code>Overridable</code>, which
        defaults to <strong>false</strong>. One attribute can be sealed while
        another on the same member is replaceable.
      </p>
      <p>
        Two exceptions. The type-level <code>[DwEntity]</code> derives from{" "}
        <code>Attribute</code> rather than the policy base and has no{" "}
        <code>Overridable</code> at all. And the flag decides nothing on{" "}
        <code>[DwOperators]</code> or <code>[DwForceWhere]</code>, because those
        two are intersected and collected rather than elected — see{" "}
        <Link href="/docs/policies/precedence">Precedence</Link>.
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
