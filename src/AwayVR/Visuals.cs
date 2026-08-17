using UnityEngine;

namespace AwayVR
{
    /// <summary>
    /// Quality settings the game's High preset leaves low: shadow cascades, shadow map
    /// resolution, anisotropic filtering. Note the game renders deferred, where MSAA does
    /// not exist - supersampling is the only anti-aliasing available.
    ///
    /// Re-applied rather than set once: the game calls SetQualityLevel on its own.
    /// </summary>
    internal static class Visuals
    {
        public static void Apply(bool log)
        {

            // ForceEnable, not Enable. The preset already says Enable, which only honours the
            // per-texture flag - and the game's textures largely do not set it. Forcing it is
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

            float dist = Plugin.CfgShadowDistance.Value;
            if (!Mathf.Approximately(QualitySettings.shadowDistance, dist))
                QualitySettings.shadowDistance = dist;

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
