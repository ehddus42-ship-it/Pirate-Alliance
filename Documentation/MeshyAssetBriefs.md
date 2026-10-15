# Meshy 기물 제작 브리프

작성일: 2026-09-29 / 백룸 개정 갱신: 2026-09-30

상태: **기존 24종 + 신규 3종 = Meshy 기물 27종 반입 완료 / 현재 198개 배치 지점 모두 연결 / 개정 방 20종 치수·이동 연결 검증 통과**

기존 Meshy `meshy-7.1` GLB 24종은 `Assets/Liminal/Art/Meshy/<key>/<key>.glb`에 보존되어 있다. 이 24종의 합계는 560,136,816바이트, 2,345,642삼각형이다. 기물당 약 10만 폴리곤을 목표로 요청했으며 실제 삼각형 수는 84,964~103,874개다. **기존 24종은 4K 설정을 요청했지만 실제 반입 텍스처가 2K·4K 혼재한다.** 모든 GLB는 텍스처를 내부에 포함하며 외부 이미지 파일에 의존하지 않는다. [기존 24종 파일 검증](Liminal/meshy-assets-validation.json)

신규 `backrooms_workstation`, `poolroom_arch`, `industrial_fan`도 같은 경로 규칙으로 최종 GLB를 확정했다. 3종 합계는 70,576,480바이트·144,604삼각형이며, 실제 내장 base color·normal은 모두 4096×4096, metallic/roughness는 2048×2048이다. 생성 후보 6개에 총 **210 Meshy API credits**를 사용했다. 최종 채택 3개는 105 credits, 품질 비교용 미채택 3개는 105 credits다. 후보 수와 API 단계 수는 다르며 text-to-3D preview 4회·refine 4회·image-to-3D 2회가 기록되어 있다. [신규 3종 GLB·해상도·비용 검증](Liminal/backrooms-meshy-validation.json)

아래 영어 프롬프트와 치수는 디자인 브리프다. 실제 API에 전송한 프롬프트, 생성·재질 작업 식별자, 모델, 요청 해상도와 성공 상태는 각 기물 폴더의 `provenance.json`에서 확인한다. 로커와 공중전화는 첫 결과의 형태를 검토한 뒤 다시 생성해 교체했다. API 키와 비밀 토큰은 기록하지 않는다. 디자인 브리프의 모든 세부 기준이 자동 검사로 보장되는 것은 아니므로, 배치를 바꿀 때는 아래 장면 검수 기준을 다시 적용한다.

개정 후 20개 방의 Meshy 배치 지점은 **198곳이며 198곳 모두 연결, 누락 0**이다. 2026-09-30의 최종 치수·충돌·이동 검사가 20/20방을 통과했다. 초기 문서의 209곳은 이전 배치 수다. 기물 단독 전시는 `Assets/Liminal/Scenes/LiminalPropGallery.unity`, 재사용 프리팹은 `Assets/Liminal/Prefabs/Props`에 있다. [현재 20방 카탈로그](Liminal/Previews/Backrooms/Catalog_20Rooms.jpg) · [개정 검사](Liminal/backrooms-validation.json) · [이동 검사](liminal-traversal-validation.json)

공간 의도와 배치 기준은 [LiminalDesign.md](LiminalDesign.md)를 따른다.

## 공통 아트 기준

- 1990~2005년경 공공 시설에서 볼 법한 생활 기물. 특정 브랜드와 상표는 사용하지 않는다.
- 실제 구조를 알아볼 수 있는 절제된 사실성. 실루엣과 재료 차이는 게임 카메라 거리에서도 읽혀야 한다.
- 방치되어 무너진 물건보다, 사람이 사라진 뒤에도 사용 가능해 보이는 물건에 가깝게 만든다.
- 오염과 마모는 손이 닿는 곳, 모서리와 바닥 접촉부 중심으로 제한한다.
- 긴 안내 문구와 번호는 생성 텍스처에 맡기지 않는다. 필요한 글자와 발광 표시는 Unity에서 별도로 붙인다.
- 기물 한 종류를 독립적으로 생성한다. 배경 벽, 바닥, 전시 받침대, 인물과 주변 장식은 제외한다.
- 생성 결과의 실제 크기와 방향은 반입 후 조정한다. 아래 치수는 **게임 안에서 맞출 목표치**다.
- 축은 Unity 기준 `X = 너비`, `Y = 높이`, `Z = 깊이`다. 원본의 앞면·중심점은 생성 기록에 남기고, 방 배치용 부모 오브젝트에서는 검수한 로컬 `-Z` 앞면과 바닥 중심으로 보정한다. 원본 출력의 축이나 중심점을 그대로 게임 배치 기준이라고 가정하지 않는다.
- PBR 재료는 베이스 컬러, 거칠기/매끄러움, 금속성, 노멀을 실제 결과에 맞게 확인한다. 텍스처 이름만 보고 올바른 채널이라고 가정하지 않는다.

## 01. 대기 벤치 / Waiting Bench / `waiting_bench`

**목표 크기:** 너비 2.10m × 높이 0.82m × 깊이 0.62m. 앉는 높이 약 0.44m. 3인용.

**사용 위치:** 접수 로비, 대기 의자 홀, 종착 대합실. 여러 개를 같은 방향과 간격으로 놓는다. 한 개만 조금 떨어져 있거나 돌아서 있도록 하여 작은 차이를 만든다.

**English prompt**

> A single freestanding three-seat public waiting bench from a quiet civic terminal built around 1995. Three clearly separated molded pale sage-green polypropylene seats with gently curved backrests, mounted on a sturdy brushed stainless steel horizontal beam, two realistic floor-standing metal supports, subtle end armrests. Grounded semi-realistic game environment prop with a strong readable silhouette and believable manufactured construction. Fine molded plastic grain, slightly polished seat edges from ordinary use, restrained scratches on the metal feet, otherwise clean and maintained. The rear construction must be complete and plausible. All three seats aligned normally; no surreal distortion in the asset itself. No people, cushions, luggage, readable logos, floor, walls, scenery or pedestal. Isolated complete object, high-quality physically based materials, neutral lighting without baked shadows or dramatic highlights.

**검수 기준**

- 세 좌석, 등받이와 지지대가 명확히 구분된다. 좌석 사이가 뭉개져 있지 않다.
- 다리와 바닥 접점이 안정적이며 공중에 떠 있는 부품이 없다.
- 등받이 뒤쪽도 완성되어 여러 방향에서 배치할 수 있다.
- 플라스틱과 금속의 반응이 구분된다. 녹과 오염이 주된 인상이 되지 않는다.
- 충돌은 좌석의 큰 외곽에 맞추고 가느다란 부품에 캐릭터가 걸리지 않게 한다.

## 02. 음료 자판기 / Vending Machine / `vending_machine`

