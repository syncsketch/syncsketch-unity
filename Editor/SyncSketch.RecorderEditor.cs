using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditorInternal;
using UnityEngine;

namespace SyncSketch
{
	// Hook to detect play mode changes in the Editor, and trigger automatic upload if needed.
	// We need to use [InitializeOnLoad] because any registered callbacks to `EditorApplication.playModeStateChanged` will be cleared on domain reload.
	[InitializeOnLoad]
	public static class PlayModeStateChangeHook
	{
		static PlayModeStateChangeHook()
		{
			EditorApplication.playModeStateChanged += OnPlayModeStateChange;
		}

		static void OnPlayModeStateChange(PlayModeStateChange state)
		{
			// When entering Edit mode, look for any SyncSketchRecorder component in the Scene,
			// and fetch the last recording from EditorPrefs if any
			if (state == PlayModeStateChange.EnteredEditMode)
			{
				var sceneRecorders = Resources.FindObjectsOfTypeAll<SyncSketchRecorder>();
				foreach (var recorder in sceneRecorders)
				{
					recorder.FetchLastRecordings();
				}
			}
		}
	}

	[CustomEditor(typeof(SyncSketchRecorder))]
	public class RecorderEditor : Editor
	{
		SyncSketchTreeView treeView;
		[SerializeField] SyncSketchTreeView.State treeViewState;

		API syncSketch;
		API.Review selectedReview;
		static string reviewUpload;
		float progressBar;

		// last recorded files

		struct Recording
		{
			public string fullPath;
			public string filename;
			public bool fileExists;
		}

		List<Recording> lastRecordings;
		Recording lastUploadedRecording;
		bool hasFetchedRecordings;
		bool showReviewUploadWarning;

		// check if we've been logged in externally
		void TryFindSyncSketchSession()
		{
			syncSketch = PersistentSession.TryFind()?.syncSketch;
		}

		void OnEnable()
		{
			TryFindSyncSketchSession();
		}

