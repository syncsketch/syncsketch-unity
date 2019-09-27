#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
using System.Collections;
using System.IO;
using System;
using System.Collections.Generic;
using UTJ.FrameCapturer;
using UnityEngine.Rendering;

// TODO Record/Pause Keys should be global settings

namespace SyncSketch
{
	public class SyncSketchRecorder : MonoBehaviour
	{
		[Serializable]
		public class RecordingSettings
		{
			public int width = 1280;
			public int height = 720;
			[Tooltip("Use the selected resolution in the Game View (will be clamped to 1280 maximum width)")]
			public bool useGameResolution;
			[Tooltip("Keep the same aspect ratio as in the Game View")]
			public bool keepAspectRatio = true;
			public int frameRate = 30;
			[Tooltip("Make sure that all frames are rendered in the output video.\nThis will slow down the rendering if necessary.")]
			public bool noFrameSkip;
			public FilePath outputFile = new FilePath("Recording", "mp4");

			int prevCaptureFramerate;

			public RecordingSettings Clone()
			{
				return (RecordingSettings)this.MemberwiseClone();
			}

			public void OnValidate()
			{
				width = Mathf.Clamp(width, 8, 1280);
				height = Mathf.Clamp(height, 8, 720);
			}

			public void OnStartRecording()
			{
				if (noFrameSkip)
				{
					prevCaptureFramerate = Time.captureFramerate;
					Time.captureFramerate = frameRate;
				}
			}

			public void OnStopRecording()
			{
				if (noFrameSkip)
				{
					Time.captureFramerate = prevCaptureFramerate;
				}
			}

			public void GetRecordingResolution(out int width, out int height)
			{
				width = this.width;
				height = this.height;

				var gameViewSize = Handles.GetMainGameViewSize();
				int screenWidth = (int)gameViewSize.x;
				int screenHeight = (int)gameViewSize.y;

				if (this.useGameResolution)
				{
					width = screenWidth;
					height = screenHeight;
				}
				else if (this.keepAspectRatio)
				{
					float ratio = (float)screenHeight/screenWidth;
					height = Mathf.FloorToInt(width * ratio);
				}

				if (width > 1280)
				{
					float ratio = (float)screenHeight/screenWidth;
					width = 1280;
					height = Mathf.FloorToInt(width * ratio);
				}

				if (height > 720)
				{
					float ratio = (float)screenWidth/screenHeight;
					height = 720;
					width = Mathf.FloorToInt(height * ratio);
				}

				// make sure we have even numbers (for mp4 format)
				if (width % 2 == 1) width++;
				if (height % 2 == 1) height++;
			}

#if UNITY_EDITOR
			#region RecordingSettings Drawer

			[CustomPropertyDrawer(typeof(RecordingSettings))]
			class RecordingSettingsDrawer : PropertyDrawer
			{
				const int childrenPropertiesCount = 6;

				SerializedProperty propFrameRate;
				SerializedProperty propNoFrameSkip;
				SerializedProperty propWidth;
				SerializedProperty propHeight;
				SerializedProperty propUseGameResolution;
				SerializedProperty propKeepAspectRatio;
				SerializedProperty propOutputFile;

				void init(SerializedProperty property)
				{
					if(propFrameRate == null)
					{
						propFrameRate = property.FindPropertyRelative("frameRate");
						propNoFrameSkip = property.FindPropertyRelative("noFrameSkip");
						propWidth = property.FindPropertyRelative("width");
						propHeight = property.FindPropertyRelative("height");
						propUseGameResolution = property.FindPropertyRelative("useGameResolution");
						propKeepAspectRatio = property.FindPropertyRelative("keepAspectRatio");
						propOutputFile = property.FindPropertyRelative("outputFile");
					}
				}

