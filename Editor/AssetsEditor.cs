using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Balancy.Editor;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.Networking;

namespace Balancy
{
    [InitializeOnLoad]
    public class AssetsEditor : EditorWindow
    {
        private const string BalancyDataRoot = "Library/BalancyData/";
        private const string CustomProfileName = "BalancyProfile";
        private const string RemoteBuildPath = BalancyDataRoot + "[BuildTarget]"; // Where assets will be built
        private const string RemoteLoadPath = "{BALANCY_URL}/"; // Where assets will be loaded from

        private bool _section2Expanded = true;

        // private bool _section3Expanded = false;
        private string _privateKey;

        private void Awake()
        {
            minSize = new Vector2(500, 500);
        }

        static AssetsEditor()
        {
            Balancy_Editor.SynchAddressablesEvent -= SynchAddressables;
            Balancy_Editor.SynchAddressablesEvent += SynchAddressables;
        }

        private static void SynchAddressables(Balancy_EditorAuth editorAuth, string gameid, string token, int branchid, string branchName,
            Action<string, float> onprogress, Action onstart, Action<string> oncomplete)
        {
            var window = GetWindow<AssetsEditor>("Balancy Addressables Manager");
            window.Init(editorAuth, gameid, token, branchid, branchName, onprogress, onstart, oncomplete);
        }

        private Balancy_EditorAuth _editorAuth;
        private BalancyS2SWrapper _wrapper;

        private void Init(Balancy_EditorAuth editorAuth, string gameId, string token, int branchId, string branchName,
            Action<string, float> onProgress, Action onStart, Action<string> onComplete)
        {
            _editorAuth = editorAuth;
            _privateKey = _editorAuth.GetPrivateKey();

            _gameInfo = new GameInfo
            {
                GameId = gameId,
                Token = token,
                BranchId = branchId,
                BranchName = branchName,
                OnProgress = onProgress,
                OnComplete = onComplete,
                OnStart = onStart
            };
        }

        private void OnGUI()
        {
            bool isBalancyProfileActive = RenderSetup();

            EditorGUILayout.Space(10);
            bool disableUI = !isBalancyProfileActive;
            RenderPrivateKey(2, ref disableUI);

            EditorGUILayout.Space(10);
            RenderBundleGroups(3, ref disableUI);

            EditorGUILayout.Space(10);
            RenderDeploy(4, ref disableUI);
        }

        bool RenderSetup()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Step 1: Addressables Profile Setup", EditorStyles.boldLabel);

            bool isBalancyProfileActive = AssetsEditor.IsCustomProfileActive();

            if (isBalancyProfileActive)
            {
                EditorGUILayout.HelpBox("Balancy Profile is active. You can proceed with the next steps.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox("Balancy Profile is not active. You need to create and activate it to proceed.",
                    MessageType.Warning);

                if (GUILayout.Button("Create & Activate Balancy Profile"))
                {
                    CreateAndActivateCustomProfile(RemoteBuildPath, RemoteLoadPath);
                    isBalancyProfileActive = AssetsEditor.IsCustomProfileActive();
                }
            }

            EditorGUILayout.EndVertical();

            return isBalancyProfileActive;
        }

        void RenderPrivateKey(int step, ref bool disableUI)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            using (new EditorGUI.DisabledGroupScope(disableUI))
            {
                GUILayout.Label($"Step {step}: Configure Private Key", EditorStyles.boldLabel);
                var newPrivateKey = EditorGUILayout.TextField("Private Key:", _privateKey);
                if (newPrivateKey != _privateKey)
                {
                    _privateKey = newPrivateKey;
                    _editorAuth.SetPrivateKey(_privateKey);
                }

                if (string.IsNullOrEmpty(newPrivateKey))
                    disableUI = true;
            }

