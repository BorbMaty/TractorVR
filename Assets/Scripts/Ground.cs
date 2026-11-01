using UnityEngine;

/// <summary>Surface types of the world (one per square metre in <see cref="GroundMap"/>).</summary>
public enum Ground : byte { Grass, Soil, Crop, Ploughed, Mud, Gravel, Asphalt, Concrete }

/// <summary>
/// Where things are in the 2 × 2 km world. The start (origin, flat within 60 m) is the farmyard:
/// concrete yard with the barn on the west side, an open machine shed on the south side and the farmhouse to the east.
/// Gravel tracks lead to the fields; an asphalt road runs north–south at x = 80.
/// </summary>
public static class WorldLayout
{
    public struct Area
    {
        public string name; public Ground type; public float x0, z0, x1, z1;
        public bool Contains(float x, float z) => x >= x0 && x <= x1 && z >= z0 && z <= z1;
        public Vector2 Centre => new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
    }

    public struct Road { public string name; public Ground type; public float width; public Vector2[] points; }

    public static readonly Area Yard = new Area { name = "Yard", type = Ground.Concrete, x0 = -26f, z0 = -42f, x1 = 26f, z1 = 22f };
    public static readonly Area Barn = new Area { name = "Barn", x0 = -44f, z0 = -12f, x1 = -30f, z1 = 12f };
    public static readonly Area Shed = new Area { name = "Machine shed", x0 = -18f, z0 = -42f, x1 = 18f, z1 = -32f };
    /// <summary>Farmhouse east of the yard: gable end to the yard road (south), porch along the yard side (west).</summary>
    public static readonly Area Farmhouse = new Area { name = "Farmhouse", x0 = 31f, z0 = 6f, x1 = 40f, z1 = 21f };
    /// <summary>Tipping pad / muck heap in the north-west of the yard: walls west, north and east, open to the south (the trailer reverses in).</summary>
    public static readonly Area TipPad = new Area { name = "Tipping pad", x0 = -24f, z0 = 12f, x1 = -14f, z1 = 21f };
    /// <summary>Yard details from tools/blender (each asset's origin): two grain silos east of the shed (in a row along X, their
    /// filling hopper 7.4 m north of them), the diesel tank, the round-bale stack north of the barn.</summary>
    public static readonly Vector2 Silos = new Vector2(25f, -38f), FuelTank = new Vector2(22.5f, -22f), BaleStack = new Vector2(-37f, 16.5f);
    /// <summary>The Székely-style yard gate across the yard road (opening 5.1 m, 4.5 m clear), the garden and yard fences: all in
    /// tools/blender/fences.py, in world coordinates (GATE_X there).</summary>
    public const float GateX = 28f;

    /// <summary>Speed bumps across the west track's first leg (from the yard's south-west corner past the barn): x, z, yaw. The asset's
    /// local X runs along the track. Away from every scripted test and benchmark drive.</summary>
    public static readonly Vector3[] SpeedBumps = { BumpOnWestTrack(10f), BumpOnWestTrack(24f) };

    /// <summary>Steep practice slopes (steeper than the hills' ~11°), x, z, yaw. Silage clamp behind the barn: 32 m along Z, heap 2.4 m
    /// with 1:3 (18°) ramps at both ends. Earth ramp on the pasture north-west of the yard: track along X, 15° side west, 22° side east.</summary>
    public static readonly Vector3 SilageClamp = new Vector3(-52f, 0f, 90f), EarthRamp = new Vector3(-35f, 40f, 0f);

    /// <summary>The countryside around Reghin (phase 5 step 3): haystacks (boglya) on the pastures beside the meadow (x, z), pole gates
    /// at the three field entries where the tracks end (x, z of the fork post, yaw: the pole swings across the track along local +X),
    /// a wayside cross at the corner of the yard road and the main road (x, z, yaw).</summary>
    public static readonly Vector2[] Haystacks =
    {
        new Vector2(-185f, 125f), new Vector2(-196f, 141f), new Vector2(-181f, 158f), new Vector2(-200f, 176f),
        new Vector2(-188f, 200f), new Vector2(102f, 30f), new Vector2(115f, 46f),
    };
    public static readonly Vector3[] PoleGates =
    {
        new Vector3(-2.6f, 113.5f, 0f),      // north track -> meadow (the pole swings across to +X)
        new Vector3(113.5f, -97.4f, 90f),    // south-east track -> south-east field (across to -Z)
        new Vector3(-223.5f, -57.4f, 90f),   // west track -> west field
    };
    public static readonly Vector3 WaysideCross = new Vector3(86.5f, 5f, -90f);

    static Vector3 BumpOnWestTrack(float along)
    {
        Vector2 a = new Vector2(-25f, -30f), b = new Vector2(-60f, -60f), d = (b - a).normalized, p = a + d * along;
        return new Vector3(p.x, p.y, Mathf.Atan2(-d.y, d.x) * Mathf.Rad2Deg);   // yaw that turns local +X onto d
    }

