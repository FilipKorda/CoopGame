using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoopGame
{
    /// <summary>
    /// Title screen of the MainMenu scene. "Zacznij przygodę" opens the lobby: the LobbyCanvas is built by
    /// NetworkBootstrap in the Bootstrap scene (next to the NetworkManager), so this loads that scene.
    /// "Ustawienia" and "Wyjdź z gry" have no function yet.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        [Tooltip("Scene with the NetworkManager and the LobbyCanvas (must be in the Build Settings).")]
        [SerializeField] string lobbySceneName = "Bootstrap";

        /// <summary>Button "Zacznij przygodę".</summary>
        public void StartAdventure() => SceneManager.LoadScene(lobbySceneName);
    }
}
