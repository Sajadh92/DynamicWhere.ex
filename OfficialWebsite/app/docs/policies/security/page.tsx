import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Security & k-anonymity — eight inference channels and five bypasses",
  description: "How DynamicWhere.ex closes the disclosure channels no per-field rule closes on its own — set-operation reconstruction, singleton-group aggregates, cardinality probes, sort-and-page binary search, SQL leakage, and schema probing through refusals — and the denials and transforms the gate could not see until 3.2.0 and 3.3.0.",
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
        then five bypasses that are not channels, then a path the query cannot compute
        — not a channel either, but the one request the tier used to answer with
        neither an answer nor a refusal — then the requests that, until 3.2.0 and
        3.3.0, carried out a denied or untransformed value the gate could not
        see; each has a test that reproduces the attack and goes red if the
        control is removed.
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
        listed every transformed column on the type — masked, generalized,
        truncated or formatted; and a deployment with no hash salt or token
        vault named the masked field it could not write.
      </p>
      <p>
        <strong>Closed by:</strong> under <code>Strict</code>, outside a dry
        run, an ambiguous name is refused exactly as an unknown name is. The
        grouping key, the hash salt and the token vault name the clause and
        carry no origin; the clause that cannot be transformed names the clause
        and keeps an origin, which names the method and what to call instead
        rather than any field. The trace keeps the real reason for the operator.{" "}
        <code>Convenience</code> and a dry run — the posture&apos;s switch or the
        caller&apos;s — are unchanged.
      </p>

      <h2 id="two-more">9 to 13. The five that are not channels</h2>
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
          <tr>
            <td>Read an audited member no path of the policy names: one only a subtype of the row&apos;s type declares, or one past the four segments the attribute walk reads, inside a row or a navigation returned whole</td>
            <td>
              The gate records a use by path, before the query runs, and such a
              member has no path it could ask about, so it came back with
              nothing written down. Since 3.3.0 the outbound walk&apos;s second
              pass reports each one it meets and the terminal records it: one
              event per path per query, for <code>Select</code>, with{" "}
              <code>Effect</code> <code>Mask</code> where the member is
              transformed as well. At <code>MaxAuditEvents</code> it fails
              closed as the gate does and the rows are withheld.
            </td>
          </tr>
          <tr>
            <td>Close the connection as the rows arrive, so the record of what was read is never written</td>
            <td>
              The audit middleware drained a request&apos;s events with the
              request&apos;s own abort token, so a client that hung up cancelled
              the write that follows the response: the sink threw, the middleware
              logged it, and the events went with the context — an audited read
              with nothing written down, for the price of a socket. Since 3.3.0
              the drain has a budget of its own, thirty seconds, which the caller
              cannot cancel and a hung sink cannot outlast.
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
        that projection copies from the entity. An anonymous type is left
        alone: EF Core follows each of its members to its argument. Since{" "}
        <strong>3.4.0</strong> a member or a row an application type&apos;s
        constructor builds with arguments is refused through every member the
        constructor leaves unbound, because EF Core follows a member only
        through an initializer&apos;s binding: <code>new LocalizedText(t.NameAr,
        t.NameEn).Ar</code> cannot be translated, where{" "}
        <code>new LocalizedText &#123; Ar = t.NameAr, En = t.NameEn &#125;.Ar</code>{" "}
        is the column. Rows in memory, a framework
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
        or not the caller names the member. Each request below carried a denied
        or untransformed value out, until 3.2.0 or, where the row says so,
        until 3.3.0 or 3.4.0. All are closed.
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
            <td>Name a path one segment beneath a denied member whose type the framework declares: <code>Salary.Value</code> on a <code>decimal?</code>, <code>Secret.Length</code>, <code>Born.Year</code>, <code>Bag.Count</code>, <code>Lines.Count</code> on an application&apos;s own collection class</td>
            <td>
              Such a path takes the policy of the member it reads since{" "}
              <strong>3.3.0</strong>: the deny effects per feature, the{" "}
              <code>[DwOperators]</code> restriction, the <code>[DwCost]</code>{" "}
              weight and the audited features, from whichever provider supplied
              them. No attribute can be placed there and no fragment named it,
              so it resolved as allowed: a <code>[DwDenied] decimal?</code> was
              filtered on, sorted by, grouped by with its values as the group
              keys, aggregated and handed back by a dynamic projection, under{" "}
              <code>Strict</code>. A transformed member gave its stored value the
              same way, an audited one was read with nothing recorded, and a
              weighted one cost the default. What is said to the caller about the
              member stays the member&apos;s: the alias, the required filter, the
              forced scope and the description.
            </td>
          </tr>
          <tr>
            <td>Name a path beneath a denied member holding an application&apos;s own struct: <code>Iban.Number</code> beneath a <code>[DwDenied] Iban</code></td>
            <td>
              Such a path takes the struct member&apos;s policy since{" "}
              <strong>3.4.0</strong>, beside its own: its deny effects, its{" "}
              <code>[DwOperators]</code> restriction, its <code>[DwCost]</code>{" "}
              weight and its audited features. A struct is a value, and it was
              read as a navigation whose members are separate fields, so{" "}
              <code>Iban.Number</code> was filtered on — a test for a guessed
              value — sorted by, grouped by with its values as the group keys and
              handed back by a dynamic projection, under <code>Strict</code>.
            </td>
          </tr>
          <tr>
            <td>Name a member of a nullable struct through <code>Value</code>: <code>Iban.Value.Number</code> or <code>Iban.HasValue</code> beneath a <code>[DwDenied] Iban?</code>, a <code>[DwDenied]</code> <code>Pair.Value.Hidden</code>, a masked <code>Pair.Value.Code</code></td>
            <td>
              Policed since <strong>3.4.0</strong>. The query spells the path
              through the nullable where the policy names it without, so the
              lookup matched nothing: each was filtered on, sorted by, grouped by
              and selected, typed and dynamic, and a mask was not applied. The
              policy now drops the <code>Value</code> after a nullable struct of
              the application&apos;s and reads <code>HasValue</code> as the member
              itself. Present since the policy layer shipped.
            </td>
          </tr>
          <tr>
            <td>Name one member of each struct in a collection in a typed selection: <code>Pairs.Shown</code> over a <code>List&lt;Pair&gt;</code> whose <code>Pair.Hidden</code> is denied</td>
            <td>
              Each element is built with what was named since{" "}
              <strong>3.4.0</strong>. The typed projection bound a collection of
              values whole, so every <code>Pair</code> came back whole, the denied
              member included, in both tiers, while the policy had approved only{" "}
              <code>Pairs.Shown</code>. Present since the policy layer shipped; the
              dynamic terminals were never affected.
            </td>
          </tr>
          <tr>
            <td>Read a masked member of an application&apos;s own struct: <code>[DwMask]</code> on <code>LocalizedText.Code</code>, in a whole row or with the struct selected whole</td>
            <td>
              Transformed since <strong>3.4.0</strong>. The struct was read as a
              copy and the setter wrote into a second one, so every transform on a
              struct&apos;s member landed on a temporary and the stored value came
              back, typed and dynamic, in both tiers. Each changed struct is now
              written back where it was read from, and one that cannot be — held
              by a member with no setter, in a set, as a dictionary&apos;s value —
              fails the query instead.
            </td>
          </tr>
          <tr>
            <td>Raise <code>Caps.MaxNavigationDepth</code> above 4 and name a denied member five or more segments out</td>
            <td>
              The attributes of the member at the end of such a path are read
              directly since <strong>3.3.0</strong>. No fragment of the attribute
              walk, which stops at four segments, reached it, so the member was
              filtered on, grouped by and returned under <code>Strict</code>.
              Default configuration was never exposed to this one.
            </td>
          </tr>
          <tr>
            <td>Read a masked member the policy names no path to: five segments down an included or in-memory graph, one only a subtype of the row&apos;s type declares, one on an object a dictionary holds, one on the far side of a cycle</td>
            <td>
              The rows are walked by run-time type as well since{" "}
              <strong>3.3.0</strong>, and a member that declares a transform and
              was not transformed along a named path is transformed by its own
              attributes, once. The outbound walk transformed along the named
              paths only, so each of these came back exactly as stored — at the
              default caps, under <code>Strict</code>, with no{" "}
              <code>Selects</code>, with the navigation named whole in{" "}
              <code>Selects</code>, and in a dynamic projection holding a real
              object.
            </td>
          </tr>
          <tr>
            <td>Compose <code>SelectDynamic</code>, <code>Group</code>, <code>FilterDynamic</code> or <code>Summary</code> on a type whose only transformed member sits where the policy names no path</td>
            <td>
              Refused with{" "}
              <code>TransformRequiresMaterialization</code> since{" "}
              <strong>3.3.0</strong>. The four hand back a query the library
              never sees materialized, and whether the type is transformed was
              read from the named paths alone, so such a type got its query and
              its rows exactly as stored — the same gap, one method call away
              from the terminals. The refusal asks what a row of the type can
              hold as well, and names the clause when it has no column to list.
            </td>
          </tr>
          <tr>
            <td>Declare a <code>[DwForceWhere]</code> on a type first met at the walk&apos;s fourth segment, then reach that type by a shorter path</td>
            <td>
              The walk returned at its depth limit with the type still marked as
              being inside it, so the type read as a cycle wherever it was met
              again — and what a cycle leaves out, the forced scope, the{" "}
              <code>[DwRequireWhere]</code> and the <code>[DwAlias]</code>, was
              left out of the shorter path. Which of two members was declared
              first decided whether a tenant scope applied. Since{" "}
              <strong>3.3.0</strong> all three apply on every path within four
              segments that is not around a cycle.
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
        <li>Give a durable token vault a key (3.3.0), and hold it where the store is not. Unkeyed, a mapping is stored under a plain digest of the value, and a tokenized column is nearly always drawn from a space small enough to hash whole — so a backup, a replica or a dump of the vault gives back every value in it, and with them the value behind every token ever issued. See <Link href="/docs/policies/transforms#vault-key">Transforms</Link>.</li>
        <li>Run <code>DwPolicy.ValidateModel(...)</code> at startup and treat its warnings as a checklist.</li>
        <li>Put <code>[DwEntity(RequirePolicy = true)]</code> on anything sensitive, so a DynamicWhere call that forgets <code>ApplyPolicy</code> fails loudly.</li>
        <li>Prefer <code>[DwOperators]</code> over allowing free filtering on a protected field.</li>
        <li>Hold a policed type in a list, never in a dictionary, another framework generic or a member typed <code>object</code>. The policy has no paths into any of them.</li>
        <li>Scope a list&apos;s elements where the row is built. A forced scope on the element type filters the rows that hold the list, never the elements.</li>
        <li>Set <code>DwCaps.DefaultPageSize</code> if the API does not page for itself. It ships off, and the request <code>MaxPageSize</code> never bounded is the one that sent no page at all.</li>
        <li>Give an export or a report a purpose of its own in <code>DwCaps.Purposes</code> (3.4.0) rather than raising the screens&apos; <code>MaxPageSize</code>, and set <code>DwPolicyContext.Purpose</code> in the endpoint that serves it. Never bind the purpose from a request: a caller who could name it could name its caps, and whatever purpose-bound grants exist.</li>
        <li>Keep <code>DwCaps.MaxConditionSets</code> near the number of sets your clients really send. A set with no conditions passes every other cap, and every set adds a condition or a subquery to the statement a segment becomes.</li>
      </ul>
    </DocPage>
  );
}
