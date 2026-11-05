using UnityEngine;

/// <summary>Clear milestones and completed cartridge reveals. Pickup jewels are deliberately unrelated.</summary>
public static class StageSelectProgress
{
    private const string Prefix = "IronBallGirl.StageSelect.";
#if UNITY_EDITOR
    // Editor probes use an isolated namespace and restore it afterwards; normal saves are never reset by tests.
    public static string TestNamespace { get; set; }
#endif
    private static string Key(string kind, string scene)
    {
#if UNITY_EDITOR
        return Prefix + TestNamespace + kind + scene;
#else
        return Prefix + kind + scene;
#endif
    }
    public static bool IsCleared(string scene) => string.IsNullOrEmpty(scene) || PlayerPrefs.GetInt(Key("Clear.", scene), 0) != 0;
    public static bool RevealCompleted(string scene) => PlayerPrefs.GetInt(Key("Reveal.", scene), 0) != 0;
    public static void RecordClear(string scene)
    {
        if (string.IsNullOrEmpty(scene) || IsCleared(scene)) return;
        PlayerPrefs.SetInt(Key("Clear.", scene), 1); PlayerPrefs.Save();
    }
    public static void CompleteReveal(string scene) { PlayerPrefs.SetInt(Key("Reveal.", scene), 1); PlayerPrefs.Save(); }
#if UNITY_EDITOR
    public static void ClearTestData(string scene)
    {
        if (string.IsNullOrEmpty(TestNamespace)) throw new System.InvalidOperationException("Only isolated probe data can be deleted.");
        PlayerPrefs.DeleteKey(Key("Clear.", scene)); PlayerPrefs.DeleteKey(Key("Reveal.", scene)); PlayerPrefs.Save();
    }
#endif
}
