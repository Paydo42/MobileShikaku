using System;
using UnityEngine;

/// <summary>
/// Global player level (1-100) earned by completing stages in any mode.
///
/// THE ECONOMY
/// -----------
/// Reward curve (linear, so later stages are worth more):
///     R(s) = REWARD_BASE + REWARD_STEP * (s - 1)          s = 1..100
///          = 20 + 1*(s - 1)      ->  R(1) = 20, R(100) = 119
///
/// Total XP available, all modes, each stage once:
///     POOL = MODE_COUNT * SUM(R(s), s=1..100)
///          = 4 * (100*20 + 1*4950) = 4 * 6950 = 27,800
///
/// Requirement curve (exponential shape, exactly normalised to POOL):
///     weight(L) = GROWTH^(L-1)                            L = 1..99
///     cum(L)    = ROUND( POOL * SUM(weight(1..L)) / SUM(weight(1..99)) )
///     Req(L)    = cum(L) - cum(L-1)                       cum(0) = 0
///
/// Normalising by the cumulative sum (rather than rounding each level
/// independently) is what makes the totals balance to the exact integer:
/// cum(99) is POOL by construction, so SUM(Req) == POOL with no drift. Verified
/// by <see cref="Simulate"/>: 400 stages -> level 100 with 0 XP left over.
///
/// GROWTH = 1.035 was chosen so the pacing lands in the designed bands:
///     levels  1-10 : 1-2 stages each
///     levels 45-54 : 3-4 stages each
///     levels 90-99 : 7-8 stages each
///
/// NOTE: the economy assumes MODE_COUNT modes of STAGES_PER_MODE stages. Ship
/// fewer and the pool shrinks, so level 100 becomes unreachable — rerun
/// <see cref="Simulate"/> after changing either constant.
/// </summary>
public static class PlayerLevel
{
    public const int MaxLevel = 100;
    public const int StagesPerMode = 100;
    public const int ModeCount = 4;

    private const int RewardBase = 20;
    private const int RewardStep = 1;
    private const double Growth = 1.035;

    private const string PrefTotalXp = "xp_total";
    private static string ClaimKey(string modeId) => $"xp_claimed_{modeId}";

    private static readonly string[] Titles =
    {
        "Beginner",     // 1-10
        "Novice",       // 11-20
        "Apprentice",   // 21-30
        "Intermediate", // 31-40
        "Adept",        // 41-50
        "Expert",       // 51-60
        "Master",       // 61-70
        "Elite",        // 71-80
        "Legend",       // 81-90
        "Grandmaster",  // 91-100
    };

    /// <summary>Raised when the player's level increases, with the new level.</summary>
    public static event Action<int> LeveledUp;

    private static int[] _requirements; // [1..99]; XP to go from level L to L+1

    #region Curves

    /// <summary>XP a stage is worth. <paramref name="stageIndex"/> is 0-based.</summary>
    public static int StageReward(int stageIndex) =>
        RewardBase + RewardStep * Mathf.Clamp(stageIndex, 0, StagesPerMode - 1);

    /// <summary>Total XP obtainable by clearing every stage of every mode once.</summary>
    public static int TotalXpPool
    {
        get
        {
            int pool = 0;
            for (int s = 0; s < StagesPerMode; s++) pool += ModeCount * StageReward(s);
            return pool;
        }
    }

    // Built once: the exponential weights are normalised against the cumulative
    // sum so the rounded per-level costs add up to exactly TotalXpPool.
    private static void EnsureCurve()
    {
        if (_requirements != null) return;

        int pool = TotalXpPool;
        int n = MaxLevel - 1;

        var weights = new double[n + 1];
        double totalWeight = 0d;
        for (int lv = 1; lv <= n; lv++)
        {
            weights[lv] = Math.Pow(Growth, lv - 1);
            totalWeight += weights[lv];
        }

        _requirements = new int[n + 1];
        double running = 0d;
        int previousCumulative = 0;
        for (int lv = 1; lv <= n; lv++)
        {
            running += weights[lv];
            int cumulative = (int)Math.Round(pool * running / totalWeight);
            _requirements[lv] = cumulative - previousCumulative;
            previousCumulative = cumulative;
        }
    }

