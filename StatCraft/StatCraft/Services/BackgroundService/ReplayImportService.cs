using s2protocol.NET;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Models.GameData.Race;
using StatCraft.Services.BattlenetApi;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataParsing;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace StatCraft.Services.BackgroundService
{
    // Solely responsible for turning one replay file into a persisted GameData for a given profile —
    // decoding, extracting, parsing, and inserting. Doesn't know or care how the file was found (folder
    // watching vs. a manual import both funnel through here).
    public class ReplayImportService(ILogger logger, ReplayDataExtractor replayDataExtractor,
        GameDataRepository gameDataRepository, Sc2LadderService ladderService)
    {
        // How long to keep asking Battle.net for the post-game rating. The ladder API doesn't reflect a
        // result the instant the replay file lands on disk, and the lag isn't fixed, so rather than guess
        // one interval we re-check on a widening schedule until the rating actually moves off its
        // pre-game value. Totals a little over four minutes before giving up.
        private static readonly TimeSpan[] MmrPollDelays =
        [
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(120),
        ];

        // Raised after a replay is parsed and persisted, so the UI can add a row for it.
        // Internal because GameData is internal; the class itself stays public.
        internal event Action<GameData>? GameParsed;

        // Raised later, once the post-game MMR has been resolved for an already-imported game, so the UI
        // can refresh that row in place. May never fire for a given game — see TrackMmrChange.
        internal event Action<GameData>? GameMmrUpdated;

        // Returns null on success, or a user-facing message describing why the replay was rejected.
        public async Task<string?> ImportReplay(string filePath, Sc2Profile profile)
        {
            logger.LogInfo($"Processing replay: {filePath}", profile);

            using ReplayDecoder decoder = new();
            Sc2Replay? replay = await decoder.DecodeAsync(filePath);
            if (replay == null)
            {
                logger.LogWarning($"Failed to decode replay: {filePath}", profile);
                return $"Could not read \"{Path.GetFileName(filePath)}\" — it doesn't look like a valid StarCraft II replay.";
            }

            DateTimeOffset replayTimestamp = new DateTimeOffset(File.GetLastWriteTimeUtc(filePath));
            RawReplayData rawReplayData = replayDataExtractor.Extract(replay, replayTimestamp);
            logger.LogInfo($"Replay parsed: {filePath}", profile, rawReplayData);

            ParsedReplayData parsedReplayData;
            try
            {
                parsedReplayData = replayDataExtractor.Parse(rawReplayData, profile);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning($"Could not match active profile in replay, skipping: {filePath} ({ex.Message})", profile);
                return $"\"{Path.GetFileName(filePath)}\" doesn't contain a match for the active profile.";
            }

            GameData game = new()
            {
                ReplayData = parsedReplayData,
                GameType = ResolveGameType(parsedReplayData, profile),
            };
            gameDataRepository.InsertGame(game, profile.Id);
            GameParsed?.Invoke(game);

            // Deliberately not awaited: resolving the post-game rating can take minutes of polling, and
            // the imported game should appear immediately regardless of whether that ever succeeds.
            _ = TrackMmrChange(game, profile);
            return null;
        }

        // Classified against the last ranked MMR known for the ladder this game was played on, which the
        // ladder service keeps current as polls resolve. Must happen before TrackMmrChange starts, since
        // that's what will move the known value on to this game's result.
        private GameType ResolveGameType(ParsedReplayData replay, Sc2Profile profile)
        {
            LadderRace? race = LadderRaceExtensions.FromPlayer(replay.Player.Race, replay.Player.Random);
            long? lastKnown = race.HasValue ? ladderService.GetLastKnownMmr(profile, race.Value) : null;
            return GameTypeResolver.Resolve(replay, lastKnown);
        }

        // Polls the ladder API until the tracked player's rating differs from what the replay recorded
        // going into the game, then persists it as MmrAfter. Best-effort throughout — a failure here must
        // never surface to the user or affect the already-successful import.
        //
        // Known limitation: if a second game finishes while this is still polling, the rating we
        // eventually observe reflects both games, so the delta gets attributed entirely to the first one.
        private async Task TrackMmrChange(GameData game, Sc2Profile profile)
        {
            try
            {
                ParsedReplayData replay = game.ReplayData;

                // Only a rated 1v1 has a rating that moves in a way this can attribute to one game, and
                // only a ranked one moves the *ranked* rating this polls for — unranked play has its own
                // separate hidden rating, so polling after one would just time out.
                if (!replay.IsRatedOneVsOne || game.GameType != GameType.Ranked)
                    return;

                int? gamePlayerId = replay.Player.GamePlayerId;
                if (gamePlayerId == null)
                    return;

                // Queueing as Random earns Random MMR whatever race then spawned, so the ladder to watch
                // comes from the flag rather than from the race the replay recorded.
                LadderRace? ladderRace = LadderRaceExtensions.FromPlayer(replay.Player.Race, replay.Player.Random);
                if (ladderRace == null)
                    return;

                foreach (TimeSpan delay in MmrPollDelays)
                {
                    await Task.Delay(delay, CancellationToken.None);

                    long? currentMmr = await ladderService.GetCurrentMmrAsync(profile, ladderRace.Value, CancellationToken.None);
                    if (currentMmr == null || currentMmr == replay.Player.Mmr)
                        continue;

                    gameDataRepository.UpdateGamePlayerMmrAfter(gamePlayerId.Value, currentMmr.Value);
                    replay.Player.MmrAfter = currentMmr.Value;

                    // Moves the known ranked rating forward, so the *next* game on this ladder is
                    // classified against where it will actually start rather than a stale value.
                    ladderService.RecordObservedMmr(profile, ladderRace.Value, currentMmr.Value);

                    logger.LogInfo($"MMR after game resolved: {replay.Player.Mmr} -> {currentMmr.Value} ({currentMmr.Value - replay.Player.Mmr:+#;-#;0})", profile);
                    GameMmrUpdated?.Invoke(game);
                    return;
                }

                logger.LogInfo($"Post-game MMR never changed from {replay.Player.Mmr}; leaving it unknown.", profile);
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Post-game MMR tracking failed: {ex.Message}", profile);
            }
        }
    }
}