				public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
				{
					init(property);
					var rect = position;
					rect.height = EditorGUIUtility.singleLineHeight;
					Action nextPropertyRect = () =>
					{
						rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
					};

					if (EditorGUI.PropertyField(rect, property))
					{
						int prevLevel = EditorGUI.indentLevel;
						EditorGUI.indentLevel++;

						// output file
						nextPropertyRect();
						EditorGUI.PropertyField(rect, propOutputFile);
						rect.y += EditorGUI.GetPropertyHeight(propOutputFile);

						// resolution
						EditorGUI.PropertyField(rect, propUseGameResolution);
						using (GUIUtils.Enabled(!propUseGameResolution.boolValue))
						{
							nextPropertyRect();
							EditorGUI.PropertyField(rect, propKeepAspectRatio);
							nextPropertyRect();
							EditorGUI.PropertyField(rect, propWidth);
							using (GUIUtils.Enabled(!propKeepAspectRatio.boolValue))
							{ 
								nextPropertyRect();
								EditorGUI.PropertyField(rect, propHeight);
							}
						}

						// frame rate
						nextPropertyRect();
						EditorGUI.PropertyField(rect, propFrameRate);
						nextPropertyRect();
						EditorGUI.PropertyField(rect, propNoFrameSkip);

						EditorGUI.indentLevel = prevLevel;
					}
				}

				public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
				{
					return EditorGUI.GetPropertyHeight(property, label, property.isExpanded);
				}
			}

			#endregion
#endif
		}

		#region Public Properties

		new public Camera camera;
		public RecordingSettings recordingSettings = new RecordingSettings();
		[Tooltip("Record the Game View as soon as the player is started")]
		public bool recordOnPlay;
		[Tooltip("Will automatically upload the recorded clip to the selected review when the player is stopped")]
		public bool uploadOnStop;
		[Label("Start/Stop Recording", "Press this key in play mode to start/stop recording")]
		public KeyCode recordKey = KeyCode.R;
		[Label("Pause Recording", "Press this key in play mode to pause/unpause the recording")]
		public KeyCode pauseKey = KeyCode.P;

		[HideInInspector] public int reviewId;
		[HideInInspector] public RecordingInfo lastRecordingInfo;
		[HideInInspector] public ReviewUploadInfo[] lastUploads;
		[HideInInspector, NonSerialized] public bool toolbox; // true when this recorder was created from the SyncSketch Toolbox

		public bool IsRecording { get { return isRecording; } }

		#endregion

		#region Private members

		bool isRecording;
		bool recorderInitialized;
		string fullPath;
		RenderTexture tempRT;
		CommandBuffer commandBuffer;
		[SerializeField, HideInInspector] Shader copyShader;
		Material copyMaterial;
		Mesh quadMesh;

		MovieEncoder encoder;

		#endregion

		#region Recording

		RenderTextureFormat GeTargetFormat(Camera camera)
		{
			return RenderTextureFormat.ARGB32;
			// return camera.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
		}

		int GetAntiAliasingLevel(Camera camera)
		{
			return camera.allowMSAA ? Mathf.Max(1, QualitySettings.antiAliasing) : 1;
		}

