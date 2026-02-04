using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using BSG.CameraEffects;

namespace BetterVision
{
    public static class NVScopeBright
    {
        private static CommandBuffer _cb;
        private static int _rtId = Shader.PropertyToID("_NVScopeBrightRT");

        private static Material _nvMat;   // NV 材质副本
        private static Camera _opticCam;
        private static bool _attached;

        // 倍镜相机初始化
        public static void Init(GClass3687 manager)
        {
            if (manager == null || manager.Camera == null)
                return;

            _opticCam = manager.Camera;

            if (_cb == null)
                _cb = new CommandBuffer { name = "NVScopeBright_CommandBuffer" };

            if (!_attached)
            {
                _opticCam.RemoveCommandBuffer(CameraEvent.BeforeImageEffects, _cb);
                _opticCam.AddCommandBuffer(CameraEvent.BeforeImageEffects, _cb);
                _attached = true;
            }

            PrepareMaterial();
        }

        // 准备 NV 材质
        private static void PrepareMaterial()
        {
            var mainNV = CameraClass.Instance.NightVision;
            if (mainNV != null)
                _nvMat = mainNV.Material_0;
        }

        // 重建 CommandBuffer（启用倍镜时调用）
        public static void Rebuild()
        {
            if (!_attached || _opticCam == null || _cb == null)
                return;

            _cb.Clear();

            // 用户选择原版 → 不处理
            if (BetterVision.NVScopeDefault.Value)
                return;

            var mainNV = CameraClass.Instance.NightVision;
            if (mainNV == null || !mainNV.On)
                return;

            if (_nvMat == null)
                PrepareMaterial();

            if (_nvMat == null)
                return;

            // 设置亮度倍率
            float boost = BetterVision.NVScopeBrightness.Value;
            float baseIntensity = mainNV.Intensity;

            _nvMat.SetFloat("_Intensity", baseIntensity * boost);

            // 创建临时 RT
            _cb.GetTemporaryRT(_rtId, -1, -1, 0, FilterMode.Bilinear);

            // NV 材质处理
            _cb.Blit(BuiltinRenderTextureType.CameraTarget, _rtId, _nvMat);
            _cb.Blit(_rtId, BuiltinRenderTextureType.CameraTarget);

            _cb.ReleaseTemporaryRT(_rtId);
        }
    }

    // ============================================================
    // Patch：倍镜相机初始化
    // ============================================================
    [HarmonyPatch(typeof(GClass3687), "Init")]
    public class Patch_NVScopeBright_Init
    {
        static void Postfix(GClass3687 __instance)
        {
            NVScopeBright.Init(__instance);
        }
    }

    // ============================================================
    // Patch：启用倍镜时重建 CommandBuffer
    // ============================================================
    [HarmonyPatch(typeof(GClass3687), "method_2")]
    public class Patch_NVScopeBright_OnOpticEnabled
    {
        static void Postfix()
        {
            NVScopeBright.Rebuild();
        }
    }
}
