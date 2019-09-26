#define LOGIN_WITH_API_KEY

using System;
using System.Collections.Generic;
using UnityEngine;
using System.Diagnostics;
using Debug = UnityEngine.Debug;
using System.Reflection;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using System.IO;
#endif

// Utilities related to SyncSketch

namespace SyncSketch
{
	public static class Utils
	{
		public static class Path
		{
			static string _ProjectRoot;
			public static string ProjectRoot
			{
				get
				{
					if (_ProjectRoot == null)
					{
						_ProjectRoot = ForwardSlashes(System.IO.Path.GetDirectoryName(Application.dataPath));
					}
					return _ProjectRoot;
				}
			}

			public static string ForwardSlashes(string path)
			{
				return path.Replace(@"\", "/");
			}

			public static string RelativeToProject(string path)
			{
				return path.Replace(ProjectRoot, "");
			}

			public static bool IsInProjectRoot(string path)
			{
				return path.StartsWith(ProjectRoot);
			}

			public static string UniqueFileName(string fullPath)
			{
				string directory = System.IO.Path.GetDirectoryName(fullPath);
				string filename = System.IO.Path.GetFileNameWithoutExtension(fullPath);
				string extension = System.IO.Path.GetExtension(fullPath);
				int count = 1;
				while (System.IO.File.Exists(fullPath))
				{
					fullPath = string.Format("{0}/{1} {2}{3}", directory, filename, count, extension);
					count++;
				}
				return fullPath;
			}
		}
	}

	/// <summary>
	/// Small class to hold informations about an uploaded file and its related review
	/// </summary>
	[Serializable]
	public class ReviewUploadInfo
	{
		public string filename;
		public string reviewName;
		public string reviewURL;
		public string date;


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
		static Color? _evenRowCOlor;
		static Color evenRowCOlor
		{
			get
			{
				if (_evenRowCOlor == null)
				{
					_evenRowCOlor = EditorGUIUtility.isProSkin ? new Color(1, 1, 1, 0.012f) : new Color(0, 0, 0, 0.02f);
				}
				return _evenRowCOlor.Value;
			}
		}

		/// <summary>
		/// Draws a list of Review Upload Info with buttons to go to the review URL, delete elements, etc.
		/// </summary>
		public static void DrawList(string label, int length, Func<int, ReviewUploadInfo> getElement, Action<string> showNotification, Action<int> onDeleteElement)
		{
			if (length > 0)
			{
				GUILayout.Label(GUIUtils.TempContent(label), EditorStyles.boldLabel);

				int elementToDelete = -1;
				for (int i = 0; i < length; i++)
				{
					var rowRect = EditorGUILayout.GetControlRect(GUILayout.Height(EditorGUIUtility.singleLineHeight*2));
					if (Event.current.type == EventType.Repaint)
					{
						EditorGUI.DrawRect(rowRect, i % 2 == 0 ? evenRowCOlor : oddRowColor);
					}

					// open review button
					var element = getElement(i);
					var btnRect = rowRect;
					btnRect.width = 32;
					rowRect.xMin += btnRect.width + 4;
					if (GUI.Button(btnRect, GUIContents.ExternalIcon.Tooltip("Open review in browser")))
					{
						Application.OpenURL(element.reviewURL);
					}

					// label
					var labelRect = rowRect;
					labelRect.y += 1;
					GUI.Label(labelRect, string.Format("'<b>{0}</b>'\nto review '<b>{1}</b>'", element.filename, element.reviewName), GUIStyles.LabelRichText);

					// delete button
					btnRect = rowRect;
					btnRect.width = 18;
					rowRect.xMax -= btnRect.width + 4;
					btnRect.x += rowRect.width - 4;
					btnRect.height = EditorGUIUtility.singleLineHeight;
					btnRect.y += EditorGUIUtility.singleLineHeight/2;
					if (GUI.Button(btnRect, GUIUtils.TempContent("x", "Remove from list"), EditorStyles.miniButton))
					{
						elementToDelete = i;
					}

					// copy to clipboard button
					btnRect = rowRect;
					btnRect.width = 26;
					rowRect.xMax -= btnRect.width + 4;
					btnRect.x += rowRect.width - 4;
					btnRect.height = EditorGUIUtility.singleLineHeight;
					btnRect.y += EditorGUIUtility.singleLineHeight/2;
					if (GUI.Button(btnRect, GUIContents.UrlIcon.Tooltip("Copy review URL to clipboard"), EditorStyles.miniButton))
					{
						EditorGUIUtility.systemCopyBuffer = element.reviewURL;
						showNotification?.Invoke("Review URL copied to clipboard");
					}
				}

				if (elementToDelete >= 0)
				{
					onDeleteElement(elementToDelete);
				}
			}
		}
	}

