import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".Where<T>(...)",
  description:
    "Apply a single Condition or a ConditionGroup (with nested sub-groups) to an IQueryable<T>.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/extensions/where/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/where">
      <h1>.Where&lt;T&gt;(...)</h1>
      <p>
        Apply a single{" "}
        <Link href="/docs/classes/condition"><code>Condition</code></Link> or a
        full{" "}
        <Link href="/docs/classes/condition-group">
          <code>ConditionGroup</code>
        </Link>{" "}
        (with optional nested sub-groups joined by <code>And</code> /{" "}
        <code>Or</code>) to a query.
      </p>

      <h2 id="single-condition">Single condition overload</h2>
      <Code lang="csharp">{`public static IQueryable<T> Where<T>(this IQueryable<T> query, Condition condition)
    where T : class`}</Code>

      <table>
        <thead>
          <tr>
            <th>Parameter</th>
            <th>Type</th>
            <th>Description</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>condition</code></td>
            <td><code>Condition</code></td>
            <td>The filter condition</td>
          </tr>
        </tbody>
      </table>

      <p>
        <strong>Returns:</strong> <code>IQueryable&lt;T&gt;</code> — filtered
        query.
      </p>

      <h2 id="group-condition">Condition-group overload</h2>
      <Code lang="csharp">{`public static IQueryable<T> Where<T>(this IQueryable<T> query, ConditionGroup group)
    where T : class`}</Code>

      <table>
        <thead>
          <tr>
            <th>Parameter</th>
            <th>Type</th>
            <th>Description</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>group</code></td>
            <td><code>ConditionGroup</code></td>
            <td>
              The filter group — flat <code>Conditions</code> plus optional
              nested <code>SubConditionGroups</code>
            </td>
          </tr>
        </tbody>
      </table>

      <p>
        <strong>Returns:</strong> <code>IQueryable&lt;T&gt;</code> — filtered
        query.
      </p>

      <h2 id="validations">Validations</h2>

      <p><strong>Condition validation</strong> (applied per condition):</p>
      <ul>
        <li>
          <code>Field</code> must be non-empty and exist on <code>T</code>{" "}
          (case-insensitive, auto-normalized) — error code{" "}
          <code>InvalidField</code>.
        </li>
        <li>
          <code>Between</code> / <code>NotBetween</code> require exactly 2
          values — <code>RequiredTwoValue</code>.
        </li>
        <li>
          <code>In</code> / <code>IIn</code> / <code>NotIn</code> /{" "}
          <code>INotIn</code> require 1+ values — <code>RequiredValues</code>.
        </li>
        <li>
          <code>IsNull</code> / <code>IsNotNull</code> require 0 values —{" "}
          <code>NotRequiredValues</code>.
        </li>
        <li>
          All other operators require exactly 1 value —{" "}
          <code>RequiredOneValue({"{Operator}"})</code>.
        </li>
        <li>
          Values must not be null/whitespace — <code>InvalidValue</code>.
        </li>
        <li>
          <code>Guid</code> values must parse as <code>Guid</code> —{" "}
          <code>InvalidFormat</code>.
        </li>
        <li>
          <code>Number</code> values must parse as a numeric type —{" "}
          <code>InvalidFormat</code>.
        </li>
        <li>
          <code>Boolean</code> values must parse as <code>bool</code> —{" "}
          <code>InvalidFormat</code>.
        </li>
        <li>
          <code>Date</code> / <code>DateTime</code> values must parse as{" "}
          <code>DateTime</code> — <code>InvalidFormat</code>.
        </li>
      </ul>

      <p><strong>ConditionGroup validation</strong> (applied per group):</p>
      <ul>
        <li>
          All <code>Condition.Sort</code> values within the group must be
          unique — <code>ConditionsUniqueSort</code>.
        </li>
        <li>
          All <code>SubConditionGroups.Sort</code> values must be unique —{" "}
          <code>SubConditionsGroupsUniqueSort</code>.
        </li>
        <li>Each child condition is validated individually.</li>
      </ul>

      <Callout tone="note">
        When a condition's <code>Field</code> path traverses a collection
        property (e.g., <code>Orders.OrderItems.ProductName</code>), the
        library automatically wraps the inner segment in a{" "}
        <code>.Any()</code> lambda. There is no built-in <code>.All()</code>{" "}
        support.
      </Callout>

      <h2 id="example-single">Example — single condition</h2>
      <Code lang="csharp">{`var condition = new Condition
{
    Sort = 1,
    Field = "Name",
    DataType = DataType.Text,
    Operator = Operator.IContains,
    Values = new List<object> { "phone" }
};

var query = dbContext.Products.Where(condition);`}</Code>
      <Code lang="json">{`{
  "sort": 1,
  "field": "Name",
  "dataType": "Text",
  "operator": "IContains",
  "values": ["phone"]
}`}</Code>

      <h2 id="example-group">Example — condition group with nested OR</h2>
      <Code lang="csharp">{`var group = new ConditionGroup
{
    Connector = Connector.And,
    Conditions = new List<Condition>
    {
        new Condition
        {
            Sort = 1,
            Field = "IsActive",
            DataType = DataType.Boolean,
            Operator = Operator.Equal,
            Values = new List<object> { true }
        }
    },
    SubConditionGroups = new List<ConditionGroup>
    {
        new ConditionGroup
        {
            Sort = 1,
            Connector = Connector.Or,
            Conditions = new List<Condition>
            {
                new Condition { Sort = 1, Field = "Role", DataType = DataType.Text, Operator = Operator.Equal, Values = new List<object> { "Admin" } },
                new Condition { Sort = 2, Field = "Role", DataType = DataType.Text, Operator = Operator.Equal, Values = new List<object> { "Manager" } }
            }
        }
    }
};

var query = dbContext.Users.Where(group);`}</Code>

      <Code lang="json">{`{
  "connector": "And",
  "conditions": [
    { "sort": 1, "field": "IsActive", "dataType": "Boolean", "operator": "Equal", "values": ["true"] }
  ],
  "subConditionGroups": [
    {
      "sort": 1,
      "connector": "Or",
      "conditions": [
        { "sort": 1, "field": "Role", "dataType": "Text", "operator": "Equal", "values": ["Admin"] },
        { "sort": 2, "field": "Role", "dataType": "Text", "operator": "Equal", "values": ["Manager"] }
      ],
      "subConditionGroups": []
    }
  ]
}`}</Code>

      <p>
        Equivalent SQL:{" "}
        <code>WHERE IsActive = true AND (Role = 'Admin' OR Role = 'Manager')</code>
        .
      </p>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/condition"><code>Condition</code></Link>{" "}
          and{" "}
          <Link href="/docs/classes/condition-group">
            <code>ConditionGroup</code>
          </Link>{" "}
          shapes.
        </li>
        <li>
          <Link href="/docs/validation/condition">Condition validation</Link>{" "}
          rules in detail.
        </li>
        <li>
          <Link href="/docs/examples/where-single">JSON Cookbook: single Where</Link>{" "}
          and{" "}
          <Link href="/docs/examples/where-group">group Where</Link>.
        </li>
      </ul>
    </DocPage>
  );
}
