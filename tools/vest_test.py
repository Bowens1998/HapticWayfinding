"""
Standalone bHaptics vest test - no Unity needed.

Talks to the bHaptics Player (must be running, vest connected) over its local WebSocket:
    ws://127.0.0.1:15881/v2/feedbacks

Usage (Anaconda prompt):
    pip install websocket-client
    python vest_test.py status          # connect and print what the Player reports (connected devices)
    python vest_test.py motors          # every motor one by one: VestFront 0..19, then VestBack 0..19
    python vest_test.py columns         # the 8 direction columns, wearer-left -> wearer-right, front then back
    python vest_test.py sweep           # the active column rotates around the torso (clockwise from above)
    python vest_test.py bearing 135     # vibrate the column for a bearing (0 front, 90 right, -90 left, 180 back)

Column / index mapping here is IDENTICAL to Unity's VestLayout.cs. If 'columns' shows left and right swapped
on the front or back, flip FRONT_INDEX0_IS_WEARERS_LEFT / BACK_INDEX0_IS_WEARERS_LEFT here AND the matching
checkbox on HapticSystem > HapticVestOutput > Layout in Unity.
"""
import json
import sys
import time

try:
    import websocket  # pip install websocket-client
except ImportError:
    sys.exit("Missing package: run  pip install websocket-client")

URL = "ws://127.0.0.1:15881/v2/feedbacks?app_id=edu.ufl.icic.hapticwayfinding&app_name=HapticWayfindingTest"
COLUMNS, ROWS = 4, 5
FRONT_INDEX0_IS_WEARERS_LEFT = True   # verified in the lab 2026-11-18
BACK_INDEX0_IS_WEARERS_LEFT = True
FRONT_ANGLES = [-67.5, -22.5, 22.5, 67.5]      # wearer's left -> right
BACK_ANGLES = [-112.5, -157.5, 157.5, 112.5]   # wearer's left -> right
COL_NAMES = ["left-outer", "left-inner", "right-inner", "right-outer"]
INTENSITY = 80  # 0..100


def device_index(side, row, wearer_col):
    left_first = FRONT_INDEX0_IS_WEARERS_LEFT if side == "VestFront" else BACK_INDEX0_IS_WEARERS_LEFT
    col = wearer_col if left_first else COLUMNS - 1 - wearer_col
    return row * COLUMNS + col


def submit_dots(ws, side, indices, duration_ms, intensity=INTENSITY):
    frame = {
        "position": side,
        "dotPoints": [{"index": i, "intensity": intensity} for i in indices],
        "pathPoints": [],
        "durationMillis": duration_ms,
    }
    ws.send(json.dumps({"Submit": [{"Type": "frame", "Key": "test_" + side, "Frame": frame}]}))


def column(ws, side, wearer_col, duration_ms):
    submit_dots(ws, side, [device_index(side, r, wearer_col) for r in range(ROWS)], duration_ms)


def nearest_column(bearing):
    best = None
    for side, angles in (("VestFront", FRONT_ANGLES), ("VestBack", BACK_ANGLES)):
        for c, a in enumerate(angles):
            d = abs((bearing - a + 180) % 360 - 180)
            if best is None or d < best[0]:
                best = (d, side, c)
    return best[1], best[2]


def connect():
    try:
        ws = websocket.create_connection(URL, timeout=3)
    except Exception as e:  # noqa: BLE001
        sys.exit(f"Cannot reach bHaptics Player at {URL}\n  -> {e}\nIs the bHaptics Player running?")
    print("Connected to bHaptics Player.")
    return ws


def print_status(ws, seconds=2.0):
    ws.settimeout(0.5)
    end = time.time() + seconds
    while time.time() < end:
        try:
            msg = ws.recv()
        except websocket.WebSocketTimeoutException:
            continue
        except Exception:  # noqa: BLE001
            break
        try:
            data = json.loads(msg)
            print("Player status:", json.dumps(data, indent=2)[:1500])
        except ValueError:
            print("Player says:", msg[:500])
        return
    print("(Player sent no status message - that is OK on some versions)")


def main():
    cmd = sys.argv[1] if len(sys.argv) > 1 else "status"
    ws = connect()
    try:
        if cmd == "status":
            print_status(ws)
        elif cmd == "motors":
            for side in ("VestFront", "VestBack"):
                for i in range(20):
                    print(f"{side} device index {i:2d}  (row {i // 4 + 1}, device col {i % 4 + 1})")
                    submit_dots(ws, side, [i], 300)
                    time.sleep(0.45)
        elif cmd == "columns":
            for side in ("VestFront", "VestBack"):
                for c in range(COLUMNS):
                    ang = (FRONT_ANGLES if side == "VestFront" else BACK_ANGLES)[c]
                    input(f"Press Enter -> {side} {COL_NAMES[c]} (should feel at {ang:+.1f} deg) ")
                    column(ws, side, c, 900)
        elif cmd == "sweep":
            order = [("VestFront", c) for c in range(4)] + [("VestBack", c) for c in (3, 2, 1, 0)]
            for _ in range(3):
                for side, c in order:
                    column(ws, side, c, 330)
                    time.sleep(0.35)
        elif cmd == "bearing":
            b = float(sys.argv[2])
            side, c = nearest_column(b)
            print(f"bearing {b:+.1f} deg -> {side} {COL_NAMES[c]}")
            for _ in range(10):
                column(ws, side, c, 150)
                time.sleep(0.1)
        else:
            print(__doc__)
    finally:
        try:
            ws.send(json.dumps({"Submit": [{"Type": "turnOffAll", "Key": ""}]}))
        except Exception:  # noqa: BLE001
            pass
        ws.close()


if __name__ == "__main__":
    main()
