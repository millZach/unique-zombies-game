using UnityEngine;
using Ashfall.Core;

namespace Ashfall.World
{
    /// <summary>
    /// Base for everything the player can look at and press Interact on.
    ///
    /// The prompt text is produced by the interactable itself, not assembled in the HUD,
    /// so adding a new kind of station never means editing the UI.
    /// </summary>
    public abstract class Interactable : MonoBehaviour
    {
        [Header("Prompt")]
        [SerializeField] protected string title = "Interact";
        [SerializeField] protected float promptRange = 3.2f;
        [SerializeField] protected bool highlightWhenTargeted = true;

        [Header("Highlight")]
        [SerializeField] protected Renderer[] highlightRenderers;
        [SerializeField] protected Color highlightColor = AshfallPalette.StormTeal;
        [SerializeField] protected float highlightPulseSpeed = 3.2f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock _propertyBlock;
        private bool _targeted;
        private float _highlightAmount;

        public string Title => title;
        public float PromptRange => promptRange;

        /// <summary>Salvage cost, or 0 for free. Shown in the prompt.</summary>
        public virtual int Cost => 0;

        /// <summary>Seconds the button must be held. 0 means a single press.</summary>
        public virtual float HoldSeconds => 0f;

        /// <summary>False hides the interactable from targeting entirely.</summary>
        public virtual bool IsAvailable => isActiveAndEnabled;

        protected virtual void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
            if (highlightRenderers == null || highlightRenderers.Length == 0)
            {
                highlightRenderers = GetComponentsInChildren<Renderer>();
            }
        }

        protected virtual void Update()
        {
            if (!highlightWhenTargeted)
            {
                return;
            }

            float target = _targeted ? 1f : 0f;
            _highlightAmount = Mathf.MoveTowards(_highlightAmount, target, Time.deltaTime * 6f);
            if (_highlightAmount > 0.001f || target > 0f)
            {
                ApplyHighlight(_highlightAmount);
            }
        }

        private void ApplyHighlight(float amount)
        {
            if (highlightRenderers == null || _propertyBlock == null)
            {
                return;
            }

            float pulse = 0.65f + 0.35f * Mathf.Sin(Time.time * highlightPulseSpeed);
            Color emission = highlightColor * (amount * pulse * 2.4f);

            for (int i = 0; i < highlightRenderers.Length; i++)
            {
                Renderer r = highlightRenderers[i];
                if (r == null)
                {
                    continue;
                }

                r.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(EmissionColorId, emission);
                r.SetPropertyBlock(_propertyBlock);
            }
        }

        public virtual void SetTargeted(bool targeted)
        {
            _targeted = targeted;
        }

        /// <summary>Full prompt line, e.g. "Open Lab Wing Shutter  [1250 salvage]".</summary>
        public virtual string BuildPrompt(SalvageWallet wallet)
        {
            if (Cost <= 0)
            {
                return title;
            }

            bool affordable = wallet == null || wallet.CanAfford(Cost);
            string suffix = affordable ? $"  [{Cost} salvage]" : $"  [{Cost} salvage - not enough]";
            return title + suffix;
        }

        public virtual bool CanInteract(SalvageWallet wallet)
        {
            return IsAvailable && (Cost <= 0 || wallet == null || wallet.CanAfford(Cost));
        }

        /// <summary>
        /// Performs the interaction. Implementations must spend from the wallet
        /// themselves; the interactor only gates on <see cref="CanInteract"/>.
        /// Return false to signal the interaction did not take effect.
        /// </summary>
        public abstract bool Interact(SalvageWallet wallet, GameObject instigator);
    }
}
