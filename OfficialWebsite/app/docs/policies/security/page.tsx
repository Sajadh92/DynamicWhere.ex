import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Security & k-anonymity — the eight inference channels",
  description: "How DynamicWhere.ex closes the disclosure channels no per-field rule closes on its own: set-operation reconstruction, singleton-group aggregates, cardinality probes, sort-and-page binary search, SQL leakage, and schema probing through refusals.",
  keywords: ["k-anonymity", "MinGroupSize", "inference attack", "data disclosure", "aggregate disclosure", "EF Core security"],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/policies/security/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/policies/security">
      <h1>Security &amp; k-anonymity</h1>
      <p>
        Denying a field is easy. The hard part is the set of ways a caller can
        learn a value <em>without</em> reading it. Eight such channels are closed;
        each has a test that reproduces the attack and goes red if the control is
        removed.
      </p>

      <Callout tone="warn" title="MinGroupSize ships on, at 5">
        Read this page before you turn it off. The compatibility argument for
        shipping it off does not hold: the floor applies only to a{" "}
        <em>guarded</em> summary, and guarded queries are new in this release, so
        there is no caller anywhere whose results it can change.
      </Callout>

      <h2 id="sets">1. Set operations reconstruct a denied field</h2>
      <p>
        <code>Segment</code> composes UNION, INTERSECT and EXCEPT. Where a field
        is deny-select but allow-where:
      </p>
      <Code lang="text">{`AllEmployees EXCEPT (AllEmployees WHERE Salary > 100000)`}</Code>
      <p>
        returns exactly the people earning under 100k, by name, with the salary
        column never selected. The protected value is reconstructed from set
        membership.
      </p>
      <p>
        <strong>Closed by:</strong> policy applies to every segment
        independently, and in the <code>Strict</code> tier a deny-select field is
        automatically deny-where <em>inside a Segment</em>.
      </p>

      <h2 id="aggregates">2. Aggregates over singleton groups</h2>
      <p>
        <code>SUM</code>, <code>MAX</code> and <code>MIN</code> execute in SQL
        against the real values, before any transform can apply.{" "}
        <code>GROUP BY Department</code> with <code>MAX(Salary)</code> over a
        department of one returns that person exact salary.
      </p>
      <p><strong>Closed by two halves, and neither works alone:</strong></p>
      <ol>
        <li>
          Aggregating a <em>transformed</em> field is <strong>denied by
          default</strong> — all six transform attributes, not masks alone — and
          opted into with <code>AllowAggregate = true</code>.
        </li>
        <li>
          <code>MinGroupSize</code> suppresses any group smaller than{" "}
          <em>k</em>. Groups below the floor are removed from the result.
        </li>
      </ol>
      <Code lang="csharp">{`[DwGeneralize(GeneralizeMode.Round, Step = 5000,
              AllowAggregate = true, MinGroupSize = 5)]
public decimal Salary { get; set; }`}</Code>
      <Callout tone="warn" title="The floor is on by default, and switching it off is one line">
        <code>DwCaps.MinGroupSize</code> defaults to <strong>5</strong>. Writing{" "}
        <code>MinGroupSize = 1</code> switches it off and it is off — in
        production, with nothing refused and nothing warned about. A deployment
        that wants singleton groups is entitled to them.
      </Callout>
      <p>
        The setting starts <em>unset</em> rather than at one, which is what makes
        both halves possible: <code>IsMinGroupSizeSet</code> tells a deliberate
        opt-out from a deployment that never heard of the control. Without that
        distinction, any check strict enough to catch the second would trap the
        first. A per-field <code>MinGroupSize</code> on any transform attribute
        raises the floor for that field; the effective floor is the largest in
        play.
      </p>
      <Code lang="csharp">{`new DwPolicyOptions()                                 // floor of 5
new DwPolicyOptions { Caps = { MinGroupSize = 1 } }   // no floor, and meant
new DwPolicyOptions { Caps = { MinGroupSize = 10 } }  // stricter`}</Code>
      <p>
        The floor <strong>suppresses rows</strong>; it does not refuse the query.
        A summary whose every group is a singleton returns nothing.
      </p>

      <h2 id="count">3. TotalCount cardinality disclosure</h2>
      <p>
        <code>ToList</code> computes <code>Count()</code> on the pre-pagination
        query. Filtering <code>Salary &gt; 200000</code> and reading{" "}
        <code>TotalCount</code> counts the high earners without selecting
        anything.
      </p>
      <p>
        This is inherent to permitting WHERE on a protected field. The control is{" "}
        <code>[DwOperators]</code> restricting the field to <code>Equal</code> and{" "}
        <code>In</code>, so a caller can confirm a value it already knows and
        cannot sweep for one it does not.{" "}
        <strong>A documented consequence, not a defect.</strong>
      </p>

      <h2 id="order">4. Sort plus paging is a binary search</h2>
      <p>
        Sorting by a masked field ranks the real values. Paging through a known
        set reveals relative magnitude, and combined with range filters it
        converges on exact values.
      </p>
      <p>
        <strong>Closed by:</strong> startup validation <em>warns</em> when a field
        is transformed but still orderable, and <code>[DwNoOrder]</code> is the
        explicit fix. A warning rather than an error because there are models
        where the ordering is the point and the transform is cosmetic — the
        engine names the fix rather than deciding for you. A declared default
        order cannot reopen the channel: a field in{" "}
        <Link href="/docs/policies/attributes#default-order"><code>[DwEntity(DefaultOrder = ...)]</code></Link>{" "}
        that the caller may not order by is left out of their query, and recorded
        in the trace.
      </p>
      <Code lang="text">{`Employee.Email: the value is transformed on output but the field can still be
sorted on, and sorting runs against the real value. Paging through it ranks the
true order. Add [DwNoOrder] unless that is intended.`}</Code>

      <h2 id="sql">5. getQueryString leaks the generated SQL</h2>
      <p>
        Returning raw SQL exposes injected tenant predicates and the column names
        of denied fields. The <code>Strict</code> tier throws{" "}
        <code>QueryStringDenied</code>; the <code>Convenience</code> tier allows
        it, documented.
      </p>
      <p>
        The trace a result carries names the same things — the fields a policy
        dropped, what sealed each one, and every injected predicate — and an API
        that serializes a result hands it over. The <code>Strict</code> tier
        therefore keeps it off the result unless{" "}
        <Link href="/docs/policies/configuration#trace"><code>IncludeTraceInResult</code></Link>{" "}
        is <code>true</code>; it stays on <code>PolicyQueryable&lt;T&gt;.LastTrace</code>,
        in-process.
      </p>

      <h2 id="probing">6. A refusal tells a missing field from a denied one</h2>
      <p>
        A caller who may not read a column can still ask about it. When a name that
        matches nothing fails validation while a denied field is refused by the
        policy — naming the field and the attribute that sealed it — every guess is
        answered: this column does not exist, that one does and is hidden. Repeated,
        the probe lists the schema, the columns the caller may never read included.
      </p>
      <p>
        <strong>Closed by:</strong> under the <code>Strict</code> tier, outside a
        dry run, a name that matches nothing is gated as a field denied for every
        feature, at the step where a denial is raised and after the same caps a
        real field passes, so it receives the code a{" "}
        <code>[DwDenied]</code> field receives in that clause —{" "}
        <code>FieldDeniedForWhere</code> … <code>FieldDeniedForSegment</code>.
        All six codes carry <code>FieldPath</code> <code>&quot;*&quot;</code> and
        no <code>RuleId</code> or <code>SourceOrigin</code>, and{" "}
        <code>CapExceeded</code> names no path either, so the two refusals are
        identical. The trace keeps the real path, and{" "}
        <Link href="/docs/policies/configuration#audit-refusals"><code>AuditRefusals</code></Link>{" "}
        writes every refused guess to the audit — a guess at a name that does not
        exist included, which no <code>[DwAudit]</code> could record. The{" "}
        <code>Convenience</code> tier still names the field, documented. See{" "}
        <Link href="/docs/policies/configuration#strict-refusals">What a strict refusal says</Link>.
      </p>
      <p>
        The strict tier closes the side doors too. Inside a <code>Segment</code> every
        field refusal is <code>FieldDeniedForSegment</code>, so a field denied for
        every clause but not for segments cannot answer by clause while a missing
        name answers for taking part. A name padded with dots or blank segments is
        normalized the way a real path is, so it cannot trip the navigation cap
        that a padded real field passes. <code>MaxQueryCost</code> is checked only
        after every field has passed its gate, so a field weighted by{" "}
        <code>[DwCost]</code> is refused as denied before its weight could set it
        apart from a name that does not exist. And <code>MissingContextValue</code>{" "}
        names neither the scope&apos;s column nor the context key it reads, which
        together describe how the rows are partitioned.
      </p>

      <h2 id="two-more">7 and 8. The two that are not channels</h2>
      <table>
        <thead><tr><th>Attack</th><th>Control</th></tr></thead>
        <tbody>
          <tr>
            <td>An unguarded DynamicWhere call on a type that requires a policy</td>
            <td><code>[DwEntity(RequirePolicy = true)]</code> throws <code>PolicyRequired</code> rather than returning rows. Only this library&apos;s own extension methods run the check, so plain EF Core or LINQ against the <code>DbSet</code> is not intercepted — the flag closes the hole in <em>this</em> API, not every route to the table.</td>
          </tr>
          <tr>
            <td>An empty policy store</td>
            <td>Attributes still enforce; an empty store never resolves to Allow</td>
          </tr>
        </tbody>
      </table>

      <h2 id="posture">Getting the posture right</h2>
      <ul>
        <li>Use <code>DwTier.Strict</code> unless you need <code>getQueryString</code>.</li>
        <li>Leave <code>IncludeTraceInResult</code> unset under <code>Strict</code>. A serialized result carries the trace to the caller; read it from <code>LastTrace</code> instead.</li>
        <li>Turn on <code>AuditRefusals</code> once an <code>IDwAuditSink</code> is registered, so a probe for hidden columns leaves a record.</li>
        <li>Leave <code>MinGroupSize</code> alone unless you have a reason; setting it to 1 is a decision, not a default.</li>
        <li>Prefer <code>Tokenize</code> over <code>Hash</code> where you can run a durable vault: neither hides equality, but only one of them can be undone by a leaked constant.</li>
        <li>Run <code>DwPolicy.ValidateModel(...)</code> at startup and treat its warnings as a checklist.</li>
        <li>Put <code>[DwEntity(RequirePolicy = true)]</code> on anything sensitive, so a DynamicWhere call that forgets <code>ApplyPolicy</code> fails loudly.</li>
        <li>Prefer <code>[DwOperators]</code> over allowing free filtering on a protected field.</li>
        <li>Set <code>DwCaps.DefaultPageSize</code> if the API does not page for itself. It ships off, and the request <code>MaxPageSize</code> never bounded is the one that sent no page at all.</li>
        <li>Keep <code>DwCaps.MaxConditionSets</code> near the number of sets your clients really send. A set with no conditions passes every other cap, and every set adds a condition or a subquery to the statement a segment becomes.</li>
      </ul>
    </DocPage>
  );
}
