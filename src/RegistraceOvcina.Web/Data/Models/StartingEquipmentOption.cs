namespace RegistraceOvcina.Web.Data;

public sealed class StartingEquipmentOption
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string Key { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public Game Game { get; set; } = default!;
}
