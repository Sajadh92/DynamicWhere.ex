import Link from "next/link";
import { SITE } from "@/lib/nav";
import Footer from "@/components/Footer";
import Header from "@/components/Header";
import { Code } from "@/components/Code";

const FEATURES = [
  {
    title: "JSON-driven filters",
    desc: "Pass a Condition / ConditionGroup straight from your front-end. No expression trees, no string LINQ.",
    icon: "filter",
  },
  {
    title: "Sort, page & project",
    desc: "Filter is a single object that wraps where, order, page, and select projection.",
    icon: "layers",
  },
  {
    title: "GroupBy + aggregations",
    desc: "Sum, Average, Count, Min, Max, FirstOrDefault, LastOrDefault — with optional Having.",
    icon: "chart",
  },
  {
    title: "Set operations",
    desc: "Union, Intersect, Except across multiple ConditionSets in one Segment query.",
    icon: "merge",
  },
  {
    title: "Nested navigation",
    desc: "Dotted paths through references and collections — auto-wrapped with .Any() where needed.",
    icon: "tree",
  },
  {
    title: "Reflection cache",
    desc: "Built-in thread-safe cache with FIFO / LRU / LFU eviction and six tuned presets.",
    icon: "cpu",
  },
];

const INSTALL_SNIPPET = `dotnet add package DynamicWhere.ex --version ${SITE.version}`;

const QUICK_SNIPPET = `using DynamicWhere.ex.Source;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;

var filter = new Filter
{
    ConditionGroup = new ConditionGroup
    {
        Connector = Connector.And,
        Conditions = new List<Condition>
        {
            new Condition
            {
                Sort = 1,
                Field = "Name",
                DataType = DataType.Text,
                Operator = Operator.IContains,
                Values = new List<object> { "john" }
            }
        }
    },
    Orders = new List<OrderBy>
    {
        new OrderBy { Sort = 1, Field = "CreatedAt", Direction = Direction.Descending }
    },
    Page = new PageBy { PageNumber = 1, PageSize = 10 }
};

FilterResult<Customer> result = await dbContext.Customers.ToListAsync(filter);`;

const JSON_SNIPPET = `{
  "conditionGroup": {
    "connector": "And",
    "conditions": [
      { "sort": 1, "field": "Price", "dataType": "Number",
        "operator": "GreaterThan", "values": ["50"] },
      { "sort": 2, "field": "Category.Name", "dataType": "Text",
        "operator": "IEqual", "values": ["electronics"] }
    ],
    "subConditionGroups": []
  },
  "selects": ["Id", "Name", "Price", "Category.Name"],
  "orders": [{ "sort": 1, "field": "Price", "direction": "Descending" }],
  "page": { "pageNumber": 1, "pageSize": 10 }
}`;

