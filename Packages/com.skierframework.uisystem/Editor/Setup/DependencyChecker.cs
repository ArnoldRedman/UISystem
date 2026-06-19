using System;
using System.IO;
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

        private const string PrimeTweenPackageName = "com.kyrylokuzyk.primetween";
        private const string PrimeTweenRegistryUrl = "https://registry.npmjs.org";
        private const string PrimeTweenRegistryName = "PrimeTween";
        private const string PrimeTweenScope = "com.kyrylokuzyk";

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
                $"Skier UI System \u9700\u8981\u4ee5\u4e0b\u4f9d\u8d56\u5e93\uff0c\u5f53\u524d\u672a\u68c0\u6d4b\u5230\uff1a\n\n{missing}\n\u662f\u5426\u81ea\u52a8\u5b89\u88c5\uff1f",
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
                AddPackageFromGit(UniTaskGitUrl, () => InstallQueue(true, hasPrimeTween));
                return;
            }

            if (!hasPrimeTween)
            {
                EnsurePrimeTweenRegistry(() =>
                {
                    AddPackageFromRegistry(PrimeTweenPackageName, () =>
                    {
                        Debug.Log("[Skier UI System] \u6240\u6709\u4f9d\u8d56\u5b89\u88c5\u5b8c\u6210\uff0cUnity \u5c06\u91cd\u65b0\u7f16\u8bd1\u3002");
                    });
                });
            }
        }

        /// <summary>
        /// 通过 Git URL 添加包，完成后回调
        /// </summary>
        private static void AddPackageFromGit(string url, Action onComplete)
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
        /// 通过 scoped registry 添加包，完成后回调
        /// </summary>
        private static void AddPackageFromRegistry(string packageName, Action onComplete)
        {
            Debug.Log($"[Skier UI System] \u6b63\u5728\u5b89\u88c5: {packageName}");
            var request = Client.Add(packageName);

            EditorApplication.CallbackFunction poller = null;
            poller = () =>
            {
                if (!request.IsCompleted) return;

                EditorApplication.update -= poller;

                if (request.Status == StatusCode.Success)
                {
                    Debug.Log($"[Skier UI System] \u5b89\u88c5\u6210\u529f: {packageName}");
                }
                else
                {
                    var err = request.Error != null ? request.Error.message : "Unknown error";
                    Debug.LogError($"[Skier UI System] \u5b89\u88c5\u5931\u8d25: {packageName}\n\u9519\u8bef: {err}");
                }

                onComplete?.Invoke();
            };
            EditorApplication.update += poller;
        }

        /// <summary>
        /// 确保 manifest.json 中包含 PrimeTween 的 scoped registry 配置
        /// </summary>
        private static void EnsurePrimeTweenRegistry(Action onComplete)
        {
            string manifestPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages", "manifest.json");

            if (!File.Exists(manifestPath))
            {
                Debug.LogError("[Skier UI System] manifest.json \u4e0d\u5b58\u5728: " + manifestPath);
                onComplete?.Invoke();
                return;
            }

            string json = File.ReadAllText(manifestPath);

            // 检查是否已包含 PrimeTween 的 scoped registry
            if (json.Contains(PrimeTweenRegistryUrl))
            {
                onComplete?.Invoke();
                return;
            }

            Debug.Log("[Skier UI System] \u6b63\u5728\u6dfb\u52a0 PrimeTween scoped registry...");

            string newJson = AddScopedRegistryToJson(json, PrimeTweenRegistryName, PrimeTweenRegistryUrl, PrimeTweenScope);
            File.WriteAllText(manifestPath, newJson);

            Debug.Log("[Skier UI System] scoped registry \u5df2\u6dfb\u52a0\uff0c\u7b49\u5f85 Unity \u5237\u65b0\u540e\u7ee7\u7eed\u5b89\u88c5...");

            // 等待 Unity 处理 manifest.json 变更后再继续
            EditorApplication.delayCall += () =>
            {
                // 再等一帧确保 registry 已生效
                EditorApplication.delayCall += () => onComplete?.Invoke();
            };
        }

        /// <summary>
        /// 手动往 manifest.json JSON 文本中插入 scopedRegistry 条目
        /// </summary>
        private static string AddScopedRegistryToJson(string json, string name, string url, string scope)
        {
            // 简单但可靠的方式：用 Newtonsoft 如果可用，否则手动插入
            // 这里用手动插入，因为 Setup asmdef 不引用 Newtonsoft

            string entry = $"    {{\n      \"name\": \"{name}\",\n      \"url\": \"{url}\",\n      \"scopes\": [\"{scope}\"]\n    }}";

            // 检查是否已有 scopedRegistries 数组
            int regIdx = json.IndexOf("\"scopedRegistries\"");
            if (regIdx < 0)
            {
                // 没有 scopedRegistries，在 dependencies 之前插入
                int depIdx = json.IndexOf("\"dependencies\"");
                if (depIdx < 0) return json;

                // 找到 dependencies 前面的 { 位置
                int braceIdx = json.LastIndexOf('{', depIdx);
                string insertion = $"  \"scopedRegistries\": [\n{entry}\n  ],\n";
                return json.Substring(0, braceIdx + 1) + "\n" + insertion + json.Substring(braceIdx + 1);
            }
            else
            {
                // 已有 scopedRegistries 数组，在数组末尾 ] 之前插入
                int arrStart = json.IndexOf('[', regIdx);
                int arrEnd = json.IndexOf(']', arrStart);

                // 检查数组是否为空
                string arrContent = json.Substring(arrStart + 1, arrEnd - arrStart - 1).Trim();
                if (string.IsNullOrEmpty(arrContent))
                {
                    // 空数组
                    return json.Substring(0, arrStart + 1) + "\n" + entry + "\n" + json.Substring(arrEnd);
                }
                else
                {
                    // 非空数组，在最后一个 } 后加逗号和新条目
                    int lastBrace = json.LastIndexOf('}', arrEnd);
                    return json.Substring(0, lastBrace + 1) + ",\n" + entry + json.Substring(lastBrace + 1);
                }
            }
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
