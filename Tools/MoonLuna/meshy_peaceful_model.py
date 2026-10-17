"""Build Moon Luna's peaceful model and humanoid rig from her original illustration.

Requires MESHY_API_KEY. Run once, or rerun to resume the same paid task IDs.
An uncertain POST outcome is never resubmitted automatically. Expiring URLs stay
inside ignored Temp/MoonLuna/Model; public provenance contains no credentials.
"""
from __future__ import annotations

import base64
import datetime
import hashlib
import json
import os
from pathlib import Path
import struct
import time
import urllib.error
import urllib.request


ROOT = Path(__file__).resolve().parents[2]
CACHE = ROOT / "Temp/MoonLuna/Model"
STATE = CACHE / "state.json"
OUT = ROOT / "Assets/Characters/MoonLuna/Peaceful/Models"
DOC = ROOT / "Documentation/MoonLuna/peaceful_model_meshy.json"
REFERENCE = Path("C:/Users/user/Desktop/남궁루나/남궁루나_일상.png")
BASE = "https://api.meshy.ai/openapi"
MODEL_OPTIONS = {
    "ai_model": "meshy-7.1", "should_remesh": True, "target_polycount": 25000,
    "topology": "triangle", "should_texture": True, "enable_pbr": False,
    "texture_resolution": "2k", "pose_mode": "a-pose", "image_enhancement": False,
    "target_formats": ["glb"],
}


def now() -> str:
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def save(path: Path, value) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    temporary.replace(path)


def load(path: Path):
    return json.loads(path.read_text(encoding="utf-8")) if path.exists() else {}


def api(endpoint: str, payload=None):
    key = os.environ.get("MESHY_API_KEY")
    if not key:
        raise RuntimeError("MESHY_API_KEY is not set.")
    request = urllib.request.Request(BASE + endpoint,
        data=None if payload is None else json.dumps(payload).encode("utf-8"),
        headers={"Authorization": "Bearer " + key, "Content-Type": "application/json"})
    attempts = 5 if payload is None else 1
    for attempt in range(attempts):
        try:
            with urllib.request.urlopen(request, timeout=120) as response:
                return json.load(response)
        except urllib.error.HTTPError as exc:
            if payload is None and exc.code in (429, 500, 502, 503, 504) and attempt + 1 < attempts:
                time.sleep(10)
                continue
            # Response bodies can contain request details; do not print them.
            raise RuntimeError(f"Meshy HTTP {exc.code} at {endpoint}; no automatic POST retry.") from None
        except (urllib.error.URLError, TimeoutError, ConnectionError):
            if payload is None and attempt + 1 < attempts:
                time.sleep(10)
                continue
            raise RuntimeError("Meshy request outcome uncertain; preserve recorded state before resuming.") from None


def task(name: str, endpoint: str, payload: dict, descriptor: dict):
    state = load(STATE)
    provenance = load(DOC)
    documented = provenance.get("task_state", {}).get(name)
    id_field = {"image_to_3d": "image_to_3d_task_id", "rigging": "rig_task_id"}[name]
    if documented is None and provenance.get(id_field):
        request = provenance.get("reference") if name == "image_to_3d" else {
            "input_task_id": provenance.get("image_to_3d_task_id"),
            "height_meters": provenance.get("height_meters"),
        }
        documented = {"id": provenance[id_field], "request": request}
    if documented is not None:
        if documented.get("request") != descriptor:
            raise RuntimeError(f"Documented {name} request differs; do not replace a paid task automatically.")
        cached = state.get(name, {})
        if cached.get("id") and documented.get("id") and cached["id"] != documented["id"]:
            raise RuntimeError(f"Cached and documented {name} task IDs conflict; recover the correct ID first.")
        state[name] = {**documented, **cached}
        if not state[name].get("id") and documented.get("id"):
            state[name]["id"] = documented["id"]
        save(STATE, state)

    def record():
        # Durable, credential-free receipt survives cleanup of Unity's Temp folder.
        save(STATE, state)
        receipt = load(DOC)
        receipt.setdefault("task_state", {})[name] = state[name]
        save(DOC, receipt)

    if name not in state:
        state[name] = {"id": None, "submission_started_utc": now(), "request": descriptor}
        record()
        response = api(endpoint, payload)
        state[name]["id"] = response["result"]
        record()
    if state[name]["request"] != descriptor:
        raise RuntimeError(f"Recorded {name} request differs; do not replace a paid task automatically.")
    record()
    identifier = state[name]["id"]
    if not identifier:
        raise RuntimeError(f"Previous {name} POST outcome unknown; recover its ID before continuing.")
    print(name, "task", identifier, flush=True)
    previous = None
    deadline = time.monotonic() + 3600
    while time.monotonic() < deadline:
        result = api(endpoint + "/" + identifier)
        save(CACHE / (name + "_result.json"), result)
        marker = (result.get("status"), result.get("progress"))
        if marker != previous:
            print(name, *marker, flush=True)
            previous = marker
        if marker[0] == "SUCCEEDED":
            return identifier, result
        if marker[0] in ("FAILED", "CANCELED", "EXPIRED"):
            raise RuntimeError(f"{name} ended with {marker[0]}; no replacement was submitted.")
        time.sleep(15)
    raise TimeoutError("Task wait timed out; rerunning resumes the recorded paid task.")


