using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rear-tipping trailer body, worked from the tractor's hydraulics (remote lever 3, panel TIP buttons) while coupled.
///   Body ...... turns on a hinge at the rear of the chassis; a telescopic ram in front of the headboard pushes the front up.
///   Tailgate .. hinged at its top edge, hangs plumb, so it swings open as the body rises.
///   Load ...... starts full; above ~20° it pours out over the tailgate into a heap on the ground behind the trailer.
///               The trailer's mass and centre of mass follow the load and the body angle.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class Tipper : MonoBehaviour
{
    public Implement implement;
    public Transform body;                  // hinge at the rear of the chassis, turns around local X
    public Transform tailgate;              // hinge at the top of the rear wall (child of the body)
    public Transform ram;                   // at the ram's base pin on the drawbar; its stages are children along +Z
    public Vector3 ramPinOnBody;            // ram top pin in body space
    public Transform[] ramStages = new Transform[0];
    public float stageLength = 0.85f;
    public Transform cargo;                 // load heap inside the body (scaled with the load), pivot on the floor
    public Material heapMaterial;
    public float maxAngle = 45f;
    public float raiseRate = 7f, lowerRate = 12f;   // degrees per second
    public float emptyMass = 2500f, bodyMass = 1200f;
    public Vector3 chassisCom = new Vector3(0f, 0.75f, -3.6f);   // trailer space
    public Vector3 bodyCom = new Vector3(0f, 0.45f, 2.2f);       // body hinge space (empty body)
    public Vector3 loadCom = new Vector3(0f, 0.35f, 2.2f);       // body hinge space (load)
    public float capacityKg = 3000f, startLoadKg = 3000f;
    public float pourTime = 4f;             // s to empty a full load at full pour
    public float density = 1500f;           // kg/m³ (soil)

    public float Angle { get; private set; }
    public float LoadKg { get; private set; }
    public float Share => Angle / maxAngle;

    Rigidbody rb;
    Transform heap;
    float heapKg;
    readonly List<Transform> heaps = new List<Transform>();
    static Mesh moundMesh;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        LoadKg = startLoadKg;
        Apply();
    }

    /// <summary>Raise (+1) / lower (-1) the body for dt (called by the Hitch while coupled).</summary>
    public void Move(float direction, float dt)
    {
        float rate = direction > 0f ? raiseRate : lowerRate;
        Angle = Mathf.Clamp(Angle + direction * rate * dt, 0f, maxAngle);
        Apply();
    }

    void Update() => FrameUpdate(Time.deltaTime);

    /// <summary>Pouring (public for the editor physics test).</summary>
    public void FrameUpdate(float dt)
    {
        float pour = Mathf.InverseLerp(20f, 40f, Angle);
        if (pour <= 0f || LoadKg <= 0f) { heap = null; return; }
        float kg = Mathf.Min(LoadKg, capacityKg / pourTime * pour * dt);
        LoadKg -= kg;
        Pile(kg);
        Apply();
    }

    // Everything that follows the body angle and the load: body, tailgate, ram, load heap, mass and centre of mass.
    void Apply()
    {
        if (body == null) return;
        body.localRotation = Quaternion.AngleAxis(-Angle, Vector3.right);   // front up
        if (tailgate) tailgate.localRotation = Quaternion.AngleAxis(Angle, Vector3.right);   // hangs plumb
        if (cargo)
        {
            float f = LoadKg / Mathf.Max(1f, capacityKg);
            cargo.gameObject.SetActive(f > 0.01f);
            cargo.localScale = new Vector3(1f, Mathf.Max(0.05f, f), Mathf.Lerp(0.6f, 1f, f));
        }
        if (ram)
        {
            Vector3 pin = transform.InverseTransformPoint(body.TransformPoint(ramPinOnBody)) - ram.localPosition;
            ram.localRotation = Quaternion.LookRotation(pin, Vector3.up);
            // Stages spread evenly from the base to the pin: collapsed they nest, extended they overlap a little.
            float step = ramStages.Length > 1 ? Mathf.Max(0f, pin.magnitude - stageLength) / (ramStages.Length - 1) : 0f;
            for (int i = 0; i < ramStages.Length; i++) ramStages[i].localPosition = new Vector3(0f, 0f, i * step);
        }
        float mass = emptyMass + LoadKg;
        if (implement) implement.mass = mass;
        if (rb)
        {
            rb.mass = mass;
            Vector3 b = transform.InverseTransformPoint(body.TransformPoint(bodyCom));
            Vector3 l = transform.InverseTransformPoint(body.TransformPoint(loadCom));
            rb.centerOfMass = (chassisCom * (emptyMass - bodyMass) + b * bodyMass + l * LoadKg) / mass;
        }
    }

    // The load lands about 0.8 m behind the tailgate and grows into a mound (repose ~25°); a new mound when the trailer has moved on.
    void Pile(float kg)
    {
        Vector3 drop = body.TransformPoint(new Vector3(0f, 0f, -0.2f)) - transform.forward * 0.8f;
        if (heap == null || Vector3.Distance(Flat(heap.position), Flat(drop)) > 1.5f)
        {
            var go = new GameObject("SoilHeap");
            go.AddComponent<MeshFilter>().sharedMesh = MoundMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = heapMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            heap = go.transform;
            heapKg = 0f;
            heaps.Add(heap);
        }
        heapKg += kg;
        // Mound volume ≈ 0.707 r³ for this shape (height 0.45 r).
        float radius = Mathf.Pow(heapKg / density / 0.707f, 1f / 3f);
        Vector3 p = Flat(drop);
        heap.position = new Vector3(p.x, TerrainGenerator.Height(p.x, p.z) - 0.03f, p.z);
        heap.localScale = Vector3.one * radius;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    /// <summary>Unit mound: radius 1, height 0.45, rounded top (also used for the load inside the body).</summary>
    public static Mesh MoundMesh => moundMesh ? moundMesh : moundMesh = Mound(1f, 1f, 0.45f, 16, 6);

    /// <summary>Elliptic mound with a rounded top: half-widths rx (x) and rz (z), height h.</summary>
    public static Mesh Mound(float rx, float rz, float h, int sectors, int rings)
    {
        var v = new List<Vector3> { new Vector3(0f, h, 0f) };
        var t = new List<int>();
        for (int r = 1; r <= rings; r++)
        {
            float f = r / (float)rings;
            float y = h * (1f - f * f) * (1f - 0.15f * f);
            for (int s = 0; s < sectors; s++)
            {
                float a = s * Mathf.PI * 2f / sectors;
                v.Add(new Vector3(Mathf.Cos(a) * rx * f, r == rings ? 0f : y, Mathf.Sin(a) * rz * f));
            }
        }
        for (int s = 0; s < sectors; s++) { t.Add(0); t.Add(1 + (s + 1) % sectors); t.Add(1 + s); }
        for (int r = 1; r < rings; r++)
        {
            int a0 = 1 + (r - 1) * sectors, b0 = 1 + r * sectors;
            for (int s = 0; s < sectors; s++)
            {
                int s1 = (s + 1) % sectors;
                t.AddRange(new[] { a0 + s, a0 + s1, b0 + s, a0 + s1, b0 + s1, b0 + s });
            }
        }
        var m = new Mesh { name = "Mound" };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}