**목표 크기:** 너비 0.92m × 높이 1.90m × 깊이 0.84m.

**사용 위치:** 긴 객실 복도 끝, 폐점 푸드코트 가장자리, 대합실 벽면. 켜진 기계 하나가 멀리서 작은 생활 흔적으로 보이게 한다. 보상이나 상점으로 오해할 상호작용 표시는 붙이지 않는다.

**English prompt**

> A single full-height late-1990s beverage vending machine for an empty indoor transit facility. A rectangular off-white enamel-coated steel cabinet with softly rounded corners, muted teal accent panels, a recessed front display containing orderly generic unbranded beverage cans behind lightly tinted glass, a simple payment panel on the right, coin return, broad dispensing recess near the bottom, a small ventilation grille and realistic leveling feet. The upper lightbox is a blank pale cream panel, designed for a separate emissive material with no generated lettering. Restrained everyday wear on touch surfaces, slightly yellowed trim, clean maintained appearance. Believable front, sides and rear service panel. Grounded semi-realistic game prop, distinct glass, painted metal and rubber materials. No brands, text, neon cyberpunk styling, broken glass, trash, people, walls, ground plane or surrounding scenery. Complete isolated object with neutral lighting and no baked glow.

**검수 기준**

- 투입부, 상품 창, 배출구가 서로 구분된다. 큰 돌출과 움푹한 형태가 실제 기계처럼 보인다.
- 캔이나 버튼이 한 덩어리의 물결 모양으로 뭉개져 있지 않다.
- 조명판은 별도의 발광 설정 또는 교체 가능한 표면으로 처리할 수 있다.
- 글자처럼 보이는 무의미한 무늬와 실제 브랜드 로고를 제거한다.
- 배출구의 어둠을 텍스처에 과도하게 구워 넣지 않는다.

## 03. 청소 카트 / Janitor Cart / `janitor_cart`

**목표 크기:** 너비 0.60m × 높이 1.03m × 깊이 1.03m. 긴 손잡이나 청소 도구는 최고 1.45m 이내.

**사용 위치:** 기계실 연결부, 로비 구석, 수영장 입구. 일을 잠시 멈춘 듯 벽과 약간 비스듬하게 둔다. 출입구를 막는 크기로 배치하지 않는다.

**English prompt**

> A single practical janitorial cleaning cart used in a quiet public building around the year 2000. Muted slate-blue molded plastic chassis with two small shelves, a pale yellow wringer bucket seated securely in its tray, a folded gray cleaning cloth on the upper shelf, one upright mop held in a proper clip, a sturdy push handle, two larger rear wheels and two front casters. Every component must have a clear mechanical connection and believable thickness. The mop head is compact and rests within the cart footprint rather than spreading across an implied floor. Light water staining inside the bucket, subtle scuffing near wheels and handle, otherwise regularly maintained. Grounded semi-realistic environment prop with distinct rubber, plastic and metal materials. No readable labels, warning text, loose bottles, rubbish, person, room, floor plane or pedestal. Complete isolated object, neutral lighting, no baked contact shadow.

**검수 기준**

- 네 바퀴가 몸체에 연결되고 같은 바닥 높이에 닿는다.
- 손잡이, 대걸레와 물통이 서로 녹아 붙은 형태가 아니다.
- 멀리서는 카트, 가까이서는 각 도구로 읽힌다.
- 긴 손잡이의 두께가 지나치게 얇거나 휘어 있지 않다.
- 배치 후 시각적 외곽과 충돌이 크게 어긋나지 않는다.

## 04. 실내 화분 / Indoor Planter / `planter`

**목표 크기:** 너비 0.95m × 높이 2.20m × 깊이 0.95m. 화분 자체 높이 약 0.65m.

**사용 위치:** 실내 정원 중앙 또는 접수 로비의 큰 빈 공간 가장자리. 여러 작은 화분을 흩뿌리기보다 한 덩어리의 뚜렷한 기준점으로 사용한다.

**English prompt**

> A single indoor civic-building planter with a modest artificial ficus tree, approximately human-height plus a little more. A broad cylindrical cream terrazzo planter with a gently rounded rim and fine warm-gray aggregate, filled with dark covered planting substrate. Several believable woody stems support an airy, carefully arranged canopy of dark desaturated green oval leaves. Slightly manufactured regularity suggests an artificial indoor tree without making it cartoonish. Preserve clear gaps between leaf clusters and an uncluttered trunk silhouette. The planter looks maintained, with faint mineral wear at the lower edge and a few small surface marks. Grounded semi-realistic game prop with carefully differentiated leaf, bark and terrazzo surfaces. No flowers, trailing vines, fantasy growth, logos, room, soil mound outside the pot, floor or pedestal. Complete isolated object with realistic proportions and neutral lighting.

**검수 기준**

- 잎이 불투명한 덩어리 하나처럼 보이지 않고 큰 군집 사이에 틈이 있다.
- 줄기는 화분 안으로 이어지며 가지가 공중에 떠 있지 않다.
- 화분의 입구, 두께, 바닥이 분명하다.
- 캐릭터 시야를 가리면 나무 높이를 임의로 줄이기보다 배치 위치를 조정한다.
- 충돌은 화분과 굵은 줄기에 맞추고 잎 전체를 단단한 벽으로 만들지 않는다.

## 05. 플라스틱 의자 / Plastic Chair / `plastic_chair`

**목표 크기:** 너비 0.52m × 높이 0.82m × 깊이 0.54m. 앉는 높이 약 0.44m.

**사용 위치:** 의자 보관 복도, 세탁실, 복사실. 정상적인 줄 배치와 한 곳에 쌓은 더미를 함께 구성한다. 방향을 틀거나 뒤집는 변화는 Unity에서 편집한다.

**English prompt**

> A single ordinary stackable molded-plastic public waiting chair from around 1995. A one-piece muted ochre polypropylene seat and back shell with gently rounded edges, shallow horizontal ribs on the backrest and four slim but sturdy chromed tubular steel legs. No armrests. The open leg structure and shell shape should be believable for chairs that can stack together. Neutral upright pose, all four feet on one level, complete rear and underside. Restrained fine scratches and slight polish on the seat edge, otherwise intact and maintained. Grounded semi-realistic game environment prop with a readable silhouette and distinct plastic and metal surfaces. No people, desk, fabric cushion, wheels, logos, lettering, floor, wall or pedestal. Isolated complete object with neutral lighting and no baked shadows.

**검수 기준**

- 네 다리의 연결이 자연스럽고 같은 바닥 높이에 닿는다.
- 좌석과 등받이의 곡률이 일관되며 접합부가 불필요하게 두껍지 않다.
- 금속 다리와 플라스틱의 표면 차이가 조명에서 보인다.
- 잘 보이지 않는 뒤쪽에 큰 구멍이나 비정상적인 부품이 없다.
- 회전·이동해 여러 방향으로 배치해도 형태가 무너지지 않는다.