	/// <summary>
	/// Represents an output file path
	/// </summary>
	[Serializable]
	public class FilePath
	{
		[SerializeField] string _directory;
		public string directory
		{
			get { return _directory; }
			set
			{
				// make sure there are no backward slashes for the path
				_directory = value.Replace(@"\", "/");
			}
		}
		[SerializeField] public string filename;
		[SerializeField] public string suffix;
		[SerializeField] public string extension;

		public FilePath(string directory, string filename, string extension)
		{
			this.directory = directory;
			this.filename = filename;
			this.extension = extension;
		}

		public FilePath(string filename, string extension)
		{
			this.directory = Utils.Path.ForwardSlashes(Path.GetTempPath());
			this.filename = filename;
			this.extension = extension;
		}

		public override string ToString()
		{
			return string.Format("{0}/{1}{2}", directory, filename, suffix ?? "");
		}
	}

	/// <summary>
	/// [Label] attribute to change a property's display label and tooltip
	/// </summary>
	[AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
	public class LabelAttribute : PropertyAttribute
	{
		public readonly string label;
		public readonly string tooltip;

		public LabelAttribute(string label, string tooltip = null)
		{
			this.label = label;
			this.tooltip = tooltip;
		}
	}

#if UNITY_EDITOR
	[CustomPropertyDrawer(typeof(FilePath))]
	public class FilePathDrawer : PropertyDrawer
	{
		SerializedProperty lastProperty; // could reuse the same FilePathDrawer for multiple file paths
		SerializedProperty directoryProp;
		SerializedProperty filenameProp;
		SerializedProperty extensionProp;
		GUIContent guiContentFullPath = new GUIContent();

		const float rightButtonsWidth = 26;
		const float rightExtensionWidth = 34;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			if (lastProperty != property || directoryProp == null || filenameProp == null)
			{
				directoryProp = property.FindPropertyRelative("_directory");
				filenameProp = property.FindPropertyRelative("filename");
				extensionProp = property.FindPropertyRelative("extension");
				UpdateGuiContent();

				lastProperty = property;
			}

			EditorGUI.BeginChangeCheck();

			var rect = position;
			rect.height = EditorGUIUtility.singleLineHeight;

			var foldoutRect = rect;
			foldoutRect.width = EditorGUIUtility.labelWidth;
			property.isExpanded = EditorGUI.Foldout(foldoutRect, property.isExpanded, label, true);

			// show the current path as a disabled label
			var pathRect = rect;
			pathRect.xMin += foldoutRect.width;

			// button to open the folder
			var openBtnRect = pathRect;
			openBtnRect.width = rightButtonsWidth;
			pathRect.xMax -= openBtnRect.width + 2;
			openBtnRect.x += pathRect.width + 2;
			openBtnRect.y -= 1;
#if UNITY_EDITOR_OSX
			const string revealTooltip = "Reveal in Finder";
#else
			const string revealTooltip = "Reveal in Explorer";
#endif
			if (GUI.Button(openBtnRect, GUIContents.ExternalIcon.Tooltip(revealTooltip), EditorStyles.miniButton))
			{
				string dirPath = directoryProp.stringValue;
				if (Directory.Exists(dirPath))
				{
					EditorUtility.RevealInFinder(dirPath + "/");
				}
				else
				{
					EditorUtility.DisplayDialog("Error", string.Format("The directory does not exist yet:\n{0}\n\nIt will be created when a file is saved there.", dirPath), "OK");
				}
			}

			// right align the label if small enough
			float width = EditorStyles.label.CalcSize(guiContentFullPath).x;
			float diff = pathRect.width - width;
			if (diff > 0)
			{
				pathRect.xMin += diff;
			}

			using (new EditorGUI.DisabledScope(true))
			{
				GUI.Label(pathRect, guiContentFullPath);
			}

			if (property.isExpanded)
			{
				int prevLevel = EditorGUI.indentLevel;
				EditorGUI.indentLevel++;

				// button to browse the folder
				rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
				var dirRect = rect;
				var browseRect = rect;
				browseRect.width = rightButtonsWidth;
				dirRect.xMax -= browseRect.width + 2;
				browseRect.x += dirRect.width + 2;
				EditorGUI.BeginChangeCheck();
				EditorGUI.DelayedTextField(dirRect, directoryProp);
				if (EditorGUI.EndChangeCheck())
				{
					// reset to temporary path if value is empty
					if (string.IsNullOrWhiteSpace(directoryProp.stringValue))
					{
						directoryProp.stringValue = Path.GetTempPath();
					}
				}
				if (GUI.Button(browseRect, GUIContents.BrowseFolderIcon.Tooltip("Browse"), EditorStyles.miniButton))
				{
					string startingPath = Utils.Path.ProjectRoot;
					if (!string.IsNullOrEmpty(directoryProp.stringValue) && Directory.Exists(directoryProp.stringValue))
					{
						startingPath = directoryProp.stringValue;
					}

					string output = EditorUtility.SaveFolderPanel("Recording Path", startingPath, "");
					if (!string.IsNullOrWhiteSpace(output))
					{
						directoryProp.stringValue = output;
					}
				}

				// filename with disabled extension
				rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
				browseRect.y = rect.y;
				browseRect.width = rightExtensionWidth;
				rect.xMax -= browseRect.width + 2;
				browseRect.x = rect.xMax + 2;
				EditorGUI.DelayedTextField(rect, filenameProp);
				bool enabled = GUI.enabled;
				GUI.enabled = false;
				GUI.Label(browseRect, "." + extensionProp.stringValue);
				GUI.enabled = enabled;

				EditorGUI.indentLevel = prevLevel;
			}

			if (EditorGUI.EndChangeCheck())
			{
				NormalizePaths();
				UpdateGuiContent();
				property.serializedObject.ApplyModifiedProperties();
			}
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * (property.isExpanded ? 3 : 1) - EditorGUIUtility.standardVerticalSpacing;
		}

		void UpdateGuiContent()
		{
			guiContentFullPath.text = string.Format("{0}/{1}.{2}", directoryProp.stringValue, filenameProp.stringValue, extensionProp.stringValue);
			guiContentFullPath.tooltip = guiContentFullPath.text;
		}

		void NormalizePaths()
		{
			directoryProp.stringValue = NormalizePath(directoryProp.stringValue).TrimEnd('/', '\\');
			filenameProp.stringValue = NormalizePath(filenameProp.stringValue);
		}

		string NormalizePath(string path)
		{
			if (path != null && !string.IsNullOrEmpty(path))
			{
				// remove invalid path characters
				var invalidChars = new List<char>(System.IO.Path.GetInvalidPathChars());
				string newPath = "";
				for (int i = 0; i < path.Length; i++)
				{
					if (!invalidChars.Contains(path[i]))
					{
						newPath += path[i];
					}
				}
				path = newPath;
			}
			return Utils.Path.ForwardSlashes(path.Trim());
		}
	}

