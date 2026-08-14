using System;
using UnityEngine;
using Ashfall.Core;

namespace Ashfall.World
{
    /// <summary>
    /// A boarded breach in the station wall.
    ///
    /// Boards are the slice's second economy: enemies tear them off to get in, and the
    /// player earns salvage putting them back. Because the boards are real colliders on
    /// the blocking layer, a fully-boarded breach physically stops enemies rather than
    /// just costing them time -- they have to stop and work.
    /// </summary>
    public class Barricade : Interactable, IDamageable
    {
        [Header("Boards")]
        [SerializeField] private Transform[] boards = Array.Empty<Transform>();
        [SerializeField] private int startingBoards = 4;
        [SerializeField] private float healthPerBoard = 100f;

        [Header("Repair")]
        [SerializeField] private float repairHoldSeconds = 0.65f;
        [SerializeField] private int salvagePerBoard = 20;

        [Header("Motion")]
        [SerializeField] private float boardTearDistance = 1.8f;
        [SerializeField] private float boardAnimateSpeed = 6f;

        public event Action<Barricade, int> BoardCountChanged;
        public event Action<Barricade, int> Repaired;

        private float _currentBoardHealth;
        private int _boardCount;
        private Vector3[] _boardRestPositions;
        private Quaternion[] _boardRestRotations;
        private Collider[][] _boardColliders;

        public int BoardCount => _boardCount;
        public int MaxBoards => boards != null ? boards.Length : 0;
        public bool IsFullyBoarded => _boardCount >= MaxBoards;
        public bool IsBreached => _boardCount <= 0;

        /// <summary>Barricades never "die"; enemies just strip them. Always alive so damage keeps flowing.</summary>
        public bool IsAlive => true;

        public override float HoldSeconds => repairHoldSeconds;
        public override int Cost => 0;
        public override bool IsAvailable => isActiveAndEnabled && !IsFullyBoarded;

        protected override void Awake()
        {
            base.Awake();
            title = "Board up breach";
            CacheBoards();
            SetBoardCount(Mathf.Clamp(startingBoards, 0, MaxBoards), instant: true);
        }

        private void CacheBoards()
        {
            int n = MaxBoards;
            _boardRestPositions = new Vector3[n];
            _boardRestRotations = new Quaternion[n];
            _boardColliders = new Collider[n][];

            for (int i = 0; i < n; i++)
            {
                if (boards[i] == null)
                {
                    _boardColliders[i] = Array.Empty<Collider>();
                    continue;
                }

                _boardRestPositions[i] = boards[i].localPosition;
                _boardRestRotations[i] = boards[i].localRotation;
                _boardColliders[i] = boards[i].GetComponentsInChildren<Collider>();
            }
        }

        public void Configure(Transform[] boardTransforms, int initialBoards, float boardHealth, int salvage)
        {
            boards = boardTransforms;
            startingBoards = initialBoards;
            healthPerBoard = boardHealth;
            salvagePerBoard = salvage;
        }

        public void ResetBarricade()
        {
            SetBoardCount(Mathf.Clamp(startingBoards, 0, MaxBoards), instant: true);
        }

        public override string BuildPrompt(SalvageWallet wallet)
        {
            return $"Hold to board up breach   +{salvagePerBoard} salvage   [{_boardCount}/{MaxBoards}]";
        }

        public override bool CanInteract(SalvageWallet wallet)
        {
            return IsAvailable;
        }

        public override bool Interact(SalvageWallet wallet, GameObject instigator)
        {
            if (IsFullyBoarded)
            {
                return false;
            }

            SetBoardCount(_boardCount + 1, instant: false);
            int granted = wallet != null ? wallet.Award(salvagePerBoard) : salvagePerBoard;
            Repaired?.Invoke(this, granted);
            return true;
        }

        /// <summary>Enemies chew through boards by dealing damage to the barricade.</summary>
        public float ApplyDamage(in DamageInfo info)
        {
            if (IsBreached)
            {
                return 0f;
            }

            float amount = Mathf.Max(0f, info.Amount);
            _currentBoardHealth -= amount;

            while (_currentBoardHealth <= 0f && _boardCount > 0)
            {
                SetBoardCount(_boardCount - 1, instant: false);
                if (_boardCount > 0)
                {
                    _currentBoardHealth += healthPerBoard;
                }
                else
                {
                    _currentBoardHealth = 0f;
                }
            }

            return amount;
        }

        private void SetBoardCount(int count, bool instant)
        {
            int n = MaxBoards;
            count = Mathf.Clamp(count, 0, n);

            bool changed = count != _boardCount;
            _boardCount = count;
            _currentBoardHealth = _boardCount > 0 ? healthPerBoard : 0f;

            for (int i = 0; i < n; i++)
            {
                bool present = i < _boardCount;
                Collider[] cols = _boardColliders[i];
                for (int c = 0; c < cols.Length; c++)
                {
                    if (cols[c] != null)
                    {
                        cols[c].enabled = present;
                    }
                }

                if (boards[i] == null)
                {
                    continue;
                }

                if (instant)
                {
                    boards[i].gameObject.SetActive(true);
                    boards[i].localPosition = present
                        ? _boardRestPositions[i]
                        : _boardRestPositions[i] + Vector3.down * boardTearDistance;
                    boards[i].localRotation = present
                        ? _boardRestRotations[i]
                        : _boardRestRotations[i] * Quaternion.Euler(0f, 0f, 55f);
                }
            }

            if (changed)
            {
                BoardCountChanged?.Invoke(this, _boardCount);
            }
        }

        protected override void Update()
        {
            base.Update();

            int n = MaxBoards;
            float t = 1f - Mathf.Exp(-boardAnimateSpeed * Time.deltaTime);

            for (int i = 0; i < n; i++)
            {
                if (boards[i] == null)
                {
                    continue;
                }

                bool present = i < _boardCount;
                Vector3 targetPosition = present
                    ? _boardRestPositions[i]
                    : _boardRestPositions[i] + Vector3.down * boardTearDistance;
                Quaternion targetRotation = present
                    ? _boardRestRotations[i]
                    : _boardRestRotations[i] * Quaternion.Euler(0f, 0f, 55f);

                boards[i].localPosition = Vector3.Lerp(boards[i].localPosition, targetPosition, t);
                boards[i].localRotation = Quaternion.Slerp(boards[i].localRotation, targetRotation, t);
            }
        }
    }
}
