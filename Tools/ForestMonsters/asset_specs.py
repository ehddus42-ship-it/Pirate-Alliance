"""Original creatures matching the moonlit forest dungeon; no franchise designs."""

STYLE = (' Single isolated original stylized realistic fantasy game asset, complete visible object,'
         ' centered, forward faces +Z, up +Y. No floor, pedestal, text or logos.'
         ' Strong readable silhouette from an elevated three-quarter action game camera,'
         ' sculpted organic detail, clear large forms, elegant moonlit enchanted forest quality.')

ASSETS = {
    'moonworm': {
        'triangles': 14000,
        'target_size_m': [0.85, 0.85, 3.4],
        'prompt': ('Long giant EARTHWORM, soft smooth fleshy cylindrical tube with many thin shallow'
                   ' annular wrinkles, natural segmented earthworm anatomy. Horizontal gently S-shaped'
                   ' body stretched out along Z, length five times diameter. Rounded tapered head at +Z,'
                   ' slightly raised above ground, narrow tapered tail behind. One smooth wide clitellum'
                   ' band behind head. No eyes, teeth, armor, plates, scales, spikes or legs.'
                   ' Soft organic creature, not a snake, larva, tire or machine.'),
        'texture_prompt': ('Soft moist organic earthworm skin, rich muted jade green back blending into'
                           ' pale sage green underside, thin warm brown folds and small mint luminous'
                           ' freckles. Smooth satin skin with fine shallow annular wrinkles and natural'
                           ' fleshy color variation. NONMETALLIC soft organic flesh, no armor plates,'
                           ' no stone, no grey slabs, no scales, no metal, no writing.'),
        'role': 'Telegraphed burrow eruption and writhing charge; longitudinal vertex deformation in Unity.',
    },
    'moss_frog': {
        'triangles': 14000,
        'target_size_m': [2.0, 1.35, 2.1],
        'prompt': ('Large magical forest frog in natural low crouched rest pose facing +Z.'
                   ' Broad toadlike body, wide clearly defined mouth, two large golden turquoise eyes'
                   ' above snout, two short separated front legs, powerful folded hind thighs,'
                   ' visible webbed feet grounded. Leaflike brow crests, small moss tufts along back,'
                   ' pebbled damp skin. No clothing, staff or humanoid arms. Mouth slightly open'
                   ' but no visible extended tongue, no insect prey.'),
        'texture_prompt': ('Rich emerald and moss green frog skin, pale warm mint throat and belly,'
                           ' dark teal dorsal patches, golden amber irises, deep glossy pupils,'
                           ' small subtle cyan bioluminescent freckles on brows and back.'
                           ' Fine pebbled amphibian skin normals, moist satin finish, leaflike brow'
                           ' and natural moss. Elegant enchanted forest animal, no armor or writing.'),
        'role': 'Elastic hopping frog body; extendable tongue and victim pull are implemented in Unity.',
    },
    'lunar_butterfly': {
        'triangles': 12000,
        'target_size_m': [3.0, 0.5, 1.9],
        'prompt': ('Large magical luna butterfly with slender fuzzy insect body horizontally aligned along Z,'
                   ' small head at front +Z, tapered segmented abdomen behind, two delicate curved antennae.'
                   ' Four broad elegant butterfly wings fully spread flat horizontally left and right,'
                   ' clear narrow wing roots at the slender thorax, paired leaf-shaped forewings and'
                   ' smaller rounded hindwings, gently scalloped edges and raised organic wing veins.'
                   ' No support stalk, no branches, no flowers.'),
        'texture_prompt': ('Velvety dark teal insect body and pearl mint antennae. Butterfly wings with'
                           ' rich turquoise emerald gradients, pale moonlit mint veins, broad ivory'
                           ' crescent markings and subtle gold dust near scalloped edges.'
                           ' Matte iridescent biological wing scales and delicate raised veins,'
                           ' elegant magical forest palette, no flat icons or printed lettering.'),
        'role': 'Airborne caster; separated left/right wing sections flap, powder burst and vortex projectile in Unity.',
    },
    'elderwood': {
        'triangles': 16000,
        'target_size_m': [2.8, 3.8, 2.4],
        'prompt': ('Original walking ancient forest tree monster, upright full body facing +Z.'
                   ' Thick asymmetric twisted oak trunk torso with expressive glowing eye hollows and'
                   ' stern carved natural bark face, two long branch arms separated from torso,'
                   ' one broad root-claw hand open for throwing. Two short thick walking root legs'
                   ' clearly separated, three-toed root feet. Sparse crown of leafy boughs frames'
                   ' head without hiding face; moss pads and hanging vines. No weapons or stone base.'),
        'texture_prompt': ('Deep warm brown ancient oak bark with rich carved grooves and dry weathered'
                           ' ridges, emerald moss, layered green leaves with pale mint tips. Small cyan'
                           ' magical eye hollows and subtle turquoise sap cracks in chest. Warm wood'
                           ' highlights against dark bark valleys, high quality bark PBR normal relief,'
                           ' no metal, runic writing, logos, skulls or human skin.'),
        'role': 'Heavy ranged summoner; throws destructible saplings and sends arched travelling root attacks.',
    },
    'volatile_sapling': {
        'triangles': 8000,
        'target_size_m': [0.95, 1.25, 0.9],
        'prompt': ('Small original animated woodland sapling creature facing +Z, full body upright.'
                   ' Squat gnarled round seedpod trunk with two bright inset eye hollows and jagged'
                   ' natural wooden mouth, two short separated root legs, tiny branch arms out at'
                   ' sides, broad pale glowing seed bulb embedded in chest. Two fresh green sprouting'
                   ' leaves on crooked head twig, moss tufts and bark ridges. Mischievous dangerous'
                   ' forest seedling, no weapon, clothing, flowerpot or ground base.'),
        'texture_prompt': ('Warm walnut and weathered chestnut bark, vivid emerald moss, fresh spring green'
                           ' leaves, pale mint inner seed bulb and small turquoise eye hollows. Fine'
                           ' grooved bark normals, light sap cracks around swelling seed bulb, matte'
                           ' natural wood with glossy sap only. Same moonlit forest palette as elder tree,'
                           ' no metal, explosive hardware, lettering or symbols.'),
        'role': 'Destructible thrown sapling: arms, chases the player, explodes only if not destroyed.',
    },
    'burrow_earth_clod': {
        'triangles': 2500,
        'target_size_m': [0.7, 0.4, 0.6],
        'prompt': ('One irregular airborne clod of dark woodland earth ripped out of the forest floor.'
                   ' Compact broken lumpy soil chunk, rough torn earthy underside with embedded pebbles,'
                   ' small vivid moss patch on top and three short snapped exposed roots hanging below.'
                   ' Strong asymmetric organic silhouette, solid opaque mass, no long grass.'
                   ' A reusable dirt debris game prop, not a creature. No face, eyes, pot, base or ground.'),
        'texture_prompt': ('Rich dark chocolate damp soil with rough brown gravel, deep torn cavities,'
                           ' vivid emerald moss only on upper surface and pale warm brown broken roots.'
                           ' Natural tiny pebbles embedded in dirt, sculpted rough PBR soil relief.'
                           ' Matte nonmetallic earth, no glow, markings, painted symbols or plastic.'),
        'role': 'Reusable Meshy earth debris for telegraphed worm eruption and moving-root ground breaks.',
    },
}