	[CustomPropertyDrawer(typeof(LabelAttribute))]
	public class LabelAttributeDrawer : PropertyDrawer
	{
		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
		{
			EditorGUI.PropertyField(position, property, GUIUtils.TempContent(((LabelAttribute)attribute).label, ((LabelAttribute)attribute).tooltip), property.isExpanded);
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
		{
			return EditorGUI.GetPropertyHeight(property, label, property.isExpanded);
		}
	}

	/// <summary>
	/// Utilities for the GUI
	/// </summary>
	public static class GUIUtils
	{
		#region Constants

#if UNITY_EDITOR_OSX
		public const string revealInExplorer = "Reveal in Finder";
#else
		public const string revealInExplorer = "Reveal in Explorer";
#endif

		#endregion

		#region GUIContent Helper

		static readonly GUIContent guiContent = new GUIContent();

		public static GUIContent TempContent(string label, string tooltip = null)
		{
			guiContent.image = null;
			guiContent.text = label;
			guiContent.tooltip = tooltip;
			return guiContent;
		}

		public static GUIContent Tooltip(this GUIContent guiContent, string tooltip)
		{
			GUIUtils.guiContent.image = guiContent.image;
			GUIUtils.guiContent.text = guiContent.text;
			GUIUtils.guiContent.tooltip = tooltip;
			return GUIUtils.guiContent;
		}

