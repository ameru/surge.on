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
    [CustomEditor(typeof(XRManagerSettings))]
    internal class XRManagerSettingsEditor : Editor
    {
        XRLoaderOrderUI m_LoadOrderUI = null;

        XRLoaderInfoManager m_LoaderInfoManager = new XRLoaderInfoManager();

        internal BuildTargetGroup BuildTarget
        {
            get { return m_LoaderInfoManager.BuildTarget;  }
            set { m_LoaderInfoManager.BuildTarget = value; }
        }

        void OnEnable()
        {
            m_LoaderInfoManager.OnEnable();
            if (m_LoadOrderUI == null)
            {
                m_LoadOrderUI = new XRLoaderOrderUI(m_LoaderInfoManager);
            }
        }

        void OnDisable()
        {
            m_LoaderInfoManager.OnDisable();
        }

        void PopulateProperty(string propertyPath, ref SerializedProperty prop)
        {
            if (prop == null) prop = serializedObject.FindProperty(propertyPath);
        }

        public void Reload()
        {
            m_LoaderInfoManager.ShouldReload = true;
        }

        /// <summary><see href="https://docs.unity3d.com/ScriptReference/Editor.OnInspectorGUI.html">Editor Documentation</see></summary>
        public override void OnInspectorGUI()
        {
            if (serializedObject == null || serializedObject.targetObject == null)
                return;

            serializedObject.Update();

            m_LoaderInfoManager.SerializedObjectData = serializedObject;

            if (m_LoaderInfoManager.ShouldReload)
                m_LoaderInfoManager.ReloadData();

            m_LoadOrderUI.OnGUI();

            serializedObject.ApplyModifiedProperties();
        }
    }

}
