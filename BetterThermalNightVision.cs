using BepInEx;
using BepInEx.Configuration;
using BSG.CameraEffects;
using EFT.CameraControl;
using HarmonyLib;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BetterVision
{
    internal static class ThermalPaletteMode
    {
        internal const string WhiteHot = "White-hot";
        internal const string BlackHot = "Black-hot";
        internal const string RedHot = "Red-hot";
        internal const string Ironbow = "Ironbow";
        internal const string Fusion = "Fusion";
        internal const string Rainbow = "Rainbow";
    }

    internal sealed class ThermalPaletteConfig
    {
        internal ConfigEntry<float> MainTexColorCoef;
        internal ConfigEntry<float> MinimumTemperatureValue;
        internal ConfigEntry<float> RampShift;
    }

    [BepInPlugin("ciallo.BetterThermalNightVision", "Better Thermal & Night Vision", "2.0.0")]
    public class BetterVision : BaseUnityPlugin
    {
        internal const int ConfigVersion = 134;
        internal static ConfigEntry<int> ConfigFileVersion;
        public static BetterVision Instance;

        internal static ConfigEntry<bool> ScopeFps;
        internal static ConfigEntry<bool> ScopeGlitch;
        internal static ConfigEntry<bool> ScopeMotion;
        internal static ConfigEntry<bool> ScopeNoise;
        internal static ConfigEntry<bool> ScopePixel;
        internal static ConfigEntry<bool> ScopeChromatic;
        internal static ConfigEntry<bool> ScopeBlur;
        internal static ConfigEntry<float> ScopeMaxDistance;
        internal static ConfigEntry<float> ScopeDepthFade;

        internal static ConfigEntry<bool> ScopeUseCustomColor;
        internal static ConfigEntry<float> ScopeMainTexColorCoef;
        internal static ConfigEntry<float> ScopeMinTempValue;
        internal static ConfigEntry<float> ScopeRampShift;
        internal static bool ScopeColorInitialized = false;
        internal static float ScopeRead_MainTexColorCoef;
        internal static float ScopeRead_MinTempValue;
        internal static float ScopeRead_RampShift;
        internal static ConfigEntry<bool> ApplyReadColorOnce;

        internal static ConfigEntry<bool> T7Fps;
        internal static ConfigEntry<bool> T7Glitch;
        internal static ConfigEntry<bool> T7Motion;
        internal static ConfigEntry<bool> T7Noise;
        internal static ConfigEntry<bool> T7Pixel;
        internal static ConfigEntry<bool> T7Blur;
        internal static ConfigEntry<bool> T7BlockScope;
        internal static ConfigEntry<bool> T7BlockGlass;
        internal static ConfigEntry<float> T7DepthFade;
        internal static ConfigEntry<string> T7Palette;
        internal static ConfigEntry<bool> T7ColorPassthrough;
        internal static readonly Dictionary<string, ThermalPaletteConfig> T7PaletteConfigs =
            new Dictionary<string, ThermalPaletteConfig>();
        internal static float T7WorkingMainTexColorCoef = 0.15f;
        internal static float T7WorkingMinimumTemperatureValue = 0.2f;
        internal static float T7WorkingRampShift = -0.1f;
        private static ThermalVision _t7ClarityTarget;
        private static ThermalVisionUtilities _t7OriginalUtilities;
        private static bool _t7OriginalIsFpsStuck;
        private static bool _t7OriginalIsGlitch;
        private static bool _t7OriginalIsMotionBlurred;
        private static bool _t7OriginalIsNoisy;
        private static bool _t7OriginalIsPixelated;
        private static float _t7OriginalDepthFade;
        private static float _t7OriginalUnsharpRadiusBlur;
        private static float _t7OriginalUnsharpBias;
        private Harmony _harmony;

        internal static ConfigEntry<bool> NVNoise;
        internal static ConfigEntry<bool> NVScopeDefault;
        internal static ConfigEntry<float> NVScopeBrightness;
        internal static ConfigEntry<bool> NVT7Mask;

        public void Log(string msg)
        {
            Logger.LogInfo(msg);
        }

        internal void LogWarning(string msg)
        {
            Logger.LogWarning(msg);
        }

        private void Awake()
        {
            Instance = this;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            string configPath = Config.ConfigFilePath;
            int oldVersion = 0;
            if (File.Exists(configPath))
            {
                var temp = new ConfigFile(configPath, true);
                oldVersion = temp.Bind("Internal", "ConfigVersion", 0).Value;
            }
            if (oldVersion > 0 && oldVersion != ConfigVersion)
                File.Delete(configPath);
            ConfigFileVersion = Config.Bind("Internal", "ConfigVersion", ConfigVersion,
                new ConfigDescription("", null, new ConfigurationManagerAttributes { Browsable = false }));

            string pluginDir = Path.GetDirectoryName(Info.Location);
            T7Color.RedHotRampTexture = LoadRampTexture(
                pluginDir,
                "RedhotRamp.png",
                "BetterThermal Red-hot Ramp");
            T7Color.IronbowRampTexture = LoadRampTexture(
                pluginDir,
                "IronbowRamp.png",
                "BetterThermal Ironbow Ramp");

            ScopeFps = Config.Bind("Thermal Optic", "FPS Limit", false);
            ScopeGlitch = Config.Bind("Thermal Optic", "Scanline", false);
            ScopeMotion = Config.Bind("Thermal Optic", "Motion Blur", false);
            ScopeNoise = Config.Bind("Thermal Optic", "Noise", false);
            ScopePixel = Config.Bind("Thermal Optic", "Pixelation", false);
            ScopeChromatic = Config.Bind("Thermal Optic", "Edge Aberration", false);
            ScopeBlur = Config.Bind("Thermal Optic", "Blur", false);

            ScopeMaxDistance = Config.Bind("Thermal Optic", "Distance", 500f,
                new ConfigDescription("", new AcceptableValueRange<float>(100f, 2000f)));
            ScopeDepthFade = Config.Bind("Thermal Optic", "Depth Fade (?)", 0.01f,
                new ConfigDescription("Lower is clearer on far distance",
                    new AcceptableValueRange<float>(0.001f, 0.050f)));

            ScopeUseCustomColor = Config.Bind("Thermal Optic", "Use Custom Color (?)", false,
                new ConfigDescription("Warning:\n These values will cover each thermal scopes modes in raid.\n" +
                                      "One set of values won't fit different scopes or color modes.\n" +
                                      "To recover, refer to Use Recover Default",
                    null, new ConfigurationManagerAttributes { IsAdvanced = true }));
            ScopeMainTexColorCoef = Config.Bind("Thermal Optic", "Use MainTexColorCoef - Brightness", 0.5f,
                new ConfigDescription("", new AcceptableValueRange<float>(0.01f, 1f), new ConfigurationManagerAttributes { IsAdvanced = true }));
            ScopeMinTempValue = Config.Bind("Thermal Optic", "Use MinTempValue - ColorDiff", 0.2f,
                new ConfigDescription("", new AcceptableValueRange<float>(-1f, 1f), new ConfigurationManagerAttributes { IsAdvanced = true }));
            ScopeRampShift = Config.Bind("Thermal Optic", "Use RampShift - ColorShift", -0.5f,
                new ConfigDescription("", new AcceptableValueRange<float>(-1f, 1f), new ConfigurationManagerAttributes { IsAdvanced = true }));
            ApplyReadColorOnce = Config.Bind("Thermal Optic", "Use Recover Default (?)", false,
                new ConfigDescription("Enable to reset the scope's default values.\n Close menu and use current thermal scope to apply.\n" +
                                      "Only cache values of first thermal scope mode used in raid.\n To recover >1 scopes (modes) you need to go next raid and disable Use Custom Color.",
                    null, new ConfigurationManagerAttributes { IsAdvanced = true }));

            T7Fps = Config.Bind("T7 Thermal", "FPS Limit", false);
            T7Glitch = Config.Bind("T7 Thermal", "Glitch effect", false);
            T7Motion = Config.Bind("T7 Thermal", "Motion Blur", false);
            T7Noise = Config.Bind("T7 Thermal", "Noise", false);
            T7Pixel = Config.Bind("T7 Thermal", "Pixelation", false);
            T7Blur = Config.Bind("T7 Thermal", "Blur", false);
            T7BlockScope = Config.Bind("T7 Thermal", "Block Optic Scope (?)", false,
                new ConfigDescription("Keep ordinary optic glass opaque and incompatible with helmet thermal. " +
                    "Native thermal scopes still use the Thermal Optic settings."));
            T7BlockGlass = Config.Bind("T7 Thermal", "Block Glass (?)", false,
                new ConfigDescription("Keep environmental glass thermally opaque. This does not block optic or collimator materials."));
            T7DepthFade = Config.Bind("T7 Thermal", "Depth Fade (?)", 0.01f,
                new ConfigDescription("Lower is more visible on far distance. T7 Default is 0.03",
                    new AcceptableValueRange<float>(0.001f, 0.050f)));
            T7Palette = Config.Bind(
                "T7 Thermal",
                "Thermal Palette",
                ThermalPaletteMode.WhiteHot,
                new ConfigDescription("",
                    new AcceptableValueList<string>(
                        ThermalPaletteMode.WhiteHot,
                        ThermalPaletteMode.BlackHot,
                        ThermalPaletteMode.RedHot,
                        ThermalPaletteMode.Ironbow,
                        ThermalPaletteMode.Fusion,
                        ThermalPaletteMode.Rainbow),
                    new ConfigurationManagerAttributes { Order = 10 }));
            T7ColorPassthrough = Config.Bind(
                "T7 Thermal",
                "Color Passthrough (?)",
                false,
                new ConfigDescription(
                    "Use the BD-T7/S-style full-color heat-bearing buffer. " +
                    "This bypasses palette presentation while enabled."));

            Config.Bind(
                "T7 Thermal",
                "Palette Tuning (?)",
                false,
                new ConfigDescription(
                    "Live preview for the selected palette. \nClick Save Palette to write these values to the config." +
                    "Unsaved slider changes are discarded when you switch palettes.",
                    null,
                    new ConfigurationManagerAttributes
                    {
                        CustomDrawer = DrawPaletteTuning,
                        HideDefaultButton = true,
                        HideSettingName = false,
                        IsAdvanced = true,
                        Order = 9
                    }));

            NVNoise = Config.Bind("Night Vision", "Noise", false);
            NVScopeDefault = Config.Bind("Night Vision", "Optic Bright Default (?)", false,
                new ConfigDescription("Whether enable vanilla scope vision brightness when using helmet NV, which is darker in zoomable scopes"));
            NVScopeBrightness = Config.Bind("Night Vision", "Optic Brightness (?)", 1.0f,
                new ConfigDescription("Need disable Optic Bright Default",
                    new AcceptableValueRange<float>(0.5f, 3f)));
            NVT7Mask = Config.Bind("Black Screen Mask", "Helmet NV & T7", false);

            ScopeFps.SettingChanged += OnThermalOpticSettingChanged;
            ScopeGlitch.SettingChanged += OnThermalOpticSettingChanged;
            ScopeMotion.SettingChanged += OnThermalOpticSettingChanged;
            ScopeNoise.SettingChanged += OnThermalOpticSettingChanged;
            ScopePixel.SettingChanged += OnThermalOpticSettingChanged;
            ScopeChromatic.SettingChanged += OnThermalOpticSettingChanged;
            ScopeBlur.SettingChanged += OnThermalOpticSettingChanged;
            ScopeMaxDistance.SettingChanged += OnThermalOpticSettingChanged;
            ScopeDepthFade.SettingChanged += OnThermalOpticSettingChanged;
            ScopeUseCustomColor.SettingChanged += OnThermalOpticSettingChanged;
            ScopeMainTexColorCoef.SettingChanged += OnThermalOpticSettingChanged;
            ScopeMinTempValue.SettingChanged += OnThermalOpticSettingChanged;
            ScopeRampShift.SettingChanged += OnThermalOpticSettingChanged;
            ApplyReadColorOnce.SettingChanged += OnThermalOpticSettingChanged;

            BindT7PaletteConfig(ThermalPaletteMode.WhiteHot, 0.15f, 0.2f, -0.1f);
            BindT7PaletteConfig(ThermalPaletteMode.BlackHot, 0.45f, 0.5f, -0.9f);
            BindT7PaletteConfig(ThermalPaletteMode.RedHot, 0.7f, 0.05f, -0.45f);
            BindT7PaletteConfig(ThermalPaletteMode.Ironbow, 0.7f, 0.07f, 0.07f);
            BindT7PaletteConfig(ThermalPaletteMode.Fusion, 0.8f, 0.1f, 0.05f);
            BindT7PaletteConfig(ThermalPaletteMode.Rainbow, 0.8f, 0.1f, 0.05f);
            LoadWorkingFromPalette(T7Palette.Value);

            T7Palette.SettingChanged += OnT7PaletteChanged;
            T7ColorPassthrough.SettingChanged += OnT7PassthroughSettingChanged;
            T7Fps.SettingChanged += OnT7ClaritySettingChanged;
            T7Glitch.SettingChanged += OnT7ClaritySettingChanged;
            T7Motion.SettingChanged += OnT7ClaritySettingChanged;
            T7Noise.SettingChanged += OnT7ClaritySettingChanged;
            T7Pixel.SettingChanged += OnT7ClaritySettingChanged;
            T7Blur.SettingChanged += OnT7ClaritySettingChanged;
            T7DepthFade.SettingChanged += OnT7ClaritySettingChanged;

            NVNoise.SettingChanged += OnNVNoiseSettingChanged;
            NVScopeDefault.SettingChanged += OnNVScopeSettingChanged;
            NVScopeBrightness.SettingChanged += OnNVScopeSettingChanged;
            NVT7Mask.SettingChanged += OnMaskSettingChanged;

            _harmony = new Harmony("ciallo.BetterThermalNightVision");
            _harmony.PatchAll();
            ThermalSightOverrides.RebuildLoadedSceneCache();
        }

        internal static void ResetScopeColorCache()
        {
            ScopeColorInitialized = false;
            ScopeRead_MainTexColorCoef = 0f;
            ScopeRead_MinTempValue = 0f;
            ScopeRead_RampShift = 0f;
        }

        private void BindT7PaletteConfig(
            string palette,
            float mainTexColorCoef,
            float minimumTemperatureValue,
            float rampShift)
        {
            var hidden = new ConfigurationManagerAttributes { Browsable = false };
            string section = "T7 Palette - " + palette;
            T7PaletteConfigs[palette] = new ThermalPaletteConfig
            {
                MainTexColorCoef = Config.Bind(
                    section,
                    "MainTexColorCoef",
                    mainTexColorCoef,
                    new ConfigDescription(
                        string.Empty,
                        new AcceptableValueRange<float>(0.01f, 1f),
                        hidden)),
                MinimumTemperatureValue = Config.Bind(
                    section,
                    "MinimumTemperatureValue",
                    minimumTemperatureValue,
                    new ConfigDescription(
                        string.Empty,
                        new AcceptableValueRange<float>(-1f, 1f),
                        hidden)),
                RampShift = Config.Bind(
                    section,
                    "RampShift",
                    rampShift,
                    new ConfigDescription(
                        string.Empty,
                        new AcceptableValueRange<float>(-1f, 1f),
                        hidden))
            };
        }

        private void ForcePaletteValues(
            string palette,
            float mainTexColorCoef,
            float minimumTemperatureValue,
            float rampShift)
        {
            ThermalPaletteConfig paletteConfig;
            if (!T7PaletteConfigs.TryGetValue(palette, out paletteConfig))
                return;

            bool previousSaveOnConfigSet = Config.SaveOnConfigSet;
            Config.SaveOnConfigSet = false;
            try
            {
                paletteConfig.MainTexColorCoef.Value = mainTexColorCoef;
                paletteConfig.MinimumTemperatureValue.Value = minimumTemperatureValue;
                paletteConfig.RampShift.Value = rampShift;
            }
            finally
            {
                Config.SaveOnConfigSet = previousSaveOnConfigSet;
            }
        }

        private static void LoadWorkingFromPalette(string palette)
        {
            ThermalPaletteConfig paletteConfig;
            if (!T7PaletteConfigs.TryGetValue(palette, out paletteConfig))
                return;

            T7WorkingMainTexColorCoef = paletteConfig.MainTexColorCoef.Value;
            T7WorkingMinimumTemperatureValue = paletteConfig.MinimumTemperatureValue.Value;
            T7WorkingRampShift = paletteConfig.RampShift.Value;
        }

        private void SaveWorkingToPalette(string palette)
        {
            ThermalPaletteConfig paletteConfig;
            if (!T7PaletteConfigs.TryGetValue(palette, out paletteConfig))
                return;

            bool previousSaveOnConfigSet = Config.SaveOnConfigSet;
            Config.SaveOnConfigSet = false;
            try
            {
                paletteConfig.MainTexColorCoef.Value = T7WorkingMainTexColorCoef;
                paletteConfig.MinimumTemperatureValue.Value = T7WorkingMinimumTemperatureValue;
                paletteConfig.RampShift.Value = T7WorkingRampShift;
            }
            finally
            {
                Config.SaveOnConfigSet = previousSaveOnConfigSet;
            }

            Config.Save();
            Log("Saved palette settings for " + palette);
        }

        private static bool PaletteTuningDirty()
        {
            ThermalPaletteConfig paletteConfig;
            if (T7Palette == null || !T7PaletteConfigs.TryGetValue(T7Palette.Value, out paletteConfig))
                return false;

            return !Mathf.Approximately(paletteConfig.MainTexColorCoef.Value, T7WorkingMainTexColorCoef)
                || !Mathf.Approximately(paletteConfig.MinimumTemperatureValue.Value, T7WorkingMinimumTemperatureValue)
                || !Mathf.Approximately(paletteConfig.RampShift.Value, T7WorkingRampShift);
        }

        private static void DrawPaletteTuning(ConfigEntryBase entry)
        {
            if (T7Palette == null)
                return;

            GUILayout.BeginVertical();
            GUILayout.Label("Tuning: " + T7Palette.Value + (PaletteTuningDirty() ? " (unsaved)" : string.Empty));

            bool changed = false;
            DrawPaletteSlider("MainTexColorCoef - Brightness", ref T7WorkingMainTexColorCoef, 0.01f, 1f, ref changed);
            DrawPaletteSlider("MinTempValue - ColorDiff", ref T7WorkingMinimumTemperatureValue, -1f, 1f, ref changed);
            DrawPaletteSlider("RampShift - ColorShift", ref T7WorkingRampShift, -1f, 1f, ref changed);

            if (changed)
                ApplyT7Color();

            if (GUILayout.Button(PaletteTuningDirty() ? "Save Palette" : "Palette Saved")
                && PaletteTuningDirty())
            {
                Instance?.SaveWorkingToPalette(T7Palette.Value);
            }

            GUILayout.EndVertical();
        }

        private static void DrawPaletteSlider(string label, ref float value, float min, float max, ref bool changed)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(200));
            float slider = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            GUILayout.Label(slider.ToString("0.000"), GUILayout.Width(48));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(slider, value))
            {
                value = slider;
                changed = true;
            }
        }

        internal static void ReleaseT7ClarityTarget(ThermalVision thermal)
        {
            if (!object.ReferenceEquals(_t7ClarityTarget, thermal))
                return;

            _t7ClarityTarget = null;
            _t7OriginalUtilities = null;
            _t7OriginalUnsharpRadiusBlur = 0f;
            _t7OriginalUnsharpBias = 0f;
        }

        private static void RestoreT7Clarity()
        {
            ThermalVision thermal = _t7ClarityTarget;
            ThermalVisionUtilities utilities = _t7OriginalUtilities;
            if (thermal == null)
            {
                ReleaseT7ClarityTarget(thermal);
                return;
            }

            thermal.IsFpsStuck = _t7OriginalIsFpsStuck;
            thermal.IsGlitch = _t7OriginalIsGlitch;
            thermal.IsMotionBlurred = _t7OriginalIsMotionBlurred;
            thermal.IsNoisy = _t7OriginalIsNoisy;
            thermal.IsPixelated = _t7OriginalIsPixelated;
            thermal.UnsharpRadiusBlur = _t7OriginalUnsharpRadiusBlur;
            thermal.UnsharpBias = _t7OriginalUnsharpBias;
            if (utilities != null)
                utilities.DepthFade = _t7OriginalDepthFade;
            thermal.SetMaterialProperties();
            ReleaseT7ClarityTarget(thermal);
        }

        private static void OnT7PaletteChanged(object sender, System.EventArgs args)
        {
            LoadWorkingFromPalette(T7Palette.Value);
            ApplyT7Color();
        }

        private static void ApplyT7Color()
        {
            if (!CameraClass.Exist)
                return;

            ThermalVision thermal = CameraClass.Instance?.ThermalVision;
            T7Color.Apply(thermal);
            thermal?.SetMaterialProperties();
            OpticThermalInheritance.Sync(thermal);
        }

        private static void OnT7ClaritySettingChanged(object sender, System.EventArgs args)
        {
            if (CameraClass.Exist)
            {
                ThermalVision thermal = CameraClass.Instance?.ThermalVision;
                ApplyT7Clarity(thermal);
                OpticThermalInheritance.Sync(thermal);
            }
        }

        private static void OnThermalOpticSettingChanged(object sender, System.EventArgs args)
        {
            ThermalOpticSettings.Refresh();
        }

        private static void OnNVNoiseSettingChanged(object sender, System.EventArgs args)
        {
            ApplyNVNoise(CameraClass.Exist ? CameraClass.Instance?.NightVision : null, false);
        }

        private static void OnNVScopeSettingChanged(object sender, System.EventArgs args)
        {
            NVScopeBright.Rebuild();
        }

        private static void OnMaskSettingChanged(object sender, System.EventArgs args)
        {
            ApplyMaskSetting();
        }

        private static void OnT7PassthroughSettingChanged(object sender, System.EventArgs args)
        {
            if (!CameraClass.Exist)
                return;

            ThermalVision thermal = CameraClass.Instance?.ThermalVision;
            T7Color.Apply(thermal);
            ApplyT7Clarity(thermal);
            OpticThermalInheritance.Sync(thermal);
        }

        internal static void ApplyT7Clarity(ThermalVision thermal)
        {
            if (thermal == null)
                return;

            if (_t7ClarityTarget != thermal)
            {
                RestoreT7Clarity();
                _t7ClarityTarget = thermal;
                _t7OriginalUtilities = thermal.ThermalVisionUtilities;
                _t7OriginalIsFpsStuck = thermal.IsFpsStuck;
                _t7OriginalIsGlitch = thermal.IsGlitch;
                _t7OriginalIsMotionBlurred = thermal.IsMotionBlurred;
                _t7OriginalIsNoisy = thermal.IsNoisy;
                _t7OriginalIsPixelated = thermal.IsPixelated;
                _t7OriginalDepthFade = _t7OriginalUtilities != null
                    ? _t7OriginalUtilities.DepthFade
                    : 0f;
                _t7OriginalUnsharpRadiusBlur = thermal.UnsharpRadiusBlur;
                _t7OriginalUnsharpBias = thermal.UnsharpBias;
            }

            thermal.IsFpsStuck = T7Fps.Value;
            thermal.IsGlitch = T7Glitch.Value;
            thermal.IsMotionBlurred = T7Motion.Value;
            thermal.IsNoisy = T7Noise.Value;
            thermal.IsPixelated = T7Pixel.Value;
            if (thermal.ThermalVisionUtilities != null)
                thermal.ThermalVisionUtilities.DepthFade = T7DepthFade.Value;

            thermal.UnsharpRadiusBlur = T7Blur.Value
                ? _t7OriginalUnsharpRadiusBlur
                : 0f;
            thermal.UnsharpBias = T7Blur.Value
                ? _t7OriginalUnsharpBias
                : 0f;
            thermal.SetMaterialProperties();
        }

        internal static void ApplyNVNoise(NightVision nightVision, bool captureNative)
        {
            NightVisionSettings.ApplyNoise(nightVision, captureNative);
        }

        internal static void ApplyMaskSetting()
        {
            if (!CameraClass.Exist)
                return;

            ThermalVision thermal = CameraClass.Instance?.ThermalVision;
            if (thermal != null && thermal.TextureMask != null)
                thermal.TextureMask.TryToEnable(thermal, thermal.On && NVT7Mask.Value);

            NightVision nightVision = CameraClass.Instance?.NightVision;
            if (nightVision != null && nightVision.TextureMask != null)
                nightVision.TextureMask.TryToEnable(nightVision, nightVision.On && NVT7Mask.Value);
        }

        internal static bool IsMainNightVision(NightVision nightVision)
        {
            if (nightVision == null)
                return false;
            if (CameraClass.Exist && object.ReferenceEquals(
                    nightVision,
                    CameraClass.Instance?.NightVision))
            {
                return true;
            }

            return nightVision.gameObject != null
                && nightVision.gameObject.name == "FPS Camera";
        }

        internal static void RestoreNativeMasks()
        {
            if (!CameraClass.Exist)
                return;

            ThermalVision thermal = CameraClass.Instance?.ThermalVision;
            if (thermal != null && thermal.TextureMask != null)
                thermal.TextureMask.TryToEnable(thermal, thermal.On);

            NightVision nightVision = CameraClass.Instance?.NightVision;
            if (nightVision != null && nightVision.TextureMask != null)
                nightVision.TextureMask.TryToEnable(nightVision, nightVision.On);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ThermalReticleExposure.PruneDestroyedSceneReferences();
            ThermalSightOverrides.RebuildLoadedSceneCache();
        }

        private static void OnSceneUnloaded(Scene scene)
        {
            ThermalReticleExposure.PruneDestroyedSceneReferences();
            ThermalSightOverrides.RebuildLoadedSceneCache();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            T7Palette.SettingChanged -= OnT7PaletteChanged;
            T7ColorPassthrough.SettingChanged -= OnT7PassthroughSettingChanged;
            T7PaletteConfigs.Clear();
            T7Fps.SettingChanged -= OnT7ClaritySettingChanged;
            T7Glitch.SettingChanged -= OnT7ClaritySettingChanged;
            T7Motion.SettingChanged -= OnT7ClaritySettingChanged;
            T7Noise.SettingChanged -= OnT7ClaritySettingChanged;
            T7Pixel.SettingChanged -= OnT7ClaritySettingChanged;
            T7Blur.SettingChanged -= OnT7ClaritySettingChanged;
            T7DepthFade.SettingChanged -= OnT7ClaritySettingChanged;
            ScopeFps.SettingChanged -= OnThermalOpticSettingChanged;
            ScopeGlitch.SettingChanged -= OnThermalOpticSettingChanged;
            ScopeMotion.SettingChanged -= OnThermalOpticSettingChanged;
            ScopeNoise.SettingChanged -= OnThermalOpticSettingChanged;
            ScopePixel.SettingChanged -= OnThermalOpticSettingChanged;
            ScopeChromatic.SettingChanged -= OnThermalOpticSettingChanged;
            ScopeBlur.SettingChanged -= OnThermalOpticSettingChanged;
            ScopeMaxDistance.SettingChanged -= OnThermalOpticSettingChanged;
            ScopeDepthFade.SettingChanged -= OnThermalOpticSettingChanged;
            ScopeUseCustomColor.SettingChanged -= OnThermalOpticSettingChanged;
            ScopeMainTexColorCoef.SettingChanged -= OnThermalOpticSettingChanged;
            ScopeMinTempValue.SettingChanged -= OnThermalOpticSettingChanged;
            ScopeRampShift.SettingChanged -= OnThermalOpticSettingChanged;
            ApplyReadColorOnce.SettingChanged -= OnThermalOpticSettingChanged;
            NVNoise.SettingChanged -= OnNVNoiseSettingChanged;
            NVScopeDefault.SettingChanged -= OnNVScopeSettingChanged;
            NVScopeBrightness.SettingChanged -= OnNVScopeSettingChanged;
            NVT7Mask.SettingChanged -= OnMaskSettingChanged;
            ThermalReticleExposure.Reset();
            OpticThermalInheritance.Reset();
            ThermalOpticSettings.Reset();
            NightVisionSettings.Reset();
            NVScopeBright.Reset();
            T7Color.Reset();
            RestoreT7Clarity();
            ResetScopeColorCache();
            RestoreNativeMasks();
            DestroyRampTexture(ref T7Color.RedHotRampTexture);
            DestroyRampTexture(ref T7Color.IronbowRampTexture);
            _harmony?.UnpatchSelf();
            _harmony = null;
            Instance = null;
        }

        private Texture2D LoadRampTexture(string pluginDir, string fileName, string textureName)
        {
            string path = Path.Combine(pluginDir, fileName);
            if (!File.Exists(path))
                return null;

            byte[] data = File.ReadAllBytes(path);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!texture.LoadImage(data, true))
            {
                Destroy(texture);
                Logger.LogWarning(fileName + " could not be loaded; its palette will use EFT's native White-hot ramp.");
                return null;
            }

            texture.name = textureName;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.anisoLevel = 1;
            return texture;
        }

        private void DestroyRampTexture(ref Texture2D texture)
        {
            if (texture == null)
                return;

            Destroy(texture);
            texture = null;
        }

        private void LateUpdate()
        {
            ThermalReticleExposure.Update();
        }
    }

    internal static class ThermalOpticSettings
    {
        private sealed class Snapshot
        {
            internal ThermalVision Thermal;
            internal ThermalVisionUtilities Utilities;
            internal bool IsFpsStuck;
            internal bool IsGlitch;
            internal bool IsMotionBlurred;
            internal bool IsNoisy;
            internal bool IsPixelated;
            internal float DepthFade;
            internal float MainTexColorCoef;
            internal float MinimumTemperatureValue;
            internal float RampShift;
            internal float UnsharpRadiusBlur;
            internal float UnsharpBias;
            internal ChromaticAberration ChromaticAberration;
            internal float ChromaticAberrationShift;
            internal Camera Camera;
            internal float FarClipPlane;
        }

        private static Snapshot _snapshot;
        private static bool _updatingConfig;

        internal static void RestoreBeforeCopy()
        {
            Reset();
        }

        internal static bool CaptureAndApply(OpticComponentUpdater updater, OpticSight optic)
        {
            ScopeThermalVisionData thermalData = optic?.ScopeData?.ThermalVisionData;
            if (thermalData == null || !thermalData.ThermalVision)
                return false;

            ThermalVision thermal = updater?.GetComponent<ThermalVision>();
            ThermalVisionUtilities utilities = thermal?.ThermalVisionUtilities;
            if (thermal == null || utilities == null)
                return false;

            var values = utilities.ValuesCoefs;
            ChromaticAberration chromatic = thermal.GetComponent<ChromaticAberration>();
            Camera camera = updater.GetComponent<Camera>();
            _snapshot = new Snapshot
            {
                Thermal = thermal,
                Utilities = utilities,
                IsFpsStuck = thermal.IsFpsStuck,
                IsGlitch = thermal.IsGlitch,
                IsMotionBlurred = thermal.IsMotionBlurred,
                IsNoisy = thermal.IsNoisy,
                IsPixelated = thermal.IsPixelated,
                DepthFade = utilities.DepthFade,
                MainTexColorCoef = values?.MainTexColorCoef ?? 0f,
                MinimumTemperatureValue = values?.MinimumTemperatureValue ?? 0f,
                RampShift = values?.RampShift ?? 0f,
                UnsharpRadiusBlur = thermal.UnsharpRadiusBlur,
                UnsharpBias = thermal.UnsharpBias,
                ChromaticAberration = chromatic,
                ChromaticAberrationShift = chromatic != null ? chromatic.Shift : 0f,
                Camera = camera,
                FarClipPlane = camera != null ? camera.farClipPlane : 0f
            };

            if (!BetterVision.ScopeColorInitialized && values != null)
            {
                BetterVision.ScopeRead_MainTexColorCoef = values.MainTexColorCoef;
                BetterVision.ScopeRead_MinTempValue = values.MinimumTemperatureValue;
                BetterVision.ScopeRead_RampShift = values.RampShift;
                BetterVision.ScopeColorInitialized = true;
            }

            ApplyRecoveryRequest();
            ApplyCurrent(false);
            return true;
        }

        internal static void Refresh()
        {
            if (_updatingConfig)
                return;

            ApplyRecoveryRequest();
            ApplyCurrent(true);
        }

        internal static void Reset()
        {
            Snapshot snapshot = _snapshot;
            _snapshot = null;
            if (snapshot == null || snapshot.Thermal == null)
                return;

            snapshot.Thermal.IsFpsStuck = snapshot.IsFpsStuck;
            snapshot.Thermal.IsGlitch = snapshot.IsGlitch;
            snapshot.Thermal.IsMotionBlurred = snapshot.IsMotionBlurred;
            snapshot.Thermal.IsNoisy = snapshot.IsNoisy;
            snapshot.Thermal.IsPixelated = snapshot.IsPixelated;
            snapshot.Thermal.UnsharpRadiusBlur = snapshot.UnsharpRadiusBlur;
            snapshot.Thermal.UnsharpBias = snapshot.UnsharpBias;
            RestoreUtilities(snapshot);

            if (snapshot.ChromaticAberration != null)
                snapshot.ChromaticAberration.Shift = snapshot.ChromaticAberrationShift;
            if (snapshot.Camera != null)
                snapshot.Camera.farClipPlane = snapshot.FarClipPlane;
            if (object.ReferenceEquals(snapshot.Thermal.ThermalVisionUtilities, snapshot.Utilities))
                snapshot.Thermal.SetMaterialProperties();
        }

        internal static void OnThermalDestroyed(ThermalVision thermal)
        {
            if (_snapshot != null && object.ReferenceEquals(_snapshot.Thermal, thermal))
                _snapshot = null;
        }

        private static void ApplyRecoveryRequest()
        {
            if (!BetterVision.ApplyReadColorOnce.Value || !BetterVision.ScopeColorInitialized)
                return;

            _updatingConfig = true;
            try
            {
                BetterVision.ScopeMainTexColorCoef.Value = BetterVision.ScopeRead_MainTexColorCoef;
                BetterVision.ScopeMinTempValue.Value = BetterVision.ScopeRead_MinTempValue;
                BetterVision.ScopeRampShift.Value = BetterVision.ScopeRead_RampShift;
                BetterVision.ApplyReadColorOnce.Value = false;
            }
            finally
            {
                _updatingConfig = false;
            }
        }

        private static void ApplyCurrent(bool syncInheritance)
        {
            Snapshot snapshot = _snapshot;
            if (snapshot == null || snapshot.Thermal == null)
                return;

            snapshot.Thermal.IsFpsStuck = BetterVision.ScopeFps.Value;
            snapshot.Thermal.IsGlitch = BetterVision.ScopeGlitch.Value;
            snapshot.Thermal.IsMotionBlurred = BetterVision.ScopeMotion.Value;
            snapshot.Thermal.IsNoisy = BetterVision.ScopeNoise.Value;
            snapshot.Thermal.IsPixelated = BetterVision.ScopePixel.Value;
            snapshot.Thermal.UnsharpRadiusBlur = BetterVision.ScopeBlur.Value
                ? snapshot.UnsharpRadiusBlur
                : 0f;
            snapshot.Thermal.UnsharpBias = BetterVision.ScopeBlur.Value
                ? snapshot.UnsharpBias
                : 0f;

            snapshot.Utilities.DepthFade = BetterVision.ScopeDepthFade.Value;
            var values = snapshot.Utilities.ValuesCoefs;
            if (values != null)
            {
                values.MainTexColorCoef = BetterVision.ScopeUseCustomColor.Value
                    ? BetterVision.ScopeMainTexColorCoef.Value
                    : snapshot.MainTexColorCoef;
                values.MinimumTemperatureValue = BetterVision.ScopeUseCustomColor.Value
                    ? BetterVision.ScopeMinTempValue.Value
                    : snapshot.MinimumTemperatureValue;
                values.RampShift = BetterVision.ScopeUseCustomColor.Value
                    ? BetterVision.ScopeRampShift.Value
                    : snapshot.RampShift;
            }

            if (snapshot.ChromaticAberration != null)
            {
                snapshot.ChromaticAberration.Shift = BetterVision.ScopeChromatic.Value
                    ? snapshot.ChromaticAberrationShift
                    : 0f;
            }
            if (snapshot.Camera != null)
                snapshot.Camera.farClipPlane = BetterVision.ScopeMaxDistance.Value;

            snapshot.Thermal.SetMaterialProperties();
            if (syncInheritance && CameraClass.Exist)
                OpticThermalInheritance.Sync(CameraClass.Instance?.ThermalVision);
        }

        private static void RestoreUtilities(Snapshot snapshot)
        {
            if (snapshot.Utilities == null)
                return;

            snapshot.Utilities.DepthFade = snapshot.DepthFade;
            var values = snapshot.Utilities.ValuesCoefs;
            if (values == null)
                return;

            values.MainTexColorCoef = snapshot.MainTexColorCoef;
            values.MinimumTemperatureValue = snapshot.MinimumTemperatureValue;
            values.RampShift = snapshot.RampShift;
        }
    }

    internal static class NightVisionSettings
    {
        private static NightVision _target;
        private static float _nativeNoiseIntensity;
        private static float _nativeNoiseScale;

        internal static void ApplyNoise(NightVision nightVision, bool captureNative)
        {
            if (nightVision == null)
                return;

            if (!object.ReferenceEquals(_target, nightVision) || captureNative)
            {
                _target = nightVision;
                _nativeNoiseIntensity = nightVision.NoiseIntensity;
                _nativeNoiseScale = nightVision.NoiseScale;
            }

            nightVision.NoiseIntensity = BetterVision.NVNoise.Value
                ? _nativeNoiseIntensity
                : 0f;
            nightVision.NoiseScale = BetterVision.NVNoise.Value
                ? _nativeNoiseScale
                : 0f;
            nightVision.ApplySettings();
        }

        internal static void Reset()
        {
            if (_target != null)
            {
                _target.NoiseIntensity = _nativeNoiseIntensity;
                _target.NoiseScale = _nativeNoiseScale;
                _target.ApplySettings();
            }

            _target = null;
            _nativeNoiseIntensity = 0f;
            _nativeNoiseScale = 0f;
        }

        internal static void OnDestroyed(NightVision nightVision)
        {
            if (!object.ReferenceEquals(_target, nightVision))
                return;

            _target = null;
            _nativeNoiseIntensity = 0f;
            _nativeNoiseScale = 0f;
        }
    }

    [HarmonyPatch(typeof(OpticComponentUpdater), "CopyComponentFromOptic")]
    public class Patch_OpticThermal
    {
        static void Prefix()
        {
            ThermalOpticSettings.RestoreBeforeCopy();
        }

        static void Postfix(OpticComponentUpdater __instance, OpticSight opticSight)
        {
            ThermalReticleExposure.TrackOptic(opticSight);
            ThermalOpticSettings.CaptureAndApply(__instance, opticSight);
            OpticThermalInheritance.Register(__instance, opticSight);
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

            T7Color.Apply(tv);
            BetterVision.ApplyT7Clarity(tv);
        }
    }

    [HarmonyPatch(typeof(PlayerCameraController), "method_4")]
    public class Patch_NV_Noise
    {
        static void Postfix(PlayerCameraController __instance)
        {
            NightVision nv = CameraClass.Instance.NightVision;
            if (nv == null || !nv.enabled)
                return;

            bool captureNative = __instance?.Player?.NightVisionObserver?.Component != null;
            BetterVision.ApplyNVNoise(nv, captureNative);
        }
    }

    [HarmonyPatch(typeof(ThermalVision), "OnPreCull")]
    public class Patch_Mask
    {
        static void Prefix(ThermalVision __instance)
        {
            if (!ThermalReticleExposure.IsFpsCamera(__instance))
                return;

            if (__instance.TextureMask != null)
            {
                __instance.TextureMask.TryToEnable(
                    __instance,
                    __instance.On && BetterVision.NVT7Mask.Value);
            }
        }
    }

    [HarmonyPatch(typeof(NightVision), "OnPreCull")]
    public class Patch_NV_Mask
    {
        static void Prefix(NightVision __instance)
        {
            if (!BetterVision.IsMainNightVision(__instance))
                return;

            if (__instance.TextureMask != null)
            {
                __instance.TextureMask.TryToEnable(
                    __instance,
                    __instance.On && BetterVision.NVT7Mask.Value);
            }
        }
    }

    [HarmonyPatch]
    internal static class NightVisionSwitchCompletePatch
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(NightVision), "method_1", new[] { typeof(bool) });
        }

        private static void Postfix()
        {
            BetterVision.ApplyMaskSetting();
            NVScopeBright.Rebuild();
        }
    }

    [HarmonyPatch(typeof(NightVision), nameof(NightVision.OnDestroy))]
    internal static class NightVisionDestroyPatch
    {
        private static void Prefix(NightVision __instance)
        {
            if (BetterVision.IsMainNightVision(__instance))
                NVScopeBright.Clear();
            NightVisionSettings.OnDestroyed(__instance);
        }
    }

    [HarmonyPatch(typeof(NightVision), nameof(NightVision.OnDisable))]
    internal static class NightVisionDisablePatch
    {
        private static void Prefix(NightVision __instance)
        {
            if (BetterVision.IsMainNightVision(__instance))
                NVScopeBright.Clear();
        }
    }
}
