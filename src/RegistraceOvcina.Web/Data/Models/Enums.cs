namespace RegistraceOvcina.Web.Data;

public enum BalanceStatus
{
    Unpaid = 0,
    Underpaid = 1,
    Balanced = 2,
    Overpaid = 3
}

public enum ContinuityStatus
{
    Unknown = 0,
    Continued = 1,
    Retired = 2
}

public enum EmailDirection
{
    Inbound = 0,
    Outbound = 1
}

public enum PaymentMethod
{
    BankTransfer = 0,
    Cash = 1,
    ManualAdjustment = 2
}

public enum AttendeeType
{
    Player = 0,
    Adult = 1
}

public enum PlayerSubType
{
    /// <summary>hrající samostatné dítě (cca 10+, PVP hráč)</summary>
    Pvp = 0,
    /// <summary>hrající samostatné dítě (cca 8+)</summary>
    Independent = 1,
    /// <summary>hrající dítě ve skupince s hraničárem (cca 5–7)</summary>
    WithRanger = 2,
    /// <summary>hrající dítě v doprovodu rodiče (cca 4+)</summary>
    WithParent = 3
}

[Flags]
public enum AdultRoleFlags
{
    None = 0,
    /// <summary>hrát cizí postavu / příšeru (skřet, kostlivec, vlci…)</summary>
    PlayMonster = 1,
    /// <summary>pomoci organizaci (obchodník, příručí ve městech)</summary>
    OrganizationHelper = 2,
    /// <summary>technická organizace (svačiny, rozvoz jídla, spojka)</summary>
    TechSupport = 4,
    /// <summary>vést skupinku menších dětí (hraničář)</summary>
    RangerLeader = 8,
    /// <summary>pouze přihlížející</summary>
    Spectator = 16
}

public enum LodgingPreference
{
    /// <summary>Chci spát uvnitř (budova)</summary>
    Indoor = 0,
    /// <summary>Mám vlastní stan</summary>
    OwnTent = 1,
    /// <summary>Mohu spát venku / pod širákem</summary>
    CampOutdoor = 2,
    /// <summary>Neplánuji přenocovat</summary>
    NotStaying = 3
}

public enum RegistrationStatus
{
    Active = 0,
    Cancelled = 1
}

public enum SubmissionStatus
{
    Draft = 0,
    Submitted = 1,
    Cancelled = 2
}

public enum VariableSymbolStrategy
{
    PerSubmissionId = 0,
    SequentialPerGame = 1,
    ManualOverride = 2
}
