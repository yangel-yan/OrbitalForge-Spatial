using System;
using System.Reflection;
using UnityEngine;

namespace SpaceBall.Editor
{
    /// <summary>Isolates Play-mode verification from the player's local journey.</summary>
    internal sealed class OrbitalVerificationScope : IDisposable
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        readonly OrbitalPrototype game;
        readonly object originalProfile;
        readonly OrbitalLevel originalLevel;
        readonly bool originalPreferences, originalPause;
        readonly SavedString main, backup;

        readonly struct SavedString
        {
            readonly string key, value;
            readonly bool existed;
            public SavedString(string key)
            {
                this.key = key;
                existed = PlayerPrefs.HasKey(key);
                value = PlayerPrefs.GetString(key, "");
            }
            public void Restore()
            {
                if (existed) PlayerPrefs.SetString(key, value);
                else PlayerPrefs.DeleteKey(key);
            }
        }

        public OrbitalVerificationScope(OrbitalPrototype game)
        {
            this.game = game;
            originalLevel = game.State.Level;
            originalProfile = Field("progress").GetValue(game);
            originalPreferences = (bool)Field("preferencesOpen").GetValue(game);
            originalPause = (bool)Field("applicationSuspended").GetValue(game);
            main = new SavedString(OrbitalProgress.StorageKey);
            backup = new SavedString(OrbitalProgress.StorageKey + ".backup");
            Field("progress").SetValue(game, new OrbitalProgress());
            Field("preferencesOpen").SetValue(game, false);
            Call(game, "OnApplicationPause", false);
            game.ResetAttempt();
        }

        public void Dispose()
        {
            try
            {
                if (game == null) return;
                // Keep all cleanup on the temporary profile. StartChallenge saves.
                Call(game, "OnApplicationPause", false);
                for (int index = 0; index < OrbitalCampaign.Count; index++)
                {
                    game.ResetAttempt();
                    game.SelectLevel((OrbitalLevel)index);
                    game.Assembly?.Clear();
                }
                game.SelectLevel(originalLevel);
                game.ResetAttempt();
            }
            finally
            {
                if (game != null)
                {
                    Field("progress").SetValue(game, originalProfile);
                    Field("preferencesOpen").SetValue(game, originalPreferences);
                    Field("applicationSuspended").SetValue(game, originalPause);
                }
                main.Restore();
                backup.Restore();
                PlayerPrefs.Save();
                if (game != null) Call(game, "RefreshExperience");
            }
        }

        internal static FieldInfo Field(string name) => typeof(OrbitalPrototype).GetField(name, Private) ??
            throw new MissingFieldException(typeof(OrbitalPrototype).FullName, name);

        internal static void Call(OrbitalPrototype game, string name, params object[] arguments)
        {
            var method = typeof(OrbitalPrototype).GetMethod(name, Private) ??
                throw new MissingMethodException(typeof(OrbitalPrototype).FullName, name);
            try { method.Invoke(game, arguments); }
            catch (TargetInvocationException exception) { throw exception.InnerException ?? exception; }
        }
    }
}
