from steam_games_manager.scanners import xbox


def _pkg(name, exts="", manifest=True):
    return {"Name": name, "Exts": exts, "Manifest": manifest}


def test_games_with_only_protocol_entries_are_kept():
    assert not xbox._is_utility(_pkg("Microsoft.MinecraftUWP", "windows.protocol,windows.fileTypeAssociation"))
    assert not xbox._is_utility(_pkg("Microsoft.4297127D64EC6", "windows.protocol"))


def test_apps_with_utility_extensions_are_skipped():
    assert xbox._is_utility(_pkg("Microsoft.WindowsCalculator", "windows.appExtension"))
    assert xbox._is_utility(_pkg("Microsoft.Paint", "windows.appExecutionAlias,windows.comServer"))
    assert xbox._is_utility(_pkg("Microsoft.GamingServices", "windows.backgroundTasks,windows.service"))


def test_system_name_tokens_are_skipped():
    assert xbox._is_utility(_pkg("NVIDIACorp.NVIDIAControlPanel", "", manifest=False))
    assert xbox._is_utility(_pkg("Windows.PrintDialog", "", manifest=False))
    assert xbox._is_utility(_pkg("Microsoft.VP9VideoExtensions", "", manifest=False))


def test_unreadable_manifest_falls_back_to_name_rules():
    assert not xbox._is_utility(_pkg("PocketpairInc.Palworld", "", manifest=False))


def test_extension_kind_normalization():
    kinds = xbox._extension_kinds("windows.appExecutionAlias, windows.protocol")
    assert kinds == {"appexecutionalias", "protocol"}
