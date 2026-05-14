import type { Metadata } from "next";
import Link from "next/link";
import DocPage from "@/components/DocPage";
import Callout from "@/components/Callout";

export const metadata: Metadata = {
  title: "ConditionGroup Validation",
  description:
    "Rules enforced on a ConditionGroup — unique Sort values across sibling conditions and sub-groups.",
};

export default function Page() {
  return (
    <DocPage pathname="/docs/validation/condition-group">
      <h1>ConditionGroup Validation</h1>
      <p>
        A{" "}
        <Link href="/docs/classes/condition-group">
          <code>ConditionGroup</code>
        </Link>{" "}
        composes multiple <code>Condition</code>s and nested
        <code>SubConditionGroups</code>. <code>Sort</code> values determine the
        order they appear in the generated expression and must be unique inside
        each list.
      </p>

      <h2 id="rules">Rules</h2>
      <table>
        <thead>
          <tr>
            <th>Rule</th>
            <th>Error Code</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td><code>Conditions</code> Sort values must be unique</td>
            <td><code>ConditionsUniqueSort</code></td>
          </tr>
          <tr>
            <td>
              <code>SubConditionGroups</code> Sort values must be unique
            </td>
            <td><code>SubConditionsGroupsUniqueSort</code></td>
          </tr>
        </tbody>
      </table>

      <Callout tone="info">
        The two lists are validated independently — a <code>Condition</code>{" "}
        with <code>Sort = 1</code> and a <code>SubConditionGroup</code> with{" "}
        <code>Sort = 1</code> in the same group is valid.
      </Callout>

      <h2 id="related">Related</h2>
      <ul>
        <li>
          <Link href="/docs/classes/condition-group">ConditionGroup class</Link>
        </li>
        <li>
          <Link href="/docs/validation/condition">Condition rules</Link> — each
          child <code>Condition</code> is also validated.
        </li>
        <li>
          <Link href="/docs/examples/where-group">Where (group) example</Link>
        </li>
      </ul>
    </DocPage>
  );
}
