using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CompletSceneValidator
{
    public static void Validate()
    {
        Scene scene = EditorSceneManager.OpenScene("Assets/Scenes/CompletScene.unity", OpenSceneMode.Single);
        int missingScripts = 0;
        List<string> missingPaths = new List<string>();
        int players = 0, balls = 0, chains = 0, cameras = 0, huds = 0;
        Player player = null;
        CameraFollow follow = null;
        SegmentHpBarUI hud = null;

        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            int count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            if (count > 0)
            {
                missingScripts += count;
                missingPaths.Add(GetPath(transform));
            }
            if (transform.TryGetComponent(out Player foundPlayer)) { players++; player = foundPlayer; }
            if (transform.CompareTag("morningstar")) balls++;
            if (transform.TryGetComponent(out ChainLineController _)) chains++;
            if (transform.TryGetComponent(out CameraFollow foundFollow)) { cameras++; follow = foundFollow; }
            if (transform.TryGetComponent(out SegmentHpBarUI foundHud)) { huds++; hud = foundHud; }
        }

        SerializedProperty target = new SerializedObject(follow).FindProperty("_target");
        bool cameraTargetOk = target != null && target.objectReferenceValue == player.transform;
        bool hudPlayerOk = ReferencesObject(hud, player.GetComponent<PlayerHealth>());
        DeathRespawnManager respawn = Object.FindAnyObjectByType<DeathRespawnManager>(FindObjectsInactive.Include);
        bool respawnPlayerOk = ReferencesObject(respawn, player) && ReferencesObject(respawn, player.GetComponent<PlayerHealth>());

        Debug.Log($"[CompletValidation] players={players}, balls={balls}, chains={chains}, cameras={cameras}, huds={huds}, missingScripts={missingScripts}, cameraTarget={cameraTargetOk}, hudPlayerHealth={hudPlayerOk}, respawnPlayer={respawnPlayerOk}");
        Debug.Log($"[CompletGimmicks] breakable={Count<BreakableWall>()}, moving={Count<MovingPlatform>()}, falling={Count<FallingPlatform>()}, hitSwitch={Count<HitSwitch>()}, weightSwitch={Count<WeightSwitch>()}, door={Count<GimmickDoor>()}, magnet={Count<MagnetPoint>()}, spike={Count<SpikeTrap>()}, respawnZone={Count<RespawnZone>()}, checkpoint={Count<Checkpoint>()}, enemy={Count<Enemy>()}, goalPoint={Count<GoalPoint>()}, goalTrigger={Count<GoalTrigger>()}");
        ParallaxBackgroundLayer[] backgrounds = Object.FindObjectsByType<ParallaxBackgroundLayer>(FindObjectsInactive.Include);
        Debug.Log($"[CompletBackgroundValidation] layers={backgrounds.Length}, root={GameObject.Find("BackgroundRoot") != null}, far={GameObject.Find("SkySea") != null}, mid={GameObject.Find("FloatingIslands") != null}, front={GameObject.Find("ForeRuins") != null}");
        foreach (string path in missingPaths)
            Debug.LogError("[CompletValidation] Missing Script: " + path);

        if (players != 1 || balls != 1 || chains != 1 || cameras != 1 || huds != 1
            || missingScripts != 0 || !cameraTargetOk || !hudPlayerOk || !respawnPlayerOk
            || backgrounds.Length != 3)
            throw new System.InvalidOperationException("CompletScene validation failed. See the preceding validation log.");
    }

    private static bool ReferencesObject(Object owner, Object expected)
    {
        if (owner == null || expected == null) return false;
        SerializedObject serialized = new SerializedObject(owner);
        SerializedProperty property = serialized.GetIterator();
        bool enterChildren = true;
        while (property.Next(enterChildren))
        {
            enterChildren = false;
            if (property.propertyType == SerializedPropertyType.ObjectReference
                && property.objectReferenceValue == expected)
                return true;
        }
        return false;
    }

    private static int Count<T>() where T : Object
    {
        return Object.FindObjectsByType<T>(FindObjectsInactive.Include).Length;
    }

    private static string GetPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }
}
