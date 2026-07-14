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
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Build;
#endif

namespace Balancy
{
    [InitializeOnLoad]
    public class AssetsEditor : EditorWindow
    {
        private const string BalancyDataRoot = "Library/BalancyData/";
        private const string CustomProfileName = "BalancyProfile";
        private const string RemoteBuildPath = BalancyDataRoot + "[BuildTarget]"; // Where assets will be built
        private const string RemoteLoadPath = "BALANCY_URL/"; // Where assets will be loaded from
        private const string LegacyRemoteLoadPath = "{BALANCY_URL}/"; // Old format with braces

        private bool _section2Expanded = true;

        // private bool _section3Expanded = false;
        private string _privateKey;
        private bool _deployAfterSync;
        private readonly Dictionary<string, bool> _expandedGroupFiles = new Dictionary<string, bool>();

        private enum GroupSetup
        {
            Remote, // Built to BalancyData and uploaded to Balancy CDN
            Legacy, // Balancy setup, but with the old {braces} load path
            Local, // Built into the app (default Unity local paths), not uploaded
            Custom, // Anything else
            NoSchema
        }

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
            _completionNotified = false;

            _gameInfo = new GameInfo
            {
                GameId = gameId,
                Token = token,
                BranchId = branchId,
                BranchName = branchName,
                OnProgress = onProgress,
                OnComplete = (msg) =>
                {
                    _completionNotified = true;
                    onComplete?.Invoke(msg);
                },
                OnStart = onStart
            };
        }

