using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using AnatomiVR.Bones;
using AnatomiVR.UI;

namespace AnatomiVR.XR
{
    /// <summary>
    /// Hem PC / Unity Editör test kontrollerini (fareyle bakma, WASD yürüme, kemik tutma/döndürme),
    /// hem de Meta Quest VR gözlükte (OpenXR) zemin kilidi, oda sınırı koruma ve otomatik masa/ekran
    /// hizalamasını (VR Rig Stabilizer & Auto-Recenter) yöneten kontrolcü.
    /// </summary>
    public class DesktopTestController : MonoBehaviour
    {
        [Header("Kamera & Hareket Ayarları")]
        [SerializeField] private float mouseLookSensitivity = 0.18f;
        [SerializeField] private float keyLookSpeed = 75f;
        [SerializeField] private float moveSpeed = 1.8f;
        [SerializeField] private float playerRadius = 0.34f;
        [SerializeField] private float minPitch = -80f;
        [SerializeField] private float maxPitch = 80f;

        [Header("Kemik Tutma & İnceleme Ayarları")]
        [SerializeField] private float maxGrabDistance = 6.0f;
        [SerializeField] private float minHoldDistance = 0.28f;
        [SerializeField] private float maxHoldDistance = 1.60f;
        [SerializeField] private float followSharpness = 18f;
        [SerializeField] private float rotateSensitivity = 0.45f;

        private Transform _rigRoot;
        private Transform _cameraOffset;
        private Camera _mainCam;

        private float _yaw;
        private float _pitch;
        private float _standingOffsetY = 1.38f;
        private bool _isCrouching;

        // VR Stabilizasyon ve Otomatik Hizalama Durumu
        private bool _vrInitialRecenterDone;
        private bool _vrDelayedRecenterDone;
        private float _vrStartTime;

        // Tutulan kemik durumu
        private Rigidbody _heldRigidbody;
        private string _heldBoneName;
        private float _holdDistance = 0.65f;
        private Quaternion _heldRotationOffset = Quaternion.identity;
        private float _grabStartTime;
        private bool _stickyGrab;
        private bool _showHelp = true;

        // Fare altındaki kemik vurgusu (Hover)
        private string _hoveredBoneName;

        private static readonly Vector3 DefaultRigPos = new Vector3(-1.35f, 0.282f, 0.0f);
        private const float DefaultRigYaw = 90f;
        private const float DefaultCamPitch = 6f;

        // Sahnedeki sabit engellerin (lab_tasarimi.fbx) dünya koordinat sınırları (MinX, MaxX, MinZ, MaxZ)
        // 1. Ortadaki Diseksiyon Masası ('masa'): x=[-0.443, 0.441], z=[-1.115, 1.115]
        private static readonly Vector4 DissectionTableRect = new Vector4(-0.45f, 0.45f, -1.12f, 1.12f);
        // 2. Arka Duvardaki Tam Köpek İskeletli Masa ('iskeletli masa'): x=[-2.695, -1.995], z=[-0.650, 0.650]
        private static readonly Vector4 SkeletonTableRect   = new Vector4(-2.70f, -1.99f, -0.66f, 0.66f);

        private static bool IsVRModeActive()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return true;
#else
            return Application.isMobilePlatform || XRSettings.isDeviceActive;
#endif
        }

        private void Start()
        {
            _vrStartTime = Time.time;
            EnsureSkeletonTablePhysicsBarrier();
            EnsureThickFloorCollider();
            InitializeCameraReferences();
            DisableXRGravityProviders();

            if (IsVRModeActive())
            {
                RecenterVROriginToTable();
            }
        }

        private void InitializeCameraReferences()
        {
            _mainCam = Camera.main;
            if (_mainCam == null)
                _mainCam = FindAnyObjectByType<Camera>();
            if (_mainCam == null)
                return;

            var xrOrigin = FindAnyObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null)
            {
                _rigRoot = xrOrigin.transform;
                _cameraOffset = xrOrigin.CameraFloorOffsetObject != null
                    ? xrOrigin.CameraFloorOffsetObject.transform
                    : _mainCam.transform.parent;
            }
            else
            {
                _rigRoot = _mainCam.transform.root;
                _cameraOffset = _mainCam.transform.parent;
            }

