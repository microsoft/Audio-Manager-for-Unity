﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Microsoft.MixedReality.Toolkit.Audio
{
    /// <summary>
    /// The runtime event of an Audio Event that is currently playing
    /// </summary>
    [System.Serializable]
    public class ActiveEvent
        {
        /// <summary>
        /// The name of the audio event to be played
        /// </summary>
        public string name = "";
        /// <summary>
        /// The AudioEvent that is being played
        /// </summary>
        public AudioEvent rootEvent { get; private set; }
        /// <summary>
        /// The AudioSource to play the AudioEvent on
        /// </summary>
        public List<EventSource> sources { get; private set; } = new List<EventSource>();
        /// <summary>
        /// The latest status of the event
        /// </summary>
        public EventStatus status = EventStatus.Initialized;
        /// <summary>
        /// The AudioParameters in use by the event
        /// </summary>
        private ActiveParameter[] activeParameters;
        public string timeStarted = "";
        /// <summary>
        /// The Transform to use to calculate the user's gaze position
        /// </summary>
        private Transform gazeReference;
        /// <summary>
        /// The transform that the sound should follow in the scene
        /// </summary>
        internal Transform emitterTransform = null;
        /// <summary>
        /// The text associated with the event, usually for subtitles
        /// </summary>
        internal string text = "";
        /// <summary>
        /// Time in seconds before the audio file will start playing
        /// </summary>
        internal float initialDelay = 0;
        /// <summary>
        /// The volume of the event before parameters are applied
        /// </summary>
        private float eventVolume = 0;
        /// <summary>
        /// The volume that the event is fading to or settled on after fading
        /// </summary>
        private float targetVolume = 1;
        /// <summary>
        /// The pitch value 
        /// </summary>
        private float eventPitch = 1;
        /// <summary>
        /// The amount of time in seconds that the event will fade in or out
        /// </summary>
        private float targetFadeTime = 0;
        /// <summary>
        /// The amount of time in seconds that the event has been fading in or out
        /// </summary>
        private float currentFadeTime = 0;
        /// <summary>
        /// The amount of time in seconds since the event started
        /// </summary>
        private float elapsedTime = 0;
        /// <summary>
        /// The previous volume the event was at before starting a fade
        /// </summary>
        private float fadeOriginVolume = 0;
        /// <summary>
        /// Whether the event is currently fading out to be stopped
        /// </summary>
        private bool fadeStopQueued = false;
        /// <summary>
        /// Whether the event has played a sound yet
        /// </summary>
        private bool hasPlayed = false;
        /// <summary>
        /// Whether the event is using the user's gaze for a parameter
        /// </summary>
        private bool useGaze = false;
        private bool callbackActivated = false;
        private bool hasTimeAdvanced = false;
        internal bool hasSnapshotTransition = false;

        /// <summary>
        /// The time left on the event unless it is stopped or the pitch changes
        /// </summary>
        public float EstimatedRemainingTime { get; private set; }
        /// <summary>
        /// Whether the event has been muted
        /// </summary>
        public bool Muted { get; private set; } = false;
        /// <summary>
        /// Whether the event has been soloed
        /// </summary>
        public bool Soloed { get; private set; } = false;

        /// <summary>
        /// Delegate for event completion callback
        /// </summary>
        public delegate void EventCompleted();

        /// <summary>
        /// Callback when the event stops
        /// </summary>
        public event EventCompleted CompletionCallback;

        /// <summary>
        /// Constructor: Create a new ActiveEvent from an AudioEvent and play it on an AudioSource
        /// </summary>
        /// <param name="eventToPlay">The AudioEvent to play</param>
        /// <param name="source">The AudioSource use for the AudioEvent</param>
        public ActiveEvent(AudioEvent eventToPlay, Transform emitterTransform)
        {
            this.rootEvent = eventToPlay;
            this.name = eventToPlay.name;
            this.emitterTransform = emitterTransform;

            InitializeParameters();
        }

        /// <summary>
        /// Internal AudioManager use: starts the audio event
        /// </summary>
        public void Play()
        {
            this.timeStarted = Time.time.ToString();
            this.rootEvent.SetActiveEventProperties(this);

            if (this.sources.Count == 0 && !this.hasSnapshotTransition)
            {
                this.status = EventStatus.Error;
                AudioManager.AddPreviousEvent(this);
                return;
            }

            SetStartingSourceProperties();

            this.status = EventStatus.Played;

            AudioManager.ActiveEvents.Add(this);
            AudioManager.AddPreviousEvent(this);
        }

        /// <summary>
        /// Internal AudioManager use: update fade and RTPC values
        /// </summary>
        public void Update()
        {
            if (this.rootEvent == null)
            {
                Debug.LogWarningFormat(this.emitterTransform, "AudioManager: Can't find audio event for {0}!", this.name);
                StopImmediate();
                return;
            }

            this.elapsedTime += Time.deltaTime;

            if (!this.hasPlayed && this.elapsedTime >= this.initialDelay)
            {
                this.hasPlayed = true;
                PlayAllSources();
            }

            if (this.emitterTransform != null)
            {
                SetAllSourcePositions(this.emitterTransform.position);
            }

            if (this.hasPlayed && this.currentFadeTime < this.targetFadeTime)
            {
                UpdateFade();
            }

            if (!this.rootEvent.Output.loop)
            {
                UpdateRemainingTime();
                if (this.hasPlayed && !IsAnySourcePlaying())
                {
                    StopImmediate();
                }
            }

            if (this.useGaze)
            {
                UpdateGaze();
            }

            UpdateParameters();
            ApplyParameters();
        }

        /// <summary>
        /// Stops the event using the default fade time if applicable
        /// </summary>
        public void Stop()
        {
            if (this.rootEvent.FadeOut <= 0)
            {
                StopImmediate();
            }
            else
            {
                this.targetVolume = 0;
                this.fadeOriginVolume = this.sources[0].source.volume;
                this.targetFadeTime = this.rootEvent.FadeOut;
                this.currentFadeTime = 0;
                this.fadeStopQueued = true;
            }
        }

        /// <summary>
        /// Stops the event immediately, ignoring the event's fade time
        /// </summary>
        public void StopImmediate()
        {
            if (this.CompletionCallback != null && !this.callbackActivated)
            {
                this.callbackActivated = true;
                CompletionCallback();
            }

            this.status = EventStatus.Stopped;

            StopAllSources();

            AudioManager.DelayRemoveActiveEvent(this);
        }

        /// <summary>
        /// Set the value of a parameter on this event independent of the root parameter's value
        /// </summary>
        /// <param name="localParameter">The AudioParameter set on the root AudioEvent</param>
        /// <param name="newValue">The new local value on this event instance (does not set the AudioParameter value)</param>
        public void SetLocalParameter(AudioParameter localParameter, float newValue)
        {
            bool hasParameter = false;
            for (int i = 0; i < this.activeParameters.Length; i++)
            {
                ActiveParameter tempParameter = this.activeParameters[i];
                if (tempParameter.rootParameter.parameter == localParameter)
                {
                    hasParameter = true;
                    tempParameter.CurrentValue = newValue;
                }
            }

            if (!hasParameter)
            {
                //Debug.LogWarningFormat("Audio event {0} does not have parameter {1}", this.rootEvent.name, localParameter.name);
            }
        }

        public bool AddEventSource(AudioClip clip, AudioParameter parameter = null, AnimationCurve responseCurve = null, float startTime = 0)
        {
            if (this.rootEvent == null)
            {
                Debug.LogWarningFormat(this.emitterTransform, "AudioManager: Can't find audio event for {0}!", this.name);
                StopImmediate();
                return false;
            }

            EventSource newSource = new EventSource();
            newSource.source = AudioManager.GetUnusedSource();

            if (newSource.source == null)
            {
                Debug.LogWarningFormat(this.emitterTransform, "AudioManager: Can't find unused audio source for event {0}!", this.name);
                StopImmediate();
                return false;
            }

            newSource.source.loop = this.rootEvent.Output.loop;
            newSource.source.clip = clip;
            newSource.parameter = parameter;
            newSource.responseCurve = responseCurve;
            newSource.startTime = startTime;
            sources.Add(newSource);

            return true;
        }

        /// <summary>
        /// Internal AudioManager use: initializes the volume based on the event's AudioOutput
        /// </summary>
        /// <param name="newVolume">The target volume for the AudioSource (not necessarily the current volume)</param>
        public void SetVolume(float newVolume)
        {
            this.targetVolume = newVolume;
        }

        /// <summary>
        /// Offset the volume by the specified amount
        /// </summary>
        /// <param name="volumeDelta">A number between -1 and 1 for volume changes</param>
        public void ModulateVolume(float volumeDelta)
        {
            this.targetVolume += volumeDelta;
        }

        /// <summary>
        /// Overwrite the pitch with a new value
        /// </summary>
        /// <param name="newPitch">Pitch value between -1 and 3</param>
        public void SetPitch(float newPitch)
        {
            if (newPitch <= 0)
            {
                Debug.LogWarningFormat("Invalid pitch set in event {0}", this.rootEvent.name);
                return;
            }

            this.eventPitch = newPitch;
        }

        /// <summary>
        /// Offset the pitch by the specified amount
        /// </summary>
        /// <param name="pitchDelta">A number between -1 and 3 for pitch changes</param>
        public void ModulatePitch(float pitchDelta)
        {
            this.eventPitch += pitchDelta;
        }

        public void SetEmitterPosition(Vector3 newPos)
        {
            if (this.sources.Count > 0)
            {
                this.sources[0].source.transform.position = newPos;
            }
            else
            {
                Debug.LogWarningFormat(this.emitterTransform, "No audio emitter for active event {0}", this.name);
            }
        }

        #region Source Property Settings

        private void SetStartingSourceProperties()
        {
            if (this.rootEvent.FadeIn > 0)
            {
                SetallSourceVolumes(0);
                this.eventVolume = 0;
                this.fadeOriginVolume = 0;
                this.currentFadeTime = 0;
                this.targetFadeTime = this.rootEvent.FadeIn;
            }
            else
            {
                SetallSourceVolumes(this.targetVolume);
                this.eventVolume = this.targetVolume;
            }

            SetAllSourcePitches(this.eventPitch);

            this.useGaze = HasGazeProperty();
            if (this.useGaze)
            {
                this.gazeReference = Camera.main.transform;
                UpdateGaze();
            }

            ApplyParameters();

            if (this.initialDelay == 0)
            {
                this.hasPlayed = true;
                PlayAllSources();
            }
        }

        private void SetallSourceVolumes(float newVolume)
        {
            for (int i = 0; i < this.sources.Count; i++)
            {
                this.sources[i].source.volume = newVolume;
            }
        }

        private void SetAllSourcePitches(float newPitch)
        {
            for (int i = 0; i < this.sources.Count; i++)
            {
                this.sources[i].source.pitch = newPitch;
            }
        }

        private void PlayAllSources()
        {
            for (int i = 0; i < this.sources.Count; i++)
            {
                this.sources[i].source.Play();
                this.sources[i].source.time = this.sources[i].startTime;
            }
        }

        private void StopAllSources()
        {
            for (int i = 0; i < this.sources.Count; i++)
            {
                EventSource tempSource = this.sources[i];
                if (tempSource != null && tempSource.source != null)
                {
                    tempSource.source.Stop();
                }
            }
        }

        public void SetAllSourcePositions(Vector3 position)
        {
            for (int i = 0; i < this.sources.Count; i++)
            {
                this.sources[i].source.transform.position = position;
            }
        }

        private bool IsAnySourcePlaying()
        {
            for (int i = 0; i < this.sources.Count; i++)
            {
                if (this.sources[i].source.isPlaying)
                {
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Private Functions

        private void InitializeParameters()
        {
            this.activeParameters = new ActiveParameter[this.rootEvent.Parameters.Count];
            for (int i = 0; i < this.activeParameters.Length; i++)
            {
                if (this.rootEvent.Parameters[i] != null)
                {
                    this.activeParameters[i] = new ActiveParameter(this.rootEvent.Parameters[i]);
                }
            }
        }

        /// <summary>
        /// Internal AudioManager use: Sync changes to the parameters on all ActiveEvents
        /// </summary>
        private void UpdateParameters()
        {
            for (int i = 0; i < this.activeParameters.Length; i++)
            {
                AudioEventParameter tempParam = this.activeParameters[i].rootParameter;
                if (tempParam == null || tempParam.parameter == null)
                {
                    continue;
                }

                if (tempParam.CurrentValue != tempParam.parameter.CurrentValue)
                {
                    tempParam.SyncParameter();
                }
            }
        }

        /// <summary>
        /// Internal AudioManager use: applies parameter changes
        /// </summary>
        private void ApplyParameters()
        {
            float tempVolume = this.eventVolume;
            float tempPitch = this.eventPitch;
            for (int i = 0; i < this.activeParameters.Length; i++)
            {
                ActiveParameter tempParameter = this.activeParameters[i];
                switch (tempParameter.rootParameter.paramType)
                {
                    case ParameterType.Volume:
                        tempVolume *= tempParameter.CurrentResult;
                        break;
                    case ParameterType.Pitch:
                        tempPitch *= tempParameter.CurrentResult;
                        break;
                }
            }

            for (int i = 0; i < this.sources.Count; i++)
            {
                EventSource tempSource = this.sources[i];
                tempSource.source.volume = tempVolume;
                tempSource.source.pitch = tempPitch;
                if (tempSource.parameter != null)
                {
                    float volumeScale = tempSource.responseCurve.Evaluate(tempSource.parameter.CurrentValue);
                    tempSource.source.volume = this.eventVolume * volumeScale;
                }
            }
        }

        /// <summary>
        /// Internal AudioManager use: update volume on ActiveEvents fading in and out
        /// </summary>
        private void UpdateFade()
        {
            float percentageFaded = (this.currentFadeTime / this.targetFadeTime);

            if (this.targetVolume > this.fadeOriginVolume)
            {
                this.eventVolume = this.fadeOriginVolume + ((this.targetVolume - this.fadeOriginVolume) * percentageFaded);
            }
            else
            {
                this.eventVolume = this.fadeOriginVolume - ((this.fadeOriginVolume - this.targetVolume) * percentageFaded);
            }

            this.currentFadeTime += Time.deltaTime;

            if (this.currentFadeTime >= this.targetFadeTime)
            {
                this.eventVolume = this.targetVolume;

                if (this.fadeStopQueued)
                {
                    StopImmediate();
                }
            }

            SetallSourceVolumes(this.eventVolume);
        }

        /// <summary>
        /// Recalculate estimated time the event will be active for
        /// </summary>
        private void UpdateRemainingTime()
        {
            if (this.rootEvent == null)
            {
                Debug.LogWarningFormat(this.emitterTransform, "AudioManager: Can't find audio event for {0}!", this.name);
                StopImmediate();
                return;
            }

            AudioSource mainSource = this.sources[0].source;
            if (mainSource == null || mainSource.clip == null || mainSource.clip.length <= 0)
            {
                StopImmediate();
                return;
            }

            if (this.rootEvent.Output.loop)
            {
                this.EstimatedRemainingTime = 500;
            }
            else
            {
                this.EstimatedRemainingTime = (mainSource.clip.length - mainSource.time) / mainSource.pitch;
            }

            if (this.hasTimeAdvanced && mainSource.time == 0)
            {
                StopImmediate();
            }

            if (this.EstimatedRemainingTime <= this.rootEvent.FadeOut)
            {
                Stop();
            }
        }

        /// <summary>
        /// Check if any of the parameters on the event use gaze for the value
        /// </summary>
        /// <returns>Whether at least one parameter uses gaze</returns>
        private bool HasGazeProperty()
        {
            for (int i = 0; i < this.rootEvent.Parameters.Count; i++)
            {
                AudioParameter tempParam = this.rootEvent.Parameters[i].parameter;

                if (tempParam == null)
                {
                    Debug.LogWarningFormat("Audio event '{0}' has a null parameter!", this.rootEvent.name);
                    continue;
                }

                if (tempParam.UseGaze)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Get the user's head position relative to the audio event, and set the property's value
        /// </summary>
        private void UpdateGaze()
        {
            AudioSource mainSource = this.sources[0].source;
            float gazeAngle = 0;
            if (this.gazeReference == null)
            {
                this.gazeReference = Camera.main.transform;
            }

            Vector3 posDelta = this.gazeReference.position - mainSource.transform.position;
            gazeAngle = Mathf.Abs(180 - Vector3.Angle(this.gazeReference.forward, posDelta));

            for (int i = 0; i < this.activeParameters.Length; i++)
            {
                ActiveParameter tempParameter = this.activeParameters[i];
                if (tempParameter.rootParameter.parameter.UseGaze)
                {
                    tempParameter.CurrentValue = gazeAngle;
                }
            }
        }

        #endregion

        #region Editor

        /// <summary>
        /// Set whether the defined parameter on the event uses the player's gaze angle
        /// </summary>
        /// <param name="toggle"></param>
        /// <param name="gazeParameter"></param>
        public void ToggleGaze(bool toggle, AudioParameter gazeParameter)
        {
            this.useGaze = toggle;

            if (!toggle)
            {
                for (int i = 0; i < this.activeParameters.Length; i++)
                {
                    ActiveParameter tempParameter = this.activeParameters[i];
                    if (tempParameter.rootParameter.parameter == gazeParameter)
                    {
                        tempParameter.Reset();
                    }
                }
            }
        }

        /// <summary>
        /// Toggles whether the event is audible
        /// </summary>
        public void ToggleMute()
        {
            SetMute(!this.Muted);
        }

        /// <summary>
        /// Sets whether the event is audible
        /// </summary>
        /// <param name="toggle">Whether sound should be made inaudible</param>
        public void SetMute(bool toggle)
        {
            this.Muted = toggle;
            for (int i = 0; i < this.sources.Count; i++)
            {
                this.sources[i].source.mute = toggle;
            }
        }

        /// <summary>
        /// Toggles whether only this sound (and other soloed sounds) are audible
        /// </summary>
        public void ToggleSolo()
        {
            this.Soloed = !this.Soloed;
            AudioManager.ApplyActiveSolos();
        }

        /// <summary>
        /// Mutes all other non-soloed events
        /// </summary>
        /// <param name="toggle">Whether this event is part of the isolated audible events</param>
        public void SetSolo(bool toggle)
        {
            this.Soloed = toggle;
            AudioManager.ApplyActiveSolos();
        }

        /// <summary>
        /// Internal AudioManager use: applies solo property to AudioSource, ignoring "mute" property
        /// </summary>
        public void ApplySolo()
        {
            SetMute(!this.Soloed);
        }

        /// <summary>
        /// Internal AudioManager use: clears all solo properties, and reverts to mute property
        /// </summary>
        public void ClearSolo()
        {
            this.Soloed = false;
            SetMute(this.Muted);
        }

#if UNITY_EDITOR

        public void DisplayProperties()
        {
            if (this.sources.Count == 0 || this.sources[0] == null || this.sources[0].source == null)
            {
                return;
            }

            if (emitterTransform != null)
            {
                EditorGUILayout.LabelField("Emitter:" + emitterTransform.gameObject.name);
            }
            else
            {
                EditorGUILayout.LabelField("Emitter:" + this.sources[0].source.gameObject.name);
            }
        }

        protected void DrawWindow(int id)
        {
            GUI.DragWindow();
            DisplayProperties();
        }

        public virtual void DrawNode(int id, Rect window)
        {
            GUI.Window(id, window, DrawWindow, this.name);
        }

#endif

#endregion
    }
    public class EventSource
    {
        public AudioSource source = new AudioSource();
        public AudioParameter parameter = null;
        public AnimationCurve responseCurve = null;
        public float startTime = 0;
    }

    public enum EventStatus
    {
        Initialized,
        Played,
        Stopped,
        Error
    }
}
