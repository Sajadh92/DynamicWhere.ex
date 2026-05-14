import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "CacheOptions",
  description:
    "Every property on CacheOptions — defaults, types, and what each one controls in the DynamicWhere.ex reflection cache.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/cache/options/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/cache/options">
      <h1><code>CacheOptions</code></h1>
      <p>
        <code>CacheOptions</code> is the single configuration object for the
        reflection cache. Every tunable lives here — size caps, eviction
        algorithm, tracking toggles, and the auto-validation safety net.
      </p>

      <h2 id="properties">Properties</h2>
      <table>
        <thead>
          <tr>
            <th>Property</th>
            <th>Type</th>
            <th>Default</th>
            <th>Description</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>MaxCacheSize</code></td>
            <td><code>int</code></td>
            <td><code>1000</code></td>
            <td>Maximum number of entries each store will hold before an eviction pass runs.</td>
          </tr>
          <tr>
            <td><code>LeastUsedThreshold</code></td>
            <td><code>int</code></td>
            <td><code>25</code></td>
            <td>Percentage of entries to remove on a single eviction pass — the "least valuable" slice.</td>
          </tr>
          <tr>
            <td><code>MostUsedThreshold</code></td>
            <td><code>int</code></td>
            <td><code>75</code></td>
            <td>Percentage of entries to keep — always equal to <code>100 − LeastUsedThreshold</code>.</td>
          </tr>
          <tr>
            <td><code>EvictionStrategy</code></td>
            <td>
              <Link href="/docs/enums/cache-eviction-strategy"><code>CacheEvictionStrategy</code></Link>
            </td>
            <td><code>LRU</code></td>
            <td>Algorithm used to pick the least-valuable entries: <code>FIFO</code>, <code>LRU</code>, or <code>LFU</code>.</td>
          </tr>
          <tr>
            <td><code>EnableLruTracking</code></td>
            <td><code>bool</code></td>
            <td><code>true</code></td>
            <td>Tracks per-entry access timestamps. Auto-managed based on <code>EvictionStrategy</code>.</td>
          </tr>
          <tr>
            <td><code>EnableLfuTracking</code></td>
            <td><code>bool</code></td>
            <td><code>false</code></td>
            <td>Tracks per-entry hit counters. Auto-managed based on <code>EvictionStrategy</code>.</td>
          </tr>
          <tr>
            <td><code>AutoValidateConfiguration</code></td>
            <td><code>bool</code></td>
            <td><code>true</code></td>
            <td>Auto-corrects mismatched settings — e.g. enables <code>EnableLfuTracking</code> when <code>EvictionStrategy = LFU</code>.</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        <code>LeastUsedThreshold</code> and <code>MostUsedThreshold</code>{" "}
        always sum to 100. Setting one updates the other when{" "}
        <code>AutoValidateConfiguration</code> is on (the default).
      </Callout>

      <h2 id="defaults-in-code">Defaults in code</h2>
      <p>
        The default options object is equivalent to <code>CacheOptions.Default</code>:
      </p>
      <Code lang="csharp">{`var defaults = new CacheOptions
{
    MaxCacheSize              = 1000,
    LeastUsedThreshold        = 25,
    MostUsedThreshold         = 75,
    EvictionStrategy          = CacheEvictionStrategy.LRU,
    EnableLruTracking         = true,
    EnableLfuTracking         = false,
    AutoValidateConfiguration = true,
};`}</Code>

      <h2 id="strategy-tracking">Strategy ↔ tracking matrix</h2>
      <p>
        When <code>AutoValidateConfiguration</code> is on, the two tracking
        flags are kept consistent with <code>EvictionStrategy</code> so that
        you never run an LFU eviction over zero hit counters.
      </p>
      <table>
        <thead>
          <tr>
            <th>EvictionStrategy</th>
            <th>EnableLruTracking</th>
            <th>EnableLfuTracking</th>
          </tr>
        </thead>
        <tbody>
          <tr><td><code>FIFO</code></td><td><code>false</code></td><td><code>false</code></td></tr>
          <tr><td><code>LRU</code></td><td><code>true</code></td><td><code>false</code></td></tr>
          <tr><td><code>LFU</code></td><td><code>false</code></td><td><code>true</code></td></tr>
        </tbody>
      </table>

      <h2 id="related">Related</h2>
      <ul>
        <li><Link href="/docs/cache/configuration">Configuration approaches →</Link></li>
        <li><Link href="/docs/cache/presets">Presets →</Link></li>
        <li><Link href="/docs/enums/cache-eviction-strategy">CacheEvictionStrategy →</Link></li>
        <li><Link href="/docs/enums/cache-memory-type">CacheMemoryType →</Link></li>
      </ul>
    </DocPage>
  );
}
