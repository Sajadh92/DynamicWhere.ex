import type { MetadataRoute } from "next";
import { NAV, SITE } from "@/lib/nav";

export const dynamic = "force-static";

export default function sitemap(): MetadataRoute.Sitemap {
  const base = `https://${SITE.domain}`;
  const now = new Date();

  const routes = [{ url: `${base}/`, lastModified: now, changeFrequency: "weekly" as const, priority: 1 }];

  for (const group of NAV) {
    for (const link of group.links) {
      routes.push({
        url: `${base}${link.href}`,
        lastModified: now,
        changeFrequency: "weekly" as const,
        priority: 0.7,
      });
    }
  }

  return routes;
}
