using System;
using UnityEngine;

namespace AcRoguelike.Liminal
{
    /// <summary>
    /// The party set up with the association's loadout officer in the lobby (로비 편성 담당 요원): the playable hunter
    /// and the support character. Stored in PlayerPrefs, so the next session starts with the same pick.
    /// HunterRoster reads the hunter, the support characters read the support.
    /// </summary>
    public static class HunterLoadout
    {
        const string HunterKey = "Hunter.Loadout.Hunter";
        const string SupportKey = "Hunter.Loadout.Support";

        /// <summary>Raised after either pick changed.</summary>
        public static event Action Changed;

        /// <summary>characterId of the picked hunter; empty means the scene's own first hunter.</summary>
        public static string HunterId
        {
            get => PlayerPrefs.GetString(HunterKey, "");
            set
            {
                value ??= "";
                if (value == HunterId) return;
                PlayerPrefs.SetString(HunterKey, value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary>Id of the picked support character (SupportCharacterCatalog); unknown ids fall back to the default.</summary>
        public static string SupportId
        {
            get
            {
                string id = PlayerPrefs.GetString(SupportKey, "");
                return SupportCharacterCatalog.Find(id) != null ? id : SupportCharacterCatalog.DefaultId;
            }
            set
            {
                if (SupportCharacterCatalog.Find(value) == null || value == SupportId) return;
                PlayerPrefs.SetString(SupportKey, value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }
    }
}
