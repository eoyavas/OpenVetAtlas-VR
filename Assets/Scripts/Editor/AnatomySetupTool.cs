// ============================================================================
// AnatomySetupTool.cs
// Konum: Assets/Scripts/Editor/AnatomySetupTool.cs
//
// Unity üst menüsüne "Tools -> Anatomi VR -> Otomatik Sistem Kurulumu"
// butonunu ekler. Tıklandığında:
//   0. URP Pipeline Asset (Quest 3S: 4x MSAA, Işık/Gölge Kapalı) oluşturur ve
//      GraphicsSettings + QualitySettings'e atar (Pembe/Magenta hatasını önler).
//   1. "Assets/Data" klasörünü oluşturur; Köpek (açık), At, İnek ve Domuz
//      (isLocked = true) için MenuData (.asset) dosyalarını üretir.
//   2. lab_tasarimi.fbx gerçek 3D laboratuvar modelini sahneye kurar:
//      - Diseksiyon masası ('masa'): Non-Static + BoxCollider (TablePhysMat)
//      - Tam köpek iskeleti ('iskeletli masa', 135k tri): Tamamen Static,
//        Rigidbody/Collider/XRGrabInteractable YOK ([BackgroundProps] altında)
//      - Akıllı Ekran ('[SmartScreen]'): 'ekran' modeli üzerine (x=2.39, y=1.721)
//        tam hizalı World-Space Canvas + TrackedDeviceGraphicRaycaster + Poke
//      - Kemik Prefab'ları: Gerçekçi ölçek (0.18x), Convex MeshCollider, Rigidbody,
//        XRGrabInteractable ve BakedUnlit materyal
//   3. Sahnede "GameManagers" objesini oluşturur (varsa yenisini yaratmaz) ve
//      AnatomyManager + UIMenuManager + BoneResetManager bileşenlerini bağlar.
//   4. Addressables Default Local Group içine kemik prefab'larını kaydeder.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;
using AnatomiVR.Bones;
using AnatomiVR.Data;
using AnatomiVR.Managers;
using AnatomiVR.UI;
using AnatomiVR.XR;

#if UNITY_6000_0_OR_NEWER
using PhysicMaterial = UnityEngine.PhysicsMaterial;
using PhysicMaterialCombine = UnityEngine.PhysicsMaterialCombine;
#endif

namespace AnatomiVR.Editor
{
    public static class AnatomySetupTool
    {
        private const string MenuPath              = "Tools/Anatomi VR/Otomatik Sistem Kurulumu";
        private const string GameManagersName      = "GameManagers";
        private const string DataFolder            = "Assets/Data";
        private const string SettingsFolder        = "Assets/Settings";
        private const string CombinedAssetPath     = DataFolder + "/AnatomyMenu_All.asset";
        private const string URPAssetPath          = SettingsFolder + "/URP_Quest3S_PipelineAsset.asset";
        private const string DefaultLocalGroupName = "Default Local Group";
        private const string MenuButtonPrefabPath  = "Assets/Prefabs/UI/MenuButton.prefab";
        private const string BonePhysMatPath       = "Assets/PhysicsMaterials/BonePhysMat.physicMaterial";
        private const string TablePhysMatPath      = "Assets/PhysicsMaterials/TablePhysMat.physicMaterial";
        private const string LabFbxPath            = "Assets/Models/Environment/lab_tasarimi.fbx";
        private const string BakedRoomObjPath      = "Assets/Models/Environment/YAPILAR_Unity_Baked.obj";
        private const string BakedRoomTexPath      = "Assets/Textures/Environment/bake_temiz_4k.png";
        private const string LogTag                = "[AnatomySetupTool]";

        // Kemiklerin diseksiyon masasına (0.88m x 2.23m) uygun gerçekçi ölçeği (~25-33 cm)
        private static readonly Vector3 BoneRealisticScale = new Vector3(0.18f, 0.18f, 0.18f);

        private const System.Reflection.BindingFlags BindingFlags_Instance =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;

        [MenuItem(MenuPath, priority = 0)]
        public static void RunFullSetup()
        {
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path) && File.Exists("Assets/Scenes/MainLab.unity"))
            {
                EditorSceneManager.OpenScene("Assets/Scenes/MainLab.unity", OpenSceneMode.Single);
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Anatomi VR Otomatik Sistem Kurulumu");

            Debug.Log($"{LogTag} Otomatik sistem kurulumu başlatıldı...");

            // 0. URP Pipeline Asset, TMP Essential Resources & Model Read/Write hazırlığı
            EnsureURPPipelineAsset();
            EnsureTMPEssentials();
            EnsureBoneModelsReadable();
            EnsurePhysicsMaterials();
            EnsureBakedTexturesAndMaterials();
            UpgradeSampleMaterialsToURP();
            EnsureMenuButtonPrefab();
            EnsureBonePrefabs();

            // 1. Assets/Data altında ScriptableObject (.asset) üretimi
            MenuData masterMenu = CreateMenuDataAssets();

            // 2. Sahnedeki lab_tasarimi.fbx, SmartScreen Canvas ve XR Rig kurulumu
            RebuildLabEnvironmentAndUI(masterMenu);
            EnsureRealXROriginInScene();
            EnsureTMPFontsAssigned();

            // 3. "GameManagers" objesi ve AnatomyManager + UIMenuManager kurulumu
            GameObject managers = SetupGameManagers(masterMenu);

            // 4. Addressables Default Local Group entegrasyonu
            RegisterAddressables(masterMenu);

            Undo.CollapseUndoOperations(undoGroup);
            Scene activeScene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(activeScene);
            if (!string.IsNullOrEmpty(activeScene.path))
                EditorSceneManager.SaveScene(activeScene);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeGameObject = managers;
            EditorGUIUtility.PingObject(managers);

            Debug.Log($"{LogTag} ✅ Kurulum tamamlandı ve sahne kaydedildi.");
        }

        [MenuItem(MenuPath, true)]
        private static bool ValidateRunFullSetup()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        [InitializeOnLoadMethod]
        private static void AutoEnsureOpenSceneReferences()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;

                // Eğer yeni 4K bake edilmiş oda modeli (YAPILAR_Unity_Baked) sahnede henüz yoksa
                // otomatik olarak tüm kurulumu ve bake_temiz_4k.png materyal atamalarını uygula
                if (GameObject.Find("YAPILAR_Unity_Baked") == null && File.Exists(BakedRoomObjPath))
                {
                    RunFullSetupAndCapturePreview();
                    return;
                }

                bool dirty = false;

                BoneResetManager resetMgr = UnityEngine.Object.FindAnyObjectByType<BoneResetManager>();
                if (resetMgr != null && resetMgr.ResetPosition == null)
                {
                    GameObject sp = GameObject.Find("[SpawnPoint]") ?? GameObject.Find("SpawnPoint");
                    if (sp != null)
                    {
                        resetMgr.ResetPosition = sp.transform;
                        resetMgr.ResetYThreshold = -0.2f;
                        EditorUtility.SetDirty(resetMgr);
                        dirty = true;
                    }
                }

                GameObject gm = GameObject.Find(GameManagersName);
                if (gm != null && gm.GetComponent<DesktopTestController>() == null)
                {
                    gm.AddComponent<DesktopTestController>();
                    EditorUtility.SetDirty(gm);
                    dirty = true;
                }

                UIMenuManager uiMgr = UnityEngine.Object.FindAnyObjectByType<UIMenuManager>();
                if (uiMgr != null)
                {
                    SetField(uiMgr, "allowDuplicateBones", false);
                    EditorUtility.SetDirty(uiMgr);
                }

                if (GameObject.Find("SkeletonTable_Collider") == null)
                {
                    GameObject envRoot = GameObject.Find("[Environment]");
                    if (envRoot != null)
                    {
                        var skelColliderGO = new GameObject("SkeletonTable_Collider");
                        skelColliderGO.transform.SetParent(envRoot.transform, false);
                        skelColliderGO.transform.position = new Vector3(-2.345f, 0.965f, 0.000f);
                        SetFullyStatic(skelColliderGO);
                        var skelBox = skelColliderGO.AddComponent<BoxCollider>();
                        skelBox.size = new Vector3(0.700f, 1.575f, 1.300f);
                        var tablePhysMat = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(TablePhysMatPath);
                        if (tablePhysMat != null)
                            skelBox.sharedMaterial = tablePhysMat;
                        dirty = true;
                    }
                }

