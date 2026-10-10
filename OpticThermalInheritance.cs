using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace BetterVision
{
    // Helmet thermal through a magnified optic.
    // A normal optic runs a full thermal pass from the main camera's current
    // state. Color Passthrough is the explicit heat-buffer path and skips
    // palette presentation. Native thermal scopes keep their own pass and
    // borrow the active T7 utilities.
    internal static class OpticThermalInheritance
    {
        private sealed class Snapshot
        {
            internal bool Enabled;
            internal bool On;
            internal bool IsFpsStuck;
            internal bool IsGlitch;
            internal bool IsMotionBlurred;
            internal bool IsNoisy;
            internal bool IsPixelated;
            internal ThermalVisionUtilities Utilities;
            internal float ChromaticAberrationShift;
            internal float UnsharpBias;
            internal float UnsharpRadiusBlur;
        }

        private static OpticComponentUpdater _activeUpdater;
        private static OpticSight _activeOptic;
        private static ThermalVision _inheritedThermal;
        private static Snapshot _snapshot;
        private static bool _useHeatOnly;

        internal static void Register(OpticComponentUpdater updater, OpticSight optic)
        {
            if (updater == null || optic == null)
                return;

            if (_activeUpdater != updater || _activeOptic != optic)
            {
                Restore();
                _activeUpdater = updater;
                _activeOptic = optic;
            }

            if (BetterVision.T7BlockScope.Value)
            {
                Restore();
                return;
            }

            Sync(CameraClass.Exist ? CameraClass.Instance?.ThermalVision : null);
        }

        internal static void Unregister(OpticComponentUpdater updater)
        {
            if (updater == null || updater != _activeUpdater)
                return;

            Restore();
            _activeUpdater = null;
            _activeOptic = null;
        }

        internal static void Sync(ThermalVision mainThermal)
        {
            if (_activeUpdater == null || _activeOptic == null)
                return;

            if (BetterVision.T7BlockScope.Value)
            {
                Restore();
                return;
            }

            if (mainThermal == null || !mainThermal.On)
            {
                Restore();
                return;
            }

            ThermalVision opticThermal = Traverse.Create(_activeUpdater)
                .Field("thermalVision_0")
                .GetValue<ThermalVision>();

            if (opticThermal == null)
                return;

            if (_inheritedThermal != opticThermal || _snapshot == null)
            {
                Restore();
                _inheritedThermal = opticThermal;
                _snapshot = new Snapshot
                {
                    Enabled = opticThermal.enabled,
                    On = opticThermal.On,
                    IsFpsStuck = opticThermal.IsFpsStuck,
                    IsGlitch = opticThermal.IsGlitch,
                    IsMotionBlurred = opticThermal.IsMotionBlurred,
                    IsNoisy = opticThermal.IsNoisy,
                    IsPixelated = opticThermal.IsPixelated,
                    Utilities = opticThermal.ThermalVisionUtilities,
                    ChromaticAberrationShift = opticThermal.ChromaticAberrationThermalShift,
                    UnsharpBias = opticThermal.UnsharpBias,
                    UnsharpRadiusBlur = opticThermal.UnsharpRadiusBlur
                };
            }

            ScopeThermalVisionData nativeData = _activeOptic.ScopeData?.ThermalVisionData;
            if (nativeData != null && nativeData.ThermalVision)
            {
                // EFT owns the native scope's thermal pass. Only borrow the
                // active T-7 utilities so its palette is presented once, then
                // restore the original utilities when inheritance ends.
                _useHeatOnly = BetterVision.T7ColorPassthrough.Value;
                opticThermal.ThermalVisionUtilities = mainThermal.ThermalVisionUtilities;
                opticThermal.SetMaterialProperties();
                return;
            }

            // Normal palettes run one complete optic thermal pass. Only the
            // BD-T7/S-style color-passthrough mode uses the heat-only buffer.
            _useHeatOnly = BetterVision.T7ColorPassthrough.Value;
            ApplyMainCameraSettings(opticThermal, mainThermal);

            opticThermal.On = true;
            opticThermal.enabled = true;
            opticThermal.SetMaterialProperties();
        }

        internal static bool IsHeatOnlyActive(ThermalVision thermal)
        {
            if (thermal != null
                && ThermalReticleExposure.PresentationReady
                && BetterVision.T7ColorPassthrough.Value
                && ThermalReticleExposure.IsFpsCamera(thermal))
            {
                return true;
            }

            return thermal != null
                && !BetterVision.T7BlockScope.Value
                && _useHeatOnly
                && thermal == _inheritedThermal
                && _snapshot != null;
        }

        internal static void SetScopeProcessingEnabled(bool enabled)
        {
            if (!enabled)
            {
                Restore();
                return;
            }

            Sync(CameraClass.Exist ? CameraClass.Instance?.ThermalVision : null);
        }

        internal static void Reset()
        {
            ClearRegistration();
            OpticHeatOnlyPipeline.ForgetAll();
        }

        internal static void OnThermalDestroyed(ThermalVision thermal)
        {
            if (ReferenceEquals(_inheritedThermal, thermal))
            {
                _inheritedThermal = null;
                _snapshot = null;
                _useHeatOnly = false;
                _activeUpdater = null;
                _activeOptic = null;
            }

            OpticHeatOnlyPipeline.Forget(thermal);
        }

        private static void ClearRegistration()
        {
            Restore();
            _activeUpdater = null;
            _activeOptic = null;
        }

        private static void ApplyMainCameraSettings(ThermalVision opticThermal, ThermalVision mainThermal)
        {
            opticThermal.ThermalVisionUtilities = mainThermal.ThermalVisionUtilities;
            opticThermal.ChromaticAberrationThermalShift = mainThermal.ChromaticAberrationThermalShift;
            opticThermal.UnsharpBias = mainThermal.UnsharpBias;
            opticThermal.UnsharpRadiusBlur = mainThermal.UnsharpRadiusBlur;
            opticThermal.IsFpsStuck = mainThermal.IsFpsStuck;
            opticThermal.IsGlitch = mainThermal.IsGlitch;
            opticThermal.IsMotionBlurred = mainThermal.IsMotionBlurred;
            opticThermal.IsNoisy = mainThermal.IsNoisy;
            opticThermal.IsPixelated = mainThermal.IsPixelated;
        }

        private static void Restore()
        {
            if (_inheritedThermal == null || _snapshot == null)
            {
                _inheritedThermal = null;
                _snapshot = null;
                _useHeatOnly = false;
                return;
            }

            _inheritedThermal.On = _snapshot.On;
            _inheritedThermal.IsFpsStuck = _snapshot.IsFpsStuck;
            _inheritedThermal.IsGlitch = _snapshot.IsGlitch;
            _inheritedThermal.IsMotionBlurred = _snapshot.IsMotionBlurred;
            _inheritedThermal.IsNoisy = _snapshot.IsNoisy;
            _inheritedThermal.IsPixelated = _snapshot.IsPixelated;
            _inheritedThermal.ThermalVisionUtilities = _snapshot.Utilities;
            _inheritedThermal.ChromaticAberrationThermalShift = _snapshot.ChromaticAberrationShift;
            _inheritedThermal.UnsharpBias = _snapshot.UnsharpBias;
            _inheritedThermal.UnsharpRadiusBlur = _snapshot.UnsharpRadiusBlur;
            if (_snapshot.Utilities != null)
                _inheritedThermal.SetMaterialProperties();

            bool restoreThermalPipeline = _snapshot.Enabled && _snapshot.On;
            _inheritedThermal.enabled = _snapshot.Enabled;
            OpticHeatOnlyPipeline.Restore(_inheritedThermal, restoreThermalPipeline);
            _inheritedThermal = null;
            _snapshot = null;
            _useHeatOnly = false;
        }
    }

    internal static class OpticHeatOnlyPipeline
    {
        private static readonly int ThermalVisionOnId = Shader.PropertyToID("_ThermalVisionOn");
        private static readonly FieldInfo MainBufferField = AccessTools.Field(typeof(ThermalVision), "commandBuffer_0");
        private static readonly FieldInfo GBufferField = AccessTools.Field(typeof(ThermalVision), "commandBuffer_1");
        private static readonly FieldInfo MaterialField = AccessTools.Field(typeof(ThermalVision), "material_0");
        private static readonly FieldInfo SsaaField = AccessTools.Field(typeof(ThermalVision), "ssaa_0");
        private static readonly FieldInfo ScatteringField = AccessTools.Field(typeof(ThermalVision), "mboit_Scattering_0");

        private static ThermalVision _cachedThermal;
        private static CommandBuffer _mainBuffer;
        private static CommandBuffer _gBuffer;
        private static Material _material;
        private static SSAA _ssaa;
        private static MBOIT_Scattering _scattering;

        internal static bool Prepare(ThermalVision thermal)
        {
            if (!OpticThermalInheritance.IsHeatOnlyActive(thermal))
                return false;

            Bind(thermal);
            _mainBuffer?.Clear();
            _gBuffer?.Clear();
            _gBuffer?.Blit(BuiltinRenderTextureType.GBuffer0, BuiltinRenderTextureType.CameraTarget);
            Shader.SetGlobalFloat(ThermalVisionOnId, 1f);
            _ssaa?.SetThermalVisionOn(true, _material);
            if (_scattering != null)
                _scattering.ThermalVisionIsEnabled = true;
            return true;
        }

        internal static void Finish()
        {
            Shader.SetGlobalFloat(ThermalVisionOnId, 0f);
            _mainBuffer?.Clear();
            _gBuffer?.Clear();
        }

        internal static void Restore(ThermalVision thermal, bool thermalOn)
        {
            if (thermal == null)
                return;

            Material material = thermalOn
                ? MaterialField.GetValue(thermal) as Material
                : null;
            SSAA ssaa = SsaaField.GetValue(thermal) as SSAA;
            MBOIT_Scattering scattering = ScatteringField.GetValue(thermal) as MBOIT_Scattering;
            ssaa?.SetThermalVisionOn(thermalOn, material);
            if (scattering != null)
                scattering.ThermalVisionIsEnabled = thermalOn;

            Forget(thermal);
        }

        internal static void Forget(ThermalVision thermal)
        {
            if (!ReferenceEquals(thermal, _cachedThermal))
                return;

            ForgetAll();
        }

        internal static void ForgetAll()
        {
            _cachedThermal = null;
            _mainBuffer = null;
            _gBuffer = null;
            _material = null;
            _ssaa = null;
            _scattering = null;
        }

        private static void Bind(ThermalVision thermal)
        {
            if (thermal == _cachedThermal)
                return;

            _cachedThermal = thermal;
            _mainBuffer = MainBufferField.GetValue(thermal) as CommandBuffer;
            _gBuffer = GBufferField.GetValue(thermal) as CommandBuffer;
            _material = MaterialField.GetValue(thermal) as Material;
            _ssaa = SsaaField.GetValue(thermal) as SSAA;
            _scattering = ScatteringField.GetValue(thermal) as MBOIT_Scattering;
        }
    }

    [HarmonyPatch]
    internal static class OpticHeatPreCullPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ThermalVision), "OnPreCull");
        private static bool Prefix(ThermalVision __instance) => !OpticHeatOnlyPipeline.Prepare(__instance);
    }

    [HarmonyPatch]
    internal static class OpticHeatPostRenderPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ThermalVision), "OnPostRender");

        private static bool Prefix(ThermalVision __instance)
        {
            if (!OpticThermalInheritance.IsHeatOnlyActive(__instance))
                return true;

            OpticHeatOnlyPipeline.Finish();
            return false;
        }
    }

    [HarmonyPatch]
    internal static class OpticAdsDisablePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("GClass3687"), "method_3", new[] { typeof(OpticSight) });
        }

        private static void Prefix(object __instance, OpticSight opticSight, out bool __state)
        {
            OpticSight current = Traverse.Create(__instance).Property("CurrentOpticSight").GetValue<OpticSight>();
            __state = current == opticSight;
        }

        private static void Postfix(object __instance, bool __state)
        {
            if (!__state)
                return;

            OpticComponentUpdater updater = Traverse.Create(__instance).Property("Updater").GetValue<OpticComponentUpdater>();
            OpticThermalInheritance.Unregister(updater);
        }
    }

    [HarmonyPatch]
    internal static class OpticDisposePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(AccessTools.TypeByName("GClass3687"), "Dispose");
        }

        private static void Prefix(object __instance)
        {
            OpticComponentUpdater updater = Traverse.Create(__instance).Property("Updater").GetValue<OpticComponentUpdater>();
            OpticThermalInheritance.Unregister(updater);
        }
    }
}
