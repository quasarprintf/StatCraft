using s2protocol.NET;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
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

            GameData game = new() { ReplayData = parsedReplayData };
            gameDataRepository.InsertGame(game, profile.Id);
            GameParsed?.Invoke(game);

            // Deliberately not awaited: resolving the post-game rating can take minutes of polling, and
            // the imported game should appear immediately regardless of whether that ever succeeds.
            _ = TrackMmrChange(game, profile);
            return null;
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

                // Only ranked 1v1 has a rating that moves in a way this can attribute to one game. Team
                // games have separate per-team ratings, and an unranked/custom game reports no rating at
                // all (Mmr 0), leaving nothing to compare against.
                if (replay.Allies.Length != 0 || replay.Opponents.Length != 1 || replay.Player.Mmr <= 0)
                    return;

                int? gamePlayerId = replay.Player.GamePlayerId;
                if (gamePlayerId == null)
                    return;

                foreach (TimeSpan delay in MmrPollDelays)
                {
                    await Task.Delay(delay, CancellationToken.None);

                    long? currentMmr = await ladderService.GetCurrentMmrAsync(profile, replay.Player.Race, CancellationToken.None);
                    if (currentMmr == null || currentMmr == replay.Player.Mmr)
                        continue;

                    gameDataRepository.UpdateGamePlayerMmrAfter(gamePlayerId.Value, currentMmr.Value);
                    replay.Player.MmrAfter = currentMmr.Value;
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
