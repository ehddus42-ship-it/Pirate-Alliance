"""Create the review manifest and preview copies without publishing signed API URLs."""
import json
import pathlib
import shutil

from meshy_monsters import ASSETS, CACHE, OUT, ROOT, MODEL, glb_stats, load, now, save


def main():
    destination = ROOT / 'Documentation/ForestMonsters'
    destination.mkdir(parents=True, exist_ok=True)
    rows = {}
    for key, spec in ASSETS.items():
        model = OUT / key / (key + '.glb')
        record_path = OUT / key / 'provenance.json'
        if not model.exists() or not record_path.exists():
            rows[key] = {'status': 'pending'}
            continue
        record = load(record_path, {})
        state = load(CACHE / key / 'state.json', {})
        credits_all_attempts = sum(v.get('consumed_credits', v['reserved_credits'])
                                  for v in state.values()
                                  if isinstance(v, dict) and 'reserved_credits' in v)
        stats = glb_stats(model)
        low, high = stats['unity_bounds_min'], stats['unity_bounds_max']
        center_xz = [(low[0] + high[0]) / 2, (low[2] + high[2]) / 2]
        # Keep source geometry unscaled. The builder applies a single uniform scale,
        # then puts the bottom of the visible mesh on the character's ground plane.
        target = spec['target_size_m']
        size = [high[a] - low[a] for a in range(3)]
        primary = 2 if key == 'moonworm' else (0 if key == 'lunar_butterfly' else 1)
        uniform_scale = target[primary] / size[primary]
        record['actual'] = stats
        record['recommended_uniform_scale'] = uniform_scale
        record['recommended_local_offset_after_scale'] = [
            -center_xz[0] * uniform_scale, -low[1] * uniform_scale,
            -center_xz[1] * uniform_scale]
        save(record_path, record)
        thumbnail = CACHE / key / 'thumbnail.png'
        preview_path = destination / ('meshy-assets-preview-' + key + '.png')
        if thumbnail.exists():
            shutil.copyfile(thumbnail, preview_path)
        rows[key] = dict(
            status='generated', asset_path=model.relative_to(ROOT).as_posix(),
            preview_path=preview_path.relative_to(ROOT).as_posix(),
            role=spec['role'], requested_triangles=spec['triangles'],
            target_size_m=target, actual=stats,
            source_front_axis=record.get('unity_front_axis'),
            recommended_uniform_scale=uniform_scale,
            recommended_local_offset_after_scale=record['recommended_local_offset_after_scale'],
            texture_resolution=record['texture_resolution'],
            consumed_credits=credits_all_attempts, review=record['review'])
    save(destination / 'meshy-assets.json', dict(
        provider='Meshy AI', model=MODEL, theme='moonlit forest', generated_utc=now(),
        coordinate_note='Bounds are in Unity axes after glTFast X reflection. Source models keep original units.',
        grounding_rule='Scale uniformly, center X/Z and offset local Y by negative source bounds minimum times scale.',
        total_triangles=sum(r.get('actual', {}).get('triangles', 0) for r in rows.values()),
        total_bytes=sum(r.get('actual', {}).get('bytes', 0) for r in rows.values()),
        consumed_credits=sum(r.get('consumed_credits', 0) for r in rows.values()),
        assets=rows))
    print(json.dumps({k: {'status': r['status'], 'triangles': r.get('actual', {}).get('triangles')}
                      for k, r in rows.items()}, indent=2))


if __name__ == '__main__':
    main()
