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
    public class ReplayImportService(ILogger logger, ReplayDataExtractor replayDataExtractor,
        GameDataRepository gameDataRepository, MapRepository mapRepository, Sc2LadderService ladderService)
    {
        private static readonly TimeSpan[] MmrPollDelays =
        [
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(15),
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(60),
            TimeSpan.FromSeconds(120),
        ];

        // Raised after a replay is parsed and persisted
        internal event Action<GameData>? GameParsed;

        // Raised once the post-game MMR has been resolved for an already-imported game
        internal event Action<GameData>? GameMmrUpdated;

        // Returns null on success, or a user-facing message describing why the replay was rejected.
        public async Task<string?> ImportReplay(string filePath, Sc2Profile profile)
        {
            try
            {
                return await ImportReplayCore(filePath, profile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Expected rather than exceptional: the watcher reports a file the moment it appears in
                // the folder, which can be while StarCraft II is still writing it.
                logger.LogWarning($"Replay file could not be read: {filePath} ({ex.Message})", profile);
                return $"\"{Path.GetFileName(filePath)}\" couldn't be read — it may still be in use. Try again in a moment.";
            }
            catch (DecodeException ex)
            {
                // How the decoder actually reports "this isn't a replay" — it throws rather than
                // returning null, so this, not the null check in ImportReplayCore, is the live path.
                logger.LogWarning($"Failed to decode replay: {filePath} ({ex.Message})", profile);
                return $"Could not read \"{Path.GetFileName(filePath)}\" — it doesn't look like a valid StarCraft II replay.";
            }
            catch (Exception ex)
            {
                logger.LogError($"Unexpected failure importing replay: {filePath} ({ex})", profile);
                return $"Something went wrong importing \"{Path.GetFileName(filePath)}\". See the log for details.";
            }
        }

        private async Task<string?> ImportReplayCore(string filePath, Sc2Profile profile)
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
                Map = mapRepository.GetOrCreateMap(rawReplayData.MapName),
                ReplayData = parsedReplayData,
                GameType = ResolveGameType(parsedReplayData, profile),
            };
            gameDataRepository.InsertGame(game, profile.Id);
            GameParsed?.Invoke(game);

            //TrackMmrChange never throws, it just logs and potentially raises an event, don't need to await it
            _ = TrackMmrChange(game, profile);
            return null;
        }

        //distinguish ranked vs unranked using the heuristic of comparing replay mmr to current ranked mmr from battlenet api
        private GameType ResolveGameType(ParsedReplayData replay, Sc2Profile profile)
        {
            LadderRace? race = LadderRaceExtensions.FromPlayer(replay.Player.Race, replay.Player.Random);
            long? lastKnown = race.HasValue ? ladderService.GetLastKnownMmr(profile, race.Value) : null;
            return GameTypeResolver.Resolve(replay, lastKnown);
        }

        //poll battlenet api for ladder mmr until we see that mmr change. Attributes that change to the replay being parsed
        private async Task TrackMmrChange(GameData game, Sc2Profile profile)
        {
            try
            {
                ParsedReplayData replay = game.ReplayData;

                if (!replay.IsRatedOneVsOne || game.GameType != GameType.Ranked)
                    return;

                int? gamePlayerId = replay.Player.GamePlayerId;
                if (gamePlayerId == null)
                    return;

                LadderRace? ladderRace = LadderRaceExtensions.FromPlayer(replay.Player.Race, replay.Player.Random);
                if (ladderRace == null)
                    return;

                foreach (TimeSpan delay in MmrPollDelays)
                {
                    await Task.Delay(delay, CancellationToken.None);

                    long? currentMmr = await ladderService.GetCurrentMmrAsync(profile, ladderRace.Value, CancellationToken.None);
                    if (currentMmr == null || currentMmr == replay.Player.Mmr)
                        continue;

                    long mmrChange = currentMmr.Value - replay.Player.Mmr;
                    gameDataRepository.UpdateGamePlayerMmrAfter(gamePlayerId.Value, currentMmr.Value);
                    replay.Player.MmrAfter = currentMmr.Value;

                    logger.LogInfo($"MMR after game resolved: {replay.Player.Mmr} -> {currentMmr.Value} ({mmrChange:+#;-#;0})", profile);

                    TryCorrectOpponentMmr(replay, mmrChange, profile);

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

        // Only reachable for a rated 1v1 (see TrackMmrChange's own guard), so there's exactly one
        // opponent whose MMR the Elo formula can meaningfully judge and, if needed, re-estimate.
        // PredictedChange says what the tracked player's own MmrChange should have been, given the
        // recorded opponent MMR — comparing that against what MmrChange actually was is a direct test of
        // whether the recorded opponent MMR is trustworthy, since (per OpponentMmrEstimator's own fit
        // against the user's real game history) every legitimate game lands within
        // MaxPlausibleResidual of its own prediction; a replay-parsed opponent MMR can be garbage (see
        // ReplayDataExtractor's ScaledRating guard for a confirmed example) while still looking
        // superficially plausible on its own, which this catches where a simpler bounds check wouldn't.
        private void TryCorrectOpponentMmr(ParsedReplayData replay, long playerMmrChange, Sc2Profile profile)
        {
            GamePlayer opponent = replay.Opponents[0];
            if (opponent.GamePlayerId == null)
                return;

            double predictedChange = OpponentMmrEstimator.PredictedChange(replay.Player.Mmr, opponent.Mmr, replay.Win);
            if (Math.Abs(predictedChange - playerMmrChange) <= OpponentMmrEstimator.MaxPlausibleResidual)
                return;

            long? estimatedMmr = OpponentMmrEstimator.Estimate(replay.Player.Mmr, playerMmrChange, replay.Win);
            if (estimatedMmr == null)
                return;

            logger.LogInfo($"Opponent MMR {opponent.Mmr} predicted a MmrChange of {predictedChange:0.#}, but the player's actual MmrChange was {playerMmrChange:+#;-#;0}; correcting to Elo-estimated {estimatedMmr.Value}.", profile);
            gameDataRepository.UpdateGamePlayerMmr(opponent.GamePlayerId.Value, estimatedMmr.Value);
            opponent.Mmr = estimatedMmr.Value;
        }
    }
}