        private void OnDestroy()
        {
            EditorApplication.update -= UpdateBuildProgress;

            if (!_completionNotified)
            {
                _completionNotified = true;
                _gameInfo?.OnComplete?.Invoke("Addressables sync was cancelled.");
            }
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
                var settings = AddressableAssetSettingsDefaultObject.Settings;
                if (HasLegacyLoadPath(settings))
                {
                    EditorGUILayout.HelpBox(
                        "Balancy Profile uses the old load path format with {braces}. This can cause issues with some Unity versions. Please update it.",
                        MessageType.Warning);

                    if (GUILayout.Button("Fix Load Path"))
                    {
                        CreateAndActivateCustomProfile(RemoteBuildPath, RemoteLoadPath);
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Balancy Profile is active. You can proceed with the next steps.",
                        MessageType.Info);
                }
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
                                RenderGroup(settings, group);
                        }
                    }
                }
            }

            EditorGUILayout.EndVertical();
        }

        private static readonly string[] RemoteBuildPathVariables =
            { AddressableAssetSettings.kRemoteBuildPath, "RemoteBuildPath", "Remote.BuildPath" };

        private static readonly string[] RemoteLoadPathVariables =
            { AddressableAssetSettings.kRemoteLoadPath, "RemoteLoadPath", "Remote.LoadPath" };

        private static readonly string[] LocalBuildPathVariables =
            { AddressableAssetSettings.kLocalBuildPath, "LocalBuildPath", "Local.BuildPath" };

        private static readonly string[] LocalLoadPathVariables =
            { AddressableAssetSettings.kLocalLoadPath, "LocalLoadPath", "Local.LoadPath" };

        private void RenderGroup(AddressableAssetSettings settings, AddressableAssetGroup group)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            var setup = GetGroupSetup(settings, group);

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField(group.Name, EditorStyles.boldLabel, GUILayout.MaxWidth(200));

            GUILayout.FlexibleSpace();

            switch (setup)
            {
                case GroupSetup.Remote:
                {
                    Rect checkRect = EditorGUILayout.GetControlRect(false, 20, GUILayout.Width(20));
                    EditorGUI.DrawRect(checkRect, new Color(0.2f, 0.7f, 0.2f, 0.8f));
                    EditorGUI.LabelField(checkRect, "✓", EditorStyles.centeredGreyMiniLabel);

                    EditorGUILayout.LabelField("Remote (Balancy CDN)", EditorStyles.miniLabel, GUILayout.Width(130));

                    if (GUILayout.Button("Switch to Local", GUILayout.Width(140)))
                        ConfigureGroupAsLocal(group);
                    break;
                }
                case GroupSetup.Legacy:
                {
                    GUIStyle warningStyle = new GUIStyle(EditorStyles.miniLabel);
                    warningStyle.normal.textColor = new Color(0.9f, 0.6f, 0.1f); // Orange
                    EditorGUILayout.LabelField("Legacy Load Path ({braces})", warningStyle, GUILayout.Width(160));

                    if (GUILayout.Button("Update Path", GUILayout.Width(140)))
                        CreateAndActivateCustomProfile(RemoteBuildPath, RemoteLoadPath);
                    break;
                }
                case GroupSetup.Local:
                {
                    Rect checkRect = EditorGUILayout.GetControlRect(false, 20, GUILayout.Width(20));
                    EditorGUI.DrawRect(checkRect, new Color(0.25f, 0.5f, 0.9f, 0.8f));
                    EditorGUI.LabelField(checkRect, "✓", EditorStyles.centeredGreyMiniLabel);

                    EditorGUILayout.LabelField("Local (in app build)", EditorStyles.miniLabel, GUILayout.Width(130));

                    if (GUILayout.Button("Switch to Remote", GUILayout.Width(140)))
                        ConfigureGroupForBalancy(group);
                    break;
                }
                default:
                {
                    GUIStyle warningStyle = new GUIStyle(EditorStyles.miniLabel);
                    warningStyle.normal.textColor = new Color(0.9f, 0.6f, 0.1f); // Orange
                    EditorGUILayout.LabelField("Custom Configuration", warningStyle, GUILayout.Width(130));

                    if (GUILayout.Button("Set Local", GUILayout.Width(80)))
                        ConfigureGroupAsLocal(group);
                    if (GUILayout.Button("Set Remote", GUILayout.Width(85)))
                        ConfigureGroupForBalancy(group);
                    break;
                }
            }

            EditorGUILayout.EndHorizontal();

            RenderGroupFiles(group);

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        private void RenderGroupFiles(AddressableAssetGroup group)
        {
            var entries = group.entries;
            if (entries == null)
                return;

            _expandedGroupFiles.TryGetValue(group.Guid, out bool expanded);
            bool newExpanded = EditorGUILayout.Foldout(expanded, $"Assets: {entries.Count}", true);
            if (newExpanded != expanded)
                _expandedGroupFiles[group.Guid] = newExpanded;

            if (!newExpanded)
                return;

            const int maxVisibleEntries = 100;
            int shown = 0;
            EditorGUI.indentLevel++;
            foreach (var entry in entries)
            {
                if (shown++ >= maxVisibleEntries)
                {
                    EditorGUILayout.LabelField($"... and {entries.Count - maxVisibleEntries} more", EditorStyles.miniLabel);
                    break;
                }

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(entry.address, EditorStyles.miniLabel, GUILayout.MaxWidth(250));
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(entry.MainAsset, typeof(UnityEngine.Object), false);
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }

        private static GroupSetup GetGroupSetup(AddressableAssetSettings settings, AddressableAssetGroup group)
        {
            BundledAssetGroupSchema bundleSchema = group.GetSchema<BundledAssetGroupSchema>();
            if (bundleSchema == null)
                return GroupSetup.NoSchema;

            var buildPathValue = bundleSchema.BuildPath.GetValue(settings) ?? string.Empty;
            var loadPathValue = bundleSchema.LoadPath.GetValue(settings) ?? string.Empty;

            bool usingRemoteBuildPaths = buildPathValue.StartsWith(BalancyDataRoot);
            if (usingRemoteBuildPaths && loadPathValue.StartsWith(RemoteLoadPath))
                return GroupSetup.Remote;
            if (usingRemoteBuildPaths && loadPathValue.Contains("{"))
                return GroupSetup.Legacy;

            var buildPathName = bundleSchema.BuildPath.GetName(settings);
            var loadPathName = bundleSchema.LoadPath.GetName(settings);

            if (LocalBuildPathVariables.Contains(buildPathName) && LocalLoadPathVariables.Contains(loadPathName))
                return GroupSetup.Local;

            return GroupSetup.Custom;
        }

        private static void SetPathVariable(AddressableAssetSettings settings, ProfileValueReference reference,
            string[] variableNames)
        {
            var allVariableNames = settings.profileSettings.GetVariableNames();
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

        private void ConfigureGroupForBalancy(AddressableAssetGroup group)
        {
            ConfigureGroupPaths(group, RemoteBuildPathVariables, RemoteLoadPathVariables, "Remote (Balancy)");
        }

        /// <summary>
        /// Reverts the group to the default Unity local paths: the bundles are included
        /// in the app build and are not uploaded to the Balancy server.
        /// </summary>
        private void ConfigureGroupAsLocal(AddressableAssetGroup group)
        {
            ConfigureGroupPaths(group, LocalBuildPathVariables, LocalLoadPathVariables, "Local");
        }

        private void ConfigureGroupPaths(AddressableAssetGroup group, string[] buildPathVariables,
            string[] loadPathVariables, string setupName)
        {
            if (group == null) return;

            var settings = AddressableAssetSettingsDefaultObject.Settings;

            BundledAssetGroupSchema bundleSchema = group.GetSchema<BundledAssetGroupSchema>();
            if (bundleSchema == null)
                bundleSchema = group.AddSchema<BundledAssetGroupSchema>();

            SetPathVariable(settings, bundleSchema.BuildPath, buildPathVariables);
            SetPathVariable(settings, bundleSchema.LoadPath, loadPathVariables);

            bundleSchema.IncludeInBuild = true;

            EditorUtility.SetDirty(bundleSchema);
            EditorUtility.SetDirty(group);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            Debug.Log($"Group '{group.Name}' configured as {setupName}");
        }

        private void ConfigureCatalogForBalancy()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            settings.BuildRemoteCatalog = true;

            settings.RemoteCatalogBuildPath = new ProfileValueReference();
            SetPathVariable(settings, settings.RemoteCatalogBuildPath, RemoteBuildPathVariables);

            settings.RemoteCatalogLoadPath = new ProfileValueReference();
            SetPathVariable(settings, settings.RemoteCatalogLoadPath, RemoteLoadPathVariables);

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
                else
                    RenderBuildSummary();

                DrawBuildPipeline();
            }

            EditorGUILayout.EndVertical();
        }

        private void RenderBuildSummary()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null || settings.groups == null)
                return;

            int remoteGroups = 0, remoteAssets = 0;
            int localGroups = 0, localAssets = 0;
            int customGroups = 0, customAssets = 0;

            foreach (var group in settings.groups)
            {
                if (group == null || ShouldExcludeGroup(group))
                    continue;

                int count = group.entries?.Count ?? 0;
                switch (GetGroupSetup(settings, group))
                {
                    case GroupSetup.Remote:
                    case GroupSetup.Legacy:
                        remoteGroups++;
                        remoteAssets += count;
                        break;
                    case GroupSetup.Local:
                        localGroups++;
                        localAssets += count;
                        break;
                    default:
                        customGroups++;
                        customAssets += count;
                        break;
                }
            }

            EditorGUILayout.HelpBox(
                $"Remote: {remoteGroups} group(s), {remoteAssets} asset(s) — uploaded to Balancy CDN.\n" +
                $"Local: {localGroups} group(s), {localAssets} asset(s) — included in the app build, not uploaded.",
                MessageType.Info);

            if (customGroups > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{customGroups} group(s) with {customAssets} asset(s) have a custom configuration. " +
                    "They will be built with their own paths. Set them to Local or Remote in Step 3 to manage them with Balancy.",
                    MessageType.Warning);
            }
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

        private static string GetProfileRemoteLoadPath(AddressableAssetSettings settings)
        {
            if (settings == null)
                return null;

            var profileId = settings.profileSettings.GetProfileId(CustomProfileName);
            if (string.IsNullOrEmpty(profileId))
                return null;

            var allVariableNames = settings.profileSettings.GetVariableNames();
            string[] candidates = { AddressableAssetSettings.kRemoteLoadPath, "RemoteLoadPath", "Remote.LoadPath" };
            foreach (var v in candidates)
            {
                if (allVariableNames.Contains(v))
                    return settings.profileSettings.GetValueByName(profileId, v);
            }

            return null;
        }

        private static bool HasLegacyLoadPath(AddressableAssetSettings settings)
        {
            var value = GetProfileRemoteLoadPath(settings);
            return value != null && value.Contains("{");
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

        private void CallDeploy(GameInfo gameInfo, Action<bool, string> callback)
        {
            var request = _wrapper.CreateRequest(
                $"/v1/games/{gameInfo.GameId}/branches/{ConvertBranchName(gameInfo.BranchName)}/deploy",
                "POST");
            _wrapper.SendRequest(request, response =>
            {
                var success = response.result == UnityWebRequest.Result.Success;
                string error = success ? null : response.error;
                if (!success)
                    Debug.LogError("Deploy failed: " + error);
                callback?.Invoke(success, error);
            });
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
        private bool _completionNotified;

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
                    
                    if (success && !_deployAfterSync)
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

            if (_deployAfterSync)
            {
                _currentStepDetails = "Deploying...";
                bool deployComplete = false;
                bool deploySuccess = false;
                string deployError = null;
                CallDeploy(_gameInfo, (s, error) =>
                {
                    deployComplete = true;
                    deploySuccess = s;
                    deployError = error;
                });

                while (!deployComplete)
                    await Task.Delay(100);

                if (deploySuccess)
                {
                    _gameInfo.OnComplete?.Invoke(null);
                }
                else
                {
                    _gameInfo.OnComplete?.Invoke(deployError);
                    _currentStepDetails = "Deploy failed!";
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

            if (_currentBuildStep == BuildStep.NotStarted)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                _deployAfterSync = EditorGUILayout.ToggleLeft("Deploy after sync", _deployAfterSync, GUILayout.Width(120));
                var helpIcon = EditorGUIUtility.IconContent("_Help");
                var helpRect = GUILayoutUtility.GetRect(helpIcon, GUIStyle.none, GUILayout.Width(36), GUILayout.Height(24));
                GUI.Label(helpRect, new GUIContent(helpIcon.image, "It will start deploy process on the Balancy's dashboard and will deploy all changes after sync. Be careful."));
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space();
            }

            // Start or reset button
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (_currentBuildStep == BuildStep.NotStarted || _currentBuildStep == BuildStep.Completed)
            {
                if (_currentBuildStep != BuildStep.Completed)
                {
                    if (!IsIL2CPPBackendInstalled())
                    {
                        EditorGUILayout.HelpBox(
                            "IL2CPP scripting backend is not installed for " +
                            EditorUserBuildSettings.activeBuildTarget +
                            ". Please install the IL2CPP module via Unity Hub.",
                            MessageType.Error);
                        if (GUILayout.Button("Install IL2CPP Module...", GUILayout.Width(200)))
                            OpenUnityHub();
                    }
                    else
                    {
                        if (GUILayout.Button("Start Build", GUILayout.Width(120)))
                            StartBuildProcess();
                    }
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
        
        private static bool IsIL2CPPBackendInstalled()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            var targetGroup = BuildPipeline.GetBuildTargetGroup(target);
#if UNITY_2021_2_OR_NEWER
            var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.FromBuildTargetGroup(targetGroup));
#else
            var backend = PlayerSettings.GetScriptingBackend(targetGroup);
#endif

            if (backend != ScriptingImplementation.IL2CPP)
                return true;

            string playbackEnginesPath = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines");
            string variationsFolder = null;

            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    variationsFolder = Path.Combine(playbackEnginesPath, "windowsstandalonesupport", "Variations");
                    break;
                case BuildTarget.StandaloneOSX:
                    variationsFolder = Path.Combine(playbackEnginesPath, "MacStandaloneSupport", "Variations");
                    break;
                case BuildTarget.StandaloneLinux64:
                    variationsFolder = Path.Combine(playbackEnginesPath, "LinuxStandaloneSupport", "Variations");
                    break;
                default:
                    return true;
            }

            if (variationsFolder != null && Directory.Exists(variationsFolder))
                return Directory.GetDirectories(variationsFolder, "*il2cpp*").Length > 0;

            return false;
        }

        private static void OpenUnityHub()
        {
            string[] possiblePaths;

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                possiblePaths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity Hub", "Unity Hub.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Unity Hub", "Unity Hub.exe"),
                };
            }
            else if (Application.platform == RuntimePlatform.OSXEditor)
            {
                possiblePaths = new[] { "/Applications/Unity Hub.app/Contents/MacOS/Unity Hub" };
            }
            else
            {
                possiblePaths = new[] { "/usr/bin/unityhub" };
            }

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    System.Diagnostics.Process.Start(path);
                    return;
                }
            }

            EditorUtility.DisplayDialog("Unity Hub Not Found",
                "Please open Unity Hub manually and install the IL2CPP module for Unity " + Application.unityVersion + ".",
                "OK");
        }

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
