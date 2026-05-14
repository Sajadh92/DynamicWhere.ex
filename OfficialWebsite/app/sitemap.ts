import type { MetadataRoute } from "next";
import { NAV, SITE } from "@/lib/nav";

export const dynamic = "force-static";

const HIGH_PRIORITY = new Set<string>([
  "/docs",
  "/docs/installation",
  "/docs/quick-start",
  "/docs/examples",
  "/docs/extensions",
  "/docs/classes",
  "/docs/enums",
  "/docs/cache",
  "/docs/classes/filter",
  "/docs/validation",
]);

const MED_PRIORITY_PREFIX = ["/docs/examples/", "/docs/extensions/", "/docs/cache/"];

function priorityFor(href: string): number {
  if (HIGH_PRIORITY.has(href)) return 0.9;
  if (MED_PRIORITY_PREFIX.some((p) => href.startsWith(p))) return 0.7;
  return 0.5;
}

export default function sitemap(): MetadataRoute.Sitemap {
  const base = `https://${SITE.domain}`;
  const now = new Date();

  const routes: MetadataRoute.Sitemap = [
    {
      url: `${base}/`,
      lastModified: now,
      changeFrequency: "weekly",
      priority: 1,
    },
  ];

  for (const group of NAV) {
    for (const link of group.links) {
      routes.push({
        url: `${base}${link.href}`,
        lastModified: now,
        changeFrequency: "weekly",
        priority: priorityFor(link.href),
      });
    }
  }

  return routes;
}
