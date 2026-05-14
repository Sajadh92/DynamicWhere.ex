"use client";

import Link from "next/link";
import Image from "next/image";
import { SITE } from "@/lib/nav";
import Search from "./Search";

export default function Header({
  onToggleMobileNav,
}: {
  onToggleMobileNav?: () => void;
}) {
  return (
    <header className="sticky top-0 z-40 border-b border-[var(--color-border)] bg-[rgba(10,11,13,0.8)] backdrop-blur-md">
      <div className="mx-auto flex h-14 max-w-[1400px] items-center gap-3 px-4 lg:px-6">
        {onToggleMobileNav && (
          <button
            onClick={onToggleMobileNav}
            aria-label="Toggle navigation"
            className="grid h-9 w-9 place-items-center rounded-md border border-[var(--color-border)] text-[var(--color-fg-2)] hover:bg-[var(--color-bg-3)] lg:hidden"
          >
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
              <line x1="3" y1="6" x2="21" y2="6" />
              <line x1="3" y1="12" x2="21" y2="12" />
              <line x1="3" y1="18" x2="21" y2="18" />
            </svg>
          </button>
        )}

        <Link href="/" className="group flex items-center gap-2.5">
          <span className="relative grid h-8 w-8 shrink-0 place-items-center overflow-hidden rounded-md bg-[var(--color-panel)] shadow-[0_0_18px_-6px_rgba(139,92,246,0.6)]">
            <Image
              src="/logo-128.png"
              alt="DynamicWhere.ex"
              width={32}
              height={32}
              priority
              className="h-8 w-8 object-contain"
            />
          </span>
          <div className="flex flex-col gap-1 leading-none">
            <span className="font-semibold tracking-tight text-white">{SITE.name}</span>
            <span className="inline-flex w-fit items-center gap-1.5 rounded-full border border-[var(--color-border)] bg-[var(--color-panel)] px-1.5 py-[2px] font-mono text-[9.5px] font-medium leading-none text-[var(--color-fg-2)] shadow-[0_0_12px_-6px_rgba(139,92,246,0.6)]">
              <span className="relative grid h-1.5 w-1.5 place-items-center">
                <span className="absolute inset-0 rounded-full bg-emerald-400/70 blur-[2px]" />
                <span className="relative h-1.5 w-1.5 rounded-full bg-emerald-400" />
              </span>
              <span className="bg-gradient-to-r from-violet-300 to-fuchsia-300 bg-clip-text text-transparent">
                v{SITE.version}
              </span>
              <span className="text-[var(--color-fg-3)]">·</span>
              <span className="uppercase tracking-wider text-[var(--color-fg-3)]">docs</span>
            </span>
          </div>
        </Link>

        <div className="ml-4 hidden items-center gap-1 text-[13px] text-[var(--color-fg-2)] md:flex">
          <Link href="/docs" className="rounded-md px-3 py-1.5 hover:bg-[var(--color-bg-3)] hover:text-white">
            Docs
          </Link>
          <Link href="/docs/quick-start" className="rounded-md px-3 py-1.5 hover:bg-[var(--color-bg-3)] hover:text-white">
            Quick Start
          </Link>
          <Link href="/docs/examples" className="rounded-md px-3 py-1.5 hover:bg-[var(--color-bg-3)] hover:text-white">
            Examples
          </Link>
          <Link href="/docs/cache" className="rounded-md px-3 py-1.5 hover:bg-[var(--color-bg-3)] hover:text-white">
            Cache
          </Link>
        </div>

        <div className="flex-1" />

        <div className="hidden sm:block">
          <Search />
        </div>

        <a
          href={SITE.nuget}
          target="_blank"
          rel="noreferrer"
          className="hidden items-center gap-1.5 rounded-md border border-[var(--color-border)] bg-[var(--color-panel)] px-3 py-1.5 text-[12.5px] text-[var(--color-fg-2)] transition hover:border-[var(--color-border-2)] hover:text-white md:inline-flex"
        >
          <svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor"><path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z"/></svg>
          NuGet
        </a>

        <a
          href={SITE.repo}
          target="_blank"
          rel="noreferrer"
          aria-label="GitHub"
          className="grid h-9 w-9 place-items-center rounded-md border border-[var(--color-border)] bg-[var(--color-panel)] text-[var(--color-fg-2)] transition hover:border-[var(--color-border-2)] hover:text-white"
        >
          <svg width="15" height="15" viewBox="0 0 24 24" fill="currentColor"><path d="M12 .3a12 12 0 0 0-3.8 23.4c.6.1.8-.3.8-.6v-2c-3.3.7-4-1.6-4-1.6-.6-1.4-1.4-1.8-1.4-1.8-1-.7.1-.7.1-.7 1.2.1 1.8 1.2 1.8 1.2 1 1.8 2.8 1.3 3.5 1 .1-.8.4-1.3.8-1.6-2.7-.3-5.5-1.3-5.5-6 0-1.2.5-2.3 1.3-3.1-.2-.4-.6-1.6 0-3.2 0 0 1-.3 3.4 1.2a11.5 11.5 0 0 1 6 0c2.3-1.5 3.3-1.2 3.3-1.2.7 1.6.2 2.8.1 3.2.8.8 1.3 1.9 1.3 3.1 0 4.6-2.8 5.6-5.5 5.9.5.4.8 1.1.8 2.2v3.3c0 .3.2.7.8.6A12 12 0 0 0 12 .3"/></svg>
        </a>
      </div>
    </header>
  );
}