## 06. 안내판 / Directory Sign / `directory_kiosk`

**목표 크기:** 너비 0.84m × 높이 1.86m × 깊이 0.32m. 안내 면의 세부 텍스트는 별도 제작.

**사용 위치:** 로비와 종착 대합실. 빈 정보 칸으로 기능이 사라진 시설임을 보여준다. 실제 출구 안내와 혼동할 화살표는 필요한 곳에만 별도로 부착한다.

**English prompt**

> A single freestanding directory sign for a 1990s civic terminal or indoor shopping arcade. A tall narrow brushed aluminum frame with a shallow rectangular cabinet, rounded corner caps, two sturdy vertical supports and a low stable base. The front contains a large blank matte warm-white information panel divided by a few subtle horizontal inset rails, with a small blank muted teal header panel. The rear is a plain finished aluminum service surface. Designed to receive separately authored text and arrows later: all panels must remain clean and blank. Restrained fingerprints and edge wear, intact maintained condition, grounded semi-realistic game environment prop. Correct frame thickness and plausible base construction. No generated letters, symbols, logos, maps, glowing text, person, wall, floor plane or pedestal. Isolated complete object with neutral lighting and distinct metal and coated panel materials.

**검수 기준**

- 정보 면은 평평하고 글자를 얹을 여백이 있다.
- 양면과 측면에 무의미한 문자나 돌출 패턴이 없다.
- 받침대가 안정적이며 얇은 다리가 휘지 않는다.
- 배경용 안내판과 실제 진행 안내의 색·표시가 분명히 구별된다.

## 07. 접수대 / Reception Counter / `reception_desk`

**목표 크기:** 너비 2.70m × 높이 1.10m × 깊이 0.88m. 안쪽 작업 면 높이 약 0.75m.

**사용 위치:** 첫 로비의 대표 기물. 정면이 입구를 바라보며 뒤쪽에는 직원이 없어도 사람이 일할 수 있을 만큼의 공간을 남긴다.

**English prompt**

> A single complete public-facility reception counter from around 1995, wide enough for one or two staff members. A simple straight rectangular counter with gently rounded front corners, pale warm-gray laminate front panels, muted oak-effect side panels, a slightly raised off-white transaction ledge facing visitors and a lower practical staff work surface behind it. The rear must include an open knee space and one modest integrated drawer cabinet with plain recessed handles. A dark recessed toe kick runs along the base. No objects on top: leave the surfaces empty for separate prop placement. Subtle wear at hand-contact edges and a few ordinary scuffs near the base, intact and maintained. Grounded semi-realistic architectural prop with believable panel thickness, joinery and material scale. No cashier, computer, signs, text, logos, wall, floor or surrounding scene. Complete isolated object in neutral lighting without baked shadows.

**검수 기준**

- 뒤가 막힌 장식용 상자가 아니라 직원이 앉을 공간이 있다.
- 앞쪽 손님용 면과 뒤쪽 작업 면의 높이가 구분된다.
- 서랍과 손잡이는 일정한 방향과 크기로 배열되어 있다.
- 뒷면과 모서리도 검수해 여러 위치에서 사용할 수 있다.
- 카운터 위를 비워 두고 다른 기물을 독립적으로 놓을 수 있다.

## 08. 탈의실 로커 / Locker Bank / `lockers`

**목표 크기:** 너비 1.20m × 높이 1.85m × 깊이 0.50m. 3열 × 2단의 6칸 구성.

**사용 위치:** 탈의실 통로의 반복 벽면. 열이 이어지도록 배치하되 통로 폭을 유지한다. 열린 문 변형은 별도 검수 후 제한적으로 사용한다.

**English prompt**

> A single bank of six public swimming-pool lockers arranged in exactly three columns and two rows, built around the year 2000. A sturdy rectangular powder-coated steel cabinet in faded pale aqua, with six flat closed doors of equal size, small consistent horizontal ventilation slots, simple recessed dark latches and blank small number-label holders. Thin off-white dividers between columns and rows, realistic hinges, a slightly recessed base and short sturdy feet. Cabinet sides and rear are complete so the unit can be placed away from a wall if needed. Very restrained edge wear and slight discoloration near handles, otherwise clean and usable. Grounded semi-realistic game prop with straight repeatable geometry. No open doors, contents, readable numbers, brand logos, rust holes, people, room, floor or pedestal. Isolated object, neutral lighting, physically based painted metal materials.

**검수 기준**

- 정확히 3열 2단이며 문 크기와 간격이 일정하다.
- 반복 배치해도 상단과 하단 높이가 맞는다.
- 문이 서로 붙거나 손잡이가 임의로 늘어나지 않는다.
- 번호는 비어 있거나 별도 표면으로 교체할 수 있다.
- 열린 문이 필요하면 별도 기물로 만들어 실제 통행을 가리지 않는지 확인한다.

## 09. 수영장 사다리 / Pool Ladder / `pool_ladder`

**목표 크기:** 너비 0.64m × 높이 1.75m × 깊이 0.78m. 전체 높이는 수면 아래로 내려가는 부분을 포함한다. 난간 상단은 수영장 가장자리보다 약 0.65m 높다.

**사용 위치:** 수영장 홀의 물 가장자리. 맵 기준점은 바닥 중심 대신 **수영장 가장자리의 설치 높이**로 정해 배치 후 손잡이와 발판 높이를 확인한다.

**English prompt**

> A single complete stainless steel swimming-pool ladder for an indoor municipal pool. Two smooth parallel polished tubular handrails rise above the pool deck, curve gently over the edge and continue downward into the pool, joined by exactly three evenly spaced horizontal non-slip steps. Include small round mounting flanges where the rails attach to the deck and realistic protective feet at the lower ends. The ladder must be a coherent symmetrical manufactured object with constant tube thickness and plausible bends, no floating parts. Light mineral marks and fine scratches, clean maintained condition. Grounded semi-realistic game prop, highly readable tubular silhouette, brushed non-slip step surfaces contrasting with polished rails. No pool wall, water, tiles, person, text, logos, floor plane or pedestal. Complete isolated object in neutral lighting, no baked reflections or dramatic shadows.

**검수 기준**

- 난간 두 개와 발판 세 개가 실제로 연결되어 있다.
- 파이프 굵기가 일정하며 굽은 부분이 납작하게 뭉개지지 않는다.
- 설치 후 난간이 가장자리 위로, 발판이 물 쪽 아래로 향한다.
- 재료는 금속으로 보이되 검은 반사를 텍스처에 고정해 놓지 않는다.
- 장식용이면 이동 가능한 사다리처럼 오해할 상호작용 표시를 붙이지 않는다.

