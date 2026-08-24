using DynamicWhere.ex.Policies.Context;
using DynamicWhere.ex.Policies.Enums;

namespace DynamicWhere.Tests.Policies;

/// <summary>
/// Covers <see cref="DwPolicyContext"/> construction, subject lookup, and ambient value access.
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
}