		public void StartRecording()
		{
			if (!recorderInitialized)
			{
				int width, height;
				recordingSettings.GetRecordingResolution(out width, out height);

				if (camera == null)
				{
					camera = this.GetComponent<Camera>();
				}
				if (camera == null)
				{
					Log.Error("Can't record, the target camera is null");
					return;
				}

				// create output directory if it does not exist
				fullPath = Utils.Path.UniqueFileName(Utils.Path.ForwardSlashes(string.Format("{0}.mp4", recordingSettings.outputFile)));
				var directory = Path.GetDirectoryName(fullPath);
				if (!Directory.Exists(directory))
				{
					Directory.CreateDirectory(directory);
				}

				// Give a newly created temporary render texture to the camera if it's set to render to a screen. Also create a blitter object to keep frames presented on the screen.
				if (camera.targetTexture == null)
				{
					/*
					tempRT = new RenderTexture(width, height, 24, GetTargetFormat(camera));
					tempRT.antiAliasing = GetAntiAliasingLevel(camera);
					camera.targetTexture = tempRT;
					blitter = new GameObject("Blitter");
					*/
				}

				// Start an FFmpeg session.
				/*
				session = FFmpegSession.CreateWithArguments(
					string.Format("-y -f rawvideo -vcodec rawvideo -pixel_format rgba -colorspace bt709 -video_size {0}x{1} -framerate {2} -loglevel warning -i - {3} {4}",
					width,
					height,
					recordingSettings.frameRate,
					EncoderSettings,
					string.Format("\"{0}\"", fullPath)));
				*/

				// create temp texture that will be used to copy frame buffer
				var format = GeTargetFormat(camera);
				tempRT = new RenderTexture(width, height, 0, format);
				tempRT.wrapMode = TextureWrapMode.Repeat;
				tempRT.antiAliasing = GetAntiAliasingLevel(camera);
				tempRT.Create();

				// create encoder
				var config = new MovieEncoderConfigs(MovieEncoder.Type.MP4);
				config.captureAudio = false;
				config.captureVideo = true;
				config.mp4EncoderSettings.videoWidth = width;
				config.mp4EncoderSettings.videoHeight = height;
				config.mp4EncoderSettings.videoBitrateMode = fcAPI.fcBitrateMode.VBR;
				config.mp4EncoderSettings.videoTargetFramerate = recordingSettings.frameRate;
				var forwardPath = Utils.Path.ForwardSlashes(fullPath).Replace(".mp4", "");
				encoder = MovieEncoder.Create(config, forwardPath);

				if (encoder == null || !encoder.IsValid())
				{
					Log.Error("Can't record, couldn't create MovieEncoder");
					return;
				}

				// Time tracking variables
				startTime = Time.unscaledTime;
				frameCount = 0;
				frameDropCount = 0;
				recordedFrames = 0;
				initialFrame = Time.renderedFrameCount;
				initialRealTime = Time.realtimeSinceStartup;

				if (recordingSettings.noFrameSkip)
				{
					Time.maximumDeltaTime = (1.0f / recordingSettings.frameRate);
				}

				recordingSettings.OnStartRecording();

				// create command buffer
				{
					if (copyShader == null)
					{
						copyShader = fcAPI.GetFrameBufferCopyShader();
						if (copyShader == null)
						{
							Log.Error("Copy shader is missing!");
							return;
						}
					}

					if (quadMesh == null) quadMesh = fcAPI.CreateFullscreenQuad();
					if (copyMaterial == null) copyMaterial = new Material(copyShader);

					if (camera.targetTexture != null)
					{
						copyMaterial.EnableKeyword("OFFSCREEN");
					}
					else
					{
						copyMaterial.DisableKeyword("OFFSCREEN");
					}

					int tid = Shader.PropertyToID("_TmpFrameBuffer");
					commandBuffer = new CommandBuffer();
					commandBuffer.name = "SyncSketch Recorder: copy frame buffer";

					commandBuffer.GetTemporaryRT(tid, -1, -1, 0, FilterMode.Bilinear);
					commandBuffer.Blit(BuiltinRenderTextureType.CurrentActive, tid);
					commandBuffer.SetRenderTarget(tempRT);
					commandBuffer.DrawMesh(quadMesh, Matrix4x4.identity, copyMaterial, 0, 0);
					commandBuffer.ReleaseTemporaryRT(tid);
					
					camera.AddCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
				}


				recorderInitialized = true;
			}
			isRecording = true;
		}

		public void PlayPauseRecording()
		{
			isRecording = !isRecording;
		}

		public void PauseRecording()
		{
			isRecording = false;
		}

		public void StopRecording()
		{
			recordingSettings.OnStopRecording();

			isRecording = false;

			if (encoder != null)
			{
				encoder.Release();
				encoder = null;
			}

			if (commandBuffer != null)
			{
				GetComponent<Camera>().RemoveCommandBuffer(CameraEvent.AfterEverything, commandBuffer);
				commandBuffer.Release();
				commandBuffer = null;
			}

			if (tempRT != null)
			{
				// Dispose the frame texture.
				camera.targetTexture = null;
				Destroy(tempRT);
				tempRT = null;
			}

			recorderInitialized = false;

#if UNITY_EDITOR
			if (!string.IsNullOrEmpty(fullPath))
			{
				// Save the name of the file that was just recorded, so that it can be retrieved in the Editor and added to the list of this component automatically.
				// We have to use this technique because when Play Mode stops, the domain is reloaded and this will lose all value changes made during Play Mode.
				if (currentRecordingInfo == null)
				{
					currentRecordingInfo = new RecordingInfo(this.GetInstanceID());
				}
				currentRecordingInfo.files.Add(fullPath);
			}
#endif
		}

