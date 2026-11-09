using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;
using AnatomiVR.Data;
using AnatomiVR.Managers;

namespace AnatomiVR.UI
{
    /// <summary>
    /// 4 seviyeli menü hiyerarşisini yönetir: Hayvan → Sistem → Bölge → Kemik.
    /// World-Space Canvas üzerindeki paneller arasında navigasyon sağlar.
    /// Kilit sistemini ve buton durumlarını kontrol eder.
    /// 
    /// Kullanım:
    ///   SmartScreen Canvas'ındaki Manager objesine ekle.
    ///   Inspector'dan panelleri ve MenuData ScriptableObject'i ata.
    /// </summary>
    public class UIMenuManager : MonoBehaviour
    {
        // ─────────────────────────── Inspector ───────────────────────────

        [Header("Veri Kaynağı")]
        [Tooltip("Menü hiyerarşisi verileri (ScriptableObject)")]
        [SerializeField] private MenuData menuData;

        [Header("Panel Referansları")]
        [Tooltip("Hayvan seçimi paneli")]
        [SerializeField] private GameObject animalPanel;

        [Tooltip("Sistem seçimi paneli")]
        [SerializeField] private GameObject systemPanel;

        [Tooltip("Bölge seçimi paneli")]
        [SerializeField] private GameObject regionPanel;

        [Tooltip("Kemik seçimi paneli")]
        [SerializeField] private GameObject bonePanel;

        [Header("Buton Container'ları")]
        [Tooltip("Hayvan butonlarının parent'ı (ScrollView Content veya GridLayout)")]
        [SerializeField] private Transform animalButtonContainer;

        [Tooltip("Sistem butonlarının parent'ı")]
        [SerializeField] private Transform systemButtonContainer;

        [Tooltip("Bölge butonlarının parent'ı")]
        [SerializeField] private Transform regionButtonContainer;

        [Tooltip("Kemik butonlarının parent'ı")]
        [SerializeField] private Transform boneButtonContainer;

        [Header("Prefab")]
        [Tooltip("Dinamik olarak oluşturulacak menü butonu prefab'ı")]
        [SerializeField] private GameObject menuButtonPrefab;

        [Header("Geri Butonu")]
        [Tooltip("Bir önceki panele dönme butonu")]
        [SerializeField] private Button backButton;

        [Header("Başlık")]
        [Tooltip("Aktif panelin başlığını gösteren text")]
        [SerializeField] private TextMeshProUGUI headerText;

        [Header("Görsel Ayarlar")]
        [Tooltip("Kilitli buton rengi")]
        [SerializeField] private Color lockedColor = new Color(0.4f, 0.4f, 0.4f, 1f);

        [Tooltip("Aktif (kilitsiz) buton rengi")]
        [SerializeField] private Color unlockedColor = Color.white;

        [Tooltip("Kilit ikonu (buton üzerine eklenir)")]
        [SerializeField] private Sprite lockIcon;

        [Tooltip("Aynı kemikten masaya birden fazla çağrılmasına izin verilsin mi?")]
        [SerializeField] private bool allowDuplicateBones = false;

        // ─────────────────────────── Runtime ─────────────────────────────

        /// <summary>Navigasyon geçmişi (geri butonu için)</summary>
        private readonly Stack<MenuLevel> _navigationStack = new Stack<MenuLevel>();

        /// <summary>Şu anki seçili hayvan index'i</summary>
        private int _selectedAnimalIndex;

        /// <summary>Şu anki seçili sistem index'i</summary>
        private int _selectedSystemIndex;

        /// <summary>Şu anki seçili bölge index'i</summary>
        private int _selectedRegionIndex;

        private bool _isMainMenuOpen = true;
        private Button _closeMenuButton;
        private Button _openMenuButton;
        private Button _clearTableButton;
        private Button _openTutorialHelpButton;
        private Canvas _rootCanvas;
        private CanvasGroup _rootCanvasGroup;
        private float _menuRayBlockUntilTime;

        // ─────────────── Açılış Kullanım Rehberi: Havada Asılı Taşınabilir Tablet ([TutorialTablet]) ───────────────
        private GameObject _tutorialOverlayGO;
        private Rigidbody _tutorialTabletRb;
        private XRGrabInteractable _tutorialTabletGrab;
        private Vector3 _tabletSuspendedPos = new Vector3(-0.62f, 1.44f, 0.72f);
        private Quaternion _tabletSuspendedRot = Quaternion.Euler(4f, 45f, 0f);
        private static readonly Vector3 DefaultTabletWorldPos = new Vector3(-0.62f, 1.44f, 0.72f);
        private static readonly Quaternion DefaultTabletWorldRot = Quaternion.Euler(4f, 45f, 0f);

        private TextMeshProUGUI _tutorialMainHeaderTmp;
        private TextMeshProUGUI _tutorialGripHintTmp;
        private TextMeshProUGUI _tutorialLangBtnTmp;
        private TextMeshProUGUI _tutorialSkipBtnTmp;
        private TextMeshProUGUI _tutorialStepTitleTmp;
        private TextMeshProUGUI _tutorialStepBodyTmp;
        private TextMeshProUGUI _tutorialStepDotsTmp;
        private TextMeshProUGUI _tutorialPrevBtnTmp;
        private TextMeshProUGUI _tutorialNextBtnTmp;
        private Button _tutorialPrevBtn;
        private Button _tutorialNextBtn;
        private Image _tutorialNextBtnImg;
        private int _tutorialStepIndex;
        private bool _isTutorialEnglish;
        private bool _isTutorialOpen;
        private Sprite _cachedOvaLogoSprite;

        private struct TutorialPageContent
        {
            public string TitleTR;
            public string BodyTR;
            public string TitleEN;
            public string BodyEN;
        }

        private static readonly TutorialPageContent[] TutorialPages = new[]
        {
            new TutorialPageContent
            {
                TitleTR = "ADIM 1 / 4   —   MENÜ VE KEMİK ÇAĞIRMA",
                BodyTR  = "Karşınızdaki Akıllı Ekrana kumanda lazeriyle nişan alıp Tetik (Trigger - İşaret Parmağı) tuşuyla tür, sistem ve bölge seçin.\n\nSeçtiğiniz kemik diseksiyon masasının üzerinde belirecektir. Masada aynı anda en fazla 4 farklı kemik inceleyebilirsiniz.",
                TitleEN = "STEP 1 / 4   —   MENU & SPAWNING BONES",
                BodyEN  = "Aim at the Smart Screen with your controller laser and press the Trigger (Index Finger) button to select a species, system, and region.\n\nThe selected bone will appear on the dissection table. You can examine up to 4 different bones on the table at once."
            },
            new TutorialPageContent
            {
                TitleTR = "ADIM 2 / 4   —   KEMİĞİ TUTMA VE HAVADA SABİTLEME",
                BodyTR  = "Kemiği Kavrama (Grip - Orta Parmak) tuşuyla tutun. Havada bıraktığınızda kemik asılı kalır.\n\nKemiği tutarken kolunuzu ileri iterseniz kemik uzaklaşır, kendinize çekerseniz kemik size yakınlaşır.",
                TitleEN = "STEP 2 / 4   —   GRABBING & MID-AIR SUSPENSION",
                BodyEN  = "Grab the bone using the Grip (Middle Finger) button. When released in mid-air, the bone remains suspended.\n\nWhile holding the bone, pushing your arm forward moves the bone further away, and pulling your arm toward yourself brings the bone closer to you."
            },
            new TutorialPageContent
            {
                TitleTR = "ADIM 3 / 4   —   ANATOMİK PİNLER VE BİLGİ KARTLARI",
                BodyTR  = "Sol kumandadaki [ X ] veya sağ kumandadaki [ A ] tuşuna basarak elinizdeki veya size en yakın kemik üzerindeki mavi anatomik noktaları (pinleri) açıp kapatın.\n\nLazerle bir noktaya nişan alıp nokta sarıya döndüğünde Tetik (Trigger) tuşuna bir kez tıklayarak bilgi kartını açın veya kapatın.",
                TitleEN = "STEP 3 / 4   —   ANATOMICAL PINS & INFO CARDS",
                BodyEN  = "Press [ X ] on the left controller or [ A ] on the right controller to toggle the blue anatomical pins on the held or closest bone.\n\nAim at a pin with the laser until it turns yellow, then click the Trigger button once to open or close its info card."
            },
            new TutorialPageContent
            {
                TitleTR = "ADIM 4 / 4   —   KEMİK SİLME VE HAREKET",
                BodyTR  = "Elinizde tuttuğunuz tek bir kemiği silmek için tuttuğunuz elin Joystick butonuna (L3 veya R3) tıklayın. Tüm masayı temizlemek için ekrandaki [ Masayı Temizle ] butonunu kullanın.\n\nSol Joystick ile laboratuvarda yürüyebilir, Sağ Joystick ile etrafınıza dönebilirsiniz.",
                TitleEN = "STEP 4 / 4   —   DELETING BONES & NAVIGATION",
                BodyEN  = "To delete a single bone you are holding, click the Thumbstick button (L3 or R3) on that hand. Use the [ Clear Table ] button on the screen to remove all bones.\n\nUse the Left Thumbstick to walk around the lab and the Right Thumbstick to turn."
            }
        };

