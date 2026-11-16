namespace CoopGame
{
    /// <summary>Secondary weapons (the wrench is always the main weapon and isn't listed here).</summary>
    public enum WeaponId : byte
    {
        None = 0,
        Pistol = 1,
        ShockWhip = 2,
        Grenade = 3,
        Shotgun = 4,
        RocketLauncher = 5,
        CharmRobot = 6,
        DiscLauncher = 7,
        Freezer = 8,
        // Future weapons: add an id here, a PlayerWeapon component on the Player prefab, and a slot below.
    }

    /// <summary>The 8 slots of the weapon select panel (TAB). Empty slots show as "—".</summary>
    public static class WeaponCatalog
    {
        public readonly struct Slot
        {
            public readonly WeaponId Id;
            public readonly string Name;

            public Slot(WeaponId id, string name)
            {
                Id = id;
                Name = name;
            }

            public bool IsEmpty => Id == WeaponId.None;
        }

        public const string MainWeaponName = "Klucz";

        public static readonly Slot[] Slots =
        {
            new(WeaponId.Pistol, "Pistolet"),
            new(WeaponId.ShockWhip, "Elektrobicz"),
            new(WeaponId.Grenade, "Granat"),
            new(WeaponId.Shotgun, "Shotgun"),
            new(WeaponId.RocketLauncher, "Wyrzutnia rakiet"),
            new(WeaponId.CharmRobot, "Robot czarodziej"),
            new(WeaponId.DiscLauncher, "Wyrzutnia dysków"),
            new(WeaponId.Freezer, "Zamrażacz"),
        };

        public static string NameOf(WeaponId id)
        {
            foreach (var slot in Slots)
                if (slot.Id == id) return slot.Name;
            return "—";
        }
    }
}
