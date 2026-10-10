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
        private static Camera _opticCam;

        public static void Init(OpticCameraManager manager)
        {
            if (manager == null || manager.Camera == null)
                return;
            _opticCam = manager.Camera;
            _cb = new CommandBuffer { name = "NVScopeBright_CommandBuffer" };
            _opticCam.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _cb);
            _opticCam.AddCommandBuffer(CameraEvent.BeforeImageEffects, _cb);
        }

        public static void Rebuild()
        {
            if (_opticCam == null || _cb == null)
                return;

            _cb.Clear();

            if (BetterVision.NVScopeDefault.Value)
                return;

            var mainNV = CameraManager.Instance.NightVision;
            if (mainNV == null || !mainNV.On)
                return;

            if (_nvMat == null || mainNV.Material != _nvMat)
                _nvMat = new Material(mainNV.Material);

            float boost = BetterVision.NVScopeBrightness.Value;
            float baseIntensity = mainNV.Intensity;

            _nvMat.SetFloat("_Intensity", baseIntensity * boost);

            _cb.GetTemporaryRT(_rtId, -1, -1, 0, FilterMode.Bilinear);

            _cb.Blit(BuiltinRenderTextureType.CameraTarget, _rtId, _nvMat);
            _cb.Blit(_rtId, BuiltinRenderTextureType.CameraTarget);

            _cb.ReleaseTemporaryRT(_rtId);
        }
    }

    // Patch：倍镜相机初始化
    [HarmonyPatch(typeof(OpticCameraManager), "Init")]
    public class Patch_NVScopeBright_Init
    {
        static void Postfix(OpticCameraManager __instance)
        {
            NVScopeBright.Init(__instance);
        }
    }

    // Patch：启用倍镜时重建 CommandBuffer
    [HarmonyPatch(typeof(OpticCameraManager), "OnOpticSightEnabled")]
    public class Patch_NVScopeBright_OnOpticEnabled
    {
        static void Postfix()
        {
            NVScopeBright.Rebuild();
        }
    }
}
