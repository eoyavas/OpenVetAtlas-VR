using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using TMPro;

namespace AnatomiVR.Bones
{
    [Serializable]
    public class AnatomicalLandmark
    {
        [Tooltip("Anatomik oluşumun Latince adı (örn: Caput humeri)")]
        public string labelText = "Caput humeri";

        [Tooltip("Kemik yüzeyi üzerindeki noktanın kemik yerel (local) koordinatı")]
        public Vector3 anchorLocalPos = new Vector3(0f, 0.5f, 0f);

        [Tooltip("Havada duran etiket kutusunun kemik yerel (local) koordinatı")]
        public Vector3 labelLocalPos = new Vector3(0.45f, 0.65f, 0f);

        [Tooltip("Derleme/Kurulum sırasında önceden hesaplanmış (Pre-Baked) yerel dış yüzey normali")]
        public Vector3 prebakedOutwardNormal = Vector3.zero;
    }

    /// <summary>
    /// Her pin küreciğinin (SurfaceDot) üzerine eklenen işaretleyici bileşen.
    /// Lazer ışını (Raycast) küreciğe çarptığında hangi kemiğin kaçıncı pininin
    /// hedeflendiğini anında tespit etmeyi sağlar.
    /// </summary>
    public class PinDotTarget : MonoBehaviour
    {
        public BoneAnatomicalLabels Owner;
        public int PinIndex;
    }

    /// <summary>
    /// Seçenek 1 — Akıllı Holografik Pin & Tetik (Trigger) ile Aç/Kapat Sistemi:
    /// 1) VR kumandadaki X (veya A) tuşuna ya da PC'de [X] / [L] tuşuna basıldığında kemik üzerindeki
    ///    anatomik noktalarda yalnızca parlayan holografik kürecikler (Pin) belirir; bilgi kartları
    ///    başlangıçta kapalı durur (üst üste binmez).
    /// 2) Kullanıcı VR lazerini (veya PC'de fare imlecini) istediği küreciğin üzerine getirip
    ///    TETİK (Trigger) tuşuna bir kez basıp bıraktığında (basılı tutmadan!) sadece o küreciğin
    ///    holografik çizgisi ve 3B bilgi kartı açılır. Tekrar tetik tuşuna basıldığında kapanır.
    /// 3) Kemik veya pin kürecikleri üzerine nişan alındığında lazerin kemiği delip arkadaki
    ///    Akıllı Ekran menüsüne tıklamasını %100 engeller.
    /// </summary>
    [ExecuteAlways]
    public class BoneAnatomicalLabels : MonoBehaviour
    {
        public static readonly List<BoneAnatomicalLabels> ActiveLabelSystems = new List<BoneAnatomicalLabels>();

        [Header("Anatomik Noktalar (Landmarks)")]
        [Tooltip("Kemik üzerinde gösterilecek anatomik oluşum noktaları ve etiket konumları")]
        public List<AnatomicalLandmark> landmarks = new List<AnatomicalLandmark>();

        [Header("Görsel Ayarlar (Holografik Pin & Çizgi)")]
        [Tooltip("Holografik çizgi kalınlığı (metre)")]
        public float lineWidth = 0.0022f;

        [Tooltip("Kemik yüzeyindeki nokta küresinin çapı (metre)")]
        public float pinDotSize = 0.0085f;

        [Tooltip("3B Etiket kartı ölçeği")]
        public float labelWorldScale = 0.00042f;

        [Tooltip("Kapalı pin küresi ve holografik çerçeve rengi")]
        public Color hologramCyan = new Color(0.00f, 0.90f, 1.00f, 0.96f);

        [Tooltip("Lazerle üzerine gelinen (Hover) küre rengi")]
        public Color hoverDotColor = new Color(1.00f, 0.92f, 0.22f, 1.00f);

        [Tooltip("Bilgi kartı açık (seçili) küre rengi")]
        public Color expandedDotColor = new Color(0.18f, 1.00f, 0.58f, 1.00f);

        [Tooltip("Etiket arka plan rengi")]
        public Color cardBgColor = new Color(0.02f, 0.07f, 0.14f, 0.95f);

        [Header("Editör Önizleme")]
        [Tooltip("Unity Scene ekranında pinleri ve çizgileri her zaman göster")]
        public bool showGizmosInEditor = true;

        // ─────────────────────────── Runtime ─────────────────────────────
        private XRGrabInteractable _grabInteractable;
        private BoneIdentifier _boneIdentifier;
        private GameObject _runtimeContainer;
        private GameObject _exactRaycasterGO;
        private MeshCollider _exactSurfaceRayCollider;
        private Transform _meshFilterTransform;
        private readonly List<RuntimePinVisual> _visuals = new List<RuntimePinVisual>();
        private bool _labelsVisible;
        private Camera _mainCam;

        // Statik Lazer & Tetik (Trigger) Takibi (Histerezis + Sekme Önleyici Kilit + En Yakın Kemik X Tuşu)
        private static int _lastRayUpdateFrame = -1;
        private static bool _prevVrPrimaryStateGlobal;
        private static bool _prevLeftTrigger;
        private static bool _prevRightTrigger;
        private static BoneAnatomicalLabels _hoveredOwnerLeft;
        private static int _hoveredPinLeft = -1;
        private static BoneAnatomicalLabels _hoveredOwnerRight;
        private static int _hoveredPinRight = -1;
        private static BoneAnatomicalLabels _hoveredOwnerMouse;
        private static int _hoveredPinMouse = -1;
        private static bool _anyRayBlockingMenuThisFrame;

        // Tetik çekilirken el titremesine karşı son seçili küre hafızası (Grace Period) ve sekme önleyici zamanlayıcı
        private static BoneAnatomicalLabels _graceOwnerLeft;
        private static int _gracePinLeft = -1;
        private static float _graceTimeLeft;
        private static float _lastSwitchTimeLeft;

        private static BoneAnatomicalLabels _graceOwnerRight;
        private static int _gracePinRight = -1;
        private static float _graceTimeRight;
        private static float _lastSwitchTimeRight;

        // Kumanda lazer ışını yumuşatma (mikro el titremesini filtreler)
        private static bool _hasSmoothedLeftRay;
        private static Vector3 _smoothedLeftOrigin;
        private static Vector3 _smoothedLeftDir = Vector3.forward;
        private static bool _hasSmoothedRightRay;
        private static Vector3 _smoothedRightOrigin;
        private static Vector3 _smoothedRightDir = Vector3.forward;

        private class RuntimePinVisual
        {
            public int Index;
            public AnatomicalLandmark Data;
            public Transform DotTransform;
            public Material DotMaterial;
            public LineRenderer Line;
            public Transform CardTransform;
            public float BaseLocalDotScale;
            public Vector3 LocalOutwardNormal;
            public bool IsExpanded;
            public bool IsHovered;
            public Vector3 CurrentWorldLabelPos;
        }

        private void Awake()
        {
            _grabInteractable = GetComponent<XRGrabInteractable>();
            _boneIdentifier = GetComponent<BoneIdentifier>();
            if (landmarks == null || landmarks.Count == 0)
            {
                SyncFromBlenderChildPinsIfPresent();
            }
        }

        private void OnEnable()
        {
            if (Application.isPlaying && !ActiveLabelSystems.Contains(this))
            {
                ActiveLabelSystems.Add(this);
            }
        }

        private void Start()
        {
            if (!Application.isPlaying)
                return;

            if (!ActiveLabelSystems.Contains(this))
                ActiveLabelSystems.Add(this);

            // Kemik menüden çağrıldığında anında (0 gecikmeyle) masaya gelmesi için
            // 20 adet World-Space Canvas + TMP + küre görseli yalnızca kullanıcı [X]/[A] tuşuna
            // bastığında (SetLabelsVisible(true)) Pre-Baked normallerle milisaniyeler içinde oluşturulur.
            _labelsVisible = false;
        }

        private void OnDisable()
        {
            ActiveLabelSystems.Remove(this);
            if (Application.isPlaying)
            {
                SetLabelsVisible(false);
            }
            if (_exactSurfaceRayCollider != null)
            {
                _exactSurfaceRayCollider.enabled = false;
            }
        }