		public static GUIContent Label(this GUIContent guiContent, string label)
		{
			GUIUtils.guiContent.image = guiContent.image;
			GUIUtils.guiContent.text = label;
			GUIUtils.guiContent.tooltip = guiContent.tooltip;
			return GUIUtils.guiContent;
		}


		#endregion

		#region Find Texture Helpers

		static string rootPath;
		static GUIUtils()
		{
			// find with GUID
			string path = AssetDatabase.GUIDToAssetPath("e2a37b343d1bd104e9cf72f799254bac");

			// if the .meta file has been recreated and GUID is different,
			// try to find the path to this script file in the Asset Database
			if (string.IsNullOrEmpty(path))
			{
				var guidMatch = AssetDatabase.FindAssets("SyncSketch.Utils");
				if (guidMatch.Length > 0)
				{
					path = AssetDatabase.GUIDToAssetPath(guidMatch[0]);
				}
			}

			if (string.IsNullOrEmpty(path))
			{
				Log.Error("Couldn't find the root path to load the icons.");
				return;
			}

			// hack: calling GetDirectoryName twice to remove the filename, then the container directory ('Runtime')
			rootPath = Path.GetDirectoryName(Path.GetDirectoryName(path)).Replace(@"\", "/") + "/Editor/Icons";
		}

		public enum FindTextureOption
		{
			None,
			HighResolution,
			HighResolutionOnly
		}

		// Texture2D - internal float pixelsPerPoint
		// Seems to be used internally by Unity for IMGUI textures for high-dpi screens
		static bool _texture2D_pixelsPerPoint_init;
		static PropertyInfo _texture2D_pixelsPerPoint;
		static PropertyInfo texture2D_pixelsPerPoint
		{
			get
			{
				if (!_texture2D_pixelsPerPoint_init && _texture2D_pixelsPerPoint == null)
				{
					_texture2D_pixelsPerPoint_init = true;
					_texture2D_pixelsPerPoint = typeof(Texture2D).GetProperty("pixelsPerPoint", BindingFlags.NonPublic | BindingFlags.Instance);
				}
				return _texture2D_pixelsPerPoint;
			}
		}

		public static Texture2D FindTexture(string filename, FindTextureOption option)
		{
			if (rootPath == null)
			{
				return null;
			}

			string extension = Path.GetExtension(filename);
			string name = Path.GetFileNameWithoutExtension(filename);

			Texture2D texture = null;

			if (option == FindTextureOption.HighResolution || option == FindTextureOption.HighResolutionOnly)
			{
				if (EditorGUIUtility.isProSkin)
				{
					texture = AssetDatabase.LoadAssetAtPath<Texture2D>(string.Format("{0}/{1}_pro_2x{2}", rootPath, name, extension));
				}

				if (texture == null)
				{
					texture = AssetDatabase.LoadAssetAtPath<Texture2D>(string.Format("{0}/{1}_2x{2}", rootPath, name, extension));
				}

				// set internal pixelsPerPoint value of the texture, so that Unity knows it's for high-dpi screens
				if (texture != null && texture2D_pixelsPerPoint != null)
				{
					texture2D_pixelsPerPoint.SetValue(texture, 2.0f);
				}

				if (option == FindTextureOption.HighResolutionOnly)
				{
					return texture;
				}
			}

			if (texture == null && EditorGUIUtility.isProSkin)
			{
				texture = AssetDatabase.LoadAssetAtPath<Texture2D>(string.Format("{0}/{1}_pro{2}", rootPath, name, extension));
			}

			if (texture == null)
			{
				texture = AssetDatabase.LoadAssetAtPath<Texture2D>(string.Format("{0}/{1}{2}", rootPath, name, extension));
			}

			return texture;
		}

		public static void FindBackgrounds(Func<GUIStyleState> stateGetter, string filename)
		{
			stateGetter().background = FindTexture(filename, FindTextureOption.None);
			var hiRes = FindTexture(filename, FindTextureOption.HighResolutionOnly);
			if (hiRes != null)
			{
				stateGetter().scaledBackgrounds = new Texture2D[] { hiRes };
			}
		}

		#endregion

		#region Progress Bar

		static Color indefiniteProgressColor = new Color32(70, 115, 175, 255);

		/// <summary>
		/// Displays a progress bar that has both finite and indefinite bars.
		/// </summary>
		/// <param name="progress">The finite progress from 0 to 1</param>
		/// <param name="indefiniteState">If true, displays an indefinite progress animation</param>
		/// <param name="height">Height of the bars</param>
		public static void LoadingBarFieldLayout(float progress, bool indefiniteState, float height = 4)
		{
			var rect = GUILayoutUtility.GetRect(0, height, GUILayout.ExpandWidth(true));
			LoadingBarField(rect, progress, indefiniteState);
		}

		/// <summary>
		/// Displays a progress bar that has both finite and indefinite bars.
		/// </summary>
		/// <param name="rect">The GUI position to draw into</param>
		/// <param name="progress">The finite progress from 0 to 1</param>
		/// <param name="indefiniteState">If true, displays an indefinite progress animation</param>
		public static void LoadingBarField(Rect rect, float progress, bool indefiniteState)
		{
			EditorGUI.ProgressBar(rect, progress, "");

			// small animation drawing a simple rectangle
			// unfortunately, it's not easy to display an animated GIF for example
			if (indefiniteState)
			{
				float t = (Time.realtimeSinceStartup * 0.5f) % 1.0f;
				var w = 1 - Mathf.Abs(t * 2 - 1);
				float fullWidth = rect.width;
				rect.yMin++;
				rect.yMax--;
				rect.width = w * fullWidth/4;
				rect.x += Mathf.SmoothStep(0, 1, t) * fullWidth - rect.width/2;
				EditorGUI.DrawRect(rect, indefiniteProgressColor);
			}
		}

		#endregion

		#region Login/Logout bar

		static string _username;
		static string username
		{
			get
			{
				if (_username == null)
				{
					_username  = Preferences.instance.savedUsername;
					if (_username == null)
					{
						_username = "";
					}
				}
				return _username;
			}
			set { _username = value; }
		}
		static string _apiKey;
		static string apiKey
		{
			get
			{
				if (_apiKey == null)
				{
					_apiKey  = Preferences.instance.savedApikey;
					if (_apiKey == null)
					{
						_apiKey = "";
					}
				}
				return _apiKey;
			}
			set { _apiKey = value; }
		}
		static bool readyForLogin;
		static bool waitingForResponse;
		static bool didAutoLog;
		static bool promptForApiKey;

		/// <summary>
		/// Displays a login button if not logged in, and a logout button with user information if logged in.
		/// </summary>
		/// <param name="syncSketch">The related SyncSketch.API object</param>
		/// <param name="onLogin">What to do when the "Log In" button is pressed. If null, "API.Login" will be called.</param>
		/// <param name="onLogout">What to do when the "Log Out" button is pressed</param>
		public static void LoginField(API syncSketch, Action<string, string> onLogin = null, Action onLogout = null, Action onCancel = null)
		{
			if (!didAutoLog)
			{
				didAutoLog = true;

				// try to log in automatically if the relevant option is enabled, and user was logged in when closing Unity previously
				if ((syncSketch == null || !syncSketch.isValid) && Preferences.instance.autoLogin && Preferences.instance.wasLoggedIn)
				{
					username = Preferences.instance.savedUsername;
					apiKey = Preferences.instance.savedApikey;

					if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(apiKey))
					{
						readyForLogin = true;
					}
				}
			}

			if (readyForLogin)
			{
				readyForLogin = false;
				if (onLogin == null)
				{
					API.Login(username, apiKey, true);
				}
				else
				{
					onLogin(username, apiKey);
				}

				Preferences.instance.savedUsername.value = username;
				Preferences.instance.savedApikey.value = apiKey;

#if LOGIN_WITH_API_KEY
				waitingForResponse = false;
#else
				username = null;
				apiKey = null;
#endif
			}

			void onLoginInternal()
			{

#if LOGIN_WITH_API_KEY
				readyForLogin = true;
#else
				BrowserLogin.LoginThroughBrowser((username, apiKey) =>
				{
					// login has to be called from the main thread,
					// because the persistent option uses a ScriptableObject
					GUIUtils.username = username;
					GUIUtils.apiKey = apiKey;
					GUIUtils.readyForLogin = true;
					waitingForResponse = false;
				});
#endif
			}

			void onLogoutInternal()
			{
				syncSketch.Logout();
				onLogout?.Invoke();
			}

			LoginFieldInternal(syncSketch, onLoginInternal, onLogoutInternal, onCancel);
		}

