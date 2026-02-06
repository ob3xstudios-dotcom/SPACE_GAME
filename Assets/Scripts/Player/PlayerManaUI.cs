using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    public class PlayerManaUI : MonoBehaviour
    {
        [Header("Refs")]
        [SerializeField] private Game.Player.PlayerMana mana;

        [Header("UI")]
        [SerializeField] private Image pipPrefab;
        [SerializeField] private Transform pipContainer;
        [SerializeField] private Sprite fullSprite;
        [SerializeField] private Sprite emptySprite;

        [Header("Tuning")]
        [SerializeField, Range(1, 50)] private int maxPipsFallback = 5;

        private readonly List<Image> pips = new();

        private void Awake()
        {
            if (mana == null)
                mana = FindFirstObjectByType<Game.Player.PlayerMana>();

            if (pipContainer == null)
                pipContainer = transform;
        }

        private void Start()
        {
            Rebuild();
            Refresh();
        }

        private void Update()
        {
            // Simple y robusto: refresca cada frame (luego si quieres lo optimizamos con eventos)
            Refresh();
        }

        public void Rebuild()
        {
            // Limpia
            for (int i = 0; i < pips.Count; i++)
                if (pips[i] != null) Destroy(pips[i].gameObject);
            pips.Clear();

            int max = mana != null ? mana.MaxMana : maxPipsFallback;
            max = Mathf.Max(0, max);

            for (int i = 0; i < max; i++)
            {
                var img = Instantiate(pipPrefab, pipContainer);
                img.sprite = emptySprite;
                pips.Add(img);
            }
        }

        public void Refresh()
        {
            if (pips.Count == 0) return;

            int current = mana != null ? mana.CurrentMana : 0;
            int max = mana != null ? mana.MaxMana : pips.Count;

            // Si cambió maxMana en runtime, reconstruimos
            if (pips.Count != max)
            {
                Rebuild();
                current = mana != null ? mana.CurrentMana : 0;
            }

            for (int i = 0; i < pips.Count; i++)
            {
                if (pips[i] == null) continue;
                pips[i].sprite = (i < current) ? fullSprite : emptySprite;
            }
        }
    }
}
