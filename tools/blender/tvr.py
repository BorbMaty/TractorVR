"""Shared helpers for the TractorVR environment assets (phase 5).

Each asset script (barn.py, ...) describes its parts once, in **Unity coordinates** (metres, Y up, +Z forward,
origin on the ground), and builds two variants from them:
  Full  (PC tier)    every part, fine grid (baked AO needs vertices: it lives in the vertex colour)
  Light (Quest tier) parts marked detail=False only, coarse grid
Both go into one FBX (`Assets/Models/Env/<Asset>.fbx`, objects `<Asset>_Full` / `<Asset>_Light`), which
ProjectSetup imports; QualityTier shows one of them. Materials are only names here (`Env_*`); Unity makes
them from `env_materials.json` (TractorVR/EnvLit: albedo + normal map from Assets/Textures/Env).

Axes: Blender's FBX export (forward -Z, up Y, bake_space_transform) lands in Unity as (-x, z, -y), checked
with a test export on 2026-10-31. `U()` converts the other way, so the scripts never see Blender axes.

UVs: each face is projected on its own plane (u horizontal, v up the slope / wall), divided by the texture's
real size from Assets/Textures/Env/sizes.json, so every texture repeats at its true scale. `rot` (degrees)
turns the texture on the face (the plank texture has horizontal boards; barn walls want them vertical).
Materials with grain=True (beams, posts) run the texture's U along the long axis of each box instead.
"""
import json
import math
import pathlib

import bmesh
import bpy
from mathutils import Vector

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT_DIR = ROOT / "Assets" / "Models" / "Env"
TEX_DIR = ROOT / "Assets" / "Textures" / "Env"
SIZES = json.loads((TEX_DIR / "sizes.json").read_text())
SIZES.setdefault("impl_paint", SIZES.get("green_metal_rust", [1.0, 1.0]))   # tools/make_impl_paint.py, from green_metal_rust

# name -> texture id (None = plain colour), UV rotation, Unity tint (multiplies the texture), smoothness.
MATERIALS = {
    "Env_Boards":     dict(tex="weathered_plank_siding", rot=90, tint=(1.00, 0.92, 0.86), smooth=0.10),
    "Env_BoardsDark": dict(tex="weathered_plank_siding", rot=90, tint=(0.62, 0.50, 0.44), smooth=0.10),
    "Env_Timber":     dict(tex="weathered_plank_siding", rot=0,  tint=(0.55, 0.47, 0.40), smooth=0.08, grain=True),
    "Env_RoofTiles":  dict(tex="clay_roof_tiles_02",     rot=0,  tint=(0.92, 0.80, 0.76), smooth=0.18),
    "Env_Plaster":    dict(tex="white_rough_plaster",    rot=0,  tint=(0.95, 0.94, 0.90), smooth=0.05),
    "Env_Corrugated": dict(tex="corrugated_iron_02",     rot=0,  tint=(0.95, 0.95, 0.95), smooth=0.30),
    "Env_PaintYellow": dict(tex=None, rot=0, tint=(0.80, 0.58, 0.08), smooth=0.20),
    "Env_PaintWhite":  dict(tex=None, rot=0, tint=(0.88, 0.88, 0.84), smooth=0.20),
    "Env_PlasterHouse": dict(tex="white_rough_plaster",  rot=0,  tint=(0.98, 0.86, 0.62), smooth=0.05),
    "Env_PlasterBase":  dict(tex="white_rough_plaster",  rot=0,  tint=(0.48, 0.54, 0.60), smooth=0.08),
    "Env_PaintGreen":   dict(tex=None, rot=0, tint=(0.16, 0.34, 0.24), smooth=0.25),
    "Env_Glass":        dict(tex=None, rot=0, tint=(0.08, 0.10, 0.12), smooth=0.85),
    "Env_Concrete":     dict(tex="white_rough_plaster",  rot=0,  tint=(0.62, 0.62, 0.60), smooth=0.08),
    "Env_SiloSteel":    dict(tex="corrugated_iron_02",   rot=90, tint=(2.0, 2.05, 2.15), smooth=0.40),   # dark texture: galvanised = brighter
    "Env_Steel":        dict(tex=None, rot=0, tint=(0.62, 0.64, 0.66), smooth=0.45),
    "Env_PaintRed":     dict(tex=None, rot=0, tint=(0.55, 0.10, 0.07), smooth=0.35),
    "Env_Hay":          dict(tex=None, rot=0, tint=(0.80, 0.68, 0.38), smooth=0.05),
    "Env_HayOld":       dict(tex=None, rot=0, tint=(0.60, 0.55, 0.40), smooth=0.05),
    "Env_Muck":         dict(tex=None, rot=0, tint=(0.20, 0.15, 0.10), smooth=0.30),
    "Env_RubberBlack":  dict(tex=None, rot=0, tint=(0.05, 0.05, 0.05), smooth=0.20),
    "Env_Earth":        dict(tex="brown_mud_dry",        rot=0,  tint=(0.95, 0.90, 0.82), smooth=0.06),
    "Env_SilageSheet":  dict(tex=None, rot=0, tint=(0.03, 0.03, 0.035), smooth=0.55),
    "Env_Rope":         dict(tex=None, rot=0, tint=(0.55, 0.48, 0.32), smooth=0.10),
    "Env_HayStack":     dict(tex=None, rot=0, tint=(0.52, 0.48, 0.28), smooth=0.05),
    "Env_HayGrey":      dict(tex=None, rot=0, tint=(0.42, 0.40, 0.33), smooth=0.05),
    # Trees (phase 5 step 4): plain colours, the crowns' light and depth come from the smooth blobs and the baked AO.
    "Env_ConeOrange":   dict(tex=None, rot=0, tint=(0.95, 0.30, 0.04), smooth=0.35),
    "Env_PlasterVillage": dict(tex=None, rot=0, tint=(0.86, 0.84, 0.78), smooth=0.05),
    "Env_RoofVillage":  dict(tex=None, rot=0, tint=(0.55, 0.24, 0.16), smooth=0.10),
    "Env_RoofSlate":    dict(tex=None, rot=0, tint=(0.24, 0.26, 0.28), smooth=0.30),
    "Env_Bark":         dict(tex=None, rot=0, tint=(0.24, 0.21, 0.18), smooth=0.05),
    "Env_BarkPale":     dict(tex=None, rot=0, tint=(0.46, 0.44, 0.40), smooth=0.05),
    "Env_LeafWalnut":   dict(tex=None, rot=0, tint=(0.20, 0.31, 0.11), smooth=0.10),
    "Env_LeafPear":     dict(tex=None, rot=0, tint=(0.22, 0.33, 0.12), smooth=0.10),
    "Env_LeafAcacia":   dict(tex=None, rot=0, tint=(0.29, 0.39, 0.14), smooth=0.10),
    "Env_LeafPoplar":   dict(tex=None, rot=0, tint=(0.18, 0.29, 0.11), smooth=0.10),
    "Env_LeafWillow":   dict(tex=None, rot=0, tint=(0.36, 0.42, 0.25), smooth=0.10),
    # Implements (phase 5 step 2): worn paint (impl_paint is near white, the tint is the paint colour), bare metal, rubber.
    "Impl_Blue":        dict(tex="impl_paint", rot=0, tint=(0.14, 0.26, 0.60), smooth=0.35),
    "Impl_Red":         dict(tex="impl_paint", rot=0, tint=(0.80, 0.12, 0.09), smooth=0.35),
    "Impl_Green":       dict(tex="impl_paint", rot=0, tint=(0.24, 0.55, 0.19), smooth=0.35),
    "Impl_Yellow":      dict(tex="impl_paint", rot=0, tint=(1.00, 0.80, 0.12), smooth=0.35),
    "Impl_Steel":       dict(tex="impl_paint", rot=0, tint=(0.62, 0.62, 0.64), smooth=0.55),
    "Impl_Cast":        dict(tex="impl_paint", rot=0, tint=(0.20, 0.20, 0.21), smooth=0.25),
    "Impl_Polished":    dict(tex=None, rot=0, tint=(0.55, 0.57, 0.60), smooth=0.85),   # mouldboards, shares: polished by the soil
    "Impl_Chrome":      dict(tex=None, rot=0, tint=(0.82, 0.84, 0.88), smooth=0.90),
    "Impl_Rubber":      dict(tex=None, rot=0, tint=(0.07, 0.07, 0.07), smooth=0.15),
    "Impl_Lamp":        dict(tex=None, rot=0, tint=(0.85, 0.08, 0.05), smooth=0.90),
    "Impl_Amber":       dict(tex=None, rot=0, tint=(1.00, 0.55, 0.05), smooth=0.90),
    "Impl_White":       dict(tex=None, rot=0, tint=(0.92, 0.92, 0.88), smooth=0.30),
}

