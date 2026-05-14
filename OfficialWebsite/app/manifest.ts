import type { MetadataRoute } from "next";
import { SITE } from "@/lib/nav";

export const dynamic = "force-static";

export default function manifest(): MetadataRoute.Manifest {
  return {
    name: `${SITE.name} — ${SITE.tagline}`,
    short_name: SITE.name,
    description: SITE.description,
    start_url: "/",
    scope: "/",
    display: "standalone",
    background_color: "#0a0b0d",
    theme_color: "#0a0b0d",
    orientation: "portrait",
    categories: ["developer", "documentation", "productivity"],
    lang: "en",
    icons: [
      { src: "/logo-32.png", sizes: "32x32", type: "image/png" },
      { src: "/logo-128.png", sizes: "128x128", type: "image/png", purpose: "any" },
      { src: "/logo-128.png", sizes: "128x128", type: "image/png", purpose: "maskable" },
      { src: "/logo.png", sizes: "512x512", type: "image/png" },
    ],
  };
}
