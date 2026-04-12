namespace RegistraceOvcina.Web.Data;

public sealed class Character
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public string Name { get; set; } = "";
    public string? Race { get; set; }
    public string? ClassOrType { get; set; }
    public string? Notes { get; set; }
    public bool IsDeleted { get; set; }
    public Person Person { get; set; } = default!;
    public List<CharacterAppearance> Appearances { get; set; } = [];
}
