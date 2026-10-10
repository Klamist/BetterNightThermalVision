using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace BetterVision
{
    public static class NVScopeBright
    {
        private static CommandBuffer _cb;
        private static int _rtId = Shader.PropertyToID("_NVScopeBrightRT");

        private static Material _nvMat;
        private static Material _sourceMat;
        private static Camera _opticCam;
        private static GClass3687 _manager;

        public static void Init(OpticCameraManager manager)
        {
            if (manager == null || manager.Camera == null)
                return;

            _opticCam = manager.Camera;
            _manager = manager;
            _cb = new CommandBuffer { name = "NVScopeBright_CommandBuffer" };
            _opticCam.AddCommandBuffer(CameraEvent.BeforeImageEffects, _cb);
        }

        public static void Rebuild()
        {
            if (_opticCam == null || _cb == null)
                return;

            _cb.Clear();

            if (BetterVision.NVScopeDefault.Value)
                return;

            if (!CameraClass.Exist)
                return;

            var scopeData = _manager?.CurrentOpticSight?.ScopeData;
            if ((scopeData?.NightVisionData != null && scopeData.NightVisionData.NightVision)
                || (scopeData?.ThermalVisionData != null && scopeData.ThermalVisionData.ThermalVision))
            {
                return;
            }

            var mainNV = CameraClass.Instance.NightVision;
            if (mainNV == null || !mainNV.On || mainNV.Material_0 == null)
                return;

            if (_nvMat == null || mainNV.Material_0 != _sourceMat)
            {
                if (_nvMat != null)
                    Object.Destroy(_nvMat);
                _sourceMat = mainNV.Material_0;
                _nvMat = new Material(mainNV.Material_0);
            }

            float boost = BetterVision.NVScopeBrightness.Value;
            float baseIntensity = mainNV.Intensity;

            _nvMat.SetFloat("_Intensity", baseIntensity * boost);

            _cb.GetTemporaryRT(_rtId, -1, -1, 0, FilterMode.Bilinear);

            _cb.Blit(BuiltinRenderTextureType.CameraTarget, _rtId, _nvMat);
            _cb.Blit(_rtId, BuiltinRenderTextureType.CameraTarget);

            _cb.ReleaseTemporaryRT(_rtId);
        }

        public static void Clear()
        {
            _cb?.Clear();
        }

        public static void Reset()
        {
            if (_opticCam != null && _cb != null)
                _opticCam.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _cb);
            _cb?.Release();
            _cb = null;
            _opticCam = null;
            _manager = null;

            if (_nvMat != null)
                Object.Destroy(_nvMat);
            _nvMat = null;
            _sourceMat = null;
        }
    }

    // Patch：倍镜相机初始化
    [HarmonyPatch(typeof(GClass3687), "Init")]
    public class Patch_NVScopeBright_Init
    {
        static void Prefix()
        {
            NVScopeBright.Reset();
            ThermalOpticSettings.Reset();
            BetterVision.ResetScopeColorCache();
        }

        static void Postfix(GClass3687 __instance)
        {
            NVScopeBright.Init(__instance);
        }
    }

    // Patch：启用倍镜时重建 CommandBuffer
    [HarmonyPatch(typeof(GClass3687), "method_2")]
    public class Patch_NVScopeBright_OnOpticEnabled
    {
        static void Postfix()
        {
            NVScopeBright.Rebuild();
        }
    }

    [HarmonyPatch(typeof(GClass3687), "Dispose")]
    public class Patch_NVScopeBright_Dispose
    {
        static void Prefix()
        {
            NVScopeBright.Reset();
            ThermalOpticSettings.Reset();
            BetterVision.ResetScopeColorCache();
        }
    }
}
