import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Cache & Optimization — Reflection cache, FIFO / LRU / LFU eviction",
  description:
    "Tune the DynamicWhere.ex reflection cache for high-throughput EF Core workloads. Thread-safe stores, FIFO / LRU / LFU eviction strategies, six tuned presets, monitoring hooks, and warmup APIs.",
  keywords: [
    "EF Core reflection cache",
    "DynamicWhere cache tuning",
    "LRU cache .NET",
    "EF Core query performance",
  ],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/cache/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/cache">
      <h1>Cache &amp; Optimization</h1>
      <p>
        DynamicWhere.ex caches every reflection lookup it performs — property
        metadata, validated property paths, collection element types — so that
        repeated queries never pay the reflection cost twice. The entire cache
        subsystem is <strong>thread-safe</strong>, fully configurable, and
        exposed through a single public class: <code>CacheExpose</code>.
      </p>

      <h2 id="motivation">Why a reflection cache?</h2>
      <p>
        Building dynamic LINQ expressions from JSON requires walking type
        metadata for every condition, sort, group, and projection. Reflection
        is expensive, but its results are <em>stable</em> for a given type and
        path. Caching turns the per-request reflection work into a one-time
        cost amortised across the lifetime of the process.
      </p>
      <ul>
        <li>First request for <code>Customer.Name</code> resolves the path via reflection and stores the result.</li>
        <li>Every subsequent request — across any thread — reads from a <code>ConcurrentDictionary</code>.</li>
        <li>When a store grows past its configured ceiling, an eviction algorithm trims it back down.</li>
      </ul>

      <h2 id="thread-safety">Thread-safety</h2>
      <p>
        Every cache store is a <code>ConcurrentDictionary</code> guarded by
        access-tracking metadata managed by <code>CacheDatabase</code>. Reads
        and writes from many threads are safe with no locking on the caller's
        side. Configuration changes via{" "}
        <Link href="/docs/cache/configuration"><code>CacheExpose.Configure(...)</code></Link>{" "}
        are eventually consistent — see the configuration page for details.
      </p>

      <h2 id="three-stores">The three cache stores</h2>
      <p>
        Reflection results are split across three independent stores so that
        eviction in one does not invalidate the others. Each store has its own
        size limit, hit counters, and eviction state.
      </p>
      <table>
        <thead>
          <tr>
            <th>Store</th>
            <th>Key</th>
            <th>Value</th>
            <th>Purpose</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>TypeProperties</code></td>
            <td><code>Type</code></td>
            <td><code>Dictionary&lt;string, PropertyInfo&gt;</code></td>
            <td>All public instance properties per type.</td>
          </tr>
          <tr>
            <td><code>PropertyPath</code></td>
            <td><code>(Type, string)</code></td>
            <td><code>string</code></td>
            <td>Validated &amp; normalized property paths.</td>
          </tr>
          <tr>
            <td><code>CollectionElementType</code></td>
            <td><code>Type</code></td>
            <td><code>Type?</code></td>
            <td>Element type for collection types.</td>
          </tr>
        </tbody>
      </table>
      <p>
        Each store is addressable by name through the{" "}
        <Link href="/docs/enums/cache-memory-type"><code>CacheMemoryType</code></Link>{" "}
        enum, which lets you target individual stores in the diagnostics API.
      </p>

      <h2 id="eviction">Three eviction strategies</h2>
      <p>
        When a store exceeds its <code>MaxCacheSize</code>, an eviction pass
        removes the least valuable entries. The selection algorithm is
        controlled by the{" "}
        <Link href="/docs/enums/cache-eviction-strategy"><code>CacheEvictionStrategy</code></Link>{" "}
        enum.
      </p>
      <table>
        <thead>
          <tr>
            <th>Strategy</th>
            <th>What gets evicted</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>FIFO</code></td>
            <td>Oldest entries first — order of insertion.</td>
          </tr>
          <tr>
            <td><code>LRU</code></td>
            <td>Least recently used — entries untouched the longest.</td>
          </tr>
          <tr>
            <td><code>LFU</code></td>
            <td>Least frequently used — entries with the lowest hit count.</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        The default configuration is <code>LRU</code> with{" "}
        <code>MaxCacheSize = 1000</code> per store. This works well for most
        applications. Tune it via{" "}
        <Link href="/docs/cache/presets">presets</Link> or a custom{" "}
        <Link href="/docs/cache/options"><code>CacheOptions</code></Link> object.
      </Callout>

      <h2 id="public-api">Quick example</h2>
      <Code lang="csharp">{`using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Optimization.Cache.Config;

// Pick a preset that matches your environment
CacheExpose.Configure(CacheOptions.ForHighMemoryEnvironment());

// Optionally warm the cache at startup
CacheExpose.WarmupCache<Customer>("Name", "Email", "Address.City");

// Inspect at runtime
CacheStatistics stats = CacheExpose.GetCacheStatistics();`}</Code>

      <h2 id="sub-pages">Sub-pages</h2>
      <ul>
        <li>
          <Link href="/docs/cache/architecture"><strong>Architecture →</strong></Link>{" "}
          the six internal components and the public surface.
        </li>
        <li>
          <Link href="/docs/cache/stores"><strong>Stores →</strong></Link>{" "}
          deep-dive on each cache store.
        </li>
        <li>
          <Link href="/docs/cache/options"><strong>CacheOptions →</strong></Link>{" "}
          every property, every default.
        </li>
        <li>
          <Link href="/docs/cache/configuration"><strong>Configuration →</strong></Link>{" "}
          the three configuration approaches.
        </li>
        <li>
          <Link href="/docs/cache/warmup"><strong>Warmup →</strong></Link>{" "}
          pre-populating the cache at startup.
        </li>
        <li>
          <Link href="/docs/cache/monitoring"><strong>Monitoring →</strong></Link>{" "}
          statistics, reports, health alerts, and cache management.
        </li>
        <li>
          <Link href="/docs/cache/presets"><strong>Presets →</strong></Link>{" "}
          the six built-in <code>CacheOptions</code> factories.
        </li>
      </ul>
    </DocPage>
  );
}
