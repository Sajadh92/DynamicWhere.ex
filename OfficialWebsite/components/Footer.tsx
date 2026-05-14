import Link from "next/link";
import Image from "next/image";
import { SITE } from "@/lib/nav";

export default function Footer() {
  return (
    <footer className="mt-24 border-t border-[var(--color-border)] bg-[var(--color-bg-2)]">
      <div className="mx-auto grid max-w-[1400px] grid-cols-1 gap-8 px-6 py-12 sm:grid-cols-2 lg:grid-cols-4">
        <div>
          <div className="flex items-center gap-2.5">
            <span className="relative grid h-8 w-8 shrink-0 place-items-center overflow-hidden rounded-md bg-[var(--color-panel)]">
              <Image
                src="/logo-128.png"
                alt="DynamicWhere.ex"
                width={32}
                height={32}
                className="h-8 w-8 object-contain"
              />
            </span>
            <span className="font-semibold tracking-tight text-white">{SITE.name}</span>
          </div>
          <p className="mt-3 text-[13px] leading-relaxed text-[var(--color-fg-3)]">
            Dynamic filter, sort, paginate, group, aggregate, and set-operation expressions for EF Core — driven by JSON.
          </p>
        </div>

        <div>
          <div className="text-[11px] font-semibold uppercase tracking-wider text-[var(--color-fg-3)]">
            Docs
          </div>
          <ul className="mt-3 space-y-2 text-[13px]">
            <li><Link className="text-[var(--color-fg-2)] hover:text-white" href="/docs/installation">Installation</Link></li>
            <li><Link className="text-[var(--color-fg-2)] hover:text-white" href="/docs/quick-start">Quick Start</Link></li>
            <li><Link className="text-[var(--color-fg-2)] hover:text-white" href="/docs/extensions">Extension Methods</Link></li>
            <li><Link className="text-[var(--color-fg-2)] hover:text-white" href="/docs/examples">JSON Cookbook</Link></li>
          </ul>
        </div>

        <div>
          <div className="text-[11px] font-semibold uppercase tracking-wider text-[var(--color-fg-3)]">
            Reference
          </div>
          <ul className="mt-3 space-y-2 text-[13px]">
            <li><Link className="text-[var(--color-fg-2)] hover:text-white" href="/docs/enums">Enums</Link></li>
            <li><Link className="text-[var(--color-fg-2)] hover:text-white" href="/docs/classes">Classes</Link></li>
            <li><Link className="text-[var(--color-fg-2)] hover:text-white" href="/docs/errors">Error Codes</Link></li>
            <li><Link className="text-[var(--color-fg-2)] hover:text-white" href="/docs/breaking-changes">Breaking Changes</Link></li>
          </ul>
        </div>

        <div>
          <div className="text-[11px] font-semibold uppercase tracking-wider text-[var(--color-fg-3)]">
            Project
          </div>
          <ul className="mt-3 space-y-2 text-[13px]">
            <li><a className="text-[var(--color-fg-2)] hover:text-white" href={SITE.repo} target="_blank" rel="noreferrer">GitHub Repository</a></li>
            <li><a className="text-[var(--color-fg-2)] hover:text-white" href={SITE.nuget} target="_blank" rel="noreferrer">NuGet Package</a></li>
            <li><span className="text-[var(--color-fg-3)]">License: Free Forever</span></li>
          </ul>
        </div>
      </div>

      <div className="border-t border-[var(--color-border)]">
        <div className="mx-auto flex max-w-[1400px] flex-col items-center justify-between gap-2 px-6 py-5 text-[12px] text-[var(--color-fg-3)] sm:flex-row">
          <span>© {new Date().getFullYear()} Sajjad H. Al-Khafaji. All rights reserved.</span>
          <span>v{SITE.version} · Built for {SITE.domain}</span>
        </div>
      </div>
    </footer>
  );
}
