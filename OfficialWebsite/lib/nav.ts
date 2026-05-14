export type NavLink = { title: string; href: string };
export type NavGroup = { title: string; links: NavLink[] };

export const SITE = {
  name: "DynamicWhere.ex",
  tagline: "JSON-driven dynamic LINQ for Entity Framework Core",
  description:
    "DynamicWhere.ex is a free .NET library for building dynamic, JSON-driven LINQ queries on Entity Framework Core — filter, sort, paginate, project, group, aggregate, and run UNION / INTERSECT / EXCEPT set operations from your front-end. Works with ASP.NET Core on .NET 6, 7, 8, and 9.",
  shortDescription:
    "Dynamic JSON filter, sort, paginate, group, aggregate, and set operations for EF Core. .NET 6/7/8/9.",
  version: "2.1.0",
  domain: "doc.dynamicwhere.com",
  repo: "https://github.com/Sajadh92/DynamicWhere.ex",
  nuget: "https://www.nuget.org/packages/DynamicWhere.ex",
  author: "Sajjad H. Al-Khafaji",
  twitterHandle: "",
  keywords: [
    "DynamicWhere",
    "DynamicWhere.ex",
    "dynamic LINQ",
    "dynamic LINQ JSON",
    "Entity Framework Core",
    "EF Core",
    "EF Core dynamic filter",
    "EF Core query builder",
    "EF Core JSON filter",
    "dynamic IQueryable",
    "IQueryable filter",
    "ASP.NET Core query builder",
    "ASP.NET Core dynamic filter",
    "C# dynamic query",
    ".NET dynamic LINQ",
    ".NET 6 EF Core filter",
    ".NET 7 EF Core filter",
    ".NET 8 EF Core filter",
    ".NET 9 EF Core filter",
    "runtime LINQ",
    "predicate builder",
    "search API .NET",
    "filter builder library",
    "groupby aggregate EF Core",
    "union intersect except EF Core",
    "pagination EF Core",
    "projection select EF Core",
  ],
};

