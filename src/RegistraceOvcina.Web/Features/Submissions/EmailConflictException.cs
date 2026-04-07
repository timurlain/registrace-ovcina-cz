namespace RegistraceOvcina.Web.Features.Submissions;

public sealed class EmailConflictException(
    int existingPersonId,
    string existingFirstName,
    string existingLastName,
    string email) : Exception($"E-mail {email} je přiřazený k osobě {existingFirstName} {existingLastName}.")
{
    public int ExistingPersonId { get; } = existingPersonId;
    public string ExistingFirstName { get; } = existingFirstName;
    public string ExistingLastName { get; } = existingLastName;
    public string ConflictEmail { get; } = email;
}
