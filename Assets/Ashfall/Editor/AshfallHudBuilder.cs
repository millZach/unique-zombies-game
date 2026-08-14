using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Ashfall.Core;
using Ashfall.UI;
using Ashfall.World;

namespace Ashfall.EditorTools
{
    /// <summary>
    /// Constructs the HUD and pause menu hierarchies and wires them to the controllers.
    ///
    /// Building the UI here rather than at runtime means it is a real, inspectable part
    /// of the scene -- a designer can nudge a panel without touching code -- while still
    /// being reproducible with one menu command.
    /// </summary>
    public static class AshfallHudBuilder
    {
        private const string SpriteFolder = "Assets/Ashfall/Art/Generated/UI";

        private static Sprite _white;
        private static Sprite _ring;
        private static Sprite _vignette;
        private static Sprite _hitMarker;
        private static Sprite _softPanel;
        private static TMP_FontAsset _font;

        public class Result
        {
            public Canvas Canvas;
            public HudController Hud;
            public PauseMenu Pause;
        }

        public static Result Build(Transform parent, GameDirector director)
        {
            EnsureSprites();
            _font = ResolveFont();

            var canvasGo = new GameObject("HUD Canvas");
            canvasGo.transform.SetParent(parent, false);
            canvasGo.layer = LayerMask.NameToLayer("UI");

            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            var hud = canvasGo.AddComponent<HudController>();
            var pause = canvasGo.AddComponent<PauseMenu>();

            var hudSerialized = new SerializedObject(hud);
            hudSerialized.FindProperty("director").objectReferenceValue = director;

            BuildFullScreenLayers(canvasGo.transform, hudSerialized);
            BuildRoundBlock(canvasGo.transform, hudSerialized);
            BuildSalvageBlock(canvasGo.transform, hudSerialized);
            BuildVitals(canvasGo.transform, hudSerialized);
            BuildWeaponBlock(canvasGo.transform, hudSerialized);
            BuildCrosshair(canvasGo.transform, hudSerialized);
            BuildCentreMessaging(canvasGo.transform, hudSerialized);
            BuildPowerUpChips(canvasGo.transform, hudSerialized);
            BuildLegend(canvasGo.transform, hudSerialized);

            hudSerialized.ApplyModifiedPropertiesWithoutUndo();

            BuildPauseMenu(canvasGo.transform, pause, director);

            return new Result { Canvas = canvas, Hud = hud, Pause = pause };
        }

        // ------------------------------------------------------------------
        // Widget helpers
        // ------------------------------------------------------------------

        private static RectTransform Rect(
            Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI");

            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        private static Image Panel(RectTransform rect, Color color, Sprite sprite = null)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite != null ? sprite : _white;
            image.color = color;
            image.type = Image.Type.Simple;
            image.raycastTarget = false;
            return image;
        }

        private static TextMeshProUGUI Label(
            RectTransform rect, string text, float fontSize, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left,
            FontStyles style = FontStyles.Normal, float characterSpacing = 0f)
        {
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            if (_font != null)
            {
                label.font = _font;
            }

            label.text = text;
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = alignment;
            label.fontStyle = style;
            label.characterSpacing = characterSpacing;
            label.raycastTarget = false;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.overflowMode = TextOverflowModes.Overflow;
            return label;
        }

        private static CanvasGroup Group(RectTransform rect)
        {
            var group = rect.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            return group;
        }

        // ------------------------------------------------------------------
        // Sections
        // ------------------------------------------------------------------

