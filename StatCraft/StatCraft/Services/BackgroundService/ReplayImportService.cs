using s2protocol.NET;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;
using StatCraft.Services.DatabaseRepository;
using StatCraft.Services.DataParsing;
using System;
using System.IO;
using System.Threading.Tasks;

namespace StatCraft.Services.BackgroundService
{
    // Solely responsible for turning one replay file into a persisted GameData for a given profile —
    // decoding, extracting, parsing, and inserting. Doesn't know or care how the file was found (folder
    // watching vs. a manual import both funnel through here).
    public class ReplayImportService(ILogger logger, ReplayDataExtractor replayDataExtractor, GameDataRepository gameDataRepository)
    {
        // Raised after a replay is parsed and persisted, so the UI can add a row for it.
        // Internal because GameData is internal; the class itself stays public.
        internal event Action<GameData>? GameParsed;

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
            return null;
        }
    }
}
