import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Operator",
  description:
    "Operator enum — the 28 comparison operators DynamicWhere.ex supports, with the value count each one requires.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/enums/operator/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/enums/operator">
      <h1>Operator</h1>
      <p>
        <code>Operator</code> declares the comparison applied by a{" "}
        <Link href="/docs/classes/condition"><code>Condition</code></Link>.
        There are 28 operators total. Every operator that starts with{" "}
        <code>I</code> is the case-insensitive variant — both sides are
        normalized with <code>.ToLower()</code> before comparison. On PostgreSQL
        the six pattern variants can match with <code>ILIKE</code> instead, since
        3.5.0 — see{" "}
        <Link href="/docs/enums/operator#text-matching">case-insensitive matching</Link>.
      </p>

      <h2 id="value-counts">Required values at a glance</h2>
      <p>
        Each operator expects a specific number of entries in{" "}
        <code>Condition.Values</code>. Sending the wrong count throws a{" "}
        <code>LogicException</code> whose message is one of{" "}
        <code>ConditionWithOperator[&lt;op&gt;]MustHasOnlyOneValue</code>,{" "}
        <code>ConditionWithOperator[Between-NotBetween]MustHasOnlyTwoValues</code>,{" "}
        <code>ConditionWithOperator[In-IIn-NotIn-INotIn]MustHasOneOrMoreValues</code>, or{" "}
        <code>ConditionWithOperator[IsNull-IsNotNull]MustHasNoValues</code>. There is
        no error-code property — the string is the message.
      </p>
      <table>
        <thead>
          <tr>
            <th>Value count</th>
            <th>Operators</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><strong>0</strong></td>
            <td>
              <code>IsNull</code>, <code>IsNotNull</code>
            </td>
          </tr>
          <tr>
            <td><strong>1</strong></td>
            <td>
              All equality, contains, starts-with, ends-with, and ordered
              comparisons (20 operators total — the 28 minus the two null
              checks, the two range checks, and the four <code>In</code>{" "}
              variants).
            </td>
          </tr>
          <tr>
            <td><strong>2</strong> (exactly)</td>
            <td>
              <code>Between</code>, <code>NotBetween</code>
            </td>
          </tr>
          <tr>
            <td><strong>1+</strong></td>
            <td>
              <code>In</code>, <code>IIn</code>, <code>NotIn</code>,{" "}
              <code>INotIn</code>
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="equality">Equality</h2>
      <table>
        <thead>
          <tr>
            <th>Operator</th>
            <th>Description</th>
            <th>Required Values</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Equal</code></td>
            <td>Equality (case-sensitive for text).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>IEqual</code></td>
            <td>Equality (case-insensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>NotEqual</code></td>
            <td>Inequality (case-sensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>INotEqual</code></td>
            <td>Inequality (case-insensitive).</td>
            <td>1</td>
          </tr>
        </tbody>
      </table>

      <h2 id="contains">Contains / StartsWith / EndsWith</h2>
      <table>
        <thead>
          <tr>
            <th>Operator</th>
            <th>Description</th>
            <th>Required Values</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Contains</code></td>
            <td>Text contains (case-sensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>IContains</code></td>
            <td>Text contains (case-insensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>NotContains</code></td>
            <td>Text does not contain (case-sensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>INotContains</code></td>
            <td>Text does not contain (case-insensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>StartsWith</code></td>
            <td>Starts with (case-sensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>IStartsWith</code></td>
            <td>Starts with (case-insensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>NotStartsWith</code></td>
            <td>Does not start with (case-sensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>INotStartsWith</code></td>
            <td>Does not start with (case-insensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>EndsWith</code></td>
            <td>Ends with (case-sensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>IEndsWith</code></td>
            <td>Ends with (case-insensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>NotEndsWith</code></td>
            <td>Does not end with (case-sensitive).</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>INotEndsWith</code></td>
            <td>Does not end with (case-insensitive).</td>
            <td>1</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="note">
        Case-insensitive <code>I*</code> operators emit <code>.ToLower()</code>{" "}
        on both sides of the comparison. On SQL Server this is typically free
        (default collations are case-insensitive). On case-sensitive collations
        (e.g. PostgreSQL with <code>C</code> locale) this still works but may
        sidestep an index. On PostgreSQL,{" "}
        <Link href="/docs/enums/operator#text-matching"><code>TextMatching.ILike</code></Link>{" "}
        lets a <code>pg_trgm</code> index serve <code>IContains</code>,{" "}
        <code>IStartsWith</code>, <code>IEndsWith</code> and their negations.
      </Callout>

      <h2 id="in">In / NotIn (set membership)</h2>
      <table>
        <thead>
          <tr>
            <th>Operator</th>
            <th>Description</th>
            <th>Required Values</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>In</code></td>
            <td>Value is in the set (case-sensitive for text).</td>
            <td>1+</td>
          </tr>
          <tr>
            <td><code>IIn</code></td>
            <td>Value is in the set (case-insensitive).</td>
            <td>1+</td>
          </tr>
          <tr>
            <td><code>NotIn</code></td>
            <td>Value is not in the set (case-sensitive).</td>
            <td>1+</td>
          </tr>
          <tr>
            <td><code>INotIn</code></td>
            <td>Value is not in the set (case-insensitive).</td>
            <td>1+</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="note" title="Long lists">
        A list of up to 32 values is written as one chain of comparisons, and a
        longer one as a balanced tree of such chains, which returns the same rows.
        Before 3.1.0 every list was one chain, and a single condition carrying about
        seven hundred values could overflow the request thread&apos;s stack and end
        the process — see{" "}
        <Link href="/docs/breaking-changes#values-are-literals">breaking changes</Link>.
        Under <code>ApplyPolicy</code>,{" "}
        <Link href="/docs/policies/configuration#caps"><code>MaxConditionValues</code></Link>{" "}
        (default 1000) bounds the values one condition may carry.
      </Callout>

      <h2 id="ranges">Ordered comparisons &amp; ranges</h2>
      <table>
        <thead>
          <tr>
            <th>Operator</th>
            <th>Description</th>
            <th>Required Values</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>GreaterThan</code></td>
            <td>Greater than.</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>GreaterThanOrEqual</code></td>
            <td>Greater than or equal.</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>LessThan</code></td>
            <td>Less than.</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>LessThanOrEqual</code></td>
            <td>Less than or equal.</td>
            <td>1</td>
          </tr>
          <tr>
            <td><code>Between</code></td>
            <td>Inclusive range — first value is the lower bound, second is the upper bound.</td>
            <td>2 (exactly)</td>
          </tr>
          <tr>
            <td><code>NotBetween</code></td>
            <td>Outside the inclusive range defined by the two values.</td>
            <td>2 (exactly)</td>
          </tr>
        </tbody>
      </table>

      <h2 id="null">Null checks</h2>
      <table>
        <thead>
          <tr>
            <th>Operator</th>
            <th>Description</th>
            <th>Required Values</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>IsNull</code></td>
            <td>Property is NULL.</td>
            <td>0</td>
          </tr>
          <tr>
            <td><code>IsNotNull</code></td>
            <td>Property is NOT NULL.</td>
            <td>0</td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info" title="On a date member that cannot be null">
        With <code>DataType.Date</code> or <code>DataType.DateTime</code>, the
        library reads the member&apos;s type first. A non-nullable{" "}
        <code>DateTime</code>, <code>DateTimeOffset</code> or{" "}
        <code>DateOnly</code> of the entity itself can never be null, so{" "}
        <code>IsNull</code> answers the constant <code>false</code> — no rows — and{" "}
        <code>IsNotNull</code> the constant <code>true</code> — every row. On
        PostgreSQL that is <code>WHERE FALSE</code> and no predicate at all.
        Reached through a navigation, as in <code>Approval.ApprovedAt</code>, they
        test the navigation instead: <code>IsNull</code> matches the rows with no{" "}
        <code>Approval</code>. On a nullable date member they test the column as
        usual. See{" "}
        <Link href="/docs/breaking-changes#date-member-type">breaking changes</Link>.
      </Callout>

      <Callout tone="warn">
        Sending any value with <code>IsNull</code> / <code>IsNotNull</code>{" "}
        throws <code>ConditionWithOperator[IsNull-IsNotNull]MustHasNoValues</code>. Send an empty array:{" "}
        <code>"values": []</code>.
      </Callout>

      <h2 id="examples">JSON examples</h2>

      <p>Range (<code>Between</code>):</p>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Price",
  "dataType": "Number",
  "operator": "Between",
  "values": [100, 500]
}`}</Code>

      <p>Set membership (<code>IIn</code>):</p>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Country",
  "dataType": "Text",
  "operator": "IIn",
  "values": ["USA", "Canada", "UK"]
}`}</Code>

      <p>Null check (<code>IsNull</code>):</p>
      <Code lang="json">{`{
  "sort": 1,
  "field": "DeletedAt",
  "dataType": "DateTime",
  "operator": "IsNull",
  "values": []
}`}</Code>

      <h2 id="text-matching">Case-insensitive matching on PostgreSQL</h2>
      <p>
        New in <strong>3.5.0</strong>. By default the <code>I*</code> operators
        lower both sides, <code>{`field.ToLower().Contains("value")`}</code>, which
        a relational provider writes as <code>lower(column) LIKE …</code>. A{" "}
        <code>pg_trgm</code> GIN or GiST index on the column cannot serve that,
        and it can serve <code>column ILIKE &apos;%value%&apos;</code>. How the
        case-insensitive operators match is one choice for the whole process,{" "}
        <code>TextMatching</code>, in <code>DynamicWhere.ex.Enums</code>; no
        request carries it.
      </p>
      <table>
        <thead>
          <tr>
            <th>Value</th>
            <th>Meaning</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Lower</code> (0)</td>
            <td>
              Both sides lowered. The default, unchanged, and what every provider
              and LINQ to objects can run.
            </td>
          </tr>
          <tr>
            <td><code>ILike</code> (1)</td>
            <td>
              PostgreSQL&apos;s <code>ILIKE</code>, through Npgsql&apos;s{" "}
              <code>EF.Functions.ILike</code>, for <code>IContains</code>,{" "}
              <code>INotContains</code>, <code>IStartsWith</code>,{" "}
              <code>INotStartsWith</code>, <code>IEndsWith</code> and{" "}
              <code>INotEndsWith</code>.
            </td>
          </tr>
        </tbody>
      </table>
      <p>A deployment on PostgreSQL chooses it once, at startup:</p>
      <Code lang="csharp">{`using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Source;

DwText.Configure(o => o.CaseInsensitive = TextMatching.ILike);

// Or from configuration. A key nothing answers to — "CaseInsensitiv" — refuses to start,
// and so does a single value where the section belongs: "DynamicWhere:Text": "ILike".
DwText.Configure(new DwTextOptions().Bind(configuration.GetSection("DynamicWhere:Text")));`}</Code>
      <Code lang="json">{`{
  "DynamicWhere": {
    "Text": {
      "CaseInsensitive": "ILike"
    }
  }
}`}</Code>
      <p>
        With <code>ILike</code> chosen, a condition on <code>Name</code> with the
        value <code>&quot;ab&quot;</code> compiles on Npgsql to:
      </p>
      <table>
        <thead>
          <tr>
            <th>Operator</th>
            <th>SQL</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>IContains</code></td>
            <td><code>{`i."Name" IS NOT NULL AND i."Name" ILIKE '%ab%' ESCAPE '\\'`}</code></td>
          </tr>
          <tr>
            <td><code>INotContains</code></td>
            <td><code>{`i."Name" IS NOT NULL AND NOT (i."Name" ILIKE '%ab%' ESCAPE '\\')`}</code></td>
          </tr>
          <tr>
            <td><code>IStartsWith</code></td>
            <td><code>{`i."Name" IS NOT NULL AND i."Name" ILIKE 'ab%' ESCAPE '\\'`}</code></td>
          </tr>
          <tr>
            <td><code>INotStartsWith</code></td>
            <td><code>{`i."Name" IS NOT NULL AND NOT (i."Name" ILIKE 'ab%' ESCAPE '\\')`}</code></td>
          </tr>
          <tr>
            <td><code>IEndsWith</code></td>
            <td><code>{`i."Name" IS NOT NULL AND i."Name" ILIKE '%ab' ESCAPE '\\'`}</code></td>
          </tr>
          <tr>
            <td><code>INotEndsWith</code></td>
            <td><code>{`i."Name" IS NOT NULL AND NOT (i."Name" ILIKE '%ab' ESCAPE '\\')`}</code></td>
          </tr>
        </tbody>
      </table>
      <ul>
        <li>
          The predicate is built and parsed exactly as before, and the parsed
          lambda is then rewritten into <code>EF.Functions.ILike</code>, with a
          backslash as the escape character. The value is the one the condition
          sent, trimmed and lowered, with every backslash, <code>%</code> and{" "}
          <code>_</code> in it escaped, so it matches as text and never as a
          pattern. A negation keeps its <code>NOT</code>, and the null guard stays
          in front. <code>getQueryString</code> shows the <code>ILIKE</code> SQL.
        </li>
        <li>
          <code>IEqual</code>, <code>INotEqual</code>, <code>IIn</code> and{" "}
          <code>INotIn</code> stay <code>lower(column) = value</code>, which an
          index on <code>lower(column)</code> serves, and a <code>Having</code>{" "}
          condition stays lowered, since an aggregate uses no index. The
          case-sensitive operators are not affected.
        </li>
        <li>
          It applies wherever the library filters through <code>Where</code>: a{" "}
          <code>Filter</code>, every set of a <code>Segment</code> and a{" "}
          <code>Summary</code>&apos;s row filter, guarded or not. Only the
          predicate the library parsed is rewritten; one the caller composed with
          LINQ beneath it is left as written.
        </li>
        <li>
          Only a query EF Core&apos;s own provider translates is rewritten. LINQ
          to objects, the <code>IEnumerable&lt;T&gt;</code> overloads and a
          provider that wraps EF Core&apos;s, such as LinqKit&apos;s{" "}
          <code>AsExpandable()</code>, keep lowering.
        </li>
        <li>
          Against PostgreSQL 16 it returns the same rows as lowering, and as LINQ
          to objects, for all six operators over ASCII values carrying{" "}
          <code>%</code>, <code>_</code>, backslashes and quotes, through the
          plain, guarded and segment paths.
        </li>
      </ul>
      <Callout tone="warn" title="The choice is the whole process's">
        Every EF Core query in the process is rewritten, whatever database it
        reaches. A process that also queries another database through EF Core —
        SQLite in tests, say — must not choose <code>ILike</code>: such a query
        fails translation with <code>InvalidOperationException</code> rather than
        match wrongly.
      </Callout>
      <table>
        <thead>
          <tr>
            <th>Member (<code>DynamicWhere.ex.Source</code>)</th>
            <th>Description</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>DwText.Configure(Action&lt;DwTextOptions&gt;)</code></td>
            <td>Fills in a fresh <code>DwTextOptions</code> and configures it.</td>
          </tr>
          <tr>
            <td><code>DwText.Configure(DwTextOptions)</code></td>
            <td>
              Checks and freezes the options and sets them for the process. Throws{" "}
              <code>ArgumentNullException</code> for <code>null</code>;{" "}
              <code>ArgumentException</code> for a value <code>TextMatching</code>{" "}
              does not define; <code>InvalidOperationException</code> when{" "}
              <code>ILike</code> is chosen and{" "}
              <code>Npgsql.EntityFrameworkCore.PostgreSQL</code> cannot be loaded —
              its <code>EF.Functions.ILike</code> is found by name, so the package
              references no provider and a deployment without it refuses to start
              — and <code>InvalidOperationException</code> on a later call asking
              for a different choice. A later call asking for the choice in force
              does nothing, so several hosts in one process, such as{" "}
              <code>WebApplicationFactory</code> hosts in one test run, can run the
              same startup. <code>DwDates.Configure</code>, by contrast, refuses
              every second call.
            </td>
          </tr>
          <tr>
            <td><code>DwText.Options</code></td>
            <td>
              The <code>DwTextOptions</code> in force. Frozen;{" "}
              <code>Lower</code> until a deployment configures otherwise.
            </td>
          </tr>
          <tr>
            <td><code>DwText.IsConfigured</code></td>
            <td><code>true</code> once <code>Configure</code> has succeeded.</td>
          </tr>
          <tr>
            <td><code>DwTextOptions.CaseInsensitive</code></td>
            <td>
              <code>TextMatching</code>, <code>Lower</code> by default. Setting it
              once the options are frozen throws{" "}
              <code>InvalidOperationException</code>.
            </td>
          </tr>
          <tr>
            <td><code>DwTextOptions.IsFrozen</code></td>
            <td>
              <code>true</code> once the options have been handed to{" "}
              <code>DwText.Configure</code>.
            </td>
          </tr>
          <tr>
            <td><code>DwTextOptions.Bind(IConfiguration)</code></td>
            <td>
              Extension method. Reads <code>CaseInsensitive</code> from a section
              and returns the same instance; an absent section changes nothing.
              Throws <code>InvalidOperationException</code> for a key nothing
              answers to, for a value it cannot read as a{" "}
              <code>TextMatching</code> such as a misspelt{" "}
              <code>&quot;ILikee&quot;</code>, for a single value where the section
              belongs (<code>&quot;DynamicWhere:Text&quot;: &quot;ILike&quot;</code>),
              or when the options are already frozen.
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/enums/data-type">DataType →</Link> which operators
          each logical type accepts.
        </li>
        <li>
          <Link href="/docs/validation/condition">Condition validation →</Link>{" "}
          the exact messages thrown on count mismatches.
        </li>
        <li>
          <Link href="/docs/errors">Error codes →</Link> full reference.
        </li>
        <li>
          <Link href="/docs/breaking-changes#ilike">Breaking changes →</Link> what
          opting in to <code>ILIKE</code> changes, and for whom.
        </li>
      </ul>
    </DocPage>
  );
}