    /// <summary>XP needed to go from <paramref name="level"/> to the next one. 0 at the cap.</summary>
    public static int XpToNextLevel(int level)
    {
        if (level < 1 || level >= MaxLevel) return 0;
        EnsureCurve();
        return _requirements[level];
    }

    #endregion

    #region Player state

    /// <summary>Lifetime XP earned. Persisted.</summary>
    public static int TotalXp
    {
        get => Mathf.Max(0, PlayerPrefs.GetInt(PrefTotalXp, 0));
        private set
        {
            PlayerPrefs.SetInt(PrefTotalXp, Mathf.Max(0, value));
            PlayerPrefs.Save();
        }
    }

    /// <summary>Current level, 1..<see cref="MaxLevel"/>, derived from <see cref="TotalXp"/>.</summary>
    public static int Level
    {
        get
        {
            SplitXp(TotalXp, out int level, out _);
            return level;
        }
    }

    /// <summary>XP earned since reaching the current level (for a progress bar).</summary>
    public static int XpIntoLevel
    {
        get
        {
            SplitXp(TotalXp, out _, out int into);
            return into;
        }
    }

    /// <summary>XP the current level needs in total (for a progress bar). 0 at the cap.</summary>
    public static int XpForCurrentLevel => XpToNextLevel(Level);

    /// <summary>
    /// Break an arbitrary lifetime-XP total into level, progress within that
    /// level, and what that level costs. Lets UI animate a bar across a gain
    /// (by sweeping totalXp) without disturbing the saved state.
    /// <paramref name="xpForLevel"/> is 0 at the cap.
    /// </summary>
    public static void SplitTotalXp(int totalXp, out int level, out int xpIntoLevel, out int xpForLevel)
    {
        SplitXp(totalXp, out level, out xpIntoLevel);
        xpForLevel = XpToNextLevel(level);
    }

    // Walks the requirement table to turn lifetime XP into level + progress.
    private static void SplitXp(int totalXp, out int level, out int xpIntoLevel)
    {
        EnsureCurve();
        level = 1;
        int remaining = Mathf.Max(0, totalXp);
        while (level < MaxLevel && remaining >= _requirements[level])
        {
            remaining -= _requirements[level];
            level++;
        }
        xpIntoLevel = level >= MaxLevel ? 0 : remaining;
    }

    #endregion

    #region Awarding

    /// <summary>
    /// Grant XP for clearing a stage. Full value the first time; 0 for every
    /// replay, so grinding an easy stage can't farm levels. Returns the XP
    /// actually granted.
    /// </summary>
    public static int AwardStage(string modeId, int stageIndex)
    {
        if (string.IsNullOrEmpty(modeId)) return 0;
        if (stageIndex < 0 || stageIndex >= StagesPerMode) return 0;
        if (IsClaimed(modeId, stageIndex)) return 0;

        MarkClaimed(modeId, stageIndex);

        int before = Level;
        int reward = StageReward(stageIndex);
        TotalXp += reward;

        int after = Level;
        if (after > before) LeveledUp?.Invoke(after);

        return reward;
    }

    /// <summary>Whether this stage has already paid out its one-time XP.</summary>
    public static bool IsClaimed(string modeId, int stageIndex)
    {
        if (stageIndex < 0 || stageIndex >= StagesPerMode) return false;
        string mask = PlayerPrefs.GetString(ClaimKey(modeId), string.Empty);
        return stageIndex < mask.Length && mask[stageIndex] == '1';
    }

