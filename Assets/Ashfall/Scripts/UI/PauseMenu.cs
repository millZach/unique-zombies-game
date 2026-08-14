using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Ashfall.Core;
using Ashfall.InputLayer;

namespace Ashfall.UI
{
    /// <summary>
    /// Pause and end-of-run menu.
    ///
    /// Navigation is driven from the project's own input abstraction rather than uGUI's
    /// EventSystem selection. That is one less package-dependent moving part, and it
    /// means the menu responds identically to keyboard, gamepad d-pad and stick without
    /// any extra configuration.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        [Serializable]
        public class MenuEntry
        {
            public string label;
            public RectTransform root;
            public Image background;
            public TextMeshProUGUI text;
            public Image selectionBar;
        }

        [Header("References")]
        [SerializeField] private GameDirector director;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private TextMeshProUGUI titleLabel;
        [SerializeField] private TextMeshProUGUI subtitleLabel;
        [SerializeField] private TextMeshProUGUI statsLabel;
        [SerializeField] private TextMeshProUGUI hintLabel;
        [SerializeField] private List<MenuEntry> entries = new();

        [Header("Tuning")]
        [SerializeField] private float navigationRepeatDelay = 0.28f;
        [SerializeField] private float fadeSpeed = 9f;

        private int _selected;
        private float _navigationCooldown;
        private bool _open;
        private bool _runOverMode;
        private AshfallInput _input;

        public bool IsOpen => _open;

        private void Awake()
        {
            director ??= FindFirstObjectByType<GameDirector>();
        }

        private void OnEnable()
        {
            if (director != null)
            {
                director.StateChanged += HandleStateChanged;
            }
        }

        private void OnDisable()
        {
            if (director != null)
            {
                director.StateChanged -= HandleStateChanged;
            }
        }

        private void Start()
        {
            _input = AshfallInput.Instance;
            SetOpen(false, runOver: false);
            if (group != null)
            {
                group.alpha = 0f;
            }
        }

        private void Update()
        {
            _input ??= AshfallInput.Instance;
            InputFrame frame = _input.Frame;
            float dt = Time.unscaledDeltaTime;

            if (!_runOverMode && (frame.PausePressed || (_open && frame.MenuCancelPressed)))
            {
                Toggle();
            }

            // F5 / Select restarts from anywhere, including mid-round.
            if (frame.RestartPressed)
            {
                Restart();
                return;
            }

            if (group != null)
            {
                group.alpha = Mathf.MoveTowards(group.alpha, _open ? 1f : 0f, dt * fadeSpeed);
                group.blocksRaycasts = _open;
                group.interactable = _open;
                if (group.alpha <= 0.001f && group.gameObject.activeSelf && !_open)
                {
                    group.gameObject.SetActive(false);
                }
            }

            if (!_open)
            {
                return;
            }

            _navigationCooldown -= dt;

            float vertical = frame.MenuNavigate.y;
            if (Mathf.Abs(vertical) > 0.5f && _navigationCooldown <= 0f)
            {
                Move(vertical > 0f ? -1 : 1);
                _navigationCooldown = navigationRepeatDelay;
            }
            else if (Mathf.Abs(vertical) <= 0.35f)
            {
                _navigationCooldown = 0f;
            }

            if (frame.MenuSubmitPressed)
            {
                Activate(_selected);
            }

            RefreshSelectionVisuals(dt);
        }

        private void Move(int delta)
        {
            int count = CountVisible();
            if (count <= 0)
            {
                return;
            }

            int guard = 0;
            do
            {
                _selected = (_selected + delta + entries.Count) % entries.Count;
                guard++;
            }
            while (guard <= entries.Count && !IsVisible(_selected));
        }

        private bool IsVisible(int index)
        {
            return index >= 0
                   && index < entries.Count
                   && entries[index]?.root != null
                   && entries[index].root.gameObject.activeSelf;
        }

        private int CountVisible()
        {
            int n = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                if (IsVisible(i))
                {
                    n++;
                }
            }

