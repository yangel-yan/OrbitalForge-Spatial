using System;
using UnityEngine;

namespace SpaceBall
{
    /// <summary>Local progress only; no account, network or device identifiers.</summary>
    [Serializable]
    public sealed class OrbitalProgress
    {
        public const string StorageKey = "SpaceBall.Journey.v1";
        public const int CurrentConstructionRevision = 1;
        public int version = 1;
        public int constructionRevision = CurrentConstructionRevision;
        public int lastLevel;
        public bool sound = true, reducedMotion, trace = true;
        public Record tutorial = new Record(), moon = new Record();
        // Keep the original two fields readable by saves from the two-mission build.
        public Record[] expedition = { new Record(), new Record(), new Record(), new Record() };

        [Serializable]
        public sealed class Record
        {
            public bool completed, independent, efficient;
            public int bestAttempts;
            public int[] tileSockets, tileTurns;
            public int BadgeCount => (completed ? 1 : 0) + (independent ? 1 : 0) + (efficient ? 1 : 0);

            public void Complete(int attempts, bool usedHint)
            {
                if (attempts < 1) return;
                completed = true;
                independent |= !usedHint;
                efficient |= attempts <= 2;
                if (bestAttempts == 0 || attempts < bestAttempts) bestAttempts = attempts;
            }
        }

        public Record For(OrbitalLevel level)
        {
            if (!OrbitalCampaign.IsValid(level)) throw new ArgumentOutOfRangeException(nameof(level));
            if (level == OrbitalLevel.Tutorial) return tutorial;
            if (level == OrbitalLevel.Moon) return moon;
            EnsureExpedition();
            return expedition[(int)level - 2];
        }

        public bool IsUnlocked(OrbitalLevel level)
        {
            if (!OrbitalCampaign.IsValid(level)) return false;
            for (int i = 0; i < (int)level; i++) if (!For((OrbitalLevel)i).completed) return false;
            return true;
        }

        public int CompletedCount
        {
            get { int count = 0; for (int i = 0; i < OrbitalCampaign.Count; i++) if (For((OrbitalLevel)i).completed) count++; return count; }
        }
        public int TotalBadges
        {
            get { int count = 0; for (int i = 0; i < OrbitalCampaign.Count; i++) count += For((OrbitalLevel)i).BadgeCount; return count; }
        }
        public bool AllCompleted => CompletedCount == OrbitalCampaign.Count;
        public int ResumeLevel => IsUnlocked((OrbitalLevel)lastLevel) ? lastLevel : 0;

        void EnsureExpedition()
        {
            if (expedition == null || expedition.Length != OrbitalCampaign.Count - 2)
            {
                var old = expedition;
                expedition = new Record[OrbitalCampaign.Count - 2];
                if (old != null) Array.Copy(old, expedition, Math.Min(old.Length, expedition.Length));
            }
            for (int i = 0; i < expedition.Length; i++) if (expedition[i] == null) expedition[i] = new Record();
        }

        public static OrbitalProgress Load()
        {
            var result = Parse(PlayerPrefs.GetString(StorageKey, "")) ??
                         Parse(PlayerPrefs.GetString(StorageKey + ".backup", ""));
            return result ?? new OrbitalProgress();
        }

        static OrbitalProgress Parse(string json)
        {
            if (string.IsNullOrEmpty(json) || !json.Contains("\"version\"")) return null;
            try
            {
                var result = JsonUtility.FromJson<OrbitalProgress>(json);
                if (result == null || result.version != 1 || result.tutorial == null || result.moon == null) return null;
                result.lastLevel = Mathf.Clamp(result.lastLevel, 0, OrbitalCampaign.Count - 1);
                Sanitize(result.tutorial);
                Sanitize(result.moon);
                result.EnsureExpedition();
                foreach (var record in result.expedition) Sanitize(record);
                // Missions 3-6 now use actual track construction. Old switch-only
                // completions are archived in the existing backup, not credited
                // as solutions of these replacement puzzles. Keep the two
                // unchanged missions and every preference.
                if (!json.Contains("\"constructionRevision\"") ||
                    result.constructionRevision != CurrentConstructionRevision)
                {
                    for (int i = 0; i < result.expedition.Length; i++) result.expedition[i] = new Record();
                    result.constructionRevision = CurrentConstructionRevision;
                    if (result.lastLevel > 2) result.lastLevel = result.moon.completed ? 2 : result.tutorial.completed ? 1 : 0;
                }
                for (int i = 0; i < result.expedition.Length; i++)
                    SanitizeBlueprint(result.expedition[i], (OrbitalLevel)(i + 2));
                return result;
            }
            catch (ArgumentException) { return null; }
        }

        static void Sanitize(Record record)
        {
            record.bestAttempts = Mathf.Clamp(record.bestAttempts, 0, 99999);
            if (!record.completed) { record.independent = record.efficient = false; record.bestAttempts = 0; }
        }

        static void SanitizeBlueprint(Record record, OrbitalLevel level)
        {
            var layout = OrbitalConstructionLayout.For(level);
            if (record.tileSockets == null || record.tileTurns == null ||
                record.tileSockets.Length != layout.TileShapes.Length || record.tileTurns.Length != layout.TileShapes.Length)
            {
                record.tileSockets = record.tileTurns = null;
                return;
            }
            var occupied = new bool[layout.SocketPositions.Length];
            for (int i = 0; i < record.tileSockets.Length; i++)
            {
                int socket = record.tileSockets[i];
                record.tileTurns[i] = ((record.tileTurns[i] % 4) + 4) % 4;
                if (socket < 0 || socket >= occupied.Length || occupied[socket]) record.tileSockets[i] = -1;
                else occupied[socket] = true;
            }
        }

        public void Save()
        {
            string previous = PlayerPrefs.GetString(StorageKey, "");
            if (Parse(previous) != null) PlayerPrefs.SetString(StorageKey + ".backup", previous);
            PlayerPrefs.SetString(StorageKey, JsonUtility.ToJson(this));
            PlayerPrefs.Save();
        }
    }
}
