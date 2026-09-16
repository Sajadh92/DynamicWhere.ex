import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Cache Monitoring & Diagnostics",
  description:
    "Every monitoring, reporting, and management API exposed by CacheExpose — statistics, memory usage, reports, health alerts, and cache control.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/cache/monitoring/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/cache/monitoring">
      <h1>Cache Monitoring &amp; Diagnostics</h1>
      <p>
        <code>CacheExpose</code> exposes a full diagnostics surface — typed
        snapshots, several pre-formatted report strings, a structured
        monitoring dictionary for dashboards, health alerts, and manual cache
        management methods.
      </p>

      <h2 id="structured-snapshots">Structured snapshots</h2>
      <p>
        Three typed objects describe the cache state at a moment in time:
      </p>
      <table>
        <thead>
          <tr>
            <th>Method</th>
            <th>Returns</th>
            <th>Use for</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>GetCacheStatistics()</code></td>
            <td><code>CacheStatistics</code></td>
            <td>Per-store entry counts, LRU / LFU tracking-record counts, and estimated memory.</td>
          </tr>
          <tr>
            <td><code>GetCacheConfiguration()</code></td>
            <td><code>CacheConfiguration</code></td>
            <td>The currently active <Link href="/docs/cache/options"><code>CacheOptions</code></Link> values.</td>
          </tr>
          <tr>
            <td><code>GetMemoryUsage()</code></td>
            <td><code>CacheMemoryUsage</code></td>
            <td>Estimated memory per store — <code>CacheCalculator</code> sizes live entries from fixed constants rather than measuring the GC.</td>
          </tr>
        </tbody>
      </table>

      <h2 id="report-generators">Report generators</h2>
      <p>
        Four pre-formatted text reports for logs, debugger output, or admin
        endpoints:
      </p>
      <table>
        <thead>
          <tr>
            <th>Method</th>
            <th>Content</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>GeneratePerformanceReport()</code></td>
            <td>Configuration, entry counts, utilization, memory and efficiency, then the recommendations.</td>
          </tr>
          <tr>
            <td><code>GenerateCompactStatusReport()</code></td>
            <td>One-paragraph summary suitable for log lines.</td>
          </tr>
          <tr>
            <td><code>GenerateCacheAnalysisReport()</code></td>
            <td>Deeper analytical view — each store's count against <code>MaxCacheSize</code>, tracking records, eviction size, and memory distribution.</td>
          </tr>
          <tr>
            <td><code>GetQuickHealthSummary()</code></td>
            <td>One line: a status icon, the total entry count, and total memory.</td>
          </tr>
        </tbody>
      </table>

      <h2 id="monitoring-data">Structured monitoring data</h2>
      <p>
        For dashboards or metrics scrapers, <code>GenerateMonitoringReport</code>{" "}
        returns a <code>Dictionary&lt;string, object&gt;</code> with the same
        information in a structured form.
      </p>

      <h2 id="health-alerts">Health alerts</h2>
      <p>
        <code>GenerateHealthAlerts(...)</code> evaluates the cache against a
        <code>HealthAlertsInput</code> threshold object and returns one string
        per rule that fires — total memory past the warning or critical
        threshold, mean utilization at or above 90%, tracking overhead at or
        above 40%, fewer than 50 entries per MB, or a single store at or above
        95% of <code>MaxCacheSize</code>. An empty list means no rule fired.
      </p>
      <p>
        Build the input from the active options. A bare{" "}
        <code>new HealthAlertsInput()</code> leaves <code>Config</code> null,
        fails <code>IsValid()</code>, and comes back as one "ERROR: Invalid
        health alerts input parameters" item instead of an evaluation.
      </p>

      <h2 id="management">Cache management</h2>
      <table>
        <thead>
          <tr>
            <th>Method</th>
            <th>Effect</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>ClearAllCaches()</code></td>
            <td>Empties every store and resets all access tracking.</td>
          </tr>
          <tr>
            <td><code>ClearCache(CacheMemoryType)</code></td>
            <td>Empties a single store — see <Link href="/docs/enums/cache-memory-type"><code>CacheMemoryType</code></Link>.</td>
          </tr>
          <tr>
            <td><code>ForceEvictionOnAllCaches()</code></td>
            <td>Runs the configured eviction strategy on every store immediately, regardless of current size.</td>
          </tr>
          <tr>
            <td><code>IsCacheFull(CacheMemoryType)</code></td>
            <td>Returns <code>true</code> when the target store holds <em>more</em> than <code>MaxCacheSize</code> entries — the test is strictly greater, not equal.</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        Statistics and reports are read-only snapshots — calling them does
        <em> not</em> count as a cache access and will not affect LRU / LFU
        ordering.
      </Callout>

      <h2 id="full-example">Full example</h2>
      <p>The most-used calls in one place:</p>
      <Code lang="csharp">{`// Get structured statistics
CacheStatistics stats = CacheExpose.GetCacheStatistics();
CacheConfiguration config = CacheExpose.GetCacheConfiguration();
CacheMemoryUsage memory = CacheExpose.GetMemoryUsage();

// Generate reports
string perfReport = CacheExpose.GeneratePerformanceReport();
string compactReport = CacheExpose.GenerateCompactStatusReport();
string analysisReport = CacheExpose.GenerateCacheAnalysisReport();
string healthSummary = CacheExpose.GetQuickHealthSummary();

// Monitoring data for dashboards
Dictionary<string, object> monitoringData = CacheExpose.GenerateMonitoringReport();

// Health alerts — the input needs the active options, or you get one ERROR item back
var alerts = CacheExpose.GenerateHealthAlerts(
    HealthAlertsInput.WithDefaults(CacheExpose.GetCacheConfigOptions()));

// Cache management
CacheExpose.ClearAllCaches();
CacheExpose.ClearCache(CacheMemoryType.PropertyPath);
CacheExpose.ForceEvictionOnAllCaches();
bool isFull = CacheExpose.IsCacheFull(CacheMemoryType.TypeProperties);`}</Code>

      <h2 id="related">Related</h2>
      <ul>
        <li><Link href="/docs/cache/architecture">Architecture →</Link></li>
        <li><Link href="/docs/cache/stores">Cache stores →</Link></li>
        <li><Link href="/docs/cache/options">CacheOptions →</Link></li>
        <li><Link href="/docs/enums/cache-memory-type">CacheMemoryType →</Link></li>
        <li><Link href="/docs/enums/cache-eviction-strategy">CacheEvictionStrategy →</Link></li>
      </ul>
    </DocPage>
  );
}
