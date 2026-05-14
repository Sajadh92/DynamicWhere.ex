import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Cache Configuration",
  description:
    "Three ways to configure the DynamicWhere.ex reflection cache — preset, builder pattern, or direct CacheOptions object.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/cache/configuration/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/cache/configuration">
      <h1>Cache Configuration</h1>
      <p>
        There are three ways to configure the reflection cache. All three call
        the same <code>CacheExpose.Configure(...)</code> overload set — pick
        whichever style fits your codebase.
      </p>

      <h2 id="namespaces">Namespaces</h2>
      <Code lang="csharp">{`using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Optimization.Cache.Config;`}</Code>

      <h2 id="option-1-preset">Option 1 — Use a preset</h2>
      <p>
        The fastest way: pick a{" "}
        <Link href="/docs/cache/presets">named preset</Link> that matches your
        environment.
      </p>
      <Code lang="csharp">{`// Option 1: Use a preset
CacheExpose.Configure(CacheOptions.ForHighMemoryEnvironment());`}</Code>

      <h2 id="option-2-builder">Option 2 — Builder pattern</h2>
      <p>
        Pass an <code>Action&lt;CacheOptions&gt;</code> that mutates the
        defaults. Useful when you only want to tweak a few values.
      </p>
      <Code lang="csharp">{`// Option 2: Builder pattern
CacheExpose.Configure(options =>
{
    options.MaxCacheSize       = 2000;
    options.LeastUsedThreshold = 20;
    options.EvictionStrategy   = CacheEvictionStrategy.LFU;
});`}</Code>

      <h2 id="option-3-direct">Option 3 — Direct options object</h2>
      <p>
        Construct a <code>CacheOptions</code> instance yourself. Every property
        is explicit — the most readable choice when you're checking config
        into source control.
      </p>
      <Code lang="csharp">{`// Option 3: Direct object
CacheExpose.Configure(new CacheOptions
{
    MaxCacheSize       = 3000,
    LeastUsedThreshold = 15,
    MostUsedThreshold  = 85,
    EvictionStrategy   = CacheEvictionStrategy.LRU
});`}</Code>

      <Callout tone="info">
        Configuration is <strong>eventually consistent</strong>. Calling{" "}
        <code>Configure</code> does not invalidate or resize existing entries
        — the new options take effect for every subsequent lookup and the next
        eviction pass.
      </Callout>

      <h2 id="when-to-configure">When to call <code>Configure</code></h2>
      <p>
        Call once at application startup, before any queries run. In ASP.NET
        Core that typically means inside <code>Program.cs</code> after building
        the host but before <code>app.Run()</code>:
      </p>
      <Code lang="csharp">{`var app = builder.Build();

CacheExpose.Configure(CacheOptions.ForHighFrequencyAccess());

app.Run();`}</Code>

      <h2 id="related">Related</h2>
      <ul>
        <li><Link href="/docs/cache/options">CacheOptions reference →</Link></li>
        <li><Link href="/docs/cache/presets">All six presets →</Link></li>
        <li><Link href="/docs/cache/warmup">Warmup after configure →</Link></li>
        <li><Link href="/docs/enums/cache-eviction-strategy">CacheEvictionStrategy →</Link></li>
      </ul>
    </DocPage>
  );
}
