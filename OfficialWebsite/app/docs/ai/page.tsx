import fs from "node:fs";
import path from "node:path";
import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";
import CopyBlock from "./CopyBlock";

export const metadata: Metadata = {
  title: "For AI agents — the whole library in one file",
  description:
    "A single plain-text reference covering every shape, enum, method, policy attribute and trap in DynamicWhere.ex. Paste it into Claude, Copilot, Cursor or Codex, or point the agent at doc.dynamicwhere.com/llms.txt.",
  keywords: [
    "llms.txt",
    "AI coding agent reference",
    "DynamicWhere.ex for Copilot",
    "LLM documentation",
    "EF Core library AI context",
  ],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/ai/" },
};

/**
 * Read at build time from the same file the site serves at /llms.txt, so the page
 * and the file cannot drift. There is one copy of this reference, not two.
 */
function readReference(): { text: string; lines: number } {
  const text = fs.readFileSync(path.join(process.cwd(), "public", "llms.txt"), "utf8");

  return { text, lines: text.split("\n").length };
}

export default function Page() {
  const { text, lines } = readReference();

  return (
    <DocPage pathname="/docs/ai">
      <h1>For AI agents</h1>
      <p>
        Most people writing against this library now have an agent open beside
        them. The rest of these docs are written for a human reading one page at a
        time, which is the wrong shape for that: an agent needs the whole surface
        at once, in plain text, with the exact spellings.
      </p>
      <p>
        So there is one file. It covers every shape, every enum member, every
        extension method, the full policy layer, and the traps that produce code
        which compiles and is quietly wrong.
      </p>

      <h2 id="use">How to use it</h2>
      <p>Either hand it over, or let the agent fetch it.</p>
      <Code lang="text">{`Read https://doc.dynamicwhere.com/llms.txt before writing any
DynamicWhere.ex code. It is the complete API surface.`}</Code>
      <p>
        That works with any agent that can read a URL. If yours cannot, use the
        copy button below and paste the file into your context.
      </p>

      <Callout tone="note" title="Why plain text rather than a nicer page">
        What an agent consumes is the text. Syntax highlighting, cards and
        collapsible sections cost context and carry no meaning once the markup is
        stripped. <code>llms.txt</code> is also the path agents and crawlers
        already look for, so pointing at it needs no explanation.
      </Callout>

      <h2 id="contents">What is in it</h2>
      <ul>
        <li>
          <strong>Shapes.</strong> Exact field names for{" "}
          <code>Condition</code>, <code>ConditionGroup</code>,{" "}
          <code>ConditionSet</code>, <code>OrderBy</code>, <code>GroupBy</code>,{" "}
          <code>AggregateBy</code>, <code>PageBy</code>, <code>Filter</code>,{" "}
          <code>Segment</code>, <code>Summary</code> and the three result types.
        </li>
        <li>
          <strong>Enums, verbatim.</strong> Every member of every enum, including
          the case-insensitive <code>I</code> variants and the one-<code>m</code>{" "}
          spelling of <code>Sumation</code> — the two things a model guesses wrong
          most often.
        </li>
        <li>
          <strong>All seventeen extension methods</strong> with their real
          signatures, and which ones have no synchronous form.
        </li>
        <li>
          <strong>The policy layer.</strong> All eighteen attributes with their
          parameters, the six precedence levels, blocked-action semantics per
          tier, the transform chain order, the caps and their defaults, the
          configuration section, the admin endpoints and all twenty-two policy
          error codes.
        </li>
        <li>
          <strong>Ten traps</strong> that produce silently wrong code, each with
          the reason. A mask without <code>[DwNoOrder]</code> leaking through
          sorting is the one an agent reproduces most often, because the attribute
          reads as sufficient on its own.
        </li>
        <li>
          <strong>Worked examples</strong> for a filter, a summary, a segment and a
          fully protected entity.
        </li>
      </ul>

      <h2 id="file">The file</h2>
      <p>
        This is the exact content served at{" "}
        <a href="/llms.txt" target="_blank" rel="noreferrer">
          /llms.txt
        </a>
        . The page reads it at build time, so the two are never out of step.
      </p>

      <CopyBlock text={text} lines={lines} />

      <Callout tone="warn" title="It says what the library does, not what it should do">
        The reference is generated against version 3.0.0 and states behaviour,
        including the parts that are deliberately blunt — that neither hashing nor
        tokenization hides equality, for instance. If an agent proposes a design
        this file says is unsafe, the file is the one to trust. For the reasoning
        behind any rule, the human pages carry it: start at{" "}
        <Link href="/docs/policies/use-cases">Use cases</Link> or{" "}
        <Link href="/docs/policies/security">Security</Link>.
      </Callout>
    </DocPage>
  );
}
