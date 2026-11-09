using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace AnatomiVR.Bones
{
    /// <summary>
    /// Her kemik prefab'ına eklenen bileşen.
    /// 1) Addressables key/handle bilgisini tutar.
    /// 2) VR'da kemiğin diseksiyon masası, iskelet masası, zemin veya oda duvarlarının içinden
    ///    geçmesini (tunneling) ve masaya bastırıldığında titremesini (jitter) %100 engeller.
    /// 3) Kemik havaya kaldırılıp bırakıldığında (veya Y/B tuşuna basıldığında) bıraktığınız
    ///    yerde havada asılı kalmasını sağlar.
    /// 4) Kemik kavrama tuşuyla (Grip) elde tutulurken Sağ Joystick ile elinizde kendi merkezi
    ///    etrafında 360° döndürülebilmesini sağlar (eski pozisyonuna kaçmaz).
    /// </summary>
    public class BoneIdentifier : MonoBehaviour
    {
        public static readonly List<BoneIdentifier> ActiveBones = new List<BoneIdentifier>();

        [Header("Addressables Bilgisi")]
        [Tooltip("Bu kemiğin Addressables key'i (ör: Dog_Humerus)")]
        [SerializeField] private string addressableKey;

        [Header("Görüntüleme")]
        [Tooltip("UI'da gösterilecek Latince/Türkçe kemik adı")]
        [SerializeField] private string displayNameTR;

        [Header("Havada Sabitleme & Döndürme (VR Y/B Tuşu & Sağ Joystick)")]
        [Tooltip("Elde tutulan kemiği Sağ Joystick ile döndürme hızı (derece/saniye)")]
        [SerializeField] private float joystickRotateSpeed = 135f;

        [Tooltip("Y/B tuşuna basılı tutarken döndürme hızı")]
        [SerializeField] private float buttonRotateSpeed = 90f;

        public AsyncOperationHandle<GameObject> InstantiateHandle { get; set; }

        public string AddressableKey
        {
            get => addressableKey;
            set => addressableKey = value;
        }

        public string DisplayNameTR
        {
            get => displayNameTR;
            set => displayNameTR = value;
        }

        /// <summary>
        /// Kemik şu an havada asılı (yerçekimsiz inceleme) modunda mı?
        /// </summary>
        public bool IsFloating => _isFloating;

        /// <summary>
        /// Kemik şu an VR kumandasıyla elde tutuluyor mu?
        /// </summary>
        public bool IsHeldInVR => _grab != null && _grab.isSelected;

        /// <summary>
        /// Sahnede herhangi bir kemik şu an oyuncunun elinde (Grip ile) tutuluyor mu?
        /// True ise Sağ Joystick eldeki kemiği döndürür; False ise Sağ Joystick ekranı/kamerayı döndürür.
        /// </summary>
        public static bool IsAnyBoneHeldInHand
        {
            get
            {
                for (int i = 0; i < ActiveBones.Count; i++)
                {
                    var b = ActiveBones[i];
                    if (b != null && b._grab != null && b._grab.isSelected)
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Sahnede herhangi bir kemik şu an havada asılı veya elde mi?
        /// </summary>
        public static bool IsAnyBoneFloating
        {
            get
            {
                for (int i = 0; i < ActiveBones.Count; i++)
                {
                    var b = ActiveBones[i];
                    if (b != null && (b._isFloating || (b._grab != null && b._grab.isSelected)))
                        return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Kemiğin dünya uzayındaki geometrik merkezini döndürür.
        /// </summary>
        public Vector3 GetWorldMeshCenter() => transform.TransformPoint(_localMeshCenter);

        // ─────────────── Masa ve Duvar Çarpışma Sınırları ───────────────
        private const float RoomMinX = -3.36f;
        private const float RoomMaxX =  2.30f;
        private const float RoomMinZ = -2.85f;
        private const float RoomMaxZ =  2.85f;
        private const float FloorTopY = 0.285f;
        private const float CeilingBottomY = 2.95f;

        private static readonly Vector4 DissectionTableXZ = new Vector4(-0.45f, 0.45f, -1.12f, 1.12f);
        private const float DissectionTableTopY = 1.024f;

        private static readonly Vector4 SkeletonTableXZ = new Vector4(-2.72f, -1.97f, -0.68f, 0.68f);
        private const float SkeletonTableTopY = 1.76f;

        private Rigidbody _rb;
        private XRGrabInteractable _grab;
        private Collider[] _colliders;
        private Vector3 _localMeshCenter = Vector3.zero;
        private bool _wasAboveDissectionTable = true;

        // Havada asılı kalma (Float) durumu
        private bool _isFloating;
        private Vector3 _floatingPosition;
        private Quaternion _floatingRotation;

        // VR Y / B (secondaryButton) ve L3 / R3 (primary2DAxisClick - Joystick Tıklama) giriş takibi
        private static int _lastSecondaryInputFrame = -1;
        private static bool _prevSecondaryPressed;
        private static float _secondaryPressStartTime;
        private static bool _secondaryHoldTriggered;
        private static bool _prevLeftStickClick;
        private static bool _prevRightStickClick;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _grab = GetComponent<XRGrabInteractable>();
            RefreshOwnColliders();

            MeshFilter mf = GetComponentInChildren<MeshFilter>(true);
            if (mf != null && mf.sharedMesh != null)
            {
                _localMeshCenter = mf.sharedMesh.bounds.center;

                // Kemiğin çukur (fossa) bölgelerinin BoxCollider tarafından havada kapatılmasını önle:
                BoxCollider bc = GetComponent<BoxCollider>();
                if (bc != null)
                {
                    bc.center = mf.sharedMesh.bounds.center;
                    bc.size = mf.sharedMesh.bounds.size * 0.32f;
                }
            }
        }

        private void OnEnable()
        {
            if (!ActiveBones.Contains(this))
                ActiveBones.Add(this);

            if (_grab == null)
                _grab = GetComponent<XRGrabInteractable>();

            if (_grab != null)
            {
                _grab.selectEntered.AddListener(OnGrabbedInVR);
                _grab.selectExited.AddListener(OnReleasedInVR);
            }
        }

        private void OnDisable()
        {
            ActiveBones.Remove(this);

            if (_grab != null)
            {
                _grab.selectEntered.RemoveListener(OnGrabbedInVR);
                _grab.selectExited.RemoveListener(OnReleasedInVR);
            }
        }

        private void RefreshOwnColliders()
        {
            var list = new List<Collider>();
            foreach (Collider c in GetComponents<Collider>())
            {
                if (c != null && !c.isTrigger)
                    list.Add(c);
            }
            if (list.Count == 0)
            {
                foreach (Collider c in GetComponentsInChildren<Collider>(true))
                {
                    if (c != null && !c.isTrigger)
                        list.Add(c);
                }
            }
            _colliders = list.ToArray();
        }

        private void OnGrabbedInVR(SelectEnterEventArgs args)
        {
            // Kemik tekrar kavrama tuşuyla (Grip) ele alındığında eski havada sabitleme kilidini
            // devre dışı bırak ki kemik eski pozisyonuna kaçmasın ve doğrudan elde dönebilsin!
            _isFloating = false;
            if (_rb != null)
            {
                _rb.useGravity = false;
            }
        }

        private void OnReleasedInVR(SelectExitEventArgs args)
        {
            if (_rb == null)
                _rb = GetComponent<Rigidbody>();

            Bounds b = CalculateWorldBounds(transform.position);
            float bottomOffset = Mathf.Max(0.03f, transform.position.y - b.min.y);

            Vector3 vel = Vector3.zero;
            if (_rb != null)
            {
#if UNITY_6000_0_OR_NEWER
                vel = _rb.linearVelocity;
#else
                vel = _rb.velocity;
#endif
            }

            // Kullanıcı kemiği masa yüzeyinin en az 11 cm üstünde (havada) bıraktıysa,
            // kemik bıraktığı YENİ pozisyonda ve açıda havada asılı kalsın!
            bool releasedInMidAir = transform.position.y > (DissectionTableTopY + bottomOffset + 0.11f);
            bool notThrownFast = vel.magnitude < 1.85f;

            if (releasedInMidAir && notThrownFast)
            {
                SetFloating(true, transform.position, transform.rotation);
            }
            else
            {
                SetFloating(false);
            }
        }

        /// <summary>
        /// Kemiği havada asılı moda (Floating Mode) alır veya serbest bırakıp masaya indirir.
        /// </summary>
        public void SetFloating(bool floating, Vector3? customPos = null, Quaternion? customRot = null)
        {
            if (_rb == null)
                _rb = GetComponent<Rigidbody>();

            _isFloating = floating;

            if (floating)
            {
                Vector3 targetPos = customPos ?? transform.position;
                Quaternion targetRot = customRot ?? transform.rotation;

                Bounds b = CalculateWorldBounds(targetPos);
                float bottomOffset = Mathf.Max(0.03f, targetPos.y - b.min.y);
                if (targetPos.y < DissectionTableTopY + bottomOffset + 0.16f)
                {
                    targetPos.y = DissectionTableTopY + bottomOffset + 0.34f;
                }

                _floatingPosition = targetPos;
                _floatingRotation = targetRot;

                transform.position = _floatingPosition;
                transform.rotation = _floatingRotation;

                if (_rb != null)
                {
                    _rb.useGravity = false;
                    if (!_rb.isKinematic)
                    {
#if UNITY_6000_0_OR_NEWER
                        _rb.linearVelocity = Vector3.zero;
#else
                        _rb.velocity = Vector3.zero;
#endif
                        _rb.angularVelocity = Vector3.zero;
                    }
                    _rb.position = _floatingPosition;
                    _rb.rotation = _floatingRotation;
                }
            }
            else
            {
                if (_rb != null)
                {
                    _rb.useGravity = true;
                    if (!_rb.isKinematic)
                    {
#if UNITY_6000_0_OR_NEWER
                        _rb.linearVelocity = Vector3.zero;
#else
                        _rb.velocity = Vector3.zero;
#endif
                        _rb.angularVelocity = Vector3.zero;
                    }
                }
            }
        }

        /// <summary>
        /// Kemiği kendi geometrik merkezi etrafında döndürür.
        /// - Eğer kemik şu an elde tutuluyorsa (IsHeldInVR): XRGrabInteractable'ın Dynamic Attach
        ///   noktasını kemik merkezi etrafında ters yönde döndürerek kemiğin doğrudan oyuncunun
        ///   ELİNDE (eski pozisyonuna kaçmadan!) dönmesini sağlar.
        /// - Eğer kemik havada asılıysa: Olduğu yerde kendi merkezi etrafında döndürür.
        /// </summary>
        public void RotateAroundCenter(Vector3 worldAxis, float angleDegrees)
        {
            if (Mathf.Abs(angleDegrees) < 0.0001f)
                return;

            // DURUM 1: Kemik şu an VR'da oyuncunun elinde tutuluyor (Grip ile)
            if (_grab != null && _grab.isSelected && _grab.interactorsSelecting.Count > 0)
            {
                IXRSelectInteractor interactor = _grab.interactorsSelecting[0];
                Transform attachTr = _grab.GetAttachTransform(interactor);
                Vector3 worldCenter = transform.TransformPoint(_localMeshCenter);

                if (attachTr != null && attachTr != transform && attachTr.IsChildOf(transform))
                {
                    // Dynamic Attach objesini kemik merkezi etrafında ters yönde döndürürsek,
                    // XRGrabInteractable kemiği elimizin içinde tam kendi merkezi etrafında döndürür!
                    attachTr.RotateAround(worldCenter, worldAxis.normalized, -angleDegrees);
                    _floatingPosition = transform.position;
                    _floatingRotation = transform.rotation;
                    return;
                }
            }

            // DURUM 2: Kemik elde değil (havada asılı veya serbest)
            Quaternion currentRot = _isFloating ? _floatingRotation : transform.rotation;
            Vector3 currentPos = _isFloating ? _floatingPosition : transform.position;

            Vector3 scaledCenterOffset = Vector3.Scale(_localMeshCenter, transform.localScale);
            Vector3 worldCenterPos = currentPos + currentRot * scaledCenterOffset;

            Quaternion deltaRot = Quaternion.AngleAxis(angleDegrees, worldAxis.normalized);
            Quaternion nextRot = deltaRot * currentRot;
            Vector3 nextPos = worldCenterPos - nextRot * scaledCenterOffset;

            _floatingRotation = nextRot;
            _floatingPosition = nextPos;
            transform.rotation = nextRot;
            transform.position = nextPos;

            if (_rb != null)
            {
                _rb.rotation = nextRot;
                _rb.position = nextPos;
                if (!_rb.isKinematic)
                {
#if UNITY_6000_0_OR_NEWER
                    _rb.linearVelocity = Vector3.zero;
#else
                    _rb.velocity = Vector3.zero;
#endif
                    _rb.angularVelocity = Vector3.zero;
                }
            }
        }

        private void Update()
        {
            if (!Application.isPlaying)
                return;

            ProcessGlobalFloatAndRotateInputs();

            if (_isFloating && !IsHeldInVR)
            {
                LockFloatingPose();
            }
        }

        private void FixedUpdate()
        {
            if (_isFloating && !IsHeldInVR)
            {
                LockFloatingPose();
            }

            EnforceSolidTableAndWalls();
        }

        private void LateUpdate()
        {
            if (_isFloating && !IsHeldInVR)
            {
                LockFloatingPose();
            }

            EnforceSolidTableAndWalls();
        }

        private void LockFloatingPose()
        {
            transform.position = _floatingPosition;
            transform.rotation = _floatingRotation;

            if (_rb != null)
            {
                if (_rb.useGravity)
                    _rb.useGravity = false;

                if (!_rb.isKinematic)
                {
#if UNITY_6000_0_OR_NEWER
                    _rb.linearVelocity = Vector3.zero;
#else
                    _rb.velocity = Vector3.zero;
#endif
                    _rb.angularVelocity = Vector3.zero;
                }
                _rb.position = _floatingPosition;
                _rb.rotation = _floatingRotation;
            }
        }

        // ─────────────── VR Kumanda (Y / B Tuşu & Sağ Joystick) + PC Kontrolleri ───────────────

        private static void ProcessGlobalFloatAndRotateInputs()
        {
            if (_lastSecondaryInputFrame == Time.frameCount)
                return;
            _lastSecondaryInputFrame = Time.frameCount;

            // 0. VR Kumanda Joystick Butonu Tıklaması (L3 / R3 — primary2DAxisClick):
            // Sağ veya sol kontrolcüyle bir kemik tutulduğunda, tutulan eldeki joystick butonuna (R3 veya L3)
            // tıklandığında o elde tutulan kemiği anında sahneden sil!
            if (CheckAndDeleteHeldBoneOnStickClick())
            {
                return;
            }

            BoneIdentifier targetBone = FindBestTargetBoneForManipulation();
            if (targetBone == null)
                return;

            // 1. PC Klavye Desteği: [G] tuşu havada sabitle/bırak
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.gKey.wasPressedThisFrame)
                {
                    targetBone.SetFloating(!targetBone._isFloating);
                }

                if (targetBone._isFloating)
                {
                    float pcYaw = 0f;
                    if (kb.zKey.isPressed) pcYaw += 1f;
                    if (kb.cKey.isPressed) pcYaw -= 1f;
                    if (Mathf.Abs(pcYaw) > 0.01f)
                    {
                        targetBone.RotateAroundCenter(Vector3.up, pcYaw * targetBone.joystickRotateSpeed * Time.deltaTime);
                    }
                }
            }

            // 2. VR Kumanda Durumlarını Oku (Sol Kumanda Y = secondaryButton, Sağ Kumanda B = secondaryButton)
            bool leftY = false;
            bool rightB = false;

            var leftHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            if (leftHand.isValid)
            {
                leftHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out leftY);
            }

            var rightHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (rightHand.isValid)
            {
                rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out rightB);
            }

            bool secondaryNow = leftY || rightB;

            if (secondaryNow && !_prevSecondaryPressed)
            {
                _secondaryPressStartTime = Time.time;
                _secondaryHoldTriggered = false;
            }
            else if (secondaryNow && _prevSecondaryPressed)
            {
                float heldDuration = Time.time - _secondaryPressStartTime;
                if (heldDuration >= 0.22f)
                {
                    _secondaryHoldTriggered = true;
                    if (!targetBone.IsHeldInVR && !targetBone._isFloating)
                    {
                        targetBone.SetFloating(true);
                    }

                    float dir = rightB ? 1f : -1f;
                    targetBone.RotateAroundCenter(Vector3.up, dir * targetBone.buttonRotateSpeed * Time.deltaTime);
                }
            }
            else if (!secondaryNow && _prevSecondaryPressed)
            {
                if (!_secondaryHoldTriggered && !targetBone.IsHeldInVR)
                {
                    targetBone.SetFloating(!targetBone._isFloating);
                }
            }

            _prevSecondaryPressed = secondaryNow;
        }

        /// <summary>
        /// Sağ veya sol kontrolcüyle bir kemik tutulurken, tutulan eldeki joystick butonuna (L3 veya R3)
        /// tıklandığında tutulan kemiği siler.
        /// </summary>
        private static bool CheckAndDeleteHeldBoneOnStickClick()
        {
            bool leftClickNow = false;
            bool rightClickNow = false;

            var leftHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);
            if (leftHand.isValid)
            {
                leftHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out leftClickNow);
            }

            var rightHand = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (rightHand.isValid)
            {
                rightHand.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxisClick, out rightClickNow);
            }

            bool leftJustClicked = leftClickNow && !_prevLeftStickClick;
            bool rightJustClicked = rightClickNow && !_prevRightStickClick;

            _prevLeftStickClick = leftClickNow;
            _prevRightStickClick = rightClickNow;

            if (!leftJustClicked && !rightJustClicked)
                return false;

            for (int i = ActiveBones.Count - 1; i >= 0; i--)
            {
                BoneIdentifier b = ActiveBones[i];
                if (b == null || !b.IsHeldInVR)
                    continue;

                if (leftJustClicked && b.IsHeldByControllerHand(true))
                {
                    if (leftHand.isValid)
                        leftHand.SendHapticImpulse(0u, 0.50f, 0.06f);
                    DeleteBoneInstance(b);
                    return true;
                }

                if (rightJustClicked && b.IsHeldByControllerHand(false))
                {
                    if (rightHand.isValid)
                        rightHand.SendHapticImpulse(0u, 0.50f, 0.06f);
                    DeleteBoneInstance(b);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Bu kemik şu an belirtilen eldeki (true = Sol Kontrolcü, false = Sağ Kontrolcü) interactor tarafından mı tutuluyor?
        /// </summary>
        public bool IsHeldByControllerHand(bool isLeftHand)
        {
            if (_grab == null || !_grab.isSelected)
                return false;

            string token = isLeftHand ? "left" : "right";
            for (int i = 0; i < _grab.interactorsSelecting.Count; i++)
            {
                var interactor = _grab.interactorsSelecting[i] as Component;
                if (interactor == null) continue;

                Transform t = interactor.transform;
                while (t != null)
                {
                    string lowerName = t.name.ToLowerInvariant();
                    if (lowerName.Contains(token))
                        return true;
                    t = t.parent;
                }
            }

            return false;
        }

        private static void DeleteBoneInstance(BoneIdentifier bone)
        {
            if (bone == null) return;

            if (AnatomiVR.Managers.AnatomyManager.Instance != null)
            {
                AnatomiVR.Managers.AnatomyManager.Instance.RemoveSpecificBone(bone.gameObject);
            }
            else
            {
                Destroy(bone.gameObject);
            }
        }

        private static BoneIdentifier FindBestTargetBoneForManipulation()
        {
            if (ActiveBones.Count == 0)
                return null;

            // 1. Öncelik: Şu an VR'da elde tutulan kemik
            for (int i = 0; i < ActiveBones.Count; i++)
            {
                BoneIdentifier b = ActiveBones[i];
                if (b != null && b.IsHeldInVR)
                    return b;
            }

            // 2. Öncelik: Kameraya / oyuncuya en yakın aktif kemik
            Camera cam = Camera.main;
            Vector3 refPos = cam != null ? cam.transform.position : Vector3.zero;
            BoneIdentifier closest = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < ActiveBones.Count; i++)
            {
                BoneIdentifier b = ActiveBones[i];
                if (b == null) continue;
                float d = Vector3.Distance(refPos, b.transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    closest = b;
                }
            }

            return closest;
        }

        // ─────────────── Masa ve Duvar Çarpışma Koruması ───────────────

        private void EnforceSolidTableAndWalls()
        {
            if (_rb == null)
                _rb = GetComponent<Rigidbody>();

            Vector3 pos = (_isFloating && !IsHeldInVR) ? _floatingPosition : (_rb != null ? _rb.position : transform.position);
            Bounds bounds = CalculateWorldBounds(pos);

            float bottomOffset = Mathf.Max(0.03f, pos.y - bounds.min.y);
            float topOffset    = Mathf.Max(0.03f, bounds.max.y - pos.y);
            float halfX        = Mathf.Clamp((bounds.max.x - bounds.min.x) * 0.5f, 0.03f, 0.22f);
            float halfZ        = Mathf.Clamp((bounds.max.z - bounds.min.z) * 0.5f, 0.03f, 0.22f);

            Vector3 vel = Vector3.zero;
            if (_rb != null && !_rb.isKinematic)
            {
#if UNITY_6000_0_OR_NEWER
                vel = _rb.linearVelocity;
#else
                vel = _rb.velocity;
#endif
            }

            bool clamped = false;
            bool dampedOnSurface = false;

            // 1. Zemin ve Tavan Koruması
            float minFloorPivotY = FloorTopY + bottomOffset;
            if (pos.y < minFloorPivotY)
            {
                pos.y = minFloorPivotY;
                if (vel.y < 0f) vel.y = 0f;
                clamped = true;
                dampedOnSurface = true;
            }

            float maxCeilingPivotY = CeilingBottomY - topOffset;
            if (pos.y > maxCeilingPivotY)
            {
                pos.y = maxCeilingPivotY;
                if (vel.y > 0f) vel.y = 0f;
                clamped = true;
            }

            // 2. 4 Oda Duvarı Koruması
            if (pos.x - halfX < RoomMinX)
            {
                pos.x = RoomMinX + halfX;
                if (vel.x < 0f) vel.x = 0f;
                clamped = true;
            }
            else if (pos.x + halfX > RoomMaxX)
            {
                pos.x = RoomMaxX - halfX;
                if (vel.x > 0f) vel.x = 0f;
                clamped = true;
            }

            if (pos.z - halfZ < RoomMinZ)
            {
                pos.z = RoomMinZ + halfZ;
                if (vel.z < 0f) vel.z = 0f;
                clamped = true;
            }
            else if (pos.z + halfZ > RoomMaxZ)
            {
                pos.z = RoomMaxZ - halfZ;
                if (vel.z > 0f) vel.z = 0f;
                clamped = true;
            }

            // 3. Diseksiyon Masası Koruması
            bool insideDissectionXZ =
                pos.x + halfX * 0.6f > DissectionTableXZ.x &&
                pos.x - halfX * 0.6f < DissectionTableXZ.y &&
                pos.z + halfZ * 0.6f > DissectionTableXZ.z &&
                pos.z - halfZ * 0.6f < DissectionTableXZ.w;

            float minTablePivotY = DissectionTableTopY + bottomOffset;

            if (insideDissectionXZ)
            {
                if (_wasAboveDissectionTable || pos.y >= DissectionTableTopY - 0.14f)
                {
                    _wasAboveDissectionTable = true;
                    if (pos.y < minTablePivotY)
                    {
                        pos.y = minTablePivotY;
                        if (vel.y < 0f) vel.y = 0f;
                        clamped = true;
                        dampedOnSurface = true;
                    }
                }
                else
                {
                    pos = PushOutHorizontal(pos, DissectionTableXZ, Mathf.Max(halfX, halfZ), ref vel);
                    clamped = true;
                }
            }
            else
            {
                _wasAboveDissectionTable = (pos.y - bottomOffset) >= (DissectionTableTopY - 0.04f);
            }

            // 4. Arka İskeletli Masa Koruması
            bool insideSkeletonXZ =
                pos.x + halfX * 0.6f > SkeletonTableXZ.x &&
                pos.x - halfX * 0.6f < SkeletonTableXZ.y &&
                pos.z + halfZ * 0.6f > SkeletonTableXZ.z &&
                pos.z - halfZ * 0.6f < SkeletonTableXZ.w;

            if (insideSkeletonXZ && pos.y - bottomOffset < SkeletonTableTopY)
            {
                pos = PushOutHorizontal(pos, SkeletonTableXZ, Mathf.Max(halfX, halfZ), ref vel);
                clamped = true;
            }

            if (clamped)
            {
                if (_isFloating && !IsHeldInVR)
                {
                    _floatingPosition = pos;
                }
                transform.position = pos;
                if (_rb != null && !_rb.isKinematic)
                {
                    _rb.position = pos;
                    if (dampedOnSurface)
                    {
                        vel.x *= 0.85f;
                        vel.z *= 0.85f;
                        _rb.angularVelocity *= 0.80f;
                    }
#if UNITY_6000_0_OR_NEWER
                    _rb.linearVelocity = vel;
#else
                    _rb.velocity = vel;
#endif
                }
            }
        }

        public Bounds CalculateWorldBounds(Vector3 fallbackCenter)
        {
            if (_colliders == null || _colliders.Length == 0)
                RefreshOwnColliders();

            bool hasBounds = false;
            Bounds combined = new Bounds(fallbackCenter, new Vector3(0.08f, 0.08f, 0.08f));

            if (_colliders != null)
            {
                for (int i = 0; i < _colliders.Length; i++)
                {
                    Collider c = _colliders[i];
                    if (c == null || !c.enabled || c.isTrigger) continue;
                    if (!hasBounds)
                    {
                        combined = c.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        combined.Encapsulate(c.bounds);
                    }
                }
            }

            return combined;
        }

        private static Vector3 PushOutHorizontal(Vector3 pos, Vector4 rectXZ, float radius, ref Vector3 vel)
        {
            float minX = rectXZ.x - radius;
            float maxX = rectXZ.y + radius;
            float minZ = rectXZ.z - radius;
            float maxZ = rectXZ.w + radius;

            float dLeft  = Mathf.Abs(pos.x - minX);
            float dRight = Mathf.Abs(maxX - pos.x);
            float dBack  = Mathf.Abs(pos.z - minZ);
            float dFront = Mathf.Abs(maxZ - pos.z);

            float minDist = Mathf.Min(Mathf.Min(dLeft, dRight), Mathf.Min(dBack, dFront));
            if (Mathf.Approximately(minDist, dLeft))
            {
                pos.x = minX;
                if (vel.x > 0f) vel.x = 0f;
            }
            else if (Mathf.Approximately(minDist, dRight))
            {
                pos.x = maxX;
                if (vel.x < 0f) vel.x = 0f;
            }
            else if (Mathf.Approximately(minDist, dBack))
            {
                pos.z = minZ;
                if (vel.z > 0f) vel.z = 0f;
            }
            else
            {
                pos.z = maxZ;
                if (vel.z < 0f) vel.z = 0f;
            }

            return pos;
        }
    }
}
