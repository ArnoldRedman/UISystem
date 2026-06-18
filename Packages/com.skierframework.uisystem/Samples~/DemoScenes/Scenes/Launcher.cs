using Cysharp.Threading.Tasks;
using SkierFramework;
using System;
using UnityEngine;

public class Launcher : MonoBehaviour
{
    public GameObject Splash;

    void Start()
    {
        if (Splash == null)
        {
            Splash = GameObject.Find(nameof(Splash));
        }

        StartAsync().Forget();
    }

    private async UniTaskVoid StartAsync()
    {
        // 如果你要使用其他资源后端（YooAsset、Addressables 等），在这里调用：
        // ResourceManager.Instance.SetProvider(new YourCustomProvider());

        await ResourceManager.Instance.InitializeAsync();

        UIManager.Instance.Initialize();

        await UIManager.Instance.InitUIConfig();

        await UIManager.Instance.Preload(UIType.UILoadingView);

        Loading.Instance.StartLoading(EnterGameAsync);

        if (Splash != null)
        {
            Splash.SetActive(false);
        }
    }

    private async UniTask EnterGameAsync(Action<float, string> loadingRefresh)
    {
        loadingRefresh?.Invoke(0.3f, "loading..........1");
        await UniTask.Delay(TimeSpan.FromSeconds(0.5));

        loadingRefresh?.Invoke(0.6f, "loading..........2");
        await UniTask.Delay(TimeSpan.FromSeconds(0.5));

        loadingRefresh?.Invoke(1, "loading..........3");
        await UniTask.Delay(TimeSpan.FromSeconds(0.5));

        UIManager.Instance.Open(UIType.UILoginView);
    }
}
