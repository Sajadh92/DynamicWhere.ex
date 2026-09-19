import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Policy Configuration — options, caps, tiers and defaults",
  description: "Every DynamicWhere.ex policy option and cap with its default: tiers, what a denied field does to a projection, dry run, the trace on a result, refusal auditing, hash salt, store failure modes, query cost budget, MinGroupSize, plus startup validation and the twenty-two error codes.",
  keywords: ["DwPolicyOptions", "DwCaps", "MaxQueryCost", "MinGroupSize", "policy configuration"],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/policies/configuration/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/policies/configuration">
      <h1>Configuration</h1>
      <Code lang="csharp">{`DwPolicy.Configure(new DwPolicyOptions
{
    Tier                 = DwTier.Convenience,
    DryRun               = false,
    IncludeTraceInResult = null,               // null follows the tier: on here, off under Strict
    AuditRefusals        = false,              // true also audits every refused guarded query
    HashSalt             = secret,             // 16 characters or more
    TokenVault           = tokenVault,         // needed only by MaskStrategy.Tokenize
    Services             = serviceProvider,    // resolves IValueTransformer
    StoreFailure         = StoreFailureMode.LastKnownGood,
    MaxSnapshotAge       = TimeSpan.FromMinutes(15),
    RefreshInterval      = TimeSpan.FromSeconds(30),
}, providers);`}</Code>
      <Callout tone="warn" title="Frozen at startup, and refused on a second call">
        The posture is read by every request thread without synchronization. A
        tier that can change while requests are in flight is one that can be
        relaxed by a code path nobody expected to be security-relevant, so
        mutation after <code>Configure</code> throws.
      </Callout>
      <p>
        <code>AttributePolicyProvider</code> is added whether or not you pass it.
        Attributes are the sealed level, and a configuration that omitted them
        would let a store grant what the source refuses.
      </p>

      <h2 id="tiers">Tiers</h2>
      <table>
        <thead><tr><th>Tier</th><th>A denied field</th><th>Also</th></tr></thead>
        <tbody>
          <tr>
            <td><code>Convenience</code> (default)</td>
            <td>Is <strong>dropped</strong> from the projection or sort</td>
            <td><code>getQueryString</code> allowed; the trace is on the result; a refusal names the field</td>
          </tr>
          <tr>
            <td><code>Strict</code></td>
            <td><strong>Throws</strong></td>
            <td><code>getQueryString</code> throws; deny-select implies deny-where inside a <code>Segment</code>; the trace stays off the result; a field that does not exist is refused like a denied one, and no field refusal names the field</td>
          </tr>
        </tbody>
      </table>
      <p>
        A dropped field leaves nothing behind in the data, so{" "}
        <code>FilterResult&lt;T&gt;.Policy</code> is the only way a caller can
        tell a policy drop from a null value. That is why the convenience tier,
        which drops, puts the trace on the result unless{" "}
        <code>IncludeTraceInResult</code> is <code>false</code>.
      </p>

      <h2 id="navigation-selects">A navigation named in Selects</h2>
      <p>
        A <code>Selects</code> entry can name a navigation, such as{" "}
        <code>&quot;Lines&quot;</code>, rather than the fields beneath it. With
        nothing denied beneath it, the entry is kept as written. With a denied
        field beneath it, the <code>Convenience</code> tier replaces the entry
        with the allowed fields beneath it, and the <code>Strict</code> tier
        refuses it.
      </p>
      <table>
        <thead><tr><th><code>Selects</code> names a navigation</th><th><code>Convenience</code></th><th><code>Strict</code></th></tr></thead>
        <tbody>
          <tr><td>with nothing denied beneath it</td><td>Kept whole, or narrowed around a transform that lands on a property with no setter (3.2.0)</td><td>Kept whole, or narrowed the same way</td></tr>
          <tr><td>with a denied field beneath it</td><td>Replaced by the allowed fields beneath it</td><td><code>FieldDeniedForSelect</code></td></tr>
          <tr><td>whose key, <code>Lines.Id</code>, is denied (3.2.0)</td><td><code>FieldDeniedForSelect</code></td><td><code>FieldDeniedForSelect</code></td></tr>
          <tr><td>with a denied field beneath it, where the narrowing cannot be built (3.2.0)</td><td><code>FieldDeniedForSelect</code></td><td><code>FieldDeniedForSelect</code></td></tr>
          <tr><td>that can carry a field denied for <code>Select</code> that no path names: past four segments, in a framework generic, on a subtype, or unasked under a <code>&quot;*&quot;</code> deny (3.2.0)</td><td>Narrowed to the allowed fields where the core can narrow it; <code>FieldDeniedForSelect</code> where it cannot</td><td><code>FieldDeniedForSelect</code></td></tr>
        </tbody>
      </table>
      <ul>
        <li>
          The fields beneath a member are read the way the attribute walker
          reads them: through any collection type, and no deeper than its four
          segments. Since 3.2.0 a member typed{" "}
          <code>IReadOnlyList&lt;T&gt;</code>,{" "}
          <code>IReadOnlyCollection&lt;T&gt;</code>,{" "}
          <code>Collection&lt;T&gt;</code> or an application&apos;s own
          collection no longer hides the denials beneath it. The providers&apos;
          own rules are asked too, so a denied property with no setter and a
          rule on a path reached through a cycle count.
        </li>
        <li>
          A narrowing that cannot be built as it was gated is refused in both
          tiers (3.2.0). The core&apos;s typed projection adds the key,{" "}
          <code>Id</code>, of every nested node it builds, so a navigation
          narrowed around its own denied key would get the key back. The core
          reads a path only through an array, <code>List&lt;T&gt;</code>,{" "}
          <code>IList&lt;T&gt;</code>, <code>ICollection&lt;T&gt;</code>,{" "}
          <code>IEnumerable&lt;T&gt;</code>, <code>HashSet&lt;T&gt;</code> or{" "}
          <code>ISet&lt;T&gt;</code>, so a narrowing through any other
          collection fails its validation. And some members cannot be narrowed
          at all: a column, a complex property or a member stored as JSON, which
          EF Core reads whole; a member of a row in memory; and a member a
          projection builds some way the core cannot narrow.
        </li>
        <li>
          A named member can also carry a field denied for <code>Select</code>{" "}
          that no path names (3.2.0): one deeper than four segments, one inside a
          framework generic such as <code>Dictionary&lt;string, T&gt;</code>, or
          one a subtype of the member&apos;s type declares — a derived entity, a
          subclass, an interface&apos;s implementation. What it can carry is read
          from the source. On an entity it is read from the EF Core model, so
          only what loads counts: the navigation&apos;s columns, a converted one
          included, its owned chain at any depth, the navigations beneath it an
          include, an automatic include or a lazy loader fills, and each member
          the model does not map, read as its type, since its getter can hand
          out what EF Core loaded — for its type and every type the model
          derives from it. On a projected row it is the type the initializer
          constructs the member as, when it says, and otherwise the
          member&apos;s type and every loaded subtype of it, as on a row in
          memory. Under a policy with a <code>&quot;*&quot;</code> deny, a path the
          walk never asks about — past four segments, with no setter, or on a
          subtype — is a denied one unless the policy names it; around a cycle it
          always is. The <code>Strict</code> tier refuses
          such a member. The <code>Convenience</code> tier narrows it where the
          core can, which builds the declared type and so drops a subtype&apos;s
          fields; a path naming a framework generic itself narrows to nothing and
          is dropped. Where the core cannot narrow it — a column at the top of{" "}
          <code>T</code>, a member of a row in memory — both tiers refuse it.
        </li>
        <li>
          A navigation named through another, <code>Main.Lead</code>, gates the
          key of every node it passes through, which the core&apos;s projection
          adds, as a dotted path to a value always did (3.2.0). A denied key
          refuses the projection. Naming a field beside a denied key,{" "}
          <code>Lines.Name</code> when <code>Lines.Id</code> is denied, was
          already refused in both tiers.
        </li>
        <li>
          Under <code>Convenience</code> the refusal names the denied key, the
          first denied field beneath the member, or, for a denial no path names,
          the member itself. Under <code>Strict</code> its{" "}
          <code>FieldPath</code> is <code>&quot;*&quot;</code>, as on every field
          refusal. The trace records the reason either way.
        </li>
      </ul>

      <h2 id="no-selects">A request that sends no Selects</h2>
      <p>
        A request that sends no <code>Selects</code> returns whole rows, denied
        fields included, because the core projects only when{" "}
        <code>Selects</code> is set. So when a field denied for{" "}
        <code>Select</code> could reach the result, a guarded query synthesizes
        the projection itself. It does so in both tiers, typed and dynamic, for
        a whole <code>Filter</code> and for a <code>Segment</code>. A clause
        composed on its own, such as <code>Where</code>, <code>Order</code> or{" "}
        <code>Page</code>, synthesizes nothing.
      </p>
      <p>
        The projection keeps the <strong>allowed members</strong>: what an
        unguarded call would return, less what the policy withholds. Before
        3.2.0 it kept the allowed scalars only, and only a simple field denied
        at the top of the type asked for it — see{" "}
        <Link href="/docs/breaking-changes#synthesized-projection">breaking changes</Link>.
      </p>

      <h3 id="no-selects-when">When a projection is needed</h3>
      <ul>
        <li>
          A field denied at the top of <code>T</code> always asks for one,
          whatever it holds: a scalar, a blob, a list, an owned object or a JSON
          column.
        </li>
        <li>
          A field denied beneath a member asks for one when its value can reach
          the result. On an entity, that is beneath a column, an owned or
          complex member, or a navigation something loads: an{" "}
          <code>Include</code> or <code>ThenInclude</code> on the query, an
          automatic include, or a lazy loader — EF Core&apos;s proxies, an
          injected <code>ILazyLoader</code>, a loader delegate or{" "}
          <code>ILazyLoader</code> the constructor takes and keeps in a field or
          any property, the asynchronous loader delegate of EF Core 7, or an
          injected <code>DbContext</code> — which fills a navigation after the
          query. On a row a projection builds, it is beneath a member the
          initializer assigns; a constructor with arguments counts every member
          as assigned, and an initializer after it still says what its own
          bindings hold. On a row in memory, it is beneath any member. A rule may
          spell the path in any letter case.
        </li>
        <li>
          Every navigation counts as loaded where the library cannot read which
          the query loads: an <code>Include</code> in a form it cannot read, one
          off the query&apos;s own chain, and a chain that reaches its rows
          through anything but the root&apos;s own rows —{" "}
          <code>{`Select(o => o.Customer)`}</code>, a <code>SelectMany</code>, a{" "}
          <code>Join</code>, a <code>GroupBy</code> — when it also has an
          include, which EF Core applies from the root to the entities it
          reaches, or builds an object in one of its lambdas, as a projection
          behind an identity <code>Select</code>, a member of an anonymous row or
          a conditional does. Such a chain with neither is read from the model.
        </li>
        <li>
          A denial beneath a navigation nothing loads never leaves the database,
          so it asks for no projection. An entity whose only denials sit beneath
          such navigations is read as it was in 3.1.0.
        </li>
        <li>
          A member whose value can hold a field denied for <code>Select</code>{" "}
          that no path names — deeper than the walker&apos;s four segments,
          inside a framework generic such as{" "}
          <code>Dictionary&lt;string, T&gt;</code>, or declared by a subtype of
          its type — asks for one too, read as for a named member, so on an
          entity only what loads counts. Under a{" "}
          <code>&quot;*&quot;</code> deny, so does a member whose value can hold a
          path the walk never asks about and the policy does not name.
        </li>
        <li>
          A member that can hold an object of any type — one typed{" "}
          <code>object</code>, a framework interface such as{" "}
          <code>IComparable</code>, or a collection that is not generic, such
          as <code>IEnumerable</code>, <code>ArrayList</code> or an
          application&apos;s own — asks for nothing on its own: the policy cannot
          see into it whether or not a projection is built.
        </li>
        <li>
          A row can be a subtype of <code>T</code>. On an entity, a member a type
          the model derives from <code>T</code> declares, and what loads beneath
          it, counts as one of <code>T</code>&apos;s own would; on a row in memory,
          a member any loaded subtype declares does; on a row a projection
          builds, a member the type its initializer constructs declares below{" "}
          <code>T</code>. The projection builds <code>T</code> and leaves them
          out, recorded with a reason starting{" "}
          <code>left out: a type derived</code>. A subtype is any type loaded
          outside the framework&apos;s assemblies that derives from the type or
          implements it, an open generic one and an application&apos;s subclass
          of <code>Exception</code> included; a rule on a path through a
          subtype&apos;s member counts as a rule on the declared type&apos;s own
          path does.
        </li>
        <li>
          A member EF Core does not map counts as loaded: its getter can hand out
          a mapped field or a private navigation, so its type is read whole.
        </li>
        <li>
          The denials beneath a member come from the providers&apos; rules as
          well as from walking the type, so a denied property with no setter, a
          rule on a path reached through a cycle, and a rule deeper than the
          walk all count.
        </li>
        <li>
          A forced scope beneath a member asks for no projection on its own. It
          filters the rows that hold the member, as it always has. When a
          projection is needed anyway, the member is left out whole.
        </li>
      </ul>

      <h3 id="no-selects-kept">What it keeps</h3>
      <p>
        A member holding a value — a simple type, or a collection of one such as{" "}
        <code>byte[]</code>, <code>string[]</code> or{" "}
        <code>List&lt;string&gt;</code> — is kept when it is allowed and the
        source carries it. A member holding an object, or a list of them, is
        kept whole, narrowed or left out whole, as below, and only where the
        source carries it:
      </p>
      <table>
        <thead><tr><th>Source</th><th>Values kept</th><th>Objects kept</th></tr></thead>
        <tbody>
          <tr>
            <td>A projection that builds its rows before <code>ApplyPolicy</code>: the outermost <code>Select</code> constructs the row, in an object initializer or with a constructor, as in <code>{`db.Roles.Select(r => new RoleRow { … })`}</code></td>
            <td>Every member the initializer assigns; every member when a constructor with arguments builds the row, with or without an initializer after it</td>
            <td>The same</td>
          </tr>
          <tr>
            <td>An entity query, or a <code>Select</code> that hands back an entity, as in <code>{`db.Orders.Select(o => o.Customer)`}</code></td>
            <td>Every member EF Core maps</td>
            <td>Its columns, converted and JSON ones included, its owned members and, on EF Core 8 or later, its complex properties, read from the EF Core model</td>
          </tr>
          <tr>
            <td>Rows in memory, as in <code>roles.ApplyPolicy(caller)</code></td>
            <td>Every member</td>
            <td>None</td>
          </tr>
        </tbody>
      </table>
      <p>
        A value EF Core does not map is left out: computing it would make EF
        Core read the whole entity, the denied columns included, and it holds
        only its initial value anyway. A source the library cannot read — no EF
        Core model, and neither a projection it can see into nor rows in memory
        — keeps values only, as in 3.1.0, and every denial beneath a member
        counts.
      </p>

      <h3 id="no-selects-whole">Whole, narrowed or left out whole</h3>
      <p>A member holding an object that the source carries is:</p>
      <ul>
        <li>
          <strong>kept whole</strong> when nothing beneath it is denied, nothing
          its value can hold is denied (its subtypes included), it cannot hold an
          object of any type (asked of a projected row or a row in memory: what
          an entity read from the database never holds one), under a{" "}
          <code>&quot;*&quot;</code> deny every path beneath it the walk skips is
          one the policy names, no forced scope is beneath it, and no transform
          beneath it lands on a property with no setter;
        </li>
        <li>
          <strong>narrowed</strong> otherwise, to the allowed fields beneath it,
          four segments deep, as a caller naming it would get it, where the
          core&apos;s narrowing translates: an object the projection&apos;s
          initializer builds, a list a subquery reads into a type the core can
          bind (not an array or a set), a navigation that is neither complex nor
          stored as JSON, or an entity&apos;s owned member not stored as JSON.
          The narrowing builds the declared type, so a subtype&apos;s fields are
          dropped. A field beneath it that can hold what the policy cannot name
          is left out;
        </li>
        <li>
          <strong>left out whole</strong> otherwise, and recorded as{" "}
          <code>Dropped</code> on <code>Select</code> with a reason that starts{" "}
          <code>left out whole</code>.
        </li>
      </ul>
      <Code lang="text">{`left out whole: a scope forced beneath it cannot be applied to what it holds
left out whole: it is a column, which EF Core reads whole
left out whole: it is a complex property, which EF Core cannot narrow
left out whole: it is stored as JSON, which EF Core cannot narrow
left out whole: the projection builds it in a way the core cannot narrow
left out whole: the projection would add its key 'Contents.Id', which is denied
left out whole: the core cannot project 'Contents.Code'
left out whole: the core cannot build 'IContact', which it narrows into
left out whole: nothing beneath it may be selected
left out whole: it can hold what the policy cannot name`}</Code>
      <p>
        The last one is recorded on the field beneath the member that the
        narrowing leaves out.
      </p>

      <h3 id="no-selects-never">Never kept</h3>
      <ul>
        <li>
          An entity&apos;s navigation, included or not: projecting it would load
          it. So once a denial needs a projection, an included or automatically
          included navigation is not returned, and the trace records it as{" "}
          <code>Dropped</code> with a reason starting <code>left out:</code>.
          Under <code>Convenience</code>, name it in <code>Selects</code> to get
          it narrowed; under <code>Strict</code>, name its allowed fields.
        </li>
        <li>
          An object held by a row in memory: a kept object is the caller&apos;s
          own, and a transform beneath it would change it in place. The
          projection&apos;s rows are new and hold no member of an object type,
          so the source objects are left as they were.
        </li>
        <li>
          A member with no setter, and a member named with one of the
          expression parser&apos;s own words.
        </li>
      </ul>

      <h3 id="no-selects-other">Other rules</h3>
      <ul>
        <li>
          A narrowed reference that is null in the source comes back as an empty
          object, as it does for a caller&apos;s own dotted{" "}
          <code>Selects</code>.
        </li>
        <li>
          A narrowed member carries every allowed field beneath it. An entity
          reached beneath it therefore has its own navigations projected, and so
          loaded, whether or not the source included them, exactly as when{" "}
          <code>Selects</code> names the member.
        </li>
        <li>
          Each denied field whose value can reach the result, at the top or
          beneath, is recorded as <code>Dropped</code> on <code>Select</code>.
        </li>
        <li>
          It never throws for a denied field, in either tier. It throws{" "}
          <code>AllSelectsDenied</code> only when no field is left. When nothing
          asks for a projection, <code>Selects</code> stays null and the query is
          the one an unguarded call runs.
        </li>
        <li>
          A typed query projects into <code>T</code>, so <code>T</code> needs a
          public parameterless constructor, or the query fails with{" "}
          <code>SelectTypeMustHaveParameterlessConstructor</code>. The dynamic
          terminals do not need one.
        </li>
        <li>
          A dry run synthesizes nothing. It records the denials and returns the
          rows whole.
        </li>
        <li>
          A simulation has no source, so it reads <code>T</code> as a source it
          cannot see into: every denial beneath a member counts, and the
          projection it shows keeps only members holding a value — see{" "}
          <Link href="/docs/policies/admin#simulate">Simulate</Link>.
        </li>
      </ul>
      <Callout tone="warn" title="What the policy cannot see into">
        A member typed <code>object</code>, a framework interface or a
        collection that is not generic, such as <code>IEnumerable</code>,{" "}
        <code>ArrayList</code> or an application&apos;s own, is opaque to the
        policy. It never asks for a projection; when one is needed anyway, a
        projected row or a row in memory leaves it out and an entity keeps it;
        and naming it returns whatever it holds. A framework generic holding a
        policed type, such as <code>Dictionary&lt;string, LineDto&gt;</code>, has
        no paths beneath it: naming it is refused in both tiers where the core
        cannot narrow it, narrowed away under <code>Convenience</code> beneath a
        navigation, and a synthesized projection leaves it out. Hold such values
        in a list of the policed type instead.
      </Callout>
      <Callout tone="warn" title="A projection builds the declared type">
        A query over the root of a hierarchy whose derived type declares a
        denied field comes back as root-type rows, the derived types&apos;
        allowed fields dropped too. Over an abstract root the typed terminals
        fail with <code>SelectTypeMustHaveParameterlessConstructor</code>, and
        the dynamic ones return the root&apos;s members. Query the derived type,{" "}
        <code>{`OfType<Company>()`}</code>, to keep its fields. Rows in memory
        can be any loaded subtype, and the policy does not look at the rows:
        when a subtype declares a denied field, they are projected and their
        objects left out, even if no row is that subtype. Under a{" "}
        <code>&quot;*&quot;</code> deny, a member is kept whole only when every
        path beneath it the walk skips is one the policy names.
      </Callout>
      <Callout tone="warn" title="A forced scope on a list's element type filters rows, not elements">
        A forced scope declared on a list&apos;s element type filters the rows
        that hold the list, never its elements. <code>Selects</code> naming the
        list returns every element, those the scope excludes included, as in
        every release; a synthesized projection leaves such a list out. Scope
        the elements where the row is built.
      </Callout>

      <h2 id="trace">The trace on a result</h2>
      <p>
        Every guarded query records a <code>PolicyTrace</code>: what the policy
        dropped, refused, transformed or injected, and why.{" "}
        <code>IncludeTraceInResult</code> (<code>bool?</code>, default{" "}
        <code>null</code>) decides whether the terminals also put it on the result
        — <code>FilterResult&lt;T&gt;.Policy</code>,{" "}
        <code>SummaryResult.Policy</code> and{" "}
        <code>SegmentResult&lt;T&gt;.Policy</code>.
      </p>
      <table>
        <thead><tr><th><code>IncludeTraceInResult</code></th><th><code>Convenience</code></th><th><code>Strict</code></th></tr></thead>
        <tbody>
          <tr><td><code>null</code> (default)</td><td>On the result</td><td><code>Policy</code> is <code>null</code></td></tr>
          <tr><td><code>true</code></td><td>On the result</td><td>On the result</td></tr>
          <tr><td><code>false</code></td><td><code>Policy</code> is <code>null</code></td><td><code>Policy</code> is <code>null</code></td></tr>
        </tbody>
      </table>
      <p>
        The strict tier keeps it off because a result is where it reaches the
        caller. An API that serializes a result serializes the trace with it, and
        the trace names the fields a policy dropped, the attribute or rule that
        sealed each one, and every predicate injected on the caller&apos;s behalf —
        the detail that tier already refuses to return through{" "}
        <code>getQueryString</code>. The trace is recorded either way, on{" "}
        <code>PolicyQueryable&lt;T&gt;.LastTrace</code>, and audit events do not
        depend on the setting. It freezes with the posture and binds from the key{" "}
        <code>IncludeTraceInResult</code>. Before 3.1.0 every guarded result
        carried the trace, whatever the tier — see{" "}
        <Link href="/docs/breaking-changes#strict-trace-off-result">breaking changes</Link>.
      </p>
      <Code lang="csharp">{`// A strict deployment whose results never leave the server can keep the trace on them.
DwPolicy.Configure(new DwPolicyOptions
{
    Tier                 = DwTier.Strict,
    IncludeTraceInResult = true,
}, providers);

// Whatever the setting, the handle that ran a query holds what the policy did.
var guarded = db.Employees.ApplyPolicy(caller);
var result  = await guarded.ToListAsync(filter);
PolicyTrace? trace = guarded.LastTrace;`}</Code>

      <h2 id="strict-refusals">What a strict refusal says</h2>
      <p>
        A refusal that names the field it refused tells a probing caller that the
        field exists. Under the <code>Strict</code> tier, outside a dry run, a
        refusal says no more than which clause was refused:
      </p>
      <ul>
        <li>
          A field path that names nothing on the type does not fail validation. It
          is gated as a field denied for every feature, at the step where a denial is
          raised — after the caps — so it gets the code a{" "}
          <code>[DwDenied]</code> field gets in that clause:{" "}
          <code>FieldDeniedForWhere</code>, <code>FieldDeniedForSelect</code>,{" "}
          <code>FieldDeniedForOrder</code>, <code>FieldDeniedForGroup</code> or{" "}
          <code>FieldDeniedForAggregate</code>. A name padded with dots or blank
          segments, such as <code>NoSuchColumn....</code>, is normalized the way a
          real path is, so it is refused as a padded real field is.
        </li>
        <li>
          Inside a segment every field refusal is{" "}
          <code>FieldDeniedForSegment</code>, with <code>Feature</code>{" "}
          <code>Segment</code>, whichever clause refused it — a condition in any
          set, an order, a select, or the field taking part at all. A field denied
          for every clause but not for segments would otherwise answer by clause
          where a name that matches nothing answers for taking part. Filters and
          summaries keep their per-clause codes.
        </li>
        <li>
          Every refusal with one of those six codes has <code>FieldPath</code>{" "}
          <code>&quot;*&quot;</code>, a <code>null</code> <code>RuleId</code> and a{" "}
          <code>null</code> <code>SourceOrigin</code>, whether the field was denied,
          named through an alias, or does not exist, so the message is identical
          as well:{" "}
          <code>{`FieldDeniedForOrder: field '*', feature 'Order', tier 'Strict'.`}</code>
        </li>
        <li>
          A <code>CapExceeded</code> refusal has <code>FieldPath</code>{" "}
          <code>&quot;*&quot;</code> too, and its <code>SourceOrigin</code> still
          names the cap.
        </li>
        <li>
          <code>MissingContextValue</code> has <code>FieldPath</code>{" "}
          <code>&quot;*&quot;</code> and a <code>null</code>{" "}
          <code>SourceOrigin</code>. Together the scope&apos;s column and the
          context key it reads describe how rows are partitioned, so the message
          names neither.
        </li>
        <li>
          <code>MaxQueryCost</code> is checked after every field has passed its
          gate. A field weighted by <code>[DwCost]</code> that the caller may not
          use is refused as denied before its weight counts, as a name that matches
          nothing is, so the budget cannot tell them apart. An allowed weighted
          field still gets <code>QueryCostExceeded</code>.
        </li>
        <li>
          The trace keeps the real path and the reason: an unknown name is recorded
          as <code>Denied</code>, with the reason <code>names nothing on</code>{" "}
          followed by the type&apos;s name. The trace also keeps a missing context
          value&apos;s column and key. With{" "}
          <Link href="/docs/policies/configuration#audit-refusals"><code>AuditRefusals</code></Link>{" "}
          on, the audit event keeps the real field as well.
        </li>
      </ul>
      <p>
        The <code>Convenience</code> tier answers as it always did: an unknown
        field fails validation with <code>LogicException</code>{" "}
        <code>ConditionMustHasValidFieldName</code>, a refusal names the field
        as the caller wrote it, with <code>RuleId</code> and{" "}
        <code>SourceOrigin</code> where one source decided, and{" "}
        <code>MaxQueryCost</code> is checked before any field is gated. In a dry
        run, which refuses nothing, an unknown name fails validation in either
        tier. See <Link href="/docs/policies/security#probing">Security</Link>.
      </p>

      <h2 id="caps">Caps</h2>
      <table>
        <thead><tr><th>Cap</th><th>Default</th><th>Meaning</th></tr></thead>
        <tbody>
          <tr><td><code>MaxPageSize</code></td><td>1000</td><td>Largest page a caller may request.</td></tr>
          <tr><td><code>DefaultPageSize</code></td><td><strong>0</strong></td><td>Page given to a guarded query that asked for none. Zero, the default, leaves it unpaged.</td></tr>
          <tr><td><code>MaxConditions</code></td><td>50</td><td>Conditions in one filter.</td></tr>
          <tr><td><code>MaxConditionDepth</code></td><td>10</td><td>How deep a filter may nest its condition groups, counting the root group as one.</td></tr>
          <tr><td><code>MaxConditionSets</code></td><td>10</td><td>Condition sets in one segment. Each set adds to the one statement a segment becomes.</td></tr>
          <tr><td><code>MaxConditionValues</code></td><td>1000</td><td>Values in any one condition, such as the list of an <code>In</code>.</td></tr>
          <tr><td><code>MaxAggregates</code></td><td>50</td><td>Aggregates one summary computes.</td></tr>
          <tr><td><code>MaxOrderFields</code></td><td>10</td><td>Order fields in one query.</td></tr>
          <tr><td><code>MaxNavigationDepth</code></td><td>4</td><td>How deep a field path may reach.</td></tr>
          <tr><td><code>MaxQueryCost</code></td><td>1000</td><td>Budget consumed by <code>[DwCost]</code> weights.</td></tr>
          <tr><td><code>DefaultFieldCost</code></td><td>1</td><td>Charged for an unweighted field, and for an aggregate with no field.</td></tr>
          <tr><td><code>MaxAuditEvents</code></td><td>10000</td><td>Audit buffer before draining.</td></tr>
          <tr><td><code>SchemaDepth</code></td><td>2</td><td>Levels a schema request walks when it names no depth.</td></tr>
          <tr><td><code>SchemaCycleLimit</code></td><td>2</td><td>Times one type may appear on one path.</td></tr>
          <tr><td><code>MaxSchemaFields</code></td><td>2000</td><td>Fields one schema response may carry before it truncates.</td></tr>
          <tr><td><code>MinGroupSize</code></td><td><strong>5</strong></td><td>k-anonymity group floor. Set 1 to switch it off. See <Link href="/docs/policies/security">Security</Link>.</td></tr>
        </tbody>
      </table>
      <p>
        Every cap is frozen at startup, and a cap that refuses records it in the
        trace; <code>DefaultPageSize</code>, which refuses nothing, records
        nothing. Most refuse
        a value below one. Two accept <code>0</code> and refuse only a negative
        value: <code>DefaultFieldCost</code>, which is the posture for a model
        that weighs its few expensive fields and wants the rest free, and{" "}
        <code>DefaultPageSize</code>, where zero is how the page-filling stays
        switched off.
      </p>
      <p>
        They do not all refuse alike, and the error code says which fired.{" "}
        <code>MaxPageSize</code>, <code>MaxConditions</code>,{" "}
        <code>MaxConditionDepth</code>, <code>MaxConditionSets</code>,{" "}
        <code>MaxConditionValues</code>, <code>MaxAggregates</code>,{" "}
        <code>MaxOrderFields</code>,{" "}
        <code>MaxNavigationDepth</code> and <code>MaxAuditEvents</code> share{" "}
        <code>CapExceeded</code>, naming the cap in <code>SourceOrigin</code>.
        Under the <code>Convenience</code> tier <code>MaxNavigationDepth</code>{" "}
        and <code>MaxAuditEvents</code> also put the field&apos;s path on{" "}
        <code>FieldPath</code>; under <code>Strict</code> every{" "}
        <code>CapExceeded</code> has <code>FieldPath</code>{" "}
        <code>&quot;*&quot;</code>.{" "}
        <code>MaxQueryCost</code> is the one with a code of its own,{" "}
        <code>QueryCostExceeded</code>, because an operator reading a log needs
        to know which of the two refused: raising the wrong one changes nothing. The three schema caps never throw at all —
        a depth is clamped, a cycle is pruned, and <code>MaxSchemaFields</code>{" "}
        truncates and reports <code>truncated</code> — and{" "}
        <code>MinGroupSize</code> suppresses groups rather than refusing the
        query. <code>DefaultPageSize</code> refuses nothing either: it supplies
        a page rather than rejecting a request that carried none.
      </p>
      <p>
        <code>DefaultPageSize</code> is the other half of{" "}
        <code>MaxPageSize</code>, which only ever read a page the caller sent —
        so the one request no cap applied to was the request with no page at
        all. It returned the whole table, while the same request naming that
        page size was refused with <code>CapExceeded</code>. Set it, and a
        guarded query that sent no page is given <code>PageNumber = 1</code> and
        a size of <code>DefaultPageSize</code> bounded by{" "}
        <code>MaxPageSize</code>, so the two cannot be configured into
        contradicting each other. A page the caller did send is never replaced,
        and is still refused when it is too large. It ships off because filling
        one in on upgrade would truncate an existing caller&apos;s results with
        nothing in the response to say so. A page filled in for a caller who sent
        no orders is only as stable as the query&apos;s order, so pair it with a{" "}
        <Link href="/docs/policies/attributes#default-order"><code>[DwEntity(DefaultOrder = ...)]</code></Link>{" "}
        that ends with a unique field.
      </p>
      <p>
        It applies to a <code>Filter</code>, <code>Summary</code> or{" "}
        <code>Segment</code> on the composable methods as well as the terminal
        ones, so the composable <code>Filter</code>, <code>FilterDynamic</code>{" "}
        and <code>Summary</code> hand back a query that is already paged: page
        through the request&apos;s <code>Page</code>, not a <code>Page()</code>{" "}
        chained after it. <code>Where</code>, <code>Order</code>,{" "}
        <code>Select</code> and <code>Group</code> take no page and are never
        given one.
      </p>
      <p>
        A <code>Segment</code> is one statement: its condition sets are combined,
        ordered and paged in the database, so for a segment as for a filter{" "}
        <code>DefaultPageSize</code> and <code>MaxPageSize</code> bound what is
        read as well as what is returned.
      </p>
      <p>
        <code>MaxConditionDepth</code> bounds the shape{" "}
        <code>MaxConditions</code> says nothing about: fifty conditions in one
        flat group and fifty nested fifty deep both pass the count, and only the
        second makes the provider plan fifty parenthesised groups. The root
        group is depth one, so the default of ten allows nine levels of nesting
        under it — deeper than any filter a person writes and shallower than
        anything generated by accident. It refuses in both tiers like every
        other cap, and it is measured on a <code>Filter</code>&apos;s condition
        group, on the deeper of a <code>Summary</code>&apos;s conditions and its{" "}
        <code>Having</code>, and on each <code>Segment</code> condition set
        separately — a segment sums its conditions across every set, but its
        depth is the depth of one.
      </p>
      <p>
        <code>MaxConditionSets</code> bounds how large that statement can get.
        Every set adds to it — a condition for a <code>Union</code> or an{" "}
        <code>Intersect</code>, a <code>NOT EXISTS</code> subquery for an{" "}
        <code>Except</code> — and a set with no conditions spends nothing from{" "}
        <code>MaxConditions</code> or <code>MaxConditionDepth</code>, so the
        number of sets is the only bound on it. It counts every set the caller
        sent, empty or not, and refuses in both tiers with{" "}
        <code>CapExceeded</code>.
      </p>
      <p>
        <code>MaxConditionValues</code> bounds what one condition carries. An{" "}
        <code>In</code> or a <code>NotIn</code> is one comparison per value, so a
        single condition could hand the database a predicate of any size while
        spending one condition from <code>MaxConditions</code> and one field from{" "}
        <code>MaxQueryCost</code>. The condition carrying the most values is the
        one compared, wherever it sits: a filter&apos;s conditions, a
        summary&apos;s conditions and its <code>Having</code>, every set of a
        segment.
      </p>
      <p>
        <code>MaxAggregates</code> bounds the <code>AggregateBy</code> entries of
        one summary, through the summary terminals and the composable{" "}
        <code>Group</code> and <code>Summary</code>. Every aggregate is a column of
        every group, and one with no field — a <code>Count</code> — names nothing a
        weight could be set on, so it is also charged <code>DefaultFieldCost</code>{" "}
        toward <code>MaxQueryCost</code>. The count the group-size floor adds for
        itself is neither counted nor charged.
      </p>
      <p>
        The caps that count — <code>MaxConditions</code>,{" "}
        <code>MaxConditionDepth</code>, <code>MaxConditionSets</code>,{" "}
        <code>MaxConditionValues</code>, <code>MaxAggregates</code>,{" "}
        <code>MaxOrderFields</code> and <code>MaxPageSize</code> — are checked
        before any field name is resolved, so an oversized request is refused
        before its names are looked at, a name that does not exist included.{" "}
        <code>MaxNavigationDepth</code> needs a resolved path and runs after them.
      </p>
      <p>
        <code>MaxConditionDepth</code>, <code>MaxConditionSets</code>,{" "}
        <code>MaxConditionValues</code> and <code>MaxAggregates</code> are new in
        3.1.0, so a guarded request 3.0.0 ran — a filter nested eleven groups deep,
        a segment with eleven sets, a summary with fifty-one aggregates — is
        refused unless the deployment raises the cap. See breaking changes, for{" "}
        <Link href="/docs/breaking-changes#condition-depth-and-set-caps">the first two</Link>{" "}
        and <Link href="/docs/breaking-changes#values-and-aggregates-caps">the last two</Link>.
      </p>
      <p>
        <code>MinGroupSize</code> is the one that starts <em>unset</em> rather
        than at its default value, so that <code>MinGroupSize = 1</code> can mean
        &quot;no floor, and I mean it&quot; rather than being indistinguishable
        from a deployment that never configured anything.{" "}
        <code>IsMinGroupSizeSet</code> reports which of the two happened.
      </p>

      <h2 id="dryrun">Dry run</h2>
      <p>
        <code>DryRun</code> traces every decision without enforcing any of them,
        so a policy can be rolled out and watched before it starts refusing
        anything. It is also per-context, not only global, so a single canary
        role can run in dry run while everyone else is enforced — an
        all-or-nothing rollout is the thing nobody does.
      </p>
      <Code lang="csharp">{`var canary = new DwPolicyContext { DryRun = true }
    .WithSubject(DwSubjectKind.User, userId);`}</Code>

      <h2 id="audit-refusals">Auditing refusals</h2>
      <p>
        <code>[DwAudit]</code> records the <em>uses</em> of the fields it
        decorates. A caller probing for columns they may not read is refused at
        every guess, and a guess at a field without <code>[DwAudit]</code>, or at
        a name that does not exist, leaves nothing in that log. With{" "}
        <code>AuditRefusals = true</code>{" "}
        (default <code>false</code>), every <code>PolicyException</code> raised
        by a guarded entry point — each terminal and composable method of{" "}
        <code>PolicyQueryable&lt;T&gt;</code>, and <code>ApplyPolicy</code>&apos;s
        refusal of an unprepared context — is written to the caller&apos;s{" "}
        <code>DwPolicyContext</code> audit buffer,{" "}
        <code>PendingAuditEvents</code>. It drains to <code>IDwAuditSink</code>{" "}
        like any <code>[DwAudit]</code> event, through{" "}
        <code>DwPolicy.DrainAuditAsync</code> or the{" "}
        <Link href="/docs/policies/admin#audit">ASP.NET Core audit middleware</Link>.
        The setting freezes with the posture and binds from the key{" "}
        <code>AuditRefusals</code>.
      </p>
      <table>
        <thead><tr><th><code>DwAuditEvent</code> member</th><th>On a refusal</th></tr></thead>
        <tbody>
          <tr><td><code>ErrorCode</code></td><td>The refusal&apos;s <code>PolicyErrorCode</code>. New in 3.1.0, and <code>null</code> on an event that records a use of an audited field.</td></tr>
          <tr><td><code>EntityType</code></td><td>The full name of the type being queried.</td></tr>
          <tr><td><code>FieldPath</code></td><td>The field the refusal was about, by its canonical path in both tiers: the path an alias stands for, and under the <code>Strict</code> tier the real field although the refusal the caller received said <code>&quot;*&quot;</code>. A name that matches nothing is recorded as the caller sent it. A refusal of the whole request, such as <code>QueryStringDenied</code> or <code>PolicyContextNotPrepared</code>, records <code>&quot;*&quot;</code>; <code>MissingContextValue</code> names the scoped field.</td></tr>
          <tr><td><code>Feature</code></td><td>The feature the refusal concerned.</td></tr>
          <tr><td><code>Effect</code></td><td><code>Deny</code>.</td></tr>
          <tr><td><code>Subjects</code>, <code>Purpose</code>, <code>Tier</code></td><td>The caller and the posture, as on every event.</td></tr>
          <tr><td><code>DryRun</code></td><td><code>false</code>: the refusal was enforced. A dry run refuses no field, so it records no field refusal; a refusal it still raises, such as <code>PolicyContextNotPrepared</code>, is recorded with <code>DryRun</code> <code>false</code>.</td></tr>
        </tbody>
      </table>
      <ul>
        <li>
          A refusal is written at most once, and it is never changed or swallowed:
          the caller receives the same exception whether or not it was recorded.
        </li>
        <li>
          A recorded path is cut to 256 characters, followed by <code>…</code>.
          After the cut, every character in Unicode category Control (Cc), Format
          (Cf), Line Separator (Zl) or Paragraph Separator (Zp) is written as{" "}
          <code>\u</code> and four lowercase hex digits: a line feed as{" "}
          <code>\u000a</code>, U+2028 as <code>\u2028</code>, U+202E as{" "}
          <code>\u202e</code>. A character outside the Basic Multilingual Plane is
          judged whole, and both halves of its surrogate pair are escaped. An
          unknown name is text the caller wrote. A line break in it would forge a
          second entry in a log written one event per line, and a format character
          such as U+202E would reverse the text after it without showing itself.
        </li>
        <li>
          A buffer already holding <code>MaxAuditEvents</code> events records
          nothing, and the original refusal is still the one thrown.
        </li>
        <li>
          A refusal with no guarded context to record against is not written.{" "}
          <code>PolicyRequired</code>, raised by an unguarded read of a{" "}
          <code>RequirePolicy</code> type, is one.
        </li>
        <li>
          <code>DwAuditEvent.ToString()</code> includes the code when there is one.
          The constructor gains an overload that takes it as a tenth argument,{" "}
          <code>PolicyErrorCode? errorCode</code>; the nine-argument constructor is
          unchanged.
        </li>
      </ul>
      <Code lang="csharp">{`public sealed class LogAuditSink : IDwAuditSink
{
    private readonly ILogger<LogAuditSink> _log;

    public LogAuditSink(ILogger<LogAuditSink> log) => _log = log;

    public ValueTask WriteAsync(DwAuditEvent auditEvent, CancellationToken ct = default)
    {
        if (auditEvent.ErrorCode is PolicyErrorCode refused)
        {
            _log.LogWarning("{Code} on {Entity}.{Field}", refused, auditEvent.EntityType, auditEvent.FieldPath);
        }
        else
        {
            _log.LogInformation("{Feature} on {Entity}.{Field}", auditEvent.Feature, auditEvent.EntityType, auditEvent.FieldPath);
        }

        return default;
    }
}`}</Code>
      <Callout tone="warn" title="Off by default, because it changes what reaches a sink">
        A deployment that registered a sink for <code>[DwAudit]</code> starts
        receiving events that carry an <code>ErrorCode</code>, and one that
        registered none is warned about discarded events on every refused request
        by the audit middleware.
      </Callout>

      <h2 id="validate">Startup validation</h2>
      <Code lang="csharp">{`// Throws an InvalidOperationException listing every error, so reaching the
// next line already means the model is sound. Do not test report.Errors here:
// it is always empty by the time you can read it.
PolicyModelReport report = DwPolicy.ValidateModel(typeof(Employee), typeof(Customer));

foreach (var warning in report.Warnings) logger.LogWarning("{W}", warning);`}</Code>
      <p>
        <code>PolicyModelValidator.Inspect</code> is the same check without the
        throw, for a health endpoint or a report that wants to list the errors
        rather than fail on the first one.
      </p>
      <Code lang="csharp">{`PolicyModelReport inspected =
    PolicyModelValidator.Inspect(new[] { typeof(Employee), typeof(Customer) });

foreach (var error in inspected.Errors) logger.LogError("{E}", error);`}</Code>
      <p>
        Reported as a list rather than thrown one at a time, so a model is fixed
        in one pass instead of one exception per restart. Errors cover
        contradictions that cannot work — a text-emitting transform on a numeric
        member, a <code>[DwMutate]</code> type that is not an{" "}
        <code>IValueTransformer</code>, a default that cannot be read as the
        member type. Warnings cover things that work but probably should not,
        chiefly a transformed field that is still orderable.
      </p>
      <p>
        Every <code>[DwForceWhere]</code> is checked the way resolution checks
        it, so a malformed one is reported at startup rather than on the first
        guarded query of its type: one that sets neither or both of{" "}
        <code>Value</code> and{" "}
        <code>ContextValue</code>, a null check that sets either, a member whose
        type has no <code>DataType</code>, and <code>AllowNull = true</code> on{" "}
        <code>IsNull</code> or <code>IsNotNull</code> or on a member that can never
        be null. Before 3.1.0 these surfaced only when a query ran.
      </p>
      <p>
        A <Link href="/docs/policies/attributes#default-order"><code>[DwEntity(DefaultOrder = ...)]</code></Link>{" "}
        is read entry by entry. A default order is never a reason to refuse a
        query, so this scan is the only place a mistake in one is reported. For{" "}
        <code>[DwEntity(DefaultOrder = &quot;Missing desc, Watchers, Null, Secret, Rank, Region, Id sideways&quot;)]</code>{" "}
        on a <code>Ticket</code> whose <code>Watchers</code> is a collection of
        entities, whose <code>Null</code> is a member named after one of the
        expression parser&apos;s own words, whose <code>Secret</code> carries{" "}
        <code>[DwNoOrder]</code>, whose <code>Rank</code> carries{" "}
        <code>[DwNoOrder(Overridable = true)]</code> and whose <code>Region</code>{" "}
        carries <code>[DwDeny(PolicyFeature.Segment)]</code>:
      </p>
      <Code lang="text">{`Ticket: DefaultOrder entry 'Id sideways' is not a field optionally followed by asc or desc, so guarded queries skip it.
Ticket: DefaultOrder names 'Missing', which Ticket does not have, so guarded queries skip it.
Ticket: DefaultOrder names 'Watchers', which no query can order by, so guarded queries skip it.
Ticket: DefaultOrder names 'Null', which starts with a name the expression parser keeps for itself, so no query can use it. Rename the member.
Ticket: DefaultOrder names 'Secret', which its attributes deny for ordering, so every guarded query leaves it out.
Ticket: DefaultOrder names 'Rank', which its attributes deny for ordering unless a rule allows it, so guarded queries leave it out until one does.
Ticket: DefaultOrder names 'Region', which its attributes deny for segments, so guarded segments leave it out.`}</Code>
      <p>
        The first, third, fourth and fifth are errors: an entry that cannot be read
        meant something, a collection of entities holds no single value to sort by,
        a field whose first segment is one of the parser&apos;s own words —{" "}
        <code>new</code>, <code>iif</code>, <code>np</code>, <code>isnull</code>,{" "}
        <code>is</code>, <code>as</code>, <code>cast</code>, <code>true</code>,{" "}
        <code>false</code>, <code>null</code>, in any letter case — is one{" "}
        <Link href="/docs/breaking-changes#root-it-parent-members">no query can reach at all</Link>,
        and a field the type&apos;s own attributes seal against ordering is left out
        of every guarded query, so the declared order is never the one used. The
        rest are warnings. A model shared across types can name a field on purpose; a
        denial every attribute marks <code>Overridable</code> can be lifted by a
        rule for the callers it names, so <code>Rank</code> is left out only until
        one does; and <code>Region</code> is left out only of guarded segments,
        which refuse it in any clause, while a filter still orders by it.
      </p>

      <h2 id="from-a-file">Configuration from a file</h2>
      <p>
        Every value on the posture binds from <code>IConfiguration</code>. Three
        things cannot, because they are objects rather than values: the entity
        catalogue, the token vault and the service provider. Those stay in code,
        which is what the callback is for.
      </p>
      <Code lang="csharp">{`builder.Services.AddDwPolicies(
    builder.Configuration.GetSection("DynamicWhere:Policies"),
    options =>
    {
        options.Entities.Expose<Employee>("Employee");
        options.TokenVault = new RedisTokenVault(redis);
    });`}</Code>
      <Code lang="json">{`{
  "DynamicWhere": {
    "Policies": {
      "Tier": "Strict",
      "AuditRefusals": true,
      "StoreFailure": "LastKnownGood",
      "MaxSnapshotAge": "00:15:00",
      "Caps": {
        "DefaultPageSize": 100,
        "MinGroupSize": 5,
        "SchemaDepth": 2,
        "MaxSchemaFields": 2000
      }
    }
  }
}`}</Code>
      <p>
        Configuration binds first and the callback runs second, so a line
        somebody wrote deliberately is never overwritten by a file. Every key is
        optional, and an absent one leaves its default in place.
      </p>
      <Callout tone="warn" title="A key nothing answers to refuses to start">
        The binder&apos;s own default is to ignore an unmatched key, which would
        let <code>MinGropSize</code> sit in a file doing nothing while the
        deployment believed it had set a floor. Binding runs with{" "}
        <code>ErrorOnUnknownConfiguration</code>, so a typo fails at startup
        rather than silently switching a control off.
      </Callout>
      <p>
        Every setter&apos;s own validation still applies. A cap below one, a
        snapshot age that is not a positive interval and a salt shorter than
        sixteen characters are all refused exactly as they are in code. The group
        floor&apos;s opt-out survives unchanged, because it lives in the setter:
        saying nothing leaves it unset, writing <code>1</code> records a
        deliberate choice.
      </p>
      <Callout tone="danger" title="A salt in appsettings.json is not a salt">
        <code>HashSalt</code> binds like anything else, and configuration is the
        right channel for it — through user secrets, an environment variable or a
        vault. Committing it to a file in the repository is the thing the
        attribute refuses to allow, and nothing here can tell the difference.
      </Callout>

      <h2 id="performance">Performance</h2>
      <p>
        There are two budgets, because there are two costs. Gating is paid{" "}
        <strong>once per query</strong>. Transformation is paid{" "}
        <strong>per row per transformed field</strong>, so no single percentage
        describes it — the same guard is 1.16× over a hundred rows and 1.62× over
        ten thousand, on identical code.
      </p>
      <p>Measured with BenchmarkDotNet over 10,000 in-memory rows:</p>
      <table>
        <thead><tr><th>10,000 rows</th><th>Time</th><th>Allocated</th></tr></thead>
        <tbody>
          <tr><td>Unguarded</td><td>685 µs</td><td>210 KB</td></tr>
          <tr><td>Guarded, nothing denied or transformed</td><td>683 µs (<strong>1.00×</strong>)</td><td>220 KB (<strong>1.05×</strong>)</td></tr>
          <tr><td>Guarded, one field deny-select</td><td>785 µs (1.15×)</td><td>409 KB (1.95×)</td></tr>
          <tr><td>Guarded, two fields transformed every row</td><td>1,111 µs (1.62×)</td><td>1,488 KB (7.1×)</td></tr>
        </tbody>
      </table>
      <p>
        <strong>Gating costs nothing measurable.</strong> Resolving every field,
        sanitizing the filter and injecting forced predicates lands inside the
        noise of the unguarded query. A cached field resolve is 232–234 ns and
        sanitizing a five-condition filter is 2.8 µs.
      </p>
      <p>
        <strong>Deny-select costs 1.15×</strong>, because denying a field means
        the query projects instead of returning entities. That belongs to the
        feature rather than to the guard.
      </p>
      <p>
        <strong>Transformation costs about 21 ns and 65 bytes per value</strong>,
        against a design budget of 100 ns. It builds a new value for each one,
        because the change happens after materialization rather than in SQL.
      </p>
      <Callout tone="note" title="No database in those numbers">
        These are in-memory LINQ, so the policy layer share looks as large as it
        ever can. Against a real query the I/O dominates and the relative
        overhead is much smaller.
      </Callout>
      <Code lang="bash">{`dotnet run -c Release --project DynamicWhere.Benchmarks \\
  -- --filter "*PolicyBenchmarks*" --job medium`}</Code>

      <h2 id="errors">Error codes</h2>
      <p><code>PolicyException.ErrorCode</code>, values 1 to 22:</p>
      <table>
        <thead><tr><th>Code</th><th>Raised when</th></tr></thead>
        <tbody>
          <tr><td><code>FieldDeniedForWhere</code> … <code>FieldDeniedForSegment</code> (1–6)</td><td>A field is refused for that feature. <code>Where</code>, <code>Group</code>, <code>Aggregate</code> and <code>Segment</code> throw in <strong>both</strong> tiers, because dropping one of those would widen the result set or answer a different question. Only <code>Order</code> and <code>Select</code> are tier-dependent: the <code>Convenience</code> tier drops the clause instead. Under <code>Strict</code> a field path that names nothing gets the same code, and all six carry <code>FieldPath</code> <code>&quot;*&quot;</code> with no <code>RuleId</code> or <code>SourceOrigin</code>; inside a segment every one of them is <code>FieldDeniedForSegment</code> — see <Link href="/docs/policies/configuration#strict-refusals">What a strict refusal says</Link>.</td></tr>
          <tr><td><code>AllSelectsDenied</code> (7)</td><td>Every requested field was denied.</td></tr>
          <tr><td><code>OperatorNotAllowed</code> (8)</td><td>An operator outside the permitted set.</td></tr>
          <tr><td><code>CapExceeded</code> (9)</td><td>A cap above was exceeded; <code>SourceOrigin</code> names it. Under <code>Strict</code>, <code>FieldPath</code> is <code>&quot;*&quot;</code>.</td></tr>
          <tr><td><code>PolicyRequired</code> (10)</td><td>An unguarded query on a <code>RequirePolicy</code> type.</td></tr>
          <tr><td><code>RequiredFilterMissing</code> (11)</td><td>A <code>[DwRequireWhere]</code> field was not filtered on.</td></tr>
          <tr><td><code>MissingContextValue</code> (12)</td><td>A forced predicate needed a context value that was absent. Under <code>Strict</code>, <code>FieldPath</code> is <code>&quot;*&quot;</code> and <code>SourceOrigin</code> is <code>null</code>.</td></tr>
          <tr><td><code>AmbiguousFieldName</code> (13)</td><td>A name could mean more than one field.</td></tr>
          <tr><td><code>QueryStringDenied</code> (14)</td><td><code>getQueryString</code> in the <code>Strict</code> tier.</td></tr>
          <tr><td><code>AmbiguousGroupKey</code> (15)</td><td>Two groups of a summary share a key once their key values were transformed, so their aggregates cannot be added together without inventing a figure.</td></tr>
          <tr><td><code>TransformRequiresMaterialization</code> (16)</td><td>A transform on a query the caller materializes itself.</td></tr>
          <tr><td><code>StoreUnavailable</code> (17)</td><td>The store failed under <code>FailClosed</code>, or the context&apos;s pinned snapshot is older than <code>MaxSnapshotAge</code>.</td></tr>
          <tr><td><code>PolicyContextNotPrepared</code> (18)</td><td><code>ApplyPolicy</code> was handed a context that never went through <code>PrepareAsync</code> — refused whether or not a store is configured — or a store saw one it had attached nothing to, or the caller gained a <code>User</code> subject after preparation.</td></tr>
          <tr><td><code>QueryCostExceeded</code> (19)</td><td>The query cost budget was exceeded. Under <code>Strict</code> it is checked after every field gate.</td></tr>
          <tr><td><code>GroupTooSmall</code> (20)</td><td>A summary already uses the alias the group floor reserves.</td></tr>
          <tr><td><code>MissingHashSalt</code> (21)</td><td>A field masks to a hash and no salt was configured.</td></tr>
          <tr><td><code>MissingTokenVault</code> (22)</td><td>A field masks to a token and no vault was configured.</td></tr>
        </tbody>
      </table>
    </DocPage>
  );
}