## 10. 공중전화 / Payphone / `payphone`

**목표 크기:** 본체 너비 0.37m × 높이 0.69m × 깊이 0.28m. 본체 중심 설치 높이 약 1.35m.

**사용 위치:** 객실 복도 끝과 종착 대합실 벽면. 벽에 장착되는 독립 기물로 만든다. 수화기는 제자리에 있고 연결할 사람만 없는 느낌을 유지한다.

**English prompt**

> A single wall-mounted public payphone from a civic transit building around 1995. A compact rectangular brushed stainless steel housing with a muted dark teal side casing, a sturdy black handset resting correctly in its cradle on the left, one short coiled black cable connecting the handset to the lower body, a clear three-by-four arrangement of blank tactile keypad buttons, a small blank recessed information panel, a coin slot and coin-return cup. Include a plausible flat rear mounting plate without any surrounding wall. Believable durable construction, restrained finger wear on the buttons and small scratches near the coin return, otherwise intact and maintained. Grounded semi-realistic game environment prop with clean readable shapes and distinct rubber, metal and plastic surfaces. No readable digits, lettering, brand marks, posters, people, booth, wall, floor plane or pedestal. Neutral lighting without baked shadows.

**검수 기준**

- 수화기, 거치대와 본체가 명확히 구분된다.
- 케이블 양 끝이 연결되어 있으며 바닥까지 불필요하게 늘어지지 않는다.
- 키패드는 3열 4행으로 일정하다. 의미 없는 숫자 텍스처는 제거한다.
- 벽에 붙이는 뒤판이 평평하고, 장착 시 본체가 벽에 과하게 묻히지 않는다.
- 전화부스 전체가 생성되었다면 독립 기물 요구에 맞게 다시 제작한다.

## 11. 정수기 / Water Dispenser / `water_dispenser`

**목표 크기:** 너비 0.34m × 높이 1.33m × 깊이 0.36m. 상단 생수통 포함.

**사용 위치:** 대기실 끝, 사무실 벽면, 정수기 홀. 정수기 홀은 같은 기계를 한 벽에 반복하고 중앙에는 한 대만 두어 편의 시설과 공간 크기의 부조화를 만든다.

**English prompt**

> A single freestanding bottled water dispenser from a late-1990s public office. A narrow off-white molded plastic body with soft rectangular corners, two distinct small dispensing taps in a recessed front bay, a removable gray drip tray, a plain lower service panel and a large translucent desaturated blue water bottle mounted upside down on top. The bottle has a plausible neck connection and subtle molded rings, no liquid splash. Restrained yellowing on plastic edges and minor ordinary wear, intact and maintained. Grounded semi-realistic game prop, complete sides and back, clear silhouette and realistic thickness. No cup stacks, labels, readable text, logos, people, room, floor or pedestal. Neutral lighting without baked highlights or shadows.

**검수 기준:** 생수통과 몸체 연결이 맞는다. 두 꼭지와 물받이가 구분된다. 통의 투명도 때문에 뒤가 검거나 빈 구멍처럼 보이지 않는다. 같은 모델을 나열해도 크기와 바닥 기준이 일정하다.

## 12. 복사기 / Photocopier / `photocopier`

**목표 크기:** 너비 0.72m × 높이 1.14m × 깊이 0.76m.

**사용 위치:** 사무실 구석과 복사실. 여러 대의 복사기를 정상 방향으로 놓고, 의자만 그중 한 대를 향하게 한다. 종이 산이나 설명 문구는 추가하지 않는다.

**English prompt**

> A single floor-standing office photocopier from around 2000, built for a municipal office. Warm-gray plastic and painted metal cabinet, an automatic document feeder with a closed scanner lid, a small angled blank control display, orderly blank physical buttons, three broad paper drawers, a recessed side paper-output tray and small realistic caster feet. Believable panel gaps, vents, handles and proportions; no exaggerated futuristic elements. Subtle scuffing at drawer handles and lower corners, otherwise maintained. Grounded semi-realistic game prop with complete rear service panels, clear large forms and restrained detail. No paper piles, readable text, brand names, cables extending into a scene, person, walls, ground plane or pedestal. Neutral lighting and physically based materials without baked shadows.

**검수 기준:** 급지부, 출력부, 서랍이 따로 읽힌다. 작은 버튼이 뭉개진 돌기 집합처럼 보이지 않는다. 뒤쪽도 완성한다. 의자와 크기를 비교했을 때 실제 사무기기 비례가 된다.

## 13. 수하물 카트 / Luggage Cart / `luggage_cart`

**목표 크기:** 너비 0.65m × 높이 1.08m × 깊이 1.00m.

**사용 위치:** 로비와 카트 보관소. 짐 없이 같은 방향으로 가지런히 줄 세운다. 정상 열에서 한 대만 떨어져 있도록 배치한다.

**English prompt**

> A single empty luggage trolley for a small indoor transport terminal around 1995. A sturdy brushed stainless tubular frame, a broad low ribbed metal luggage platform, one shallow wire basket beneath the push handle, a muted dark-gray rubber handle grip, two larger rear wheels and one centered front swivel wheel. Every bar should connect mechanically, with consistent tube thickness and realistic bends. The cart is empty and stands level in a neutral position. Fine scratches and lightly worn rubber wheels, maintained condition. Grounded semi-realistic game prop with an open readable silhouette and believable nesting construction. No suitcase, person, chains to other carts, text, logos, room, ground plane or pedestal. Complete isolated object in neutral lighting.

**검수 기준:** 프레임이 실제로 연결되고 바구니 내부가 막히지 않는다. 세 바퀴의 위치와 바닥 접점이 맞는다. 카트를 겹쳐 보관할 때 큰 형태가 서로 심하게 관통하지 않는다.

## 14. 발권기 / Ticket Machine / `ticket_machine`

**목표 크기:** 너비 0.74m × 높이 1.66m × 깊이 0.55m.

**사용 위치:** 무인 발권소와 대합실 입구. 같은 기계 여러 대 중 표시등 한두 개만 켜진 상태로 둔다. 실제 강화 선택 장치로 사용하지 않는다.

**English prompt**

> A single late-1990s indoor transit ticket vending machine. A freestanding muted blue-gray steel kiosk with a slightly sloped front control surface, one modest recessed dark blank screen, a tidy group of plain physical buttons, a realistic coin slot, card slot, ticket dispensing opening and coin-return recess. The lower cabinet is simple and solid with a small service door, ventilation slits and a flat stable base. Clear functional hierarchy, believable manufactured construction and restrained touch wear. Grounded semi-realistic game prop with complete rear and side panels. All screen and label areas blank for later graphics. No letters, numbers, payment brands, futuristic holograms, people, station wall, floor plane or pedestal. Neutral lighting with no baked glow or shadows.

