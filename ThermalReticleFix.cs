using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BSG.CameraEffects;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace BetterVision
{
    internal static class ThermalReticleExposure
    {
        private static readonly HashSet<CollimatorSight> TrackedCollimators =
            new HashSet<CollimatorSight>();
        private static OpticSight _activeOptic;
        private static bool _active;
        private static bool _presentationReady;
        private static bool _scopeOverridesApplied;
        private static bool _worldGlassOverridesApplied;
        private static bool _scopeProcessingEnabled;

        internal static bool Active => _active && !BetterVision.T7BlockScope.Value;
        internal static bool WorldGlassActive => _active && !BetterVision.T7BlockGlass.Value;
        internal static bool PresentationReady => _active && _presentationReady;

        internal static void SetThermalState(bool active)
        {
            _active = active;
            if (!active)
                _presentationReady = false;
            UpdateGates();
        }

        internal static void SetPresentationReady()
        {
            if (_active)
                _presentationReady = true;
        }

        private static void ApplyTrackedSights()
        {
            _scopeOverridesApplied = true;
            ThermalSightOverrides.ApplyToOptic(_activeOptic);
            foreach (CollimatorSight collimator in TrackedCollimators)
            {
                if (collimator != null && collimator.enabled && collimator.gameObject.activeInHierarchy)
                {
                    ThermalSightOverrides.ApplyToCollimator(collimator);
                    ScheduleCollimatorRefresh(collimator);
                }
            }
        }

        internal static bool IsFpsCamera(ThermalVision thermal)
        {
            if (thermal == null || thermal.gameObject == null || !thermal.gameObject.activeInHierarchy)
                return false;

            ThermalVision mainThermal = CameraManager.Exist ? CameraManager.Instance?.ThermalVision : null;
            if (mainThermal != null)
                return ReferenceEquals(thermal, mainThermal);

            return string.Equals(thermal.gameObject.name, "FPS Camera", StringComparison.Ordinal);
        }

        internal static void Update()
        {
            UpdateGates();
            if (Active)
            {
                LateCollimatorRenderer.Update();
            }
            else
                LateCollimatorRenderer.Reset();
        }

        private static void UpdateGates()
        {
            bool scopeActive = Active;
            if (scopeActive != _scopeProcessingEnabled)
            {
                _scopeProcessingEnabled = scopeActive;
                OpticThermalInheritance.SetScopeProcessingEnabled(scopeActive);
            }

            if (scopeActive && !_scopeOverridesApplied)
                ApplyTrackedSights();
            else if (!scopeActive && _scopeOverridesApplied)
            {
                ThermalSightOverrides.RestoreSightOverrides();
                _scopeOverridesApplied = false;
            }

            bool worldGlassActive = WorldGlassActive;
            if (worldGlassActive && !_worldGlassOverridesApplied)
            {
                ThermalSightOverrides.ApplyCachedWorldSlots();
                _worldGlassOverridesApplied = true;
            }
            else if (!worldGlassActive && _worldGlassOverridesApplied)
            {
                ThermalSightOverrides.RestoreWorldOverrides();
                _worldGlassOverridesApplied = false;
            }
        }

        internal static void Reset()
        {
            _active = false;
            _presentationReady = false;
            LateCollimatorRenderer.Reset();
            ThermalSightOverrides.RestoreSightOverrides();
            ThermalSightOverrides.RestoreWorldOverrides();
            ThermalSightOverrides.ClearWorldCache();
            _scopeOverridesApplied = false;
            _worldGlassOverridesApplied = false;
            _scopeProcessingEnabled = false;
            OpticThermalInheritance.SetScopeProcessingEnabled(false);
            TrackedCollimators.Clear();
            _activeOptic = null;
        }

        internal static void PruneDestroyedSceneReferences()
        {
            TrackedCollimators.RemoveWhere(collimator => collimator == null);
            if (_activeOptic == null)
                _activeOptic = null;
            ThermalSightOverrides.PruneDestroyedSightOverrides();
        }

        internal static void OnThermalDestroyed(ThermalVision thermal)
        {
            bool isMainThermal = thermal != null
                && ((CameraManager.Exist
                        && ReferenceEquals(thermal, CameraManager.Instance?.ThermalVision))
                    || (thermal.gameObject != null
                        && string.Equals(
                            thermal.gameObject.name,
                            "FPS Camera",
                            StringComparison.Ordinal)));

            if (isMainThermal)
            {
                SetThermalState(false);
                T7Color.Release(thermal);
                BetterVision.ReleaseT7ClarityTarget(thermal);
            }

            ThermalOpticSettings.OnThermalDestroyed(thermal);
            OpticThermalInheritance.OnThermalDestroyed(thermal);
        }

        internal static void TrackOptic(OpticSight optic)
        {
            _activeOptic = optic;
            if (Active)
            {
                ThermalSightOverrides.ApplyToOptic(optic);
                ScheduleSightRefresh(optic);
            }
        }

        internal static void TrackCollimator(CollimatorSight collimator)
        {
            if (collimator == null)
                return;

            TrackedCollimators.Add(collimator);
            if (Active)
            {
                ThermalSightOverrides.ApplyToCollimator(collimator);
                ScheduleCollimatorRefresh(collimator);
            }
        }

        internal static void UntrackCollimator(CollimatorSight collimator)
        {
            if (collimator != null)
                TrackedCollimators.Remove(collimator);
        }

        internal static void RefreshHybrid(Component sight)
        {
            if (!Active)
                return;

            ThermalSightOverrides.ApplyToSightHierarchy(sight);
            ScheduleSightRefresh(sight);
        }

        private static void ScheduleSightRefresh(Component sight)
        {
            if (BetterVision.Instance != null && sight != null)
                BetterVision.Instance.StartCoroutine(RefreshSightHierarchy(sight));
        }

        private static void ScheduleCollimatorRefresh(CollimatorSight collimator)
        {
            if (BetterVision.Instance != null && collimator != null)
                BetterVision.Instance.StartCoroutine(RefreshCollimator(collimator));
        }

        private static IEnumerator RefreshSightHierarchy(Component sight)
        {
            yield return null;
            if (sight == null || !Active)
                yield break;

            ThermalSightOverrides.ApplyToSightHierarchy(sight);
            yield return new WaitForEndOfFrame();
            if (sight != null && Active)
                ThermalSightOverrides.ApplyToSightHierarchy(sight);
        }

        private static IEnumerator RefreshCollimator(CollimatorSight collimator)
        {
            yield return null;
            if (collimator == null || !Active)
                yield break;

            ThermalSightOverrides.ApplyToCollimator(collimator);
            yield return new WaitForEndOfFrame();
            if (collimator != null && Active)
                ThermalSightOverrides.ApplyToCollimator(collimator);
        }
    }

    internal static class ThermalSightOverrides
    {
        private const string WorldGlassShaderName = "Global Fog/Transparent Reflective Specular";
        private const string SecondaryWorldGlassShaderName = "p0/Reflective/Bumped Specular";
        private const string TransparentCutoffShaderName = "p0/Reflective/Bumped Specular SMap Transparent Cutoff";
        private const string EmaFenceMaterialName = "fence_ema_01_blue";
        private const string OpticSightShaderName = "CW FX/OpticSight";
        private const string OpticGlassShaderName = "Custom/OpticGlass";
        private const string CollimatorShaderName = "CW FX/Collimator";
        private const string CollimatorGlassShaderName = "EFT/CollimatorGlass";
        private const string LegacyOpticLensShaderName = "p0/OpticDeferredLinza";
        private const float OpticMarkLightnessOffset = 0.4f;

        private static readonly int ThermalVisionOnId = Shader.PropertyToID("_ThermalVisionOn");
        private static readonly int HdrId = Shader.PropertyToID("_HDR");
        private static readonly int MarkLightnessId = Shader.PropertyToID("_MarkLightness");
        private static readonly int MarkTextureId = Shader.PropertyToID("_MarkTex");
        private static readonly Dictionary<Renderer, Dictionary<int, MaterialPropertyBlock>> OriginalSightBlocks =
            new Dictionary<Renderer, Dictionary<int, MaterialPropertyBlock>>();
        private static readonly Dictionary<Renderer, Dictionary<int, MaterialPropertyBlock>> OriginalWorldBlocks =
            new Dictionary<Renderer, Dictionary<int, MaterialPropertyBlock>>();
        private static readonly Dictionary<Renderer, HashSet<int>> CachedWorldSlots =
            new Dictionary<Renderer, HashSet<int>>();

        internal static void RebuildLoadedSceneCache()
        {
            RestoreWorldOverrides();
            CachedWorldSlots.Clear();

            for (int i = 0; i < SceneManager.sceneCount; i++)
                ApplyToScene(SceneManager.GetSceneAt(i));

            if (ThermalReticleExposure.WorldGlassActive)
                ApplyCachedWorldSlots();
        }

        internal static void ClearWorldCache()
        {
            CachedWorldSlots.Clear();
        }

        internal static void PruneDestroyedSightOverrides()
        {
            var destroyed = new List<Renderer>();
            foreach (Renderer renderer in OriginalSightBlocks.Keys)
            {
                if (renderer == null)
                    destroyed.Add(renderer);
            }

            foreach (Renderer renderer in destroyed)
                OriginalSightBlocks.Remove(renderer);
        }

        internal static void ApplyToScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                    CacheWorldRenderer(renderer);
            }

        }

        private static void CacheWorldRenderer(Renderer renderer)
        {
            if (renderer == null)
                return;

            Material[] materials = renderer.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                if (!IsWorldGlassMaterial(materials[slot]))
                    continue;

                HashSet<int> slots;
                if (!CachedWorldSlots.TryGetValue(renderer, out slots))
                {
                    slots = new HashSet<int>();
                    CachedWorldSlots.Add(renderer, slots);
                }
                slots.Add(slot);
            }
        }

        internal static void ApplyCachedWorldSlots()
        {
            var destroyed = new List<Renderer>();
            foreach (KeyValuePair<Renderer, HashSet<int>> entry in CachedWorldSlots)
            {
                Renderer renderer = entry.Key;
                if (renderer == null)
                {
                    destroyed.Add(renderer);
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                foreach (int slot in entry.Value)
                {
                    if (slot < 0 || slot >= materials.Length
                        || !IsWorldGlassMaterial(materials[slot])
                        || IsAlreadyOverridden(OriginalWorldBlocks, renderer, slot))
                    {
                        continue;
                    }
                    ApplyToSlot(OriginalWorldBlocks, renderer, slot, materials[slot]);
                }
            }

            foreach (Renderer renderer in destroyed)
                CachedWorldSlots.Remove(renderer);
        }

        internal static void ApplyToOptic(OpticSight optic)
        {
            if (optic == null)
                return;

            foreach (Renderer renderer in optic.transform.root.GetComponentsInChildren<Renderer>(true))
                ApplyToRenderer(renderer);
            ApplyToRenderer(optic.LensRenderer);
        }

        internal static void ApplyToSightHierarchy(Component sight)
        {
            if (sight == null)
                return;

            foreach (Renderer renderer in sight.transform.root.GetComponentsInChildren<Renderer>(true))
                ApplyToRenderer(renderer);
        }

        internal static void ApplyToCollimator(CollimatorSight collimator)
        {
            if (collimator == null)
                return;

            Transform root = collimator.transform.parent != null
                ? collimator.transform.parent
                : collimator.transform;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                ApplyToRenderer(renderer);
        }

        internal static void RestoreSightOverrides()
        {
            Restore(OriginalSightBlocks);
        }

        internal static void RestoreWorldOverrides()
        {
            Restore(OriginalWorldBlocks);
        }

        private static void Restore(
            Dictionary<Renderer, Dictionary<int, MaterialPropertyBlock>> originals)
        {
            foreach (KeyValuePair<Renderer, Dictionary<int, MaterialPropertyBlock>> rendererEntry in originals)
            {
                if (rendererEntry.Key == null)
                    continue;
                foreach (KeyValuePair<int, MaterialPropertyBlock> slotEntry in rendererEntry.Value)
                    rendererEntry.Key.SetPropertyBlock(slotEntry.Value, slotEntry.Key);
            }
            originals.Clear();
        }

        private static bool IsAlreadyOverridden(
            Dictionary<Renderer, Dictionary<int, MaterialPropertyBlock>> originals,
            Renderer renderer,
            int slot)
        {
            Dictionary<int, MaterialPropertyBlock> slots;
            return originals.TryGetValue(renderer, out slots) && slots.ContainsKey(slot);
        }

        private static void ApplyToRenderer(Renderer renderer)
        {
            if (renderer == null)
                return;

            Material[] materials = renderer.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++)
            {
                Material material = materials[slot];
                if (!IsSightMaterial(material))
                    continue;

                Dictionary<int, MaterialPropertyBlock> slots;
                if (!OriginalSightBlocks.TryGetValue(renderer, out slots))
                {
                    slots = new Dictionary<int, MaterialPropertyBlock>();
                    OriginalSightBlocks.Add(renderer, slots);
                }

                if (slots.ContainsKey(slot))
                    ReapplyToSlot(renderer, slot, material);
                else
                    ApplyToSlot(OriginalSightBlocks, renderer, slot, material);
            }
        }

        private static void ApplyToSlot(
            Dictionary<Renderer, Dictionary<int, MaterialPropertyBlock>> originals,
            Renderer renderer,
            int slot,
            Material material)
        {
            var original = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(original, slot);
            ReapplyToSlot(renderer, slot, material);

            Dictionary<int, MaterialPropertyBlock> slots;
            if (!originals.TryGetValue(renderer, out slots))
            {
                slots = new Dictionary<int, MaterialPropertyBlock>();
                originals.Add(renderer, slots);
            }
            slots.Add(slot, original);
        }

        private static void ReapplyToSlot(Renderer renderer, int slot, Material material)
        {
            var thermalNeutral = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(thermalNeutral, slot);
            thermalNeutral.SetFloat(ThermalVisionOnId, 0f);

            if (material != null && material.shader != null
                && material.shader.name == CollimatorShaderName && material.HasProperty(HdrId))
            {
                thermalNeutral.SetFloat(HdrId, 0.4f);
            }

            bool hasReticleTexture = material != null
                && material.HasProperty(MarkTextureId)
                && material.GetTexture(MarkTextureId) != null;
            if (hasReticleTexture && material.HasProperty(MarkLightnessId))
            {
                thermalNeutral.SetFloat(
                    MarkLightnessId,
                    material.GetFloat(MarkLightnessId) - OpticMarkLightnessOffset);
            }
            renderer.SetPropertyBlock(thermalNeutral, slot);
        }

        private static bool IsWorldGlassMaterial(Material material)
        {
            if (material == null || material.shader == null)
                return false;

            string shaderName = material.shader.name;
            if (shaderName == WorldGlassShaderName)
                return true;

            if (shaderName == SecondaryWorldGlassShaderName
                && material.GetTag("RenderType", false, string.Empty) == "Transparent"
                && material.name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (shaderName == TransparentCutoffShaderName
                && material.name.StartsWith(EmaFenceMaterialName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool IsSightMaterial(Material material)
        {
            if (material == null || material.shader == null)
                return false;

            string shaderName = material.shader.name;
            return shaderName == OpticSightShaderName
                || shaderName == OpticGlassShaderName
                || shaderName == CollimatorShaderName
                || shaderName == CollimatorGlassShaderName
                || shaderName == LegacyOpticLensShaderName;
        }
    }

    internal static class LateCollimatorRenderer
    {
        private const string BufferName = "Better Thermal Late Collimator";
        private static readonly FieldInfo CollimatorRendererField =
            AccessTools.Field(typeof(SSAAPropagator), "_collimatorRenderer");
        private static readonly FieldInfo CollimatorMaterialField =
            AccessTools.Field(typeof(SSAAPropagator), "_collimatorMaterial");
        private static Camera _camera;
        private static SSAAPropagator _propagator;
        private static CommandBuffer _buffer;
        private static Renderer _renderer;
        private static Material _material;
        private static bool _originalForceRenderingOff;

        internal static void Update()
        {
            if (!ThermalReticleExposure.Active)
            {
                Reset();
                return;
            }

            Camera camera = CameraManager.Exist ? CameraManager.Instance?.Camera : null;
            if (camera == null)
            {
                Reset();
                return;
            }

            if (camera != _camera)
            {
                Reset();
                _camera = camera;
                _propagator = camera.GetComponent<SSAAPropagator>();
                _buffer = new CommandBuffer { name = BufferName };
                camera.AddCommandBuffer(CameraEvent.AfterForwardAlpha, _buffer);
            }

            Renderer renderer = _propagator == null
                ? null
                : CollimatorRendererField.GetValue(_propagator) as Renderer;
            Material material = _propagator == null
                ? null
                : CollimatorMaterialField.GetValue(_propagator) as Material;
            if (renderer != _renderer || material != _material)
                Bind(renderer, material);
            if (_renderer != null)
                _renderer.forceRenderingOff = true;
        }

        internal static void Reset()
        {
            RestoreRenderer();
            if (_camera != null && _buffer != null)
                _camera.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, _buffer);
            _buffer?.Release();
            _buffer = null;
            _camera = null;
            _propagator = null;
        }

        private static void Bind(Renderer renderer, Material material)
        {
            RestoreRenderer();
            _renderer = renderer;
            _material = material;
            _buffer?.Clear();
            if (renderer == null || material == null)
                return;

            _originalForceRenderingOff = renderer.forceRenderingOff;
            renderer.forceRenderingOff = true;
            _buffer.BeginSample(BufferName);
            _buffer.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
            _buffer.DrawRenderer(renderer, material, 0, 0);
            _buffer.EndSample(BufferName);
        }

        private static void RestoreRenderer()
        {
            if (_renderer != null)
                _renderer.forceRenderingOff = _originalForceRenderingOff;
            _renderer = null;
            _material = null;
        }
    }

    // EFT normally skips OutputOptic whenever thermal is enabled. Retain its
    // native lens-depth/motion composite while the Spectrum-style path is active.
    [HarmonyPatch]
    internal static class ThermalOpticOutputPatch
    {
        private static readonly FieldInfo ThermalVisionIsOnField =
            AccessTools.Field(typeof(SSAAPropagator), "_thermalVisionIsOn");
        private static readonly FieldInfo OpticLensRendererField =
            AccessTools.Field(typeof(SSAAPropagator), "_opticLensRenderer");

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(SSAAPropagator), "OnRenderImage");
            yield return AccessTools.Method(typeof(SSAAPropagator), "FillCommandBuffer");
        }

        private static bool ShouldPreserveOpticOutput(SSAAPropagator propagator)
        {
            return ThermalReticleExposure.Active
                && propagator != null
                && OpticLensRendererField.GetValue(propagator) != null;
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator,
            MethodBase __originalMethod)
        {
            var codes = new List<CodeInstruction>(instructions);
            MethodInfo shouldPreserve = AccessTools.Method(
                typeof(ThermalOpticOutputPatch),
                nameof(ShouldPreserveOpticOutput));
            var guardIndices = new List<int>();

            for (int i = 1; i < codes.Count - 1; i++)
            {
                if (codes[i - 1].opcode != OpCodes.Ldfld
                    || !Equals(codes[i - 1].operand, ThermalVisionIsOnField)
                    || (codes[i].opcode != OpCodes.Brtrue
                        && codes[i].opcode != OpCodes.Brtrue_S))
                {
                    continue;
                }

                guardIndices.Add(i);
            }

            if (guardIndices.Count != 1)
            {
                BetterVision.Instance?.LogWarning(
                    $"Skipping thermal OutputOptic compatibility patch for " +
                    $"SSAAPropagator.{__originalMethod.Name}: expected one guard, " +
                    $"found {guardIndices.Count}.");
                return codes;
            }

            int guardIndex = guardIndices[0];
            object skipOpticOutput = codes[guardIndex].operand;
            Label continueOpticOutput = generator.DefineLabel();
            codes[guardIndex + 1].labels.Add(continueOpticOutput);
            codes[guardIndex].opcode = OpCodes.Brfalse;
            codes[guardIndex].operand = continueOpticOutput;
            codes.Insert(guardIndex + 1, new CodeInstruction(OpCodes.Ldarg_0));
            codes.Insert(guardIndex + 2, new CodeInstruction(OpCodes.Call, shouldPreserve));
            codes.Insert(guardIndex + 3, new CodeInstruction(OpCodes.Brfalse, skipOpticOutput));

            return codes;
        }
    }

    [HarmonyPatch(typeof(CollimatorSight), nameof(CollimatorSight.OnEnable))]
    internal static class ThermalReticleCollimatorEnablePatch
    {
        private static void Postfix(CollimatorSight __instance)
        {
            ThermalReticleExposure.TrackCollimator(__instance);
        }
    }

    [HarmonyPatch(typeof(CollimatorSight), nameof(CollimatorSight.OnDisable))]
    internal static class ThermalReticleCollimatorDisablePatch
    {
        private static void Postfix(CollimatorSight __instance)
        {
            ThermalReticleExposure.UntrackCollimator(__instance);
        }
    }

    [HarmonyPatch(typeof(ScopePrefabCache), nameof(ScopePrefabCache.SetMode))]
    internal static class ThermalReticleHybridModePatch
    {
        private static void Postfix(ScopePrefabCache __instance)
        {
            ThermalReticleExposure.RefreshHybrid(__instance);
            NVScopeBright.Rebuild();
        }
    }

    [HarmonyPatch(typeof(ThermalVision), nameof(ThermalVision.StartSwitch))]
    internal static class ThermalReticleStartSwitchPatch
    {
        private static void Prefix(ThermalVision __instance, bool on)
        {
            if (on && ThermalReticleExposure.IsFpsCamera(__instance))
            {
                // Preserve upstream's T-7 configuration at the point where
                // EFT builds the native thermal render state.
                T7Color.Apply(__instance);
                BetterVision.ApplyT7Clarity(__instance);
                ThermalReticleExposure.SetThermalState(true);
            }
        }
    }

    [HarmonyPatch(typeof(ThermalVision), nameof(ThermalVision.OnDestroy))]
    internal static class ThermalVisionDestroyPatch
    {
        private static void Prefix(ThermalVision __instance)
        {
            ThermalReticleExposure.OnThermalDestroyed(__instance);
        }
    }

    [HarmonyPatch]
    internal static class ThermalReticleSwitchCompletePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(ThermalVision), "Switch", new[] { typeof(bool) });
        }

        private static void Postfix(ThermalVision __instance, bool __0)
        {
            if (!ThermalReticleExposure.IsFpsCamera(__instance))
                return;

            if (!__0)
            {
                ThermalReticleExposure.SetThermalState(false);
                T7Color.Reset();
            }
            else
            {
                ThermalReticleExposure.SetPresentationReady();
                // EFT may rewrite effect fields while completing the switch.
                // Reassert this repo's menu values on the finished native pass.
                T7Color.Apply(__instance);
                BetterVision.ApplyT7Clarity(__instance);
                if (ThermalReticleExposure.Active)
                    OpticThermalInheritance.Sync(__instance);
                else
                    OpticThermalInheritance.SetScopeProcessingEnabled(false);
            }

            BetterVision.ApplyMaskSetting();
        }
    }
}
