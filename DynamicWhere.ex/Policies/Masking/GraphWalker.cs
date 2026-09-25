using System.Collections;
using System.Collections.Concurrent;
using System.Linq.Dynamic.Core;
using System.Reflection;
using DynamicWhere.ex.Optimization.Cache.Source;
using DynamicWhere.ex.Policies.Config;
using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.DTOs;
using DynamicWhere.ex.Policies.Resolution;
using DynamicWhere.ex.Policies.Source;

namespace DynamicWhere.ex.Policies.Masking;

/// <summary>
/// Applies a type's transform chains to a materialized result.
/// </summary>
/// <remarks>
/// Driven by the paths the policy names rather than by a blind walk of the object graph. The set of
/// paths that need transforming is known before a single row is read, so the walker navigates
/// exactly those and nothing else — which is both far cheaper on a wide entity and, more
/// importantly, incapable of missing a path because it failed to recognise a navigation. That was
/// the shape of five separate defects on the input side.
/// <para>
/// A path is not the only place a value sits. A member only a subtype of the row's type declares,
/// one past the four segments the policy names, one on an object a dictionary holds: no path reaches
/// any of them, and each came back exactly as stored. A second pass walks the rows themselves, by
/// run-time type, and applies the transform a member's own attributes declare wherever the first
/// pass did not. It reads only what can lead to such a member, and runs only for a model that
/// declares a transform at all.
/// </para>
/// <para>
/// The objects being written to are detached. That is what makes this safe at all: a transform
/// applied to a tracked entity is recorded by EF as a pending modification and written back as the
/// real value on the next save anywhere in the same unit of work.
/// </para>
/// <para>
/// This is the only part of the policy layer whose cost grows with the size of the result, so it is
/// the only part written for it. Everything that can be hoisted out of the per-row loop is: the set
/// of root objects is built once for the whole result rather than once per path, and the property
/// and its compiled accessors are resolved once per runtime type rather than once per row. What is
/// left per value is a delegate call, the transform itself, and a second delegate call — which is
/// as close to the floor as changing a value after materialization can get.
/// </para>
/// </remarks>
internal static class GraphWalker
{
    /// <summary>
    /// Transforms every value the policy speaks to, across a materialized result.
    /// </summary>
    /// <param name="rows">The materialized objects. Nulls are skipped.</param>
    /// <param name="entityType">
    /// The type the query was written over, which says whether anything its rows can hold declares a
    /// transform where no path reaches.
    /// </param>
    /// <param name="policy">The type's transform chains, keyed by canonical path.</param>
    /// <param name="projected">
    /// The paths the result actually carries, or null when it carries the whole entity. A path
    /// outside this set was never selected, so there is no value present to transform.
    /// </param>
    /// <param name="context">Who is asking.</param>
    /// <param name="options">Carries the hash salt, the token vault and the service provider.</param>
    /// <param name="trace">Collects what was transformed.</param>
    /// <param name="attributes">
    /// False when the resolver in force was built over no attribute provider. It reads no attribute
    /// along a path, and none off one either.
    /// </param>
    /// <param name="past">
    /// The chains of members the projection names past the attribute walk's depth, keyed by path, or
    /// null. Applied by path beside the type's own, so a generated row is reached as any other is.
    /// </param>
    /// <returns>
    /// Each audited member the rows hand back where no path names it, once per path, for the caller to
    /// record: the gate records a use by path before the query runs, and these have none it could ask
    /// about.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a path the result should carry cannot be reached on it. Skipping instead would
    /// emit the value untransformed, which is the failure this whole layer exists to prevent.
    /// </exception>
    internal static IReadOnlyList<UnnamedRead> Apply(
        IEnumerable rows,
        Type entityType,
        TypePolicy policy,
        IReadOnlyCollection<string>? projected,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyTrace trace,
        bool attributes = true,
        IReadOnlyDictionary<string, ValueTransform>? past = null)
    {
        // Whether anything a row of this type can hold declares a transform at all. False for a model
        // with no transform attribute, which then pays nothing for the second pass below, and for a
        // resolver built over no attribute provider, which reads no attribute anywhere.
        bool unwalked = attributes && (HoldsTransform(entityType) || HoldsAudit(entityType));

        if (policy.Transforms.Count == 0 && !unwalked && past is null)
        {
            return Array.Empty<UnnamedRead>();
        }

        // Reference identity, not equality. Two rows can share one referenced object, and
        // transforming it twice would hash a hash or truncate a truncation.
        //
        // Built once for the whole result rather than once per transformed path. It was the second,
        // and on a result of ten thousand rows that is a ten-thousand-entry set allocated, filled
        // and discarded for every field the policy transforms — several times the cost of the
        // masking it was there to support.
        HashSet<object> roots = new(ReferenceComparer.Instance);

        foreach (object? row in rows)
        {
            if (row is not null)
            {
                roots.Add(row);
            }
        }

        if (roots.Count == 0)
        {
            return Array.Empty<UnnamedRead>();
        }

        // What the first pass transformed, so the second never transforms it again: a hash of a hash
        // is not the token the first pass issued. Kept only when there is a second pass to read it.
        HashSet<(object Owner, string Member)>? done = unwalked ? new(DoneComparer.Instance) : null;

        foreach (KeyValuePair<string, ValueTransform> entry in policy.Transforms)
        {
            if (!IsCarried(entry.Key, projected))
            {
                continue;
            }

            ApplyPath(roots, entry.Key, entry.Value, context, options, trace, done);
        }

        // Members the projection names past the walk's depth, which the type's list does not hold.
        // Every one of them is carried: the projection is where they were read from.
        if (past is not null)
        {
            foreach (KeyValuePair<string, ValueTransform> entry in past)
            {
                ApplyPath(roots, entry.Key, entry.Value, context, options, trace, done);
            }
        }

        return unwalked
            ? ApplyUnwalked(roots, entityType, projected, context, options, trace, done!)
            : Array.Empty<UnnamedRead>();
    }