        /// <summary>Menü seviyesi enum'u</summary>
        private enum MenuLevel
        {
            Animal,
            System,
            Region,
            Bone
        }

        // ─────────────────────── Unity Lifecycle ─────────────────────────

        private void Start()
        {
            if (menuData == null)
            {
                Debug.LogError("[UIMenuManager] MenuData atanmamış! Inspector'dan ata.");
                return;
            }

            // Masaüstü test kontrolcüsü yoksa otomatik ekle
            if (FindAnyObjectByType<AnatomiVR.XR.DesktopTestController>() == null)
            {
                gameObject.AddComponent<AnatomiVR.XR.DesktopTestController>();
            }

            EnsureMenuToggleButtons();

            if (AnatomyManager.Instance != null)
            {
                AnatomyManager.Instance.OnTableBoneCountChanged -= HandleTableBoneCountChanged;
                AnatomyManager.Instance.OnTableBoneCountChanged += HandleTableBoneCountChanged;
                HandleTableBoneCountChanged(AnatomyManager.Instance.CurrentBoneCount);
            }

            // Başlangıçta hayvan panelini göster
            ShowAnimalPanel();

            // Geri butonu
            if (backButton != null)
                backButton.onClick.AddListener(OnBackButtonClicked);

            // Her uygulama açılışında Türkçe / İngilizce, İleri/Geri sayfalı ve Atlanabilir (Skippable)
            // Kullanım Rehberi (Tutorial) ekranını otomatik olarak aç:
            OpenTutorialOverlay(resetToFirstStep: true);
        }

        private void Update()
        {
            // Kemik havada asılıyken veya kemik üzerindeki küreciklere lazerle tıklanırken
            // lazerin kemiği delip arkadaki Akıllı Ekran menüsüne tıklamasını %100 engelle:
            if (_rootCanvas == null && animalPanel != null)
                _rootCanvas = animalPanel.GetComponentInParent<Canvas>();

            if (_rootCanvas != null)
            {
                if (_rootCanvasGroup == null)
                {
                    _rootCanvasGroup = _rootCanvas.GetComponent<CanvasGroup>();
                    if (_rootCanvasGroup == null)
                        _rootCanvasGroup = _rootCanvas.gameObject.AddComponent<CanvasGroup>();
                }

                bool blockedByBoneOrPin = AnatomiVR.Bones.BoneAnatomicalLabels.IsAnyRayBlockedByBoneOrPin();
                if (blockedByBoneOrPin)
                {
                    _menuRayBlockUntilTime = Time.unscaledTime + 0.25f;
                }

                bool shouldBlockMenu = blockedByBoneOrPin || (Time.unscaledTime < _menuRayBlockUntilTime);
                if (_rootCanvasGroup.blocksRaycasts == shouldBlockMenu)
                {
                    _rootCanvasGroup.blocksRaycasts = !shouldBlockMenu;
                }
            }
        }

        private void LateUpdate()
        {
            // Havada asılı taşınabilir Rehber Tableti ([TutorialTablet]):
            // Grip (Kavrama) tuşuyla tutulduğunda serbestçe taşınır/döndürülür;
            // bırakıldığı anda yerçekimsiz olarak havada tam bırakıldığı konum ve açıda sabit kalır.
            if (_tutorialOverlayGO != null && _tutorialOverlayGO.activeInHierarchy && _tutorialTabletGrab != null)
            {
                if (_tutorialTabletGrab.isSelected)
                {
                    _tabletSuspendedPos = _tutorialOverlayGO.transform.position;
                    _tabletSuspendedRot = _tutorialOverlayGO.transform.rotation;
                }
                else
                {
                    if (_tutorialTabletRb != null && !_tutorialTabletRb.isKinematic)
                    {
#if UNITY_6000_0_OR_NEWER
                        _tutorialTabletRb.linearVelocity = Vector3.zero;
#else
                        _tutorialTabletRb.velocity = Vector3.zero;
#endif
                        _tutorialTabletRb.angularVelocity = Vector3.zero;
                    }
                    _tutorialOverlayGO.transform.SetPositionAndRotation(_tabletSuspendedPos, _tabletSuspendedRot);
                }
            }
        }

        private void OnDestroy()
        {
            if (AnatomyManager.Instance != null)
            {
                AnatomyManager.Instance.OnTableBoneCountChanged -= HandleTableBoneCountChanged;
            }
        }

        private void HandleTableBoneCountChanged(int boneCount)
        {
            if (_clearTableButton != null)
            {
                _clearTableButton.gameObject.SetActive(_isMainMenuOpen && boneCount > 0);
            }
        }

        private Sprite GetOvaLogoSprite()
        {
            if (_cachedOvaLogoSprite != null)
                return _cachedOvaLogoSprite;

            _cachedOvaLogoSprite = Resources.Load<Sprite>("OVA_Header_Logo");
            if (_cachedOvaLogoSprite == null)
            {
                Texture2D tex = Resources.Load<Texture2D>("OVA_Header_Logo");
                if (tex != null)
                {
                    _cachedOvaLogoSprite = Sprite.Create(
                        tex,
                        new Rect(0f, 0f, tex.width, tex.height),
                        new Vector2(0.5f, 0.5f),
                        100f);
                }
            }
            return _cachedOvaLogoSprite;
        }

        // ─────────────── Büyük Menüyü Kapatma / Açma (Toggle) ────────────

        /// <summary>
        /// Büyük Akıllı Ekran menüsünü açar veya kapatır (M / Tab tuşu veya sağ üst X butonu).
        /// </summary>
        public void ToggleMainMenu()
        {
            SetMainMenuVisible(!_isMainMenuOpen);
        }

        public void SetMainMenuVisible(bool visible)
        {
            _isMainMenuOpen = visible;

            if (_rootCanvas == null && animalPanel != null)
                _rootCanvas = animalPanel.GetComponentInParent<Canvas>();

            if (_rootCanvas == null)
                return;

            foreach (Transform child in _rootCanvas.transform)
            {
                if (_openMenuButton != null && child == _openMenuButton.transform)
                {
                    child.gameObject.SetActive(!visible);
                    continue;
                }

                // Paneller: Sadece aktif menü seviyesindeki panel açık kalmalı
                if (child.gameObject == animalPanel ||
                    child.gameObject == systemPanel ||
                    child.gameObject == regionPanel ||
                    child.gameObject == bonePanel)
                {
                    if (!visible)
                        child.gameObject.SetActive(false);
                    continue;
                }

                if (backButton != null && child == backButton.transform)
                {
                    child.gameObject.SetActive(visible && _navigationStack.Count > 1);
                    continue;
                }

                if (_clearTableButton != null && child == _clearTableButton.transform)
                {
                    int count = AnatomyManager.Instance != null ? AnatomyManager.Instance.CurrentBoneCount : 0;
                    child.gameObject.SetActive(visible && count > 0);
                    continue;
                }

                if (child.name == "BoneCountText")
                {
                    child.gameObject.SetActive(false);
                    continue;
                }

                child.gameObject.SetActive(visible);
            }

            if (visible)
            {
                // Mevcut menü seviyesinin panelini tekrar görünür yap
                MenuLevel current = _navigationStack.Count > 0 ? _navigationStack.Peek() : MenuLevel.Animal;
                if (animalPanel != null) animalPanel.SetActive(current == MenuLevel.Animal);
                if (systemPanel != null) systemPanel.SetActive(current == MenuLevel.System);
                if (regionPanel != null) regionPanel.SetActive(current == MenuLevel.Region);
                if (bonePanel != null)   bonePanel.SetActive(current == MenuLevel.Bone);
            }
        }

