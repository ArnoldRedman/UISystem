using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SkierFramework
{
    /// <summary>
    /// 资源管理门面（Facade）。
    ///
    /// 框架对外只依赖这个类，背后的资源后端通过 <see cref="IResourceProvider"/> 抽象，
    /// 默认使用 <see cref="ResourcesProvider"/>。如果你想接入 YooAsset / Addressables，
    /// 实现一个自己的 <see cref="IResourceProvider"/>，在 App 启动早期调用 <see cref="SetProvider"/> 即可。
    ///
    /// 这个类只负责：
    ///   - 引用计数（每个 path 一个）
    ///   - 常驻资源标记
    ///   - GameObject 实例池（InstancePool）
    ///   - SpriteAtlas 缓存
    ///   - 资源缓存（避免重复异步加载）
    ///
    /// 真正“去拿资源 / 释放资源 / 加载场景”都委托给 IResourceProvider。
    /// </summary>
    public class ResourceManager : Singleton<ResourceManager>
    {
        private IResourceProvider _provider;

        // 已加载的资源缓存：path -> asset
        private readonly Dictionary<string, UnityEngine.Object> _assetCaches = new Dictionary<string, UnityEngine.Object>();

        // 正在加载中的任务，避免并发重复加载
        private readonly Dictionary<string, UniTask<UnityEngine.Object>> _loadingTasks = new Dictionary<string, UniTask<UnityEngine.Object>>();

        // 常驻资源
        private readonly HashSet<string> _residentAssetsHashSet = new HashSet<string>();

        // 引用计数
        private readonly Dictionary<string, int> _loadedAssetInstanceCountDic = new Dictionary<string, int>();

        // 实例对象 -> 资源 path
        private readonly Dictionary<int, string> _objectInstanceIdKeyDic = new Dictionary<int, string>();

        private InstancePool _instancePool;
        private int _loadingAssetCount = 0;

        public bool IsProcessLoading => _loadingAssetCount > 0;

        public IResourceProvider Provider => _provider;

        public override void OnInitialize()
        {
            base.OnInitialize();
            _instancePool = new InstancePool();
            // 默认 Provider：基于 UnityEngine.Resources
            _provider ??= new ResourcesProvider();
        }

        /// <summary>
        /// 注入资源后端。建议在框架启动最早期调用（比如 Launcher 里）。
        /// </summary>
        public void SetProvider(IResourceProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        #region 初始化/清除
        public UniTask InitializeAsync()
        {
            return _provider.InitializeAsync();
        }

        /// <summary>
        /// 清除所有非常驻资源。等待当前所有加载任务结束后再清。
        /// </summary>
        public async UniTask CleanupAsync()
        {
            await UniTask.WaitUntil(() => !IsProcessLoading);
            Cleanup();
        }

        public void Cleanup()
        {
            var keysToRemove = ListPool<string>.Get();
            foreach (var kv in _assetCaches)
            {
                if (!_residentAssetsHashSet.Contains(kv.Key))
                {
                    keysToRemove.Add(kv.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                if (_assetCaches.TryGetValue(key, out var asset))
                {
                    _provider.ReleaseAsset(key, asset);
                }
                if (_spriteCache.TryGetValue(key, out SpriteAtlas spriteAtlas))
                {
                    spriteAtlas.Cleanup();
                    _spriteCache.Remove(key);
                }
                _assetCaches.Remove(key);
                _loadedAssetInstanceCountDic.Remove(key);
                _instancePool.Clear(key);
            }
            ListPool<string>.Release(keysToRemove);
        }

        public void AddResidentAsset(string key)
        {
            _residentAssetsHashSet.Add(key);
        }
        #endregion

        #region 实例化和回收对象
        /// <summary>
        /// 异步实例化一个 GameObject。会自动加载资源、走 InstancePool 复用。
        /// </summary>
        public async UniTask<GameObject> InstantiateAsync(string path, bool active = true, CancellationToken cancellationToken = default)
        {
            var prefab = await LoadAssetAsync<GameObject>(path, cancellationToken);
            return InternalInstantiate(path, prefab, active);
        }

        /// <summary>
        /// 兼容旧的回调式调用。
        /// </summary>
        public void InstantiateAsync(string path, Action<GameObject> callback, bool active = true)
        {
            InstantiateAsync(path, active).ContinueWith(go => callback?.Invoke(go)).Forget();
        }

        public void Recycle(GameObject instanceObject, bool forceDestroy = false)
        {
            if (instanceObject == null) return;

            int id = instanceObject.GetInstanceID();
            if (_objectInstanceIdKeyDic.TryGetValue(id, out string path))
            {
                _instancePool.Recycle(path, instanceObject, forceDestroy);
                if (_loadedAssetInstanceCountDic.ContainsKey(path))
                {
                    _loadedAssetInstanceCountDic[path]--;
                }
                _objectInstanceIdKeyDic.Remove(id);
            }
            else
            {
                Debug.LogErrorFormat("此模块不回收不是从这个模块实例化出去的对象：{0}", instanceObject.name);
                GameObject.Destroy(instanceObject);
            }
        }

        private GameObject InternalInstantiate(string path, GameObject prefab, bool active)
        {
            GameObject reused = _instancePool.Get(path);
            GameObject result = null;

            if (reused == null)
            {
                if (prefab != null)
                {
                    result = GameObject.Instantiate(prefab);
                }
            }
            else
            {
                result = reused;
            }

            if (result != null)
            {
                _instancePool.InitInst(result, active);
                _objectInstanceIdKeyDic[result.GetInstanceID()] = path;
                if (!_loadedAssetInstanceCountDic.ContainsKey(path))
                {
                    _loadedAssetInstanceCountDic[path] = 0;
                }
                _loadedAssetInstanceCountDic[path]++;
            }
            return result;
        }
        #endregion

        #region 资源加载/卸载
        /// <summary>
        /// 异步加载资源（带缓存）。
        /// </summary>
        public async UniTask<T> LoadAssetAsync<T>(string path, CancellationToken cancellationToken = default) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(path)) return null;

            // 命中缓存
            if (_assetCaches.TryGetValue(path, out var cached))
            {
                return cached as T;
            }

            // 命中正在进行中的加载
            if (_loadingTasks.TryGetValue(path, out var existing))
            {
                var existingResult = await existing;
                return existingResult as T;
            }

            _loadingAssetCount++;
            if (!_loadedAssetInstanceCountDic.ContainsKey(path))
            {
                _loadedAssetInstanceCountDic[path] = 1;
            }

            var task = LoadAssetInternal<T>(path, cancellationToken);
            _loadingTasks[path] = task;

            try
            {
                var asset = await task;
                if (asset != null)
                {
                    _assetCaches[path] = asset;
                }
                return asset as T;
            }
            finally
            {
                _loadingTasks.Remove(path);
                _loadingAssetCount--;
            }
        }

        private async UniTask<UnityEngine.Object> LoadAssetInternal<T>(string path, CancellationToken cancellationToken) where T : UnityEngine.Object
        {
            return await _provider.LoadAssetAsync<T>(path, cancellationToken);
        }

        /// <summary>
        /// 兼容旧的回调式调用。
        /// </summary>
        public void LoadAssetAsync<T>(string path, Action<T> onComplete, bool autoUnload = false) where T : UnityEngine.Object
        {
            LoadAssetAsyncCallback(path, onComplete, autoUnload).Forget();
        }

        private async UniTaskVoid LoadAssetAsyncCallback<T>(string path, Action<T> onComplete, bool autoUnload) where T : UnityEngine.Object
        {
            var asset = await LoadAssetAsync<T>(path);
            onComplete?.Invoke(asset);
            if (autoUnload)
            {
                UnLoadAsset(path);
            }
        }

        public void UnLoadAsset(string path)
        {
            if (_residentAssetsHashSet.Contains(path))
            {
                Debug.LogErrorFormat("[UnLoadAsset] 禁止卸载常驻资源：{0} ！", path);
                return;
            }

            if (_assetCaches.TryGetValue(path, out var asset))
            {
                if (_spriteCache.TryGetValue(path, out SpriteAtlas spriteAtlas))
                {
                    spriteAtlas.Cleanup();
                    _spriteCache.Remove(path);
                }
                _assetCaches.Remove(path);
                _loadedAssetInstanceCountDic.Remove(path);
                _provider.ReleaseAsset(path, asset);
            }
            else if (!_loadingTasks.ContainsKey(path))
            {
                Debug.LogErrorFormat("[UnLoadAsset] 卸载未加载资源：{0} ！", path);
            }
        }

        /// <summary>
        /// 释放引用，引用为 0 时自动卸载。
        /// </summary>
        public void ReleaseRef(string path)
        {
            if (_loadedAssetInstanceCountDic.TryGetValue(path, out int count))
            {
                _loadedAssetInstanceCountDic[path] = --count;
                if (count <= 0)
                {
                    UnLoadAsset(path);
                }
            }
        }
        #endregion

        #region 预加载
        public UniTask<T> PreLoadAssetAsync<T>(string path) where T : UnityEngine.Object
        {
            return LoadAssetAsync<T>(path);
        }

        public bool TryGetAsset<T>(string path, out T target) where T : UnityEngine.Object
        {
            target = null;
            if (_assetCaches.TryGetValue(path, out var asset))
            {
                target = asset as T;
                return target != null;
            }
            return false;
        }
        #endregion

        #region 图片加载
        /// <summary>
        /// SpriteAtlas.GetSprite() 会 clone，需要缓存避免重复 clone 和泄漏。
        /// </summary>
        private class SpriteAtlas
        {
            public UnityEngine.U2D.SpriteAtlas spriteAtlas;
            private readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>();

            public Sprite Get(string name)
            {
                if (!_spriteCache.TryGetValue(name, out Sprite sprite))
                {
                    sprite = spriteAtlas.GetSprite(name);
                    _spriteCache.Add(name, sprite);
                }
                return sprite;
            }

            public void Cleanup()
            {
                foreach (var sprite in _spriteCache.Values)
                {
                    GameObject.Destroy(sprite);
                }
                _spriteCache.Clear();
            }
        }
        private readonly Dictionary<string, SpriteAtlas> _spriteCache = new Dictionary<string, SpriteAtlas>();

        public async UniTask<Sprite> LoadSpriteAsync(string atlasPath, string spriteName)
        {
            if (string.IsNullOrEmpty(atlasPath) || string.IsNullOrEmpty(spriteName))
            {
                Debug.LogErrorFormat("[LoadSpriteAsync] error：atlasPath = {0}, spriteName = {1}！", atlasPath, spriteName);
                return null;
            }

            if (_spriteCache.TryGetValue(atlasPath, out var atlas))
            {
                return atlas.Get(spriteName);
            }

            var spriteAtlas = await LoadAssetAsync<UnityEngine.U2D.SpriteAtlas>(atlasPath);
            if (spriteAtlas == null)
            {
                Debug.LogErrorFormat("[LoadSpriteAsync] load failed：atlasPath = {0}！", atlasPath);
                return null;
            }

            // 加载完毕后再次检查缓存（并发场景）
            if (_spriteCache.TryGetValue(atlasPath, out atlas))
            {
                return atlas.Get(spriteName);
            }
            atlas = new SpriteAtlas { spriteAtlas = spriteAtlas };
            _spriteCache.Add(atlasPath, atlas);
            return atlas.Get(spriteName);
        }

        public void LoadSpriteAsync(string atlasPath, string spriteName, Action<Sprite> callback)
        {
            LoadSpriteAsync(atlasPath, spriteName).ContinueWith(s => callback?.Invoke(s)).Forget();
        }
        #endregion

        #region 场景加载
        public UniTask<Scene> LoadSceneAsync(string name, LoadSceneMode loadMode = LoadSceneMode.Single, CancellationToken cancellationToken = default)
        {
            return _provider.LoadSceneAsync(name, loadMode, cancellationToken);
        }

        public UniTask UnloadSceneAsync(Scene scene, CancellationToken cancellationToken = default)
        {
            return _provider.UnloadSceneAsync(scene, cancellationToken);
        }
        #endregion

        #region Text读取
        public async UniTask<string> ReadTextStringAsync(string path)
        {
            var text = await LoadAssetAsync<TextAsset>(path);
            if (text == null)
            {
                Debug.LogErrorFormat("[ReadTextStringAsync] load failed：path = {0}！", path);
                return string.Empty;
            }
            string result = text.text;
            UnLoadAsset(path);
            return result;
        }

        public async UniTask<byte[]> ReadTextBytesAsync(string path)
        {
            var text = await LoadAssetAsync<TextAsset>(path);
            if (text == null)
            {
                Debug.LogErrorFormat("[ReadTextBytesAsync] load failed：path = {0}！", path);
                return null;
            }
            byte[] result = text.bytes;
            UnLoadAsset(path);
            return result;
        }
        #endregion

        #region Debug
        public void PrintState()
        {
            foreach (var item in _loadedAssetInstanceCountDic)
            {
                Debug.LogFormat("Asset Key: {0}, Count: {1}", item.Key, item.Value);
            }
        }
        #endregion
    }
}
