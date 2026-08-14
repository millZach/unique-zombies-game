using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Ashfall.Core;
using Ashfall.InputLayer;
using Ashfall.Player;
using Ashfall.Weapons;
using Ashfall.World;

namespace Ashfall.UI
{
    /// <summary>
    /// The whole heads-up display.
    ///
    /// The layout rule is corners-and-centre: status lives in the four corners where it
    /// can be read peripherally, and only things that demand a decision -- a prompt, an
    /// objective, a hit confirm -- are allowed near the crosshair. Nothing animates
    /// unless its value changed, so the screen is quiet while the player is just moving.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        [Serializable]
        public class PowerUpChip
        {
            public GameObject root;
            public Image background;
            public Image fill;
            public TextMeshProUGUI label;
            [NonSerialized] public PowerUpKind Kind;
            [NonSerialized] public float Duration;
        }

        [Header("Systems")]
        [SerializeField] private GameDirector director;
        [SerializeField] private PlayerRig player;
        [SerializeField] private SalvageWallet wallet;
        [SerializeField] private PowerUpManager powerUps;

        [Header("Round / phase")]
        [SerializeField] private TextMeshProUGUI roundNumberLabel;
        [SerializeField] private TextMeshProUGUI roundCaptionLabel;
        [SerializeField] private TextMeshProUGUI phaseLabel;
        [SerializeField] private Image[] phasePips;
        [SerializeField] private TextMeshProUGUI enemiesLabel;

        [Header("Salvage")]
        [SerializeField] private TextMeshProUGUI salvageLabel;
        [SerializeField] private TextMeshProUGUI salvageDeltaLabel;

        [Header("Vitals")]
        [SerializeField] private Image healthFill;
        [SerializeField] private Image healthDelayedFill;
        [SerializeField] private TextMeshProUGUI healthLabel;

        [Header("Weapon")]
        [SerializeField] private TextMeshProUGUI weaponNameLabel;
        [SerializeField] private TextMeshProUGUI ammoLabel;
        [SerializeField] private TextMeshProUGUI reserveLabel;
        [SerializeField] private Image reloadRing;
        [SerializeField] private Image weaponAccentBar;

        [Header("Crosshair")]
        [SerializeField] private RectTransform crosshairRoot;
        [SerializeField] private RectTransform[] crosshairBlades;
        [SerializeField] private Image crosshairDot;
        [SerializeField] private CanvasGroup hitMarker;
        [SerializeField] private Image hitMarkerImage;

        [Header("Objective / prompt")]
        [SerializeField] private CanvasGroup objectiveGroup;
        [SerializeField] private TextMeshProUGUI objectiveLabel;
        [SerializeField] private CanvasGroup promptGroup;
        [SerializeField] private TextMeshProUGUI promptLabel;
        [SerializeField] private Image promptFill;

        [Header("Power-ups")]
        [SerializeField] private List<PowerUpChip> powerUpChips = new();

        [Header("Full screen")]
        [SerializeField] private Image damageVignette;
        [SerializeField] private Image lowHealthVignette;
        [SerializeField] private Image phaseFlash;
        [SerializeField] private Image exposureVignette;
        [SerializeField] private TextMeshProUGUI exposureLabel;

        [Header("Legend")]
        [SerializeField] private TextMeshProUGUI legendLabel;

        [Header("Banner")]
        [SerializeField] private CanvasGroup bannerGroup;
        [SerializeField] private TextMeshProUGUI bannerTitle;
        [SerializeField] private TextMeshProUGUI bannerSubtitle;

        [Header("Tuning")]
        [SerializeField] private float crosshairPixelsPerDegree = 34f;
        [SerializeField] private float crosshairMinGap = 6f;
        [SerializeField] private float salvageDeltaSeconds = 1.4f;
        [SerializeField] private float bannerSeconds = 3.4f;

        private float _objectiveHideAt;
        private float _bannerHideAt;
        private float _salvageDeltaHideAt;
        private float _hitMarkerHideAt;
        private float _damageFlash;
        private float _phaseFlashAmount;
        private float _displayedHealth = 1f;
        private float _delayedHealth = 1f;
        private float _exposureAmount;
        private float _currentExposureDps;
        private InputScheme _legendScheme = (InputScheme)(-1);
        private int _lastSalvage;

        private void Awake()
        {
            director ??= FindFirstObjectByType<GameDirector>();
            player ??= FindFirstObjectByType<PlayerRig>();
            wallet ??= FindFirstObjectByType<SalvageWallet>();
            powerUps ??= FindFirstObjectByType<PowerUpManager>();
        }

