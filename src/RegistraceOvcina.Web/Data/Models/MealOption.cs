namespace RegistraceOvcina.Web.Data;

public sealed class MealOption
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public bool IsActive { get; set; } = true;
    public Game Game { get; set; } = default!;
}
