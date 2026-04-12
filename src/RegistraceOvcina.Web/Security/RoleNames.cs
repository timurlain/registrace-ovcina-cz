namespace RegistraceOvcina.Web.Security;

public static class RoleNames
{
    // System roles (access control)
    public const string Admin = "Admin";
    public const string Organizer = "Organizer";
    public const string Registrant = "Registrant";
    public const string Guest = "Guest";

    // Game roles are managed by admins in /admin/roles — no hardcoded constants

    // Staff roles (organizational)
    public const string StaffRegistration = "Staff-Registration";
    public const string StaffAccounts = "Staff-Accounts";
    public const string StaffLogistics = "Staff-Logistics";
}