		IEnumerator OnPostRender()
		{
			if (isRecording && encoder != null)
			{
				yield return new WaitForEndOfFrame();

				double timestamp = 1.0 / recordingSettings.frameRate * recordedFrames;

				fcAPI.fcLock(tempRT, TextureFormat.RGB24, (data, fmt) =>
				{
					encoder.AddVideoFrame(data, fmt, timestamp);
				});
				recordedFrames++;
			}
		}

		#endregion

		#region Unity MonoBehaviour

		void OnValidate()
		{
			// Currently the plugin is Editor-only
			this.hideFlags = HideFlags.DontSaveInBuild;

			if (camera == null)
			{
				camera = this.GetComponent<Camera>();
			}

			recordingSettings.OnValidate();
		}

		void Reset()
		{
			if (camera == null)
			{
				camera = this.GetComponent<Camera>();
			}

#if UNITY_EDITOR
			copyShader = fcAPI.GetFrameBufferCopyShader();
#endif
		}

		void OnEnable()
		{
			if (recordOnPlay)
			{
				StartRecording();
			}
		}

		void OnDisable()
		{
			if (IsRecording)
			{
				StopRecording();
			}
		}

#if UNITY_EDITOR
		void OnDestroy()
		{
			if (currentRecordingInfo != null)
			{
				string key = toolbox ? "SyncSketch_LastRecordedFiles_Toolbox" : "SyncSketch_LastRecordedFiles";
				EditorPrefs.SetString(key, currentRecordingInfo.ToJSON());
			}
		}
#endif

		void Update()
		{
			if (Input.GetKeyDown(recordKey))
			{
				if (!isRecording)
				{
					StartRecording();
				}
				else
				{
					StopRecording();
				}
			}

			if (recorderInitialized)
			{
				if (Input.GetKeyDown(pauseKey))
				{
					PlayPauseRecording();
				}
			}

			if (isRecording && recordingSettings.noFrameSkip)
			{
				StartCoroutine(Wait());
			}
		}

		IEnumerator Wait()
		{
			yield return new WaitForEndOfFrame();

			float wt = (1.0f / recordingSettings.frameRate) * (Time.renderedFrameCount - initialFrame);
			while (Time.realtimeSinceStartup - initialRealTime < wt)
			{
				System.Threading.Thread.Sleep(1);
			}
		}


#if UNITY_EDITOR

		#region Recording UI Overlay

		Texture2D _recordingRedDot;
		Texture2D recordingRedDot
		{
			get
			{
				if(_recordingRedDot == null)
				{
					_recordingRedDot = GUIUtils.FindTexture("icon_record.png", GUIUtils.FindTextureOption.None);
				}
				return _recordingRedDot;
			}
		}
		// static Color recordColor = new Color32(255, 0, 0, 255);
		// static Color pauseColor = new Color32(128, 128, 128, 255);
		static Color blackTransparentColor = new Color32(0, 0, 0, 128);

