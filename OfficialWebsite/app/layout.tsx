import type { Metadata, Viewport } from "next";
import "./globals.css";
import { SITE } from "@/lib/nav";

export const metadata: Metadata = {
  metadataBase: new URL(`https://${SITE.domain}`),
  title: {
    default: `${SITE.name} — Documentation`,
    template: `%s · ${SITE.name}`,
  },
  description: SITE.description,
  keywords: [
    "DynamicWhere",
    "Entity Framework Core",
    "EF Core",
    "dynamic LINQ",
    "filter",
    "JSON filter",
    "C#",
    ".NET",
    "query builder",
  ],
  authors: [{ name: "Sajjad H. Al-Khafaji" }],
  openGraph: {
    title: `${SITE.name} — Documentation`,
    description: SITE.description,
    url: `https://${SITE.domain}`,
    siteName: SITE.name,
    type: "website",
    images: [
      {
        url: "/og-image.png",
        width: 1200,
        height: 1200,
        alt: SITE.name,
      },
    ],
  },
  twitter: {
    card: "summary_large_image",
    title: `${SITE.name} — Documentation`,
    description: SITE.description,
    images: ["/og-image.png"],
  },
  alternates: {
    canonical: `https://${SITE.domain}`,
  },
};

export const viewport: Viewport = {
  themeColor: "#0a0b0d",
  colorScheme: "dark",
  width: "device-width",
  initialScale: 1,
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" dir="ltr">
      <head>
        <link
          rel="preconnect"
          href="https://fonts.googleapis.com"
        />
        <link
          rel="preconnect"
          href="https://fonts.gstatic.com"
          crossOrigin=""
        />
        <link
          href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=JetBrains+Mono:wght@400;500;600&display=swap"
          rel="stylesheet"
        />
      </head>
      <body>{children}</body>
    </html>
  );
}
