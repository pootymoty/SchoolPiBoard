using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace SchoolPiBoard.Services;

/// <summary>
/// Отпечаток компьютера, к которому привязывается лицензия.
///
/// Берутся только стабильные параметры: MachineGuid из реестра (создаётся
/// при установке Windows и живёт до переустановки) и серийный номер тома
/// системного диска. MAC-адрес намеренно не используется — он меняется
/// вместе с USB-адаптером и отвязывал бы лицензию на ровном месте.
/// </summary>
public static class HardwareId
{
    private static string? _cached;

    /// <summary>Идентификатор этого компьютера: 32 шестнадцатеричных символа.</summary>
    public static string Current => _cached ??= Compute();

    private static string Compute()
    {
        var parts = new List<string>();

        var machineGuid = ReadMachineGuid();
        if (!string.IsNullOrWhiteSpace(machineGuid))
            parts.Add("mg:" + machineGuid);

        var volumeSerial = ReadSystemVolumeSerial();
        if (!string.IsNullOrWhiteSpace(volumeSerial))
            parts.Add("vol:" + volumeSerial);

        // Крайний случай: ни реестр, ни том не прочитались. Имя машины хуже
        // как идентификатор, но лучше, чем полное отсутствие привязки.
        if (parts.Count == 0)
            parts.Add("host:" + Environment.MachineName);

        // Строка-соль входит в хеш, поэтому её правка меняет отпечатки всех
        // компьютеров разом: активированные устройства станут для сервера
        // новыми и займут дополнительные слоты. Менять только вместе
        // с очисткой таблицы активаций и только пока нет покупателей.
        var raw = "SchoolPiBoard.HardwareId.v1|" + string.Join("|", parts);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..32];
    }

    private static string? ReadMachineGuid()
    {
        try
        {
            // Явно 64-битное представление реестра: под 32-битным процессом
            // ключ иначе читался бы из ветки WOW6432Node, где его нет.
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string;
        }
        catch
        {
            return null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(
        string rootPathName,
        StringBuilder? volumeNameBuffer,
        int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder? fileSystemNameBuffer,
        int fileSystemNameSize);

    private static string? ReadSystemVolumeSerial()
    {
        try
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var root = System.IO.Path.GetPathRoot(windows);
            if (string.IsNullOrEmpty(root))
                return null;

            var ok = GetVolumeInformationW(root, null, 0, out var serial, out _, out _, null, 0);
            return ok ? serial.ToString("X8") : null;
        }
        catch
        {
            return null;
        }
    }
}
