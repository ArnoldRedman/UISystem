using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkierFramework
{
    /// <summary>
    /// 默认资源后端：基于 UnityEngine.Resources。
    ///
    /// 路径处理规则：
    /// - 自动剥离 "Assets/" 前缀
    /// - 自动剥离形如 "AssetsPackage/" 这类常见前缀（可在 <see cref="StripPrefixes"/> 中配置）
    /// - 自动剥离扩展名
    /// - Resources.Load 要求的相对路径必须能在 Assets/.../Resources 下找到
    ///
    /// 例：传入 "Assets/AssetsPackage/UI/Prefabs/UILoginView/UILoginView.prefab"
    ///     实际加载 Resources.Load("UI/Prefabs/UILoginView/UILoginView")
    /// </summary>
    public class ResourcesProvider : IResourceProvider
    {
        /// <summary>
        /// 进行路径裁剪时尝试剥离的前缀（按顺序匹配，命中即停）。
        /// 你可以根据自己项目的目录结构追加，比如 "Res/"。
        /// </summary>
        public string[] StripPrefixes { get; set; } = new[]
        {
            "Assets/AssetsPackage/",
            "Assets/Resources/",
            "Assets/",
            "AssetsPackage/",
            "Resources/",
        };

        public UniTask InitializeAsync()
        {
            // Resources 不需要异步初始化
            return UniTask.CompletedTask;
        }

        public async UniTask<T> LoadAssetAsync<T>(string path, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            string resPath = NormalizePath(path);
            if (string.IsNullOrEmpty(resPath))
            {
                Debug.LogErrorFormat("[ResourcesProvider] 无效路径：{0}", path);
                return null;
            }

            ResourceRequest request = Resources.LoadAsync<T>(resPath);
            await request.WithCancellation(cancellationToken);
            var asset = request.asset as T;
            if (asset == null)
            {
                Debug.LogErrorFormat("[ResourcesProvider] 加载失败：{0} (resolved: {1})", path, resPath);
            }
            return asset;
        }

        public T LoadAsset<T>(string path) where T : UnityEngine.Object
        {
            string resPath = NormalizePath(path);
            if (string.IsNullOrEmpty(resPath))
            {
                return null;
            }
            return Resources.Load<T>(resPath);
        }

        public void ReleaseAsset(string path, UnityEngine.Object asset)
        {
            // Resources 没有引用计数。GameObject 类型不能 UnloadAsset，统一走 UnloadUnusedAssets 时机。
            if (asset == null) return;
            if (asset is GameObject || asset is Component) return;

            try
            {
                Resources.UnloadAsset(asset);
            }
            catch (Exception ex)
            {
                Debug.LogWarningFormat("[ResourcesProvider] UnloadAsset failed：{0}, {1}", path, ex.Message);
            }
        }

        public void ReleaseInstance(GameObject instance)
        {
            if (instance == null) return;
            if (Application.isPlaying)
            {
                GameObject.Destroy(instance);
            }
            else
            {
                GameObject.DestroyImmediate(instance);
            }
        }

        public async UniTask<Scene> LoadSceneAsync(string sceneName, LoadSceneMode mode, CancellationToken cancellationToken = default)
        {
            // Resources 后端走 Unity 默认 SceneManager（场景需要在 Build Settings 中）
            var op = SceneManager.LoadSceneAsync(sceneName, mode);
            await op.WithCancellation(cancellationToken);
            return SceneManager.GetSceneByName(sceneName);
        }

        public async UniTask UnloadSceneAsync(Scene scene, CancellationToken cancellationToken = default)
        {
            if (!scene.IsValid()) return;
            var op = SceneManager.UnloadSceneAsync(scene);
            if (op != null)
            {
                await op.WithCancellation(cancellationToken);
            }
        }

        /// <summary>
        /// 把外部传入的路径标准化为 Resources.Load 能接受的相对路径。
        /// </summary>
        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string p = path.Replace('\\', '/');

            // 剥离前缀
            if (StripPrefixes != null)
            {
                foreach (var prefix in StripPrefixes)
                {
                    if (string.IsNullOrEmpty(prefix)) continue;
                    if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        p = p.Substring(prefix.Length);
                        break;
                    }
                }
            }

            // 剥离扩展名
            string ext = Path.GetExtension(p);
            if (!string.IsNullOrEmpty(ext))
            {
                p = p.Substring(0, p.Length - ext.Length);
            }
            return p;
        }
    }
}