**검수 기준:** 투입구와 배출구의 용도가 형태로 구별된다. 안내 면은 평평하고 교체 가능하다. 같은 줄의 기계 높이가 맞는다. 발광은 별도로 조절한다.

## 15. 개찰구 / Turnstile / `turnstile`

**목표 크기:** 몸체 너비 0.28m × 높이 1.02m × 깊이 1.18m. 회전봉 포함 너비 약 0.78m.

**사용 위치:** 무인 발권소. 장식용 줄과 실제 통과 구간을 구별한다. 실제 통로는 봉을 제거하거나 열린 상태의 별도 변형으로 구성한다.

**English prompt**

> A single waist-high tripod turnstile from a modest 1990s indoor transit station. A narrow elongated brushed stainless steel cabinet with softly rounded ends, a small blank dark card-reader plate on top, a simple side service door and a believable central rotating hub with exactly three straight tubular arms spaced evenly around it. Consistent metal thickness, practical mechanical joints and a stable floor-mounted base. Neutral resting position, restrained contact scratches, otherwise clean and functional. Grounded semi-realistic game prop, complete all-around construction and readable silhouette. No adjacent gates, fence, person, floor slab, readable arrows, letters, brands or pedestal. Isolated object with neutral lighting, no baked reflections.

**검수 기준:** 회전축과 봉 세 개가 정확히 연결된다. 열린 통로 변형 제작이 가능한지 확인한다. 카메라에서 길을 막는 장식과 통행 가능한 곳의 차이가 분명해야 한다.

## 16. 환기 장치 / Ventilation Unit / `ventilation_unit`

**목표 크기:** 너비 1.10m × 높이 1.35m × 깊이 0.65m.

**사용 위치:** 기계실 연결부와 주차장 벽 가장자리. 배관 모듈과 연결되지만 독립 기물로 편집한다. 소리의 위치를 설명하는 기물로 사용한다.

**English prompt**

> A single compact industrial air-handling ventilation cabinet for the service area of a public building. A rectangular dull galvanized steel body on two low support rails, a large clearly defined circular fan behind a sturdy square protective grille on the front, a side maintenance panel with simple latches, a short rectangular duct connection on top and restrained external conduit detail. Plausible mechanical construction with visible fasteners and believable sheet-metal thickness. Light dust near the grille and subtle oxidation at seams, not heavily corroded. Grounded semi-realistic game environment prop with complete sides and rear. No building wall, long attached duct network, labels, text, logos, person, floor plane or pedestal. Neutral lighting and physically based metal surfaces.

**검수 기준:** 팬과 보호망이 서로 붙어 보이지 않는다. 연결부를 다른 건축 모듈에 맞출 수 있다. 주 통로를 차지하지 않도록 몸체 외곽에 충돌을 맞춘다.

## 17. 세탁기 / Laundry Washer / `laundry_washer`

**목표 크기:** 너비 0.74m × 높이 1.05m × 깊이 0.78m.

**사용 위치:** 세탁 대기실의 벽면 반복. 동일한 원형 문 간격과 높이를 맞추고 내용물은 비운다. 행의 마지막 한 대만 약간 다른 방향으로 놓는 변형을 허용한다.

**English prompt**

> A single commercial front-loading washing machine from a neighborhood laundromat around 1995. A square warm-white enamel steel cabinet, one large circular smoked-glass door with a brushed metal ring and clearly defined hinge, a simple upper control strip with one plain rotary dial and a blank small display, a compact coin mechanism on the upper right, a narrow lower service panel and short leveling feet. Empty drum visible subtly through the glass. Believable straight repeatable geometry for placing machines side by side, complete rear with compact hose connections. Restrained edge wear and mild discoloration, maintained condition. Grounded semi-realistic game prop. No clothes, foam, water spill, labels, numbers, brand names, person, wall, floor or pedestal. Neutral lighting.

**검수 기준:** 원형 도어가 실제 원으로 유지되고 다른 면에 녹아들지 않는다. 나란히 놓을 때 몸체가 직선으로 맞는다. 유리 안 드럼이 불필요하게 밝거나 왜곡되지 않는다.

## 18. 건조기 / Laundry Dryer / `laundry_dryer`

**목표 크기:** 너비 0.82m × 높이 1.95m × 깊이 0.84m. 상하 2단 구성.

**사용 위치:** 세탁실 끝 벽. 낮은 세탁기와 다른 높이를 만들어 공간 끝을 강조한다. 두 원형 문을 세탁기 줄과 시각적으로 연결한다.

**English prompt**

> A single stacked two-drum commercial tumble dryer cabinet from a late-1990s public laundromat. Two equal large circular glass-fronted dryer doors arranged vertically in one tall off-white enamel steel housing, each with a restrained brushed metal rim and a simple robust handle. A narrow central control strip has plain tactile buttons and blank display panels, with one compact coin slot. Empty dark drums, a small lower ventilation grille, realistic hinges and service seams. Straight stable cabinet designed for repeated side-by-side placement. Slight ordinary handle wear, intact and maintained. Grounded semi-realistic game environment prop with complete sides and rear. No clothes, labels, logos, readable text, people, room, floor or pedestal. Neutral lighting without baked shadows.

**검수 기준:** 두 드럼 크기와 중심이 맞는다. 문 사이의 제어부가 명확하다. 2m 안팎 기물로 보이는 비례가 유지된다. 드럼 내부가 막힌 검은 얼룩처럼 보이지 않는다.

## 19. 쓰레기통 / Trash Bin / `trash_bin`

**목표 크기:** 너비 0.53m × 높이 0.97m × 깊이 0.46m.

**사용 위치:** 푸드코트 기둥 옆과 대합실 가장자리. 테이블·기둥 간격과 연결되는 반복 요소로 사용한다. 넘치는 쓰레기는 넣지 않는다.

**English prompt**

> A single freestanding public food-court waste bin from around 1995. A tall rectangular muted brick-red molded cabinet with softly rounded edges, a dark recessed swing-flap opening near the top front, a shallow flat tray-return recess on top, a plain lower service door and a dark durable base. Plausible plastic and coated metal construction, modest manufactured seams and recessed handles. Clean and empty, with restrained scuffing near the base and touch-polished flap edges. Grounded semi-realistic game prop with complete all-around surfaces and a readable silhouette. No garbage, food scraps, bags, recycling icons, words, brand logos, people, tables, room, floor plane or pedestal. Neutral lighting without baked shadows.

**검수 기준:** 상단 트레이 받침과 투입구가 구분된다. 비어 있는 시설의 느낌을 유지한다. 표면 글자와 상표가 없다. 캐릭터가 모서리에 걸리지 않게 단순 충돌을 쓴다.

## 20. 주의 표지 / Caution Sign / `caution_sign`

**목표 크기:** 너비 0.31m × 높이 0.62m × 깊이 0.34m. 펼친 상태.

