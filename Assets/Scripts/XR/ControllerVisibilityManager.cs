using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace AnatomiVR.XR
{
    /// <summary>
    /// Kemik tutulduğunda (Grab) controller modelini gizler,
    /// bırakıldığında geri gösterir.
    /// Hem XRDirectInteractor / XRRayInteractor hem de XRI 3 NearFarInteractor ile tam uyumludur.
    /// </summary>
    public class ControllerVisibilityManager : MonoBehaviour
    {
        [Header("Referanslar")]
        [Tooltip("Bu controller'daki XR Direct Interactor bileşeni (opsiyonel, alt objelerden otomatik bulunur)")]
        [SerializeField] private XRDirectInteractor directInteractor;

        [Tooltip("Bu controller'daki XR Ray Interactor bileşeni (opsiyonel, alt objelerden otomatik bulunur)")]
        [SerializeField] private XRRayInteractor rayInteractor;

        [Tooltip("Gizlenecek controller model objesi (Left/Right Controller Visual veya ControllerModel)")]
        [SerializeField] private GameObject controllerModel;

        [Header("Ray Görünürlüğü")]
        [Tooltip("Grab sırasında ray line'ı da gizlensin mi?")]
        [SerializeField] private bool hideRayOnGrab = true;

        private readonly List<XRBaseInteractor> _interactors = new List<XRBaseInteractor>();
        private XRInteractorLineVisual[] _rayLineVisuals;

        private void Awake()
        {
            AutoResolveReferences();
        }

        private void OnEnable()
        {
            AutoResolveReferences();

            foreach (XRBaseInteractor interactor in _interactors)
            {
                if (interactor != null)
                {
                    interactor.selectEntered.AddListener(OnGrabStarted);
                    interactor.selectExited.AddListener(OnGrabEnded);
                }
            }
        }

        private void OnDisable()
        {
            foreach (XRBaseInteractor interactor in _interactors)
            {
                if (interactor != null)
                {
                    interactor.selectEntered.RemoveListener(OnGrabStarted);
                    interactor.selectExited.RemoveListener(OnGrabEnded);
                }
            }
        }

        private void AutoResolveReferences()
        {
            _interactors.Clear();

            if (directInteractor != null && !_interactors.Contains(directInteractor))
                _interactors.Add(directInteractor);

            if (rayInteractor != null && !_interactors.Contains(rayInteractor))
                _interactors.Add(rayInteractor);

            // Alt hiyerarşideki tüm XRBaseInteractor bileşenlerini (NearFarInteractor dahil) ekle
            foreach (XRBaseInteractor childInteractor in GetComponentsInChildren<XRBaseInteractor>(true))
            {
                if (childInteractor != null && !_interactors.Contains(childInteractor))
                    _interactors.Add(childInteractor);
            }

            if (controllerModel == null)
            {
                Transform found = transform.Find("Left Controller Visual")
                               ?? transform.Find("Right Controller Visual")
                               ?? transform.Find("ControllerModel")
                               ?? transform.Find("Model");
                if (found != null)
                    controllerModel = found.gameObject;
            }

            _rayLineVisuals = GetComponentsInChildren<XRInteractorLineVisual>(true);
        }

        private void OnGrabStarted(SelectEnterEventArgs args)
        {
            if (!IsGrabbableBone(args.interactableObject))
                return;

            SetControllerVisibility(false);
        }

        private void OnGrabEnded(SelectExitEventArgs args)
        {
            if (!IsGrabbableBone(args.interactableObject))
                return;

            SetControllerVisibility(true);
        }

        private void SetControllerVisibility(bool visible)
        {
            if (controllerModel != null)
            {
                controllerModel.SetActive(visible);
            }
            else
            {
                Debug.LogWarning("[ControllerVisibility] Controller model referansı atanmamış!");
            }

            if (hideRayOnGrab && _rayLineVisuals != null)
            {
                foreach (var lineVisual in _rayLineVisuals)
                {
                    if (lineVisual != null)
                        lineVisual.enabled = visible;
                }
            }
        }

        private bool IsGrabbableBone(IXRInteractable interactable)
        {
            if (interactable == null) return false;
            return interactable is XRGrabInteractable || interactable.transform.GetComponent<XRGrabInteractable>() != null;
        }

        private void Reset()
        {
            if (directInteractor == null)
                directInteractor = GetComponentInChildren<XRDirectInteractor>(true);

            if (rayInteractor == null)
                rayInteractor = GetComponentInChildren<XRRayInteractor>(true);

            AutoResolveReferences();
        }
    }
}
