using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AnatomiVR.Editor
{
    /// <summary>
    /// Blender'dan bir kemik FBX dosyası kaydedildiği anda (ister Masaüstündeki 'Anatomi VR' klasörüne,
    /// ister doğrudan 'Assets/Models/Bones' klasörüne kaydedilsin) bunu otomatik olarak algılar,
    /// kemik Prefab'ını ve üzerindeki 3B Anatomik Pin noktalarını hiçbir tuşa basmaya gerek kalmadan
    /// kendi kendine günceller.
    /// </summary>
    [InitializeOnLoad]
    public class BlenderAutoSyncPostprocessor : AssetPostprocessor
    {
        private static double _lastDesktopCheckTime;
        private static bool _isRebuildingPrefabs;

        static BlenderAutoSyncPostprocessor()
        {
            EditorApplication.update -= PollDesktopFbxChanges;
            EditorApplication.update += PollDesktopFbxChanges;
        }

        /// <summary>
        /// Unity açıkken her 2 saniyede bir Masaüstündeki 'Anatomi VR' klasöründeki FBX dosyalarını kontrol eder.
        /// Blender'dan masaüstüne Export yapıldığı anda otomatik olarak Unity'ye çeker ve Prefab'ı günceller.
        /// </summary>
        private static void PollDesktopFbxChanges()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || _isRebuildingPrefabs)
                return;

            if (EditorApplication.timeSinceStartup - _lastDesktopCheckTime < 2.0)
                return;

            _lastDesktopCheckTime = EditorApplication.timeSinceStartup;

            if (CheckAndCopyDesktopFbxIfNewer())
            {
                RebuildBonePrefabsAutomatically();
            }
        }

        /// <summary>
        /// Doğrudan 'Assets/Models/Bones/' içine FBX kaydedildiğinde otomatik tetiklenir.
        /// </summary>
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (_isRebuildingPrefabs)
                return;

            bool boneModelImported = false;
            foreach (string path in importedAssets)
            {
                if (path.StartsWith("Assets/Models/Bones/", StringComparison.OrdinalIgnoreCase) &&
                    path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                {
                    boneModelImported = true;
                    break;
                }
            }

            if (boneModelImported)
            {
                EditorApplication.delayCall += RebuildBonePrefabsAutomatically;
            }
        }

        private static bool CheckAndCopyDesktopFbxIfNewer()
        {
            string desktopRoot = @"C:\Users\Osteometri\Desktop\Anatomi VR";
            if (!Directory.Exists(desktopRoot))
                return false;

            var pairs = new (string[] sources, string destAssetPath)[]
            {
                (new[] {
                    Path.Combine(desktopRoot, @"Kemik\Canis\Systema Skeletale\Ossa Membri Thoracici\Humerus\Dog_humerus.fbx"),
                    Path.Combine(desktopRoot, @"Humerus\source\Dog_humerus.fbx"),
                    Path.Combine(desktopRoot, @"Humerus\Dog_humerus.fbx"),
                    Path.Combine(desktopRoot, @"Dog_humerus.fbx")
                }, "Assets/Models/Bones/Dog_humerus.fbx"),

                (new[] {
                    Path.Combine(desktopRoot, @"Kemik\Canis\Systema Skeletale\Ossa Membri Thoracici\Radius\Dog_radius.fbx"),
                    Path.Combine(desktopRoot, @"Kemik\Canis\Systema Skeletale\Ossa Membri Thoracici\Radius\Dog_Radius.fbx"),
                    Path.Combine(desktopRoot, @"Radius\source\Dog_Radius.fbx"),
                    Path.Combine(desktopRoot, @"Radius\Dog_Radius.fbx"),
                    Path.Combine(desktopRoot, @"Dog_Radius.fbx")
                }, "Assets/Models/Bones/Dog_Radius.fbx"),

                (new[] {
                    Path.Combine(desktopRoot, @"Kemik\Canis\Systema Skeletale\Ossa Membri Thoracici\Scapula\Dog_scapula.fbx"),
                    Path.Combine(desktopRoot, @"Scapula\Dog_scapula.fbx"),
                    Path.Combine(desktopRoot, @"Scapula\source\Dog_scapula.fbx"),
                    Path.Combine(desktopRoot, @"Dog_scapula.fbx")
                }, "Assets/Models/Bones/Dog_scapula.fbx"),

                (new[] {
                    Path.Combine(desktopRoot, @"Kemik\Canis\Systema Skeletale\Ossa Membri Thoracici\Ulna\Dog_ulna.fbx"),
                    Path.Combine(desktopRoot, @"Ulna\Dog_ulna.fbx"),
                    Path.Combine(desktopRoot, @"Ulna\source\Dog_ulna.fbx"),
                    Path.Combine(desktopRoot, @"Dog_ulna.fbx")
                }, "Assets/Models/Bones/Dog_ulna.fbx")
            };

            bool anyCopied = false;
            foreach (var pair in pairs)
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
                            Debug.Log($"[BlenderAutoSync] Yeni Blender FBX algılandı ve otomatik aktarıldı: {src} -> {pair.destAssetPath}");
                        }
                    }
                }
            }

            if (anyCopied)
            {
                AssetDatabase.Refresh();
            }

            return anyCopied;
        }

        private static void RebuildBonePrefabsAutomatically()
        {
            if (_isRebuildingPrefabs || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            try
            {
                _isRebuildingPrefabs = true;
                Debug.Log("[BlenderAutoSync] Kemik FBX değişikliği algılandı — Kemik Prefab'ları ve Blender Pin noktaları otomatik güncelleniyor...");
                AnatomySetupTool.RunFullSetup();
            }
            finally
            {
                _isRebuildingPrefabs = false;
            }
        }
    }
}
