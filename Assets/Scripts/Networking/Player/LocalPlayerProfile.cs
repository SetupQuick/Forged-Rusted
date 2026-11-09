using System;
using UnityEngine;

namespace Networking
{
    /// <summary>
    /// Who this player is across sessions: a random id created once per
    /// PC (saved in PlayerPrefs) and a display name. Sent to the server
    /// when connecting so a saved world gives you your inventory back.
    ///
    /// Note: the id is not a password - anyone who copies it could claim
    /// the inventory. Fine for playing with friends; accounts come later.
    /// </summary>
    public static class LocalPlayerProfile
    {
        private const string IdKey = "FR.PlayerId";
        private const string NameKey = "FR.PlayerName";

        public static string Id
        {
            get
            {
                string id = PlayerPrefs.GetString(IdKey, string.Empty);

                if (string.IsNullOrEmpty(id))
                {
                    id = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString(IdKey, id);
                    PlayerPrefs.Save();
                }

                return id;
            }
        }

        public static string Name
        {
            get
            {
                string name = PlayerPrefs.GetString(NameKey, string.Empty);
                return string.IsNullOrWhiteSpace(name) ? "Player " + Id.Substring(0, 4).ToUpperInvariant() : name;
            }
            set
            {
                value = (value ?? string.Empty).Trim();
                if (value.Length > 24)
                    value = value.Substring(0, 24);

                PlayerPrefs.SetString(NameKey, value);
                PlayerPrefs.Save();
            }
        }
    }
}
