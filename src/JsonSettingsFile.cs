using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace LumenPlayer;

internal static class JsonSettingsFile
{
    internal static T? Read<T>(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceWarning(ex.Message);
            return default;
        }
    }

    internal static bool Save<T>(string path, T value)
    {
        var pending = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(pending, JsonSerializer.Serialize(value));
            File.Move(pending, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning(ex.Message);
            try { File.Delete(pending); }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            { Trace.TraceWarning(cleanup.Message); }
            return false;
        }
    }
}