**사용 위치:** 닫힌 정문 전실, 청소 카트 주변, 수영장 입구. 위험 요소가 없는 빈 바닥을 향하도록 두어 기능의 어긋남을 만들 수 있다. 실제 위험 안내는 다른 표시로 명확히 구분한다.

**English prompt**

> A single open A-frame folding caution sign used by a cleaner in a public building. Two slim mustard-yellow molded plastic panels joined by a realistic top hinge, a broad integrated carry-handle cutout near the top and stable splayed feet. Both sign faces are flat blank recessed panels intended for separately applied graphics. Believable panel thickness and hinge construction, subtle scratches and slight darkening around the lower edges, otherwise intact. Grounded semi-realistic game environment prop with a simple crisp silhouette and complete rear panel. No lettering, warning symbols, logos, wet floor, puddles, person, mop, room, floor plane or pedestal. Isolated object, neutral lighting and no baked shadows.

**검수 기준:** 두 판과 경첩이 분리되어 읽힌다. 글자를 붙일 면이 평평하다. 낮은 기물이므로 바닥과 묻히지 않게 색 대비와 충돌을 확인한다.

## 21. 벽시계 / Wall Clock / `wall_clock`

**목표 크기:** 지름 0.38m × 두께 0.045m. 벽 설치 중심 높이 약 2.25m.

**사용 위치:** 로비, 세탁실, 정문 전실. 서로 다른 방에서 같은 시간을 가리키는 연출에 사용할 수 있다. 이 경우 바늘 각도는 별도로 편집할 수 있게 한다.

**English prompt**

> A single simple round analog wall clock from a 1990s school or civic office. A shallow dark charcoal plastic circular rim, a warm-white matte dial with twelve clean simple hour tick marks and no numerals, two clearly separated plain black hands, a small central pin and a transparent flat protective cover. Balanced ordinary proportions, consistent circular shape, complete flat rear mounting surface. Restrained fine scratches on the rim, intact and maintained. Grounded semi-realistic game prop; the dial should be clean and readable for later replacement or separate hand setup. No lettering, brand marks, ornate decoration, alarm bells, wall, person, room, floor or pedestal. Front facing neutral orientation, neutral lighting without baked reflections.

**검수 기준:** 원형이 찌그러지지 않는다. 숫자 대신 12개 눈금이 일정하다. 바늘이 텍스처에 뭉개졌으면 별도 바늘과 문자판으로 교체한다. 벽 설치 방향과 두께를 확인한다.

## 22. 형광등 기구 / Fluorescent Fixture / `fluorescent_fixture`

**목표 크기:** 길이 1.24m × 높이 0.09m × 폭 0.28m. Unity 방향에 맞춰 장축 보정.

**사용 위치:** 복도·사무실·주차장 반복. 조명의 간격 자체가 공간의 길이와 정상적인 반복을 만든다. 광원은 별도로 붙여 밝기와 색을 제어한다.

**English prompt**

> A single surface-mounted twin-tube fluorescent ceiling light fixture from a late-1990s public building. A long narrow off-white pressed-metal housing containing exactly two parallel frosted white fluorescent tubes, each held by small realistic end sockets, with a shallow protective metal reflector and complete flat upper mounting surface. Precise straight geometry, consistent tube thickness and believable wiring enclosure, no visible loose wires. Mild yellowing of the housing and subtle dust at seams, otherwise intact. Grounded semi-realistic game environment prop intended to receive separate emission and lighting in the engine. Tubes unlit in neutral material preview. No baked glow, lighting rays, ceiling tile, room, chains, labels, logos, people, floor or pedestal. Isolated complete object with neutral lighting.

**검수 기준:** 두 관과 소켓이 실제로 연결된다. 관에 별도 발광 재료를 적용할 수 있다. 조명 효과가 텍스처에 고정되지 않는다. 천장에 붙였을 때 윗면이 관통해 보이지 않는다.

## 23. 푸드코트 테이블 / Food-Court Table / `foodcourt_table`

**목표 크기:** 상판 지름 0.88m × 높이 0.74m. 받침 지름 약 0.52m.

**사용 위치:** 푸드코트와 폐점 식당. 의자는 독립 기물로 정상 배치하거나 상판에 뒤집어 놓는다. 식당 전체가 폐점 상태여도 한 테이블만 정상 상태로 남길 수 있다.

**English prompt**

> A single round freestanding food-court table from a shopping mall around 1995. A modest circular warm-ivory laminate tabletop with a thin muted teal protective edge band, one sturdy brushed-metal central pedestal and a broad flat circular dark-gray weighted base. Believable tabletop thickness and manufactured proportions, enough clear upper surface for separate chairs or small props to be placed later. Restrained fine scratches and light wear along the edge, otherwise clean and maintained. Grounded semi-realistic game prop with distinct laminate, coated edge and metal materials, complete underside joinery. No attached chairs, food, plates, text, logos, person, room, floor plane or pedestal beyond the table's own base. Neutral lighting without baked shadows.

**검수 기준:** 상판과 받침이 동심에 가깝고 기울지 않는다. 의자를 뒤집어 올릴 넓이가 실제로 확보된다. 표면의 강한 얼룩이나 식기 흔적을 제거한다.

## 24. 접이식 통행 가림대 / Folding Barrier / `folding_barrier`

**목표 크기:** 펼친 너비 1.80m × 높이 0.95m × 깊이 0.40m.

**사용 위치:** 카트 보관소와 닫힌 정문 전실. 쓸모없는 빈 벽 앞을 통제하거나 닫힌 문을 한 번 더 막는 장면에 둔다. 실제 진행로에는 넓은 통과 공간을 남긴다.

**English prompt**

> A single portable expandable accordion safety barrier used indoors in an older public building. A waist-high arrangement of connected flat muted yellow and dark charcoal metal scissor slats forming a regular series of diamond openings between two sturdy end posts, with broad stable black feet. Moderately extended to a practical width, all pivots and crossing slats mechanically connected and consistent in thickness. A small blank panel on one end post may receive signage later. Restrained edge chips and light wheel-free base scuffing, intact and maintained. Grounded semi-realistic game environment prop with a clearly open lattice silhouette, complete rear construction. No warning text, logos, people, ropes, surrounding fence, room, ground plane or pedestal. Isolated complete object, neutral lighting without baked shadows.

**검수 기준:** 격자 교차점이 연결되고 빈 마름모 공간이 유지된다. 접이식 구조가 벽처럼 한 덩어리로 뭉개지지 않는다. 받침과 충돌을 맞춰 막힌 길임을 명확히 읽게 한다.

## 25. 백룸 사무 작업대 / Backrooms Workstation / `backrooms_workstation`

