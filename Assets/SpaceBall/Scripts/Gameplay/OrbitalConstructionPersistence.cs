using UnityEngine;

namespace SpaceBall
{
    public sealed partial class OrbitalPrototype
    {
        bool restoringConstruction;

        /// <summary>Stores the player's current assembly, independently of earned results.</summary>
        void SaveConstructionBlueprint()
        {
            var kit = Construction;
            if (restoringConstruction || IsReviewSession || kit == null || kit.HasHeld ||
                kit.tiles == null || kit.tiles.Length == 0) return;

            var record = Profile.For(State.Level);
            int count = kit.tiles.Length;
            bool unchanged = record.tileSockets != null && record.tileTurns != null &&
                record.tileSockets.Length == count && record.tileTurns.Length == count;
            bool untouchedTray = true;
            for (int i = 0; i < count; i++)
            {
                var tile = kit.tiles[i];
                int socket = tile != null ? tile.SocketIndex : -1;
                int turns = tile != null ? tile.QuarterTurns : 0;
                if (socket != -1 || turns != 0) untouchedTray = false;
                if (unchanged && (record.tileSockets[i] != socket || record.tileTurns[i] != turns))
                    unchanged = false;
            }
            if (unchanged || untouchedTray && record.tileSockets == null && record.tileTurns == null) return;

            var sockets = new int[count];
            var orientations = new int[count];
            for (int i = 0; i < count; i++)
            {
                var tile = kit.tiles[i];
                sockets[i] = tile != null ? tile.SocketIndex : -1;
                orientations[i] = tile != null ? tile.QuarterTurns : 0;
            }
            record.tileSockets = sockets;
            record.tileTurns = orientations;
            SaveExperience();
        }

        /// <summary>Restores only saved player placements, never an authored reference solution.</summary>
        void RestoreConstructionBlueprint()
        {
            var kit = Construction;
            if (restoringConstruction || kit == null || !kit.Editable) return;

            var record = Profile.For(State.Level);
            // Clear and Place notify the owner. Keep a private snapshot even if
            // future presentation callbacks inspect or replace the record arrays.
            var savedSockets = record.tileSockets != null ? (int[])record.tileSockets.Clone() : null;
            var savedTurns = record.tileTurns != null ? (int[])record.tileTurns.Clone() : null;
            restoringConstruction = true;
            try
            {
                kit.Clear();
                if (savedSockets == null || savedTurns == null || kit.tiles == null || kit.sockets == null) return;
                int count = System.Math.Min(kit.tiles.Length, System.Math.Min(savedSockets.Length, savedTurns.Length));
                for (int i = 0; i < count; i++)
                {
                    var tile = kit.tiles[i];
                    int socket = savedSockets[i], turns = savedTurns[i];
                    if (tile == null || turns < 0 || turns > 3 || socket < -1 || socket >= kit.sockets.Length) continue;
                    if (socket == -1) tile.SetPlacement(-1, turns);
                    else if (kit.sockets[socket] != null) kit.Place(i, socket, turns);
                }
            }
            finally
            {
                restoringConstruction = false;
                kit.selected = -1;
                kit.SettlePresentation();
                kit.RefreshConnections();
                kit.UpdateHighlights();
                Physics.SyncTransforms();
            }
        }
    }
}