		public override void OnInspectorGUI()
		{
			#region Header & Login Field

			void onLogin(string username, string apiKey)
			{
				this.Repaint();
				API.Login_Async(username, apiKey,
					(error, syncSketch) =>
					{
						progressBar = 0;
						if (error)
						{
							Log.Error("Couldn't log in");
						}

						this.syncSketch = syncSketch;
						this.Repaint();
					},
					(progress) =>
					{
						progressBar = Mathf.Max(0.0001f, progress);
					},
					true);
			}

			// loading/progress bar
			GUILayout.Space(4);
			const float progressBarHeight = 6;
			GUIUtils.LoadingBarFieldLayout(progressBar, progressBar > 0, progressBarHeight);
			if (progressBar > 0)
			{
				Repaint();
			}

			bool guiEnabled = GUI.enabled;
			GUI.enabled &= progressBar == 0;

			// login button
			using (GUIUtils.Enabled(progressBar == 0))
			{
				GUIUtils.LoginField(syncSketch, onLogin);
			}

			GUIUtils.Separator();

			#endregion

			DrawDefaultInspectorCustom();

			if (!hasFetchedRecordings)
			{
				hasFetchedRecordings = true;

				var lastRecordingInfo = ((SyncSketchRecorder)target).lastRecordingInfo;
				lastRecordings = new List<Recording>();
				if (lastRecordingInfo != null)
				{
					foreach (var file in lastRecordingInfo.files)
					{
						var r = new Recording()
						{
							fullPath = file,
							fileExists = File.Exists(file),
							filename = Path.GetFileName(file)
						};
						lastRecordings.Add(r);
					}
				}
			}

			if (syncSketch == null || !syncSketch.isValid)
			{
				if (Event.current.type == EventType.Repaint)
				{
					// Repaint() is only called when needed internally, so this shouldn't be too costly
					// (it uses Resources.FindObjectsOfTypeAll internally)
					TryFindSyncSketchSession();
				}
			}
			else
			{
				GUIUtils.Separator();

				var reviewIdProp = serializedObject.FindProperty(nameof(SyncSketchRecorder.reviewId));

				// tree view init
				if (treeView == null)
				{
					void onSync()
					{
						syncSketch.SyncAccount_Async(
							// success
							() =>
							{
								progressBar = 0f;
								treeView.Reload();
							},

							// error
							() => { progressBar = 0f; },

							// progress
							(progress) => { progressBar = Mathf.Max(0.0001f, progress); });
					}

					treeViewState = new SyncSketchTreeView.State(Preferences.instance.treeViewRowsCount);
					treeViewState.onVisibleRowCountChange += (count) => Preferences.instance.treeViewRowsCount.value = count;
					treeView = new SyncSketchTreeView(syncSketch, treeViewState, "Select a review to upload to:", onSync);
					treeView.itemSelected += (API.SyncSketchItem item) =>
					{
						var review = item as API.Review;
						if (review != null && review.isValid)
						{
							selectedReview = review;
							reviewIdProp.intValue = review.id;
						}
						else
						{
							selectedReview = null;
							reviewIdProp.intValue = 0;
						}
						reviewIdProp.serializedObject.ApplyModifiedProperties();
						Repaint();
					};

					if (selectedReview != null)
					{
						treeView.SetSelection(new int[] { selectedReview.id }, TreeViewSelectionOptions.RevealAndFrame);
					}
				}

				// find review
				int reviewId = reviewIdProp.intValue;
				if (selectedReview == null || selectedReview.id != reviewId)
				{
					selectedReview = syncSketch.FindReviewById(reviewId);
					if (selectedReview != null)
					{
						treeView.SetSelection(new int[] { selectedReview.id }, TreeViewSelectionOptions.RevealAndFrame);
					}
				}

				treeView.OnGUILayout();

				GUIUtils.Separator();

				#region Last Recorded File(s)

				// automatic upload
				bool shouldUploadAutomatically = false;
				var recorder = (SyncSketchRecorder)target;
				if (recorder.editorShouldUpload)
				{
					recorder.editorShouldUpload = false;

					if (syncSketch != null && syncSketch.isValid && selectedReview != null)
					{
						shouldUploadAutomatically = true;
					}
				}

#if UNITY_EDITOR_OSX
				const string revealTooltip = "Reveal in Finder";
#else
				const string revealTooltip = "Reveal in Explorer";
#endif

				// No recordings
				if (lastRecordings == null || lastRecordings.Count == 0)
				{
					using (GUIUtils.Horizontal)
					{
						GUILayout.Label(GUIUtils.TempContent("Last Recorded File:"), GUILayout.ExpandWidth(false));
						using (GUIUtils.Disabled)
						{
							GUILayout.Label("-", EditorStyles.helpBox);
							var rect = EditorGUILayout.GetControlRect(GUILayout.Width(26));
							GUI.Button(rect, GUIContents.PlayMediaIcon.Tooltip("Play the clip with the default application"), EditorStyles.miniButton);
							rect = EditorGUILayout.GetControlRect(GUILayout.Width(26));
							GUI.Button(rect, GUIContents.ExternalIcon.Tooltip(revealTooltip), EditorStyles.miniButton);
						}
					}
				}
				// Only 1 recording
				else if (lastRecordings.Count == 1)
				{
					var recording = lastRecordings[0];

					using (GUIUtils.Horizontal)
					{
						GUILayout.Label(GUIUtils.TempContent("Last Recorded File:"), GUILayout.ExpandWidth(false));
						var rect = EditorGUILayout.GetControlRect();
						EditorGUI.DrawRect(rect, Color.black * 0.1f);
						GUI.Label(rect, recording.filename);

						using (GUIUtils.Enabled(recording.fileExists))
						{
							rect = EditorGUILayout.GetControlRect(GUILayout.Width(26));
							if (GUI.Button(rect, GUIContents.PlayMediaIcon.Tooltip("Play the clip with the default application"), EditorStyles.miniButton))
							{
								if (File.Exists(recording.fullPath))
								{
									System.Diagnostics.Process.Start(recording.fullPath);
								}
								else
								{
									EditorUtility.DisplayDialog("SyncSketch : Error", string.Format("The file does not exists:\n'{0}'", recording.fullPath), "OK");
									recording.fileExists = false;
									lastRecordings[0] = recording;
								}
							}
							rect = EditorGUILayout.GetControlRect(GUILayout.Width(26));
							if (GUI.Button(rect, GUIContents.ExternalIcon.Tooltip(revealTooltip), EditorStyles.miniButton))
							{
								EditorUtility.RevealInFinder(recording.fullPath);
							}
						}
					}

					// "Upload to review" button
					using (GUIUtils.Enabled(selectedReview != null && recording.fileExists))
					{
						bool canUpload = selectedReview != null && selectedReview.isValid && !string.IsNullOrEmpty(recording.filename);
						var label = GUIUtils.TempContent(canUpload ? string.Format("Upload '<b>{0}</b>'\nto review '<b>{1}</b>'", recording.filename, selectedReview.name) : "Upload").Tooltip(canUpload ? "" : "Select a review to be able to upload");
						bool uploadClicked = GUILayout.Button(label, GUIStyles.ButtonRichText, GUILayout.Height(Mathf.CeilToInt(EditorGUIUtility.singleLineHeight * 2.1f)));
						if (uploadClicked || shouldUploadAutomatically)
						{
							// TODO function to upload file

							if (syncSketch == null || !syncSketch.isValid)
							{
								EditorUtility.DisplayDialog("SyncSketch : Error", "You don't seem to be logged in to SyncSketch", "OK");
							}
							else
							{
								//verify that the file seems valid
								var fileInfo = new FileInfo(recording.fullPath);
								if (fileInfo.Length < 1000)
								{
									// consider files less than 1kB to be invalid (only header written, most likely error during recording)
									EditorUtility.DisplayDialog("SyncSketch : Error", string.Format("The video file doesn't seem to be valid:\n'{0}'", recording.fullPath), "OK");
								}
								else if (!File.Exists(recording.fullPath))
								{
									EditorUtility.DisplayDialog("SyncSketch : Error", string.Format("The video file does not exist anymore:\n'{0}'", recording.fullPath), "OK");
								}
								else
								{
									// upload the video file to the selected review
									var fileData = File.ReadAllBytes(recording.fullPath);
									API.UploadMediaAsync(syncSketch, OnUploadFinished, OnUploadProgress, reviewId, fileData, recording.filename, "video/mp4", true);
									lastUploadedRecording = recording;
									reviewUpload = selectedReview.name;
								}
							}
						}
					}
				}
				// Multiple recording (e.g. from Timeline clips)
				else
				{
					GUILayout.Label(GUIUtils.TempContent("Last Recorded Files:"));

					var areaRect = EditorGUILayout.BeginVertical();
					GUI.Box(areaRect, GUIContent.none);
					GUILayout.Space(4);
					for (int i = 0; i < lastRecordings.Count; i++)
					{
						var lineRect = EditorGUILayout.BeginHorizontal();
						{
							GUILayout.Space(4);

							if (i % 2 == 0)
							{
								lineRect.xMin += 4;
								lineRect.xMax -= 4;
								EditorGUI.DrawRect(lineRect, Color.black * 0.1f);
							}

							GUILayout.Label(lastRecordings[i].filename);

							using (GUIUtils.Enabled(lastRecordings[i].fileExists))
							{
								var rect = EditorGUILayout.GetControlRect(GUILayout.Width(26));
								if (GUI.Button(rect, GUIContents.PlayMediaIcon.Tooltip("Play the clip with the default application"), EditorStyles.miniButton))
								{
									if (File.Exists(lastRecordings[i].fullPath))
									{
										System.Diagnostics.Process.Start(lastRecordings[i].fullPath);
									}
									else
									{
										EditorUtility.DisplayDialog("SyncSketch : Error", string.Format("The file does not exists:\n'{0}'", lastRecordings[i].fullPath), "OK");
										var r = lastRecordings[i];
										r.fileExists = false;
										lastRecordings[i] = r;
									}
								}

								rect = EditorGUILayout.GetControlRect(GUILayout.Width(26));
								if (GUI.Button(rect, GUIContents.ExternalIcon.Tooltip(revealTooltip), EditorStyles.miniButton))
								{
									EditorUtility.RevealInFinder(lastRecordings[i].fullPath);
								}

								using (GUIUtils.Enabled((selectedReview != null && selectedReview.isValid)))
								{
									rect = EditorGUILayout.GetControlRect(GUILayout.Width(26));
									if (GUI.Button(rect, GUIContents.UploadIcon.Tooltip(GUI.enabled ? "Upload to selected review" : "Please select a review to be able to upload the file"), EditorStyles.miniButton))
									{
										// TODO function to upload file

										if (syncSketch == null || !syncSketch.isValid)
										{
											EditorUtility.DisplayDialog("SyncSketch : Error", "You don't seem to be logged in to SyncSketch", "OK");
										}
										else
										{
											//verify that the file seems valid
											var fileInfo = new FileInfo(lastRecordings[i].fullPath);
											if (fileInfo.Length < 1000)
											{
												// consider files less than 1kB to be invalid (only header written, most likely error during recording)
												EditorUtility.DisplayDialog("SyncSketch : Error", string.Format("The video file doesn't seem to be valid:\n'{0}'", lastRecordings[i].fullPath), "OK");
											}
											else if (!File.Exists(lastRecordings[i].fullPath))
											{
												EditorUtility.DisplayDialog("SyncSketch : Error", string.Format("The video file does not exist anymore:\n'{0}'", lastRecordings[i].fullPath), "OK");
											}
											else
											{
												// upload the video file to the selected review
												var fileData = File.ReadAllBytes(lastRecordings[i].fullPath);
												API.UploadMediaAsync(syncSketch, OnUploadFinished, OnUploadProgress, reviewId, fileData, lastRecordings[i].filename, "video/mp4", true);
												lastUploadedRecording = lastRecordings[i];
												reviewUpload = selectedReview.name;
											}
										}
									}
								}
							}

							GUILayout.Space(4);
						}
						EditorGUILayout.EndHorizontal();
					}
					GUILayout.Space(4);
					EditorGUILayout.EndVertical();

					if (shouldUploadAutomatically)
					{
						UploadMultipleRecordings();
					}
				}

				#endregion

				// Select a review warning
				if (Event.current.type == EventType.Layout)
				{
					// can only update this flag on Layout to avoid IMGUI layout mismatch errors
					var uploadOnStopProp = serializedObject.FindProperty(nameof(SyncSketchRecorder.uploadOnStop));
					showReviewUploadWarning = uploadOnStopProp.boolValue && (selectedReview == null || !selectedReview.isValid);
				}

				if (showReviewUploadWarning)
				{
					EditorGUILayout.HelpBox("Please select a review for the 'Upload On Stop' option to work.", MessageType.Warning);
				}
			}

			// Last uploaded files list
			var lastReviewsProp = serializedObject.FindProperty(nameof(SyncSketchRecorder.lastUploads));
			int length = lastReviewsProp.arraySize;
			ReviewUploadInfo.DrawList("Last Screenshot Uploads:", length, (index) => ((SyncSketchRecorder)target).lastUploads[index], null,
			(deletedIndex) =>
			{
				lastReviewsProp.DeleteArrayElementAtIndex(deletedIndex);
				lastReviewsProp.serializedObject.ApplyModifiedProperties();
			});

			GUI.enabled = guiEnabled;
		}

