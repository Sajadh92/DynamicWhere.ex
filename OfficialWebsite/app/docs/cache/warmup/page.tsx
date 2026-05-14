import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Cache Warmup",
  description:
    "Pre-populate the DynamicWhere.ex reflection cache at startup with both the generic and non-generic WarmupCache APIs.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/cache/warmup/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/cache/warmup">
      <h1>Cache Warmup</h1>
      <p>
        Warmup pre-populates the reflection cache at application startup so
        that the first user request doesn't pay the per-property reflection
        cost. Without warmup, the cache fills lazily — every previously-unseen
        type or path triggers a single reflection pass before the cached
        result is available.
      </p>

      <h2 id="why">Why warm up?</h2>
      <ul>
        <li><strong>First-request latency</strong> — the very first dynamic query against a type performs reflection inline. For complex types or deep property paths, that adds tens of milliseconds.</li>
        <li><strong>Cold-start consistency</strong> — after a process restart or scale-out event, the new instance starts with an empty cache. Warmup makes every instance behave identically from request #1.</li>
        <li><strong>Predictable timings in load tests</strong> — eliminates the "first request is mysteriously slower" anomaly.</li>
      </ul>

      <h2 id="generic">Generic warmup</h2>
      <p>
        Use the generic overload when you know the entity type at compile
        time. Pass one or more property paths — dotted paths are supported.
      </p>
      <Code lang="csharp">{`// Generic warmup
CacheExpose.WarmupCache<Product>("Name", "Category.Name", "Price");
CacheExpose.WarmupCache<Order>("Customer.Name", "OrderItems.Product.Name");`}</Code>

      <h2 id="non-generic">Non-generic warmup</h2>
      <p>
        Use the non-generic overload when the type is only known at runtime —
        for example, when warming up types discovered via assembly scanning or
        configuration files.
      </p>
      <Code lang="csharp">{`// Non-generic warmup
CacheExpose.WarmupCache(typeof(Customer), "Name", "Email", "Address.City");`}</Code>

      <Callout tone="info">
        Warmup is idempotent. Calling it multiple times with the same arguments
        is safe — entries already in the cache are simply touched (which
        refreshes LRU timestamps and increments LFU counters).
      </Callout>

      <h2 id="when-to-warm">When to warm up</h2>
      <p>
        Warm up at process startup, after configuring the cache and before
        accepting requests. In ASP.NET Core, a hosted service or a one-shot
        call right before <code>app.Run()</code> works well:
      </p>
      <Code lang="csharp">{`var app = builder.Build();

CacheExpose.Configure(CacheOptions.ForHighFrequencyAccess());

// Warm the entity types this service queries the most
CacheExpose.WarmupCache<Customer>("Name", "Email", "Address.City");
CacheExpose.WarmupCache<Order>("OrderDate", "Customer.Name", "OrderItems.Product.Name");
CacheExpose.WarmupCache<Product>("Name", "Price", "Category.Name");

app.Run();`}</Code>

      <h2 id="what-gets-cached">What warmup populates</h2>
      <p>
        For each <code>(Type, path)</code> pair, warmup touches all three
        stores it might end up using:
      </p>
      <ul>
        <li><code>TypeProperties</code> — for every type along the path.</li>
        <li><code>PropertyPath</code> — the validated and normalized path string.</li>
        <li><code>CollectionElementType</code> — for any collection segment in the path (e.g. <code>OrderItems</code>).</li>
      </ul>

      <h2 id="related">Related</h2>
      <ul>
        <li><Link href="/docs/cache/stores">Cache stores →</Link></li>
        <li><Link href="/docs/cache/configuration">Configuration →</Link></li>
        <li><Link href="/docs/cache/monitoring">Verify warmup via statistics →</Link></li>
      </ul>
    </DocPage>
  );
}
