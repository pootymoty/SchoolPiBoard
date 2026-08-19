namespace Whiteboard.LicenseServer.Data;

/// <summary>Роли на доске. Порядок значений — от большего к меньшему по правам.</summary>
public enum BoardRole
{
    Viewer = 0,
    Editor = 1,
    Owner = 2
}

/// <summary>Участник доски.</summary>
public class BoardMember
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BoardId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>owner | editor | viewer — строкой, чтобы роль читалась прямо в базе.</summary>
    public string Role { get; set; } = BoardRoles.Viewer;

    public DateTime InvitedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}

/// <summary>Имена ролей и их разбор. Роль хранится строкой, а сравнивается перечислением.</summary>
public static class BoardRoles
{
    public const string Owner = "owner";
    public const string Editor = "editor";
    public const string Viewer = "viewer";

    public static bool TryParse(string? value, out BoardRole role)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case Owner:
                role = BoardRole.Owner;
                return true;
            case Editor:
                role = BoardRole.Editor;
                return true;
            case Viewer:
                role = BoardRole.Viewer;
                return true;
            default:
                role = BoardRole.Viewer;
                return false;
        }
    }

    public static string ToName(BoardRole role) => role switch
    {
        BoardRole.Owner => Owner,
        BoardRole.Editor => Editor,
        _ => Viewer
    };

    /// <summary>Может ли роль менять содержимое доски.</summary>
    public static bool CanEdit(BoardRole role) => role >= BoardRole.Editor;

    /// <summary>Может ли роль управлять участниками и удалять доску.</summary>
    public static bool CanManage(BoardRole role) => role == BoardRole.Owner;
}
