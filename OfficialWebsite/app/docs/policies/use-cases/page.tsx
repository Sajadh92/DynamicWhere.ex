import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Policy Use Cases — real scenarios and the attribute combinations that solve them",
  description:
    "Worked field-level policy recipes for DynamicWhere.ex: tenant isolation, support agents, analysts, auditors, regulated identifiers and right-to-erasure — each showing which attributes combine, and exactly what leaks if you drop one.",
  keywords: [
    "field-level security examples",
    "EF Core data masking example",
    "multitenancy row filter",
    "k-anonymity example",
    "tokenization example",
    "policy attribute combinations",
  ],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/policies/use-cases/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/policies/use-cases">
      <h1>Use cases</h1>
      <p>
        The <Link href="/docs/policies/attributes">attribute reference</Link> lists
        the parts. This page shows the machine. Almost every real control here is a{" "}
        <strong>pair</strong> of attributes, and the pairing is the part you cannot
        infer from either half.
      </p>
      <p>
        Each recipe below states the requirement, gives the code, says what the
        caller actually receives, and then names what leaks if you drop one
        attribute. That last line is the point of the page.
      </p>

      <Callout tone="note" title="Every recipe assumes the guard is on">
        A policy only applies to a query that went through{" "}
        <code>ApplyPolicy(ctx)</code>. Add{" "}
        <code>[DwEntity(RequirePolicy = true)]</code> to the class so a
        DynamicWhere call that forgets throws instead of quietly returning
        everything. It guards this library&apos;s extension methods and nothing
        else: plain EF Core or LINQ against the <code>DbSet</code> still reads
        the table.
      </Callout>

      {/* ------------------------------------------------------------------ 1 */}
      <h2 id="tenant">1. A tenant must never see another tenant&apos;s rows</h2>
      <p>
        The row-level boundary. The caller does not ask for it and cannot widen
        it: a forced predicate is collected and ANDed rather than elected, so even
        a rule with higher authority can only narrow it further.
      </p>
      <Code lang="csharp">{`[DwEntity(RequirePolicy = true)]
public class Invoice
{
    [DwForceWhere(Operator.Equal, ContextValue = "TenantId")]
    public Guid TenantId { get; set; }

    // The soft-delete half of the same idea: a constant rather than a context value.
    [DwForceWhere(Operator.Equal, Value = "false")]
    public bool IsVoid { get; set; }
}`}</Code>
      <Code lang="csharp">{`var caller = await DwPolicy.PrepareAsync(
    new DwPolicyContext()
        .WithSubject(DwSubjectKind.Tenant, tenantId)
        .WithValue("TenantId", tenantId));   // the ContextValue above reads this`}</Code>
      <p>
        <strong>What the caller gets.</strong> Their own rows, whatever filter they
        sent. The injected predicate wraps their condition group rather than
        merging into it, so <code>(A OR B) AND TenantId = 5</code> — never{" "}
        <code>(A OR B OR TenantId = 5)</code>, which would return every tenant.
      </p>
      <Callout tone="danger" title="Drop the ContextValue and the query fails, deliberately">
        A context that does not supply <code>TenantId</code> raises{" "}
        <code>MissingContextValue</code> in both tiers. A tenant scope that
        silently fails to apply is worse than a failed request, so neither tier
        skips it. A dry run injects no forced predicate at all, so there the
        missing value is recorded in the trace and nothing is thrown.
      </Callout>
      <p>
        <strong>Rows that belong to no tenant.</strong> A role no institution owns
        has a null scope column, and <code>InstitutionId = 5</code> never matches
        null, so the scope hides that role from every caller. Two forced
        predicates on one member are joined by <code>And</code> and cannot say
        &quot;or null&quot;; <code>AllowNull</code> can:
      </p>
      <Code lang="csharp">{`public class Role
{
    [DwForceWhere(Operator.Equal, ContextValue = "TenantId", AllowNull = true)]
    public int? InstitutionId { get; set; }
}`}</Code>
      <p>
        <strong>What the caller gets.</strong> Their own institution&apos;s roles
        and the roles that belong to none. The widened term sits in a group of its
        own, so <code>(A OR B) AND (InstitutionId = 5 OR InstitutionId IS NULL)</code>{" "}
        still cannot reach institution 9. A context with no <code>TenantId</code>{" "}
        is still refused with <code>MissingContextValue</code>, and a{" "}
        <code>[DwRequireWhere]</code> on the same member is not satisfied by the
        widened term. See{" "}
        <Link href="/docs/policies/attributes#allow-null">the attribute</Link>.
      </p>

      {/* ------------------------------------------------------------------ 2 */}
      <h2 id="support">2. A support agent may find a customer but not read their card</h2>
      <p>
        The agent needs to confirm an identifier a caller reads out to them over
        the phone. They must not be able to sweep for one, and must not see the
        stored value at all.
      </p>
      <Code lang="csharp">{`[DwOperators(Allow = new[] { Operator.Equal, Operator.In })]
[DwMask(MaskStrategy.Partial, KeepEnd = 4)]
[DwNoOrder]
public string CardNumber { get; set; } = string.Empty;`}</Code>
      <p>
        <strong>What the caller gets.</strong> <code>************4242</code>, and a
        query for an exact number they already know still matches, because
        filtering runs against the stored value in SQL.
      </p>
      <p>
        <strong>Three attributes, three different jobs.</strong> Take any one away
        and a different hole opens:
      </p>
      <ul>
        <li>
          Without <code>[DwOperators]</code> the agent sends{" "}
          <code>StartsWith &quot;4242&quot;</code> and reads <code>TotalCount</code> to
          count matches — the cardinality channel, and the mask never comes into
          it.
        </li>
        <li>
          Without <code>[DwMask]</code> the number is simply returned.
        </li>
        <li>
          Without <code>[DwNoOrder]</code> the agent sorts by the column and pages
          through it. Sorting ranks the <em>real</em> values, so the order alone
          reveals magnitude, and combined with range filters it converges.
        </li>
      </ul>

      {/* ------------------------------------------------------------------ 3 */}
      <h2 id="analyst">3. An analyst needs salary bands, never salaries</h2>
      <p>
        The k-anonymity recipe, and the one control on this page a reader cannot
        guess from the API.
      </p>
      <Code lang="csharp">{`[DwGeneralize(GeneralizeMode.Round, Step = 5000,
              AllowAggregate = true, MinGroupSize = 5)]
[DwNoOrder]
public decimal Salary { get; set; }`}</Code>
      <p>
        <strong>What the caller gets.</strong> Salaries rounded to the nearest
        5,000, and grouped reports where every group holds at least five people.
        Groups below the floor are removed from the result and recorded in the
        trace; the query is not refused.
      </p>
      <Callout tone="warn" title="Neither half works alone">
        <p>
          <code>SUM</code>, <code>MAX</code> and <code>MIN</code> run in SQL
          against the stored value, <em>before</em> any transform applies. So{" "}
          <code>MAX(Salary)</code> over a department of one returns that
          person&apos;s exact pay, and the rounding never happened.
        </p>
        <p>
          That is why aggregation over a transformed field is denied by default.{" "}
          <code>AllowAggregate = true</code> opens it, and{" "}
          <code>MinGroupSize</code> is what makes the opening safe. Setting
          the first without the second turns a control into a hole.
        </p>
      </Callout>
      <p>
        The global floor is <code>DwCaps.MinGroupSize</code>, which defaults to 5.
        A per-field <code>MinGroupSize</code> raises it for that field; the
        effective floor is the largest in play. See{" "}
        <Link href="/docs/policies/security">Security</Link>.
      </p>

      {/* ------------------------------------------------------------------ 4 */}
      <h2 id="warehouse">4. A warehouse must join on an identifier it may never read</h2>
      <p>
        Two export pipelines, run separately, whose output has to line up on the
        same person. Nobody downstream may recover the identifier.
      </p>
      <Code lang="csharp">{`[DwMask(MaskStrategy.Hash)]
[DwNoOrder]
public string EmployeeCode { get; set; } = string.Empty;`}</Code>
      <Code lang="csharp">{`new DwPolicyOptions { HashSalt = config["Dw:HashSalt"] }   // 16 characters or more`}</Code>
      <p>
        <strong>Why a hash and not a token here.</strong> The two pipelines never
        talk to each other. Both hold the same salt, so both compute the same
        digest for the same person with no shared table, no lookup and no ordering
        dependency between the jobs. A vault cannot do that, because agreeing
        would mean sharing the vault.
      </p>
      <p>
        The trade you accept: whoever holds that salt holds every digest the
        deployment has ever emitted, and a card number or a national identifier
        has few enough possible values to recompute in seconds. Use a hash for
        identifiers that are already high-entropy, and read the next recipe for
        the ones that are not.
      </p>

      {/* ------------------------------------------------------------------ 5 */}
      <h2 id="regulated">5. A regulated identifier, and a subject who can ask to be erased</h2>
      <p>
        National identifiers, medical record numbers, card numbers. Low-entropy,
        long-lived, and covered by a right to erasure.
      </p>
      <Code lang="csharp">{`[DwMask(MaskStrategy.Tokenize)]
[DwNoOrder]
public string NationalId { get; set; } = string.Empty;`}</Code>
      <Code lang="csharp">{`new DwPolicyOptions { TokenVault = new EfTokenVault(() => new DwPolicyDbContext(opts)) }`}</Code>
      <p>
        <strong>Why a token and not a hash.</strong> Two reasons, and the second is
        the one that decides it.
      </p>
      <ul>
        <li>
          A token is drawn at random rather than computed, so there is no secret
          whose leak makes every past value recoverable offline.
        </li>
        <li>
          <strong>Erasure actually works.</strong> Delete the subject&apos;s row and
          a hash still sits in every export and backup, recomputable by anyone
          with the salt. Delete the vault mapping and the token becomes a
          meaningless random string permanently.
        </li>
      </ul>
      <p>
        The library ships no reverse lookup on purpose, so erasure is a direct
        operation against the store. The key is public so you can compute it:
      </p>
      <Code lang="csharp">{`string key = DwToken.KeyFor("patient-id", nationalId);

// EF Core: DELETE FROM DwPolicyTokens WHERE [Key] = @key
// Redis:   HDEL dw:policy:tokens <key>`}</Code>

      {/* ------------------------------------------------------------------ 6 */}
      <h2 id="join">6. One person, three entities, one token</h2>
      <p>
        A claim, a visit and an invoice all carry the same patient identifier, and
        a report has to join them without anyone reading it.
      </p>
      <Code lang="csharp">{`public class Claim
{
    [DwMask(MaskStrategy.Tokenize, TokenScope = "patient-id")]
    [DwNoOrder]
    public string PatientNationalId { get; set; } = string.Empty;
}

public class Visit
{
    [DwMask(MaskStrategy.Tokenize, TokenScope = "patient-id")]
    [DwNoOrder]
    public string PatientNationalId { get; set; } = string.Empty;
}`}</Code>
      <p>
        <strong>The scope is what makes the join a decision rather than a
        coincidence.</strong> Left off, the namespace falls back to the
        field&apos;s path relative to the entity being queried, with no type name
        in it — so these two members, spelled identically, already share tokens,
        and would stop sharing them the day somebody renamed one or moved it
        behind a navigation. Write the scope and the join survives both.
      </p>
      <p>
        It costs something either way, which is why it is worth writing down: a
        caller who can see both columns learns the two rows concern the same
        person. Two columns that should <em>not</em> give each other away need
        distinct scopes for exactly the same reason — the default will not
        separate them for you when the paths happen to match.
      </p>

      {/* ------------------------------------------------------------------ 7 */}
      <h2 id="roles">7. Most roles see a mask, one role sees the value</h2>
      <p>
        Attributes are sealed by default: no runtime rule can lift one. Opting a
        field into runtime lifting is a single flag, and it is the only way an
        operator can ever be granted more than the source code allows.
      </p>
      <Code lang="csharp">{`// Masked for everyone, unless a rule with higher precedence says otherwise.
[DwMask(MaskStrategy.Partial, KeepEnd = 4, Overridable = true)]
public string AccountNumber { get; set; } = string.Empty;

// Nothing at runtime can grant this. Not to an admin, not by a rule, not ever.
[DwDenied]
public string RecoveryKey { get; set; } = string.Empty;`}</Code>
      <p>
        A rule for the finance role now outranks that mask, and{" "}
        <code>POST /dw-policies/explain</code> shows which level decided, what it
        overrode and what tied with it. What such a rule cannot do is hand back
        the raw value: each transform stage is elected among the fragments that
        carry that stage, so a rule with no mask of its own leaves the
        attribute&apos;s mask the only candidate. A rule replaces the mask with
        its own, or decides <code>Select</code>; it never removes one.
      </p>
      <p>
        Showing one role the value itself is a transformer, not a rule — it is
        the stage that can read the caller:
      </p>
      <Code lang="csharp">{`public sealed class AccountNumberForFinance : IValueTransformer
{
    public object? Transform(object? value, DwTransformContext context)
    {
        if (context.Policy.Identities(DwSubjectKind.Role).Contains("finance"))
        {
            return value;
        }

        string raw = value as string ?? string.Empty;

        return raw.Length <= 4
            ? raw
            : new string('*', raw.Length - 4) + raw.Substring(raw.Length - 4);
    }
}

[DwMutate(typeof(AccountNumberForFinance))]
public string AccountNumber { get; set; } = string.Empty;`}</Code>
      <p>
        A rule targeting <code>RecoveryKey</code> is rejected at write time, when
        the store can resolve the entity type, and loses at resolution time in
        every case — <code>[DwDenied]</code> is sealed, so no dynamic level
        outranks it.
      </p>

      {/* ------------------------------------------------------------------ 8 */}
      <h2 id="vocabulary">8. The caller uses their own names for fields</h2>
      <p>
        An alias adds a spelling and never removes one, so decorating a field is
        not a breaking change to a filter already in production.
      </p>
      <Code lang="csharp">{`[DwAlias("customer_name")]
[DwDescribe(Label = "Customer", Group = "Identity", Order = 1)]
[DwAllowedValues("Active", "Suspended", "Closed")]
public string Name { get; set; } = string.Empty;`}</Code>
      <p>
        The caller may write either spelling going in, and the column comes back
        under the alias. <code>[DwDescribe]</code> and{" "}
        <code>[DwAllowedValues]</code> feed the{" "}
        <Link href="/docs/policies/admin">schema endpoint</Link>, so a filter UI
        can render a labelled dropdown rather than a free-text box without knowing
        the model.
      </p>

      {/* ------------------------------------------------------------------ 9 */}
      <h2 id="expensive">9. A field nobody should be able to query a thousand times</h2>
      <Code lang="csharp">{`[DwCost(10)]
[DwAudit(PolicyFeature.Select | PolicyFeature.Where)]
public string FullTextNotes { get; set; } = string.Empty;`}</Code>
      <p>
        Every <em>reference</em> is charged, not every distinct field, because
        charging per field would let a caller generate the same work by naming one
        field a thousand times. <code>[DwAudit]</code> records each use to your{" "}
        <code>IDwAuditSink</code>, and reaching{" "}
        <code>MaxAuditEvents</code> refuses the query rather than dropping the
        record — an audited field whose log quietly stopped being written is the
        outcome the attribute exists to prevent.
      </p>
      <p>
        <code>[DwAudit]</code> records the uses of the fields it decorates, so a
        probe aimed at other fields — denied ones, or names that do not exist —
        leaves nothing in that log. Turn on{" "}
        <Link href="/docs/policies/configuration#audit-refusals"><code>DwPolicyOptions.AuditRefusals</code></Link>{" "}
        and every refused guarded query is written to the same sink, carrying its{" "}
        <code>ErrorCode</code>.
      </p>

      {/* --------------------------------------------------------------- recap */}
      <h2 id="pairs">The pairs, in one table</h2>
      <p>
        If you remember nothing else from this page, remember that the left column
        alone is not a control.
      </p>
      <table>
        <thead>
          <tr>
            <th>This</th>
            <th>needs this</th>
            <th>or else</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>[DwMask]</code></td>
            <td><code>[DwNoOrder]</code></td>
            <td>sorting ranks the real values and paging reads them off</td>
          </tr>
          <tr>
            <td><code>AllowAggregate = true</code></td>
            <td>a floor left in place</td>
            <td>a group of one returns the exact value under any aggregate — which needs the global <code>DwCaps.MinGroupSize</code> (5 by default) to have been switched off (set to 1), with no per-field floor above 1 — the larger of the two applies</td>
          </tr>
          <tr>
            <td>a mask on a filterable field</td>
            <td><code>[DwOperators]</code></td>
            <td><code>TotalCount</code> counts matches without selecting anything</td>
          </tr>
          <tr>
            <td><code>MaskStrategy.Hash</code></td>
            <td><code>HashSalt</code>, 16+ chars</td>
            <td>the query is refused with <code>MissingHashSalt</code></td>
          </tr>
          <tr>
            <td><code>MaskStrategy.Tokenize</code></td>
            <td><code>TokenVault</code></td>
            <td>the query is refused with <code>MissingTokenVault</code></td>
          </tr>
          <tr>
            <td>a token joined across entities</td>
            <td>a shared <code>TokenScope</code></td>
            <td>the namespace falls back to each field&apos;s own path, so the join works only where the paths happen to match, and breaks on a rename</td>
          </tr>
          <tr>
            <td>any policy at all</td>
            <td><code>[DwEntity(RequirePolicy)]</code></td>
            <td>a path that forgets <code>ApplyPolicy</code> returns everything</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="danger" title="What no combination on this page closes">
        Both <code>Hash</code> and <code>Tokenize</code> map one value to one
        output, which is what keeps the column groupable and joinable. So anyone
        who can write a chosen value and read the column back learns that
        value&apos;s stand-in, and can recognise it in every other row. No setting
        removes this. If the caller has no reason to group or join on the column,
        use <code>Fixed</code>, <code>Null</code>, or deny it outright.
      </Callout>
    </DocPage>
  );
}
