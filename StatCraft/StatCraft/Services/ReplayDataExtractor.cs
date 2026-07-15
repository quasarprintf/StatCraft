using System;
using System.Collections.Generic;
using System.Linq;
using s2protocol.NET;
using s2protocol.NET.Models;
using StatCraft.Models;

namespace StatCraft.Services
{
    public class ReplayDataExtractor
    {
        internal ReplayData Extract(Sc2Replay replay)
        {
            List<DetailsPlayer> detailsPlayers = replay.Details?.Players?.ToList() ?? new List<DetailsPlayer>();
            List<MetadataPlayer> metadataPlayers = replay.Metadata?.Players?.ToList() ?? new List<MetadataPlayer>();

            List<string> names = new();
            List<string?> clans = new();
            List<char> races = new();
            List<bool> randomRace = new();
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

                if (string.Equals(metadataPlayer?.Result, "Win", StringComparison.OrdinalIgnoreCase))
                    winningIndices.Add(i);
                else if (string.Equals(metadataPlayer?.Result, "Tie", StringComparison.OrdinalIgnoreCase))
                    isDraw = true;
            }

            return new ReplayData
            {
                MapName = replay.Details?.Title ?? "",
                PlayerNames = names,
                PlayerClans = clans,
                PlayerRaces = races,
                PlayerRandomRace = randomRace,
                PlayerMmrs = null,
                IsDraw = isDraw,
                WinningPlayerIndices = winningIndices,
                GameLengthSeconds = replay.Metadata?.Duration ?? 0, //TODO: this is using hots time. Need to get the exact conversion ratio to lotv time
                ReplayPath = replay.FileName,
            };
        }
    }
}
