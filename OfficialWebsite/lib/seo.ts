import type { Metadata } from "next";
import { SITE } from "./nav";

const BASE = `https://${SITE.domain}`;

export function absUrl(href: string): string {
  if (!href.startsWith("/")) href = `/${href}`;
  return `${BASE}${href}`.replace(/\/+$/, "/");
}

export function canonical(href: string): string {
  return absUrl(href);
}

export type DocMetaInput = {
  href: string;
  title: string;
  description: string;
  keywords?: string[];
};

export function docMeta({ href, title, description, keywords }: DocMetaInput): Metadata {
  const url = canonical(href);
  return {
    title,
    description,
    keywords: keywords && keywords.length ? keywords : undefined,
    alternates: { canonical: url },
    openGraph: {
      title: `${title} · ${SITE.name}`,
      description,
      url,
      siteName: SITE.name,
      type: "article",
    },
    twitter: {
      card: "summary_large_image",
      title: `${title} · ${SITE.name}`,
      description,
    },
  };
}

export function softwareApplicationJsonLd() {
  return {
    "@context": "https://schema.org",
    "@type": "SoftwareApplication",
    name: SITE.name,
    alternateName: ["DynamicWhere", "Dynamic Where ex"],
    description: SITE.description,
    applicationCategory: "DeveloperApplication",
    operatingSystem: "Cross-platform (.NET 6, 7, 8, 9)",
    softwareVersion: SITE.version,
    url: BASE,
    downloadUrl: SITE.nuget,
    codeRepository: SITE.repo,
    programmingLanguage: ["C#", ".NET"],
    license: "https://opensource.org/licenses/MIT",
    offers: {
      "@type": "Offer",
      price: "0",
      priceCurrency: "USD",
    },
    author: {
      "@type": "Person",
      name: SITE.author,
    },
    keywords: SITE.keywords.join(", "),
  };
}

export function organizationJsonLd() {
  return {
    "@context": "https://schema.org",
    "@type": "Organization",
    name: SITE.name,
    url: BASE,
    logo: `${BASE}/logo-128.png`,
    sameAs: [SITE.repo, SITE.nuget],
  };
}

export function websiteJsonLd() {
  return {
    "@context": "https://schema.org",
    "@type": "WebSite",
    name: SITE.name,
    url: BASE,
    description: SITE.description,
    inLanguage: "en",
    potentialAction: {
      "@type": "SearchAction",
      target: {
        "@type": "EntryPoint",
        urlTemplate: `${BASE}/docs?q={search_term_string}`,
      },
      "query-input": "required name=search_term_string",
    },
  };
}

export function breadcrumbJsonLd(items: Array<{ name: string; href: string }>) {
  return {
    "@context": "https://schema.org",
    "@type": "BreadcrumbList",
    itemListElement: items.map((item, i) => ({
      "@type": "ListItem",
      position: i + 1,
      name: item.name,
      item: canonical(item.href),
    })),
  };
}

export type Faq = { q: string; a: string };

export function faqJsonLd(items: Faq[]) {
  return {
    "@context": "https://schema.org",
    "@type": "FAQPage",
    mainEntity: items.map((f) => ({
      "@type": "Question",
      name: f.q,
      acceptedAnswer: {
        "@type": "Answer",
        text: f.a,
      },
    })),
  };
}

export const HOME_FAQ: Faq[] = [
  {
    q: "What is DynamicWhere.ex?",
    a: "DynamicWhere.ex is a free, open-source .NET library that turns JSON filter objects into safe, validated, native Entity Framework Core LINQ queries. It handles where, order, paging, projection, group-by, aggregation, having, and UNION / INTERSECT / EXCEPT set operations.",
  },
  {
    q: "Which .NET and EF Core versions are supported?",
    a: "DynamicWhere.ex targets .NET 6, .NET 7, .NET 8, and .NET 9. It works with Entity Framework Core 6+ on any provider that supports IQueryable<T> — SQL Server, PostgreSQL (Npgsql), MySQL (Pomelo), and SQLite are all known to work.",
  },
  {
    q: "How is DynamicWhere.ex different from System.Linq.Dynamic.Core?",
    a: "System.Linq.Dynamic.Core uses string-based predicates. DynamicWhere.ex uses strongly-typed JSON-friendly objects (Condition, ConditionGroup, Filter, Segment, Summary) that are validated, serializable, and safe to expose to your front-end. It also bundles ordering, paging, projection, grouping, aggregation, and set operations in one API.",
  },
  {
    q: "Is DynamicWhere.ex safe from SQL injection?",
    a: "Yes. All filters are parsed into LINQ expression trees and executed through EF Core's parameterized query pipeline. User-supplied field names are validated against the entity type, and operators are restricted to a closed enum.",
  },
  {
    q: "Can I use DynamicWhere.ex in ASP.NET Core APIs?",
    a: "Yes — it is designed for that. Accept a Filter, Segment, or Summary JSON body in your controller, then call .ToListAsync(filter) on any DbSet<T>. The library validates the shape, builds the IQueryable, and returns a strongly-typed result with pagination metadata.",
  },
  {
    q: "Does DynamicWhere.ex support nested navigation properties?",
    a: "Yes. Use dotted field paths like 'Category.Name' or 'Orders.Items.Price'. The library auto-wraps collection traversals with .Any() where needed and validates the path against your entity model.",
  },
  {
    q: "Is it free?",
    a: "Yes. DynamicWhere.ex is licensed under MIT and is free forever for commercial and personal use. The package is published on NuGet.",
  },
];
