// Stable world-layer identities shared by actors, projectiles and world services.
// Display names and generation choices belong to layer definitions.
using System;

public static class WorldLayerId
{
    public const string Surface = "surface";
    public const string Underground1 = "underground_1";

    // =========================================================
    // Reject empty or unstable IDs without restricting future layer names.
    public static void Validate(string id)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("A world layer requires an ID.");

        foreach (char character in id)
            if (!(character >= 'a' && character <= 'z') &&
                !(character >= '0' && character <= '9') &&
                character != '_')
                throw new ArgumentException(
                    $"Layer ID '{id}' must use lowercase letters, digits or underscores.");
    }
}