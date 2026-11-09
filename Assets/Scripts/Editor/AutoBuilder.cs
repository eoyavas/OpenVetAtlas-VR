using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace AnatomiVR.Editor
{
    /// <summary>
    /// OpenVetAtlas VR Otomatik Versiyonlamalı APK Derleme Sistemi (Auto-Increment APK Builder).
    /// Menü: Tools -> OpenVetAtlas -> Build APK (Auto-Version)
    /// </summary>
    public static class AutoBuilder
    {
        private const string MenuPath          = "Tools/OpenVetAtlas/Build APK (Auto-Version)";
        private const string OutputDirectory   = @"C:\Users\Osteometri\Desktop\Anatomi VR";
        private const string ApkFilePrefix     = "OpenVetAtlas_VR_V";
        private const string BuildReqFilePath  = "Temp/TriggerQuestApkBuild.req";
        private const string BuildStatusPath   = "Temp/QuestApkBuildStatus.txt";
        private const string LogTag            = "[AutoBuilder]";

        private static readonly Regex VersionApkRegex = new Regex(
            @"^OpenVetAtlas_VR_V(\d+)\.apk$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        [InitializeOnLoadMethod]
        private static void CheckPendingAutoBuild()
        {
            if (!File.Exists(BuildReqFilePath))
                return;

            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    EditorApplication.isPlaying = false;
                    return;
                }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                    return;

                if (!File.Exists(BuildReqFilePath))
                    return;

                string currentStage = File.ReadAllText(BuildReqFilePath).Trim();
                File.Delete(BuildReqFilePath);

                if (currentStage == "STAGE_2")
                {
                    ExecuteAutoBuildStage2();
                }
                else
                {
                    BuildApkAutoVersion();
                }
            };
        }

        [MenuItem(MenuPath, priority = 10)]
        public static void BuildApkAutoVersion()
        {
            try
            {
                File.WriteAllText(BuildStatusPath, $"STARTED_STAGE_1|{DateTime.Now:HH:mm:ss}");
                Debug.Log($"{LogTag} [OpenVetAtlas Auto-Build] 1/4: Android + OpenXR + ASTC ayarları yapılandırılıyor...");

                AnatomySetupTool.ConfigureQuestXRAndAndroidSettings();

                if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                {
                    Debug.Log($"{LogTag} Aktif hedef platform Android'e geçiriliyor...");
                    File.WriteAllText(BuildReqFilePath, "STAGE_2");
                    File.WriteAllText(BuildStatusPath, $"SWITCHING_TO_ANDROID|{DateTime.Now:HH:mm:ss}");

                    bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                    if (!switched)
                    {
                        if (File.Exists(BuildReqFilePath)) File.Delete(BuildReqFilePath);
                        const string err = "Android Build Target geçişi başarısız oldu! Unity Hub üzerinden Android Build Support kurulu olduğundan emin olun.";
                        File.WriteAllText(BuildStatusPath, $"FAILED|{err}");
                        Debug.LogError($"{LogTag} {err}");
                        return;
                    }

                    if (!EditorApplication.isCompiling && File.Exists(BuildReqFilePath))
                    {
                        File.Delete(BuildReqFilePath);
                        ExecuteAutoBuildStage2();
                    }
                    return;
                }

                ExecuteAutoBuildStage2();
            }
            catch (Exception ex)
            {
                File.WriteAllText(BuildStatusPath, $"FAILED|{ex}");
                Debug.LogError($"{LogTag} Hata: {ex}");
            }
        }

        /// <summary>
        /// C:\Users\Osteometri\Desktop\Anatomi VR klasöründeki mevcut OpenVetAtlas_VR_V*.apk
        /// dosyalarını tarar ve bir sonraki versiyon numarasını döner (örn. V1 varsa 2 döner).
        /// </summary>
        private static int DetermineNextApkVersion(string outputDir)
        {
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
                return 1;
            }

            int maxFoundVersion = 0;
            string[] existingApks = Directory.GetFiles(outputDir, "*.apk", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < existingApks.Length; i++)
            {
                string fileName = Path.GetFileName(existingApks[i]);
                Match m = VersionApkRegex.Match(fileName);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int parsedVersion))
                {
                    if (parsedVersion > maxFoundVersion)
                    {
                        maxFoundVersion = parsedVersion;
                    }
                }
            }

            return maxFoundVersion >= 1 ? (maxFoundVersion + 1) : 1;
        }

        private static void ExecuteAutoBuildStage2()
        {
            try
            {
                File.WriteAllText(BuildStatusPath, $"STAGE_2_CONFIG|{DateTime.Now:HH:mm:ss}");

                // 1. Sahneyi, kemik prefab'larını, materyalleri ve Addressables kayıtlarını hazırla
                AnatomySetupTool.PrepareSceneAndAddressablesForBuild();

                // 2. Çıktı klasöründeki mevcut APK'ları tara ve yeni versiyonu belirle
                if (!Directory.Exists(OutputDirectory))
                {
                    Directory.CreateDirectory(OutputDirectory);
                }

                int nextVersion = DetermineNextApkVersion(OutputDirectory);
                string versionedFileName = $"{ApkFilePrefix}{nextVersion}.apk";
                string outputApkPath = Path.Combine(OutputDirectory, versionedFileName);

                // Her yeni derlemede bundleVersionCode değerini 1 artır ve bundleVersion'ı senkronize et ("1.0.2", "1.0.3" vb.)
                PlayerSettings.Android.bundleVersionCode += 1;
                if (PlayerSettings.Android.bundleVersionCode < nextVersion)
                {
                    PlayerSettings.Android.bundleVersionCode = nextVersion;
                }
                PlayerSettings.bundleVersion = $"1.0.{nextVersion}";
                AssetDatabase.SaveAssets();

                Debug.Log($"{LogTag} Otomatik Versiyon: V{nextVersion} | bundleVersion={PlayerSettings.bundleVersion} | bundleVersionCode={PlayerSettings.Android.bundleVersionCode} | Çıktı: {outputApkPath}");

                // 3. Addressables Player Content Build
                File.WriteAllText(BuildStatusPath, $"BUILDING_ADDRESSABLES|{DateTime.Now:HH:mm:ss}");
                Debug.Log($"{LogTag} [OpenVetAtlas Auto-Build] 2/4: Addressables verileri (Player Content) yeniden derleniyor...");

                AddressableAssetSettings addrSettings = AddressableAssetSettingsDefaultObject.GetSettings(true);
                if (addrSettings != null)
                {
                    AddressableAssetSettings.CleanPlayerContent(addrSettings.ActivePlayerDataBuilder);
                    AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult addrResult);
                    if (!string.IsNullOrEmpty(addrResult.Error))
                    {
                        Debug.LogWarning($"{LogTag} Addressables uyarısı: {addrResult.Error}");
                    }
                    else
                    {
                        Debug.Log($"{LogTag} Addressables başarıyla derlendi ({addrResult.Duration:F1} sn).");
                    }
                }

                // 4. IL2CPP + Gradle Android APK Derlemesi
                if (File.Exists(outputApkPath))
                {
                    File.Delete(outputApkPath);
                }

                File.WriteAllText(BuildStatusPath, $"BUILDING_APK|{DateTime.Now:HH:mm:ss}");
                Debug.Log($"{LogTag} [OpenVetAtlas Auto-Build] 3/4: APK derlemesi başlatılıyor -> {outputApkPath}");

                var buildOptions = new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Scenes/MainLab.unity" },
                    locationPathName = outputApkPath,
                    targetGroup = BuildTargetGroup.Android,
                    target = BuildTarget.Android,
                    options = BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(buildOptions);
                BuildSummary summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                {
                    double sizeMb = summary.totalSize / (1024.0 * 1024.0);
                    string msg = $"SUCCEEDED|{outputApkPath}|v{PlayerSettings.bundleVersion} (code {PlayerSettings.Android.bundleVersionCode})|{sizeMb:F1} MB|{summary.totalTime.TotalSeconds:F1}s";
                    File.WriteAllText(BuildStatusPath, msg);
                    Debug.Log($"{LogTag} [OpenVetAtlas Auto-Build] 4/4: APK BAŞARIYLA OLUŞTURULDU! Dosya: {outputApkPath} | Sürüm: {PlayerSettings.bundleVersion} (Code: {PlayerSettings.Android.bundleVersionCode}) | Boyut: {sizeMb:F1} MB | Süre: {summary.totalTime.TotalSeconds:F1} sn");
                }
                else
                {
                    string msg = $"FAILED|Result={summary.result}, Errors={summary.totalErrors}";
                    File.WriteAllText(BuildStatusPath, msg);
                    Debug.LogError($"{LogTag} APK derlemesi başarısız oldu: {msg}");
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(BuildStatusPath, $"FAILED|{ex}");
                Debug.LogError($"{LogTag} Stage 2 Hatası: {ex}");
            }
        }
    }
}