def fetch(url: str, path: Path) -> dict:
    path.parent.mkdir(parents=True, exist_ok=True)
    if not path.exists():
        temp = path.with_suffix(path.suffix + ".download")
        for attempt in range(4):
            try:
                urllib.request.urlretrieve(url, temp)
                temp.replace(path)
                break
            except (urllib.error.URLError, TimeoutError, ConnectionError):
                if attempt == 3:
                    raise
                time.sleep(5)
    raw = path.read_bytes()
    info = {"path": path.relative_to(ROOT).as_posix(), "bytes": len(raw),
            "sha256": hashlib.sha256(raw).hexdigest()}
    if path.suffix == ".glb":
        if raw[:4] != b"glTF":
            raise RuntimeError("Downloaded model is not a GLB.")
        chunk_length = struct.unpack_from("<I", raw, 12)[0]
        gltf = json.loads(raw[20:20 + chunk_length])
        info.update(meshes=len(gltf.get("meshes", [])), skins=len(gltf.get("skins", [])),
                    animation_count=len(gltf.get("animations", [])),
                    joints=[gltf["nodes"][n].get("name", str(n))
                            for skin in gltf.get("skins", []) for n in skin["joints"]])
    if path.suffix == ".fbx" and not raw.startswith(b"Kaydara FBX Binary"):
        raise RuntimeError("Downloaded rig is not a binary FBX.")
    return info


def main():
    image = REFERENCE.read_bytes()
    descriptor = {**MODEL_OPTIONS, "reference": REFERENCE.name,
                  "reference_sha256": hashlib.sha256(image).hexdigest()}
    model_id, model = task("image_to_3d", "/v1/image-to-3d",
        {**MODEL_OPTIONS, "image_url": "data:image/png;base64," + base64.b64encode(image).decode("ascii")}, descriptor)
    assets = []
    if model.get("model_urls", {}).get("glb"):
        assets.append(fetch(model["model_urls"]["glb"], OUT / "LunaPeaceful_Source.glb"))
    for index, textures in enumerate(model.get("texture_urls", [])):
        if textures.get("base_color"):
            assets.append(fetch(textures["base_color"], OUT / f"LunaPeaceful_Albedo_{index}.png"))
    rig_request = {"input_task_id": model_id, "height_meters": 1.65}
    rig_id, rig = task("rigging", "/v1/rigging", rig_request, rig_request)
    for extension in ("glb", "fbx"):
        url = rig["result"].get("rigged_character_" + extension + "_url")
        if url:
            assets.append(fetch(url, OUT / ("LunaPeaceful_Rig." + extension)))
    # Rigging includes basic motion references without a separate paid request.
    for label, url in rig["result"].get("basic_animations", {}).items():
        if url and "armature" not in label:
            extension = ".fbx" if "_fbx_" in label else ".glb"
            assets.append(fetch(url, CACHE / ("basic_" + label.split("_")[0] + extension)))
    save(DOC, {"character_name": "문루나", "state": "peaceful", "provider": "Meshy AI",
        "created_utc": now(), "documentation": ["https://docs.meshy.ai/en/api/image-to-3d",
        "https://docs.meshy.ai/en/api/rigging"], "reference": descriptor,
        "image_to_3d_task_id": model_id, "rig_task_id": rig_id, "height_meters": 1.65,
        "task_state": load(STATE),
        "consumed_credits": {"model": model.get("consumed_credits"), "rig": rig.get("consumed_credits")},
        "assets": assets,
        "notes": "Peaceful original illustration recovered from user's prior character design. Rig ready for custom calm locomotion retargeting."})
    print(json.dumps({"model_task_id": model_id, "rig_task_id": rig_id, "assets": assets}, ensure_ascii=False), flush=True)


if __name__ == "__main__":
    main()
