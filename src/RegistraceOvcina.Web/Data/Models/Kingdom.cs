namespace RegistraceOvcina.Web.Data;

public sealed class Kingdom
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Color { get; set; }
}
