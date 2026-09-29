using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace ModSource
{
    public class ModSourceEntry
    {
        [JsonProperty("modName")]
        public string ModName { get; set; }

        [JsonProperty("modAuthor")]
        public string ModAuthor { get; set; }

        [JsonProperty("modGuid")]
        public string ModGuid { get; set; }

        [JsonProperty("modVersion")]
        public string ModVersion { get; set; }

        [JsonProperty("bundleModName")]
        public string BundleModName { get; set; }

        [JsonProperty("confidence")]
        public string Confidence { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }
    }

    public class ModSourcePayload
    {
        [JsonProperty("items")]
        public Dictionary<string, ModSourceEntry> Items { get; set; }

        [JsonProperty("quests")]
        public Dictionary<string, ModSourceEntry> Quests { get; set; }

        [JsonProperty("achievements")]
        public Dictionary<string, ModSourceEntry> Achievements { get; set; }

        [JsonProperty("customization")]
        public Dictionary<string, ModSourceEntry> Customization { get; set; }

        [JsonProperty("assorts")]
        public Dictionary<string, ModSourceEntry> Assorts { get; set; }

        [JsonProperty("ready")]
        public bool Ready { get; set; }
    }

    public static class ModSourceClientDatabase
    {
        private static Dictionary<string, ModSourceEntry> _items = new Dictionary<string, ModSourceEntry>();
        private static Dictionary<string, ModSourceEntry> _quests = new Dictionary<string, ModSourceEntry>();
        private static Dictionary<string, ModSourceEntry> _achievements = new Dictionary<string, ModSourceEntry>();
        private static Dictionary<string, ModSourceEntry> _customization = new Dictionary<string, ModSourceEntry>();
        private static Dictionary<string, ModSourceEntry> _assorts = new Dictionary<string, ModSourceEntry>();
        private static bool _loadStarted;

        public static bool Loaded { get; private set; }

        public static int Count
        {
            get { return _items.Count; }
        }

        public static void BeginLoad()
        {
            if (_loadStarted)
            {
                return;
            }

            _loadStarted = true;
            Task.Run((Action)Load);
        }

        private static void Load()
        {
            try
            {
                var json = RequestHandler.GetJson("/modsource/db");
                var payload = JsonConvert.DeserializeObject<ModSourcePayload>(json);

                if (payload == null || !payload.Ready || payload.Items == null)
                {
                    ModSourcePlugin.Log.LogWarning("[ModSource] Server reported no provenance database; attribution disabled.");
                    return;
                }

                _items = payload.Items;

                _quests = payload.Quests ?? new Dictionary<string, ModSourceEntry>();
                _achievements = payload.Achievements ?? new Dictionary<string, ModSourceEntry>();
                _customization = payload.Customization ?? new Dictionary<string, ModSourceEntry>();
                _assorts = payload.Assorts ?? new Dictionary<string, ModSourceEntry>();

                Loaded = true;
            }
            catch (Exception ex)
            {
                ModSourcePlugin.Log.LogWarning("[ModSource] Could not load origin database: " + ex.Message);
            }
        }

        public static ModSourceEntry Lookup(string templateId)
        {
            if (!Loaded || string.IsNullOrEmpty(templateId))
            {
                return null;
            }

            ModSourceEntry entry;
            return _items.TryGetValue(templateId, out entry) ? entry : null;
        }

        public static ModSourceEntry LookupQuest(string questId)
        {
            return Find(_quests, questId);
        }

        public static ModSourceEntry LookupAchievement(string achievementId)
        {
            return Find(_achievements, achievementId);
        }

        public static ModSourceEntry LookupCustomization(string customizationId)
        {
            return Find(_customization, customizationId);
        }

        public static ModSourceEntry LookupAssort(string offerId)
        {
            return Find(_assorts, offerId);
        }

        private static ModSourceEntry Find(Dictionary<string, ModSourceEntry> table, string id)
        {
            if (!Loaded || string.IsNullOrEmpty(id))
            {
                return null;
            }

            ModSourceEntry entry;
            return table.TryGetValue(id, out entry) ? entry : null;
        }
    }
}