    /// <summary>
    /// One audited member the rows handed back where no path of the policy names it, and the effect it
    /// was handed back with.
    /// </summary>
    internal readonly record struct UnnamedRead(string Path, Enums.PolicyEffect Effect);

    /// <summary>
    /// True when a result carrying this projection holds the path at all.
    /// </summary>
    /// <remarks>
    /// A null projection means the whole entity, so every path is carried. Otherwise a path is
    /// carried when it was selected, or when it sits beneath something that was: projecting
    /// <c>Contact</c> brings <c>Contact.Email</c> with it.
    /// </remarks>
    /// <summary>True when the projection names this very path, rather than a member above it.</summary>
    private static bool Spelled(string path, IReadOnlyCollection<string>? projected)
    {
        if (projected is null)
        {
            return false;
        }

        foreach (string selected in projected)
        {
            if (string.Equals(selected, path, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCarried(string path, IReadOnlyCollection<string>? projected)
    {
        if (projected is null)
        {
            return true;
        }

        foreach (string selected in projected)
        {
            if (string.Equals(selected, path, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(selected + ".", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Navigates one path from every root and transforms the value it lands on.</summary>
    /// <remarks>
    /// The root set is read and never written, so every path walks from the same one. A path with
    /// no navigation in it — which is most of them — does not build a set at all.
    /// </remarks>
    private static void ApplyPath(
        HashSet<object> roots,
        string path,
        ValueTransform chain,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyTrace trace,
        HashSet<(object Owner, string Member)>? done)
    {
        string[] segments = path.Split('.');

        IReadOnlyCollection<object> owners = roots;

        // A struct on the way down is a copy in a box, so what is written into it has to be written
        // back into whatever held it, innermost first, once the leaf is done.
        List<Action>? writeBacks = null;

        try
        {
            for (int i = 0; i < segments.Length - 1; i++)
            {
                owners = Descend(owners, segments[i], path, ref writeBacks);

                if (owners.Count == 0)
                {
                    return;
                }
            }

            Transform(owners, segments[^1], path, chain, context, options, trace, done);
        }
        finally
        {
            WriteBack(writeBacks);
        }
    }

    /// <summary>Transforms one member on every owner a path reached.</summary>
    private static void Transform(
        IReadOnlyCollection<object> owners,
        string memberName,
        string path,
        ValueTransform chain,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyTrace trace,
        HashSet<(object Owner, string Member)>? done)
    {
        string member = memberName;

        bool transformed = false;

        // One entry, not a dictionary. A materialized result is one runtime type in every case that
        // matters — entities of one class, or rows of one generated projection — so a single
        // remembered type turns the per-row property lookup into a reference comparison. A result
        // that really did alternate types would fall back to the lookup it does today.
        Type? resolvedFor = null;
        Accessors accessors = default;

        foreach (object owner in owners)
        {
            Type type = owner.GetType();

            if (!ReferenceEquals(type, resolvedFor))
            {
                accessors = Accessors.For(type, member, path);
                resolvedFor = type;
            }

            object? value = accessors.Get(owner);

            object? replacement = TransformPipeline.Apply(
                chain, value, accessors.MemberType, new DwTransformContext(owner, path, context), options);

            if (accessors.Set is null)
            {
                throw new InvalidOperationException(
                    $"'{path}' is transformed by policy but has no setter, so the transformed value " +
                    "cannot replace the real one. Give the member a setter, or project into a type " +
                    "that has one.");
            }

            accessors.Set(owner, replacement);

            done?.Add((owner, accessors.Name));

            transformed = true;
        }

        if (transformed)
        {
            trace.Add(new PolicyDecision(
                path, Enums.PolicyFeature.Select, chain.Action,
                string.Join(" then ", chain.Stages.Select(s => s.Kind))));
        }
    }

    /// <summary>
    /// Steps one segment down, following collections into their elements.
    /// </summary>
    /// <remarks>
    /// A null navigation contributes nothing rather than failing: there is no value beneath it to
    /// leave untransformed. A missing <em>property</em> is a different matter and does fail, because
    /// it means the result is not the shape the policy was resolved against.
    /// </remarks>
    private static HashSet<object> Descend(
        IReadOnlyCollection<object> owners, string segment, string path, ref List<Action>? writeBacks)
    {
        HashSet<object> next = new(ReferenceComparer.Instance);

        Type? resolvedFor = null;
        Accessors accessors = default;

        foreach (object owner in owners)
        {
            Type type = owner.GetType();

            if (!ReferenceEquals(type, resolvedFor))
            {
                accessors = Accessors.For(type, segment, path);
                resolvedFor = type;
            }

            object? value = accessors.Get(owner);

            if (value is null)
            {
                continue;
            }

            if (value is IEnumerable collection and not string)
            {
                Elements(collection, path, next, ref writeBacks);

                continue;
            }

            if (value.GetType().IsValueType)
            {
                (writeBacks ??= new List<Action>()).Add(Into(owner, value, accessors.Set, path));
            }

            next.Add(value);
        }

        return next;
    }

    /// <summary>
    /// Adds a collection's elements to the next level, each struct among them with the write that puts
    /// it back where it was read from.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a collection holds structs and cannot be written by position. The transform would
    /// otherwise land in copies and every element would be emitted as stored.
    /// </exception>
    private static void Elements(IEnumerable collection, string path, HashSet<object> next, ref List<Action>? writeBacks)
    {
        IList? positions = collection as IList;

        int index = -1;

        foreach (object? element in collection)
        {
            index++;

            if (element is null)
            {
                continue;
            }

            if (element.GetType().IsValueType)
            {
                if (positions is null || positions.IsReadOnly)
                {
                    throw new InvalidOperationException(
                        $"'{path}' is transformed by policy and passes through a collection of structs " +
                        $"({collection.GetType().Name}) that cannot be written by position, so the transformed " +
                        "values cannot replace the real ones. Hold the structs in a list or an array.");
                }

                int at = index;

                (writeBacks ??= new List<Action>()).Add(() => positions[at] = element);
            }

            next.Add(element);
        }
    }

    /// <summary>The write that puts a struct, changed in its box, back into the member it was read from.</summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the member has no setter. The transform would land in a copy and the struct would be
    /// emitted as stored.
    /// </exception>
    private static Action Into(object owner, object box, Action<object, object?>? set, string path)
    {
        if (set is null)
        {
            throw new InvalidOperationException(
                $"'{path}' is transformed by policy and passes through a struct held by a member with no " +
                "setter, so the transformed value cannot replace the real one. Give the member a setter, or " +
                "project into a type that has one.");
        }

        return () => set(owner, box);
    }

    /// <summary>Runs the write-backs a walk collected, innermost first.</summary>
    /// <remarks>
    /// A struct inside a struct is written into its outer box before that box is written into what held
    /// it, so the outermost write carries every change beneath it.
    /// </remarks>
    private static void WriteBack(List<Action>? writeBacks)
    {
        if (writeBacks is null)
        {
            return;
        }

        for (int i = writeBacks.Count - 1; i >= 0; i--)
        {
            writeBacks[i]();
        }
    }

    /// <summary>
    /// One property of one runtime type, with its compiled accessors already resolved.
    /// </summary>
    /// <remarks>
    /// A struct so that remembering one costs nothing. It exists to be resolved once and read many
    /// times, which is the whole of the optimisation: the three lookups behind it — the property by
    /// name, the getter, the setter — are each a hash of something, and each was being paid on
    /// every row of every result.
    /// </remarks>
    /// <summary>
    /// Applies the transform a member's own attributes declare, wherever in the materialized rows the
    /// pass above did not reach that member.
    /// </summary>
    /// <remarks>
    /// The attribute walk names the paths of the declared types, four segments deep, and the pass
    /// above transforms along those paths and no others. A value can sit where none of them goes: on
    /// a member only a subtype of the row's type declares, five segments down a graph somebody
    /// included, on an object a dictionary holds, or the far side of a cycle. Each came back exactly as
    /// stored, under a mask that the same member wears four segments up.
    /// <para>
    /// So the rows are walked as they are, by run-time type, and a member that declares a transform
    /// and was not transformed above is transformed by its own attributes. No rule can take a
    /// transform off a member, so where the pass above left one untouched it is because no path led
    /// there, never because an election decided against it. Only what can lead to such a member is
    /// read: a navigation whose type can reach no transform is never touched, so a lazy loader behind
    /// it is not woken.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<UnnamedRead> ApplyUnwalked(
        HashSet<object> roots,
        Type entityType,
        IReadOnlyCollection<string>? projected,
        DwPolicyContext context,
        DwPolicyOptions options,
        PolicyTrace trace,
        HashSet<(object Owner, string Member)> done)
    {
        HashSet<object> seen = new(ReferenceComparer.Instance);
        Stack<Visit> pending = new();
        Dictionary<string, ValueTransform> recorded = new(StringComparer.Ordinal);
        Dictionary<string, Enums.PolicyEffect> read = new(StringComparer.Ordinal);

        // Every struct the walk read into, in the order it was met, so the ones a transform changed are
        // written back innermost first once the walk is done.
        List<Carry> carries = new();

        foreach (object root in roots)
        {
            seen.Add(root);
            pending.Push(new Visit(root, entityType, 0, string.Empty, null));
        }

        while (pending.Count > 0)
        {
            Visit visit = pending.Pop();

            foreach (Member member in Members(visit.Node.GetType()))
            {
                string path = visit.Path.Length == 0
                    ? member.Property.Name
                    : visit.Path + "." + member.Property.Name;

                // Named by the policy: a member the declared type has, within the walk's depth. The
                // gate asked about that path before the query ran and recorded its use; the generated
                // row of a dynamic projection carries the entity's member names and is read as it is.
                PropertyInfo? named = visit.Declared is not null && visit.Depth < AttributePolicyProvider.MaxDepth
                    ? CacheReflection.FindProperty(visit.Declared, member.Property.Name)
                    : null;

                bool carried = IsCarried(path, projected);

                // A struct is a fresh box on every read, so what the pass above did to it is not in done.
                // A member the policy names on one was transformed there, along its path, and written back.
                bool handled = visit.Carry is not null && named is not null;

                if (member.Transform is { } chain && carried && !handled && done.Add((visit.Node, member.Property.Name)))
                {
                    if (member.Set is null)
                    {
                        throw new InvalidOperationException(
                            $"'{path}' is transformed by policy but has no setter, so the transformed value " +
                            "cannot replace the real one. Give the member a setter, or project into a type " +
                            "that has one.");
                    }

                    member.Set(visit.Node, TransformPipeline.Apply(
                        chain, member.Get(visit.Node), member.Property.PropertyType,
                        new DwTransformContext(visit.Node, path, context), options));

                    if (visit.Carry is not null)
                    {
                        visit.Carry.Changed = true;
                    }

                    recorded[path] = chain;
                }

                // An audited member no path names was handed back with nothing written down: the gate
                // records by path, and this one has none. Once per path, not per row, as a named one is.
                // A path the projection spells out is one the gate was asked about, however long it is.
                if (member.Audited && named is null && carried && !Spelled(path, projected))
                {
                    read[path] = member.Transform is null ? Enums.PolicyEffect.Allow : Enums.PolicyEffect.Mask;
                }

                if (!member.Leads)
                {
                    continue;
                }

                Type? beneath = named is null
                    ? null
                    : AttributePolicyProvider.NavigationTypeOf(named.PropertyType);

                object? value = member.Get(visit.Node);

                foreach ((object held, int position) in Held(value))
                {
                    if (!seen.Add(held))
                    {
                        continue;
                    }

                    Carry? carry = null;

                    if (held.GetType().IsValueType)
                    {
                        carry = new Carry(visit.Carry, path, WriteFor(visit.Node, member.Set, value, held, position));
                        carries.Add(carry);
                    }

                    pending.Push(new Visit(held, beneath, visit.Depth + 1, path, carry));
                }
            }
        }

        for (int i = carries.Count - 1; i >= 0; i--)
        {
            carries[i].Settle();
        }

        foreach (KeyValuePair<string, ValueTransform> entry in recorded)
        {
            trace.Add(new PolicyDecision(
                entry.Key, Enums.PolicyFeature.Select, entry.Value.Action,
                string.Join(" then ", entry.Value.Stages.Select(s => s.Kind))
                + " (declared on the member; no path of the policy names it)"));
        }

        if (read.Count == 0)
        {
            return Array.Empty<UnnamedRead>();
        }

        List<UnnamedRead> reads = new(read.Count);

        foreach (KeyValuePair<string, Enums.PolicyEffect> entry in read)
        {
            reads.Add(new UnnamedRead(entry.Key, entry.Value));
        }

        return reads;
    }

    /// <summary>
    /// One object of the result, where it stands, the declared type the policy read it as, and, for a
    /// struct, what writes it back.
    /// </summary>
    private readonly record struct Visit(object Node, Type? Declared, int Depth, string Path, Carry? Carry);

    /// <summary>
    /// A struct the walk read into, changed or not, and the write that puts it back where it was read
    /// from.
    /// </summary>
    /// <remarks>
    /// Written back only when something inside it changed, so a struct no transform touched is never
    /// written, and one held where it cannot be written back fails only when a transform landed in it.
    /// </remarks>
    private sealed class Carry
    {
        private readonly Carry? _outer;
        private readonly string _path;
        private readonly Action? _write;

        internal Carry(Carry? outer, string path, Action? write)
        {
            _outer = outer;
            _path = path;
            _write = write;
        }

        /// <summary>True once a transform landed in this struct, or in one it holds.</summary>
        internal bool Changed { get; set; }

        /// <summary>Writes the struct back when it changed, and marks the struct holding it as changed.</summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when it changed and cannot be written back: the transformed value would be dropped with
        /// the copy and the stored one emitted.
        /// </exception>
        internal void Settle()
        {
            if (!Changed)
            {
                return;
            }

            if (_write is null)
            {
                throw new InvalidOperationException(
                    $"'{_path}' holds a struct whose member is transformed by policy, and it cannot be " +
                    "written back where it was read from, so the transformed value cannot replace the real " +
                    "one. Give the member a setter, and hold structs in a list or an array.");
            }

            _write();

            if (_outer is not null)
            {
                _outer.Changed = true;
            }
        }
    }

    /// <summary>
    /// The write that puts a struct read from a member back into it: into the member itself, or into the
    /// position of the collection it was read from. Null when neither can be written.
    /// </summary>
    /// <param name="owner">The object the member was read from.</param>
    /// <param name="set">The member's setter, or null when it has none.</param>
    /// <param name="value">What the member held.</param>
    /// <param name="held">The struct, in its box.</param>
    /// <param name="position">The struct's position in the collection the member held, or -1 for the member's own value.</param>
    private static Action? WriteFor(object owner, Action<object, object?>? set, object? value, object held, int position)
    {
        if (position < 0)
        {
            return set is null ? null : () => set(owner, held);
        }

        return value is IList positions && !positions.IsReadOnly
            ? () => positions[position] = held
            : null;
    }

    /// <summary>One readable member of a run-time type that transforms or is audited, or can lead to one.</summary>
    private sealed record Member(
        PropertyInfo Property,
        Func<object, object?> Get,
        Action<object, object?>? Set,
        ValueTransform? Transform,
        bool Audited,
        bool Leads);

    /// <summary>
    /// The members of a run-time type the second pass reads: those declaring a transform or an audit,
    /// and those whose value can hold an object that does. Read once per type and again once another assembly
    /// has loaded, since that can add a subtype.
    /// </summary>
    private static Member[] Members(Type type)
    {
        int epoch = KnownSubtypes.Epoch;

        if (MembersByType.TryGetValue(type, out (int Epoch, Member[] Members) known) && known.Epoch == epoch)
        {
            return known.Members;
        }

        List<Member> members = new();

        if (Reads(type))
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                ValueTransform? transform = AttributePolicyProvider.TransformOn(property);
                bool audited = AuditsSelect(property);
                bool leads = HoldsTransform(property.PropertyType) || HoldsAudit(property.PropertyType);

                if (transform is not null || audited || leads)
                {
                    members.Add(new Member(
                        property, MutatorCache.Getter(property), MutatorCache.Setter(property),
                        transform, audited, leads));
                }
            }
        }

        Member[] read = members.ToArray();

        MembersByType[type] = (epoch, read);

        return read;
    }

    private static readonly ConcurrentDictionary<Type, (int Epoch, Member[] Members)> MembersByType = new();

    /// <summary>
    /// True for an object whose members are an application's: its own types, the rows a dynamic
    /// projection generates, and the pair a dictionary hands out. A framework object is a value here.
    /// </summary>
    private static bool Reads(Type type) =>
        !AttributePolicyProvider.IsFramework(type)
        || typeof(DynamicClass).IsAssignableFrom(type)
        || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>));

    /// <summary>
    /// The objects a value holds, each with its position in the collection it came from: itself, at -1,
    /// or each element when it is a collection.
    /// </summary>
    private static IEnumerable<(object Held, int Position)> Held(object? value)
    {
        if (value is null or string)
        {
            yield break;
        }

        if (value is IEnumerable collection)
        {
            int position = -1;

            foreach (object? element in collection)
            {
                position++;

                if (element is not null and not string && !element.GetType().IsPrimitive)
                {
                    yield return (element, position);
                }
            }

            // An application's own collection class declares members beside its elements.
            if (AttributePolicyProvider.IsFramework(value.GetType()))
            {
                yield break;
            }
        }

        yield return (value, -1);
    }

    /// <summary>
    /// True when a value of this type can hold, at any depth, a member that declares a transform.
    /// </summary>
    /// <remarks>
    /// Wider than the attribute walk on purpose, as the projection gate's own scan is: every property,
    /// every collection layer, the arguments of a framework generic, and every loaded subtype, with
    /// the types already read remembered rather than levels counted. A member that can hold an object
    /// of any type, one typed <see cref="object"/> or a collection that is not generic, says nothing
    /// about what it holds and is not read, here or by the projection gate. Read once per type, and
    /// again once another assembly has loaded.
    /// </remarks>
    internal static bool HoldsTransform(Type type)
    {
        int epoch = KnownSubtypes.Epoch;

        if (HoldsByType.TryGetValue(type, out (int Epoch, bool Holds) known) && known.Epoch == epoch)
        {
            return known.Holds;
        }

        bool holds = Scan(type, static property => AttributePolicyProvider.TransformOn(property) is not null);

        HoldsByType[type] = (epoch, holds);

        return holds;
    }

    private static readonly ConcurrentDictionary<Type, (int Epoch, bool Holds)> HoldsByType = new();

    /// <summary>
    /// True when a value of this type can hold, at any depth, a member audited for Select. Read as
    /// <see cref="HoldsTransform"/> is, and for the same reason: the second pass runs only where there
    /// is something for it to find.
    /// </summary>
    internal static bool HoldsAudit(Type type)
    {
        int epoch = KnownSubtypes.Epoch;

        if (AuditsByType.TryGetValue(type, out (int Epoch, bool Holds) known) && known.Epoch == epoch)
        {
            return known.Holds;
        }

        bool holds = Scan(type, AuditsSelect);

        AuditsByType[type] = (epoch, holds);

        return holds;
    }

    private static readonly ConcurrentDictionary<Type, (int Epoch, bool Holds)> AuditsByType = new();

    /// <summary>True when a member's own attribute audits it for Select.</summary>
    private static bool AuditsSelect(PropertyInfo property) =>
        property.GetCustomAttribute<Attributes.DwAuditAttribute>(inherit: true) is { } audit
        && (audit.Features & Enums.PolicyFeature.Select) != 0;

    private static bool Scan(Type root, Func<PropertyInfo, bool> found)
    {
        HashSet<Type> seen = new();
        Stack<Type> pending = new();

        void Enqueue(Type candidate)
        {
            foreach (Type layer in AttributePolicyProvider.Layers(candidate))
            {
                if (layer.IsGenericType && AttributePolicyProvider.IsFramework(layer))
                {
                    foreach (Type argument in layer.GetGenericArguments())
                    {
                        Enqueue(argument);
                    }
                }

                if (layer == typeof(string) || layer.IsPrimitive || layer.IsEnum)
                {
                    continue;
                }

                if (!AttributePolicyProvider.IsFramework(layer) && seen.Add(layer))
                {
                    pending.Push(layer);
                }

                // An application's subclass of the type, a framework class's included.
                if (!layer.IsSealed)
                {
                    foreach (Type subtype in KnownSubtypes.Of(layer))
                    {
                        if (seen.Add(subtype))
                        {
                            pending.Push(subtype);
                        }
                    }
                }
            }
        }

        Enqueue(root);

        while (pending.Count > 0)
        {
            foreach (PropertyInfo property in pending.Pop().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                if (found(property))
                {
                    return true;
                }

                Enqueue(property.PropertyType);
            }
        }

        return false;
    }

    private readonly struct Accessors
    {
        private Accessors(
            string name, Type memberType, Func<object, object?> get, Action<object, object?>? set)
        {
            Name = name;
            MemberType = memberType;
            Get = get;
            Set = set;
        }

        /// <summary>The member's name as its type declares it, whatever letter case the path used.</summary>
        internal string Name { get; }

        /// <summary>The type the transformed value has to fit.</summary>
        internal Type MemberType { get; }

        /// <summary>Reads the value from an instance.</summary>
        internal Func<object, object?> Get { get; }

        /// <summary>Writes the value onto an instance, or null when the member is read-only.</summary>
        internal Action<object, object?>? Set { get; }

        /// <summary>
        /// Resolves a property on a runtime type.
        /// </summary>
        /// <remarks>
        /// The runtime type, never the declared one: a dynamic projection's type is generated per
        /// query shape, and the declared type of a row in one is <see cref="object"/>.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the type does not carry the member. The result is then not the shape the
        /// policy was resolved against, and going on would emit the value exactly as stored.
        /// </exception>
        internal static Accessors For(Type type, string segment, string path)
        {
            PropertyInfo property = CacheReflection.FindProperty(type, segment)
                ?? throw new InvalidOperationException(
                    $"'{path}' is transformed by policy, but '{segment}' does not exist on " +
                    $"{type.Name}. The value would otherwise be emitted exactly as stored.");

            return new Accessors(
                property.Name, property.PropertyType, MutatorCache.Getter(property), MutatorCache.Setter(property));
        }
    }

    /// <summary>One member of one object: the object by reference, the name as its type declares it.</summary>
    private sealed class DoneComparer : IEqualityComparer<(object Owner, string Member)>
    {
        internal static DoneComparer Instance { get; } = new();

        public bool Equals((object Owner, string Member) x, (object Owner, string Member) y) =>
            ReferenceEquals(x.Owner, y.Owner) && string.Equals(x.Member, y.Member, StringComparison.Ordinal);

        public int GetHashCode((object Owner, string Member) pair) =>
            HashCode.Combine(
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(pair.Owner),
                StringComparer.Ordinal.GetHashCode(pair.Member));
    }

    /// <summary>Compares by reference, so two equal-but-distinct objects are both visited.</summary>
    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static ReferenceComparer Instance { get; } = new();

        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

        public int GetHashCode(object obj) =>
            System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
