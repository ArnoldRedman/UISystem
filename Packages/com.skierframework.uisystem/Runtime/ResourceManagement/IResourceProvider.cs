using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkierFramework
{
    /// <summary>
    /// 资源后端抽象接口。
    ///
    /// 框架本身只依赖这个接口，具体后端（Resources / YooAsset / Addressables 等）
    /// 通过 <see cref="ResourceManager.SetProvider"/> 在运行时注入。
    ///
    /// 注意：
    /// - path 的语义由具体 Provider 解释（比如 ResourcesProvider 走 Resources 相对路径，
    ///   YooAssetProvider 走 location）。
    /// - 所有异步方法都基于 UniTask，避免对 Addressables 的强依赖。
    /// </summary>
    public interface IResourceProvider
    {
        /// <summary>
        /// 后端初始化。无需初始化的实现（如 Resources）直接返回。
        /// </summary>
        UniTask InitializeAsync();

        /// <summary>
        /// 异步加载单个资源。
        /// </summary>
        UniTask<T> LoadAssetAsync<T>(string path, CancellationToken cancellationToken = default) where T : UnityEngine.Object;

        /// <summary>
        /// 同步加载（部分后端可能不支持，需自行处理）。
        /// </summary>
        T LoadAsset<T>(string path) where T : UnityEngine.Object;

        /// <summary>
        /// 释放某个资源（如果后端有引用计数，则减一；ResourcesProvider 实现为 no-op）。
        /// </summary>
        void ReleaseAsset(string path, UnityEngine.Object asset);

        /// <summary>
        /// 释放某个由该 Provider 实例化出来的对象。
        /// 默认实现可以是 GameObject.Destroy，YooAsset 等可走自身释放接口。
        /// </summary>
        void ReleaseInstance(GameObject instance);

        /// <summary>
        /// 加载场景。Resources 后端只支持 Build Settings 中已添加的场景。
        /// </summary>
        UniTask<Scene> LoadSceneAsync(string sceneName, LoadSceneMode mode, CancellationToken cancellationToken = default);

        /// <summary>
        /// 卸载场景。
        /// </summary>
        UniTask UnloadSceneAsync(Scene scene, CancellationToken cancellationToken = default);
    }
}
