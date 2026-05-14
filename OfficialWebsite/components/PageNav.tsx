import Link from "next/link";
import { findAdjacent } from "@/lib/nav";

export default function PageNav({ pathname }: { pathname: string }) {
  const { prev, next } = findAdjacent(pathname);
  if (!prev && !next) return null;

  return (
    <div className="mt-16 grid grid-cols-1 gap-3 border-t border-[var(--color-border)] pt-8 sm:grid-cols-2">
      {prev ? (
        <Link
          href={prev.href}
          className="group flex flex-col rounded-lg border border-[var(--color-border)] bg-[var(--color-panel)] p-4 transition hover:border-[var(--color-border-2)] hover:bg-[var(--color-bg-3)]"
        >
          <span className="text-[11px] uppercase tracking-wider text-[var(--color-fg-3)]">
            ← Previous
          </span>
          <span className="mt-1 text-[14.5px] font-medium text-[var(--color-fg)] group-hover:text-white">
            {prev.title}
          </span>
        </Link>
      ) : (
        <div />
      )}

      {next ? (
        <Link
          href={next.href}
          className="group flex flex-col rounded-lg border border-[var(--color-border)] bg-[var(--color-panel)] p-4 text-right transition hover:border-[var(--color-border-2)] hover:bg-[var(--color-bg-3)]"
        >
          <span className="text-[11px] uppercase tracking-wider text-[var(--color-fg-3)]">
            Next →
          </span>
          <span className="mt-1 text-[14.5px] font-medium text-[var(--color-fg)] group-hover:text-white">
            {next.title}
          </span>
        </Link>
      ) : (
        <div />
      )}
    </div>
  );
}
