using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Audio
{
    [CreateAssetMenu(
        fileName = "AudioEventCatalog",
        menuName = "Space World/Audio/Audio Event Catalog")]
    public sealed class AudioEventCatalog : ScriptableObject
    {
        [SerializeField] private List<AudioEventDefinition> events =
            new List<AudioEventDefinition>();

        private Dictionary<string, AudioEventDefinition> lookup;

        public IReadOnlyList<AudioEventDefinition> Events => events;

        public bool TryGet(string eventId, out AudioEventDefinition definition)
        {
            definition = null;
            if (string.IsNullOrWhiteSpace(eventId))
                return false;

            EnsureLookup();
            return lookup.TryGetValue(eventId.Trim(), out definition) &&
                   definition != null;
        }

        private void OnEnable()
        {
            lookup = null;
        }

        private void OnValidate()
        {
            lookup = null;
        }

        private void EnsureLookup()
        {
            if (lookup != null)
                return;

            lookup = new Dictionary<string, AudioEventDefinition>(StringComparer.Ordinal);
            if (events == null)
                return;

            for (int i = 0; i < events.Count; i++)
            {
                AudioEventDefinition definition = events[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.EventId))
                    continue;

                string key = definition.EventId.Trim();
                if (!lookup.ContainsKey(key))
                    lookup.Add(key, definition);
            }
        }
    }
}

