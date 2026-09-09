import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Transforms & Masking — eight strategies and five other transforms",
  description: "How DynamicWhere.ex changes values on the way out: eight mask strategies, generalization, mutation, defaults, truncation and formatting, applied in memory after materialization.",
  keywords: ["data masking .NET", "MaskStrategy", "EF Core data masking", "generalization k-anonymity", "PII masking"],
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/policies/transforms/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/policies/transforms">
      <h1>Transforms &amp; Masking</h1>
      <p>
        Transformation happens <strong>in memory, after materialization</strong>
        — never in SQL. Filtering and sorting still run against the real values
        in the database; what reaches the caller is changed on the way out.
      </p>

      <Callout tone="warn" title="Why that matters more than it sounds">
        Because the real value is what the database sorts and aggregates, a
        masked field that is still sortable or aggregatable leaks. Both have
        controls, and both are on <Link href="/docs/policies/security">Security</Link>.
      </Callout>

      <h2 id="detach">Detached, then transformed</h2>
      <p>
        Entities returned from a guarded query are detached before anything is
        changed. If they were tracked, the masked value would be a pending
        modification and the next unrelated <code>SaveChanges</code> on the same
        context would write asterisks over the real data — silently, with no
        exception and no way back without a restore.
      </p>
      <p>
        A transform on a query the caller materializes itself is refused with{" "}
        <code>TransformRequiresMaterialization</code> rather than skipped, so a
        composable method cannot hand back an <code>IQueryable</code> that quietly
        never masks anything.
      </p>

      <h2 id="mask">The eight mask strategies</h2>
      <table>
        <thead><tr><th>Strategy</th><th>Result</th></tr></thead>
        <tbody>
          <tr><td><code>Full</code></td><td>Every character replaced.</td></tr>
          <tr><td><code>Partial</code></td><td>Keeps <code>KeepStart</code> and <code>KeepEnd</code> characters.</td></tr>
          <tr><td><code>Email</code></td><td>Masks the local part and the domain, keeps the shape.</td></tr>
          <tr><td><code>Phone</code></td><td>Keeps the last group of digits.</td></tr>
          <tr><td><code>Regex</code></td><td><code>Pattern</code> and <code>Replacement</code>.</td></tr>
          <tr><td><code>Fixed</code></td><td>A constant string from <code>Text</code>.</td></tr>
          <tr><td><code>Hash</code></td><td>Salted hash. Needs <code>options.HashSalt</code>.</td></tr>
          <tr><td><code>Null</code></td><td>Removes the value. Refused at startup on a non-nullable value type.</td></tr>
        </tbody>
      </table>
      <p>
        <code>MaskStrategy.Tokenize</code> is deferred to a later release.
      </p>
      <Code lang="csharp">{`[DwMask(MaskStrategy.Partial, KeepEnd = 4)]
public string CardNumber { get; set; }        // ************4242

[DwMask(MaskStrategy.Email)]
public string Email { get; set; }             // s*************@c******.com

[DwMask(MaskStrategy.Hash)]
public string NationalId { get; set; }        // stable per salt, useful for joins`}</Code>

      <h2 id="others">The other five</h2>
      <table>
        <thead><tr><th>Attribute</th><th>What it does</th></tr></thead>
        <tbody>
          <tr><td><code>[DwGeneralize(mode)]</code></td><td><code>Round</code> (to <code>Step</code>), <code>Bucket</code> (a band label), <code>DatePart</code> (<code>Year</code>, <code>Quarter</code>, <code>Month</code>, <code>Day</code>), <code>Truncate</code> (drop decimals).</td></tr>
          <tr><td><code>[DwMutate(typeof(T))]</code></td><td>Your own <code>IValueTransformer</code>. Resolved from <code>options.Services</code>; the type is checked by the startup scan rather than on the first query.</td></tr>
          <tr><td><code>[DwDefault]</code></td><td>The type default, or a constant that must be readable as the member type.</td></tr>
          <tr><td><code>[DwTruncate(n)]</code></td><td>Shorten text, with an optional <code>Ellipsis</code>.</td></tr>
          <tr><td><code>[DwFormat("fmt")]</code></td><td>A standard or custom .NET format string.</td></tr>
        </tbody>
      </table>

      <Callout tone="note" title="Generalization is the half masking cannot reach">
        Arithmetic runs in <code>decimal</code> and converts back to the member
        own type, so rounding an <code>int</code> yields an <code>int</code>.{" "}
        <code>Bucket</code> is the one mode that emits text, because a band is a
        label rather than a number — so it is valid only on a string member, and
        the startup scan enforces that.
      </Callout>

      <h2 id="types">What can be applied to what</h2>
      <p>
        Masking, truncation and formatting all emit text, so they cannot be
        assigned to a <code>decimal</code> or a <code>DateTime</code>. Startup
        validation reports this as an error and names the fix.
      </p>
      <Code lang="text">{`Employee.Salary: [DwMask] emits text, which cannot be assigned to Decimal.
Use [DwGeneralize] to reduce a number or a date while keeping its type, or
project into a type whose member is a string.`}</Code>

      <h2 id="chain">Chaining</h2>
      <p>
        Several transforms on one member compose into a chain, elected one stage
        at a time. A runtime rule can add a stage on top of a sealed one and
        cannot replace it.
      </p>
      <Code lang="csharp">{`[DwGeneralize(GeneralizeMode.Round, Step = 1000)]
[DwFormat("C0")]
public string SalaryBand { get; set; } = string.Empty;`}</Code>
      <p>
        The member is a <code>string</code> because <code>[DwFormat]</code> ends
        the chain in text, and startup validation refuses a chain that emits text
        into a member that cannot hold it. Reduce a number while keeping its type
        with <code>[DwGeneralize]</code> alone.
      </p>

      <h2 id="graph">Through the graph</h2>
      <p>
        The walk descends through reference navigations, collections, arrays,
        interfaces, structs and jagged collections, transforming every element it
        reaches. A field one navigation deeper than the walk reaches is a field
        that is quietly not protected, which is why the depth is a{" "}
        <Link href="/docs/policies/configuration">configured cap</Link> rather
        than a guess.
      </p>
    </DocPage>
  );
}
