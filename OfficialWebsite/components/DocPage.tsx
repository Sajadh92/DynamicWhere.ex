import { ReactNode } from "react";
import DocLayout from "./DocLayout";
import PageNav from "./PageNav";
import Footer from "./Footer";
import JsonLd from "./JsonLd";
import { breadcrumbJsonLd } from "@/lib/seo";
import { flatNav } from "@/lib/nav";

function buildBreadcrumbs(pathname: string) {
  const flat = flatNav();
  const current = flat.find((l) => l.href === pathname);
  const crumbs: Array<{ name: string; href: string }> = [
    { name: "Home", href: "/" },
    { name: "Documentation", href: "/docs" },
  ];
  if (current && current.href !== "/docs") {
    if (current.group) crumbs.push({ name: current.group, href: pathname });
    if (current.title !== current.group)
      crumbs.push({ name: current.title, href: pathname });
  }
  return crumbs;
}

export default function DocPage({
  pathname,
  children,
}: {
  pathname: string;
  children: ReactNode;
}) {
  return (
    <>
      <DocLayout>
        {children}
        <PageNav pathname={pathname} />
      </DocLayout>
      <Footer />
      <JsonLd data={breadcrumbJsonLd(buildBreadcrumbs(pathname))} />
    </>
  );
}
