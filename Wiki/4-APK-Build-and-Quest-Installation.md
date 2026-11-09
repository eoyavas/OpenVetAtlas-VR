# 📦 4. APK Build & Quest Installation / APK Derleme ve Gözlüğe Kurulum

This page covers how to build a new auto-versioned APK from Unity and how to install (sideload) the `.apk` file onto a **Meta Quest 3, Quest 3S, or Quest 2** headset.

---

## 🛠️ 1. Building an Auto-Versioned APK in Unity / Otomatik Versiyonlu APK Üretimi

**OpenVetAtlas VR** includes a dedicated build pipeline (`Assets/Scripts/Editor/AutoBuilder.cs`) that automates scene verification, Addressables asset bundle compilation, version incrementing, and IL2CPP Android APK packaging in a single click.

### Steps / Adımlar:
1. Open the project in **Unity 6 (`6000.0+`)** with the **Android Build Support (SDK/NDK + OpenJDK)** module installed.
2. In the top menu bar, click:
   **`Tools -> OpenVetAtlas -> Build APK (Auto-Version)`**

### What `AutoBuilder` Does Automatically / Otomatik Yapılan İşlemler:
1. **Scene & Prefab Sync:** Runs `AnatomySetupTool.PrepareSceneAndAddressablesForBuild()` to ensure all bone prefabs, pre-baked collision meshes, pre-baked pin normals, and URP materials are up to date.
2. **Auto-Increment Versioning:**
   * Scans the output folder (`C:\Users\Osteometri\Desktop\Anatomi VR`) for existing `OpenVetAtlas_VR_V*.apk` files.
   * If `OpenVetAtlas_VR_V1.apk` exists, the new build is automatically named **`OpenVetAtlas_VR_V2.apk`** (then `V3`, `V4`, etc. on subsequent builds).
   * Increments `PlayerSettings.Android.bundleVersionCode` by `+1` and synchronizes `PlayerSettings.bundleVersion` with the filename (`"1.0.2"`, `"1.0.3"`, etc.).
3. **Addressables Clean Build:** Cleans and rebuilds all Addressables bone bundles (`AddressableAssetSettings.BuildPlayerContent()`).
4. **IL2CPP ARM64 APK Compilation:** Produces the final standalone Meta Quest `.apk`.

---

## 🥽 2. Installing the APK onto Meta Quest / Gözlüğe APK Kurulumu (Sideloading)

You can install `OpenVetAtlas_VR_V2.apk` onto your Meta Quest 3 / 3S / 2 headset using **Meta Quest Developer Hub (MQDH)**, **SideQuest**, or **ADB**:

### Method A: Using SideQuest or Meta Quest Developer Hub (Recommended)
1. Enable **Developer Mode** on your Meta Quest headset via the Meta Horizon mobile app (*Menu -> Devices -> Headset Settings -> Developer Mode*).
2. Connect the headset to your PC via a USB-C cable and click **"Allow"** on the USB debugging prompt inside the headset.
3. Open **SideQuest** (or **Meta Quest Developer Hub**) and drag-and-drop **`OpenVetAtlas_VR_V2.apk`** onto the window.

### Method B: Command Line (ADB)
```bash
adb install -r OpenVetAtlas_VR_V2.apk
```

### Launching the App Inside Meta Quest / Gözlük İçinde Uygulamayı Açma
1. Put on your Meta Quest headset and press the **Meta** button to open the universal menu.
2. Open **Library (Uygulama Kütüphanesi)**.
3. On the left sidebar (or top-right filter dropdown), select **Unknown Sources (Bilinmeyen Kaynaklar)**.
4. Click **OpenVetAtlas VR** to launch the laboratory!
