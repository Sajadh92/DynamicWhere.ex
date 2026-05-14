import { ReactNode } from "react";
import DocLayout from "./DocLayout";
import PageNav from "./PageNav";
import Footer from "./Footer";

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
    </>
  );
}
