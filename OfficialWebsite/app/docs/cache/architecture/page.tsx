import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Cache Architecture",
  description:
    "The six classes behind the DynamicWhere.ex reflection cache and which one is the public API.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/cache/architecture/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/cache/architecture">
      <h1>Cache Architecture</h1>
      <p>
        The cache subsystem is split into six single-responsibility components.
        Only one of them — <code>CacheExpose</code> — is part of the public
        API; the other five are implementation details you can read about for
        context.
      </p>

      <h2 id="components">The six components</h2>
      <table>
        <thead>
          <tr>
            <th>Component</th>
            <th>Responsibility</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>CacheReflection</code></td>
            <td>Core reflection operations with caching — turns a type or path lookup into a cache hit (or a fresh reflection call on miss).</td>
          </tr>
          <tr>
            <td><code>CacheDatabase</code></td>
            <td>Thread-safe <code>ConcurrentDictionary</code> stores &amp; per-entry access tracking (timestamps for LRU, access counts for LFU).</td>
          </tr>
          <tr>
            <td><code>CacheEviction</code></td>
            <td>The FIFO, LRU, and LFU eviction algorithms. Runs when a store crosses <code>MaxCacheSize</code> or when forced.</td>
          </tr>
          <tr>
            <td><code>CacheReporting</code></td>
            <td>Renders statistics, memory usage, and performance reports — every report string and the monitoring dictionary come from here.</td>
          </tr>
          <tr>
            <td><code>CacheCalculator</code></td>
            <td>Memory estimation — walks live cache entries and sizes them from fixed constants to produce a <code>CacheMemoryUsage</code> snapshot. Nothing is measured from the GC.</td>
          </tr>
          <tr>
            <td><code>CacheExpose</code></td>
            <td><strong>Public API</strong> — the only class consumers interact with. Every method you can call on the cache lives here.</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        <strong><code>CacheExpose</code> is the only class you call.</strong>{" "}
        The other five are <code>internal</code> classes inside that same
        public namespace and may change without notice. The rest of the public
        surface — 16 types in all — is data you pass in or read back:{" "}
        <code>CacheOptions</code>, the two enums, and the result types.
      </Callout>

      <h2 id="namespaces">Namespaces</h2>
      <p>The public surface lives in six namespaces:</p>
      <Code lang="csharp">{`using DynamicWhere.ex.Optimization.Cache.Source;   // CacheExpose
using DynamicWhere.ex.Optimization.Cache.Config;   // CacheOptions
using DynamicWhere.ex.Optimization.Cache.Enums;    // CacheEvictionStrategy, CacheMemoryType
using DynamicWhere.ex.Optimization.Cache.DTOs;     // CacheStatistics, CacheConfiguration, CacheMemoryUsage, ...
using DynamicWhere.ex.Optimization.Cache.Input;    // HealthAlertsInput, CacheFullCheckInput, ...
using DynamicWhere.ex.Optimization.Cache.Output;   // CacheCounts, TrackingCounts, CacheDatabases`}</Code>

      <h2 id="flow">Flow of a cached lookup</h2>
      <p>
        When a query asks for the resolved property path{" "}
        <code>"Customer.Address.City"</code>, the components cooperate as
        follows:
      </p>
      <ol>
        <li><code>CacheReflection</code> receives the lookup request.</li>
        <li>It asks <code>CacheDatabase</code> for the cached path. A hit skips the next two steps.</li>
        <li>On a miss, <code>CacheEviction</code> runs first: if the store already holds more than <code>MaxCacheSize</code> entries, the configured algorithm trims it.</li>
        <li><code>CacheReflection</code> then performs the real reflection, validates the path, normalises the casing, and writes the result into <code>CacheDatabase</code>. The eviction pass runs before that write, so a store settles at <code>MaxCacheSize</code> + 1 entries. A path that fails validation throws here, and nothing is written.</li>
        <li>Only a path that has validated records an access under the active strategy — a timestamp for LRU, a counter for LFU, nothing for FIFO. A path that fails records nothing (3.1.0), so an invented name leaves no record behind. Under LRU the timestamp is refreshed once it is a second old rather than on every read (3.3.0).</li>
        <li>A lookup takes no lock and a hit allocates nothing (3.3.0). The configuration in force is read with one volatile read, where every lookup used to lock and copy it; <code>GetCacheConfigOptions()</code> still returns a copy, because an instance a caller edited would be the one in force.</li>
        <li><code>CacheReporting</code> and <code>CacheCalculator</code> are read-only consumers of <code>CacheDatabase</code> — they never mutate cache state.</li>
      </ol>

      <h2 id="related">Related</h2>
      <ul>
        <li><Link href="/docs/cache/stores">Cache stores →</Link></li>
        <li><Link href="/docs/cache/monitoring">Monitoring &amp; diagnostics →</Link></li>
        <li><Link href="/docs/enums/cache-eviction-strategy">CacheEvictionStrategy →</Link></li>
        <li><Link href="/docs/enums/cache-memory-type">CacheMemoryType →</Link></li>
      </ul>
    </DocPage>
  );
}
