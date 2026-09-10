import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Policy Configuration — options, caps, tiers and defaults",
  description: "Every DynamicWhere.ex policy option and cap with its default: tiers, dry run, hash salt, store failure modes, query cost budget, MinGroupSize, plus startup validation and the twenty-two error codes.",
  keywords: ["DwPolicyOptions", "DwCaps", "MaxQueryCost", "MinGroupSize", "policy configuration"],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/policies/configuration/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/policies/configuration">
      <h1>Configuration</h1>
      <Code lang="csharp">{`DwPolicy.Configure(new DwPolicyOptions
{
    Tier            = DwTier.Convenience,
    DryRun          = false,
    HashSalt        = secret,              // 16 characters or more
    TokenVault      = tokenVault,          // needed only by MaskStrategy.Tokenize
    Services        = serviceProvider,     // resolves IValueTransformer
    StoreFailure    = StoreFailureMode.LastKnownGood,
    MaxSnapshotAge  = TimeSpan.FromMinutes(15),
    RefreshInterval = TimeSpan.FromSeconds(30),
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
            <td>Is <strong>dropped</strong> from the projection, sort or grouping</td>
            <td><code>getQueryString</code> allowed</td>
          </tr>
          <tr>
            <td><code>Strict</code></td>
            <td><strong>Throws</strong></td>
            <td><code>getQueryString</code> throws; deny-select implies deny-where inside a <code>Segment</code></td>
          </tr>
        </tbody>
      </table>
      <p>
        A dropped field leaves nothing behind in the data, so{" "}
        <code>FilterResult&lt;T&gt;.Policy</code> is the only way a caller can
        tell a policy drop from a null value.
      </p>

      <h2 id="caps">Caps</h2>
      <table>
        <thead><tr><th>Cap</th><th>Default</th><th>Meaning</th></tr></thead>
        <tbody>
          <tr><td><code>MaxPageSize</code></td><td>1000</td><td>Largest page a caller may request.</td></tr>
          <tr><td><code>MaxConditions</code></td><td>50</td><td>Conditions in one filter.</td></tr>
          <tr><td><code>MaxOrderFields</code></td><td>10</td><td>Order fields in one query.</td></tr>
          <tr><td><code>MaxNavigationDepth</code></td><td>4</td><td>How deep a field path may reach.</td></tr>
          <tr><td><code>MaxQueryCost</code></td><td>1000</td><td>Budget consumed by <code>[DwCost]</code> weights.</td></tr>
          <tr><td><code>DefaultFieldCost</code></td><td>1</td><td>Charged for an unweighted field.</td></tr>
          <tr><td><code>MaxAuditEvents</code></td><td>10000</td><td>Audit buffer before draining.</td></tr>
          <tr><td><code>MinGroupSize</code></td><td><strong>5</strong></td><td>k-anonymity group floor. Set 1 to switch it off. See <Link href="/docs/policies/security">Security</Link>.</td></tr>
        </tbody>
      </table>
      <p>
        Every cap is frozen at startup, refuses a value below one, and reports
        through the trace with its own error code.
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

      <h2 id="validate">Startup validation</h2>
      <Code lang="csharp">{`PolicyModelReport report = DwPolicy.ValidateModel(typeof(Employee), typeof(Customer));

foreach (var warning in report.Warnings) logger.LogWarning("{W}", warning);
if (report.Errors.Count > 0) throw new InvalidOperationException("Policy model invalid.");`}</Code>
      <p>
        Reported as a list rather than thrown one at a time, so a model is fixed
        in one pass instead of one exception per restart. Errors cover
        contradictions that cannot work — a text-emitting transform on a numeric
        member, a <code>[DwMutate]</code> type that is not an{" "}
        <code>IValueTransformer</code>, a default that cannot be read as the
        member type. Warnings cover things that work but probably should not,
        chiefly a transformed field that is still orderable.
      </p>

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
          <tr><td><code>FieldDeniedForWhere</code> … <code>FieldDeniedForSegment</code> (1–6)</td><td>A field is refused for that feature, in the <code>Strict</code> tier.</td></tr>
          <tr><td><code>AllSelectsDenied</code> (7)</td><td>Every requested field was denied.</td></tr>
          <tr><td><code>OperatorNotAllowed</code> (8)</td><td>An operator outside the permitted set.</td></tr>
          <tr><td><code>CapExceeded</code> (9)</td><td>A cap above was exceeded.</td></tr>
          <tr><td><code>PolicyRequired</code> (10)</td><td>An unguarded query on a <code>RequirePolicy</code> type.</td></tr>
          <tr><td><code>RequiredFilterMissing</code> (11)</td><td>A <code>[DwRequireWhere]</code> field was not filtered on.</td></tr>
          <tr><td><code>MissingContextValue</code> (12)</td><td>A forced predicate needed a context value that was absent.</td></tr>
          <tr><td><code>AmbiguousFieldName</code> (13)</td><td>A name could mean more than one field.</td></tr>
          <tr><td><code>QueryStringDenied</code> (14)</td><td><code>getQueryString</code> in the <code>Strict</code> tier.</td></tr>
          <tr><td><code>AmbiguousGroupKey</code> (15)</td><td>A group key could mean more than one field.</td></tr>
          <tr><td><code>TransformRequiresMaterialization</code> (16)</td><td>A transform on a query the caller materializes itself.</td></tr>
          <tr><td><code>StoreUnavailable</code> (17)</td><td>The store failed under <code>FailClosed</code>.</td></tr>
          <tr><td><code>PolicyContextNotPrepared</code> (18)</td><td><code>PrepareAsync</code> was never called.</td></tr>
          <tr><td><code>QueryCostExceeded</code> (19)</td><td>The query cost budget was exceeded.</td></tr>
          <tr><td><code>GroupTooSmall</code> (20)</td><td>A summary already uses the alias the group floor reserves.</td></tr>
          <tr><td><code>MissingHashSalt</code> (21)</td><td>A field masks to a hash and no salt was configured.</td></tr>
          <tr><td><code>MissingTokenVault</code> (22)</td><td>A field masks to a token and no vault was configured.</td></tr>
        </tbody>
      </table>
    </DocPage>
  );
}
