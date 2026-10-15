using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CityGen.EditorTools
{
    // Step 2: turns each blockout cube into a detailed 1930s building (one renderer, one mesh asset).
    // Every building draws its own facade "recipe" from its seed — window rhythm, window proportions, blank
    // party walls, roof type and colours — so neighbouring buildings never repeat the same dense grid.
    // Per-building inputs come from CityBuildingInfo (style, floors, bays, seed): edit them in the Inspector and
    // use CityGen → Rebuild Selected Buildings. Sizes follow CityGenSettings.heightScale / footprintScale.
    internal static class CityBuildings
    {
        // Material slots inside a building mesh.
        const int Wall = 0, Trim = 1, Glass = 2, GlassLit = 3, Storefront = 4, Awning = 5, Roof = 6, Iron = 7, Wood = 8, Curtain = 9, SlotCount = 10;

        // One restrained New York palette (1920s–30s): red-brown brick and brownstone row houses, buff brick and
        // limestone apartments and Art Deco towers, light limestone trim. Saturation is kept low so the city reads
        // as one place; colour accents come from signs, taxis and awnings only. Each wall colour appears in more
        // than one style so neighbouring blocks share a family of tones.
        static readonly Color[] AwningColors =
        {
            new Color(.16f, .30f, .23f), new Color(.42f, .14f, .13f), new Color(.15f, .19f, .30f), new Color(.30f, .22f, .16f)
        };

        static readonly (string name, Color color)[] RowHouseWalls =
        {
            ("City Brick", new Color(.55f, .28f, .21f)), ("City Brick", new Color(.55f, .28f, .21f)),
            ("City Brick Dark", new Color(.43f, .24f, .19f)), ("City Brownstone", new Color(.43f, .31f, .25f)),
            ("City Brownstone", new Color(.43f, .31f, .25f)), ("City Buff Brick", new Color(.74f, .63f, .48f)),
        };
        static readonly (string name, Color color)[] ApartmentWalls =
        {
            ("City Limestone", new Color(.80f, .76f, .67f)), ("City Buff Brick", new Color(.74f, .63f, .48f)),
            ("City Brick", new Color(.55f, .28f, .21f)), ("City Gray Stone", new Color(.57f, .56f, .53f)),
        };
        static readonly (string name, Color color)[] TowerWalls =
        {
            ("City Buff Brick", new Color(.74f, .63f, .48f)), ("City Limestone", new Color(.80f, .76f, .67f)),
            ("City Gray Stone", new Color(.57f, .56f, .53f)), ("City Brick Dark", new Color(.43f, .24f, .19f)),
        };
        static readonly (string name, Color color)[] Trims =
        {
            ("City Trim", new Color(.84f, .80f, .71f)), ("City Trim Stone", new Color(.74f, .70f, .62f)),
        };

        // Window rhythms across a facade (1 = window bay, 0 = blank wall, 2 = a pair of narrow windows).
        static readonly int[][] Rhythms =
        {
            new[] { 1 },          // every bay, but with wide bays
            new[] { 1, 0 },       // alternate window / wall
            new[] { 2, 0 },       // paired windows with wall between
            new[] { 0, 1, 1, 0 }, // solid ends, windows in the middle
            new[] { 1, 0, 0 },    // sparse
            new[] { 2 },          // pairs everywhere (calm, narrow windows)
        };

        sealed class Recipe
        {
            public int[] rhythm;
            public float bayScale, windowWidth, windowHeight;
            public float curtainRatio;
            public bool blankSides;      // row houses: party walls without windows
            public int roof;             // 0 flat parapet, 1 stepped gable, 2 pitched gable, 3 mansard
            public int towerTop;         // 0 spire, 1 stepped crown, 2 flat with banding
            public Material wall, trim;
        }

        public static void Build(CityBuildingInfo info, CityGenSettings settings)
        {
            var t = info.transform;
            var filter = t.GetComponent<MeshFilter>();
            var renderer = t.GetComponent<MeshRenderer>();
            float hs = settings.heightScale;
            float G = 4f * hs, F = 3.2f * hs;
            int floors = Mathf.Max(1, info.floors);
            float W = info.footprint.x, D = info.footprint.y;
            float H = G + (floors - 1) * F;
            var p = t.position;
            t.position = new Vector3(p.x, 0, p.z);
            t.rotation = Quaternion.identity;
            t.localScale = Vector3.one;

            var rnd = new System.Random(info.seed);
            var recipe = NewRecipe(info, rnd);
            var kit = new MeshKit(SlotCount);
            var f = new Facades(kit, rnd, settings, G, F, info, t.position, recipe);
            if (info.style == CityBuildingInfo.Style.Tower) BuildTower(f, W, D, floors, H);
            else BuildLowRise(f, W, D, floors, H);

            var mesh = kit.ToMesh(t.name, out int[] used);
            Directory.CreateDirectory(CityBlockout.MeshFolder + "/Buildings");
            filter.sharedMesh = CityBlockout.SaveMesh("Buildings/" + t.name.Split(' ')[0], mesh);
            var awning = Mat("City Awning " + f.awningIndex, AwningColors[f.awningIndex]);
            var materials = new Material[used.Length];
            for (int i = 0; i < used.Length; i++)
                materials[i] = used[i] switch
                {
                    Wall => recipe.wall,
                    Trim => recipe.trim,
                    Glass => Mat("City Glass", new Color(.14f, .19f, .27f), .8f),
                    GlassLit => LitGlass(),
                    Storefront => Mat("City Storefront", new Color(.13f, .14f, .16f)),
                    Awning => awning,
                    Roof => Mat("City Roof", new Color(.24f, .24f, .26f)),
                    Iron => IronMaterial(),
                    Curtain => Mat("City Glass Curtain", new Color(.84f, .76f, .60f), .3f),
                    _ => Mat("City Wood", new Color(.46f, .31f, .18f)),
                };
            renderer.sharedMaterials = materials;
            if (f.flagAnchor.HasValue) CityProps.AddFlag(t, f.flagAnchor.Value, f.flagNormal, rnd, settings);
        }

        static Recipe NewRecipe(CityBuildingInfo info, System.Random rnd)
        {
            var r = new Recipe();
            bool tower = info.style == CityBuildingInfo.Style.Tower;
            r.rhythm = tower ? Rhythms[rnd.Next(2) == 0 ? 0 : 5] : Rhythms[rnd.Next(Rhythms.Length)];
            r.bayScale = tower ? Range(rnd, 1.1f, 1.5f) : Range(rnd, 1.0f, 1.6f);
            r.windowWidth = Range(rnd, .32f, .48f);   // of the bay pitch
            r.windowHeight = Range(rnd, .45f, .62f);  // of the storey height
            r.curtainRatio = Range(rnd, .05f, .25f);
            r.blankSides = info.style == CityBuildingInfo.Style.RowHouse && rnd.NextDouble() < .8;
            r.roof = info.style == CityBuildingInfo.Style.RowHouse ? Pick(rnd, 0, 0, 1, 1, 2)
                : info.style == CityBuildingInfo.Style.Apartment ? Pick(rnd, 0, 0, 3) : 0;
            r.towerTop = rnd.Next(3);
            var palette = info.style == CityBuildingInfo.Style.RowHouse ? RowHouseWalls
                : info.style == CityBuildingInfo.Style.Apartment ? ApartmentWalls : TowerWalls;
            var (wallName, wallColor) = palette[rnd.Next(palette.Length)];
            r.wall = Mat(wallName, wallColor);
            // Dark trim only on light walls; light trim everywhere else.
            // Light limestone trim on brick and brownstone; a slightly darker stone trim on light walls.
            var (trimName, trimColor) = wallColor.grayscale > .6f ? Trims[1] : Trims[0];
            r.trim = Mat(trimName, trimColor);
            return r;
        }

        // ---------------- Low rise: brick row houses and stone apartments ----------------

        static void BuildLowRise(Facades f, float W, float D, int floors, float H)
        {
            bool apartment = f.info.style == CityBuildingInfo.Style.Apartment;
            f.Body(Vector3.zero, W, D, H);
            foreach (var face in f.Faces(Vector3.zero, W, D))
            {
                bool front = face.normal == f.front;
                bool back = face.normal == -f.front;
                bool side = !front && !back;
                if (side && f.recipe.blankSides) { f.Band(face, f.G, .05f, .03f); f.Cornice(face, H, .08f, .04f); continue; } // party wall
                // Ground floor: shop front or door on the street side; plain windows elsewhere.
                if (front && f.rnd.NextDouble() < (apartment ? .75 : .5)) f.ShopFront(face);
                else if (front || f.rnd.NextDouble() < .5) f.WindowRow(face, 0, front, front, sparse: !front);
                for (int floor = 1; floor < floors; floor++)
                    f.WindowRow(face, floor, face.visible, false, sparse: !front, attic: apartment && floor == floors - 1 && floors >= 5);
                f.Band(face, f.G, .05f, .03f);
                if (apartment && floors >= 5) f.Band(face, f.G + 2 * f.F, .04f, .025f);
                if (f.recipe.roof != 3) f.Cornice(face, H, apartment ? .16f : .1f, apartment ? .1f : .06f);
                if (front && floors >= 3 && f.rnd.NextDouble() < f.settings.fireEscapeChance) f.FireEscape(face, floors);
                if (front && apartment && f.rnd.NextDouble() < f.settings.flagChance)
                {
                    f.flagAnchor = face.center + face.normal * .02f + Vector3.up * (f.G + f.F * 1.4f) + face.right * (face.width * .25f);
                    f.flagNormal = face.normal;
                }
            }
            switch (f.recipe.roof)
            {
                case 1: f.Parapet(Vector3.zero, W, D, H); f.SteppedGable(W, D, H); break;
                case 2: f.PitchedRoof(W, D, H); break;
                case 3: f.Mansard(W, D, H); break;
                default: f.Parapet(Vector3.zero, W, D, H); break;
            }
            if (f.recipe.roof != 2 && f.rnd.NextDouble() < f.settings.waterTowerChance) f.WaterTower(H, W, D);
        }

        // ---------------- Art Deco tower: setbacks, piers, crown ----------------

        static void BuildTower(Facades f, float W, float D, int floors, float H)
        {
            int f0 = Mathf.Max(3, Mathf.RoundToInt(floors * .62f)), f1 = Mathf.Max(f0 + 1, Mathf.RoundToInt(floors * .85f));
            float m = Mathf.Min(W, D);
            var tiers = new List<(int from, int to, float inset)> { (0, f0, 0), (f0, f1, .13f * m), (f1, floors, .26f * m) };
            bool banded = f.recipe.towerTop == 2;
            foreach (var (from, to, inset) in tiers)
            {
                if (to <= from) continue;
                float w = W - 2 * inset, d = D - 2 * inset;
                if (w < .8f || d < .8f) break;
                float y0 = f.FloorBase(from), y1 = f.FloorBase(to);
                f.Body(new Vector3(0, y0, 0), w, d, y1 - y0);
                foreach (var face in f.Faces(new Vector3(0, y0, 0), w, d))
                {
                    for (int floor = from; floor < to; floor++)
                    {
                        if (floor == 0 && face.normal == f.front) { f.Lobby(face); continue; }
                        if (banded && floor % 4 == 3) { f.Band(face, f.FloorBase(floor) + f.F * .5f - y0, .07f, .03f); continue; } // streamline bands
                        f.WindowRow(face, floor, false, false, sparse: false, tierBase: y0);
                    }
                    if (!banded) f.Piers(face, y0, y1 - y0);
                    f.Cornice(face, y1 - y0, .12f, .05f);
                }
            }
            float top = f.FloorBase(floors), cw = Mathf.Max(.5f, (W - .52f * m) * .55f);
            if (f.recipe.towerTop == 0 && floors >= f.settings.spireFromFloors)
            {
                f.kit.Box(Trim, new Vector3(0, top + .2f, 0), new Vector3(cw, .4f, cw));
                f.kit.Box(Trim, new Vector3(0, top + .55f, 0), new Vector3(cw * .65f, .3f, cw * .65f));
                f.kit.Cone(Trim, new Vector3(0, top + .7f, 0), cw * .4f, H * .12f, 4, 45);
                f.kit.Prism(Iron, new Vector3(0, top + .7f + H * .1f, 0), .025f, H * .08f, 6);
            }
            else if (f.recipe.towerTop == 1)
            {
                for (int step = 0; step < 4; step++)
                    f.kit.Box(Trim, new Vector3(0, top + .18f + step * .3f, 0), new Vector3(cw * (1 - step * .2f), .3f, cw * (1 - step * .2f)));
            }
        }

        // ---------------- Facade helpers ----------------

        internal struct Face
        {
            public Vector3 center;   // on the wall surface at the face's base height
            public Vector3 normal, right;
            public float width;
            public bool visible;     // faces the course: sills, lintels and fire escapes
        }

        sealed class Facades
        {
            public readonly MeshKit kit;
            public readonly System.Random rnd;
            public readonly CityGenSettings settings;
            public readonly float G, F, pitch;
            public readonly CityBuildingInfo info;
            public readonly Recipe recipe;
            public readonly Vector3 front, worldPosition;
            public readonly int awningIndex;
            public Vector3? flagAnchor;
            public Vector3 flagNormal;

            public Facades(MeshKit kit, System.Random rnd, CityGenSettings settings, float g, float f, CityBuildingInfo info, Vector3 world, Recipe recipe)
            {
                this.kit = kit; this.rnd = rnd; this.settings = settings; G = g; F = f; this.info = info; worldPosition = world; this.recipe = recipe;
                front = info.frontDirection.sqrMagnitude > .1f ? info.frontDirection.normalized : Vector3.forward;
                awningIndex = rnd.Next(AwningColors.Length);
                // Bay pitch: the building's bay count across its front (CityBuildingInfo.bays), never narrower than
                // the settings' bay width × this building's own spacing.
                float frontWidth = Mathf.Abs(front.x) > .5f ? info.footprint.y : info.footprint.x;
                float minimum = Mathf.Max(.6f, settings.bayWidth * settings.footprintScale * recipe.bayScale);
                int bays = Mathf.Clamp(info.bays > 0 ? info.bays : Mathf.RoundToInt(frontWidth / minimum), 1, Mathf.Max(1, Mathf.FloorToInt(frontWidth / minimum)));
                pitch = frontWidth / bays;
            }

            public float FloorBase(int floor) => floor == 0 ? 0 : G + (floor - 1) * F;
            float FloorHeight(int floor) => floor == 0 ? G : F;

            public IEnumerable<Face> Faces(Vector3 origin, float w, float d)
            {
                var toCourse = -new Vector3(worldPosition.x, 0, worldPosition.z).normalized;
                foreach (var (n, width, half) in new[] { (Vector3.right, d, w / 2), (Vector3.left, d, w / 2), (Vector3.forward, w, d / 2), (Vector3.back, w, d / 2) })
                    yield return new Face
                    {
                        center = origin + n * half, normal = n, right = Vector3.Cross(n, Vector3.up), width = width,
                        visible = Vector3.Dot(n, toCourse) > -.2f || n == front
                    };
            }

            public void Body(Vector3 baseCenter, float w, float d, float h)
            {
                foreach (var face in Faces(baseCenter, w, d)) kit.Quad(Wall, face.center + Vector3.up * h / 2, face.normal, Vector3.up, face.width, h);
                kit.Quad(Roof, baseCenter + Vector3.up * h, Vector3.up, Vector3.forward, w, d);
            }

            // One storey of windows following the building's rhythm. `sparse` (backs, sides) keeps one window in three.
            public void WindowRow(Face face, int floor, bool detailed, bool frontGround, bool sparse = false, bool attic = false, float tierBase = 0)
            {
                int bays = Mathf.Max(1, Mathf.RoundToInt(face.width / pitch));
                float bayPitch = face.width / bays, h = FloorHeight(floor), y = FloorBase(floor);
                float wh = h * recipe.windowHeight * (attic ? .7f : 1), cy = y + h * (attic ? .45f : .52f);
                float ww = Mathf.Min(bayPitch * recipe.windowWidth, wh * 1.1f);
                int door = frontGround ? bays / 2 : -1;
                int phase = (floor + (sparse ? 1 : 0)) % 2; // tiny offset keeps sparse backs from lining up in columns
                bool any = false;
                for (int b = 0; b < bays; b++)
                {
                    float x = -face.width / 2 + bayPitch * (b + .5f);
                    if (b == door)
                    {
                        Vector3 dc = face.center + face.right * x + Vector3.up * (h * .38f - tierBase);
                        kit.Quad(Storefront, dc + face.normal * .012f, face.normal, Vector3.up, Mathf.Min(bayPitch * .5f, .5f), h * .72f);
                        kit.Box(Trim, dc + face.normal * .02f + Vector3.up * h * .38f, Rot(face, Mathf.Min(bayPitch * .6f, .6f), .06f, .05f));
                        any = true;
                        continue;
                    }
                    int kind = sparse ? ((b + phase) % 3 == 0 ? 1 : 0) : recipe.rhythm[b % recipe.rhythm.Length];
                    if (kind == 0 && !(b == bays - 1 && !any && bays <= 2)) continue; // keep at least one window on narrow faces
                    Vector3 c = face.center + face.right * x + Vector3.up * (cy - tierBase);
                    if (kind == 2)
                    {
                        float narrow = ww * .55f, gap = ww * .25f;
                        Window(face, c - face.right * (narrow / 2 + gap / 2), narrow, wh, detailed);
                        Window(face, c + face.right * (narrow / 2 + gap / 2), narrow, wh, detailed);
                    }
                    else Window(face, c, ww, wh, detailed);
                    any = true;
                }
            }

            void Window(Face face, Vector3 c, float w, float h, bool detailed)
            {
                double roll = rnd.NextDouble();
                int glass = roll < settings.litWindowRatio ? GlassLit : roll < settings.litWindowRatio + recipe.curtainRatio ? Curtain : Glass;
                kit.Quad(glass, c + face.normal * .01f, face.normal, Vector3.up, w, h);
                if (!detailed) return;
                kit.Box(Trim, c + face.normal * .03f - Vector3.up * (h / 2 + .02f), Rot(face, w + .07f, .04f, .06f));  // sill
                kit.Box(Trim, c + face.normal * .02f + Vector3.up * (h / 2 + .03f), Rot(face, w + .05f, .05f, .04f));  // lintel
            }

            public void ShopFront(Face face)
            {
                float glassW = face.width * Range(rnd, .6f, .82f);
                Vector3 baseC = face.center;
                kit.Quad(GlassLit, baseC + face.normal * .01f + Vector3.up * G * .4f, face.normal, Vector3.up, glassW, G * .56f);
                int panes = Mathf.Max(2, Mathf.RoundToInt(glassW / .7f));
                for (int i = 0; i <= panes; i++)
                    kit.Box(Storefront, baseC + face.normal * .02f + face.right * (-glassW / 2 + glassW * i / panes) + Vector3.up * G * .4f, Rot(face, .03f, G * .56f, .03f));
                kit.Box(Storefront, baseC + face.normal * .02f + Vector3.up * G * .8f, Rot(face, glassW + .1f, G * .16f, .04f));
                if (rnd.NextDouble() < .8)
                {
                    var tilt = Quaternion.LookRotation(face.normal, Vector3.up) * Quaternion.Euler(20, 0, 0);
                    kit.Box(Awning, baseC + face.normal * .2f + Vector3.up * (G * .7f), new Vector3(glassW + .1f, .02f, .4f), tilt, true);
                }
            }

            public void Lobby(Face face)
            {
                float w = Mathf.Min(face.width * .5f, 2.4f);
                kit.Quad(GlassLit, face.center + face.normal * .01f + Vector3.up * G * .45f, face.normal, Vector3.up, w, G * .8f);
                kit.Box(Trim, face.center + face.normal * .18f + Vector3.up * G * .88f, Rot(face, w + .3f, .05f, .36f));
            }

            public void Band(Face face, float y, float height, float depth)
                => kit.Box(Trim, face.center + face.normal * depth / 2 + Vector3.up * y, Rot(face, face.width + depth * 2, height, depth));

            public void Cornice(Face face, float topLocal, float height, float depth)
                => kit.Box(Trim, face.center + face.normal * depth / 2 + Vector3.up * (topLocal - height / 2), Rot(face, face.width + depth * 2, height, depth));

            public void Piers(Face face, float y0, float height)
            {
                int bays = Mathf.Max(1, Mathf.RoundToInt(face.width / pitch));
                for (int b = 0; b <= bays; b++)
                {
                    float x = -face.width / 2 + face.width * b / bays;
                    float w = b == 0 || b == bays ? .09f : .06f;
                    kit.Box(Trim, face.center + face.right * x + face.normal * .02f + Vector3.up * height / 2, Rot(face, w, height, .04f));
                }
            }

            public void Parapet(Vector3 baseCenter, float w, float d, float h)
            {
                foreach (var face in Faces(baseCenter, w, d))
                    kit.Box(Wall, face.center - face.normal * .03f + Vector3.up * (h + .06f), Rot(face, face.width, .12f, .06f));
            }

            // Dutch stepped gable rising from the front parapet.
            public void SteppedGable(float w, float d, float h)
            {
                var face = FrontFace(w, d);
                float step = .16f, width = face.width * .7f;
                for (int i = 0; i < 3; i++, width *= .65f)
                    kit.Box(Wall, face.center - face.normal * .04f + Vector3.up * (h + .12f + step * (i + .5f)), Rot(face, width, step, .08f));
                kit.Box(Trim, face.center - face.normal * .04f + Vector3.up * (h + .12f + step * 3 + .02f), Rot(face, width / .65f + .05f, .04f, .1f));
            }

            // Pitched roof with the ridge parallel to the street, gable ends on the sides.
            public void PitchedRoof(float w, float d, float h)
            {
                var face = FrontFace(w, d);
                float depth = Mathf.Abs(face.normal.x) > .5f ? w : d, along = face.width;
                float rise = Mathf.Min(depth * .35f, .9f);
                Vector3 n = face.normal, r = face.right, top = Vector3.up * (h + rise);
                Vector3 eaveF = n * depth / 2 + Vector3.up * h, eaveB = -n * depth / 2 + Vector3.up * h;
                Vector3 slopeF = (n * rise + Vector3.up * depth / 2).normalized, slopeB = (-n * rise + Vector3.up * depth / 2).normalized;
                // Corner order chosen so both slopes face up and outwards (verified numerically).
                kit.AddQuad(Roof, eaveF - r * along / 2, eaveF + r * along / 2, top + r * along / 2, top - r * along / 2, slopeF);
                kit.AddQuad(Roof, eaveB + r * along / 2, eaveB - r * along / 2, top - r * along / 2, top + r * along / 2, slopeB);
                kit.Triangle(Wall, eaveF + r * along / 2, eaveB + r * along / 2, top + r * along / 2, r);
                kit.Triangle(Wall, eaveF - r * along / 2, eaveB - r * along / 2, top - r * along / 2, -r);
            }

            // Mansard top storey: sloped roof faces with small dormers on the street side.
            public void Mansard(float w, float d, float h)
            {
                float rise = F * .85f, inset = .28f;
                var corners = new[] { new Vector3(-w / 2, 0, -d / 2), new Vector3(w / 2, 0, -d / 2), new Vector3(w / 2, 0, d / 2), new Vector3(-w / 2, 0, d / 2) };
                for (int i = 0; i < 4; i++)
                {
                    Vector3 a = corners[i], b = corners[(i + 1) % 4];
                    Vector3 ai = new Vector3(a.x - Mathf.Sign(a.x) * inset, 0, a.z - Mathf.Sign(a.z) * inset);
                    Vector3 bi = new Vector3(b.x - Mathf.Sign(b.x) * inset, 0, b.z - Mathf.Sign(b.z) * inset);
                    Vector3 outward = Vector3.Cross(Vector3.up, b - a).normalized;
                    Vector3 n = (outward * rise + Vector3.up * inset).normalized;
                    kit.AddQuad(Roof, a + Vector3.up * h, b + Vector3.up * h, bi + Vector3.up * (h + rise), ai + Vector3.up * (h + rise), n);
                }
                kit.Quad(Roof, Vector3.up * (h + rise), Vector3.up, Vector3.forward, w - 2 * inset, d - 2 * inset);
                var face = FrontFace(w, d);
                int dormers = Mathf.Max(1, Mathf.RoundToInt(face.width / (pitch * 1.5f)));
                for (int i = 0; i < dormers; i++)
                {
                    Vector3 c = face.center + face.right * (-face.width / 2 + face.width * (i + .5f) / dormers) - face.normal * .1f + Vector3.up * (h + rise * .45f);
                    kit.Box(Trim, c, Rot(face, .34f, rise * .7f, .26f));
                    kit.Quad(rnd.NextDouble() < .3 ? GlassLit : Glass, c + face.normal * .135f, face.normal, Vector3.up, .2f, rise * .4f);
                    kit.Cone(Roof, c + Vector3.up * rise * .35f, .25f, .14f, 4, 45);
                }
                kit.Box(Trim, Vector3.up * (h + .04f), new Vector3(w + .16f, .08f, d + .16f)); // cornice under the mansard
            }

            Face FrontFace(float w, float d)
            {
                foreach (var face in Faces(Vector3.zero, w, d)) if (face.normal == front) return face;
                return default;
            }

            public void FireEscape(Face face, int floors)
            {
                float fw = Mathf.Min(face.width * .5f, 1.6f), depth = .32f;
                float side = (rnd.NextDouble() < .5 ? -1 : 1) * (face.width / 2 - fw / 2 - .1f);
                Vector3 axis = face.center + face.right * side;
                for (int floor = 1; floor < floors; floor++)
                {
                    float y = FloorBase(floor) + .02f;
                    Vector3 c = axis + face.normal * depth / 2 + Vector3.up * y;
                    kit.Box(Iron, c, Rot(face, fw, .025f, depth), Quaternion.identity, true);
                    kit.Box(Iron, c + face.normal * depth / 2 + Vector3.up * .26f, Rot(face, fw, .02f, .02f));
                    foreach (float s in new[] { -fw / 2, fw / 2 })
                        kit.Box(Iron, axis + face.right * s + face.normal * depth / 2 + Vector3.up * (y + .13f), Rot(face, .02f, .26f, depth));
                    if (floor < floors - 1)
                    {
                        float rise = F, run = fw * .6f, length = Mathf.Sqrt(rise * rise + run * run);
                        var r = Quaternion.LookRotation(face.normal, Vector3.up) * Quaternion.Euler(0, 0, Mathf.Atan2(rise, run) * Mathf.Rad2Deg);
                        kit.Box(Iron, axis + face.normal * (depth * .5f) + Vector3.up * (y + rise / 2), new Vector3(length, .02f, .1f), r, true);
                    }
                }
            }

            public void WaterTower(float roof, float w, float d)
            {
                float s = settings.heightScale / .3f;
                float r = .3f * s, legs = .45f * s;
                float x = (float)(rnd.NextDouble() - .5) * Mathf.Max(0, w - 4 * r), z = (float)(rnd.NextDouble() - .5) * Mathf.Max(0, d - 4 * r);
                Vector3 c = new Vector3(x, roof, z);
                foreach (var o in new[] { new Vector3(1, 0, 1), new Vector3(-1, 0, 1), new Vector3(1, 0, -1), new Vector3(-1, 0, -1) })
                    kit.Box(Iron, c + o * r * .7f + Vector3.up * legs / 2, new Vector3(.04f, legs, .04f) * s);
                kit.Prism(Wood, c + Vector3.up * legs, r, .45f * s, 12, false);
                kit.Cone(Roof, c + Vector3.up * (legs + .45f * s), r * 1.1f, .22f * s, 12);
            }

            static Vector3 Rot(Face face, float along, float up, float outward)
                => Mathf.Abs(face.normal.x) > .5f ? new Vector3(outward, up, along) : new Vector3(along, up, outward);
        }

        // ---------------- Helpers ----------------

        static float Range(System.Random r, float min, float max) => min + (float)r.NextDouble() * (max - min);
        static int Pick(System.Random r, params int[] options) => options[r.Next(options.Length)];

        internal static Material Mat(string name, Color color, float smoothness = .15f)
        {
            var m = CityBlockout.Mat(name, color);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }

        // Fire escapes, railings and street iron: near-black, as the city's landmark rules describe historic
        // fire escapes ("a dark color such as black or brown").
        internal static Material IronMaterial() => Mat("City Iron", new Color(.11f, .11f, .10f), .3f);

        internal static Material LitGlass()
        {
            var m = Mat("City Glass Lit", new Color(1f, .83f, .52f), .6f);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", new Color(1f, .72f, .38f) * 1.4f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            return m;
        }
    }
}
