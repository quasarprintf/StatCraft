using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using s2protocol.NET;
using s2protocol.NET.Models;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;

namespace StatCraft.Services.DataParsing
{
    public class ReplayDataExtractor
    {
        internal RawReplayData Extract(Sc2Replay replay, DateTimeOffset replayTimestamp)
        {
            List<DetailsPlayer> detailsPlayers = replay.Details?.Players?.ToList() ?? new List<DetailsPlayer>();
            List<MetadataPlayer> metadataPlayers = replay.Metadata?.Players?.ToList() ?? new List<MetadataPlayer>();

            List<string> names = new();
            List<string?> clans = new();
            List<char> races = new();
            List<bool> randomRace = new();
            List<int> colorsArgb = new();
            List<int> teamIds = new();
            List<int> winningIndices = new();
            List<int> profileIds = new();
            bool isDraw = false;

            for (int i = 0; i < detailsPlayers.Count; i++)
            {
                DetailsPlayer detailsPlayer = detailsPlayers[i];
                MetadataPlayer? metadataPlayer = i < metadataPlayers.Count ? metadataPlayers[i] : null;

                names.Add(detailsPlayer.Name);
                clans.Add(string.IsNullOrEmpty(detailsPlayer.ClanName) ? null : detailsPlayer.ClanName);

                string race = metadataPlayer?.AssignedRace ?? detailsPlayer.Race;
                races.Add(string.IsNullOrEmpty(race) ? '?' : char.ToUpperInvariant(race[0]));

                randomRace.Add(string.Equals(metadataPlayer?.SelectedRace, "random", StringComparison.OrdinalIgnoreCase));

                PlayerColor color = detailsPlayer.Color;
                colorsArgb.Add((color.A << 24) | (color.R << 16) | (color.G << 8) | color.B);

                teamIds.Add(detailsPlayer.TeamId);
                profileIds.Add(detailsPlayer.Toon.Id);

                if (string.Equals(metadataPlayer?.Result, "Win", StringComparison.OrdinalIgnoreCase))
                    winningIndices.Add(i);
                else if (string.Equals(metadataPlayer?.Result, "Tie", StringComparison.OrdinalIgnoreCase))
                    isDraw = true;
            }

            return new RawReplayData
            {
                MapName = replay.Details?.Title ?? "",
                PlayerNames = names,
                PlayerClans = clans,
                PlayerRaces = races,
                PlayerRandomRace = randomRace,
                PlayerColorsArgb = colorsArgb,
                // Gating this on HighestLeague == 0 (unranked) was tried and reverted: Unranked matchmaking
                // uses its own real, internal MMR — just never shown as a league — so a player who's only
                // ever played Unranked legitimately has HighestLeague == 0 *and* a genuine ScaledRating.
                // Discarding it there would silently lose real data instead of just displaying a wrong
                // number. The one thing actually verified wrong (observed on an unranked "barcode" player)
                // is the rating itself being negative — real MMR, ranked or unranked, is never negative —
                // so that's what's checked. Treated the same as an absent rating (null) rather than stored
                // verbatim, collapsing to the same "no data" 0 that BuildPlayer already falls back to below.
                PlayerMmrs = replay.Initdata!.UserInitialData.Select(d => d.ScaledRating is > 0 ? d.ScaledRating : null).ToArray(),
                PlayerProfileIds = profileIds,
                PlayerTeams = teamIds,
                IsMatchmade = replay.Initdata?.GameDescription?.GameOptions?.Amm ?? false,
                IsDraw = isDraw,
                WinningPlayerIndices = winningIndices,
                GameLengthSeconds = replay.Metadata?.Duration ?? 0, //TODO: this is using hots time. Need to get the exact conversion ratio to lotv time
                ReplayPath = replay.FileName,
                ReplayTimestamp = replayTimestamp,
            };
        }

        // Backfills GamePlayer.ColorArgb for rows recorded before it was captured, by re-reading the
        // replay file rather than re-importing the game — the file itself is the only remaining source
        // for data that was never persisted the first time around. Matched by player name, the only
        // stable identifier already stored on a GamePlayer; returns null (never throws) for anything
        // that stops the file being read (moved, deleted, corrupt), since this always runs as a
        // best-effort UI backfill — logging a failure is the caller's call, not this method's.
        internal async Task<int?> TryResolvePlayerColorAsync(string replayPath, string playerName)
        {
            try
            {
                using ReplayDecoder decoder = new();
                Sc2Replay? replay = await decoder.DecodeAsync(replayPath);
                if (replay == null)
                    return null;

                RawReplayData raw = Extract(replay, DateTimeOffset.MinValue);
                int index = raw.PlayerNames.ToList().FindIndex(n => n == playerName);
                return index < 0 ? null : raw.PlayerColorsArgb.ElementAt(index);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Reframes the raw, index-parallel replay data around a specific player: who they are, whether
        // they won, and which other players were on their side vs. the other side.
        internal ParsedReplayData Parse(RawReplayData rawReplayData, Sc2Profile profile)
        {
            List<string> names = new(rawReplayData.PlayerNames);
            List<string?> clans = new(rawReplayData.PlayerClans);
            List<char> races = new(rawReplayData.PlayerRaces);
            List<bool> randomRace = new(rawReplayData.PlayerRandomRace);
            List<int> colorsArgb = new(rawReplayData.PlayerColorsArgb);
            List<long?> mmrs = new(rawReplayData.PlayerMmrs);
            List<int> teams = new(rawReplayData.PlayerTeams);
            HashSet<int> winners = new(rawReplayData.WinningPlayerIndices);

            int playerIndex = rawReplayData.PlayerProfileIds.ToList().FindIndex(i => i == profile.ProfileId);
            if (playerIndex < 0)
                throw new InvalidOperationException($"Could not find a player named '{profile.Name}' in the replay.");

            GamePlayer BuildPlayer(int i) => new()
            {
                Name = names[i],
                Clan = clans[i] ?? "",
                Mmr = new PlayerMmr { ParsedMmr = mmrs[i] ?? 0 },
                Race = races[i],
                Random = randomRace[i],
                ColorArgb = colorsArgb[i],
            };

            decimal win = rawReplayData.IsDraw ? 0.5m : winners.Contains(playerIndex) ? 1m : 0m;

            List<GamePlayer> allies = new();
            List<GamePlayer> opponents = new();
            for (int i = 0; i < names.Count; i++)
            {
                if (i == playerIndex)
                    continue;

                bool isAlly = teams[i] == teams[playerIndex];
                if (isAlly)
                    allies.Add(BuildPlayer(i));
                else
                    opponents.Add(BuildPlayer(i));
            }

            return new ParsedReplayData
            {
                GameLengthSeconds = rawReplayData.GameLengthSeconds,
                ReplayPath = rawReplayData.ReplayPath,
                ReplayTimestamp = rawReplayData.ReplayTimestamp,
                Win = win,
                Player = BuildPlayer(playerIndex),
                Allies = allies.ToArray(),
                Opponents = opponents.ToArray(),
                IsMatchmade = rawReplayData.IsMatchmade,
            };
        }
    }
}
