using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI.Menus
{
    [RequireComponent(typeof(Button))]
    public sealed class ControlBindingButton : MonoBehaviour,
        ISelectHandler,
        IPointerEnterHandler,
        IPointerClickHandler
    {
        private SpaceWorldMenuController owner;
        private string controlName;
        private IReadOnlyList<string> actionNames;

        public void Configure(
            SpaceWorldMenuController menuOwner,
            string displayControlName,
            IReadOnlyList<string> actions)
        {
            owner = menuOwner;
            controlName = displayControlName;
            actionNames = actions;
        }

        public void OnSelect(BaseEventData eventData)
        {
            owner?.ShowControlDetails(controlName, actionNames);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            EventSystem.current?.SetSelectedGameObject(gameObject);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            EventSystem.current?.SetSelectedGameObject(gameObject);
            owner?.ShowControlDetails(controlName, actionNames);
        }
    }
}
