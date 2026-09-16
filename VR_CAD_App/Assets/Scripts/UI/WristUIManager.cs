using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using VRCAD.Core;
using UnityEngine.EventSystems;

namespace VRCAD.UI
{
    public class WristUIManager : MonoBehaviour
    {
        public Transform leftHandAnchor;
        public Camera mainCamera;

        private CanvasGroup canvasGroup;
        private bool isVisible = false;

        private float currentRoughness = 0.35f;
        private float currentMetallic = 0.1f;
        private float currentOpacity = 1.0f;

        private void Start()
        {
            if (mainCamera == null) mainCamera = Camera.main;

            if (leftHandAnchor == null)
            {
                var leftController = GameObject.Find("LeftHand Controller");
                if (leftController != null) leftHandAnchor = leftController.transform;
            }

            BuildWristUI();
        }

        private void BuildWristUI()
        {
            GameObject uiObj = new GameObject("CAD_WristCanvas");
            if (leftHandAnchor != null)
            {
                uiObj.transform.SetParent(leftHandAnchor, false);
                // Positioned slightly above the back of the wrist
                uiObj.transform.localPosition = new Vector3(-0.06f, 0.05f, -0.05f); 
                uiObj.transform.localEulerAngles = new Vector3(35, 10, 0); 
            }

            Canvas c = uiObj.AddComponent<Canvas>();
            c.renderMode = RenderMode.WorldSpace;
            uiObj.GetComponent<RectTransform>().sizeDelta = new Vector2(300, 200);
            uiObj.transform.localScale = Vector3.one * 0.0006f; // small enough for a wrist

            canvasGroup = uiObj.AddComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;

            uiObj.AddComponent<GraphicRaycaster>();
            
            // Background
            GameObject bgObj = new GameObject("Background");
            bgObj.transform.SetParent(uiObj.transform, false);
            Image bgImg = bgObj.AddComponent<Image>();
            bgImg.color = new Color(0.12f, 0.14f, 0.18f, 0.95f);
            RectTransform bgRect = bgObj.GetComponent<RectTransform>();
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;

            // Header
            GameObject hdrObj = new GameObject("Header");
            hdrObj.transform.SetParent(uiObj.transform, false);
            TextMeshProUGUI hdrTxt = hdrObj.AddComponent<TextMeshProUGUI>();
            hdrTxt.text = "<size=12><b>PALETTE</b></size>";
            hdrTxt.color = new Color(0.4f, 0.7f, 1.0f);
            hdrTxt.alignment = TextAlignmentOptions.TopLeft;
            RectTransform hdrRect = hdrObj.GetComponent<RectTransform>();
            hdrRect.anchoredPosition = new Vector2(10, -5);
            hdrRect.sizeDelta = new Vector2(280, 20);

            // Colors
            Color[] colors = { Color.white, Color.gray, Color.black, Color.red, new Color(1f, 0.5f, 0f), Color.yellow, Color.green, Color.cyan, Color.blue, Color.magenta };
            float startX = -120f;
            for(int i=0; i<colors.Length; i++)
            {
                int r = i / 5;
                int col = i % 5;
                CreateColorBtn(uiObj.transform, new Vector2(startX + col*50f, 50f - r*30f), colors[i]);
            }
        }

        private void CreateColorBtn(Transform parent, Vector2 pos, Color c)
        {
            GameObject btnObj = new GameObject("ColBtn");
            btnObj.transform.SetParent(parent, false);
            RectTransform rect = btnObj.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = new Vector2(30, 30);

            Image img = btnObj.AddComponent<Image>();
            img.color = c;

            Button btn = btnObj.AddComponent<Button>();
            btn.onClick.AddListener(() => CADManagerHub.Instance?.SetSelectedColor(c));
        }

        private void Update()
        {
            if (leftHandAnchor == null || mainCamera == null || canvasGroup == null) return;

            Vector3 palmNormal = leftHandAnchor.up; // or forward depending on XRI
            Vector3 toCamera = (mainCamera.transform.position - leftHandAnchor.position).normalized;
            
            float angle = Vector3.Angle(palmNormal, toCamera);
            
            isVisible = (angle < 45f);
            float targetAlpha = isVisible ? 1f : 0f;
            
            canvasGroup.alpha = Mathf.Lerp(canvasGroup.alpha, targetAlpha, Time.deltaTime * 6f);
            canvasGroup.interactable = (canvasGroup.alpha > 0.8f);
            canvasGroup.blocksRaycasts = canvasGroup.interactable;
        }
    }
}