        private void OnEnable()
        {
            if (director != null)
            {
                director.RoundStarted += HandleRoundStarted;
                director.RoundCleared += HandleRoundCleared;
                director.PhaseAnnounced += HandlePhaseAnnounced;
                director.ObjectiveChanged += HandleObjective;
                director.StateChanged += HandleStateChanged;
            }

            if (wallet != null)
            {
                wallet.BalanceChanged += HandleSalvageChanged;
                wallet.PurchaseDenied += HandlePurchaseDenied;
            }

            if (player != null)
            {
                if (player.Health != null)
                {
                    player.Health.HealthChanged += HandleHealthChanged;
                    player.Health.Damaged += HandleDamaged;
                    player.Health.LastStandSaved += HandleLastStandSaved;
                }

                if (player.Loadout != null)
                {
                    player.Loadout.WeaponChanged += HandleWeaponChanged;
                    player.Loadout.AmmoChanged += HandleAmmoChanged;
                    player.Loadout.HitConfirmed += HandleHitConfirmed;
                }

                if (player.Interactor != null)
                {
                    player.Interactor.TargetChanged += HandlePromptChanged;
                }
            }

            if (powerUps != null)
            {
                powerUps.PowerUpActivated += HandlePowerUpActivated;
                powerUps.PowerUpExpired += HandlePowerUpExpired;
            }

            foreach (StormExposureVolume volume in FindObjectsByType<StormExposureVolume>(FindObjectsSortMode.None))
            {
                volume.ExposureChanged += HandleExposureChanged;
            }
        }

        private void OnDisable()
        {
            if (director != null)
            {
                director.RoundStarted -= HandleRoundStarted;
                director.RoundCleared -= HandleRoundCleared;
                director.PhaseAnnounced -= HandlePhaseAnnounced;
                director.ObjectiveChanged -= HandleObjective;
                director.StateChanged -= HandleStateChanged;
            }

            if (wallet != null)
            {
                wallet.BalanceChanged -= HandleSalvageChanged;
                wallet.PurchaseDenied -= HandlePurchaseDenied;
            }

            if (player != null)
            {
                if (player.Health != null)
                {
                    player.Health.HealthChanged -= HandleHealthChanged;
                    player.Health.Damaged -= HandleDamaged;
                    player.Health.LastStandSaved -= HandleLastStandSaved;
                }

                if (player.Loadout != null)
                {
                    player.Loadout.WeaponChanged -= HandleWeaponChanged;
                    player.Loadout.AmmoChanged -= HandleAmmoChanged;
                    player.Loadout.HitConfirmed -= HandleHitConfirmed;
                }

                if (player.Interactor != null)
                {
                    player.Interactor.TargetChanged -= HandlePromptChanged;
                }
            }

            if (powerUps != null)
            {
                powerUps.PowerUpActivated -= HandlePowerUpActivated;
                powerUps.PowerUpExpired -= HandlePowerUpExpired;
            }
        }

        private void Start()
        {
            SetAlpha(objectiveGroup, 0f);
            SetAlpha(promptGroup, 0f);
            SetAlpha(bannerGroup, 0f);
            if (hitMarker != null) hitMarker.alpha = 0f;

            for (int i = 0; i < powerUpChips.Count; i++)
            {
                if (powerUpChips[i]?.root != null)
                {
                    powerUpChips[i].root.SetActive(false);
                }
            }

            RefreshLegend(true);
            HandleAmmoChanged(player?.Loadout?.CurrentWeapon);
            HandleWeaponChanged(player?.Loadout?.CurrentSlot);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;

            TickCrosshair(dt);
            TickVitals(dt);
            TickFades(now, dt);
            TickPowerUpChips();
            TickReload();
            TickEnemyCount();
            RefreshLegend(false);
        }

        // ------------------------------------------------------------------
        // Per-frame widgets
        // ------------------------------------------------------------------

