using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRCAD.Core
{
    public class UIHapticTrigger : MonoBehaviour, IPointerDownHandler
    {
        public float amplitude = 0.4f;
        public float duration = 0.05f;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (HapticFeedbackManager.Instance == null) return;

            if (eventData is TrackedDeviceEventData trackedEventData)
            {
                if (trackedEventData.interactor is XRBaseControllerInteractor controllerInteractor)
                {
                    HapticFeedbackManager.Instance.TriggerHaptic(controllerInteractor.xrController, amplitude, duration);
                }
            }
        }
    }
}
