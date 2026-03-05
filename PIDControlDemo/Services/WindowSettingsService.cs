using System;
using System.IO;
using System.Text.Json;

namespace PIDControlDemo.Services;

public record WindowSettings
{
    public double Width { get; init; } = 850;
    public double Height { get; init; } = 700;
    public int X { get; init; } = -1;
    public int Y { get; init; } = -1;
}

public static class WindowSettingsService
{
    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PIDControlDemo",
            "window.json");

    public static WindowSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<WindowSettings>(json) ?? new WindowSettings();
            }
        }
        catch
        {
            // 読み込み失敗時はデフォルト
        }
        return new WindowSettings();
    }

    public static void Save(WindowSettings settings)
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // 保存失敗は無視
        }
    }
}
