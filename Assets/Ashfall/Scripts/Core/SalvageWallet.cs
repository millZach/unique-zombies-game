using System;
using UnityEngine;

namespace Ashfall.Core
{
    /// <summary>
    /// The run's currency. Salvage is earned from kills, from boarding up breaches and
    /// from clearing rounds, and is spent on routes and weapons.
    /// </summary>
    public class SalvageWallet : MonoBehaviour
    {
        [SerializeField] private int startingSalvage = 500;
        [SerializeField] private int balance;

        /// <summary>(newBalance, delta) -- delta is signed.</summary>
        public event Action<int, int> BalanceChanged;

        /// <summary>Raised when a purchase is attempted with insufficient funds.</summary>
        public event Action<int> PurchaseDenied;

        public int Balance => balance;
        public int TotalEarned { get; private set; }
        public int TotalSpent { get; private set; }

        /// <summary>Scales every award. Salvage Surge raises this temporarily.</summary>
        public float EarnMultiplier { get; set; } = 1f;

        private void Awake()
        {
            balance = startingSalvage;
        }

        private void Start()
        {
            BalanceChanged?.Invoke(balance, 0);
        }

        public void ResetWallet()
        {
            int delta = startingSalvage - balance;
            balance = startingSalvage;
            TotalEarned = 0;
            TotalSpent = 0;
            EarnMultiplier = 1f;
            BalanceChanged?.Invoke(balance, delta);
        }

        /// <summary>Awards salvage after applying the current multiplier. Returns the amount granted.</summary>
        public int Award(int baseAmount)
        {
            if (baseAmount <= 0)
            {
                return 0;
            }

            int amount = Mathf.RoundToInt(baseAmount * Mathf.Max(0f, EarnMultiplier));
            if (amount <= 0)
            {
                return 0;
            }

            balance += amount;
            TotalEarned += amount;
            BalanceChanged?.Invoke(balance, amount);
            return amount;
        }

        /// <summary>Awards salvage ignoring the multiplier, for fixed scripted payouts.</summary>
        public int AwardFlat(int amount)
        {
            if (amount <= 0)
            {
                return 0;
            }

            balance += amount;
            TotalEarned += amount;
            BalanceChanged?.Invoke(balance, amount);
            return amount;
        }

        public bool CanAfford(int cost) => balance >= cost;

        /// <summary>Spends if affordable. Returns false and raises PurchaseDenied otherwise.</summary>
        public bool TrySpend(int cost)
        {
            if (cost <= 0)
            {
                return true;
            }

            if (balance < cost)
            {
                PurchaseDenied?.Invoke(cost);
                return false;
            }

            balance -= cost;
            TotalSpent += cost;
            BalanceChanged?.Invoke(balance, -cost);
            return true;
        }
    }
}