            EditorGUILayout.EndVertical();
        }

        void RenderBundleGroups(int step, ref bool disableUI)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            using (new EditorGUI.DisabledGroupScope(disableUI))
            {
                GUILayout.Label($"Step {step}: Validate Bundle Groups Setup", EditorStyles.boldLabel);

                if (disableUI)
                    EditorGUILayout.HelpBox("Complete the previous Step to unlock this section", MessageType.Info);

                _section2Expanded = EditorGUILayout.Foldout(_section2Expanded, "Group Settings", true);
                if (_section2Expanded)
                {
                    var settings = AddressableAssetSettingsDefaultObject.Settings;
                    if (settings != null && settings.groups != null)
                    {
                        for (int i = 0;i<settings.groups.Count;i++)
                        {
                            var group = settings.groups[i];
                            if (group != null && !ShouldExcludeGroup(group))
                            {
                                EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                                BundledAssetGroupSchema bundleSchema = group.GetSchema<BundledAssetGroupSchema>();
                                bool balancySetup = false;

                                if (bundleSchema != null)
                                {
                                    bool usingRemoteBuildPaths = bundleSchema.BuildPath.GetValue(settings)
                                        .StartsWith(BalancyDataRoot);
                                    bool usingRemotePaths = bundleSchema.LoadPath.GetValue(settings)
                                        .StartsWith(RemoteLoadPath);
                                    
                                    balancySetup = usingRemotePaths && usingRemoteBuildPaths;
                                }

                                EditorGUILayout.BeginHorizontal();

                                EditorGUILayout.LabelField(group.Name, EditorStyles.boldLabel, GUILayout.MaxWidth(200));

                                GUILayout.FlexibleSpace();

                                if (balancySetup)
                                {
                                    Rect checkRect = EditorGUILayout.GetControlRect(false, 20, GUILayout.Width(20));
                                    EditorGUI.DrawRect(checkRect, new Color(0.2f, 0.7f, 0.2f, 0.8f));
                                    GUI.color = Color.white;
                                    EditorGUI.LabelField(checkRect, "✓", EditorStyles.centeredGreyMiniLabel);
                                    GUI.color = Color.white;

                                    EditorGUILayout.LabelField("Balancy Configuration", EditorStyles.miniLabel);
                                }
                                else
                                {
                                    GUIStyle warningStyle = new GUIStyle(EditorStyles.miniLabel);
                                    warningStyle.normal.textColor = new Color(0.9f, 0.6f, 0.1f); // Orange
                                    EditorGUILayout.LabelField("Custom Configuration", warningStyle);

                                    if (GUILayout.Button("Configure for Balancy", GUILayout.Width(140)))
                                    {
                                        ConfigureGroupForBalancy(group);
                                    }
                                }

                                EditorGUILayout.EndHorizontal();

                                if (group.entries != null)
                                    EditorGUILayout.LabelField($"Assets: {group.entries.Count}");

                                EditorGUILayout.EndVertical();
                                EditorGUILayout.Space(5);
                            }
                        }
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void ConfigureGroupForBalancy(AddressableAssetGroup group)
        {
            if (group == null) return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;

            BundledAssetGroupSchema bundleSchema = group.GetSchema<BundledAssetGroupSchema>();
            if (bundleSchema == null)
                bundleSchema = group.AddSchema<BundledAssetGroupSchema>();

            var allVariableNames = settings.profileSettings.GetVariableNames();

            void SetBuildPathVariable(ProfileValueReference reference, string[] variableNames)
            {
                foreach (var v in variableNames)
                {
                    if (allVariableNames.Contains(v))
                    {
                        reference.SetVariableByName(settings, v);
                        return;
                    }
                }

                Debug.LogError("Variable not found: " + variableNames[0]);
            }

            SetBuildPathVariable(bundleSchema.BuildPath,
                new string[] { AddressableAssetSettings.kRemoteBuildPath, "RemoteBuildPath", "Remote.BuildPath" });

            SetBuildPathVariable(bundleSchema.LoadPath,
                new string[] { AddressableAssetSettings.kRemoteLoadPath, "RemoteLoadPath", "Remote.LoadPath" });

            bundleSchema.IncludeInBuild = true;

            EditorUtility.SetDirty(group);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log($"Group '{group.Name}' configured for Balancy");
        }
        
        private void ConfigureCatalogForBalancy()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            settings.BuildRemoteCatalog = true;
            
            var allVariableNames = settings.profileSettings.GetVariableNames();
            
            void SetBuildPathVariable(ProfileValueReference reference, string[] variableNames)
            {
                foreach (var v in variableNames)
                {
                    if (allVariableNames.Contains(v))
                    {
                        reference.SetVariableByName(settings, v);
                        return;
                    }
                }

                Debug.LogError("Variable not found: " + variableNames[0]);
            }

            settings.RemoteCatalogBuildPath = new ProfileValueReference();
            SetBuildPathVariable(settings.RemoteCatalogBuildPath,
                new string[] { AddressableAssetSettings.kRemoteBuildPath, "RemoteBuildPath", "Remote.BuildPath" });

            settings.RemoteCatalogLoadPath = new ProfileValueReference();
            SetBuildPathVariable(settings.RemoteCatalogLoadPath,
                new string[] { AddressableAssetSettings.kRemoteLoadPath, "RemoteLoadPath", "Remote.LoadPath" });

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        void RenderDeploy(int step, ref bool disableUI)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            using (new EditorGUI.DisabledGroupScope(disableUI))
            {
                GUILayout.Label($"Step {step}: Build and Deploy", EditorStyles.boldLabel);

                if (disableUI)
                    EditorGUILayout.HelpBox("Complete the previous Step to unlock this section", MessageType.Info);

                DrawBuildPipeline();
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Creates a new Addressables profile with custom CDN paths or activates it if it already exists
        /// </summary>
        /// <param name="remoteBuildPath">Path where assets will be built to</param>
        /// <param name="remoteLoadPath">URL from which assets will be loaded</param>
        public static void CreateAndActivateCustomProfile(string remoteBuildPath, string remoteLoadPath)
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError(
                    "Addressables settings not found. Make sure Addressables package is installed and initialized.");
                return;
            }

            string profileId = settings.profileSettings.GetProfileId(CustomProfileName);
            if (string.IsNullOrEmpty(profileId))
                profileId = settings.profileSettings.AddProfile(CustomProfileName, settings.activeProfileId);

            var allVariableNames = settings.profileSettings.GetVariableNames();

            void SetVariable(string[] variableNames, string value)
            {
                foreach (var v in variableNames)
                {
                    if (allVariableNames.Contains(v))
                    {
                        settings.profileSettings.SetValue(profileId, v, value);
                        return;
                    }
                }

                Debug.LogError("Variable not found: " + variableNames[0]);
            }

            SetVariable(
                new string[] { AddressableAssetSettings.kRemoteBuildPath, "RemoteBuildPath", "Remote.BuildPath" },
                remoteBuildPath);
            SetVariable(new string[] { AddressableAssetSettings.kRemoteLoadPath, "RemoteLoadPath", "Remote.LoadPath" },
                remoteLoadPath);

            settings.activeProfileId = settings.profileSettings.GetProfileId(CustomProfileName);
            Debug.Log(
                $"Activated profile: {CustomProfileName} with RemoteBuildPath: {remoteBuildPath} and RemoteLoadPath: {remoteLoadPath}");

            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Checks if the custom profile is currently active
        /// </summary>
        /// <returns>True if the custom profile is active</returns>
        public static bool IsCustomProfileActive()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                return false;

            string activeProfileId = settings.activeProfileId;
            string customProfileId = settings.profileSettings.GetProfileId(CustomProfileName);

            return activeProfileId == customProfileId;
        }

        private static string GetBuildPath()
        {
            string buildPath = BalancyDataRoot + EditorUserBuildSettings.activeBuildTarget;
            string fullBuildPath = Path.Combine(Application.dataPath, "..", buildPath);
            return fullBuildPath;
        }

        public static string BuildAddressables()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;

            // AddressableAssetSettings.CleanPlayerContent(settings.ActivePlayerDataBuilder);
            // settings.activeProfileId = settings.profileSettings.GetProfileId("Default");

            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

            if (string.IsNullOrEmpty(result.Error))
            {
                Debug.Log("Addressables build completed successfully");
                CreateMainCatalog(GetBuildPath());
                return null;
            }
            else
            {
                Debug.LogError($"Addressables build error: {result.Error}");
                return result.Error;
            }
        }
        
        public static void CreateMainCatalog(string buildPath)
        {
            string[] catalogFiles = Directory.GetFiles(buildPath, "catalog_*.bin");
            if (catalogFiles.Length > 0)
            {
                Array.Sort(catalogFiles);

                string latestCatalog = catalogFiles[^1];

                string mainCatalogPath = Path.Combine(buildPath, "catalog.bin");
                File.Copy(latestCatalog, mainCatalogPath, true);

                string hashCatalogPath = Path.Combine(buildPath, "catalog.hash");
                File.Copy(Path.ChangeExtension(latestCatalog, ".hash"), hashCatalogPath, true);

                Debug.Log($"Created main catalog.bin from {Path.GetFileName(latestCatalog)}");
                return;
            }
            
            
            catalogFiles = Directory.GetFiles(buildPath, "catalog_*.json");

            if (catalogFiles.Length > 0)
            {
                Array.Sort(catalogFiles);

                string latestCatalog = catalogFiles[^1];

                string mainCatalogPath = Path.Combine(buildPath, "catalog.json");
                File.Copy(latestCatalog, mainCatalogPath, true);

                string hashCatalogPath = Path.Combine(buildPath, "catalog.hash");
                File.Copy(Path.ChangeExtension(latestCatalog, ".hash"), hashCatalogPath, true);

                Debug.Log($"Created main catalog.json from {Path.GetFileName(latestCatalog)}");
                return;
            }
            
            Debug.LogWarning("No catalog files found to copy!");
        }

        private static FullInfo ReadData()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            List<AddressablesGroup> groups = new List<AddressablesGroup>();
        
            int i = 0;
            foreach (var group in settings.groups)
            {
                if (ShouldExcludeGroup(group))
                    continue;

                var entries = group.entries;
        
                var infoGroup = new AddressablesGroup(entries.Count);
                groups.Add(infoGroup);
                infoGroup.name = group.Name;
                infoGroup.guid = group.Guid;
                var files = infoGroup.entries;
        
                var j = 0;
                foreach (var entry in entries)
                {
                    files[j++] = new FileInfo
                    {
                        link = entry.MainAsset,
                        guid = entry.guid,
                        name = entry.address,
                        path = entry.AssetPath,
                        group = group.Name,
                        labels = entry.labels.ToArray()
                    };
                }
        
                i++;
            }
        
            var info = new FullInfo {groups = groups.ToArray()};

            info.platform = ConvertBuildTargetToDevicePlatform(EditorUserBuildSettings.activeBuildTarget);
            return info;
        }

        private static string NormalizePath(string rootFolder, string fullPath)
        {
            var rel = Path.GetRelativePath(rootFolder, fullPath);

            rel = rel.Replace(Path.DirectorySeparatorChar, '/')
                .Replace(Path.AltDirectorySeparatorChar, '/');

            while (rel.Contains("//")) rel = rel.Replace("//", "/");

            rel = rel.Normalize(NormalizationForm.FormC);

            return rel;
        }

        private static List<string> GetCurrentBundleFiles()
        {
            var folder = GetBuildPath();
            var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                .Select(f => NormalizePath(folder, f))
                .ToList();
            return files;
        }
        
        private static void CalculateHashes(FullInfo info)
        {
            var files = GetCurrentBundleFiles();
            info.files = new BundleFileInfo[files.Count]; 
            for (int i = 0; i < files.Count; i++)
            {
                info.files[i] = new BundleFileInfo
                {
                    name = files[i]
                };
            }
            
            var hashFilePath = $"{GetBuildPath()}/catalog.hash";
            info.hash = File.ReadAllText(hashFilePath); 
            
            foreach (var group in info.groups)
            {
                foreach (var entry in group.entries)
                {
                    string filePath = null;
                    switch (entry.link)
                    {
                        case Texture2D _texture2D:
                            filePath = entry.texturePath = entry.path;
                            break;
                        // case GameObject _gameObject:
                        // {
                        //     var script = _gameObject.GetComponentInChildren<IUnnyAsset>();
                        //     filePath = entry.texturePath = script?.GetPreviewImagePath();
                        //     break;
                        // }
                    }

                    if (!string.IsNullOrEmpty(filePath))
                    {
                        var md5 = MD5.Create();
                        var stream = File.OpenRead(filePath);
                        var checkSum = md5.ComputeHash(stream);
                        var hash = BitConverter.ToString(checkSum).Replace("-", string.Empty);
                        entry.hash = hash;
                    }
                }
            }
        }
        
        [Serializable]
        private class FilesResponse : FullInfo
        {
            public string id;
        }

        private static string ConvertBranchName(string branchName)
        {
            return UnityWebRequest.EscapeURL(branchName);
        }
        
        private void SendInfoToServer(FullInfo info, GameInfo gameInfo, Action<bool> callback)
        {
            var request = _wrapper.CreateRequest($"/v1/games/{gameInfo.GameId}/branches/{ConvertBranchName(gameInfo.BranchName)}/bundles", "POST");
            request.AddBody("groups", info.groups);
            request.AddBody("hash", info.hash);
            request.AddBody("platform", (int)info.platform);
            request.AddBody("files", info.files);

            Debug.LogWarning("request " + request.Url);
            _wrapper.SendRequest(request, response => 
            {
                Debug.LogWarning("response " + response.result + " >> " + response.error);
                if (response.result != UnityWebRequest.Result.Success)
                {
                    gameInfo.OnComplete?.Invoke(response.error + " : " + response.downloadHandler.text);
                    callback?.Invoke(false);
                }
                else
                {
                    if (!string.IsNullOrEmpty(response.downloadHandler.text))
                    {
                        var filesResponse = JsonUtility.FromJson<FilesResponse>(response.downloadHandler.text);
                        if (!string.IsNullOrEmpty(filesResponse.id))
                        {
                            List<string> fileNames = new List<string>();
                            foreach (var fileInfo in filesResponse.files)
                                fileNames.Add(fileInfo.name);
                            
                            List<FileInfo> previewsToUpload = new List<FileInfo>();
                            foreach (var groupInfo in filesResponse.groups)
                            {
                                foreach (var entry in groupInfo.entries)
                                {
                                    var myEntry = info.GetFileInfoByName(entry.name);
                                    previewsToUpload.Add(myEntry);
                                }
                            }

                            var buildPath = GetBuildPath();
                            var bundleId = filesResponse.id;
                            
                            void UploadNextAssetPreview(int index)
                            {
                                if (index >= previewsToUpload.Count)
                                {
                                    Debug.Log("All Previews uploaded successfully.");
                                    callback?.Invoke(true);
                                    return;
                                }

                                var preview = previewsToUpload[index];

                                //TEMP - it should come from the server
                                var maxSize = new Vector2Int(512, 512);
                                var newTexture = GetCompressedTexture(preview, maxSize);

                                if (newTexture == null)
                                {
                                    gameInfo.OnComplete?.Invoke("Can't find compressed texture " + preview?.name);
                                    callback?.Invoke(false);
                                    return;
                                }

                                var fileRequest = _wrapper.CreateRequest(
                                    $"/v1/games/{gameInfo.GameId}/branches/{ConvertBranchName(gameInfo.BranchName)}/bundles/{bundleId}/asset",
                                    "PUT");

                                fileRequest.AddTexture(newTexture, preview.texturePath, preview.name, "file");
                                fileRequest.AddBody("guid", preview.guid);
                                fileRequest.SetMultipart();

                                _wrapper.SendRequest(fileRequest, fileResponse =>
                                {
                                    Debug.Log($"Preview Uploaded {preview.name}: result={fileResponse.result}, error={fileResponse.error}, code={fileResponse.responseCode}");
                                    if (fileResponse.result != UnityWebRequest.Result.Success)
                                    {
                                        gameInfo.OnComplete?.Invoke(fileResponse.error + " : " + fileResponse.downloadHandler.text);
                                        callback?.Invoke(false);
                                    } else
                                        UploadNextAssetPreview(index + 1);
                                });
                            }

                            void UploadNextFile(int index)
                            {
                                if (index >= fileNames.Count)
                                {
                                    Debug.Log("All files uploaded successfully.");
                                    UploadNextAssetPreview(0);
                                    return;
                                }

                                var fileName = fileNames[index];
                                var filePath = $"{buildPath}/{fileName}";

                                var fileRequest = _wrapper.CreateRequest(
                                    $"/v1/games/{gameInfo.GameId}/branches/{ConvertBranchName(gameInfo.BranchName)}/bundles/{bundleId}/file",
                                    "PUT");

                                fileRequest.AddFile(fileName, filePath);
                                fileRequest.AddBody("name", fileName);
                                fileRequest.SetMultipart();

                                _wrapper.SendRequest(fileRequest, fileResponse =>
                                {
                                    Debug.Log($"Uploaded {fileName}: result={fileResponse.result}, error={fileResponse.error}, code={fileResponse.responseCode}");
                                    if (fileResponse.result != UnityWebRequest.Result.Success)
                                    {
                                        gameInfo.OnComplete?.Invoke(fileResponse.error + " : " + fileResponse.downloadHandler.text);
                                        callback?.Invoke(false);
                                    } else
                                        UploadNextFile(index + 1);
                                });
                            }

                            UploadNextFile(0);
                        }
                    }
                }
            });
        }

        private static Texture2D GetCompressedTexture(FileInfo fileInfo, Vector2Int maxSize)
        {
            if (string.IsNullOrEmpty(fileInfo.texturePath))
            {
                // check only for image type assets
                switch (fileInfo.link)
                {
                    case Texture2D:
                    case Sprite:
                    case Texture:
                        Debug.LogError("No image found for guid " + fileInfo.guid);
                        return null;
                }

                return null;
            }

            
            var tImporter = AssetImporter.GetAtPath(fileInfo.texturePath) as TextureImporter;
            if (tImporter == null)
                return null;

            tImporter.isReadable = true;
            AssetDatabase.ImportAsset(fileInfo.texturePath);
            AssetDatabase.Refresh();
        
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(fileInfo.texturePath);

            Texture2D newTexture = null;
            if (texture.width > maxSize.x || texture.height > maxSize.y)
            {
                var scaleX = (float) maxSize.x / texture.width;
                var scaleY = (float) maxSize.y / texture.height;
                var scale = Mathf.Min(scaleX, scaleY);

                int newWidth = Mathf.Max(Mathf.RoundToInt(scale * texture.width), 1);
                int newHeight = Mathf.Max(Mathf.RoundToInt(scale * texture.height), 1);

                newTexture = ScaleTexture(texture, newWidth, newHeight);
            }
            else
            {
                newTexture = CopyTexture(texture);
            }

            tImporter.isReadable = false;
            AssetDatabase.ImportAsset(fileInfo.texturePath);
            AssetDatabase.Refresh();

            return newTexture;
        }

        private static Texture2D CopyTexture(Texture2D source)
        {
            Texture2D texture2D = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            texture2D.SetPixels32(source.GetPixels32(), 0);
            texture2D.Apply();

            return texture2D;
        }

        private static Texture2D ScaleTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            Texture2D texture2D = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            Color[] pixels = texture2D.GetPixels(0);
            Color32[] sourcePixels = source.GetPixels32(0);
            
            float num1 = 1f / (float) targetWidth;
            float num2 = 1f / (float) targetHeight;
            for (int index = 0; index < pixels.Length; ++index)
            {
                var tx = (index % targetWidth) * num1;
                var ty = (index / targetWidth) * num2;
                var sx = Mathf.RoundToInt(tx * source.width);
                var sy = Mathf.RoundToInt(ty * source.height);
                pixels[index] = sourcePixels[sx + sy * source.width];
            }

            texture2D.SetPixels(pixels, 0);
            texture2D.Apply();
            return texture2D;
        }
        
        private static Dictionary<string, FileInfo> MapFiles(FullInfo info)
        {
            return info.groups.SelectMany(group => group.entries).ToDictionary(entry => entry.guid);
        }

        private static bool ShouldExcludeGroup(AddressableAssetGroup group)
        {
            return string.Equals(group.Name, "Built In Data");
        }

        [Serializable]
        private class FullInfo
        {
            public Constants.DevicePlatform platform;
            public string hash;
            public AddressablesGroup[] groups;
            public BundleFileInfo[] files;

            public FileInfo GetFileInfoByName(string name)
            {
                foreach (var group in groups)
                {
                    foreach (var entry in group.entries)
                    {
                        if (entry.name == name)
                            return entry;
                    }
                }

                return null;
            } 
        }

        [Serializable]
        private class AddressablesGroup
        {
            public string guid;
            public string name;
            public FileInfo[] entries;

            public AddressablesGroup(int size)
            {
                entries = new FileInfo[size];
            }
        }

        [Serializable]
        private class BundleFileInfo
        {
            public string name;
        }

        [Serializable]
        private class FileInfo
        {
            [NonSerialized] public UnityEngine.Object link;
            [NonSerialized] public string path;
            [NonSerialized] public string texturePath;

            public string guid;
            public string name;
            public string hash;
            public string[] labels;
            [NonSerialized] public string group;
        }

        private class SynchAddressablesResponse
        {
            public string[] assets;
            public Size size;

            public class Size
            {
                public int x;
                public int y;
            }
        }

        private class GameInfo
        {
            public string GameId;
            public string Token;
            public int BranchId;
            public string BranchName;
            public Action<string, float> OnProgress;
            public Action<string> OnComplete;
            public Action OnStart;
        }

        #region BuildAnimation

        private enum BuildStep
        {
            NotStarted,
            Building,
            // Uploading,
            Syncing,
            Completed,
            Error
        }

        private BuildStep _currentBuildStep = BuildStep.NotStarted;
        private float _stepProgress = 0f;
        private double _startBuildTime = 0f;
        private string _currentStepDetails = "";

        private GameInfo _gameInfo;

        private void DeleteUpFolder()
        {
            var folder = GetBuildPath();
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
        
        private void StartBuildProcess()
        {
            DeleteUpFolder();
            _wrapper = new Balancy.Editor.BalancyS2SWrapper(_gameInfo.GameId, _privateKey);
            ConfigureCatalogForBalancy();
            
            _currentBuildStep = BuildStep.Building;
            _stepProgress = 0f;
            _startBuildTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += UpdateBuildProgress;

            RunBuildProcess();
            // SimulateBuildProcess();
        }

        private async void RunBuildProcess()
        {
            // Start building
            _currentBuildStep = BuildStep.Building;
            _currentStepDetails = "Compiling assets...";
            var buildError = BuildAddressables();
            if (!string.IsNullOrEmpty(buildError))
            {
                _currentStepDetails = "Build failed! " + buildError;
                _currentBuildStep = BuildStep.Error;
                return;
            }

            // // Start uploading
            // _currentBuildStep = BuildStep.Uploading;
            // _currentStepDetails = "Preparing files...";
            // await Task.Delay(1000); // Simulate work

            // Start syncing
            if (_currentBuildStep != BuildStep.NotStarted)
            {
                _currentBuildStep = BuildStep.Syncing;
                _currentStepDetails = "Verifying uploads...";
                
                var info = ReadData();
                CalculateHashes(info);
                bool complete = false;
                bool success = false;
                SendInfoToServer(info, _gameInfo, (_success) =>
                {
                    complete = true;
                    success = _success;

                    if (success)
                        _gameInfo.OnComplete?.Invoke(null);
                });
                
                while (!complete)
                    await Task.Delay(100);

                if (!success)
                {
                    _currentStepDetails = "Sync Assets failed!";
                    _currentBuildStep = BuildStep.Error;
                    EditorApplication.update -= UpdateBuildProgress;
                    Repaint();
                    return;
                }
            }

            // Complete
            _currentBuildStep = BuildStep.Completed;
            _currentStepDetails = "All done!";

            // Clean up
            EditorApplication.update -= UpdateBuildProgress;

            // Refresh the window
            Repaint();
        }

        private void UpdateBuildProgress()
        {
            if (_currentBuildStep == BuildStep.NotStarted || _currentBuildStep == BuildStep.Completed)
                return;

            _stepProgress = (float)(EditorApplication.timeSinceStartup - _startBuildTime) / 2 % 1f;

            // Force repaint to update the UI
            Repaint();
        }

        private void DrawBuildPipeline()
        {
            DrawPipelineStep("1. Building Addressables", BuildStep.Building);
            DrawPipelineStep("2. Syncing with server", BuildStep.Syncing);

            if (_currentBuildStep != BuildStep.NotStarted && _currentBuildStep != BuildStep.Completed)
            {
                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                // Show animated ellipsis based on progress
                string ellipsis = "";
                int dotsCount = Mathf.FloorToInt(_stepProgress * 3) + 1;
                for (int i = 0; i < dotsCount; i++)
                    ellipsis += ".";

                GUILayout.Label(_currentStepDetails + ellipsis, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                // Progress bar
                Rect progressRect = EditorGUILayout.GetControlRect(false, 5);
                EditorGUI.DrawRect(progressRect, new Color(0.2f, 0.2f, 0.2f));

                // Animated bar (with "Knight Rider" effect)
                float width = progressRect.width * 0.2f; // 20% of total width
                float position = Mathf.PingPong(_stepProgress * 2f, 1f) * (progressRect.width - width);
                Rect barRect = new Rect(progressRect.x + position, progressRect.y, width, progressRect.height);
                EditorGUI.DrawRect(barRect, GetStepColor(_currentBuildStep));
            }

            if (_currentBuildStep == BuildStep.Completed)
            {
                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUILayout.Label("Build process completed successfully!", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space();

            // Start or reset button
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (_currentBuildStep == BuildStep.NotStarted || _currentBuildStep == BuildStep.Completed)
            {
                if (_currentBuildStep != BuildStep.Completed)
                {
                    if (GUILayout.Button("Start Build",
                            GUILayout.Width(120)))
                        StartBuildProcess();
                }
            }
            else
            {
                if (GUILayout.Button("Cancel", GUILayout.Width(120)))
                {
                    _currentBuildStep = BuildStep.NotStarted;
                    EditorApplication.update -= UpdateBuildProgress;
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPipelineStep(string stepName, BuildStep step)
        {
            EditorGUILayout.BeginHorizontal();

            // Status indicator
            Rect indicatorRect = EditorGUILayout.GetControlRect(false, 20, GUILayout.Width(20));
            if (_currentBuildStep > step)
            {
                // Completed step - checkmark
                EditorGUI.DrawRect(indicatorRect, new Color(0.2f, 0.7f, 0.2f));
                GUI.color = Color.white;
                EditorGUI.LabelField(indicatorRect, "✓", EditorStyles.centeredGreyMiniLabel);
                GUI.color = Color.white;
            }
            else if (_currentBuildStep == step)
            {
                // Current step - animated indicator
                EditorGUI.DrawRect(indicatorRect, GetStepColor(step));
                float pulse = (Mathf.Sin(_stepProgress * Mathf.PI * 2) + 1) * 0.5f;
                EditorGUI.DrawRect(
                    new Rect(indicatorRect.x + 4, indicatorRect.y + 4, indicatorRect.width - 8,
                        indicatorRect.height - 8),
                    new Color(1f, 1f, 1f, pulse));
            }
            else
            {
                // Future step - empty box
                EditorGUI.DrawRect(indicatorRect, new Color(0.3f, 0.3f, 0.3f));
            }

            // Step name
            GUIStyle style = new GUIStyle(EditorStyles.label);
            if (_currentBuildStep == step)
            {
                style.fontStyle = FontStyle.Bold;
                EditorGUILayout.LabelField(stepName, style);
            }
            else if (_currentBuildStep > step)
            {
                style.fontStyle = FontStyle.Normal;
                style.normal.textColor = Color.green;
                EditorGUILayout.LabelField(stepName, style);
            }
            else
            {
                style.normal.textColor = Color.gray;
                EditorGUILayout.LabelField(stepName, style);
            }

            EditorGUILayout.EndHorizontal();
        }

        private Color GetStepColor(BuildStep step)
        {
            switch (step)
            {
                case BuildStep.Building:
                    return new Color(0.1f, 0.5f, 0.9f); // Blue
                // case BuildStep.Uploading:
                //     return new Color(0.9f, 0.6f, 0.1f); // Orange
                case BuildStep.Syncing:
                    return new Color(0.6f, 0.3f, 0.9f); // Purple
                case BuildStep.Completed:
                    return new Color(0.1f, 0.8f, 0.2f); // Green
                case BuildStep.Error:
                    return new Color(0.9f, 0.1f, 0.1f); // Red
                default:
                    return Color.gray;
            }
        }

        #endregion
        
        public static Balancy.Constants.DevicePlatform ConvertBuildTargetToDevicePlatform(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.Android:
                    return Balancy.Constants.DevicePlatform.Android;
                case BuildTarget.iOS:
                    return Balancy.Constants.DevicePlatform.IPhonePlayer;
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return Balancy.Constants.DevicePlatform.WindowsPlayer;
                case BuildTarget.StandaloneOSX:
                    return Balancy.Constants.DevicePlatform.OSXPlayer;
                case BuildTarget.WebGL:
                    return Balancy.Constants.DevicePlatform.WebGLPlayer;
                case BuildTarget.LinuxHeadlessSimulation:
                case BuildTarget.StandaloneLinux64:
                    return Balancy.Constants.DevicePlatform.LinuxPlayer;
                default:
                    Debug.LogWarning($"Unknown BuildTarget: {target}, returning -1");
                    return Balancy.Constants.DevicePlatform.Unknown;
            }
        }
    }
}