export default function HomePage() {
  return (
    <>
      <Header />

      {/* HERO */}
      <section className="relative overflow-hidden">
        <div className="hero-grid" />
        <div
          className="hero-orb"
          style={{ top: "-160px", left: "50%", transform: "translateX(-50%)" }}
        />

        <div className="relative mx-auto max-w-[1100px] px-6 pb-20 pt-20 text-center lg:pt-28">
          <a
            href={SITE.repo}
            target="_blank"
            rel="noreferrer"
            className="inline-flex items-center gap-2 rounded-full border border-[var(--color-border)] bg-[var(--color-panel)] px-3 py-1 text-[12px] text-[var(--color-fg-2)] transition hover:border-[var(--color-accent)] hover:text-white"
          >
            <span className="grid h-4 w-4 place-items-center rounded-full bg-[var(--color-accent)] text-[9px] font-bold text-white">
              v
            </span>
            <span>
              {SITE.version} released · heterogeneous values, six cache presets
            </span>
            <span className="text-[var(--color-fg-3)]">→</span>
          </a>

          <h1 className="mt-6 text-balance bg-gradient-to-b from-white via-white to-[#9ca3af] bg-clip-text text-5xl font-bold tracking-tight text-transparent sm:text-6xl lg:text-7xl">
            JSON-driven queries.<br />
            <span className="bg-gradient-to-r from-violet-400 to-fuchsia-400 bg-clip-text text-transparent">
              For EF Core. Done right.
            </span>
          </h1>

          <p className="mx-auto mt-6 max-w-[640px] text-pretty text-[16px] leading-relaxed text-[var(--color-fg-2)] sm:text-[17px]">
            {SITE.description}
          </p>

          <div className="mt-8 flex flex-col items-center justify-center gap-3 sm:flex-row">
            <Link
              href="/docs/quick-start"
              className="inline-flex items-center gap-2 rounded-md bg-gradient-to-r from-violet-500 to-purple-600 px-5 py-2.5 text-[14px] font-medium text-white shadow-[0_0_30px_-6px_rgba(139,92,246,0.7)] transition hover:from-violet-400 hover:to-purple-500"
            >
              Get Started
              <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <path d="M5 12h14M13 5l7 7-7 7" />
              </svg>
            </Link>
            <Link
              href="/docs/examples"
              className="inline-flex items-center gap-2 rounded-md border border-[var(--color-border)] bg-[var(--color-panel)] px-5 py-2.5 text-[14px] font-medium text-[var(--color-fg)] transition hover:border-[var(--color-border-2)] hover:bg-[var(--color-bg-3)]"
            >
              JSON Cookbook
            </Link>
          </div>

          <div className="mx-auto mt-10 w-full min-w-0 max-w-[560px]">
            <div className="relative min-w-0 overflow-hidden rounded-lg border border-[var(--color-border)] bg-[var(--color-panel)]">
              <div className="flex items-center justify-between border-b border-[var(--color-border)] px-3 py-2">
                <div className="flex items-center gap-1.5">
                  <span className="h-2.5 w-2.5 rounded-full bg-[#3b3f47]" />
                  <span className="h-2.5 w-2.5 rounded-full bg-[#3b3f47]" />
                  <span className="h-2.5 w-2.5 rounded-full bg-[#3b3f47]" />
                </div>
                <span className="text-[11px] text-[var(--color-fg-3)]">terminal</span>
              </div>
              <pre className="overflow-x-auto whitespace-pre px-4 py-3 text-left text-[13px] text-[var(--color-fg-2)] font-mono">
                <span className="text-[var(--color-fg-3)]">$</span> {INSTALL_SNIPPET}
              </pre>
            </div>
          </div>
        </div>
      </section>

      {/* FEATURES */}
      <section className="border-t border-[var(--color-border)] bg-[var(--color-bg-2)]">
        <div className="mx-auto max-w-[1100px] px-6 py-20">
          <div className="text-center">
            <span className="text-[11px] font-semibold uppercase tracking-wider text-[var(--color-accent)]">
              What's inside
            </span>
            <h2 className="mt-2 text-3xl font-bold tracking-tight text-white sm:text-4xl">
              Everything you need to query dynamically
            </h2>
            <p className="mx-auto mt-3 max-w-[560px] text-[15px] text-[var(--color-fg-2)]">
              One package. Eleven extension methods. Three composable shapes (Filter, Segment, Summary).
            </p>
          </div>

          <div className="mt-12 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {FEATURES.map((f) => (
              <div
                key={f.title}
                className="group relative overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-panel)] p-5 transition hover:border-[var(--color-border-2)]"
              >
                <div className="mb-3 grid h-9 w-9 place-items-center rounded-md bg-gradient-to-br from-violet-500/20 to-purple-600/20 text-[var(--color-accent)]">
                  <FeatureIcon kind={f.icon} />
                </div>
                <h3 className="text-[15px] font-semibold text-white">{f.title}</h3>
                <p className="mt-1.5 text-[13.5px] leading-relaxed text-[var(--color-fg-2)]">
                  {f.desc}
                </p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* CODE COMPARISON */}
      <section className="border-t border-[var(--color-border)]">
        <div className="mx-auto max-w-[1100px] px-6 py-20">
          <div className="text-center">
            <span className="text-[11px] font-semibold uppercase tracking-wider text-[var(--color-accent)]">
              In practice
            </span>
            <h2 className="mt-2 text-3xl font-bold tracking-tight text-white sm:text-4xl">
              From front-end JSON to materialized result
            </h2>
          </div>

          <div className="mt-12 grid grid-cols-1 gap-6 lg:grid-cols-2">
            <div className="min-w-0 rounded-xl border border-[var(--color-border)] bg-[var(--color-panel)] p-5">
              <div className="mb-3 flex items-center justify-between">
                <span className="text-[13px] font-medium text-white">Front-end / API body</span>
                <span className="rounded-md bg-[var(--color-bg-3)] px-2 py-0.5 text-[11px] text-[var(--color-fg-2)]">JSON</span>
              </div>
              <Code lang="json">{JSON_SNIPPET}</Code>
            </div>

            <div className="min-w-0 rounded-xl border border-[var(--color-border)] bg-[var(--color-panel)] p-5">
              <div className="mb-3 flex items-center justify-between">
                <span className="text-[13px] font-medium text-white">Back-end / C# usage</span>
                <span className="rounded-md bg-[var(--color-bg-3)] px-2 py-0.5 text-[11px] text-[var(--color-fg-2)]">C#</span>
              </div>
              <Code lang="csharp">{QUICK_SNIPPET}</Code>
            </div>
          </div>
        </div>
      </section>

      {/* QUICK NAV */}
      <section className="border-t border-[var(--color-border)] bg-[var(--color-bg-2)]">
        <div className="mx-auto max-w-[1100px] px-6 py-20">
          <div className="text-center">
            <span className="text-[11px] font-semibold uppercase tracking-wider text-[var(--color-accent)]">
              Pick your path
            </span>
            <h2 className="mt-2 text-3xl font-bold tracking-tight text-white sm:text-4xl">
              Jump straight to what you need
            </h2>
          </div>

          <div className="mt-12 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
            {[
              { title: "Installation", desc: "Add to your project in seconds.", href: "/docs/installation" },
              { title: "Quick Start", desc: "A working filter in 30 lines.", href: "/docs/quick-start" },
              { title: "Extension Methods", desc: "All 17 methods, one place.", href: "/docs/extensions" },
              { title: "JSON Cookbook", desc: "13 copy-pasteable examples.", href: "/docs/examples" },
              { title: "Enums Reference", desc: "Every DataType & Operator.", href: "/docs/enums" },
              { title: "Classes Reference", desc: "Condition → Filter → Result.", href: "/docs/classes" },
              { title: "Cache Tuning", desc: "Six tuned presets.", href: "/docs/cache/presets" },
              { title: "Error Codes", desc: "Every LogicException explained.", href: "/docs/errors" },
            ].map((c) => (
              <Link
                key={c.href}
                href={c.href}
                className="group flex flex-col rounded-xl border border-[var(--color-border)] bg-[var(--color-panel)] p-5 transition hover:border-[var(--color-accent)]"
              >
                <span className="text-[14.5px] font-semibold text-white group-hover:text-[var(--color-accent)]">
                  {c.title}
                </span>
                <span className="mt-1 text-[13px] text-[var(--color-fg-2)]">{c.desc}</span>
                <span className="mt-3 text-[12px] text-[var(--color-fg-3)]">Read →</span>
              </Link>
            ))}
          </div>
        </div>
      </section>

      <Footer />
    </>
  );
}

