from steam_games_manager.scanners import gaming_root


def _game(root, folder, identity, display, exe):
    content = root / folder / "Content"
    content.mkdir(parents=True)
    (content / exe).write_bytes(b"MZ")
    (content / "MicrosoftGame.config").write_text(
        '<?xml version="1.0"?>'
        '<Game configVersion="1">'
        f'<Identity Name="{identity}" Publisher="CN=X"/>'
        f'<ShellVisuals DefaultDisplayName="{display}" />'
        f'<ExecutableList><Executable Name="{exe}" Id="{folder}"/></ExecutableList>'
        '</Game>', encoding="utf-8")


def test_gaming_root_skips_titles_already_listed_as_appx(tmp_path, monkeypatch):
    monkeypatch.setenv("SGM_TEST_GAMING_ROOT", "1")
    monkeypatch.setattr(gaming_root, "_gaming_root_targets", lambda: [tmp_path])
    _game(tmp_path, "Shadow", "Some.Shadow", "Shadow Quest", "Shadow.exe")
    _game(tmp_path, "Ghost", "Some.Ghost", "Ghost Run", "Ghost.exe")

    all_games = gaming_root.scan_gaming_roots()
    assert {g.name for g in all_games} == {"Shadow Quest", "Ghost Run"}

    kept = gaming_root.scan_gaming_roots(skip_names={"Some.Shadow"})
    assert [g.name for g in kept] == ["Ghost Run"]
