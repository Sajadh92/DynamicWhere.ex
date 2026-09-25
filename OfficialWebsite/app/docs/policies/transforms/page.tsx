import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Transforms & Masking — nine strategies and five other transforms",
  description: "How DynamicWhere.ex changes values on the way out: nine mask strategies including hashing and tokenization, generalization, mutation, defaults, truncation and formatting, applied in memory after materialization.",
  keywords: ["data masking .NET", "MaskStrategy", "EF Core data masking", "generalization k-anonymity", "PII masking"],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/policies/transforms/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/policies/transforms">
      <h1>Transforms &amp; Masking</h1>
      <p>
        Transformation happens <strong>in memory, after materialization</strong>
        — never in SQL. Filtering and sorting still run against the real values
        in the database; what reaches the caller is changed on the way out.
      </p>

      <Callout tone="warn" title="Why that matters more than it sounds">
        Because the real value is what the database sorts and aggregates, a
        masked field that is still sortable or aggregatable leaks. Both have
        controls, and both are on <Link href="/docs/policies/security">Security</Link>.
      </Callout>

      <h2 id="detach">Detached, then transformed</h2>
      <p>
        Entities returned from a guarded query are detached before anything is
        changed. If they were tracked, the masked value would be a pending
        modification and the next unrelated <code>SaveChanges</code> on the same
        context would write asterisks over the real data — silently, with no
        exception and no way back without a restore.
      </p>
      <p>
        A transform on a query the caller materializes itself is refused with{" "}
        <code>TransformRequiresMaterialization</code> rather than skipped, so a
        composable method cannot hand back an <code>IQueryable</code> that quietly
        never masks anything. Since <strong>3.3.0</strong> that refusal asks
        what a row of the type can hold as well as which paths the policy
        names: a type whose only transform sits where no path reaches it — on a
        member only a subtype declares, one five segments down, one of an object
        a dictionary holds — was handed the query, and its rows came back
        exactly as stored. With no named column to list, the refusal names the
        clause, <code>FieldPath</code> <code>&quot;*&quot;</code>, in both
        tiers. A type nothing transforms anywhere still gets its query.
      </p>

      <h2 id="mask">The nine mask strategies</h2>
      <table>
        <thead><tr><th>Strategy</th><th>Result</th></tr></thead>
        <tbody>
          <tr><td><code>Full</code></td><td>Every character replaced.</td></tr>
          <tr><td><code>Partial</code></td><td>Keeps <code>KeepStart</code> and <code>KeepEnd</code> characters.</td></tr>
          <tr><td><code>Email</code></td><td>Masks the local part and the domain, keeps the shape.</td></tr>
          <tr><td><code>Phone</code></td><td>Keeps the last group of digits.</td></tr>
          <tr><td><code>Regex</code></td><td><code>Pattern</code> and <code>Replacement</code>.</td></tr>
          <tr><td><code>Fixed</code></td><td>A constant string from <code>Text</code>.</td></tr>
          <tr><td><code>Hash</code></td><td>HMAC-SHA256 keyed by <code>options.HashSalt</code>. A query is refused without one, and a salt under 16 characters is refused where it is written.</td></tr>
          <tr><td><code>Null</code></td><td>Removes the value. Refused at startup on a non-nullable value type.</td></tr>
          <tr><td><code>Tokenize</code></td><td>A random token from <code>options.TokenVault</code>. A query is refused without one.</td></tr>
        </tbody>
      </table>
      <Code lang="csharp">{`[DwMask(MaskStrategy.Partial, KeepEnd = 4)]
public string CardNumber { get; set; }        // ************4242

[DwMask(MaskStrategy.Email)]
public string Email { get; set; }             // s*************@c******.com

[DwMask(MaskStrategy.Hash)]
public string NationalId { get; set; }        // stable per salt, useful for joins

[DwMask(MaskStrategy.Tokenize)]
public string PassportNumber { get; set; }    // stable per vault, useful for joins`}</Code>

      <h2 id="hash-vs-token">Hashing against tokenizing</h2>
      <p>
        Both keep a column groupable and joinable while hiding what is in it, and
        both do it by mapping one value to one output. The difference is where the
        secret lives, and it decides what an attacker has to reach to undo the mask.
      </p>
      <table>
        <thead><tr><th></th><th><code>Hash</code></th><th><code>Tokenize</code></th></tr></thead>
        <tbody>
          <tr><td>Output</td><td>64 hex characters</td><td>32 hex characters</td></tr>
          <tr><td>Derived from the value</td><td>yes</td><td>no</td></tr>
          <tr><td>Reversed by</td><td>holding the salt</td><td>reading the vault, and its key where it has one</td></tr>
          <tr><td>A weak secret</td><td>brute-forced offline</td><td>only if you give the vault a short key, which is refused</td></tr>
          <tr><td>Survives a restart</td><td>always</td><td>only with a durable vault</td></tr>
          <tr><td>Discloses equality</td><td>yes</td><td>yes</td></tr>
        </tbody>
      </table>
      <p>
        A hash is computed, so whoever holds the salt can recompute every digest the
        deployment has ever emitted, and a guessable salt is recovered offline. A
        token is drawn at random the first time a value is seen and written into a
        vault, so the only way back is to read that vault — a store you can lock,
        move and revoke separately from the data. Guard it as you would guard the
        column it protects, and <a href="#vault-key">give it a key</a>.
      </p>
      <Code lang="csharp">{`new DwPolicyOptions
{
    HashSalt   = secret,                      // 16 characters or more
    TokenVault = new RedisTokenVault(redis)   // or EfTokenVault, or InMemoryTokenVault
}`}</Code>
      <p>
        Three vaults ship and all three pass one conformance suite.{" "}
        <code>InMemoryTokenVault</code> lives and dies with the process, which is
        right for a test and wrong for any column compared across restarts.{" "}
        <code>RedisTokenVault</code> and <code>EfTokenVault</code> keep the mapping
        outside the process and cache every mapping they resolve, which they can do
        safely because a token is written once and never rewritten.
      </p>

      <h3 id="vault-key">Give a durable vault a key (3.3.0)</h3>
      <p>
        A vault stores its mapping under the scope and a digest of the value.
        Without a key that digest is a plain SHA-256, and a tokenized column is
        nearly always drawn from a space small enough to hash whole — phone
        numbers, national identifiers, card numbers. So a copy of the store, a
        backup or a replica or a dump, gives back every value in it, and with them
        the value behind every token ever issued. Under a key held where the store
        is not — configuration, a secret manager — the digest is an HMAC-SHA256,
        and the store and the key have to be taken together.
      </p>
      <Code lang="csharp">{`new DwPolicyOptions
{
    TokenVault = new RedisTokenVault(redis, key)              // 16 bytes or more
    // TokenVault = new EfTokenVault(() => new AppDbContext(opts), key)
}`}</Code>
      <p>
        The constructors that take no key are unchanged and unkeyed.{" "}
        <code>InMemoryTokenVault</code> draws a random 32-byte key of its own per
        instance — nothing to configure, and no API change — because its mappings
        die with the process anyway. The key must be at least{" "}
        <code>DwToken.MinimumKeyLength</code> (16) bytes; a shorter one is refused
        by the constructor, as a short hash salt is refused. A keyed mapping&apos;s key starts
        with <code>DwToken.KeyedPrefix</code> (<code>&quot;hmac:&quot;</code>), so
        an operator can tell the two kinds apart in a store holding both. The
        scope sits inside the digest as well as in front of it, so one value
        tokenized in two scopes is two unrelated keys.
      </p>
      <Callout tone="warn" title="Adding a key keeps every token already issued — roll it out in two steps">
        A keyed vault meeting a value with no keyed mapping looks up the unkeyed
        mapping too, and the token found there is the one written under the keyed
        key, so yesterday&apos;s export still lines up with today&apos;s. The
        unkeyed mapping stays until <code>retireUnkeyed: true</code>, and a
        retiring vault deletes it the first time it meets the value, whether it
        wrote the keyed mapping or found it. <strong>Give every instance the key
        first, and turn <code>retireUnkeyed</code> on only after that:</strong> an
        instance still running without the key mints a <em>new</em> token for a
        value whose unkeyed mapping is gone, and a value first met while keyed and
        unkeyed instances run side by side can end up with two tokens. Unkeyed
        mappings for values never met again stay until an operator removes them —{" "}
        <code>HSCAN</code> the Redis token hash and delete the fields that do not
        match <code>hmac:*</code>, or delete the rows of{" "}
        <code>DwPolicyTokens</code> whose <code>Key</code> does not start with{" "}
        <code>hmac:</code> — knowing such a value gets a new token the next time it
        is met. Changing the key re-issues every token, unless unkeyed mappings
        remain to adopt from.
      </Callout>
      <p>
        The cost is small and there is no schema change. Redis reads both fields in
        one round trip, so a value new to the store costs two round trips instead
        of one; EF Core costs one more read for a new value, and in retire mode one
        more read per first-met value. A keyed key is at most 326 characters
        against the 512 the <code>Key</code> column already holds.
      </p>
      <p>
        Tokens are namespaced by <code>TokenScope</code>, and where none is given
        the namespace falls back to the field&apos;s path relative to the entity
        being queried — with no type name in it. Two columns reached by different
        paths therefore get different tokens for the same value, and two entities
        whose member path is spelled identically share them, whether or not
        anybody decided they should. Name the scope explicitly on both sides when
        you want the match — at the cost of telling a caller the two rows concern
        the same subject — and name a distinct one on each when you do not.
      </p>
      <Callout tone="warn" title="Neither one hides equality">
        The same value maps to the same output under both, which is what makes the
        column usable and is also a disclosure no setting removes. Anyone who can
        write a chosen value and read the column back masked learns that
        value&apos;s stand-in and can recognise it in every other row. A field that
        cannot accept that wants <code>Fixed</code>, <code>Null</code>, or a denial.
      </Callout>

      <h2 id="others">The other five</h2>
      <table>
        <thead><tr><th>Attribute</th><th>What it does</th></tr></thead>
        <tbody>
          <tr><td><code>[DwGeneralize(mode)]</code></td><td><code>Round</code> (to <code>Step</code>), <code>Bucket</code> (a band label), <code>DatePart</code> (<code>Year</code>, <code>Quarter</code>, <code>Month</code>, <code>Day</code>), <code>Truncate</code> (drop decimals).</td></tr>
          <tr><td><code>[DwMutate(typeof(T))]</code></td><td>Your own <code>IValueTransformer</code>. Resolved from <code>options.Services</code>; the type is checked by the startup scan rather than on the first query.</td></tr>
          <tr><td><code>[DwDefault]</code></td><td>The type default, or a constant that must be readable as the member type.</td></tr>
          <tr><td><code>[DwTruncate(n)]</code></td><td>Shorten text, with an optional <code>Ellipsis</code>.</td></tr>
          <tr><td><code>[DwFormat("fmt")]</code></td><td>A standard or custom .NET format string.</td></tr>
        </tbody>
      </table>

      <Callout tone="note" title="Generalization is the half masking cannot reach">
        Arithmetic runs in <code>decimal</code> and converts back to the member
        own type, so rounding an <code>int</code> yields an <code>int</code>.{" "}
        <code>Bucket</code> is the one mode that emits text, because a band is a
        label rather than a number — so it is valid only on a string member, and
        the startup scan enforces that.
      </Callout>

      <h2 id="types">What can be applied to what</h2>
      <p>
        Masking, truncation and formatting all emit text, so they cannot be
        assigned to a <code>decimal</code> or a <code>DateTime</code>. Startup
        validation reports this as an error and names the fix.
      </p>
      <Code lang="text">{`Employee.Salary: [DwMask] emits text, which cannot be assigned to Decimal.
Use [DwGeneralize] to reduce a number or a date while keeping its type, or
project into a type whose member is a string.`}</Code>

      <h2 id="chain">Chaining</h2>
      <p>
        Several transforms on one member compose into a chain, elected one stage
        at a time. A runtime rule can add a stage on top of a sealed one and
        cannot replace it.
      </p>
      <Code lang="csharp">{`[DwGeneralize(GeneralizeMode.Bucket, Step = 5000)]   // 45000-49999
[DwTruncate(5, Ellipsis = "+")]                      // 45000+
public string SalaryBand { get; set; } = string.Empty;`}</Code>
      <p>
        The member is a <code>string</code> because both stages emit text, and
        startup validation refuses a chain that emits text into a member that
        cannot hold it. Reduce a number while keeping its type with{" "}
        <code>[DwGeneralize]</code> alone.
      </p>
      <Callout tone="warn" title="Order decides whether a stage does anything">
        Stages run in a fixed order — <code>[DwDefault]</code>,{" "}
        <code>[DwMutate]</code>, <code>[DwGeneralize]</code>,{" "}
        <code>[DwFormat]</code>, <code>[DwMask]</code>,{" "}
        <code>[DwTruncate]</code> — and not in the order you wrote them.{" "}
        <code>[DwFormat]</code> applies its format string only to an{" "}
        <code>IFormattable</code>, and <code>Round</code> converts back to the
        value&apos;s own runtime type. So{" "}
        <code>[DwGeneralize(Round)]</code> with <code>[DwFormat(&quot;C0&quot;)]</code>{" "}
        on a string member rounds a string back into a string and leaves the
        format nothing to act on: it is dropped in silence, with no error at
        startup and none at query time.
      </Callout>

      <h2 id="graph">Through the graph</h2>
      <p>
        The walk descends through reference navigations, collections, arrays,
        interfaces, structs and jagged collections, transforming every element it
        reaches. It is driven by the paths the policy names — the declared
        types, four segments deep — which is what makes it incapable of missing
        a path because it failed to recognise a navigation.
      </p>
      <Callout tone="danger" title="Fixed (security) in 3.4.0: a transform on a struct's member is applied">
        A struct is read as a copy, and the setter the walk used unboxed a
        second copy to write into, so a mask, generalization, format,
        truncation, default or mutation declared on a member of an
        application&apos;s own struct — <code>Hash</code> and{" "}
        <code>Tokenize</code> included — landed on a temporary and the stored
        value was emitted: in both tiers, from rows in memory and from EF Core,
        typed and dynamic, wherever the struct came back in a whole row or was
        selected whole, since the policy layer shipped. A class member was
        never affected. The setter writes into the box in place now, and both
        passes of the walk write each changed struct back where it was read
        from, innermost first: into the member that held it, into its position
        in a list or an array, or into the outer struct that held it. A
        member the policy names is transformed once, by the pass along its
        path.
      </Callout>
      <Callout tone="warn" title="A struct that cannot be written back fails the query">
        A struct a transform changed that cannot be written back where it was
        read from fails the query with <code>InvalidOperationException</code>{" "}
        rather than hand back the stored value: a struct held by a member with
        no setter, structs in a collection that cannot be written by position
        such as a <code>HashSet&lt;T&gt;</code>, and a struct that is a
        dictionary&apos;s value. Give the member a setter, and hold such structs
        in a list or an array.
      </Callout>
      <Callout tone="note" title="A member of a nullable struct, named through Value (3.4.0)">
        A selection naming a member inside a nullable struct through{" "}
        <code>Value</code>, <code>Pair.Value.Code</code>, is transformed: on the
        typed terminal, on the dynamic one — whose generated row holds the
        member under <code>Value</code>, and the walk steps through it — as a
        group key, and past the walk&apos;s depth, and the group floor a masked
        member declares applies to an aggregate over it. Until 3.4.0 the path
        was looked up as the query spells it, matched no transform, and the
        value came back as stored. See{" "}
        <Link href="/docs/policies/attributes#access">Access control</Link>.
      </Callout>

      <h2 id="unnamed">A value no path of the policy names</h2>
      <p>
        A value can sit in the materialized rows where none of those paths goes.
        A <code>[DwMask]</code> member five segments down an included or
        in-memory graph, one only a subtype of the row&apos;s type declares —{" "}
        <code>Dog.Chip</code> on rows typed <code>Animal</code>, in memory or in
        a TPH hierarchy — one on an object a dictionary holds, and the far side
        of a cycle: each came back exactly as stored, at the default caps, under{" "}
        <code>Strict</code>, with no <code>Selects</code>, with the navigation
        named whole in <code>Selects</code>, and in a dynamic projection holding
        a real object.
      </p>
      <Callout tone="danger" title="Fixed (security) in 3.3.0: the rows are walked by run-time type as well">
        A member that declares a transform attribute and was not transformed
        along a named path is transformed by its own attributes, exactly once —
        an object reached both ways is not transformed twice. No option had to
        be set and no cap raised for the old behaviour: a column masked four
        segments down was returned in the clear five segments down.
      </Callout>
      <ul>
        <li>
          Only members that declare a transform or an audit for{" "}
          <code>Select</code>, or that can lead to one, are read. A navigation
          whose type can reach neither is never touched, so a lazy loader behind
          it is not woken, and a model that declares neither anywhere pays for no
          second pass at all.
        </li>
        <li>
          The same pass reports each audited member it meets where the policy
          names no path to it, which the terminal records as a read — see{" "}
          <Link href="/docs/breaking-changes#unnamed-audit">breaking point 43</Link>.
        </li>
        <li>
          The transform is the member&apos;s own attributes. No rule can speak to
          such a member, since no path names it — the same answer as{" "}
          <em>no runtime rule can unmask a field</em> — and a resolver built over
          no <code>AttributePolicyProvider</code> reads no attribute here either.
        </li>
        <li>
          It obeys <code>Selects</code> as the first pass does, runs in a dry run
          as transforms always have, and fails the query with{" "}
          <code>InvalidOperationException</code> for a transformed member with no
          setter, exactly as one along a named path does.
        </li>
        <li>
          The trace records the path with its stages and the note{" "}
          <code>(declared on the member; no path of the policy names it)</code>.
        </li>
        <li>
          Still a limit: a member typed <code>object</code>, or a collection that
          is not generic, says nothing about what it holds and is not read into.
        </li>
      </ul>
      <p>
        A transform is applied to a <em>member</em>, which is why a path{" "}
        <em>beneath</em> one — <code>Bonus.Value</code>, a decimal where{" "}
        <code>Bonus</code> is what is rounded — is refused for{" "}
        <code>Select</code>, <code>Group</code> and <code>Aggregate</code>{" "}
        instead: there is no member there to apply the chain to, and the value it
        would hand back is the stored one. So, since 3.4.0, is a part of a
        struct member that is itself transformed — <code>Price.Amount</code>{" "}
        with <code>[DwMask]</code> on <code>Price</code>; a part that declares
        its own transform is transformed as any member is. A transformed member past four
        segments, which only a raised{" "}
        <Link href="/docs/policies/configuration#caps"><code>MaxNavigationDepth</code></Link>{" "}
        lets a request name, is a member, so naming it in <code>Selects</code>{" "}
        returns it transformed; only a grouping key and an aggregated field are
        refused there, because a summary&apos;s own transform finds a generated
        row&apos;s columns by the type&apos;s list, and that list stops at four
        segments.
      </p>
    </DocPage>
  );
}
