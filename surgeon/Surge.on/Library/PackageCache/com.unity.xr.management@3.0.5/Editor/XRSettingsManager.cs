using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using UnityEditor;
using UnityEditor.PackageManager;

using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.Management;

namespace UnityEditor.XR.Management
{
    class XRSettingsManager : SettingsProvider
    {

        struct Content
        {
            internal static GUIContent s_LoaderInitOnStartLabel = new GUIContent("Initialize on Startup");
            internal static GUIContent s_ProvidersToInstall = new GUIContent("Installable XR Plugin Providers");
            internal static GUIContent s_LookingForProviders = new GUIContent("Looking for installable provider packages... ");
            internal static GUIContent s_NoInstallablePackages = new GUIContent("No installable provider packages found.");
            internal static string k_NeedToInstallAProvider = "Before you can use the XR system you need to install at least one provider from the list.";
            internal static string k_ProvidersUnavailable = "We are unable to find any providers usable within Unity at this time. XR is currently unavailable to use.";
            internal static GUIContent s_InstallPackage = new GUIContent("Install");
            internal static GUIContent s_InstallingPackage = new GUIContent("Installing");
            internal static GUIContent s_InstalledPackage = new GUIContent("Installed");
        }

        internal static GUIStyle GetStyle(string styleName)
        {
            GUIStyle s = GUI.skin.FindStyle(styleName) ?? EditorGUIUtility.GetBuiltinSkin(EditorSkin.Inspector).FindStyle(styleName);
            if (s == null)
            {
                Debug.LogError("Missing built-in guistyle " + styleName);
                s = GUI.skin.box;
            }
            return s;
        }

        static string s_SettingsRootTitle = "Project/XR Plugin Management";
        static XRSettingsManager s_SettingsManager = null;


        SerializedObject m_SettingsWrapper;

        private Dictionary<BuildTargetGroup, XRManagerSettingsEditor> CachedSettingsEditor = new Dictionary<BuildTargetGroup, XRManagerSettingsEditor>();

        private bool m_HasCompletedRequest = false;
        private bool m_HasProviders = false;
        private bool m_HasInstalledProviders = false;

        struct XRPackageInformation
        {
            internal PackageManager.PackageInfo uninstalledPackageInfo;
            internal bool isInstalled;
        }
        private Dictionary<string, XRPackageInformation> m_XRPackages = new Dictionary<string, XRPackageInformation>();
        private PackageManager.Requests.SearchRequest m_PackageListRequest = null;
        private PackageManager.Requests.AddRequest m_InstallingPackage = null;
        private string m_InstallingPackageName = "";

        private BuildTargetGroup m_LastBuildTargetGroup = BuildTargetGroup.Unknown;

