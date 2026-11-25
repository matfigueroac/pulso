using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LumenPlayer;

internal sealed class SubtitleSyncStore(string videoPath)
{
    private readonly string _directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LumenPlayer", "SubtitleSync", Hash(Path.GetFullPath(videoPath).ToUpperInvariant()));

    public T? Read<T>(string key) => JsonSettingsFile.Read<T>(SettingPath(key));

    public bool Save<T>(string key, T value) => JsonSettingsFile.Save(SettingPath(key), value);

    public bool Clear(string key)
    {
        try
        {
            File.Delete(SettingPath(key));
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning(ex.Message);
            return false;
        }
    }

    private string SettingPath(string key) => Path.Combine(_directory, Hash(key) + ".json");

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
