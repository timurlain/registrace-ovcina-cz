namespace RegistraceOvcina.Web.Security;

public static class RoleNames
{
    // System roles (access control)
    public const string Admin = "Admin";
    public const string Organizer = "Organizer";
    public const string Registrant = "Registrant";
    public const string Guest = "Guest";

    // Game roles (world identity)
    public const string King = "King";
    public const string Merchant = "Merchant";
    public const string Player = "Player";
    public const string Healer = "Healer";

    // Staff roles (organizational)
    public const string StaffRegistration = "Staff-Registration";
    public const string StaffAccounts = "Staff-Accounts";
    public const string StaffLogistics = "Staff-Logistics";
}
