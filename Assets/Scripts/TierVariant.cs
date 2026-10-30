using UnityEngine;

/// <summary>
/// Marks one variant of an environment asset (phase 5, tools/blender/): the Full mesh for the PC tier or the Light one for
/// the Quest tier. Both are in the scene; <see cref="QualityTier.Apply"/> keeps only the current tier's variant active.
/// </summary>
public class TierVariant : MonoBehaviour
{
    public QualityTier.Tier tier;
}