            if (_cameraOffset != null)
                _standingOffsetY = _cameraOffset.localPosition.y > 0.2f ? _cameraOffset.localPosition.y : 1.38f;

            _yaw = _rigRoot != null ? _rigRoot.eulerAngles.y : DefaultRigYaw;
            _pitch = _mainCam.transform.localEulerAngles.x;
            if (_pitch > 180f) _pitch -= 360f;

            var poseDriver = _mainCam.GetComponent<TrackedPoseDriver>();
            if (poseDriver != null)
            {
                poseDriver.enabled = IsVRModeActive();
            }
        }

        /// <summary>
        /// XR Origin (XR Rig) prefab'ı içindeki GravityProvider bileşeninin oyuncuyu zeminden aşağı
        /// boşluğa düşürmesini (90 m/sn) kesin olarak engeller.
        /// </summary>
        private void DisableXRGravityProviders()
        {
            if (_rigRoot == null) return;

            foreach (MonoBehaviour mb in _rigRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                string typeName = mb.GetType().Name;
                if (typeName == "GravityProvider" ||
                    typeName == "DynamicMoveProvider" ||
                    typeName == "ContinuousMoveProviderBase" ||
                    typeName == "ActionBasedContinuousMoveProvider" ||
                    typeName == "ActionBasedSnapTurnProvider" ||
                    typeName == "ActionBasedContinuousTurnProvider" ||
                    typeName == "SnapTurnProvider" ||
                    typeName == "ContinuousTurnProvider")
                {
                    mb.enabled = false;
                    if (mb.gameObject.name == "Gravity")
                        mb.gameObject.SetActive(false);
                }
                else if (typeName == "InteractionAttachController")
                {
                    // Sağ/Sol Joystick ile eldeki kemiği döndürürken XRI'nin varsayılan ileri/geri
                    // itme-çekme (manipulation) girişinin kemiği elden uzaklaştırmasını engelle:
                    var prop = mb.GetType().GetProperty("useManipulationInput",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (prop != null && prop.CanWrite && prop.PropertyType == typeof(bool))
                    {
                        prop.SetValue(mb, false);
                    }
                    var field = mb.GetType().GetField("m_UseManipulationInput",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null && field.FieldType == typeof(bool))
                    {
                        field.SetValue(mb, false);
                    }
                }
            }

            var charCtrl = _rigRoot.GetComponent<CharacterController>();
            if (charCtrl != null)
            {
                charCtrl.enabled = false;
            }
        }

        /// <summary>
        /// Zemin çarpıştırıcısının (Floor_Collider) çok ince (5 cm) olup kemiklerin veya oyuncunun
        /// içinden geçmesini önlemek için kalınlığını 2 metreye çıkarır (üst yüzey y = 0.282m).
        /// </summary>
        private static void EnsureThickFloorCollider()
        {
            GameObject floorGO = GameObject.Find("Floor_Collider");
            if (floorGO == null)
            {
                GameObject env = GameObject.Find("[Environment]");
                floorGO = new GameObject("Floor_Collider");
                if (env != null)
                    floorGO.transform.SetParent(env.transform, false);
            }

            floorGO.transform.position = new Vector3(-0.5f, 0.282f - 1.0f, 0f);
            floorGO.transform.rotation = Quaternion.identity;
            floorGO.transform.localScale = Vector3.one;

            BoxCollider box = floorGO.GetComponent<BoxCollider>();
            if (box == null)
                box = floorGO.AddComponent<BoxCollider>();

            box.center = Vector3.zero;
            box.size = new Vector3(12.0f, 2.0f, 12.0f);
        }

        /// <summary>
        /// Yüksek poligonlu (135k tri) köpek iskeleti objesinin kendisine dokunmadan,
        /// [Environment] altında bağımsız 1 adet hafif BoxCollider oluşturur; böylece
        /// fırlatılan kemikler de iskeletli masanın içinden geçmez.
        /// </summary>
        private static void EnsureSkeletonTablePhysicsBarrier()
        {
            if (GameObject.Find("SkeletonTable_Collider") != null)
                return;

            GameObject env = GameObject.Find("[Environment]");
            var barrier = new GameObject("SkeletonTable_Collider");
            if (env != null)
                barrier.transform.SetParent(env.transform, false);

            barrier.transform.position = new Vector3(-2.345f, 0.965f, 0.000f);
            var box = barrier.AddComponent<BoxCollider>();
            box.size = new Vector3(0.700f, 1.575f, 1.300f);
        }

        private void Update()
        {
            if (_mainCam == null)
            {
                InitializeCameraReferences();
                if (_mainCam == null) return;
            }

            if (IsVRModeActive())
            {
                var poseDriver = _mainCam.GetComponent<TrackedPoseDriver>();
                if (poseDriver != null && !poseDriver.enabled)
                    poseDriver.enabled = true;

                return;
            }

            HandleShortcuts();
            HandleCameraLook();
            HandleMovement();
            UpdateHoverDetection();
            HandleBoneGrabAndInspect();
        }

        private void LateUpdate()
        {
            if (!IsVRModeActive())
                return;

            if (_mainCam == null || _rigRoot == null)
            {
                InitializeCameraReferences();
                if (_mainCam == null || _rigRoot == null) return;
            }

            UpdateVRStabilizer();
        }

        /// <summary>
        /// VR gözlükte:
        /// 1) Oyuncunun zemin altına düşmesini veya tavanın üstüne çıkmasını kesin olarak önler.
        /// 2) Gözlük takıldığında oyuncuyu diseksiyon masasının arkasına (-1.35, y, 0) ve
        ///    Akıllı Ekran'a (+X yönüne) bakacak şekilde otomatik hizalar.
        /// 3) Oyuncunun oda duvarlarının veya masaların içine girmesini engeller.
        /// </summary>
        private void UpdateVRStabilizer()
        {
            // 1. İlk OpenXR takip verisi geldiğinde (veya ilk 0.8 saniyede) otomatik masaya hizala
            Vector3 hmdLocalPos = _mainCam.transform.localPosition;
            Quaternion hmdLocalRot = _mainCam.transform.localRotation;
            bool hasTrackingData = hmdLocalPos.sqrMagnitude > 0.0001f || Quaternion.Angle(hmdLocalRot, Quaternion.identity) > 0.1f;

            if (!_vrInitialRecenterDone && hasTrackingData)
            {
                RecenterVROriginToTable();
                _vrInitialRecenterDone = true;
            }
            else if (!_vrDelayedRecenterDone && (Time.time - _vrStartTime) > 0.85f)
            {
                RecenterVROriginToTable();
                _vrDelayedRecenterDone = true;
            }

            // 2. Zemin yüksekliğini (y = 0.282m) sabitle ve göz yüksekliğini (Floor vs Device) kalibre et
            Vector3 rigPos = _rigRoot.position;
            rigPos.y = DefaultRigPos.y;

            if (_cameraOffset != null)
            {
                Vector3 offsetLocal = _cameraOffset.localPosition;
                // Eğer OpenXR Floor modundaysa hmdLocalPos.y gerçek boyu (~1.1m - 1.85m) verir;
                // Device modundaysa hmdLocalPos.y ~0.0m civarındadır.
                float targetEyeAboveFloor = Mathf.Clamp(1.42f - hmdLocalPos.y, 0.0f, 1.42f);
                // Toplam göz yüksekliği zemin (0.282) + offset + hmdLocalPos.y = ~1.70m (tavan 3.0m'nin hep altında)
                float currentEyeWorldY = rigPos.y + offsetLocal.y + hmdLocalPos.y;
                if (currentEyeWorldY < 1.35f || currentEyeWorldY > 2.25f)
                {
                    offsetLocal.y = targetEyeAboveFloor;
                    _cameraOffset.localPosition = offsetLocal;
                }
            }

            // 2.5. VR Kumanda Joystick (Thumbstick) ile Yürüme ve Dönme:
            // - Sol Kumanda Joystick (LeftHand primary2DAxis): Her zaman oyuncuyu baktığı yöne göre yürütür.
            // - Sağ Kumanda Joystick (RightHand primary2DAxis): Kemik ELDE DEĞİLKEN oyuncunun ekranını/bakış yönünü döndürür
            //   (kemik kavrama tuşuyla elde tutulurken ise Sağ Joystick doğrudan eldeki kemiği 360° döndürür).
            var leftHandDev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            if (leftHandDev.isValid &&
                leftHandDev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 leftStick) &&
                leftStick.sqrMagnitude > 0.025f)
            {
                Vector3 camFwd = _mainCam.transform.forward;
                camFwd.y = 0f;
                if (camFwd.sqrMagnitude > 0.0001f) camFwd.Normalize();

                Vector3 camRight = _mainCam.transform.right;
                camRight.y = 0f;
                if (camRight.sqrMagnitude > 0.0001f) camRight.Normalize();

                Vector3 moveDelta = (camFwd * leftStick.y + camRight * leftStick.x) * (moveSpeed * Time.deltaTime);
                rigPos += moveDelta;
            }

            _rigRoot.position = rigPos;

            var rightHandDev = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (rightHandDev.isValid &&
                rightHandDev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 rightStick) &&
                Mathf.Abs(rightStick.x) > 0.18f)
            {
                _rigRoot.RotateAround(_mainCam.transform.position, Vector3.up, rightStick.x * 85f * Time.deltaTime);
            }

            // 3. Baş (Main Camera) dünya pozisyonunu oda duvarları ve masaların dışında tut
            Vector3 headWorldPos = _mainCam.transform.position;
            Vector3 clampedHeadPos = headWorldPos;
            clampedHeadPos.x = Mathf.Clamp(clampedHeadPos.x, -3.10f, 1.95f);
            clampedHeadPos.z = Mathf.Clamp(clampedHeadPos.z, -2.55f, 2.55f);
            clampedHeadPos = PushOutOfBoxXZ(clampedHeadPos, DissectionTableRect, playerRadius);
            clampedHeadPos = PushOutOfBoxXZ(clampedHeadPos, SkeletonTableRect, playerRadius);
            clampedHeadPos.x = Mathf.Clamp(clampedHeadPos.x, -3.10f, 1.95f);
            clampedHeadPos.z = Mathf.Clamp(clampedHeadPos.z, -2.55f, 2.55f);

            Vector3 headCorrectionXZ = new Vector3(clampedHeadPos.x - headWorldPos.x, 0f, clampedHeadPos.z - headWorldPos.z);
            if (headCorrectionXZ.sqrMagnitude > 0.00001f)
            {
                _rigRoot.position += headCorrectionXZ;
            }
        }

        /// <summary>
        /// VR oyuncusunu diseksiyon masasının arkasına (-1.35, 0.282, 0) ve karşıdaki Akıllı Ekran'a
        /// (+X yönüne, Y = 90°) bakacak şekilde hizalar.
        /// </summary>
        public void RecenterVROriginToTable()
        {
            if (_rigRoot == null || _mainCam == null)
                return;

            float camLocalYaw = _mainCam.transform.localEulerAngles.y;
            if (_cameraOffset != null)
                camLocalYaw += _cameraOffset.localEulerAngles.y;

            _rigRoot.rotation = Quaternion.Euler(0f, DefaultRigYaw - camLocalYaw, 0f);

            Vector3 hmdLocalPos = _mainCam.transform.localPosition;
            if (_cameraOffset != null)
            {
                Vector3 offsetLocal = _cameraOffset.localPosition;
                offsetLocal.x = 0f;
                offsetLocal.z = 0f;
                offsetLocal.y = Mathf.Clamp(1.42f - hmdLocalPos.y, 0.0f, 1.42f);
                _cameraOffset.localPosition = offsetLocal;
            }

            Vector3 camWorldOffsetXZ = _mainCam.transform.position - _rigRoot.position;
            camWorldOffsetXZ.y = 0f;

            _rigRoot.position = new Vector3(
                DefaultRigPos.x - camWorldOffsetXZ.x,
                DefaultRigPos.y,
                DefaultRigPos.z - camWorldOffsetXZ.z);
        }

        private void FixedUpdate()
        {
            if (_heldRigidbody != null && _mainCam != null)
            {
                MoveHeldBonePhysics();
            }
        }

        // ─────────────────────── Kısayol Tuşları ─────────────────────────

        private void HandleShortcuts()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            // M veya Tab: Büyük Menüyü (Akıllı Ekran) Aç / Kapat
            if (kb.mKey.wasPressedThisFrame || kb.tabKey.wasPressedThisFrame)
            {
                UIMenuManager uiMgr = FindAnyObjectByType<UIMenuManager>();
                if (uiMgr != null)
                {
                    uiMgr.ToggleMainMenu();
                }
            }

            // H: Yardım kutusunu gizle/göster
            if (kb.hKey.wasPressedThisFrame)
            {
                _showHelp = !_showHelp;
            }

            // V: Kamerayı başlangıç pozisyonuna (masanın arkasına) sıfırla
            if (kb.vKey.wasPressedThisFrame && _rigRoot != null)
            {
                _rigRoot.position = DefaultRigPos;
                _yaw = DefaultRigYaw;
                _pitch = DefaultCamPitch;
                _isCrouching = false;
                ApplyCameraTransform();
            }

            // C: Eğil / Kalk (Yere düşen kemikleri almak için)
            if (kb.cKey.wasPressedThisFrame)
            {
                _isCrouching = !_isCrouching;
                if (_cameraOffset != null)
                {
                    Vector3 lp = _cameraOffset.localPosition;
                    lp.y = _isCrouching ? 0.65f : _standingOffsetY;
                    _cameraOffset.localPosition = lp;
                }
            }
        }

        // ─────────────────────── Kamera Bakış (Look) ─────────────────────

        private void HandleCameraLook()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;

            bool isRotatingHeldBone = _heldRigidbody != null && kb != null && kb.rKey.isPressed;

            if (mouse != null && mouse.rightButton.isPressed && !isRotatingHeldBone)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw   += delta.x * mouseLookSensitivity;
                _pitch -= delta.y * mouseLookSensitivity;
            }

            if (kb != null && !isRotatingHeldBone)
            {
                float yawInput = 0f;
                if (kb.rightArrowKey.isPressed || kb.eKey.isPressed) yawInput += 1f;
                if (kb.leftArrowKey.isPressed  || kb.qKey.isPressed) yawInput -= 1f;

                float pitchInput = 0f;
                if (kb.upArrowKey.isPressed)   pitchInput -= 1f;
                if (kb.downArrowKey.isPressed) pitchInput += 1f;

                _yaw   += yawInput * keyLookSpeed * Time.deltaTime;
                _pitch += pitchInput * keyLookSpeed * Time.deltaTime;
            }

            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            ApplyCameraTransform();
        }

        private void ApplyCameraTransform()
        {
            if (_rigRoot != null)
                _rigRoot.rotation = Quaternion.Euler(0f, _yaw, 0f);

            if (_mainCam != null)
                _mainCam.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        // ─────────────── Çarpışmalı Yürüme (W, A, S, D) ──────────────────

        private void HandleMovement()
        {
            var kb = Keyboard.current;
            if (kb == null || _rigRoot == null) return;

            Vector2 moveInput = Vector2.zero;
            if (kb.wKey.isPressed) moveInput.y += 1f;
            if (kb.sKey.isPressed) moveInput.y -= 1f;
            if (kb.dKey.isPressed) moveInput.x += 1f;
            if (kb.aKey.isPressed) moveInput.x -= 1f;

            if (moveInput.sqrMagnitude < 0.001f)
                return;

            moveInput.Normalize();
            float speed = kb.leftShiftKey.isPressed ? moveSpeed * 1.75f : moveSpeed;

            Vector3 forward = _rigRoot.forward;
            forward.y = 0f;
            forward.Normalize();

            Vector3 right = _rigRoot.right;
            right.y = 0f;
            right.Normalize();

            Vector3 nextPos = _rigRoot.position + (forward * moveInput.y + right * moveInput.x) * (speed * Time.deltaTime);

            // 1. Oda duvarları ve Akıllı Ekran sınırları içinde tut
            nextPos.x = Mathf.Clamp(nextPos.x, -3.12f, 1.98f);
            nextPos.z = Mathf.Clamp(nextPos.z, -2.62f, 2.62f);

            // 2. Ortadaki Diseksiyon Masası ('masa') çarpışma engeli — içinden geçmeyi önler, kenarından kaydırır
            nextPos = PushOutOfBoxXZ(nextPos, DissectionTableRect, playerRadius);

            // 3. Arka Duvardaki Köpek İskeletli Masa ('iskeletli masa') çarpışma engeli — içinden geçmeyi önler
            nextPos = PushOutOfBoxXZ(nextPos, SkeletonTableRect, playerRadius);

            // Tekrar oda duvar sınırlarını doğrula (köşe sıkışmalarını önlemek için)
            nextPos.x = Mathf.Clamp(nextPos.x, -3.12f, 1.98f);
            nextPos.z = Mathf.Clamp(nextPos.z, -2.62f, 2.62f);

            _rigRoot.position = nextPos;
        }

        /// <summary>
        /// Oyuncunun (x, z) konumunu verilen dikdörtgen engelin (minX, maxX, minZ, maxZ) + yarıçap
        /// dışına en kısa eksen boyunca iter; böylece masaların ve iskeletin içinden geçilmez,
        /// kenarlarından pürüzsüzce kayılır.
        /// </summary>
        private static Vector3 PushOutOfBoxXZ(Vector3 pos, Vector4 boxMinMaxXZ, float radius)
        {
            float minX = boxMinMaxXZ.x - radius;
            float maxX = boxMinMaxXZ.y + radius;
            float minZ = boxMinMaxXZ.z - radius;
            float maxZ = boxMinMaxXZ.w + radius;

            if (pos.x > minX && pos.x < maxX && pos.z > minZ && pos.z < maxZ)
            {
                float distLeft  = pos.x - minX;
                float distRight = maxX - pos.x;
                float distBack  = pos.z - minZ;
                float distFront = maxZ - pos.z;

                float minDist = Mathf.Min(Mathf.Min(distLeft, distRight), Mathf.Min(distBack, distFront));

                if (Mathf.Approximately(minDist, distLeft))
                    pos.x = minX;
                else if (Mathf.Approximately(minDist, distRight))
                    pos.x = maxX;
                else if (Mathf.Approximately(minDist, distBack))
                    pos.z = minZ;
                else
                    pos.z = maxZ;
            }

            return pos;
        }

        // ─────────────────── Kemik Tutma, Yaklaştırma, Döndürme ──────────

        private void UpdateHoverDetection()
        {
            _hoveredBoneName = null;
            if (_heldRigidbody != null || _mainCam == null || Mouse.current == null)
                return;

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            Ray ray = _mainCam.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out RaycastHit hit, maxGrabDistance))
            {
                Rigidbody rb = hit.rigidbody ?? hit.collider.GetComponentInParent<Rigidbody>();
                if (rb != null)
                {
                    BoneIdentifier id = rb.GetComponent<BoneIdentifier>();
                    _hoveredBoneName = id != null && !string.IsNullOrEmpty(id.DisplayNameTR)
                        ? id.DisplayNameTR
                        : rb.gameObject.name;
                }
            }
        }

        private void HandleBoneGrabAndInspect()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null || _mainCam == null) return;

            bool leftDown = mouse.leftButton.wasPressedThisFrame;
            bool leftUp   = mouse.leftButton.wasReleasedThisFrame;
            bool fPressed = kb != null && kb.fKey.wasPressedThisFrame;

            // Eğer fare bir Pin Küreciği (PinDotTarget) üzerindeyse Sol Tık sadece küreciğin kartını açıp kapatsın,
            // kemiği tutmasın veya bırakmasın:
            if (leftDown && IsCursorOverPinDot())
            {
                return;
            }

            // Kemik elde değilken ancak havada asılıyken [R + Fare] ile havada döndürme desteği
            if (_heldRigidbody == null && BoneIdentifier.IsAnyBoneFloating)
            {
                bool rotateFloating = (kb != null && kb.rKey.isPressed) || mouse.middleButton.isPressed;
                if (rotateFloating)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    for (int i = 0; i < BoneIdentifier.ActiveBones.Count; i++)
                    {
                        var b = BoneIdentifier.ActiveBones[i];
                        if (b != null && b.IsFloating)
                        {
                            b.RotateAroundCenter(Vector3.up, -delta.x * rotateSensitivity);
                            b.RotateAroundCenter(_mainCam.transform.right, delta.y * rotateSensitivity);
                        }
                    }
                }
            }

            if (_heldRigidbody == null)
            {
                if (leftDown || fPressed)
                {
                    if (leftDown && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                        return;

                    TryGrabBoneUnderCursor(fPressed);
                }
                return;
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _holdDistance = Mathf.Clamp(_holdDistance + Mathf.Sign(scroll) * 0.08f, minHoldDistance, maxHoldDistance);
            }

            bool rotateWithMouse = (kb != null && kb.rKey.isPressed) || mouse.middleButton.isPressed;
            if (rotateWithMouse)
            {
                Vector2 delta = mouse.delta.ReadValue();
                Quaternion rotY = Quaternion.AngleAxis(-delta.x * rotateSensitivity, _mainCam.transform.up);
                Quaternion rotX = Quaternion.AngleAxis( delta.y * rotateSensitivity, _mainCam.transform.right);
                _heldRotationOffset = rotY * rotX * _heldRotationOffset;
            }

            if (kb != null)
            {
                if (kb.zKey.isPressed)
                    _heldRotationOffset = Quaternion.AngleAxis( 110f * Time.deltaTime, Vector3.up) * _heldRotationOffset;
                if (kb.xKey.isPressed)
                    _heldRotationOffset = Quaternion.AngleAxis(-110f * Time.deltaTime, Vector3.up) * _heldRotationOffset;
            }

            if (!_stickyGrab && leftUp)
            {
                if (Time.time - _grabStartTime < 0.25f)
                {
                    _stickyGrab = true;
                }
                else
                {
                    ReleaseHeldBone();
                }
            }
            else if (_stickyGrab && (leftDown || fPressed || (kb != null && kb.spaceKey.wasPressedThisFrame)))
            {
                if (leftDown && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                    return;

                ReleaseHeldBone();
            }
        }

        private bool IsCursorOverPinDot()
        {
            return BoneAnatomicalLabels.IsMouseHoveringAnyPin();
        }

        private void TryGrabBoneUnderCursor(bool forceSticky)
        {
            Ray ray = _mainCam.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, maxGrabDistance))
                return;

            Rigidbody rb = hit.rigidbody ?? hit.collider.GetComponentInParent<Rigidbody>();
            if (rb == null)
                return;

            _heldRigidbody = rb;
            BoneIdentifier id = rb.GetComponent<BoneIdentifier>();
            _heldBoneName = id != null && !string.IsNullOrEmpty(id.DisplayNameTR) ? id.DisplayNameTR : rb.gameObject.name;

            _holdDistance = Mathf.Clamp(Vector3.Distance(_mainCam.transform.position, rb.position), 0.45f, 1.10f);
            _heldRotationOffset = Quaternion.Inverse(_mainCam.transform.rotation) * rb.rotation;
            _grabStartTime = Time.time;
            _stickyGrab = forceSticky;

            _heldRigidbody.useGravity = false;
