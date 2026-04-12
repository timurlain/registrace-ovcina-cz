namespace RegistraceOvcina.Web.Data;

public sealed class Person
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public int BirthYear { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public List<Character> Characters { get; set; } = [];
    public List<Registration> Registrations { get; set; } = [];
    public List<OrganizerNote> OrganizerNotes { get; set; } = [];
}
