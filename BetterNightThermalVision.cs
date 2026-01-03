using BepInEx;
using BepInEx.Configuration;
using BSG.CameraEffects;
using EFT.CameraControl;
using HarmonyLib;

namespace BetterVision
{
    [BepInPlugin("ciallo.BetterNightThermalVision", "Better Night Thermal Vision", "2.0.0")]
    public class BetterVision : BaseUnityPlugin
    {
        internal static ConfigEntry<bool> ScopeFps;
        internal static ConfigEntry<bool> ScopeGlitch;
        internal static ConfigEntry<bool> ScopeMotion;
        internal static ConfigEntry<bool> ScopeNoise;
        internal static ConfigEntry<bool> ScopePixel;
        internal static ConfigEntry<bool> ScopeChromatic;

        internal static ConfigEntry<bool> T7Fps;
        internal static ConfigEntry<bool> T7Glitch;
        internal static ConfigEntry<bool> T7Motion;
        internal static ConfigEntry<bool> T7Noise;
        internal static ConfigEntry<bool> T7Pixel;

        internal static ConfigEntry<bool> NVNoise;

        internal static ConfigEntry<bool> NVT7Mask;

        private void Awake()
        {
            ScopeFps = Config.Bind("Thermal Optic", "FPS Limit", false);
            ScopeGlitch = Config.Bind("Thermal Optic", "Glitch Refresh", false);
            ScopeMotion = Config.Bind("Thermal Optic", "Motion Blur", false);
            ScopeNoise = Config.Bind("Thermal Optic", "Noise", false);
            ScopePixel = Config.Bind("Thermal Optic", "Pixelation", false);
            ScopeChromatic = Config.Bind("Thermal Optic", "Chromatic Aberration", false);

            T7Fps = Config.Bind("T7 Thermal", "FPS Limit", false);
            T7Glitch = Config.Bind("T7 Thermal", "Glitch Refresh", false);
            T7Motion = Config.Bind("T7 Thermal", "Motion Blur", false);
            T7Noise = Config.Bind("T7 Thermal", "Noise", false);
            T7Pixel = Config.Bind("T7 Thermal", "Pixelation", false);

            NVNoise = Config.Bind("Night Vision", "Noise", false);

            NVT7Mask = Config.Bind("Black Mask", "Helmet NV & T7 Mask", false);

            new Harmony("ciallo.BetterNightThermalVision").PatchAll();
        }
    }

    [HarmonyPatch(typeof(OpticComponentUpdater), "CopyComponentFromOptic")]
    public class Patch_OpticThermal
    {
        static void Postfix(OpticComponentUpdater __instance)
        {
            ThermalVision tv = __instance.GetComponent<ThermalVision>();
            if (tv == null)
                return;

            tv.IsFpsStuck = BetterVision.ScopeFps.Value;
            tv.IsGlitch = BetterVision.ScopeGlitch.Value;
            tv.IsMotionBlurred = BetterVision.ScopeMotion.Value;
            tv.IsNoisy = BetterVision.ScopeNoise.Value;
            tv.IsPixelated = BetterVision.ScopePixel.Value;

            tv.SetMaterialProperties();

            ChromaticAberration ca = tv.GetComponent<ChromaticAberration>();
            if (ca != null)
            {
                ca.Shift = BetterVision.ScopeChromatic.Value
                    ? tv.ChromaticAberrationThermalShift
                    : 0f;
            }
        }
    }

    [HarmonyPatch(typeof(PlayerCameraController), "method_5")]
    public class Patch_T7Thermal
    {
        static void Postfix()
        {
            ThermalVision tv = CameraClass.Instance.ThermalVision;
            if (tv == null)
                return;

            tv.IsFpsStuck = BetterVision.T7Fps.Value;
            tv.IsGlitch = BetterVision.T7Glitch.Value;
            tv.IsMotionBlurred = BetterVision.T7Motion.Value;
            tv.IsNoisy = BetterVision.T7Noise.Value;
            tv.IsPixelated = BetterVision.T7Pixel.Value;

            tv.SetMaterialProperties();
        }
    }

    [HarmonyPatch(typeof(NightVision), "ApplySettings")]
    public class Patch_NV_Noise
    {
        static void Postfix(NightVision __instance)
        {
            if (BetterVision.NVNoise.Value)
                return;

            __instance.NoiseIntensity = 0f;
            __instance.NoiseScale = 0f;
        }
    }


    [HarmonyPatch(typeof(ThermalVision), "OnPreCull")]
    public class Patch_Mask
    {
        static void Prefix(ThermalVision __instance)
        {
            if (BetterVision.NVT7Mask.Value)
                return;

            if (__instance.TextureMask != null)
                __instance.TextureMask.enabled = false;
        }
    }
}
