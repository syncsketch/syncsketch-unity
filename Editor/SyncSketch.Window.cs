using UnityEngine;
using UnityEditor;
using Object = UnityEngine.Object;
using UnityEditor.IMGUI.Controls;
using System.IO;
using System;
using System.Collections.Generic;

namespace SyncSketch
{
	/// <summary>
	/// SyncSketch UI window
	/// </summary>
	public class Window : EditorWindow
	{
		#region Private Members

		// The current selected items from SyncSketch:
		API _syncSketch;
		API syncSketch
		{
			get { return _syncSketch; }
			set
			{
				_syncSketch = value;
				if (_syncSketch != null)
				{
					InitTreeView();
				}
			}
		}
		API.Project selectedProject;
		API.Review selectedReview;
		SyncSketchRecorder videoRecorder;

		[SerializeField] FilePath screenshotOutputFile = new FilePath("Screenshot", "png");
		[SerializeField] FilePath videoOutputFile = new FilePath("Video", "mp4");
		// Required to draw the FilePath inspector properly (Unity makes it very complicated to draw with the custom property drawer!)
		SerializedProperty screenshotOutputFileProperty;
		SerializedProperty videoOutputFileProperty;
		GUIContent screenshotOutputPathLabel;
		GUIContent videoOutputPathLabel;
		FilePathDrawer filePathDrawer;

		// last taken screenshot
		[SerializeField] string lastScreenshotFullPath;
		[SerializeField] string lastScreenshotFilename;
		[SerializeField] bool lastScreenshotFileExists;
		bool showReviewUploadWarning;

		// video recording
		bool startRecordingWithPlayMode = false;
		// TODO this is the same as in SyncSketch.RecorderEditor, might be worth sharing the same code eventually?
		struct Recording
		{
			public string fullPath;
			public string filename;
			public bool fileExists;

			public void Reset()
			{
				filename = null;
				fileExists = false;
				fullPath = null;
			}
		}
		[SerializeField] List<Recording> lastRecordings = new List<Recording>();

		// UI

		Vector2 scrollPosition;
		[SerializeField] SyncSketchTreeView.State treeViewState;
		SyncSketchTreeView treeView;

		/// <summary>
		/// How many UI-blocking requests are awaiting an answer
		/// </summary>
		int blockingRequests;

		/// <summary>
		/// UI value for the progress bar, to indicate status of requests
		/// </summary>
		float progressBar;

		#endregion

		/// <summary>
		/// Adds the menu option to show the window.
		/// </summary>
		[MenuItem("Window/SyncSketch/Toolbox", priority = 2222)]
		static void OpenWindow()
		{
			GetWindow<Window>("SyncSketch");
		}

		[MenuItem("Window/SyncSketch/Clear Preferences", priority = 2223)]
		static void ClearPrefs()
		{
			Preferences.Clear();
		}

		#region GUI

		void OnEnable()
		{
			EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

			if (Preferences.instance.screenshotOutputFile != null)
			{
				screenshotOutputFile = Preferences.instance.screenshotOutputFile;
			}
			var serializedObject = new SerializedObject(this);
			screenshotOutputFileProperty = serializedObject.FindProperty("screenshotOutputFile");
			screenshotOutputPathLabel = new GUIContent(screenshotOutputFileProperty.displayName);
			if (Preferences.instance.videoOutputFile != null)
			{
				videoOutputFile = Preferences.instance.videoOutputFile;
			}
			videoOutputFileProperty = serializedObject.FindProperty("videoOutputFile");
			videoOutputPathLabel = new GUIContent(videoOutputFileProperty.displayName);
			filePathDrawer = new FilePathDrawer();

			syncSketch = PersistentSession.TryFind()?.syncSketch;

			this.minSize = new Vector2(310, this.minSize.y);
		}

		void OnDisable()
		{
			EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
		}

		void OnPlayModeStateChanged(PlayModeStateChange playMode)
		{
			if (playMode == PlayModeStateChange.EnteredPlayMode)
			{
				OnPlayModeStarted();
			}
			else if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
			{
				OnPlayModeEnded();
			}
		}

		void OnPlayModeStarted()
		{
			if (startRecordingWithPlayMode)
			{
				InitVideoRecorder();
				startRecordingWithPlayMode = false;
			}

			this.Repaint();
		}

		void OnPlayModeEnded()
		{
			FetchLastRecording();
			videoRecorder = null;

			this.Repaint();
		}

		void FetchLastRecording()
		{
			// Try to retrieve last saved recording from EditorPrefs
			string json = EditorPrefs.GetString("SyncSketch_LastRecordedFiles_Toolbox", null);
			if (!string.IsNullOrEmpty(json))
			{
				var recordings = SyncSketchRecorder.RecordingInfo.FromJSON(json);

				if (recordings.files != null && recordings.files.Count > 0)
				{
					foreach (var file in recordings.files)
					{
						var recording = new Recording()
						{
							fullPath = file,
							fileExists = File.Exists(file),
							filename = Path.GetFileName(file)
						};
						if (recording.fileExists)
						{
							this.lastRecordings.Add(recording);
						}
					}

					// auto upload
					if (Preferences.instance.uploadToReviewAfterScreenshot && selectedReview != null)
					{
						if (this.lastRecordings.Count == 1)
						{
							var recording = this.lastRecordings[0];
							if (recording.fileExists)
							{
								var bytes = File.ReadAllBytes(recording.fullPath);
								UploadMediaFile(bytes, recording.filename, "video/mp4", true);
							}
						}
						else if (this.lastRecordings.Count > 1)
						{
							UploadMultipleRecordings();
						}
					}
				}

				EditorPrefs.DeleteKey("SyncSketch_LastRecordedFiles_Toolbox");
			}
		}

