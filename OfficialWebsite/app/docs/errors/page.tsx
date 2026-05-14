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
        The <code>Message</code> property carries one of the 22 stable error strings
        listed below, so you can pattern‑match them in middleware and surface
        meaningful problems to API callers.
      </p>

      <h2 id="overview">How errors are raised</h2>
      <p>
        Validation runs eagerly — before any expression tree is built or any SQL is
        emitted. If your <code>Filter</code>, <code>Segment</code>,{" "}
        <code>Summary</code>, <code>ConditionGroup</code>, <code>GroupBy</code>, or{" "}
        <code>PageBy</code> shape is invalid, the corresponding extension method
        throws synchronously (even on the async overloads, the throw happens before
        the first <code>await</code>). The thrown type is always{" "}
        <code>LogicException</code> and the message is always one of the rows in the
        table below.
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

      <h2 id="all-errors">All 22 error codes</h2>
      <p>
        Codes wrapped in <code>(parens)</code> are parameterized — the bracketed
        token in the message is replaced at runtime with the offending operator,
        alias, aggregator, type, or field name.
      </p>

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
            <td>Null / whitespace value</td>
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
            <td>Value doesn't parse for declared <code>DataType</code></td>
          </tr>
          <tr>
            <td><code>InvalidAlias</code></td>
            <td><code>AggregationMustHasValidAlias</code></td>
            <td>Empty or dotted alias</td>
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
            <td>Non‑simple <code>GroupBy</code> field</td>
          </tr>
          <tr>
            <td><code>GroupByFieldCannotBeCollection</code></td>
            <td><code>GroupByFieldCannotBeCollectionType</code></td>
            <td>Collection <code>GroupBy</code> field</td>
          </tr>
          <tr>
            <td><code>AggregationFieldMustBeSimpleType</code></td>
            <td><code>AggregationFieldMustBeSimpleType</code></td>
            <td>Complex aggregation field</td>
          </tr>
          <tr>
            <td><code>AggregationFieldCannotBeCollection</code></td>
            <td><code>AggregationFieldCannotBeCollectionType</code></td>
            <td>Collection aggregation field</td>
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
        </tbody>
      </table>

      <Callout tone="note" title="Why messages and not numeric codes?">
        The messages are stable across versions and self‑documenting, which keeps
        client error handling readable. If you need numeric codes for i18n, map them
        in your API layer using the <em>Error Code</em> column as the key — the
        Error Code names are also stable.
      </Callout>

      <h2 id="related-validation">Where each error lives</h2>
      <p>
        Each error is raised by a specific validation entry point. Follow the link
        for the full validation rules and the exact shape that triggers each code:
      </p>
      <ul>
        <li>
          <Link href="/docs/validation/condition">Condition validation →</Link>{" "}
          <code>InvalidField</code>, <code>InvalidValue</code>,{" "}
          <code>RequiredValues</code>, <code>NotRequiredValues</code>,{" "}
          <code>RequiredTwoValue</code>, <code>RequiredOneValue(op)</code>,{" "}
          <code>InvalidFormat</code>.
        </li>
        <li>
          <Link href="/docs/validation/condition-group">ConditionGroup validation →</Link>{" "}
          <code>SetsUniqueSort</code>, <code>ConditionsUniqueSort</code>,{" "}
          <code>SubConditionsGroupsUniqueSort</code>,{" "}
          <code>RequiredIntersection</code>.
        </li>
        <li>
          <Link href="/docs/validation/page">Page validation →</Link>{" "}
          <code>InvalidPageNumber</code>, <code>InvalidPageSize</code>.
        </li>
        <li>
          <Link href="/docs/validation/group-by">GroupBy validation →</Link>{" "}
          <code>MustHaveFields</code>, <code>GroupByMustHaveFields</code>,{" "}
          <code>GroupByFieldsMustBeUnique</code>,{" "}
          <code>GroupByFieldCannotBeComplexType</code>,{" "}
          <code>GroupByFieldCannotBeCollection</code>.
        </li>
        <li>
          <Link href="/docs/validation/summary">Summary validation →</Link>{" "}
          <code>InvalidAlias</code>,{" "}
          <code>AggregationFieldMustBeSimpleType</code>,{" "}
          <code>AggregationFieldCannotBeCollection</code>,{" "}
          <code>AggregationAliasesMustBeUnique</code>,{" "}
          <code>AggregationAliasCannotBeGroupByField(alias)</code>,{" "}
          <code>UnsupportedAggregatorForType(agg, type)</code>,{" "}
          <code>SummaryOrderFieldMustExistInGroupByOrAggregate(f)</code>,{" "}
          <code>HavingFieldMustExistInAggregateByAlias(f)</code>.
        </li>
        <li>
          <Link href="/docs/validation/segment">Segment validation →</Link>{" "}
          inherits all <code>ConditionGroup</code> + <code>Page</code> errors and
          additionally enforces <code>RequiredIntersection</code> for sets at index
          1 and above.
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
