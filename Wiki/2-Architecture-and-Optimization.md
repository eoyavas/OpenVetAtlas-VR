# ⚙️ 2. Architecture & Quest Optimization / Yazılım Mimarisi ve Optimizasyon

**OpenVetAtlas VR (oVa)** is engineered specifically for standalone mobile VR headsets (**Meta Quest 3, Quest 3S, Quest 2**) running on Android (ARM64 / Vulkan / Universal Render Pipeline) at a stable **72–90 FPS** without thermal throttling or RAM spikes.

---

## 🏛️ Software Architecture / C# Katmanlı Mimari

All project scripts are located under `Assets/Scripts/` and organized into 6 clean namespaces:

```text
Assets/Scripts/
├── Bones/
│   ├── BoneAnatomicalLabels.cs      # 3D Holographic Pin System, Pre-Baked Normals & Raycast Hysteresis
│   └── BoneIdentifier.cs            # Zero-G Suspension, L3/R3 Quick Delete & Table/Wall Boundary Lock
├── Data/
│   └── MenuData.cs                  # 4-Level ScriptableObject Data Model (Animal -> System -> Region -> Bone)
├── Editor/
│   ├── AnatomicalPinEditorWindow.cs # Interactive SceneView Click-to-Place 3D Pin Editor Tool
│   ├── AnatomySetupTool.cs          # One-Click Scene, Prefab, URP, Material & Addressables Setup Pipeline
│   ├── AutoBuilder.cs               # Auto-Incrementing APK Build Pipeline (V1 -> V2 -> V3...)
│   └── BlenderAutoSyncPostprocessor.cs # Automatic FBX & Blender Pin_ Empty Synchronizer
├── Managers/
│   ├── AnatomyManager.cs            # Addressables Async Loader & 4-Bone FIFO Queue Memory Manager
│   └── BoneResetManager.cs          # Out-of-Bounds Safety Respawner
├── UI/
│   └── UIMenuManager.cs             # Smart Screen UI, Bilingual [TR|EN] Engine & 3D Floating Tutorial Tablet
└── XR/
    ├── ControllerVisibilityManager.cs # Hides Controller Mesh & Laser Line While Grabbing Bones
    ├── DesktopTestController.cs     # PC Editor Simulator + VR Floor/Room Boundary Stabilizer
    └── XRUISetup.cs                 # World-Space Canvas & 3D Occlusion Raycaster Configurator
```

---

## 🚀 Key Technical Innovations / Temel Teknik Çözümler

### 1. Dynamic Memory Management (Addressables + 4-Bone FIFO Queue)
High-resolution 3D scanned bones contain tens of thousands of vertices. Loading all species and bones into memory at startup would cause severe RAM pressure on mobile VR headsets.
* **`AnatomyManager.cs`** uses Unity **Addressables** (`Addressables.InstantiateAsync`) to load bone prefabs on demand.
* A strict **First-In-First-Out (FIFO)** queue limits active bones on the table to **4 unique specimens** (`maxBonesOnTable = 4`).
* Duplicate prevention (`IsBoneOnTable`) ensures only 1 instance of the same bone key (e.g., `Dog_Humerus`) can exist at once, while allowing cross-species comparison (e.g., `Dog_Humerus` alongside `Horse_Humerus`).
* When the 5th bone is requested (or when a bone is deleted via `L3`/`R3`), `Addressables.ReleaseInstance` immediately frees the GPU/CPU memory.

### 2. Pre-Baked Surface Normals & Fossa Raycast Hysteresis (`BoneAnatomicalLabels.cs`)
Selecting anatomical landmarks located inside deep concavities—such as *fossa olecrani*, *fossa supraspinata*, *fossa infraspinata*, and *fossa subscapularis*—is notoriously difficult in VR because standard convex colliders bridge over concavities and hand tremor causes laser jitter.
* **Dual-Collider Architecture:** Each bone uses a `Convex MeshCollider` for stable physics and grabbing, paired with a dedicated, non-physical, **Non-Convex `MeshCollider`** (`__ExactBoneSurfaceRaycaster__`) used exclusively for mathematical laser raycasting into deep fossae.
* **Pre-Baked Outward Normals (`PrebakeAllPinNormals`):** During Editor setup/build, the true outward surface normal of every landmark is computed once from the mesh vertices and serialized into the prefab (`prebakedOutwardNormal`). At runtime on Meta Quest, **zero vertex array scans** are performed (`0 ms` CPU overhead).
* **Pure Mathematical Ray Selection & Hysteresis:** Pin spheres have no physical colliders that could deflect XRI laser curves. Selection uses ray-to-point perpendicular distance (`enterRadius = 2.6 cm`, `exitRadius = 4.2 cm` hysteresis), an 80 ms anti-bounce lock, and a 250 ms trigger grace period to eliminate hand tremor when pulling the trigger.

### 3. Instant Bone Spawning via Pre-Baked Collision Meshes
Cooking `MeshCollider` physics data at runtime causes noticeable frame hitches when spawning high-poly bones.
* **`PlayerSettings.bakeCollisionMeshes = true`** and **`Physics.BakeMesh(meshId, convex: true/false)`** pre-bake both the convex physics hull and the non-convex fossa raycast mesh at build time inside `AnatomySetupTool.EnsureBonePrefabs()`.
* Combined with `Addressables.InitializeAsync()` at startup, bones spawn onto the table with **zero physics cooking delay**.

### 4. Custom `AnatomiVR/BakedUnlit` Shader & URP Pipeline
* **Zero Realtime Lights / Zero Shadows:** All environment surfaces (`YAPILAR_Unity_Baked.obj` with 4K baked lightmaps) and bones use a custom single-pass instanced shader (`Assets/Shaders/BakedUnlit.shader`) with view-dependent 3D contour shading (`_ShadingStrength`) so bone concavities and ridges remain crisp without any real-time light cost.
* **4x MSAA + ASTC Compression:** Configured for Vulkan Single-Pass Instanced rendering on Meta Quest.