VARIANTS = {"Full": dict(cell=1.0, detail=True), "Light": dict(cell=3.0, detail=False)}


def U(x, y, z):
    """Unity position -> Blender position."""
    return Vector((-x, -z, y))


# ---------------------------------------------------------------- mesh building

class Builder:
    """Collects closed solids into one bmesh with material slots. Positions in Unity coordinates."""

    def __init__(self, cell, detail):
        self.bm = bmesh.new()
        self.cell = cell          # grid size (m) for big faces, so the baked AO has vertices to live on
        self.detail = detail      # Full variant: add the detail parts too
        self.mats = []
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.grain = [self.bm.faces.layers.float.new(f"grain_{a}") for a in "xyz"]   # long axis of the box (Blender)
        self.keep = self.bm.faces.layers.int.new("keep_normals")   # text: winding from the font, not recalculated
        self.own_uv = self.bm.faces.layers.int.new("own_uv")       # revolve: UVs set when built (around / along)
        self.smooth = self.bm.faces.layers.int.new("smooth")       # blob: smooth shading (crowns)
        self.colliders = []       # boxes for Unity (centre, size, yaw), written to <Asset>.json by build_asset

    def _mat(self, name):
        if name not in MATERIALS:
            raise KeyError(f"unknown material {name}: add it to tvr.MATERIALS")
        if name not in self.mats:
            self.mats.append(name)
        return self.mats.index(name)

    def _grid(self, a, b, d, mat, cell):
        """Quad a, b, b+(d-a), d (Blender coords) split into cells of about `cell` metres."""
        cell = cell or self.cell
        e1, e2 = b - a, d - a
        nu = max(1, math.ceil(e1.length / cell - 0.01))
        nv = max(1, math.ceil(e2.length / cell - 0.01))
        vs = [[self.bm.verts.new(a + e1 * (i / nu) + e2 * (j / nv)) for i in range(nu + 1)] for j in range(nv + 1)]
        mi = self._mat(mat)
        faces = []
        for j in range(nv):
            for i in range(nu):
                f = self.bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))
                f.material_index = mi
                faces.append(f)
        return faces

    def _uv(self, f):
        info = MATERIALS[self.mats[f.material_index]]
        size = SIZES[info["tex"]] if info["tex"] else (1.0, 1.0)
        n = f.normal
        up = Vector((0, 0, 1))
        t = up.cross(n) if abs(n.z) < 0.99 else Vector((1, 0, 0))
        if info.get("grain"):
            g = Vector([f[layer] for layer in self.grain])
            g -= n * g.dot(n)
            if g.length > 0.3:   # end faces (grain along the normal) keep the default
                t = g
        t.normalize()
        b = n.cross(t).normalized()
        c, s = math.cos(math.radians(info["rot"])), math.sin(math.radians(info["rot"]))
        for loop in f.loops:
            p = loop.vert.co
            u, v = p.dot(t) / size[0], p.dot(b) / size[1]
            loop[self.uv].uv = (u * c - v * s, u * s + v * c)

    def box(self, centre, size, mat, yaw=0.0, pitch=0.0, roll=0.0, cell=None, detail=False):
        """Box in Unity coords: centre, size (x, y, z), Euler angles in degrees applied Z, X, Y like Unity."""
        if detail and not self.detail:
            return
        hx, hy, hz = (s * 0.5 for s in size)
        rot = _unity_rotation(pitch, yaw, roll)
        cx, cy, cz = centre

        def P(x, y, z):
            q = rot @ Vector((x, y, z))
            return U(cx + q.x, cy + q.y, cz + q.z)

        corners = [P(sx * hx, sy * hy, sz * hz) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
        i = lambda sx, sy, sz: corners[((sx > 0) << 2) | ((sy > 0) << 1) | (sz > 0)]
        quads = [
            (i(-1, -1, -1), i(1, -1, -1), i(-1, -1, 1)),   # bottom
            (i(-1, 1, -1), i(1, 1, -1), i(-1, 1, 1)),      # top
            (i(-1, -1, -1), i(1, -1, -1), i(-1, 1, -1)),   # back (-z)
            (i(-1, -1, 1), i(1, -1, 1), i(-1, 1, 1)),      # front (+z)
            (i(-1, -1, -1), i(-1, -1, 1), i(-1, 1, -1)),   # left
            (i(1, -1, -1), i(1, -1, 1), i(1, 1, -1)),      # right
        ]
        first = len(self.bm.verts)
        axis = max(range(3), key=lambda k: size[k])
        g = U(*(rot @ Vector([1.0 if k == axis else 0.0 for k in range(3)])))
        for a, b, d in quads:
            for f in self._grid(a, b, d, mat, cell):
                for k, layer in enumerate(self.grain):
                    f[layer] = g[k]
        self._weld(first)

    def gable(self, x0, x1, y0, rise, z0, z1, mat, cell=None, detail=False):
        """Triangular gable (Unity coords): base from x0 to x1 at height y0, apex `rise` above the middle,
        extruded from z0 to z1. The end faces are banded grids, so the baked AO has vertices inside them."""
        if detail and not self.detail:
            return
        cell = cell or self.cell
        first = len(self.bm.verts)
        nv = max(1, math.ceil(rise / cell - 0.01))
        nu = max(1, math.ceil((x1 - x0) / cell - 0.01))
        xm = (x0 + x1) * 0.5
        mi = self._mat(mat)

        def row(j, z):
            f = j / nv
            a, b = x0 + (xm - x0) * f, x1 + (xm - x1) * f
            return [self.bm.verts.new(U(a + (b - a) * i / nu, y0 + rise * f, z)) for i in range(nu + 1)]

        ends = []
        for z in (z0, z1):
            rows = [row(j, z) for j in range(nv + 1)]
            ends.append(rows)
            for j in range(nv):
                for i in range(nu):
                    q = (rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i])
                    if j == nv - 1:   # apex row: all top vertices meet, so triangles
                        q = (rows[j][i], rows[j][i + 1], rows[j + 1][0])
                    self.bm.faces.new(q).material_index = mi
        # Sides and bottom between the two ends.
        f_rows, b_rows = ends
        rims = [[r[0] for r in f_rows], [r[-1] for r in f_rows]]
        rims_b = [[r[0] for r in b_rows], [r[-1] for r in b_rows]]
        for side in range(2):
            for j in range(nv):
                a, b = rims[side][j], rims[side][j + 1]
                c, d = rims_b[side][j + 1], rims_b[side][j]
                if j == nv - 1:
                    b, c = f_rows[-1][0], b_rows[-1][0]
                self.bm.faces.new((a, b, c, d)).material_index = mi
        for i in range(nu):
            self.bm.faces.new((f_rows[0][i], f_rows[0][i + 1], b_rows[0][i + 1], b_rows[0][i])).material_index = mi
        self._weld(first)

    def wall(self, p0, p1, y0, top0, top1, thick, mat, cell=None, detail=False):
        """Straight wall from p0 to p1 (Unity XZ, centre line), bottom y0, top rising from top0 at p0 to top1 at p1
        (a lean-to roof's side walls). Gridded on both faces for the baked AO."""
        if detail and not self.detail:
            return
        cell = cell or self.cell
        first = len(self.bm.verts)
        a, b = Vector((p0[0], p0[1])), Vector((p1[0], p1[1]))
        along = b - a
        n = Vector((-along.y, along.x)).normalized() * (thick * 0.5)
        nu = max(1, math.ceil(along.length / cell - 0.01))
        nv = max(1, math.ceil((max(top0, top1) - y0) / cell - 0.01))
        mi = self._mat(mat)
        sides = []
        for sgn in (-1, 1):
            vs = []
            for j in range(nv + 1):
                row = []
                for i in range(nu + 1):
                    f = i / nu
                    q = a + along * f + n * sgn
                    top = top0 + (top1 - top0) * f
                    row.append(self.bm.verts.new(U(q.x, y0 + (top - y0) * j / nv, q.y)))
                vs.append(row)
            sides.append(vs)
            for j in range(nv):
                for i in range(nu):
                    self.bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i])).material_index = mi
        A, B = sides
        for i in range(nu):   # bottom, top
            self.bm.faces.new((A[0][i], A[0][i + 1], B[0][i + 1], B[0][i])).material_index = mi
            self.bm.faces.new((A[nv][i], A[nv][i + 1], B[nv][i + 1], B[nv][i])).material_index = mi
        for i in (0, nu):     # ends
            for j in range(nv):
                self.bm.faces.new((A[j][i], A[j + 1][i], B[j + 1][i], B[j][i])).material_index = mi
        self._weld(first)

    def text(self, body, centre, height, mat, yaw=0.0, depth=0.02, detail=False):
        """Upright letters as geometry (Blender's built-in font), centred on `centre` (Unity), cap height about
        `height`, readable from the +Z side turned by `yaw` degrees. For signs (bays, tutorial spots)."""
        if detail and not self.detail:
            return
        cu = bpy.data.curves.new("_tvr_text", "FONT")
        cu.body = body
        cu.align_x, cu.align_y = "CENTER", "CENTER"
        cu.resolution_u = 2 if not self.detail else 3
        ob = bpy.data.objects.new("_tvr_text", cu)
        bpy.context.scene.collection.objects.link(ob)
        bpy.context.view_layer.update()
        dims_y = max(ob.dimensions.y, 1e-3)
        scale = height / (dims_y * 0.75)   # dimensions include descenders: caps are ~3/4 of it
        cu.extrude = depth * 0.5 / scale
        dg = bpy.context.evaluated_depsgraph_get()
        ev = ob.evaluated_get(dg)
        me = ev.to_mesh()
        rot = _unity_rotation(0.0, yaw, 0.0)
        right = rot @ Vector((-1, 0, 0))   # a reader facing the sign looks along -Z: their right is -X
        up = Vector((0, 1, 0))
        out = rot @ Vector((0, 0, 1))
        c = Vector(centre)
        first = len(self.bm.verts)
        vs = [self.bm.verts.new(U(*(c + right * (v.co.x * scale) + up * (v.co.y * scale) + out * (v.co.z * scale))))
              for v in me.vertices]
        mi = self._mat(mat)
        for poly in me.polygons:
            try:
                f = self.bm.faces.new([vs[i] for i in poly.vertices])
                f.material_index = mi
                f[self.keep] = 1
            except ValueError:   # duplicate face from the font fill
                pass
        ev.to_mesh_clear()
        bpy.data.objects.remove(ob, do_unlink=True)
        bpy.data.curves.remove(cu)
        self._weld(first)

    def collider(self, centre, size, yaw=0.0, roll=0.0):
        """A BoxCollider for Unity (asset space, Unity coords), turned by Euler(0, yaw, roll) (roll: ramps, a speed bump's slopes).
        Nothing is drawn; ProjectSetup adds it from the JSON."""
        if self.detail:   # recorded once (the Full pass)
            self.colliders.append(dict(centre=[round(v, 4) for v in centre], size=[round(v, 4) for v in size],
                                       yaw=round(yaw, 3), roll=round(roll, 3)))

    def revolve(self, centre, profile, mat, segs=16, pitch=0.0, yaw=0.0, roll=0.0, cell=None, detail=False):
        """Solid of revolution around the local Y axis: `profile` = [(radius, y), ...] bottom to top (radius 0 = a point),
        open ends closed with a fan. Turned by Unity Euler angles and moved to `centre` (cylinders lying down: pitch 90).
        The Light variant gets half the segments. UVs: u around the circumference, v along the profile (metres / texture)."""
        if detail and not self.detail:
            return
        cell = cell or self.cell
        segs = segs if self.detail else max(6, segs // 2)
        rot = _unity_rotation(pitch, yaw, roll)
        c = Vector(centre)
        info = MATERIALS[mat]
        size = SIZES[info["tex"]] if info["tex"] else (1.0, 1.0)
        cr, sr = math.cos(math.radians(info["rot"])), math.sin(math.radians(info["rot"]))
        mi = self._mat(mat)
        # Subdivide the profile so the baked AO has vertices along it.
        pts = [profile[0]]
        for (r0, y0), (r1, y1) in zip(profile, profile[1:]):
            n = max(1, math.ceil(math.hypot(r1 - r0, y1 - y0) / cell - 0.01))
            pts += [(r0 + (r1 - r0) * k / n, y0 + (y1 - y0) * k / n) for k in range(1, n + 1)]
        arc = [0.0]
        for (r0, y0), (r1, y1) in zip(pts, pts[1:]):
            arc.append(arc[-1] + math.hypot(r1 - r0, y1 - y0))
        first = len(self.bm.verts)

        def V(r, y, a):
            q = rot @ Vector((r * math.cos(a), y, r * math.sin(a)))
            return self.bm.verts.new(U(*(c + q)))

        rings = []
        for r, y in pts:
            rings.append([V(r, y, 0.0)] if r < 1e-6 else [V(r, y, 2 * math.pi * i / segs) for i in range(segs)])

        def face(vs, uvs):
            try:
                f = self.bm.faces.new(vs)
            except ValueError:
                return
            f.material_index = mi
            f[self.own_uv] = 1
            for loop, (u, v) in zip(f.loops, uvs):
                u, v = u / size[0], v / size[1]
                loop[self.uv].uv = (u * cr - v * sr, u * sr + v * cr)

        for j in range(len(pts) - 1):
            A, B = rings[j], rings[j + 1]
            ra, rb = max(pts[j][0], 1e-3), max(pts[j + 1][0], 1e-3)
            for i in range(segs):
                i1 = (i + 1) % segs
                ua0, ua1 = 2 * math.pi * ra * i / segs, 2 * math.pi * ra * (i + 1) / segs
                ub0, ub1 = 2 * math.pi * rb * i / segs, 2 * math.pi * rb * (i + 1) / segs
                if len(A) == 1:
                    face((A[0], B[i1], B[i]), [((ub0 + ub1) * 0.5, arc[j]), (ub1, arc[j + 1]), (ub0, arc[j + 1])])
                elif len(B) == 1:
                    face((A[i], A[i1], B[0]), [(ua0, arc[j]), (ua1, arc[j]), ((ua0 + ua1) * 0.5, arc[j + 1])])
                else:
                    face((A[i], A[i1], B[i1], B[i]), [(ua0, arc[j]), (ua1, arc[j]), (ub1, arc[j + 1]), (ub0, arc[j + 1])])
        for ring, (r, y) in ((rings[0], pts[0]), (rings[-1], pts[-1])):   # caps
            if len(ring) > 1:
                mid = V(0.0, y, 0.0)
                for i in range(segs):
                    a0, a1 = 2 * math.pi * i / segs, 2 * math.pi * (i + 1) / segs
                    face((ring[i], ring[(i + 1) % segs], mid),
                         [(r * math.cos(a0), r * math.sin(a0)), (r * math.cos(a1), r * math.sin(a1)), (0.0, 0.0)])
        self._weld(first)

    def prism(self, profile, z0, z1, mat, cell=None, detail=False):
        """A convex polygon in the Unity X-Y plane (`profile`, counter-clockwise seen from +Z) extruded from z0 to z1: ramps,
        mounds, a silage heap. Every side is gridded for the baked AO; the ends are fans."""
        if detail and not self.detail:
            return
        first = len(self.bm.verts)
        n = len(profile)
        for i in range(n):
            (xa, ya), (xb, yb) = profile[i], profile[(i + 1) % n]
            self._grid(U(xa, ya, z0), U(xb, yb, z0), U(xa, ya, z1), mat, cell)
        cx = sum(p[0] for p in profile) / n
        cy = sum(p[1] for p in profile) / n
        mi = self._mat(mat)
        for z in (z0, z1):
            mid = self.bm.verts.new(U(cx, cy, z))
            ring = [self.bm.verts.new(U(x, y, z)) for x, y in profile]
            for i in range(n):
                self.bm.faces.new((ring[i], ring[(i + 1) % n], mid)).material_index = mi
        self._weld(first)

    def heightfield(self, xs, zs, fn, mat, detail=False):
        """Ground-like surface y = fn(x, z) over the grid lines xs x zs (Unity coords; put grid lines on every crease so the
        slopes stay exact), normals up, open underneath (edges should sink below the ground)."""
        if detail and not self.detail:
            return
        first = len(self.bm.verts)
        vs = [[self.bm.verts.new(U(x, fn(x, z), z)) for x in xs] for z in zs]
        mi = self._mat(mat)
        for j in range(len(zs) - 1):
            for i in range(len(xs) - 1):
                f = self.bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))
                f.material_index = mi
                f[self.keep] = 1
                f.normal_update()
                if f.normal.z < 0:
                    f.normal_flip()
        self._weld(first)

    @staticmethod
    def lines(breaks, cell):
        """Grid lines through every value in `breaks` (sorted), with extra lines at most `cell` apart between them."""
        out = [breaks[0]]
        for a, b in zip(breaks, breaks[1:]):
            n = max(1, math.ceil((b - a) / cell - 0.01))
            out += [a + (b - a) * k / n for k in range(1, n + 1)]
        return out

    def slope_collider(self, p0, p1, z0, z1, thick=0.3):
        """BoxCollider whose top face runs along the line p0 -> p1 (Unity X-Y, p0.x < p1.x) from z0 to z1: a ramp the wheel rays ride on."""
        (xa, ya), (xb, yb) = p0, p1
        a = math.atan2(yb - ya, xb - xa)
        nx, ny = -math.sin(a), math.cos(a)
        mx, my = (xa + xb) * 0.5 - nx * thick * 0.5, (ya + yb) * 0.5 - ny * thick * 0.5
        self.collider((mx, my, (z0 + z1) * 0.5), (math.hypot(xb - xa, yb - ya), thick, z1 - z0), roll=math.degrees(a))

    def bar(self, p0, p1, size, mat, cell=None, detail=False):
        """Rectangular bar from p0 to p1 (Unity coords), cross-section size = (width, height) before it is turned onto the
        line (width stays horizontal unless the line is vertical)."""
        if detail and not self.detail:
            return
        d = Vector(p1) - Vector(p0)
        length = d.length
        d.normalize()
        pitch = -math.degrees(math.asin(max(-1.0, min(1.0, d.y))))
        yaw = math.degrees(math.atan2(d.x, d.z))
        c = (Vector(p0) + Vector(p1)) * 0.5
        self.box(tuple(c), (size[0], size[1], length), mat, yaw=yaw, pitch=pitch, cell=cell)

    def sheet(self, rows, mat, thick=0.008, detail=False):
        """A curved plate from a grid of Unity points (rows[j][i]), `thick` thick (offset along the surface normal): mouldboards,
        mudguards, hoods. Closed (both faces and the rim)."""
        if detail and not self.detail:
            return
        nj, ni = len(rows), len(rows[0])
        P = [[Vector(p) for p in r] for r in rows]

        def normal(j, i):
            a = P[j][min(i + 1, ni - 1)] - P[j][max(i - 1, 0)]
            b = P[min(j + 1, nj - 1)][i] - P[max(j - 1, 0)][i]
            n = a.cross(b)
            return n.normalized() if n.length > 1e-9 else Vector((0, 1, 0))

        first = len(self.bm.verts)
        top = [[self.bm.verts.new(U(*(P[j][i] + normal(j, i) * thick * 0.5))) for i in range(ni)] for j in range(nj)]
        bot = [[self.bm.verts.new(U(*(P[j][i] - normal(j, i) * thick * 0.5))) for i in range(ni)] for j in range(nj)]
        mi = self._mat(mat)

        def quad(vs):
            self.bm.faces.new(vs).material_index = mi

        for j in range(nj - 1):
            for i in range(ni - 1):
                quad((top[j][i], top[j][i + 1], top[j + 1][i + 1], top[j + 1][i]))
                quad((bot[j][i], bot[j + 1][i], bot[j + 1][i + 1], bot[j][i + 1]))
        for i in range(ni - 1):
            for j in (0, nj - 1):
                quad((top[j][i], bot[j][i], bot[j][i + 1], top[j][i + 1]))
        for j in range(nj - 1):
            for i in (0, ni - 1):
                quad((top[j][i], top[j + 1][i], bot[j + 1][i], bot[j][i]))
        self._weld(first)

    def tyre(self, centre, radius, width, mat_rubber, lugs=20, lug_h=0.03, segs=24, detail_lugs=True):
        """Agricultural tyre around the local X axis at `centre` (Unity): rounded carcass (revolve) and bar lugs staggered left /
        right across the tread (Full variant only when detail_lugs)."""
        lugs_here = self.detail or not detail_lugs
        r, hw = (radius - lug_h if lugs_here else radius), width * 0.5   # no lugs: the carcass takes the full radius
        rim_r = radius * 0.62
        prof = [(rim_r, -hw * 0.9), (r - 0.06, -hw), (r - 0.015, -hw * 0.94), (r, -hw * 0.75), (r, hw * 0.75),
                (r - 0.015, hw * 0.94), (r - 0.06, hw), (rim_r, hw * 0.9)]
        self.revolve(centre, prof, mat_rubber, segs=segs, roll=90)
        if detail_lugs and not self.detail:
            return
        cx, cy, cz = centre
        for k in range(lugs):
            for side in (-1, 1):
                a = 2 * math.pi * (k + (0.5 if side > 0 else 0.0)) / lugs
                rr = r + lug_h * 0.5
                self.box((cx + side * hw * 0.42, cy + rr * math.cos(a), cz + rr * math.sin(a)),
                         (hw * 0.9, lug_h + 0.01, width * 0.12), mat_rubber, pitch=math.degrees(a), cell=1.0)

    def tube(self, p0, p1, radius, mat, segs=10, detail=False):
        """Round bar / pipe from p0 to p1 (Unity coords), closed ends."""
        d = Vector(p1) - Vector(p0)
        length = d.length
        d.normalize()
        pitch = math.degrees(math.acos(max(-1.0, min(1.0, d.y))))
        yaw = math.degrees(math.atan2(d.x, d.z))
        self.revolve(tuple(Vector(p0)), [(radius, 0.0), (radius, length)], mat, segs=segs, pitch=pitch, yaw=yaw,
                     detail=detail)

    def heap(self, centre, sx, sz, height, mat, seed=1, detail=False):
        """A heap (muck, soil): a gridded height field over sx x sz, highest in the middle, edges just below `centre`'s
        height (so they sink into the floor). Open underneath; normals up."""
        if detail and not self.detail:
            return
        n = max(4, math.ceil(max(sx, sz) / (self.cell * 0.5)))
        cx, cy, cz = centre
        first = len(self.bm.verts)

        def h(u, v):
            d = min(1.0, math.hypot(u, v))
            bump = (1.0 - d * d) ** 0.8
            noise = (math.sin(u * 7.1 + seed) * math.cos(v * 5.3 + 2 * seed) + 0.5 * math.sin(u * 13.7 - v * 11.1 + seed)) * 0.07
            return height * bump * (1.0 + noise) - 0.04

        vs = [[self.bm.verts.new(U(cx + sx * 0.5 * u, cy + h(u, v), cz + sz * 0.5 * v))
               for u in (2 * i / n - 1 for i in range(n + 1))] for v in (2 * j / n - 1 for j in range(n + 1))]
        mi = self._mat(mat)
        for j in range(n):
            for i in range(n):
                f = self.bm.faces.new((vs[j][i], vs[j][i + 1], vs[j + 1][i + 1], vs[j + 1][i]))
                f.material_index = mi
                f[self.keep] = 1
                f.normal_update()
                if f.normal.z < 0:      # Blender Z = Unity Y: face up
                    f.normal_flip()
        self._weld(first)

    def blob(self, centre, radii, mat, seed=0, rough=0.28, subdiv=None, flat=0.35, detail=False):
        """Lumpy, smooth-shaded ellipsoid (a tree crown's leaf mass): an icosphere with radii (x, y, z, Unity), pushed in and out
        by noise (`rough` = share of the radius), its underside flattened by `flat`. Full: 3 subdivisions (320 faces), Light: 2
        (80), unless `subdiv` = (full, light)."""
        from mathutils import noise
        if detail and not self.detail:
            return
        sub = (subdiv or (3, 2))[0 if self.detail else 1]
        tmp = bmesh.new()
        bmesh.ops.create_icosphere(tmp, subdivisions=sub, radius=1.0)
        off = Vector((seed * 7.31, seed * 3.17, seed * 5.53))
        mi = self._mat(mat)
        first = len(self.bm.verts)
        vs = []
        for v in tmp.verts:
            d = v.co.normalized()                  # Blender axes here are only a direction on the unit sphere
            k = 1.0 + rough * noise.noise(d * 1.7 + off) + rough * 0.5 * noise.noise(d * 4.1 + off)
            x, y, z = d.x * radii[0] * k, d.z * radii[1] * k, d.y * radii[2] * k
            if y < 0:
                y *= 1.0 - flat
            vs.append(self.bm.verts.new(U(centre[0] + x, centre[1] + y, centre[2] + z)))
        for f in tmp.faces:
            nf = self.bm.faces.new([vs[v.index] for v in f.verts])
            nf.material_index = mi
            nf[self.own_uv] = 1
            nf[self.smooth] = 1
        tmp.free()
        self._weld(first)

    def _weld(self, first):
        """Merge the vertices of the piece just added (from index `first`), never across pieces."""
        self.bm.verts.ensure_lookup_table()
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts[first:], dist=1e-4)

    def finish(self, name):
        """Normals outward per connected piece, UVs, then an object with the material slots."""
        self.bm.normal_update()
        # Per connected piece. Text keeps the font's winding: the mapping (mirror across X, then U()) is a rotation, so
        # the letters' front faces still face out, and the font fill confuses the recalculation (letters turned inside out).
        bmesh.ops.recalc_face_normals(self.bm, faces=[f for f in self.bm.faces if not f[self.keep]])
        for f in self.bm.faces:
            if not f[self.own_uv]:
                self._uv(f)
        smooth = [bool(f[self.smooth]) for f in self.bm.faces]
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        for m in self.mats:
            me.materials.append(_material(m))
        for p in me.polygons:
            p.use_smooth = smooth[p.index]
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


