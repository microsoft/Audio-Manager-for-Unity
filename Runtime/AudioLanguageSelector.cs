﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using UnityEngine;

namespace Microsoft.MixedReality.Toolkit.Audio
{
    /// <summary>
    /// An AudioNode for branching an AudioEvent based on the AudioManager's language setting
    /// </summary>
    public class AudioLanguageSelector : AudioNode
    {
        /// <summary>
        /// Select a node with the current language in the AudioManager
        /// </summary>
        /// <param name="activeEvent">The existing runtime audio event</param>
        public override void ProcessNode(ActiveEvent activeEvent)
        {
            if (this.input.ConnectedNodes == null || this.input.ConnectedNodes.Length == 0)
            {
                Debug.LogWarningFormat("No connected nodes for {0}", this.name);
                return;
            }

            for (int i = 0; i < this.input.ConnectedNodes.Length; i++)
            {
                AudioNode tempNode = this.input.ConnectedNodes[i].ParentNode;
                if (tempNode.GetType() == typeof(AudioVoiceFile))
                {
                    AudioVoiceFile voiceNode = (AudioVoiceFile)tempNode;
                    if (voiceNode.Language == AudioManager.CurrentLanguage)
                    {
                        ProcessConnectedNode(i, activeEvent);
                        return;
                    }
                }
            }

            Debug.LogErrorFormat("AudioManager: Event \"{0}\" not localized for language: {1}", activeEvent.name, AudioManager.CurrentLanguage);
        }

#if UNITY_EDITOR

        /// <summary>
        /// EDITOR: Set the initial values for the node's properties
        /// </summary>
        /// <param name="position">The position of the node on the graph</param>
        public override void InitializeNode(Vector2 position)
        {
            this.name = "Language Selector";
            this.nodeRect.height = 50;
            this.nodeRect.width = 150;
            this.nodeRect.position = position;
            AddInput();
            AddOutput();
        }

#endif

    }
}