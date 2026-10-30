using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Graphics tier of the running build: the quality level "Quest" (Android default) or "PC" (Windows default: PC VR over Link, desktop).
/// Setup makes the two levels, each with its own URP asset (MSAA, render scale, shadow map). This component applies the rest:
/// on PC the tractor model casts its own shadows and the low-poly <c>ShadowProxy</c> is switched off.
/// Environment assets (phase 5) read their budget from <see cref="Current"/>: draw distance, vegetation density.
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
    }

    public static readonly Budget QuestBudget = new Budget { detailDistance = 120f, vegetationDensity = 0.35f, terrainShadows = false };
    public static readonly Budget PCBudget = new Budget { detailDistance = 400f, vegetationDensity = 1f, terrainShadows = true };

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
    }
}
