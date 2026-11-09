using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AnatomiVR.XR
{
    /// <summary>
    /// World Space Canvas'ın VR etkileşimi için doğru yapılandırıldığından emin olan yardımcı bileşen.
    /// Bu script Canvas GameObject'ine yerleştirilmelidir.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class XRUISetup : MonoBehaviour
    {
        /// <summary>
        /// TrackedDeviceGraphicRaycaster için assembly-qualified tür adı.
        /// XR Interaction Toolkit henüz içe aktarılmadığında yedek olarak kullanılır.
        /// </summary>
        private const string TrackedDeviceGraphicRaycasterTypeName =
            "UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster, Unity.XR.Interaction.Toolkit";

        /// <summary>
        /// XRUIInputModule için assembly-qualified tür adı.
        /// XR Interaction Toolkit henüz içe aktarılmadığında yedek olarak kullanılır.
        /// </summary>
        private const string XRUIInputModuleTypeName =
            "UnityEngine.XR.Interaction.Toolkit.UI.XRUIInputModule, Unity.XR.Interaction.Toolkit";

        [Header("Yapılandırma Ayarları")]
        [SerializeField, Tooltip("Etkinleştirilirse, ana kamerayı bulur ve Canvas olay kamerası olarak atar.")]
        private bool _autoConfigureCamera = true;

        /// <summary>
        /// Bu Canvas üzerindeki referans.
        /// </summary>
        private Canvas _canvas;

        /// <summary>
        /// Başlangıçta Canvas'ı VR etkileşimi için yapılandırır.
        /// </summary>
        private void Start()
        {
            _canvas = GetComponent<Canvas>();

            ValidateWorldSpaceRenderMode();
            ConfigureGraphicRaycaster();
            ValidateXRInteractionManager();
            ConfigureEventSystem();
            ConfigureEventCamera();

            Debug.Log("[XRUISetup] Canvas VR yapılandırması tamamlandı.", this);
        }

        /// <summary>
        /// Canvas'ın World Space render moduna ayarlandığını doğrular.
        /// </summary>
        private void ValidateWorldSpaceRenderMode()
        {
            if (_canvas.renderMode != RenderMode.WorldSpace)
            {
                Debug.LogWarning(
                    $"[XRUISetup] Canvas render modu '{_canvas.renderMode}' olarak ayarlı. " +
                    "VR etkileşimi için 'World Space' olması gerekir. Otomatik olarak değiştiriliyor.",
                    this
                );
                _canvas.renderMode = RenderMode.WorldSpace;
            }
            else
            {
                Debug.Log("[XRUISetup] Canvas render modu doğru: World Space.", this);
            }
        }

        /// <summary>
        /// Standart <see cref="GraphicRaycaster"/>'ı kaldırır (VR ile uyumsuz)
        /// ve <c>TrackedDeviceGraphicRaycaster</c> ekler.
        /// </summary>
        private void ConfigureGraphicRaycaster()
        {
            GraphicRaycaster standardRaycaster = GetComponent<GraphicRaycaster>();
            if (standardRaycaster != null)
            {
                standardRaycaster.blockingObjects = GraphicRaycaster.BlockingObjects.All;
#if !UNITY_EDITOR
                // Cihazda standart GraphicRaycaster varsa kaldır (Editor'da fare ile tıklama için korunur)
                Destroy(standardRaycaster);
#endif
            }

            // TrackedDeviceGraphicRaycaster türünü bul
            Type trackedRaycasterType = Type.GetType(TrackedDeviceGraphicRaycasterTypeName);

            if (trackedRaycasterType == null)
            {
                Debug.LogError(
                    "[XRUISetup] TrackedDeviceGraphicRaycaster türü bulunamadı. " +
                    "XR Interaction Toolkit paketinin projeye eklendiğinden emin olun.",
                    this
                );
                return;
            }

            // Zaten ekliyse al, yoksa ekle
            Component trackedRaycaster = GetComponent(trackedRaycasterType);
            if (trackedRaycaster == null)
            {
                trackedRaycaster = gameObject.AddComponent(trackedRaycasterType);
            }

            // Havada asılı duran kemiklerin ve pin küreciklerinin arkadaki menüye lazer geçirmesini engelle (3D Occlusion).
            // Not: Eğer bu Canvas kendisi tutulabilir bir 3B tablet ise ([TutorialTablet]), üzerindeki BoxCollider'ın
            // kendi UI butonlarını engellememesi için 3D Occlusion yalnızca sabit ekranlarda aktif edilir.
            bool isGrabbableTablet = GetComponent<Collider>() != null;
            var occProp = trackedRaycasterType.GetProperty("checkFor3DOcclusion");
            if (occProp != null && occProp.CanWrite)
                occProp.SetValue(trackedRaycaster, !isGrabbableTablet);

            var trigProp = trackedRaycasterType.GetProperty("raycastTriggerInteraction");
            if (trigProp != null && trigProp.CanWrite)
                trigProp.SetValue(trackedRaycaster, QueryTriggerInteraction.Ignore);

            Debug.Log("[XRUISetup] TrackedDeviceGraphicRaycaster 3D Occlusion aktif edildi.", this);
        }

        /// <summary>
        /// Sahnede XR Interaction Manager bulunup bulunmadığını kontrol eder.
        /// </summary>
        private void ValidateXRInteractionManager()
        {
            // XRInteractionManager doğrudan erişilebilir çünkü temel XRIT paketinin parçası
            var interactionManager =
#if UNITY_6000_0_OR_NEWER
                UnityEngine.Object.FindAnyObjectByType<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>();
#else
                FindObjectOfType<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>();
#endif

            if (interactionManager == null)
            {
                Debug.LogWarning(
                    "[XRUISetup] Sahnede XRInteractionManager bulunamadı! " +
                    "VR etkileşimlerinin çalışması için bir XRInteractionManager gereklidir. " +
                    "Lütfen sahneye bir XR Interaction Manager ekleyin.",
                    this
                );
            }
            else
            {
                Debug.Log(
                    $"[XRUISetup] XRInteractionManager bulundu: '{interactionManager.gameObject.name}'.",
                    this
                );
            }
        }

        /// <summary>
        /// <see cref="EventSystem"/> bulur ve <c>XRUIInputModule</c> bileşeninin
        /// mevcut olduğundan emin olur.
        /// </summary>
        private void ConfigureEventSystem()
        {
            EventSystem eventSystem =
#if UNITY_6000_0_OR_NEWER
                UnityEngine.Object.FindAnyObjectByType<EventSystem>();
#else
                FindObjectOfType<EventSystem>();
#endif

            if (eventSystem == null)
            {
                Debug.LogWarning(
                    "[XRUISetup] Sahnede EventSystem bulunamadı! " +
                    "UI etkileşimleri için bir EventSystem gereklidir.",
                    this
                );
                return;
            }

            Debug.Log(
                $"[XRUISetup] EventSystem bulundu: '{eventSystem.gameObject.name}'.",
                this
            );

            // XRUIInputModule türünü bul
            Type xrInputModuleType = Type.GetType(XRUIInputModuleTypeName);

            if (xrInputModuleType == null)
            {
                Debug.LogError(
                    "[XRUISetup] XRUIInputModule türü bulunamadı. " +
                    "XR Interaction Toolkit paketinin projeye eklendiğinden emin olun.",
                    this
                );
                return;
            }

            // XRUIInputModule zaten var mı kontrol et
            Component existingInputModule = eventSystem.GetComponent(xrInputModuleType);
            if (existingInputModule != null)
            {
                Debug.Log("[XRUISetup] XRUIInputModule zaten EventSystem üzerinde mevcut.", this);
                return;
            }

            // XRUIInputModule ekle
            eventSystem.gameObject.AddComponent(xrInputModuleType);
            Debug.Log("[XRUISetup] XRUIInputModule EventSystem'e başarıyla eklendi.", this);
        }

        /// <summary>
        /// <see cref="_autoConfigureCamera"/> etkinse ana kamerayı bulur
        /// ve Canvas olay kamerası olarak atar.
        /// </summary>
        private void ConfigureEventCamera()
        {
            if (!_autoConfigureCamera)
            {
                Debug.Log("[XRUISetup] Otomatik kamera yapılandırması devre dışı.", this);
                return;
            }

            Camera mainCamera = Camera.main;

            if (mainCamera == null)
            {
                Debug.LogWarning(
                    "[XRUISetup] Ana kamera (Main Camera) bulunamadı. " +
                    "Canvas olay kamerası atanamadı.",
                    this
                );
                return;
            }

            _canvas.worldCamera = mainCamera;
            Debug.Log(
                $"[XRUISetup] Canvas olay kamerası olarak '{mainCamera.gameObject.name}' atandı.",
                this
            );
        }
    }
}
