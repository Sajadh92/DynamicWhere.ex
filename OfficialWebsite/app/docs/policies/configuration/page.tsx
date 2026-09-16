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
          <tr><td><code>DefaultPageSize</code></td><td><strong>0</strong></td><td>Page given to a guarded query that asked for none. Zero, the default, leaves it unpaged.</td></tr>
          <tr><td><code>MaxConditions</code></td><td>50</td><td>Conditions in one filter.</td></tr>
          <tr><td><code>MaxConditionDepth</code></td><td>10</td><td>How deep a filter may nest its condition groups, counting the root group as one.</td></tr>
          <tr><td><code>MaxOrderFields</code></td><td>10</td><td>Order fields in one query.</td></tr>
          <tr><td><code>MaxNavigationDepth</code></td><td>4</td><td>How deep a field path may reach.</td></tr>
          <tr><td><code>MaxQueryCost</code></td><td>1000</td><td>Budget consumed by <code>[DwCost]</code> weights.</td></tr>
          <tr><td><code>DefaultFieldCost</code></td><td>1</td><td>Charged for an unweighted field.</td></tr>
          <tr><td><code>MaxAuditEvents</code></td><td>10000</td><td>Audit buffer before draining.</td></tr>
          <tr><td><code>SchemaDepth</code></td><td>2</td><td>Levels a schema request walks when it names no depth.</td></tr>
          <tr><td><code>SchemaCycleLimit</code></td><td>2</td><td>Times one type may appear on one path.</td></tr>
          <tr><td><code>MaxSchemaFields</code></td><td>2000</td><td>Fields one schema response may carry before it truncates.</td></tr>
          <tr><td><code>MinGroupSize</code></td><td><strong>5</strong></td><td>k-anonymity group floor. Set 1 to switch it off. See <Link href="/docs/policies/security">Security</Link>.</td></tr>
        </tbody>
      </table>
      <p>
        Every cap is frozen at startup and reports through the trace. Most refuse
        a value below one. Two accept <code>0</code> and refuse only a negative
        value: <code>DefaultFieldCost</code>, which is the posture for a model
        that weighs its few expensive fields and wants the rest free, and{" "}
        <code>DefaultPageSize</code>, where zero is how the page-filling stays
        switched off.
      </p>
      <p>
        They do not all refuse alike, and the error code says which fired.{" "}
        <code>MaxPageSize</code>, <code>MaxConditions</code>,{" "}
        <code>MaxConditionDepth</code>, <code>MaxOrderFields</code>,{" "}
        <code>MaxNavigationDepth</code> and <code>MaxAuditEvents</code> share{" "}
        <code>CapExceeded</code>, naming the cap in <code>SourceOrigin</code>.{" "}
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
        nothing in the response to say so.
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
          <tr><td><code>FieldDeniedForWhere</code> … <code>FieldDeniedForSegment</code> (1–6)</td><td>A field is refused for that feature. <code>Where</code>, <code>Group</code>, <code>Aggregate</code> and <code>Segment</code> throw in <strong>both</strong> tiers, because dropping one of those would widen the result set or answer a different question. Only <code>Order</code> and <code>Select</code> are tier-dependent: the <code>Convenience</code> tier drops the clause instead.</td></tr>
          <tr><td><code>AllSelectsDenied</code> (7)</td><td>Every requested field was denied.</td></tr>
          <tr><td><code>OperatorNotAllowed</code> (8)</td><td>An operator outside the permitted set.</td></tr>
          <tr><td><code>CapExceeded</code> (9)</td><td>A cap above was exceeded.</td></tr>
          <tr><td><code>PolicyRequired</code> (10)</td><td>An unguarded query on a <code>RequirePolicy</code> type.</td></tr>
          <tr><td><code>RequiredFilterMissing</code> (11)</td><td>A <code>[DwRequireWhere]</code> field was not filtered on.</td></tr>
          <tr><td><code>MissingContextValue</code> (12)</td><td>A forced predicate needed a context value that was absent.</td></tr>
          <tr><td><code>AmbiguousFieldName</code> (13)</td><td>A name could mean more than one field.</td></tr>
          <tr><td><code>QueryStringDenied</code> (14)</td><td><code>getQueryString</code> in the <code>Strict</code> tier.</td></tr>
          <tr><td><code>AmbiguousGroupKey</code> (15)</td><td>Two groups of a summary share a key once their key values were transformed, so their aggregates cannot be added together without inventing a figure.</td></tr>
          <tr><td><code>TransformRequiresMaterialization</code> (16)</td><td>A transform on a query the caller materializes itself.</td></tr>
          <tr><td><code>StoreUnavailable</code> (17)</td><td>The store failed under <code>FailClosed</code>, or the context&apos;s pinned snapshot is older than <code>MaxSnapshotAge</code>.</td></tr>
          <tr><td><code>PolicyContextNotPrepared</code> (18)</td><td><code>ApplyPolicy</code> was handed a context that never went through <code>PrepareAsync</code> — refused whether or not a store is configured — or a store saw one it had attached nothing to, or the caller gained a <code>User</code> subject after preparation.</td></tr>
          <tr><td><code>QueryCostExceeded</code> (19)</td><td>The query cost budget was exceeded.</td></tr>
          <tr><td><code>GroupTooSmall</code> (20)</td><td>A summary already uses the alias the group floor reserves.</td></tr>
          <tr><td><code>MissingHashSalt</code> (21)</td><td>A field masks to a hash and no salt was configured.</td></tr>
          <tr><td><code>MissingTokenVault</code> (22)</td><td>A field masks to a token and no vault was configured.</td></tr>
        </tbody>
      </table>
    </DocPage>
  );
}
