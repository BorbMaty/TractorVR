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
            self._uv(f)
        me = bpy.data.meshes.new(name)
        self.bm.to_mesh(me)
        self.bm.free()
        for m in self.mats:
            me.materials.append(_material(m))
        for p in me.polygons:
            p.use_smooth = False
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


def bake_ao(ob, distance=2.5, samples=64):
    """Cycles AO into the corner colour attribute "AO" (stored in red/green/blue, read as red in Unity).
    A big ground plane takes part as an occluder, so walls darken where they meet the ground."""
    sc = bpy.context.scene
    ground_me = bpy.data.meshes.new("_ao_ground")
    s = 200.0
    ground_me.from_pydata([(-s, -s, 0), (s, -s, 0), (s, s, 0), (-s, s, 0)], [], [(0, 1, 2, 3)])
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


def export(asset, objects, notes=""):
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
    info = {"asset": asset, "notes": notes, "variants": {o.name.split("_")[-1]: stats(o) for o in objects}}
    (OUT_DIR / f"{asset}.json").write_text(json.dumps(info, indent=2) + "\n")
    mats = {k: dict(tex=v["tex"], tint=list(v["tint"]), smoothness=v["smooth"]) for k, v in MATERIALS.items()}
    (OUT_DIR / "env_materials.json").write_text(json.dumps(
        {"materials": [dict(name=k, **v) for k, v in mats.items()]}, indent=2) + "\n")
    print("exported", path, json.dumps(info["variants"]))
    return info


def build_asset(asset, describe, notes="", ao_distance=2.5):
    """Run `describe(builder)` for each variant, bake AO, export. Returns the export info."""
    reset()
    objects = []
    for vname, v in VARIANTS.items():
        b = Builder(v["cell"], v["detail"])
        describe(b)
        ob = b.finish(f"{asset}_{vname}")
        mean = bake_ao(ob, distance=ao_distance, samples=64 if vname == "Full" else 32)
        print(f"{ob.name}: mean AO {mean:.2f}")
        objects.append(ob)
    # Side by side in the Blender scene for `look` (export uses the origin: both variants are moved back first).
    info = export(asset, objects, notes)
    for k, o in enumerate(objects):
        o.location.x = -k * 30.0
    return info
