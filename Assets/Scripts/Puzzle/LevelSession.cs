/// <summary>
/// Tiny static carrier for the player's current choices. Static fields persist
/// across scene loads, so the menu scenes write here and the game scene reads.
/// No GameObject needed.
/// </summary>
public static class LevelSession
{
    /// <summary>The active game mode's levels, chosen on the game-mode screen.</summary>
    public static LevelDatabase SelectedDatabase;

    /// <summary>Index into <see cref="SelectedDatabase"/> chosen on level select.</summary>
    public static int SelectedLevel = 0;
}
