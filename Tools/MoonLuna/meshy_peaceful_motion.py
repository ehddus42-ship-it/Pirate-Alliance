"""Create and resume Moon Luna's calm, peaceful walking motion with Meshy.

Run from any directory: python Tools/MoonLuna/meshy_peaceful_motion.py
Requires MESHY_API_KEY in the environment. A single Prime request costs 10 credits.
Paid tasks are recorded before polling; reruns resume instead of creating duplicates.
The ignored Temp cache may contain expiring download URLs. Provenance does not.
"""
from __future__ import annotations

import hashlib
import argparse
import json
import os
from pathlib import Path
import time
import urllib.error
import urllib.request


ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / "Temp/MoonLuna/Meshy"
STATE = CACHE / "peaceful_walk_state.json"
OUTPUT = CACHE / "peaceful_walk_raw.fbx"
PROVENANCE = ROOT / "Documentation/MoonLuna/peaceful_walk_meshy.json"
RETARGET_PROVENANCE = ROOT / "Documentation/MoonLuna/peaceful_walk_retarget.json"
BASE = "https://api.meshy.ai/openapi/v1/text-to-motion"
PROMPT = (
    "A calm, confident young woman in a floor-length nun dress walks slowly in place. "
    "Seamless looping walk cycle, small measured steps, upright relaxed posture, level head, "
    "gentle weight shifts, low restrained alternating arm swing with hands beside hips. "
    "Feet roll heel to toe and stay close to the floor. Graceful, warm, dignified. "
    "No running, jumping, floating, turning, posing or gestures."
)
PAYLOAD = {"prompt": PROMPT, "mode": "prime", "duration": 4.0}


