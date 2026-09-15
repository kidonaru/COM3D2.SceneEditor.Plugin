#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public class CreateAssetBundles
{
    [MenuItem("Assets/Build Bundle")]
    static void BuildAllAssetBundles()
    {
        string assetBundleDirectory = "Assets/Bundles";
        BuildPipeline.BuildAssetBundles(assetBundleDirectory, BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows);
        AssetDatabase.Refresh();

        Debug.Log("Asset bundles created at " + assetBundleDirectory);

        EditorUtility.DisplayDialog(
            "ビルド完了",
            "AssetBundleの作成が完了しました。\n保存先: " + assetBundleDirectory,
            "OK"
        );
    }

    // batchmode 用（ダイアログ表示なし）
    static void BuildAllAssetBundlesBatch()
    {
        string assetBundleDirectory = "Assets/Bundles";
        BuildPipeline.BuildAssetBundles(assetBundleDirectory, BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows);
        Debug.Log("Asset bundles created at " + assetBundleDirectory);
    }
}
#endif
