using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
[RequireComponent(typeof(NetworkView))]
public class PureVOIP : MonoBehaviour {
	public float volumeMultiplier = 30.0f;
	
	private AudioClip micClip;
	private List<byte[]> playbackQueue = new List<byte[]>();
	private bool transmissionStarted = false;
	
	private static readonly int SAMPLERATE = 8000;
	private static readonly int CHANNELS = 1;
	private static readonly int SAMPLE_DIVISOR = 3;
	private static readonly int RECORD_LENGTH = 60;
	
	private static readonly string LOG_PREFIX = "VOIP: ";
	
	
	/// <summary>
	/// Use this for initialization
	/// </summary>
	void Start() {
		if (Microphone.devices.Length == 0) {
			Debug.LogError(LOG_PREFIX + "No mic plugged in !");
			return;
		}
		
		networkView.observed = this;
		networkView.stateSynchronization = NetworkStateSynchronization.Off;
		
		if (networkView.isMine) {
			StartTransmission();
			
		} else {
			StartCoroutine("ProcessReceivedSamples");
		}
	}
	
	/// <summary>
	/// Starts the transmission.
	/// </summary>
	public void StartTransmission() {
		if (transmissionStarted) {
			StopTransmission();
		}
		
		// prepare microphone for recording
		micClip = Microphone.Start(null, true, RECORD_LENGTH, SAMPLERATE);
		while (!(Microphone.GetPosition(null) > 0)) {}
		
		// start coroutine which sends the microphone input
		StartCoroutine("TransmitMicData");
		transmissionStarted = true;
	}
	
	/// <summary>
	/// Stops the transmission.
	/// </summary>
	public void StopTransmission() {
		if (transmissionStarted) {
			Microphone.End(null);
			StopCoroutine("TransmitMicData");
			transmissionStarted = false;
		}
	}
	
	/// <summary>
	/// Transmits the mic data.
	/// </summary>
	/// <returns>
	/// The mic data.
	/// </returns>
	private IEnumerator TransmitMicData() {
		byte[] micSampleBytes, compressedSampleBytes;
		int currentSample = 0, lastSample = 0, delta = 0;
		
		while (true) {
			currentSample = Microphone.GetPosition(null);
			
			// reset last sample if the microphone starts recording
			// or loops
			if (currentSample <= SAMPLERATE/SAMPLE_DIVISOR)
				lastSample = 0;
			
			// calculate samples since last send
			delta = currentSample - lastSample;
			
			// send a half a second of audio record
			// so the playback delay at the other trainees is round about 
			// a half a second
			if (delta >= SAMPLERATE/SAMPLE_DIVISOR) {
				float[] micSamples = new float[delta * micClip.channels];
				micClip.GetData(micSamples, lastSample);
				lastSample = currentSample;
				
				// adjust volume
				for (int i=0; i<micSamples.Length; i++) {
					micSamples[i] *= volumeMultiplier;
				}
				
				// convert data to byte and compress it
				micSampleBytes = ToByteArray(micSamples);
				compressedSampleBytes = LZFCompressor.Compress(micSampleBytes);
				
				// send it
				networkView.RPC("ReceiveSamples", RPCMode.Others, compressedSampleBytes);
			}
			
			yield return null;
		}
	}
	
	/// <summary>
	/// Processes the received samples.
	/// </summary>
	/// <returns>
	/// The received samples.
	/// </returns>
	private IEnumerator ProcessReceivedSamples() {
		while (true) {
			if (playbackQueue.Count > 0) {
				float[] receivedSamples = ToFloatArray(playbackQueue[0]);
				
				// create a audioclip from received samples and play it
				AudioClip bufferedClip = AudioClip.Create("mic", receivedSamples.Length, 
														  CHANNELS, SAMPLERATE, 
														  true, false);
				
				bufferedClip.SetData(receivedSamples, 0);
				audio.clip = bufferedClip;
				audio.Play();
				
				Destroy(bufferedClip, 1.0f);
				playbackQueue.RemoveAt(0);
			}
			
			yield return null;
		}
	}
	
	/// <summary>
	/// Receives the samples.
	/// </summary>
	/// <param name='bytes'>
	/// Bytes.
	/// </param>
	[RPC]
    private void ReceiveSamples(byte[] bytes) {
		// convert the bytes back to floats and add it to the process list
		// do nothing else her so the next rpc can be called by sender
		// decompress
		byte[] decompressedSamples = LZFCompressor.Decompress(bytes);
		playbackQueue.Add(decompressedSamples);
	}
	
	/// <summary>
	/// Tos the byte array.
	/// </summary>
	/// <returns>
	/// The byte array.
	/// </returns>
	/// <param name='array'>
	/// Array.
	/// </param>
	private byte[] ToByteArray(float[] array) {
		byte[] bytes = new byte[array.Length*4];
		
		for (int i=0; i<array.Length; i++) {
			byte[] singleFloatVal = System.BitConverter.GetBytes(array[i]);
			
			bytes[i] = singleFloatVal[0];
			bytes[i+1] = singleFloatVal[1];
			bytes[i+2] = singleFloatVal[2];
			bytes[i+3] = singleFloatVal[3];
		}
		
		return bytes;
	} 
	
	/// <summary>
	/// Tos the float array.
	/// </summary>
	/// <returns>
	/// The float array.
	/// </returns>
	/// <param name='array'>
	/// Array.
	/// </param>
	private float[] ToFloatArray(byte[] array) {
		float[] floats = new float[array.Length/4];
		
		for (int i=0; i<array.Length; i+=4) {
			floats[i/4] = System.BitConverter.ToSingle(array, i);
		}
		
		return floats;
	}
}