def _unity_rotation(pitch, yaw, roll):
    """Unity's Quaternion.Euler(pitch, yaw, roll) as a 3x3 for Unity-space vectors: Z first, then X, then Y.
    Its matrices are the textbook ones (Euler(0, 90, 0) turns +Z into +X, Euler(0, 0, 90) turns +X into +Y)."""
    from mathutils import Matrix
    rx, ry, rz = (math.radians(a) for a in (pitch, yaw, roll))
    return Matrix.Rotation(ry, 3, "Y") @ Matrix.Rotation(rx, 3, "X") @ Matrix.Rotation(rz, 3, "Z")


def _material(name):
    """Blender preview material: the albedo texture through the UVs, so `look` shows what Unity will show."""
    m = bpy.data.materials.get(name)
    if m:
        return m
    info = MATERIALS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Roughness"].default_value = 1.0 - info["smooth"]
    if not info["tex"]:
        bsdf.inputs["Base Color"].default_value = (*info["tint"], 1.0)
        return m
    img = bpy.data.images.load(str(TEX_DIR / f"{info['tex']}_diff.jpg"), check_existing=True)
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    mix = nt.nodes.new("ShaderNodeMix")
    mix.data_type = "RGBA"
    mix.blend_type = "MULTIPLY"
    mix.inputs["Factor"].default_value = 1.0
    nt.links.new(tex.outputs["Color"], mix.inputs[6])
    mix.inputs[7].default_value = (*info["tint"], 1.0)
    nt.links.new(mix.outputs[2], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 1.0 - info["smooth"]
    return m


# ---------------------------------------------------------------- scene, AO, export

def reset():
    """Empty working scene. In a running Blender: a scene of its own ("TVR_Build"), the user's scenes stay
    untouched. Headless (-b --factory-startup, no window): the throwaway startup scene, default cube included."""
    if bpy.context.window:
        sc = bpy.data.scenes.get("TVR_Build") or bpy.data.scenes.new("TVR_Build")
        bpy.context.window.scene = sc
    else:
        sc = bpy.context.scene
    for ob in list(sc.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for me in [m for m in bpy.data.meshes if m.users == 0]:
        bpy.data.meshes.remove(me)
    return sc


def bake_ao(ob, distance=2.5, samples=64, ground=True):
    """Cycles AO into the corner colour attribute "AO" (stored in red/green/blue, read as red in Unity).
    A big ground plane takes part as an occluder, so walls darken where they meet the ground."""
    sc = bpy.context.scene
    ground_me = bpy.data.meshes.new("_ao_ground")
    s = 200.0
    z = 0.0 if ground else -1000.0      # parts not standing on the ground in their own space: the plane goes far away
    ground_me.from_pydata([(-s, -s, z), (s, -s, z), (s, s, z), (-s, s, z)], [], [(0, 1, 2, 3)])
    ground = bpy.data.objects.new("_ao_ground", ground_me)
    sc.collection.objects.link(ground)

    attr = ob.data.color_attributes.new("AO", "BYTE_COLOR", "CORNER")
    ob.data.color_attributes.active_color = attr
    ob.data.attributes.active_color = attr

    # Bake alone: the other variants stand at the same origin and would occlude it completely.
    hidden = [o for o in sc.objects if o.type == "MESH" and o not in (ob, ground) and not o.hide_render]
    for o in hidden:
        o.hide_render = True
    prev_engine = sc.render.engine
    sc.render.engine = "CYCLES"
    sc.cycles.samples = samples
    sc.cycles.device = "CPU"
    if sc.world is None:
        sc.world = bpy.data.worlds.new("TVR_World")
    sc.world.light_settings.distance = distance
    for o in sc.objects:
        o.select_set(o == ob)
    bpy.context.view_layer.objects.active = ob
    bpy.ops.object.bake(type="AO", target="VERTEX_COLORS")
    sc.render.engine = prev_engine
    for o in hidden:
        o.hide_render = False

    bpy.data.objects.remove(ground, do_unlink=True)
    bpy.data.meshes.remove(ground_me)
    vals = [c.color[0] for c in attr.data]
    return sum(vals) / max(1, len(vals))


def stats(ob):
    me = ob.data
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    bb = [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    lo = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb)))
    hi = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
    size = hi - lo
    return dict(triangles=tris, vertices=len(me.vertices), materials=[m.name for m in me.materials],
                size_unity=[round(size.x, 3), round(size.z, 3), round(size.y, 3)])


