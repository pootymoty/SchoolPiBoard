namespace SchoolPiBoard.Online.Data;

public enum BoardRole
{
    Viewer = 0,
    Editor = 1,
    Owner = 2
}

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

    public static bool CanEdit(BoardRole role) => role >= BoardRole.Editor;

    public static bool CanManage(BoardRole role) => role == BoardRole.Owner;
}

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

public class BoardMember
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BoardId { get; set; }

    public Guid UserId { get; set; }

    public string Role { get; set; } = BoardRoles.Viewer;

    public DateTime InvitedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Участник пришёл по ссылке, а не по личному приглашению.</summary>
    public bool ViaLink { get; set; }

    /// <summary>
    /// До какого момента участник может менять доску. NULL — бессрочно.
    /// Так ограничивается расползание ссылки: доска у человека остаётся,
    /// но через оговорённый срок он может только смотреть.
    /// </summary>
    public DateTime? EditUntil { get; set; }

    public User? User { get; set; }

    /// <summary>Роль с учётом истёкшего срока правок.</summary>
    public BoardRole EffectiveRole(DateTime now)
    {
        if (!BoardRoles.TryParse(Role, out var role))
            role = BoardRole.Viewer;

        if (role == BoardRole.Owner)
            return role;

        if (EditUntil is not null && now >= EditUntil.Value)
            return BoardRole.Viewer;

        return role;
    }
}

/// <summary>Ссылка-приглашение на доску.</summary>
public class BoardInvite
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BoardId { get; set; }

    public Guid CreatedBy { get; set; }

    /// <summary>Хеш кода из ссылки: из базы саму ссылку не восстановить.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public string Role { get; set; } = BoardRoles.Editor;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public int Uses { get; set; }

    /// <summary>Сколько дней вошедший по ссылке может менять доску. NULL — без ограничения.</summary>
    public int? EditDays { get; set; }

    public bool IsUsable(DateTime now) => RevokedAt is null && now < ExpiresAt;
}

/// <summary>Объект доски: штрих, фигура, текст или изображение.</summary>
public class BoardItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BoardId { get; set; }

    public string Kind { get; set; } = string.Empty;

    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public double Rotation { get; set; }
    public int ZIndex { get; set; }

    public string? StrokeColor { get; set; }
    public string? FillColor { get; set; }
    public double? Thickness { get; set; }
    public double? Opacity { get; set; }

    public string? Points { get; set; }
    public string? Text { get; set; }
    public double? FontSize { get; set; }
    public string? ImageRef { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
