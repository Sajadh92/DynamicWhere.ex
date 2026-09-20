using DynamicWhere.ex.Policies.Config;

namespace DynamicWhere.Tests.Policies
{
    /// <summary>
    /// The one posture this test assembly configures, whichever suite asks for it first.
    /// </summary>
    /// <remarks>
    /// <c>DwPolicy</c> is process-wide, so a suite that configures a posture of its own decides what
    /// every other suite in the run sees. Since 3.3.0 a second call asking for the same posture is a
    /// no-op, which is what lets several suites call this without a lock — but only while they all
    /// ask for the <i>same</i> posture, which is what this type is for.
    /// <para>
    /// The demo API's <c>Employee</c> is exposed by name rather than by reference: the floor leg
    /// drops the API project along with the rest of the EF Core 8 graph, and the endpoint suites
    /// that need the type are dropped with it.
    /// </para>
    /// </remarks>
    internal static class PolicyBootstrap
    {
        /// <summary>The name the endpoint suites address <c>Staff</c> by.</summary>
        internal const string StaffName = "staff";

        /// <summary>Configures the posture, or confirms the one already in force is it.</summary>
        internal static void Ensure()
        {
            DwPolicyOptions options = new();

            options.Entities
                .Expose<Staff>(StaffName)
                .Expose<Person>();

            if (Type.GetType("DynamicWhere.API.Models.Employee, DynamicWhere.API") is { } employee)
            {
                options.Entities.Expose(employee, "Employee");
            }

            DwPolicy.Configure(options);
        }
    }
}
