import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Store Providers — Redis and Entity Framework Core",
  description: "The two DynamicWhere.ex policy store packages: Redis with pub/sub invalidation and a poll behind it, and Entity Framework Core on any provider, both held to one conformance suite.",
  keywords: ["Redis policy store", "EF Core policy store", "distributed authorization rules", "pub sub invalidation"],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/policies/providers/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/policies/providers">
      <h1>Store Providers</h1>
      <p>
        Two packages hold rules outside the process. Both implement the same
        contracts and both pass the same conformance suite as the in-memory
        store, so swapping one for the other changes nothing about behaviour.
      </p>

      <h2 id="redis">Redis</h2>
      <Code lang="bash">{`dotnet add package DynamicWhere.ex.Policies.Redis --version 3.0.0`}</Code>
      <Code lang="csharp">{`var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
var store = new RedisPolicyStore(redis);

var provider = await StorePolicyProvider.CreateAsync(store, options);

DwPolicy.Configure(options, provider);`}</Code>
      <Callout tone="note" title="The poll is not redundant">
        Redis pub/sub is fire-and-forget: a subscriber that is briefly
        disconnected never learns it missed a message. The poll behind the watch
        is what bounds a dropped notification, so it stays even though the watch
        usually wins the race.
      </Callout>

      <h2 id="ef">Entity Framework Core</h2>
      <Code lang="bash">{`dotnet add package DynamicWhere.ex.Policies.EntityFrameworkCore --version 3.0.0`}</Code>
      <Code lang="csharp">{`var policyDbOptions = new DbContextOptionsBuilder<DwPolicyDbContext>()
    .UseNpgsql(connection, sql => sql.MigrationsAssembly("YourProject"))
    .Options;

// A new context per read: the provider polls on a background timer, and a
// context shared with request threads would be used concurrently.
var store = new EfPolicyStore(() => new DwPolicyDbContext(policyDbOptions));

var provider = await StorePolicyProvider.CreateAsync(store, options);

DwPolicy.Configure(options, provider);`}</Code>

      <Callout tone="warn" title="MigrationsAssembly is not optional">
        <code>DwPolicyDbContext</code> is declared in the NuGet package, and EF
        Core looks for migrations in the assembly that declares the context. Left
        alone, <code>dotnet ef migrations add</code> refuses outright with
        &quot;your target project does not match your migrations assembly&quot;,
        and a running application finds no migrations to apply. Point it at your
        own project in <em>both</em> places you build the context — the runtime
        registration and any design-time factory.
      </Callout>

      <p>Two tables, on their own migration history:</p>
      <table>
        <thead><tr><th>Table</th><th>Holds</th></tr></thead>
        <tbody>
          <tr><td><code>DwPolicyRules</code></td><td>One row per rule, indexed by entity and field, and by subject.</td></tr>
          <tr><td><code>DwPolicyVersion</code></td><td>A single row carrying the snapshot version.</td></tr>
        </tbody>
      </table>

      <h2 id="serialization">One serializer, three stores</h2>
      <p>
        <code>PolicyPayload</code> is the only place a rule payload is read or
        written. Neither provider parses JSON of its own, because three parsers
        would become three standards. Every enumeration is written and read{" "}
        <strong>by name</strong>, in JSON and in a database column alike: the zero
        member of several enumerations is the permissive one, so an unparsed
        value must not read as a plausible-looking default.
      </p>

      <h2 id="readonly">Read-only deployments</h2>
      <p>
        Register only <code>IDwPolicyStore</code> and writes become impossible by
        construction rather than by convention.{" "}
        <code>IDwPolicyWritableStore</code> is a separate interface, so a replica
        that should never accept a rule simply does not implement it.
      </p>

      <h2 id="conformance">The conformance suite</h2>
      <p>
        One abstract suite runs against in-memory, a real Redis and a real
        PostgreSQL, covering load, version, watch and poll, the atomic swap,
        the zone split, startup failure, refresh failure, the staleness ceiling,
        the sealed-field refusal and the read-only store. Any future provider
        inherits all of it.
      </p>
      <Callout tone="note" title="It fails rather than skips">
        The suite needs a Docker daemon and refuses to run without one. A
        conformance leg that quietly does not run reads as three stores agreeing
        when only one of them was checked.
      </Callout>
    </DocPage>
  );
}
