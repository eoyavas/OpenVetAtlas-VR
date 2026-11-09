using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnatomiVR.Data
{
    /// <summary>
    /// Menü hiyerarşisi için ScriptableObject veri yapısı.
    /// Inspector'dan düzenlenebilir. Hayvan → Sistem → Bölge → Kemik ağacını tanımlar.
    /// 
    /// Oluşturma:
    ///   Project panelinde sağ tık → Create → AnatomiVR → Menu Data
    /// </summary>
    [CreateAssetMenu(fileName = "MenuData", menuName = "AnatomiVR/Menu Data")]
    public class MenuData : ScriptableObject
    {
        [Tooltip("Uygulamadaki tüm hayvan verileri")]
        public List<AnimalData> animals = new List<AnimalData>();
    }

    /// <summary>
    /// Bir hayvan türünü temsil eder (ör: Köpek, At, İnek).
    /// </summary>
    [Serializable]
    public class AnimalData
    {
        [Tooltip("Hayvan adı (Türkçe, UI'da gösterilir)")]
        public string displayName = "Köpek";

        [Tooltip("Hayvan kodu (Addressables key prefix'i, ör: Dog)")]
        public string animalCode = "Dog";

        [Tooltip("Bu hayvan başlangıçta kilitli mi?")]
        public bool isLocked = false;

        [Tooltip("Kilit açma ikonu (opsiyonel)")]
        public Sprite icon;

        [Tooltip("Bu hayvana ait anatomik sistemler")]
        public List<SystemData> systems = new List<SystemData>();
    }

    /// <summary>
    /// Bir anatomik sistemi temsil eder (ör: İskelet, Kas, Sinir).
    /// </summary>
    [Serializable]
    public class SystemData
    {
        [Tooltip("Sistem adı (Türkçe)")]
        public string displayName = "İskelet Sistemi";

        [Tooltip("Bu sistem başlangıçta kilitli mi?")]
        public bool isLocked = false;

        [Tooltip("Bu sisteme ait vücut bölgeleri")]
        public List<RegionData> regions = new List<RegionData>();
    }

    /// <summary>
    /// Bir vücut bölgesini temsil eder (ör: Ön Kol, Arka Kol, Gövde).
    /// </summary>
    [Serializable]
    public class RegionData
    {
        [Tooltip("Bölge adı (Türkçe)")]
        public string displayName = "Ön Kol";

        [Tooltip("Bu bölge başlangıçta kilitli mi?")]
        public bool isLocked = false;

        [Tooltip("Bu bölgedeki kemikler")]
        public List<BoneData> bones = new List<BoneData>();
    }

    /// <summary>
    /// Tek bir kemiği temsil eder. Addressables key'ini içerir.
    /// </summary>
    [Serializable]
    public class BoneData
    {
        [Tooltip("Kemik adı (Türkçe, UI'da gösterilir)")]
        public string displayName = "Humerus";

        [Tooltip("Addressables key'i (ör: Dog_Humerus). Format: {HayvanKodu}_{KemikAdı}")]
        public string addressableKey = "Dog_Humerus";

        [Tooltip("Bu kemik başlangıçta kilitli mi?")]
        public bool isLocked = false;

        [Tooltip("Kemik önizleme görseli (opsiyonel, UI butonunda gösterilir)")]
        public Sprite thumbnail;
    }
}
