import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import { Code } from "@/components/Code";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: ".ToListAsync<T>(Segment)",
  description:
    "Async-only segment entry — combine the ConditionSets with Union / Intersect / Except into one query, then order, page and project it in the database like a filter. An overload takes a CancellationToken.",
  alternates: { canonical: "https://doc.dynamicwhere.com/docs/extensions/to-list-async-segment/" },
};

export default function Page() {
  return (
    <DocPage pathname="/docs/extensions/to-list-async-segment">
      <h1>.ToListAsync&lt;T&gt;(Segment)</h1>
      <p>
        Async-only segment operation. Combines every{" "}
        <Link href="/docs/classes/condition-set">
          <code>ConditionSet</code>
        </Link>{" "}
        with set operations (<code>Union</code> / <code>Intersect</code> /{" "}
        <code>Except</code>) into one query, then orders, pages and projects it
        in the database exactly as a <code>Filter</code> is.
      </p>

      <Callout tone="warn">
        <strong>Async-only.</strong>{" "}
        <code>.ToListAsync&lt;T&gt;(Segment)</code> is the only entry point for
        segment queries — there is no synchronous{" "}
        <code>.ToList&lt;T&gt;(Segment)</code> variant. It needs an EF Core
        provider. On a type with a primary key, only <code>Except</code> needs one
        that translates a correlated <code>EXISTS</code>.
      </Callout>

      <h2 id="signature">Signature</h2>
      <Code lang="csharp">{`public static Task<SegmentResult<T>> ToListAsync<T>(
    this IQueryable<T> query,
    Segment segment)
    where T : class

// 3.2.0
public static Task<SegmentResult<T>> ToListAsync<T>(
    this IQueryable<T> query,
    Segment segment,
    CancellationToken cancellationToken)
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
            <td><code>segment</code></td>
            <td>
              <Link href="/docs/classes/segment"><code>Segment</code></Link>
            </td>
            <td>
              Composition object — <code>ConditionSets</code>,{" "}
              <code>Selects</code>, <code>Orders</code>, <code>Page</code>
            </td>
          </tr>
          <tr>
            <td><code>cancellationToken</code></td>
            <td><code>CancellationToken</code></td>
            <td>
              Cancels the count and the read. New in 3.2.0; the overload
              without it passes <code>CancellationToken.None</code>
            </td>
          </tr>
        </tbody>
      </table>

      <h2 id="pipeline">Pipeline</h2>
      <ul>
        <li>
          Combine the sets in <code>Sort</code> order, left to right, using each
          later set&apos;s <code>Intersection</code>. The first set&apos;s{" "}
          <code>Intersection</code> is ignored.
        </li>
        <li>
          <code>Union</code> and <code>Intersect</code> join the sets&apos; own
          conditions with <code>OR</code> and <code>AND</code>.{" "}
          <code>Except</code> removes the rows of its set with{" "}
          <code>NOT EXISTS</code>, matched on <code>T</code>&apos;s primary key as
          EF Core maps it — composite, value-converted and inherited keys
          included.
        </li>
        <li>
          Apply <code>Orders</code>, then <code>Page</code>, then{" "}
          <code>Selects</code> to the combined query, and count it for{" "}
          <code>TotalCount</code> — the same steps, in the same order, as{" "}
          <Link href="/docs/extensions/to-list-async-filter"><code>ToListAsync(Filter)</code></Link>.
          Only the requested page is read, and an order field need not be
          selected.
        </li>
        <li>
          The overload that takes a <code>CancellationToken</code> passes it to
          that count and that read. A canceled token stops whichever of the two
          is running, and the call throws <code>OperationCanceledException</code>.
        </li>
      </ul>
      <p>
        A segment takes no <code>getQueryString</code>, so{" "}
        <code>ToListAsync(segment, default)</code> is not ambiguous: it binds
        the token overload and passes <code>CancellationToken.None</code>. The
        3.1 signature is unchanged, so code compiled against 3.1 still binds.
      </p>
      <p>
        Which rows belong is decided in the database, not by object reference, so
        a tracking query, an <code>AsNoTracking()</code> query and a query with{" "}
        <code>Selects</code> all return the same rows. Ordering is the
        database&apos;s: text sorts by its collation, and NULLs fall where the
        provider puts them.
      </p>

      <h2 id="validations">Validations</h2>
      <ul>
        <li>
          <code>ConditionSets</code> <code>Sort</code> values must be unique —{" "}
          <code>SetsUniqueSort</code>.
        </li>
        <li>
          Sets at index 1+ must have <code>Intersection</code> specified —{" "}
          <code>RequiredIntersection</code>.
        </li>
        <li>
          Each <code>ConditionSet.ConditionGroup</code> is validated as in{" "}
          <Link href="/docs/extensions/where"><code>.Where&lt;T&gt;</code></Link>
          .
        </li>
        <li>
          <code>Orders</code> (if provided): each <code>Field</code> must be
          non-empty and valid on <code>T</code>.
        </li>
        <li>
          <code>Page</code> (if provided): both <code>PageNumber</code> and{" "}
          <code>PageSize</code> must be &gt; 0.
        </li>
      </ul>

      <h2 id="returns">Returns</h2>
      <p>
        <code>Task&lt;</code>
        <Link href="/docs/classes/segment-result">
          <code>SegmentResult&lt;T&gt;</code>
        </Link>
        <code>&gt;</code> — inherits all properties from{" "}
        <Link href="/docs/classes/filter-result">
          <code>FilterResult&lt;T&gt;</code>
        </Link>
        .
      </p>

      <h2 id="example">Example</h2>
      <Code lang="csharp">{`var segment = new Segment
{
    ConditionSets = new List<ConditionSet>
    {
        new ConditionSet
        {
            Sort = 1,
            Intersection = null,
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = new List<Condition>
                {
                    new Condition { Sort = 1, Field = "Category.Name", DataType = DataType.Text, Operator = Operator.Equal, Values = new List<object> { "Electronics" } }
                }
            }
        },
        new ConditionSet
        {
            Sort = 2,
            Intersection = Intersection.Union,
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = new List<Condition>
                {
                    new Condition { Sort = 1, Field = "Price", DataType = DataType.Number, Operator = Operator.LessThan, Values = new List<object> { 20 } }
                }
            }
        },
        new ConditionSet
        {
            Sort = 3,
            Intersection = Intersection.Except,
            ConditionGroup = new ConditionGroup
            {
                Connector = Connector.And,
                Conditions = new List<Condition>
                {
                    new Condition { Sort = 1, Field = "IsActive", DataType = DataType.Boolean, Operator = Operator.Equal, Values = new List<object> { false } }
                }
            }
        }
    },
    Selects = new List<string> { "Id", "Name", "Price" },
    Orders  = new List<OrderBy> { new OrderBy { Sort = 1, Field = "Name", Direction = Direction.Ascending } },
    Page    = new PageBy { PageNumber = 1, PageSize = 20 }
};

SegmentResult<Product> result = await dbContext.Products.ToListAsync(segment);`}</Code>

      <Code lang="json">{`{
  "conditionSets": [
    {
      "sort": 1,
      "intersection": null,
      "conditionGroup": {
        "connector": "And",
        "conditions": [
          { "sort": 1, "field": "Category.Name", "dataType": "Text", "operator": "Equal", "values": ["Electronics"] }
        ],
        "subConditionGroups": []
      }
    },
    {
      "sort": 2,
      "intersection": "Union",
      "conditionGroup": {
        "connector": "And",
        "conditions": [
          { "sort": 1, "field": "Price", "dataType": "Number", "operator": "LessThan", "values": ["20"] }
        ],
        "subConditionGroups": []
      }
    },
    {
      "sort": 3,
      "intersection": "Except",
      "conditionGroup": {
        "connector": "And",
        "conditions": [
          { "sort": 1, "field": "IsActive", "dataType": "Boolean", "operator": "Equal", "values": ["false"] }
        ],
        "subConditionGroups": []
      }
    }
  ],
  "selects": ["Id", "Name", "Price"],
  "orders": [
    { "sort": 1, "field": "Name", "direction": "Ascending" }
  ],
  "page": { "pageNumber": 1, "pageSize": 20 }
}`}</Code>

      <p>
        <strong>Logic:</strong>{" "}
        <code>(Electronics) UNION (Price &lt; 20) EXCEPT (Inactive)</code> →
        order → paginate.
      </p>

      <Callout tone="warn" title="A type with no primary key compares whole rows">
        A keyless entity type, or a query EF Core does not map to{" "}
        <code>T</code>, has no key to match on. Its sets are combined with SQL{" "}
        <code>UNION</code> / <code>INTERSECT</code> / <code>EXCEPT</code>, which
        compare every column: identical rows collapse into one, a column the
        database cannot compare (PostgreSQL <code>json</code>, SQL Server{" "}
        <code>xml</code>) fails the query even when it is not selected, and the
        provider must support the operators the sets use.
      </Callout>

      <Callout tone="danger" title="Changed in 3.1.0">
        The sets used to be loaded one query each and combined in memory by
        object reference. With <code>AsNoTracking()</code>, with{" "}
        <code>Selects</code>, and under <code>ApplyPolicy</code>, which is always
        untracked, <code>Intersect</code> returned nothing, <code>Except</code>{" "}
        removed nothing and <code>Union</code> counted a row once per set.
        Ordering and paging ran in memory after projection, and every row of
        every set was read. See{" "}
        <Link href="/docs/breaking-changes#segment-in-database">breaking changes</Link>.
      </Callout>

      <p>
        A typed row is a whole <code>Product</code>, not a trimmed object: the
        members outside <code>Selects</code> are still present, holding their
        defaults.
      </p>
      <Code lang="json">{`{
  "pageNumber": 1,
  "pageSize": 20,
  "pageCount": 2,
  "totalCount": 35,
  "data": [
    {
      "id": 1,
      "name": "Adapter Cable",
      "price": 9.99,
      "isActive": false,
      "createdAt": "0001-01-01T00:00:00",
      "category": null
    }
  ],
  "queryString": null
}`}</Code>

      <h2 id="see-also">See also</h2>
      <ul>
        <li>
          <Link href="/docs/classes/segment"><code>Segment</code></Link> shape.
        </li>
        <li>
          <Link href="/docs/classes/condition-set">
            <code>ConditionSet</code>
          </Link>{" "}
          shape and{" "}
          <Link href="/docs/enums/intersection">
            <code>Intersection</code>
          </Link>{" "}
          values.
        </li>
        <li>
          <Link href="/docs/validation/segment">Segment validation</Link>.
        </li>
        <li>
          <Link href="/docs/examples/segment">JSON Cookbook: Segment</Link>.
        </li>
      </ul>
    </DocPage>
  );
}