        private void OnDestroy()
        {
            if (_exactRaycasterGO != null)
            {
                if (Application.isPlaying)
                    Destroy(_exactRaycasterGO);
                else
                    DestroyImmediate(_exactRaycasterGO);
                _exactRaycasterGO = null;
                _exactSurfaceRayCollider = null;
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            // Global lazer taraması, X tuşu (sadece en yakın kemik) ve tetik (Trigger) tıklamalarını her frame'de bir kez çalıştır
            UpdateGlobalLaserHoverAndTriggerClicks();

            if (_labelsVisible)
            {
                UpdateRuntimeVisualPositions();
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || !_labelsVisible)
                return;

            // Görsel VR lazer çizgisinin (LineRenderer) Convex MeshCollider yüzünden Fossa (çukur) bölgelerin
            // 3-4 cm üstünde havada durmasını önle; lazeri doğrudan çukurun içindeki küreciğe / gerçek yüzeye uzat!
            ExtendControllerLaserIntoFossaAndPins();
        }

        /// <summary>
        /// Herhangi bir VR kumanda lazeri (veya PC fare ışını) şu an bir kemiğin ya da
        /// pin küreciğinin üzerine/yakınına nişan alıyor mu?
        /// True olduğunda arkadaki [SmartScreen] menüsünün tıklanması engellenir.
        /// </summary>
        public static bool IsAnyRayBlockedByBoneOrPin()
        {
            if (!Application.isPlaying)
                return false;

            if (_lastRayUpdateFrame != Time.frameCount)
            {
                UpdateGlobalLaserHoverAndTriggerClicks();
            }

            return _anyRayBlockingMenuThisFrame;
        }

        public static bool IsMouseHoveringAnyPin()
        {
            if (!Application.isPlaying)
                return false;

            if (_lastRayUpdateFrame != Time.frameCount)
            {
                UpdateGlobalLaserHoverAndTriggerClicks();
            }

            return _hoveredOwnerMouse != null && _hoveredPinMouse >= 0;
        }

        // ─────────────── Global VR Lazer Nişan & Tetik (Trigger) Tıklama ───────────────

        private static void UpdateGlobalLaserHoverAndTriggerClicks()
        {
            if (_lastRayUpdateFrame == Time.frameCount)
                return;
            _lastRayUpdateFrame = Time.frameCount;

            // 0. [X] / [A] Tuşu Kontrolü — Yalnızca ELDEKİ veya KULLANICIYA EN YAKIN TEK KEMİKTE çalışır!
            if (CheckGlobalToggleInputPressed())
            {
                BoneAnatomicalLabels targetSys = FindClosestOrHeldLabelSystem();
                if (targetSys != null)
                {
                    targetSys.ToggleLabels();
                }
            }

            _anyRayBlockingMenuThisFrame = false;

            BoneAnatomicalLabels prevOwnerL = _hoveredOwnerLeft;
            int prevPinL = _hoveredPinLeft;
            BoneAnatomicalLabels prevOwnerR = _hoveredOwnerRight;
            int prevPinR = _hoveredPinRight;
            BoneAnatomicalLabels prevOwnerM = _hoveredOwnerMouse;
            int prevPinM = _hoveredPinMouse;

            _hoveredOwnerLeft = null;
            _hoveredPinLeft = -1;
            _hoveredOwnerRight = null;
            _hoveredPinRight = -1;
            _hoveredOwnerMouse = null;
            _hoveredPinMouse = -1;

            // Tüm küreciklerin hover bayrağını sıfırla
            for (int s = 0; s < ActiveLabelSystems.Count; s++)
            {
                var sys = ActiveLabelSystems[s];
                if (sys == null) continue;
                for (int i = 0; i < sys._visuals.Count; i++)
                {
                    sys._visuals[i].IsHovered = false;
                }
            }

            // 1. Sol ve Sağ VR Kumanda Lazer Işınlarını Tespit Et (Histerezis + Sekme Önleyici Kilit + Ön Yüzey Kontrolü ile)
            //    Not: Kemiği tutan (Grip basılı olan) kumandanın lazeri devre dışıdır; takılı sarı nokta bırakmaz!
            float leftBestScore = float.MaxValue;
            float rightBestScore = float.MaxValue;

            if (TryGetControllerLaserRay(true, out Ray leftRay))
            {
                if (EvaluateRayAgainstBonesAndPins(
                    leftRay,
                    prevOwnerL,
                    prevPinL,
                    ref _lastSwitchTimeLeft,
                    out _hoveredOwnerLeft,
                    out _hoveredPinLeft,
                    out leftBestScore))
                {
                    _anyRayBlockingMenuThisFrame = true;
                }
            }
            else
            {
                _graceOwnerLeft = null;
                _gracePinLeft = -1;
            }

            if (TryGetControllerLaserRay(false, out Ray rightRay))
            {
                if (EvaluateRayAgainstBonesAndPins(
                    rightRay,
                    prevOwnerR,
                    prevPinR,
                    ref _lastSwitchTimeRight,
                    out _hoveredOwnerRight,
                    out _hoveredPinRight,
                    out rightBestScore))
                {
                    _anyRayBlockingMenuThisFrame = true;
                }
            }
            else
            {
                _graceOwnerRight = null;
                _gracePinRight = -1;
            }

            // Aynı anda iki farklı kürenin sarı yanmasını (boştaki elin kumandasının arkada başka bir küreyi
            // sarı tutmasını) kesin olarak engelle: Her zaman yalnızca aktif/en yakın TEK küre sarı yanar!
            if (_hoveredOwnerLeft != null && _hoveredPinLeft >= 0 &&
                _hoveredOwnerRight != null && _hoveredPinRight >= 0)
            {
                if (rightBestScore <= leftBestScore)
                {
                    _hoveredOwnerLeft = null;
                    _hoveredPinLeft = -1;
                }
                else
                {
                    _hoveredOwnerRight = null;
                    _hoveredPinRight = -1;
                }
            }

            // 2. PC Fare Işınını Tespit Et
            Camera cam = Camera.main;
            if (cam != null && Mouse.current != null)
            {
                Ray mouseRay = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                float dummySwitchTime = 0f;
                if (EvaluateRayAgainstBonesAndPins(
                    mouseRay,
                    prevOwnerM,
                    prevPinM,
                    ref dummySwitchTime,
                    out _hoveredOwnerMouse,
                    out _hoveredPinMouse,
                    out _))
                {
                    _anyRayBlockingMenuThisFrame = true;
                }
            }

            // Hover olan kürecikleri işaretle, Grace hafızasını güncelle ve yeni hover olduğunda hafif titreşim ver
            if (_hoveredOwnerLeft != null && _hoveredPinLeft >= 0 && _hoveredPinLeft < _hoveredOwnerLeft._visuals.Count)
            {
                _hoveredOwnerLeft._visuals[_hoveredPinLeft].IsHovered = true;
                _graceOwnerLeft = _hoveredOwnerLeft;
                _gracePinLeft = _hoveredPinLeft;
                _graceTimeLeft = Time.unscaledTime;

                if (_hoveredOwnerLeft != prevOwnerL || _hoveredPinLeft != prevPinL)
                {
                    SendControllerHaptic(UnityEngine.XR.XRNode.LeftHand, 0.16f, 0.025f);
                }
            }

            if (_hoveredOwnerRight != null && _hoveredPinRight >= 0 && _hoveredPinRight < _hoveredOwnerRight._visuals.Count)
            {
                _hoveredOwnerRight._visuals[_hoveredPinRight].IsHovered = true;
                _graceOwnerRight = _hoveredOwnerRight;
                _gracePinRight = _hoveredPinRight;
                _graceTimeRight = Time.unscaledTime;

                if (_hoveredOwnerRight != prevOwnerR || _hoveredPinRight != prevPinR)
                {
                    SendControllerHaptic(UnityEngine.XR.XRNode.RightHand, 0.16f, 0.025f);
                }
            }

            if (_hoveredOwnerMouse != null && _hoveredPinMouse >= 0 && _hoveredPinMouse < _hoveredOwnerMouse._visuals.Count)
            {
                _hoveredOwnerMouse._visuals[_hoveredPinMouse].IsHovered = true;
            }

            // 3. TETİK TUŞU (Trigger) Kontrolü — Basılı tutmaya gerek kalmadan tek tıkla Aç/Kapat!
            //    (Tetik çekilirken el milimetrik oynasa bile 0.28 sn Grace hafızası sayesinde seçimi kaçırmaz)
            bool leftTriggerNow = IsControllerTriggerPressed(UnityEngine.XR.XRNode.LeftHand, _prevLeftTrigger);
            bool rightTriggerNow = IsControllerTriggerPressed(UnityEngine.XR.XRNode.RightHand, _prevRightTrigger);

            bool leftTriggerJustClicked = leftTriggerNow && !_prevLeftTrigger;
            bool rightTriggerJustClicked = rightTriggerNow && !_prevRightTrigger;

            _prevLeftTrigger = leftTriggerNow;
            _prevRightTrigger = rightTriggerNow;

            const float triggerGraceSeconds = 0.28f;

            if (leftTriggerJustClicked)
            {
                BoneAnatomicalLabels targetOwner = _hoveredOwnerLeft;
                int targetPin = _hoveredPinLeft;

                if ((targetOwner == null || targetPin < 0) &&
                    _graceOwnerLeft != null && _graceOwnerLeft._labelsVisible &&
                    (Time.unscaledTime - _graceTimeLeft) <= triggerGraceSeconds)
                {
                    targetOwner = _graceOwnerLeft;
                    targetPin = _gracePinLeft;
                }

                if (targetOwner == null || targetPin < 0)
                {
                    targetOwner = _hoveredOwnerRight;
                    targetPin = _hoveredPinRight;
                }

                if (targetOwner != null && targetPin >= 0)
                {
                    targetOwner.ToggleSinglePinCard(targetPin);
                    SendControllerHaptic(UnityEngine.XR.XRNode.LeftHand, 0.40f, 0.05f);
                }
            }

            if (rightTriggerJustClicked)
            {
                BoneAnatomicalLabels targetOwner = _hoveredOwnerRight;
                int targetPin = _hoveredPinRight;

                if ((targetOwner == null || targetPin < 0) &&
                    _graceOwnerRight != null && _graceOwnerRight._labelsVisible &&
                    (Time.unscaledTime - _graceTimeRight) <= triggerGraceSeconds)
                {
                    targetOwner = _graceOwnerRight;
                    targetPin = _gracePinRight;
                }

                if (targetOwner == null || targetPin < 0)
                {
                    targetOwner = _hoveredOwnerLeft;
                    targetPin = _hoveredPinLeft;
                }

                if (targetOwner != null && targetPin >= 0)
                {
                    targetOwner.ToggleSinglePinCard(targetPin);
                    SendControllerHaptic(UnityEngine.XR.XRNode.RightHand, 0.40f, 0.05f);
                }
            }

            // PC Sol Fare Tıkı ile Pin Küreciği Aç/Kapat
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (_hoveredOwnerMouse != null && _hoveredPinMouse >= 0)
                {
                    _hoveredOwnerMouse.ToggleSinglePinCard(_hoveredPinMouse);
                }
            }
        }

