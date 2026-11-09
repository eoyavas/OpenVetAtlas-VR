using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using AnatomiVR.Bones;

namespace AnatomiVR.Editor
{
    /// <summary>
    /// Kemiklerin üzerindeki anatomik oluşum etiketlerini (Caput humeri, Tuberculum majus, Fossa olecrani vb.)
    /// Unity Scene ekranında fare ile doğrudan kemik yüzeyine tıklayarak (Click-to-Place) veya
    /// 3B oklarla (PositionHandle) sürükleyerek manuel olarak yerleştirmeyi ve Prefab'a kaydetmeyi sağlayan araç.
    /// 
    /// Menü: AnatomiVR → Anatomik Etiket (Pin) Düzenleyici
    /// </summary>
    public class AnatomicalPinEditorWindow : EditorWindow
    {
        private static readonly string[] BonePrefabPaths =
        {
            "Assets/Prefabs/Bones/Dog_Humerus.prefab",
            "Assets/Prefabs/Bones/Dog_Scapula.prefab",
            "Assets/Prefabs/Bones/Dog_Radius.prefab",
            "Assets/Prefabs/Bones/Dog_Ulna.prefab"
        };

        private static readonly string[] BoneDisplayNames =
        {
            "Dog_Humerus (Humerus)",
            "Dog_Scapula (Scapula)",
            "Dog_Radius (Radius)",
            "Dog_Ulna (Ulna)"
        };

        private int _selectedBoneIdx;
        private BoneAnatomicalLabels _activeTarget;
        private int _selectedPinIdx = -1;
        private bool _clickToPlaceMode;
        private string _newPinLabelText = "Yeni Oluşum (Örn: Caput humeri)";
        private float _defaultNormalOffset = 0.08f; // Dünya biriminde ~8 cm dışarı
        private Vector2 _scrollPos;

        [MenuItem("AnatomiVR/Anatomik Etiket (Pin) Düzenleyici", priority = 5)]
        public static void OpenWindow()
        {
            var win = GetWindow<AnatomicalPinEditorWindow>("Anatomik Pin Aracı");
            win.minSize = new Vector2(420f, 560f);
            win.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            TryAutoDetectSelection();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            _clickToPlaceMode = false;
        }

        private void OnSelectionChange()
        {
            TryAutoDetectSelection();
            Repaint();
        }

        private void TryAutoDetectSelection()
        {
            if (Selection.activeGameObject != null)
            {
                var labels = Selection.activeGameObject.GetComponentInParent<BoneAnatomicalLabels>();
                if (labels != null)
                {
                    _activeTarget = labels;
                }
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                normal = { textColor = new Color(0.25f, 0.88f, 1.0f) }
            };
            EditorGUILayout.LabelField("🦴 3B Anatomik Etiket (Pin) Yerleştirici", titleStyle);
            EditorGUILayout.HelpBox(
                "1. Düzenlemek istediğiniz kemiği seçip 'Sahneye Çağır ve Odaklan' butonuna basın.\n" +
                "2. 'Yüzeye Tıklayarak Pin Ekle' modunu açıp Scene ekranında kemiğin istediğiniz noktasına Sol Tıklayın.\n" +
                "3. Listeden bir pini seçerek 3B oklarla nokta/etiket yerini ince ayarlayın.\n" +
                "4. İşiniz bitince 'Prefab'a Kaydet' butonuna basın.",
                MessageType.Info);

            EditorGUILayout.Space(6);

            // ─── 1. Kemik Seçimi ───
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("1. Düzenlenecek Kemik Prefab'ı", EditorStyles.boldLabel);
            _selectedBoneIdx = EditorGUILayout.Popup("Kemik Seçin:", _selectedBoneIdx, BoneDisplayNames);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.20f, 0.68f, 0.95f);
            if (GUILayout.Button("🔍 Sahneye Çağır ve Odaklan", GUILayout.Height(30)))
            {
                SpawnOrFocusSelectedBoneInScene();
            }
            GUI.backgroundColor = Color.white;

