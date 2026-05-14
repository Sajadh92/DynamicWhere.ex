import Link from "next/link";
import Header from "@/components/Header";
import Footer from "@/components/Footer";

export default function NotFound() {
  return (
    <div className="flex min-h-screen flex-col bg-[var(--color-bg-1)] text-[var(--color-fg-1)]">
      <Header />
      <main className="flex flex-1 items-center justify-center px-6 py-24">
        <div className="mx-auto max-w-lg text-center">
          <p className="text-sm font-mono uppercase tracking-widest text-[var(--color-fg-3)]">
            Error 404
          </p>
          <h1 className="mt-4 text-4xl font-semibold tracking-tight md:text-5xl">
            Page not found
          </h1>
          <p className="mt-4 text-[var(--color-fg-2)]">
            The page you&apos;re looking for doesn&apos;t exist or may have been moved.
            If you got here from a link in the docs, please let us know.
          </p>
          <div className="mt-8 flex flex-col items-center justify-center gap-3 sm:flex-row">
            <Link
              href="/docs"
              className="inline-flex items-center justify-center rounded-md border border-[var(--color-border)] bg-[var(--color-accent)] px-5 py-2.5 text-sm font-medium text-black hover:opacity-90"
            >
              Back to docs
            </Link>
            <Link
              href="/"
              className="inline-flex items-center justify-center rounded-md border border-[var(--color-border)] px-5 py-2.5 text-sm font-medium text-[var(--color-fg-1)] hover:bg-[var(--color-bg-3)]"
            >
              Home
            </Link>
          </div>
        </div>
      </main>
      <Footer />
    </div>
  );
}
