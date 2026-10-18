using System.Collections.Generic;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Sgm.Core.Scanners;

/// <summary>Reads named values for every subkey of an HKLM path (both registry views).</summary>
public static class RegistryReader
{
    [SupportedOSPlatform("windows")]
    public static List<Dictionary<string, string>> Values(string keyPath, params string[] names)
    {
        var rows = new List<Dictionary<string, string>>();
        if (!OperatingSystem.IsWindows())
            return rows;

        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            RegistryKey? baseKey;
            try
            {
                baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(keyPath);
            }
            catch
            {
                continue;
            }

            if (baseKey == null)
                continue;

            using (baseKey)
            {
                foreach (var sub in baseKey.GetSubKeyNames())
                {
                    try
                    {
                        using var key = baseKey.OpenSubKey(sub);
                        if (key == null)
                            continue;
                        var row = new Dictionary<string, string> { ["_key"] = sub };
                        foreach (var n in names)
                            row[n] = key.GetValue(n)?.ToString() ?? "";
                        rows.Add(row);
                    }
                    catch
                    {
                        // skip unreadable subkey
                    }
                }
            }
        }

        return rows;
    }
}
