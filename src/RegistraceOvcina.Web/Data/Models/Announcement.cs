namespace RegistraceOvcina.Web.Data;

public sealed class Announcement
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string HtmlContent { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public List<AnnouncementDismissal> Dismissals { get; set; } = [];
}
