using System;
using UnityEngine;
using Ashfall.Core;
using Ashfall.Nav;

namespace Ashfall.World
{
    /// <summary>
    /// A purchasable route. Buying it slides the shutter open, unlocks the matching
    /// gate in the nav graph so enemies immediately re-route through it, and enables
    /// whatever spawn points sit beyond.
    ///
    /// Doors can also be opened by the map phase controller, which is how the station
    /// "fails open" on its own at rounds 3, 6, 9 and 12.
    /// </summary>
    public class RouteDoor : Interactable
    {
        [Header("Route")]
        [SerializeField] private int salvageCost = 900;
        [SerializeField] private string navGateName = "";
        [SerializeField] private StationZone unlocksZone = StationZone.LabWing;

        [Header("Motion")]
        [SerializeField] private Transform movingPart;
        [SerializeField] private Vector3 openLocalOffset = new Vector3(0f, 3.6f, 0f);
        [SerializeField] private float openSeconds = 1.35f;
        [SerializeField] private AnimationCurve openCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("Blocking")]
        [Tooltip("Colliders disabled once the route is open.")]
        [SerializeField] private Collider[] blockingColliders;

        [Header("Signage")]
        [SerializeField] private Renderer[] signRenderers;
        [SerializeField] private Light[] signLights;
        [SerializeField] private Color lockedColor = AshfallPalette.WarningRed;
        [SerializeField] private Color unlockedColor = AshfallPalette.SalvageGreen;

        public event Action<RouteDoor> Opened;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock _signBlock;
        private Vector3 _closedLocalPosition;
        private float _openProgress;
        private bool _isOpen;
        private bool _animating;

        public bool IsOpen => _isOpen;
        public string NavGateName => navGateName;
        public StationZone UnlocksZone => unlocksZone;

        public override int Cost => _isOpen ? 0 : salvageCost;
        public override bool IsAvailable => isActiveAndEnabled && !_isOpen;

        protected override void Awake()
        {
            base.Awake();
            _signBlock = new MaterialPropertyBlock();
            movingPart ??= transform;
            _closedLocalPosition = movingPart.localPosition;
            ApplySignColor(lockedColor);
        }

        private void Start()
        {
            // Make sure the gate starts shut in the graph even if the scene was saved
            // with a different state.
            if (!_isOpen && NavGraph.Active != null && !string.IsNullOrEmpty(navGateName))
            {
                NavGraph.Active.SetGateOpen(navGateName, false);
            }
        }

        public void Configure(int cost, string gateName, StationZone zone, Transform moving, Vector3 offset)
        {
            salvageCost = cost;
            navGateName = gateName;
            unlocksZone = zone;
            movingPart = moving;
            openLocalOffset = offset;
        }

        public override string BuildPrompt(SalvageWallet wallet)
        {
            if (_isOpen)
            {
                return string.Empty;
            }

            bool affordable = wallet == null || wallet.CanAfford(salvageCost);
            return affordable
                ? $"{title}   {salvageCost} salvage"
                : $"{title}   {salvageCost} salvage  (need {salvageCost - (wallet != null ? wallet.Balance : 0)} more)";
        }

        public override bool Interact(SalvageWallet wallet, GameObject instigator)
        {
            if (_isOpen)
            {
                return false;
            }

            if (wallet != null && !wallet.TrySpend(salvageCost))
            {
                return false;
            }

            Open();
            return true;
        }

        /// <summary>Opens without charging. Used by the map phase controller.</summary>
        public void ForceOpen(bool instant = false)
        {
            if (_isOpen)
            {
                return;
            }

            Open();
            if (instant)
            {
                _openProgress = 1f;
                _animating = false;
                if (movingPart != null)
                {
                    movingPart.localPosition = _closedLocalPosition + openLocalOffset;
                }
            }
        }

        private void Open()
        {
            _isOpen = true;
            _animating = true;
            SetTargeted(false);

            if (NavGraph.Active != null && !string.IsNullOrEmpty(navGateName))
            {
                NavGraph.Active.SetGateOpen(navGateName, true);
            }

            if (blockingColliders != null)
            {
                for (int i = 0; i < blockingColliders.Length; i++)
                {
                    if (blockingColliders[i] != null)
                    {
                        blockingColliders[i].enabled = false;
                    }
                }
            }

            ApplySignColor(unlockedColor);
            Opened?.Invoke(this);
        }

        /// <summary>Puts the door back to locked. Used by the restart path.</summary>
        public void ResetToClosed()
        {
            _isOpen = false;
            _animating = false;
            _openProgress = 0f;

            if (movingPart != null)
            {
                movingPart.localPosition = _closedLocalPosition;
            }

            if (blockingColliders != null)
            {
                for (int i = 0; i < blockingColliders.Length; i++)
                {
                    if (blockingColliders[i] != null)
                    {
                        blockingColliders[i].enabled = true;
                    }
                }
            }

            if (NavGraph.Active != null && !string.IsNullOrEmpty(navGateName))
            {
                NavGraph.Active.SetGateOpen(navGateName, false);
            }

            ApplySignColor(lockedColor);
        }

        protected override void Update()
        {
            base.Update();

            if (!_animating || movingPart == null)
            {
                return;
            }

            _openProgress += Time.deltaTime / Mathf.Max(0.05f, openSeconds);
            float eased = openCurve.Evaluate(Mathf.Clamp01(_openProgress));
            movingPart.localPosition = _closedLocalPosition + openLocalOffset * eased;

            if (_openProgress >= 1f)
            {
                _animating = false;
            }
        }

        private void ApplySignColor(Color color)
        {
            if (signRenderers != null)
            {
                _signBlock ??= new MaterialPropertyBlock();
                for (int i = 0; i < signRenderers.Length; i++)
                {
                    Renderer r = signRenderers[i];
                    if (r == null)
                    {
                        continue;
                    }

                    r.GetPropertyBlock(_signBlock);
                    _signBlock.SetColor(BaseColorId, color * 0.4f);
                    _signBlock.SetColor(EmissionId, color * 2.6f);
                    r.SetPropertyBlock(_signBlock);
                }
            }

            if (signLights != null)
            {
                for (int i = 0; i < signLights.Length; i++)
                {
                    if (signLights[i] != null)
                    {
                        signLights[i].color = color;
                    }
                }
            }
        }
    }
}
