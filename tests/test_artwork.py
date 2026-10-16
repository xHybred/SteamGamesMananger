from steam_games_manager.artwork.steamgriddb import SteamGridDBClient


class FakeResp:
    def __init__(self, payload, status=200):
        self._payload = payload
        self.status_code = status
        self.ok = status == 200

    def json(self):
        return self._payload


class FakeSession:
    def __init__(self, data):
        self.headers = {}
        self._data = data

    def get(self, url, params=None, timeout=None, stream=False):
        return FakeResp({"success": True, "data": self._data})


def make_client(data):
    c = SteamGridDBClient.__new__(SteamGridDBClient)
    c.api_key = "x"
    c.session = FakeSession(data)
    return c


def test_pick_exact_wins_over_first():
    c = make_client([{"id": 1, "name": "Epic Game"}, {"id": 2, "name": "Fortnite"}])
    assert c.pick_game("Fortnite")["id"] == 2
    assert c.pick_game("fortnite", exact_only=True)["id"] == 2


def test_pick_exact_only_rejects_fuzzy():
    c = make_client([{"id": 1, "name": "Something Else"}])
    assert c.pick_game("Fortnite", exact_only=True) is None
    assert c.pick_game("Fortnite", exact_only=False)["id"] == 1


def test_validate_ok():
    assert make_client([]).validate() is True
