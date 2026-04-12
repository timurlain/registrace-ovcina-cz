namespace RegistraceOvcina.Web.Data;

public sealed class FoodOrder
{
    public int Id { get; set; }
    public int RegistrationId { get; set; }
    public int MealOptionId { get; set; }
    public DateTime MealDayUtc { get; set; }
    public decimal Price { get; set; }
    public Registration Registration { get; set; } = default!;
    public MealOption MealOption { get; set; } = default!;
}
