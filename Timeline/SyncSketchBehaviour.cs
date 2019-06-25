using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using SyncSketch;

namespace SyncSketch
{
	namespace Timeline
	{
		[Serializable]
		public class SyncSketchBehaviour : PlayableBehaviour
		{
			public string filenameSuffix = "";
			[Tooltip("Multiplier over the recording resolution")]
			public float resolutionMultiplier = 1;
			public bool overrideOutputFile;
			public FilePath outputFile = new FilePath("Timeline Recording", "mp4");
			public bool overrideResolution;
			public int width = 1280;
			public int height = 720;
			[Tooltip("Use the selected resolution in the Game View (will be clamped to 1280 maximum width)")]
			public bool useGameResolution;
			[Tooltip("Keep the same aspect ratio as in the Game View")]
			public bool keepAspectRatio = true;

			SyncSketchRecorder recorder;
			SyncSketchRecorder.RecordingSettings originalSettings;

			// Note: we can't use OnBehaviourPlay() because we're missing the playerData parameter (= the SyncSketchRecorder instance)
			public override void ProcessFrame(Playable playable, FrameData info, object playerData)
			{
				if (playerData == null)
				{
					return;
				}

				if (!Application.isPlaying)
				{
					return;
				}

				// fetch the recorder
				if (recorder == null)
				{
					recorder = (SyncSketchRecorder)playerData;
				}

				// start recording if needed, and update overridden settings
				if (recorder != null && !recorder.IsRecording)
				{
					if (!string.IsNullOrEmpty(filenameSuffix)
						|| overrideOutputFile
						|| overrideResolution
						|| resolutionMultiplier != 1)
					{
						// keep a copy of the recording settings
						originalSettings = recorder.recordingSettings.Clone();

						// change the settings depending on overrides:

						recorder.recordingSettings.outputFile.suffix = filenameSuffix;

						if (overrideOutputFile)
						{
							recorder.recordingSettings.outputFile = outputFile;
						}

						if (overrideResolution)
						{
							recorder.recordingSettings.width = width;
							recorder.recordingSettings.height = height;
							recorder.recordingSettings.useGameResolution = useGameResolution;
							recorder.recordingSettings.keepAspectRatio = keepAspectRatio;
						}
						
						if (resolutionMultiplier != 1)
						{
							recorder.recordingSettings.width = Mathf.FloorToInt(recorder.recordingSettings.width * resolutionMultiplier);
							recorder.recordingSettings.height = Mathf.FloorToInt(recorder.recordingSettings.height * resolutionMultiplier);
						}
					}

					recorder.StartRecording();
				}
			}

			public override void OnBehaviourPause(Playable playable, FrameData info)
			{
				if (!Application.isPlaying)
				{
					return;
				}

				if (info.effectivePlayState == PlayState.Paused
					&& recorder != null
					&& recorder.IsRecording)
				{
					// clip has ended, stop the recording
					recorder.StopRecording();

					// restore settings
					if (originalSettings != null)
					{
						recorder.recordingSettings = originalSettings;
					}

					recorder = null;
				}
			}
		}
	}
}