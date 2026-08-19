using System.Security.Cryptography;
using System.Text;

namespace Whiteboard.LicenseServer.Services;

/// <summary>
/// Цвет участника: курсор и рамка занятого объекта. Выбирается по
/// идентификатору пользователя, поэтому у одного человека он одинаковый
/// во всех досках и во всех сессиях.
/// </summary>
public static class UserColor
{
    private static readonly string[] Palette =
    {
        "#E5484D", "#E5A32B", "#46A758", "#12A594",
        "#0091FF", "#5B6CF7", "#8E4EC6", "#D6409F",
        "#3E9B4F", "#D14D41", "#0D74CE", "#AB6400"
    };

    public static string For(Guid userId)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(userId.ToString()));
        return Palette[hash[0] % Palette.Length];
    }
}
