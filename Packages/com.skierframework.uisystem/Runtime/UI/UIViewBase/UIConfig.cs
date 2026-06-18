using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkierFramework
{
    [System.Serializable]
    public class UIConfigJson
    {
        public string uiType;
        public string path;
        public bool isWindow;
        public string uiLayer;
        public bool isAutoNavigation;
    }

    public class UIConfig
    {
        public string path;
        public UIType uiType;
        public UILayer uiLayer;
        public Type viewType;
        public bool isWindow;

        // 资源相对路径，由当前的 IResourceProvider 解释。
        // ResourcesProvider 默认会自动剥离 "Assets/AssetsPackage/" 前缀和扩展名。
        private const string UIConfigPath = "Assets/AssetsPackage/UI/UIConfig.json";

        public static async UniTask<List<UIConfig>> GetAllConfigsAsync()
        {
            var textAsset = await ResourceManager.Instance.LoadAssetAsync<TextAsset>(UIConfigPath);
            if (textAsset == null)
            {
                Debug.LogError("未找到配置：" + UIConfigPath);
                return null;
            }

            var list = new List<UIConfig>();
            var uiConfigs = Newtonsoft.Json.JsonConvert.DeserializeObject<List<UIConfigJson>>(textAsset.text);
            foreach (var config in uiConfigs)
            {
                if (!Enum.TryParse<UILayer>(config.uiLayer, out UILayer layer))
                {
                    layer = UILayer.NormalLayer;
                    Debug.LogErrorFormat("UIConfig.json 中的：{0}  uiLayer解析异常 {1}", config.path, config.uiLayer);
                }
                if (!Enum.TryParse<UIType>(config.uiType, out UIType type))
                {
                    Debug.LogErrorFormat("UIConfig.json 中的：{0}  uiType解析异常 {1}", config.path, config.uiType);
                }
                Type viewType = GetType(config.uiType.ToString());
                if (viewType == null)
                {
                    viewType = GetType($"{typeof(UIConfig).Namespace}.{config.uiType}");
                }
                list.Add(new UIConfig
                {
                    path = config.path,
                    uiLayer = layer,
                    uiType = type,
                    viewType = viewType,
                    isWindow = config.isWindow
                });
            }

            // 配置文件读完之后就可以卸载了
            ResourceManager.Instance.UnLoadAsset(UIConfigPath);
            return list;
        }

        public static Type GetType(string typeName)
        {
            var type = Type.GetType(typeName);
            if (type != null)
            {
                return type;
            }

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (System.Reflection.Assembly assembly in assemblies)
            {
                type = Type.GetType(string.Format("{0}, {1}", typeName, assembly.FullName));
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