#if UNITY_6000_0_OR_NEWER
            _heldRigidbody.linearVelocity = Vector3.zero;
#else
            _heldRigidbody.velocity = Vector3.zero;
#endif
            _heldRigidbody.angularVelocity = Vector3.zero;
        }

        private void MoveHeldBonePhysics()
        {
            if (_heldRigidbody == null || _mainCam == null)
                return;

            Vector2 mousePos = Mouse.current != null
                ? Mouse.current.position.ReadValue()
                : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            Ray cursorRay = _mainCam.ScreenPointToRay(mousePos);
            Vector3 targetPos = cursorRay.GetPoint(_holdDistance);

            // Zeminin altına inmesini engelle
            targetPos.y = Mathf.Max(targetPos.y, 0.38f);

            // Eldeki kemiğin en alt noktasının (döndürülmüş uçlarının) diseksiyon masasının içine girmesini engelle (masa üst yüzeyi y = 1.023m)
            float bottomOffset = 0.06f;
            Collider heldCol = _heldRigidbody.GetComponent<Collider>();
            if (heldCol != null)
            {
                bottomOffset = Mathf.Max(0.04f, _heldRigidbody.position.y - heldCol.bounds.min.y + 0.008f);
            }

            if (targetPos.x > DissectionTableRect.x - 0.12f && targetPos.x < DissectionTableRect.y + 0.12f &&
                targetPos.z > DissectionTableRect.z - 0.12f && targetPos.z < DissectionTableRect.w + 0.12f)
            {
                float minTableY = 1.025f + bottomOffset;
                if (targetPos.y < minTableY)
                {
                    targetPos.y = minTableY;
                }
            }

            // Eldeki kemiğin arkadaki iskeletli masanın içine girmesini engelle
            targetPos = PushOutOfBoxXZ(targetPos, SkeletonTableRect, 0.12f);

            Vector3 newPos = Vector3.Lerp(_heldRigidbody.position, targetPos, 1f - Mathf.Exp(-followSharpness * Time.fixedDeltaTime));
            Quaternion targetRot = _mainCam.transform.rotation * _heldRotationOffset;
            Quaternion newRot = Quaternion.Slerp(_heldRigidbody.rotation, targetRot, 1f - Mathf.Exp(-followSharpness * Time.fixedDeltaTime));

#if UNITY_6000_0_OR_NEWER
            _heldRigidbody.linearVelocity = Vector3.zero;
#else
            _heldRigidbody.velocity = Vector3.zero;
#endif
            _heldRigidbody.angularVelocity = Vector3.zero;
            _heldRigidbody.MovePosition(newPos);
            _heldRigidbody.MoveRotation(newRot);
        }

        private void ReleaseHeldBone()
        {
            if (_heldRigidbody != null)
            {
                _heldRigidbody.useGravity = true;
#if UNITY_6000_0_OR_NEWER
                _heldRigidbody.linearVelocity = Vector3.zero;
#else
                _heldRigidbody.velocity = Vector3.zero;
#endif
                _heldRigidbody.angularVelocity = Vector3.zero;
            }

            _heldRigidbody = null;
            _heldBoneName = null;
            _stickyGrab = false;
        }

        // ─────────────────────── Ekran Üstü Kontrol Rehberi ──────────────

        private void OnGUI()
        {
            if (IsVRModeActive())
                return;

            if (_heldRigidbody != null)
            {
                DrawCenteredBanner($"🦴 Eldeki Kemik: {_heldBoneName}   |   [X / L] Anatomik Etiketler   |   [R + Fare] Döndür   |   [Sol Tık / F] Bırak",
                    new Color(0.10f, 0.38f, 0.65f, 0.92f));
            }
            else if (!string.IsNullOrEmpty(_hoveredBoneName))
            {
                DrawCenteredBanner($"👆 {_hoveredBoneName} — Tutmak için [Sol Tık / F]  |  Etiketler için [X / L]",
                    new Color(0.12f, 0.22f, 0.34f, 0.88f));
            }

            if (!_showHelp)
            {
                GUI.Box(new Rect(12, 135, 220, 28), " [H] PC Kontrol Rehberini Göster");
                return;
            }

            Rect boxRect = new Rect(12, 135, 385, 212);
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.06f, 0.10f, 0.16f, 0.92f);
            GUI.Box(boxRect, GUIContent.none);
            GUI.backgroundColor = prevBg;

            GUILayout.BeginArea(new Rect(boxRect.x + 12, boxRect.y + 8, boxRect.width - 24, boxRect.height - 16));
            var titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 13 };
            titleStyle.normal.textColor = new Color(0.45f, 0.85f, 1f);
            var itemStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            itemStyle.normal.textColor = Color.white;

            GUILayout.Label("🖥️ PC Test Kontrolleri  ([H] Gizle)", titleStyle);
            GUILayout.Space(2);
            GUILayout.Label("• Etrafa Bakma:  Sağ Tık + Fare  veya  Ok Tuşları / Q-E", itemStyle);
            GUILayout.Label("• Yürüme / Eğilme:  W, A, S, D   |   [C] Eğil/Kalk   |   [V] Sıfırla", itemStyle);
            GUILayout.Label("• Kemiği Ele Al / Bırak:  Kemiğe Sol Tık  veya  [F] Tuşu", itemStyle);
            GUILayout.Label("• 3B Anatomik Etiketler (Pin):  [X]  veya  [L] Tuşu (VR'da X / A)", itemStyle);
            GUILayout.Label("• Kemik Yaklaştır / Döndür:  Tekerlek  |  [R] + Fare  /  [Z]", itemStyle);
            GUILayout.Label("• Büyük Menüyü Kapat / Aç:  [M] / [Tab]  veya Sağ Üst [X]", itemStyle);
            GUILayout.EndArea();
        }

        private static void DrawCenteredBanner(string text, Color bgColor)
        {
            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = Color.white;

            float width = Mathf.Min(Screen.width - 40f, 760f);
            Rect rect = new Rect((Screen.width - width) * 0.5f, Screen.height - 52f, width, 36f);
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = bgColor;
            GUI.Box(rect, text, style);
            GUI.backgroundColor = prev;
        }
    }
}
