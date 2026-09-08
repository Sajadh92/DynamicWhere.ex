using DynamicWhere.ex.Enums;
using DynamicWhere.ex.Policies.Attributes;
using DynamicWhere.ex.Policies.Enums;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace DynamicWhere.API.Models;

/// <summary>
/// Employee entity with hierarchical structure and complex types.
/// </summary>
/// <remarks>
/// The policy demonstration model. No other controller queries <c>Employees</c> — the nine existing
/// suites touch Products, Orders and Customers only — which is why this type can carry
/// <see cref="DwEntityAttribute.RequirePolicy"/>, refusing every unguarded read, without failing a
/// single existing endpoint.
/// <para>
/// Every attribute here is sealed, the default: no runtime rule can lift any of it. The dynamic half
/// of the feature is demonstrated on fields that are deliberately left undecorated, so a rule from
/// the store has somewhere to act.
/// </para>
/// </remarks>
[DwEntity(RequirePolicy = true)]
public class Employee
{
    [Key]
    public Guid Id { get; set; }

    [DwDescribe(Label = "First name", Group = "Identity", Order = 1)]
    public string FirstName { get; set; } = string.Empty;

    [DwDescribe(Label = "Last name", Group = "Identity", Order = 2)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>Masked on the way out, and unsortable because sorting would rank the real value.</summary>
    /// <remarks>
    /// Design section 7.4: ordering runs in SQL against the stored value, so paging a masked column
    /// ranks the true order. Startup validation warns about exactly this pairing; [DwNoOrder] is the
    /// fix it names.
    /// </remarks>
    [DwMask(MaskStrategy.Email)]
    [DwNoOrder]
    [DwDescribe(Label = "Email", Group = "Identity", Order = 3)]
    public string Email { get; set; } = string.Empty;

    /// <summary>Addressed by a public name, and confirmable rather than searchable.</summary>
    /// <remarks>
    /// Design section 7.3: permitting WHERE on a protected field lets TotalCount count the matches
    /// without selecting anything. Restricting the operators to Equal and In is the control — a
    /// caller can confirm a code it already knows and cannot sweep for one it does not.
    /// </remarks>
    [DwAlias("Code")]
    [DwOperators(Allow = new[] { Operator.Equal, Operator.In })]
    [DwDescribe(Label = "Employee code", Group = "Identity", Order = 4)]
    public string EmployeeCode { get; set; } = string.Empty;

    /// <summary>Reduced to the year it falls in.</summary>
    [DwGeneralize(GeneralizeMode.DatePart, Part = DatePart.Year)]
    [DwDescribe(Label = "Hire date", Group = "Employment", Order = 5)]
    public DateTime HireDate { get; set; }

    public DateTime? TerminationDate { get; set; }

    /// <summary>Scopes every guarded query to serving employees, asked for or not.</summary>
    /// <remarks>
    /// The row-level boundary. A caller cannot widen it, because a forced predicate is collected and
    /// ANDed rather than elected: even a rule with higher authority can only narrow it further.
    /// </remarks>
    [DwForceWhere(Operator.Equal, Value = "true")]
    public bool IsActive { get; set; }

    /// <summary>Rounded to the nearest 5,000, aggregatable only over groups of five or more.</summary>
    /// <remarks>
    /// The k-anonymity story in one field. Rounding alone does not protect anything, because SUM,
    /// MAX and MIN run in SQL against the stored value before any transform applies — MAX(Salary)
    /// over a department of one returns that person's exact pay. So aggregation is refused by
    /// default and opted into here explicitly, and <c>MinGroupSize = 5</c> suppresses any group too
    /// small to hide an individual. Neither half is any use without the other.
    /// </remarks>
    [DwGeneralize(GeneralizeMode.Round, Step = 5000, AllowAggregate = true, MinGroupSize = 5)]
    [DwNoOrder]
    [DwCost(10)]
    [DwAudit]
    [DwDescribe(Label = "Salary", Description = "Rounded to the nearest 5,000.", Group = "Compensation", Order = 6)]
    public decimal Salary { get; set; }

    [DwAllowedValues("FullTime", "PartTime", "Contract", "Intern", "Consultant")]
    [DwDescribe(Label = "Employment type", Group = "Employment", Order = 7)]
    public EmploymentType EmploymentType { get; set; }

    /// <summary>The caller must say which department they are asking about.</summary>
    /// <remarks>
    /// Not a denial: an unscoped read of every department is the query worth refusing, and a
    /// requirement says so without also blocking the legitimate one.
    /// </remarks>
    [DwRequireWhere]
    [DwAllowedValues("Engineering", "Sales", "Support", "Finance", "Operations")]
    [DwDescribe(Label = "Department", Group = "Employment", Order = 8)]
    public string Department { get; set; } = string.Empty;

    [DwDescribe(Label = "Position", Group = "Employment", Order = 9)]
    public string Position { get; set; } = string.Empty;

    // Complex type
    public Address Address { get; set; } = new();

    // JSON field for skills
    public List<Skill> Skills { get; set; } = [];

    // JSON field for certifications
    public List<Certification> Certifications { get; set; } = [];

    /// <summary>Refused for every feature: absent from /schema, and rejected by POST /rules.</summary>
    [DwDenied]
    public JsonDocument? WorkSchedule { get; set; }

    // JSON field for emergency contacts
    public List<EmergencyContact> EmergencyContacts { get; set; } = [];

    // Self-referencing for manager hierarchy
    public Guid? ManagerId { get; set; }
    public Employee? Manager { get; set; }

    public ICollection<Employee> Subordinates { get; set; } = [];
}

public enum EmploymentType
{
    FullTime = 0,
    PartTime = 1,
    Contract = 2,
    Intern = 3,
    Consultant = 4
}

/// <summary>
/// Complex type for skills
/// </summary>
public class Skill
{
    public string Name { get; set; } = string.Empty;
    public int ProficiencyLevel { get; set; } // 1-5
    public int YearsOfExperience { get; set; }
    public DateTime? LastUsed { get; set; }
}

/// <summary>
/// Complex type for certifications
/// </summary>
public class Certification
{
    public string Name { get; set; } = string.Empty;
    public string IssuingOrganization { get; set; } = string.Empty;
    public DateTime IssueDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? CredentialId { get; set; }
}

/// <summary>
/// Complex type for emergency contacts
/// </summary>
public class EmergencyContact
{
    public string Name { get; set; } = string.Empty;

    public string Relationship { get; set; } = string.Empty;

    /// <summary>Masked through the collection walk, not just at the top level.</summary>
    /// <remarks>
    /// Employee.EmergencyContacts is a list, so reaching this member means descending into a
    /// collection and transforming every element. A field one navigation deeper than the engine
    /// walks is a field that is quietly not protected, which is why it is worth demonstrating here
    /// rather than only on a scalar.
    /// </remarks>
    [DwMask(MaskStrategy.Phone)]
    public string PhoneNumber { get; set; } = string.Empty;

    [DwMask(MaskStrategy.Phone)]
    public string? AlternatePhoneNumber { get; set; }
}