		/// <summary>
		/// Same as Editor.DrawDefaultInspector with a few changes
		/// </summary>
		void DrawDefaultInspectorCustom()
		{
			serializedObject.Update();
			SerializedProperty iterator = serializedObject.GetIterator();
			bool enterChildren = true;
			while (iterator.NextVisible(enterChildren))
			{
				if ("m_Script" == iterator.propertyPath)
				{
					continue;
				}

				EditorGUILayout.PropertyField(iterator, true);
				enterChildren = false;
			}
			serializedObject.ApplyModifiedProperties();
		}

		// This can't be initialized directly as we're not allowed to call EditorGUIUtility.isProSkin
		// from a constructor, so it will be initialized on first use instead.
		static Color? _oddRowColor;
		static Color oddRowColor
		{
			get
			{
				if (_oddRowColor == null)
				{
					_oddRowColor = EditorGUIUtility.isProSkin ? new Color(1, 1, 1, 0.033f) : new Color(0, 0, 0, 0.075f);
				}
				return _oddRowColor.Value;
			}
		}

		void OnUploadFinished(bool isError, string message)
		{
			EditorUtility.ClearProgressBar();
			progressBar = 0f;

			if (isError)
			{
				// error
				EditorUtility.DisplayDialog("SyncSketch : Error", "An error occurred during upload:\n\n" + message, "OK");
			}
			else
			{
				// success: fetch review URL
				var json = TinyJSON.JSON.Load(message);
				string reviewURL = json["reviewURL"] + "?offlineMode=1";

				// open review URL
				Application.OpenURL(reviewURL);

				// copy review URL to clipboard
				EditorGUIUtility.systemCopyBuffer = reviewURL;

				// add to list of recent reviews for this Recorder instance
				var lastReviewsProp = serializedObject.FindProperty(nameof(SyncSketchRecorder.lastUploads));
				lastReviewsProp.InsertArrayElementAtIndex(0);
				var element = lastReviewsProp.GetArrayElementAtIndex(0);
				element.FindPropertyRelative(nameof(ReviewUploadInfo.filename)).stringValue = lastUploadedRecording.filename;
				element.FindPropertyRelative(nameof(ReviewUploadInfo.reviewName)).stringValue = selectedReview.name;
				element.FindPropertyRelative(nameof(ReviewUploadInfo.reviewURL)).stringValue = reviewURL;
				element.FindPropertyRelative(nameof(ReviewUploadInfo.date)).stringValue = json["created"];
				while (lastReviewsProp.arraySize > 5)
				{
					lastReviewsProp.DeleteArrayElementAtIndex(lastReviewsProp.arraySize - 1);
				}
				serializedObject.ApplyModifiedProperties();

				// feedback
				EditorUtility.DisplayDialog("SyncSketch", "The file has been successfully uploaded for review.\n\nReview URL has been copied to clipboard.", "OK");
			}
		}