    /// <summary>Parking bay in the machine shed (open to the yard, +Z): where an implement's coupling point stands and which way it faces.
    /// Rear implements face the yard (yaw 0: the tractor reverses in), front ones the back wall (yaw 180: the tractor drives in nose first).</summary>
    public struct Bay { public string implement; public Vector2 coupling; public float yaw; }

    /// <summary>The tutorial's implement spots, west to east. Same order and bay width (shed width / 4) as tools/blender/shed.py,
    /// which paints the lines and the name boards.</summary>
    public static readonly Bay[] ShedBays =
    {
        ShedBay(0, "FrontWeight", true), ShedBay(1, "Mower", true), ShedBay(2, "Plough", false), ShedBay(3, "Trailer", false),
    };

    static Bay ShedBay(int i, string implement, bool front) => new Bay
    {
        implement = implement,
        coupling = new Vector2(Shed.x0 + (i + 0.5f) * (Shed.x1 - Shed.x0) / 4f, Shed.z1 - (front ? 3f : 2f)),
        yaw = front ? 180f : 0f,
    };

    public static Bay ShedBayOf(string implement) => System.Array.Find(ShedBays, b => b.implement == implement);

    public static readonly Area[] Fields =
    {
        // Phase 5 step 3: the north field is split by the main road into a meadow (grass, the mower's work) and a wheat field.
        new Area { name = "North meadow", type = Ground.Grass, x0 = -160f, z0 = 115f, x1 = 80f, z1 = 420f },
        new Area { name = "North wheat field", type = Ground.Crop, x0 = 80f, z0 = 115f, x1 = 160f, z1 = 420f },
        new Area { name = "South-east field", type = Ground.Soil, x0 = 115f, z0 = -360f, x1 = 410f, z1 = -75f },
        new Area { name = "West field", type = Ground.Soil, x0 = -420f, z0 = -160f, x1 = -225f, z1 = 140f },
    };

    public static readonly Road[] Roads =
    {
        new Road { name = "Main road", type = Ground.Asphalt, width = 6.5f, points = new[] { new Vector2(80f, -1000f), new Vector2(80f, 1000f) } },
        new Road { name = "Yard to road", type = Ground.Gravel, width = 4f, points = new[] { new Vector2(25f, 0f), new Vector2(80f, 0f) } },
        new Road { name = "North track", type = Ground.Gravel, width = 4f, points = new[] { new Vector2(0f, 21f), new Vector2(0f, 117f) } },
        new Road { name = "South-east track", type = Ground.Gravel, width = 4f, points = new[] { new Vector2(80f, -100f), new Vector2(117f, -100f) } },
        new Road { name = "West track", type = Ground.Gravel, width = 4f, points = new[] { new Vector2(-25f, -30f), new Vector2(-60f, -60f), new Vector2(-227f, -60f) } },
    };

    /// <summary>Muddy field gates (the tracks end in them) and a few wet hollows; more mud comes from noise in the pasture.</summary>
    public static readonly Vector3[] MudSpots =   // x, z, radius
    {
        new Vector3(0f, 116f, 8f), new Vector3(116f, -100f, 8f), new Vector3(-226f, -60f, 8f),
    };

    public static float DistanceToRoad(in Road r, float x, float z)
    {
        float best = float.MaxValue;
        var p = new Vector2(x, z);
        for (int i = 0; i + 1 < r.points.Length; i++)
        {
            Vector2 a = r.points[i], b = r.points[i + 1], ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
        }
        return best;
    }
}

/// <summary>
/// Surface type for every square metre of the world, with the tyre / soil numbers the physics uses.
/// Built from <see cref="WorldLayout"/> + noise (mud in hollows of the pasture, wet spots in the fields).
/// Work changes it at runtime (the plough leaves "ploughed").
/// </summary>
public static class GroundMap
{
    public const int Size = 2000;          // cells per side, 1 m each
    public const float Half = Size * 0.5f;

    public struct Surface
    {
        public string label;
        public float grip;      // tyre friction coefficient
        public float rolling;   // rolling resistance (× weight)
        public float draft;     // plough draft multiplier; 0 = hard surface the shares can't enter
        public float slipK;     // traction curve μ(s) = grip·(1 − e^(−k·s)): high k = little slip (hard ground), low k = soft ground
        public Color dirt;      // clods thrown by a spinning wheel (alpha 0 = none: hard surfaces)
    }

