import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Policy Precedence — six levels, and why attributes are sealed",
  description: "How DynamicWhere.ex decides one field policy from many sources: six precedence levels, tie-breaking by specificity, priority and effect, and what is intersected rather than elected.",
  keywords: ["policy precedence", "sealed attribute", "authorization precedence .NET", "rule conflict resolution"],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/policies/precedence/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/policies/precedence">
      <h1>Precedence</h1>
      <p>
        A field can be spoken about by an attribute, a rule aimed at the user, a
        rule aimed at one of their roles, a tenant rule and a global rule, all at
        once. Precedence decides which one wins.
      </p>

      <h2 id="levels">Six levels</h2>
      <p>Lowest number wins.</p>
      <table>
        <thead><tr><th>Level</th><th>Source</th><th>Notes</th></tr></thead>
        <tbody>
          <tr><td>1</td><td><code>SealedAttribute</code></td><td>The default for every attribute. Absolute: no runtime rule can lift it.</td></tr>
          <tr><td>2</td><td><code>DynamicUser</code></td><td>A rule aimed at one user.</td></tr>
          <tr><td>3</td><td><code>DynamicRole</code></td><td>A rule aimed at a role.</td></tr>
          <tr><td>4</td><td><code>DynamicTenant</code></td><td>A rule aimed at a tenant.</td></tr>
          <tr><td>5</td><td><code>DynamicGlobal</code></td><td>A rule aimed at everyone.</td></tr>
          <tr><td>6</td><td><code>OverridableAttribute</code></td><td>An attribute written with <code>Overridable = true</code>.</td></tr>
        </tbody>
      </table>
      <Callout tone="note" title="Sealed by default is the whole design">
        An attribute is a statement in the source code. If a runtime rule could
        lift it, the source would stop being the ceiling and an operator with
        write access to the rule store would have write access to your security
        model. Opting out is one keyword; opting in should never be silent.
      </Callout>

      <h2 id="ties">Breaking a tie</h2>
      <p>Four passes, in order:</p>
      <ol>
        <li><strong>Level</strong> — the table above.</li>
        <li><strong>Specificity</strong> — an exact field path beats a <code>*</code> wildcard within the same level.</li>
        <li><strong>Priority</strong> — the rule <code>Priority</code> column, higher first.</li>
        <li><strong>Effect</strong> — the strictest survives: <code>Deny</code> beats <code>Mask</code> beats <code>Allow</code>.</li>
      </ol>
      <p>
        When two sources tie on all four, the decided effect is still fully
        deterministic. Only which of several equally-winning sources gets named
        by <Link href="/docs/policies/admin">explain</Link> is arbitrary.
      </p>

      <h2 id="not-elected">What is not elected</h2>
      <p>Three things combine instead of competing, and each for a reason.</p>
      <table>
        <thead><tr><th>Carrier</th><th>How it combines</th><th>Why not elect</th></tr></thead>
        <tbody>
          <tr>
            <td>Forced predicates</td><td>Collected and ANDed</td>
            <td>A conjunction can only narrow. Electing one would let a low-authority rule silently discard a sealed tenant scope.</td>
          </tr>
          <tr>
            <td>Operator restrictions</td><td>Intersected</td>
            <td>Same reason: the result of combining two restrictions must be at least as tight as either.</td>
          </tr>
          <tr>
            <td>Transform stages</td><td>Elected <em>per stage</em></td>
            <td>A rule can add a truncation on top of a sealed mask; electing the whole chain would let it discard the mask by supplying anything at all.</td>
          </tr>
        </tbody>
      </table>

      <h2 id="carriers">A fragment that carries something decides nothing</h2>
      <p>
        An alias, a required-filter demand, a forced predicate and a cost weight
        all attach to a field without saying whether it may be read. They are
        recorded against <code>PolicyFeature.None</code> so they cannot win an
        election they never entered — otherwise decorating a field with{" "}
        <code>[DwAlias]</code> would make it undeniable, because a sealed
        allowance outranks every runtime denial.
      </p>

      <h2 id="mask-vs-deny">Masked and denied at once</h2>
      <p>
        A field that is both denied and masked is <strong>dropped</strong>, not
        masked. <code>Deny</code> is the stronger effect and wins the election;
        the mask never runs.
      </p>
      <Code lang="csharp">{`// Salary stays filterable and sortable: Allows() refuses only a denial,
// so a Mask effect on Select does not remove the field from a WHERE clause.
[DwMask(MaskStrategy.Full)]
public decimal Salary { get; set; }`}</Code>
    </DocPage>
  );
}
