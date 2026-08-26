using UnityEngine;

/// <summary>
/// シーン共通の2D効果音Sourceを解決し、破棄される対象の効果音も最後まで再生する。
/// </summary>
public static class OneShotAudioUtility
{
    public const string WorldImpactSourceName = "WorldImpactSfx";

    public static AudioSource FindWorldImpactSource()
    {
        GameObject sourceObject = GameObject.Find(WorldImpactSourceName);
        return sourceObject != null ? sourceObject.GetComponent<AudioSource>() : null;
    }

    public static bool Play2D(AudioSource preferredSource, AudioClip clip, float volume, Vector3 worldPosition)
    {
        if (clip == null)
            return false;

        if (preferredSource != null)
        {
            preferredSource.PlayOneShot(clip, volume);
            return true;
        }

        GameObject oneShotObject = new GameObject($"OneShotAudio_{clip.name}");
        oneShotObject.transform.position = worldPosition;

        AudioSource transientSource = oneShotObject.AddComponent<AudioSource>();
        transientSource.playOnAwake = false;
        transientSource.loop = false;
        transientSource.spatialBlend = 0f;
        transientSource.volume = 1f;
        transientSource.PlayOneShot(clip, volume);

        Object.Destroy(oneShotObject, Mathf.Max(0.1f, clip.length + 0.1f));
        return true;
    }
}
