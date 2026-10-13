using UnityEngine;

namespace Game.Tutorials
{
    [CreateAssetMenu(
        fileName = "TUT_New",
        menuName = "Space World/Tutorials/Radio Tutorial Definition")]
    public sealed class RadioTutorialDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string tutorialId = "TUT_New";

        [Header("Radio dialogue")]
        [SerializeField] private string speaker = "Comunicaciones";
        [SerializeField, TextArea(2, 5)] private string openingLine;
        [SerializeField, TextArea(2, 5)] private string instruction;
        [SerializeField, TextArea(1, 3)] private string successLine = "Objetivo confirmado.";

        [Header("Completion")]
        [Tooltip("All listed actions must be reported after they actually succeed.")]
        [SerializeField] private RadioTutorialAction[] requiredActions =
            new RadioTutorialAction[0];

        public string TutorialId => tutorialId;
        public string Speaker => speaker;
        public string OpeningLine => openingLine;
        public string Instruction => instruction;
        public string SuccessLine => successLine;
        public RadioTutorialAction[] RequiredActions => requiredActions;

        public bool HasValidConfiguration =>
            !string.IsNullOrWhiteSpace(tutorialId) &&
            requiredActions != null &&
            requiredActions.Length > 0;
    }
}
