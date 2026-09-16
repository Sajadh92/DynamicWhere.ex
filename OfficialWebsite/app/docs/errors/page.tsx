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
        The <code>Message</code> property carries one of the 28 stable error strings
        listed below, so you can pattern‑match them in middleware and surface
        meaningful problems to API callers.
      </p>

      <h2 id="overview">How errors are raised</h2>
      <p>
        Validation runs clause by clause while the query is composed, not in a
        single pass before it. Most shapes — <code>Filter</code>,{" "}
        <code>ConditionGroup</code>, <code>GroupBy</code>, <code>PageBy</code> —
        are checked before their part of the query executes, but three entry
        points reach the database first: <code>ToListDynamic</code> and{" "}
        <code>ToListAsyncDynamic</code> run the <code>COUNT</code> query before
        validating <code>Orders</code>, <code>Page</code> and{" "}
        <code>Selects</code>, and <code>ToListAsync(Segment)</code> queries each
        condition set before validating the clauses that follow. The async
        overloads are <code>async</code> methods, so their exceptions surface at
        the <code>await</code> rather than at the call.
      </p>
      <p>
        <code>LogicException</code> is the usual type, but not the only one a
        caller sees. A null argument or a blank <code>Selects</code> entry raises{" "}
        <code>ArgumentNullException</code>; a null element inside{" "}
        <code>Conditions</code>, <code>SubConditionGroups</code>,{" "}
        <code>ConditionSets</code> or <code>Orders</code> raises{" "}
        <code>NullReferenceException</code>; a guarded type raises{" "}
        <code>PolicyException</code>, which derives from{" "}
        <code>LogicException</code>; and input that passes validation but not the
        expression parser raises <code>ParseException</code> from{" "}
        <code>System.Linq.Dynamic.Core</code>.
      </p>

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

      <h2 id="all-errors">All 28 error codes</h2>
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
        full name moved to a new property on the exception,{" "}
        <code>LogicException.Subject</code> (<code>string?</code>). Any middleware
        matching on that old sentence — or reading the type name out of it — needs
        updating. See{" "}
        <Link href="/docs/breaking-changes#select-code">breaking changes</Link>.
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
            <td>Empty or invalid field name</td>
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
              <code>Date</code> / <code>DateTime</code> value is read with the
              invariant culture, as the member&apos;s own date type — the same
              reading at validation and when the predicate is built — so a
              host-specific form such as <code>15/09/2026</code> raises this on
              every server
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
              most often. The type&apos;s full name is on{" "}
              <code>Subject</code>, not in the message. Also reached by a typed
              guarded query whose policy denies a field for <code>Select</code>,
              since the deny synthesizes a projection
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
        say: today that is the rejected type&apos;s <code>FullName</code> on{" "}
        <code>SelectTypeMustHaveParameterlessConstructor</code>. It is{" "}
        <code>null</code> for every other code, because the parameterized codes
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
    // ex.Subject   -> "MyApp.Dtos.CustomerRow"  (null for every other code)
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
          <code>RequiredOneValue(op)</code>, <code>InvalidFormat</code>.
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
          the <code>ConditionGroup</code>, <code>Selects</code>,{" "}
          <code>OrderBy</code> and <code>Page</code> errors of every set it runs.
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
