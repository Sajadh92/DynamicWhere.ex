import { ReactNode } from "react";

type Tone = "info" | "warn" | "success" | "danger" | "note";

const PALETTE: Record<Tone, { bd: string; bg: string; fg: string; label: string }> = {
  info:    { bd: "rgba(56,189,248,0.45)",  bg: "rgba(56,189,248,0.08)",  fg: "#7dd3fc", label: "Note" },
  note:    { bd: "rgba(139,92,246,0.45)",  bg: "rgba(139,92,246,0.08)",  fg: "#c4b5fd", label: "Note" },
  warn:    { bd: "rgba(245,158,11,0.45)",  bg: "rgba(245,158,11,0.08)",  fg: "#fcd34d", label: "Warning" },
  success: { bd: "rgba(34,197,94,0.45)",   bg: "rgba(34,197,94,0.08)",   fg: "#86efac", label: "Tip" },
  danger:  { bd: "rgba(239,68,68,0.45)",   bg: "rgba(239,68,68,0.08)",   fg: "#fca5a5", label: "Breaking" },
};

export default function Callout({
  tone = "note",
  title,
  children,
}: {
  tone?: Tone;
  title?: string;
  children: ReactNode;
}) {
  const c = PALETTE[tone];
  return (
    <div
      className="my-5 rounded-lg border px-4 py-3"
      style={{ borderColor: c.bd, background: c.bg }}
    >
      <div
        className="mb-1 text-[11px] font-semibold uppercase tracking-wider"
        style={{ color: c.fg }}
      >
        {title ?? c.label}
      </div>
      <div className="text-[14.5px] text-[var(--color-fg-2)] leading-relaxed">{children}</div>
    </div>
  );
}