def export(asset, objects, notes="", colliders=(), key=None):
    """FBX with both variants + a JSON with their numbers (read by ProjectSetup's report and the DEVLOG)."""
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    for o in bpy.context.scene.objects:
        o.select_set(o in objects)
    path = OUT_DIR / f"{asset}.fbx"
    bpy.ops.export_scene.fbx(
        filepath=str(path), use_selection=True, object_types={"MESH"},
        apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS", bake_space_transform=True,
        axis_forward="-Z", axis_up="Y", mesh_smooth_type="FACE", use_mesh_modifiers=True,
        colors_type="LINEAR", prioritize_active_color=True, add_leaf_bones=False, bake_anim=False,
        path_mode="STRIP", use_custom_props=False)
    key = key or (lambda o: o.name.split("_")[-1])
    info = {"asset": asset, "notes": notes, "variants": {key(o): stats(o) for o in objects}}
    if colliders:
        info["colliders"] = list(colliders)
    (OUT_DIR / f"{asset}.json").write_text(json.dumps(info, indent=2) + "\n")
    mats = {k: dict(tex=v["tex"], tint=list(v["tint"]), smoothness=v["smooth"]) for k, v in MATERIALS.items()}
    (OUT_DIR / "env_materials.json").write_text(json.dumps(
        {"materials": [dict(name=k, **v) for k, v in mats.items()]}, indent=2) + "\n")
    print("exported", path, json.dumps(info["variants"]))
    return info


