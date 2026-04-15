using System.Text.Json;

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

/// <summary>Full game info including parsed organization metadata.</summary>
public sealed record GameInfoDto(
    int Id,
    string Name,
    string? Description,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    DateTime RegistrationClosesAtUtc,
    int TargetPlayerCountTotal,
    int TotalRegistered,
    bool IsPublished,
    JsonElement? OrganizationInfo);

/// <summary>Attendee (registration) entry for a user's game info response.</summary>
public sealed record UserAttendeeDto(
    int PersonId,
    string FirstName,
    string LastName,
    int BirthYear,
    string AttendeeType,
    string? CharacterName,
    int? PreferredKingdomId);

/// <summary>Lodging assignment details for a user in a game.</summary>
public sealed record UserLodgingDto(
    string LodgingType,
    string? RoomName,
    int? RoomCapacity,
    List<string> Roommates);

/// <summary>Full user-in-game info: registration, payments, attendees, lodging, roles.</summary>
public sealed record UserGameInfoDto(
    string Email,
    int GameId,
    bool Registered,
    string? GroupName,
    DateTime? RegistrationDate,
    string PaymentStatus,
    decimal? ExpectedAmount,
    decimal? PaidAmount,
    List<UserAttendeeDto> Attendees,
    UserLodgingDto? Lodging,
    List<string> GameRoles);

/// <summary>Adult registered for a game, with optional game-role assignments.</summary>
public sealed record AdultDto(
    int PersonId,
    string FirstName,
    string LastName,
    int BirthYear,
    string? Email,
    List<string> Roles);

/// <summary>Character seed data for hra import.</summary>
public sealed record CharacterSeedDto(
    int? CharacterId,
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
