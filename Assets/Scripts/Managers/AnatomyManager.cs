using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using AnatomiVR.Bones;

namespace AnatomiVR.Managers
{
    /// <summary>
    /// Addressables üzerinden kemik prefab'larını yükler, sahneye spawn eder
    /// ve FIFO (First-In-First-Out) kuyruğu ile maksimum kemik sayısını yönetir.
    /// Aynı "Hayvan_Kemik" (örn. "Dog_Scapula") anahtarına sahip kemikten masada
    /// aynı anda yalnızca 1 adet bulunabilir; farklı hayvanların aynı isimli kemiği
    /// (örn. "Dog_Scapula" ve "Horse_Scapula") birlikte masada bulunabilir.
    /// </summary>
    public class AnatomyManager : MonoBehaviour
    {
        // ─────────────────────────── Singleton ───────────────────────────
        public static AnatomyManager Instance { get; private set; }

        /// <summary>Masadaki kemik sayısı değiştiğinde tetiklenir (parametre: güncel kemik sayısı).</summary>
        public event Action<int> OnTableBoneCountChanged;

        // ─────────────────────────── Inspector ───────────────────────────
        [Header("Spawn Ayarları")]
        [Tooltip("Kemiklerin spawn olacağı Transform (masanın ~25cm üstü)")]
        [SerializeField] private Transform spawnPoint;

        [Tooltip("Aynı anda masada/sahnede bulunabilecek maksimum kemik sayısı")]
        [SerializeField] private int maxBonesOnTable = 4;

        [Header("Spawn Offset")]
        [Tooltip("Birden fazla kemik yan yana spawn olurken aralarındaki mesafe")]
        [SerializeField] private float spawnOffsetX = 0.36f;

        // ─────────────────────────── Runtime ─────────────────────────────

        private readonly Queue<SpawnedBoneEntry> _boneQueue = new Queue<SpawnedBoneEntry>();
        private readonly HashSet<string> _loadingKeys = new HashSet<string>();
        private int _spawnCounter;

        private struct SpawnedBoneEntry
        {
            public GameObject Instance;
            public AsyncOperationHandle<GameObject> Handle;
            public string Key;
        }

        // ─────────────────────────── Unity Lifecycle ─────────────────────
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[AnatomyManager] Birden fazla instance tespit edildi. Fazla bileşen yok ediliyor.");
                Destroy(this);
                return;
            }
            Instance = this;

            // VR (72Hz/90Hz) çarpışma kararlılığı için fizik motoru ayarları
            Time.fixedDeltaTime = 1f / 72f;
            Physics.defaultMaxDepenetrationVelocity = 8.0f;
            Physics.defaultSolverIterations = 14;
            Physics.defaultSolverVelocityIterations = 8;

            // Eski alt bilgi metni varsa gizle
            GameObject legacyCountGO = GameObject.Find("BoneCountText");
            if (legacyCountGO != null)
            {
                legacyCountGO.SetActive(false);
            }

            // Sahnedeki maksimum kemik sınırının 4 olduğundan emin ol
            if (maxBonesOnTable < 4)
            {
                maxBonesOnTable = 4;
            }

            // Addressables kataloğunu açılışta hazırla (arka planda kemik modeli yüklemez, sadece kataloğu açar)
            Addressables.InitializeAsync();

