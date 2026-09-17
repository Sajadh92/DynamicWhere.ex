import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "DataType",
  description:
    "DataType enum — the logical type of a condition value. Drives parsing, coercion, and the set of legal operators per type.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/enums/data-type/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/enums/data-type">
      <h1>DataType</h1>
      <p>
        <code>DataType</code> declares the <em>logical</em> type of a{" "}
        <Link href="/docs/classes/condition"><code>Condition</code></Link>'s
        values. The library uses it to pick the right comparison expression,
        parse incoming JSON values, and validate that the chosen{" "}
        <Link href="/docs/enums/operator"><code>Operator</code></Link> is legal
        for that type.
      </p>

      <h2 id="values">Values</h2>
      <p>
        Seven logical types. Each row lists every operator the library will
        accept when paired with that type. An unsupported pairing is not caught
        by validation: the predicate builder throws a <code>LogicException</code>{" "}
        reading{" "}
        <code>
          Unsupported combination of DataType &apos;&lt;type&gt;&apos; and Operator
          &apos;&lt;op&gt;&apos;.
        </code>
      </p>

      <table>
        <thead>
          <tr>
            <th>Value</th>
            <th>Description</th>
            <th>Supported Operators</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Text</code></td>
            <td>String data.</td>
            <td>
              All text operators including the case-insensitive{" "}
              <code>I*</code> variants (<code>IEqual</code>,{" "}
              <code>IContains</code>, <code>IStartsWith</code>,{" "}
              <code>IEndsWith</code>, <code>IIn</code>, etc.),{" "}
              <code>In</code> / <code>NotIn</code>, <code>IsNull</code> /{" "}
              <code>IsNotNull</code>.
            </td>
          </tr>
          <tr>
            <td><code>Guid</code></td>
            <td>GUID stored as a string.</td>
            <td>
              <code>Equal</code>, <code>NotEqual</code>, <code>In</code>,{" "}
              <code>NotIn</code>, <code>IsNull</code>, <code>IsNotNull</code>.
            </td>
          </tr>
          <tr>
            <td><code>Number</code></td>
            <td>
              Any numeric value — <code>byte</code> through <code>decimal</code>{" "}
              (including <code>short</code>, <code>int</code>,{" "}
              <code>long</code>, <code>float</code>, <code>double</code>).
            </td>
            <td>
              <code>Equal</code>, <code>NotEqual</code>,{" "}
              <code>GreaterThan</code>, <code>GreaterThanOrEqual</code>,{" "}
              <code>LessThan</code>, <code>LessThanOrEqual</code>,{" "}
              <code>Between</code>, <code>NotBetween</code>, <code>In</code>,{" "}
              <code>NotIn</code>, <code>IsNull</code>, <code>IsNotNull</code>.
            </td>
          </tr>
          <tr>
            <td><code>Boolean</code></td>
            <td>
              <code>true</code> or <code>false</code>.
            </td>
            <td>
              <code>Equal</code>, <code>NotEqual</code>, <code>IsNull</code>,{" "}
              <code>IsNotNull</code>.
            </td>
          </tr>
          <tr>
            <td><code>DateTime</code></td>
            <td>
              Full timestamp (date + time), compared as the member&apos;s own type
              — <code>DateTime</code>, <code>DateTimeOffset</code> or{" "}
              <code>DateOnly</code>, nullable or not. Values must be in one of the{" "}
              <a href="#date-formats">date formats</a> below.
            </td>
            <td>
              <code>Equal</code>, <code>NotEqual</code>,{" "}
              <code>GreaterThan</code>, <code>GreaterThanOrEqual</code>,{" "}
              <code>LessThan</code>, <code>LessThanOrEqual</code>,{" "}
              <code>Between</code>, <code>NotBetween</code>,{" "}
              <code>IsNull</code>, <code>IsNotNull</code>.
            </td>
          </tr>
          <tr>
            <td><code>Date</code></td>
            <td>
              Date-only — compared via the <code>.Date</code> part of the
              underlying property (<code>.Value.Date</code> on a nullable one),
              so the time component is ignored. A <code>DateOnly</code> member is
              already a day and is compared as it is.
            </td>
            <td>
              Same as <code>DateTime</code> (the comparison strips the time
              component on both sides).
            </td>
          </tr>
          <tr>
            <td><code>Enum</code></td>
            <td>
              An enum member, matched by name (any case) or by number. The column
              may store either.
            </td>
            <td>
              <code>Equal</code>, <code>NotEqual</code>, <code>In</code>,{" "}
              <code>NotIn</code>, <code>IsNull</code>, <code>IsNotNull</code>. The
              string operators pass validation but throw{" "}
              <code>ParseException</code> on an enum-typed member.
            </td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn" title="How the two date types build their predicate">
        <code>DateTime</code> and <code>Date</code> read the member&apos;s CLR type
        before building the predicate. The null guard is emitted only where the
        value can be null, so on a non-nullable member of the entity itself{" "}
        <code>IsNull</code> answers <code>false</code> and <code>IsNotNull</code>{" "}
        answers <code>true</code>. On a nullable one the guard wraps the whole
        comparison, so a null row fails <code>NotEqual</code> and{" "}
        <code>NotBetween</code> too. Reached through a navigation —{" "}
        <code>Approval.ApprovedAt</code> — the navigation is guarded instead, and{" "}
        <code>IsNull</code> / <code>IsNotNull</code> ask whether it is there: the
        provider reads the member of a missing approval as NULL. A{" "}
        <code>DateTimeOffset</code> member is compared against a{" "}
        <code>DateTimeOffset</code> literal and a <code>DateTime</code> member
        against a <code>DateTime</code> literal. A <code>DateOnly</code> member is
        compared against a <code>DateOnly</code> — as a day under both data types —
        so on PostgreSQL an <code>Equal</code> becomes{" "}
        <code>{`WHERE "Day" = DATE '2026-09-01'`}</code>. Before 3.1.0 every
        comparison on a <code>DateTimeOffset</code> member threw, so did{" "}
        <code>Date</code> on any nullable date member, and no comparison on a{" "}
        <code>DateOnly</code> member worked — see{" "}
        <Link href="/docs/breaking-changes#date-member-type">breaking changes</Link>.
      </Callout>

      <Callout tone="warn" title="Enum storage does not matter; the operator list does">
        <code>DataType.Enum</code> matches a member by name or by number, and EF
        Core translates it for an integer column as readily as for a string one.
        What the type decides is which operators work: <code>Contains</code>,{" "}
        <code>StartsWith</code>, <code>EndsWith</code> and their negations are
        accepted by validation and then throw <code>ParseException</code>{" "}
        (&ldquo;No applicable method &apos;Contains&apos; exists in type&rdquo;)
        against an enum-typed member, whatever the storage. For a{" "}
        <code>string</code> column that merely holds enum names and needs those
        operators, use <code>DataType.Text</code>.
      </Callout>

      <h2 id="date-formats">Date formats</h2>
      <p>
        A <code>Date</code> or <code>DateTime</code> value is read against an
        explicit list of formats, never the lenient .NET parser — at validation
        and in the predicate builder alike, as the member&apos;s own{" "}
        <code>DateTime</code>, <code>DateTimeOffset</code> or{" "}
        <code>DateOnly</code>. The host&apos;s culture and calendar play no part,
        so a value is accepted or refused the same way on every server. The one
        exception is a declared format that writes its zone as a quoted literal,
        described under{" "}
        <a href="#declaring-date-formats">Declaring a local format</a>. Every
        deployment accepts these forms:
      </p>
      <table>
        <thead>
          <tr>
            <th>Form</th>
            <th>Examples</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>ISO&nbsp;8601 date</td>
            <td>
              <code>2026-09-01</code> — the month and day may take one or two
              digits, so <code>2026-9-1</code> too
            </td>
          </tr>
          <tr>
            <td>… with a time</td>
            <td>
              After a <code>T</code> or a space: <code>2026-09-01T12:30</code>,{" "}
              <code>2026-09-01 12:30:15</code>,{" "}
              <code>2026-09-01T12:30:15.123</code>
            </td>
          </tr>
          <tr>
            <td>… with a zone</td>
            <td>
              <code>Z</code> or an offset after the time:{" "}
              <code>2026-09-01T12:30:00Z</code>,{" "}
              <code>2026-09-01T12:30:00+03:00</code>, <code>+0300</code>,{" "}
              <code>+03</code>
            </td>
          </tr>
          <tr>
            <td>… as other systems write it</td>
            <td>
              A lowercase <code>t</code> or <code>z</code>, a comma before the
              fraction, and a fraction of more than seven digits — Go and Java
              write nine — which is cut to the seven a <code>DateTime</code> holds
            </td>
          </tr>
          <tr>
            <td>Year-first, with <code>/</code> or <code>.</code></td>
            <td>
              <code>2026/09/01</code>, <code>2026.09.01</code>, with the same
              optional time and zone
            </td>
          </tr>
        </tbody>
      </table>
      <p>
        A C# <code>DateTime</code>, <code>DateTimeOffset</code> or{" "}
        <code>DateOnly</code> placed in <code>Values</code> is written in one of
        these forms (see <a href="#value-coercion">Value coercion</a>), so a C#
        caller is never refused for sending one. Anything else is refused, with
        one of two codes:
      </p>
      <table>
        <thead>
          <tr>
            <th>Value</th>
            <th>Error Code</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td>
              A numeric date that leads with a day or a month —{" "}
              <code>01/09/2026</code>, <code>15/09/2026</code>,{" "}
              <code>09/15/2026</code>, <code>01.09.2026</code>,{" "}
              <code>01-09-2026</code>, <code>1/9/26</code> — with or without a time
            </td>
            <td><code>AmbiguousDateFormat</code></td>
          </tr>
          <tr>
            <td>
              Anything else, including what the lenient parser used to accept
              silently: <code>12:00</code> (today at noon), <code>1/9</code> (a day
              of the current year), <code>Sep 2026</code>,{" "}
              <code>1 September 2026</code>
            </td>
            <td><code>InvalidFormat</code></td>
          </tr>
        </tbody>
      </table>

      <Callout tone="danger" title="A day-first or month-first date is refused by its shape">
        <code>AmbiguousDateFormat</code> does not look at the numbers.{" "}
        <code>&quot;15/09/2026&quot;</code> has only one valid reading and is
        refused all the same, on purpose: refusing only the values with two
        readings would fail on the 5th of the month and pass on the 15th, so a
        client would find out in production instead of on its first request. The
        field — or the <code>Having</code> alias — is on{" "}
        <code>LogicException.Subject</code>, as it is for an{" "}
        <code>InvalidFormat</code> raised by a date value. An unguarded query
        names the field by its canonical path. Under <code>ApplyPolicy</code> it is
        named as the caller wrote it, even under a <code>[DwAlias]</code>. Send
        ISO&nbsp;8601 (
        <code>&quot;2026-09-15&quot;</code>,{" "}
        <code>&quot;2026-09-15T12:00:00Z&quot;</code>), or declare the form your
        clients send — see{" "}
        <Link href="/docs/breaking-changes#date-value-formats">breaking changes</Link>.
      </Callout>

      <h3 id="declaring-date-formats">Declaring a local format</h3>
      <p>
        A deployment whose clients send a local form declares it once, at
        startup. With <code>dd/MM/yyyy</code> declared,{" "}
        <code>&quot;01/09/2026&quot;</code> is 1 September on every server.
      </p>
      <Code lang="csharp">{`using DynamicWhere.ex.Source;

// A day-first API.
DwDates.Configure(o => o.Formats.Add("dd/MM/yyyy"));

// Or from configuration. A key nothing answers to — "Fromats" — refuses to start,
// and so does a single value where the list belongs: "Formats": "dd/MM/yyyy".
DwDates.Configure(new DwDateOptions().Bind(configuration.GetSection("DynamicWhere:Dates")));`}</Code>
      <Code lang="json">{`{
  "DynamicWhere": {
    "Dates": {
      "Formats": [ "dd/MM/yyyy" ]
    }
  }
}`}</Code>
      <ul>
        <li>
          A format is a .NET exact format string, read with the invariant culture.
          ISO&nbsp;8601 and year-first dates stay accepted alongside it.
        </li>
        <li>
          Exact means exact: <code>dd/MM/yyyy</code> reads neither{" "}
          <code>1/9/2026</code> nor <code>01/09/2026 12:30</code>. Declare{" "}
          <code>d/M/yyyy</code> or <code>dd/MM/yyyy HH:mm</code> beside it if
          clients send those. A value no declared format reads is refused as
          before, so <code>09/15/2026</code> sent to a <code>dd/MM/yyyy</code>{" "}
          deployment is still <code>AmbiguousDateFormat</code>.
        </li>
        <li>
          Two formats that read one text as different dates —{" "}
          <code>dd/MM/yyyy</code> beside <code>MM/dd/yyyy</code>, or a format that
          contradicts ISO&nbsp;8601 such as <code>yyyy-dd-MM</code> — are refused
          when configured, with <code>ArgumentException</code>, rather than left to
          disagree on a request. So is a blank format.
        </li>
        <li>
          So is a format with no year — <code>dd/MM</code>, <code>HH:mm</code>,{" "}
          <code>t</code>. The parser completes a missing year from the clock, and a
          missing date from today, so the same value would name a different date
          depending on when the query ran. A format with a year but no day, such
          as <code>yyyy-MM</code>, is accepted and reads the 1st.
        </li>
        <li>
          So are a malformed format; a format that reads part of what it writes
          back differently — <code>dd/MM/yyyy hh:mm</code>, a 12-hour clock with no{" "}
          <code>tt</code>, reads 4 PM as 4 AM; a format with a day but no month
          — <code>dd/mm/yyyy</code>, where <code>mm</code> is minutes; and two
          formats that put the day and the month in opposite orders even in
          different shapes, since <code>dd/MM/yyyy HH:mm</code> beside{" "}
          <code>MM/dd/yyyy</code> would read <code>01/09/2026 00:00</code> as 1
          September and <code>01/09/2026</code> as 9 January.
        </li>
        <li>
          A declared format that writes its zone as a quoted literal, such as{" "}
          <code>yyyy-MM-dd&apos;T&apos;HH:mm:ss&apos;Z&apos;</code>, is not caught
          when formats are checked at configuration. ISO&nbsp;8601 reads the same
          text as a zoned instant, and on a <code>DateTime</code> member converts it
          to the host&apos;s local time. The declared format reads the digits as
          written. On a host that is not on UTC the two readings differ, and the
          value is refused with <code>AmbiguousDateFormat</code>. Declare no format
          that ISO&nbsp;8601 already reads.
        </li>
        <li>
          The formats are process-wide and every query reads them without a lock,
          so they are set once: a second <code>DwDates.Configure</code> call throws{" "}
          <code>InvalidOperationException</code>.
        </li>
      </ul>
      <table>
        <thead>
          <tr>
            <th>Member (<code>DynamicWhere.ex.Source</code>)</th>
            <th>Description</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>DwDates.Configure(Action&lt;DwDateOptions&gt;)</code></td>
            <td>Fills in a fresh <code>DwDateOptions</code> and configures it.</td>
          </tr>
          <tr>
            <td><code>DwDates.Configure(DwDateOptions)</code></td>
            <td>
              Checks and freezes the options and sets them for the process.
              Throws <code>ArgumentNullException</code> for <code>null</code>;{" "}
              <code>ArgumentException</code> for a blank or malformed format, a
              format that cannot read back what it writes, a format with no year or
              with a day but no month, two that read one text as different dates,
              or two that put the day and the month in opposite orders; and{" "}
              <code>InvalidOperationException</code> on a second call.
            </td>
          </tr>
          <tr>
            <td><code>DwDates.Options</code></td>
            <td>
              The <code>DwDateOptions</code> in force. Frozen; declares no formats
              until a deployment configures some.
            </td>
          </tr>
          <tr>
            <td><code>DwDates.IsConfigured</code></td>
            <td><code>true</code> once <code>Configure</code> has succeeded.</td>
          </tr>
          <tr>
            <td><code>DwDateOptions.Formats</code></td>
            <td>
              <code>IList&lt;string&gt;</code> — the formats accepted in addition to
              ISO&nbsp;8601 and year-first dates. Read-only once configured.
            </td>
          </tr>
          <tr>
            <td><code>DwDateOptions.IsFrozen</code></td>
            <td>
              <code>true</code> once the options have been handed to{" "}
              <code>DwDates.Configure</code>.
            </td>
          </tr>
          <tr>
            <td><code>DwDateOptions.Bind(IConfiguration)</code></td>
            <td>
              Extension method. Reads <code>Formats</code> from a section and
              returns the same instance. Throws{" "}
              <code>InvalidOperationException</code> for a key nothing answers to,
              for a single value where the list belongs (<code>&quot;Formats&quot;:
              &quot;dd/MM/yyyy&quot;</code>, or one{" "}
              <code>DynamicWhere__Dates__Formats</code> environment variable), or
              when the options are already frozen.
            </td>
          </tr>
        </tbody>
      </table>

      <Callout tone="warn" title="Zones: DateTimeOffset reads as UTC, DateTime as host local time">
        On a <code>DateTimeOffset</code> member the value is normalized to UTC, and
        one carrying no zone is read as UTC, so <code>Date</code> compares the
        calendar day you wrote — send a day comparison without a zone. The
        member&apos;s own day is the provider&apos;s: its UTC day on PostgreSQL,
        but the day in its stored offset in memory, so a row at{" "}
        <code>2026-09-01T01:00+03:00</code> is 31 August on one and 1 September
        on the other. On a <code>DateTime</code> member a value carrying
        a zone is converted to the host&apos;s local time. A C#{" "}
        <code>DateTime</code> placed in <code>Values</code> is written with no
        zone, and so is read as UTC on a <code>DateTimeOffset</code> member — with
        one exception. A <code>DateTime</code> whose <code>Kind</code> is{" "}
        <code>Local</code> (<code>DateTime.Now</code>, or a value Newtonsoft.Json
        produced from a string carrying an offset), compared under{" "}
        <code>DataType.DateTime</code> with a <code>DateTimeOffset</code> or{" "}
        <code>DateTimeOffset?</code> member or with a <code>Having</code> alias
        over such a member&apos;s aggregate, is written with its offset —{" "}
        <code>2026-09-17T15:00:00+03:00</code> — and filters on the moment it
        holds. Under <code>DataType.Date</code> it keeps no zone, so{" "}
        <code>DateTime.Today</code> compares the day it was written for rather
        than the UTC day of local midnight.
      </Callout>

      <h2 id="json-examples">JSON examples per type</h2>

      <h3 id="text">Text</h3>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Name",
  "dataType": "Text",
  "operator": "IContains",
  "values": ["phone"]
}`}</Code>

      <h3 id="guid">Guid</h3>
      <Code lang="json">{`{
  "sort": 1,
  "field": "CustomerId",
  "dataType": "Guid",
  "operator": "Equal",
  "values": ["a1b2c3d4-e5f6-7890-abcd-ef1234567890"]
}`}</Code>

      <h3 id="number">Number</h3>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Price",
  "dataType": "Number",
  "operator": "Between",
  "values": [0, 1.569]
}`}</Code>

      <h3 id="boolean">Boolean</h3>
      <Code lang="json">{`{
  "sort": 1,
  "field": "IsActive",
  "dataType": "Boolean",
  "operator": "Equal",
  "values": [true]
}`}</Code>

      <h3 id="datetime">DateTime</h3>
      <Code lang="json">{`{
  "sort": 1,
  "field": "CreatedAt",
  "dataType": "DateTime",
  "operator": "Equal",
  "values": ["2024-06-15T14:30:00"]
}`}</Code>

      <h3 id="date">Date</h3>
      <Code lang="json">{`{
  "sort": 1,
  "field": "CreatedAt",
  "dataType": "Date",
  "operator": "GreaterThan",
  "values": ["2024-01-01"]
}`}</Code>

      <h3 id="enum">Enum</h3>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Status",
  "dataType": "Enum",
  "operator": "In",
  "values": ["Active", "Pending"]
}`}</Code>

      <h2 id="value-coercion">Value coercion</h2>
      <p>
        <code>Values</code> is <code>List&lt;object&gt;</code>. Whatever the
        front-end sends, the library normalizes each element before validating
        and building the expression.
      </p>
      <table>
        <thead>
          <tr>
            <th>Incoming runtime type</th>
            <th>Normalized form</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>string</code></td>
            <td>as-is</td>
          </tr>
          <tr>
            <td><code>bool</code></td>
            <td><code>"true"</code> / <code>"false"</code> (lowercase)</td>
          </tr>
          <tr>
            <td><code>JsonElement</code></td>
            <td>
              Unwrapped by <code>ValueKind</code>: string → text, number → raw
              JSON token, <code>True</code> / <code>False</code> → lowercase
              string.
            </td>
          </tr>
          <tr>
            <td>
              <code>DateTime</code> / <code>DateTimeOffset</code> /{" "}
              <code>DateOnly</code>
            </td>
            <td>
              Year-first text: <code>&quot;2026-09-01T12:30:00&quot;</code> (no
              zone marker), <code>&quot;2026-09-01T12:30:00+03:00&quot;</code>,{" "}
              <code>&quot;2026-09-01&quot;</code>. The exception is a{" "}
              <code>DateTime</code> of <code>Kind</code> <code>Local</code>{" "}
              compared under <code>DataType.DateTime</code> with a{" "}
              <code>DateTimeOffset</code> member, which keeps its offset:{" "}
              <code>&quot;2026-09-01T12:30:00+03:00&quot;</code>. Before 3.1.0 these took the
              month-first invariant form, such as{" "}
              <code>&quot;09/01/2026 12:30:00&quot;</code>, which is now refused.
            </td>
          </tr>
          <tr>
            <td>numeric / other <code>IFormattable</code></td>
            <td><code>InvariantCulture</code> formatting</td>
          </tr>
          <tr>
            <td>anything else (e.g. <code>JValue</code>)</td>
            <td><code>value.ToString()</code></td>
          </tr>
          <tr>
            <td><code>null</code></td>
            <td><code>string.Empty</code></td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        Old clients that send <code>["true"]</code> or <code>["100"]</code>{" "}
        (quoted strings) keep working unchanged — strings deserialize into the{" "}
        <code>List&lt;object&gt;</code> as string elements and the normalizer
        passes them through.
      </Callout>

      <h2 id="csharp">C# usage</h2>
      <Code lang="csharp">{`using DynamicWhere.ex.Enums;

var condition = new Condition
{
    Sort = 1,
    Field = "Price",
    DataType = DataType.Number,
    Operator = Operator.GreaterThan,
    Values = new List<object> { 50 }
};`}</Code>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/enums/operator">Operator →</Link> all 28 operators
          and their value-count requirements.
        </li>
        <li>
          <Link href="/docs/classes/condition">Condition →</Link> the class that
          carries the <code>DataType</code>.
        </li>
        <li>
          <Link href="/docs/validation/condition">Condition validation →</Link>{" "}
          the rules used when parsing each <code>DataType</code>.
        </li>
        <li>
          <Link href="/docs/errors">Error Codes →</Link>{" "}
          <code>InvalidFormat</code> and <code>AmbiguousDateFormat</code>.
        </li>
      </ul>
    </DocPage>
  );
}