        /// <summary>
        /// Masada birden fazla kemik varken X tuşuna basıldığında yalnızca:
        /// 1) Kullanıcının elinde tuttuğu kemiği,
        /// 2) Elde kemik yoksa kullanıcıya (kameraya) EN YAKIN olan tek kemiği döndürür.
        /// Diğer kemiklerle etkileşime girmez.
        /// </summary>
        private static BoneAnatomicalLabels FindClosestOrHeldLabelSystem()
        {
            // 1. Öncelik: Şu an VR'da elde kavranmış (Grip) olan kemik
            for (int i = 0; i < ActiveLabelSystems.Count; i++)
            {
                var sys = ActiveLabelSystems[i];
                if (sys == null || !sys.gameObject.activeInHierarchy) continue;
                if (sys._grabInteractable != null && sys._grabInteractable.isSelected)
                    return sys;
            }

            // 2. Öncelik: Kullanıcıya (Main Camera) en yakın olan tek kemik
            Camera cam = Camera.main;
            Vector3 refPos = cam != null ? cam.transform.position : Vector3.zero;
            BoneAnatomicalLabels closestSys = null;
            float bestDist = 3.5f;

            for (int i = 0; i < ActiveLabelSystems.Count; i++)
            {
                var sys = ActiveLabelSystems[i];
                if (sys == null || !sys.gameObject.activeInHierarchy) continue;

                Vector3 boneCenter = sys._boneIdentifier != null
                    ? sys._boneIdentifier.GetWorldMeshCenter()
                    : sys.transform.position;

                float d = Vector3.Distance(refPos, boneCenter);
                if (d < bestDist)
                {
                    bestDist = d;
                    closestSys = sys;
                }
            }

            return closestSys;
        }

        private static bool IsControllerTriggerPressed(UnityEngine.XR.XRNode handNode, bool wasPressedLastFrame)
        {
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(handNode);
            if (!dev.isValid)
                return false;

            float threshold = wasPressedLastFrame ? 0.35f : 0.55f;
            if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float trigVal))
            {
                return trigVal >= threshold;
            }