            if (_activeTarget != null)
            {
                GUI.backgroundColor = new Color(0.22f, 0.82f, 0.45f);
                if (GUILayout.Button("💾 Prefab'a Kaydet", GUILayout.Height(30)))
                {
                    SaveActiveTargetToPrefab();
                }
                GUI.backgroundColor = Color.white;
            }
            EditorGUILayout.EndHorizontal();

            _activeTarget = (BoneAnatomicalLabels)EditorGUILayout.ObjectField(
                "Aktif Kemik Objesi:", _activeTarget, typeof(BoneAnatomicalLabels), true);
            EditorGUILayout.EndVertical();

            if (_activeTarget == null)
            {
                EditorGUILayout.HelpBox("Lütfen yukarıdan 'Sahneye Çağır ve Odaklan' butonuna basın veya Hierarchy'den bir kemik seçin.", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(6);

            // ─── 2. Yüzeye Tıklayarak Pin Ekleme (Click-to-Place) ───
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("2. Fare ile Yüzeye Tıklayarak Pin Ekle (Click-to-Place)", EditorStyles.boldLabel);
            _newPinLabelText = EditorGUILayout.TextField("Yeni Etiket Adı (Latince):", _newPinLabelText);
            _defaultNormalOffset = EditorGUILayout.Slider("Etiket Uzaklığı (m):", _defaultNormalOffset, 0.03f, 0.22f);

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = _clickToPlaceMode
                ? new Color(1.0f, 0.42f, 0.28f)
                : new Color(0.18f, 0.75f, 0.55f);

            string modeBtnText = _clickToPlaceMode
                ? "🎯 YÜZEYE TIKLAMA MODU AÇIK (Kapatmak İçin Tıklayın)"
                : "🖱️ Yüzeye Tıklayarak Pin Ekleme Modunu Aç";

            if (GUILayout.Button(modeBtnText, GUILayout.Height(34)))
            {
                _clickToPlaceMode = !_clickToPlaceMode;
                SceneView.RepaintAll();
            }
            GUI.backgroundColor = prevBg;

            if (_clickToPlaceMode)
            {
                EditorGUILayout.HelpBox("Scene penceresinde kemik yüzeyinde istediğiniz anatomik oluşumun üzerine SOL TIKLAYIN!", MessageType.None);
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            // ─── 3. Mevcut Anatomik Pin Listesi ───
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"3. Anatomik Noktalar ({_activeTarget.landmarks.Count} adet)", EditorStyles.boldLabel);
            if (GUILayout.Button("+ Manuel Ekle", GUILayout.Width(105)))
            {
                Undo.RecordObject(_activeTarget, "Add Anatomical Landmark");
                _activeTarget.landmarks.Add(new AnatomicalLandmark
                {
                    labelText = _newPinLabelText,
                    anchorLocalPos = Vector3.zero,
                    labelLocalPos = new Vector3(0.4f, 0.4f, 0f)
                });
                _selectedPinIdx = _activeTarget.landmarks.Count - 1;
                EditorUtility.SetDirty(_activeTarget);
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);
            for (int i = 0; i < _activeTarget.landmarks.Count; i++)
            {
                var lm = _activeTarget.landmarks[i];
                bool isSelected = (_selectedPinIdx == i);

                GUI.backgroundColor = isSelected ? new Color(0.25f, 0.55f, 0.85f, 0.5f) : Color.white;
                EditorGUILayout.BeginVertical("helpbox");
                GUI.backgroundColor = Color.white;

                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Toggle(isSelected, $"#{i + 1}", "Button", GUILayout.Width(42)))
                {
                    if (_selectedPinIdx != i)
                    {
                        _selectedPinIdx = i;
                        SceneView.RepaintAll();
                    }
                }

                EditorGUI.BeginChangeCheck();
                string newLabel = EditorGUILayout.TextField(lm.labelText);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_activeTarget, "Rename Landmark");
                    lm.labelText = newLabel;
                    EditorUtility.SetDirty(_activeTarget);
                    SceneView.RepaintAll();
                }

