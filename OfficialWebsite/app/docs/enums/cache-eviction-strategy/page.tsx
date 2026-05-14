import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "CacheEvictionStrategy",
  description:
    "CacheEvictionStrategy enum — the eviction algorithm (FIFO / LRU / LFU) used by the internal reflection cache.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/enums/cache-eviction-strategy">
      <h1>CacheEvictionStrategy</h1>
      <p>
        <code>CacheEvictionStrategy</code> selects the algorithm DynamicWhere.ex
        uses to evict entries from its internal reflection cache when a store
        reaches <code>MaxCacheSize</code>. It lives in{" "}
        <code>DynamicWhere.ex.Optimization.Cache.Config</code>.
      </p>

      <h2 id="values">Values</h2>
      <table>
        <thead>
          <tr>
            <th>Value</th>
            <th>Description</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>FIFO</code></td>
            <td>
              First-In-First-Out. Predictable, minimal overhead — no per-access
              bookkeeping.
            </td>
          </tr>
          <tr>
            <td><code>LRU</code></td>
            <td>
              Least Recently Used. Optimizes for temporal locality.{" "}
              <strong>(Default)</strong>
            </td>
          </tr>
          <tr>
            <td><code>LFU</code></td>
            <td>
              Least Frequently Used. Optimizes for access-frequency patterns.
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="tradeoffs">Trade-offs</h2>
      <table>
        <thead>
          <tr>
            <th>Strategy</th>
            <th>Best for</th>
            <th>Overhead</th>
            <th>Tracking</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>FIFO</code></td>
            <td>
              Testing, debugging, predictable behavior. Easy to reason about.
            </td>
            <td>Minimal — only insertion order.</td>
            <td>None per-access.</td>
          </tr>
          <tr>
            <td><code>LRU</code></td>
            <td>
              Recent-access-heavy workloads (admin dashboards, paginated
              browsing). Default for a reason — usually the right answer.
            </td>
            <td>Light — timestamp per access.</td>
            <td>
              <code>EnableLruTracking</code> auto-enabled.
            </td>
          </tr>
          <tr>
            <td><code>LFU</code></td>
            <td>
              Hot-set workloads where the same types/paths are hit repeatedly
              (high-frequency endpoints, type catalogs).
            </td>
            <td>Light — counter per access.</td>
            <td>
              <code>EnableLfuTracking</code> auto-enabled.
            </td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        Tracking flags are managed automatically based on the strategy you
        select. You normally don't need to touch <code>EnableLruTracking</code>{" "}
        or <code>EnableLfuTracking</code> directly.
      </Callout>

      <h2 id="csharp">C# usage</h2>

      <p>Builder pattern:</p>
      <Code lang="csharp">{`using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Optimization.Cache.Config;

CacheExpose.Configure(options =>
{
    options.MaxCacheSize = 2000;
    options.LeastUsedThreshold = 20;
    options.EvictionStrategy = CacheEvictionStrategy.LFU;
});`}</Code>

      <p>Direct object:</p>
      <Code lang="csharp">{`CacheExpose.Configure(new CacheOptions
{
    MaxCacheSize = 3000,
    LeastUsedThreshold = 15,
    MostUsedThreshold = 85,
    EvictionStrategy = CacheEvictionStrategy.LRU
});`}</Code>

      <h2 id="presets">Presets that use each strategy</h2>
      <table>
        <thead>
          <tr>
            <th>Preset</th>
            <th>Strategy</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>ForHighMemoryEnvironment()</code></td>
            <td><code>LRU</code></td>
          </tr>
          <tr>
            <td><code>ForLowMemoryEnvironment()</code></td>
            <td><code>LFU</code></td>
          </tr>
          <tr>
            <td><code>ForDevelopment()</code></td>
            <td><code>FIFO</code></td>
          </tr>
          <tr>
            <td><code>ForHighFrequencyAccess()</code></td>
            <td><code>LFU</code></td>
          </tr>
          <tr>
            <td><code>ForTemporalAccess()</code></td>
            <td><code>LRU</code></td>
          </tr>
        </tbody>
      </table>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/cache/options">CacheOptions →</Link> all tunable
          knobs and defaults.
        </li>
        <li>
          <Link href="/docs/cache/presets">Configuration presets →</Link> ready-made
          configurations for common workloads.
        </li>
        <li>
          <Link href="/docs/enums/cache-memory-type">CacheMemoryType →</Link>{" "}
          identifies the three stores the strategy applies to.
        </li>
        <li>
          <Link href="/docs/cache/architecture">Cache architecture →</Link>{" "}
          full system overview.
        </li>
      </ul>
    </DocPage>
  );
}
