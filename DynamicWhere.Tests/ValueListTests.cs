using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Source;

namespace DynamicWhere.Tests;

/// <summary>A row found by one code among many.</summary>
public class ListedCode
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;
}

/// <summary>
/// <c>In</c> and <c>NotIn</c> with a long value list.
/// </summary>
/// <remarks>
/// Written as one flat chain, <c>a || b || c ...</c> nests one level per value, and both EF Core and the
/// expression compiler walk a query tree recursively: a few hundred values overflowed a request thread's
/// stack, which ends the process however the caller handles exceptions.
/// </remarks>
public class ValueListTests
{
    private static List<string> Codes(int count) => Enumerable.Range(0, count).Select(i => $"c{i}").ToList();

    /// <summary>The deepest chain of nested nodes, measured without recursion so a deep tree cannot overflow the test.</summary>
    private static int Depth(Expression root)
    {
        int deepest = 0;
        Stack<(Expression Node, int Depth)> pending = new();

        pending.Push((root, 1));

        while (pending.Count > 0)
        {
            (Expression node, int depth) = pending.Pop();

            deepest = Math.Max(deepest, depth);

            switch (node)
            {
                case BinaryExpression binary:
                    pending.Push((binary.Left, depth + 1));
                    pending.Push((binary.Right, depth + 1));
                    break;
                case UnaryExpression unary:
                    pending.Push((unary.Operand, depth + 1));
                    break;
                case MethodCallExpression call:
                    if (call.Object is not null)
                    {
                        pending.Push((call.Object, depth + 1));
                    }

                    foreach (Expression argument in call.Arguments)
                    {
                        pending.Push((argument, depth + 1));
                    }

                    break;
                case MemberExpression { Expression: not null } member:
                    pending.Push((member.Expression, depth + 1));
                    break;
            }
        }

        return deepest;
    }

    [Theory]
    [InlineData(Operator.In, "||", "==")]
    [InlineData(Operator.NotIn, "&&", "!=")]
    public void A_list_of_up_to_thirty_two_values_is_written_exactly_as_before(Operator op, string join, string compare)
    {
        List<string> values = Codes(32);

        string before = $"Code != null && ({string.Join($" {join} ", values.Select(v => $"Code {compare} \"{v}\""))})";

        Assert.Equal(before, Builder.BuildCondition(DataType.Text, op, "Code", values));
    }

    [Theory]
    [InlineData(DataType.Text, Operator.In)]
    [InlineData(DataType.Text, Operator.NotIn)]
    [InlineData(DataType.Text, Operator.IIn)]
    [InlineData(DataType.Text, Operator.INotIn)]
    public void A_long_list_nests_by_halves_rather_than_by_values(DataType dataType, Operator op)
    {
        string predicate = Builder.BuildCondition(dataType, op, "Code", Codes(5000));

        LambdaExpression lambda = DynamicExpressionParser.ParseLambda(
            DynamicLinq.Config, typeof(ListedCode), typeof(bool), predicate);

        Assert.InRange(Depth(lambda.Body), 1, 64);
    }

    [Theory]
    [InlineData(Operator.In)]
    [InlineData(Operator.NotIn)]
    public void A_number_list_nests_by_halves_too(Operator op)
    {
        List<string> values = Enumerable.Range(0, 5000).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();

        string predicate = Builder.BuildCondition(DataType.Number, op, "Id", values);

        LambdaExpression lambda = DynamicExpressionParser.ParseLambda(
            DynamicLinq.Config, typeof(ListedCode), typeof(bool), predicate);

        Assert.InRange(Depth(lambda.Body), 1, 64);
    }

    [Fact]
    public void A_long_list_runs_on_a_small_stack_and_answers_as_a_short_one_would()
    {
        // Three thousand values compiled on a quarter-megabyte stack. Flat, the chain overflowed long
        // before that, and a stack overflow cannot be caught.
        List<ListedCode> rows = Enumerable.Range(0, 10)
            .Select(i => new ListedCode { Id = i + 1, Code = i < 5 ? $"c{i}" : $"z{i}" })
            .ToList();

        Condition In(Operator op)
        {
            Condition condition = new() { Field = "Code", DataType = DataType.Text, Operator = op };

            condition.Values.AddRange(Codes(3000));

            return condition;
        }

        int[]? found = null;
        int[]? rest = null;
        Exception? failure = null;

        Thread thread = new(
            () =>
            {
                try
                {
                    found = rows.AsQueryable().ToList(new Filter { ConditionGroup = new ConditionGroup { Conditions = { In(Operator.In) } } })
                        .Data.Select(row => row.Id).ToArray();
                    rest = rows.AsQueryable().ToList(new Filter { ConditionGroup = new ConditionGroup { Conditions = { In(Operator.NotIn) } } })
                        .Data.Select(row => row.Id).ToArray();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            },
            maxStackSize: 256 * 1024);

        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, found);
        Assert.Equal(new[] { 6, 7, 8, 9, 10 }, rest);
    }
}