    static readonly Surface[] surfaces =
    {
        new Surface { label = "GRASS",    grip = 0.70f, rolling = 0.050f, draft = 1.25f, slipK = 14f, dirt = new Color(0.30f, 0.33f, 0.16f, 1f) },
        new Surface { label = "SOIL",     grip = 0.65f, rolling = 0.070f, draft = 1.00f, slipK = 11f, dirt = new Color(0.36f, 0.27f, 0.18f, 1f) },
        new Surface { label = "CROP",     grip = 0.65f, rolling = 0.070f, draft = 1.00f, slipK = 11f, dirt = new Color(0.36f, 0.27f, 0.18f, 1f) },
        new Surface { label = "PLOUGHED", grip = 0.50f, rolling = 0.120f, draft = 0.60f, slipK = 8f, dirt = new Color(0.30f, 0.22f, 0.15f, 1f) },
        new Surface { label = "MUD",      grip = 0.35f, rolling = 0.150f, draft = 0.80f, slipK = 6f, dirt = new Color(0.22f, 0.17f, 0.11f, 1f) },
        new Surface { label = "GRAVEL",   grip = 0.65f, rolling = 0.030f, draft = 0f, slipK = 22f, dirt = new Color(0f, 0f, 0f, 0f) },   // draft 0 = hard: shares can't enter
        new Surface { label = "ASPHALT",  grip = 0.90f, rolling = 0.015f, draft = 0f, slipK = 35f, dirt = new Color(0f, 0f, 0f, 0f) },
        new Surface { label = "CONCRETE", grip = 0.85f, rolling = 0.015f, draft = 0f, slipK = 35f, dirt = new Color(0f, 0f, 0f, 0f) },
    };

    static byte[] cells;
    public static bool Built => cells != null;

    public static Surface Info(Ground g) => surfaces[(int)g];

    public static Ground At(float x, float z)
    {
        if (cells == null) return Ground.Grass;
        int i = Mathf.FloorToInt(x + Half), j = Mathf.FloorToInt(z + Half);
        if (i < 0 || j < 0 || i >= Size || j >= Size) return Ground.Grass;
        return (Ground)cells[j * Size + i];
    }

    public static Surface SurfaceAt(Vector3 p) => Info(At(p.x, p.z));

    public static void Set(float x, float z, Ground g)
    {
        if (cells == null) return;
        int i = Mathf.FloorToInt(x + Half), j = Mathf.FloorToInt(z + Half);
        if (i >= 0 && j >= 0 && i < Size && j < Size) cells[j * Size + i] = (byte)g;
    }

    public static void Build()
    {
        cells = new byte[Size * Size];
        for (int j = 0; j < Size; j++)
        for (int i = 0; i < Size; i++)
            cells[j * Size + i] = (byte)Classify(i - Half + 0.5f, j - Half + 0.5f);
    }

    /// <summary>Mud from noise: hollows of the pasture and wet spots in the fields (not near the yard).</summary>
    static bool Wet(float x, float z, float threshold)
    {
        if (x * x + z * z < 120f * 120f) return false;
        return Mathf.PerlinNoise(x / 45f + 311.7f, z / 45f + 77.3f) * 0.75f + Mathf.PerlinNoise(x / 11f + 5.1f, z / 11f + 9.4f) * 0.25f > threshold;
    }

    public static Ground Classify(float x, float z)
    {
        foreach (var r in WorldLayout.Roads)
            if (r.type == Ground.Asphalt && WorldLayout.DistanceToRoad(r, x, z) < r.width * 0.5f) return r.type;
        foreach (var r in WorldLayout.Roads)
            if (r.type != Ground.Asphalt && WorldLayout.DistanceToRoad(r, x, z) < r.width * 0.5f) return r.type;
        if (WorldLayout.Yard.Contains(x, z)) return Ground.Concrete;
        foreach (var m in WorldLayout.MudSpots)
            if ((x - m.x) * (x - m.x) + (z - m.y) * (z - m.y) < m.z * m.z) return Ground.Mud;
        foreach (var f in WorldLayout.Fields)
            if (f.Contains(x, z)) return Wet(x, z, 0.74f) ? Ground.Mud : f.type;
        return Wet(x, z, 0.70f) ? Ground.Mud : Ground.Grass;
    }

    /// <summary>
    /// Terrain control texture (2 m per texel): R = field soil, G = mud, B = ploughed (darker soil), A = standing crop; grass is what's left.
    /// Roads and the yard are separate meshes; under them the terrain shows soil (a dirt verge).
    /// </summary>
    public static Texture2D ControlTexture(int res = 1024)
    {
        var tex = new Texture2D(res, res, TextureFormat.RGBA32, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "GroundControl" };
        var px = new Color32[res * res];
        float step = Size / (float)res;
        for (int j = 0; j < res; j++)
        for (int i = 0; i < res; i++)
        {
            Ground g = At(-Half + (i + 0.5f) * step, -Half + (j + 0.5f) * step);
            byte r = 0, gm = 0, b = 0, a = 0;
            switch (g)
            {
                case Ground.Crop: r = 255; a = 255; break;   // A: standing crop (GroundLit colours it like wheat beyond the clumps)
                case Ground.Soil: case Ground.Gravel: case Ground.Asphalt: case Ground.Concrete: r = 255; break;
                case Ground.Ploughed: r = 255; b = 255; break;
                case Ground.Mud: gm = 255; break;
            }
            px[j * res + i] = new Color32(r, gm, b, a);
        }
        tex.SetPixels32(px);
        tex.Apply(false, false);
        return tex;
    }
}
