using UnityEngine;

// In a normal (non-development) build, ordinary Debug.Log messages are skipped: the game has many of them and
// writing them to the log file every frame costs performance. Warnings and errors are still written.
public static class BuildLogFilter
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        if (!Application.isEditor && !Debug.isDebugBuild) Debug.unityLogger.filterLogType = LogType.Warning;
    }
}