    // The claim record is one character per stage, so a mode costs a single
    // PlayerPrefs entry instead of one per level.
    private static void MarkClaimed(string modeId, int stageIndex)
    {
        string key = ClaimKey(modeId);
        string mask = PlayerPrefs.GetString(key, string.Empty);
        if (mask.Length < StagesPerMode) mask = mask.PadRight(StagesPerMode, '0');

        var chars = mask.ToCharArray();
        chars[stageIndex] = '1';
        PlayerPrefs.SetString(key, new string(chars));
        PlayerPrefs.Save();
    }

    #endregion

    #region Titles

    /// <summary>Status title for a level: 1-10 Beginner ... 91-100 Grandmaster.</summary>
    public static string GetTitle(int level)
    {
        int tier = Mathf.Clamp((Mathf.Clamp(level, 1, MaxLevel) - 1) / 10, 0, Titles.Length - 1);
        return Titles[tier];
    }

    /// <summary>Status title for the player's current level.</summary>
    public static string CurrentTitle => GetTitle(Level);

    #endregion

    #region Debug / validation

    /// <summary>Wipe level, XP, and every mode's claim record.</summary>
    public static void ResetAll(params string[] modeIds)
    {
        PlayerPrefs.DeleteKey(PrefTotalXp);
        if (modeIds != null)
            foreach (string modeId in modeIds) PlayerPrefs.DeleteKey(ClaimKey(modeId));
        PlayerPrefs.Save();
    }

    /// <summary>
    /// Proves the economy balances: awards every stage of every mode exactly
    /// once against a throwaway in-memory tally (PlayerPrefs untouched) and logs
    /// the resulting level, which must be exactly <see cref="MaxLevel"/> with no
    /// XP left over.
    /// </summary>
    public static void Simulate()
    {
        EnsureCurve();

        int pool = TotalXpPool;
        int requirementSum = 0;
        for (int lv = 1; lv < MaxLevel; lv++) requirementSum += _requirements[lv];

        int level = 1;
        int xp = 0;
        int stagesPlayed = 0;
        var stagesAtLevel = new int[MaxLevel + 1];

        // Stage s across every mode, then stage s+1: roughly how a player
        // actually advances when the modes are played side by side.
        for (int s = 0; s < StagesPerMode; s++)
        {
            for (int mode = 0; mode < ModeCount; mode++)
            {
                xp += StageReward(s);
                stagesPlayed++;
                stagesAtLevel[level]++;
                while (level < MaxLevel && xp >= _requirements[level])
                {
                    xp -= _requirements[level];
                    level++;
                }
            }
        }

        bool balanced = pool == requirementSum && level == MaxLevel && xp == 0;

        Debug.Log(
            $"[PlayerLevel] XP economy check\n" +
            $"  reward curve      R(s) = {RewardBase} + {RewardStep}*(s-1)  ->  R(1)={StageReward(0)}, R(100)={StageReward(StagesPerMode - 1)}\n" +
            $"  requirement curve weight(L) = {Growth}^(L-1), normalised to the pool\n" +
            $"  XP pool ({ModeCount} modes x {StagesPerMode} stages) = {pool}\n" +
            $"  sum of {MaxLevel - 1} level requirements     = {requirementSum}\n" +
            $"  stages played = {stagesPlayed}, final level = {level}, leftover XP = {xp}\n" +
            $"  stages per level  L1-L10: {Band(stagesAtLevel, 1, 10)}\n" +
            $"                   L45-L54: {Band(stagesAtLevel, 45, 54)}\n" +
            $"                   L90-L99: {Band(stagesAtLevel, 90, 99)}\n" +
            $"  BALANCED = {balanced}");

        if (!balanced)
            Debug.LogError("[PlayerLevel] Economy does NOT balance — check the curve constants.");
    }

    private static string Band(int[] stagesAtLevel, int from, int to)
    {
        var parts = new string[to - from + 1];
        for (int lv = from; lv <= to; lv++) parts[lv - from] = stagesAtLevel[lv].ToString();
        return string.Join(", ", parts);
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Shikaku/Validate XP Economy")]
    private static void ValidateFromMenu() => Simulate();
#endif

    #endregion
}
