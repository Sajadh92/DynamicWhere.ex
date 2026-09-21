import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "Error Codes Reference",
  description:
    "Every validation error DynamicWhere.ex can throw — error code, exact message, and the condition that triggers it.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/errors/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/errors">
      <h1>Error Codes Reference</h1>
      <p>
        Every validation failure in DynamicWhere.ex throws a{" "}
        <code>LogicException</code> (which inherits from <code>Exception</code>).
        The <code>Message</code> property carries one of the 31 stable error strings
        listed below, so you can pattern‑match them in middleware and surface
        meaningful problems to API callers.
      </p>

      <h2 id="overview">How errors are raised</h2>
      <p>
        Validation runs clause by clause while the query is composed, not in a
        single pass before it. Most shapes — <code>Filter</code>,{" "}
        <code>ConditionGroup</code>, <code>GroupBy</code>, <code>PageBy</code> —
        are checked before their part of the query executes, but two entry
        points reach the database first: <code>ToListDynamic</code> and{" "}
        <code>ToListAsyncDynamic</code> run the <code>COUNT</code> query before
        validating <code>Orders</code>, <code>Page</code> and{" "}
        <code>Selects</code>. <code>ToListAsync(Segment)</code> combines its sets
        into one query and validates every clause before that query runs. The async
        overloads are <code>async</code> methods, so their exceptions surface at
        the <code>await</code> rather than at the call.
      </p>
      <p>
        <code>LogicException</code> is the usual type, but not the only one a
        caller sees. A null argument raises <code>ArgumentNullException</code>; a
        guarded type raises <code>PolicyException</code>, which derives from{" "}
        <code>LogicException</code>; and input that passes validation but not the
        expression parser raises <code>ParseException</code> from{" "}
        <code>System.Linq.Dynamic.Core</code>.
      </p>
      <Callout tone="danger" title="Changed in 3.3.0: a malformed request is a LogicException">
        A null element inside <code>Conditions</code>,{" "}
        <code>SubConditionGroups</code>, <code>ConditionSets</code>,{" "}
        <code>Orders</code> or <code>AggregateBy</code> used to raise{" "}
        <code>NullReferenceException</code>, and a null or blank{" "}
        <code>Selects</code> entry used to raise{" "}
        <code>ArgumentNullException</code> from the name lookup — a five-hundred
        for a request that was simply malformed. They are{" "}
        <code>{`ListOf[{list}]MustNotHasNullEntry`}</code> and{" "}
        <code>ConditionMustHasValidFieldName</code> now. A{" "}
        <code>ConditionSet</code> whose <code>ConditionGroup</code> is null, and a
        null <code>Summary.GroupBy</code>, are still{" "}
        <code>ArgumentNullException</code>. See{" "}
        <Link href="/docs/breaking-changes#null-entries">breaking point 45</Link>.
      </Callout>
      <Callout tone="danger" title="Changed in 3.3.0: no Number value reaches the parser">
        A <code>DataType.Number</code> value is read as the expression parser
        reads it, so one the parser cannot read — <code>&quot;1,000&quot;</code>,{" "}
        <code>&quot;+5&quot;</code>, <code>&quot;NaN&quot;</code> — or cannot
        compare with the member the condition names is{" "}
        <code>InvalidFormat</code> at validation, where it used to pass validation
        and throw <code>ParseException</code> when the query was built. See{" "}
        <Link href="/docs/breaking-changes#number-values">breaking point 44</Link>.
      </Callout>

      <Callout tone="info" title="Surfacing errors in an API">
        Wrap the call in a <code>try / catch (LogicException ex)</code> and map it
        to a <code>400 Bad Request</code> with the message as the validation reason.
        Other exception types should bubble up as <code>500</code>s.
        <Code lang="csharp">{`app.MapPost("/customers/search", async (Filter filter, AppDbContext db) =>
{
    try
    {
        var result = await db.Customers.ToListAsync(filter);
        return Results.Ok(result);
    }
    catch (LogicException ex)
    {
        // ex.Message is one of the stable error codes below
        return Results.BadRequest(new { error = ex.Message });
    }
});`}</Code>
      </Callout>

      <h2 id="all-errors">All 31 error codes</h2>
      <p>
        Codes wrapped in <code>(parens)</code> are parameterized — the bracketed
        token in the message is replaced at runtime with the offending operator,
        alias, aggregator, type, or field name.
      </p>
      <p>
        One failure still carries a literal message rather than one of these
        codes:{" "}
        <code>{`Unsupported combination of DataType '{type}' and Operator '{op}'.`}</code>{" "}
        for a pair the predicate builder has no form for. It is the only one
        left — the <code>Select</code> projection refusal became the code{" "}
        <code>SelectTypeMustHaveParameterlessConstructor</code> in 3.1.0.
      </p>

      <Callout tone="danger" title="Changed in 3.1.0">
        A <code>Select&lt;T&gt;</code> or <code>Filter.Selects</code> whose{" "}
        <code>T</code> has no parameterless constructor used to throw the English
        sentence{" "}
        <code>{`Select projection requires a parameterless constructor on type '{T}'.`}</code>{" "}
        Its <code>Message</code> is now the stable code{" "}
        <code>SelectTypeMustHaveParameterlessConstructor</code>, and the type&apos;s
        name moved to a new property on the exception,{" "}
        <code>LogicException.Subject</code> (<code>string?</code>). Any middleware
        matching on that old sentence — or reading the type name out of it — needs
        updating. See{" "}
        <Link href="/docs/breaking-changes#select-code">breaking changes</Link>.
      </Callout>

      <Callout tone="danger" title="New in 3.1.0: AmbiguousDateFormat">
        A <code>Date</code> or <code>DateTime</code> value that leads with a day or
        a month — <code>01/09/2026</code>, <code>09/15/2026</code> — is refused with
        its own code rather than read one way or the other, and the field is on{" "}
        <code>Subject</code>. Its fix differs from <code>InvalidFormat</code>&apos;s:
        the value is a date, so either the client sends ISO&nbsp;8601 or the
        deployment declares the order it uses through{" "}
        <code>DwDates.Configure</code>. See{" "}
        <Link href="/docs/enums/data-type#date-formats">date formats</Link> and{" "}
        <Link href="/docs/breaking-changes#date-value-formats">breaking changes</Link>.
      </Callout>

      <Callout tone="danger" title="New in 3.1.0: StartsWithReservedName">
        A field path whose first segment is one of the expression parser&apos;s own
        words — <code>new</code>, <code>iif</code>, <code>np</code>,{" "}
        <code>isnull</code>, <code>is</code>, <code>as</code>, <code>cast</code>,{" "}
        <code>true</code>, <code>false</code>, <code>null</code>, in any letter
        case — is refused with{" "}
        <code>{`FieldPath[{path}]StartsWithReservedName`}</code> and that segment on{" "}
        <code>Subject</code>. The parser reads its own functions and literals before
        it looks for a member, so the path never reached the member: before{" "}
        <strong>3.1.0</strong> seven of the names raised <code>ParseException</code>,{" "}
        <code>True</code> and <code>False</code> an{" "}
        <code>InvalidOperationException</code>, and <code>Null</code> was read as
        the null literal, so the query returned no rows and no error. See{" "}
        <Link href="/docs/breaking-changes#root-it-parent-members">breaking changes</Link>.
      </Callout>

      <Callout tone="danger" title="Changed in 3.1.0: an unknown field under a strict policy">
        On a query guarded by <code>ApplyPolicy</code> under the{" "}
        <code>Strict</code> tier, outside a dry run, a field path that names
        nothing on the type no longer raises{" "}
        <code>ConditionMustHasValidFieldName</code>. It is refused the way a field
        denied for every feature is: a <code>PolicyException</code> with the code of
        the clause it appeared in — <code>FieldDeniedForWhere</code> …{" "}
        <code>FieldDeniedForSegment</code> — and <code>FieldPath</code>{" "}
        <code>&quot;*&quot;</code>, so the answer does not say whether the field
        exists. Unguarded queries, the convenience tier and a dry run still raise{" "}
        <code>ConditionMustHasValidFieldName</code>. See{" "}
        <Link href="/docs/policies/configuration#strict-refusals">what a strict refusal says</Link>{" "}
        and <Link href="/docs/breaking-changes#strict-unknown-field">breaking changes</Link>.
      </Callout>

      <table>
        <thead>
          <tr>
            <th>Error Code</th>
            <th>Message</th>
            <th>Triggered When</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>SetsUniqueSort</code></td>
            <td><code>ListOfConditionsSetsMustHasUniqueSortValue</code></td>
            <td>Duplicate <code>Sort</code> in ConditionSets</td>
          </tr>
          <tr>
            <td><code>ConditionsUniqueSort</code></td>
            <td><code>AnyListOfConditionsMustHasUniqueSortValue</code></td>
            <td>Duplicate <code>Sort</code> in Conditions</td>
          </tr>
          <tr>
            <td><code>SubConditionsGroupsUniqueSort</code></td>
            <td><code>AnyListOfSubConditionsGroupsMustHasUniqueSortValue</code></td>
            <td>Duplicate <code>Sort</code> in SubConditionGroups</td>
          </tr>
          <tr>
            <td><code>RequiredIntersection</code></td>
            <td><code>ConditionsSetOfIndex[1-N]MustHasIntersection</code></td>
            <td>Missing <code>Intersection</code> on set index 1+</td>
          </tr>
          <tr>
            <td><code>InvalidField</code></td>
            <td><code>ConditionMustHasValidFieldName</code></td>
            <td>Empty or invalid field name. On a strict-tier guarded query an unknown name is a <code>PolicyException</code> instead</td>
          </tr>
          <tr>
            <td><code>StartsWithReservedName(path)</code></td>
            <td><code>{`FieldPath[{path}]StartsWithReservedName`}</code></td>
            <td>
              A field path whose first segment is one of the expression
              parser&apos;s own words — <code>new</code>, <code>iif</code>,{" "}
              <code>np</code>, <code>isnull</code>, <code>is</code>,{" "}
              <code>as</code>, <code>cast</code>, <code>true</code>,{" "}
              <code>false</code>, <code>null</code>, in any letter case. Raised
              wherever a path is validated, so a condition <code>Field</code>,{" "}
              <code>Orders</code>, <code>Selects</code>,{" "}
              <code>GroupBy.Fields</code>, <code>AggregateBy.Field</code> and the
              member a <code>[DwAlias]</code> stands for all answer alike. A{" "}
              <code>DefaultOrder</code> entry naming one is skipped, and reported by
              the startup scan. Only the first segment counts: <code>Owner.New</code> names the member. The
              segment, trimmed, is on <code>Subject</code>. On a strict-tier
              guarded query it arrives as that clause&apos;s{" "}
              <code>FieldDeniedFor*</code> instead
            </td>
          </tr>
          <tr>
            <td><code>InvalidValue</code></td>
            <td><code>ConditionValuesAreNullOrWhiteSpace</code></td>
            <td>
              Defined but never thrown — a null value normalizes to an empty
              string, which <code>Text</code> and <code>Enum</code> accept and
              every other <code>DataType</code> rejects with{" "}
              <code>InvalidFormat</code>
            </td>
          </tr>
          <tr>
            <td><code>RequiredValues</code></td>
            <td><code>ConditionWithOperator[In-IIn-NotIn-INotIn]MustHasOneOrMoreValues</code></td>
            <td><code>In</code> / <code>NotIn</code> with 0 values</td>
          </tr>
          <tr>
            <td><code>NotRequiredValues</code></td>
            <td><code>ConditionWithOperator[IsNull-IsNotNull]MustHasNoValues</code></td>
            <td><code>IsNull</code> / <code>IsNotNull</code> with values</td>
          </tr>
          <tr>
            <td><code>RequiredTwoValue</code></td>
            <td><code>ConditionWithOperator[Between-NotBetween]MustHasOnlyTwoValues</code></td>
            <td><code>Between</code> without exactly 2 values</td>
          </tr>
          <tr>
            <td><code>RequiredOneValue(op)</code></td>
            <td><code>{`ConditionWithOperator[{op}]MustHasOnlyOneValue`}</code></td>
            <td>Single‑value operator with wrong count</td>
          </tr>
          <tr>
            <td><code>InvalidPageNumber</code></td>
            <td><code>PageNumberMustBeGreaterThanZero</code></td>
            <td><code>PageNumber</code> &le; 0</td>
          </tr>
          <tr>
            <td><code>InvalidPageSize</code></td>
            <td><code>PageSizeMustBeGreaterThanZero</code></td>
            <td><code>PageSize</code> &le; 0</td>
          </tr>
          <tr>
            <td><code>MustHaveFields</code></td>
            <td><code>MustHasFields</code></td>
            <td>Empty fields list in <code>Select</code></td>
          </tr>
          <tr>
            <td><code>InvalidFormat</code></td>
            <td><code>InvalidFormat</code></td>
            <td>
              Value doesn't parse for declared <code>DataType</code>. A{" "}
              <code>Number</code> value must be a literal the expression parser
              reads — invariant, no thousands separator, no leading plus, no{" "}
              <code>NaN</code> — and one it can compare with the member the
              condition names (3.3.0). A{" "}
              <code>Date</code> / <code>DateTime</code> value must be ISO&nbsp;8601,
              year-first, or a format declared through{" "}
              <code>DwDates.Configure</code>, read as the member&apos;s own date
              type — the same reading at validation and when the predicate is
              built — so <code>12:00</code>, <code>1/9</code> or{" "}
              <code>Sep 2026</code> raises this on every server. Raised by a date
              value, it carries the field on <code>Subject</code>
            </td>
          </tr>
          <tr>
            <td><code>AmbiguousDateFormat</code></td>
            <td><code>AmbiguousDateFormat</code></td>
            <td>
              A <code>Date</code> / <code>DateTime</code> value leads with a day or
              a month — <code>01/09/2026</code>, <code>15/09/2026</code>,{" "}
              <code>09/15/2026</code>, <code>01.09.2026</code>,{" "}
              <code>1/9/26</code>, with or without a time — and no format declared
              through <code>DwDates.Configure</code> reads it. Refused by shape,
              whatever the numbers. The field, or the <code>Having</code> alias, is
              on <code>Subject</code>
            </td>
          </tr>
          <tr>
            <td><code>InvalidAlias</code></td>
            <td><code>AggregationMustHasValidAlias</code></td>
            <td>Alias is not a plain identifier — empty, starting with a digit, or carrying any character that is not a letter, digit, or underscore</td>
          </tr>
          <tr>
            <td><code>GroupByMustHaveFields</code></td>
            <td><code>GroupByMustHasAtLeastOneField</code></td>
            <td><code>GroupBy</code> with no fields</td>
          </tr>
          <tr>
            <td><code>GroupByFieldsMustBeUnique</code></td>
            <td><code>GroupByFieldsMustBeUnique</code></td>
            <td>Duplicate <code>GroupBy</code> fields</td>
          </tr>
          <tr>
            <td><code>GroupByFieldCannotBeComplexType</code></td>
            <td><code>GroupByFieldCannotBeComplexType</code></td>
            <td><code>GroupBy</code> field ends on a navigation or on a collection of entities</td>
          </tr>
          <tr>
            <td><code>GroupByFieldCannotBeCollection</code></td>
            <td><code>GroupByFieldCannotBeCollectionType</code></td>
            <td><code>GroupBy</code> field ends on a collection of collections</td>
          </tr>
          <tr>
            <td><code>AggregationFieldMustBeSimpleType</code></td>
            <td><code>AggregationFieldMustBeSimpleType</code></td>
            <td>Aggregation field ends on a navigation or on a collection of entities</td>
          </tr>
          <tr>
            <td><code>AggregationFieldCannotBeCollection</code></td>
            <td><code>AggregationFieldCannotBeCollectionType</code></td>
            <td>Aggregation field ends on a collection of collections</td>
          </tr>
          <tr>
            <td><code>AggregationAliasesMustBeUnique</code></td>
            <td><code>AggregationAliasesMustBeUnique</code></td>
            <td>Duplicate aliases</td>
          </tr>
          <tr>
            <td><code>AggregationAliasCannotBeGroupByField(alias)</code></td>
            <td><code>{`AggregationAlias[{alias}]CannotBeUsedInGroupByFields`}</code></td>
            <td>Alias clashes with a <code>GroupBy</code> field</td>
          </tr>
          <tr>
            <td><code>UnsupportedAggregatorForType(agg, type)</code></td>
            <td><code>{`Aggregator[{agg}]IsNotSupportedForFieldType[{type}]`}</code></td>
            <td>Invalid aggregator for the field's type</td>
          </tr>
          <tr>
            <td><code>SummaryOrderFieldMustExistInGroupByOrAggregate(f)</code></td>
            <td><code>{`SummaryOrderField[{f}]MustExistInGroupByFieldsOrAggregateByAliases`}</code></td>
            <td>Order on a non‑grouped, non‑aggregated field</td>
          </tr>
          <tr>
            <td><code>HavingFieldMustExistInAggregateByAlias(f)</code></td>
            <td><code>{`HavingField[{f}]MustExistInAggregateByAliases`}</code></td>
            <td><code>Having</code> references an unknown alias</td>
          </tr>
          <tr>
            <td><code>OrderFieldCannotEndOnComplexCollection(f)</code></td>
            <td><code>{`OrderField[{f}]CannotEndOnCollectionOfComplexElements`}</code></td>
            <td>Order path ends on a collection of entities — sort by a scalar inside it</td>
          </tr>
          <tr>
            <td><code>SelectTypeMustHaveParameterlessConstructor</code></td>
            <td><code>SelectTypeMustHaveParameterlessConstructor</code></td>
            <td>
              <code>Select&lt;T&gt;</code> or <code>Filter.Selects</code> on a{" "}
              <code>T</code> the projection cannot construct — a positional record,
              most often. The type&apos;s name is on{" "}
              <code>Subject</code>, not in the message. Also reached by a typed
              guarded query whose policy denies a field for <code>Select</code>{" "}
              — since 3.2.0 whatever the field holds, and beneath a member where
              its value can reach the result — because the deny synthesizes a
              projection
            </td>
          </tr>
          <tr>
            <td><code>NullEntry(list)</code></td>
            <td><code>{`ListOf[{list}]MustNotHasNullEntry`}</code></td>
            <td>
              A list of the request shape holds a <code>null</code> entry —{" "}
              <code>Conditions</code>, <code>SubConditionGroups</code>,{" "}
              <code>ConditionSets</code>, <code>Orders</code> or{" "}
              <code>AggregateBy</code>, spelled as the shape declares it. New in{" "}
              <strong>3.3.0</strong>: such an entry used to surface as a{" "}
              <code>NullReferenceException</code> from wherever it was first
              touched. See{" "}
              <Link href="/docs/breaking-changes#null-entries">breaking point 45</Link>
            </td>
          </tr>
        </tbody>
      </table>

      <Callout tone="note" title="Why messages and not numeric codes?">
        The messages are stable across versions and self‑documenting, which keeps
        client error handling readable. If you need numeric codes for i18n, map them
        in your API layer using the <em>Error Code</em> column as the key — the
        Error Code names are also stable.
      </Callout>

      <h2 id="subject">LogicException.Subject</h2>
      <p>
        <code>LogicException</code> gained a second constructor in 3.1.0 —{" "}
        <code>LogicException(string message, string? subject)</code> — and the
        matching read-only property <code>Subject</code> (<code>string?</code>).
        It carries what a refusal is <em>about</em> where the code alone does not
        say: the rejected type&apos;s <code>Name</code> on{" "}
        <code>SelectTypeMustHaveParameterlessConstructor</code>, the field —
        or the <code>Having</code> alias — on <code>AmbiguousDateFormat</code> and
        on an <code>InvalidFormat</code> raised by a <code>Date</code> or{" "}
        <code>DateTime</code> value, and the offending first segment, trimmed, on{" "}
        <code>{`FieldPath[{path}]StartsWithReservedName`}</code>. Under{" "}
        <code>ApplyPolicy</code> the field is
        named as the caller wrote it, so a <code>[DwAlias]</code> name is never
        swapped for the member it hides. It is <code>null</code> for every other code,
        including <code>InvalidFormat</code> on a <code>Guid</code>,{" "}
        <code>Number</code> or <code>Boolean</code> value; the parameterized codes
        already interpolate their operator, alias, aggregator, type, or field name
        into the message themselves.
      </p>
      <p>
        Keeping the type name out of the message is the point: a code that carried
        it would be a different string on every type, and neither your middleware
        nor an error envelope could match on it.
      </p>
      <Code lang="csharp">{`catch (LogicException ex)
{
    // ex.Message   -> "SelectTypeMustHaveParameterlessConstructor"
    // ex.Subject   -> "CustomerRow"
    //
    // ex.Message   -> "AmbiguousDateFormat"
    // ex.Subject   -> "CreatedAt"
    //
    // null for most other codes
    return Results.BadRequest(new { error = ex.Message, subject = ex.Subject });
}`}</Code>

      <h2 id="related-validation">Where each error lives</h2>
      <p>
        Each error is raised by a specific validation entry point. Follow the link
        for the full validation rules and the exact shape that triggers each code:
      </p>
      <ul>
        <li>
          <Link href="/docs/validation/condition">Condition validation →</Link>{" "}
          <code>InvalidField</code> (also raised for any other blank or
          unresolvable field path — <code>OrderBy</code>, <code>GroupBy</code>,{" "}
          <code>AggregateBy</code>, <code>Having</code>,{" "}
          <code>Summary.Orders</code>), <code>RequiredValues</code>,{" "}
          <code>NotRequiredValues</code>, <code>RequiredTwoValue</code>,{" "}
          <code>RequiredOneValue(op)</code>, <code>InvalidFormat</code>,{" "}
          <code>AmbiguousDateFormat</code> (the two format codes are also raised
          for a date value in a <code>Having</code> condition),{" "}
          <code>StartsWithReservedName(path)</code> (also raised for any other
          field path whose first segment is one of the parser&apos;s own words —{" "}
          <code>OrderBy</code>, <code>Selects</code>, <code>GroupBy</code>,{" "}
          <code>AggregateBy</code>).
        </li>
        <li>
          <Link href="/docs/validation/condition-group">ConditionGroup validation →</Link>{" "}
          <code>ConditionsUniqueSort</code>,{" "}
          <code>SubConditionsGroupsUniqueSort</code>.
        </li>
        <li>
          <Link href="/docs/validation/page">Page validation →</Link>{" "}
          <code>InvalidPageNumber</code>, <code>InvalidPageSize</code>.
        </li>
        <li>
          <Link href="/docs/validation/group-by">GroupBy validation →</Link>{" "}
          <code>GroupByMustHaveFields</code>,{" "}
          <code>GroupByFieldsMustBeUnique</code>,{" "}
          <code>GroupByFieldCannotBeComplexType</code>,{" "}
          <code>GroupByFieldCannotBeCollection</code>, and the{" "}
          <code>AggregateBy</code> codes <code>InvalidAlias</code>,{" "}
          <code>AggregationFieldMustBeSimpleType</code>,{" "}
          <code>AggregationFieldCannotBeCollection</code>,{" "}
          <code>AggregationAliasesMustBeUnique</code>,{" "}
          <code>AggregationAliasCannotBeGroupByField(alias)</code>,{" "}
          <code>UnsupportedAggregatorForType(agg, type)</code> — all reachable
          through <Link href="/docs/extensions/group"><code>Group</code></Link>{" "}
          as well as through <code>Summary</code>.
        </li>
        <li>
          <Link href="/docs/validation/summary">Summary validation →</Link>{" "}
          <code>SummaryOrderFieldMustExistInGroupByOrAggregate(f)</code>,{" "}
          <code>HavingFieldMustExistInAggregateByAlias(f)</code>, plus every
          GroupBy code above from the nested <code>GroupBy</code>.
        </li>
        <li>
          <Link href="/docs/validation/segment">Segment validation →</Link>{" "}
          <code>SetsUniqueSort</code> and <code>RequiredIntersection</code>, plus
          the <code>ConditionGroup</code> errors of each set and the{" "}
          <code>Selects</code>, <code>OrderBy</code> and <code>Page</code> errors of
          the segment itself.
        </li>
        <li>
          <Link href="/docs/extensions/select">Select / SelectDynamic →</Link>{" "}
          <code>MustHaveFields</code>, for an empty <code>Select</code>,{" "}
          <code>SelectDynamic</code>, or <code>Filter.Selects</code> list, and{" "}
          <code>SelectTypeMustHaveParameterlessConstructor</code>, for a typed{" "}
          <code>Select&lt;T&gt;</code> the projection cannot construct.
        </li>
        <li>
          <Link href="/docs/extensions/order">Order →</Link>{" "}
          <code>OrderFieldCannotEndOnComplexCollection(f)</code>, when an order
          path ends on a collection of entities.
        </li>
      </ul>

      <h2 id="next">See also</h2>
      <ul>
        <li>
          <Link href="/docs/breaking-changes">Breaking Changes & Known Limitations →</Link>{" "}
          behaviour that is <em>not</em> an error but may surprise you.
        </li>
        <li>
          <Link href="/docs/classes/filter">Filter →</Link> the most common entry
          point that triggers these validations.
        </li>
      </ul>
    </DocPage>
  );
}
