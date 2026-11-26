using System;
using System.Collections.Generic;
using UnityEngine;

// Colores y fuerzas de la iluminacion de cada nivel (lo lee LevelLighting).
// Hay una entrada por escena; si una escena no tiene entrada usa la primera.
// El asset vive en Resources/IluminacionNiveles para que se cargue solo.
[CreateAssetMenu(menuName = "Iluminacion/Perfil de niveles", fileName = "IluminacionNiveles")]
public class LevelLightingProfile : ScriptableObject
{
    [Serializable]
    public class Mood
    {
        [Tooltip("Nombre exacto de la escena")]
        public string scene = "NormalLevel";

        [Header("Luz base")]
        public Color ambientColor = new Color(1f, 0.85f, 0.7f);
        [Range(0f, 1.5f)] public float ambientIntensity = 0.72f;

        [Header("Luces")]
        public Color playerLight = new Color(0.85f, 0.95f, 1f);
        [Range(0f, 2f)] public float playerLightIntensity = 0.55f;
        public float playerLightRadius = 6.5f;
        public Color lampLight = new Color(1f, 0.72f, 0.38f);
        [Range(0f, 3f)] public float lampIntensity = 1.1f;
        public float lampRadius = 7f;
        public Color exitLockedLight = new Color(1f, 0.45f, 0.25f);
        public Color exitOpenLight = new Color(0.35f, 1f, 0.8f);
        public Color hazardLight = new Color(1f, 0.25f, 0.2f);
        [Range(0f, 2f)] public float hazardIntensity = 0.35f;

        [Header("Faroles de pared (se colocan solos en cada cuarto)")]
        public bool wallLanterns = true;
        [Tooltip("Faroles por cada cuarto de 33x33")]
        [Range(0, 6)] public int lanternsPerRoom = 3;
        [Range(0f, 3f)] public float lanternIntensity = 1.1f;
        public float lanternRadius = 7.5f;

        [Header("Rayos de luz desde arriba")]
        public bool lightShafts = true;
        public Color shaftColor = new Color(1f, 0.88f, 0.65f);
        [Range(0f, 2f)] public float shaftIntensity = 0.35f;

        [Header("Polvo flotando")]
        public Color dustColor = new Color(1f, 0.92f, 0.8f, 0.6f);
        public float dustPerSecond = 12f;

        [Header("Post-proceso")]
        [Range(0f, 3f)] public float bloom = 0.9f;
        [Range(0f, 1f)] public float vignette = 0.32f;
        [Range(-100f, 100f)] public float saturation = 8f;
        [Range(-100f, 100f)] public float contrast = 14f;
        public Color colorFilter = Color.white;
        public Color shadowsTone = new Color(0.35f, 0.4f, 0.6f);
        public Color highlightsTone = new Color(1f, 0.8f, 0.55f);
    }

    public List<Mood> moods = new List<Mood>();

    public Mood Get(string scene)
    {
        foreach (var m in moods) if (m.scene == scene) return m;
        return moods.Count > 0 ? moods[0] : new Mood();
    }

    // Starting values for every level type (used to fill a new asset and when the asset is missing)
    public static List<Mood> Defaults()
    {
        return new List<Mood>
        {
            // Mine / saloon with lanterns: warm amber
            new Mood { scene = "NormalLevel" },

            // Night, moonlight from above: cold blue, the lamps stand out warm
            new Mood
            {
                scene = "PlatformerLevel",
                ambientColor = new Color(0.55f, 0.65f, 1f), ambientIntensity = 0.68f,
                playerLight = new Color(0.8f, 0.9f, 1f),
                shaftColor = new Color(0.65f, 0.8f, 1f), shaftIntensity = 0.5f,
                dustColor = new Color(0.8f, 0.9f, 1f, 0.6f), dustPerSecond = 10f,
                saturation = -5f, contrast = 16f,
                shadowsTone = new Color(0.3f, 0.4f, 0.75f), highlightsTone = new Color(0.85f, 0.9f, 1f),
            },

            // Tense and dangerous: crimson and purple
            new Mood
            {
                scene = "EliteLevel",
                ambientColor = new Color(1f, 0.78f, 0.88f), ambientIntensity = 0.9f, playerLightIntensity = 0.7f, lampIntensity = 1.4f,
                lampLight = new Color(1f, 0.5f, 0.35f),
                shaftColor = new Color(1f, 0.55f, 0.55f), shaftIntensity = 0.3f,
                dustColor = new Color(1f, 0.75f, 0.8f, 0.55f),
                hazardIntensity = 0.5f, vignette = 0.28f, saturation = 4f, contrast = 10f,
                shadowsTone = new Color(0.6f, 0.45f, 0.65f), highlightsTone = new Color(1f, 0.6f, 0.5f),
            },

            // Almost dark: the bull and its fire are the main light
            new Mood
            {
                scene = "BossLevel",
                ambientColor = new Color(0.95f, 0.82f, 0.82f), ambientIntensity = 0.85f, playerLightIntensity = 0.7f,
                lampLight = new Color(1f, 0.6f, 0.3f), lampIntensity = 1.3f,
                shaftColor = new Color(1f, 0.6f, 0.4f), shaftIntensity = 0.22f,
                dustColor = new Color(1f, 0.6f, 0.35f, 0.7f), dustPerSecond = 16f,
                bloom = 1.2f, vignette = 0.3f, contrast = 10f,
                shadowsTone = new Color(0.55f, 0.42f, 0.5f), highlightsTone = new Color(1f, 0.6f, 0.35f),
            },

            // Clear and readable: the player is learning
            new Mood
            {
                scene = "TutorialScene",
                ambientColor = new Color(1f, 0.92f, 0.82f), ambientIntensity = 0.7f,
                playerLightIntensity = 0.4f, shaftIntensity = 0.3f,
                dustPerSecond = 8f, vignette = 0.25f, contrast = 10f,
            },
        };
    }

    private void Reset() { moods = Defaults(); }
}
