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

/// <summary>Game roles for a user.</summary>
public sealed record UserGameRolesDto(List<string> Roles);

/// <summary>Has-role check result.</summary>
public sealed record HasRoleDto(bool HasRole);

/// <summary>Role assignment request body.</summary>
public sealed record AssignRoleRequest(int GameId, string RoleName);

/// <summary>Character seed data for hra import.</summary>
public sealed record CharacterSeedDto(
    int CharacterId,
    int PersonId,
    string PersonFirstName,
    string PersonLastName,
    int PersonBirthYear,
    string CharacterName,
    string? Race,
    string? ClassOrType,
    string? KingdomName,
    int? KingdomId,
    int? LevelReached,
    string ContinuityStatus);
