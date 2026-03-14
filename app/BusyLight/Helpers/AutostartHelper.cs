using Microsoft.Win32;

namespace BusyLight.Helpers;

/// <summary>
/// Reads and writes the Windows autostart registry entry for BusyLight.
/// Uses HKCU\Software\Microsoft\Windows\CurrentVersion\Run so that no
/// administrator privileges are required.
/// </summary>
public static class AutostartHelper
{
    private const string RegistryKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";

    private const string ValueName = "BusyLight";

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the autostart registry entry exists and points
    /// to the current executable path.
    /// </summary>
    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: false);
            if (key is null) return false;

            var value = key.GetValue(ValueName) as string;
            return string.Equals(value, GetExePath(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Autostart] IsAutoStartEnabled error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Enable or disable the autostart registry entry.
    /// </summary>
    /// <param name="enable">
    /// <c>true</c> to create/update the entry; <c>false</c> to remove it.
    /// </param>
    public static void SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, writable: true);
            if (key is null)
            {
                Debug.WriteLine("[Autostart] Could not open registry key for writing.");
                return;
            }

            if (enable)
            {
                key.SetValue(ValueName, GetExePath(), RegistryValueKind.String);
                Debug.WriteLine("[Autostart] Autostart enabled.");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Debug.WriteLine("[Autostart] Autostart disabled.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Autostart] SetAutoStart error: {ex.Message}");
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string GetExePath()
        => Environment.ProcessPath ?? Application.ExecutablePath;
}
