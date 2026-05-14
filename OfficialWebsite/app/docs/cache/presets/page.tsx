import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Cache Presets",
  description:
    "The six built-in CacheOptions presets — Default, ForHighMemoryEnvironment, ForLowMemoryEnvironment, ForDevelopment, ForHighFrequencyAccess, ForTemporalAccess.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/cache/presets/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/cache/presets">
      <h1>Cache Presets</h1>
      <p>
        <code>CacheOptions</code> ships with six factory presets that cover the
        common deployment shapes. Each one returns a fully-populated{" "}
        <code>CacheOptions</code> instance that you pass straight to{" "}
        <code>CacheExpose.Configure(...)</code>.
      </p>

      <h2 id="table">All six presets</h2>
      <table>
        <thead>
          <tr>
            <th>Preset</th>
            <th>MaxCacheSize</th>
            <th>Eviction</th>
            <th>LeastUsed%</th>
            <th>Use case</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><strong>Default</strong></td>
            <td>1000</td>
            <td><code>LRU</code></td>
            <td>25%</td>
            <td>General purpose — sane balance for any app.</td>
          </tr>
          <tr>
            <td><code>ForHighMemoryEnvironment()</code></td>
            <td>5000</td>
            <td><code>LRU</code></td>
            <td>10%</td>
            <td>Servers with ample RAM — large working set, gentle eviction.</td>
          </tr>
          <tr>
            <td><code>ForLowMemoryEnvironment()</code></td>
            <td>250</td>
            <td><code>LFU</code></td>
            <td>40%</td>
            <td>Constrained environments — small footprint, aggressive eviction, keep only hot entries.</td>
          </tr>
          <tr>
            <td><code>ForDevelopment()</code></td>
            <td>100</td>
            <td><code>FIFO</code></td>
            <td>50%</td>
            <td>Testing &amp; debugging — fast turnover for predictable behaviour.</td>
          </tr>
          <tr>
            <td><code>ForHighFrequencyAccess()</code></td>
            <td>2000</td>
            <td><code>LFU</code></td>
            <td>20%</td>
            <td>Repeated queries on the same types — biased toward retaining frequently-hit entries.</td>
          </tr>
          <tr>
            <td><code>ForTemporalAccess()</code></td>
            <td>1500</td>
            <td><code>LRU</code></td>
            <td>25%</td>
            <td>Recent-access-heavy workloads — favours entries used in the recent past.</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        Each preset returns a <em>new</em> <code>CacheOptions</code> instance,
        so you can mutate the result if you need a small adjustment on top of a
        preset baseline.
      </Callout>

      <h2 id="usage">Per-preset usage</h2>

      <h3 id="default">Default</h3>
      <Code lang="csharp">{`// Default — equivalent to "do nothing", but explicit
CacheExpose.Configure(CacheOptions.Default);`}</Code>

      <h3 id="high-memory">ForHighMemoryEnvironment</h3>
      <Code lang="csharp">{`// Big servers with plenty of RAM — keep a large working set
CacheExpose.Configure(CacheOptions.ForHighMemoryEnvironment());`}</Code>

      <h3 id="low-memory">ForLowMemoryEnvironment</h3>
      <Code lang="csharp">{`// Tight memory budget — small cap, aggressive eviction, LFU
CacheExpose.Configure(CacheOptions.ForLowMemoryEnvironment());`}</Code>

      <h3 id="development">ForDevelopment</h3>
      <Code lang="csharp">{`// Dev / test — small cap, FIFO eviction for predictable timings
CacheExpose.Configure(CacheOptions.ForDevelopment());`}</Code>

      <h3 id="high-frequency">ForHighFrequencyAccess</h3>
      <Code lang="csharp">{`// Hot-set workload — many requests against the same handful of types
CacheExpose.Configure(CacheOptions.ForHighFrequencyAccess());`}</Code>

      <h3 id="temporal">ForTemporalAccess</h3>
      <Code lang="csharp">{`// Recent-access workload — sliding-window of property paths
CacheExpose.Configure(CacheOptions.ForTemporalAccess());`}</Code>

      <h2 id="combine">Tweak a preset</h2>
      <p>
        Presets return mutable objects, so you can adjust one or two values
        without writing the whole <code>CacheOptions</code> by hand:
      </p>
      <Code lang="csharp">{`var options = CacheOptions.ForHighMemoryEnvironment();
options.MaxCacheSize = 8000; // bump the cap, keep everything else
CacheExpose.Configure(options);`}</Code>

      <h2 id="related">Related</h2>
      <ul>
        <li><Link href="/docs/cache/options">CacheOptions reference →</Link></li>
        <li><Link href="/docs/cache/configuration">All three configuration approaches →</Link></li>
        <li><Link href="/docs/enums/cache-eviction-strategy">CacheEvictionStrategy →</Link></li>
      </ul>
    </DocPage>
  );
}