		static float buttonHeight = EditorGUIUtility.singleLineHeight * 2 + EditorGUIUtility.standardVerticalSpacing;
		const float buttonWidth = 80;
		static void LoginFieldInternal(API syncSketch, Action onLogin, Action onLogout, Action onCancel = null)
		{
			if (syncSketch == null || !syncSketch.isValid)
			{
				// Not logged in

				// Initial view: login button that opens URL in browser
				if (!promptForApiKey)
				{
					using (GUIUtils.Horizontal)
					{
						var logoRect = EditorGUILayout.GetControlRect(GUILayout.Width(34), GUILayout.Height(34));
						logoRect.y += 1;
						if (Event.current.type == EventType.Repaint)
						{
							GUIStyles.SyncSketchLogo.Draw(logoRect, false, false, false, false);
						}

						var labelRect = EditorGUILayout.GetControlRect(true, buttonHeight);
						labelRect.y += 10;
						GUI.Label(labelRect, GUIUtils.TempContent("SyncSketch"), EditorStyles.boldLabel);

						GUILayout.FlexibleSpace();

						// Could be waiting for response here during auto-login
						using (GUIUtils.Enabled(!waitingForResponse))
						{
							if (GUILayout.Button("Log In to SyncSketch", GUILayout.Height(buttonHeight), GUILayout.Width(buttonWidth * 2)))
							{
								Application.OpenURL("https://syncsketch.com/login/?next=/users/getToken/?show_key=1");
								promptForApiKey = true;
							}
						}
					}
				}
				// Second view: asks for username/api key, retrieved manually by user from browser
				else
				{
#if LOGIN_WITH_API_KEY
					using (GUIUtils.Enabled(!waitingForResponse))
					{
						using (GUIUtils.Horizontal)
						{
							var logoRect = EditorGUILayout.GetControlRect(GUILayout.Width(34), GUILayout.Height(34));
							logoRect.y += 1;
							if (Event.current.type == EventType.Repaint)
							{
								GUIStyles.SyncSketchLogo.Draw(logoRect, false, false, false, false);
							}

							var rect = EditorGUILayout.GetControlRect(false, 34);
							rect.y += 1;
							EditorGUI.HelpBox(rect, "Please retrieve and enter your Username and Api Key from SyncSketch website.", MessageType.Info);
						}

						using (GUIUtils.Horizontal)
						{
							var rect = EditorGUILayout.GetControlRect(GUILayout.Height(buttonHeight));
							rect.height = EditorGUIUtility.singleLineHeight;
							rect.y += 1;
							var labelRect = rect;
							labelRect.width = 80;
							rect.xMin += labelRect.width;

							GUI.Label(labelRect, "Username:");
							username = EditorGUI.TextField(rect, username).Trim();
							rect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
							labelRect.y += rect.height + EditorGUIUtility.standardVerticalSpacing;
							GUI.Label(labelRect, "Api Key:");
							apiKey = EditorGUI.TextField(rect, apiKey).Trim();
						}

						Preferences.instance.autoLogin.value = GUILayout.Toggle(Preferences.instance.autoLogin, new GUIContent("Automatically log in next time", "Will automatically try to log in to SyncSketch with the same credentials next time the tool is opened in Unity"));

						using (GUIUtils.HorizontalRightAligned)
						{
							if (GUILayout.Button("Cancel", GUILayout.Width(buttonWidth)))
							{
								promptForApiKey = false;
							}

							if (GUILayout.Button("Log In", GUILayout.Width(buttonWidth)))
							{
								waitingForResponse = true;
								onLogin();
							}
						}
					}
#else
					using (GUIUtils.Horizontal)
					{
						using (GUIUtils.Enabled(!waitingForResponse))
						{
							string label = waitingForResponse ? "Waiting for website..." : "Log In to SyncSketch";
							if (GUILayout.Button(label, GUILayout.Height(buttonHeight)))
							{
								waitingForResponse = true;
								onLogin();
							}
						}

						GUILayout.FlexibleSpace();

						if (waitingForResponse)
						{
							if (GUILayout.Button("Cancel", GUILayout.Height(buttonHeight)))
							{
								waitingForResponse = false;
								BrowserLogin.CancelLogin();
								onCancel?.Invoke();
							}
						}
					}

					Preferences.instance.autoLogin.value = GUILayout.Toggle(Preferences.instance.autoLogin, new GUIContent("Automatically log in next time", "Will automatically try to log in to SyncSketch with the same credentials next time the tool is opened in Unity"));
#endif
				}
			}
			else
			{
				// Logged in

				if (waitingForResponse)
				{
					waitingForResponse = false;
				}

				if (promptForApiKey)
				{
					promptForApiKey = false;
				}

				using (GUIUtils.Horizontal)
				{
					var logoRect = EditorGUILayout.GetControlRect(GUILayout.Width(34), GUILayout.Height(34));
					logoRect.y += 1;
					if (Event.current.type == EventType.Repaint)
					{
						GUIStyles.SyncSketchLogo.Draw(logoRect, false, false, false, false);
					}

					var labelRect = EditorGUILayout.GetControlRect(true, buttonHeight);
					labelRect.y += 3;
					GUI.Label(labelRect, GUIUtils.TempContent("Logged into SyncSketch as:\n" + syncSketch.username));

					GUILayout.FlexibleSpace();

					// shrink button if space is too narrow (especially in inspector)
					var label = GUIUtils.TempContent(EditorGUIUtility.currentViewWidth >= 315 ? "Log Out" : "Log\nOut");
					float width = EditorGUIUtility.currentViewWidth >= 315 ? 60 : 40;
					if (GUILayout.Button(label, GUILayout.Width(width), GUILayout.Height(buttonHeight)))
					{
						onLogout();
					}

					if (EditorGUIUtility.currentViewWidth >= 365)
					{
						if (GUILayout.Button("?", GUILayout.Width(buttonHeight), GUILayout.Height(buttonHeight)))
						{
							Application.OpenURL("https://support.syncsketch.com/article/67-syncsketch-unity-integration");
						}
					}
				}
			}
		}

