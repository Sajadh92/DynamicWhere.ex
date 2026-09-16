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
    "A single plain-text reference covering every shape, enum, method, error string, policy attribute, cache setting and trap in DynamicWhere.ex, plus the JSON on the wire. Paste it into Claude, Copilot, Cursor or Codex, or point the agent at doc.dynamicwhere.com/llms.txt.",
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
        So there is one file, in thirty-five sections. It carries every public type
        and member of the four packages, the behaviour behind them, the JSON a
        client sends and receives, every error string, and the traps that produce
        code which compiles and is quietly wrong. An agent that reads it needs no
        other page here.
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
          <strong>Shapes and results.</strong> Every property of{" "}
          <code>Condition</code>, <code>ConditionGroup</code>,{" "}
          <code>ConditionSet</code>, <code>OrderBy</code>, <code>GroupBy</code>,{" "}
          <code>AggregateBy</code>, <code>PageBy</code>, <code>Filter</code>,{" "}
          <code>Segment</code> and <code>Summary</code> with its type and default,
          and what each member of the three result types holds.
        </li>
        <li>
          <strong>Enums, verbatim, with their numbers</strong> — the numbers a JSON
          body must send when the host registers no string enum converter —
          including the case-insensitive <code>I</code> variants and the one-
          <code>m</code> spelling of <code>Sumation</code>.
        </li>
        <li>
          <strong>All twenty-one extension methods</strong> with their real
          signatures, what each one validates, and which have no synchronous or
          in-memory form. Plus the generated predicate for every operator, value
          coercion per <code>DataType</code>, and how field paths resolve.
        </li>
        <li>
          <strong>The JSON on the wire.</strong> Which body binds to which shape,
          casing and enum converters, how <code>values</code> must be typed, the
          result envelope, what rows look like per method, and copy-paste recipes.
        </li>
        <li>
          <strong>Validation and errors.</strong> Every rule in the order it is
          checked, all twenty-seven error strings with what raises them, and the
          other exception types a caller can receive.
        </li>
        <li>
          <strong>The policy layer.</strong> All twenty-two attributes with their
          parameters, the six precedence levels, enforcement tier by tier, the
          transform chain with the exact output of every mask and generalize mode,
          the group floor, dynamic rules and stores, the admin API, and all
          twenty-two policy error codes.
        </li>
        <li>
          <strong>The reflection cache.</strong> Every <code>CacheExpose</code>{" "}
          member, the options and their ranges, the presets, and what eviction
          actually does.
        </li>
        <li>
          <strong>Forty-eight traps</strong> that produce silently wrong code:
          sixteen for the query engine, thirty-two for policies. A mask without{" "}
          <code>[DwNoOrder]</code> leaking through sorting is the one an agent
          reproduces most often, because the attribute reads as sufficient on its
          own.
        </li>
        <li>
          <strong>Worked examples</strong> for a filter, a summary, a segment, an
          endpoint, a fully protected entity and the policy wiring around it.
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
        The reference is generated against version 3.0.0 from the source, and
        checked by running the library, so it states behaviour — including the
        parts that are deliberately blunt, such as neither hashing nor
        tokenization hiding equality. Where a page in these docs disagrees with it,
        the file is the one to trust. For the reasoning behind a rule, the human
        pages carry it: start at{" "}
        <Link href="/docs/policies/use-cases">Use cases</Link> or{" "}
        <Link href="/docs/policies/security">Security</Link>.
      </Callout>
    </DocPage>
  );
}