        private void EnsureMenuToggleButtons()
        {
            if (animalPanel != null)
                _rootCanvas = animalPanel.GetComponentInParent<Canvas>();
            if (_rootCanvas == null)
                return;

            TMP_FontAsset font = headerText != null ? headerText.font : TMP_Settings.defaultFontAsset;

            // 0. Akıllı Ekran Üst Barına OpenVetAtlas VR (OVA VR) Logo Rozeti
            Sprite ovaSprite = GetOvaLogoSprite();
            if (ovaSprite != null)
            {
                Transform existingLogo = _rootCanvas.transform.Find("OVAHeaderLogoBadge");
                if (existingLogo == null)
                {
                    var logoGO = new GameObject("OVAHeaderLogoBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                    logoGO.transform.SetParent(_rootCanvas.transform, false);
                    var lrt = logoGO.GetComponent<RectTransform>();
                    lrt.anchorMin = new Vector2(0f, 1f);
                    lrt.anchorMax = new Vector2(0f, 1f);
                    lrt.pivot = new Vector2(0f, 1f);
                    lrt.anchoredPosition = new Vector2(218f, -10f);
                    lrt.sizeDelta = new Vector2(215f, 86f);
                    var limg = logoGO.GetComponent<Image>();
                    limg.sprite = ovaSprite;
                    limg.preserveAspect = true;
                    limg.raycastTarget = false;
                }
            }

            // 1. Sağ üst '✕ Menüyü Kapat' butonu
            Transform existingClose = _rootCanvas.transform.Find("CloseMenuButton");
            if (existingClose == null)
            {
                var closeGO = new GameObject("CloseMenuButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                closeGO.transform.SetParent(_rootCanvas.transform, false);
                var rt = closeGO.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-25f, -20f);
                rt.sizeDelta = new Vector2(230f, 68f);
                closeGO.GetComponent<Image>().color = new Color(0.65f, 0.18f, 0.22f, 0.96f);

                var txtGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                txtGO.transform.SetParent(closeGO.transform, false);
                var trt = txtGO.GetComponent<RectTransform>();
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.sizeDelta = Vector2.zero;
                var tmp = txtGO.GetComponent<TextMeshProUGUI>();
                if (font != null) tmp.font = font;
                tmp.text = "[X] Menüyü Kapat [M]";
                tmp.fontSize = 22f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;

                _closeMenuButton = closeGO.GetComponent<Button>();
            }
            else
            {
                _closeMenuButton = existingClose.GetComponent<Button>();
            }

            if (_closeMenuButton != null)
            {
                _closeMenuButton.onClick.RemoveAllListeners();
                _closeMenuButton.onClick.AddListener(() => SetMainMenuVisible(false));
            }

            // 1.5. Ana menünün SAĞ ALT köşesinde yer alan '[ ? ] Rehber' butonu
            Transform existingHelp = _rootCanvas.transform.Find("OpenTutorialHelpButton");
            if (existingHelp == null)
            {
                var helpGO = new GameObject("OpenTutorialHelpButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Button));
                helpGO.transform.SetParent(_rootCanvas.transform, false);
                var rt = helpGO.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 0f);
                rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(1f, 0f);
                rt.anchoredPosition = new Vector2(-32f, 18f);
                rt.sizeDelta = new Vector2(230f, 68f);
                helpGO.GetComponent<Image>().color = new Color(0.06f, 0.26f, 0.44f, 0.96f);
                var helpOutline = helpGO.GetComponent<Outline>();
                helpOutline.effectColor = new Color(0.00f, 0.88f, 1.00f, 0.85f);
                helpOutline.effectDistance = new Vector2(2f, 2f);

                var txtGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                txtGO.transform.SetParent(helpGO.transform, false);
                var trt = txtGO.GetComponent<RectTransform>();
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.sizeDelta = Vector2.zero;
                var tmp = txtGO.GetComponent<TextMeshProUGUI>();
                if (font != null) tmp.font = font;
                tmp.text = "[ ? ]   Rehber";
                tmp.fontSize = 24f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.characterSpacing = 2f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = new Color(0.90f, 0.98f, 1.00f, 1f);

                _openTutorialHelpButton = helpGO.GetComponent<Button>();
            }
            else
            {
                var rt = existingHelp.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(1f, 0f);
                    rt.anchorMax = new Vector2(1f, 0f);
                    rt.pivot = new Vector2(1f, 0f);
                    rt.anchoredPosition = new Vector2(-32f, 18f);
                    rt.sizeDelta = new Vector2(230f, 68f);
                }
                _openTutorialHelpButton = existingHelp.GetComponent<Button>();
            }

            if (_openTutorialHelpButton != null)
            {
                _openTutorialHelpButton.onClick.RemoveAllListeners();
                _openTutorialHelpButton.onClick.AddListener(() => OpenTutorialOverlay(resetToFirstStep: true));
            }

            // 1.6. Ana menünün SOL ALT köşesinde yer alan uygulama geneli '[ TR | EN ]' Dil Geçiş Butonu
            Transform existingLang = _rootCanvas.transform.Find("MainLanguageToggleButton");
            if (existingLang == null)
            {
                var langGO = CreateStylizedTutorialButton(
                    _rootCanvas.transform,
                    "MainLanguageToggleButton",
                    new Vector2(0f, 0f),
                    new Vector2(0f, 0f),
                    new Vector2(0f, 0f),
                    new Vector2(32f, 18f),
                    new Vector2(230f, 68f),
                    new Color(0.06f, 0.26f, 0.44f, 0.96f),
                    new Color(0.00f, 0.88f, 1.00f, 0.85f),
                    font,
                    24f,
                    out _tutorialLangBtnTmp);
                langGO.GetComponent<Button>().onClick.AddListener(ToggleTutorialLanguage);
            }
            else
            {
                var rt = existingLang.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0f, 0f);
                    rt.anchorMax = new Vector2(0f, 0f);
                    rt.pivot = new Vector2(0f, 0f);
                    rt.anchoredPosition = new Vector2(32f, 18f);
                    rt.sizeDelta = new Vector2(230f, 68f);
                }
                _tutorialLangBtnTmp = existingLang.GetComponentInChildren<TextMeshProUGUI>(true);
                var btn = existingLang.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(ToggleTutorialLanguage);
                }
            }

            // 2. Menü kapalıyken sağ üstte beliren küçük '☰ Menüyü Aç' butonu
            Transform existingOpen = _rootCanvas.transform.Find("OpenMenuButton");
            if (existingOpen == null)
            {
                var openGO = new GameObject("OpenMenuButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                openGO.transform.SetParent(_rootCanvas.transform, false);
                var rt = openGO.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.anchoredPosition = new Vector2(-25f, -20f);
                rt.sizeDelta = new Vector2(250f, 74f);
                openGO.GetComponent<Image>().color = new Color(0.14f, 0.50f, 0.82f, 0.96f);

                var txtGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                txtGO.transform.SetParent(openGO.transform, false);
                var trt = txtGO.GetComponent<RectTransform>();
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.sizeDelta = Vector2.zero;
                var tmp = txtGO.GetComponent<TextMeshProUGUI>();
                if (font != null) tmp.font = font;
                tmp.text = "☰ Menüyü Aç [M]";
                tmp.fontSize = 24f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = Color.white;

                openGO.SetActive(false);
                _openMenuButton = openGO.GetComponent<Button>();
            }
            else
            {
                _openMenuButton = existingOpen.GetComponent<Button>();
            }

            if (_openMenuButton != null)
            {
                _openMenuButton.onClick.RemoveAllListeners();
                _openMenuButton.onClick.AddListener(() => SetMainMenuVisible(true));
            }

            // 3. Eski alt bilgi metnini (BoneCountText) tamamen kaldır/gizle
            Transform oldInfo = _rootCanvas.transform.Find("BoneCountText");
            if (oldInfo != null)
            {
                oldInfo.gameObject.SetActive(false);
            }

            // 4. Kemik getirildikten sonra beliren fütüristik 'MASAYI TEMİZLE' butonu
            Transform existingClear = _rootCanvas.transform.Find("ClearTableButton");
            if (existingClear == null)
            {
                var clearGO = new GameObject("ClearTableButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Button));
                clearGO.transform.SetParent(_rootCanvas.transform, false);
                var rt = clearGO.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 18f);
                rt.sizeDelta = new Vector2(460f, 68f);

                var img = clearGO.GetComponent<Image>();
                img.color = new Color(0.48f, 0.10f, 0.18f, 0.96f);

                var outline = clearGO.GetComponent<Outline>();
                outline.effectColor = new Color(1.0f, 0.32f, 0.42f, 0.85f);
                outline.effectDistance = new Vector2(2f, 2f);

                var txtGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                txtGO.transform.SetParent(clearGO.transform, false);
                var trt = txtGO.GetComponent<RectTransform>();
                trt.anchorMin = Vector2.zero;
                trt.anchorMax = Vector2.one;
                trt.sizeDelta = Vector2.zero;
                var tmp = txtGO.GetComponent<TextMeshProUGUI>();
                if (font != null) tmp.font = font;
                tmp.text = "[ X ]   MASAYI TEMİZLE";
                tmp.fontSize = 25f;
                tmp.fontStyle = FontStyles.Bold;
                tmp.characterSpacing = 3f;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.color = new Color(1.0f, 0.92f, 0.94f, 1f);

                _clearTableButton = clearGO.GetComponent<Button>();
            }
            else
            {
                _clearTableButton = existingClear.GetComponent<Button>();
            }

            if (_clearTableButton != null)
            {
                _clearTableButton.onClick.RemoveAllListeners();
                _clearTableButton.onClick.AddListener(() =>
                {
                    if (AnatomyManager.Instance != null)
                    {
                        AnatomyManager.Instance.ClearAllBones();
                    }
                });
                int currentCount = AnatomyManager.Instance != null ? AnatomyManager.Instance.CurrentBoneCount : 0;
                _clearTableButton.gameObject.SetActive(_isMainMenuOpen && currentCount > 0);
            }

            // 5. Havada Asılı Kalabilen, Grip Tuşuyla Taşınabilen Bağımsız 3B Rehber Tableti ([TutorialTablet])
            EnsureTutorialOverlay(font);
        }

        private void EnsureTutorialOverlay(TMP_FontAsset font)
        {
            if (_rootCanvas != null)
            {
                Transform oldChildTut = _rootCanvas.transform.Find("TutorialOverlayPanel");
                if (oldChildTut != null)
                {
                    Destroy(oldChildTut.gameObject);
                }
            }

            if (_tutorialOverlayGO != null)
            {
                Destroy(_tutorialOverlayGO);
            }

            GameObject existingTablet = GameObject.Find("[TutorialTablet]");
            if (existingTablet != null)
            {
                Destroy(existingTablet);
            }

            // Kullanıcının sol-ön tarafında (kafasını hafif sola çevirdiğinde göz hizasında) havada asılı duran
            // ve Grip (Kavrama) tuşuyla istenilen yere taşınabilen bağımsız 3B Tablet:
            _tabletSuspendedPos = DefaultTabletWorldPos;
            _tabletSuspendedRot = DefaultTabletWorldRot;

            _tutorialOverlayGO = new GameObject("[TutorialTablet]");
            _tutorialOverlayGO.transform.SetPositionAndRotation(_tabletSuspendedPos, _tabletSuspendedRot);

            // 1. 3B Fiziksel Tablet Gövdesi (Koyu Cyber-Navy Kasa + Alt Tutma Çubuğu)
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

            var bodyCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bodyCube.name = "TabletBodyMesh";
            bodyCube.transform.SetParent(_tutorialOverlayGO.transform, false);
            bodyCube.transform.localPosition = new Vector3(0f, 0f, 0.014f);
            bodyCube.transform.localScale = new Vector3(0.73f, 0.48f, 0.020f);
            Collider bodyCubeCol = bodyCube.GetComponent<Collider>();
            if (bodyCubeCol != null) Destroy(bodyCubeCol);
            var bodyRenderer = bodyCube.GetComponent<MeshRenderer>();
            if (bodyRenderer != null && unlitShader != null)
            {
                var bodyMat = new Material(unlitShader);
                bodyMat.color = new Color(0.05f, 0.11f, 0.19f, 1f);
                if (bodyMat.HasProperty("_BaseColor")) bodyMat.SetColor("_BaseColor", bodyMat.color);
                bodyRenderer.sharedMaterial = bodyMat;
                bodyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                bodyRenderer.receiveShadows = false;
            }

            var gripBarCube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gripBarCube.name = "TabletGripHandleBar";
            gripBarCube.transform.SetParent(_tutorialOverlayGO.transform, false);
            gripBarCube.transform.localPosition = new Vector3(0f, -0.258f, 0.008f);
            gripBarCube.transform.localScale = new Vector3(0.32f, 0.022f, 0.022f);
            Collider gripBarCol = gripBarCube.GetComponent<Collider>();
            if (gripBarCol != null) Destroy(gripBarCol);
            var gripRenderer = gripBarCube.GetComponent<MeshRenderer>();
            if (gripRenderer != null && unlitShader != null)
            {
                var gripMat = new Material(unlitShader);
                gripMat.color = new Color(0.00f, 0.85f, 1.00f, 1f);
                if (gripMat.HasProperty("_BaseColor")) gripMat.SetColor("_BaseColor", gripMat.color);
                gripRenderer.sharedMaterial = gripMat;
                gripRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                gripRenderer.receiveShadows = false;
            }

            // 2. Collider + Rigidbody + XRGrabInteractable (Grip tuşuyla taşıma ve havada asılı kalma)
            // Not: Collider merkezini Canvas düzleminin (z=0) hemen arkasına (z=0.016) koyuyoruz ki
            // ön yüzdeki UI butonlarına Trigger ile tıklanırken lazeri engellemesin,
            // ama Grip tuşuna basıldığında tüm tablet gövdesinden rahatça tutulabilsin.
            var tabletCol = _tutorialOverlayGO.AddComponent<BoxCollider>();
            tabletCol.center = new Vector3(0f, -0.01f, 0.016f);
            tabletCol.size = new Vector3(0.75f, 0.52f, 0.028f);

            _tutorialTabletRb = _tutorialOverlayGO.AddComponent<Rigidbody>();
            _tutorialTabletRb.mass = 0.4f;
            _tutorialTabletRb.useGravity = false;
            _tutorialTabletRb.isKinematic = false;
#if UNITY_6000_0_OR_NEWER
            _tutorialTabletRb.linearDamping = 10f;
            _tutorialTabletRb.angularDamping = 12f;
#else
            _tutorialTabletRb.drag = 10f;
            _tutorialTabletRb.angularDrag = 12f;
#endif
            _tutorialTabletRb.interpolation = RigidbodyInterpolation.Interpolate;
            _tutorialTabletRb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            _tutorialTabletGrab = _tutorialOverlayGO.AddComponent<XRGrabInteractable>();
            _tutorialTabletGrab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
            _tutorialTabletGrab.throwOnDetach = false;
            _tutorialTabletGrab.useDynamicAttach = true;
            _tutorialTabletGrab.trackPosition = true;
            _tutorialTabletGrab.trackRotation = true;
            _tutorialTabletGrab.smoothPosition = true;
            _tutorialTabletGrab.smoothPositionAmount = 16f;
            _tutorialTabletGrab.tightenPosition = 0f;
            _tutorialTabletGrab.smoothRotation = true;
            _tutorialTabletGrab.smoothRotationAmount = 14f;
            _tutorialTabletGrab.tightenRotation = 0f;
            _tutorialTabletGrab.colliders.Clear();
            _tutorialTabletGrab.colliders.Add(tabletCol);

            // 3. Tablet World-Space Canvas (1200 x 760 px -> 0.72m x 0.456m)
            var canvasGO = new GameObject("TabletCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
            canvasGO.transform.SetParent(_tutorialOverlayGO.transform, false);
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.dynamicPixelsPerUnit = 10f;

            var tdRaycaster = canvasGO.GetComponent<TrackedDeviceGraphicRaycaster>();
            if (tdRaycaster != null)
            {
                tdRaycaster.checkFor3DOcclusion = false;
            }

            canvasGO.AddComponent<AnatomiVR.XR.XRUISetup>();

            var canvasRt = canvasGO.GetComponent<RectTransform>();
            canvasRt.localPosition = Vector3.zero;
            canvasRt.localRotation = Quaternion.identity;
            canvasRt.sizeDelta = new Vector2(1200f, 760f);
            canvasRt.localScale = new Vector3(0.0006f, 0.0006f, 0.0006f);

            // Tablet Ekran Arka Planı (raycastTarget = false -> Grip ile tabletin her yerinden tutulabilmesini sağlar)
            var bgGO = new GameObject("TabletScreenBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
            bgGO.transform.SetParent(canvasGO.transform, false);
            var bgRt = bgGO.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;
            var bgImg = bgGO.GetComponent<Image>();
            bgImg.color = new Color(0.02f, 0.06f, 0.12f, 0.985f);
            bgImg.raycastTarget = false;
            var rootOutline = bgGO.GetComponent<Outline>();
            rootOutline.effectColor = new Color(0.00f, 0.90f, 1.00f, 0.90f);
            rootOutline.effectDistance = new Vector2(3f, 3f);

            // Üst Bar (TopBar)
            var topBar = new GameObject("TutorialTopBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            topBar.transform.SetParent(bgGO.transform, false);
            var topRt = topBar.GetComponent<RectTransform>();
            topRt.anchorMin = new Vector2(0f, 1f);
            topRt.anchorMax = new Vector2(1f, 1f);
            topRt.pivot = new Vector2(0.5f, 1f);
            topRt.anchoredPosition = Vector2.zero;
            topRt.sizeDelta = new Vector2(0f, 102f);
            var topBarImg = topBar.GetComponent<Image>();
            topBarImg.color = new Color(0.04f, 0.13f, 0.24f, 0.98f);
            topBarImg.raycastTarget = false;

            var topGlow = new GameObject("TopGlowLine", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            topGlow.transform.SetParent(topBar.transform, false);
            var topGlowRt = topGlow.GetComponent<RectTransform>();
            topGlowRt.anchorMin = new Vector2(0.02f, 0f);
            topGlowRt.anchorMax = new Vector2(0.98f, 0f);
            topGlowRt.pivot = new Vector2(0.5f, 0f);
            topGlowRt.anchoredPosition = Vector2.zero;
            topGlowRt.sizeDelta = new Vector2(0f, 3f);
            var topGlowImg = topGlow.GetComponent<Image>();
            topGlowImg.color = new Color(0.00f, 0.92f, 1.00f, 0.90f);
            topGlowImg.raycastTarget = false;

            // Sol-Üst OVA VR Logo Rozeti
            float headerLeftOffset = 28f;
            Sprite ovaLogo = GetOvaLogoSprite();
            if (ovaLogo != null)
            {
                var logoGO = new GameObject("OVATabletLogo", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                logoGO.transform.SetParent(topBar.transform, false);
                var logoRt = logoGO.GetComponent<RectTransform>();
                logoRt.anchorMin = new Vector2(0f, 0.5f);
                logoRt.anchorMax = new Vector2(0f, 0.5f);
                logoRt.pivot = new Vector2(0f, 0.5f);
                logoRt.anchoredPosition = new Vector2(22f, 0f);
                logoRt.sizeDelta = new Vector2(195f, 78f);
                var logoImg = logoGO.GetComponent<Image>();
                logoImg.sprite = ovaLogo;
                logoImg.preserveAspect = true;
                logoImg.raycastTarget = false;
                headerLeftOffset = 228f;
            }

            // Sol-Üst Başlık (Header)
            var mainHdrGO = new GameObject("TutorialMainHeader", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            mainHdrGO.transform.SetParent(topBar.transform, false);
            var mainHdrRt = mainHdrGO.GetComponent<RectTransform>();
            mainHdrRt.anchorMin = new Vector2(0f, 0f);
            mainHdrRt.anchorMax = new Vector2(1f, 1f);
            mainHdrRt.offsetMin = new Vector2(headerLeftOffset, 8f);
            mainHdrRt.offsetMax = new Vector2(-265f, -8f);
            _tutorialMainHeaderTmp = mainHdrGO.GetComponent<TextMeshProUGUI>();
            if (font != null) _tutorialMainHeaderTmp.font = font;
            _tutorialMainHeaderTmp.fontSize = 27f;
            _tutorialMainHeaderTmp.fontStyle = FontStyles.Bold;
            _tutorialMainHeaderTmp.characterSpacing = 2.5f;
            _tutorialMainHeaderTmp.alignment = TextAlignmentOptions.MidlineLeft;
            _tutorialMainHeaderTmp.color = new Color(0.30f, 0.95f, 1.00f, 1f);
            _tutorialMainHeaderTmp.raycastTarget = false;

            // Sağ-Üst 'Rehberi Geç / Skip' Butonu (Basıldığında Rehber Tabletini Kapatır)
            var skipBtnGO = CreateStylizedTutorialButton(
                topBar.transform,
                "SkipTutorialButton",
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-22f, 0f),
                new Vector2(236f, 66f),
                new Color(0.55f, 0.14f, 0.22f, 0.98f),
                new Color(1.00f, 0.36f, 0.46f, 0.85f),
                font,
                21f,
                out _tutorialSkipBtnTmp);
            skipBtnGO.GetComponent<Button>().onClick.AddListener(CloseTutorialOverlay);

            // Orta İçerik Kartı (Card Panel)
            var cardGO = new GameObject("StepCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
            cardGO.transform.SetParent(bgGO.transform, false);
            var cardRt = cardGO.GetComponent<RectTransform>();
            cardRt.anchorMin = new Vector2(0.045f, 0.17f);
            cardRt.anchorMax = new Vector2(0.955f, 0.835f);
            cardRt.offsetMin = Vector2.zero;
            cardRt.offsetMax = Vector2.zero;
            var cardImg = cardGO.GetComponent<Image>();
            cardImg.color = new Color(0.04f, 0.13f, 0.24f, 0.95f);
            cardImg.raycastTarget = false;
            var cardOutline = cardGO.GetComponent<Outline>();
            cardOutline.effectColor = new Color(0.00f, 0.85f, 1.00f, 0.65f);
            cardOutline.effectDistance = new Vector2(2f, 2f);

            // Sol neon vurgu şeridi
            var accentGO = new GameObject("CardAccent", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            accentGO.transform.SetParent(cardGO.transform, false);
            var accentRt = accentGO.GetComponent<RectTransform>();
            accentRt.anchorMin = new Vector2(0f, 0f);
            accentRt.anchorMax = new Vector2(0f, 1f);
            accentRt.pivot = new Vector2(0f, 0.5f);
            accentRt.anchoredPosition = Vector2.zero;
            accentRt.sizeDelta = new Vector2(10f, 0f);
            var accentImg = accentGO.GetComponent<Image>();
            accentImg.color = new Color(0.00f, 0.95f, 1.00f, 1.00f);
            accentImg.raycastTarget = false;

            // Adım Başlığı (StepTitle)
            var stepTitleGO = new GameObject("StepTitle", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            stepTitleGO.transform.SetParent(cardGO.transform, false);
            var stepTitleRt = stepTitleGO.GetComponent<RectTransform>();
            stepTitleRt.anchorMin = new Vector2(0f, 1f);
            stepTitleRt.anchorMax = new Vector2(1f, 1f);
            stepTitleRt.pivot = new Vector2(0.5f, 1f);
            stepTitleRt.anchoredPosition = new Vector2(0f, -18f);
            stepTitleRt.sizeDelta = new Vector2(-70f, 56f);
            _tutorialStepTitleTmp = stepTitleGO.GetComponent<TextMeshProUGUI>();
            if (font != null) _tutorialStepTitleTmp.font = font;
            _tutorialStepTitleTmp.fontSize = 28f;
            _tutorialStepTitleTmp.fontStyle = FontStyles.Bold;
            _tutorialStepTitleTmp.characterSpacing = 2f;
            _tutorialStepTitleTmp.alignment = TextAlignmentOptions.Center;
            _tutorialStepTitleTmp.color = new Color(0.25f, 0.96f, 1.00f, 1f);
            _tutorialStepTitleTmp.raycastTarget = false;

            // Başlık altı ince ayırıcı çizgi
            var dividerGO = new GameObject("StepDivider", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dividerGO.transform.SetParent(cardGO.transform, false);
            var dividerRt = dividerGO.GetComponent<RectTransform>();
            dividerRt.anchorMin = new Vector2(0.05f, 1f);
            dividerRt.anchorMax = new Vector2(0.95f, 1f);
            dividerRt.pivot = new Vector2(0.5f, 1f);
            dividerRt.anchoredPosition = new Vector2(0f, -82f);
            dividerRt.sizeDelta = new Vector2(0f, 2f);
            var divImg = dividerGO.GetComponent<Image>();
            divImg.color = new Color(0.00f, 0.85f, 1.00f, 0.45f);
            divImg.raycastTarget = false;

            // Adım Açıklama Metni (StepBody)
            var stepBodyGO = new GameObject("StepBody", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            stepBodyGO.transform.SetParent(cardGO.transform, false);
            var stepBodyRt = stepBodyGO.GetComponent<RectTransform>();
            stepBodyRt.anchorMin = new Vector2(0f, 0f);
            stepBodyRt.anchorMax = new Vector2(1f, 1f);
            stepBodyRt.offsetMin = new Vector2(46f, 46f);
            stepBodyRt.offsetMax = new Vector2(-40f, -92f);
            _tutorialStepBodyTmp = stepBodyGO.GetComponent<TextMeshProUGUI>();
            if (font != null) _tutorialStepBodyTmp.font = font;
            _tutorialStepBodyTmp.fontSize = 25f;
            _tutorialStepBodyTmp.lineSpacing = 10f;
            _tutorialStepBodyTmp.alignment = TextAlignmentOptions.Center;
            _tutorialStepBodyTmp.color = new Color(0.93f, 0.98f, 1.00f, 1f);
            _tutorialStepBodyTmp.raycastTarget = false;

            // Tablet Taşıma İpucu (Kartın alt kısmında ince bilgi satırı)
            var gripHintGO = new GameObject("GripHintText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            gripHintGO.transform.SetParent(cardGO.transform, false);
            var gripHintRt = gripHintGO.GetComponent<RectTransform>();
            gripHintRt.anchorMin = new Vector2(0f, 0f);
            gripHintRt.anchorMax = new Vector2(1f, 0f);
            gripHintRt.pivot = new Vector2(0.5f, 0f);
            gripHintRt.anchoredPosition = new Vector2(0f, 10f);
            gripHintRt.sizeDelta = new Vector2(-60f, 32f);
            _tutorialGripHintTmp = gripHintGO.GetComponent<TextMeshProUGUI>();
            if (font != null) _tutorialGripHintTmp.font = font;
            _tutorialGripHintTmp.fontSize = 18.5f;
            _tutorialGripHintTmp.fontStyle = FontStyles.Italic;
            _tutorialGripHintTmp.alignment = TextAlignmentOptions.Center;
            _tutorialGripHintTmp.color = new Color(0.55f, 0.90f, 1.00f, 0.85f);
            _tutorialGripHintTmp.raycastTarget = false;

            // Alt Navigasyon Barı (Önceki / Sayfa Göstergesi / Sonraki)
            var prevBtnGO = CreateStylizedTutorialButton(
                bgGO.transform,
                "PrevStepButton",
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(54f, 22f),
                new Vector2(240f, 70f),
                new Color(0.05f, 0.20f, 0.35f, 0.96f),
                new Color(0.00f, 0.88f, 1.00f, 0.80f),
                font,
                23f,
                out _tutorialPrevBtnTmp);
            _tutorialPrevBtn = prevBtnGO.GetComponent<Button>();
            _tutorialPrevBtn.onClick.AddListener(() => StepTutorialPage(-1));

            // Sayfa Noktaları (● ○ ○ ○)
            var dotsGO = new GameObject("StepDotsText", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            dotsGO.transform.SetParent(bgGO.transform, false);
            var dotsRt = dotsGO.GetComponent<RectTransform>();
            dotsRt.anchorMin = new Vector2(0.5f, 0f);
            dotsRt.anchorMax = new Vector2(0.5f, 0f);
            dotsRt.pivot = new Vector2(0.5f, 0f);
            dotsRt.anchoredPosition = new Vector2(0f, 25f);
            dotsRt.sizeDelta = new Vector2(460f, 64f);
            _tutorialStepDotsTmp = dotsGO.GetComponent<TextMeshProUGUI>();
            if (font != null) _tutorialStepDotsTmp.font = font;
            _tutorialStepDotsTmp.fontSize = 24f;
            _tutorialStepDotsTmp.fontStyle = FontStyles.Bold;
            _tutorialStepDotsTmp.characterSpacing = 3.5f;
            _tutorialStepDotsTmp.alignment = TextAlignmentOptions.Center;
            _tutorialStepDotsTmp.color = new Color(0.35f, 0.95f, 1.00f, 1f);
            _tutorialStepDotsTmp.raycastTarget = false;

            var nextBtnGO = CreateStylizedTutorialButton(
                bgGO.transform,
                "NextStepButton",
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-54f, 22f),
                new Vector2(240f, 70f),
                new Color(0.06f, 0.34f, 0.56f, 0.98f),
                new Color(0.00f, 0.95f, 1.00f, 0.90f),
                font,
                23f,
                out _tutorialNextBtnTmp);
            _tutorialNextBtn = nextBtnGO.GetComponent<Button>();
            _tutorialNextBtnImg = nextBtnGO.GetComponent<Image>();
            _tutorialNextBtn.onClick.AddListener(() => StepTutorialPage(1));

            RefreshTutorialUI();
        }

        private static GameObject CreateStylizedTutorialButton(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPos,
            Vector2 sizeDelta,
            Color bgColor,
            Color outlineColor,
            TMP_FontAsset font,
            float fontSize,
            out TextMeshProUGUI labelTmp)
        {
            var btnGO = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Button));
            btnGO.transform.SetParent(parent, false);
            var rt = btnGO.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = sizeDelta;

            var img = btnGO.GetComponent<Image>();
            img.color = bgColor;
            img.raycastTarget = true;

            var outline = btnGO.GetComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = new Vector2(2f, 2f);

            var txtGO = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            txtGO.transform.SetParent(btnGO.transform, false);
            var trt = txtGO.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.sizeDelta = Vector2.zero;

            labelTmp = txtGO.GetComponent<TextMeshProUGUI>();
            if (font != null) labelTmp.font = font;
            labelTmp.fontSize = fontSize;
            labelTmp.fontStyle = FontStyles.Bold;
            labelTmp.characterSpacing = 2f;
            labelTmp.alignment = TextAlignmentOptions.Center;
            labelTmp.color = Color.white;
            labelTmp.raycastTarget = false;

            return btnGO;
        }

        public void OpenTutorialOverlay(bool resetToFirstStep = true)
        {
            if (_tutorialOverlayGO == null)
            {
                TMP_FontAsset font = headerText != null ? headerText.font : TMP_Settings.defaultFontAsset;
                EnsureTutorialOverlay(font);
            }

            if (resetToFirstStep)
            {
                _tutorialStepIndex = 0;
            }

            // Eğer tablet kapalıyken '? Rehber' butonuna basıldıysa ve oyuncu hareket ettiyse,
            // tableti ergonomik sol-ön başlangıç konumuna veya oyuncunun sol-ön görüş açısına getir:
            if (!_isTutorialOpen && _tutorialOverlayGO != null)
            {
                Camera cam = Camera.main;
                if (cam != null && Vector3.Distance(cam.transform.position, new Vector3(-1.35f, 1.48f, 0f)) > 0.65f)
                {
                    Vector3 flatFwd = Vector3.ProjectOnPlane(cam.transform.forward, Vector3.up).normalized;
                    if (flatFwd.sqrMagnitude < 0.01f) flatFwd = Vector3.right;
                    Vector3 tabletDir = Quaternion.Euler(0f, -38f, 0f) * flatFwd;
                    _tabletSuspendedPos = cam.transform.position + tabletDir * 0.95f + new Vector3(0f, -0.05f, 0f);
                    _tabletSuspendedPos.y = Mathf.Clamp(_tabletSuspendedPos.y, 1.10f, 1.75f);
                    _tabletSuspendedRot = Quaternion.LookRotation(_tabletSuspendedPos - cam.transform.position, Vector3.up);
                }
                else
                {
                    _tabletSuspendedPos = DefaultTabletWorldPos;
                    _tabletSuspendedRot = DefaultTabletWorldRot;
                }
                _tutorialOverlayGO.transform.SetPositionAndRotation(_tabletSuspendedPos, _tabletSuspendedRot);
            }

            _isTutorialOpen = true;
            if (_tutorialOverlayGO != null)
            {
                _tutorialOverlayGO.SetActive(true);
            }

            RefreshTutorialUI();
        }

        public void CloseTutorialOverlay()
        {
            _isTutorialOpen = false;
            if (_tutorialOverlayGO != null)
            {
                _tutorialOverlayGO.SetActive(false);
            }
        }

        public void ToggleTutorialLanguage()
        {
            _isTutorialEnglish = !_isTutorialEnglish;
            RefreshTutorialUI();
        }

        private void StepTutorialPage(int delta)
        {
            int nextIndex = _tutorialStepIndex + delta;
            if (nextIndex >= TutorialPages.Length)
            {
                // Son sayfada 'BAŞLA / START' butonuna basıldığında rehber tabletini kapat
                CloseTutorialOverlay();
                return;
            }

            _tutorialStepIndex = Mathf.Clamp(nextIndex, 0, TutorialPages.Length - 1);
            RefreshTutorialUI();
        }

        private void RefreshTutorialUI()
        {
            if (_tutorialLangBtnTmp != null)
            {
                _tutorialLangBtnTmp.text = _isTutorialEnglish
                    ? "[ TR  |  <color=#4DF7FF>EN</color> ]"
                    : "[ <color=#4DF7FF>TR</color>  |  EN ]";
            }

            if (_openTutorialHelpButton != null)
            {
                var helpTmp = _openTutorialHelpButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (helpTmp != null)
                {
                    helpTmp.text = _isTutorialEnglish ? "[ ? ]   Guide" : "[ ? ]   Rehber";
                }
            }

            if (backButton != null)
            {
                var backTmp = backButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (backTmp != null)
                {
                    backTmp.text = _isTutorialEnglish ? "◄  BACK" : "◄  GERİ";
                }
            }

            if (_closeMenuButton != null)
            {
                var closeTmp = _closeMenuButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (closeTmp != null)
                {
                    closeTmp.text = _isTutorialEnglish ? "[X] Close Menu [M]" : "[X] Menüyü Kapat [M]";
                }
            }

            if (_openMenuButton != null)
            {
                var openTmp = _openMenuButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (openTmp != null)
                {
                    openTmp.text = _isTutorialEnglish ? "☰ Open Menu [M]" : "☰ Menüyü Aç [M]";
                }
            }

            if (_clearTableButton != null)
            {
                var clearTmp = _clearTableButton.GetComponentInChildren<TextMeshProUGUI>(true);
                if (clearTmp != null)
                {
                    clearTmp.text = _isTutorialEnglish ? "[ X ]   CLEAR TABLE" : "[ X ]   MASAYI TEMİZLE";
                }
            }

            if (_tutorialOverlayGO == null || TutorialPages.Length == 0)
                return;

            _tutorialStepIndex = Mathf.Clamp(_tutorialStepIndex, 0, TutorialPages.Length - 1);
            TutorialPageContent page = TutorialPages[_tutorialStepIndex];

            if (_tutorialMainHeaderTmp != null)
            {
                _tutorialMainHeaderTmp.text = _isTutorialEnglish
                    ? "USER GUIDE  (TUTORIAL)"
                    : "KULLANIM REHBERİ";
            }

            if (_tutorialGripHintTmp != null)
            {
                _tutorialGripHintTmp.text = _isTutorialEnglish
                    ? "✋ Tip: Grab this tablet with the Grip (Middle Finger) button to move and suspend it anywhere in mid-air."
                    : "✋ İpucu: Bu tableti Kavrama (Grip - Orta Parmak) tuşuyla tutup istediğiniz yere taşıyabilir ve havada bırakabilirsiniz.";
            }

            if (_tutorialSkipBtnTmp != null)
            {
                _tutorialSkipBtnTmp.text = _isTutorialEnglish
                    ? "[ SKIP GUIDE ✕ ]"
                    : "[ REHBERİ GEÇ ✕ ]";
            }

            if (_tutorialStepTitleTmp != null)
            {
                _tutorialStepTitleTmp.text = _isTutorialEnglish ? page.TitleEN : page.TitleTR;
            }

            if (_tutorialStepBodyTmp != null)
            {
                _tutorialStepBodyTmp.text = _isTutorialEnglish ? page.BodyEN : page.BodyTR;
            }

            if (_tutorialStepDotsTmp != null)
            {
                string dots = "";
                for (int i = 0; i < TutorialPages.Length; i++)
                {
                    dots += (i == _tutorialStepIndex) ? "●   " : "○   ";
                }
                _tutorialStepDotsTmp.text = $"{dots}({_tutorialStepIndex + 1} / {TutorialPages.Length})";
            }

            bool isFirst = (_tutorialStepIndex == 0);
            bool isLast  = (_tutorialStepIndex == TutorialPages.Length - 1);

            if (_tutorialPrevBtn != null)
            {
                _tutorialPrevBtn.interactable = !isFirst;
            }
            if (_tutorialPrevBtnTmp != null)
            {
                _tutorialPrevBtnTmp.text = _isTutorialEnglish ? "◄   PREV" : "◄   GERİ";
                _tutorialPrevBtnTmp.color = isFirst
                    ? new Color(0.45f, 0.55f, 0.65f, 0.65f)
                    : Color.white;
            }

            if (_tutorialNextBtnTmp != null)
            {
                if (isLast)
                {
                    _tutorialNextBtnTmp.text = _isTutorialEnglish ? "START  ✔" : "BAŞLA  ✔";
                }
                else
                {
                    _tutorialNextBtnTmp.text = _isTutorialEnglish ? "NEXT   ►" : "İLERİ   ►";
                }
            }

            if (_tutorialNextBtnImg != null)
            {
                _tutorialNextBtnImg.color = isLast
                    ? new Color(0.08f, 0.48f, 0.34f, 0.98f)
                    : new Color(0.06f, 0.34f, 0.56f, 0.98f);
            }
        }

        // ─────────────────────── Panel Gösterimi ─────────────────────────

        /// <summary>
        /// Tüm panelleri gizler.
        /// </summary>
        private void HideAllPanels()
        {
            if (animalPanel != null) animalPanel.SetActive(false);
            if (systemPanel != null) systemPanel.SetActive(false);
            if (regionPanel != null) regionPanel.SetActive(false);
            if (bonePanel != null) bonePanel.SetActive(false);
        }

        /// <summary>
        /// Hayvan seçim panelini gösterir ve butonları oluşturur.
        /// </summary>
        public void ShowAnimalPanel()
        {
            HideAllPanels();
            animalPanel.SetActive(true);
            _navigationStack.Clear();
            _navigationStack.Push(MenuLevel.Animal);

            UpdateHeader("OPENVETATLAS VR  //  SPECIES");
            UpdateBackButton();
            ClearContainer(animalButtonContainer);

            for (int i = 0; i < menuData.animals.Count; i++)
            {
                AnimalData animal = menuData.animals[i];
                int index = i; // Closure için lokal kopya

                CreateMenuButton(
                    animalButtonContainer,
                    animal.displayName,
                    animal.isLocked,
                    animal.icon,
                    () => OnAnimalSelected(index)
                );
            }
        }

        /// <summary>
        /// Seçili hayvanın sistem panelini gösterir.
        /// </summary>
        private void ShowSystemPanel(int animalIndex)
        {
            _selectedAnimalIndex = animalIndex;
            AnimalData animal = menuData.animals[animalIndex];

            HideAllPanels();
            systemPanel.SetActive(true);
            _navigationStack.Push(MenuLevel.System);

            UpdateHeader($"{animal.displayName.ToUpperInvariant()}  //  SYSTEMATA");
            UpdateBackButton();
            ClearContainer(systemButtonContainer);

            for (int i = 0; i < animal.systems.Count; i++)
            {
                SystemData system = animal.systems[i];
                int index = i;

                CreateMenuButton(
                    systemButtonContainer,
                    system.displayName,
                    system.isLocked,
                    null,
                    () => OnSystemSelected(index)
                );
            }
        }

        /// <summary>
        /// Seçili sistemin bölge panelini gösterir.
        /// </summary>
        private void ShowRegionPanel(int systemIndex)
        {
            _selectedSystemIndex = systemIndex;
            AnimalData animal = menuData.animals[_selectedAnimalIndex];
            SystemData system = animal.systems[systemIndex];

            HideAllPanels();
            regionPanel.SetActive(true);
            _navigationStack.Push(MenuLevel.Region);

            UpdateHeader($"{animal.displayName.ToUpperInvariant()}  //  {system.displayName.ToUpperInvariant()}");
            UpdateBackButton();
            ClearContainer(regionButtonContainer);

            for (int i = 0; i < system.regions.Count; i++)
            {
                RegionData region = system.regions[i];
                int index = i;

                CreateMenuButton(
                    regionButtonContainer,
                    region.displayName,
                    region.isLocked,
                    null,
                    () => OnRegionSelected(index)
                );
            }
        }

        /// <summary>
        /// Seçili bölgenin kemik panelini gösterir.
        /// </summary>
        private void ShowBonePanel(int regionIndex)
        {
            _selectedRegionIndex = regionIndex;
            AnimalData animal = menuData.animals[_selectedAnimalIndex];
            SystemData system = animal.systems[_selectedSystemIndex];
            RegionData region = system.regions[regionIndex];

            HideAllPanels();
            bonePanel.SetActive(true);
            _navigationStack.Push(MenuLevel.Bone);

            UpdateHeader($"{animal.displayName.ToUpperInvariant()}  //  {region.displayName.ToUpperInvariant()}");
            UpdateBackButton();
            ClearContainer(boneButtonContainer);

            for (int i = 0; i < region.bones.Count; i++)
            {
                BoneData bone = region.bones[i];

                CreateMenuButton(
                    boneButtonContainer,
                    bone.displayName,
                    bone.isLocked,
                    bone.thumbnail,
                    () => OnBoneSelected(bone.addressableKey)
                );
            }
        }

        // ─────────────────────── Seçim Callback'leri ─────────────────────

        private void OnAnimalSelected(int index)
        {
            Debug.Log($"[UIMenu] Hayvan seçildi: {menuData.animals[index].displayName}");
            ShowSystemPanel(index);
        }

        private void OnSystemSelected(int index)
        {
            Debug.Log($"[UIMenu] Sistem seçildi: {menuData.animals[_selectedAnimalIndex].systems[index].displayName}");
            ShowRegionPanel(index);
        }

        private void OnRegionSelected(int index)
        {
            Debug.Log($"[UIMenu] Bölge seçildi: {menuData.animals[_selectedAnimalIndex].systems[_selectedSystemIndex].regions[index].displayName}");
            ShowBonePanel(index);
        }

        /// <summary>
        /// Kemik butonuna basıldığında AnatomyManager'a spawn komutu gönderir.
        /// </summary>
        private void OnBoneSelected(string addressableKey)
        {
            Debug.Log($"[UIMenu] Kemik seçildi: {addressableKey}");

            if (AnatomyManager.Instance == null)
            {
                Debug.LogError("[UIMenu] AnatomyManager bulunamadı!");
                return;
            }

            if (!allowDuplicateBones && AnatomyManager.Instance.IsBoneOnTable(addressableKey))
            {
                return;
            }

            // AnatomyManager.SpawnBone içinde aynı "Hayvan_Kemik" (örn. Dog_Scapula) kontrolü yapılır;
            // zaten masadaysa ikinci kez düşürmez.
            AnatomyManager.Instance.SpawnBone(addressableKey);
        }

        // ─────────────────────── Geri Butonu ─────────────────────────────

        private void OnBackButtonClicked()
        {
            if (_navigationStack.Count <= 1)
            {
                // Zaten en üst seviyedeyiz
                return;
            }

            // Mevcut seviyeyi çıkar
            _navigationStack.Pop();

            // Önceki seviyeye dön
            MenuLevel previousLevel = _navigationStack.Peek();
            // Peek'i çıkar çünkü Show metodu tekrar Push yapacak
            _navigationStack.Pop();

            switch (previousLevel)
            {
                case MenuLevel.Animal:
                    ShowAnimalPanel();
                    break;
                case MenuLevel.System:
                    ShowSystemPanel(_selectedAnimalIndex);
                    break;
                case MenuLevel.Region:
                    ShowRegionPanel(_selectedSystemIndex);
                    break;
                case MenuLevel.Bone:
                    ShowBonePanel(_selectedRegionIndex);
                    break;
            }
        }

        private void UpdateBackButton()
        {
            if (backButton != null)
            {
                // İlk seviyede (Animal) geri butonu gizle
                backButton.gameObject.SetActive(_navigationStack.Count > 1);
            }
        }

        // ─────────────────────── Başlık ──────────────────────────────────

        private void UpdateHeader(string text)
        {
            if (headerText != null)
                headerText.text = text;
        }

        // ─────────────────────── Buton Oluşturma ────────────────────────

        /// <summary>
        /// Dinamik menü butonu oluşturur.
        /// Kilitli butonlar gri renkte ve tıklanamaz olur.
        /// </summary>
        private void CreateMenuButton(
            Transform parent,
            string label,
            bool isLocked,
            Sprite icon,
            System.Action onClick)
        {
            if (menuButtonPrefab == null)
            {
                Debug.LogError("[UIMenu] MenuButton prefab'ı atanmamış!");
                return;
            }

            GameObject buttonObj = Instantiate(menuButtonPrefab, parent);
            Button button = buttonObj.GetComponent<Button>();

            // Buton text'ini ayarla (Fütüristik Latince görünüm)
            TextMeshProUGUI buttonText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText != null)
            {
                buttonText.text = isLocked ? $"[ - ]   {label}" : $"►   {label}";
                buttonText.characterSpacing = 2.5f;
                buttonText.color = isLocked
                    ? new Color(0.46f, 0.56f, 0.66f, 0.85f)
                    : new Color(0.90f, 0.98f, 1.00f, 1.00f);
            }

            // İkon varsa ayarla (Kilitli öğelerde ikon yoksa varsayılan kilit ikonunu kullan)
            Sprite effectiveIcon = icon != null ? icon : (isLocked ? lockIcon : null);
            if (effectiveIcon != null)
            {
                Image iconImage = buttonObj.transform.Find("Icon")?.GetComponent<Image>();
                if (iconImage != null)
                {
                    iconImage.sprite = effectiveIcon;
                    iconImage.enabled = true;
                }
            }

            Image bgImg = buttonObj.GetComponent<Image>();
            Outline outline = buttonObj.GetComponent<Outline>();
            if (outline == null)
                outline = buttonObj.AddComponent<Outline>();

            Transform accentTr = buttonObj.transform.Find("AccentBar");
            Image accentImg = accentTr != null ? accentTr.GetComponent<Image>() : null;

            // Kilit durumu (Fütüristik Holografik Tema)
            if (isLocked)
            {
                button.interactable = false;
                if (bgImg != null)
                    bgImg.color = new Color(0.05f, 0.09f, 0.15f, Mathf.Clamp(lockedColor.a, 0.75f, 0.90f));

                outline.effectColor = new Color(0.18f, 0.28f, 0.38f, 0.45f);
                outline.effectDistance = new Vector2(1.5f, 1.5f);

                if (accentImg != null)
                    accentImg.color = new Color(0.22f, 0.32f, 0.42f, 0.55f);

                ColorBlock colors = button.colors;
                colors.disabledColor = unlockedColor;
                button.colors = colors;
            }
            else
            {
                button.interactable = true;
                if (bgImg != null)
                    bgImg.color = new Color(0.04f, 0.18f, 0.32f, Mathf.Clamp(unlockedColor.a, 0.90f, 0.98f));

                outline.effectColor = new Color(0.00f, 0.88f, 1.00f, 0.80f);
                outline.effectDistance = new Vector2(2f, 2f);

                if (accentImg != null)
                    accentImg.color = new Color(0.00f, 0.95f, 1.00f, 1.00f);

                button.onClick.AddListener(() => onClick?.Invoke());

                ColorBlock colors = button.colors;
                colors.normalColor = unlockedColor;
                colors.highlightedColor = new Color(0.75f, 0.96f, 1.0f, 1f);
                colors.pressedColor = new Color(0.45f, 0.85f, 1.0f, 1f);
                button.colors = colors;
            }
        }

        /// <summary>
        /// Bir container'daki tüm child butonları temizler.
        /// Yeni butonlar oluşturulmadan önce çağrılır.
        /// </summary>
        private void ClearContainer(Transform container)
        {
            if (container == null) return;

            for (int i = container.childCount - 1; i >= 0; i--)
            {
                Destroy(container.GetChild(i).gameObject);
            }
        }

        // ─────────────────────── Kilit Yönetimi (Public API) ─────────────

        /// <summary>
        /// Belirli bir hayvanın kilidini açar.
        /// İlerleme sistemi veya satın alma sonrası çağrılabilir.
        /// </summary>
        /// <param name="animalCode">Hayvan kodu (ör: "Dog", "Horse")</param>
        public void UnlockAnimal(string animalCode)
        {
            AnimalData animal = menuData.animals.Find(a => a.animalCode == animalCode);
            if (animal != null)
            {
                animal.isLocked = false;
                Debug.Log($"[UIMenu] Hayvan kilidi açıldı: {animal.displayName}");

                // Eğer hayvan panelindeyse yenile
                if (animalPanel.activeSelf)
                    ShowAnimalPanel();
            }
        }

        /// <summary>
        /// Belirli bir hayvanın belirli bir sisteminin kilidini açar.
        /// </summary>
        public void UnlockSystem(string animalCode, string systemName)
        {
            AnimalData animal = menuData.animals.Find(a => a.animalCode == animalCode);
            if (animal != null)
            {
                SystemData system = animal.systems.Find(s => s.displayName == systemName);
                if (system != null)
                {
                    system.isLocked = false;
                    Debug.Log($"[UIMenu] Sistem kilidi açıldı: {animal.displayName} → {system.displayName}");
                }
            }
        }

        /// <summary>
        /// Mevcut paneli yeniden yükler (veri değişikliği sonrası).
        /// </summary>
        public void RefreshCurrentPanel()
        {
            if (_navigationStack.Count == 0) return;

            MenuLevel current = _navigationStack.Peek();
            _navigationStack.Pop(); // Show metodu tekrar Push yapacak

            switch (current)
            {
                case MenuLevel.Animal:
                    ShowAnimalPanel();
                    break;
                case MenuLevel.System:
                    ShowSystemPanel(_selectedAnimalIndex);
                    break;
                case MenuLevel.Region:
                    ShowRegionPanel(_selectedSystemIndex);
                    break;
                case MenuLevel.Bone:
                    ShowBonePanel(_selectedRegionIndex);
                    break;
            }
        }
    }
}