        private void TickCrosshair(float dt)
        {
            WeaponRuntime weapon = player?.Loadout?.CurrentWeapon;
            if (weapon == null || crosshairBlades == null)
            {
                return;
            }

            float gap = crosshairMinGap + weapon.CurrentSpreadDegrees * crosshairPixelsPerDegree;

            for (int i = 0; i < crosshairBlades.Length; i++)
            {
                RectTransform blade = crosshairBlades[i];
                if (blade == null)
                {
                    continue;
                }

                Vector2 direction = i switch
                {
                    0 => Vector2.up,
                    1 => Vector2.down,
                    2 => Vector2.left,
                    _ => Vector2.right
                };

                blade.anchoredPosition = Vector2.Lerp(
                    blade.anchoredPosition,
                    direction * gap,
                    1f - Mathf.Exp(-22f * dt));
            }

            if (crosshairRoot != null)
            {
                bool hide = player.Loadout.IsAiming && weapon.Definition != null && weapon.Definition.pelletsPerShot == 1;
                float targetAlpha = hide ? 0.25f : 1f;
                var group = crosshairRoot.GetComponent<CanvasGroup>();
                if (group != null)
                {
                    group.alpha = Mathf.Lerp(group.alpha, targetAlpha, 1f - Mathf.Exp(-14f * dt));
                }
            }

            if (crosshairDot != null && weapon.Definition != null)
            {
                crosshairDot.color = Color.Lerp(
                    AshfallPalette.HudInk,
                    weapon.Definition.accentColor,
                    0.55f);
            }
        }

        private void TickVitals(float dt)
        {
            PlayerHealth health = player?.Health;
            if (health == null)
            {
                return;
            }

            float target = health.HealthFraction;
            _displayedHealth = Mathf.Lerp(_displayedHealth, target, 1f - Mathf.Exp(-18f * dt));

            // The delayed bar trails behind so the size of a hit is visible after it lands.
            _delayedHealth = _delayedHealth > _displayedHealth
                ? Mathf.MoveTowards(_delayedHealth, _displayedHealth, dt * 0.45f)
                : _displayedHealth;

            if (healthFill != null)
            {
                healthFill.fillAmount = _displayedHealth;
                healthFill.color = Color.Lerp(AshfallPalette.HudDanger, AshfallPalette.HudInk, Mathf.Clamp01(target * 1.6f));
            }

            if (healthDelayedFill != null)
            {
                healthDelayedFill.fillAmount = _delayedHealth;
            }

            if (healthLabel != null)
            {
                healthLabel.text = Mathf.CeilToInt(health.Health).ToString();
            }

            if (lowHealthVignette != null)
            {
                // Kept deliberately restrained. The station is already very dark, so a
                // strong red wash reads as a full-screen tint rather than an edge
                // vignette and swallows the enemy silhouettes it is meant to sit behind.
                float danger = 1f - Mathf.Clamp01(target / 0.35f);
                float pulse = 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 4.2f);
                SetImageAlpha(lowHealthVignette, danger * pulse * 0.34f);
            }

