using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace DynamicWhere.API.Models;

/// <summary>
/// Employee entity with hierarchical structure and complex types
/// </summary>
public class Employee
{
    [Key]
    public Guid Id { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string EmployeeCode { get; set; } = string.Empty;

    public DateTime HireDate { get; set; }

    public DateTime? TerminationDate { get; set; }

    public bool IsActive { get; set; }

    public decimal Salary { get; set; }

    public EmploymentType EmploymentType { get; set; }

    public string Department { get; set; } = string.Empty;

    public string Position { get; set; } = string.Empty;

    // Complex type
    public Address Address { get; set; } = new();

    // JSON field for skills
    public List<Skill> Skills { get; set; } = [];

    // JSON field for certifications
    public List<Certification> Certifications { get; set; } = [];

    // JSON field for work schedule
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
    public string PhoneNumber { get; set; } = string.Empty;
    public string? AlternatePhoneNumber { get; set; }
}
