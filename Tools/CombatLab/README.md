# Combat Lab (2D HTML prototype)

A browser prototype for checking and tuning movement speed and combat feel without opening Unity.
Open `combat-lab.html` in a browser (it is a single file with no build step).

- Controls: WASD to move, mouse to aim, LMB/J to attack (hold for the full six-hit combo), Space/Shift/RMB to dash, R to reset the room, H to show hitboxes, P to pause.
- The right-hand panel holds every tuning value. Changes apply immediately and are kept in the browser's localStorage. Values that differ from the Unity defaults are marked with a •.
- "값 JSON 복사" copies only the changed values as JSON, so they can be carried back into the Unity scripts.
- Defaults come from `PlayerMotor` / `PlayerCombat` (movement, dash, combo, lunge, hit stop, just dodge) and from the Copier, Locker and Monitor monster scripts in `Assets/Liminal/Scripts/Monsters/`.
- The "보간 곡선 · 그래프" panel edits eight interpolation curves (move accel/decel, dash and lunge speed profiles, hit stop and just-dodge slow recovery, knockback decay, shake decay) with draggable keys, smooth/linear modes and presets. A live playhead shows where the game is reading each curve, and the speed / time-scale strips plot the last 3 s. "Unity C# 복사" exports the curves as `AnimationCurve` fields whose Keyframe tangents evaluate the same in Unity; the JSON export carries the curves too.
