using EFT.InventoryLogic;
using System.Collections.Generic;
using UnityEngine;

namespace BetterVision
{
    internal static class T7Color
    {
        internal static Texture2D RedHotRampTexture;
        internal static Texture2D IronbowRampTexture;
        private static readonly Dictionary<RampTexPalletteConnector, Texture> OriginalWhiteHotTextures =
            new Dictionary<RampTexPalletteConnector, Texture>();
        private static ThermalVision _activeThermal;
        private static ThermalVisionUtilities _activeUtilities;
        private static ThermalVisionComponent.SelectablePalette _originalPalette;
        private static float _originalMainTexColorCoef;
        private static float _originalMinimumTemperatureValue;
        private static float _originalRampShift;
        private static bool _hasOriginalPresentation;

        private class ThermalParams
        {
            public ThermalVisionComponent.SelectablePalette RampPalette;
            public float MainTexColorCoef;
            public float MinimumTemperatureValue;
            public float RampShift;
        }

        // EFT's BlackHot enum slot presents as white-hot on the current game build.
        internal static void Apply(ThermalVision tv)
        {
            if (tv == null || tv.ThermalVisionUtilities == null)
                return;

            if ((!object.ReferenceEquals(_activeThermal, null)
                    && !object.ReferenceEquals(_activeThermal, tv))
                || (!object.ReferenceEquals(_activeUtilities, null)
                    && !object.ReferenceEquals(_activeUtilities, tv.ThermalVisionUtilities)))
            {
                Reset();
            }
            _activeThermal = tv;

            var util = tv.ThermalVisionUtilities;
            var vc = util.ValuesCoefs;
            if (!_hasOriginalPresentation)
            {
                _activeUtilities = util;
                _originalPalette = util.CurrentRampPalette;
                if (vc != null)
                {
                    _originalMainTexColorCoef = vc.MainTexColorCoef;
                    _originalMinimumTemperatureValue = vc.MinimumTemperatureValue;
                    _originalRampShift = vc.RampShift;
                }
                _hasOriginalPresentation = true;
            }

            string selectedPalette = BetterVision.T7Palette.Value;
            ThermalVisionComponent.SelectablePalette nativePalette;
            switch (selectedPalette)
            {
                case ThermalPaletteMode.BlackHot:
                case ThermalPaletteMode.RedHot:
                case ThermalPaletteMode.Ironbow:
                    nativePalette = ThermalVisionComponent.SelectablePalette.WhiteHot;
                    break;
                case ThermalPaletteMode.Fusion:
                    nativePalette = ThermalVisionComponent.SelectablePalette.Fusion;
                    break;
                case ThermalPaletteMode.Rainbow:
                    nativePalette = ThermalVisionComponent.SelectablePalette.Rainbow;
                    break;
                default:
                    nativePalette = ThermalVisionComponent.SelectablePalette.BlackHot;
                    break;
            }

            var preset = new ThermalParams
            {
                RampPalette = nativePalette,
                MainTexColorCoef = BetterVision.T7WorkingMainTexColorCoef,
                MinimumTemperatureValue = BetterVision.T7WorkingMinimumTemperatureValue,
                RampShift = BetterVision.T7WorkingRampShift
            };

            util.CurrentRampPalette = preset.RampPalette;

            if (util.RampTexPalletteConnectors != null)
            {
                foreach (var connector in util.RampTexPalletteConnectors)
                {
                    if (connector == null ||
                        connector.SelectablePalette != ThermalVisionComponent.SelectablePalette.WhiteHot)
                        continue;

                    Texture overrideTexture = null;
                    if (BetterVision.T7Palette.Value == ThermalPaletteMode.RedHot)
                        overrideTexture = RedHotRampTexture;
                    else if (BetterVision.T7Palette.Value == ThermalPaletteMode.Ironbow)
                        overrideTexture = IronbowRampTexture;

                    if (overrideTexture != null)
                    {
                        if (!OriginalWhiteHotTextures.ContainsKey(connector))
                            OriginalWhiteHotTextures.Add(connector, connector.Texture);
                        connector.Texture = overrideTexture;
                    }
                    else if (OriginalWhiteHotTextures.TryGetValue(connector, out Texture originalTexture))
                    {
                        connector.Texture = originalTexture;
                        OriginalWhiteHotTextures.Remove(connector);
                    }
                }
            }

            if (vc != null)
            {
                vc.MainTexColorCoef = preset.MainTexColorCoef;
                vc.MinimumTemperatureValue = preset.MinimumTemperatureValue;
                vc.RampShift = preset.RampShift;
            }
        }

        internal static void Reset()
        {
            if (_hasOriginalPresentation && _activeUtilities != null)
            {
                _activeUtilities.CurrentRampPalette = _originalPalette;
                var values = _activeUtilities.ValuesCoefs;
                if (values != null)
                {
                    values.MainTexColorCoef = _originalMainTexColorCoef;
                    values.MinimumTemperatureValue = _originalMinimumTemperatureValue;
                    values.RampShift = _originalRampShift;
                }
            }

            foreach (var entry in OriginalWhiteHotTextures)
            {
                if (entry.Key != null)
                    entry.Key.Texture = entry.Value;
            }
            OriginalWhiteHotTextures.Clear();
            _activeThermal = null;
            _activeUtilities = null;
            _hasOriginalPresentation = false;
        }

        internal static void Release(ThermalVision thermal)
        {
            if (object.ReferenceEquals(_activeThermal, thermal))
                Reset();
        }
    }
}
