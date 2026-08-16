using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Quality settings the game leaves on the table.
    ///
    /// AWAY ships two quality levels, Low and High, and runs on High. Reading that preset
    /// shows what is and is not worth touching: texture quality is already at full
    /// resolution, soft particles and camera-facing billboards are already on, so there is
    /// nothing to win there. What it does leave low are the shadow cascades, the shadow map
    /// resolution and the anisotropic filtering mode.
    ///
    /// One thing is NOT available and it shapes everything else: the game renders DEFERRED
    /// (renderingPath 3 on all three tiers), and MSAA does not exist in deferred — Unity
    /// ignores QualitySettings.antiAliasing entirely. That is why the preset ships with
    /// antiAliasing at 0 on both levels: it is not an oversight. The only anti-aliasing
    /// available to us is supersampling through XRSettings.eyeTextureResolutionScale, which
    /// is why that setting carries most of the visual improvement on its own.
    ///
    /// Re-applied rather than set once: QualitySettings are global, but the game calls
    /// SetQualityLevel of its own accord and that resets every one of them.
    /// </summary>
    internal static class Visuals
    {
        public static void Apply(bool log)
        {
            // ForceEnable, not Enable. The preset already says Enable, which only honours the
            // per-texture flag — and the game's textures largely do not set it. Forcing it is
            // what actually sharpens floors and walls seen at a grazing angle, and it is close
            // to free on any GPU that can drive a headset at all.
            if (Plugin.CfgAnisotropic.Value)
            {
                if (QualitySettings.anisotropicFiltering != AnisotropicFiltering.ForceEnable)
                    QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
                Texture.SetGlobalAnisotropicFilteringLimits(9, 16);
            }

            // More cascades at the same shadow distance means the near ones cover far less
            // ground each, so contact shadows tighten up. In a headset that is where the eye
            // goes: you stand in the scene rather than looking at it from across the room.
            int cascades = Plugin.CfgShadowCascades.Value;
            if (QualitySettings.shadowCascades != cascades)
                QualitySettings.shadowCascades = cascades;

            var res = Plugin.CfgShadowResolution.Value;
            if (QualitySettings.shadowResolution != res)
                QualitySettings.shadowResolution = res;

            float lod = Plugin.CfgLodBias.Value;
            if (!Mathf.Approximately(QualitySettings.lodBias, lod))
                QualitySettings.lodBias = lod;

            if (log)
                Plugin.Log.LogInfo("  visuals: aniso=" + QualitySettings.anisotropicFiltering
                                   + " cascades=" + QualitySettings.shadowCascades
                                   + " shadowRes=" + QualitySettings.shadowResolution
                                   + " lodBias=" + QualitySettings.lodBias.ToString("0.#")
                                   + " shadowDist=" + QualitySettings.shadowDistance.ToString("0"));
        }
    }
}
