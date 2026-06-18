using Cysharp.Threading.Tasks;
using System;
using UnityEngine;

namespace SkierFramework
{
    public class LoadingData
    {
        public LoadingFunc loadingFunc;
        public bool isCleanupAsset = false;
    }

    public delegate UniTask LoadingFunc(Action<float, string> loadingRefresh);

    /// <summary>
    /// 实际游戏中的loading
    /// </summary>
    public class Loading : SingletonMono<Loading>
    {
        private LoadingData _loadingData;
        private bool _isRunning;

        public void StartLoading(LoadingFunc loadingFunc, bool isCleanupAsset = false)
        {
            StartLoading(new LoadingData { loadingFunc = loadingFunc, isCleanupAsset = isCleanupAsset });
        }

        public void StartLoading(LoadingData loadingData)
        {
            //开启UI
            UIManager.Instance.Open(UIType.UILoadingView);

            if (loadingData.loadingFunc != null)
            {
                _loadingData = loadingData;
                if (_isRunning)
                {
                    // 上一个 loading 还在跑，让它自然结束
                    return;
                }
                RunLoadingAsync().Forget();
            }
            else
            {
                Debug.LogError("加载错误,没有参数LoadingData！");
            }
        }

        private async UniTaskVoid RunLoadingAsync()
        {
            _isRunning = true;
            try
            {
                await _loadingData.loadingFunc(RefreshLoading);

                if (_loadingData != null && _loadingData.isCleanupAsset)
                {
                    await ResourceManager.Instance.CleanupAsync();
                    var op = Resources.UnloadUnusedAssets();
                    if (op != null)
                    {
                        await op;
                    }
                }

                Pool.ReleaseAll();
                await UniTask.Yield();

                GC.Collect();
                await UniTask.Yield();

                Exit();
            }
            finally
            {
                _isRunning = false;
            }
        }

        private void RefreshLoading(float loading, string desc)
        {
            // 刷新
            var view = UIManager.Instance.GetView<UILoadingView>(UIType.UILoadingView);
            if (view != null)
            {
                view.SetLoading(loading, desc);
            }
            if (!string.IsNullOrEmpty(desc))
            {
                Debug.Log(desc);
            }
        }

        private void Exit()
        {
            // 关闭UI
            UIManager.Instance.Close(UIType.UILoadingView);

            ObjectPool<LoadingData>.Release(_loadingData);
            _loadingData = null;
        }
    }
}
