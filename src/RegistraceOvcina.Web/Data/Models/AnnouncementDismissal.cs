namespace RegistraceOvcina.Web.Data;

public sealed class AnnouncementDismissal
{
    public int Id { get; set; }
    public int AnnouncementId { get; set; }
    public string UserId { get; set; } = "";
    public DateTime DismissedAtUtc { get; set; }
    public Announcement Announcement { get; set; } = default!;
    public ApplicationUser User { get; set; } = default!;
}
