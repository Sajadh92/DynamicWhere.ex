import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Cache Architecture",
  description:
    "The six internal components of the DynamicWhere.ex reflection cache and which one is the public API.",
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
            <td>Thread-safe <code>ConcurrentDictionary</code> stores &amp; per-entry access tracking (timestamps for LRU, hit counts for LFU).</td>
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
            <td>Actual memory measurement — walks live cache entries to produce a real byte-level <code>CacheMemoryUsage</code> snapshot.</td>
          </tr>
          <tr>
            <td><code>CacheExpose</code></td>
            <td><strong>Public API</strong> — the only class consumers interact with. Every method you can call on the cache lives here.</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        <strong><code>CacheExpose</code> is the only public surface.</strong>{" "}
        The other five types live in internal namespaces and may change without
        notice. Treat <code>CacheExpose</code> as the stable contract.
      </Callout>

      <h2 id="namespaces">Namespaces</h2>
      <p>The public surface lives in two namespaces:</p>
      <Code lang="csharp">{`using DynamicWhere.ex.Optimization.Cache.Source;   // CacheExpose
using DynamicWhere.ex.Optimization.Cache.Config;   // CacheOptions, CacheEvictionStrategy, CacheMemoryType`}</Code>

      <h2 id="flow">Flow of a cached lookup</h2>
      <p>
        When a query asks for the resolved property path{" "}
        <code>"Customer.Address.City"</code>, the components cooperate as
        follows:
      </p>
      <ol>
        <li><code>CacheReflection</code> receives the lookup request.</li>
        <li>It asks <code>CacheDatabase</code> for the cached path — a hit returns immediately and records an access for LRU/LFU tracking.</li>
        <li>On miss, <code>CacheReflection</code> performs the real reflection, validates the path, normalises the casing, then writes the result back into <code>CacheDatabase</code>.</li>
        <li>If the store now exceeds <code>MaxCacheSize</code>, <code>CacheEviction</code> runs the configured algorithm to bring it back under threshold.</li>
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