		void OnFocus()
		{
			var sceneViews = Resources.FindObjectsOfTypeAll<SceneView>();
			sceneViewAvailable = sceneViews.Length > 0;
			FocusLastSelectedItem();
		}

		void UpdateProgressBar(float progress)
		{
			progressBar = progress;
		}

		void OnSync()
		{
			void syncDone()
			{
				blockingRequests--;
				treeView.Reload();
			}

			blockingRequests++;
			syncSketch.SyncAccount_Async(syncDone, syncDone, null);
		}

		/// <summary>
		/// The window interface entry point, using GUILayout elements.
		/// </summary>
		void OnGUI()
		{
			bool guiEnabled = GUI.enabled;

			// loading/progress bar
			const float progressBarHeight = 6;
			bool indefiniteLoading = blockingRequests > 0;
			float progress = indefiniteLoading ? progressBar : 0;
			GUIUtils.LoadingBarFieldLayout(progress, blockingRequests > 0, progressBarHeight);
			if (indefiniteLoading)
			{
				Repaint();
			}

			GUI.enabled &= blockingRequests == 0;

			const float labelWidth = 120;
			const float padding = 20;
			Rect paddedRect = new Rect(padding, padding, position.width - padding*2, position.height-padding*2);
			GUILayout.BeginArea(paddedRect);
			float prevLabelWidth = EditorGUIUtility.labelWidth;
			EditorGUIUtility.labelWidth = labelWidth;

			// Login/Logout bar

			void onLogout()
			{
				DoLogout();
				GUIUtility.ExitGUI();
				return;
			}

			void onLogin(string username, string apiKey)
			{
				DoLogin(username, apiKey);
			}

			GUIUtils.LoginField(syncSketch, onLogin, onLogout);

			if (syncSketch == null || !syncSketch.isValid)
			{
				// Not logged in

				// check if we've been logged in externally
				if (Event.current.type == EventType.Repaint)
				{
					syncSketch = PersistentSession.TryFind()?.syncSketch;
				}
			}
			else
			{
				// Logged in

				const int bigButtonSize = 44;

				GUIUtils.Separator();

				scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
				bool isWideEnough = EditorGUIUtility.currentViewWidth >= 470;

				// screenshot buttons
				GUILayout.Label("Take Screenshot:", EditorStyles.largeLabel, GUILayout.Width(labelWidth));
				using (GUIUtils.Horizontal)
				{
					using (GUIUtils.Enabled(!snippingScreenshot))
					{
						GUIStyle buttonStyle = isWideEnough ? GUIStyles.ButtonLeftAligned : "Button";

						if (GUILayout.Button(GUIContents.ScreenshotGame.Label(isWideEnough ? " Full\n Game View" : null).Tooltip("Game View Screenshot"), buttonStyle, GUILayout.Height(bigButtonSize)))
						{
							DoTakeScreenshotGameView();
						}

						using (GUIUtils.Enabled(sceneViewAvailable))
						{
							if (GUILayout.Button(GUIContents.ScreenshotScene.Label(isWideEnough ? " Full\n Scene View" : null).Tooltip("Scene View Screenshot"), buttonStyle, GUILayout.Height(bigButtonSize)))
							{
								DoTakeScreenshotSceneView();
							}

							if (GUILayout.Button(GUIContents.ScreenshotSnip.Label(isWideEnough ? " Snip\n Scene View" : null).Tooltip("Scene View Screenshot Snip"), buttonStyle, GUILayout.Height(bigButtonSize)))
							{
								StartSnippingScreenshot();
							}
						}
					}

					HandleScreenshotCancelEvents(Event.current);
				}

				Preferences.instance.captureSceneViewGizmos.value = GUILayout.Toggle(Preferences.instance.captureSceneViewGizmos, " Capture Scene View UI & Gizmos");

				// output path
				float height = filePathDrawer.GetPropertyHeight(screenshotOutputFileProperty, screenshotOutputPathLabel);
				var outputRect = EditorGUILayout.GetControlRect(GUILayout.Height(height));
				EditorGUI.BeginChangeCheck();
				filePathDrawer.OnGUI(outputRect, screenshotOutputFileProperty, screenshotOutputPathLabel);
				if (EditorGUI.EndChangeCheck())
				{
					Preferences.instance.screenshotOutputFile = screenshotOutputFile;
					Preferences.Save();
				}

				GUIUtils.Separator();

				// video buttons
				GUILayout.Label(GUIUtils.TempContent("Record Video:"), EditorStyles.largeLabel, GUILayout.Width(labelWidth));
				using (GUIUtils.Horizontal)
				{
					using (GUIUtils.Enabled(!snippingScreenshot))
					{
						GUIStyle buttonStyle = GUIStyles.ButtonLeftAligned;

						using (GUIUtils.Enabled(!startRecordingWithPlayMode && !(videoRecorder != null && videoRecorder.IsRecording)))
						{
							string label = (EditorApplication.isPlaying && !startRecordingWithPlayMode) ? " Record Video" : " Play & Record";
							if (GUILayout.Button(GUIContents.Video.Label(label), buttonStyle, GUILayout.Height(bigButtonSize)))
							{
								DoRecordVideo();
							}
						}

						using (GUIUtils.Enabled(videoRecorder != null && videoRecorder.IsRecording && EditorApplication.isPlaying))
						{
							if (GUILayout.Button(GUIContents.PlaybackStop.Label(" Stop Recording"), buttonStyle, GUILayout.Height(bigButtonSize)))
							{
								StopRecording(Preferences.instance.stopPlayerOnStopRecording);
							}
						}
					}
				}

				Preferences.instance.stopPlayerOnStopRecording.value = GUILayout.Toggle(Preferences.instance.stopPlayerOnStopRecording, GUIUtils.TempContent(" Stop Play Mode when Stopping Recording"));

				// output path
				height = filePathDrawer.GetPropertyHeight(videoOutputFileProperty, videoOutputPathLabel);
				outputRect = EditorGUILayout.GetControlRect(GUILayout.Height(height));
				EditorGUI.BeginChangeCheck();
				filePathDrawer.OnGUI(outputRect, videoOutputFileProperty, videoOutputPathLabel);
				if (EditorGUI.EndChangeCheck())
				{
					Preferences.instance.videoOutputFile = videoOutputFile;
					Preferences.Save();
				}

				GUIUtils.Separator();

				// projects/reviews tree view
				treeView.OnGUILayout();

				GUIUtils.Separator();

				// auto upload checkbox
				using (GUIUtils.Enabled(selectedReview != null && selectedReview.isValid))
				{
					string label = string.Format(" Auto upload files to selected review ({0})", selectedReview != null && selectedReview.isValid ? "'" + selectedReview.name + "'" : "none selected");
					var guiContent = GUIUtils.TempContent(label, "Automatically upload the screenshot/video once it has been taken/recorded to the currently selected review.");
					Preferences.instance.uploadToReviewAfterScreenshot.value = GUILayout.Toggle(Preferences.instance.uploadToReviewAfterScreenshot, guiContent);
				}

				// Select a review warning
				if (Event.current.type == EventType.Layout)
				{
					// can only update this flag on Layout to avoid IMGUI layout mismatch errors
					showReviewUploadWarning = Preferences.instance.uploadToReviewAfterScreenshot && (selectedReview == null || !selectedReview.isValid);
				}

				if (showReviewUploadWarning)
				{
					EditorGUILayout.HelpBox("Please select a review for the 'Automatically upload screenshot to review' option to work.", MessageType.Warning);
				}

				GUIUtils.Separator();

				// last saved file(s)
				// - video
				if (lastRecordings.Count > 0)
				{
					// - one video
					if (lastRecordings.Count == 1)
					{
						var lastRecording = lastRecordings[0];
						using (GUIUtils.Enabled(lastRecording.fileExists))
						{
							using (GUIUtils.Horizontal)
							{
								GUILayout.Label(GUIUtils.TempContent("Last Recorded Video:"), GUILayout.ExpandWidth(false));
								var rect = EditorGUILayout.GetControlRect();
								EditorGUI.DrawRect(rect, Color.black * 0.1f);
								GUI.Label(rect, lastRecording.filename);

								using (GUIUtils.Enabled(lastRecording.fileExists))
								{
									rect = EditorGUILayout.GetControlRect(GUILayout.Width(26));
									if (GUI.Button(rect, GUIContents.PlayMediaIcon.Tooltip("Play the clip with the default application"), EditorStyles.miniButton))
									{
										if (File.Exists(lastRecording.fullPath))
										{
											System.Diagnostics.Process.Start(lastRecording.fullPath);
										}
										else
										{
											EditorUtility.DisplayDialog("SyncSketch : Error", string.Format("The file does not exists:\n'{0}'", lastRecording.fullPath), "OK");
											lastRecording.fileExists = false;
										}
									}
									rect = EditorGUILayout.GetControlRect(GUILayout.Width(26));
									if (GUI.Button(rect, GUIContents.ExternalIcon.Tooltip(GUIUtils.revealInExplorer), EditorStyles.miniButton))
									{
										EditorUtility.RevealInFinder(lastRecording.fullPath);
									}
								}
							}
						}
					}
					// - multiple videos
					else
					{
						GUILayout.Label(GUIUtils.TempContent("Last Recorded Videos:"));

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
									if (GUI.Button(rect, GUIContents.ExternalIcon.Tooltip(GUIUtils.revealInExplorer), EditorStyles.miniButton))
									{
										EditorUtility.RevealInFinder(lastRecordings[i].fullPath);
									}

									using (GUIUtils.Enabled((selectedReview != null && selectedReview.isValid)))
									{
										rect = EditorGUILayout.GetControlRect(GUILayout.Width(26));
										if (GUI.Button(rect, GUIContents.UploadIcon.Tooltip(GUI.enabled ? "Upload to selected review" : "Please select a review to be able to upload the file"), EditorStyles.miniButton))
										{
											if (syncSketch == null || !syncSketch.isValid)
											{
												EditorUtility.DisplayDialog("SyncSketch : Error", "You don't seem to be logged in to SyncSketch", "OK");
											}
											else
											{
												//verify that the file seems valid
												var fileInfo = new FileInfo(lastRecordings[i].fullPath);
												if (!fileInfo.Exists)
												{
													EditorUtility.DisplayDialog("SyncSketch : Error", string.Format("The video file does not exist anymore:\n'{0}'", lastRecordings[i].fullPath), "OK");
													var r = lastRecordings[i];
													r.fileExists = false;
													lastRecordings[i] = r;
												}
												else if (fileInfo.Length < 1000)
												{
													// consider files less than 1kB to be invalid (only header written, most likely error during recording)
													EditorUtility.DisplayDialog("SyncSketch : Error", string.Format("The video file doesn't seem to be valid:\n'{0}'", lastRecordings[i].fullPath), "OK");
												}
												else
												{
													// upload the video file to the selected review
													var bytes = File.ReadAllBytes(lastRecordings[i].fullPath);
													UploadMediaFile(bytes, lastRecordings[i].filename, "video/mp4", true);
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
					}
				}
				else
				// - screenshot
				{
					using (GUIUtils.Horizontal)
					{
						EditorGUILayout.PrefixLabel(GUIUtils.TempContent("Last Screenshot File:"), EditorStyles.textField);
						EditorGUILayout.TextField(lastScreenshotFilename);

						using (GUIUtils.Enabled(lastScreenshotFileExists))
						{
							/*
							var rect = EditorGUILayout.GetControlRect(GUILayout.Width(26));
							if (GUI.Button(rect, GUIContents.ClipboardIcon.Tooltip("Copy image to clipboard"), EditorStyles.miniButton))
							{
								// copy to clipboard
								if (File.Exists(lastScreenshotFullPath))
								{
									CopyToClipboard(lastScreenshotFullPath);
									ShowNotification(GUIUtils.TempContent("Image copied to clipboard"));
								}
								else
								{
									EditorUtility.DisplayDialog("SyncSketch : Error", string.Format("The file does not exist:\n'{0}'", lastScreenshotFullPath), "OK");
									lastScreenshotFileExists = false;
								}
							}
							*/

							var rect = EditorGUILayout.GetControlRect(GUILayout.Width(26));
							if (GUI.Button(rect, GUIContents.PlayMediaIcon.Tooltip("Play the clip with the default application"), EditorStyles.miniButton))
							{
								if (File.Exists(lastScreenshotFullPath))
								{
									System.Diagnostics.Process.Start(lastScreenshotFullPath);
								}
								else
								{
									EditorUtility.DisplayDialog("SyncSketch : Error", string.Format("The file does not exist:\n'{0}'", lastScreenshotFullPath), "OK");
									lastScreenshotFileExists = false;
								}
							}

							rect = EditorGUILayout.GetControlRect(GUILayout.Width(26));
							if (GUI.Button(rect, GUIContents.ExternalIcon.Tooltip(GUIUtils.revealInExplorer), EditorStyles.miniButton))
							{
								EditorUtility.RevealInFinder(lastScreenshotFullPath);
							}
						}
					}
				}

				// "Upload to review" button
				bool showUploadButton = lastRecordings.Count <= 1;
				if (showUploadButton)
				{
					using (GUIUtils.Enabled(selectedReview != null && (lastScreenshotFileExists || lastRecordings.Count > 0)))
					{
						// multiple video files
						if (lastRecordings.Count > 1)
						{

						}
						else
						// one screenshot or one video
						{
							// figure out if we're uploading a screenshot or a video
							bool isVideo = lastRecordings.Count == 1;
							string filename = isVideo ? lastRecordings[0].filename : lastScreenshotFilename;

							string label = selectedReview != null && selectedReview.isValid && !string.IsNullOrEmpty(filename) ? string.Format("Upload '<b>{0}</b>'\nto selected review '<b>{1}</b>'", filename, selectedReview.name) : "Upload";
							if (GUILayout.Button(label, GUIStyles.ButtonRichText, GUILayout.Height(Mathf.CeilToInt(EditorGUIUtility.singleLineHeight * 2.1f))))
							{
								if (syncSketch == null || !syncSketch.isValid)
								{
									EditorUtility.DisplayDialog("SyncSketch : Error", "You don't seem to be logged in to SyncSketch", "OK");
								}
								else
								{
									// upload the image/video file to the selected review
									string fullpath = isVideo ? lastRecordings[0].fullPath : lastScreenshotFullPath;
									byte[] bytes = File.ReadAllBytes(fullpath);
									UploadMediaFile(bytes, filename, isVideo ? "video/mp4" : "image/png", isVideo);
								}
							}
						}
					}
				}

				GUIUtils.Separator();

				// Last uploaded files list
				var lastReviews = Preferences.instance.lastScreenshotUploads;

				if (lastReviews != null)
				{
					ReviewUploadInfo.DrawList("Last Screenshot Uploads:", lastReviews.Count, (index) => lastReviews[index], (notification) => this.ShowNotification(GUIUtils.TempContent(notification)),
					(deletedIndex) =>
					{
						Preferences.instance.lastScreenshotUploads.RemoveAt(deletedIndex);
						Preferences.Save();
					});
				}

				EditorGUILayout.EndScrollView();
			}

			GUILayout.EndArea();
			EditorGUIUtility.labelWidth = prevLabelWidth;

			GUI.enabled = guiEnabled;
		}

		void OnTreeViewItemSelected(API.SyncSketchItem item)
		{
			if (item.id > 0)
			{
				Preferences.instance.lastSelectedItemId.value = item.id;
			}

			if (item == null)
			{
				selectedProject = null;
				selectedReview = null;
			}

			var project = item as API.Project;
			if (project != null)
			{
				OnSelectProject(project);
				return;
			}

			var review = item as API.Review;
			if (review != null)
			{
				OnSelectReview(review);
				return;
			}
		}

		void OnSelectProject(object projectObj)
		{
			var project = projectObj as API.Project;
			if (project == null)
			{
				return;
			}
			if (project == selectedProject)
			{
				return;
			}
			OnSelectProject(project);
		}

		void OnSelectProject(API.Project project)
		{
			selectedProject = project;
			selectedReview = null;
			treeView?.SetSelection(project.id, TreeViewSelectionOptions.RevealAndFrame);
		}

		void OnSelectReview(object reviewObj)
		{
			var review = reviewObj as API.Review;
			if (review == null)
			{
				return;
			}

			OnSelectReview(review);
		}

		void OnSelectReview(API.Review review)
		{
			selectedReview = review;
			treeView?.SetSelection(review.id, TreeViewSelectionOptions.RevealAndFrame);
		}

		void DoAddReview(string name, string description, object projectObj)
		{
			var project = (API.Project)projectObj;

			if (string.IsNullOrEmpty(name))
			{
				Log.Error("Review name can't be empty");
				return;
			}

			Log.Message("Add review: " + name + " : " + description);

			var newReview = project.AddReview(syncSketch, name, description);
			if (newReview != null)
			{
				treeView.Reload();
				OnSelectReview(newReview);
			}
			else
			{
				Log.Error("Failed to create review.");
			}
		}

		#endregion

		#region Logging In

		void DoLogin(string username, string apiKey)
		{
			API.Login_Async(username, apiKey, OnLoginResponse, UpdateProgressBar, true);
			blockingRequests++;
		}

		void OnLoginResponse(bool isError, API syncSketchObject)
		{
			blockingRequests--;
			progressBar = 0;
			if (isError || syncSketchObject == null)
			{
				Log.Error("Couldn't login.");
			}
			else
			{
				syncSketch = syncSketchObject;
				FocusLastSelectedItem();
			}
			Repaint();
		}

		void DoLogout()
		{
			PersistentSession.Logout();
			syncSketch = null;
			treeView = null;
		}

		void InitTreeView()
		{
			if (treeView == null)
			{
				treeViewState = new SyncSketchTreeView.State(Preferences.instance.treeViewRowsCount);
				treeViewState.onVisibleRowCountChange += (count) => Preferences.instance.treeViewRowsCount.value = count;
				treeView = new SyncSketchTreeView(syncSketch, treeViewState, "Select a review to upload to:", OnSync);
				treeView.itemSelected += OnTreeViewItemSelected;
			}
		}

		#endregion

		#region Take Screenshot

		// Reflected field to find the exact position of the Scene View, allowing us to take a screenshot that includes all the gizmos

		// UnityEditor.EditorWindow.m_Parent
		static System.Reflection.FieldInfo m_Parent = typeof(EditorWindow).GetField("m_Parent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		// UnityEditor.View.screenPosition
		static System.Reflection.PropertyInfo screenPosition = typeof(EditorWindow).Assembly.GetType("UnityEditor.View").GetProperty("screenPosition", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);

		/// <summary>
		/// Returns the exact screen position of the Scene View
		/// </summary>
		/// <returns>The position if found, else Rect.zero</returns>
		static Rect GetSceneViewScreenPosition(SceneView sceneView)
		{
			if (m_Parent != null && screenPosition != null)
			{
				var view = m_Parent.GetValue(sceneView);
				if (view != null)
				{
					var position = (Rect)screenPosition.GetValue(view, null);
					return position;
				}
			}

			return Rect.zero;
		}

		/// <summary>
		/// Take a full screenshot of the Scene View
		/// </summary>
		void DoTakeScreenshotSceneView()
		{
			if (SceneView.lastActiveSceneView != null && SceneView.lastActiveSceneView.camera != null)
			{
				// This method also captures all UI elements (lights icons, grid, etc.), but I can't reliably find the accurate Scene View position yet
				/*
				var cam = SceneView.lastActiveSceneView.camera;
				Rect pos = SceneView.lastActiveSceneView.position;
				const float sceneViewHeaderOffset = 36; // number of pixels above the actual scene view
				var colors = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(new Vector2(pos.x, pos.y + sceneViewHeaderOffset), cam.pixelWidth, cam.pixelHeight);
				var screenshotTexture = new Texture2D(cam.pixelWidth, cam.pixelHeight, TextureFormat.ARGB32, false);
				screenshotTexture.SetPixels(colors);
				screenshotTexture.Apply(false, false);
				*/

				Texture2D screenshotTexture;
				var screenPos = Preferences.instance.captureSceneViewGizmos ? GetSceneViewScreenPosition(SceneView.lastActiveSceneView) : Rect.zero;
				if (Preferences.instance.captureSceneViewGizmos && screenPos.width > 0 && screenPos.height > 0)
				{
					screenshotTexture = SceneViewScreenshot(screenPos);
				}
				else
				{
					screenshotTexture = CameraToTexture(SceneView.lastActiveSceneView.camera);
				}

				// save to disk and send for review
				string fullPath = Utils.Path.UniqueFileName(Utils.Path.ForwardSlashes(string.Format("{0}.png", screenshotOutputFile)));
				SaveScreenshotPNG(screenshotTexture, fullPath);
				DestroyImmediate(screenshotTexture);
			}
			else
			{
				EditorUtility.DisplayDialog("SyncSketch : Error", "No Scene View could be found.\nMake sure that a Scene View window is opened.", "OK");
			}
		}

		void OnMediaUploaded(string reviewURL)
		{
			EditorGUIUtility.systemCopyBuffer = reviewURL;
			ShowNotification(new GUIContent("Review URL copied to clipboard"));
			Application.OpenURL(reviewURL);
		}

		/// <summary>
		/// Set to true if at least one Scene View is opened in the Editor.
		/// Updated when the SyncSketch window is focused.
		/// </summary>
		bool sceneViewAvailable;

		void FocusLastSelectedItem()
		{
			// try to retrieve and focus the latest selected item
			int id = Preferences.instance.lastSelectedItemId;
			if (id > 0 && treeView != null)
			{
				if (treeView.HasItem(id))
				{
					treeView.SetSelection(id, TreeViewSelectionOptions.RevealAndFrame);
					var item = syncSketch.FindItemById(id);
					OnTreeViewItemSelected(item);
					this.Repaint();
				}
			}
		}

		/// <summary>
		/// Take a full screenshot of the Game View
		/// </summary>
		void DoTakeScreenshotGameView()
		{
			//Note: this function has no callback, so we'd have to manually check for the file creation before being able to send the file for review
			/*
			ScreenCapture.CaptureScreenshot(@"S:\WORK\FREELANCE\SYNCSKETCH\SyncSketch Unity\__screenshots\game_view.png");

			// The screenshot will only be taken next time the Game View is redrawn, which means that it needs manual focus first.
			// This method ensures that it is repainted so that we can take the screenshot immediately, provided that the Game View is visible in the Editor.
			var typeGameView = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
			if (typeGameView != null)
			{
				// UnityEditor.GameView : public static void RepaintAll()
				var methodRepaintAll = typeGameView.GetMethod("RepaintAll", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
				if (methodRepaintAll != null)
				{
					methodRepaintAll.Invoke(null, null);
				}
			}

			return;
			*/

			var mainCam = Camera.main;
			if (mainCam == null)
			{
				var allCameras = Resources.FindObjectsOfTypeAll<Camera>();
				foreach (var cam in allCameras)
				{
					if (cam.name == "SceneCamera")
					{
						continue;
					}

					mainCam = cam;
					break;
				}

				if (mainCam != null)
				{
					EditorUtility.DisplayDialog("SyncSketch", string.Format("Can't find a Main Camera in the current scene.\nThe following camera will be used:\n'{0}'", mainCam.name), "OK");
				}
			}

			if (mainCam != null)
			{
				var screenshotTexture = CameraToTexture(mainCam);

				// save to disk and send for review
				string fullPath = Utils.Path.UniqueFileName(Utils.Path.ForwardSlashes(string.Format("{0}.png", screenshotOutputFile)));
				SaveScreenshotPNG(screenshotTexture, fullPath);
				DestroyImmediate(screenshotTexture);
			}
			else
			{
				EditorUtility.DisplayDialog("SyncSketch : Error", "Can't find any camera in the current scene.", "OK");
			}
		}

		void UploadMediaFile(byte[] data, string filename, string mimeType, bool noConvert)
		{
			blockingRequests++;
			void callback(bool isError, string message)
			{
				blockingRequests--;
				progressBar = 0;

				if (!isError)
				{
					var json = TinyJSON.JSON.Load(message);
					var reviewURL = json["reviewURL"];
					OnMediaUploaded(reviewURL);

					var uploadInfo = new ReviewUploadInfo()
					{
						filename = filename,
						reviewName = selectedReview.name,
						reviewURL = reviewURL,
						date = json["created"]
					};
					AddToLastUploads(uploadInfo);
				}
				else
				{
					Log.Error("An error occurred: " + message);
				}
			}
			selectedReview.UploadMediaAsync(syncSketch, callback, UpdateProgressBar, data, filename, mimeType, noConvertFlag: noConvert);
		}

		void AddToLastUploads(ReviewUploadInfo uploadInfo)
		{
			Preferences.instance.lastScreenshotUploads.Insert(0, uploadInfo);
			while (Preferences.instance.lastScreenshotUploads.Count > 10)
			{
				Preferences.instance.lastScreenshotUploads.RemoveAt(10);
			}
			Preferences.Save();
		}

		/// <summary>
		/// Start snipping a screenshot: user will drag a screenshot rectangle in the Scene View using the right mouse button.
		/// </summary>
		void StartSnippingScreenshot()
		{
			if (snippingScreenshot)
			{
				return;
			}

			snippingScreenshot = true;
			SceneView.onSceneGUIDelegate += OnSceneGUI;
			if (SceneView.lastActiveSceneView != null)
			{
				SceneView.lastActiveSceneView.Focus();
			}
		}

		bool snippingScreenshot;
		bool draggingRect;
		Vector2 dragStart;
		Rect dragRect;

		void StartDrag()
		{
			draggingRect = true;
			dragStart = Event.current.mousePosition;
		}

		void EndDrag()
		{
			draggingRect = false;
			snippingScreenshot = false;
			SceneView.onSceneGUIDelegate -= OnSceneGUI;
			dragStart = Vector2.zero;
			dragRect = Rect.zero;

			// force the UI to refresh (re-enable take screenshot button)
			Repaint();
		}

		void OnSceneGUI(SceneView sceneView)
		{
			var evt = Event.current;
			float pixelRatio = EditorGUIUtility.pixelsPerPoint;

			// use the right mouse button because the left one starts the default selection rect in the Scene View
			if (evt.type == EventType.MouseDown && evt.button == 1)
			{
				StartDrag();
				evt.Use();
			}

			// cancel the drag/screenshot?
			HandleScreenshotCancelEvents(evt);

			if (draggingRect)
			{
				EditorGUIUtility.AddCursorRect(new Rect(evt.mousePosition.x - 50, evt.mousePosition.y - 50, 100, 100), MouseCursor.ScaleArrow);

				// calculate the current dragging rectangle
				if (evt.type == EventType.MouseDrag)
				{
					dragRect.xMin = Mathf.Min(dragStart.x, evt.mousePosition.x);
					dragRect.yMin = Mathf.Min(dragStart.y, evt.mousePosition.y);
					dragRect.xMax = Mathf.Max(dragStart.x, evt.mousePosition.x);
					dragRect.yMax = Mathf.Max(dragStart.y, evt.mousePosition.y);
					evt.Use();
				}

				if (evt.type == EventType.MouseUp || evt.rawType == EventType.MouseUp)
				{
					var screenPos = Preferences.instance.captureSceneViewGizmos ? GetSceneViewScreenPosition(SceneView.lastActiveSceneView) : Rect.zero;
					if (Preferences.instance.captureSceneViewGizmos && screenPos.width > 0 && screenPos.height > 0)
					{
						// header of the Scene View port
						const float sceneViewHeader = 34;

						Rect offsetRect = dragRect;
						offsetRect.x += screenPos.x;
						offsetRect.y += screenPos.y + sceneViewHeader;

						// delay to make sure our own overlay UI is not captured
						EditorApplication.delayCall += () =>
						{
							var screenshotTexture = SceneViewScreenshot(offsetRect);
							string fullPath = Utils.Path.UniqueFileName(Utils.Path.ForwardSlashes(string.Format("{0}.png", screenshotOutputFile)));
							SaveScreenshotPNG(screenshotTexture, fullPath);
							DestroyImmediate(screenshotTexture);
						};

						SceneView.RepaintAll();
					}
					else
					{
						// full screen screenshot (scene view)
						var screenshotTexture = CameraToTexture(sceneView.camera);

						// crop the result
						int camWidth = sceneView.camera.pixelWidth;
						int camHeight = sceneView.camera.pixelHeight;

						Rect offsetRect = dragRect;
						offsetRect.x *= pixelRatio;
						offsetRect.width *= pixelRatio;
						offsetRect.height *= pixelRatio;
						offsetRect.y *= pixelRatio;
						// U  or y coordinates are flipped, so we measure from the bottom of the texture and offset by the height
						offsetRect.y = screenshotTexture.height - offsetRect.y - offsetRect.height;

						int x = (int)Mathf.Clamp(offsetRect.x, 0, camWidth);
						int y = (int)Mathf.Clamp(offsetRect.y, 0, camHeight);
						int w = (int)Mathf.Clamp(offsetRect.width, 1, camWidth - offsetRect.x);
						int h = (int)Mathf.Clamp(offsetRect.height, 1, camHeight - offsetRect.y);

						var pixels = screenshotTexture.GetPixels(x, y, w, h);
						screenshotTexture.Resize(w, h);
						screenshotTexture.SetPixels(pixels);
						screenshotTexture.Apply(false, false);

						// save to disk
						string fullPath = Utils.Path.UniqueFileName(Utils.Path.ForwardSlashes(string.Format("{0}.png", screenshotOutputFile)));
						SaveScreenshotPNG(screenshotTexture, fullPath);
						DestroyImmediate(screenshotTexture);
					}

					EndDrag();
					evt.Use();
				}
			}

			if (evt.type == EventType.Repaint)
			{
				Handles.BeginGUI();

				// calculate the rectangles _outside_ the current drag rect, to dim all the surroundings

				var dimColor = new Color(0, 0, 0, 0.33f);
				var width = sceneView.position.width;
				var height = sceneView.position.height;

				var topRect = new Rect(0, 0, width, dragRect.yMin);
				EditorGUI.DrawRect(topRect, dimColor);

				var bottomRect = new Rect(0, dragRect.yMax, width, height - dragRect.yMax);
				EditorGUI.DrawRect(bottomRect, dimColor);

				var leftRect = new Rect(0, dragRect.yMin, dragRect.xMin, dragRect.yMax - dragRect.yMin);
				EditorGUI.DrawRect(leftRect, dimColor);

				var rightRect = new Rect(dragRect.xMax, dragRect.yMin, width - dragRect.xMax, dragRect.yMax - dragRect.yMin);
				EditorGUI.DrawRect(rightRect, dimColor);

				var guiContent = GUIUtils.TempContent("Use the right mouse button to drag a screenshot rectangle.\nPress ESC to cancel.");
				var style = EditorGUIUtility.isProSkin ? EditorStyles.boldLabel : EditorStyles.whiteBoldLabel; // whiteBoldLabel is actually black in dark mode
				var size = style.CalcSize(guiContent);
				EditorGUI.DrawRect(new Rect(10, 10, size.x + 20, size.y + 20), dimColor);
				GUI.Label(new Rect(20, 20, size.x, size.y), guiContent, style);

				Handles.EndGUI();
			}
		}

		void SaveScreenshotPNG(Texture2D screenshotTexture, string fullPath)
		{
			byte[] screenshotBytes = screenshotTexture.EncodeToPNG();
			File.WriteAllBytes(fullPath, screenshotBytes);
			lastScreenshotFullPath = fullPath;
			lastScreenshotFilename = Path.GetFileName(fullPath);
			lastScreenshotFileExists = true;

			// replace any pending videos
			lastRecordings.Clear();

			string filename = Path.GetFileName(fullPath);
			if (selectedReview != null && Preferences.instance.uploadToReviewAfterScreenshot)
			{
				// upload to review: the review URL will be copied to clipboard
				UploadMediaFile(screenshotBytes, filename, "image/png", false);
			}
			else
			{
				// else copy the image data to clipboard
				/*
				CopyToClipboard(fullPath);
				*/
			}

			ShowNotification(new GUIContent(string.Format("Screenshot saved:\n'{0}'", filename)));
		}

		/// <summary>
		/// Handle events that could interrupt taking the screenshot
		/// </summary>
		void HandleScreenshotCancelEvents(Event evt)
		{
			if (!snippingScreenshot)
			{
				return;
			}

			// cancel the drag/screenshot
			if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
			{
				EndDrag();
				evt.Use();
			}
		}

		/// <summary>
		/// Render the camera into a Texture2D
		/// </summary>
		static Texture2D CameraToTexture(Camera camera, bool withAlpha = false)
		{
			var prevTarget = camera.targetTexture;
			var rt = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
			camera.targetTexture = rt;
			camera.Render();
			camera.targetTexture = prevTarget;

			var prevActive = RenderTexture.active;
			RenderTexture.active = rt;
			var readTex = new Texture2D(rt.width, rt.height, withAlpha ? TextureFormat.ARGB32 : TextureFormat.RGB24, false);
			readTex.ReadPixels(new Rect(0, 0, readTex.width, readTex.height), 0, 0, false);
			readTex.Apply(false, false);
			RenderTexture.active = prevActive;

			rt.Release();
			Object.DestroyImmediate(rt);

			return readTex;
		}

		static Texture2D SceneViewScreenshot(Rect screenPosition, bool withAlpha = false)
		{
			int width = (int)screenPosition.width;
			int height = (int)screenPosition.height;
			var pixels = UnityEditorInternal.InternalEditorUtility.ReadScreenPixel(new Vector2(screenPosition.x, screenPosition.y), width, height);
			var readTex = new Texture2D(width, height, withAlpha ? TextureFormat.ARGB32 : TextureFormat.RGB24, false);
			readTex.SetPixels(pixels);
			readTex.Apply(false, false);

			return readTex;
		}

		// Disabled for now, System.Windows.Forms & System.Drawing DLLs don't work properly everwhere
		/*
		static void CopyToClipboard(string fullpath)
		{
			var image = System.Drawing.Image.FromFile(fullpath);
			System.Windows.Forms.Clipboard.SetImage(image);
		}
		*/

		#endregion

		#region Record Video

		void DoRecordVideo()
		{
			if (EditorApplication.isPlaying)
			{
				// trigger recording during play mode
				InitVideoRecorder();
			}
			else
			{
				startRecordingWithPlayMode = true;
				EditorApplication.isPlaying = true;
			}
		}

		void InitVideoRecorder()
		{
			// try to fetch the one from the main camera
			if (videoRecorder == null)
			{
				var mainCam = Camera.main;
				if (mainCam != null)
				{
					videoRecorder = mainCam.GetComponent<SyncSketchRecorder>();
				}
			}

			// else create a temporary recorder
			if (videoRecorder == null)
			{
				var gameObject = new GameObject("SyncSketch Video Recorder");
				DontDestroyOnLoad(gameObject);
				// gameObject.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
				var postCamera = gameObject.AddComponent<Camera>();
				postCamera.clearFlags = CameraClearFlags.Nothing;
				postCamera.cullingMask = 0;
				postCamera.farClipPlane = 1;
				postCamera.depth = 100;
				postCamera.allowMSAA = false;
				postCamera.allowHDR = false;
				postCamera.hideFlags = HideFlags.NotEditable;

				// Added at the start of play mode, so it will destroy itself when stopping because
				// it was not part of the serialized scene beforehand.
				videoRecorder = gameObject.AddComponent<SyncSketchRecorder>();
				videoRecorder.toolbox = true;
				videoRecorder.recordingSettings = new SyncSketchRecorder.RecordingSettings()
				{
					frameRate = 30,
					useGameResolution = true,
					noFrameSkip = true,
					outputFile = videoOutputFile
				};
				videoRecorder.hideFlags = HideFlags.NotEditable;
			}

			videoRecorder.StartRecording();
		}

		void StopRecording(bool stopPlayMode)
		{
			if (videoRecorder != null)
			{
				videoRecorder.StopRecording();
				// Destroy(videoRecorder.gameObject);
			}

			if (stopPlayMode)
			{
				EditorApplication.isPlaying = false;
			}
		}

		#endregion

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
			blockingRequests++;

			return true;
		}

		// During multiple-file upload, called when one part has finished
		void OnUploadFinishedMultiple(bool isError, string message)
		{
			blockingRequests--;

			if (isError)
			{
				// error
				EditorUtility.DisplayDialog("SyncSketch : Error", "An error occurred during upload:\n\n" + message, "OK");
			}

			var recording = lastRecordings[uploadMultipleIndex];
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

				// add to list of recent uploaded reviews
				var uploadInfo = new ReviewUploadInfo()
				{
					filename = recording.filename,
					reviewName = selectedReview.name,
					reviewURL = reviewURL,
					date = json["created"]
				};
				AddToLastUploads(uploadInfo);

				if (allDone)
				{
					// feedback
					EditorUtility.DisplayDialog("SyncSketch", "The files have been successfully uploaded for review.", "OK");

					// open review URL
					Application.OpenURL(reviewURL);
				}
			}
		}

		// During multiple-file upload
		void OnUploadProgressMultiple(float progress)
		{
			float realProgress = (uploadMultipleIndex + progress) / lastRecordings.Count;

			EditorUtility.DisplayProgressBar("SyncSketch", string.Format("Uploading video {0}/{1} to review '{2}'...", uploadMultipleIndex+1, lastRecordings.Count, selectedReview.name), realProgress);
			progressBar = progress;
		}

		#endregion
	}
}
