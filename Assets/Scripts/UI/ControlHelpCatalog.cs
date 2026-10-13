using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.UI.Menus
{
    [CreateAssetMenu(
        fileName = "SpaceWorldControlHelpCatalog",
        menuName = "Space World/UI/Control Help Catalog")]
    public sealed class ControlHelpCatalog : ScriptableObject
    {
        public enum ControlCategory
        {
            Movement,
            Combat,
            Traversal,
            Interaction,
            System
        }

        [Serializable]
        public sealed class Entry
        {
            public string actionName;
            public string displayName;
            [TextArea(2, 5)] public string description;
            public string inputType;
            public string condition;
            public string context;
            [TextArea(1, 4)] public string notes;
            public ControlCategory category;
        }

        [SerializeField] private List<Entry> entries = new List<Entry>();

        public IReadOnlyList<Entry> Entries => entries;

        public Entry Find(string actionName)
        {
            return entries.Find(entry => string.Equals(
                entry.actionName,
                actionName,
                StringComparison.Ordinal));
        }

        public void PopulateDefaults()
        {
            entries = new List<Entry>
            {
                Create("Move", "MOVIMIENTO",
                    "Desplaza al personaje y aporta la dirección usada por varias acciones contextuales.",
                    "Mantener / dirección analógica.",
                    "Disponible durante el control normal del personaje.",
                    "Exploración y combate.",
                    "El stick izquierdo permite controlar también la intensidad de marcha sigilosa.",
                    ControlCategory.Movement),
                Create("Jump", "SALTO",
                    "Salta, ejecuta Wall Jump cuando corresponde y permite abandonar ciertos elementos de traversal.",
                    "Pulsación.",
                    "Respeta suelo, coyote time, Wall Slide y las habilidades equipadas.",
                    "Exploración y traversal.",
                    "Una habilidad equipada puede habilitar un segundo salto.",
                    ControlCategory.Movement),
                Create("Dash", "DASH",
                    "Ejecuta el impulso rápido en la dirección válida actual.",
                    "Pulsación.",
                    "Requiere que Dash esté disponible y cumplir sus reglas de stamina y recarga.",
                    "Exploración y combate.",
                    "No altera sus costes ni tiempos desde este menú.",
                    ControlCategory.Movement),
                Create("Sprint", "SPRINT",
                    "Mantiene la carrera rápida mientras haya reserva de sprint.",
                    "Mantener.",
                    "Requiere movimiento y reserva de sprint disponible.",
                    "Exploración.",
                    "Durante Sprint habilita Slide y Dolphin Dive.",
                    ControlCategory.Movement),
                Create("Attack", "ATAQUE",
                    "Realiza el ataque correspondiente a la dirección y al estado del personaje.",
                    "Pulsación.",
                    "Sujeto a las reglas actuales de ataque y cancelación.",
                    "Combate.",
                    "En el aire, la dirección descendente inicia el ataque hacia abajo sostenido.",
                    ControlCategory.Combat),
                Create("Parry", "PARRY",
                    "Intenta desviar un ataque enemigo dentro de la ventana válida.",
                    "Pulsación.",
                    "Solo tiene efecto cuando el estado y la ventana de parry lo permiten.",
                    "Combate.",
                    "Respeta los bloqueos y cooldowns existentes.",
                    ControlCategory.Combat),
                Create("Interact", "INTERACTUAR",
                    "Activa la interacción disponible más cercana, incluidos diálogos, bancos y objetos compatibles.",
                    "Pulsación.",
                    "Debe existir una interacción válida al alcance.",
                    "Exploración.",
                    "En un banco también puede utilizarse para abandonar el asiento.",
                    ControlCategory.Interaction),
                Create("Crouch", "AGACHARSE / SLIDE / LAYDOWN",
                    "Cambia de postura. Una pulsación durante Sprint inicia Slide; mantener permite pasar a LayDown.",
                    "Pulsación, mantener o combinación.",
                    "Slide requiere Sprint válido; los cambios de postura requieren espacio físico suficiente.",
                    "Exploración y evasión.",
                    "Al acabar un Slide bajo techo bajo, el personaje adopta LayDown automáticamente.",
                    ControlCategory.Movement),
                Create("StealthWalk", "MARCHA SIGILOSA",
                    "Reduce la intensidad de movimiento para avanzar con más control.",
                    "Mantener.",
                    "Disponible durante la locomoción normal.",
                    "Exploración.",
                    "Con mando, una inclinación suave del stick izquierdo cumple esta función.",
                    ControlCategory.Movement),
                Create("SwingGrab", "AGARRE / TRAVERSAL",
                    "Solicita el agarre de cuerdas, SwingPole y elementos de traversal compatibles.",
                    "Pulsación.",
                    "Requiere estar dentro de una zona de agarre válida.",
                    "Traversal.",
                    "También se usa en los puntos de agarre de cornisa configurados.",
                    ControlCategory.Traversal),
                Create("Cape", "CAPA",
                    "Extiende la capa mientras se mantiene la entrada.",
                    "Mantener.",
                    "Su efecto depende del estado del personaje y de corrientes ascendentes compatibles.",
                    "Traversal.",
                    "Al soltar el botón, la capa se repliega.",
                    ControlCategory.Traversal),
                Create("CameraLook", "MIRAR",
                    "Desplaza temporalmente el encuadre sin mover al personaje.",
                    "Mantener dirección.",
                    "Disponible cuando ningún control de cámara de mayor prioridad está activo.",
                    "Exploración.",
                    "La cámara vuelve suavemente al seguimiento habitual al soltar la entrada.",
                    ControlCategory.System),
                Create("DolphinDive", "DOLPHIN DIVE",
                    "El personaje realiza una zambullida hacia delante.",
                    "Pulsación y combinación con Sprint.",
                    "Solo disponible durante un Sprint válido.",
                    "Movimiento.",
                    "Se ejecuta inmediatamente y continúa hacia Prone / Prone Slide según el aterrizaje.",
                    ControlCategory.Movement),
                Create("Map", "MAPA",
                    "Abre o cierra el mapa del mundo.",
                    "Pulsación.",
                    "Requiere haber obtenido el mapa y que ninguna interfaz incompatible tenga el control.",
                    "Exploración.",
                    "El mapa conserva su sistema actual de exploración.",
                    ControlCategory.System),
                Create("Skills", "HABILIDADES",
                    "Abre la consulta de habilidades equipadas. Sentado en un banco permite modificar el equipamiento.",
                    "Pulsación.",
                    "La edición solo está permitida mientras se descansa en un banco.",
                    "Progresión.",
                    "Las habilidades equipadas funcionan durante el gameplay sin mantener este botón.",
                    ControlCategory.System),
                Create("Pause", "PAUSA",
                    "Abre el menú de pausa o retrocede dentro de sus pantallas.",
                    "Pulsación.",
                    "Durante una escena jugable.",
                    "Sistema.",
                    "Detiene gameplay y físicas, pero mantiene activa la navegación de interfaz.",
                    ControlCategory.System)
            };
        }

        public static ControlHelpCatalog CreateRuntimeDefault()
        {
            ControlHelpCatalog catalog = CreateInstance<ControlHelpCatalog>();
            catalog.PopulateDefaults();
            return catalog;
        }

        private static Entry Create(
            string actionName,
            string displayName,
            string description,
            string inputType,
            string condition,
            string context,
            string notes,
            ControlCategory category)
        {
            return new Entry
            {
                actionName = actionName,
                displayName = displayName,
                description = description,
                inputType = inputType,
                condition = condition,
                context = context,
                notes = notes,
                category = category
            };
        }
    }
}