def build_parts(asset, parts, notes="", no_ground=(), ao_distance=1.0):
    """Asset made of several meshes, each modelled in the space of the pivot it hangs on in Unity (an implement's frame, a wheel's
    spin pivot, a tipping body's hinge ...): `parts` = {part: describe}. Objects <Asset>_<Part>_Full / _Light. Parts in `no_ground`
    bake their AO without the ground plane (they are not standing at y = 0 in their own space)."""
    reset()
    objects = []
    for vname, v in VARIANTS.items():
        for part, describe in parts.items():
            b = Builder(v["cell"], v["detail"])
            describe(b)
            if not b.bm.faces:
                b.bm.free()
                continue
            ob = b.finish(f"{asset}_{part}_{vname}")
            mean = bake_ao(ob, distance=ao_distance, samples=48 if vname == "Full" else 24, ground=part not in no_ground)
            print(f"{ob.name}: mean AO {mean:.2f}")
            objects.append(ob)
    info = export(asset, objects, notes, key=lambda o: o.name[len(asset) + 1:])
    for k, o in enumerate(objects):
        o.location.x = -(k % len(parts)) * 4.0
        o.location.y = (k // len(parts)) * 8.0
    return info


def build_asset(asset, describe, notes="", ao_distance=2.5):
    """Run `describe(builder)` for each variant, bake AO, export. Returns the export info."""
    reset()
    objects = []
    colliders = []
    for vname, v in VARIANTS.items():
        b = Builder(v["cell"], v["detail"])
        describe(b)
        colliders += b.colliders
        ob = b.finish(f"{asset}_{vname}")
        mean = bake_ao(ob, distance=ao_distance, samples=64 if vname == "Full" else 32)
        print(f"{ob.name}: mean AO {mean:.2f}")
        objects.append(ob)
    # Side by side in the Blender scene for `look` (export uses the origin: both variants are moved back first).
    info = export(asset, objects, notes, colliders)
    for k, o in enumerate(objects):
        o.location.x = -k * 30.0
    return info
