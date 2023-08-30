﻿using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Microsoft.MixedReality.Toolkit.Audio
{
    public class AudioSwitch : ScriptableObject
    {
        private int defaultValue = 0;
        public int CurrentValue { get; private set; }

        public void InitializeSwitch()
        {
            this.CurrentValue = this.defaultValue;
        }

        public void ResetSwitch()
        {
            this.CurrentValue = this.defaultValue;
        }

        public void SetValue(int newValue)
        {
            if (newValue == this.CurrentValue)
            {
                return;
            }

            this.CurrentValue = newValue;
        }

#if UNITY_EDITOR

        public void DrawSwitchEditor()
        {
            this.name = EditorGUILayout.TextField("Name", this.name);
            this.defaultValue = EditorGUILayout.IntField("Default Value", this.defaultValue);
        }

#endif
    }
}