            if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool trigBtn) && trigBtn)
                return true;

            return false;
        }

        private static void SendControllerHaptic(UnityEngine.XR.XRNode handNode, float amplitude, float duration)
        {
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(handNode);
            if (dev.isValid)
            {
                dev.SendHapticImpulse(0u, amplitude, duration);
            }
        }

        /// <summary>
        /// İlgili kumanda şu an bir kemiği kavrıyor mu (Grip basılı mı veya Interactor bir nesne tutuyor mu)?
        /// Kemiği tutan kumandanın lazeri küre seçimi yapmamalıdır; aksi halde eldeki kemiğin üzerinde
        /// sabit sarı nokta takılı kalır.
        /// </summary>
        private static bool IsControllerHoldingOrGripping(bool isLeftHand, Transform ctrlTr)
        {
            var handNode = isLeftHand ? UnityEngine.XR.XRNode.LeftHand : UnityEngine.XR.XRNode.RightHand;
            var dev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(handNode);
            if (dev.isValid)
            {
                if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool gripBtn) && gripBtn)
                    return true;
                if (dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float gripVal) && gripVal > 0.25f)
                    return true;
            }

            if (ctrlTr != null)
            {
                foreach (var interactor in ctrlTr.GetComponentsInChildren<XRBaseInteractor>(true))
                {
                    if (interactor != null && interactor.hasSelection)
                    {
                        for (int i = 0; i < interactor.interactablesSelected.Count; i++)
                        {
                            var sel = interactor.interactablesSelected[i] as Component;
                            if (sel != null && sel.GetComponentInParent<BoneIdentifier>() != null)
                                return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Sol veya Sağ VR kumandanın gerçek, bükülmemiş ve titreşimi filtrelenmiş lazer ışınını döndürür.
        /// Kemiği kavrayan (Grip basılı) veya görsel lazer çizgisi kapalı olan kumanda için false döner.
        /// </summary>
        private static bool TryGetControllerLaserRay(bool isLeftHand, out Ray ray)
        {
            ray = default;
            var xrOrigin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin == null)
                return false;

            string ctrlName = isLeftHand ? "Left Controller" : "Right Controller";
            Transform ctrlTr = null;

            foreach (Transform t in xrOrigin.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == ctrlName)
                {
                    ctrlTr = t;
                    break;
                }
            }

            if (ctrlTr == null || !ctrlTr.gameObject.activeInHierarchy)
                return false;

            // Kemiği tutan kumandanın lazeri küre seçimi yapmasın (takılı sarı nokta kalmasını %100 önler)
            if (IsControllerHoldingOrGripping(isLeftHand, ctrlTr))
            {
                if (isLeftHand) _hasSmoothedLeftRay = false;
                else _hasSmoothedRightRay = false;
                return false;
            }

            // Kumandanın görünür lazer çizgisi (LineRenderer) aktif mi kontrol et
            bool hasVisibleLine = false;
            Vector3 lineOrigin = Vector3.zero;
            Vector3 lineTangent = Vector3.forward;

            foreach (LineRenderer lr in ctrlTr.GetComponentsInChildren<LineRenderer>(true))
            {
                if (lr != null && lr.enabled && lr.gameObject.activeInHierarchy && lr.positionCount >= 2 && lr.useWorldSpace)
                {
                    Vector3 p0 = lr.GetPosition(0);
                    Vector3 p1 = lr.GetPosition(1);
                    Vector3 dir = p1 - p0;
                    if (dir.sqrMagnitude > 0.000001f)
                    {
                        hasVisibleLine = true;
                        lineOrigin = p0;
                        lineTangent = dir.normalized;
                        break;
                    }
                }
            }

            if (!hasVisibleLine)
            {
                if (isLeftHand) _hasSmoothedLeftRay = false;
                else _hasSmoothedRightRay = false;
                return false;
            }

            Vector3 rawOrigin = lineOrigin;
            Vector3 rawDir = lineTangent;

            // XRI'nin bükülmemiş, stabilize edilmiş ışın başlangıç transformunu tercih et
            string attachName = isLeftHand ? "Left Controller Stabilized Attach" : "Right Controller Stabilized Attach";
            foreach (Transform t in ctrlTr.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == attachName)
                {
                    rawOrigin = t.position;
                    rawDir = t.forward;
                    break;
                }
            }

            // Mikro el titremesini (tremor) sönümleyen üstel filtre
            float smoothFactor = 1f - Mathf.Exp(-28f * Mathf.Max(0.004f, Time.unscaledDeltaTime));
            if (isLeftHand)
            {
                if (!_hasSmoothedLeftRay)
                {
                    _smoothedLeftOrigin = rawOrigin;
                    _smoothedLeftDir = rawDir.normalized;
                    _hasSmoothedLeftRay = true;
                }
                else
                {
                    _smoothedLeftOrigin = Vector3.Lerp(_smoothedLeftOrigin, rawOrigin, smoothFactor);
                    _smoothedLeftDir = Vector3.Slerp(_smoothedLeftDir, rawDir.normalized, smoothFactor).normalized;
                }
                ray = new Ray(_smoothedLeftOrigin, _smoothedLeftDir);
            }
            else
            {
                if (!_hasSmoothedRightRay)
                {
                    _smoothedRightOrigin = rawOrigin;
                    _smoothedRightDir = rawDir.normalized;
                    _hasSmoothedRightRay = true;
                }
                else
                {
                    _smoothedRightOrigin = Vector3.Lerp(_smoothedRightOrigin, rawOrigin, smoothFactor);
                    _smoothedRightDir = Vector3.Slerp(_smoothedRightDir, rawDir.normalized, smoothFactor).normalized;
                }
                ray = new Ray(_smoothedRightOrigin, _smoothedRightDir);
            }

            return true;
        }

        /// <summary>
        /// Kemiğin çukur (fossa olecrani, fossa subscapularis, fossa supraspinata, fossa infraspinata vb.)
        /// bölgelerinin Convex MeshCollider veya BoxCollider tarafından örtülmesini önlemek için,
        /// Rigidbody'den bağımsız (Non-Convex) gerçek kemik mesh çarpıştırıcısını sadece lazer sorgusu
        /// anında senkronize eder.
        /// </summary>
        private MeshCollider EnsureExactSurfaceColliderSynced()
        {
            if (_meshFilterTransform == null)
            {
                MeshFilter mf = GetComponentInChildren<MeshFilter>(true);
                if (mf != null && mf.sharedMesh != null)
                {
                    _meshFilterTransform = mf.transform;
                    if (_exactRaycasterGO == null)
                    {
                        _exactRaycasterGO = new GameObject($"[ExactSurfaceRaycaster_{gameObject.name}]");
                        _exactRaycasterGO.hideFlags = HideFlags.HideAndDontSave;
                        _exactSurfaceRayCollider = _exactRaycasterGO.AddComponent<MeshCollider>();
                        _exactSurfaceRayCollider.sharedMesh = mf.sharedMesh;
                        _exactSurfaceRayCollider.convex = false;
                        _exactSurfaceRayCollider.enabled = false;
                    }
                }
            }

            if (_exactSurfaceRayCollider != null && _meshFilterTransform != null)
            {
                _exactRaycasterGO.transform.SetPositionAndRotation(_meshFilterTransform.position, _meshFilterTransform.rotation);
                _exactRaycasterGO.transform.localScale = _meshFilterTransform.lossyScale;
            }

            return _exactSurfaceRayCollider;
        }

        /// <summary>
        /// Görsel VR kumanda lazerinin (LineRenderer) Convex MeshCollider yüzünden Fossa (çukur) bölgelerin
        /// üzerinde havada asılı kalmasını ("lazer oraya ulaşmıyor gibi" görünmesini) önler;
        /// lazer bir küreciğin üzerine geldiğinde doğrudan küreciğin merkezine, çukur bölgeye tutulduğunda ise
        /// gerçek içbükey (Non-Convex) kemik yüzeyine kadar uzanmasını sağlar.
        /// </summary>
        private static int _lastLaserExtendFrame = -1;
        private static void ExtendControllerLaserIntoFossaAndPins()
        {
            if (_lastLaserExtendFrame == Time.frameCount)
                return;
            _lastLaserExtendFrame = Time.frameCount;

            ExtendSingleHandLaserVisual(true, _hoveredOwnerLeft, _hoveredPinLeft);
            ExtendSingleHandLaserVisual(false, _hoveredOwnerRight, _hoveredPinRight);
        }

        private static void ExtendSingleHandLaserVisual(bool isLeftHand, BoneAnatomicalLabels hoveredOwner, int hoveredPin)
        {
            var xrOrigin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin == null) return;

            string ctrlName = isLeftHand ? "Left Controller" : "Right Controller";
            Transform ctrlTr = null;
            foreach (Transform t in xrOrigin.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == ctrlName)
                {
                    ctrlTr = t;
                    break;
                }
            }

            if (ctrlTr == null || !ctrlTr.gameObject.activeInHierarchy) return;
            if (IsControllerHoldingOrGripping(isLeftHand, ctrlTr)) return;

            LineRenderer activeLr = null;
            foreach (LineRenderer lr in ctrlTr.GetComponentsInChildren<LineRenderer>(true))
            {
                if (lr != null && lr.enabled && lr.gameObject.activeInHierarchy && lr.positionCount >= 2 && lr.useWorldSpace)
                {
                    activeLr = lr;
                    break;
                }
            }

            if (activeLr == null) return;

            Vector3 p0 = activeLr.GetPosition(0);
            Vector3 targetEndPoint = Vector3.zero;
            bool shouldOverrideEnd = false;

            // 1. Eğer lazer şu an bir pin küreciğinin (fossa vb.) üzerindeyse, görsel lazerin ucunu
            //    tam o sarı küreciğin merkezine değdir:
            if (hoveredOwner != null && hoveredPin >= 0 && hoveredPin < hoveredOwner._visuals.Count)
            {
                var v = hoveredOwner._visuals[hoveredPin];
                if (v != null && v.DotTransform != null)
                {
                    targetEndPoint = v.DotTransform.position;
                    shouldOverrideEnd = true;
                }
            }
            else if (TryGetControllerLaserRay(isLeftHand, out Ray ray))
            {
                // 2. Küre üzerinde değilse ama bir kemiğin çukur (fossa) bölgesine nişan alıyorsa,
                //    görsel lazeri Convex kabukta durdurmak yerine gerçek Non-Convex çukur yüzeyine kadar uzat:
                for (int s = 0; s < ActiveLabelSystems.Count; s++)
                {
                    var sys = ActiveLabelSystems[s];
                    if (sys == null || !sys.gameObject.activeInHierarchy || !sys._labelsVisible) continue;

                    MeshCollider exactMc = sys.EnsureExactSurfaceColliderSynced();
                    if (exactMc != null)
                    {
                        exactMc.enabled = true;
                        bool hitExact = exactMc.Raycast(ray, out RaycastHit hit, 4.0f);
                        exactMc.enabled = false;

                        if (hitExact)
                        {
                            targetEndPoint = hit.point;
                            shouldOverrideEnd = true;
                            break;
                        }
                    }
                }
            }

            if (shouldOverrideEnd)
            {
                int count = activeLr.positionCount;
                for (int i = 0; i < count; i++)
                {
                    float t = count > 1 ? (float)i / (count - 1) : 1f;
                    activeLr.SetPosition(i, Vector3.Lerp(p0, targetEndPoint, t));
                }
            }
        }

        /// <summary>
        /// Bir pin küreciğinin (dotPos) verilen bakış/lazer noktasından (ray.origin) veya kameradan bakıldığında
        /// kemiğin gerçek 3B yüzeyinin arkasında kalıp kalmadığını tespit eder.
        /// Non-Convex gerçek mesh kullanıldığı için fossa olecrani, fossa subscapularis, fossa supraspinata ve
        /// fossa infraspinata gibi derin çukurlardaki tüm noktalar sorunsuz seçilir; yalnızca kemiğin arka
        /// yüzünde kalan noktalar elenir.
        /// </summary>
        private static bool IsPinOccludedBehindBone(
            BoneAnatomicalLabels sys,
            RuntimePinVisual pin,
            Vector3 dotPos,
            Ray laserRay)
        {
            // 1. Gerçek Mesh Yüzey Normali Kontrolü (Backface Culling):
            // Küreciğin bulunduğu kemik yüzeyinin gerçek dış normali hem lazere hem de kameraya sırtını dönmüşse arka yüzeydedir.
            if (pin.LocalOutwardNormal.sqrMagnitude > 0.001f)
            {
                Vector3 worldNormal = sys.transform.TransformDirection(pin.LocalOutwardNormal).normalized;
                Vector3 toLaserOrigin = (laserRay.origin - dotPos).normalized;
                float dotLaser = Vector3.Dot(worldNormal, toLaserOrigin);

                Camera mainCam = Camera.main;
                float dotCam = dotLaser;
                if (mainCam != null)
                {
                    Vector3 toCam = (mainCam.transform.position - dotPos).normalized;
                    dotCam = Vector3.Dot(worldNormal, toCam);
                }

                // Fossa içi eğimli yüzeyleri asla elememek için yalnızca gerçekten arkaya bakan (<-0.28) yüzeyleri ele:
                if (dotLaser < -0.28f && dotCam < -0.28f)
                    return true;
            }

            // 2. Non-Convex Gerçek Kemik Mesh'i ile Doğrudan Görüş Hattı (Line-of-Sight) Kontrolü:
            // Convex BoxCollider/MeshCollider çukurların (fossa) üzerini kapattığı için burada SADECE
            // gerçek içbükey (concave) kemik mesh'ine karşı ışın testi yapılır!
            Vector3 seg = dotPos - laserRay.origin;
            float distToDot = seg.magnitude;
            if (distToDot > 0.04f)
            {
                MeshCollider exactMc = sys.EnsureExactSurfaceColliderSynced();
                if (exactMc != null)
                {
                    Vector3 dir = seg / distToDot;
                    exactMc.enabled = true;
                    bool hitBeforePin = exactMc.Raycast(new Ray(laserRay.origin, dir), out RaycastHit hit, distToDot);
                    exactMc.enabled = false;

                    // Eğer ışın küreciğe ulaşmadan en az 1.5 cm (15 mm) önce gerçek kemik duvarına çarptıysa
                    // ve çarptığı yüzey ile kürecik aynı çukurda değilse, bu kürecik kemiğin arka yüzündedir:
                    if (hitBeforePin && hit.distance < distToDot - 0.015f)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Verilen lazer ışınının:
        /// 1) Kemiği delip arkadaki noktaları seçmeden, Fossa'lar (çukur bölgeler) dahil YALNIZCA kullanıcıya
        ///    bakan ÖN YÜZEYDEKİ noktaları histerezis (yapışkan kilit) ile kararlı biçimde seçmesini sağlar.
        /// 2) Herhangi bir kemiğin veya pin küreciğinin üzerinden/yakınından geçip geçmediğini döndürür
        ///    (böylece lazer kemiği delip arkadaki menüye tıklayamaz).
        /// </summary>
        private static bool EvaluateRayAgainstBonesAndPins(
            Ray ray,
            BoneAnatomicalLabels prevOwner,
            int prevPinIndex,
            ref float lastSwitchTime,
            out BoneAnatomicalLabels hoveredOwner,
            out int hoveredPinIndex,
            out float bestOutScore)
        {
            hoveredOwner = null;
            hoveredPinIndex = -1;
            bestOutScore = float.MaxValue;
            bool blocksMenuBehind = false;

            // A) Kemik Collider kontrolü (Arkadaki menüye lazer geçmesini engellemek için)
            RaycastHit[] hits = Physics.RaycastAll(ray, 5.0f, ~0, QueryTriggerInteraction.Collide);
            if (hits != null && hits.Length > 0)
            {
                for (int i = 0; i < hits.Length; i++)
                {
                    Collider col = hits[i].collider;
                    if (col == null) continue;

                    PinDotTarget pinTarget = col.GetComponent<PinDotTarget>();
                    if (pinTarget != null && pinTarget.Owner != null && pinTarget.Owner._labelsVisible)
                    {
                        blocksMenuBehind = true;
                        continue;
                    }

                    BoneIdentifier boneId = col.GetComponentInParent<BoneIdentifier>();
                    if (boneId != null)
                    {
                        blocksMenuBehind = true;
                    }
                }
            }

            // B) Kararlı (Histerezisli) ve Gerçek Ön Yüzey (Fossa Uyumlu) Küre Seçimi:
            const float enterRadius = 0.026f;
            const float stayRadius  = 0.036f;

            // 1. Aşama: Lazer konisi içindeki görünür (arka yüzde olmayan) küreler arasında en öndekinin derinliğini bul
            float minFrontAlongRay = float.MaxValue;
            for (int s = 0; s < ActiveLabelSystems.Count; s++)
            {
                BoneAnatomicalLabels sys = ActiveLabelSystems[s];
                if (sys == null || !sys.gameObject.activeInHierarchy)
                    continue;

                if (sys._boneIdentifier != null)
                {
                    Bounds b = sys._boneIdentifier.CalculateWorldBounds(sys.transform.position);
                    b.Expand(0.065f);
                    if (b.IntersectRay(ray))
                    {
                        blocksMenuBehind = true;
                    }
                }

                if (!sys._labelsVisible)
                    continue;

                for (int i = 0; i < sys._visuals.Count; i++)
                {
                    RuntimePinVisual v = sys._visuals[i];
                    if (v == null || v.DotTransform == null) continue;

                    Vector3 dotPos = v.DotTransform.position;
                    Vector3 toDot = dotPos - ray.origin;
                    float alongRay = Vector3.Dot(toDot, ray.direction);
                    if (alongRay < 0.05f || alongRay > 4.2f)
                        continue;

                    Vector3 closestPointOnRay = ray.origin + ray.direction * alongRay;
                    float perpDist = Vector3.Distance(dotPos, closestPointOnRay);
                    if (perpDist < 0.042f)
                        blocksMenuBehind = true;

                    bool isCurrentHovered = (sys == prevOwner && i == prevPinIndex);
                    float maxAllowedRadius = isCurrentHovered ? stayRadius : enterRadius;
                    if (perpDist > maxAllowedRadius)
                        continue;

                    if (IsPinOccludedBehindBone(sys, v, dotPos, ray))
                        continue;

                    if (alongRay < minFrontAlongRay)
                        minFrontAlongRay = alongRay;
                }
            }

            // 2. Aşama: Yalnızca ön yüzeydeki (ve fossa içindeki) görünür küreleri puanla
            float bestScore = float.MaxValue;
            BoneAnatomicalLabels bestCandidateOwner = null;
            int bestCandidatePin = -1;
            float prevPinPerpDist = float.MaxValue;
            bool prevPinStillValidOnFront = false;

            for (int s = 0; s < ActiveLabelSystems.Count; s++)
            {
                BoneAnatomicalLabels sys = ActiveLabelSystems[s];
                if (sys == null || !sys.gameObject.activeInHierarchy || !sys._labelsVisible)
                    continue;

                for (int i = 0; i < sys._visuals.Count; i++)
                {
                    RuntimePinVisual v = sys._visuals[i];
                    if (v == null || v.DotTransform == null) continue;

                    Vector3 dotPos = v.DotTransform.position;
                    Vector3 toDot = dotPos - ray.origin;
                    float alongRay = Vector3.Dot(toDot, ray.direction);
                    if (alongRay < 0.05f || alongRay > 4.2f)
                        continue;

                    Vector3 closestPointOnRay = ray.origin + ray.direction * alongRay;
                    float perpDist = Vector3.Distance(dotPos, closestPointOnRay);

                    bool isCurrentHovered = (sys == prevOwner && i == prevPinIndex);
                    float maxAllowedRadius = isCurrentHovered ? stayRadius : enterRadius;
                    if (perpDist > maxAllowedRadius)
                        continue;

                    // Kemiğin gerçek 3B mesh'inin arkasında kalan küreleri kesin olarak reddet!
                    if (IsPinOccludedBehindBone(sys, v, dotPos, ray))
                        continue;

                    // Eğer aynı hizada hem önde hem de çok daha geride (4.5 cm+) bir küre varsa öndekini tercih et:
                    if (minFrontAlongRay < float.MaxValue && alongRay > minFrontAlongRay + 0.045f)
                        continue;

                    if (isCurrentHovered)
                    {
                        prevPinPerpDist = perpDist;
                        prevPinStillValidOnFront = true;
                    }

                    float depthPenalty = (minFrontAlongRay < float.MaxValue)
                        ? Mathf.Max(0f, alongRay - minFrontAlongRay) * 0.25f
                        : 0f;

                    float score = isCurrentHovered
                        ? (perpDist * 0.55f + depthPenalty - 0.0035f)
                        : (perpDist + depthPenalty);

                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestCandidateOwner = sys;
                        bestCandidatePin = i;
                    }
                }
            }

            if (bestCandidateOwner != null &&
                prevOwner != null && prevPinIndex >= 0 &&
                prevPinStillValidOnFront &&
                (bestCandidateOwner != prevOwner || bestCandidatePin != prevPinIndex))
            {
                if (prevPinPerpDist <= stayRadius && (Time.unscaledTime - lastSwitchTime) < 0.12f)
                {
                    hoveredOwner = prevOwner;
                    hoveredPinIndex = prevPinIndex;
                    bestOutScore = prevPinPerpDist;
                    return blocksMenuBehind;
                }

                lastSwitchTime = Time.unscaledTime;
            }

            hoveredOwner = bestCandidateOwner;
            hoveredPinIndex = bestCandidatePin;
            bestOutScore = bestScore;
            return blocksMenuBehind;
        }

        // ─────────────── Pin Küreciği & Kart Aç/Kapat Metodları ───────────────

        /// <summary>
        /// Tetik (Trigger) tuşuyla tıklanan tek bir küreciğin bilgi kartını ve çizgisini açar/kapatır.
        /// Basılı tutmaya gerek yoktur; açık kalır, tekrar tıklanınca kapanır.
        /// </summary>
        public void ToggleSinglePinCard(int pinIndex)
        {
            if (pinIndex < 0 || pinIndex >= _visuals.Count)
                return;

            RuntimePinVisual v = _visuals[pinIndex];
            if (v == null)
                return;

            v.IsExpanded = !v.IsExpanded;

            if (v.Line != null)
                v.Line.gameObject.SetActive(v.IsExpanded);

            if (v.CardTransform != null)
            {
                v.CardTransform.gameObject.SetActive(v.IsExpanded);
                if (v.IsExpanded)
                {
                    v.CurrentWorldLabelPos = transform.TransformPoint(v.Data.labelLocalPos);
                }
            }

            UpdateRuntimeVisualPositions();
        }

        /// <summary>
        /// Blender veya Unity içinde kemik altına "Pin_Caput_humeri" veya doğrudan "Caput humeri" şeklinde
        /// alt obje (Empty) yerleştirildiyse bunları otomatik olarak landmarks listesine aktarır.
        /// </summary>
        public bool SyncFromBlenderChildPinsIfPresent(bool replaceExistingIfFound = false)
        {
            var detected = new List<AnatomicalLandmark>();
            MeshFilter mf = GetComponentInChildren<MeshFilter>(true);
            Transform meshTr = mf != null ? mf.transform : transform;
            Vector3 meshCenterLocal = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.bounds.center : Vector3.zero;
            float meshExtent = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.bounds.extents.magnitude : 1.0f;
            float outOffset = Mathf.Max(meshExtent * 0.30f, 0.32f);

            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child == transform || child == meshTr) continue;
                if (child.name.StartsWith("[Holographic", StringComparison.OrdinalIgnoreCase)) continue;
                if (child.GetComponentInParent<Canvas>() != null) continue;

                bool hasPrefix = child.name.StartsWith("Pin_", StringComparison.OrdinalIgnoreCase);
                bool isBlenderEmpty = child.GetComponent<MeshRenderer>() == null &&
                                      child.GetComponent<MeshFilter>() == null &&
                                      child.GetComponent<LineRenderer>() == null &&
                                      child.GetComponent<Canvas>() == null &&
                                      child.childCount == 0;

                if (!hasPrefix && !isBlenderEmpty)
                    continue;

                string rawName = hasPrefix ? child.name.Substring(4) : child.name;
                rawName = rawName.Replace("_", " ").Trim();
                if (string.IsNullOrEmpty(rawName)) continue;

                // Blender varsayılan sahne objelerini (Camera, Light, Lamp, kemik kök isimleri vb.) filtrele
                string lower = rawName.ToLowerInvariant();
                if (!hasPrefix && (lower == "camera" || lower == "light" || lower == "lamp" || lower == "armature" ||
                                   lower == "surface" || lower == "default" || lower == "empty" || lower == "cube" ||
                                   lower == "sphere" || lower == "icosphere" || lower == "cylinder" || lower == "plane" ||
                                   lower == "rootnode" || lower == "scene" || lower == "collection" ||
                                   lower == "humerus" || lower == "radius" || lower == "scapula" || lower == "ulna" ||
                                   lower == "dog humerus" || lower == "dog radius" || lower == "dog scapula" || lower == "dog ulna"))
                    continue;

                // Pin noktasını doğrudan kemik mesh'inin yerel koordinat uzayına (meshTr) çevir
                Vector3 localAnchor = meshTr.InverseTransformPoint(child.position);
                Vector3 radialOut = (localAnchor - meshCenterLocal);
                if (radialOut.sqrMagnitude < 0.0001f) radialOut = Vector3.right;
                radialOut.Normalize();

                detected.Add(new AnatomicalLandmark
                {
                    labelText = rawName,
                    anchorLocalPos = localAnchor,
                    labelLocalPos = localAnchor + radialOut * outOffset
                });
            }

            if (detected.Count > 0)
            {
                if (replaceExistingIfFound || landmarks == null || landmarks.Count == 0)
                {
                    landmarks = detected;
                }
                else
                {
                    foreach (var d in detected)
                    {
                        int idx = landmarks.FindIndex(l => string.Equals(l.labelText, d.labelText, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0)
                            landmarks[idx] = d;
                        else
                            landmarks.Add(d);
                    }
                }
                return true;
            }

            return false;
        }

        public void ToggleLabels()
        {
            SetLabelsVisible(!_labelsVisible);
        }

        public void SetLabelsVisible(bool visible)
        {
            if (visible && _visuals.Count == 0 && Application.isPlaying)
            {
                BuildRuntimeVisuals();
            }

            _labelsVisible = visible;
            if (_runtimeContainer != null)
            {
                _runtimeContainer.SetActive(visible);
            }

            // Pin modu ilk açıldığında yalnızca kürecikler görünsün, kartlar kapalı başlasın (Seçenek 1)
            if (visible)
            {
                for (int i = 0; i < _visuals.Count; i++)
                {
                    RuntimePinVisual v = _visuals[i];
                    if (v == null) continue;
                    v.IsExpanded = false;
                    v.IsHovered = false;
                    if (v.Line != null) v.Line.gameObject.SetActive(false);
                    if (v.CardTransform != null) v.CardTransform.gameObject.SetActive(false);
                }
                UpdateRuntimeVisualPositions();
            }
        }

        public bool AreLabelsVisible => _labelsVisible;

        public void RebuildVisualsNow()
        {
            if (_runtimeContainer != null)
            {
                if (Application.isPlaying)
                    Destroy(_runtimeContainer);
                else
                    DestroyImmediate(_runtimeContainer);
                _runtimeContainer = null;
            }
            _visuals.Clear();

            if (Application.isPlaying)
            {
                BuildRuntimeVisuals();
                if (_runtimeContainer != null)
                    _runtimeContainer.SetActive(_labelsVisible);
            }
        }

        // ─────────────── VR Kumanda (X / A Tuşu) & PC Giriş Kontrolü ───────────────

        private static bool CheckGlobalToggleInputPressed()
        {
            // 1. PC Klavye: [L] veya [X] tuşu
            var kb = Keyboard.current;
            if (kb != null && (kb.lKey.wasPressedThisFrame || kb.xKey.wasPressedThisFrame))
            {
                return true;
            }

            // 2. Meta Quest 3S / OpenXR Kumanda:
            // Sol Kumanda [X] (primaryButton) veya Sağ Kumanda [A] (primaryButton)
            // (Not: Y ve B tuşları kemiği havada sabitlemek ve döndürmek için ayrılmıştır!)
            bool vrPrimaryNow = false;

            var leftHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            if (leftHand.isValid)
            {
                if (leftHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool leftX) && leftX)
                    vrPrimaryNow = true;
            }

            var rightHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (rightHand.isValid)
            {
                if (rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool rightA) && rightA)
                    vrPrimaryNow = true;
            }

            bool justPressed = vrPrimaryNow && !_prevVrPrimaryStateGlobal;
            _prevVrPrimaryStateGlobal = vrPrimaryNow;
            return justPressed;
        }

        // ─────────────── 3B Holografik Pin, Çizgi ve Kart Oluşturma ───────────────

        private void BuildRuntimeVisuals()
        {
            if (_runtimeContainer != null)
                Destroy(_runtimeContainer);

            _visuals.Clear();
            if (landmarks == null || landmarks.Count == 0)
                return;

            _runtimeContainer = new GameObject("[Holographic_Anatomical_Labels]");
            _runtimeContainer.transform.SetParent(transform, false);
            _runtimeContainer.transform.localPosition = Vector3.zero;
            _runtimeContainer.transform.localRotation = Quaternion.identity;
            _runtimeContainer.transform.localScale = Vector3.one;

            Shader unlitShader = Shader.Find("AnatomiVR/BakedUnlit")
                              ?? Shader.Find("Universal Render Pipeline/Unlit")
                              ?? Shader.Find("Unlit/Color");

            Material lineMat = new Material(unlitShader);
            lineMat.color = hologramCyan;
            if (lineMat.HasProperty("_BaseColor")) lineMat.SetColor("_BaseColor", hologramCyan);
            if (lineMat.HasProperty("_ShadingStrength")) lineMat.SetFloat("_ShadingStrength", 0f);
            lineMat.enableInstancing = true;

            TMP_FontAsset defaultFont = TMP_Settings.defaultFontAsset;
            float parentScale = Mathf.Max(0.01f, transform.lossyScale.x);

            for (int i = 0; i < landmarks.Count; i++)
            {
                AnatomicalLandmark lm = landmarks[i];
                if (lm == null || string.IsNullOrEmpty(lm.labelText))
                    continue;

                var pinRoot = new GameObject($"Pin_{i}_{lm.labelText}");
                pinRoot.transform.SetParent(_runtimeContainer.transform, false);

                // 1. Kemik yüzeyindeki tıklanabilir holografik küre (SurfaceDot)
                GameObject dotGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dotGO.name = "SurfaceDot";
                dotGO.transform.SetParent(pinRoot.transform, false);
                dotGO.transform.localPosition = lm.anchorLocalPos;

                float localDotScale = pinDotSize / parentScale;
                dotGO.transform.localScale = Vector3.one * localDotScale;

                // Küreciğin üzerindeki fiziksel SphereCollider'ı kaldırıyoruz:
                // Çünkü XRI NearFarInteractor fiziksel collider'a çarptığında lazer çizgisini (CurveVisualController)
                // büküp zıplatıyor ve sarı-mavi titremeye yol açıyordu. Küre seçimi artık EvaluateRayAgainstBonesAndPins
                // içinde saf 3B matematiksel histerezis ile %100 kararlı yapılıyor.
                SphereCollider dotCol = dotGO.GetComponent<SphereCollider>();
                if (dotCol != null)
                {
                    if (Application.isPlaying)
                        Destroy(dotCol);
                    else
                        DestroyImmediate(dotCol);
                    dotCol = null;
                }

                PinDotTarget dotTarget = dotGO.AddComponent<PinDotTarget>();
                dotTarget.Owner = this;
                dotTarget.PinIndex = _visuals.Count;

                Material dotMat = new Material(unlitShader);
                dotMat.color = hologramCyan;
                if (dotMat.HasProperty("_BaseColor")) dotMat.SetColor("_BaseColor", hologramCyan);
                if (dotMat.HasProperty("_ShadingStrength")) dotMat.SetFloat("_ShadingStrength", 0.25f);

                MeshRenderer dotMr = dotGO.GetComponent<MeshRenderer>();
                if (dotMr != null)
                {
                    dotMr.sharedMaterial = dotMat;
                    dotMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    dotMr.receiveShadows = false;
                }

                // 2. İnce Holografik Çizgi (LineRenderer) — Başlangıçta kapalı, küreye tıklanınca açılır
                var lineGO = new GameObject("LeaderLine", typeof(LineRenderer));
                lineGO.transform.SetParent(pinRoot.transform, false);
                LineRenderer lr = lineGO.GetComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.startWidth = lineWidth;
                lr.endWidth = lineWidth * 0.65f;
                lr.sharedMaterial = lineMat;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.numCapVertices = 2;
                lineGO.SetActive(false);

                // 3. 3B Holografik Etiket Kartı (World-Space Canvas) — Başlangıçta kapalı, küreye tıklanınca açılır
                var cardGO = new GameObject("LabelCard", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                cardGO.transform.SetParent(pinRoot.transform, false);
                cardGO.transform.localPosition = lm.labelLocalPos;

                Canvas c = cardGO.GetComponent<Canvas>();
                c.renderMode = RenderMode.WorldSpace;

                RectTransform cardRt = cardGO.GetComponent<RectTransform>();
                float widthPx = Mathf.Max(220f, lm.labelText.Length * 16f + 48f);
                cardRt.sizeDelta = new Vector2(widthPx, 58f);
                float localCardScale = labelWorldScale / parentScale;
                cardRt.localScale = Vector3.one * localCardScale;

                var bgGO = new GameObject("Bg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
                bgGO.transform.SetParent(cardGO.transform, false);
                RectTransform bgRt = bgGO.GetComponent<RectTransform>();
                bgRt.anchorMin = Vector2.zero;
                bgRt.anchorMax = Vector2.one;
                bgRt.sizeDelta = Vector2.zero;
                bgGO.GetComponent<Image>().color = cardBgColor;

                Outline outline = bgGO.GetComponent<Outline>();
                outline.effectColor = hologramCyan;
                outline.effectDistance = new Vector2(2f, 2f);

                // Sol vurgu şeridi
                var barGO = new GameObject("Accent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                barGO.transform.SetParent(bgGO.transform, false);
                RectTransform barRt = barGO.GetComponent<RectTransform>();
                barRt.anchorMin = new Vector2(0f, 0f);
                barRt.anchorMax = new Vector2(0f, 1f);
                barRt.pivot = new Vector2(0f, 0.5f);
                barRt.anchoredPosition = Vector2.zero;
                barRt.sizeDelta = new Vector2(6f, 0f);
                barGO.GetComponent<Image>().color = hologramCyan;

                var txtGO = new GameObject("LabelText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                txtGO.transform.SetParent(bgGO.transform, false);
                RectTransform txtRt = txtGO.GetComponent<RectTransform>();
                txtRt.anchorMin = Vector2.zero;
                txtRt.anchorMax = Vector2.one;
                txtRt.offsetMin = new Vector2(14f, 4f);
                txtRt.offsetMax = new Vector2(-10f, -4f);

                TextMeshProUGUI tmp = txtGO.GetComponent<TextMeshProUGUI>();
                if (defaultFont != null) tmp.font = defaultFont;
                tmp.text = lm.labelText;
                tmp.fontSize = 24f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.characterSpacing = 1.5f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = new Color(0.90f, 0.98f, 1.00f, 1.00f);
                tmp.raycastTarget = false;

                cardGO.SetActive(false);

                Vector3 outwardNormal = lm.prebakedOutwardNormal.sqrMagnitude > 0.0001f
                    ? lm.prebakedOutwardNormal
                    : ComputePinLocalOutwardNormal(lm.anchorLocalPos);

                _visuals.Add(new RuntimePinVisual
                {
                    Index = _visuals.Count,
                    Data = lm,
                    DotTransform = dotGO.transform,
                    DotMaterial = dotMat,
                    Line = lr,
                    CardTransform = cardGO.transform,
                    BaseLocalDotScale = localDotScale,
                    LocalOutwardNormal = outwardNormal,
                    IsExpanded = false,
                    IsHovered = false,
                    CurrentWorldLabelPos = transform.TransformPoint(lm.labelLocalPos)
                });
            }
        }

        /// <summary>
        /// Editör kurulumu / APK derlemesi sırasında kemik üzerindeki tüm pinlerin gerçek dış yüzey
        /// normallerini tek seferde (mesh dizilerini yalnızca 1 kez okuyarak) hesaplar ve prefab içine
        /// kaydeder (Pre-Baked Normals). Böylece çalışma zamanında (Quest 3S'te) sıfır vertex taraması yapılır!
        /// </summary>
        public void PrebakeAllPinNormals()
        {
            if (landmarks == null || landmarks.Count == 0)
                return;

            MeshFilter mf = GetComponentInChildren<MeshFilter>(true);
            if (mf == null || mf.sharedMesh == null || !mf.sharedMesh.isReadable)
                return;

            Mesh m = mf.sharedMesh;
            Vector3[] verts = m.vertices;
            Vector3[] norms = m.normals;
            if (verts == null || norms == null || verts.Length != norms.Length || verts.Length == 0)
                return;

            for (int p = 0; p < landmarks.Count; p++)
            {
                AnatomicalLandmark lm = landmarks[p];
                if (lm == null) continue;

                Vector3 anchorLocal = lm.anchorLocalPos;
                float bestDistSq = float.MaxValue;
                int closestIdx = 0;

                for (int i = 0; i < verts.Length; i++)
                {
                    float dSq = (verts[i] - anchorLocal).sqrMagnitude;
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        closestIdx = i;
                    }
                }

                Vector3 primaryNorm = norms[closestIdx].normalized;
                Vector3 avgNorm = primaryNorm;

                float neighborRadiusSq = bestDistSq + 0.0003f;
                for (int i = 0; i < verts.Length; i++)
                {
                    float dSq = (verts[i] - anchorLocal).sqrMagnitude;
                    if (dSq <= neighborRadiusSq && Vector3.Dot(norms[i], primaryNorm) > 0.5f)
                    {
                        avgNorm += norms[i];
                    }
                }

                lm.prebakedOutwardNormal = avgNorm.sqrMagnitude > 0.0001f ? avgNorm.normalized : Vector3.up;
            }
        }

        /// <summary>
        /// Pin küreciğinin kemik mesh yüzeyindeki gerçek yerel dış normalini (outward normal) hesaplar.
        /// ÖNEMLİ: Fossa (çukur) ve ince Scapula/Humerus duvarlarında arka yüzeyin normaliyle karışmaması ve
        /// çukur içindeki normalin ters çevrilmemesi için yalnızca en yakın tepe noktasının (ve onunla aynı
        /// yöne bakan komşularının) gerçek mesh normali alınır.
        /// </summary>
        private Vector3 ComputePinLocalOutwardNormal(Vector3 anchorLocal)
        {
            MeshFilter mf = GetComponentInChildren<MeshFilter>(true);
            if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable)
            {
                Mesh m = mf.sharedMesh;
                Vector3[] verts = m.vertices;
                Vector3[] norms = m.normals;
                if (verts != null && norms != null && verts.Length == norms.Length && verts.Length > 0)
                {
                    float bestDistSq = float.MaxValue;
                    int closestIdx = 0;

                    for (int i = 0; i < verts.Length; i++)
                    {
                        float dSq = (verts[i] - anchorLocal).sqrMagnitude;
                        if (dSq < bestDistSq)
                        {
                            bestDistSq = dSq;
                            closestIdx = i;
                        }
                    }

                    Vector3 primaryNorm = norms[closestIdx].normalized;
                    Vector3 avgNorm = primaryNorm;

                    // Sadece en yakın noktanın çok yakınındaki (3 mm) ve aynı yüzeye bakan (Dot > 0.5) normalleri ortala:
                    float neighborRadiusSq = bestDistSq + 0.0003f;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        float dSq = (verts[i] - anchorLocal).sqrMagnitude;
                        if (dSq <= neighborRadiusSq && Vector3.Dot(norms[i], primaryNorm) > 0.5f)
                        {
                            avgNorm += norms[i];
                        }
                    }

                    if (avgNorm.sqrMagnitude > 0.0001f)
                        return avgNorm.normalized;
                }
            }

            return Vector3.zero;
        }

        /// <summary>
        /// Açılan bilgi kartının (örn: "fossa infraspinata") kemiğin içine girmesini veya kemiğin arkasında
        /// kalmasını %100 engelleyerek, kartı her zaman kullanıcıya (Main Camera) göre kemiğin ÖNÜNDE ve
        /// net okunabilir bir konumda tutar.
        /// </summary>
        private Vector3 ComputeCameraFacingReadableCardPos(RuntimePinVisual v)
        {
            Vector3 worldAnchor = transform.TransformPoint(v.Data.anchorLocalPos);
            if (_mainCam == null)
                _mainCam = Camera.main;
            if (_mainCam == null)
                return transform.TransformPoint(v.Data.labelLocalPos);

            Vector3 camPos = _mainCam.transform.position;
            Vector3 camRight = _mainCam.transform.right;
            Vector3 camUp = _mainCam.transform.up;

            Bounds boneBounds = _boneIdentifier != null
                ? _boneIdentifier.CalculateWorldBounds(transform.position)
                : new Bounds(transform.position, Vector3.one * 0.22f);

            Vector3 boneCenter = boneBounds.center;
            Vector3 fromCenter = worldAnchor - boneCenter;
            float relX = Vector3.Dot(fromCenter, camRight);
            float relY = Vector3.Dot(fromCenter, camUp);

            // Kartın kemiğin alt gövdesine veya masaya gömülmemesi için ekran düzleminde yukarı ve dışa yönlendir:
            float dirX = relX;
            if (Mathf.Abs(dirX) < 0.025f)
                dirX = ((v.Index % 2 == 0) ? 1f : -1f) * 0.045f;

            float dirY = Mathf.Max(0.055f, relY + 0.045f);
            Vector2 screenDir = new Vector2(dirX, dirY).normalized;

            Vector3 planeOffset = (camRight * screenDir.x + camUp * screenDir.y) * 0.095f;
            Vector3 toCam = (camPos - worldAnchor);
            float distCamToAnchor = toCam.magnitude;
            Vector3 toCamDir = distCamToAnchor > 0.001f ? (toCam / distCamToAnchor) : -_mainCam.transform.forward;

            // Başlangıç adayı: Küreden kullanıcıya doğru 7.5 cm önde + ekran düzleminde yukarı/dışa
            Vector3 candidatePos = worldAnchor + planeOffset + toCamDir * 0.075f;

            // Kemiğin ön yüzeyini kontrol et (Kartın merkezi, sol ucu ve sağ ucu kameradan bakıldığında
            // kemiğin hiçbir noktasının arkasında veya içinde kalmasın; en az 5.5 cm önünde dursun!)
            candidatePos = PullCardInFrontOfBoneTowardsCamera(candidatePos, camPos, camRight);
            return candidatePos;
        }

        private Vector3 PullCardInFrontOfBoneTowardsCamera(Vector3 cardPos, Vector3 camPos, Vector3 camRight)
        {
            Vector3 camToCard = cardPos - camPos;
            float distCamToCard = camToCard.magnitude;
            if (distCamToCard < 0.12f)
                return cardPos;

            Vector3 dirCamToCard = camToCard / distCamToCard;
            float minBoneDistFromCam = float.MaxValue;

            // Kartın orta, sol ve sağ kenar hizalarından kemiğe olan en yakın ön yüzey mesafesini ölç
            const float halfCardWidthWorld = 0.062f;
            for (int sample = -1; sample <= 1; sample++)
            {
                Vector3 targetPt = cardPos + camRight * (sample * halfCardWidthWorld);
                Vector3 rayVec = targetPt - camPos;
                float rayLen = rayVec.magnitude;
                if (rayLen < 0.05f) continue;

                RaycastHit[] hits = Physics.SphereCastAll(camPos, 0.022f, rayVec / rayLen, rayLen + 0.25f, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hits.Length; i++)
                {
                    Collider col = hits[i].collider;
                    if (col != null && col.transform.IsChildOf(transform) && hits[i].distance > 0.04f)
                    {
                        if (hits[i].distance < minBoneDistFromCam)
                            minBoneDistFromCam = hits[i].distance;
                    }
                }
            }

            // Eğer kemik yüzeyi kartın önünde veya kartın 5.5 cm yakınındaysa, kartı kameraya doğru kemiğin önüne çek:
            const float frontClearance = 0.055f;
            if (minBoneDistFromCam < float.MaxValue && distCamToCard > minBoneDistFromCam - frontClearance)
            {
                float safeDist = Mathf.Max(0.18f, minBoneDistFromCam - frontClearance);
                cardPos = camPos + dirCamToCard * safeDist;
            }

            return cardPos;
        }

        private void UpdateRuntimeVisualPositions()
        {
            if (_mainCam == null)
                _mainCam = Camera.main;

            // 1. Küre renklerini, ölçeklerini ve kameraya göre kemiğin ÖNÜNDEKİ etiket pozisyonlarını güncelle
            var openPins = new List<RuntimePinVisual>();

            for (int i = 0; i < _visuals.Count; i++)
            {
                RuntimePinVisual v = _visuals[i];
                if (v == null || v.Data == null) continue;

                if (v.DotTransform != null)
                {
                    v.DotTransform.localPosition = v.Data.anchorLocalPos;
                    float scaleMult = v.IsHovered ? 1.42f : (v.IsExpanded ? 1.22f : 1.0f);
                    v.DotTransform.localScale = Vector3.one * (v.BaseLocalDotScale * scaleMult);
                }

                if (v.DotMaterial != null)
                {
                    Color targetCol = v.IsHovered
                        ? hoverDotColor
                        : (v.IsExpanded ? expandedDotColor : hologramCyan);

                    v.DotMaterial.color = targetCol;
                    if (v.DotMaterial.HasProperty("_BaseColor"))
                        v.DotMaterial.SetColor("_BaseColor", targetCol);
                }

                if (v.IsExpanded)
                {
                    v.CurrentWorldLabelPos = ComputeCameraFacingReadableCardPos(v);
                    openPins.Add(v);
                }
            }

            // 2. Eğer kullanıcı aynı anda birden fazla küreyi açtıysa, açık olan kartların
            //    birbirinin üstüne binmesini önleyen dinamik itme (Anti-Overlap Repulsion) uygula:
            if (openPins.Count > 1 && _mainCam != null)
            {
                Vector3 camRight = _mainCam.transform.right;
                Vector3 camUp    = _mainCam.transform.up;
                const float minHorizDist = 0.130f;
                const float minVertDist  = 0.038f;

                for (int iter = 0; iter < 6; iter++)
                {
                    for (int a = 0; a < openPins.Count; a++)
                    {
                        for (int b = a + 1; b < openPins.Count; b++)
                        {
                            Vector3 diff = openPins[b].CurrentWorldLabelPos - openPins[a].CurrentWorldLabelPos;
                            float dx = Vector3.Dot(diff, camRight);
                            float dy = Vector3.Dot(diff, camUp);

                            if (Mathf.Abs(dx) < minHorizDist && Mathf.Abs(dy) < minVertDist)
                            {
                                float pushY = (minVertDist - Mathf.Abs(dy)) * 0.55f;
                                float signY = dy >= 0f ? 1f : -1f;
                                if (Mathf.Abs(dy) < 0.001f) signY = (b > a) ? 1f : -1f;

                                Vector3 offset = camUp * (pushY * signY);
                                openPins[a].CurrentWorldLabelPos -= offset;
                                openPins[b].CurrentWorldLabelPos += offset;
                            }
                        }
                    }
                }

                // İtme sonrasında da tüm kartların kemiğin önünde kaldığından emin ol
                Vector3 camPos = _mainCam.transform.position;
                for (int i = 0; i < openPins.Count; i++)
                {
                    openPins[i].CurrentWorldLabelPos = PullCardInFrontOfBoneTowardsCamera(
                        openPins[i].CurrentWorldLabelPos, camPos, camRight);
                }
            }

            // 3. Açık olan kartların ve çizgilerin dünya konumlarını ve kameraya bakışını (Billboard) uygula
            for (int i = 0; i < openPins.Count; i++)
            {
                RuntimePinVisual v = openPins[i];
                Vector3 worldAnchor = transform.TransformPoint(v.Data.anchorLocalPos);
                Vector3 worldLabel  = v.CurrentWorldLabelPos;

                if (v.CardTransform != null)
                {
                    v.CardTransform.position = worldLabel;
                    if (_mainCam != null)
                    {
                        Vector3 lookDir = v.CardTransform.position - _mainCam.transform.position;
                        if (lookDir.sqrMagnitude > 0.0001f)
                        {
                            v.CardTransform.rotation = Quaternion.LookRotation(lookDir.normalized, _mainCam.transform.up);
                        }
                    }
                }

                if (v.Line != null)
                {
                    v.Line.SetPosition(0, worldAnchor);
                    v.Line.SetPosition(1, worldLabel);
                }
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!showGizmosInEditor || landmarks == null)
                return;

            for (int i = 0; i < landmarks.Count; i++)
            {
                AnatomicalLandmark lm = landmarks[i];
                if (lm == null) continue;

                Vector3 worldAnchor = transform.TransformPoint(lm.anchorLocalPos);
                Vector3 worldLabel  = transform.TransformPoint(lm.labelLocalPos);

                Gizmos.color = hologramCyan;
                Gizmos.DrawSphere(worldAnchor, 0.0045f);
                Gizmos.DrawLine(worldAnchor, worldLabel);

                Gizmos.color = new Color(0.0f, 0.85f, 1.0f, 0.35f);
                Gizmos.DrawWireCube(worldLabel, new Vector3(0.06f, 0.02f, 0.005f));

                UnityEditor.Handles.color = hologramCyan;
                var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel)
                {
                    normal = { textColor = new Color(0.25f, 0.95f, 1.0f) },
                    fontSize = 11
                };
                UnityEditor.Handles.Label(worldLabel + Vector3.up * 0.012f, $"[{i + 1}] {lm.labelText}", style);
            }
        }
#endif
    }
}