        private static void BuildFullScreenLayers(Transform parent, SerializedObject hud)
        {
            RectTransform Full(string name)
            {
                RectTransform rect = Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                return rect;
            }

            Image damage = Panel(Full("DamageVignette"), new Color(0.62f, 0.06f, 0.06f, 0f), _vignette);
            Image lowHealth = Panel(Full("LowHealthVignette"), new Color(0.75f, 0.10f, 0.10f, 0f), _vignette);
            Image exposure = Panel(Full("ExposureVignette"), new Color(AshfallPalette.StormTeal.r, AshfallPalette.StormTeal.g, AshfallPalette.StormTeal.b, 0f), _vignette);
            Image phaseFlash = Panel(Full("PhaseFlash"), new Color(AshfallPalette.StormTeal.r, AshfallPalette.StormTeal.g, AshfallPalette.StormTeal.b, 0f));

            hud.FindProperty("damageVignette").objectReferenceValue = damage;
            hud.FindProperty("lowHealthVignette").objectReferenceValue = lowHealth;
            hud.FindProperty("exposureVignette").objectReferenceValue = exposure;
            hud.FindProperty("phaseFlash").objectReferenceValue = phaseFlash;

            RectTransform exposureRect = Rect(parent, "ExposureLabel",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -210f), new Vector2(620f, 34f));
            TextMeshProUGUI exposureLabel = Label(exposureRect, "STORM EXPOSURE", 26f,
                AshfallPalette.StormTeal, TextAlignmentOptions.Center, FontStyles.Bold, 6f);
            exposureLabel.gameObject.SetActive(false);
            hud.FindProperty("exposureLabel").objectReferenceValue = exposureLabel;
        }

        private static void BuildRoundBlock(Transform parent, SerializedObject hud)
        {
            RectTransform root = Rect(parent, "RoundBlock",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(44f, -38f), new Vector2(400f, 190f));

            RectTransform caption = Rect(root, "Caption",
                Vector2.zero, Vector2.zero, new Vector2(0f, 1f), new Vector2(4f, 0f), new Vector2(300f, 24f));
            TextMeshProUGUI captionLabel = Label(caption, "ROUND / 12", 18f, AshfallPalette.HudInkDim,
                TextAlignmentOptions.Left, FontStyles.Bold, 10f);

            RectTransform number = Rect(root, "Number",
                Vector2.zero, Vector2.zero, new Vector2(0f, 1f), new Vector2(0f, -20f), new Vector2(240f, 92f));
            TextMeshProUGUI numberLabel = Label(number, "01", 84f, AshfallPalette.HudInk,
                TextAlignmentOptions.TopLeft, FontStyles.Bold, -2f);

            RectTransform phase = Rect(root, "Phase",
                Vector2.zero, Vector2.zero, new Vector2(0f, 1f), new Vector2(4f, -108f), new Vector2(360f, 28f));
            TextMeshProUGUI phaseLabel = Label(phase, "STANDBY", 22f, AshfallPalette.EmergencyAmber,
                TextAlignmentOptions.Left, FontStyles.Bold, 8f);

            var pips = new List<Object>();
            for (int i = 0; i < MapPhases.Count; i++)
            {
                RectTransform pip = Rect(root, $"Pip{i}",
                    Vector2.zero, Vector2.zero, new Vector2(0f, 1f),
                    new Vector2(4f + i * 30f, -140f), new Vector2(22f, 5f));
                pips.Add(Panel(pip, new Color(1f, 1f, 1f, 0.25f)));
            }

            RectTransform enemies = Rect(root, "EnemiesLeft",
                Vector2.zero, Vector2.zero, new Vector2(0f, 1f), new Vector2(4f, -158f), new Vector2(300f, 26f));
            TextMeshProUGUI enemiesLabel = Label(enemies, "0 LEFT", 20f, AshfallPalette.HudInkDim,
                TextAlignmentOptions.Left, FontStyles.Bold, 6f);
            enemiesLabel.gameObject.SetActive(false);

            hud.FindProperty("roundCaptionLabel").objectReferenceValue = captionLabel;
            hud.FindProperty("roundNumberLabel").objectReferenceValue = numberLabel;
            hud.FindProperty("phaseLabel").objectReferenceValue = phaseLabel;
            hud.FindProperty("enemiesLabel").objectReferenceValue = enemiesLabel;
            SetArray(hud, "phasePips", pips);
        }

        private static void BuildSalvageBlock(Transform parent, SerializedObject hud)
        {
            RectTransform root = Rect(parent, "SalvageBlock",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-44f, -38f), new Vector2(400f, 130f));

            RectTransform caption = Rect(root, "Caption",
                Vector2.one, Vector2.one, new Vector2(1f, 1f), new Vector2(-4f, 0f), new Vector2(300f, 24f));
            Label(caption, "SALVAGE", 18f, AshfallPalette.HudInkDim, TextAlignmentOptions.Right, FontStyles.Bold, 10f);

