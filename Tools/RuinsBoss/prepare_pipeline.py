"""Construct this task's independent resume-safe pipeline from the established local implementation."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
destination = ROOT / 'Tools/RuinsBoss'
original = (ROOT / 'Tools/RuinsMonsters/meshy_monsters.py').read_text(encoding='utf-8')
head = '''"""Meshy boss-support assets. Run plan, then run --budget 120. Keys resume persisted task IDs.

Four text assets cost at most 120 credits. Image boss and its rig add at most 44.
Only MESHY_API_KEY from environment is used. Never retry an ambiguous POST.
"""
'''
original = head + original[original.index('import argparse'):]
original = original.replace("Tools/RuinsMonsters/Source", "Tools/RuinsBoss/Source")
original = original.replace("Assets/RuinsMonsters/Art/Meshy", "Assets/RuinsBoss/Art/Meshy")
first = original.index('STYLE = ')
last = original.index('\n\n\ndef now():')
specs = '''STYLE = (' Single complete isolated original game asset, no floor or pedestal, all parts visible.'
         ' Rough heavy industrial salvage, worn angular armor, clear action-game silhouette.'
         ' No text, logos, neon or toy styling.')
ASSETS = {
    'iron_ram': {
        'triangles': 12000,
        'prompt': ('Massive low wide unmanned armored battering machine on two huge caterpillar tracks.'
                   ' Heavy sloped bulldozer wedge and short thick twin hydraulic impact pistons at front,'
                   ' squat asymmetric armored superstructure with one recessed amber sensor slit,'
                   ' exposed rear engine block, clustered exhaust pipes, thick welded patch plates,'
                   ' side cable conduits and scarred roll cage. Compact ground-hugging brute silhouette,'
                   ' no human head, no humanoid arms, no tank gun barrel. Front is positive Z.'),
        'texture_prompt': ('Rusty charcoal and faded olive salvaged steel, large dirty ivory welded replacement'
                           ' panels, orange brown rust streaks, black soot, tarnished brass hydraulic rods,'
                           ' dark oily tracks, small amber sensor slit. Heavy rough matte PBR metal,'
                           ' chipped paint, no symbols, writing, logos or colorful lights.'),
        'role': 'Left giant charge machine; rigid body for procedural suspension, recoil and charge motion.',
    },
    'siege_walker': {
        'triangles': 14000,
        'prompt': ('Enormous tall narrow four-legged industrial siege walker, an armored weapon platform'
                   ' held high on four clearly separated thick jointed hydraulic legs with broad metal feet.'
                   ' Upright rectangular furnace-like central armored torso, asymmetric shoulder rocket'
                   ' pod with round launch sockets, opposing thick cannon assembly, small recessed amber'
                   ' optical slit, exposed pistons, hanging short armored conduits, rear boiler exhausts.'
                   ' Rugged asymmetrical military salvage, no humanoid face, no tracked tank base.'
                   ' Front is positive Z.'),
        'texture_prompt': ('Worn gunmetal and charcoal steel, faded dark olive patches, one dirty ivory armor'
                           ' plate, rusted welded seams and rusty feet, brass piston shafts, soot around'
                           ' furnace vents, tiny amber optic, black missile tube recesses. Rough realistic'
                           ' industrial PBR metal. No letters, logos, neon or bright toy colors.'),
        'role': 'Right giant ranged machine; distinct tall four-leg silhouette and rocket launch mount.',
    },
    'missile_turret': {
        'triangles': 7500,
        'prompt': ('Heavy stationary salvaged missile turret, low wide armored turntable pedestal with'
                   ' reinforced mounting feet, three large vertical missile launch tubes clustered on top'
                   ' with open dark circular sockets facing upward, angled rectangular armor around the'
                   ' launcher rack, one side hydraulic support cylinder and exposed rear control box.'
                   ' Tubes empty, no missile inside, no smoke or projectiles. Industrial battlefield'
                   ' machinery, three distinct large upright launch barrels, compact centered silhouette.'),
        'texture_prompt': ('Rough charcoal armored steel, dirty faded olive launch tube casings, worn ivory'
                           ' side plate, rusty bolts, tarnished brass piston, oily dark launch sockets,'
                           ' soot and scratched paint. Heavy weathered PBR metal. No text, logos or decals.'),
        'role': 'Rear three-tube missile artillery emplacement; pivot, reload and firing animated in Unity.',
    },
    'apocalypse_missile': {
        'triangles': 2200,
        'prompt': ('One compact heavy industrial guided missile, long cylindrical steel body, pointed'
                   ' conical armored nose, four short triangular stabilizer fins around rear, one circular'
                   ' recessed rocket exhaust nozzle at tail, a few thick segmented body rings and rivets.'
                   ' One missile only, horizontal length axis, front nose pointing positive Z. No launcher,'
                   ' smoke, fire, stand, cable or extra missiles. Chunky readable projectile silhouette.'),
        'texture_prompt': ('Weathered dirty ivory and charcoal painted steel missile body, dark gunmetal'
                           ' nose and rear fins, faded olive body ring, orange rust scratches, brass seams,'
                           ' black exhaust cavity. Rough PBR industrial metal, no text, logos or symbols.'),
        'role': 'Shared solid missile projectile for siege walker and rear artillery turret.',
    },
}'''
original = original[:first] + specs + original[last:]
original = original.replace("    if key == 'mourning_matron' and (folder / 'rejected_text_body').exists():\n        raise RuntimeError('The Matron text body was rejected. Resume meshy_matron_image.py; do not restore the rejected body.')\n", '')
original = original.replace("theme='ruins/apocalypse'", "theme='ruins/apocalypse boss'")
original = original.replace("default=150", "default=120")
(destination / 'meshy_assets.py').write_text(original, encoding='utf-8')
(destination / 'Source').mkdir(parents=True, exist_ok=True)
(destination / '.gitignore').write_text('__pycache__/\n*.pyc\n', encoding='utf-8')
(destination / 'Source/.gitignore').write_text('*\n!.gitignore\n', encoding='utf-8')
print('Independent pipeline ready.')
