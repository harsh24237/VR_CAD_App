using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace VRCAD.Core
{
    public class HapticFeedbackManager : MonoBehaviour
    {
        public static HapticFeedbackManager Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void TriggerHaptic(XRBaseController controller, float amplitude, float duration)
        {
            if (controller != null)
            {
                controller.SendHapticImpulse(amplitude, duration);
            }
        }

        public void TriggerHaptic(UnityEngine.XR.Interaction.Toolkit.IXRInteractor interactor, float amplitude, float duration)
        {
            if (interactor != null && interactor is XRBaseControllerInteractor controllerInteractor)
            {
                TriggerHaptic(controllerInteractor.xrController, amplitude, duration);
            }
        }

        public void TriggerHaptic(SelectEnterEventArgs args, float amplitude, float duration)
        {
            if (args != null && args.interactorObject != null)
            {
                TriggerHaptic(args.interactorObject, amplitude, duration);
            }
        }
    }
}
