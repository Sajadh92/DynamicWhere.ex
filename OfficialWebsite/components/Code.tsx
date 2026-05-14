"use client";

import { useState } from "react";

export function Code({
  children,
  lang = "text",
}: {
  children: string;
  lang?: string;
}) {
  const [copied, setCopied] = useState(false);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(children);
      setCopied(true);
      setTimeout(() => setCopied(false), 1200);
    } catch {}
  };

  return (
    <div className="code-wrap code-block min-w-0">
      <pre>
        <code className={`language-${lang}`}>{children}</code>
      </pre>
      <button onClick={copy} className="code-copy" type="button">
        {copied ? "Copied" : "Copy"}
      </button>
    </div>
  );
}