		#endregion

		#region IDisposable blocks

		public static HorizontalBlock Horizontal { get { return new HorizontalBlock(HorizontalBlock.Alignment.None); } }
		public static HorizontalBlock HorizontalLeftAligned { get { return new HorizontalBlock(HorizontalBlock.Alignment.Left); } }
		public static HorizontalBlock HorizontalRightAligned { get { return new HorizontalBlock(HorizontalBlock.Alignment.Right); } }
		public static HorizontalBlock HorizontalCentered { get { return new HorizontalBlock(HorizontalBlock.Alignment.Center); } }

		public struct HorizontalBlock : IDisposable
		{
			[Flags]
			internal enum Alignment
			{
				None = 0 << 1,
				Left = 1 << 1,
				Right = 2 << 1,
				Center = Left | Right
			}

			readonly Alignment alignment;

			internal HorizontalBlock(Alignment alignment)
			{
				this.alignment = alignment;

				GUILayout.BeginHorizontal();

				if ((alignment & Alignment.Right) != 0)
				{
					GUILayout.FlexibleSpace();
				}
			}

			public void Dispose()
			{
				if ((alignment & Alignment.Left) != 0)
				{
					GUILayout.FlexibleSpace();
				}

				GUILayout.EndHorizontal();
			}
		}

