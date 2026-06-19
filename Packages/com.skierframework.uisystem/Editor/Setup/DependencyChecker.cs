using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace SkierFramework.EditorTools
{
    /// <summary>
    /// 自动检测并安装框架依赖（UniTask、PrimeTween）。
    /// 放在独立 asmdef 中，不引用 UniTask/PrimeTween，确保即使依赖缺失也能编译运行。
    /// </summary>
    [InitializeOnLoad]
    public static class DependencyChecker
    {
        private const string UniTaskGitUrl =
            "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask";
        private const string PrimeTweenGitUrl =
            "https://github.com/KyryloKuzyk/PrimeTween.git";

        private const string SessionKey = "SkierFramework.DepCheck.Done";

        static DependencyChecker()
        {
            EditorApplication.delayCall += CheckDependencies;
        }

        private static void CheckDependencies()
        {
            if (SessionState.GetBool(SessionKey, false)) return;
            SessionState.SetBool(SessionKey, true);

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            bool hasUniTask = assemblies.Any(a => a.GetName().Name == "UniTask");
            bool hasPrimeTween = assemblies.Any(a => a.GetName().Name == "PrimeTween.Runtime");

            if (hasUniTask && hasPrimeTween) return;

            string missing = "";
            if (!hasUniTask) missing += "  \u2022 UniTask\n";
            if (!hasPrimeTween) missing += "  \u2022 PrimeTween\n";

            bool install = EditorUtility.DisplayDialog(
                "Skier UI System \u2014 \u7f3a\u5c11\u4f9d\u8d56",
                $"Skier UI System \u9700\u8981\u4ee5\u4e0b\u4f9d\u8d56\u5e93\uff0c\u5f53\u524d\u672a\u68c0\u6d4b\u5230\uff1a\n\n{missing}\n\u662f\u5426\u81ea\u52a8\u901a\u8fc7 Git URL \u5b89\u88c5\uff1f",
                "\u81ea\u52a8\u5b89\u88c5",
                "\u8df3\u8fc7");

            if (!install)
            {
                Debug.LogWarning(
                    "[Skier UI System] \u672a\u5b89\u88c5\u4f9d\u8d56\uff0c\u6846\u67b6\u53ef\u80fd\u65e0\u6cd5\u7f16\u8bd1\u3002" +
                    "\u53ef\u901a\u8fc7\u83dc\u5355 Skier Framework \u2192 Install Dependencies \u91cd\u65b0\u5b89\u88c5\u3002");
                return;
            }

            InstallQueue(hasUniTask, hasPrimeTween);
        }

        /// <summary>
        /// 依次安装缺失的依赖（避免并发修改 manifest.json）
        /// </summary>
        private static void InstallQueue(bool hasUniTask, bool hasPrimeTween)
        {
            if (!hasUniTask)
            {
                AddPackage(UniTaskGitUrl, () => InstallQueue(true, hasPrimeTween));
                return;
            }

            if (!hasPrimeTween)
            {
                AddPackage(PrimeTweenGitUrl, () =>
                {
                    Debug.Log("[Skier UI System] \u6240\u6709\u4f9d\u8d56\u5b89\u88c5\u5b8c\u6210\uff0cUnity \u5c06\u91cd\u65b0\u7f16\u8bd1\u3002");
                });
            }
        }

        /// <summary>
        /// 通过 Git URL 添加包，完成后回调
        /// </summary>
        private static void AddPackage(string url, Action onComplete)
        {
            Debug.Log($"[Skier UI System] \u6b63\u5728\u5b89\u88c5: {url}");
            var request = Client.Add(url);

            EditorApplication.CallbackFunction poller = null;
            poller = () =>
            {
                if (!request.IsCompleted) return;

                EditorApplication.update -= poller;

                if (request.Status == StatusCode.Success)
                {
                    Debug.Log($"[Skier UI System] \u5b89\u88c5\u6210\u529f: {url}");
                }
                else
                {
                    var err = request.Error != null ? request.Error.message : "Unknown error";
                    Debug.LogError($"[Skier UI System] \u5b89\u88c5\u5931\u8d25: {url}\n\u9519\u8bef: {err}");
                }

                onComplete?.Invoke();
            };
            EditorApplication.update += poller;
        }

        /// <summary>
        /// 手动触发依赖检查（菜单入口）
        /// </summary>
        [MenuItem("Skier Framework/Install Dependencies")]
        public static void ManualCheck()
        {
            SessionState.SetBool(SessionKey, false);
            CheckDependencies();
        }
    }
}
