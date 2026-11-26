using System.Collections.Generic;
using UnityEngine;

// Reproductor central de efectos de sonido. Los clips viven en Resources/Sonidos y se piden por nombre:
//   GameAudio.Play("Caja_Rompe");             efecto suelto (se pausa con el juego)
//   GameAudio.PlayAt("Rifle_Disparo", pos);   igual, pero mas bajo si pasa lejos de la camara
//   GameAudio.PlayUI("UI_Click");             interfaz: suena aunque el juego este en pausa
//   GameAudio.SetLoop("Laser_Apuntando", this, true/false);   sonido continuo ligado a un objeto
// Si un nombre no existe, simplemente no suena.
public static class GameAudio
{
    [Tooltip("Volumen general de los efectos")]
    public static float Volume = 0.8f;
    /// <summary>Beyond this distance from the camera, world sounds are silent.</summary>
    public static float HearingDistance = 22f;

    private static GameObject host;
    private static AudioSource[] pool;
    private static AudioSource ui;
    private static int next;
    private static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
    private static readonly Dictionary<(Object, string), (Object owner, AudioSource source)> loops = new Dictionary<(Object, string), (Object, AudioSource)>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        host = null; pool = null; ui = null; next = 0;
        clips.Clear(); loops.Clear();
    }

    public static AudioClip Clip(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (!clips.TryGetValue(name, out var c))
        {
            c = Resources.Load<AudioClip>("Sonidos/" + name);
            if (c == null) Debug.LogWarning($"[GameAudio] No existe el sonido '{name}' en Resources/Sonidos.");
            clips[name] = c;
        }
        return c;
    }

    private static void Ensure()
    {
        if (host != null) return;
        host = new GameObject("Audio Juego");
        Object.DontDestroyOnLoad(host);
        host.AddComponent<GameAudioHost>();
        pool = new AudioSource[12];
        for (int i = 0; i < pool.Length; i++) pool[i] = NewSource();
        ui = NewSource();
        ui.ignoreListenerPause = true;   // menus make noise while the game is paused
    }

    private static AudioSource NewSource()
    {
        var s = host.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0f;
        return s;
    }

    public static void Play(string name, float volume = 1f, float pitchVariation = 0.06f)
    {
        var c = Clip(name);
        if (c == null) return;
        Ensure();
        var s = pool[next++ % pool.Length];
        s.pitch = 1f + Random.Range(-pitchVariation, pitchVariation);
        s.PlayOneShot(c, volume * Volume);
    }

    /// <summary>World sound: fades with the distance to the camera, silent when far away.</summary>
    public static void PlayAt(string name, Vector3 position, float volume = 1f, float pitchVariation = 0.06f)
    {
        float k = DistanceFactor(position);
        if (k <= 0.01f) return;
        Play(name, volume * k, pitchVariation);
    }

    public static float DistanceFactor(Vector3 position)
    {
        var cam = Camera.main;
        if (cam == null) return 1f;
        float d = Vector2.Distance(cam.transform.position, position);
        return Mathf.Clamp01(1f - Mathf.Max(0f, d - HearingDistance * 0.5f) / (HearingDistance * 0.5f));
    }

    public static void PlayUI(string name, float volume = 1f)
    {
        var c = Clip(name);
        if (c == null) return;
        Ensure();
        ui.pitch = 1f;
        ui.PlayOneShot(c, volume * Volume);
    }

    /// <summary>Starts / stops a looping sound tied to 'owner' (it stops by itself if the owner is destroyed).</summary>
    public static void SetLoop(string name, Object owner, bool on, float volume = 1f)
    {
        if (owner == null) return;
        var key = (owner, name);
        loops.TryGetValue(key, out var entry);
        if (!on)
        {
            if (entry.source != null) entry.source.Stop();
            return;
        }
        var c = Clip(name);
        if (c == null) return;
        Ensure();
        if (entry.source == null)
        {
            entry = (owner, NewSource());
            entry.source.loop = true;
            entry.source.clip = c;
            loops[key] = entry;
        }
        entry.source.volume = volume * Volume;
        if (!entry.source.isPlaying) entry.source.Play();
    }

    // Stops loops whose owner no longer exists (scene change, enemy killed...)
    internal static void CleanLoops()
    {
        if (loops.Count == 0) return;
        List<(Object, string)> dead = null;
        foreach (var kv in loops)
        {
            if (kv.Value.owner != null) continue;
            if (kv.Value.source != null) Object.Destroy(kv.Value.source);
            (dead ??= new List<(Object, string)>()).Add(kv.Key);
        }
        if (dead != null) foreach (var k in dead) loops.Remove(k);
    }
}

public class GameAudioHost : MonoBehaviour
{
    private void LateUpdate() => GameAudio.CleanLoops();
}
