namespace CoopGame
{
    /// <summary>
    /// Local UI state that gameplay has to respect: while a menu (e.g. the weapon select panel) is open the
    /// cursor is free, the camera doesn't turn, and attacks are off so clicks only go to the menu.
    /// </summary>
    public static class GameplayUI
    {
        public static bool IsMenuOpen { get; set; }

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => IsMenuOpen = false;
    }
}