                GUI.backgroundColor = new Color(0.85f, 0.25f, 0.28f);
                if (GUILayout.Button("Sil", GUILayout.Width(42)))
                {
                    Undo.RecordObject(_activeTarget, "Remove Landmark");
                    _activeTarget.landmarks.RemoveAt(i);
                    if (_selectedPinIdx >= _activeTarget.landmarks.Count)
                        _selectedPinIdx = _activeTarget.landmarks.Count - 1;
                    EditorUtility.SetDirty(_activeTarget);
                    SceneView.RepaintAll();
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();

                if (isSelected)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 newAnchor = EditorGUILayout.Vector3Field("Kemik Noktası (Local):", lm.anchorLocalPos);
                    Vector3 newLabelPos = EditorGUILayout.Vector3Field("Etiket Konumu (Local):", lm.labelLocalPos);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(_activeTarget, "Move Landmark");
                        lm.anchorLocalPos = newAnchor;
                        lm.labelLocalPos = newLabelPos;
                        EditorUtility.SetDirty(_activeTarget);
                        SceneView.RepaintAll();
                    }
                }

                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ─────────────── Scene View 3B Etkileşimi (Raycast & Handles) ───────────────

        private void OnSceneGUI(SceneView sceneView)
        {
            if (_activeTarget == null)
                return;

            Event e = Event.current;

            // 1. Yüzeye Tıklayarak Pin Ekleme Modu
            if (_clickToPlaceMode)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

                Ray worldRay = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (TryRaycastBoneMesh(_activeTarget.gameObject, worldRay, out Vector3 hitPoint, out Vector3 hitNormal))
                {
                    Handles.color = new Color(0.0f, 1.0f, 0.85f, 0.95f);
                    Handles.DrawWireDisc(hitPoint, hitNormal, 0.012f);
                    Handles.DrawLine(hitPoint, hitPoint + hitNormal * _defaultNormalOffset);
                    Handles.Label(hitPoint + hitNormal * _defaultNormalOffset, $"  + {_newPinLabelText}", EditorStyles.whiteBoldLabel);
                    sceneView.Repaint();

                    if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
                    {
                        Undo.RecordObject(_activeTarget, "Place Anatomical Pin on Surface");
                        Vector3 localAnchor = _activeTarget.transform.InverseTransformPoint(hitPoint);
                        Vector3 worldLabel  = hitPoint + hitNormal.normalized * _defaultNormalOffset;
                        Vector3 localLabel  = _activeTarget.transform.InverseTransformPoint(worldLabel);

                        _activeTarget.landmarks.Add(new AnatomicalLandmark
                        {
                            labelText = _newPinLabelText,
                            anchorLocalPos = localAnchor,
                            labelLocalPos = localLabel
                        });

                        _selectedPinIdx = _activeTarget.landmarks.Count - 1;
                        EditorUtility.SetDirty(_activeTarget);
                        e.Use();
                        Repaint();
                    }
                }
            }