def write_json(path: Path, value: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def load_task_state(state_path: Path, provenance_path: Path, payload: dict) -> dict:
    state = json.loads(state_path.read_text(encoding="utf-8")) if state_path.exists() else {}
    provenance = json.loads(provenance_path.read_text(encoding="utf-8")) if provenance_path.exists() else {}
    if state.get("payload", payload) != payload or provenance.get("request", payload) != payload:
        raise RuntimeError("Existing paid task uses a different request. Keep its state intact.")
    documented_id = provenance.get("task_id")
    if state.get("task_id") and documented_id and state["task_id"] != documented_id:
        raise RuntimeError("Cached and documented task IDs conflict; recover the correct ID first.")
    if documented_id and not state.get("task_id"):
        state["task_id"] = documented_id
    if provenance.get("submission_started_at") and not state.get("submission_started_at"):
        state["submission_started_at"] = provenance["submission_started_at"]
    if provenance and not state.get("task_id") and not state.get("submission_started_at"):
        raise RuntimeError("Existing provenance has no recoverable task ID; do not create another paid task.")
    if state:
        state["payload"] = payload
        write_json(state_path, state)
    return state


def record_task_state(state_path: Path, provenance_path: Path, state: dict) -> None:
    write_json(state_path, state)
    receipt = json.loads(provenance_path.read_text(encoding="utf-8")) if provenance_path.exists() else {}
    receipt.update({"request": state["payload"], "task_id": state.get("task_id"),
                    "submission_started_at": state.get("submission_started_at")})
    # Persist both a pre-POST sentinel and its eventual ID outside disposable Temp.
    write_json(provenance_path, receipt)


def api(task_id: str | None = None, *, endpoint: str = BASE, payload: dict = PAYLOAD) -> dict:
    token = os.environ.get("MESHY_API_KEY")
    if not token:
        raise RuntimeError("MESHY_API_KEY is not set.")
    request = urllib.request.Request(
        endpoint + ("/" + task_id if task_id else ""),
        data=None if task_id else json.dumps(payload).encode("utf-8"),
        headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"},
    )
    # Never retry a paid POST automatically: a timeout can hide a successful submission.
    for attempt in range(4 if task_id else 1):
        try:
            with urllib.request.urlopen(request, timeout=90) as response:
                return json.load(response)
        except urllib.error.HTTPError as exc:
            if task_id and exc.code in (429, 500, 502, 503, 504) and attempt < 3:
                time.sleep(10)
                continue
            raise RuntimeError(f"Meshy HTTP {exc.code}; no request secrets or response URLs logged.") from None
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            if task_id and attempt < 3:
                time.sleep(10)
                continue
            raise RuntimeError("Meshy request failed; inspect task state before retrying a submission.") from None
    raise RuntimeError("Meshy polling retries exhausted.")


def main() -> None:
    assert len(PROMPT) <= 400
    state = load_task_state(STATE, PROVENANCE, PAYLOAD)
    if not state.get("task_id"):
        if state.get("submission_started_at"):
            raise RuntimeError("Previous submission outcome is unknown; recover task ID before retrying.")
        state = {"payload": PAYLOAD, "submission_started_at": int(time.time())}
        record_task_state(STATE, PROVENANCE, state)
        state["task_id"] = api()["result"]
        record_task_state(STATE, PROVENANCE, state)
    record_task_state(STATE, PROVENANCE, state)
    print("Resuming peaceful walk task", state["task_id"], flush=True)
    previous = None
    while True:
        result = api(state["task_id"])
        write_json(CACHE / "peaceful_walk_result.json", result)
        status = (result.get("status"), result.get("progress"))
        if status != previous:
            print("Peaceful walk:", *status, flush=True)
            previous = status
        if status[0] == "SUCCEEDED":
            break
        if status[0] in ("FAILED", "CANCELED", "EXPIRED"):
            raise RuntimeError("Peaceful walk task ended with status " + status[0])
        time.sleep(10)
    motion = result["result"]
    if motion["motion_format"].lower() != "fbx":
        raise RuntimeError("Expected a Prime FBX result.")
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    if not OUTPUT.exists():
        temporary = OUTPUT.with_suffix(".download")
        urllib.request.urlretrieve(motion["motion_url"], temporary)
        temporary.replace(OUTPUT)
    data = OUTPUT.read_bytes()
    if not data.startswith(b"Kaydara FBX Binary"):
        raise RuntimeError("Downloaded motion is not a binary FBX.")
    provenance = {
        "character_name": "문루나",
        "state": "peaceful",
        "provider": "Meshy",
        "api_documentation": "https://docs.meshy.ai/en/api/text-to-motion",
        "task_id": state["task_id"],
        "request": PAYLOAD,
        "status": result["status"],
        "actual_duration_ms": motion.get("duration_ms"),
        "consumed_credits": result.get("consumed_credits"),
        "created_at": result.get("created_at"),
        "finished_at": result.get("finished_at"),
        "output": OUTPUT.relative_to(ROOT).as_posix(),
        "sha256": hashlib.sha256(data).hexdigest(),
        "reference": "User supplied peaceful character illustration and character brief pages 6 and 17.",
        "direction": "Small measured steps, restrained arm swing, upright calm posture; intended for in-place Humanoid retargeting.",
        "notes": "Raw provider FBX is preserved in ignored Temp. build_peaceful_walk_loop.py exports skeleton-only Unity assets and an in-place loop.",
    }
    write_json(PROVENANCE, provenance)
    print(json.dumps({"output": provenance["output"], "bytes": len(data), "duration_ms": motion.get("duration_ms"),
                      "consumed_credits": result.get("consumed_credits")}), flush=True)


def retarget(rig_task_id: str) -> None:
    """Apply the existing paid motion to the completed peaceful character rig once."""
    motion_state = load_task_state(STATE, PROVENANCE, PAYLOAD)
    if not motion_state.get("task_id"):
        raise RuntimeError("Recover or create the source motion before retargeting.")
    payload = {"rig_task_id": rig_task_id, "motion_task_id": motion_state["task_id"]}
    endpoint = "https://api.meshy.ai/openapi/v1/animations"
    state_path = CACHE / "peaceful_walk_retarget_state.json"
    state = load_task_state(state_path, RETARGET_PROVENANCE, payload)
    if not state.get("task_id"):
        if state.get("submission_started_at"):
            raise RuntimeError("Retarget submission outcome is unknown; recover its task ID first.")
        state = {"payload": payload, "submission_started_at": int(time.time())}
        record_task_state(state_path, RETARGET_PROVENANCE, state)
        state["task_id"] = api(endpoint=endpoint, payload=payload)["result"]
        record_task_state(state_path, RETARGET_PROVENANCE, state)
    record_task_state(state_path, RETARGET_PROVENANCE, state)
    previous = None
    while True:
        result = api(state["task_id"], endpoint=endpoint)
        write_json(CACHE / "peaceful_walk_retarget_result.json", result)
        status = (result.get("status"), result.get("progress"))
        if status != previous:
            print("Peaceful walk retarget:", *status, flush=True)
            previous = status
        if status[0] == "SUCCEEDED":
            break
        if status[0] in ("FAILED", "CANCELED", "EXPIRED"):
            raise RuntimeError("Retarget task ended with status " + status[0])
        time.sleep(10)
    outputs = []
    for suffix in ("glb", "fbx"):
        url = result["result"].get("animation_" + suffix + "_url")
        if not url:
            continue
        path = CACHE / ("peaceful_walk_retargeted." + suffix)
        if not path.exists():
            urllib.request.urlretrieve(url, path)
        outputs.append({"path": path.relative_to(ROOT).as_posix(), "bytes": path.stat().st_size,
                        "sha256": hashlib.sha256(path.read_bytes()).hexdigest()})
    write_json(RETARGET_PROVENANCE, {
        "character_name": "문루나", "provider": "Meshy", "task_id": state["task_id"],
        "api_documentation": "https://docs.meshy.ai/en/api/animation", "request": payload,
        "status": result["status"], "consumed_credits": result.get("consumed_credits"),
        "created_at": result.get("created_at"), "finished_at": result.get("finished_at"), "raw_outputs": outputs,
        "notes": "Raw retargeted character motion preserved in ignored Temp; skeleton-only loop exported separately."
    })
    print(json.dumps({"retarget_task_id": state["task_id"], "outputs": outputs}), flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--rig-task", help="Retarget the existing motion onto this completed Meshy rig task.")
    args = parser.parse_args()
    if args.rig_task:
        retarget(args.rig_task)
    else:
        main()