        static XRGeneralSettingsPerBuildTarget currentSettings
        {
            get
            {
                XRGeneralSettingsPerBuildTarget generalSettings = null;
                EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out generalSettings);
                if (generalSettings == null)
                {
                    lock(s_SettingsManager)
                    {
                        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out generalSettings);
                        if (generalSettings == null)
                        {
                            string searchText = "t:XRGeneralSettings";
                            string[] assets = AssetDatabase.FindAssets(searchText);
                            if (assets.Length > 0)
                            {
                                string path = AssetDatabase.GUIDToAssetPath(assets[0]);
                                generalSettings = AssetDatabase.LoadAssetAtPath(path, typeof(XRGeneralSettingsPerBuildTarget)) as XRGeneralSettingsPerBuildTarget;
                            }
                        }

                        if (generalSettings == null)
                        {
                            generalSettings = ScriptableObject.CreateInstance(typeof(XRGeneralSettingsPerBuildTarget)) as XRGeneralSettingsPerBuildTarget;
                            string assetPath = EditorUtilities.GetAssetPathForComponents(EditorUtilities.s_DefaultGeneralSettingsPath);
                            if (!string.IsNullOrEmpty(assetPath))
                            {
                                assetPath = Path.Combine(assetPath, "XRGeneralSettings.asset");
                                AssetDatabase.CreateAsset(generalSettings, assetPath);
                            }
                        }

                        EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, generalSettings, true);

                    }
                }
                return generalSettings;
            }
        }

        [UnityEngine.Internal.ExcludeFromDocs]
        XRSettingsManager(string path, SettingsScope scopes = SettingsScope.Project) : base(path, scopes)
        {
            m_HasCompletedRequest = false;
            m_XRPackages.Clear();
            m_PackageListRequest = Client.SearchAll();

            EditorApplication.update += UpdatePackageManagerQuery;
        }

        [SettingsProvider]
        [UnityEngine.Internal.ExcludeFromDocs]
        static SettingsProvider Create()
        {
            if (s_SettingsManager == null)
            {
                s_SettingsManager = new XRSettingsManager(s_SettingsRootTitle);
            }

            return s_SettingsManager;
        }

        [SettingsProviderGroup]
        [UnityEngine.Internal.ExcludeFromDocs]
        static SettingsProvider[] CreateAllChildSettingsProviders()
        {
            List<SettingsProvider> ret = new List<SettingsProvider>();
            if (s_SettingsManager != null)
            {
                var ats = TypeLoaderExtensions.GetAllTypesWithAttribute<XRConfigurationDataAttribute>();
                foreach (var at in ats)
                {
                    XRConfigurationDataAttribute xrbda = at.GetCustomAttributes(typeof(XRConfigurationDataAttribute), true)[0] as XRConfigurationDataAttribute;
                    string settingsPath = String.Format("{1}/{0}", xrbda.displayName, s_SettingsRootTitle);
                    var resProv = new XRConfigurationProvider(settingsPath, xrbda.buildSettingsKey, at);
                    ret.Add(resProv);
                }
            }

            // LIH Package Provider
            string settingsPathLIH = String.Format("{1}/{0}", "Input Helpers", s_SettingsRootTitle);
            var lihProv = new InputHelpersConfigurationProvider(settingsPathLIH);
            ret.Add(lihProv);
            return ret.ToArray();
        }

        void InitEditorData(ScriptableObject settings)
        {
            if (settings != null)
            {
                m_SettingsWrapper = new SerializedObject(settings);
            }
            EditorApplication.update += UpdatePackageManagerQuery;
        }

        void UpdatePackageManagerQuery()
        {
            Repaint();
            EditorApplication.update -= UpdatePackageManagerQuery;

            if (m_PackageListRequest == null)
                return;

            if (!m_PackageListRequest.IsCompleted)
            {
                EditorApplication.update += UpdatePackageManagerQuery;
                return;
            }

            m_HasCompletedRequest = true;
            m_HasInstalledProviders = false;

            if (m_PackageListRequest.Status != StatusCode.Success)
            {
                m_HasProviders = false;
                m_PackageListRequest = null;
                return;
            }

            foreach (var pinfo in m_PackageListRequest.Result)
            {
                var xrp = from keyword in pinfo.keywords 
                    where String.Compare("xreditorsubsystem", keyword, true) == 0 
                    select keyword;

                if (xrp.Any())
                {
                    string tempPath = $"Packages/{pinfo.name}/package.json";
                    try
                    {
                        XRPackageInformation xrpinfo = new XRPackageInformation();
                        xrpinfo.uninstalledPackageInfo = pinfo;
                        xrpinfo.isInstalled = false;

                        var packagePath = Path.GetFullPath(tempPath);

                        if (File.Exists(packagePath))
                        {
                            xrpinfo.isInstalled = true;
                            m_HasInstalledProviders = true;
                        }

                        m_XRPackages.Add(pinfo.name, xrpinfo);
                    }
                    catch (Exception)
                    {
                        // DO NOTHING...
                    }
                }
            }

            m_HasProviders = m_XRPackages.Any();
            m_PackageListRequest = null;
        }

        void UpdatePackageInstallationQuery()
        {
            Repaint();
            EditorApplication.update -= UpdatePackageInstallationQuery;

            if (m_InstallingPackage == null)
                return;

            if (!m_InstallingPackage.IsCompleted)
            {
                EditorApplication.update += UpdatePackageInstallationQuery;
                return;
            }

            if (m_InstallingPackage.Status != StatusCode.Success)
            {
                // TODO Track installation error...
                m_InstallingPackage = null;
                m_InstallingPackageName = "";
                return;
            }

            try
            {
                PackageManager.PackageInfo pinfo = m_InstallingPackage.Result;

                XRPackageInformation xrpinfo;
                bool addPackage = false;

                if (!m_XRPackages.TryGetValue(pinfo.name, out xrpinfo))
                {
                    xrpinfo.uninstalledPackageInfo = pinfo;
                    xrpinfo.isInstalled = false;
                    addPackage = true;
                }

                string tempPath = $"Packages/{pinfo.name}/package.json";
                var packagePath = Path.GetFullPath(tempPath);

                if (File.Exists(packagePath))
                {
                    xrpinfo.isInstalled = true;
                    m_HasInstalledProviders = true;
                }

                if (addPackage)
                    m_XRPackages.Add(pinfo.name, xrpinfo);
            }
            catch (Exception)
            {
                // TODO Track creation error...
            }

            m_InstallingPackage = null;
            m_InstallingPackageName = "";
        }


        /// <summary>See <see href="https://docs.unity3d.com/ScriptReference/SettingsProvider.html">SettingsProvider documentation</see>.</summary>
        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            InitEditorData(currentSettings);
        }

        /// <summary>See <see href="https://docs.unity3d.com/ScriptReference/SettingsProvider.html">SettingsProvider documentation</see>.</summary>
        public override void OnDeactivate()
        {
            EditorApplication.update -= UpdatePackageManagerQuery;
            EditorApplication.update -= UpdatePackageInstallationQuery;
            m_SettingsWrapper = null;
            CachedSettingsEditor.Clear();
        }

        private void DisplayProviderSelectionUI()
        {
            EditorGUI.BeginDisabledGroup(!m_HasProviders);
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(Content.s_ProvidersToInstall, GetStyle("BoldLabel"));
            EditorGUILayout.Space();

            using (new EditorGUILayout.VerticalScope(GUI.skin.box, GUILayout.ExpandWidth(true)))
            {

                if (!m_HasCompletedRequest)
                {
                    EditorGUILayout.LabelField(Content.s_LookingForProviders);
                }
                else
                {
                    if (m_XRPackages.Any())
                    {
                        bool isInstalling = !String.IsNullOrEmpty(m_InstallingPackageName);

                        EditorGUI.BeginDisabledGroup(isInstalling);
                        foreach (var kv in m_XRPackages)
                        {
                            var isInstalled = kv.Value.isInstalled;
                            var pinfo = kv.Value.uninstalledPackageInfo;
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField(pinfo.displayName, GetStyle("label"));
                            if (isInstalling && String.Compare(m_InstallingPackageName, pinfo.name) == 0)
                            {
                                EditorGUILayout.LabelField(Content.s_InstallingPackage, GUILayout.Width(100));
                            }
                            else if (isInstalled)
                            {
                                EditorGUILayout.LabelField(Content.s_InstalledPackage, GUILayout.Width(100));
                            }
                            else
                            {
                                if (GUILayout.Button(Content.s_InstallPackage, GUILayout.Width(100)))
                                {
                                    m_InstallingPackageName = pinfo.name;
                                    m_InstallingPackage = PackageManager.Client.Add(pinfo.name);
                                    EditorApplication.update += UpdatePackageInstallationQuery;
                                }
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                        EditorGUI.EndDisabledGroup();
                    }
                    else
                    {
                        EditorGUILayout.LabelField(Content.s_NoInstallablePackages);
                    }
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.Space();

        }

        private void DisplayLoaderSelectionUI()
        {
            if (!m_HasCompletedRequest)
                return;

            if (!m_XRPackages.Any())
            {
                EditorGUILayout.HelpBox(Content.k_ProvidersUnavailable, MessageType.Error);
                return;
            }

            if (!m_HasInstalledProviders)
            {
                EditorGUILayout.HelpBox(Content.k_NeedToInstallAProvider, MessageType.Warning);
                return;
            }
            
            BuildTargetGroup buildTargetGroup = EditorGUILayout.BeginBuildTargetSelectionGrouping();
            bool buildTargetChanged = m_LastBuildTargetGroup != buildTargetGroup;
            if (buildTargetChanged)
                m_LastBuildTargetGroup = buildTargetGroup;

            XRGeneralSettings settings = currentSettings.SettingsForBuildTarget(buildTargetGroup);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<XRGeneralSettings>() as XRGeneralSettings;
                currentSettings.SetSettingsForBuildTarget(buildTargetGroup, settings);
                settings.name = $"{buildTargetGroup.ToString()} Settings";
                AssetDatabase.AddObjectToAsset(settings, AssetDatabase.GetAssetOrScenePath(currentSettings));
            }

            var serializedSettingsObject = new SerializedObject(settings);
            serializedSettingsObject.Update();

            SerializedProperty initOnStart = serializedSettingsObject.FindProperty("m_InitManagerOnStart");
            EditorGUILayout.PropertyField(initOnStart, Content.s_LoaderInitOnStartLabel);

            SerializedProperty loaderProp = serializedSettingsObject.FindProperty("m_LoaderManagerInstance");

            if (!CachedSettingsEditor.ContainsKey(buildTargetGroup))
            {
                CachedSettingsEditor.Add(buildTargetGroup, null);
            }

            if (loaderProp.objectReferenceValue == null)
            {
                var xrManagerSettings = ScriptableObject.CreateInstance<XRManagerSettings>() as XRManagerSettings;
                xrManagerSettings.name = $"{buildTargetGroup.ToString()} Providers";
                AssetDatabase.AddObjectToAsset(xrManagerSettings, AssetDatabase.GetAssetOrScenePath(currentSettings));
                loaderProp.objectReferenceValue = xrManagerSettings;
                serializedSettingsObject.ApplyModifiedProperties();
            }

            var obj = loaderProp.objectReferenceValue;

            if (obj != null)
            {
                loaderProp.objectReferenceValue = obj;

                if(CachedSettingsEditor[buildTargetGroup] == null)
                {
                    CachedSettingsEditor[buildTargetGroup] = Editor.CreateEditor(obj) as XRManagerSettingsEditor;

                    if (CachedSettingsEditor[buildTargetGroup] == null)
                    {
                        Debug.LogError("Failed to create a view for XR Manager Settings Instance");
                    }
                }

                if (CachedSettingsEditor[buildTargetGroup] != null)
                {
                    if (buildTargetChanged)
                    {
                        CachedSettingsEditor[buildTargetGroup].BuildTarget = buildTargetGroup;
                        CachedSettingsEditor[buildTargetGroup].Reload();
                    }
                    CachedSettingsEditor[buildTargetGroup].OnInspectorGUI();
                }
            }
            else if (obj == null)
            {
                settings.AssignedSettings = null;
                loaderProp.objectReferenceValue = null;
            }

            EditorGUILayout.EndBuildTargetSelectionGrouping();

            serializedSettingsObject.ApplyModifiedProperties();
        }

        /// <summary>See <see href="https://docs.unity3d.com/ScriptReference/SettingsProvider.html">SettingsProvider documentation</see>.</summary>
        public override void OnGUI(string searchContext)
        {
            if (m_SettingsWrapper != null  && m_SettingsWrapper.targetObject != null)
            {
                m_SettingsWrapper.Update();

                EditorGUILayout.Space();

                DisplayLoaderSelectionUI();

                EditorGUILayout.Space();

                DisplayProviderSelectionUI();

                m_SettingsWrapper.ApplyModifiedProperties();
            }

            base.OnGUI(searchContext);
        }
    }
}

