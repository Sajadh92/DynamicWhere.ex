"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { NAV } from "@/lib/nav";
import { useEffect, useRef, useState } from "react";

function normalizePath(p: string): string {
  if (!p) return "/";
  if (p.length > 1 && p.endsWith("/")) return p.slice(0, -1);
  return p;
}

export default function Sidebar({ onNavigate }: { onNavigate?: () => void }) {
  const rawPathname = usePathname();
  const pathname = normalizePath(rawPathname || "/");
  const [query, setQuery] = useState("");
  const activeRef = useRef<HTMLAnchorElement | null>(null);

  useEffect(() => {
    onNavigate?.();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pathname]);

  // Auto-scroll active link into view whenever path changes.
  useEffect(() => {
    const el = activeRef.current;
    if (!el) return;
    // Defer to next frame so layout is settled.
    requestAnimationFrame(() => {
      el.scrollIntoView({ block: "center", behavior: "smooth" });
    });
  }, [pathname]);

  const filtered = NAV.map((g) => ({
    ...g,
    links: g.links.filter((l) =>
      query ? l.title.toLowerCase().includes(query.toLowerCase()) : true,
    ),
  })).filter((g) => g.links.length > 0);

  return (
    <nav className="flex flex-col gap-1 px-3 pb-12 pt-4 text-sm">
      <div className="px-2 pb-3">
        <input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Filter nav…"
          className="w-full rounded-md border border-[var(--color-border)] bg-[var(--color-bg-2)] px-3 py-1.5 text-[13px] text-[var(--color-fg-2)] outline-none placeholder:text-[var(--color-fg-3)] focus:border-[var(--color-accent)] focus:ring-1 focus:ring-[var(--color-accent)]"
        />
      </div>

      {filtered.map((group) => (
        <div key={group.title}>
          <div className="side-section">{group.title}</div>
          {group.links.map((link) => {
            const active = pathname === link.href;
            return (
              <Link
                key={link.href}
                href={link.href}
                ref={active ? activeRef : undefined}
                aria-current={active ? "page" : undefined}
                className={`side-link ${active ? "active" : ""}`}
              >
                {active && <span className="side-link-dot" aria-hidden="true" />}
                <span className="truncate">{link.title}</span>
                {active && (
                  <svg
                    className="ml-auto shrink-0 text-[var(--color-accent)]"
                    width="12"
                    height="12"
                    viewBox="0 0 24 24"
                    fill="none"
                    stroke="currentColor"
                    strokeWidth="2.5"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  >
                    <path d="M5 12h14M13 5l7 7-7 7" />
                  </svg>
                )}
              </Link>
            );
          })}
        </div>
      ))}
    </nav>
  );
}
