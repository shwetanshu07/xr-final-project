using UnityEngine;
using MidiPlayerTK;

public class MidiUATTest : MonoBehaviour
{
    public MidiFilePlayer midiPlayer;

    void Update()
    {
        if (midiPlayer == null) return;

        // Mute ALL channels - press M
        if (Input.GetKeyDown(KeyCode.M))
        {
            for (int i = 0; i < 16; i++)
                midiPlayer.MPTK_Channels[i].Enable = false;
            Debug.Log("ALL MUTED - should hear silence");
        }

        // Unmute ALL channels - press U
        if (Input.GetKeyDown(KeyCode.U))
        {
            for (int i = 0; i < 16; i++)
                midiPlayer.MPTK_Channels[i].Enable = true;
            Debug.Log("ALL UNMUTED - full orchestra returns");
        }

        // Mute channel 0 only - press 1
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            midiPlayer.MPTK_Channels[0].Enable = false;
            Debug.Log("Channel 0 muted - one instrument should disappear");
        }

        // Unmute channel 0 - press 2
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            midiPlayer.MPTK_Channels[0].Enable = true;
            Debug.Log("Channel 0 unmuted - instrument returns");
        }

        // Speed up - press T
        if (Input.GetKeyDown(KeyCode.T))
        {
            midiPlayer.MPTK_Speed = 5f;
            Debug.Log("Tempo 150% - should be faster");
        }

        // Slow down - press Y
        if (Input.GetKeyDown(KeyCode.Y))
        {
            midiPlayer.MPTK_Speed = 0.1f;
            Debug.Log("Tempo 50% - should be slower");
        }

        // Reset tempo - press R
        if (Input.GetKeyDown(KeyCode.R))
        {
            midiPlayer.MPTK_Speed = 1.0f;
            Debug.Log("Tempo normal");
        }
    }
}