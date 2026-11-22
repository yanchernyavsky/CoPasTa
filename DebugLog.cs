using System;
using System.IO;

namespace CoPasTa;

/// <summary>Небольшой диагностический лог (отключается в меню). Текст буфера не пишется.</summary>
internal static class DebugLog
{
    private static readonly string FilePath = Path.Combine(Path.GetTempPath(), "CoPasTa.log");

    internal static bool Enabled { get; set; }

    internal static void Write(string message)
    {
        if (!Enabled)
            return;

        try
        {
            File.AppendAllText(FilePath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch
        {
            // лог — дело необязательное
        }
    }
}