            EnsureSpawnPoint();
            NotifyBoneCountChanged();
        }

        private void OnDestroy()
        {
            ReleaseAllBones();

            if (Instance == this)
                Instance = null;
        }

        private void EnsureSpawnPoint()
        {
            if (spawnPoint != null)
                return;

            GameObject sp = GameObject.Find("[SpawnPoint]") ?? GameObject.Find("SpawnPoint");
            if (sp != null)
            {
                spawnPoint = sp.transform;
            }
        }

        private void NotifyBoneCountChanged()
        {
            OnTableBoneCountChanged?.Invoke(_boneQueue.Count);
        }

        // ─────────────────────── Public API ──────────────────────────────

        /// <summary>
        /// UI butonları tarafından çağrılır.
        /// Verilen Addressables key ("Hayvan_Kemik", örn. "Dog_Scapula", "Horse_Scapula")
        /// masada zaten varsa ikinci kez oluşturmaz. Yoksa yükler ve kuyruk doluysa
        /// (>= maxBonesOnTable) en eski kemiği FIFO ile siler.
        /// </summary>
        public void SpawnBone(string addressableKey)
        {
            if (string.IsNullOrEmpty(addressableKey))
            {
                Debug.LogError("[AnatomyManager] Addressable key boş olamaz!");
                return;
            }

            // Aynı "Hayvan_Kemik" (örn. Dog_Scapula) zaten masadaysa veya yükleniyorsa tekrar düşürme!
            // (Farklı hayvanın aynı kemiği, örn. Horse_Scapula farklı key olduğu için düşebilir.)
            if (IsBoneOnTable(addressableKey) || _loadingKeys.Contains(addressableKey))
            {
                Debug.Log($"[AnatomyManager] '{addressableKey}' zaten masada mevcut; aynı kemikten ikinci kez oluşturulmadı.");
                return;
            }

            EnsureSpawnPoint();
            _loadingKeys.Add(addressableKey);

            // FIFO: Kuyruk doluysa en eski kemiği sil
            if (_boneQueue.Count >= maxBonesOnTable)
            {
                DequeuAndRelease();
            }

            Quaternion spawnRot = spawnPoint != null ? spawnPoint.rotation : Quaternion.Euler(0f, 90f, 0f);

            AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(
                addressableKey,
                GetNextSpawnPosition(),
                spawnRot,
                parent: null,
                trackHandle: true
            );

            handle.Completed += op => OnBoneLoaded(op, addressableKey);
        }

        public void ClearAllBones()
        {
            ReleaseAllBones();
            _loadingKeys.Clear();
            NotifyBoneCountChanged();
            Debug.Log("[AnatomyManager] Tüm kemikler temizlendi.");
        }

        /// <summary>
        /// Kullanıcının sağ veya sol kumandayla elinde tuttuğu kemiği R3/L3 (Joystick tıklaması)
        /// ile tekil olarak silmesini sağlar. Silinen kemik kuyruktan çıkarılır ve tekrar menüden
        /// çağrılabilir hale gelir.
        /// </summary>
        public bool RemoveSpecificBone(GameObject boneObject)
        {
            if (boneObject == null)
                return false;

            bool found = false;
            int count = _boneQueue.Count;
            for (int i = 0; i < count; i++)
            {
                SpawnedBoneEntry entry = _boneQueue.Dequeue();
                if (!found && entry.Instance == boneObject)
                {
                    found = true;
                    Debug.Log($"[AnatomyManager] Eldeki kemik silindi (L3/R3): {entry.Key}");
                    if (entry.Handle.IsValid())
                    {
                        Addressables.ReleaseInstance(entry.Instance);
                    }
                    else
                    {
                        Destroy(entry.Instance);
                    }
                }
                else if (entry.Instance != null)
                {
                    _boneQueue.Enqueue(entry);
                }
            }

            if (!found)
            {
                BoneIdentifier id = boneObject.GetComponent<BoneIdentifier>();
                if (id != null && id.InstantiateHandle.IsValid())
                {
                    Addressables.ReleaseInstance(boneObject);
                }
                else
                {
                    Destroy(boneObject);
                }
                found = true;
            }

            NotifyBoneCountChanged();
            return found;
        }

        public int CurrentBoneCount => _boneQueue.Count;

        /// <summary>
        /// Belirli bir "Hayvan_Kemik" anahtarının (örn. "Dog_Scapula") şu anda
        /// masada/sahnede aktif olup olmadığını kontrol eder.
        /// </summary>
        public bool IsBoneOnTable(string addressableKey)
        {
            foreach (var entry in _boneQueue)
            {
                if (entry.Key == addressableKey && entry.Instance != null)
                    return true;
            }
            return false;
        }

        // ─────────────────────── Addressables Callback ───────────────────

        private void OnBoneLoaded(AsyncOperationHandle<GameObject> handle, string key)
        {
            _loadingKeys.Remove(key);

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError($"[AnatomyManager] Kemik yüklenemedi! Key: {key}, Status: {handle.Status}");

                if (handle.IsValid())
                    Addressables.Release(handle);

                return;
            }

            GameObject boneInstance = handle.Result;

            // Eğer yükleme sırasında aynı key bir şekilde eklendiyse yeni kopyayı hemen temizle
            if (IsBoneOnTable(key))
            {
                Addressables.ReleaseInstance(boneInstance);
                return;
            }

            // Masaya bastırıldığında titremeyi (collision jitter) önleyen fizik & XRGrabInteractable ayarları
            ConfigureAntiJitterPhysics(boneInstance);

            BoneIdentifier identifier = boneInstance.GetComponent<BoneIdentifier>();
            if (identifier == null)
            {
                identifier = boneInstance.AddComponent<BoneIdentifier>();
            }
            identifier.AddressableKey = key;
            identifier.InstantiateHandle = handle;

            SpawnedBoneEntry entry = new SpawnedBoneEntry
            {
                Instance = boneInstance,
                Handle = handle,
                Key = key
            };
            _boneQueue.Enqueue(entry);
            _spawnCounter++;

            NotifyBoneCountChanged();
            Debug.Log($"[AnatomyManager] Kemik spawn edildi: {key} | Masadaki toplam: {_boneQueue.Count}/{maxBonesOnTable}");
        }

        private static void ConfigureAntiJitterPhysics(GameObject boneInstance)
        {
            if (boneInstance == null) return;

            Rigidbody rb = boneInstance.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.maxDepenetrationVelocity = 8.0f;
                rb.solverIterations = 16;
                rb.solverVelocityIterations = 8;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

            XRGrabInteractable grab = boneInstance.GetComponent<XRGrabInteractable>();
            if (grab != null)
            {
                grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
                grab.tightenPosition = 0f;
                grab.tightenRotation = 0f;
                grab.smoothPosition = true;
                grab.smoothPositionAmount = 16f;
                grab.smoothRotation = true;
                grab.smoothRotationAmount = 14f;
                grab.velocityDamping = 1.0f;
                grab.velocityScale = 0.65f;
                grab.angularVelocityDamping = 1.0f;
                grab.angularVelocityScale = 0.65f;
            }
        }

        // ─────────────────────── FIFO Yönetimi ──────────────────────────

        private void DequeuAndRelease()
        {
            if (_boneQueue.Count == 0) return;

            SpawnedBoneEntry oldest = _boneQueue.Dequeue();

            Debug.Log($"[AnatomyManager] FIFO: En eski kemik siliniyor → {oldest.Key}");

            if (oldest.Instance != null)
            {
                if (oldest.Handle.IsValid())
                {
                    Addressables.ReleaseInstance(oldest.Instance);
                }
                else
                {
                    Destroy(oldest.Instance);
                }
            }

            NotifyBoneCountChanged();
        }

        private void ReleaseAllBones()
        {
            while (_boneQueue.Count > 0)
            {
                DequeuAndRelease();
            }
        }

        // ─────────────────────── Spawn Pozisyonu ────────────────────────

        private Vector3 GetNextSpawnPosition()
        {
            if (spawnPoint == null)
            {
                return new Vector3(0f, 1.24f, 0f);
            }

            // 4 yuva arasında mevcut kemiklerden en uzak/boş olan yuvayı seç
            int slots = Mathf.Max(1, maxBonesOnTable);
            Vector3 rightAxis = spawnPoint.right.sqrMagnitude > 0.01f ? spawnPoint.right : Vector3.forward;

            int bestSlot = _spawnCounter % slots;
            float bestMinDist = -1f;

            for (int s = 0; s < slots; s++)
            {
                float centered = s - (slots - 1) * 0.5f;
                Vector3 candidatePos = spawnPoint.position + rightAxis * (centered * spawnOffsetX);

                float minDistToExisting = float.MaxValue;
                foreach (var entry in _boneQueue)
                {
                    if (entry.Instance == null) continue;
                    float d = Vector3.Distance(candidatePos, entry.Instance.transform.position);
                    if (d < minDistToExisting)
                        minDistToExisting = d;
                }

                if (minDistToExisting > bestMinDist)
                {
                    bestMinDist = minDistToExisting;
                    bestSlot = s;
                }
            }

            float finalCentered = bestSlot - (slots - 1) * 0.5f;
            return spawnPoint.position + rightAxis * (finalCentered * spawnOffsetX);
        }

        private void OnValidate()
        {
            if (maxBonesOnTable < 1)
            {
                maxBonesOnTable = 1;
            }
        }
    }
}
