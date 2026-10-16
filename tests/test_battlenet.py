from steam_games_manager.scanners import battlenet


def _varint(n: int) -> bytes:
    out = bytearray()
    while True:
        byte = n & 0x7F
        n >>= 7
        if n:
            out.append(byte | 0x80)
        else:
            out.append(byte)
            return bytes(out)


def _ld(number: int, payload: bytes) -> bytes:
    return _varint((number << 3) | 2) + _varint(len(payload)) + payload


def _install(uid: str, path: str, installed: bool) -> bytes:
    base = b"\x08" + (b"\x01" if installed else b"\x00")
    settings = _ld(1, path.encode("utf-8"))
    return _ld(1, uid.encode()) + _ld(3, settings) + _ld(4, _ld(1, base))


def test_product_db_parse_reads_uid_path_and_installed(tmp_path):
    db = _ld(1, _install("s2", str(tmp_path), True)) + _ld(1, _install("ghost", str(tmp_path), False))
    blobs = [v for n, w, v in battlenet._fields(db) if n == 1 and w == 2]
    parsed = [battlenet._parse_install(b) for b in blobs]
    assert parsed[0] == {"uid": "s2", "product_code": "", "installed": True,
                         "install_path": str(tmp_path)}
    assert parsed[1]["installed"] is False


def test_uninstalled_and_unknown_titles_are_not_launchable():
    assert "s2" in battlenet.KNOWN_GAMES
    assert battlenet.IGNORED_UID.match("agent")
    assert battlenet.IGNORED_UID.match("wow_classic")
    assert "unknown_uid" not in battlenet.KNOWN_GAMES


def test_scan_is_empty_off_windows(monkeypatch):
    monkeypatch.setattr(battlenet.sys, "platform", "linux")
    assert battlenet.scan_battlenet() == []
