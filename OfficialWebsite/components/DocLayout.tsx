"use client";

import { useState } from "react";
import Sidebar from "./Sidebar";
import Header from "./Header";

export default function DocLayout({ children }: { children: React.ReactNode }) {
  const [mobileOpen, setMobileOpen] = useState(false);

  return (
    <>
      <Header onToggleMobileNav={() => setMobileOpen((v) => !v)} />

      <div className="mx-auto flex max-w-[1400px] gap-0 px-0 lg:px-6">
        {/* Sidebar — desktop */}
        <aside className="sticky top-14 hidden h-[calc(100vh-3.5rem)] w-[260px] shrink-0 overflow-y-auto border-r border-[var(--color-border)] lg:block">
          <Sidebar />
        </aside>

        {/* Sidebar — mobile drawer */}
        {mobileOpen && (
          <div className="fixed inset-0 z-50 lg:hidden">
            <div
              className="absolute inset-0 bg-black/70 backdrop-blur-sm"
              onClick={() => setMobileOpen(false)}
            />
            <aside className="absolute left-0 top-0 h-full w-[280px] overflow-y-auto border-r border-[var(--color-border)] bg-[var(--color-bg)]">
              <div className="flex h-14 items-center justify-between border-b border-[var(--color-border)] px-4">
                <span className="text-sm font-semibold text-white">Navigation</span>
                <button
                  onClick={() => setMobileOpen(false)}
                  className="grid h-8 w-8 place-items-center rounded-md border border-[var(--color-border)] text-[var(--color-fg-2)]"
                >
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                    <line x1="18" y1="6" x2="6" y2="18" />
                    <line x1="6" y1="6" x2="18" y2="18" />
                  </svg>
                </button>
              </div>
              <Sidebar onNavigate={() => setMobileOpen(false)} />
            </aside>
          </div>
        )}

        <main className="min-w-0 flex-1 px-4 py-8 lg:px-12 lg:py-12">
          <article className="prose-doc fade-in mx-auto max-w-[820px]">{children}</article>
        </main>
      </div>
    </>
  );
}
