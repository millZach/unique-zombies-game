namespace Ashfall.Core
{
    /// <summary>
    /// The run's top-level state machine.
    ///
    /// Boot -> Briefing -> RoundIntro -> Combat -> RoundClear -> RoundIntro ... -> RunComplete
    ///                                      \-> Defeat
    /// Paused is tracked separately from the state so unpausing always resumes the
    /// state the run was actually in.
    /// </summary>
    public enum GameState
    {
        Boot,
        Briefing,
        RoundIntro,
        Combat,
        RoundClear,
        Defeat,
        RunComplete
    }

    public static class GameStateExtensions
    {
        /// <summary>True while the player should have full control and enemies may act.</summary>
        public static bool IsPlayable(this GameState state)
        {
            return state == GameState.Briefing
                   || state == GameState.RoundIntro
                   || state == GameState.Combat
                   || state == GameState.RoundClear;
        }

        public static bool IsRunOver(this GameState state)
        {
            return state == GameState.Defeat || state == GameState.RunComplete;
        }
    }
}