                if (dirty)
                {
                    Scene activeScene = SceneManager.GetActiveScene();
                    if (activeScene.IsValid() && !string.IsNullOrEmpty(activeScene.path))
                    {
                        EditorSceneManager.SaveScene(activeScene);
                    }
                }
            };
        }

        public static void RunFullSetupAndCapturePreview()
        {
            RunFullSetup();

            try
            {
                Camera cam = Camera.main;
                if (cam == null)
                    cam = UnityEngine.Object.FindAnyObjectByType<Camera>();
                if (cam == null)
                    return;

                // Ön izleme için masanın üzerine geçici olarak 3 kemik yerleştir
                string[] previewBones = { "Dog_Scapula", "Dog_Humerus", "Dog_Radius" };
                Vector3[] offsets = {
                    new Vector3(0.0f, 1.07f, -0.45f),
                    new Vector3(0.0f, 1.07f,  0.00f),
                    new Vector3(0.0f, 1.07f,  0.45f)
                };
                var tempInstances = new List<GameObject>();
                for (int i = 0; i < previewBones.Length; i++)
                {
                    var p = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Bones/{previewBones[i]}.prefab");
                    if (p != null)
                    {
                        var inst = (GameObject)PrefabUtility.InstantiatePrefab(p);
                        inst.transform.position = offsets[i];
                        inst.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
                        tempInstances.Add(inst);
                    }
                }

                Canvas.ForceUpdateCanvases();

                string artifactDir = @"C:\Users\Osteometri\.gemini\antigravity\brain\35d513b3-6128-4851-9b35-18c8892926e1";
                if (Directory.Exists(artifactDir))
                {
                    // 1. Ön görünüm (Oyuncu gözünden Masa + Akıllı Ekran)
                    CaptureCameraToPng(cam, new Vector3(-1.35f, 1.66f, 0.0f), Quaternion.Euler(7f, 90f, 0f),
                        Path.Combine(artifactDir, "preview_front.png"));

                    // 2. Arka duvar görünümü (135k tri Tam Köpek İskeleti + Posterler)
                    CaptureCameraToPng(cam, new Vector3(-0.30f, 1.55f, 0.0f), Quaternion.Euler(5f, 270f, 0f),
                        Path.Combine(artifactDir, "preview_back.png"));
                }

                foreach (var inst in tempInstances)
                {
                    if (inst != null) UnityEngine.Object.DestroyImmediate(inst);
                }

                // Sahneyi temiz haliyle tekrar kaydet
                Scene activeScene = SceneManager.GetActiveScene();
                EditorSceneManager.SaveScene(activeScene);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogTag} Önizleme yakalama uyarısı: {ex.Message}");
            }
        }

        private static void CaptureCameraToPng(Camera cam, Vector3 worldPos, Quaternion worldRot, string outPath)
        {
            Vector3 oldPos = cam.transform.position;
            Quaternion oldRot = cam.transform.rotation;
            RenderTexture oldTarget = cam.targetTexture;
            RenderTexture oldActive = RenderTexture.active;

            cam.transform.position = worldPos;
            cam.transform.rotation = worldRot;

            const int width = 1920, height = 1080;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = 4;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            File.WriteAllBytes(outPath, tex.EncodeToPNG());

            cam.targetTexture = oldTarget;
            RenderTexture.active = oldActive;
            cam.transform.position = oldPos;
            cam.transform.rotation = oldRot;

            UnityEngine.Object.DestroyImmediate(tex);
            UnityEngine.Object.DestroyImmediate(rt);
            Debug.Log($"{LogTag} Önizleme kaydedildi: {outPath}");
        }

        // ──────────── 0. URP Pipeline & Materyal Hazırlığı ────────────

        private static void EnsureURPPipelineAsset()
        {
            EnsureFolder(SettingsFolder);

            UniversalRenderPipelineAsset urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(URPAssetPath);
            if (urpAsset == null)
            {
                urpAsset = UniversalRenderPipelineAsset.Create();
                AssetDatabase.CreateAsset(urpAsset, URPAssetPath);
            }

            if (AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/UniversalRenderer.asset") == null)
            {
                urpAsset.LoadBuiltinRendererData();
            }

            // Meta Quest 3S (URP Unlit / Sıfır Işık & Gölge / 4x MSAA)
            urpAsset.supportsHDR = false;
            urpAsset.msaaSampleCount = 4;
            urpAsset.renderScale = 1.0f;
            urpAsset.supportsCameraDepthTexture = false;
            urpAsset.supportsCameraOpaqueTexture = false;
            EditorUtility.SetDirty(urpAsset);

            GraphicsSettings.defaultRenderPipeline = urpAsset;

            int currentQuality = QualitySettings.GetQualityLevel();
            int qualityCount = QualitySettings.names.Length;
            for (int i = 0; i < qualityCount; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = urpAsset;
                QualitySettings.vSyncCount = 0;
                QualitySettings.antiAliasing = 4;
            }
            QualitySettings.SetQualityLevel(currentQuality, false);

            Debug.Log($"{LogTag} URP Pipeline Asset aktif edildi: {URPAssetPath}");
        }

        private static void EnsureTMPEssentials()
        {
            if (AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset") != null)
                return;

            string packagePath = null;
            var pkgInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui")
                       ?? UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.textmeshpro");
            if (pkgInfo != null && !string.IsNullOrEmpty(pkgInfo.resolvedPath))
            {
                string candidate = Path.Combine(pkgInfo.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
                if (File.Exists(candidate))
                    packagePath = candidate;
            }

            if (!string.IsNullOrEmpty(packagePath) && File.Exists(packagePath))
            {
                Debug.Log($"{LogTag} TMP Essential Resources içe aktarılıyor: {packagePath}");
#pragma warning disable CS0618
                AssetDatabase.ImportPackage(packagePath, false);
#pragma warning restore CS0618
                AssetDatabase.Refresh();
            }
        }

        private static void EnsureBoneModelsReadable()
        {
            // Masaüstündeki 'Anatomi VR' klasörüne kaydedilmiş daha güncel kemik FBX dosyaları varsa otomatik olarak projeye kopyala
            SyncDesktopBoneFbxFilesIfNewer();

            string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models" });
            foreach (string guid in modelGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer != null && !importer.isReadable)
                {
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    Debug.Log($"{LogTag} Model için Read/Write (isReadable) açıldı: {path}");
                }
            }
        }

        private static string GetBoneFbxAssetPath(string animalCode, string regionName, string boneFolderName)
        {
            if (string.Equals(animalCode, "Dog", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(boneFolderName, "Humerus", StringComparison.OrdinalIgnoreCase))
                    return "Assets/Models/Bones/Dog_humerus.fbx";
                if (string.Equals(boneFolderName, "Radius", StringComparison.OrdinalIgnoreCase))
                    return "Assets/Models/Bones/Dog_Radius.fbx";
                if (string.Equals(boneFolderName, "Scapula", StringComparison.OrdinalIgnoreCase))
                    return "Assets/Models/Bones/Dog_scapula.fbx";
                if (string.Equals(boneFolderName, "Ulna", StringComparison.OrdinalIgnoreCase))
                    return "Assets/Models/Bones/Dog_ulna.fbx";
            }

            string key = GetBoneAddressableKey(animalCode, regionName, boneFolderName);
            return $"Assets/Models/Bones/{key}.fbx";
        }

        private static string GetBoneAddressableKey(string animalCode, string regionName, string boneFolderName)
        {
            string cleanBone = boneFolderName.Replace(" ", "");
            // Phalanx hem Ossa Membri Thoracici hem Ossa Membri Pelvini altında bulunduğu için çakışmayı önle
            if (string.Equals(cleanBone, "Phalanx", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(regionName) &&
                regionName.IndexOf("Pelvini", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return $"{animalCode}_Pelvini_Phalanx";
            }
            return $"{animalCode}_{cleanBone}";
        }

        private static void SyncDesktopBoneFbxFilesIfNewer()
        {
            string desktopRoot = @"C:\Users\Osteometri\Desktop\Anatomi VR";
            if (!Directory.Exists(desktopRoot))
                return;

            EnsureFolder("Assets/Models/Bones");
            bool anyCopied = false;

            // 1. Yeni 'Kemik\<Tür>\<Sistem>\<Bölge>\<Kemik>\*.fbx' klasör hiyerarşisini otomatik tara
            string kemikRoot = Path.Combine(desktopRoot, "Kemik");
            var speciesMap = new (string folderName, string animalCode)[]
            {
                ("Canis",    "Dog"),
                ("Equide",   "Horse"),
                ("Ruminant", "Cow"),
                ("Sus",      "Pig")
            };

            if (Directory.Exists(kemikRoot))
            {
                foreach (var sp in speciesMap)
                {
                    string spDir = Path.Combine(kemikRoot, sp.folderName);
                    if (!Directory.Exists(spDir)) continue;

                    foreach (string sysDir in Directory.GetDirectories(spDir))
                    {
                        foreach (string regDir in Directory.GetDirectories(sysDir))
                        {
                            string regName = Path.GetFileName(regDir);
                            foreach (string boneDir in Directory.GetDirectories(regDir))
                            {
                                string boneName = Path.GetFileName(boneDir);
                                string[] fbxFiles = Directory.GetFiles(boneDir, "*.fbx", SearchOption.AllDirectories);
                                if (fbxFiles.Length == 0) continue;

                                // En son değiştirilen FBX dosyasını seç
                                string srcFbx = fbxFiles
                                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                                    .First();

                                string destAssetPath = GetBoneFbxAssetPath(sp.animalCode, regName, boneName);
                                string destFull = Path.GetFullPath(destAssetPath);

                                FileInfo srcInfo = new FileInfo(srcFbx);
                                FileInfo destInfo = new FileInfo(destFull);

                                bool needsCopy = !destInfo.Exists ||
                                                 srcInfo.Length != destInfo.Length ||
                                                 Math.Abs((srcInfo.LastWriteTimeUtc - destInfo.LastWriteTimeUtc).TotalSeconds) > 1.0;

                                if (needsCopy)
                                {
                                    File.Copy(srcFbx, destFull, true);
                                    anyCopied = true;
                                    Debug.Log($"{LogTag} Kemik klasöründen yeni FBX projeye aktarıldı: {srcFbx} -> {destAssetPath}");
                                }
                            }
                        }
                    }
                }
            }

            // 2. Geriye dönük uyumluluk (Eski klasör yapısı varsa)
            var legacyPairs = new (string[] sources, string destAssetPath)[]
            {
                (new[] {
                    Path.Combine(desktopRoot, @"Humerus\source\Dog_humerus.fbx"),
                    Path.Combine(desktopRoot, @"Humerus\Dog_humerus.fbx"),
                    Path.Combine(desktopRoot, @"Dog_humerus.fbx")
                }, "Assets/Models/Bones/Dog_humerus.fbx"),

                (new[] {
                    Path.Combine(desktopRoot, @"Radius\source\Dog_Radius.fbx"),
                    Path.Combine(desktopRoot, @"Radius\Dog_Radius.fbx"),
                    Path.Combine(desktopRoot, @"Dog_Radius.fbx")
                }, "Assets/Models/Bones/Dog_Radius.fbx"),

                (new[] {
                    Path.Combine(desktopRoot, @"Scapula\Dog_scapula.fbx"),
                    Path.Combine(desktopRoot, @"Scapula\source\Dog_scapula.fbx"),
                    Path.Combine(desktopRoot, @"Dog_scapula.fbx")
                }, "Assets/Models/Bones/Dog_scapula.fbx"),

                (new[] {
                    Path.Combine(desktopRoot, @"Ulna\Dog_ulna.fbx"),
                    Path.Combine(desktopRoot, @"Ulna\source\Dog_ulna.fbx"),
                    Path.Combine(desktopRoot, @"Dog_ulna.fbx")
                }, "Assets/Models/Bones/Dog_ulna.fbx")
            };

            foreach (var pair in legacyPairs)
            {
                string destFull = Path.GetFullPath(pair.destAssetPath);
                DateTime destTime = File.Exists(destFull) ? File.GetLastWriteTimeUtc(destFull) : DateTime.MinValue;

                foreach (string src in pair.sources)
                {
                    if (File.Exists(src))
                    {
                        DateTime srcTime = File.GetLastWriteTimeUtc(src);
                        if (srcTime > destTime.AddSeconds(1))
                        {
                            File.Copy(src, destFull, true);
                            destTime = srcTime;
                            anyCopied = true;
                            Debug.Log($"{LogTag} Blender ile güncellenen FBX projeye aktarıldı: {src} -> {pair.destAssetPath}");
                        }
                    }
                }
            }

            if (anyCopied)
            {
                AssetDatabase.Refresh();
            }
        }

        private static void EnsurePhysicsMaterials()
        {
            EnsureFolder("Assets/PhysicsMaterials");

            // VR çarpışma kararlılığı ve çalışma zamanında 0 ms MeshCollider yüklemesi için Prebake Collision Meshes
            PlayerSettings.bakeCollisionMeshes = true;
            Time.fixedDeltaTime = 1f / 72f;
            Physics.defaultMaxDepenetrationVelocity = 8.0f;
            Physics.defaultSolverIterations = 14;
            Physics.defaultSolverVelocityIterations = 8;

            var boneMat = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(BonePhysMatPath);
            if (boneMat == null)
            {
                boneMat = new PhysicMaterial("BonePhysMat");
                AssetDatabase.CreateAsset(boneMat, BonePhysMatPath);
            }
            boneMat.dynamicFriction = 0.65f;
            boneMat.staticFriction = 0.80f;
            boneMat.bounciness = 0.0f;
            boneMat.frictionCombine = PhysicMaterialCombine.Average;
            boneMat.bounceCombine = PhysicMaterialCombine.Minimum;
            EditorUtility.SetDirty(boneMat);

            var tableMat = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(TablePhysMatPath);
            if (tableMat == null)
            {
                tableMat = new PhysicMaterial("TablePhysMat");
                AssetDatabase.CreateAsset(tableMat, TablePhysMatPath);
            }
            tableMat.dynamicFriction = 0.80f;
            tableMat.staticFriction = 0.85f;
            tableMat.bounciness = 0.0f;
            tableMat.frictionCombine = PhysicMaterialCombine.Average;
            tableMat.bounceCombine = PhysicMaterialCombine.Minimum;
            EditorUtility.SetDirty(tableMat);
        }

        private static Shader GetBakedUnlitShader()
        {
            return Shader.Find("AnatomiVR/BakedUnlit")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Texture");
        }

        private static void EnsureBakedTexturesAndMaterials()
        {
            EnsureFolder("Assets/Textures/Environment");
            EnsureFolder("Assets/Materials/Environment");
            EnsureFolder("Assets/Materials/Bones");

            string floorTexPath     = "Assets/Textures/Environment/FloorTiles_Baked.png";
            string wallTexPath      = "Assets/Textures/Environment/WallPanel_Baked.png";
            string ceilingTexPath   = "Assets/Textures/Environment/Ceiling_Baked.png";
            string poster1TexPath   = "Assets/Textures/Environment/Poster1_Baked.png";
            string poster2TexPath   = "Assets/Textures/Environment/Poster2_Baked.png";
            string logoTexPath      = "Assets/Textures/Environment/Logo_Baked.png";
            string blueTableTexPath = "Assets/Textures/Environment/BlueTable_Baked.png";
            string tableTexPath     = "Assets/Textures/Environment/Table_Baked.png";

            GenerateFloorTileTexture(floorTexPath);
            GenerateWallPanelTexture(wallTexPath);
            GenerateCeilingTexture(ceilingTexPath);
            GeneratePosterTexture(poster1TexPath, new Color(0.14f, 0.28f, 0.44f), new Color(0.88f, 0.93f, 0.97f));
            GeneratePosterTexture(poster2TexPath, new Color(0.16f, 0.38f, 0.34f), new Color(0.89f, 0.95f, 0.92f));
            GeneratePosterTexture(logoTexPath,    new Color(0.18f, 0.22f, 0.30f), new Color(0.95f, 0.92f, 0.84f));

            AssetDatabase.Refresh();

            // 4K bake_temiz_4k.png ve gerçek laboratuvar dokularının yüksek çözünürlüklü import ayarları
            ConfigureTextureImporter(BakedRoomTexPath, 4096, 8);
            ConfigureTextureImporter(poster1TexPath,   2048, 4);
            ConfigureTextureImporter(poster2TexPath,   2048, 4);
            ConfigureTextureImporter(logoTexPath,      2048, 4);
            ConfigureTextureImporter(blueTableTexPath, 2048, 4);
            ConfigureTextureImporter(tableTexPath,     2048, 4);

            // OpenVetAtlas VR (OVA VR) İkon ve Header Logo import ayarları
            ConfigureLogoAndIconTextures();

            Shader shader = GetBakedUnlitShader();

            // Kemik Materyalleri (Homojen, doğal fildişi/krem anatomik kemik rengi + 3D kontur shading)
            Color naturalBoneIvory = new Color(0.91f, 0.86f, 0.76f);
            CreateOrUpdateBakedMat("Assets/Materials/Bones/Bone_Default_Unlit.mat", shader,
                null, naturalBoneIvory, 0.80f, 0.32f, Vector2.one);
            CreateOrUpdateBakedMat("Assets/Materials/Bones/Dog_Humerus_Unlit.mat", shader,
                null, naturalBoneIvory, 0.80f, 0.32f, Vector2.one);
            CreateOrUpdateBakedMat("Assets/Materials/Bones/Dog_Radius_Unlit.mat", shader,
                null, naturalBoneIvory, 0.80f, 0.32f, Vector2.one);
            CreateOrUpdateBakedMat("Assets/Materials/Bones/Dog_Scapula_Unlit.mat", shader,
                null, naturalBoneIvory, 0.80f, 0.32f, Vector2.one);
            CreateOrUpdateBakedMat("Assets/Materials/Bones/Dog_Ulna_Unlit.mat", shader,
                null, naturalBoneIvory, 0.80f, 0.32f, Vector2.one);

            // 4K Tek Parça Bake Edilmiş Oda Materyali (bake_temiz_4k.png -> YAPILAR_Unity_Baked)
            CreateOrUpdateBakedMat("Assets/Materials/Environment/Oda_Baked_Unlit.mat", shader,
                BakedRoomTexPath, Color.white, 0.0f, 0.0f, Vector2.one);

            // Laboratuvar (lab_tasarimi.fbx) Materyalleri
            CreateOrUpdateBakedMat("Assets/Materials/Environment/Floor_Unlit.mat", shader,
                floorTexPath, Color.white, 0.25f, 0.20f, new Vector2(8f, 8f));
            CreateOrUpdateBakedMat("Assets/Materials/Environment/Room_Unlit.mat", shader,
                wallTexPath, Color.white, 0.35f, 0.10f, new Vector2(4f, 1f));
            CreateOrUpdateBakedMat("Assets/Materials/Environment/Ceiling_Unlit.mat", shader,
                ceilingTexPath, Color.white, 0.20f, 0.05f, new Vector2(6f, 6f));
            CreateOrUpdateBakedMat("Assets/Materials/Environment/Lamp_Unlit.mat", shader,
                null, new Color(0.98f, 0.98f, 0.94f), 0.0f, 0.0f, Vector2.one);
            // Hijyenik Fırçalanmış Paslanmaz Çelik (Inox) Diseksiyon Masası Materyali
            CreateOrUpdateBakedMat("Assets/Materials/Environment/Table_Unlit.mat", shader,
                File.Exists(tableTexPath) ? tableTexPath : null,
                File.Exists(tableTexPath) ? new Color(0.96f, 0.98f, 1.00f) : new Color(0.82f, 0.86f, 0.90f),
                0.28f, 0.55f, Vector2.one);
            CreateOrUpdateBakedMat("Assets/Materials/Environment/SkeletonBone_Unlit.mat", shader,
                null, new Color(0.92f, 0.87f, 0.78f), 0.85f, 0.30f, Vector2.one);
            CreateOrUpdateBakedMat("Assets/Materials/Environment/BlueTable_Unlit.mat", shader,
                File.Exists(blueTableTexPath) ? blueTableTexPath : null,
                File.Exists(blueTableTexPath) ? Color.white : new Color(0.20f, 0.38f, 0.56f), 0.30f, 0.35f, Vector2.one);
            CreateOrUpdateBakedMat("Assets/Materials/Environment/ScreenFrame_Unlit.mat", shader,
                null, new Color(0.16f, 0.18f, 0.22f), 0.55f, 0.50f, Vector2.one);
            CreateOrUpdateBakedMat("Assets/Materials/Environment/ScreenGlass_Unlit.mat", shader,
                null, new Color(0.06f, 0.08f, 0.12f), 0.10f, 0.60f, Vector2.one);
            CreateOrUpdateBakedMat("Assets/Materials/Environment/WindowFrame_Unlit.mat", shader,
                null, new Color(0.28f, 0.32f, 0.36f), 0.50f, 0.40f, Vector2.one);
            CreateOrUpdateBakedMat("Assets/Materials/Environment/WindowGlass_Unlit.mat", shader,
                null, new Color(0.76f, 0.89f, 0.98f), 0.0f, 0.20f, Vector2.one);
            CreateOrUpdateBakedMat("Assets/Materials/Environment/PosterFrame_Unlit.mat", shader,
                null, new Color(0.22f, 0.25f, 0.30f), 0.50f, 0.35f, Vector2.one);
            CreateOrUpdateBakedMat("Assets/Materials/Environment/Poster1_Unlit.mat", shader,
                poster1TexPath, Color.white, 0.0f, 0.0f, Vector2.one);
            CreateOrUpdateBakedMat("Assets/Materials/Environment/Poster2_Unlit.mat", shader,
                poster2TexPath, Color.white, 0.0f, 0.0f, Vector2.one);
            CreateOrUpdateBakedMat("Assets/Materials/Environment/Logo_Unlit.mat", shader,
                logoTexPath, Color.white, 0.0f, 0.0f, Vector2.one);
        }

        private static void ConfigureTextureImporter(string assetPath, int maxSize, int aniso)
        {
            if (!File.Exists(assetPath)) return;
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;
            bool changed = false;
            if (importer.maxTextureSize != maxSize) { importer.maxTextureSize = maxSize; changed = true; }
            if (importer.anisoLevel != aniso) { importer.anisoLevel = aniso; changed = true; }
            if (importer.filterMode != FilterMode.Trilinear) { importer.filterMode = FilterMode.Trilinear; changed = true; }
            if (importer.textureCompression != TextureImporterCompression.CompressedHQ)
            {
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                changed = true;
            }

            // Android (Meta Quest 3S) ASTC doku sıkıştırma ayarları
            TextureImporterPlatformSettings androidSettings = importer.GetPlatformTextureSettings("Android");
            TextureImporterFormat targetFormat = maxSize >= 4096
                ? TextureImporterFormat.ASTC_4x4
                : TextureImporterFormat.ASTC_6x6;
            if (!androidSettings.overridden ||
                androidSettings.maxTextureSize != maxSize ||
                androidSettings.format != targetFormat)
            {
                androidSettings.overridden = true;
                androidSettings.name = "Android";
                androidSettings.maxTextureSize = maxSize;
                androidSettings.format = targetFormat;
                androidSettings.compressionQuality = 100;
                importer.SetPlatformTextureSettings(androidSettings);
                changed = true;
            }

            if (changed) importer.SaveAndReimport();
        }

        private static void ConfigureLogoAndIconTextures()
        {
            const string squareIconPath = "Assets/Textures/UI/OVA_AppIcon_Square.png";
            if (File.Exists(squareIconPath))
            {
                var imp = AssetImporter.GetAtPath(squareIconPath) as TextureImporter;
                if (imp != null)
                {
                    bool changed = false;
                    if (imp.textureType != TextureImporterType.Sprite) { imp.textureType = TextureImporterType.Sprite; changed = true; }
                    if (imp.spriteImportMode != SpriteImportMode.Single) { imp.spriteImportMode = SpriteImportMode.Single; changed = true; }
                    if (!imp.isReadable) { imp.isReadable = true; changed = true; }
                    if (imp.maxTextureSize != 1024) { imp.maxTextureSize = 1024; changed = true; }
                    if (changed) imp.SaveAndReimport();
                }
            }

            const string headerLogoPath = "Assets/Resources/OVA_Header_Logo.png";
            if (File.Exists(headerLogoPath))
            {
                var imp = AssetImporter.GetAtPath(headerLogoPath) as TextureImporter;
                if (imp != null)
                {
                    bool changed = false;
                    if (imp.textureType != TextureImporterType.Sprite) { imp.textureType = TextureImporterType.Sprite; changed = true; }
                    if (imp.spriteImportMode != SpriteImportMode.Single) { imp.spriteImportMode = SpriteImportMode.Single; changed = true; }
                    if (!imp.isReadable) { imp.isReadable = true; changed = true; }
                    if (imp.maxTextureSize != 1024) { imp.maxTextureSize = 1024; changed = true; }
                    if (changed) imp.SaveAndReimport();
                }
            }
        }

        private static Material CreateOrUpdateBakedMat(
            string matPath,
            Shader shader,
            string texPath,
            Color baseColor,
            float shadingStrength,
            float smoothness,
            Vector2 tiling)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat.shader = shader;
            }

            Texture2D tex = !string.IsNullOrEmpty(texPath) ? AssetDatabase.LoadAssetAtPath<Texture2D>(texPath) : null;
            mat.mainTexture = tex;
            if (mat.HasProperty("_BaseMap"))
            {
                mat.SetTexture("_BaseMap", tex);
                mat.SetTextureScale("_BaseMap", tiling);
            }
            mat.mainTextureScale = tiling;

            mat.color = baseColor;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", baseColor);
            if (mat.HasProperty("_ShadingStrength"))
                mat.SetFloat("_ShadingStrength", shadingStrength);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", smoothness);

            mat.SetInt("_CastShadows", 0);
            mat.SetInt("_ReceiveShadows", 0);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void UpgradeSampleMaterialsToURP()
        {
            Shader shader = GetBakedUnlitShader();
            if (shader == null) return;

            string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Samples" });
            foreach (string guid in matGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m != null && (m.shader == null || m.shader.name == "Standard" || m.shader.name.Contains("InternalErrorShader")))
                {
                    Color c = m.HasProperty("_Color") ? m.color : new Color(0.25f, 0.28f, 0.32f, 1f);
                    m.shader = shader;
                    m.color = c;
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                    if (m.HasProperty("_ShadingStrength")) m.SetFloat("_ShadingStrength", 0.65f);
                }
                if (m != null && !m.enableInstancing)
                {
                    m.enableInstancing = true;
                }
                if (m != null)
                {
                    EditorUtility.SetDirty(m);
                }
            }
        }

        private static void GenerateFloorTileTexture(string path)
        {
            if (File.Exists(path)) return;
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isGrout = (x < 4 || x >= size - 4 || y < 4 || y >= size - 4);
                    bool isSubGrout = (Mathf.Abs(x - size / 2) < 2 || Mathf.Abs(y - size / 2) < 2);
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(size * 0.5f, size * 0.5f)) / (size * 0.707f);
                    float subtleBevel = Mathf.Clamp01(1f - dist * 0.08f);

                    Color baseCol = isGrout
                        ? new Color(0.42f, 0.45f, 0.48f)
                        : isSubGrout
                            ? new Color(0.52f, 0.56f, 0.60f)
                            : new Color(0.68f, 0.72f, 0.76f) * subtleBevel;
                    tex.SetPixel(x, y, baseCol);
                }
            }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static void GenerateWallPanelTexture(string path)
        {
            if (File.Exists(path)) return;
            const int w = 256, h = 512;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, true);
            for (int y = 0; y < h; y++)
            {
                float ny = (float)y / h;
                // Alt kısım medikal mavi-gri kuşak, orta kısım metal şerit, üst kısım klinik krem-beyaz
                Color rowCol;
                if (ny < 0.04f)
                    rowCol = new Color(0.24f, 0.27f, 0.30f); // Süpürgelik
                else if (ny < 0.36f)
                    rowCol = Color.Lerp(new Color(0.66f, 0.73f, 0.79f), new Color(0.72f, 0.78f, 0.84f), ny / 0.36f);
                else if (ny < 0.385f)
                    rowCol = new Color(0.32f, 0.48f, 0.64f); // Medikal şerit
                else
                {
                    float ao = 1f - Mathf.Pow(Mathf.Max(0f, (ny - 0.85f) / 0.15f), 2f) * 0.12f;
                    rowCol = new Color(0.89f, 0.91f, 0.93f) * ao;
                }

                for (int x = 0; x < w; x++)
                {
                    bool panelSeam = (x == 0 || x == w - 1);
                    tex.SetPixel(x, y, panelSeam ? rowCol * 0.93f : rowCol);
                }
            }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static void GenerateCeilingTexture(string path)
        {
            if (File.Exists(path)) return;
            const int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, true);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool grid = (x < 3 || y < 3 || x > size - 4 || y > size - 4);
                    Color c = grid ? new Color(0.70f, 0.73f, 0.76f) : new Color(0.90f, 0.92f, 0.94f);
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static void GeneratePosterTexture(string path, Color headerCol, Color bgCol)
        {
            if (File.Exists(path)) return;
            const int w = 256, h = 384;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, true);
            for (int y = 0; y < h; y++)
            {
                float ny = (float)y / h;
                for (int x = 0; x < w; x++)
                {
                    float nx = (float)x / w;
                    Color c = bgCol;
                    if (ny > 0.82f)
                        c = headerCol;
                    else if (nx > 0.08f && nx < 0.92f && ny > 0.08f && ny < 0.76f)
                    {
                        // Poster içerik kutuları ve diyagram çizgileri
                        bool inBox = (ny > 0.42f && ny < 0.74f && nx > 0.12f && nx < 0.88f);
                        bool isLine = ((int)(ny * 40) % 3 == 0) && ny < 0.38f && nx > 0.14f && nx < 0.86f;
                        if (inBox) c = Color.Lerp(bgCol, headerCol, 0.15f);
                        else if (isLine) c = Color.Lerp(bgCol, headerCol, 0.45f);
                    }
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        private static void EnsureMenuButtonPrefab()
        {
            EnsureFolder("Assets/Prefabs/UI");

            var buttonObj = new GameObject("MenuButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Button));
            var rt = buttonObj.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(660f, 102f);

            var img = buttonObj.GetComponent<Image>();
            img.color = new Color(0.04f, 0.18f, 0.32f, 0.95f);

            var outline = buttonObj.GetComponent<Outline>();
            outline.effectColor = new Color(0.00f, 0.88f, 1.00f, 0.80f);
            outline.effectDistance = new Vector2(2f, 2f);

            var btn = buttonObj.GetComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.75f, 0.96f, 1.0f, 1f);
            colors.pressedColor = new Color(0.45f, 0.85f, 1.0f, 1f);
            colors.selectedColor = new Color(0.65f, 0.92f, 1.0f, 1f);
            colors.disabledColor = Color.white;
            btn.colors = colors;

            // Sol neon vurgu şeridi (Fütüristik HUD AccentBar)
            var accentObj = new GameObject("AccentBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            accentObj.transform.SetParent(buttonObj.transform, false);
            var accentRt = accentObj.GetComponent<RectTransform>();
            accentRt.anchorMin = new Vector2(0f, 0f);
            accentRt.anchorMax = new Vector2(0f, 1f);
            accentRt.pivot = new Vector2(0f, 0.5f);
            accentRt.anchoredPosition = Vector2.zero;
            accentRt.sizeDelta = new Vector2(10f, 0f);
            accentObj.GetComponent<Image>().color = new Color(0.00f, 0.95f, 1.00f, 1.00f);

            var textObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObj.transform.SetParent(buttonObj.transform, false);
            var textRt = textObj.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(24f, 4f);
            textRt.offsetMax = new Vector2(-18f, -4f);

            var tmp = textObj.GetComponent<TextMeshProUGUI>();
            tmp.text = "►   Canis";
            tmp.fontSize = 27f;
            tmp.fontStyle = FontStyles.Bold;
            tmp.characterSpacing = 2.5f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.90f, 0.98f, 1.00f, 1.00f);

            var iconObj = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconObj.transform.SetParent(buttonObj.transform, false);
            var iconRt = iconObj.GetComponent<RectTransform>();
            iconRt.anchorMin = new Vector2(0f, 0.5f);
            iconRt.anchorMax = new Vector2(0f, 0.5f);
            iconRt.anchoredPosition = new Vector2(36f, 0f);
            iconRt.sizeDelta = new Vector2(36f, 36f);
            iconObj.GetComponent<Image>().enabled = false;

            PrefabUtility.SaveAsPrefabAsset(buttonObj, MenuButtonPrefabPath);
            UnityEngine.Object.DestroyImmediate(buttonObj);
        }

        private struct BonePrefabBuildConfig
        {
            public string Key;
            public string NameTR;
            public string Fbx;
            public string Mat;
        }

        private static List<BonePrefabBuildConfig> GatherAllBonePrefabConfigs()
        {
            var list = new List<BonePrefabBuildConfig>
            {
                new BonePrefabBuildConfig { Key = "Dog_Humerus", NameTR = "Humerus", Fbx = "Assets/Models/Bones/Dog_humerus.fbx", Mat = "Assets/Materials/Bones/Dog_Humerus_Unlit.mat" },
                new BonePrefabBuildConfig { Key = "Dog_Radius",  NameTR = "Radius",  Fbx = "Assets/Models/Bones/Dog_Radius.fbx",  Mat = "Assets/Materials/Bones/Dog_Radius_Unlit.mat" },
                new BonePrefabBuildConfig { Key = "Dog_Scapula", NameTR = "Scapula", Fbx = "Assets/Models/Bones/Dog_scapula.fbx", Mat = "Assets/Materials/Bones/Dog_Scapula_Unlit.mat" },
                new BonePrefabBuildConfig { Key = "Dog_Ulna",    NameTR = "Ulna",    Fbx = "Assets/Models/Bones/Dog_ulna.fbx",    Mat = "Assets/Materials/Bones/Dog_Ulna_Unlit.mat" },
            };

            // 'Kemik\<Tür>\<Sistem>\<Bölge>\<Kemik>' klasörlerine sonradan eklenen tüm FBX kemiklerini de otomatik dahil et
            string kemikRoot = @"C:\Users\Osteometri\Desktop\Anatomi VR\Kemik";
            var speciesMap = new (string folderName, string animalCode)[]
            {
                ("Canis",    "Dog"),
                ("Equide",   "Horse"),
                ("Ruminant", "Cow"),
                ("Sus",      "Pig")
            };

            if (Directory.Exists(kemikRoot))
            {
                foreach (var sp in speciesMap)
                {
                    string spDir = Path.Combine(kemikRoot, sp.folderName);
                    if (!Directory.Exists(spDir)) continue;

                    foreach (string sysDir in Directory.GetDirectories(spDir))
                    {
                        foreach (string regDir in Directory.GetDirectories(sysDir))
                        {
                            string regName = Path.GetFileName(regDir);
                            foreach (string boneDir in Directory.GetDirectories(regDir))
                            {
                                string boneName = Path.GetFileName(boneDir);
                                string key = GetBoneAddressableKey(sp.animalCode, regName, boneName);
                                string fbxAssetPath = GetBoneFbxAssetPath(sp.animalCode, regName, boneName);

                                if (!File.Exists(Path.GetFullPath(fbxAssetPath)))
                                    continue;

                                if (list.Any(c => string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase)))
                                    continue;

                                list.Add(new BonePrefabBuildConfig
                                {
                                    Key = key,
                                    NameTR = boneName,
                                    Fbx = fbxAssetPath,
                                    Mat = "Assets/Materials/Bones/Bone_Default_Unlit.mat"
                                });
                            }
                        }
                    }
                }
            }

            return list;
        }

        private static void EnsureBonePrefabs()
        {
            EnsureFolder("Assets/Prefabs/Bones");
            var bonePhysMat = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(BonePhysMatPath);
            var boneConfigs = GatherAllBonePrefabConfigs();

            foreach (var cfg in boneConfigs)
            {
                string prefabPath = $"Assets/Prefabs/Bones/{cfg.Key}.prefab";

                // Kullanıcı daha önce Pin Düzenleyici ile bu prefab üzerinde özel etiket kaydettiyse koru
                List<AnatomicalLandmark> existingCustomLandmarks = null;
                GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (existingPrefab != null)
                {
                    var oldLabels = existingPrefab.GetComponent<BoneAnatomicalLabels>();
                    if (oldLabels != null && oldLabels.landmarks != null && oldLabels.landmarks.Count > 0)
                    {
                        existingCustomLandmarks = new List<AnatomicalLandmark>(oldLabels.landmarks);
                    }
                }

                var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(cfg.Fbx);
                GameObject tempInstance;

                if (fbxAsset != null)
                {
                    tempInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset);
                    tempInstance.name = cfg.Key;
                    PrefabUtility.UnpackPrefabInstance(tempInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                }
                else
                {
                    tempInstance = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    tempInstance.name = cfg.Key;
                }

                // Eğer Blender'dan çoklu obje (Kemik Mesh + Pin Empty'leri) olarak export edildiyse,
                // önce Pin konumlarını Mesh'in kendi yerel uzayına göre kaydet, ardından Mesh'i kök objeye taşı:
                var labels = tempInstance.GetComponent<BoneAnatomicalLabels>();
                if (labels == null) labels = tempInstance.AddComponent<BoneAnatomicalLabels>();
                bool foundBlenderPins = labels.SyncFromBlenderChildPinsIfPresent(replaceExistingIfFound: true);

                var childMeshFilter = tempInstance.GetComponentInChildren<MeshFilter>(true);
                if (childMeshFilter != null && childMeshFilter.gameObject != tempInstance)
                {
                    Mesh boneMesh = childMeshFilter.sharedMesh;
                    var rootMf = tempInstance.GetComponent<MeshFilter>();
                    if (rootMf == null) rootMf = tempInstance.AddComponent<MeshFilter>();
                    rootMf.sharedMesh = boneMesh;

                    var rootMr = tempInstance.GetComponent<MeshRenderer>();
                    if (rootMr == null) rootMr = tempInstance.AddComponent<MeshRenderer>();

                    tempInstance.transform.localRotation = childMeshFilter.transform.localRotation;

                    // Alt objeleri temizle ve Blender pinlerini kök altında temizce yeniden oluştur
                    for (int cIdx = tempInstance.transform.childCount - 1; cIdx >= 0; cIdx--)
                    {
                        UnityEngine.Object.DestroyImmediate(tempInstance.transform.GetChild(cIdx).gameObject);
                    }

                    if (foundBlenderPins && labels.landmarks != null)
                    {
                        foreach (var lm in labels.landmarks)
                        {
                            var pinChild = new GameObject($"Pin_{lm.labelText}");
                            pinChild.transform.SetParent(tempInstance.transform, false);
                            pinChild.transform.localPosition = lm.anchorLocalPos;
                        }
                    }
                }

                var mat = AssetDatabase.LoadAssetAtPath<Material>(cfg.Mat)
                       ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Bones/Bone_Default_Unlit.mat");
                foreach (var r in tempInstance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mat != null) r.sharedMaterial = mat;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }

                var rb = tempInstance.GetComponent<Rigidbody>();
                if (rb == null) rb = tempInstance.AddComponent<Rigidbody>();
                rb.mass = 0.5f;
                rb.useGravity = true;
#if UNITY_6000_0_OR_NEWER
                rb.linearDamping = 0.6f;
                rb.angularDamping = 2.5f;
#else
                rb.drag = 0.6f;
                rb.angularDrag = 2.5f;
#endif
                // Masadan/duvarlardan geçmeyi önleyen ContinuousDynamic + BoneIdentifier yüzey kilidi:
                rb.maxDepenetrationVelocity = 8.0f;
                rb.solverIterations = 16;
                rb.solverVelocityIterations = 8;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                Bounds localMeshBounds = new Bounds(Vector3.zero, Vector3.one);
                var meshFilter = tempInstance.GetComponentInChildren<MeshFilter>(true);
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
                    localMeshBounds = meshFilter.sharedMesh.bounds;

                    var mc = tempInstance.GetComponent<MeshCollider>();
                    if (mc == null) mc = tempInstance.AddComponent<MeshCollider>();
                    mc.sharedMesh = meshFilter.sharedMesh;
                    mc.convex = true;
                    mc.sharedMaterial = bonePhysMat;

                    // Ek güvenlik: İnce iç omurga BoxCollider (Fossa/çukur bölgelerin üzerini kapatmaması için 0.32x iç çekirdek)
                    var bc = tempInstance.GetComponent<BoxCollider>();
                    if (bc == null) bc = tempInstance.AddComponent<BoxCollider>();
                    bc.center = localMeshBounds.center;
                    bc.size = localMeshBounds.size * 0.32f;
                    bc.sharedMaterial = bonePhysMat;
                }

                // Yeni taranan FBX modellerinin ham birim ölçeği ne olursa olsun (mm, cm, m),
                // masada gerçekçi anatomik kemik boyutunda (~25-32 cm) görünmesini garanti altına al:
                float maxMeshDim = Mathf.Max(localMeshBounds.size.x, localMeshBounds.size.y, localMeshBounds.size.z);
                Vector3 computedScale = BoneRealisticScale;
                if (maxMeshDim > 0.0001f)
                {
                    float defaultWorldLen = maxMeshDim * BoneRealisticScale.x;
                    if (defaultWorldLen < 0.16f || defaultWorldLen > 0.38f)
                    {
                        float uniformScale = 0.27f / maxMeshDim;
                        computedScale = new Vector3(uniformScale, uniformScale, uniformScale);
                    }
                }
                tempInstance.transform.localScale = computedScale;

                var grab = tempInstance.GetComponent<XRGrabInteractable>();
                if (grab == null) grab = tempInstance.AddComponent<XRGrabInteractable>();
                grab.movementType = XRBaseInteractable.MovementType.VelocityTracking;
                grab.throwOnDetach = true;
                grab.useDynamicAttach = true;
                grab.smoothPosition = true;
                grab.smoothPositionAmount = 16f;
                grab.tightenPosition = 0.0f;
                grab.smoothRotation = true;
                grab.smoothRotationAmount = 14f;
                grab.tightenRotation = 0.0f;
                grab.velocityDamping = 1.0f;
                grab.velocityScale = 0.65f;
                grab.angularVelocityDamping = 1.0f;
                grab.angularVelocityScale = 0.65f;

                grab.colliders.Clear();
                foreach (Collider rootCol in tempInstance.GetComponents<Collider>())
                {
                    if (rootCol != null && !rootCol.isTrigger)
                        grab.colliders.Add(rootCol);
                }

                var id = tempInstance.GetComponent<BoneIdentifier>();
                if (id == null) id = tempInstance.AddComponent<BoneIdentifier>();
                id.AddressableKey = cfg.Key;
                id.DisplayNameTR = cfg.NameTR;

                // 3B Holografik Anatomik Etiket (Pin) Sistemi (X / A tuşu ile açılır/kapanır)
                if (!foundBlenderPins)
                {
                    labels.landmarks = existingCustomLandmarks != null && existingCustomLandmarks.Count > 0
                        ? existingCustomLandmarks
                        : BuildDefaultAnatomicalLandmarks(cfg.Key, localMeshBounds);
                }

                // Prebake Collision Meshes & Pre-Baked Normals:
                // 1) Kemik üzerindeki tüm pinlerin gerçek dış yüzey normallerini Editör'de 1 kez hesaplayıp
                //    prefab içine göm (Quest 3S'te çalışma zamanında 0 ms vertex taraması!).
                labels.PrebakeAllPinNormals();

                // 2) Hem Convex hem de Non-Convex (Fossa) PhysX çarpışma verilerini derleme öncesi pişir:
                if (meshFilter != null && meshFilter.sharedMesh != null)
                {
#if UNITY_6000_0_OR_NEWER
                    var meshId = meshFilter.sharedMesh.GetEntityId();
                    Physics.BakeMesh(meshId, true);
                    Physics.BakeMesh(meshId, false);
#else
                    int meshId = meshFilter.sharedMesh.GetInstanceID();
                    Physics.BakeMesh(meshId, true);
                    Physics.BakeMesh(meshId, false);
#endif
                }

                if (foundBlenderPins)
                {
                    Debug.Log($"{LogTag} '{cfg.Key}' FBX modelinden {labels.landmarks.Count} adet Blender Empty/Pin noktası ve Pre-Baked normaller otomatik alındı! (MeshBounds={localMeshBounds.size:F3}, Scale={computedScale.x:F4})");
                }

                PrefabUtility.SaveAsPrefabAsset(tempInstance, prefabPath);
                UnityEngine.Object.DestroyImmediate(tempInstance);
            }
        }

        private static List<AnatomicalLandmark> BuildDefaultAnatomicalLandmarks(string boneKey, Bounds b)
        {
            Vector3 c = b.center;
            Vector3 e = b.extents;

            // En uzun ekseni (kemik boyu) otomatik tespit et
            int longAxis = 2; // 0=X, 1=Y, 2=Z
            if (e.x >= e.y && e.x >= e.z) longAxis = 0;
            else if (e.y >= e.x && e.y >= e.z) longAxis = 1;

            Vector3 dirProximal = Vector3.zero;
            dirProximal[longAxis] = 1f;
            Vector3 dirDistal = -dirProximal;

            int sideAxis1 = (longAxis + 1) % 3;
            int sideAxis2 = (longAxis + 2) % 3;
            Vector3 dirSide1 = Vector3.zero; dirSide1[sideAxis1] = 1f;
            Vector3 dirSide2 = Vector3.zero; dirSide2[sideAxis2] = 1f;

            float len = b.extents[longAxis];
            float r1  = Mathf.Max(0.05f, b.extents[sideAxis1]);
            float r2  = Mathf.Max(0.05f, b.extents[sideAxis2]);
            float outDist = Mathf.Max(len * 0.42f, 0.35f);

            AnatomicalLandmark MakePin(string title, float alongFrac, float s1Frac, float s2Frac, Vector3 labelOutDir)
            {
                Vector3 anchor = c + dirProximal * (alongFrac * len) + dirSide1 * (s1Frac * r1) + dirSide2 * (s2Frac * r2);
                Vector3 label  = anchor + labelOutDir.normalized * outDist;
                return new AnatomicalLandmark
                {
                    labelText = title,
                    anchorLocalPos = anchor,
                    labelLocalPos = label
                };
            }

            switch (boneKey)
            {
                case "Dog_Humerus":
                    return new List<AnatomicalLandmark>
                    {
                        MakePin("Caput humeri",        0.82f,  0.45f,  0.20f, dirProximal * 0.5f + dirSide1),
                        MakePin("Tuberculum majus",    0.88f, -0.55f,  0.25f, dirProximal * 0.5f - dirSide1),
                        MakePin("Tuberculum minus",    0.72f,  0.15f, -0.55f, -dirSide2 + dirProximal * 0.3f),
                        MakePin("Corpus humeri",       0.00f,  0.45f,  0.00f, dirSide1),
                        MakePin("Condylus humeri",    -0.85f, -0.45f,  0.15f, dirDistal * 0.5f - dirSide1),
                        MakePin("Fossa olecrani",     -0.72f,  0.40f, -0.35f, dirSide1 + dirDistal * 0.3f)
                    };

                case "Dog_Scapula":
                    return new List<AnatomicalLandmark>
                    {
                        MakePin("Spina scapulae",       0.10f,  0.55f,  0.00f, dirSide1),
                        MakePin("Fossa supraspinata",   0.35f,  0.30f,  0.50f, dirSide2 + dirSide1 * 0.5f),
                        MakePin("Fossa infraspinata",   0.25f,  0.30f, -0.50f, -dirSide2 + dirSide1 * 0.5f),
                        MakePin("Acromion",            -0.65f,  0.60f,  0.15f, dirSide1 + dirDistal * 0.4f),
                        MakePin("Cavitas glenoidalis", -0.88f,  0.00f,  0.00f, dirDistal + dirSide2 * 0.5f)
                    };

                case "Dog_Radius":
                    return new List<AnatomicalLandmark>
                    {
                        MakePin("Caput radii",          0.85f,  0.40f,  0.20f, dirProximal * 0.4f + dirSide1),
                        MakePin("Collum radii",         0.62f, -0.45f,  0.00f, -dirSide1),
                        MakePin("Corpus radii",         0.00f,  0.45f,  0.00f, dirSide1),
                        MakePin("Trochlea radii",      -0.85f,  0.35f,  0.25f, dirDistal * 0.4f + dirSide1),
                        MakePin("Processus styloideus",-0.92f, -0.45f, -0.20f, dirDistal * 0.4f - dirSide1)
                    };

                case "Dog_Ulna":
                    return new List<AnatomicalLandmark>
                    {
                        MakePin("Tuber olecrani",       0.90f,  0.35f,  0.15f, dirProximal * 0.5f + dirSide1),
                        MakePin("Olecranon",            0.72f, -0.45f,  0.20f, -dirSide1 + dirProximal * 0.3f),
                        MakePin("Incisura trochlearis", 0.52f,  0.50f, -0.20f, dirSide1),
                        MakePin("Corpus ulnae",        -0.05f, -0.45f,  0.00f, -dirSide1),
                        MakePin("Caput ulnae",         -0.88f,  0.40f,  0.15f, dirDistal * 0.4f + dirSide1)
                    };

                default:
                    return new List<AnatomicalLandmark>
                    {
                        MakePin("Extremitas proximalis", 0.80f, 0.4f, 0f, dirSide1),
                        MakePin("Corpus",                0.00f, -0.4f, 0f, -dirSide1),
                        MakePin("Extremitas distalis",  -0.80f, 0.4f, 0f, dirSide1)
                    };
            }
        }

        // ─────────── 1. Gerçek 3D Laboratuvar Sahnesi (lab_tasarimi.fbx) ───────────

        private static void RebuildLabEnvironmentAndUI(MenuData masterMenu)
        {
            Scene scene = SceneManager.GetActiveScene();

            // Gerçek zamanlı ışıkları ve gölgeleri kapat, ortamı aydınlık tut
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.white;
            foreach (Light l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                l.shadows = LightShadows.None;
                l.enabled = false;
            }

            // Eski [Environment], [BackgroundProps], [SpawnPoint], [SmartScreen], [TutorialTablet] objelerini temizle
            string[] toRebuild = { "[Environment]", "[BackgroundProps]", "[SpawnPoint]", "[SmartScreen]", "[TutorialTablet]" };
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (toRebuild.Contains(root.name))
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            var tablePhysMat = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(TablePhysMatPath);

            // 1. [Environment] & [BackgroundProps] — YAPILAR_Unity_Baked (bake_temiz_4k.png) + lab_tasarimi.fbx
            var envRoot = new GameObject("[Environment]");
            var propsRoot = new GameObject("[BackgroundProps]");

            var matOdaBaked = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/Oda_Baked_Unlit.mat");
            var bakedRoomPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BakedRoomObjPath);
            bool hasBakedRoom = false;

            if (bakedRoomPrefab != null && matOdaBaked != null)
            {
                var bakedRoomInstance = (GameObject)PrefabUtility.InstantiatePrefab(bakedRoomPrefab);
                bakedRoomInstance.name = "YAPILAR_Unity_Baked";
                bakedRoomInstance.transform.SetParent(envRoot.transform, false);
                bakedRoomInstance.transform.localPosition = Vector3.zero;
                bakedRoomInstance.transform.localRotation = Quaternion.identity;
                bakedRoomInstance.transform.localScale = Vector3.one;
                PrefabUtility.UnpackPrefabInstance(bakedRoomInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                foreach (var mr in bakedRoomInstance.GetComponentsInChildren<MeshRenderer>(true))
                {
                    mr.sharedMaterials = Enumerable.Repeat(matOdaBaked, mr.sharedMaterials.Length).ToArray();
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                }

                // Oda sınırlarının X=[-3.5, 2.5] (center.x ≈ -0.5) yönünde olduğundan emin ol
                var firstMr = bakedRoomInstance.GetComponentInChildren<MeshRenderer>(true);
                if (firstMr != null && firstMr.bounds.center.x > 0.1f)
                {
                    bakedRoomInstance.transform.localScale = new Vector3(-1f, 1f, 1f);
                }

                SetFullyStatic(bakedRoomInstance);
                hasBakedRoom = true;
            }

            var labFbx = AssetDatabase.LoadAssetAtPath<GameObject>(LabFbxPath);
            if (labFbx != null)
            {
                var labInstance = (GameObject)PrefabUtility.InstantiatePrefab(labFbx);
                labInstance.name = "Lab_Room_Model";
                labInstance.transform.SetParent(envRoot.transform, false);
                labInstance.transform.localPosition = Vector3.zero;
                labInstance.transform.localRotation = Quaternion.identity;
                labInstance.transform.localScale = Vector3.one;
                PrefabUtility.UnpackPrefabInstance(labInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

                // Materyalleri yükle
                var matFloor       = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/Floor_Unlit.mat");
                var matWalls       = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/Room_Unlit.mat");
                var matCeiling     = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/Ceiling_Unlit.mat");
                var matLamp        = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/Lamp_Unlit.mat");
                var matTable       = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/Table_Unlit.mat");
                var matSkelBone    = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/SkeletonBone_Unlit.mat");
                var matBlueTable   = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/BlueTable_Unlit.mat");
                var matScreenFrame = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/ScreenFrame_Unlit.mat");
                var matScreenGlass = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/ScreenGlass_Unlit.mat");
                var matWinFrame    = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/WindowFrame_Unlit.mat");
                var matWinGlass    = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/WindowGlass_Unlit.mat");
                var matPosterFrame = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/PosterFrame_Unlit.mat");
                var matPoster1     = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/Poster1_Unlit.mat");
                var matPoster2     = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/Poster2_Unlit.mat");
                var matLogo        = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/Logo_Unlit.mat");

                Transform skeletonTableTransform = null;
                var toRemove = new List<GameObject>();

                foreach (Transform child in labInstance.GetComponentsInChildren<Transform>(true))
                {
                    if (child == labInstance || child == labInstance.transform) continue;

                    var mr = child.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        mr.receiveShadows = false;

                        string n = child.name.ToLowerInvariant();
                        if (n.Contains("zemin"))
                        {
                            if (hasBakedRoom) toRemove.Add(child.gameObject);
                            else { mr.sharedMaterials = new[] { matFloor }; SetFullyStatic(child.gameObject); }
                        }
                        else if (n.Contains("duvar"))
                        {
                            if (hasBakedRoom) toRemove.Add(child.gameObject);
                            else { mr.sharedMaterials = new[] { matWalls }; SetFullyStatic(child.gameObject); }
                        }
                        else if (n == "tavan")
                        {
                            if (hasBakedRoom) toRemove.Add(child.gameObject);
                            else { mr.sharedMaterials = new[] { matCeiling }; SetFullyStatic(child.gameObject); }
                        }
                        else if (n.Contains("i\u015f\u0131k") || n.Contains("isik") || child.parent?.name == "tavan")
                        {
                            if (hasBakedRoom) toRemove.Add(child.gameObject);
                            else { mr.sharedMaterials = new[] { matLamp }; SetFullyStatic(child.gameObject); }
                        }
                        else if (n.Contains("iskeletli"))
                        {
                            mr.sharedMaterials = new[] { matSkelBone, matBlueTable };
                            skeletonTableTransform = child;
                        }
                        else if (n == "masa")
                        {
                            // Diseksiyon Masası: KESİNLİKLE Static DEĞİL!
                            mr.sharedMaterials = new[] { matTable };
                            child.gameObject.isStatic = false;
                            GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
                        }
                        else if (n.Contains("ekran"))
                        {
                            if (hasBakedRoom) toRemove.Add(child.gameObject);
                            else { mr.sharedMaterials = new[] { matScreenFrame, matScreenGlass }; SetFullyStatic(child.gameObject); }
                        }
                        else if (n.Contains("cam"))
                        {
                            if (hasBakedRoom) toRemove.Add(child.gameObject);
                            else { mr.sharedMaterials = new[] { matWinFrame, matWinGlass }; SetFullyStatic(child.gameObject); }
                        }
                        else if (n.Contains(".001"))
                        {
                            mr.sharedMaterials = new[] { matPosterFrame, matPoster2 };
                            SetFullyStatic(child.gameObject);
                        }
                        else if (n.Contains(".002"))
                        {
                            mr.sharedMaterials = new[] { matPosterFrame, matLogo };
                            SetFullyStatic(child.gameObject);
                        }
                        else if (n.Contains("afi"))
                        {
                            mr.sharedMaterials = new[] { matPoster1, matPosterFrame };
                            SetFullyStatic(child.gameObject);
                        }
                        else
                        {
                            mr.sharedMaterials = new[] { matScreenFrame };
                            SetFullyStatic(child.gameObject);
                        }
                    }
                }

                foreach (GameObject go in toRemove)
                {
                    if (go != null) UnityEngine.Object.DestroyImmediate(go);
                }

                // Yüksek poligonlu (135k tri) tam köpek iskeletini [BackgroundProps] altına taşı
                // Zorunlu Optimizasyon Kuralları: Rigidbody YOK, Collider YOK, XR Interactable YOK, Tamamen Static!
                if (skeletonTableTransform != null)
                {
                    skeletonTableTransform.SetParent(propsRoot.transform, true);
                    skeletonTableTransform.name = "Dog_FullSkeleton_Decor";
                    foreach (var col in skeletonTableTransform.GetComponentsInChildren<Collider>(true))
                        UnityEngine.Object.DestroyImmediate(col);
                    foreach (var rb in skeletonTableTransform.GetComponentsInChildren<Rigidbody>(true))
                        UnityEngine.Object.DestroyImmediate(rb);
                    foreach (var grab in skeletonTableTransform.GetComponentsInChildren<XRBaseInteractable>(true))
                        UnityEngine.Object.DestroyImmediate(grab);
                    SetFullyStatic(skeletonTableTransform.gameObject);
                }
            }

            // Diseksiyon Masası Fizik Collider'ı (Non-Static, masa üst yüzeyi y = 1.023m)
            var tableColliderGO = new GameObject("DissectionTable_Collider");
            tableColliderGO.transform.SetParent(envRoot.transform, false);
            tableColliderGO.transform.position = new Vector3(-0.001f, 0.635f, 0.000f);
            tableColliderGO.isStatic = false;
            var tableBox = tableColliderGO.AddComponent<BoxCollider>();
            tableBox.size = new Vector3(0.883f, 0.775f, 2.229f);
            tableBox.sharedMaterial = tablePhysMat;

            // İskeletli Masa Koruyucu Kutu Bariyeri (135k tri 'Dog_FullSkeleton_Decor' objesine dokunmadan ayrı tek BoxCollider)
            var skelColliderGO = new GameObject("SkeletonTable_Collider");
            skelColliderGO.transform.SetParent(envRoot.transform, false);
            skelColliderGO.transform.position = new Vector3(-2.345f, 0.965f, 0.000f);
            SetFullyStatic(skelColliderGO);
            var skelBox = skelColliderGO.AddComponent<BoxCollider>();
            skelBox.size = new Vector3(0.700f, 1.575f, 1.300f);
            skelBox.sharedMaterial = tablePhysMat;

            // Zemin Fizik Collider'ı (zemin üst yüzeyi y = 0.282m, 2m kalınlığında blok)
            var floorColliderGO = new GameObject("Floor_Collider");
            floorColliderGO.transform.SetParent(envRoot.transform, false);
            floorColliderGO.transform.position = new Vector3(-0.500f, 0.282f - 1.0f, 0.000f);
            SetFullyStatic(floorColliderGO);
            var floorBox = floorColliderGO.AddComponent<BoxCollider>();
            floorBox.size = new Vector3(12.0f, 2.0f, 12.0f);
            floorBox.sharedMaterial = tablePhysMat;

            // 4 Oda Duvarı ve Tavan Fizik Collider'ları (Kemiklerin duvarlardan geçmesini %100 önler)
            CreateRoomWallCollider(envRoot.transform, "Wall_East_Screen_Collider", new Vector3( 2.95f, 1.65f,  0.00f), new Vector3(1.0f, 3.6f, 7.0f), tablePhysMat);
            CreateRoomWallCollider(envRoot.transform, "Wall_West_Back_Collider",   new Vector3(-3.95f, 1.65f,  0.00f), new Vector3(1.0f, 3.6f, 7.0f), tablePhysMat);
            CreateRoomWallCollider(envRoot.transform, "Wall_North_Left_Collider",  new Vector3(-0.50f, 1.65f,  3.45f), new Vector3(7.6f, 3.6f, 1.0f), tablePhysMat);
            CreateRoomWallCollider(envRoot.transform, "Wall_South_Right_Collider", new Vector3(-0.50f, 1.65f, -3.45f), new Vector3(7.6f, 3.6f, 1.0f), tablePhysMat);
            CreateRoomWallCollider(envRoot.transform, "Ceiling_Collider",          new Vector3(-0.50f, 3.55f,  0.00f), new Vector3(7.6f, 1.0f, 7.0f), tablePhysMat);

            // 2. [SpawnPoint] (Diseksiyon masası üst yüzeyinin 22cm üstü: y = 1.24m)
            var spawnPoint = new GameObject("[SpawnPoint]");
            spawnPoint.transform.position = new Vector3(0.0f, 1.24f, 0.0f);
            spawnPoint.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

            // 3. [SmartScreen] — lab_tasarimi.fbx içindeki 'ekran' (center=(2.45, 1.721, 0), size=(0.095, 1.889, 3.447)) üzerine tam hizalı
            var screenParent = new GameObject("[SmartScreen]");
            screenParent.transform.position = new Vector3(2.39f, 1.721f, 0.0f);
            screenParent.transform.rotation = Quaternion.Euler(0f, 90f, 0f); // Oyuncuya (-X yönüne) bakar

            var canvasObj = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup), typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
            canvasObj.transform.SetParent(screenParent.transform, false);
            var canvas = canvasObj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            // Kemik havada asılıyken lazerin kemiği delip arkadaki Akıllı Ekran menüsüne tıklamasını önle (3D Occlusion):
            var gRaycaster = canvasObj.GetComponent<GraphicRaycaster>();
            if (gRaycaster != null)
            {
                gRaycaster.blockingObjects = GraphicRaycaster.BlockingObjects.All;
            }
            var tdRaycaster = canvasObj.GetComponent<TrackedDeviceGraphicRaycaster>();
            if (tdRaycaster != null)
            {
                tdRaycaster.checkFor3DOcclusion = true;
                tdRaycaster.raycastTriggerInteraction = QueryTriggerInteraction.Collide;
            }

            var scaler = canvasObj.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.dynamicPixelsPerUnit = 10f;

            var canvasRt = canvasObj.GetComponent<RectTransform>();
            canvasRt.sizeDelta = new Vector2(1600f, 880f);
            canvasRt.localScale = new Vector3(0.002f, 0.002f, 0.002f); // 3.20m x 1.76m (ekran çerçevesinin tam içi)

            // Ekran Arka Planı (Fütüristik Holografik Cyber-Navy + Neon Cyan Çerçeve)
            var bgPanel = new GameObject("ScreenBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
            bgPanel.transform.SetParent(canvasObj.transform, false);
            var bgRt = bgPanel.GetComponent<RectTransform>();
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.sizeDelta = Vector2.zero;
            bgPanel.GetComponent<Image>().color = new Color(0.02f, 0.05f, 0.10f, 0.96f);
            var bgOutline = bgPanel.GetComponent<Outline>();
            bgOutline.effectColor = new Color(0.00f, 0.85f, 1.00f, 0.70f);
            bgOutline.effectDistance = new Vector2(3f, 3f);

            // Üst Bar Arka Planı + Neon Alt Çizgi
            var topBar = new GameObject("TopBar", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            topBar.transform.SetParent(canvasObj.transform, false);
            var topBarRt = topBar.GetComponent<RectTransform>();
            topBarRt.anchorMin = new Vector2(0f, 1f);
            topBarRt.anchorMax = new Vector2(1f, 1f);
            topBarRt.pivot = new Vector2(0.5f, 1f);
            topBarRt.anchoredPosition = Vector2.zero;
            topBarRt.sizeDelta = new Vector2(0f, 108f);
            topBar.GetComponent<Image>().color = new Color(0.04f, 0.11f, 0.20f, 0.98f);

            var topGlowLine = new GameObject("TopBarGlowLine", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            topGlowLine.transform.SetParent(topBar.transform, false);
            var topGlowRt = topGlowLine.GetComponent<RectTransform>();
            topGlowRt.anchorMin = new Vector2(0.02f, 0f);
            topGlowRt.anchorMax = new Vector2(0.98f, 0f);
            topGlowRt.pivot = new Vector2(0.5f, 0f);
            topGlowRt.anchoredPosition = Vector2.zero;
            topGlowRt.sizeDelta = new Vector2(0f, 3f);
            topGlowLine.GetComponent<Image>().color = new Color(0.00f, 0.92f, 1.00f, 0.90f);

            // Alt Holografik Çizgi
            var bottomGlowLine = new GameObject("BottomGlowLine", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bottomGlowLine.transform.SetParent(canvasObj.transform, false);
            var bottomGlowRt = bottomGlowLine.GetComponent<RectTransform>();
            bottomGlowRt.anchorMin = new Vector2(0.04f, 0f);
            bottomGlowRt.anchorMax = new Vector2(0.96f, 0f);
            bottomGlowRt.pivot = new Vector2(0.5f, 0f);
            bottomGlowRt.anchoredPosition = new Vector2(0f, 98f);
            bottomGlowRt.sizeDelta = new Vector2(0f, 2f);
            bottomGlowLine.GetComponent<Image>().color = new Color(0.00f, 0.78f, 0.98f, 0.40f);

            canvasObj.AddComponent<XRUISetup>();

            // Header Text (Fütüristik Latince Başlık)
            var headerObj = new GameObject("Header", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            headerObj.transform.SetParent(canvasObj.transform, false);
            var headerRt = headerObj.GetComponent<RectTransform>();
            headerRt.anchorMin = new Vector2(0f, 1f);
            headerRt.anchorMax = new Vector2(1f, 1f);
            headerRt.pivot = new Vector2(0.5f, 1f);
            headerRt.anchoredPosition = new Vector2(0f, -14f);
            headerRt.sizeDelta = new Vector2(-420f, 80f);
            var headerTmp = headerObj.GetComponent<TextMeshProUGUI>();
            headerTmp.text = "OPENVETATLAS VR  //  SPECIES";
            headerTmp.fontSize = 34f;
            headerTmp.fontStyle = FontStyles.Bold;
            headerTmp.characterSpacing = 4f;
            headerTmp.alignment = TextAlignmentOptions.Center;
            headerTmp.color = new Color(0.30f, 0.95f, 1.00f, 1.00f);

            // BackButton (Fütüristik Geri Butonu)
            var backBtnObj = new GameObject("BackButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Button));
            backBtnObj.transform.SetParent(canvasObj.transform, false);
            var backRt = backBtnObj.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(0f, 1f);
            backRt.anchorMax = new Vector2(0f, 1f);
            backRt.pivot = new Vector2(0f, 1f);
            backRt.anchoredPosition = new Vector2(28f, -19f);
            backRt.sizeDelta = new Vector2(175f, 70f);
            backBtnObj.GetComponent<Image>().color = new Color(0.06f, 0.22f, 0.36f, 0.96f);
            var backOutline = backBtnObj.GetComponent<Outline>();
            backOutline.effectColor = new Color(0.00f, 0.88f, 1.00f, 0.80f);
            backOutline.effectDistance = new Vector2(2f, 2f);
            backBtnObj.SetActive(false);

            var backTextObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            backTextObj.transform.SetParent(backBtnObj.transform, false);
            var backTextRt = backTextObj.GetComponent<RectTransform>();
            backTextRt.anchorMin = Vector2.zero;
            backTextRt.anchorMax = Vector2.one;
            backTextRt.sizeDelta = Vector2.zero;
            var btmp = backTextObj.GetComponent<TextMeshProUGUI>();
            btmp.text = "◄  GERİ";
            btmp.fontSize = 26f;
            btmp.fontStyle = FontStyles.Bold;
            btmp.characterSpacing = 2.5f;
            btmp.alignment = TextAlignmentOptions.Center;
            btmp.color = new Color(0.90f, 0.98f, 1.00f, 1.00f);

            // 4 Seviyeli Menü Panelleri
            var animalPanel = CreateUIPanel(canvasObj.transform, "AnimalPanel", true);
            CreateUIPanel(canvasObj.transform, "SystemPanel", false);
            CreateUIPanel(canvasObj.transform, "RegionPanel", false);
            CreateUIPanel(canvasObj.transform, "BonePanel", false);

            // Edit Mode'da da ekranın dolu görünmesi için başlangıç hayvan butonlarını yerleştir
            // (Play Mode'da UIMenuManager.Start -> ClearContainer ile otomatik yenilenir)
            PopulateEditModePreviewButtons(ContentOf(animalPanel), masterMenu);

            // Kemik getirildikten sonra beliren fütüristik 'MASAYI TEMİZLE' butonu (Alt info yazıları kaldırıldı)
            var clearBtnObj = new GameObject("ClearTableButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline), typeof(Button));
            clearBtnObj.transform.SetParent(canvasObj.transform, false);
            var clearRt = clearBtnObj.GetComponent<RectTransform>();
            clearRt.anchorMin = new Vector2(0.5f, 0f);
            clearRt.anchorMax = new Vector2(0.5f, 0f);
            clearRt.pivot = new Vector2(0.5f, 0f);
            clearRt.anchoredPosition = new Vector2(0f, 18f);
            clearRt.sizeDelta = new Vector2(460f, 68f);
            clearBtnObj.GetComponent<Image>().color = new Color(0.48f, 0.10f, 0.18f, 0.96f);
            var clearOutline = clearBtnObj.GetComponent<Outline>();
            clearOutline.effectColor = new Color(1.00f, 0.32f, 0.42f, 0.85f);
            clearOutline.effectDistance = new Vector2(2f, 2f);

            var clearTxtObj = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            clearTxtObj.transform.SetParent(clearBtnObj.transform, false);
            var clearTxtRt = clearTxtObj.GetComponent<RectTransform>();
            clearTxtRt.anchorMin = Vector2.zero;
            clearTxtRt.anchorMax = Vector2.one;
            clearTxtRt.sizeDelta = Vector2.zero;
            var clearTmp = clearTxtObj.GetComponent<TextMeshProUGUI>();
            clearTmp.text = "[ X ]   MASAYI TEMİZLE";
            clearTmp.fontSize = 25f;
            clearTmp.fontStyle = FontStyles.Bold;
            clearTmp.characterSpacing = 3f;
            clearTmp.alignment = TextAlignmentOptions.Center;
            clearTmp.color = new Color(1.00f, 0.92f, 0.94f, 1.00f);
            clearBtnObj.SetActive(false);
        }

        private static void CreateRoomWallCollider(Transform parent, string name, Vector3 pos, Vector3 size, PhysicMaterial mat)
        {
            var wallGO = new GameObject(name);
            wallGO.transform.SetParent(parent, false);
            wallGO.transform.position = pos;
            wallGO.isStatic = false;
            var box = wallGO.AddComponent<BoxCollider>();
            box.size = size;
            box.sharedMaterial = mat;
        }

        private static GameObject CreateUIPanel(Transform canvasTransform, string name, bool active)
        {
            var panel = new GameObject(name, typeof(RectTransform));
            panel.transform.SetParent(canvasTransform, false);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.05f, 0.13f);
            rt.anchorMax = new Vector2(0.95f, 0.85f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var content = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup));
            content.transform.SetParent(panel.transform, false);
            var crt = content.GetComponent<RectTransform>();
            crt.anchorMin = Vector2.zero;
            crt.anchorMax = Vector2.one;
            crt.sizeDelta = Vector2.zero;

            var grid = content.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(660f, 96f);
            grid.spacing = new Vector2(36f, 18f);
            grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.MiddleCenter;
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;

            panel.SetActive(active);
            return panel;
        }

        private static void PopulateEditModePreviewButtons(Transform container, MenuData menuData)
        {
            if (container == null || menuData == null || menuData.animals == null) return;
            var btnPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuButtonPrefabPath);
            if (btnPrefab == null) return;

            foreach (var animal in menuData.animals)
            {
                var btnGO = (GameObject)PrefabUtility.InstantiatePrefab(btnPrefab, container);
                btnGO.name = $"Btn_{animal.animalCode}";
                var btn = btnGO.GetComponent<Button>();
                var img = btnGO.GetComponent<Image>();
                var outline = btnGO.GetComponent<Outline>();
                var accent = btnGO.transform.Find("AccentBar")?.GetComponent<Image>();
                var tmp = btnGO.GetComponentInChildren<TextMeshProUGUI>(true);

                if (tmp != null)
                {
                    tmp.text = animal.isLocked ? $"[ - ]   {animal.displayName}" : $"►   {animal.displayName}";
                    tmp.fontSize = 27f;
                    tmp.characterSpacing = 2.5f;
                    tmp.color = animal.isLocked
                        ? new Color(0.46f, 0.56f, 0.66f, 0.85f)
                        : new Color(0.90f, 0.98f, 1.00f, 1.00f);
                }

                if (animal.isLocked)
                {
                    if (btn != null) btn.interactable = false;
                    if (img != null) img.color = new Color(0.05f, 0.09f, 0.15f, 0.82f);
                    if (outline != null) outline.effectColor = new Color(0.18f, 0.28f, 0.38f, 0.45f);
                    if (accent != null) accent.color = new Color(0.22f, 0.32f, 0.42f, 0.55f);
                }
                else
                {
                    if (btn != null) btn.interactable = true;
                    if (img != null) img.color = new Color(0.04f, 0.18f, 0.32f, 0.95f);
                    if (outline != null) outline.effectColor = new Color(0.00f, 0.88f, 1.00f, 0.80f);
                    if (accent != null) accent.color = new Color(0.00f, 0.95f, 1.00f, 1.00f);
                }
            }
        }

        private static void SetFullyStatic(GameObject go)
        {
            // Android / Meta Quest 3S build sırasında Static Batching nedeniyle
            // mesh'lerin (YAPILAR_Unity_Baked vb.) kırpılmasını veya gizlenmesini önlemek için
            // BatchingStatic bayrağını KAPALI tutuyoruz (GPU Instancing + Multi-View aktif).
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.isStatic = false;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            }
        }

        private static void EnsureTMPFontsAssigned()
        {
            TMP_FontAsset defaultFont = null;
            var settingsAsset = AssetDatabase.LoadAssetAtPath<TMP_Settings>("Assets/TextMesh Pro/Resources/TMP Settings.asset");
            if (settingsAsset != null)
            {
                defaultFont = TMP_Settings.defaultFontAsset;
            }
            if (defaultFont == null)
            {
                string[] fontGuids = AssetDatabase.FindAssets("LiberationSans SDF t:TMP_FontAsset");
                if (fontGuids.Length > 0)
                    defaultFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(fontGuids[0]));
            }

            if (defaultFont == null)
                return;

            GameObject btnPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuButtonPrefabPath);
            if (btnPrefab != null)
            {
                bool changed = false;
                foreach (TextMeshProUGUI tmp in btnPrefab.GetComponentsInChildren<TextMeshProUGUI>(true))
                {
                    if (tmp.font == null)
                    {
                        tmp.font = defaultFont;
                        changed = true;
                    }
                }
                if (changed)
                {
                    EditorUtility.SetDirty(btnPrefab);
                    PrefabUtility.SavePrefabAsset(btnPrefab);
                }
            }

            foreach (TextMeshProUGUI tmp in UnityEngine.Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include))
            {
                if (tmp.font == null)
                {
                    tmp.font = defaultFont;
                    EditorUtility.SetDirty(tmp);
                }
            }
        }

        // ─────────── 2. Assets/Data ScriptableObject Üretimi ────────────

        private static MenuData CreateMenuDataAssets()
        {
            EnsureFolder(DataFolder);

            MenuData dog   = CreateOrUpdateAnimalAsset($"{DataFolder}/Dog_MenuData.asset",   BuildAnimalDataFromKemikFolder("Canis",    "Dog"));
            MenuData horse = CreateOrUpdateAnimalAsset($"{DataFolder}/Horse_MenuData.asset", BuildAnimalDataFromKemikFolder("Equide",   "Horse"));
            MenuData cow   = CreateOrUpdateAnimalAsset($"{DataFolder}/Cow_MenuData.asset",   BuildAnimalDataFromKemikFolder("Ruminant", "Cow"));
            MenuData pig   = CreateOrUpdateAnimalAsset($"{DataFolder}/Pig_MenuData.asset",   BuildAnimalDataFromKemikFolder("Sus",      "Pig"));

            MenuData combined = AssetDatabase.LoadAssetAtPath<MenuData>(CombinedAssetPath);
            if (combined == null)
            {
                combined = ScriptableObject.CreateInstance<MenuData>();
                AssetDatabase.CreateAsset(combined, CombinedAssetPath);
            }

            combined.animals = new List<AnimalData>
            {
                dog.animals[0],
                horse.animals[0],
                cow.animals[0],
                pig.animals[0]
            };
            EditorUtility.SetDirty(combined);

            Debug.Log($"{LogTag} Birleşik menü verisi güncellendi (Kemik klasör hiyerarşisi ile senkronize): {CombinedAssetPath} (4 hayvan)");
            return combined;
        }

        private static MenuData CreateOrUpdateAnimalAsset(string assetPath, AnimalData animalData)
        {
            MenuData asset = AssetDatabase.LoadAssetAtPath<MenuData>(assetPath);
            bool isNew = asset == null;

            if (isNew)
            {
                asset = ScriptableObject.CreateInstance<MenuData>();
                asset.animals = new List<AnimalData> { animalData };
                AssetDatabase.CreateAsset(asset, assetPath);
                Debug.Log($"{LogTag} Oluşturuldu: {assetPath}");
            }
            else
            {
                asset.animals = new List<AnimalData> { animalData };
                EditorUtility.SetDirty(asset);
                Debug.Log($"{LogTag} Latince menü verisi güncellendi: {assetPath}");
            }

            return asset;
        }

        private static bool DoesBoneHaveFbxModel(string speciesFolder, string animalCode, string systemName, string regionName, string boneName)
        {
            // 1. Önce Masaüstündeki 'Anatomi VR\Kemik\<Tür>\<Sistem>\<Bölge>\<Kemik>' klasöründe .fbx var mı bak
            string kemikBoneDir = Path.Combine(@"C:\Users\Osteometri\Desktop\Anatomi VR\Kemik", speciesFolder, systemName, regionName, boneName);
            if (Directory.Exists(kemikBoneDir))
            {
                string[] fbxFiles = Directory.GetFiles(kemikBoneDir, "*.fbx", SearchOption.AllDirectories);
                return fbxFiles.Length > 0;
            }

            // 2. Klasör yoksa Unity projesi içindeki 'Assets/Models/Bones/' karşılığını kontrol et
            string fbxAssetPath = GetBoneFbxAssetPath(animalCode, regionName, boneName);
            return File.Exists(Path.GetFullPath(fbxAssetPath));
        }

        private static AnimalData BuildAnimalDataFromKemikFolder(string displayName, string code)
        {
            var regionDefinitions = new (string regionName, string[] boneNames)[]
            {
                ("Ossa Membri Thoracici", new[] { "Scapula", "Humerus", "Radius", "Ulna", "Carpal", "Metacarpal", "Phalanx" }),
                ("Ossa Membri Pelvini",   new[] { "Coxae", "Femur", "Tibia", "Fibula", "Tarsal", "Metatarsal", "Phalanx" }),
                ("Columna Vertebralis",   new[] { "Cervical", "Throcal", "Lumbal", "Sacrum", "Caudal" }),
                ("Skeleton Thoracis",     new[] { "Costae", "Sternum" }),
                ("Skeleton Axiale",       new[] { "Cranium", "Mandibula" })
            };

            var skeletaleRegions = new List<RegionData>();
            foreach (var regDef in regionDefinitions)
            {
                var boneList = new List<BoneData>();
                foreach (string boneName in regDef.boneNames)
                {
                    bool hasFbx = DoesBoneHaveFbxModel(displayName, code, "Systema Skeletale", regDef.regionName, boneName);
                    string addrKey = GetBoneAddressableKey(code, regDef.regionName, boneName);
                    boneList.Add(new BoneData
                    {
                        displayName    = boneName,
                        addressableKey = addrKey,
                        isLocked       = !hasFbx
                    });
                }

                bool anyUnlockedBone = boneList.Any(b => !b.isLocked);
                skeletaleRegions.Add(new RegionData
                {
                    displayName = regDef.regionName,
                    isLocked    = !anyUnlockedBone,
                    bones       = boneList
                });
            }

            bool skeletaleUnlocked = skeletaleRegions.Any(r => !r.isLocked);
            var systems = new List<SystemData>
            {
                new SystemData
                {
                    displayName = "Systema Skeletale",
                    isLocked    = !skeletaleUnlocked,
                    regions     = skeletaleRegions
                },
                new SystemData { displayName = "Systema Musculare",     isLocked = true },
                new SystemData { displayName = "Systema Respiratorium", isLocked = true },
                new SystemData { displayName = "Systema Digestorium",   isLocked = true },
                new SystemData { displayName = "Systema Urogenitale",   isLocked = true },
                new SystemData { displayName = "Systema Vasorum",       isLocked = true },
                new SystemData { displayName = "Systema Nervosum",      isLocked = true }
            };

            bool animalUnlocked = systems.Any(s => !s.isLocked);
            return new AnimalData
            {
                displayName = displayName,
                animalCode  = code,
                isLocked    = !animalUnlocked,
                systems     = systems
            };
        }

        // ─────────── 3. Sahne XR Rig & EventSystem Doğrulaması ──────────

        private static void EnsureRealXROriginInScene()
        {
            if (UnityEngine.Object.FindAnyObjectByType<XRInteractionManager>() == null)
            {
                var xrimGO = new GameObject("XR Interaction Manager");
                Undo.RegisterCreatedObjectUndo(xrimGO, "Create XR Interaction Manager");
                xrimGO.AddComponent<XRInteractionManager>();
            }

            // EventSystem: Hem VR (XRUIInputModule) hem PC Mouse uyumlu
            EventSystem es = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (es == null)
            {
                var esGO = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
                es = esGO.GetComponent<EventSystem>();
            }
            else if (es.GetComponent<XRUIInputModule>() == null && es.GetComponent<InputSystemUIInputModule>() == null)
            {
                es.gameObject.AddComponent<XRUIInputModule>();
            }

            // Eski yer tutucu kamera veya rig'leri temizle
            GameObject oldPlaceholder = GameObject.Find("[XR Origin (VR)]");
            if (oldPlaceholder != null && oldPlaceholder.GetComponentInChildren<XROrigin>(true) == null)
            {
                UnityEngine.Object.DestroyImmediate(oldPlaceholder);
            }

            XROrigin existingOrigin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            GameObject xrOriginInstance = existingOrigin != null ? existingOrigin.gameObject : null;

            if (xrOriginInstance == null)
            {
                string[] guids = AssetDatabase.FindAssets("XR Origin (XR Rig) t:Prefab");
                if (guids.Length > 0)
                {
                    string prefabPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                    if (prefab != null)
                    {
                        xrOriginInstance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                        xrOriginInstance.name = "XR Origin (XR Rig)";
                    }
                }
            }

            if (xrOriginInstance != null)
            {
                // Oyuncu: Diseksiyon masasının (0, y, 0) arkasında (x = -1.35), zemin (y = 0.282) üzerinde,
                // karşıdaki Akıllı Ekrana (+X yönüne, rot Y = 90) bakacak şekilde konumlanır.
                xrOriginInstance.transform.position = new Vector3(-1.35f, 0.282f, 0.0f);
                xrOriginInstance.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

                var xrOriginComp = xrOriginInstance.GetComponent<XROrigin>();
                if (xrOriginComp != null)
                {
                    xrOriginComp.CameraYOffset = 1.38f;
                    if (xrOriginComp.CameraFloorOffsetObject != null)
                    {
                        xrOriginComp.CameraFloorOffsetObject.transform.localPosition = new Vector3(0f, 1.38f, 0f);
                    }
                    if (xrOriginComp.Camera != null)
                    {
                        xrOriginComp.Camera.nearClipPlane = 0.05f;
                        xrOriginComp.Camera.fieldOfView = 65f;
                        xrOriginComp.Camera.clearFlags = CameraClearFlags.SolidColor;
                        xrOriginComp.Camera.backgroundColor = new Color(0.18f, 0.22f, 0.27f, 1f);
                        xrOriginComp.Camera.transform.localPosition = Vector3.zero;
                        xrOriginComp.Camera.transform.localRotation = Quaternion.Euler(6f, 0f, 0f);

                        // World-Space Canvas kamerasını bağla
                        foreach (Canvas c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include))
                        {
                            if (c.renderMode == RenderMode.WorldSpace)
                                c.worldCamera = xrOriginComp.Camera;
                        }
                    }
                }

                // XR Rig altındaki tüm Renderer ve LineRenderer bileşenlerinde Built-in / eksik materyal kalmasını önle
                Material fallbackCtrlMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/ScreenFrame_Unlit.mat");
                Material laserMat = CreateOrUpdateBakedMat("Assets/Materials/Environment/LaserLine_Unlit.mat",
                    GetBakedUnlitShader(), null, new Color(0.25f, 0.78f, 1.0f, 1f), 0.0f, 0.0f, Vector2.one);

                foreach (Renderer r in xrOriginInstance.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    if (r is LineRenderer)
                    {
                        r.sharedMaterial = laserMat;
                        continue;
                    }
                    Material[] mats = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] == null || mats[i].shader == null ||
                            mats[i].shader.name == "Standard" ||
                            mats[i].shader.name.Contains("Error") ||
                            mats[i].shader.name.Contains("Legacy"))
                        {
                            mats[i] = fallbackCtrlMat;
                            changed = true;
                        }
                    }
                    if (changed) r.sharedMaterials = mats;
                }

                AttachControllerVisibilityAndPose(xrOriginInstance.transform, "Left Controller",  new Vector3(-0.28f, -0.42f, 0.42f));
                AttachControllerVisibilityAndPose(xrOriginInstance.transform, "Right Controller", new Vector3( 0.28f, -0.42f, 0.42f));

                // Meta Quest 3S'te XR Origin'in yerçekimiyle zeminden aşağı boşluğa düşmesini önlemek için
                // GravityProvider ve CharacterController bileşenlerini sahnede devre dışı bırak.
                foreach (CharacterController cc in xrOriginInstance.GetComponentsInChildren<CharacterController>(true))
                {
                    cc.enabled = false;
                    EditorUtility.SetDirty(cc);
                }
                foreach (MonoBehaviour mb in xrOriginInstance.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null) continue;
                    string typeName = mb.GetType().Name;
                    if (typeName.Contains("Gravity"))
                    {
                        mb.enabled = false;
                        EditorUtility.SetDirty(mb);
                        if (mb.gameObject != xrOriginInstance && mb.gameObject.name.Contains("Gravity"))
                        {
                            mb.gameObject.SetActive(false);
                            EditorUtility.SetDirty(mb.gameObject);
                        }
                    }
                }
            }
        }

        private static void AttachControllerVisibilityAndPose(Transform root, string controllerName, Vector3 restingLocalPos)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == controllerName)
                {
                    t.localPosition = restingLocalPos;
                    if (t.GetComponent<ControllerVisibilityManager>() == null)
                    {
                        t.gameObject.AddComponent<ControllerVisibilityManager>();
                    }
                    break;
                }
            }
        }

        // ─── 4. "GameManagers" Kurulumu ve Bileşenlerin Atanması ────────

        private static GameObject SetupGameManagers(MenuData masterMenu)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
                activeScene = EditorSceneManager.GetActiveScene();

            GameObject managers = activeScene.GetRootGameObjects()
                .FirstOrDefault(go => go.name == GameManagersName);

            if (managers == null)
            {
                managers = GameObject.Find(GameManagersName);
            }

            if (managers == null)
            {
                managers = new GameObject(GameManagersName);
                Undo.RegisterCreatedObjectUndo(managers, "Create GameManagers");
                Debug.Log($"{LogTag} '{GameManagersName}' oluşturuldu.");
            }
            else
            {
                Debug.Log($"{LogTag} '{GameManagersName}' zaten mevcut, yenisi oluşturulmadı.");
            }

            AnatomyManager        anatomyManager = EnsureComponentOnHost<AnatomyManager>(activeScene, managers);
            UIMenuManager         uiMenuManager  = EnsureComponentOnHost<UIMenuManager>(activeScene, managers);
            BoneResetManager      resetManager   = EnsureComponentOnHost<BoneResetManager>(activeScene, managers);
            EnsureComponentOnHost<DesktopTestController>(activeScene, managers);

            AutoWireSceneReferences(activeScene, anatomyManager, uiMenuManager, resetManager, masterMenu);

            GameObject oldManagersGroup = GameObject.Find("[Managers]");
            if (oldManagersGroup != null && oldManagersGroup != managers && oldManagersGroup.transform.childCount == 0)
            {
                Undo.DestroyObjectImmediate(oldManagersGroup);
            }

            return managers;
        }

        private static T EnsureComponentOnHost<T>(Scene scene, GameObject host) where T : Component
        {
            T onHost = host.GetComponent<T>();
            if (onHost == null)
            {
                onHost = Undo.AddComponent<T>(host);
                Debug.Log($"{LogTag} {typeof(T).Name} bileşeni '{host.name}' üzerine eklendi.");
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (T comp in root.GetComponentsInChildren<T>(true))
                {
                    if (comp == null || comp == onHost)
                        continue;

                    EditorUtility.CopySerialized(comp, onHost);
                    GameObject oldObj = comp.gameObject;
                    Undo.DestroyObjectImmediate(comp);

                    if (oldObj != host &&
                        oldObj.transform.childCount == 0 &&
                        oldObj.GetComponents<Component>().Length <= 1)
                    {
                        Undo.DestroyObjectImmediate(oldObj);
                    }

                    Debug.Log($"{LogTag} Eski {typeof(T).Name} bileşeni '{host.name}' üzerine taşındı.");
                }
            }

            return onHost;
        }

        private static void AutoWireSceneReferences(
            Scene scene,
            AnatomyManager anatomy,
            UIMenuManager ui,
            BoneResetManager resetMgr,
            MenuData masterMenu)
        {
            Transform[] allTransforms = SceneRoots(scene)
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))
                .ToArray();

            GameObject spawnPoint = allTransforms
                .FirstOrDefault(t => t.name == "[SpawnPoint]" || t.name == "SpawnPoint")?.gameObject;

            if (anatomy != null)
            {
                Undo.RecordObject(anatomy, "Wire AnatomyManager");
                if (spawnPoint != null)
                    SetField(anatomy, "spawnPoint", spawnPoint.transform);
                SetField(anatomy, "maxBonesOnTable", 4);
                EditorUtility.SetDirty(anatomy);
            }

            if (resetMgr != null && spawnPoint != null)
            {
                Undo.RecordObject(resetMgr, "Wire BoneResetManager");
                resetMgr.ResetPosition = spawnPoint.transform;
                resetMgr.ResetYThreshold = -0.2f;
                SetField(resetMgr, "_resetPosition", spawnPoint.transform);
                SetField(resetMgr, "_resetYThreshold", -0.2f);
                EditorUtility.SetDirty(resetMgr);
            }

            if (ui == null)
                return;

            Undo.RecordObject(ui, "Wire UIMenuManager");
            SetField(ui, "menuData", masterMenu);
            SetField(ui, "allowDuplicateBones", false);

            GameObject btnPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuButtonPrefabPath);
            if (btnPrefab != null)
                SetField(ui, "menuButtonPrefab", btnPrefab);

            GameObject animalPanel = FindByName(allTransforms, "AnimalPanel");
            GameObject systemPanel = FindByName(allTransforms, "SystemPanel");
            GameObject regionPanel = FindByName(allTransforms, "RegionPanel");
            GameObject bonePanel   = FindByName(allTransforms, "BonePanel");

            SetField(ui, "animalPanel", animalPanel);
            SetField(ui, "systemPanel", systemPanel);
            SetField(ui, "regionPanel", regionPanel);
            SetField(ui, "bonePanel",   bonePanel);

            SetField(ui, "animalButtonContainer", ContentOf(animalPanel));
            SetField(ui, "systemButtonContainer", ContentOf(systemPanel));
            SetField(ui, "regionButtonContainer", ContentOf(regionPanel));
            SetField(ui, "boneButtonContainer",   ContentOf(bonePanel));

            GameObject backBtn = FindByName(allTransforms, "BackButton");
            if (backBtn != null)
                SetField(ui, "backButton", backBtn.GetComponent<Button>());

            GameObject header = FindByName(allTransforms, "Header");
            if (header != null)
                SetField(ui, "headerText", header.GetComponent<TextMeshProUGUI>());

            EditorUtility.SetDirty(ui);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            if (target == null || value == null)
                return;

            var field = target.GetType().GetField(fieldName, BindingFlags_Instance);
            if (field != null && field.FieldType.IsAssignableFrom(value.GetType()))
            {
                field.SetValue(target, value);
            }
        }

        // ─────────── 5. Addressables Default Local Group Kaydı ──────────

        private static void RegisterAddressables(MenuData masterMenu)
        {
            const string manualHint =
                "Addressables atamalarını manuel yapmayı unutma (Window -> Asset Management -> Addressables -> Groups).";

            try
            {
                AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
                if (settings == null)
                {
                    Debug.LogWarning($"{LogTag} {manualHint}");
                    return;
                }

                AddressableAssetGroup defaultGroup = settings.FindGroup(DefaultLocalGroupName) ?? settings.DefaultGroup;
                if (defaultGroup == null)
                {
                    defaultGroup = settings.CreateGroup(
                        DefaultLocalGroupName,
                        setAsDefaultGroup: true,
                        readOnly: false,
                        postEvent: false,
                        schemasToCopy: null,
                        typeof(BundledAssetGroupSchema),
                        typeof(ContentUpdateGroupSchema));
                }

                List<string> keys = masterMenu.animals
                    .SelectMany(a => a.systems)
                    .SelectMany(s => s.regions)
                    .SelectMany(r => r.bones)
                    .Where(b => !b.isLocked && !string.IsNullOrEmpty(b.addressableKey))
                    .Select(b => b.addressableKey)
                    .Distinct()
                    .ToList();

                int registered = 0;
                List<string> missingPrefabs = new List<string>();

                foreach (string key in keys)
                {
                    string prefabPath = FindPrefabPath(key);
                    if (string.IsNullOrEmpty(prefabPath))
                    {
                        missingPrefabs.Add(key);
                        continue;
                    }

                    string guid = AssetDatabase.AssetPathToGUID(prefabPath);
                    AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, defaultGroup, false, false);
                    entry.SetAddress(key, false);

                    registered++;
                    Debug.Log($"{LogTag} Addressable Entry kaydedildi: '{key}' → {prefabPath} (Grup: {defaultGroup.Name})");
                }

                settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
                EditorUtility.SetDirty(defaultGroup);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();

                Debug.Log($"{LogTag} Addressables: Default Local Group içine {registered}/{keys.Count} kemik kaydedildi.");

                if (missingPrefabs.Count > 0)
                {
                    Debug.LogWarning(
                        $"{LogTag} {manualHint} (Eksik prefab'lar: {string.Join(", ", missingPrefabs)}).");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"{LogTag} {manualHint} ({ex.Message})");
            }
        }

        // ───────────────────────── Yardımcılar ──────────────────────────

        private static IEnumerable<Transform> SceneRoots(Scene scene) =>
            scene.GetRootGameObjects().Select(go => go.transform);

        private static GameObject FindByName(IEnumerable<Transform> allTransforms, string name)
        {
            foreach (Transform t in allTransforms)
            {
                if (t != null && t.name == name)
                    return t.gameObject;
            }
            return null;
        }

        private static Transform ContentOf(GameObject panel)
        {
            if (panel == null) return null;
            Transform content = panel.transform.Find("Content");
            return content != null ? content : panel.transform;
        }

        private static string FindPrefabPath(string fileName)
        {
            return AssetDatabase.FindAssets($"{fileName} t:Prefab")
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => string.Equals(
                    Path.GetFileNameWithoutExtension(p), fileName, StringComparison.OrdinalIgnoreCase));
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        // ─────────── 6. Meta Quest 3S VR APK Otomatik Derleme (Build) ──────────

        private const string QuestApkMenuPath = "Tools/Anatomi VR/Quest VR APK Oluştur";

        [MenuItem(QuestApkMenuPath, priority = 10)]
        public static void BuildQuestApk()
        {
            AutoBuilder.BuildApkAutoVersion();
        }

        internal static void PrepareSceneAndAddressablesForBuild()
        {
            const string mainScenePath = "Assets/Scenes/MainLab.unity";
            if (File.Exists(mainScenePath))
            {
                EditorSceneManager.OpenScene(mainScenePath, OpenSceneMode.Single);
            }

            RunFullSetup();
            ConfigureQuestXRAndAndroidSettings();

            MenuData masterMenu = AssetDatabase.LoadAssetAtPath<MenuData>(CombinedAssetPath) ?? CreateMenuDataAssets();
            RegisterAddressables(masterMenu);
        }

        internal static void ConfigureQuestXRAndAndroidSettings()
        {
            // 1. URP ve ASTC doku ayarlarını garantiye al
            EnsureURPPipelineAsset();
            EnsureBakedTexturesAndMaterials();

            // 2. Android PlayerSettings (Meta Quest 3S / Quest 3 / Quest 2)
            PlayerSettings.companyName = "ADU Veteriner Fakultesi";
            PlayerSettings.productName = "OpenVetAtlas VR";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.ADU.VeterinerAnatomiVR");

            // OpenVetAtlas VR (OVA VR) 1024x1024 Uygulama İkonu ve Splash Logosu Ataması
            const string squareIconPath = "Assets/Textures/UI/OVA_AppIcon_Square.png";
            Texture2D appIconTex = AssetDatabase.LoadAssetAtPath<Texture2D>(squareIconPath);
            Sprite appIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>(squareIconPath);
            if (appIconTex != null)
            {
                int[] defaultSizes = PlayerSettings.GetIconSizes(NamedBuildTarget.Unknown, IconKind.Any);
                if (defaultSizes != null && defaultSizes.Length > 0)
                {
                    Texture2D[] defaultIcons = new Texture2D[defaultSizes.Length];
                    for (int i = 0; i < defaultIcons.Length; i++) defaultIcons[i] = appIconTex;
                    PlayerSettings.SetIcons(NamedBuildTarget.Unknown, defaultIcons, IconKind.Any);
                }

                PlatformIconKind[] androidIconKinds = PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android);
                if (androidIconKinds != null)
                {
                    foreach (PlatformIconKind kind in androidIconKinds)
                    {
                        PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                        if (icons == null) continue;
                        foreach (PlatformIcon pi in icons)
                        {
                            int layerCount = Mathf.Max(1, pi.maxLayerCount);
                            Texture2D[] layers = new Texture2D[layerCount];
                            for (int l = 0; l < layerCount; l++) layers[l] = appIconTex;
                            pi.SetTextures(layers);
                        }
                        PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
                    }
                }

                PlayerSettings.virtualRealitySplashScreen = appIconTex;
            }
            if (appIconSprite != null)
            {
                PlayerSettings.SplashScreen.show = true;
                PlayerSettings.SplashScreen.backgroundColor = new Color(0.145f, 0.161f, 0.180f, 1f);
                PlayerSettings.SplashScreen.logos = new[]
                {
                    PlayerSettings.SplashScreenLogo.Create(2.5f, appIconSprite)
                };
            }
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
#if UNITY_6000_0_OR_NEWER
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
#endif
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.development = false;

            // 3. XR Plug-in Management (XRGeneralSettingsPerBuildTarget -> OpenXRLoader)
            var getOrCreateMethod = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
                "GetOrCreate",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            XRGeneralSettingsPerBuildTarget perTarget = getOrCreateMethod != null
                ? getOrCreateMethod.Invoke(null, null) as XRGeneralSettingsPerBuildTarget
                : null;

            if (perTarget != null)
            {
                XRLoader openXrLoader = AssetDatabase.LoadAssetAtPath<XRLoader>("Assets/XR/Loaders/OpenXRLoader.asset");
                foreach (BuildTargetGroup btg in new[] { BuildTargetGroup.Android, BuildTargetGroup.Standalone })
                {
                    if (!perTarget.HasSettingsForBuildTarget(btg))
                        perTarget.CreateDefaultSettingsForBuildTarget(btg);
                    if (!perTarget.HasManagerSettingsForBuildTarget(btg))
                        perTarget.CreateDefaultManagerSettingsForBuildTarget(btg);

                    XRGeneralSettings genSettings = perTarget.SettingsForBuildTarget(btg);
                    if (genSettings != null)
                    {
                        genSettings.InitManagerOnStart = true;
                        EditorUtility.SetDirty(genSettings);
                    }

                    XRManagerSettings mgrSettings = perTarget.ManagerSettingsForBuildTarget(btg);
                    if (mgrSettings != null)
                    {
                        XRPackageMetadataStore.AssignLoader(mgrSettings, typeof(OpenXRLoader).FullName, btg);
                        if (openXrLoader != null && !mgrSettings.activeLoaders.Contains(openXrLoader))
                        {
                            mgrSettings.TryAddLoader(openXrLoader);
                        }
                        EditorUtility.SetDirty(mgrSettings);
                    }
                }
                EditorUtility.SetDirty(perTarget);
            }

            // 4. OpenXR Package Settings (Single Pass Instanced + Meta Quest Support + Touch Controller Profilleri)
            UnityEngine.Object[] allOpenXrAssets = AssetDatabase.LoadAllAssetsAtPath("Assets/XR/Settings/OpenXR Package Settings.asset");
            if (allOpenXrAssets != null)
            {
                foreach (UnityEngine.Object obj in allOpenXrAssets)
                {
                    if (obj is OpenXRSettings oxrSettings)
                    {
                        oxrSettings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
                        oxrSettings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.None;
                        EditorUtility.SetDirty(oxrSettings);
                    }
                    else if (obj is OpenXRFeature feature)
                    {
                        if (feature is MetaQuestFeature ||
                            feature is OculusTouchControllerProfile ||
                            feature is MetaQuestTouchPlusControllerProfile ||
                            feature is MetaQuestTouchProControllerProfile ||
                            feature is HandInteractionProfile)
                        {
                            feature.enabled = true;
                            EditorUtility.SetDirty(feature);
                        }
                    }
                }
            }

            // 5. Sahneyi Build Settings'e ekle
            const string mainScenePath = "Assets/Scenes/MainLab.unity";
            if (File.Exists(mainScenePath))
            {
                EditorBuildSettings.scenes = new[]
                {
                    new EditorBuildSettingsScene(mainScenePath, true)
                };
            }

            AssetDatabase.SaveAssets();
        }
    }
}
