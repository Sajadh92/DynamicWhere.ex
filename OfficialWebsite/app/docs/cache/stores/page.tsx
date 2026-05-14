import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Cache Stores",
  description:
    "The three internal cache stores — TypeProperties, PropertyPath, and CollectionElementType — with keys, values, and purposes.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/cache/stores">
      <h1>Cache Stores</h1>
      <p>
        The reflection cache is partitioned into three independent stores —
        each holding a different reflection artifact. Every store is a
        thread-safe <code>ConcurrentDictionary</code> with its own size cap and
        eviction timeline.
      </p>

      <h2 id="stores">The three stores</h2>
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
            <td><strong>TypeProperties</strong></td>
            <td><code>Type</code></td>
            <td><code>Dictionary&lt;string, PropertyInfo&gt;</code></td>
            <td>All public instance properties per type, keyed by property name for O(1) lookup.</td>
          </tr>
          <tr>
            <td><strong>PropertyPath</strong></td>
            <td><code>(Type, string)</code></td>
            <td><code>string</code></td>
            <td>Validated &amp; normalized dotted property paths — e.g. <code>"customer.address.city"</code> → <code>"Customer.Address.City"</code>.</td>
          </tr>
          <tr>
            <td><strong>CollectionElementType</strong></td>
            <td><code>Type</code></td>
            <td><code>Type?</code></td>
            <td>Element type for collection types — e.g. <code>List&lt;Order&gt;</code> → <code>Order</code>. <code>null</code> for non-collection types.</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        Each store is identifiable at runtime via the{" "}
        <Link href="/docs/enums/cache-memory-type"><code>CacheMemoryType</code></Link>{" "}
        enum: <code>TypeProperties</code>, <code>PropertyPath</code>, and{" "}
        <code>CollectionElementType</code>. Pass that value to{" "}
        <code>ClearCache</code> or <code>IsCacheFull</code> to target a single
        store.
      </Callout>

      <h2 id="targeting">Targeting a specific store</h2>
      <Code lang="csharp">{`using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Optimization.Cache.Config;

// Clear only the validated-path store, leave property metadata intact
CacheExpose.ClearCache(CacheMemoryType.PropertyPath);

// Check if a single store has hit its cap
bool typesFull       = CacheExpose.IsCacheFull(CacheMemoryType.TypeProperties);
bool pathsFull       = CacheExpose.IsCacheFull(CacheMemoryType.PropertyPath);
bool collectionsFull = CacheExpose.IsCacheFull(CacheMemoryType.CollectionElementType);`}</Code>

      <h2 id="sizing">Sizing</h2>
      <p>
        All three stores share the same <code>MaxCacheSize</code> from your{" "}
        <Link href="/docs/cache/options"><code>CacheOptions</code></Link>. With
        the default of <code>1000</code>, each store independently holds up to
        1000 entries — so the cache ceiling totals 3000 entries across the
        process. Increase or decrease this through a{" "}
        <Link href="/docs/cache/presets">preset</Link> or a custom options
        object.
      </p>

      <h2 id="lifetime">Entry lifetime</h2>
      <ul>
        <li>Entries are added on first reflection miss for a given key.</li>
        <li>Entries record access (timestamp / hit count) on every read when LRU / LFU tracking is enabled.</li>
        <li>Entries are removed only when an eviction pass runs (automatic on overflow, or manual via <code>ForceEvictionOnAllCaches</code> / <code>ClearCache</code> / <code>ClearAllCaches</code>).</li>
      </ul>

      <h2 id="related">Related</h2>
      <ul>
        <li><Link href="/docs/enums/cache-memory-type">CacheMemoryType enum →</Link></li>
        <li><Link href="/docs/cache/options">CacheOptions →</Link></li>
        <li><Link href="/docs/cache/monitoring">Monitoring &amp; diagnostics →</Link></li>
      </ul>
    </DocPage>
  );
}