**게임용 배치 기준:** 너비 3.3m × 높이 1.75m × 깊이 1.9m. 생성 원본의 실측 크기가 아니라 `PropSize`의 정규화 기준이며, 원본 비례를 유지해 맞춘다.

**사용 위치:** 노란 대합실, 무인 사무실, 복사기 대기실과 노란 기둥 홀. 머스터드색 천 파티션, 상아색 책상, CRT 모니터와 서랍을 하나의 큰 사무 가구 덩어리로 읽히게 한다. 의자는 별도 기물로 배치한다.

**제작 방식:** 이미지 기준 `image-to-3d`, `meshy-7.1`, 목표 50,000 폴리곤. 최종 결과는 44,513삼각형·29,054,632바이트다. 노란 공간의 색과 넓은 책상 상판을 유지하도록 생성 후보를 비교했다.

**검수 기준:** CRT와 책상·파티션의 관계가 쿼터뷰에서도 구별된다. 파티션을 중앙 전투 바닥까지 밀어 넣지 않는다. 모델 원본은 그대로 보존하고 배치용 부모의 방향·충돌을 조정한다.

[최종 GLB](../Assets/Liminal/Art/Meshy/backrooms_workstation/backrooms_workstation.glb) · [생성 기록](../Assets/Liminal/Art/Meshy/backrooms_workstation/provenance.json) · [참고 이미지 프롬프트](../Assets/Liminal/Art/Meshy/backrooms_workstation/reference_prompt.txt)

## 26. 풀룸 타일 아치 / Poolroom Arch / `poolroom_arch`

**게임용 배치 기준:** 너비 4.2m × 높이 4.5m × 깊이 1.15m. 생성 요청의 치수와 게임 배치 기준은 다르며, 원본 비례를 유지해 정규화한다.

**사용 위치:** 푸른 수영장, 분수 홀과 물의 아치 회랑. 상아색 타일과 아래쪽 청록색 띠를 가진 둥근 사각 개구부로 풀룸의 반복 구조를 만든다. 마른 중앙 이동 축 바깥에 배치한다.

**제작 방식:** `text-to-3d` 후 재질 생성, `meshy-7.1`, 목표 50,000 폴리곤. 최종 결과는 50,314삼각형·19,133,896바이트다. 첫 형태를 검토한 뒤 넓고 막히지 않은 개구부를 가진 결과로 교체했다.

**검수 기준:** 아래를 가로막는 받침이나 전체 문틀을 채우는 충돌 상자를 만들지 않는다. 재사용 프리팹은 좌우 발 두 개의 BoxCollider를 사용하며, 최종 검증에서 중앙 충돌 간격 약 2.403m와 통행 여유를 확인했다. 앞뒤에서 타일과 개구부가 이어진다.

[최종 GLB](../Assets/Liminal/Art/Meshy/poolroom_arch/poolroom_arch.glb) · [생성 기록](../Assets/Liminal/Art/Meshy/poolroom_arch/provenance.json) · [개구부 검사](Liminal/backrooms-validation.json)

## 27. 산업용 대형 환풍기 / Industrial Fan / `industrial_fan`

**게임용 배치 기준:** 너비 3.4m × 높이 3.4m × 깊이 0.7m. 원본 비례를 유지해 정규화하고 벽면 높이에 배치한다.

**사용 위치:** 끝없는 노란 홀의 뒤쪽 외곽. 크림색 사각 케이스 안의 어두운 원형 그릴과 큰 날개가 보스 공간의 크기를 보여 준다. 중앙 카펫과 보스 공격 예고는 비워 둔다.

**제작 방식:** 최종 채택본은 `text-to-3d` 후 재질 생성, `meshy-7.1`, 목표 50,000 폴리곤. 최종 결과는 49,777삼각형·22,387,952바이트다. 별도의 이미지 기반 후보도 생성되었으나 Unity 보스 공간에서 확인한 현재 모델을 최종본으로 유지했다.

**검수 기준:** 밝은 사각 외곽과 어두운 원형 내부가 한눈에 구별된다. 게임 카메라에서 그릴 뒤 날개가 형태로 읽히며 벽과 겹쳐 깜박이지 않도록 배치한다.

[최종 GLB](../Assets/Liminal/Art/Meshy/industrial_fan/industrial_fan.glb) · [생성 기록](../Assets/Liminal/Art/Meshy/industrial_fan/provenance.json)

신규 3종 공통으로 GLB 2.0·파일 길이·UV·노멀·재료 연결·내장 이미지 해상도를 검사했다. 요청은 4K였지만 실제 PBR 맵은 base color·normal 4K와 metallic/roughness 2K의 조합이다. [검증 JSON](Liminal/backrooms-meshy-validation.json)

## 공통 반입 및 장면 검수

1. 생성 결과를 정면, 측면, 뒷면과 게임 카메라 거리에서 확인한다.
2. 크기를 목표 치수에 맞추고 Unity의 1m 기준 물체와 비교한다.
3. 방향과 배치 기준점을 보정한다. 같은 기물을 여러 개 배치해 일관성을 확인한다.
4. 재료 연결과 색 공간, 금속성, 거칠기/매끄러움, 노멀 방향을 확인한다.
5. 강한 빛과 어두운 곳에서 각각 확인한다. 생성된 조명이 텍스처에 고정돼 보이지 않아야 한다.
6. 큰 외곽에 맞는 충돌을 만들고 캐릭터의 이동과 회피로 확인한다.
7. 대표 방에 배치해 시야, 출구, 적의 공격 신호를 가리지 않는지 확인한다.
8. 기물 단독으로 좋아 보여도 방의 분위기를 해치면 크기, 색, 위치 또는 생성 결과를 수정한다.
9. 원본 결과와 게임용 프리팹을 구분해 보존한다. 원본을 덮어써서 수정 과정을 잃지 않는다.

질감이 흐려지거나 구조가 뭉개진 결과를 완료로 처리하지 않는다. 필요하면 다시 생성하거나 수정하고 같은 장면에서 비교한다.

## 제작 기록

기존 24종의 제작 기록은 아래에 보존하고 신규 3종을 끝에 추가했다. 두 검증 JSON에서 Meshy 성공 기록과 GLB 2.0 형식, 파일 길이, 내장 텍스처를 확인할 수 있다. 아래 삼각형 수는 실제 GLB에서 읽은 값이다. 가장 큰 개별 파일은 푸드코트 테이블의 33,419,172바이트다. 생성 결과의 이용 조건은 소유자의 Meshy 계정 요금제와 약관을 따른다.

