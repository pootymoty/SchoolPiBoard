namespace Whiteboard.LicenseServer.Data;

public class Board
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    public bool Archived { get; set; }

    public string BackgroundStyle { get; set; } = "plain";

    public string BackgroundColor { get; set; } = "#FFFFFF";

    public List<BoardMember> Members { get; set; } = new();
}
