-- EstimatedMmr: set instead of overwriting Mmr when OpponentMmrEstimator judges the replay-parsed value
-- implausible (see ReplayImportService.TryCorrectOpponentMmr) — keeps the original parsed value intact
-- for anyone re-deriving/re-checking it later, rather than clobbering it in place.
-- OverrideMmr: not populated by anything yet — a future manual-correction path.
ALTER TABLE GamePlayers ADD COLUMN EstimatedMmr INTEGER;
ALTER TABLE GamePlayers ADD COLUMN OverrideMmr INTEGER;
