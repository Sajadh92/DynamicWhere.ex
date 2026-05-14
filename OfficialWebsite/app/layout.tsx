import type { Metadata, Viewport } from "next";
import { Inter, JetBrains_Mono } from "next/font/google";
import "./globals.css";
import { SITE } from "@/lib/nav";
import JsonLd from "@/components/JsonLd";
import {
  organizationJsonLd,
  softwareApplicationJsonLd,
  websiteJsonLd,
} from "@/lib/seo";

const inter = Inter({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
  variable: "--font-inter",
  display: "swap",
  preload: true,
});

const jetbrainsMono = JetBrains_Mono({
  subsets: ["latin"],
  weight: ["400", "500", "600"],
  variable: "--font-jetbrains",
  display: "swap",
  preload: false,
});

const BASE = `https://${SITE.domain}`;

const homeTitle = `${SITE.name} — JSON Dynamic LINQ Filter for Entity Framework Core (.NET 6/7/8/9)`;

export const metadata: Metadata = {
  metadataBase: new URL(BASE),
  title: {
    default: homeTitle,
    template: `%s · ${SITE.name} Docs`,
  },
  description: SITE.description,
  applicationName: SITE.name,
  generator: "Next.js",
  referrer: "origin-when-cross-origin",
  keywords: SITE.keywords,
  authors: [{ name: SITE.author }],
  creator: SITE.author,
  publisher: SITE.author,
  category: "technology",
  formatDetection: { email: false, address: false, telephone: false },
  alternates: {
    canonical: BASE + "/",
  },
  openGraph: {
    title: homeTitle,
    description: SITE.description,
    url: BASE + "/",
    siteName: SITE.name,
    type: "website",
    locale: "en_US",
    images: [
      {
        url: "/og-image.png",
        width: 1200,
        height: 1200,
        alt: `${SITE.name} — ${SITE.tagline}`,
        type: "image/png",
      },
    ],
  },
  twitter: {
    card: "summary_large_image",
    title: homeTitle,
    description: SITE.description,
    images: ["/og-image.png"],
  },
  icons: {
    icon: [
      { url: "/logo-32.png", sizes: "32x32", type: "image/png" },
      { url: "/logo-128.png", sizes: "128x128", type: "image/png" },
    ],
    apple: [{ url: "/logo-128.png", sizes: "128x128", type: "image/png" }],
    shortcut: ["/logo-32.png"],
  },
  manifest: "/manifest.webmanifest",
  verification: {
    google: "1oo_W-Yl08bPwEUFjNgl5COn7MPQ4n8c8kvCCWSYwQw",
    other: { "msvalidate.01": "311343216077B910A1D29787D0A20EA2" },
  },
  robots: {
    index: true,
    follow: true,
    nocache: false,
    googleBot: {
      index: true,
      follow: true,
      "max-video-preview": -1,
      "max-image-preview": "large",
      "max-snippet": -1,
    },
  },
};

export const viewport: Viewport = {
  themeColor: [
    { media: "(prefers-color-scheme: dark)", color: "#0a0b0d" },
    { media: "(prefers-color-scheme: light)", color: "#0a0b0d" },
  ],
  colorScheme: "dark",
  width: "device-width",
  initialScale: 1,
  maximumScale: 5,
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" dir="ltr" className={`${inter.variable} ${jetbrainsMono.variable}`}>
      <body className={inter.className}>
        {children}
        <JsonLd data={softwareApplicationJsonLd()} />
        <JsonLd data={organizationJsonLd()} />
        <JsonLd data={websiteJsonLd()} />
      </body>
    </html>
  );
}
