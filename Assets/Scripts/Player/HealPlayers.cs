using UnityEngine;

namespace CoopGame
{
    /// <summary>Server: brings every player back to full health (e.g. from a cutscene's server event).</summary>
    public class HealPlayers : MonoBehaviour
    {
        public void HealAll()
        {
            foreach (var player in PlayerHealth.All)
                if (player != null && player.IsServer && player.IsAlive) player.Heal(1000);
        }
    }
}
