using System;
using UnityEngine;
using Ashfall.Core;
using Ashfall.InputLayer;
using Ashfall.World;

namespace Ashfall.Player
{
    /// <summary>
    /// Finds what the player is looking at and drives press-or-hold interaction.
    ///
    /// Targeting is a short spherecast rather than a trigger-volume list: it means the
    /// prompt always matches what is under the crosshair, which is what the player
    /// actually expects when two stations sit near each other.
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private PlayerCameraRig cameraRig;
        [SerializeField] private SalvageWallet wallet;
        [SerializeField] private float reach = 3.4f;
        [SerializeField] private float castRadius = 0.28f;
        [SerializeField] private LayerMask interactMask = ~0;

        /// <summary>(interactable or null, prompt text, holdProgress 0..1)</summary>
        public event Action<Interactable, string, float> TargetChanged;

        public event Action<Interactable> Interacted;
        public event Action<Interactable> InteractionDenied;

        private readonly RaycastHit[] _hits = new RaycastHit[12];
        private Interactable _target;
        private float _holdTimer;
        private string _lastPrompt = string.Empty;

        public Interactable CurrentTarget => _target;

        private void Awake()
        {
            cameraRig ??= GetComponentInChildren<PlayerCameraRig>();
            wallet ??= FindFirstObjectByType<SalvageWallet>();

            if (interactMask == ~0)
            {
                interactMask = AshfallLayers.InteractMask;
            }
        }

        public void Configure(PlayerCameraRig rig, SalvageWallet salvageWallet)
        {
            cameraRig = rig;
            wallet = salvageWallet;
        }

        public void ClearTarget()
        {
            if (_target != null)
            {
                _target.SetTargeted(false);
                _target = null;
            }

            _holdTimer = 0f;
            _lastPrompt = string.Empty;
            TargetChanged?.Invoke(null, string.Empty, 0f);
        }

        public void Tick(in InputFrame input, float deltaTime, bool allowInteraction)
        {
            Interactable found = allowInteraction ? FindTarget() : null;

            if (found != _target)
            {
                _target?.SetTargeted(false);
                _target = found;
                _target?.SetTargeted(true);
                _holdTimer = 0f;
            }

            if (_target == null)
            {
                if (_lastPrompt.Length > 0)
                {
                    _lastPrompt = string.Empty;
                    TargetChanged?.Invoke(null, string.Empty, 0f);
                }

                return;
            }

            float hold = _target.HoldSeconds;
            float progress = 0f;

            if (hold > 0f)
            {
                if (input.InteractHeld && _target.CanInteract(wallet))
                {
                    _holdTimer += deltaTime;
                    progress = Mathf.Clamp01(_holdTimer / hold);
                    if (_holdTimer >= hold)
                    {
                        _holdTimer = 0f;
                        progress = 0f;
                        Execute();
                    }
                }
                else
                {
                    // Bleed the hold back down instead of resetting it instantly; a
                    // dodge mid-repair should not throw away all the progress.
                    _holdTimer = Mathf.Max(0f, _holdTimer - deltaTime * 2f);
                    progress = Mathf.Clamp01(_holdTimer / hold);
                }
            }
            else if (input.InteractPressed)
            {
                if (_target.CanInteract(wallet))
                {
                    Execute();
                }
                else
                {
                    InteractionDenied?.Invoke(_target);
                }
            }

            string prompt = _target != null ? _target.BuildPrompt(wallet) : string.Empty;
            if (prompt != _lastPrompt || progress > 0f)
            {
                _lastPrompt = prompt;
                TargetChanged?.Invoke(_target, prompt, progress);
            }
        }

        private void Execute()
        {
            Interactable target = _target;
            if (target == null)
            {
                return;
            }

            if (target.Interact(wallet, gameObject))
            {
                Interacted?.Invoke(target);

                // The interaction may have disabled itself (a door that opened); drop the
                // stale target so the prompt clears on the same frame.
                if (!target.IsAvailable)
                {
                    ClearTarget();
                }
            }
            else
            {
                InteractionDenied?.Invoke(target);
            }
        }

        private Interactable FindTarget()
        {
            Camera cam = cameraRig != null ? cameraRig.ViewCamera : Camera.main;
            if (cam == null)
            {
                return null;
            }

            Transform camTransform = cam.transform;
            int count = Physics.SphereCastNonAlloc(
                camTransform.position,
                castRadius,
                camTransform.forward,
                _hits,
                reach,
                interactMask,
                QueryTriggerInteraction.Collide);

            Interactable best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider collider = _hits[i].collider;
                if (collider == null)
                {
                    continue;
                }

                var candidate = collider.GetComponentInParent<Interactable>();
                if (candidate == null || !candidate.IsAvailable)
                {
                    continue;
                }

                float distance = _hits[i].distance;
                if (distance > candidate.PromptRange)
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }
    }
}
