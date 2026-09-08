import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Dynamic Policy Store — rules without a redeploy",
  description: "Supply DynamicWhere.ex policy rules at runtime: the rule shape, broad and narrow zones, snapshot staleness, three failure modes, and why a context must be prepared once per request.",
  keywords: ["dynamic authorization rules", "policy store", "runtime access control", "snapshot staleness"],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/policies/store/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/policies/store">
      <h1>Dynamic Policy Store</h1>
      <p>
        Attributes are the compile-time half. A store supplies the other half at
        runtime, so an operator can grant or revoke access without a redeploy —
        and can never grant what the source code seals.
      </p>

      <h2 id="rule">A rule</h2>
      <Code lang="csharp">{`var rule = new PolicyRule(
    subjectKind: DwSubjectKind.Role,
    subjectKey:  "Support",
    entityType:  typeof(Employee).FullName!,
    fieldPath:   "Position",          // or "*" for every field of the type
    features:    PolicyFeature.Select | PolicyFeature.Order,
    effect:      PolicyEffect.Deny,
    priority:    10,
    validFrom:   DateTimeOffset.UtcNow,
    validTo:     DateTimeOffset.UtcNow.AddDays(30));`}</Code>
      <p>
        A rule also carries a transform, an operator restriction, an alias, a
        forced predicate, a required-operator list and the discovery facts, plus
        audit columns. <code>ValidFrom</code> and <code>ValidTo</code> make a
        grant expire on its own — expiry that depends on someone remembering is
        expiry that does not happen.
      </p>
      <Callout tone="warn" title="Bad rules are refused at the boundary">
        Validation lives in the <code>PolicyRule</code> constructor itself, so every
        store and the admin API get the same refusals. A rule naming a sealed
        field is rejected on write, not ignored on read.
      </Callout>

      <h2 id="zones">Two zones</h2>
      <table>
        <thead><tr><th>Zone</th><th>Holds</th><th>Lifetime</th></tr></thead>
        <tbody>
          <tr><td><strong>Broad</strong></td><td>Global, tenant and role rules</td><td>Cached and shared across requests</td></tr>
          <tr><td><strong>Narrow</strong></td><td>Per-user rules</td><td>Loaded for the identities on one context</td></tr>
        </tbody>
      </table>
      <p>
        Each zone refuses the other rules at construction rather than filtering
        them out, so a store that returns user rules from the broad load fails
        loudly instead of quietly caching one user grants for everyone.
      </p>

      <h2 id="prepare">Prepare once per request</h2>
      <Code lang="csharp">{`var caller = await DwPolicy.PrepareAsync(
    new DwPolicyContext()
        .WithSubject(DwSubjectKind.User, userId)
        .WithSubject(DwSubjectKind.Tenant, tenantId));`}</Code>
      <Callout tone="danger" title="An unprepared context is refused, not tolerated">
        Any store provider that sees one throws{" "}
        <code>PolicyContextNotPrepared</code>. Falling back to attributes alone
        would look exactly like a working policy with the dynamic half missing,
        which is the worst possible failure for this feature.
      </Callout>
      <p>
        A context carries the snapshot it was served, and the staleness ceiling
        measures how old <em>that</em> snapshot is — not how fresh the provider
        is now. A context pinned longer than <code>MaxSnapshotAge</code> is
        refused even after the provider has refreshed, which is exactly why it is
        built once per request.
      </p>

      <h2 id="failure">Three failure modes</h2>
      <table>
        <thead><tr><th><code>options.StoreFailure</code></th><th>When the store is unreachable</th></tr></thead>
        <tbody>
          <tr><td><code>LastKnownGood</code> (default)</td><td>Serve the last snapshot that loaded, bounded by <code>MaxSnapshotAge</code>.</td></tr>
          <tr><td><code>FailClosed</code></td><td>Refuse the query with <code>StoreUnavailable</code>.</td></tr>
          <tr><td><code>StaticOnly</code></td><td>Fall back to attributes alone.</td></tr>
        </tbody>
      </table>
      <p>
        All three are bounded by the ceiling. A startup load failure always
        throws, whatever the mode: an application that has never loaded a policy
        has no last known good to serve.
      </p>

      <h2 id="refresh">Refresh</h2>
      <p>
        The provider polls on <code>options.RefreshInterval</code> and, where the
        store supports it, also watches for change notifications. Refresh happens
        on a background timer and never on the query path. A query that starts on
        version 41 finishes on version 41 — the swap is atomic.
      </p>
      <Code lang="csharp">{`foreach (var provider in DwPolicy.StoreProviders)
{
    Console.WriteLine(provider.Version);      // snapshot version
    Console.WriteLine(provider.Age);          // how old it is
    Console.WriteLine(provider.IsDegraded);   // serving last known good
    Console.WriteLine(provider.LastError);    // why
}`}</Code>

      <h2 id="stores">Choosing a store</h2>
      <p>
        <code>InMemoryPolicyStore</code> ships in the core package and is enough
        for a single process. For anything else see{" "}
        <Link href="/docs/policies/providers">Store providers</Link>. All three
        pass one shared conformance suite, so they behave alike or the build
        fails.
      </p>
    </DocPage>
  );
}
