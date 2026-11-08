using System;

namespace Hakaton.Economy
{
    /// <summary>One balance shared by earning and spending systems. Mutations are validated before changing it.</summary>
    public sealed class CoinWallet
    {
        public int Balance { get; private set; }

        public CoinWallet(int startingBalance = 0)
        {
            if (startingBalance < 0) throw new ArgumentOutOfRangeException(nameof(startingBalance));
            Balance = startingBalance;
        }

        public void Credit(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            Balance = checked(Balance + amount);
        }

        public bool TrySpend(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (amount > Balance) return false;
            Balance -= amount;
            return true;
        }

        /// <summary>Restores a validated game snapshot without replacing the shared wallet object.</summary>
        public bool TryRestoreBalance(int balance)
        {
            if (balance < 0) return false;
            Balance = balance;
            return true;
        }
    }
}
