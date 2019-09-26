// This file is Editor only, but needs to be accessible from the runtime Assembly
#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;
using System;
using Object = UnityEngine.Object;
using System.Collections.Generic;

// TODO: differentiate data that has to be saved:
// - per project and shared across users (version control)
// - per project on local machine
// - globally on local machine
// Currently properties in this class will be saved globally using EditorPrefs

namespace SyncSketch
{
	/// <summary>
	/// Container for all the saved preferences, will be serialized as JSON and saved using EditorPrefs
	/// </summary>
	[Serializable]
	public class Preferences
	{
		static Preferences _instance;
		public static Preferences instance
		{
			get
			{
				if (_instance == null)
				{
					_instance = new Preferences();
					_instance.load();
				}
				return _instance;
			}
		}

		/// <summary>
		/// Save the Preferences
		/// </summary>
		public static void Save()
		{
			instance.save();
		}

		/// <summary>
		/// Delete all saved preferences
		/// </summary>
		public static void Clear()
		{
			instance.clear();
			_instance = null;
			Log.Message("Preferences cleared");
		}

		//--------------------------------------------------------------------------------------------------------------------------------

		// Global preferences

		// Login
		/// <summary>
		/// Should the Editor automatically try to login with the saved credentials when opened?
		/// </summary>
		public BoolPref autoLogin = new BoolPref(false);
		public BoolPref wasLoggedIn = new BoolPref(false);
		public StringPref savedUsername = new StringPref(null);
		public StringPref savedApikey = new StringPref(null);

		// Screenshot options
		public FilePath screenshotOutputFile = new FilePath("Screenshot", "png");
		public FilePath videoOutputFile = new FilePath("Video", "mp4");
		public BoolPref captureSceneViewGizmos = new BoolPref(false);
		public List<ReviewUploadInfo> lastScreenshotUploads = new List<ReviewUploadInfo>();

		// Record options
		public BoolPref uploadToReviewAfterScreenshot = new BoolPref(false);
		public IntPref lastSelectedItemId = new IntPref(0);
		public BoolPref stopPlayerOnStopRecording = new BoolPref(false);

		// UI options
		public IntPref treeViewRowsCount = new IntPref(10);

		//--------------------------------------------------------------------------------------------------------------------------------

		void load()
		{
			string json = EditorPrefs.GetString("SyncSketch_Prefs_JSON", null);
			if (json != null)
			{
				EditorJsonUtility.FromJsonOverwrite(json, this);
			}
		}

		void save()
		{
			var json = EditorJsonUtility.ToJson(this);
			EditorPrefs.SetString("SyncSketch_Prefs_JSON", json);
		}

		void clear()
		{
			EditorPrefs.DeleteKey("SyncSketch_Prefs_JSON");
		}

		//--------------------------------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Utility class that will automatically save any field modified in the Preferences
		/// </summary>
		/// <typeparam name="T"></typeparam>
		[Serializable]
		public class FieldPref<T>
		{
			[SerializeField] T _value;
			public T value
			{
				get
				{
					return _value;
				}

				set
				{
					if (_value == null || !_value.Equals(value))
					{
						_value = value;
						Preferences.Save();
					}
				}
			}

			public FieldPref(T value)
			{
				this._value = value;
			}

			public override string ToString()
			{
				return _value.ToString();
			}
		}

		//--------------------------------------------------------------------------------------------------------------------------------

		[Serializable] public class BoolPref : FieldPref<bool>
		{
			public BoolPref(bool value) : base(value) {}
			public static implicit operator bool(BoolPref boolPref)	{ return boolPref.value; }
		}

		[Serializable] public class StringPref : FieldPref<string>
		{
			public StringPref(string value) : base(value) { }
			public static implicit operator string(StringPref stringPref) { return stringPref.value; }
		}

		[Serializable] public class IntPref : FieldPref<int>
		{
			public IntPref(int value) : base(value) { }
			public static implicit operator int(IntPref intPref) { return intPref.value; }
		}
	}
}

#endif