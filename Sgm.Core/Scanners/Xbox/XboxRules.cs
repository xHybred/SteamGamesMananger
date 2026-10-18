using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Sgm.Core.Scanners.Xbox;

/// <summary>
/// Xbox/UWP classification rules: package-family skip prefixes, friendly
/// display names, and the heuristics that separate games from app/utility packages.
/// </summary>
public static partial class XboxRules
{
    public static readonly string[] SystemPrefixes =
    {
        "Microsoft.Windows",
        "Microsoft.VCLibs",
        "Microsoft.NET",
        "Microsoft.UI",
        "Microsoft.AAD",
        "Microsoft.AccountsControl",
        "Microsoft.AsyncTextService",
        "Microsoft.BioEnrollment",
        "Microsoft.CredDialogHost",
        "Microsoft.LockApp",
        "Microsoft.MicrosoftEdge",
        "Microsoft.Win32WebViewHost",
        "Microsoft.XboxGameCallableUI",
        "Microsoft.Xbox.TCUI",
        "Microsoft.SecHealthUI",
        "Microsoft.ECApp",
        "Microsoft.DirectXRuntime",
        "Microsoft.Services.Store",
        "Microsoft.WinAppRuntime",
        "Microsoft.AV1VideoExtension",
        "Microsoft.AVCEncoderVideoExtension",
        "Microsoft.HEVCVideoExtension",
        "Microsoft.MPEG2VideoExtension",
        "Microsoft.HEIFImageExtension",
        "Microsoft.VP9VideoExtensions",
        "Microsoft.RawImageExtension",
        "Microsoft.WebMediaExtensions",
        "Microsoft.WebpImageExtension",
        "Microsoft.DesktopAppInstaller",
        "Microsoft.AppInstaller",
        "Microsoft.Winget.Source",
        "Microsoft.StorePurchaseApp",
        "Microsoft.GetHelp",
        "Microsoft.Getstarted",
        "Microsoft.WindowsFeedbackHub",
        "Microsoft.Tips",
        "Microsoft.MSPaint",
        "Microsoft.ScreenSketch",
        "Microsoft.People",
        "Microsoft.YourPhone",
        "Microsoft.MicrosoftStickyNotes",
        "Microsoft.549981C3F5F10",
        "MicrosoftCorporationII.",
        "MicrosoftWindows.",
        "Windows.",
        "windows.",
    };

    public static readonly HashSet<string> SkipExact = new(StringComparer.Ordinal)
    {
        "Microsoft.MicrosoftEdgeDevToolsClient",
        "ROBLOXCorporation.RobloxGDK",
    };

    public static readonly IReadOnlyDictionary<string, string> FriendlyNames = new Dictionary<string, string>
    {
        ["Microsoft.MinecraftUWP"] = "Minecraft: Bedrock",
        ["Microsoft.4297127D64EC6"] = "Minecraft Launcher",
        ["Microsoft.MinecraftEducationEdition"] = "Minecraft Education",
        ["ROBLOXCORPORATION.ROBLOX"] = "Roblox",
        ["Microsoft.GamingApp"] = "Xbox",
    };

    private static readonly HashSet<string> UtilityExtensions = new(StringComparer.Ordinal)
    {
        "appexecutionalias", "appextension", "appextensionhost", "comserver",
        "fileexplorercontextmenus", "desktopappmigration", "service", "backgroundtasks",
        "startuptask", "appservice", "fulltrustprocess", "sharetarget",
    };

    private static readonly string[] SystemNameTokens =
    {
        "extension", "dialog", "picker", "webviewhost", "experiencehost", "oobe",
        "captiveportal", "nvidiacorp.", "xboxgamecallable", "xbox.tcui", "xboxidentity",
        "accountscontrol", "lockapp", "credential", "bioenrollment", "appcompat",
        "winappruntime", "appinstaller", "winget", "devkit", "narratorquickstart",
        "applicationcompatibility",
        "asynctextservice", "chxapp", "cloudexperience", "contentdelivery",
        "startmenu", "shellexperience", "windows.", "microsoftwindows.",
    };

    [GeneratedRegex("^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$")]
    private static partial Regex GuidRegex();

    public static bool ShouldSkip(string family, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || GuidRegex().IsMatch(trimmed))
            return true;
        if (SkipExact.Contains(trimmed))
            return true;
        if (string.IsNullOrWhiteSpace(family))
            return true;
        return SystemPrefixes.Any(p => family.StartsWith(p, StringComparison.Ordinal));
    }

    public static HashSet<string> ExtensionKinds(string raw)
    {
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in raw.Split(','))
        {
            var t = item.Trim();
            if (t.Length == 0)
                continue;
            var lower = t.ToLowerInvariant();
            kinds.Add(lower.StartsWith("windows.", StringComparison.Ordinal) ? lower["windows.".Length..] : lower);
        }
        return kinds;
    }

    public static bool IsUtility(string name, string extensions, bool hasManifest)
    {
        var lowered = name.ToLowerInvariant();
        if (SystemNameTokens.Any(tok => lowered.Contains(tok)))
            return true;
        if (hasManifest)
        {
            var kinds = ExtensionKinds(extensions);
            if (kinds.Overlaps(UtilityExtensions))
                return true;
        }
        return false;
    }

    public static string FriendlyName(string name)
    {
        if (FriendlyNames.TryGetValue(name, out var friendly))
            return friendly;
        // "PocketpairInc.Palworld" -> "Palworld" (strip vendor prefix).
        if (name.Contains('.') && !name.Contains(' '))
        {
            var tail = name.Split('.')[^1];
            if (tail.Length > 0 && char.IsUpper(tail[0]))
                return tail;
        }
        return name;
    }
}