		void OnUploadProgress(float progress)
		{
			EditorUtility.DisplayProgressBar("SyncSketch", string.Format("Uploading video to review '{0}'...", reviewUpload), progress);
			progressBar = progress;
		}

		#region Upload Multiple Videos

		int uploadMultipleIndex;
		int uploadMultipleReview;
		void UploadMultipleRecordings()
		{
			if (selectedReview == null || !selectedReview.isValid)
			{
				return;
			}

			uploadMultipleIndex = -1;
			uploadMultipleReview = selectedReview.id;
			reviewUpload = selectedReview.name;

			UploadMultipleNext();
		}

		bool UploadMultipleNext()
		{
			uploadMultipleIndex++;

			if (uploadMultipleIndex >= lastRecordings.Count)
			{
				return false;
			}

			var fileData = File.ReadAllBytes(lastRecordings[uploadMultipleIndex].fullPath);
			API.UploadMediaAsync(syncSketch, OnUploadFinishedMultiple, OnUploadProgressMultiple, uploadMultipleReview, fileData, lastRecordings[uploadMultipleIndex].filename, "video/mp4", true);

			return true;
		}

		// During multiple-file upload, called when one part has finished
		void OnUploadFinishedMultiple(bool isError, string message)
		{
			if (isError)
			{
				// error
				EditorUtility.DisplayDialog("SyncSketch : Error", "An error occurred during upload:\n\n" + message, "OK");
			}

			bool allDone = !UploadMultipleNext();

			if (allDone)
			{
				EditorUtility.ClearProgressBar();
				progressBar = 0f;
			}

			if (!isError)
			{
				// success: fetch review URL
				var json = TinyJSON.JSON.Load(message);
				string reviewURL = json["reviewURL"] + "?offlineMode=1";

				// add to list of recent reviews for this Recorder instance
				var lastReviewsProp = serializedObject.FindProperty(nameof(SyncSketchRecorder.lastUploads));
				lastReviewsProp.InsertArrayElementAtIndex(0);
				var element = lastReviewsProp.GetArrayElementAtIndex(0);
				element.FindPropertyRelative(nameof(ReviewUploadInfo.filename)).stringValue = lastRecordings[uploadMultipleIndex-1].filename;
				element.FindPropertyRelative(nameof(ReviewUploadInfo.reviewName)).stringValue = selectedReview.name;
				element.FindPropertyRelative(nameof(ReviewUploadInfo.reviewURL)).stringValue = reviewURL;
				element.FindPropertyRelative(nameof(ReviewUploadInfo.date)).stringValue = json["created"];
				while (lastReviewsProp.arraySize > 5)
				{
					lastReviewsProp.DeleteArrayElementAtIndex(lastReviewsProp.arraySize - 1);
				}
				serializedObject.ApplyModifiedProperties();

				if (allDone)
				{
					// open review URL
					Application.OpenURL(reviewURL);

					// feedback
					EditorUtility.DisplayDialog("SyncSketch", "The files have been successfully uploaded for review.", "OK");
				}
			}
		}

		// During multiple-file upload
		void OnUploadProgressMultiple(float progress)
		{
			float realProgress = (uploadMultipleIndex + progress) / lastRecordings.Count;

			EditorUtility.DisplayProgressBar("SyncSketch", string.Format("Uploading video {0}/{1} to review '{2}'...", uploadMultipleIndex+1, lastRecordings.Count, reviewUpload), realProgress);
			progressBar = progress;
		}

		#endregion
	}
}