            RectTransform amount = Rect(root, "Amount",
                Vector2.one, Vector2.one, new Vector2(1f, 1f), new Vector2(0f, -20f), new Vector2(340f, 72f));
            TextMeshProUGUI amountLabel = Label(amount, "500", 62f, AshfallPalette.SalvageGreen,
                TextAlignmentOptions.TopRight, FontStyles.Bold, -1f);

            RectTransform delta = Rect(root, "Delta",
                Vector2.one, Vector2.one, new Vector2(1f, 1f), new Vector2(-4f, -92f), new Vector2(300f, 30f));
            TextMeshProUGUI deltaLabel = Label(delta, "", 24f, new Color(0.51f, 0.90f, 0.44f, 0f),
                TextAlignmentOptions.Right, FontStyles.Bold, 4f);

            hud.FindProperty("salvageLabel").objectReferenceValue = amountLabel;
            hud.FindProperty("salvageDeltaLabel").objectReferenceValue = deltaLabel;
        }

        private static void BuildVitals(Transform parent, SerializedObject hud)
        {
            RectTransform root = Rect(parent, "Vitals",
                Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(44f, 62f), new Vector2(420f, 66f));

            RectTransform caption = Rect(root, "Caption",
                Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(2f, 40f), new Vector2(200f, 22f));
            Label(caption, "INTEGRITY", 16f, AshfallPalette.HudInkDim, TextAlignmentOptions.Left, FontStyles.Bold, 8f);

            RectTransform track = Rect(root, "Track",
                Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(0f, 8f), new Vector2(340f, 16f));
            Panel(track, new Color(0.04f, 0.05f, 0.06f, 0.82f));

            RectTransform delayed = Rect(track, "Delayed",
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero);
            delayed.offsetMin = new Vector2(2f, 2f);
            delayed.offsetMax = new Vector2(-2f, -2f);
            Image delayedFill = Panel(delayed, new Color(0.62f, 0.16f, 0.16f, 0.85f));
            delayedFill.type = Image.Type.Filled;
            delayedFill.fillMethod = Image.FillMethod.Horizontal;
            delayedFill.fillAmount = 1f;

            RectTransform fill = Rect(track, "Fill",
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, Vector2.zero);
            fill.offsetMin = new Vector2(2f, 2f);
            fill.offsetMax = new Vector2(-2f, -2f);
            Image healthFill = Panel(fill, AshfallPalette.HudInk);
            healthFill.type = Image.Type.Filled;
            healthFill.fillMethod = Image.FillMethod.Horizontal;
            healthFill.fillAmount = 1f;

            RectTransform value = Rect(root, "Value",
                Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(352f, -6f), new Vector2(110f, 44f));
            TextMeshProUGUI healthLabel = Label(value, "150", 34f, AshfallPalette.HudInk,
                TextAlignmentOptions.Left, FontStyles.Bold);

            hud.FindProperty("healthFill").objectReferenceValue = healthFill;
            hud.FindProperty("healthDelayedFill").objectReferenceValue = delayedFill;
            hud.FindProperty("healthLabel").objectReferenceValue = healthLabel;
        }

        private static void BuildWeaponBlock(Transform parent, SerializedObject hud)
        {
            RectTransform root = Rect(parent, "WeaponBlock",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-44f, 58f), new Vector2(460f, 120f));

            RectTransform accent = Rect(root, "AccentBar",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 88f), new Vector2(240f, 4f));
            Image accentBar = Panel(accent, AshfallPalette.EmergencyAmber);

            RectTransform name = Rect(root, "Name",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 58f), new Vector2(420f, 28f));
            TextMeshProUGUI nameLabel = Label(name, "MERIDIAN SIDEARM", 22f, AshfallPalette.HudInkDim,
                TextAlignmentOptions.Right, FontStyles.Bold, 8f);

            RectTransform reserve = Rect(root, "Reserve",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 0f), new Vector2(140f, 52f));
            TextMeshProUGUI reserveLabel = Label(reserve, "/ 168", 30f, AshfallPalette.HudInkDim,
                TextAlignmentOptions.Right, FontStyles.Bold);

            RectTransform ammo = Rect(root, "Ammo",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-150f, -6f), new Vector2(200f, 72f));
            TextMeshProUGUI ammoLabel = Label(ammo, "14", 64f, AshfallPalette.HudInk,
                TextAlignmentOptions.Right, FontStyles.Bold, -1f);

            // Reload ring sits just under the crosshair, where the eye already is.
            RectTransform ring = Rect(parent, "ReloadRing",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -74f), new Vector2(56f, 56f));
            Image reloadRing = Panel(ring, AshfallPalette.HudAccent, _ring);
            reloadRing.type = Image.Type.Filled;
            reloadRing.fillMethod = Image.FillMethod.Radial360;
            reloadRing.fillOrigin = (int)Image.Origin360.Top;
            reloadRing.fillClockwise = true;
            reloadRing.fillAmount = 0f;
            ring.gameObject.SetActive(false);

            hud.FindProperty("weaponNameLabel").objectReferenceValue = nameLabel;
            hud.FindProperty("ammoLabel").objectReferenceValue = ammoLabel;
            hud.FindProperty("reserveLabel").objectReferenceValue = reserveLabel;
            hud.FindProperty("weaponAccentBar").objectReferenceValue = accentBar;
            hud.FindProperty("reloadRing").objectReferenceValue = reloadRing;
        }

        private static void BuildCrosshair(Transform parent, SerializedObject hud)
        {
            RectTransform root = Rect(parent, "Crosshair",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(120f, 120f));
            Group(root);

            RectTransform dotRect = Rect(root, "Dot",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(3f, 3f));
            Image dot = Panel(dotRect, AshfallPalette.HudInk);

            var blades = new List<Object>();
            for (int i = 0; i < 4; i++)
            {
                bool vertical = i < 2;
                RectTransform blade = Rect(root, $"Blade{i}",
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, vertical ? new Vector2(2.5f, 11f) : new Vector2(11f, 2.5f));
                Panel(blade, new Color(AshfallPalette.HudInk.r, AshfallPalette.HudInk.g, AshfallPalette.HudInk.b, 0.9f));
                blades.Add(blade);
            }

            RectTransform marker = Rect(parent, "HitMarker",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(40f, 40f));
            Image markerImage = Panel(marker, AshfallPalette.HudInk, _hitMarker);
            CanvasGroup markerGroup = Group(marker);
            markerGroup.alpha = 0f;

            hud.FindProperty("crosshairRoot").objectReferenceValue = root;
            hud.FindProperty("crosshairDot").objectReferenceValue = dot;
            hud.FindProperty("hitMarker").objectReferenceValue = markerGroup;
            hud.FindProperty("hitMarkerImage").objectReferenceValue = markerImage;
            SetArray(hud, "crosshairBlades", blades);
        }

        private static void BuildCentreMessaging(Transform parent, SerializedObject hud)
        {
            // --- banner ---------------------------------------------------------
            RectTransform bannerRoot = Rect(parent, "Banner",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -96f), new Vector2(1100f, 120f));
            CanvasGroup bannerGroup = Group(bannerRoot);
            bannerGroup.alpha = 0f;

            RectTransform bannerBar = Rect(bannerRoot, "Rule",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 6f), new Vector2(360f, 2f));
            Panel(bannerBar, new Color(AshfallPalette.StormTeal.r, AshfallPalette.StormTeal.g, AshfallPalette.StormTeal.b, 0.55f));

            RectTransform titleRect = Rect(bannerRoot, "Title",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 0f), new Vector2(1100f, 62f));
            TextMeshProUGUI title = Label(titleRect, "ROUND 1", 52f, AshfallPalette.HudInk,
                TextAlignmentOptions.Center, FontStyles.Bold, 14f);

            RectTransform subtitleRect = Rect(bannerRoot, "Subtitle",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -58f), new Vector2(1100f, 34f));
            TextMeshProUGUI subtitle = Label(subtitleRect, "STANDBY", 24f, AshfallPalette.HudInkDim,
                TextAlignmentOptions.Center, FontStyles.Normal, 8f);

            // --- objective -------------------------------------------------------
            RectTransform objectiveRoot = Rect(parent, "Objective",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -232f), new Vector2(1000f, 40f));
            CanvasGroup objectiveGroup = Group(objectiveRoot);
            objectiveGroup.alpha = 0f;

            RectTransform objectiveBg = Rect(objectiveRoot, "Backing",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            objectiveBg.offsetMin = new Vector2(-16f, -6f);
            objectiveBg.offsetMax = new Vector2(16f, 6f);
            Panel(objectiveBg, AshfallPalette.HudPanel);

            RectTransform objectiveTextRect = Rect(objectiveRoot, "Text",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            TextMeshProUGUI objective = Label(objectiveTextRect, "", 24f, AshfallPalette.HudInk,
                TextAlignmentOptions.Center);

            // --- interact prompt ---------------------------------------------------
            RectTransform promptRoot = Rect(parent, "Prompt",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -140f), new Vector2(900f, 56f));
            CanvasGroup promptGroup = Group(promptRoot);
            promptGroup.alpha = 0f;

            RectTransform promptBg = Rect(promptRoot, "Backing",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            promptBg.offsetMin = new Vector2(-14f, -4f);
            promptBg.offsetMax = new Vector2(14f, 4f);
            Panel(promptBg, AshfallPalette.HudPanel);

            RectTransform promptTextRect = Rect(promptRoot, "Text",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, 5f), Vector2.zero);
            TextMeshProUGUI prompt = Label(promptTextRect, "", 26f, AshfallPalette.HudInk,
                TextAlignmentOptions.Center, FontStyles.Bold);

            RectTransform fillTrack = Rect(promptRoot, "HoldTrack",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 4f), new Vector2(320f, 5f));
            Image promptFill = Panel(fillTrack, AshfallPalette.HudAccent);
            promptFill.type = Image.Type.Filled;
            promptFill.fillMethod = Image.FillMethod.Horizontal;
            promptFill.fillAmount = 0f;
            fillTrack.gameObject.SetActive(false);

            hud.FindProperty("bannerGroup").objectReferenceValue = bannerGroup;
            hud.FindProperty("bannerTitle").objectReferenceValue = title;
            hud.FindProperty("bannerSubtitle").objectReferenceValue = subtitle;
            hud.FindProperty("objectiveGroup").objectReferenceValue = objectiveGroup;
            hud.FindProperty("objectiveLabel").objectReferenceValue = objective;
            hud.FindProperty("promptGroup").objectReferenceValue = promptGroup;
            hud.FindProperty("promptLabel").objectReferenceValue = prompt;
            hud.FindProperty("promptFill").objectReferenceValue = promptFill;
        }

        private static void BuildPowerUpChips(Transform parent, SerializedObject hud)
        {
            RectTransform root = Rect(parent, "PowerUps",
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(44f, 0f), new Vector2(320f, 200f));

            SerializedProperty chips = hud.FindProperty("powerUpChips");
            chips.arraySize = 3;

            for (int i = 0; i < 3; i++)
            {
                RectTransform chip = Rect(root, $"Chip{i}",
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(0f, 62f - i * 56f), new Vector2(300f, 46f));
                Image background = Panel(chip, new Color(0.04f, 0.05f, 0.06f, 0.78f));

                RectTransform bar = Rect(chip, "Fill",
                    new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                    new Vector2(0f, 0f), new Vector2(6f, 0f));
                bar.anchoredPosition = new Vector2(3f, 0f);
                Image fill = Panel(bar, AshfallPalette.OverchargeViolet);
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Vertical;
                fill.fillOrigin = (int)Image.OriginVertical.Bottom;
                fill.fillAmount = 1f;

                RectTransform textRect = Rect(chip, "Label",
                    Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                textRect.offsetMin = new Vector2(18f, 0f);
                textRect.offsetMax = new Vector2(-10f, 0f);
                TextMeshProUGUI label = Label(textRect, "OVERCHARGE 20.0s", 19f,
                    AshfallPalette.OverchargeViolet, TextAlignmentOptions.Left, FontStyles.Bold, 3f);

                chip.gameObject.SetActive(false);

                SerializedProperty element = chips.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("root").objectReferenceValue = chip.gameObject;
                element.FindPropertyRelative("background").objectReferenceValue = background;
                element.FindPropertyRelative("fill").objectReferenceValue = fill;
                element.FindPropertyRelative("label").objectReferenceValue = label;
            }
        }

        private static void BuildLegend(Transform parent, SerializedObject hud)
        {
            RectTransform root = Rect(parent, "ControlLegend",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 18f), new Vector2(1500f, 26f));
            TextMeshProUGUI legend = Label(root, "", 17f, AshfallPalette.HudInkDim,
                TextAlignmentOptions.Center, FontStyles.Normal, 2f);
            legend.textWrappingMode = TextWrappingModes.NoWrap;

            hud.FindProperty("legendLabel").objectReferenceValue = legend;
        }

        // ------------------------------------------------------------------
        // Pause menu
        // ------------------------------------------------------------------

        private static void BuildPauseMenu(Transform parent, PauseMenu pause, GameDirector director)
        {
            RectTransform root = Rect(parent, "PauseMenu",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;
            root.SetAsLastSibling();

            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            RectTransform scrim = Rect(root, "Scrim",
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            scrim.offsetMin = Vector2.zero;
            scrim.offsetMax = Vector2.zero;
            Panel(scrim, new Color(0.02f, 0.03f, 0.04f, 0.86f));

            RectTransform card = Rect(root, "Card",
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(820f, 520f));
            Panel(card, new Color(0.05f, 0.065f, 0.078f, 0.96f));

            RectTransform edge = Rect(card, "AccentEdge",
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(3f, 0f), new Vector2(6f, 0f));
            Panel(edge, AshfallPalette.StormTeal);

            RectTransform titleRect = Rect(card, "Title",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -46f), new Vector2(-96f, 62f));
            TextMeshProUGUI title = Label(titleRect, "PAUSED", 50f, AshfallPalette.HudInk,
                TextAlignmentOptions.Left, FontStyles.Bold, 12f);

            RectTransform subtitleRect = Rect(card, "Subtitle",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -106f), new Vector2(-96f, 32f));
            TextMeshProUGUI subtitle = Label(subtitleRect, "ASHFALL: BLACK MERIDIAN", 22f,
                AshfallPalette.HudInkDim, TextAlignmentOptions.Left, FontStyles.Normal, 8f);

            RectTransform rule = Rect(card, "Rule",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -150f), new Vector2(-96f, 2f));
            Panel(rule, new Color(1f, 1f, 1f, 0.10f));

            RectTransform statsRect = Rect(card, "Stats",
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -172f), new Vector2(-96f, 30f));
            TextMeshProUGUI stats = Label(statsRect, "", 20f, AshfallPalette.HudInkDim,
                TextAlignmentOptions.Left, FontStyles.Bold, 3f);

            string[] options = { "RESUME", "RESTART RUN", "QUIT" };
            var entries = new List<PauseMenu.MenuEntry>();

            for (int i = 0; i < options.Length; i++)
            {
                RectTransform row = Rect(card, options[i].Replace(' ', '_'),
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    new Vector2(0f, -240f - i * 68f), new Vector2(-96f, 56f));
                Image background = Panel(row, new Color(1f, 1f, 1f, 0.03f));

                RectTransform barRect = Rect(row, "SelectionBar",
                    new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                    new Vector2(0f, 0f), new Vector2(4f, 0f));
                Image bar = Panel(barRect, new Color(0f, 0f, 0f, 0f));

                RectTransform textRect = Rect(row, "Text",
                    Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                textRect.offsetMin = new Vector2(28f, 0f);
                textRect.offsetMax = new Vector2(-20f, 0f);
                TextMeshProUGUI text = Label(textRect, options[i], 28f, AshfallPalette.HudInkDim,
                    TextAlignmentOptions.Left, FontStyles.Bold, 8f);

                entries.Add(new PauseMenu.MenuEntry
                {
                    label = options[i],
                    root = row,
                    background = background,
                    text = text,
                    selectionBar = bar
                });
            }

            RectTransform hintRect = Rect(card, "Hint",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 28f), new Vector2(-96f, 26f));
            TextMeshProUGUI hint = Label(hintRect, "", 17f, new Color(0.42f, 0.48f, 0.50f),
                TextAlignmentOptions.Left, FontStyles.Normal, 2f);

            var serialized = new SerializedObject(pause);
            serialized.FindProperty("director").objectReferenceValue = director;
            serialized.FindProperty("group").objectReferenceValue = group;
            serialized.FindProperty("titleLabel").objectReferenceValue = title;
            serialized.FindProperty("subtitleLabel").objectReferenceValue = subtitle;
            serialized.FindProperty("statsLabel").objectReferenceValue = stats;
            serialized.FindProperty("hintLabel").objectReferenceValue = hint;

            SerializedProperty entryList = serialized.FindProperty("entries");
            entryList.arraySize = entries.Count;
            for (int i = 0; i < entries.Count; i++)
            {
                SerializedProperty element = entryList.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("label").stringValue = entries[i].label;
                element.FindPropertyRelative("root").objectReferenceValue = entries[i].root;
                element.FindPropertyRelative("background").objectReferenceValue = entries[i].background;
                element.FindPropertyRelative("text").objectReferenceValue = entries[i].text;
                element.FindPropertyRelative("selectionBar").objectReferenceValue = entries[i].selectionBar;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();

            root.gameObject.SetActive(false);
        }

        private static void SetArray(SerializedObject serialized, string propertyName, List<Object> values)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            property.arraySize = values.Count;
            for (int i = 0; i < values.Count; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
        }

        // ------------------------------------------------------------------
        // Sprites
        // ------------------------------------------------------------------

        private static void EnsureSprites()
        {
            AshfallAssetUtility.EnsureFolder(SpriteFolder);

            _white = MakeSprite("UI_White", 8, (x, y, s) => Color.white);

            _ring = MakeSprite("UI_Ring", 128, (x, y, s) =>
            {
                float cx = s * 0.5f, cy = s * 0.5f;
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / (s * 0.5f);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.74f, 0.80f, d))
                          * Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.94f, 1.0f, d));
                return new Color(1f, 1f, 1f, a);
            });

            _vignette = MakeSprite("UI_Vignette", 256, (x, y, s) =>
            {
                float cx = s * 0.5f, cy = s * 0.5f;
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / (s * 0.5f);
                float a = Mathf.Pow(Mathf.Clamp01(Mathf.InverseLerp(0.35f, 1.05f, d)), 1.6f);
                return new Color(1f, 1f, 1f, a);
            });

            _hitMarker = MakeSprite("UI_HitMarker", 64, (x, y, s) =>
            {
                float cx = s * 0.5f, cy = s * 0.5f;
                float dx = Mathf.Abs(x - cx + 0.5f);
                float dy = Mathf.Abs(y - cy + 0.5f);

                // Four diagonal ticks around a hollow centre.
                float diagonal = Mathf.Abs(dx - dy);
                float radius = Mathf.Max(dx, dy);
                bool onTick = diagonal < 1.6f && radius > s * 0.20f && radius < s * 0.44f;
                return new Color(1f, 1f, 1f, onTick ? 1f : 0f);
            });

            _softPanel = _white;
            _ = _softPanel;
        }

        private static Sprite MakeSprite(string name, int size, System.Func<int, int, int, Color> shader)
        {
            string path = $"{SpriteFolder}/{name}.png";
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            var pixels = new Color[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = shader(x, y, size);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.maxTextureSize = Mathf.Max(32, size);
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>
        /// Finds a usable TMP font. The essential resources are imported by the project
        /// builder before this runs, so the default asset is normally present; the search
        /// is a fallback for a project where they were imported under a different path.
        /// </summary>
        private static TMP_FontAsset ResolveFont()
        {
            if (TMP_Settings.defaultFontAsset != null)
            {
                return TMP_Settings.defaultFontAsset;
            }

            string[] guids = AssetDatabase.FindAssets("t:TMP_FontAsset");
            for (int i = 0; i < guids.Length; i++)
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (font != null)
                {
                    return font;
                }
            }

            Debug.LogWarning("[Ashfall] No TMP font asset found; HUD text will use TMP's runtime fallback.");
            return null;
        }
    }
}