| 기물 | GLB 원본 | 실제 삼각형 | 생성 기록 | 추가 기록 |
| --- | --- | ---: | --- | --- |
| 대기 벤치 | [waiting_bench](../Assets/Liminal/Art/Meshy/waiting_bench/waiting_bench.glb) | 95,412 | [provenance](../Assets/Liminal/Art/Meshy/waiting_bench/provenance.json) | 완료 |
| 자판기 | [vending_machine](../Assets/Liminal/Art/Meshy/vending_machine/vending_machine.glb) | 97,787 | [provenance](../Assets/Liminal/Art/Meshy/vending_machine/provenance.json) | 완료 |
| 청소 카트 | [janitor_cart](../Assets/Liminal/Art/Meshy/janitor_cart/janitor_cart.glb) | 101,344 | [provenance](../Assets/Liminal/Art/Meshy/janitor_cart/provenance.json) | 완료 |
| 실내 화분 | [planter](../Assets/Liminal/Art/Meshy/planter/planter.glb) | 103,567 | [provenance](../Assets/Liminal/Art/Meshy/planter/provenance.json) | 완료 |
| 플라스틱 의자 | [plastic_chair](../Assets/Liminal/Art/Meshy/plastic_chair/plastic_chair.glb) | 99,308 | [provenance](../Assets/Liminal/Art/Meshy/plastic_chair/provenance.json) | 완료 |
| 안내판 | [directory_kiosk](../Assets/Liminal/Art/Meshy/directory_kiosk/directory_kiosk.glb) | 100,930 | [provenance](../Assets/Liminal/Art/Meshy/directory_kiosk/provenance.json) | 완료 |
| 접수대 | [reception_desk](../Assets/Liminal/Art/Meshy/reception_desk/reception_desk.glb) | 94,536 | [provenance](../Assets/Liminal/Art/Meshy/reception_desk/provenance.json) | 완료 |
| 로커 | [lockers](../Assets/Liminal/Art/Meshy/lockers/lockers.glb) | 88,248 | [provenance](../Assets/Liminal/Art/Meshy/lockers/provenance.json) | 형태 검토 후 재생성 |
| 수영장 사다리 | [pool_ladder](../Assets/Liminal/Art/Meshy/pool_ladder/pool_ladder.glb) | 93,122 | [provenance](../Assets/Liminal/Art/Meshy/pool_ladder/provenance.json) | 완료 |
| 공중전화 | [payphone](../Assets/Liminal/Art/Meshy/payphone/payphone.glb) | 100,167 | [provenance](../Assets/Liminal/Art/Meshy/payphone/provenance.json) | 형태 검토 후 재생성 |
| 정수기 | [water_dispenser](../Assets/Liminal/Art/Meshy/water_dispenser/water_dispenser.glb) | 92,038 | [provenance](../Assets/Liminal/Art/Meshy/water_dispenser/provenance.json) | 완료 |
| 복사기 | [photocopier](../Assets/Liminal/Art/Meshy/photocopier/photocopier.glb) | 95,567 | [provenance](../Assets/Liminal/Art/Meshy/photocopier/provenance.json) | 완료 |
| 수하물 카트 | [luggage_cart](../Assets/Liminal/Art/Meshy/luggage_cart/luggage_cart.glb) | 102,069 | [provenance](../Assets/Liminal/Art/Meshy/luggage_cart/provenance.json) | 완료 |
| 발권기 | [ticket_machine](../Assets/Liminal/Art/Meshy/ticket_machine/ticket_machine.glb) | 102,655 | [provenance](../Assets/Liminal/Art/Meshy/ticket_machine/provenance.json) | 완료 |
| 개찰구 | [turnstile](../Assets/Liminal/Art/Meshy/turnstile/turnstile.glb) | 99,551 | [provenance](../Assets/Liminal/Art/Meshy/turnstile/provenance.json) | 완료 |
| 환기 장치 | [ventilation_unit](../Assets/Liminal/Art/Meshy/ventilation_unit/ventilation_unit.glb) | 98,524 | [provenance](../Assets/Liminal/Art/Meshy/ventilation_unit/provenance.json) | 완료 |
| 세탁기 | [laundry_washer](../Assets/Liminal/Art/Meshy/laundry_washer/laundry_washer.glb) | 98,702 | [provenance](../Assets/Liminal/Art/Meshy/laundry_washer/provenance.json) | 완료 |
| 건조기 | [laundry_dryer](../Assets/Liminal/Art/Meshy/laundry_dryer/laundry_dryer.glb) | 100,928 | [provenance](../Assets/Liminal/Art/Meshy/laundry_dryer/provenance.json) | 완료 |
| 쓰레기통 | [trash_bin](../Assets/Liminal/Art/Meshy/trash_bin/trash_bin.glb) | 103,527 | [provenance](../Assets/Liminal/Art/Meshy/trash_bin/provenance.json) | 완료 |
| 주의 표지 | [caution_sign](../Assets/Liminal/Art/Meshy/caution_sign/caution_sign.glb) | 97,491 | [provenance](../Assets/Liminal/Art/Meshy/caution_sign/provenance.json) | 완료 |
| 벽시계 | [wall_clock](../Assets/Liminal/Art/Meshy/wall_clock/wall_clock.glb) | 93,017 | [provenance](../Assets/Liminal/Art/Meshy/wall_clock/provenance.json) | 완료 |
| 형광등 기구 | [fluorescent_fixture](../Assets/Liminal/Art/Meshy/fluorescent_fixture/fluorescent_fixture.glb) | 84,964 | [provenance](../Assets/Liminal/Art/Meshy/fluorescent_fixture/provenance.json) | 완료 |
| 푸드코트 테이블 | [foodcourt_table](../Assets/Liminal/Art/Meshy/foodcourt_table/foodcourt_table.glb) | 98,314 | [provenance](../Assets/Liminal/Art/Meshy/foodcourt_table/provenance.json) | 완료 |
| 접이식 가림대 | [folding_barrier](../Assets/Liminal/Art/Meshy/folding_barrier/folding_barrier.glb) | 103,874 | [provenance](../Assets/Liminal/Art/Meshy/folding_barrier/provenance.json) | 완료 |
| 백룸 사무 작업대 | [backrooms_workstation](../Assets/Liminal/Art/Meshy/backrooms_workstation/backrooms_workstation.glb) | 44,513 | [provenance](../Assets/Liminal/Art/Meshy/backrooms_workstation/provenance.json) | 이미지 기반 최종본 확정 |
| 풀룸 타일 아치 | [poolroom_arch](../Assets/Liminal/Art/Meshy/poolroom_arch/poolroom_arch.glb) | 50,314 | [provenance](../Assets/Liminal/Art/Meshy/poolroom_arch/provenance.json) | 개구부 형태 교체·두 발 충돌 검증 |
| 산업용 대형 환풍기 | [industrial_fan](../Assets/Liminal/Art/Meshy/industrial_fan/industrial_fan.glb) | 49,777 | [provenance](../Assets/Liminal/Art/Meshy/industrial_fan/provenance.json) | Unity 보스 공간에서 최종본 확정 |