            // 2. Seçili Pin için 3B Taşıma Okları (Handles.PositionHandle)
            if (_selectedPinIdx >= 0 && _selectedPinIdx < _activeTarget.landmarks.Count)
            {
                var lm = _activeTarget.landmarks[_selectedPinIdx];
                Vector3 worldAnchor = _activeTarget.transform.TransformPoint(lm.anchorLocalPos);
                Vector3 worldLabel  = _activeTarget.transform.TransformPoint(lm.labelLocalPos);

                EditorGUI.BeginChangeCheck();
                Vector3 newWorldAnchor = Handles.PositionHandle(worldAnchor, _activeTarget.transform.rotation);
                Vector3 newWorldLabel  = Handles.PositionHandle(worldLabel, Quaternion.identity);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(_activeTarget, "Move Anatomical Pin Handle");
                    lm.anchorLocalPos = _activeTarget.transform.InverseTransformPoint(newWorldAnchor);
                    lm.labelLocalPos  = _activeTarget.transform.InverseTransformPoint(newWorldLabel);
                    EditorUtility.SetDirty(_activeTarget);
                    Repaint();
                }
            }
        }

        private static bool TryRaycastBoneMesh(GameObject boneRoot, Ray ray, out Vector3 hitPoint, out Vector3 hitNormal)
        {
            hitPoint = Vector3.zero;
            hitNormal = Vector3.up;

            // Geçici olarak non-convex tam mesh üzerinden hassas raycast yap veya mevcut collider'ı kullan
            MeshFilter mf = boneRoot.GetComponentInChildren<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var tempGO = new GameObject("__TempPinRaycastCollider__") { hideFlags = HideFlags.HideAndDontSave };
                tempGO.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation);
                tempGO.transform.localScale = mf.transform.lossyScale;
                var mc = tempGO.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;

                bool hit = mc.Raycast(ray, out RaycastHit hitInfo, 50f);
                if (hit)
                {
                    hitPoint = hitInfo.point;
                    hitNormal = hitInfo.normal;
                }
                DestroyImmediate(tempGO);
                return hit;
            }

            return false;
        }

        // ─────────────── Sahneye Çağırma & Prefab Kayıt ───────────────

        private void SpawnOrFocusSelectedBoneInScene()
        {
            string prefabPath = BonePrefabPaths[Mathf.Clamp(_selectedBoneIdx, 0, BonePrefabPaths.Length - 1)];
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[AnatomicalPinEditor] Prefab bulunamadı: {prefabPath}");
                return;
            }

            // Sahnede zaten düzenleme örneği varsa onu bul, yoksa diseksiyon masasının üstünde oluştur
            string instanceName = $"[PinEdit]_{prefab.name}";
            GameObject existing = GameObject.Find(instanceName) ?? GameObject.Find(prefab.name);

            if (existing == null)
            {
                existing = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                existing.name = instanceName;
                existing.transform.position = new Vector3(0.0f, 1.22f, 0.0f);
                Undo.RegisterCreatedObjectUndo(existing, "Spawn Bone For Pin Editing");
            }

            var labels = existing.GetComponent<BoneAnatomicalLabels>();
            if (labels == null)
            {
                labels = Undo.AddComponent<BoneAnatomicalLabels>(existing);
            }

            _activeTarget = labels;
            _selectedPinIdx = labels.landmarks.Count > 0 ? 0 : -1;
            Selection.activeGameObject = existing;

            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }
        }

        private void SaveActiveTargetToPrefab()
        {
            if (_activeTarget == null) return;

            GameObject root = _activeTarget.gameObject;
            string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);

            if (string.IsNullOrEmpty(prefabPath))
            {
                // İsimden eşleştir
                for (int i = 0; i < BonePrefabPaths.Length; i++)
                {
                    if (root.name.IndexOf( System.IO.Path.GetFileNameWithoutExtension(BonePrefabPaths[i]), StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        prefabPath = BonePrefabPaths[i];
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(prefabPath))
            {
                prefabPath = BonePrefabPaths[Mathf.Clamp(_selectedBoneIdx, 0, BonePrefabPaths.Length - 1)];
            }

            // Prefab asset'ini yükle ve landmarks listesini kopyala
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot != null)
            {
                var prefabLabels = prefabRoot.GetComponent<BoneAnatomicalLabels>();
                if (prefabLabels == null)
                    prefabLabels = prefabRoot.AddComponent<BoneAnatomicalLabels>();

                prefabLabels.landmarks = new List<AnatomicalLandmark>();
                foreach (var lm in _activeTarget.landmarks)
                {
                    prefabLabels.landmarks.Add(new AnatomicalLandmark
                    {
                        labelText = lm.labelText,
                        anchorLocalPos = lm.anchorLocalPos,
                        labelLocalPos = lm.labelLocalPos
                    });
                }

                prefabLabels.PrebakeAllPinNormals();

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                PrefabUtility.UnloadPrefabContents(prefabRoot);
                AssetDatabase.SaveAssets();

                Debug.Log($"[AnatomicalPinEditor] '{prefabPath}' üzerine {_activeTarget.landmarks.Count} anatomik etiket kaydedildi!");
                EditorUtility.DisplayDialog("Prefab Kaydedildi",
                    $"'{prefabPath}' prefab'ına {_activeTarget.landmarks.Count} adet anatomik etiket başarıyla kaydedildi!",
                    "Tamam");
            }
        }
    }
}