export const NAV: NavGroup[] = [
  {
    title: "Getting Started",
    links: [
      { title: "Introduction", href: "/docs" },
      { title: "Installation", href: "/docs/installation" },
      { title: "Quick Start", href: "/docs/quick-start" },
    ],
  },
  {
    title: "Enums",
    links: [
      { title: "Overview", href: "/docs/enums" },
      { title: "DataType", href: "/docs/enums/data-type" },
      { title: "Operator", href: "/docs/enums/operator" },
      { title: "Connector", href: "/docs/enums/connector" },
      { title: "Direction", href: "/docs/enums/direction" },
      { title: "Intersection", href: "/docs/enums/intersection" },
      { title: "Aggregator", href: "/docs/enums/aggregator" },
      { title: "CacheEvictionStrategy", href: "/docs/enums/cache-eviction-strategy" },
      { title: "CacheMemoryType", href: "/docs/enums/cache-memory-type" },
    ],
  },
  {
    title: "Classes — Core",
    links: [
      { title: "Overview", href: "/docs/classes" },
      { title: "Condition", href: "/docs/classes/condition" },
      { title: "ConditionGroup", href: "/docs/classes/condition-group" },
      { title: "ConditionSet", href: "/docs/classes/condition-set" },
      { title: "OrderBy", href: "/docs/classes/order-by" },
      { title: "GroupBy", href: "/docs/classes/group-by" },
      { title: "AggregateBy", href: "/docs/classes/aggregate-by" },
      { title: "PageBy", href: "/docs/classes/page-by" },
    ],
  },
  {
    title: "Classes — Complex",
    links: [
      { title: "Filter", href: "/docs/classes/filter" },
      { title: "Segment", href: "/docs/classes/segment" },
      { title: "Summary", href: "/docs/classes/summary" },
    ],
  },
  {
    title: "Classes — Result",
    links: [
      { title: "FilterResult<T>", href: "/docs/classes/filter-result" },
      { title: "SegmentResult<T>", href: "/docs/classes/segment-result" },
      { title: "SummaryResult", href: "/docs/classes/summary-result" },
    ],
  },
  {
    title: "Extension Methods",
    links: [
      { title: "Overview", href: "/docs/extensions" },
      { title: ".Select<T>", href: "/docs/extensions/select" },
      { title: ".SelectDynamic<T>", href: "/docs/extensions/select-dynamic" },
      { title: ".Where<T>", href: "/docs/extensions/where" },
      { title: ".Group<T>", href: "/docs/extensions/group" },
      { title: ".Order<T>", href: "/docs/extensions/order" },
      { title: ".Page<T>", href: "/docs/extensions/page" },
      { title: ".Filter<T>", href: "/docs/extensions/filter" },
      { title: ".FilterDynamic<T>", href: "/docs/extensions/filter-dynamic" },
      { title: ".ToList<T>(Filter)", href: "/docs/extensions/to-list-filter" },
      { title: ".ToListAsync<T>(Filter)", href: "/docs/extensions/to-list-async-filter" },
      { title: ".ToListDynamic<T>(Filter)", href: "/docs/extensions/to-list-dynamic-filter" },
      { title: ".ToListAsyncDynamic<T>(Filter)", href: "/docs/extensions/to-list-async-dynamic-filter" },
      { title: ".Summary<T>", href: "/docs/extensions/summary" },
      { title: ".ToList<T>(Summary)", href: "/docs/extensions/to-list-summary" },
      { title: ".ToListAsync<T>(Summary)", href: "/docs/extensions/to-list-async-summary" },
      { title: ".ToListAsync<T>(Segment)", href: "/docs/extensions/to-list-async-segment" },
    ],
  },
  {
    title: "Validation",
    links: [
      { title: "Overview", href: "/docs/validation" },
      { title: "Condition Rules", href: "/docs/validation/condition" },
      { title: "ConditionGroup Rules", href: "/docs/validation/condition-group" },
      { title: "GroupBy Rules", href: "/docs/validation/group-by" },
      { title: "Segment Rules", href: "/docs/validation/segment" },
      { title: "Summary Rules", href: "/docs/validation/summary" },
      { title: "Page Rules", href: "/docs/validation/page" },
    ],
  },
  {
    title: "JSON Cookbook",
    links: [
      { title: "Overview", href: "/docs/examples" },
      { title: "1. Select — Projection", href: "/docs/examples/select" },
      { title: "2. Where(Condition)", href: "/docs/examples/where-single" },
      { title: "3. Where(ConditionGroup)", href: "/docs/examples/where-group" },
      { title: "4. Order", href: "/docs/examples/order" },
      { title: "5. Page", href: "/docs/examples/page" },
      { title: "6. Group + Aggregate", href: "/docs/examples/group" },
      { title: "7. Filter (Typed)", href: "/docs/examples/filter" },
      { title: "8. Summary", href: "/docs/examples/summary" },
      { title: "9. Segment", href: "/docs/examples/segment" },
      { title: "11. SelectDynamic", href: "/docs/examples/select-dynamic" },
      { title: "12. FilterDynamic", href: "/docs/examples/filter-dynamic" },
      { title: "13. Nested Collections", href: "/docs/examples/nested-collection" },
    ],
  },
  {
    title: "Cache & Optimization",
    links: [
      { title: "Overview", href: "/docs/cache" },
      { title: "Architecture", href: "/docs/cache/architecture" },
      { title: "Cache Stores", href: "/docs/cache/stores" },
      { title: "CacheOptions", href: "/docs/cache/options" },
      { title: "Configuration", href: "/docs/cache/configuration" },
      { title: "Warmup", href: "/docs/cache/warmup" },
      { title: "Monitoring", href: "/docs/cache/monitoring" },
      { title: "Presets", href: "/docs/cache/presets" },
    ],
  },
  {
    title: "Reference",
    links: [
      { title: "Error Codes", href: "/docs/errors" },
      { title: "Breaking Changes & Limits", href: "/docs/breaking-changes" },
    ],
  },
];

export type NavLinkWithGroup = NavLink & { group: string };

export function flatNav(): NavLinkWithGroup[] {
  return NAV.flatMap((g) => g.links.map((l) => ({ ...l, group: g.title })));
}

export type AdjacentLink = {
  link: NavLinkWithGroup;
  isNewGroup: boolean;
};

export function findAdjacent(href: string): {
  prev: AdjacentLink | null;
  next: AdjacentLink | null;
} {
  const flat = flatNav();
  const i = flat.findIndex((l) => l.href === href);
  if (i < 0) return { prev: null, next: null };
  const current = flat[i];
  const prev = i > 0 ? flat[i - 1] : null;
  const next = i < flat.length - 1 ? flat[i + 1] : null;
  return {
    prev: prev ? { link: prev, isNewGroup: prev.group !== current.group } : null,
    next: next ? { link: next, isNewGroup: next.group !== current.group } : null,
  };
}
