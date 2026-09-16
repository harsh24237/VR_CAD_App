using UnityEngine;

namespace VRCAD.Core
{
    public class PassthroughManager : MonoBehaviour
    {
        public static PassthroughManager Instance { get; private set; }

        private bool isPassthroughEnabled = false;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public void TogglePassthrough()
        {
            SetPassthrough(!isPassthroughEnabled);
        }

        public void SetPassthrough(bool enable)
        {
            isPassthroughEnabled = enable;
            
            if (CAD_EnvironmentManager.Instance != null)
            {
                CAD_EnvironmentManager.Instance.SetPassthroughActive(enable);
            }

            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                if (enable)
                {
                    mainCam.clearFlags = CameraClearFlags.SolidColor;
                    mainCam.backgroundColor = new Color(0, 0, 0, 0); // Transparent black for Oculus Passthrough compositor
                }
                else
                {
                    mainCam.clearFlags = CameraClearFlags.SolidColor;
                    mainCam.backgroundColor = new Color(0.015f, 0.015f, 0.02f, 1f); // Deep space color
                }
            }

            // Hook into Meta's OVRManager if available
            System.Type ovrManagerType = System.Type.GetType("OVRManager, Assembly-CSharp");
            if (ovrManagerType != null)
            {
                var instanceProp = ovrManagerType.GetProperty("instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (instanceProp != null)
                {
                    var instance = instanceProp.GetValue(null);
                    if (instance != null)
                    {
                        var field = ovrManagerType.GetField("isInsightPassthroughEnabled");
                        if (field != null)
                        {
                            field.SetValue(instance, enable);
                        }
                    }
                }
            }
        }
    }
}
