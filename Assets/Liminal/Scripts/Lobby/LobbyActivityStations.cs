using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// Small, open-air working places around the association plaza. The tools and silhouettes read from
    /// the game's overhead camera, while the centre aisle, upgrade agent and gate approach stay clear.
    /// Static details are batched by material; only the four status lamps change during play.
    /// </summary>
    public sealed class LobbyActivityStations : MonoBehaviour
    {
        public static readonly Vector3 MedicalPosition = new Vector3(-11.4f, 0, -5.1f);
        public static readonly Vector3 MedicPosition = new Vector3(-12.65f, 0, -5.2f);
        public static readonly Vector3 MedicalGuestPosition = new Vector3(-9.8f, 0, -5.2f);
        public static readonly Vector3 WorkshopPosition = new Vector3(-10.8f, 0, -10.9f);
        public static readonly Vector3 TechnicianPosition = new Vector3(-10.8f, 0, -12.1f);
        public static readonly Vector3 BriefingPosition = new Vector3(9.4f, 0, 5.25f);
        public static readonly Vector3 BriefingHunterPosition = new Vector3(8.15f, 0, 5.3f);
        public static readonly Vector3 RestPosition = new Vector3(9.8f, 0, -9.8f);
        public static readonly Vector3 RestHunterPosition = new Vector3(9.8f, 0, -11.3f);
        public static readonly Vector3 PatientPosition = MedicalGuestPosition;
        public static readonly Vector3 EngineerPosition = TechnicianPosition;
        public static readonly Vector3 VeteranPosition = BriefingHunterPosition;
        public static readonly Vector3 RookiePosition = RestHunterPosition;
        public const float MedicalWorkHeight = .88f;
        public const float WorkshopHeight = .95f;
        public const float BriefingHeight = .85f;
        public const float RestTableHeight = .72f;
        public const float SeatHeight = .46f;

        public readonly struct Station
        {
            public readonly string id;
            public readonly Vector3 position, actorPosition;
            public readonly float actorYaw, workHeight;

            public Station(string id, Vector3 position, Vector3 actorPosition, float actorYaw, float workHeight)
            {
                this.id = id; this.position = position; this.actorPosition = actorPosition;
                this.actorYaw = actorYaw; this.workHeight = workHeight;
            }
        }

        static readonly Station[] Layout =
        {
            new Station("medical", MedicalPosition, MedicPosition, 90, MedicalWorkHeight),
            new Station("workshop", WorkshopPosition, TechnicianPosition, 0, WorkshopHeight),
            new Station("briefing", BriefingPosition, BriefingHunterPosition, 90, BriefingHeight),
            new Station("rest", RestPosition, RestHunterPosition, 0, RestTableHeight)
        };
        public static IReadOnlyList<Station> Stations => Layout;

        struct Pulse { public Material material; public Color color; public float phase; }
        readonly List<Pulse> pulses = new List<Pulse>(4);
        List<Object> owned;
        Material navy, white, steel, rubber, teal, orange, wine, paper, wood, leaf, coffee, gold;
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

        public static LobbyActivityStations Build(Transform parent, List<Object> owned)
        {
            var root = new GameObject("LobbyActivityStations");
            root.transform.SetParent(parent, false);
            var stations = root.AddComponent<LobbyActivityStations>();
            stations.owned = owned;
            stations.Construct();
            return stations;
        }

        void Construct()
        {
            navy = Material("Association navy", new Color(.075f, .12f, .24f), .34f);
            white = Material("Warm white shell", new Color(.89f, .92f, .92f), .42f);
            steel = Material("Brushed steel", new Color(.36f, .42f, .48f), .68f, .65f);
            rubber = Material("Rubber and shadow", new Color(.055f, .065f, .09f), .22f);
            teal = Material("Medical turquoise", new Color(.13f, .60f, .57f), .38f);
            orange = Material("Workshop orange", new Color(.94f, .40f, .13f), .40f);
            wine = Material("Rest burgundy", new Color(.47f, .16f, .24f), .30f);
            paper = Material("Printed ivory", new Color(.97f, .93f, .80f), .10f);
            wood = Material("Warm oak", new Color(.56f, .37f, .20f), .26f);
            leaf = Material("Planter leaves", new Color(.20f, .44f, .27f), .25f);
            coffee = Material("Coffee", new Color(.13f, .07f, .04f), .8f);
            gold = Material("Association brass", new Color(.81f, .66f, .36f), .6f, .55f);
            Medical(); Workshop(); Briefing(); Rest();
        }

        void Medical()
        {
            var b = Begin("Medical aid", MedicalPosition);
            FloorCorners(b, new Vector2(2.65f, 3.05f), teal);
            Table(b, new Vector2(1.16f, 2.05f), MedicalWorkHeight, white, teal);
            // A recessed instrument tray, rolled gauze and a disinfectant pump.
            Box(b, new Vector3(0, .895f, -.30f), new Vector3(.84f, .035f, .66f), steel);
            Box(b, new Vector3(0, .92f, -.30f), new Vector3(.73f, .022f, .55f), navy);
            for (int i = 0; i < 3; i++)
            {
                var a = new Vector3(-.22f + i * .21f, 1.00f, -.35f);
                Tube(b, a + Vector3.back * .12f, a + Vector3.forward * .12f, .085f, paper, 12);
                Tube(b, a + Vector3.back * .125f, a + Vector3.back * .133f, .025f, rubber, 10);
            }
            Cylinder(b, new Vector3(.30f, 1.06f, .15f), .09f, .28f, teal);
            Cylinder(b, new Vector3(.30f, 1.225f, .15f), .035f, .065f, white);
            Box(b, new Vector3(.27f, 1.26f, .15f), new Vector3(.15f, .03f, .055f), white);
            // White first-aid case: rounded handle, corner guards, clasp and a turquoise medical cross.
            Box(b, new Vector3(-.02f, 1.07f, .60f), new Vector3(.79f, .35f, .51f), white);
            Box(b, new Vector3(-.02f, 1.22f, .60f), new Vector3(.79f, .035f, .51f), teal);
            Cross(b, new Vector3(-.02f, 1.245f, .60f), .28f, white);
            for (int s = -1; s <= 1; s += 2)
                Box(b, new Vector3(s * .28f, 1.08f, .333f), new Vector3(.09f, .10f, .028f), steel);
            Tube(b, new Vector3(-.18f, 1.24f, .79f), new Vector3(-.18f, 1.34f, .79f), .023f, rubber);
            Tube(b, new Vector3(.14f, 1.24f, .79f), new Vector3(.14f, 1.34f, .79f), .023f, rubber);
            Tube(b, new Vector3(-.18f, 1.34f, .79f), new Vector3(.14f, 1.34f, .79f), .023f, rubber);
            // The low sign stays below head height and faces upwards enough for the game camera.
            Sign(b, new Vector3(0, .93f, 1.32f), teal, 0);
            Cross(b, new Vector3(0, .945f, 1.32f), .34f, white);
            Clipboard(b, new Vector3(-.15f, .93f, -.78f), -9, teal);
            StatusLamp(b, new Vector3(.43f, 1.07f, .94f), new Color(.14f, .8f, .63f), .8f);
            End(b, new Vector3(0, MedicalWorkHeight * .5f, 0), new Vector3(1.16f, MedicalWorkHeight, 2.05f));
            AddCollider(b.root, "Medical sign base", new Vector3(0, .40f, 1.32f), new Vector3(.46f, .80f, .32f));
        }

        void Workshop()
        {
            var b = Begin("Equipment maintenance", WorkshopPosition);
            FloorCorners(b, new Vector2(3.6f, 2.8f), orange);
            Table(b, new Vector2(2.45f, 1.12f), WorkshopHeight, wood, navy);
            Box(b, new Vector3(0, .97f, 0), new Vector3(1.98f, .025f, .84f), rubber);
            // Maintenance weapon lies on the padded bench, rather than being hidden under a canopy.
            var sword = new Vector3(-.12f, 1.005f, -.03f);
            Box(b, sword, new Vector3(.16f, .045f, .78f), steel, 63);
            Box(b, sword + new Vector3(.39f, 0, .20f), new Vector3(.17f, .052f, .28f), navy, 63);
            Box(b, sword + new Vector3(.26f, .015f, .13f), new Vector3(.41f, .055f, .05f), gold, 63);
            for (int i = 0; i < 4; i++)
                Box(b, sword + new Vector3(.32f + i * .046f, .031f, .164f + i * .023f), new Vector3(.18f, .008f, .013f), wood, 63);
            // Tool rolls, wrench jaws and a handled service drawer.
            Box(b, new Vector3(-.81f, 1.005f, -.05f), new Vector3(.36f, .08f, .67f), orange);
            for (int i = 0; i < 3; i++)
            {
                float z = -.26f + i * .20f;
                Tube(b, new Vector3(-.96f, 1.066f, z), new Vector3(-.66f, 1.066f, z), .024f, steel, 8);
                Box(b, new Vector3(-.96f, 1.066f, z), new Vector3(.08f, .07f, .08f), navy);
                Box(b, new Vector3(-.63f, 1.066f, z - .04f), new Vector3(.11f, .04f, .027f), steel);
                Box(b, new Vector3(-.63f, 1.066f, z + .04f), new Vector3(.11f, .04f, .027f), steel);
            }
            Box(b, new Vector3(.78f, .52f, .10f), new Vector3(.69f, .56f, .73f), navy);
            for (int i = 0; i < 3; i++)
            {
                float y = .34f + i * .17f;
                Box(b, new Vector3(.78f, y, -.282f), new Vector3(.58f, .135f, .04f), steel);
                Tube(b, new Vector3(.65f, y, -.326f), new Vector3(.91f, y, -.326f), .019f, rubber);
            }
            // Inspection lamp and compact cell charger. No flickering light or particles in the player's path.
            Cylinder(b, new Vector3(.85f, .99f, .28f), .14f, .05f, steel);
            Tube(b, new Vector3(.85f, 1.02f, .28f), new Vector3(.85f, 1.48f, .38f), .025f, steel);
            Tube(b, new Vector3(.85f, 1.48f, .38f), new Vector3(.55f, 1.55f, .16f), .025f, steel);
            Box(b, new Vector3(.55f, 1.54f, .16f), new Vector3(.35f, .065f, .18f), navy);
            Box(b, new Vector3(.55f, 1.50f, .16f), new Vector3(.29f, .015f, .13f), paper);
            Box(b, new Vector3(.89f, 1.04f, -.25f), new Vector3(.43f, .14f, .29f), white);
            for (int i = 0; i < 3; i++) Cylinder(b, new Vector3(.77f + i * .12f, 1.145f, -.25f), .035f, .12f, teal);
            StatusLamp(b, new Vector3(1.10f, 1.02f, .35f), new Color(1, .51f, .12f), 2.4f);
            End(b, new Vector3(0, WorkshopHeight * .5f, 0), new Vector3(2.45f, WorkshopHeight, 1.12f));
        }

        void Briefing()
        {
            var b = Begin("Hunter mission briefing", BriefingPosition);
            FloorCorners(b, new Vector2(2.7f, 3.15f), teal);
            Table(b, new Vector2(1.24f, 1.96f), BriefingHeight, navy, white);
            Box(b, new Vector3(0, .88f, .03f), new Vector3(1.09f, .045f, 1.74f), steel);
            Box(b, new Vector3(0, .91f, .03f), new Vector3(.97f, .035f, 1.62f), navy);
            // A tiny, legible mission topography: contour islands, a route, three destinations and a gate ring.
            for (int i = 0; i < 3; i++)
            {
                float h = .944f + i * .013f;
                Box(b, new Vector3(-.19f + i * .018f, h, -.33f), new Vector3(.32f - i * .065f, .012f, .49f - i * .075f), teal, -18);
                Box(b, new Vector3(.19f - i * .012f, h, .41f), new Vector3(.27f - i * .058f, .012f, .37f - i * .056f), leaf, 24);
            }
            Vector3[] route = { new Vector3(-.31f, .962f, -.61f), new Vector3(.24f, .962f, -.24f), new Vector3(-.16f, .962f, .12f), new Vector3(.18f, .962f, .60f) };
            for (int i = 1; i < route.Length; i++) Tube(b, route[i - 1], route[i], .013f, gold, 6);
            for (int i = 0; i < route.Length; i++)
            {
                Cylinder(b, route[i], .045f, .022f, white, 12);
                Cylinder(b, route[i] + Vector3.up * .016f, .022f, .018f, orange, 10);
            }
            Ring(b, new Vector3(.35f, .953f, -.61f), .075f, .014f, teal);
            Clipboard(b, new Vector3(.78f, .41f, .44f), 0, navy);
            // A slim side pocket contains two visible route sheets.
            Box(b, new Vector3(.68f, .43f, .39f), new Vector3(.12f, .31f, .48f), navy);
            Box(b, new Vector3(.69f, .62f, .39f), new Vector3(.018f, .14f, .39f), paper);
            Box(b, new Vector3(.73f, .61f, .39f), new Vector3(.018f, .16f, .35f), teal);
            StatusLamp(b, new Vector3(.47f, 1.025f, .82f), new Color(.2f, .77f, 1), 4.1f);
            End(b, new Vector3(0, BriefingHeight * .5f, 0), new Vector3(1.24f, BriefingHeight, 1.96f));
        }

        void Rest()
        {
            var b = Begin("Rookie rest and preparation", RestPosition);
            FloorCorners(b, new Vector2(3.5f, 3.2f), wine);
            Cylinder(b, new Vector3(0, RestTableHeight - .045f, 0), .62f, .09f, wood, 24);
            Cylinder(b, new Vector3(0, .34f, 0), .065f, .62f, steel);
            Cylinder(b, new Vector3(0, .06f, 0), .38f, .08f, navy, 20);
            Cup(b, new Vector3(-.26f, RestTableHeight, -.15f), wine, 35);
            Cup(b, new Vector3(.29f, RestTableHeight, .20f), white, 180);
            Clipboard(b, new Vector3(.07f, RestTableHeight + .015f, -.04f), -22, teal);
            // Folded field jacket, compact rucksack and a small herb pot make this a waiting place.
            Box(b, new Vector3(.21f, .772f, -.33f), new Vector3(.35f, .065f, .24f), wine, -15);
            Box(b, new Vector3(.21f, .81f, -.33f), new Vector3(.14f, .012f, .23f), navy, -15);
            Plant(b, new Vector3(-.24f, RestTableHeight, .28f));
            Chair(b, new Vector3(-.95f, 0, 0), -90);
            Chair(b, new Vector3(.95f, 0, 0), 90);
            Box(b, new Vector3(-1.36f, .21f, .24f), new Vector3(.30f, .41f, .36f), navy, -12);
            Box(b, new Vector3(-1.36f, .25f, .043f), new Vector3(.25f, .19f, .07f), wine, -12);
            Tube(b, new Vector3(-1.46f, .43f, .24f), new Vector3(-1.26f, .43f, .24f), .024f, steel);
            StatusLamp(b, new Vector3(1.45f, .19f, 1.12f), new Color(1, .69f, .33f), 5.4f);
            End(b, new Vector3(0, RestTableHeight * .5f, 0), new Vector3(1.24f, RestTableHeight, 1.24f));
            AddCollider(b.root, "Left chair", new Vector3(-.95f, .28f, 0), new Vector3(.47f, .56f, .53f));
            AddCollider(b.root, "Right chair", new Vector3(.95f, .28f, 0), new Vector3(.47f, .56f, .53f));
        }

        void Update()
        {
            float time = Time.time;
            for (int i = 0; i < pulses.Count; i++)
            {
                var p = pulses[i];
                p.material.SetColor(EmissionColor, p.color * (1.1f + .13f * Mathf.Sin(time * 1.4f + p.phase)));
            }
        }

        Material Material(string label, Color color, float smoothness, float metallic = 0)
        {
            var material = LiminalMonsterKit.Lit(color, smoothness, metallic);
            material.name = "Lobby stations / " + label;
            owned.Add(material);
            return material;
        }

        Batch Begin(string name, Vector3 position)
        {
            var root = new GameObject(name).transform;
            root.SetParent(transform, false); root.localPosition = position;
            return new Batch(root);
        }

        void End(Batch b, Vector3 colliderCentre, Vector3 colliderSize)
        {
            foreach (var pair in b.geometry)
            {
                var mesh = new Mesh { name = b.root.name + " / " + pair.Key.name };
                var g = pair.Value;
                if (g.vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(g.vertices); mesh.SetNormals(g.normals); mesh.SetTriangles(g.triangles, 0);
                mesh.RecalculateBounds();
                owned.Add(mesh);
                var go = new GameObject(pair.Key.name);
                go.transform.SetParent(b.root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = pair.Key;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;
            }
            AddCollider(b.root, "Furniture footprint", colliderCentre, colliderSize);
        }

        static void AddCollider(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false);
            var collider = go.AddComponent<BoxCollider>(); collider.center = position; collider.size = size;
        }

        void Table(Batch b, Vector2 size, float top, Material surface, Material trim)
        {
            Box(b, new Vector3(0, top - .055f, 0), new Vector3(size.x, .11f, size.y), surface);
            Box(b, new Vector3(0, top - .13f, 0), new Vector3(size.x * .94f, .055f, size.y * .94f), trim);
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    var foot = new Vector3(x * (size.x * .5f - .14f), .08f, z * (size.y * .5f - .15f));
                    Tube(b, foot, foot + Vector3.up * (top - .23f), .043f, steel);
                    Cylinder(b, foot - Vector3.up * .015f, .066f, .065f, rubber);
                }
            for (int z = -1; z <= 1; z += 2)
                Tube(b, new Vector3(-size.x * .38f, .30f, z * size.y * .36f), new Vector3(size.x * .38f, .30f, z * size.y * .36f), .024f, steel);
        }

        void FloorCorners(Batch b, Vector2 size, Material accent)
        {
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Box(b, new Vector3(x * (size.x * .5f - .19f), .012f, z * size.y * .5f), new Vector3(.44f, .012f, .065f), accent);
                    Box(b, new Vector3(x * size.x * .5f, .012f, z * (size.y * .5f - .19f)), new Vector3(.065f, .012f, .44f), accent);
                }
        }

        void Cross(Batch b, Vector3 position, float size, Material material)
        {
            Box(b, position, new Vector3(size, .013f, size * .32f), material);
            Box(b, position, new Vector3(size * .32f, .015f, size), material);
        }

        void Sign(Batch b, Vector3 position, Material color, float yaw)
        {
            Cylinder(b, new Vector3(position.x, .43f, position.z), .032f, .80f, steel);
            Box(b, position, new Vector3(.58f, .04f, .49f), color, yaw);
            Box(b, new Vector3(position.x, .035f, position.z), new Vector3(.46f, .055f, .32f), navy);
        }

        void Clipboard(Batch b, Vector3 centre, float yaw, Material cover)
        {
            Box(b, centre, new Vector3(.27f, .017f, .37f), cover, yaw);
            Box(b, centre + Vector3.up * .012f, new Vector3(.225f, .008f, .30f), paper, yaw);
            var rotation = Quaternion.Euler(0, yaw, 0);
            Box(b, centre + rotation * new Vector3(0, .024f, .15f), new Vector3(.12f, .012f, .04f), steel, yaw);
            for (int i = 0; i < 3; i++)
                Box(b, centre + rotation * new Vector3(0, .020f, .055f - i * .055f), new Vector3(.16f - i * .023f, .003f, .009f), navy, yaw);
        }

        void Cup(Batch b, Vector3 basePosition, Material shell, float yaw)
        {
            Cylinder(b, basePosition + Vector3.up * .027f, .125f, .02f, shell, 16);
            Cylinder(b, basePosition + Vector3.up * .115f, .079f, .17f, shell, 16);
            Cylinder(b, basePosition + Vector3.up * .203f, .065f, .007f, coffee, 16);
            var offset = Quaternion.Euler(0, yaw, 0) * Vector3.right * .095f;
            var a = basePosition + offset + Vector3.up * .08f;
            var c = basePosition + offset + Vector3.up * .17f;
            Tube(b, a, a + offset * .55f, .018f, shell);
            Tube(b, c, c + offset * .55f, .018f, shell);
            Tube(b, a + offset * .55f, c + offset * .55f, .018f, shell);
        }

        void Plant(Batch b, Vector3 basePosition)
        {
            Cylinder(b, basePosition + Vector3.up * .075f, .10f, .15f, white, 12);
            Cylinder(b, basePosition + Vector3.up * .153f, .084f, .01f, wood, 12);
            for (int i = 0; i < 7; i++)
            {
                float angle = i * Mathf.PI * 2 / 7;
                var end = basePosition + new Vector3(Mathf.Sin(angle) * .12f, .25f + (i % 3) * .035f, Mathf.Cos(angle) * .12f);
                Tube(b, basePosition + Vector3.up * .15f, end, .015f, leaf, 6);
                Box(b, end, new Vector3(.04f, .025f, .14f), leaf, -angle * Mathf.Rad2Deg);
            }
        }

        void Chair(Batch b, Vector3 centre, float yaw)
        {
            var q = Quaternion.Euler(0, yaw, 0);
            Box(b, centre + Vector3.up * (SeatHeight - .04f), new Vector3(.48f, .08f, .46f), wine, yaw);
            Box(b, centre + q * new Vector3(0, .70f, .235f), new Vector3(.48f, .35f, .045f), wine, yaw);
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    Tube(b, centre + q * new Vector3(x * .19f, .045f, z * .19f), centre + q * new Vector3(x * .19f, z > 0 ? .87f : .44f, z * .19f), .022f, steel);
        }

        void StatusLamp(Batch b, Vector3 centre, Color color, float phase)
        {
            Cylinder(b, centre - Vector3.up * .055f, .058f, .11f, navy);
            var material = Material(b.root.name + " status", color, .50f);
            material.EnableKeyword("_EMISSION"); material.SetColor(EmissionColor, color * 1.2f);
            Cylinder(b, centre + Vector3.up * .027f, .043f, .075f, material, 12);
            pulses.Add(new Pulse { material = material, color = color, phase = phase });
        }

        void Ring(Batch b, Vector3 centre, float radius, float thickness, Material material)
        {
            const int count = 20;
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2 / count, c = (i + 1) * Mathf.PI * 2 / count;
                Tube(b, centre + new Vector3(Mathf.Sin(a) * radius, 0, Mathf.Cos(a) * radius),
                    centre + new Vector3(Mathf.Sin(c) * radius, 0, Mathf.Cos(c) * radius), thickness, material, 6);
            }
        }

        sealed class Batch
        {
            public readonly Transform root;
            public readonly Dictionary<Material, Geometry> geometry = new Dictionary<Material, Geometry>();
            public Batch(Transform root) { this.root = root; }
            public Geometry For(Material material)
            {
                if (!geometry.TryGetValue(material, out var result)) geometry.Add(material, result = new Geometry());
                return result;
            }
        }

        sealed class Geometry
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public readonly List<int> triangles = new List<int>();
            public void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
            {
                int index = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                normals.Add(normal); normals.Add(normal); normals.Add(normal);
                triangles.Add(index); triangles.Add(index + 1); triangles.Add(index + 2);
            }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            { Triangle(a, b, c, normal); Triangle(a, c, d, normal); }
        }

        static void Box(Batch b, Vector3 centre, Vector3 size, Material material, float yaw = 0)
        {
            var g = b.For(material); var rotation = Quaternion.Euler(0, yaw, 0); var h = size * .5f;
            var x = rotation * Vector3.right; var y = Vector3.up; var z = rotation * Vector3.forward;
            Face(g, centre + x * h.x, z * h.z, y * h.y, x);
            Face(g, centre - x * h.x, -z * h.z, y * h.y, -x);
            Face(g, centre + y * h.y, x * h.x, z * h.z, y);
            Face(g, centre - y * h.y, x * h.x, -z * h.z, -y);
            Face(g, centre + z * h.z, -x * h.x, y * h.y, z);
            Face(g, centre - z * h.z, x * h.x, y * h.y, -z);
        }

        static void Face(Geometry geometry, Vector3 centre, Vector3 horizontal, Vector3 vertical, Vector3 normal)
        { geometry.Quad(centre - horizontal - vertical, centre - horizontal + vertical, centre + horizontal + vertical, centre + horizontal - vertical, normal); }

        static void Cylinder(Batch b, Vector3 centre, float radius, float height, Material material, int sides = 12)
        { Tube(b, centre - Vector3.up * height * .5f, centre + Vector3.up * height * .5f, radius, material, sides); }

        static void Tube(Batch b, Vector3 start, Vector3 end, float radius, Material material, int sides = 10)
        {
            var g = b.For(material); var axis = (end - start).normalized;
            var rotation = Quaternion.FromToRotation(Vector3.up, axis);
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, c = (i + 1) * Mathf.PI * 2 / sides;
                var av = rotation * new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                var cv = rotation * new Vector3(Mathf.Sin(c), 0, Mathf.Cos(c));
                var p0 = start + av * radius; var p1 = start + cv * radius;
                var p2 = end + cv * radius; var p3 = end + av * radius;
                g.Quad(p0, p1, p2, p3, (av + cv).normalized);
                g.Triangle(start, p1, p0, -axis);
                g.Triangle(end, p3, p2, axis);
            }
        }
    }
}
