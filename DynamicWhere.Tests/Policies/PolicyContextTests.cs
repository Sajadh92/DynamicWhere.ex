using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;
using DynamicWhere.ex.Policies.Storage;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="DwPolicyContext"/> construction, subject lookup, and ambient value access,
/// together with <see cref="DwSubject"/> equality, hashing, and identity normalisation.
/// </summary>
public class PolicyContextTests
{
    [Fact]
    public void Context_exposes_the_subjects_it_was_built_with()
    {
        DwPolicyContext ctx = new DwPolicyContext()
            .WithSubject(DwSubjectKind.User, "123")
            .WithSubject(DwSubjectKind.Role, "Manager")
            .WithSubject(DwSubjectKind.Role, "Auditor");

        Assert.Equal(3, ctx.Subjects.Count);
        Assert.Contains(ctx.Subjects, s => s.Kind == DwSubjectKind.User && s.Identity == "123");
        Assert.Equal(2, ctx.Subjects.Count(s => s.Kind == DwSubjectKind.Role));
    }

    [Fact]
    public void Identities_returns_every_identity_for_one_kind()
    {
        DwPolicyContext ctx = new DwPolicyContext()
            .WithSubject(DwSubjectKind.Role, "Manager")
            .WithSubject(DwSubjectKind.Role, "Auditor");

        string[] roles = ctx.Identities(DwSubjectKind.Role).OrderBy(r => r, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "Auditor", "Manager" }, roles);
    }

    [Fact]
    public void Ambient_values_round_trip()
    {
        DwPolicyContext ctx = new DwPolicyContext().WithValue("TenantId", 5);

        Assert.True(ctx.TryGetValue("TenantId", out object? value));
        Assert.Equal(5, value);
        Assert.False(ctx.TryGetValue("Missing", out _));
    }

    [Fact]
    public void Subject_identity_may_not_be_blank_for_an_identified_kind()
    {
        DwPolicyContext ctx = new DwPolicyContext();

        Assert.Throws<ArgumentException>(() => ctx.WithSubject(DwSubjectKind.Role, "  "));
    }

    [Fact]
    public void Duplicate_subjects_are_ignored_rather_than_stored_twice()
    {
        DwPolicyContext ctx = new DwPolicyContext()
            .WithSubject(DwSubjectKind.Role, "Manager")
            .WithSubject(DwSubjectKind.Role, "Manager");

        Assert.Single(ctx.Subjects);
    }

    [Fact]
    public void Subjects_differing_only_in_identity_casing_are_equal()
    {
        DwSubject declared = new DwSubject(DwSubjectKind.Role, "Manager");
        DwSubject fromToken = new DwSubject(DwSubjectKind.Role, "manager");

        Assert.Equal(declared, fromToken);
    }

    [Fact]
    public void Equal_subjects_share_a_hash_code()
    {
        DwSubject declared = new DwSubject(DwSubjectKind.Role, "Manager");
        DwSubject fromToken = new DwSubject(DwSubjectKind.Role, "manager");

        Assert.Equal(declared.GetHashCode(), fromToken.GetHashCode());
    }

    [Fact]
    public void Subject_identity_keeps_the_casing_it_was_supplied_with()
    {
        DwSubject subject = new DwSubject(DwSubjectKind.Role, "  Manager  ");

        Assert.Equal("Manager", subject.Identity);
        Assert.Equal("Role:Manager", subject.ToString());
    }

    [Fact]
    public void Global_subjects_accept_a_blank_identity_and_expose_it_as_empty()
    {
        DwSubject subject = new DwSubject(DwSubjectKind.Global, "   ");

        Assert.Equal(string.Empty, subject.Identity);
        Assert.Equal("Global", subject.ToString());
    }
    [Fact]
    public void An_attachment_is_held_per_provider_and_absent_until_one_prepares()
    {
        DwPolicyContext context = new();
        object first = new();
        object second = new();

        // Absent, not empty. The difference is what lets a store provider tell "this caller has no
        // user rules" from "nobody looked", and the second is refused rather than served.
        Assert.Null(context.AttachmentFor(first));

        PolicyAttachment attachment = new(
            StoreSnapshot.Empty, DateTimeOffset.UtcNow, NarrowZone.Empty, Array.Empty<string>());

        context.Attach(first, attachment);

        Assert.Same(attachment, context.AttachmentFor(first));

        // Keyed on the provider: a second store's absence is not answered by the first's presence.
        Assert.Null(context.AttachmentFor(second));
    }

    [Fact]
    public void Preparing_again_replaces_what_was_pinned()
    {
        DwPolicyContext context = new();
        object provider = new();

        context.Attach(
            provider,
            new PolicyAttachment(
                StoreSnapshot.Empty, DateTimeOffset.UtcNow, NarrowZone.Empty, Array.Empty<string>()));

        PolicyAttachment second = new(
            new StoreSnapshot(41, DateTimeOffset.UtcNow, Array.Empty<PolicyRule>()),
            DateTimeOffset.UtcNow,
            NarrowZone.Empty,
            Array.Empty<string>());

        context.Attach(provider, second);

        Assert.Equal(41, context.AttachmentFor(provider)!.Snapshot.Version);
    }
}