		public static EnabledBlock Enabled(bool enabled) { return new EnabledBlock(enabled); }
		public static EnabledBlock Disabled { get { return new EnabledBlock(false); } }
		public struct EnabledBlock : IDisposable
		{
			readonly bool prevEnabled;
			internal EnabledBlock(bool enabled)
			{
				prevEnabled = GUI.enabled;
				GUI.enabled &= enabled;
			}

			public void Dispose()
			{
				GUI.enabled = prevEnabled;
			}
		}

		public static void DrawLine(Color color)
		{
			var rect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
			DrawLineRect(rect, color);
		}

		static void DrawLineRect(Rect rect, Color color)
		{
			EditorGUI.DrawRect(rect, color);
		}

		static Color colorLineDark = new Color(0.17f, 0.17f, 0.17f);
		static Color colorLineLight = new Color(0.5f, 0.5f, 0.5f);
		public static void Separator()
		{
			GUILayout.Space(8);
			var color = EditorGUIUtility.isProSkin ? colorLineDark : colorLineLight;
			DrawLine(color);
			GUILayout.Space(8);
		}

		static Color colorLineShadowDark = new Color(0.125f, 0.125f, 0.125f);
		static Color colorLineHighlightDark = new Color(0.3f, 0.3f, 0.3f);
		static Color colorLineShadowLight = new Color(0.4f, 0.4f, 0.4f);
		static Color colorLineHighlightLight = new Color(0.85f, 0.85f, 0.85f);
		public static Rect ResizeSeparator()
		{
			var rect = EditorGUILayout.BeginVertical();
			{
				GUILayout.Space(1);

				var colorShadow = EditorGUIUtility.isProSkin ? colorLineShadowDark : colorLineShadowLight;
				var colorHighlight = EditorGUIUtility.isProSkin ? colorLineHighlightDark : colorLineHighlightLight;

				GUILayout.Space(1);
				DrawLine(colorHighlight);
				DrawLine(colorShadow);
				GUILayout.Space(1);
				DrawLine(colorHighlight);
				DrawLine(colorShadow);
				GUILayout.Space(1);
			}
			EditorGUILayout.EndVertical();
			return rect;
		}

		#endregion

	}
#endif
}