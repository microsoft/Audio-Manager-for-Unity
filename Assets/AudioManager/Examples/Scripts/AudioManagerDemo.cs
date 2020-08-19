// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.MixedReality.Toolkit.Audio;
using UnityEngine;

public class AudioManagerDemo : MonoBehaviour
{
    [SerializeField]
    private KeyCode testKey = KeyCode.F;
    [SerializeField]
    private AudioEvent testEvent = null;

    private void Update()
    {
        if (Input.GetKeyDown(testKey))
        {
            AudioManager.PlayEvent(testEvent, gameObject);
        }
    }
}