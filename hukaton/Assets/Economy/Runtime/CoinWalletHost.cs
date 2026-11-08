using UnityEngine;

namespace Hakaton.Economy.Unity
{
    /// <summary>Authored scene ownership of a shared wallet. Gameplay consumers reference this same component.</summary>
    public sealed class CoinWalletHost : MonoBehaviour
    {
        [Min(0)] public int startingBalance;
        CoinWallet wallet;

        // Lazy initialization makes consumer Awake order irrelevant; never re-seed on enable.
        public CoinWallet Wallet => wallet ?? (wallet = new CoinWallet(startingBalance));
    }
}
