using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Reflection;

using UnityEditorInternal;
using UnityEngine;
using UnityEngine.XR.Management;

namespace UnityEditor.XR.Management
{

    internal interface IXRLoaderOrderManager
    {
        List<XRLoaderInfo> AssignedLoaders { get; }
        List<XRLoaderInfo> UnassignedLoaders { get; }

        void AssignLoader(XRLoaderInfo assignedInfo);
        void UnassignLoader(XRLoaderInfo unassignedInfo);
        void Update();
    }

    internal class XRLoaderOrderUI
    {
        const string k_AtLeastOneLoaderInstance = "Must add at least one installed plugin provider to use this platform for XR.";
        const string k_AtNoLoaderInstance = "There are no installed plugin providers available for this platform.";

        IXRLoaderOrderManager m_Manager = null;

        ReorderableList m_OrderedList = null;

        internal XRLoaderOrderUI(IXRLoaderOrderManager manager = null)
        {
            m_Manager = manager;
        }

        void DrawElementCallback(Rect rect, int index, bool isActive, bool isFocused)
        {
            XRLoaderInfo info = index < m_Manager.AssignedLoaders.Count ? m_Manager.AssignedLoaders[index] : null;
            var label = (info == null || info.instance == null) ? EditorGUIUtility.TrTextContent("Missing (XRLoader)") : EditorGUIUtility.TrTextContent(info.assetName);
            EditorGUI.LabelField(rect, label);
        }

        float GetElementHeight(int index)
        {
            return m_OrderedList.elementHeight;
        }

        void ReorderLoaderList(ReorderableList list)
        {
            m_Manager.Update();
        }

        void DrawAddDropdown(Rect rect, ReorderableList list)
        {
            GenericMenu menu = new GenericMenu();

            int index = 0;
            if (m_Manager.UnassignedLoaders.Count > 0)
            {
                foreach (var info in m_Manager.UnassignedLoaders)
                {
                    string name = info.assetName;
                    if (String.IsNullOrEmpty(name) && info.loaderType != null)
                    {
                        name = EditorUtilities.TypeNameToString(info.loaderType);
                    }

                    menu.AddItem(new GUIContent(string.Format("{0}. {1}", index + 1, name)), false, AddLoaderMenuSelected, index);
                    index++;
                }
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("No Assignable Providers"));
            }

            menu.ShowAsContext();
        }

        void AddLoaderMenuSelected(object data)
        {
            int selected = (int)data;
            XRLoaderInfo info = m_Manager.UnassignedLoaders[selected];

            AddLoaderMenu(info);
        }

        // TODO: Move out to manager
        void AddLoaderMenu(XRLoaderInfo info)
        {
            if (info.instance == null)
            {
                string newAssetName = String.Format("{0}.asset", EditorUtilities.TypeNameToString(info.loaderType));
                XRLoader loader = ScriptableObject.CreateInstance(info.loaderType) as XRLoader;
                string assetPath = EditorUtilities.GetAssetPathForComponents(EditorUtilities.s_DefaultLoaderPath);
                if (string.IsNullOrEmpty(assetPath))
                {
                    return;
                }

                assetPath = Path.Combine(assetPath, newAssetName);
                info.instance = loader;
                info.assetName = Path.GetFileNameWithoutExtension(assetPath);
                AssetDatabase.CreateAsset(loader, assetPath);
            }

            m_Manager.AssignLoader(info);
        }

        void RemoveInstanceFromList(ReorderableList list)
        {
            XRLoaderInfo info = m_Manager.AssignedLoaders[list.index];
            m_Manager.UnassignLoader(info);
        }

        internal bool OnGUI()
        {
            if (!m_Manager.AssignedLoaders.Any() && !m_Manager.UnassignedLoaders.Any())
            {
                EditorGUILayout.HelpBox(k_AtNoLoaderInstance, MessageType.Warning);
                return false;
            }
            else if (!m_Manager.AssignedLoaders.Any())
            {
                EditorGUILayout.HelpBox(k_AtLeastOneLoaderInstance, MessageType.Warning);
            }

            if (m_OrderedList == null)
            {
                m_OrderedList = new ReorderableList(m_Manager.AssignedLoaders, typeof(XRLoader), true, true, true, true);
                m_OrderedList.drawHeaderCallback = (rect) => GUI.Label(rect, EditorGUIUtility.TrTextContent("Plugin Providers"), EditorStyles.label);
                m_OrderedList.drawElementCallback = (rect, index, isActive, isFocused) => DrawElementCallback(rect, index, isActive, isFocused);
                m_OrderedList.elementHeightCallback = (index) => GetElementHeight(index);
                m_OrderedList.onReorderCallback = (list) => ReorderLoaderList(list);
                m_OrderedList.onAddDropdownCallback = (rect, list) => DrawAddDropdown(rect, list);
                m_OrderedList.onRemoveCallback = (list) => RemoveInstanceFromList(list);
            }

            m_OrderedList.DoLayoutList();

            return false;
        }
    }
}
