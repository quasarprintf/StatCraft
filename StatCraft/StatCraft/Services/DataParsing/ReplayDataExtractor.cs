using System;
using System.Collections.Generic;
using System.Linq;
using s2protocol.NET;
using s2protocol.NET.Models;
using StatCraft.Models.Battlenet;
using StatCraft.Models.GameData;

namespace StatCraft.Services.DataParsing
{
    public class ReplayDataExtractor
    {
        internal RawReplayData Extract(Sc2Replay replay)
        {
            List<DetailsPlayer> detailsPlayers = replay.Details?.Players?.ToList() ?? new List<DetailsPlayer>();
            List<MetadataPlayer> metadataPlayers = replay.Metadata?.Players?.ToList() ?? new List<MetadataPlayer>();

            List<string> names = new();
            List<string?> clans = new();
            List<char> races = new();
            List<bool> randomRace = new();
            List<int> teamIds = new();
            List<int> winningIndices = new();
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

                teamIds.Add(detailsPlayer.TeamId);

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
                PlayerMmrs = replay.Initdata!.UserInitialData.Select(d => d.ScaledRating).ToArray(),
                PlayerTeams = teamIds,
                IsDraw = isDraw,
                WinningPlayerIndices = winningIndices,
                GameLengthSeconds = replay.Metadata?.Duration ?? 0, //TODO: this is using hots time. Need to get the exact conversion ratio to lotv time
                ReplayPath = replay.FileName,
            };
        }

        // Reframes the raw, index-parallel replay data around a specific player: who they are, whether
        // they won, and which other players were on their side vs. the other side.
        internal ParsedReplayData Parse(RawReplayData rawReplayData, Sc2Profile profile)
        {
            List<string> names = new(rawReplayData.PlayerNames);
            List<string?> clans = new(rawReplayData.PlayerClans);
            List<char> races = new(rawReplayData.PlayerRaces);
            List<bool> randomRace = new(rawReplayData.PlayerRandomRace);
            List<long?> mmrs = new(rawReplayData.PlayerMmrs);
            List<int> teams = new(rawReplayData.PlayerTeams);
            HashSet<int> winners = new(rawReplayData.WinningPlayerIndices);

            int playerIndex = names.FindIndex(name => string.Equals(name, profile.Name, StringComparison.OrdinalIgnoreCase));
            if (playerIndex < 0)
                throw new InvalidOperationException($"Could not find a player named '{profile.Name}' in the replay.");

            GamePlayer BuildPlayer(int i) => new()
            {
                Name = names[i],
                Clan = clans[i] ?? "",
                Mmr = mmrs[i].HasValue ? mmrs[i]!.Value : 0,
                Race = races[i],
                Random = randomRace[i],
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
                MapName = rawReplayData.MapName,
                GameLengthSeconds = rawReplayData.GameLengthSeconds,
                ReplayPath = rawReplayData.ReplayPath,
                Win = win,
                Player = BuildPlayer(playerIndex),
                Allies = allies.ToArray(),
                Opponents = opponents.ToArray(),
            };
        }
    }
}
