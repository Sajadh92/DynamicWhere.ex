using System.Collections;
using System.Reflection;
using System.Text.Json;
using DynamicWhere.ex.Classes.Complex;
using DynamicWhere.ex.Classes.Core;
using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Exceptions;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>Shared helpers for the tracking, variance, converter, shared-name and collection tests.</summary>
    internal static class ZxKit
    {
        internal static PolicyQueryable<T> Guard<T>(IQueryable<T> source, DwTier tier = DwTier.Strict) where T : class =>
            source.ApplyPolicy(
                new DwPolicyContext().WithSubject(DwSubjectKind.User, "u1"),
                new DwPolicyOptions { Tier = tier, Caps = { MinGroupSize = 1 } },
                new PolicyResolver(new IDwPolicyProvider[] { new AttributePolicyProvider() }));

        internal static Filter Where(string field, string value) => new()
        {
            ConditionGroup = new ConditionGroup
            {
                Conditions = { new Condition { Sort = 1, Field = field, DataType = DataType.Text, Operator = Operator.Equal, Values = { value } } }
            }
        };

        internal static Filter Selecting(params string[] fields) => new() { Selects = fields.ToList() };

        /// <summary>The refusal code, "ran" when it ran, or the other exception.</summary>
        internal static string Code(Action run)
        {
            try
            {
                run();

                return "ran";
            }
            catch (PolicyException refusal)
            {
                return refusal.ErrorCode.ToString();
            }
            catch (Exception other)
            {
                return $"{other.GetType().Name}: {other.Message.Split('\n')[0]}";
            }
        }

        /// <summary>Runs a read; a policy refusal or a core refusal returns null (nothing reached the caller).</summary>
        internal static object? SafeRead(Func<object?> read)
        {
            try
            {
                return read();
            }
            catch (PolicyException)
            {
                return null;
            }
            catch (LogicException)
            {
                return null;
            }
        }

        internal static IEnumerable<PolicyFragment> Denials(Type type, string path) =>
            new AttributePolicyProvider().GetFragments(type, new DwPolicyContext())
                .Where(f => f.Effect == PolicyEffect.Deny && string.Equals(f.FieldPath, path, StringComparison.OrdinalIgnoreCase));

        internal static string Trace<T>(PolicyQueryable<T> guarded) where T : class =>
            string.Join(" | ", guarded.LastTrace?.Decisions
                .Where(d => d.Action != PolicyAction.Allowed)
                .Select(d => $"{d.FieldPath} {d.Action}: {d.Reason}") ?? Array.Empty<string>());

        internal static string Json(object? value)
        {
            try
            {
                return JsonSerializer.Serialize(value, new JsonSerializerOptions
                {
                    ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles
                });
            }
            catch (Exception e)
            {
                return $"<unserializable: {e.GetType().Name}>";
            }
        }

        /// <summary>Everything reachable from a value, read by runtime type (public and non-public properties and fields).</summary>
        internal static bool Holds(object? value, string text)
        {
            HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
            Stack<object?> pending = new();

            pending.Push(value);

            while (pending.Count > 0)
            {
                object? current = pending.Pop();

                if (current is null || current is MemberInfo || current is Delegate
                    || (current.GetType().Namespace ?? string.Empty).StartsWith("Microsoft.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (current is string held)
                {
                    if (held.Contains(text, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    continue;
                }

                if (current.GetType().IsPrimitive || current is decimal || current is DateTime || current is Guid)
                {
                    continue;
                }

                if (!current.GetType().IsValueType && !seen.Add(current))
                {
                    continue;
                }

                if (current is IDictionary map)
                {
                    foreach (DictionaryEntry item in map)
                    {
                        pending.Push(item.Key);
                        pending.Push(item.Value);
                    }
                }
                else if (current is IEnumerable items)
                {
                    try
                    {
                        foreach (object? item in items)
                        {
                            pending.Push(item);
                        }
                    }
                    catch
                    {
                        // An enumerator that throws holds nothing readable.
                    }
                }

                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

                foreach (PropertyInfo property in current.GetType().GetProperties(all))
                {
                    if (property.GetIndexParameters().Length != 0 || !property.CanRead)
                    {
                        continue;
                    }

                    try
                    {
                        pending.Push(property.GetValue(current));
                    }
                    catch
                    {
                        // A getter that throws holds nothing readable.
                    }
                }

                for (Type? type = current.GetType(); type is not null && type != typeof(object); type = type.BaseType)
                {
                    if (type.Namespace is { } ns && (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)
                                                     || ns.StartsWith("Microsoft.", StringComparison.Ordinal)))
                    {
                        continue;
                    }

                    foreach (FieldInfo field in type.GetFields(all | BindingFlags.DeclaredOnly))
                    {
                        try
                        {
                            pending.Push(field.GetValue(current));
                        }
                        catch
                        {
                            // Unreadable.
                        }
                    }
                }
            }

            return false;
        }
    }
}
