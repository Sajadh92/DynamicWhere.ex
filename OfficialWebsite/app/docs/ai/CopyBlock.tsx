"use client";

import { useState } from "react";

export default function CopyBlock({ text, lines }: { text: string; lines: number }) {
  const [copied, setCopied] = useState(false);

  async function copy() {
    try {
      await navigator.clipboard.writeText(text);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      // Clipboard access can be refused outright — in an insecure context, or by
      // permission. Say so rather than flashing "Copied" over a clipboard that
      // never changed; the raw file link below the block still works.
      setCopied(false);
      window.open("/llms.txt", "_blank", "noopener");
    }
  }

  return (
    <div className="not-prose my-6 overflow-hidden rounded-xl border border-[var(--color-border)] bg-[var(--color-panel)]">
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-[var(--color-border)] bg-[var(--color-bg-2)] px-4 py-2.5">
        <span className="font-mono text-[12px] text-[var(--color-fg-3)]">
          llms.txt · {lines.toLocaleString()} lines
        </span>

        <div className="flex items-center gap-2">
          <a
            href="/llms.txt"
            target="_blank"
            rel="noreferrer"
            className="rounded-md border border-[var(--color-border)] px-3 py-1.5 text-[12px] text-[var(--color-fg-2)] transition hover:border-[var(--color-border-2)] hover:text-white"
          >
            Open raw
          </a>
          <button
            type="button"
            onClick={copy}
            aria-live="polite"
            className="rounded-md border border-[var(--color-accent)] bg-[var(--color-accent-soft)] px-3 py-1.5 text-[12px] font-medium text-[var(--color-accent)] transition hover:bg-[var(--color-accent)] hover:text-white"
          >
            {copied ? "Copied" : "Copy all"}
          </button>
        </div>
      </div>

      <pre className="max-h-[70vh] overflow-auto px-4 py-4 text-[12.5px] leading-relaxed text-[var(--color-fg-2)]">
        {text}
      </pre>
    </div>
  );
}
