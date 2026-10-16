import zlib


def test_appid_top_bit_and_vector():
    from steam_games_manager.vdf_handler.appid import appid_signed, appid_unsigned

    exe, name = r"C:\Games\Foo\foo.exe", "Foo"
    expected = (zlib.crc32((exe + name).encode()) & 0xFFFFFFFF) | 0x80000000
    assert appid_unsigned(exe, name) == expected
    assert appid_unsigned(exe, name) & 0x80000000
    s = appid_signed(exe, name)
    assert s == (expected - 0x100000000 if expected >= 0x80000000 else expected)
