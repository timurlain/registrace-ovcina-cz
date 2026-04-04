namespace RegistraceOvcina.Web.Features.Integration;

/// <summary>Publicly safe game info — excludes payment/pricing fields.</summary>
public sealed record GameDto(
    int Id,
    string Name,
    string? Description,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    DateTime RegistrationClosesAtUtc,
    int TargetPlayerCountTotal,
    bool IsPublished);

/// <summary>Non-sensitive registration record for sibling apps.</summary>
public sealed record RegistrationDto(
    int RegistrationId,
    int PersonId,
    string FirstName,
    string LastName,
    int BirthYear,
    string AttendeeType,
    string? CharacterName,
    string Status);

/// <summary>Presence check result.</summary>
public sealed record PresenceCheckDto(bool IsRegistered);