function FeatureIcon({ kind }: { kind: string }) {
  const props = {
    width: 18,
    height: 18,
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: 2,
    strokeLinecap: "round" as const,
    strokeLinejoin: "round" as const,
  };
  switch (kind) {
    case "filter": return <svg {...props}><polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3" /></svg>;
    case "layers": return <svg {...props}><polygon points="12 2 2 7 12 12 22 7 12 2" /><polyline points="2 17 12 22 22 17" /><polyline points="2 12 12 17 22 12" /></svg>;
    case "chart": return <svg {...props}><line x1="18" y1="20" x2="18" y2="10" /><line x1="12" y1="20" x2="12" y2="4" /><line x1="6" y1="20" x2="6" y2="14" /></svg>;
    case "merge": return <svg {...props}><circle cx="18" cy="18" r="3" /><circle cx="6" cy="6" r="3" /><path d="M6 21V9a9 9 0 0 0 9 9" /></svg>;
    case "tree": return <svg {...props}><path d="M12 2v6M12 8l-4 4M12 8l4 4M8 12v6M16 12v6M8 18h.01M16 18h.01M12 22h.01" /></svg>;
    case "cpu": return <svg {...props}><rect x="4" y="4" width="16" height="16" rx="2" /><rect x="9" y="9" width="6" height="6" /><line x1="9" y1="1" x2="9" y2="4" /><line x1="15" y1="1" x2="15" y2="4" /><line x1="9" y1="20" x2="9" y2="23" /><line x1="15" y1="20" x2="15" y2="23" /><line x1="20" y1="9" x2="23" y2="9" /><line x1="20" y1="14" x2="23" y2="14" /><line x1="1" y1="9" x2="4" y2="9" /><line x1="1" y1="14" x2="4" y2="14" /></svg>;
    default: return null;
  }
}
