namespace RegistraceOvcina.Web.Data;

public sealed class Room
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int DefaultCapacity { get; set; }
}