		void OnGUI()
		{
			if(!recorderInitialized)
			{
				return;
			}

			// calculate drawing rectangles

			const float margin = 5;

			string label = isRecording ? "SyncSketch: recording" : "SyncSketch: recording paused";
			var guiContent = GUIUtils.TempContent(label);

			var style = EditorGUIUtility.isProSkin ? EditorStyles.boldLabel : EditorStyles.whiteBoldLabel; // whiteBoldLabel is actually black in dark mode
			float labelWidth = style.CalcSize(guiContent).x;

			int dotWidth = recordingRedDot != null ? recordingRedDot.width : 0;
			int dotHeight = recordingRedDot != null ? recordingRedDot.height : 0;

			var bgRect = new Rect(margin, margin, labelWidth + dotWidth + margin*3, Mathf.Max(dotHeight + 10, 30));
			float bgMidY = bgRect.y + bgRect.height/2;

			var dotRect = new Rect(margin*2, bgMidY - dotHeight/2, dotWidth, dotHeight);

			var labelRect = bgRect;
			labelRect.x += margin*2 + dotWidth;
			labelRect.height = EditorGUIUtility.singleLineHeight;
			labelRect.y = bgMidY - labelRect.height/2;

			// draw "recording" UI

			EditorGUI.DrawRect(bgRect, blackTransparentColor);
			float blink = Time.realtimeSinceStartup % 1f;
			if (recordingRedDot != null && (blink > 0.5f || !isRecording))
			{
				// GUI.DrawTexture(dotRect, recordingRedDot, ScaleMode.ScaleToFit, true, 0, isRecording ? recordColor : pauseColor, 0, 0);
				GUI.DrawTexture(dotRect, recordingRedDot, ScaleMode.ScaleToFit, true, 0);
			}
			GUI.Label(labelRect, guiContent, style);
		}

		#endregion
#endif

		#endregion

		#region Time Tracking

		int frameCount;
		float startTime;
		int frameDropCount;
		int initialFrame;
		int recordedFrames;
		float initialRealTime;

		float FrameTime
		{
			get { return startTime + (frameCount - 0.5f) / recordingSettings.frameRate; }
		}

		void WarnFrameDrop()
		{
			if (++frameDropCount != 10) return;

			Debug.LogWarning("Significant frame droppping was detected. This may introduce gaps into output video.");
			//Debug.LogWarning("Significant frame droppping was detected. This may introduce time instability into output video. Decreasing the recording frame rate is recommended.");
		}

		#endregion

#if UNITY_EDITOR
		#region Auto-upload Feature

		// Auto-upload is trickier than it may seem: we need to ensure data is kept between domain assembly reloads,
		// so we use EditorPrefs to store data and retrieve it when entering edit mode

		[Serializable]
		public class RecordingInfo
		{
			public int recorderInstanceID;  // InstanceID stays the same between assembly reloads
			public List<string> files;

			public RecordingInfo(int instanceID)
			{
				files = new List<string>();
				recorderInstanceID = instanceID;
			}

			public string ToJSON()
			{
				return JsonUtility.ToJson(this);
			}

			public static RecordingInfo FromJSON(string json)
			{
				return JsonUtility.FromJson<RecordingInfo>(json);
			}
		}

		[NonSerialized] RecordingInfo currentRecordingInfo;
		[NonSerialized] public bool editorShouldUpload;
		[NonSerialized] public bool newRecordingsFetched;

		public void FetchLastRecordings()
		{
			// Try to retrieve last saved recording from EditorPrefs
			string json = EditorPrefs.GetString("SyncSketch_LastRecordedFiles", null);
			if (!string.IsNullOrEmpty(json))
			{
				var recordingInfo = RecordingInfo.FromJSON(json);
				if (recordingInfo.recorderInstanceID == this.GetInstanceID())
				{
					this.lastRecordingInfo = recordingInfo;
					this.newRecordingsFetched = true;

					if (uploadOnStop)
					{
						// Will trigger auto-upload in the Inspector, hence why we force selection on the GameObject so that the Inspector has focus (not ideal but it works)
						editorShouldUpload = true;
						Selection.activeGameObject = this.gameObject;
					}

					EditorPrefs.DeleteKey("SyncSketch_LastRecordedFiles");
				}
			}

			if (EditorPrefs.HasKey("SyncSketch_LastRecordedFiles"))
			{
				Log.Message("Couldn't find the associated SyncSketchRecorder with EditorPrefs Key 'SyncSketch_LastRecordedFiles'", Log.Level.Full);
			}
		}

		#endregion
#endif
	}
}
