import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Security & k-anonymity — the eleven inference channels",
  description: "How DynamicWhere.ex closes the disclosure channels no per-field rule closes on its own — set-operation reconstruction, singleton-group aggregates, cardinality probes, sort-and-page binary search, SQL leakage, and schema probing through refusals — and the denials the gate could not see until 3.2.0.",
  keywords: ["k-anonymity", "MinGroupSize", "inference attack", "data disclosure", "aggregate disclosure", "EF Core security"],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/policies/security/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/policies/security">
      <h1>Security &amp; k-anonymity</h1>
      <p>
        Denying a field is easy. The hard part is the set of ways a caller can
        learn a value <em>without</em> reading it. Eight such channels follow,
        then three bypasses that are not channels, then a path the query cannot compute
        — not a channel either, but the one request the tier used to answer with
        neither an answer nor a refusal — then the requests that, until 3.2.0,
        carried out a denied value the gate could not see; each has a test that
        reproduces the attack and goes red if the control is removed.
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

      <h2 id="audit-cap">7. The audit cap answers differently for a real field</h2>
      <p>
        An audited field records an event per use, and the query is refused
        rather than the record dropped when <code>DwCaps.MaxAuditEvents</code>{" "}
        is reached. Until 3.3.0 that refusal carried <code>CapExceeded</code>{" "}
        and a <code>SourceOrigin</code> naming the cap, where a name matching
        nothing carried the ordinary field refusal and no origin. One guess per
        request therefore told a caller which names are real and audited — which
        is to say, exactly the fields <code>[DwAudit]</code> is put on, since an
        unknown name is never audited and never reaches the cap.
      </p>
      <p>
        <strong>Closed by:</strong> under <code>Strict</code>, outside a dry
        run, the cap refuses with the clause&apos;s own field refusal — same
        code, <code>FieldPath</code> <code>&quot;*&quot;</code>, no origin. The
        request still fails, so the buffer still fails closed, and the trace
        still records which refusal it really was.
      </p>

      <h2 id="names-the-clause">8. Four refusals that named a field</h2>
      <p>
        A strict refusal names no field, so that a denied field, a misspelling
        and a field that does not exist cannot be told apart. Four refusals
        named one anyway: an ambiguous name said the caller&apos;s guess matched
        more than one field, and so at least one; an ambiguous grouping key
        reported the column behind the caller&apos;s alias and said its values
        are transformed; the refusal for a clause that cannot be transformed
        listed every masked column on the type; and a deployment with no hash
        salt or token vault named the masked field it could not write.
      </p>
      <p>
        <strong>Closed by:</strong> under <code>Strict</code>, outside a dry
        run, an ambiguous name is refused exactly as an unknown name is, and the
        other three name the clause with no origin. The trace keeps the real
        reason for the operator. <code>Convenience</code> and a dry run are
        unchanged.
      </p>

      <h2 id="two-more">9, 10 and 11. The three that are not channels</h2>
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
          <tr>
            <td>Reading an audited field by sending no <code>Selects</code></td>
            <td>
              A use is what the request reads, not only what it spells out. Until
              3.3.0 only a field the request named was recorded, so a caller who
              named none received every audited member of the row with nothing
              written down — one token past <code>[DwAudit]</code>. Every audited
              member a projection the caller did not name hands back is recorded
              for <code>Select</code>, one event per query rather than per row.
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="unexpressible">A path the query cannot compute</h2>
      <p>
        A member of a row&apos;s type is not always a value a database can
        produce. A shared kernel type carrying two columns and a getter over
        them gives <code>Name.Ar</code> and <code>Name.En</code>, which
        translate, and <code>Name.IsEmpty</code>, which does not. The policy has
        nothing to say about the third — <code>[DwNoWhere]</code> on{" "}
        <code>Name</code> matches that path and not the ones beneath it — so
        until <strong>3.3.0</strong> every check passed and EF Core threw. The
        caller got a five-hundred where the strict tier promises a refusal.
      </p>
      <p>
        Such a path is now refused as an unknown name is: the clause&apos;s own
        code, <code>FieldPath</code> <code>&quot;*&quot;</code>, so it cannot be
        told from a misspelling or from a field the caller may not use. It
        applies to every clause the database has to compute, and not to{" "}
        <code>Selects</code>, which EF Core evaluates on the client when it
        cannot translate it. It is refused only where the whole set of members a
        container can produce is known — an entity&apos;s own EF Core model, and the initializers of a
        projection composed before <code>ApplyPolicy</code>, including a member
        that projection copies from the entity. A projection that builds its
        rows any other way — an anonymous type, a constructor with arguments —
        says nothing about which member each value sets, so no member of such a
        row is refused here. Rows in memory, a framework
        member the provider translates such as <code>Length</code> or{" "}
        <code>Year</code>, anything beneath a column, the convenience tier and a
        dry run are all unchanged: the path is left alone, and behaves exactly
        as it does unguarded — which for rows in memory and a framework member
        means it runs and returns rows, and beneath a converted column means the
        provider decides.
        So is a query a provider in front of EF Core translates — LinqKit&apos;s{" "}
        <code>AsExpandable()</code>, DelegateDecompiler&apos;s{" "}
        <code>Decompile()</code>, or a host&apos;s own registered through{" "}
        <code>ReplaceService&lt;IAsyncQueryProvider, …&gt;</code> — since such a
        provider may rewrite what EF Core cannot, and the library cannot tell
        one that does from one that passes straight through. The test is EF
        Core&apos;s own provider type, from EF Core&apos;s own assembly.
      </p>
      <p>
        The refusal raises no <code>[DwAudit]</code> event, for the reason an
        unknown name raises none: no field was read, and the refusal names none.{" "}
        <code>AuditRefusals</code> records it, and the trace carries the real
        path. A simulation is handed no source, so it cannot refuse such a path
        at all.
      </p>
      <p>
        This closes no leak: the query failed, it did not answer. It removes a
        way of telling one member from another by the shape of the failure, and
        it keeps the tier&apos;s promise that a guarded request is answered or
        refused. The trace records the reason.
      </p>

      <h2 id="beneath">Denials the gate could not see</h2>
      <p>
        A denied field often sits on a type the query reaches through a member:
        a secret on each line of an order, a code inside a nested object. The
        denial holds on every path that reaches it, and it has to hold whether
        or not the caller names the member. Until 3.2.0 each request below
        carried a denied value out. All are closed.
      </p>
      <table>
        <thead><tr><th>Attack</th><th>Control</th></tr></thead>
        <tbody>
          <tr>
            <td>Send no <code>Selects</code>, on a type whose only denied fields sit beneath a member</td>
            <td>
              A guarded query synthesizes a projection whenever the denied value
              can reach the result, and narrows the member around it or leaves
              the member out. Only a simple field denied at the top of{" "}
              <code>T</code> used to synthesize one, so the whole row came back
              with the denied value in it: in a list or nested object of a row
              projected before <code>ApplyPolicy</code>, in a row held in memory,
              and in an entity&apos;s included, automatically included, lazily
              loaded or owned member — in both tiers. A denial beneath a
              navigation nothing loads never leaves the database, so it asks for
              nothing. See{" "}
              <Link href="/docs/policies/configuration#no-selects">A request that sends no Selects</Link>.
            </td>
          </tr>
          <tr>
            <td>Send no <code>Selects</code>, on a type whose denied field holds no simple value: a blob, a list, an owned object, a JSON column</td>
            <td>
              A field denied at the top of <code>T</code> asks for the projection
              whatever it holds. Such a field used to be passed over, so with
              nothing else denied the whole row came back with it.
            </td>
          </tr>
          <tr>
            <td>Name a navigation whose key, <code>Id</code>, is denied</td>
            <td>
              Refused with <code>FieldDeniedForSelect</code> in both tiers. The
              core&apos;s typed projection adds the key of every nested node it
              builds, so the convenience tier used to narrow the key away and
              get it back. A navigation named through another,{" "}
              <code>Main.Lead</code>, now gates the key of <code>Main</code> as
              well, which the projection adds.
            </td>
          </tr>
          <tr>
            <td>Name a member typed <code>IReadOnlyList&lt;T&gt;</code>, or another collection the core does not unwrap, with a denied field beneath it</td>
            <td>
              Refused with <code>FieldDeniedForSelect</code> in both tiers. The
              projection gate now reads collections the way the attribute walker
              does. It used to read them through a narrower list, found nothing
              beneath such a member, and returned every field, the denied ones
              included, in both tiers.
            </td>
          </tr>
          <tr>
            <td>Name a member that carries a denied field no path reaches: deeper than four segments, inside a framework generic such as <code>Dictionary&lt;string, T&gt;</code>, or, on an entity&apos;s navigation, in its owned chain or a converted column</td>
            <td>
              Refused under <code>Strict</code>. Under <code>Convenience</code> it
              is narrowed where the core can narrow it and refused where it
              cannot. What the member carries is read from the source — from the
              EF Core model for an entity, so only what loads counts. The gate
              also reads the rules themselves, so a denied property with no
              setter and a rule on a path reached through a cycle are found
              beneath a named member too.
            </td>
          </tr>
          <tr>
            <td>Include a navigation from the root, then reach the rows through it — <code>{`Select(o => o.Customer)`}</code>, <code>SelectMany</code>, <code>Join</code> — or hide a projection behind another <code>Select</code></td>
            <td>
              Every navigation counts as loaded on such a chain, since EF Core
              still applies includes named from the root to the entities it
              reaches, and the library cannot read which. The includes used to be
              read against the wrong root, so the denied value beneath them was
              returned.
            </td>
          </tr>
          <tr>
            <td>Let a lazy loader fill a navigation after the query: a loader delegate or <code>ILazyLoader</code> the constructor takes, kept in a field or a property of any name</td>
            <td>
              Counts as loading every navigation, as EF Core&apos;s proxies and an
              injected <code>ILazyLoader</code> property already did. The model
              keeps no record of such a loader, so the navigation it filled came
              back with the denied value.
            </td>
          </tr>
          <tr>
            <td>Declare the denied field on a subtype — a derived entity, a subclass, an interface&apos;s implementation — and read it through the base type: a query over the hierarchy&apos;s root, or a member declared as the base type</td>
            <td>
              The subtypes are read too: the types the EF Core model derives for
              an entity, and for a projected or in-memory row every loaded
              subtype, an open generic one and an application&apos;s subclass of a
              framework class such as <code>Exception</code> included. Such rows
              are projected to <code>T</code> and such members narrowed to the
              declared type; a named one is refused under <code>Strict</code>. A
              projection constructing a subtype of <code>T</code> is read as it,
              and a rule on a subtype&apos;s field through a base-typed member is
              enforced. The policy used to read the declared type only.
            </td>
          </tr>
          <tr>
            <td>Put the <code>[DwDenied]</code> on an override, on a public member a subtype hides with <code>new</code>, or on a class&apos;s implementation of an interface member, and read the member through the base type or the interface, a variant instantiation of it included</td>
            <td>
              The denial applies to the path for every row, in every clause. The
              attribute walker read the declaration it walked and the attributes
              above it, never an override, a hiding member or an implementation
              below, so the base path filtered, sorted, grouped and returned the
              value.
            </td>
          </tr>
          <tr>
            <td>Guard a query through a provider that wraps EF Core&apos;s, as LinqKit&apos;s <code>AsExpandable</code> or DelegateDecompiler&apos;s <code>Decompile</code> do</td>
            <td>
              The query runs untracked. EF Core&apos;s <code>AsNoTracking</code>{" "}
              hands such a query back unchanged, so it tracked: the context filled
              in navigations it already held, the denied ones included, and a
              masked value became a pending change the next{" "}
              <code>SaveChanges</code> would write. The call now goes into the
              query itself.
            </td>
          </tr>
          <tr>
            <td>Under a <code>&quot;*&quot;</code> deny with exact allows, reach a path the walk never asks about: past four segments, around a cycle, a property with no setter</td>
            <td>
              Such a path is denied, so a member holding one is narrowed, left out
              or refused. It used to resolve as allowed, so the member was
              returned whole, named or not.
            </td>
          </tr>
          <tr>
            <td>Put the policed type in an application namespace that starts with <code>System</code>, such as <code>SystemsCorp.Payroll</code></td>
            <td>
              Policed. The walker read any namespace starting with{" "}
              <code>System</code> as the framework&apos;s and put no policy
              beneath its types, so a <code>[DwDenied]</code> field there was
              returned, filterable and sortable. Only <code>System</code> and the
              namespaces beneath it are the framework&apos;s now.
            </td>
          </tr>
        </tbody>
      </table>
      <Callout tone="warn" title="What the policy cannot see into">
        A member typed <code>object</code>, a framework interface or a
        collection that is not generic, such as <code>IEnumerable</code>,{" "}
        <code>ArrayList</code> or an application&apos;s own, is opaque to the
        policy: it never asks for a projection, a synthesized projection over a
        projected row or rows in memory leaves it out, and naming it returns
        whatever it holds. A framework generic holding a policed type, such as{" "}
        <code>Dictionary&lt;string, LineDto&gt;</code>, has no paths beneath it:
        naming it is refused in both tiers where the core cannot narrow it,
        narrowed away under <code>Convenience</code> beneath a navigation, and a
        synthesized projection leaves it out. Hold such values in a list of the
        policed type instead. A member EF Core does not map is read as its type,
        since its getter can hand out what EF Core loaded; a getter that copies a
        denied column into a type with no denial is the application&apos;s to
        withhold.
      </Callout>
      <Callout tone="warn" title="A forced scope on a list's element type filters rows, not elements">
        A forced scope declared on a list&apos;s element type filters the rows
        that hold the list, never its elements. <code>Selects</code> naming the
        list returns every element, those the scope excludes included, as in
        every release; a synthesized projection leaves such a list out. Scope
        the elements where the row is built.
      </Callout>

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
        <li>Hold a policed type in a list, never in a dictionary, another framework generic or a member typed <code>object</code>. The policy has no paths into any of them.</li>
        <li>Scope a list&apos;s elements where the row is built. A forced scope on the element type filters the rows that hold the list, never the elements.</li>
        <li>Set <code>DwCaps.DefaultPageSize</code> if the API does not page for itself. It ships off, and the request <code>MaxPageSize</code> never bounded is the one that sent no page at all.</li>
        <li>Keep <code>DwCaps.MaxConditionSets</code> near the number of sets your clients really send. A set with no conditions passes every other cap, and every set adds a condition or a subquery to the statement a segment becomes.</li>
      </ul>
    </DocPage>
  );
}
