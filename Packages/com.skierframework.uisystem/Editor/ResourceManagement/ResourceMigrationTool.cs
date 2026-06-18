using System.IO;
using UnityEditor;
using UnityEngine;

namespace SkierFramework.EditorTools
{
    /// <summary>
    /// 把 Assets/AssetsPackage 下的资源迁移到 Assets/Resources 下。
    ///
    /// 之所以需要这个工具：默认的 <see cref="ResourcesProvider"/> 走 UnityEngine.Resources，
    /// 而 Resources.Load 要求资源必须放在某个 "Resources" 文件夹下。
    /// 老项目原本是用 Addressables，资源都放在 Assets/AssetsPackage，所以做一次性迁移。
    ///
    /// 用法：
    ///   Tools / SkierFramework / Migrate AssetsPackage to Resources
    ///
    /// 脚本会：
    ///   1. 把 Assets/AssetsPackage 下的所有内容移动到 Assets/Resources（保持子目录结构）
    ///   2. 更新 UIConfig.json 里的 path（Assets/AssetsPackage/... -> Assets/Resources/...）
    ///   3. 刷新 AssetDatabase
    /// </summary>
    public static class ResourceMigrationTool
    {
        private const string SourceRoot = "Assets/AssetsPackage";
        private const string TargetRoot = "Assets/Resources";
        private const string UIConfigRelative = "UI/UIConfig.json";

        [MenuItem("Tools/SkierFramework/Migrate AssetsPackage to Resources")]
        public static void Migrate()
        {
            if (!Directory.Exists(SourceRoot))
            {
                EditorUtility.DisplayDialog("Migrate", $"未找到目录：{SourceRoot}，无需迁移。", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog(
                    "确认迁移",
                    $"将把 {SourceRoot} 下的所有资源移动到 {TargetRoot}，并更新 UIConfig.json 内的路径。\n\n建议先备份/提交一次代码再操作。",
                    "继续", "取消"))
            {
                return;
            }

            try
            {
                AssetDatabase.StartAssetEditing();

                // 1. 确保目标根目录存在
                if (!AssetDatabase.IsValidFolder(TargetRoot))
                {
                    AssetDatabase.CreateFolder("Assets", "Resources");
                }

                // 2. 递归移动 SourceRoot 下的子目录/文件
                MoveDirectoryContents(SourceRoot, TargetRoot);

                // 3. 删除空的源目录
                if (Directory.Exists(SourceRoot) && IsDirectoryEmpty(SourceRoot))
                {
                    AssetDatabase.DeleteAsset(SourceRoot);
                }

                // 4. 更新 UIConfig.json
                string configPath = Path.Combine(TargetRoot, UIConfigRelative).Replace('\\', '/');
                if (File.Exists(configPath))
                {
                    string text = File.ReadAllText(configPath);
                    text = text.Replace(SourceRoot + "/", TargetRoot + "/");
                    File.WriteAllText(configPath, text);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[ResourceMigration] 迁移完成：{SourceRoot} -> {TargetRoot}");
            EditorUtility.DisplayDialog("Migrate", "迁移完成，请检查 Resources 目录与 UIConfig.json。", "OK");
        }

        private static void MoveDirectoryContents(string fromDir, string toDir)
        {
            if (!Directory.Exists(fromDir)) return;

            // 处理直接子项
            foreach (var sub in Directory.GetDirectories(fromDir))
            {
                string name = Path.GetFileName(sub);
                string target = Path.Combine(toDir, name).Replace('\\', '/');

                if (!AssetDatabase.IsValidFolder(target))
                {
                    // 目标目录不存在 → 直接移动整个文件夹
                    string error = AssetDatabase.MoveAsset(sub.Replace('\\', '/'), target);
                    if (!string.IsNullOrEmpty(error))
                    {
                        Debug.LogErrorFormat("[ResourceMigration] MoveAsset 失败：{0} -> {1}, error={2}", sub, target, error);
                    }
                }
                else
                {
                    // 目标目录存在 → 递归合并
                    MoveDirectoryContents(sub.Replace('\\', '/'), target);
                    if (Directory.Exists(sub) && IsDirectoryEmpty(sub))
                    {
                        AssetDatabase.DeleteAsset(sub.Replace('\\', '/'));
                    }
                }
            }

            foreach (var file in Directory.GetFiles(fromDir))
            {
                if (file.EndsWith(".meta")) continue;

                string name = Path.GetFileName(file);
                string target = Path.Combine(toDir, name).Replace('\\', '/');
                string error = AssetDatabase.MoveAsset(file.Replace('\\', '/'), target);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogErrorFormat("[ResourceMigration] MoveAsset 失败：{0} -> {1}, error={2}", file, target, error);
                }
            }
        }

        private static bool IsDirectoryEmpty(string path)
        {
            // 忽略 .meta 文件
            foreach (var f in Directory.GetFiles(path))
            {
                if (!f.EndsWith(".meta")) return false;
            }
            foreach (var d in Directory.GetDirectories(path))
            {
                if (!IsDirectoryEmpty(d)) return false;
            }
            return true;
        }
    }
}
