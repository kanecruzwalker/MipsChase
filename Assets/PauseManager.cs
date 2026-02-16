using JetBrains.Annotations;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 
/// Manages game pause/resume and audio settings.
/// 
/// Controls:
///     - Escape key toggles pause on/off
///     - Pause freezes game time (Time.timeScale = 0)
///     - Volume slider adjusts taunt audio in real time
///     
/// Note: Time.timeScale = 0 freezes FixedUpdate and time-based operations but UI remains interactive
/// </summary>
/// 

public class PauseManager : MonoBehaviour
{
    // UI References - assign in Inspector
    public GameObject m_pauseMenuPanel;     // The pause menu panel
    public Slider m_volumeSlider;           // Volume control slider
    public Target m_target;                 // Reference to Target for audio control
    public Button m_muteButton;             // Mute toggle button
    private bool m_bIsMuted = false;        // Track mute state
    private float m_fPreMuteVolume = 0.3f;  // Store volume before muting

    // Internal state
    private bool m_bIsPaused = false;

     
    // Start is called before the first frame update
    void Start()
    {
        // Ensure game starts unpaused with menu hidden
        m_pauseMenuPanel.SetActive(false);
        Time.timeScale = 1f;

        // Initialize slider to match current audio volume
        if(m_volumeSlider != null)
        {
            m_volumeSlider.value = 0.3f;
            m_volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        }
    }

    // Update is called once per frame
    void Update()
    {
        // Check for pause input - Escape key toggles pause.
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (m_bIsPaused)
            {
                ResumeGame();
            }
            else
            {
                PauseGame();
            }
        }
    }


    ///<summary>
    /// 
    /// Pause the game by freezing time and showing the pause menu
    /// Time.timeScale = 0 stops all FixedUpdate and time-dependent logic.
    /// 
    /// </summary>

    public void PauseGame()
    {
        m_pauseMenuPanel.SetActive(true);
        Time.timeScale = 0f;
        m_bIsPaused = true;
    }


    ///<summary>
    ///
    /// Resumes the game by restoring time and hiding the pause menu
    /// 
    /// </summary>

    public void ResumeGame()
    {
        m_pauseMenuPanel.SetActive(false);
        Time.timeScale = 1f;
        m_bIsPaused = false;
    }



    ///<summary>
    ///
    /// Called when the volume slider value changes
    /// Updates the Target's AudioSource volume in real time.
    /// 
    /// </summary>
    

    void OnVolumeChanged(float value)
    {
        if (m_target != null && m_target.m_audioSource != null)
        {
            // Cap volume at 50% to prevent the taunt from being too loud
            m_target.m_audioSource.volume = value * 0.3f;
        }

        // If user moves slider while muted? unmute
        if(m_bIsMuted && value > 0f)
        {
            m_bIsMuted = false;
            m_muteButton.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "Mute";
        }
    }




    ///<summary>
    ///
    /// Toggles mute on/off. When muting, stores current volume and sets to 0.
    /// When unmuting, restores the previous volume level.
    /// Updates button text to reflect current state
    /// 
    /// </summary>
    /// 

    public void ToggleMute()
    {
        m_bIsMuted = !m_bIsMuted;

        if (m_bIsMuted)
        {
            // Store current volume and mute
            m_fPreMuteVolume = m_volumeSlider.value;
            m_volumeSlider.value = 0f;
            m_muteButton.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "Unmute";
        }
        else
        {
            // Restore previous volume
            m_volumeSlider.value = m_fPreMuteVolume;
            m_muteButton.GetComponentInChildren<TMPro.TextMeshProUGUI>().text = "Mute";
            PreviewAudio();
        }
    }





    ///<summary>
    ///
    /// Plays a preview of the taunt audio so the user can hear
    /// the current volume level while adjusting settings.
    /// 
    /// </summary>
    /// 
    public void PreviewAudio()
    {
        if(m_target != null && m_target.m_audioSource != null && m_target.m_tauntClip != null)
        {
            m_target.m_audioSource.PlayOneShot(m_target.m_tauntClip);
        }
    }
}
