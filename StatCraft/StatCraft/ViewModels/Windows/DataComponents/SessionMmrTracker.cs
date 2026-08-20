using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.BattlenetApi;

namespace StatCraft.ViewModels.Windows.DataComponents
{
    // Owns the Data tab header's per-race MMR display: each race's current ladder rating and how far it
    // has moved from where the active session started. A session-start baseline comes from two sources —
    // an explicit API lookup when the session begins (SetBaseline), and, for a ladder that lookup didn't
    // already cover, the replay of that ladder's first game played this session (SeedBaselineIfAbsent).
    //
    // Deliberately has no Avalonia dependency (no Dispatcher) so it stays plain-xunit testable. FetchCurrentMmrs
    // resolves on whatever thread its await continues on; marshaling the result back to the UI thread —
    // and deciding whether it's even still relevant, e.g. after the user has since switched profiles — is
    // DataPageViewModel's job, not this class's.
    public class SessionMmrTracker(Sc2LadderService ladderService)
    {
        // One entry per race the active profile has a current ladder rating on. Empty until a fetch
        // completes, and stays empty when there's nothing to show (no saved API credentials, or a
        // profile with no current-season ladder).
        public ObservableCollection<RaceMmrViewModel> CurrentMmrs { get; } = [];

        // Each ladder's rating as of when the session started, so CurrentMmrs can show movement since
        // then. Reset alongside CurrentMmrs; a ladder absent here has no baseline to measure against.
        private readonly Dictionary<LadderRace, long> _sessionStartMmrs = [];

        // Clears back to "no session". Called both when a session ends and just before a fresh fetch
        // starts, so a slow-to-arrive previous lookup can't smear its results into the new session.
        public void Reset()
        {
            CurrentMmrs.Clear();
            _sessionStartMmrs.Clear();
        }

        // Queries the ladder API for every race the profile has placed in. Best-effort: returns null
        // (rather than throwing) if the lookup fails, so the header just stays empty.
        public async Task<IReadOnlyDictionary<LadderRace, long>?> FetchCurrentMmrs(Sc2Profile profile, CancellationToken cancellationToken)
        {
            try
            {
                return await ladderService.GetCurrentMmrAllRacesAsync(profile, cancellationToken);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Applies a successful FetchCurrentMmrs result as the session's baseline — this first successful
        // lookup *is* the baseline; every later change shown in CurrentMmrs is measured against it.
        public void SetBaseline(IReadOnlyDictionary<LadderRace, long> byRace)
        {
            _sessionStartMmrs.Clear();
            foreach ((LadderRace race, long mmr) in byRace)
                _sessionStartMmrs[race] = mmr;

            CurrentMmrs.Clear();
            foreach ((LadderRace race, long mmr) in byRace.OrderBy(kv => kv.Key))
                CurrentMmrs.Add(new RaceMmrViewModel(race, mmr, mmr));
        }

        // The session-start lookup (SetBaseline) only covers ladders the profile had already placed in.
        // Playing the first game of an unplaced race mid-session would otherwise leave that ladder with
        // no baseline and so no visible change for the rest of the session. TryAdd, never overwrite: a
        // ladder that already has a baseline must keep the one from session start, or the delta would
        // silently reset each game.
        public void SeedBaselineIfAbsent(LadderRace race, long mmr) => _sessionStartMmrs.TryAdd(race, mmr);

        // Keeps CurrentMmrs sorted by race so entries don't jump around as they're replaced.
        public void UpdateCurrent(LadderRace race, long mmr)
        {
            RaceMmrViewModel? existing = CurrentMmrs.FirstOrDefault(m => m.Race == race);
            if (existing != null)
                CurrentMmrs.Remove(existing);

            RaceMmrViewModel updated = new(race, mmr, SessionStartMmrFor(race));
            int index = 0;
            while (index < CurrentMmrs.Count && CurrentMmrs[index].Race < race)
                index++;
            CurrentMmrs.Insert(index, updated);
        }

        private long? SessionStartMmrFor(LadderRace race) =>
            _sessionStartMmrs.TryGetValue(race, out long start) ? start : null;
    }
}
