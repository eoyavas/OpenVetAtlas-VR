using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace AnatomiVR.Managers
{
    /// <summary>
    /// Oyun alanının altına düşen kemikleri yakalar ve masaya geri ışınlar.
    /// Kemiklerin sonsuzluğa düşmesini engeller.
    /// </summary>
    public class BoneResetManager : MonoBehaviour
    {
        [Header("Sıfırlama Ayarları")]
        [SerializeField, Tooltip("Bu Y pozisyonunun altına düşen kemikler sıfırlanır.")]
        private float _resetYThreshold = -0.2f;

        [SerializeField, Tooltip("Kemiklerin geri ışınlanacağı konum (SpawnPoint).")]
        private Transform _resetPosition;

        [Header("Cooldown")]
        [SerializeField, Tooltip("Aynı kemik için ardışık sıfırlamalar arasındaki bekleme süresi (saniye).")]
        private float _resetCooldown = 0.5f;

        [Header("Görsel Geri Bildirim")]
        [SerializeField, Tooltip("Sıfırlama sırasında parçacık efekti gösterilsin mi?")]
        private bool _enableResetParticle;

        [SerializeField, Tooltip("Sıfırlama noktasında oluşturulacak parçacık efekti prefab'ı.")]
        private GameObject _resetParticlePrefab;

        public Transform ResetPosition
        {
            get => _resetPosition;
            set => _resetPosition = value;
        }

        public float ResetYThreshold
        {
            get => _resetYThreshold;
            set => _resetYThreshold = value;
        }

        private readonly Dictionary<int, float> _lastResetTimes = new Dictionary<int, float>();

        private void Awake()
        {
            EnsureResetPosition();
        }

        private void OnEnable()
        {
            EnsureResetPosition();
            InvokeRepeating(nameof(CheckAndResetBones), 1.0f, 0.25f);
        }

        private void OnDisable()
        {
            CancelInvoke(nameof(CheckAndResetBones));
        }

        private void OnValidate()
        {
            if (_resetPosition == null)
            {
                GameObject sp = GameObject.Find("[SpawnPoint]") ?? GameObject.Find("SpawnPoint");
                if (sp != null)
                    _resetPosition = sp.transform;
            }
        }

        private void EnsureResetPosition()
        {
            if (_resetPosition != null)
                return;

            GameObject sp = GameObject.Find("[SpawnPoint]") ?? GameObject.Find("SpawnPoint");
            if (sp != null)
            {
                _resetPosition = sp.transform;
                return;
            }

            // Sahnede [SpawnPoint] yoksa otomatik oluştur
            var fallback = new GameObject("[SpawnPoint]");
            fallback.transform.position = new Vector3(0f, 1.24f, 0f);
            fallback.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            _resetPosition = fallback.transform;
        }

        /// <summary>
        /// Sahnedeki tüm <see cref="XRGrabInteractable"/> nesnelerini kontrol eder
        /// ve eşik değerinin altına düşenleri sıfırlar.
        /// </summary>
        private void CheckAndResetBones()
        {
            if (_resetPosition == null)
            {
                EnsureResetPosition();
                if (_resetPosition == null)
                    return;
            }

            XRGrabInteractable[] grabInteractables =
#if UNITY_6000_0_OR_NEWER
                Object.FindObjectsByType<XRGrabInteractable>(FindObjectsInactive.Exclude);
#else
                FindObjectsOfType<XRGrabInteractable>();
#endif

            for (int i = 0; i < grabInteractables.Length; i++)
            {
                XRGrabInteractable interactable = grabInteractables[i];

                if (interactable == null)
                    continue;

                // Şu anda tutulmakta olan kemikleri atla
                if (interactable.isSelected)
                    continue;

                // Y pozisyonu eşik değerinin üstündeyse atla
                if (interactable.transform.position.y >= _resetYThreshold)
                    continue;

                int instanceId = interactable.gameObject.GetHashCode();

                // Cooldown kontrolü
                if (_lastResetTimes.TryGetValue(instanceId, out float lastTime))
                {
                    if (Time.time - lastTime < _resetCooldown)
                        continue;
                }

                ResetBone(interactable, instanceId);
            }
        }

        private void ResetBone(XRGrabInteractable interactable, int instanceId)
        {
            Rigidbody rb = interactable.GetComponent<Rigidbody>();

            if (rb != null)
            {
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector3.zero;
#else
                rb.velocity = Vector3.zero;
#endif
                rb.angularVelocity = Vector3.zero;
            }

            interactable.transform.position = _resetPosition.position;
            interactable.transform.rotation = _resetPosition.rotation;

            _lastResetTimes[instanceId] = Time.time;

            Debug.Log($"[BoneResetManager] '{interactable.gameObject.name}' sıfırlama noktasına geri ışınlandı.", interactable);

            if (_enableResetParticle && _resetParticlePrefab != null)
            {
                GameObject particleInstance = Instantiate(
                    _resetParticlePrefab,
                    _resetPosition.position,
                    Quaternion.identity
                );

                Destroy(particleInstance, 3.0f);
            }
        }
    }
}
