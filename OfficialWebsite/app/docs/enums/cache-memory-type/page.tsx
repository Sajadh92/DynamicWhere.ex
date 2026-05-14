import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "CacheMemoryType",
  description:
    "CacheMemoryType enum — identifies one of the three internal reflection cache stores for monitoring and clearing operations.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/enums/cache-memory-type">
      <h1>CacheMemoryType</h1>
      <p>
        <code>CacheMemoryType</code> names the three internal cache stores
        DynamicWhere.ex maintains for reflection metadata. It's the handle you
        pass to <code>CacheExpose.ClearCache(...)</code> or{" "}
        <code>CacheExpose.IsCacheFull(...)</code> to operate on one store at a
        time. It lives in <code>DynamicWhere.ex.Optimization.Cache.Config</code>.
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
            <td><code>TypeProperties</code></td>
            <td>Cached property metadata per <code>Type</code>.</td>
          </tr>
          <tr>
            <td><code>PropertyPath</code></td>
            <td>Cached validated &amp; normalized property paths.</td>
          </tr>
          <tr>
            <td><code>CollectionElementType</code></td>
            <td>Cached collection element-type lookups.</td>
          </tr>
        </tbody>
      </table>

      <h2 id="three-stores">The three cache stores</h2>
      <p>
        Each enum value maps one-to-one to an internal{" "}
        <code>ConcurrentDictionary</code> store managed by{" "}
        <code>CacheDatabase</code>. The <code>CacheEvictionStrategy</code> you
        configure applies to <em>each</em> store independently.
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

      <Callout tone="info">
        All three stores share the same <code>MaxCacheSize</code> and{" "}
        <code>EvictionStrategy</code> from <code>CacheOptions</code>. To tune a
        store independently, you would need to manage it manually via{" "}
        <code>CacheExpose</code> — there is no per-store configuration.
      </Callout>

      <h2 id="csharp">C# usage</h2>

      <p>Clear one store:</p>
      <Code lang="csharp">{`using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Optimization.Cache.Config;

CacheExpose.ClearCache(CacheMemoryType.PropertyPath);`}</Code>

      <p>Check whether a specific store is full:</p>
      <Code lang="csharp">{`bool full = CacheExpose.IsCacheFull(CacheMemoryType.TypeProperties);`}</Code>

      <p>Clear all stores at once (does not require the enum):</p>
      <Code lang="csharp">{`CacheExpose.ClearAllCaches();`}</Code>

      <h2 id="monitoring">Monitoring</h2>
      <p>
        Per-store statistics surface through the reporting APIs — every report
        enumerates all three values of this enum so you can see hit/miss rates,
        memory footprint, and eviction counts independently.
      </p>
      <Code lang="csharp">{`var stats = CacheExpose.GetCacheStatistics();
var memory = CacheExpose.GetMemoryUsage();
string report = CacheExpose.GeneratePerformanceReport();`}</Code>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/cache/architecture">Cache architecture →</Link>{" "}
          overview of the six cache classes.
        </li>
        <li>
          <Link href="/docs/cache/stores">Cache stores →</Link> detailed
          per-store reference.
        </li>
        <li>
          <Link href="/docs/enums/cache-eviction-strategy">
            CacheEvictionStrategy →
          </Link>{" "}
          the algorithm applied to each store.
        </li>
        <li>
          <Link href="/docs/cache/monitoring">Cache monitoring →</Link>{" "}
          reporting APIs and dashboards.
        </li>
      </ul>
    </DocPage>
  );
}