            if (exposureVignette != null)
            {
                _exposureAmount = Mathf.MoveTowards(_exposureAmount, _currentExposureDps > 0.01f ? 1f : 0f, dt * 3f);
                float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 6f);
                SetImageAlpha(exposureVignette, _exposureAmount * pulse * 0.4f);
            }

            if (exposureLabel != null)
            {
                exposureLabel.gameObject.SetActive(_exposureAmount > 0.05f);
                if (_exposureAmount > 0.05f)
                {
                    exposureLabel.text = $"STORM EXPOSURE  -{_currentExposureDps:0} /s";
                    exposureLabel.alpha = _exposureAmount;
                }
            }
        }

        private void TickFades(float now, float dt)
        {
            _damageFlash = Mathf.MoveTowards(_damageFlash, 0f, dt * 2.2f);
            if (damageVignette != null)
            {
                SetImageAlpha(damageVignette, _damageFlash * 0.7f);
            }

            _phaseFlashAmount = Mathf.MoveTowards(_phaseFlashAmount, 0f, dt * 0.75f);
            if (phaseFlash != null)
            {
                SetImageAlpha(phaseFlash, _phaseFlashAmount * 0.5f);
            }

            FadeGroup(objectiveGroup, now < _objectiveHideAt, dt, 8f);
            FadeGroup(bannerGroup, now < _bannerHideAt, dt, 5f);

            if (hitMarker != null)
            {
                hitMarker.alpha = Mathf.MoveTowards(hitMarker.alpha, now < _hitMarkerHideAt ? 1f : 0f, dt * 7f);
                hitMarker.transform.localScale = Vector3.Lerp(
                    hitMarker.transform.localScale,
                    Vector3.one,
                    1f - Mathf.Exp(-18f * dt));
            }

            if (salvageDeltaLabel != null)
            {
                bool show = now < _salvageDeltaHideAt;
                Color c = salvageDeltaLabel.color;
                c.a = Mathf.MoveTowards(c.a, show ? 1f : 0f, dt * 3.2f);
                salvageDeltaLabel.color = c;
            }
        }

        private void TickPowerUpChips()
        {
            if (powerUps == null)
            {
                return;
            }

            for (int i = 0; i < powerUpChips.Count; i++)
            {
                PowerUpChip chip = powerUpChips[i];
                if (chip?.root == null || !chip.root.activeSelf)
                {
                    continue;
                }

                float remaining = powerUps.RemainingSeconds(chip.Kind);
                if (remaining <= 0f)
                {
                    chip.root.SetActive(false);
                    continue;
                }

                if (chip.fill != null && chip.Duration > 0f)
                {
                    chip.fill.fillAmount = remaining / chip.Duration;
                }

                if (chip.label != null)
                {
                    // Flash the last three seconds so it is obvious the buff is ending.
                    bool ending = remaining <= 3f;
                    float alpha = ending ? 0.45f + 0.55f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 8f)) : 1f;
                    chip.label.text = $"{PowerUps.DisplayName(chip.Kind)}  {remaining:0.0}s";
                    chip.label.alpha = alpha;
                }
            }
        }

        private void TickReload()
        {
            WeaponRuntime weapon = player?.Loadout?.CurrentWeapon;
            if (reloadRing == null)
            {
                return;
            }

            bool reloading = weapon != null && weapon.State == WeaponState.Reloading;
            reloadRing.gameObject.SetActive(reloading);
            if (reloading)
            {
                reloadRing.fillAmount = weapon.ReloadProgress;
            }
        }

        private void TickEnemyCount()
        {
            if (enemiesLabel == null || director == null)
            {
                return;
            }

            int remaining = director.RemainingThisRound;
            if (director.State == GameState.Combat)
            {
                enemiesLabel.gameObject.SetActive(true);
                enemiesLabel.text = $"{remaining} LEFT";
                enemiesLabel.color = remaining <= 3 ? AshfallPalette.HudAccent : AshfallPalette.HudInkDim;
            }
            else
            {
                enemiesLabel.gameObject.SetActive(false);
            }
        }

        private void RefreshLegend(bool force)
        {
            if (legendLabel == null)
            {
                return;
            }

            InputScheme scheme = AshfallInput.Instance.LastScheme;
            if (!force && scheme == _legendScheme)
            {
                return;
            }

            _legendScheme = scheme;
            legendLabel.text = scheme == InputScheme.Gamepad
                ? "<color=#8CA0A6>LS</color> move   <color=#8CA0A6>RS</color> look   <color=#8CA0A6>RT</color> fire   " +
                  "<color=#8CA0A6>LT</color> aim   <color=#8CA0A6>X</color> reload   <color=#8CA0A6>Y</color> interact   " +
                  "<color=#8CA0A6>A</color> jump   <color=#8CA0A6>B</color> crouch   <color=#8CA0A6>LS-click</color> sprint   " +
                  "<color=#8CA0A6>RB/LB</color> weapon   <color=#8CA0A6>Start</color> pause"
                : "<color=#8CA0A6>WASD</color> move   <color=#8CA0A6>Mouse</color> look   <color=#8CA0A6>LMB</color> fire   " +
                  "<color=#8CA0A6>RMB</color> aim   <color=#8CA0A6>R</color> reload   <color=#8CA0A6>E</color> interact   " +
                  "<color=#8CA0A6>Space</color> jump   <color=#8CA0A6>C</color> crouch   <color=#8CA0A6>Shift</color> sprint   " +
                  "<color=#8CA0A6>1-3 / Q</color> weapon   <color=#8CA0A6>Esc</color> pause";
        }

        // ------------------------------------------------------------------
        // Event handlers
        // ------------------------------------------------------------------

        private void HandleRoundStarted(int round, RoundComposition composition)
        {
            if (roundNumberLabel != null)
            {
                roundNumberLabel.text = round.ToString("00");
            }

            if (roundCaptionLabel != null)
            {
                roundCaptionLabel.text = director != null ? $"ROUND / {director.FinalRound}" : "ROUND";
            }

            UpdatePhaseWidgets(composition.Phase);

            ShowBanner(
                $"ROUND {round}",
                composition.IsEliteRound ? "STORM BRUTE INBOUND" : MapPhases.DisplayName(composition.Phase));
        }

        private void HandleRoundCleared(int round)
        {
            ShowBanner("ROUND CLEAR", "SPEND YOUR SALVAGE");
        }

        private void HandlePhaseAnnounced(MapPhase phase, string headline)
        {
            UpdatePhaseWidgets(phase);
            _phaseFlashAmount = 1f;

            if (phaseFlash != null)
            {
                phaseFlash.color = new Color(
                    MapPhases.Tint(phase).r,
                    MapPhases.Tint(phase).g,
                    MapPhases.Tint(phase).b,
                    phaseFlash.color.a);
            }

            ShowBanner(MapPhases.DisplayName(phase), headline);
        }

        private void UpdatePhaseWidgets(MapPhase phase)
        {
            if (phaseLabel != null)
            {
                phaseLabel.text = MapPhases.DisplayName(phase);
                phaseLabel.color = MapPhases.Tint(phase);
            }

            if (phasePips == null)
            {
                return;
            }

            for (int i = 0; i < phasePips.Length; i++)
            {
                if (phasePips[i] == null)
                {
                    continue;
                }

                bool reached = i <= (int)phase;
                phasePips[i].color = reached
                    ? MapPhases.Tint((MapPhase)i)
                    : new Color(AshfallPalette.HudInkDim.r, AshfallPalette.HudInkDim.g, AshfallPalette.HudInkDim.b, 0.28f);
            }
        }

        private void HandleObjective(string text, float seconds)
        {
            if (objectiveLabel != null)
            {
                objectiveLabel.text = text;
            }

            _objectiveHideAt = Time.unscaledTime + Mathf.Max(1.5f, seconds);
        }

        private void HandleStateChanged(GameState state)
        {
            if (state == GameState.Defeat)
            {
                ShowBanner("SIGNAL LOST", $"You held {director.Round} rounds");
            }
            else if (state == GameState.RunComplete)
            {
                ShowBanner("STORM PASSED", $"All {director.FinalRound} rounds survived");
            }
        }

        private void HandleSalvageChanged(int balance, int delta)
        {
            if (salvageLabel != null)
            {
                salvageLabel.text = balance.ToString("N0");
            }

            if (delta != 0 && salvageDeltaLabel != null)
            {
                salvageDeltaLabel.text = delta > 0 ? $"+{delta}" : delta.ToString();
                salvageDeltaLabel.color = delta > 0 ? AshfallPalette.SalvageGreen : AshfallPalette.HudDanger;
                _salvageDeltaHideAt = Time.unscaledTime + salvageDeltaSeconds;
            }

            _lastSalvage = balance;
        }

        private void HandlePurchaseDenied(int cost)
        {
            if (salvageDeltaLabel != null)
            {
                salvageDeltaLabel.text = $"NEED {cost - _lastSalvage}";
                salvageDeltaLabel.color = AshfallPalette.HudDanger;
                _salvageDeltaHideAt = Time.unscaledTime + salvageDeltaSeconds;
            }
        }

        private void HandleHealthChanged(float current, float max)
        {
            // The bar itself is lerped in TickVitals; nothing to do on the event beyond
            // making sure the first frame is not empty.
            if (healthLabel != null)
            {
                healthLabel.text = Mathf.CeilToInt(current).ToString();
            }
        }

        private void HandleDamaged(DamageInfo info)
        {
            _damageFlash = Mathf.Clamp01(_damageFlash + Mathf.Clamp01(info.Amount / 45f));
            GamepadRumble.Pulse(0.55f, 0.25f, 0.14f);
        }

        private void HandleLastStandSaved()
        {
            ShowBanner("LAST STAND", "You should not have survived that");
            GamepadRumble.Pulse(1f, 0.8f, 0.45f);
        }

        private void HandleWeaponChanged(PlayerLoadout.WeaponSlot slot)
        {
            if (slot?.definition == null)
            {
                return;
            }

            if (weaponNameLabel != null)
            {
                weaponNameLabel.text = slot.definition.displayName.ToUpperInvariant();
            }

            if (weaponAccentBar != null)
            {
                weaponAccentBar.color = slot.definition.accentColor;
            }

            HandleAmmoChanged(slot.Runtime);
        }

        private void HandleAmmoChanged(WeaponRuntime weapon)
        {
            if (weapon == null)
            {
                return;
            }

            if (ammoLabel != null)
            {
                ammoLabel.text = weapon.Magazine.ToString();
                ammoLabel.color = weapon.Magazine <= Mathf.Max(1, weapon.Definition.magazineSize / 5)
                    ? AshfallPalette.HudDanger
                    : AshfallPalette.HudInk;
            }

            if (reserveLabel != null)
            {
                reserveLabel.text = $"/ {weapon.Reserve}";
            }
        }

        private void HandleHitConfirmed(bool critical, bool killed)
        {
            _hitMarkerHideAt = Time.unscaledTime + (killed ? 0.32f : 0.18f);

            if (hitMarker != null)
            {
                hitMarker.transform.localScale = Vector3.one * (killed ? 1.75f : critical ? 1.4f : 1.2f);
            }

            if (hitMarkerImage != null)
            {
                hitMarkerImage.color = killed
                    ? AshfallPalette.HudDanger
                    : critical
                        ? AshfallPalette.HazardYellow
                        : AshfallPalette.HudInk;
            }
        }

        private void HandlePromptChanged(Interactable target, string prompt, float holdProgress)
        {
            bool show = target != null && !string.IsNullOrEmpty(prompt);

            if (promptLabel != null && show)
            {
                string key = AshfallInput.Instance.LastScheme == InputScheme.Gamepad ? "Y" : "E";
                promptLabel.text = target.HoldSeconds > 0f
                    ? $"<color=#3DE0DA>[HOLD {key}]</color>  {prompt}"
                    : $"<color=#3DE0DA>[{key}]</color>  {prompt}";
            }

            if (promptGroup != null)
            {
                promptGroup.alpha = show ? 1f : 0f;
                promptGroup.gameObject.SetActive(show);
            }

            if (promptFill != null)
            {
                promptFill.gameObject.SetActive(show && holdProgress > 0.001f);
                promptFill.fillAmount = holdProgress;
            }
        }

        private void HandlePowerUpActivated(PowerUpKind kind, float duration)
        {
            PowerUpChip chip = null;

            for (int i = 0; i < powerUpChips.Count; i++)
            {
                if (powerUpChips[i].root != null && powerUpChips[i].root.activeSelf && powerUpChips[i].Kind == kind)
                {
                    chip = powerUpChips[i];
                    break;
                }
            }

            if (chip == null)
            {
                for (int i = 0; i < powerUpChips.Count; i++)
                {
                    if (powerUpChips[i].root != null && !powerUpChips[i].root.activeSelf)
                    {
                        chip = powerUpChips[i];
                        break;
                    }
                }
            }

            if (chip == null)
            {
                return;
            }

            chip.Kind = kind;
            chip.Duration = duration;
            chip.root.SetActive(true);

            Color tint = PowerUps.Tint(kind);
            if (chip.fill != null)
            {
                chip.fill.color = tint;
                chip.fill.fillAmount = 1f;
            }

            if (chip.background != null)
            {
                chip.background.color = new Color(tint.r * 0.2f, tint.g * 0.2f, tint.b * 0.2f, 0.78f);
            }

            if (chip.label != null)
            {
                chip.label.color = tint;
            }

            ShowBanner(PowerUps.DisplayName(kind), PowerUps.Blurb(kind));
        }

        private void HandlePowerUpExpired(PowerUpKind kind)
        {
            for (int i = 0; i < powerUpChips.Count; i++)
            {
                if (powerUpChips[i].root != null && powerUpChips[i].Kind == kind)
                {
                    powerUpChips[i].root.SetActive(false);
                }
            }
        }

        private void HandleExposureChanged(bool inside, float dps)
        {
            _currentExposureDps = inside ? dps : 0f;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private void ShowBanner(string titleText, string subtitleText)
        {
            if (bannerTitle != null) bannerTitle.text = titleText;
            if (bannerSubtitle != null) bannerSubtitle.text = subtitleText;
            _bannerHideAt = Time.unscaledTime + bannerSeconds;
        }

        private static void FadeGroup(CanvasGroup group, bool visible, float dt, float speed)
        {
            if (group == null)
            {
                return;
            }

            group.alpha = Mathf.MoveTowards(group.alpha, visible ? 1f : 0f, dt * speed);
        }

        private static void SetAlpha(CanvasGroup group, float alpha)
        {
            if (group != null)
            {
                group.alpha = alpha;
            }
        }

        private static void SetImageAlpha(Image image, float alpha)
        {
            if (image == null)
            {
                return;
            }

            Color c = image.color;
            c.a = alpha;
            image.color = c;
            image.enabled = alpha > 0.002f;
        }
    }
}
