using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace BetterVision
{
    // Captures heat in the zoom optic, then leaves presentation to the main
    // thermal camera. No palette or visual-filter preferences are changed here.
    internal static class OpticThermalInheritance
    {
        private sealed class Snapshot
        {
            internal bool Enabled;
            internal bool On;
            internal ThermalVisionUtilities Utilities;
            internal float ChromaticAberrationShift;
            internal float UnsharpBias;
            internal float UnsharpRadiusBlur;
        }

        private static OpticComponentUpdater _activeUpdater;
        private static OpticSight _activeOptic;
        private static ThermalVision _inheritedThermal;
        private static Snapshot _snapshot;

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
                    Utilities = opticThermal.ThermalVisionUtilities,
                    ChromaticAberrationShift = opticThermal.ChromaticAberrationThermalShift,
                    UnsharpBias = opticThermal.UnsharpBias,
                    UnsharpRadiusBlur = opticThermal.UnsharpRadiusBlur
                };
            }

            ScopeThermalVisionData nativeData =
                _activeOptic.ScopeData?.ThermalVisionData;
            if (nativeData == null || !nativeData.ThermalVision)
            {
                opticThermal.ThermalVisionUtilities = mainThermal.ThermalVisionUtilities;
                opticThermal.ChromaticAberrationThermalShift =
                    mainThermal.ChromaticAberrationThermalShift;
                opticThermal.UnsharpBias = mainThermal.UnsharpBias;
                opticThermal.UnsharpRadiusBlur = mainThermal.UnsharpRadiusBlur;
            }

            opticThermal.On = true;
            opticThermal.enabled = true;
            opticThermal.SetMaterialProperties();
        }

        internal static bool IsHeatOnlyActive(ThermalVision thermal)
        {
            return thermal != null && thermal == _inheritedThermal && _snapshot != null;
        }

        internal static void Reset()
        {
            Restore();
            _activeUpdater = null;
            _activeOptic = null;
        }

        private static void Restore()
        {
            if (_inheritedThermal == null || _snapshot == null)
            {
                _inheritedThermal = null;
                _snapshot = null;
                return;
            }

            _inheritedThermal.On = _snapshot.On;
            _inheritedThermal.ThermalVisionUtilities = _snapshot.Utilities;
            _inheritedThermal.ChromaticAberrationThermalShift = _snapshot.ChromaticAberrationShift;
            _inheritedThermal.UnsharpBias = _snapshot.UnsharpBias;
            _inheritedThermal.UnsharpRadiusBlur = _snapshot.UnsharpRadiusBlur;
            if (_snapshot.Utilities != null)
                _inheritedThermal.SetMaterialProperties();

            _inheritedThermal.enabled = _snapshot.Enabled;
            OpticHeatOnlyPipeline.Release(_inheritedThermal);
            _inheritedThermal = null;
            _snapshot = null;
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

        internal static void Release(ThermalVision thermal)
        {
            if (thermal != _cachedThermal)
                return;

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
    internal static class FpsThermalStatePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ThermalVision), "method_1", new[] { typeof(bool) });
        }

        private static void Postfix(ThermalVision __instance)
        {
            if (CameraClass.Exist && __instance == CameraClass.Instance?.ThermalVision)
                OpticThermalInheritance.Sync(__instance);
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
