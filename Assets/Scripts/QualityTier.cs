using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Graphics tier of the running build: the quality level "Quest" (Android default) or "PC" (Windows default: PC VR over Link, desktop).
/// Setup makes the two levels, each with its own URP asset (MSAA, render scale, shadow map). This component applies the rest:
/// on PC the tractor model casts its own shadows and the low-poly <c>ShadowProxy</c> is switched off.
/// Environment assets (phase 5) read their budget from <see cref="Current"/>: draw distance, vegetation density.
/// Blender assets come in two variants (<see cref="TierVariant"/>): only the current tier's stays active, and the
/// global keyword <c>ENV_NORMALS</c> turns on their normal maps (TractorVR/EnvLit) on PC.
/// </summary>
public class QualityTier : MonoBehaviour
{
    public enum Tier { Quest, PC }

    /// <summary>Budget per tier for generated / imported environment assets.</summary>
    public struct Budget
    {
        public float detailDistance;      // small props, grass, crop rows are culled beyond this (m)
        public float vegetationDensity;   // 0..1 share of instances placed
        public bool terrainShadows;       // hills / trees cast shadows
        public float grassDistance;       // standing grass / crop clumps are drawn up to this far (m), then sink into the ground
    }

    public static readonly Budget QuestBudget = new Budget { detailDistance = 120f, vegetationDensity = 0.5f, terrainShadows = false, grassDistance = 22f };
    public static readonly Budget PCBudget = new Budget { detailDistance = 400f, vegetationDensity = 1f, terrainShadows = true, grassDistance = 70f };

    public static Tier Current => QualitySettings.names[QualitySettings.GetQualityLevel()] == "PC" ? Tier.PC : Tier.Quest;
    public static Budget CurrentBudget => Current == Tier.PC ? PCBudget : QuestBudget;

    public Renderer shadowProxy;          // low-poly stand-in (Quest)
    public Renderer[] modelRenderers;     // the tractor model's renderers that would cast (PC)

    void Awake() => Apply(Current);

    public void Apply(Tier tier)
    {
        bool pc = tier == Tier.PC;
        if (shadowProxy != null) shadowProxy.enabled = !pc;
        foreach (var r in modelRenderers)
            if (r != null) r.shadowCastingMode = pc ? ShadowCastingMode.On : ShadowCastingMode.Off;
        ApplyEnvironment(tier);
    }

    public static void ApplyEnvironment(Tier tier)
    {
        if (tier == Tier.PC) Shader.EnableKeyword("ENV_NORMALS");
        else Shader.DisableKeyword("ENV_NORMALS");
        foreach (var v in FindObjectsByType<TierVariant>(FindObjectsInactive.Include))
            v.gameObject.SetActive(v.tier == tier);
    }
}
