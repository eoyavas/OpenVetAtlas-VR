# 🦴 3. Adding New Bones & Blender Workflow / Yeni Kemik ve Pin Ekleme Rehberi

**OpenVetAtlas VR (oVa)** features an automated pipeline that synchronizes 3D `.fbx` bone models and anatomical landmarks directly from your desktop folder hierarchy and Blender into Unity Prefabs and Addressables.

**OpenVetAtlas VR (oVa)**, yeni taranan 3B `.fbx` kemik modellerini ve Blender'da işaretlenen anatomik noktaları otomatik olarak tanıyan ve Unity Prefab + Addressables sistemine kaydeden tam otomatik bir iş akışına sahiptir.

---

## 📁 1. Folder Hierarchy for Adding New Bones / Yeni Kemik Klasör Hiyerarşisi

To add a new bone to the application, place its `.fbx` file inside the corresponding species, system, region, and bone folder under `Kemik/`:

Uygulamaya yeni bir kemik eklemek için `.fbx` dosyasını masaüstündeki `Anatomi VR\Kemik\` klasörü altındaki ilgili tür, sistem, bölge ve kemik klasörüne atmanız yeterlidir:

```text
Kemik/
├── Canis/                      # Dog (Köpek)
│   └── Systema Skeletale/
│       ├── Ossa Membri Thoracici/
│       │   ├── Scapula/        (Dog_scapula.fbx)
│       │   ├── Humerus/        (Dog_humerus.fbx)
│       │   ├── Radius/         (Dog_radius.fbx)
│       │   ├── Ulna/           (Dog_ulna.fbx)
│       │   ├── Carpal/
│       │   ├── Metacarpal/
│       │   └── Phalanx/
│       ├── Ossa Membri Pelvini/
│       │   ├── Coxae, Femur, Tibia, Fibula, Tarsal, Metatarsal, Phalanx
│       ├── Columna Vertebralis/
│       │   ├── Cervical, Throcal, Lumbal, Sacrum, Caudal
│       ├── Skeleton Thoracis/
│       │   ├── Costae, Sternum
│       └── Skeleton Axiale/
│           ├── Cranium, Mandibula
├── Equide/                     # Horse (At)
├── Ruminant/                   # Cattle (Sığır)
└── Sus/                        # Pig (Domuz)
```

### Automatic Menu Unlocking / Otomatik Menü Kilidi Açma
When you run **`Tools -> Anatomi VR -> Otomatik Sistem Kurulumu`** in Unity:
1. `AnatomySetupTool` scans the `Kemik/` directory tree.
2. Any folder containing an `.fbx` file is **automatically unlocked (`isLocked = false`)** in the Smart Screen UI, converted into a ready-to-grab VR Prefab (`Assets/Prefabs/Bones/`), pre-baked for physics and normals, and registered in **Addressables**!
3. Folders without an `.fbx` file remain visible in the menu as locked (`[ - ]`) placeholders for upcoming specimens.

---

## 🎨 2. Adding Anatomical Landmarks in Blender / Blender İle Anatomik Pin Ekleme

You can place anatomical landmarks directly in **Blender** before exporting the `.fbx`:

1. Open your bone mesh in Blender.
2. For each anatomical landmark, create an **Empty** object (`Add -> Empty -> Plain Axes`) as a child of the bone mesh (or in the same scene).
3. Name the Empty using the prefix **`Pin_`** followed by the Latin anatomical name:
   * `Pin_Caput humeri`
   * `Pin_Tuberculum majus`
   * `Pin_Fossa olecrani`
   * `Pin_Sulcus m. brachialis`
4. Snap the Empty directly onto the bone surface vertex.
5. Export the `.fbx` file. **`BlenderAutoSyncPostprocessor.cs`** automatically detects the updated `.fbx`, extracts all `Pin_*` coordinates, computes their outward surface normals (`PrebakeAllPinNormals`), and updates the Unity Prefab!

---

## 🖱️ 3. Unity In-Editor "Click-to-Place" Pin Tool / Unity İçi Pin Düzenleyici

You can also add, rename, move, or delete anatomical landmarks directly inside the Unity Editor without opening Blender:

1. In the Unity top menu, click **`AnatomiVR -> Anatomik Etiket (Pin) Düzenleyici`**.
2. Select the bone prefab from the dropdown and click **"🔍 Sahneye Çağır ve Odaklan"**.
3. Type the Latin name in **"Yeni Etiket Adı (Latince)"** and click **"🖱️ Yüzeye Tıklayarak Pin Ekleme Modunu Aç"**.
4. Simply **Left-Click** anywhere on the 3D bone surface in the Scene View to place the pin!
5. Click **"💾 Prefab'a Kaydet"** to save and pre-bake the normals into the prefab.
