namespace Whiteboard.LicenseServer.Realtime;

/// <summary>Участник, который сейчас открыл доску.</summary>
public sealed record ParticipantDto(Guid UserId, string Name, string Color, string Role);

/// <summary>Участник доски по списку доступа — независимо от того, в сети он или нет.</summary>
public sealed record BoardMemberDto(Guid UserId, string Email, string Name, string Role);

/// <summary>
/// Ответ на JoinBoard: всё состояние комнаты одним куском.
///
/// Именно это делает переподключение простым: клиент после обрыва связи
/// не «доигрывает» пропущенные события, а заново получает текущее состояние.
/// </summary>
public sealed record BoardJoinedDto(
    Guid BoardId,
    string Name,
    string Role,
    bool CanEdit,
    bool CanManage,
    IReadOnlyList<ParticipantDto> Participants,
    IReadOnlyList<BoardMemberDto> Members);

public sealed record CursorDto(Guid UserId, double X, double Y);