            return n;
        }

        private void RefreshSelectionVisuals(float dt)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                MenuEntry entry = entries[i];
                if (entry?.root == null)
                {
                    continue;
                }

                bool selected = i == _selected;

                if (entry.text != null)
                {
                    entry.text.color = Color.Lerp(
                        entry.text.color,
                        selected ? AshfallPalette.HudInk : AshfallPalette.HudInkDim,
                        1f - Mathf.Exp(-16f * dt));
                }

                if (entry.background != null)
                {
                    Color target = selected
                        ? new Color(AshfallPalette.StormTeal.r, AshfallPalette.StormTeal.g, AshfallPalette.StormTeal.b, 0.16f)
                        : new Color(1f, 1f, 1f, 0.03f);
                    entry.background.color = Color.Lerp(entry.background.color, target, 1f - Mathf.Exp(-16f * dt));
                }

                if (entry.selectionBar != null)
                {
                    entry.selectionBar.color = Color.Lerp(
                        entry.selectionBar.color,
                        selected ? AshfallPalette.StormTeal : new Color(0f, 0f, 0f, 0f),
                        1f - Mathf.Exp(-16f * dt));
                }

                // Nudge the selected row right so the choice reads without colour alone.
                entry.root.anchoredPosition = Vector2.Lerp(
                    entry.root.anchoredPosition,
                    new Vector2(selected ? 14f : 0f, entry.root.anchoredPosition.y),
                    1f - Mathf.Exp(-16f * dt));
            }
        }

        private void Activate(int index)
        {
            if (!IsVisible(index))
            {
                return;
            }

            switch (entries[index].label)
            {
                case "RESUME":
                    Toggle();
                    break;

                case "RESTART RUN":
                    Restart();
                    break;

                case "QUIT":
                    Quit();
                    break;
            }
        }

        public void Toggle()
        {
            if (_runOverMode)
            {
                return;
            }

            SetOpen(!_open, runOver: false);
            director?.SetPaused(_open);
        }

        public void Restart()
        {
            SetOpen(false, runOver: false);
            director?.SetPaused(false);
            director?.RestartRun();
            AshfallInput.Instance.SetCursorLocked(true);
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void HandleStateChanged(GameState state)
        {
            if (state == GameState.Defeat || state == GameState.RunComplete)
            {
                SetOpen(true, runOver: true);
                bool won = state == GameState.RunComplete;
                if (titleLabel != null)
                {
                    titleLabel.text = won ? "STORM PASSED" : "SIGNAL LOST";
                    titleLabel.color = won ? AshfallPalette.StormTeal : AshfallPalette.HudDanger;
                }

                if (subtitleLabel != null)
                {
                    subtitleLabel.text = won
                        ? "You held Black Meridian to the end of the slice."
                        : "The station belongs to the storm now.";
                }

                if (statsLabel != null && director != null)
                {
                    statsLabel.text =
                        $"ROUND REACHED   <color=#3DE0DA>{director.HighestRoundReached}</color>          " +
                        $"CONTACTS DOWNED   <color=#3DE0DA>{director.KillsThisRun}</color>          " +
                        $"PHASE   <color=#3DE0DA>{MapPhases.DisplayName(MapPhases.ForRound(director.HighestRoundReached))}</color>";
                }
            }
            else if (_runOverMode && state == GameState.Briefing)
            {
                SetOpen(false, runOver: false);
            }
        }

        private void SetOpen(bool open, bool runOver)
        {
            _open = open;
            _runOverMode = runOver;

            if (group != null)
            {
                if (open)
                {
                    group.gameObject.SetActive(true);
                }

                group.blocksRaycasts = open;
                group.interactable = open;
            }

            if (open)
            {
                if (!runOver)
                {
                    if (titleLabel != null)
                    {
                        titleLabel.text = "PAUSED";
                        titleLabel.color = AshfallPalette.HudInk;
                    }

                    if (subtitleLabel != null)
                    {
                        subtitleLabel.text = "ASHFALL: BLACK MERIDIAN";
                    }

                    if (statsLabel != null && director != null)
                    {
                        statsLabel.text =
                            $"ROUND   <color=#3DE0DA>{director.Round}</color>          " +
                            $"PHASE   <color=#3DE0DA>{MapPhases.DisplayName(director.MapPhase != null ? director.MapPhase.CurrentPhase : Core.MapPhase.Standby)}</color>          " +
                            $"SALVAGE   <color=#83E66F>{(director.Wallet != null ? director.Wallet.Balance : 0)}</color>";
                    }
                }

                if (hintLabel != null)
                {
                    hintLabel.text = AshfallInput.Instance.LastScheme == InputScheme.Gamepad
                        ? "D-PAD / LS to choose      A to confirm      B to resume      SELECT restarts"
                        : "W / S to choose      ENTER to confirm      ESC to resume      F5 restarts";
                }

                // The resume row is meaningless once the run is over.
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i]?.root == null)
                    {
                        continue;
                    }

                    bool visible = !(runOver && entries[i].label == "RESUME");
                    entries[i].root.gameObject.SetActive(visible);
                }

                _selected = 0;
                if (!IsVisible(_selected))
                {
                    Move(1);
                }
            }

            AshfallInput.Instance.SetCursorLocked(!open);
        }

        public void Configure(GameDirector gameDirector, CanvasGroup canvasGroup, List<MenuEntry> menuEntries)
        {
            director = gameDirector;
            group = canvasGroup;
            entries = menuEntries;
        }
    }
}
