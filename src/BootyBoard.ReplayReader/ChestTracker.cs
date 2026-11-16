namespace BootyBoard.Replay;

/// <summary>One chest search (or break) by a pawn. Kind: opened, broken, or carried (already searched when first seen).</summary>
public sealed record SearchEvent(float T, uint Pawn, uint? Guid, string Chest, string Kind);

/// <summary>
/// Turns chest replication updates into search EVENTS. Chests respawn, so one chest can be searched more than once:
/// a new searcher, or the search-animation counter going up, is one search. A chest that already carries a searcher
/// when first seen is "carried" (searched before it came into view) and is not counted.
/// Broken = searcher stamped + destroyed at the same moment, never bAlreadySearched; or destroyed &lt; 1 s after an
/// "opened" event without having been searched.
/// </summary>
public sealed class ChestTracker
{
    private readonly Dictionary<uint, (uint pawn, uint anim)> _prev = new();
    public List<SearchEvent> Events { get; } = new();

    /// <summary>A channel index was reused for a different actor.</summary>
    public void Reset(uint channel) => _prev.Remove(channel);

    public void Update(uint channel, uint? guid, string chestType, uint pawnNow, uint animCount, bool destroyed, bool alreadySearched, float t)
    {
        var seen = _prev.TryGetValue(channel, out var prev);
        var newPawn = pawnNow != 0 && pawnNow != prev.pawn;
        var newAnim = animCount != 0 && animCount > prev.anim;
        var who = pawnNow != 0 ? pawnNow : prev.pawn;
        if ((newPawn || (newAnim && seen)) && who != 0)
        {
            var kind = !seen ? "carried" : (destroyed && !alreadySearched ? "broken" : "opened");
            Events.Add(new SearchEvent(t, who, guid, chestType, kind));
        }
        else if (seen && destroyed && prev.pawn != 0 && Events.Count > 0)
        {
            var i = Events.FindLastIndex(e => e.Guid == guid);
            if (i >= 0 && Events[i].Kind == "opened" && t - Events[i].T < 1.0f && !alreadySearched)
                Events[i] = Events[i] with { Kind = "broken" };
        }
        _prev[channel] = (who, Math.Max(prev.anim, animCount));
    }
}
