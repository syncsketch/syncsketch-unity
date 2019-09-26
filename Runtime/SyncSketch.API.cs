using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using TinyJSON;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace SyncSketch
{
	/// <summary>
	/// Creates a persistent wrapper for the API object, so that we will
	/// stay logged in for the duration of the Unity session even if we
	/// close the window, change scenes, etc.
	/// </summary>
	public class PersistentSession : ScriptableObject
	{
		public API syncSketch;

		public static void Create(API syncSketch)
		{
			var existing = TryFind();
			if (existing != null)
			{
				Log.Error("PersistentSession already exists; can't create a new.");
				return;
			}

			if (syncSketch == null || !syncSketch.isValid)
			{
				Log.Error("Can't create a PersistentSession with invalid SyncSketch API object.");
				return;
			}

			var persistentObject = CreateInstance<PersistentSession>();
			persistentObject.hideFlags = HideFlags.HideAndDontSave;
			persistentObject.syncSketch = syncSketch;
			Preferences.instance.wasLoggedIn.value = true;
		}

		public static PersistentSession TryFind()
		{
			var persistents = Resources.FindObjectsOfTypeAll<PersistentSession>();
			if (persistents != null && persistents.Length > 0)
			{
				var p = persistents[0];
				return p;
			}

			return null;
		}

		/// <summary>
		/// Log out of the persistent session.
		/// </summary>
		public static void Logout()
		{
			var session = TryFind();
			if (session != null)
			{
				session.syncSketch.Logout();
				session.syncSketch = null;
				Object.DestroyImmediate(session);
				Preferences.instance.wasLoggedIn.value = false;
			}
		}

		/// <summary>
		/// Log out if the supplied SyncSketch object is the same as the persistent session.
		/// </summary>
		public static void LogoutIfMatch(API syncSketch)
		{
			var session = TryFind();
			if (session != null && session.syncSketch == syncSketch)
			{
				session.syncSketch.isValid = false;
				session.syncSketch = null;
				Object.DestroyImmediate(session);
			}
		}
	}

	/// <summary>
	/// Object that handles all direct communication to the SyncSketch API
	/// </summary>
	[Serializable]
	public class API
	{
		//--------------------------------------------------------------------------------------------------------------------------------
		// Constants/settings

		const string URL_SyncSketch = @"https://www.syncsketch.com";
		const int API_VERSION = 1;
		static string URL_API { get { return string.Format("{0}/api/v{1}", URL_SyncSketch, API_VERSION); } }

		/// <summary>
		/// Timeout until we cancel a request, in seconds.
		/// </summary>
		public static float RequestTimeout = 3;
		/// <summary>
		/// Timeout until we cancel an upload, in seconds.
		/// </summary>
		public static float UploadTimeout = 60;

		//--------------------------------------------------------------------------------------------------------------------------------
		// Static

		#region Login/Logout

		/// <summary>
		/// Try to login given a username and API key.
		/// </summary>
		/// <param name="username">Username associated with the SyncSketch Account</param>
		/// <param name="apiKey">API key associated with the SyncSketch Account</param>
		/// <param name="persistentLogin">If true, will make the logged in session persistent until Unity is closed, or until Logout() is called</param>
		/// <returns>A SyncSketch object to communicate with the corresponding account if successful, else null</returns>
		public static API Login(string username, string apiKey, bool persistentLogin)
		{
			if (persistentLogin)
			{
				var p = PersistentSession.TryFind();
				if (p != null)
				{
					Log.Error(string.Format("SyncSketch API is already logged in with username '{0}'\nMake sure to log out of this account before logging in a new when the persistent option is on.", p.syncSketch.username));
					return null;
				}
			}

			var syncSketch = new API(username, apiKey);

			if (syncSketch.SyncAccount())
			{
				if (persistentLogin)
				{
					PersistentSession.Create(syncSketch);
				}

				return syncSketch;
			}

			return null;
		}

		/// <summary>
		/// Synchronize the accounts with SyncSketch.com
		/// </summary>
		/// <returns>'true' if account was updated (note: it does not mean that there is necessarily new data)</returns>
		public bool SyncAccount()
		{
#if UNITY_EDITOR
			try
			{
				UnityEditor.EditorUtility.DisplayProgressBar("SyncSketch", "Syncing account...", 0f);
#endif
				string json = this.GetJSON("person/tree", "active=1");
				if (!string.IsNullOrEmpty(json) && IsValidJSON(json))
				{
					return this.GetAccountsFromJSON(json);
				}

				return false;
#if UNITY_EDITOR
			}
			finally
			{
				UnityEditor.EditorUtility.ClearProgressBar();
			}
#endif
		}

		/// <summary>
		/// Synchronize the accounts with SyncSketch.com
		/// </summary>
		public void SyncAccount_Async(Action onSuccess, Action onError, AsyncProgress progressCallback)
		{
			AsyncResult resultCallback = (isError, message) =>
			{
				if(!isError)
				{
					string json = message;
					if (!string.IsNullOrEmpty(json) && IsValidJSON(json))
					{
						this.GetAccountsFromJSON(json);
						onSuccess?.Invoke();
					}
					else
					{
						isError = true;
					}
				}

				if(isError)
				{
					onError?.Invoke();
				}
			};

			this.GetJSON_Async(resultCallback, progressCallback, "person/tree", "active=1");
		}

		/// <summary>
		/// Try to login asynchronously given a username and API key.
		/// </summary>
		/// <param name="username">Username associated with the SyncSketch Account</param>
		/// <param name="apiKey">API key associated with the SyncSketch Account</param>
		/// <param name="resultCallback">Callback when the process has finished, with the API object on success</param>
		/// <param name="progressCallback">Progress callback</param>
		/// <param name="persistentLogin">If true, will make the logged in session persistent until Unity is closed, or until Logout() is called</param>
		public static void Login_Async(string username, string apiKey, AsyncResult<API> resultCallback, AsyncProgress progressCallback, bool persistentLogin)
		{
			if (persistentLogin)
			{
				var p = PersistentSession.TryFind();
				if (p != null)
				{
					Debug.LogWarning(string.Format("SyncSketch API is already logged in with username '{0}'\nMake sure to log out of this account before logging in a new when the persistent option is on.", p.syncSketch.username));
					resultCallback(true, null);
					return;
				}
			}

			var syncSketch = new API(username, apiKey);

			void callback(bool isError, string message)
			{
				if (isError)
				{
					Log.Error(message);
					resultCallback(isError, null);
				}
				else
				{
					var json = message;
					if (syncSketch.GetAccountsFromJSON(json))
					{
						if (persistentLogin)
						{
							PersistentSession.Create(syncSketch);
						}
						resultCallback(false, syncSketch);
					}
					else
					{
						resultCallback(true, null);
					}
				}
			}
			syncSketch.GetJSON_Async(callback, progressCallback, "person/tree", "active=1");
		}

		/// <summary>
		/// Log out
		/// </summary>
		public void Logout()
		{
			this.isValid = false;
			PersistentSession.LogoutIfMatch(this);
		}

	#endregion

		#region Static API interface

		/// <summary>
		/// Upload a media file synchronously to a specific review id
		/// </summary>
		/// <param name="syncSketch">The SyncSketchAPI object representing the Account that will upload the file</param>
		/// <param name="reviewId">The id of the review</param>
		/// <param name="mediaData">The file as a byte array</param>
		/// <param name="fileName">The file name</param>
		/// <param name="mimeType">The mime type of the file, e.g. 'video/mp4'</param>
		/// <param name="noConvertFlag">Specifies to SyncSketch to not convert the video when it is received. This assumes that the file is already in a proper format supported by SyncSketch.</param>
		/// <param name="itemParentId"></param>
		public static void UploadMedia(API syncSketch, int reviewId, byte[] mediaData, string fileName = null, string mimeType = null, bool noConvertFlag = false, string itemParentId = null)
		{
			var getParams = "";
			if (noConvertFlag)
			{
				getParams += "&noConvertFlag=1";
			}
			if (!string.IsNullOrEmpty(itemParentId))
			{
				getParams += "&itemParentId=" + itemParentId;
			}

			string uploadURL = string.Format("{0}/items/uploadToReview/{1}/?username={2}&api_key={3}{4}", URL_SyncSketch, reviewId, syncSketch.username, syncSketch.apiKey, getParams);
			var data = new FilePostData("reviewFile", mediaData);
			PostData(uploadURL, data);
		}

		/// <summary>
		/// Upload a media file asynchronously.
		/// </summary>
		/// <param name="syncSketch">The SyncSketchAPI object representing the Account that will upload the file</param>
		/// <param name="resultCallback">Called when the process has finished</param>
		/// <param name="progressCallback">Called during the upload</param>
		/// <param name="reviewId">The id of the review</param>
		/// <param name="mediaData">The file as a byte array</param>
		/// <param name="fileName">The file name</param>
		/// <param name="mimeType">The mime type of the file, e.g. 'video/mp4'</param>
		/// <param name="noConvertFlag">Specifies to SyncSketch to not convert the video when it is received. This assumes that the file is already in a proper format supported by SyncSketch.</param>
		/// <param name="itemParentId"></param>
		public static void UploadMediaAsync(API syncSketch, AsyncResult resultCallback, AsyncProgress progressCallback, int reviewId, byte[] mediaData, string fileName = null, string mimeType = null, bool noConvertFlag = false, string itemParentId = null)
		{
			var getParams = "";
			if (noConvertFlag)
			{
				getParams += "&noConvertFlag=1";
			}
			if (!string.IsNullOrEmpty(itemParentId))
			{
				getParams += "&itemParentId=" + itemParentId;
			}

			string uploadURL = string.Format("{0}/items/uploadToReview/{1}/?username={2}&api_key={3}{4}", URL_SyncSketch, reviewId, syncSketch.username, syncSketch.apiKey, getParams);
			var data = new FilePostData("reviewFile", mediaData, fileName, mimeType);
			PostData_Async(resultCallback, progressCallback, uploadURL, data);
		}

		#endregion

		static bool IsValidJSON(string jsonString)
		{
			var trimmedString = jsonString.Trim();
			return !string.IsNullOrEmpty(trimmedString) && (trimmedString[0] == '{' || trimmedString[0] == '[');
		}

		//--------------------------------------------------------------------------------------------------------------------------------

		[SerializeField] public string username;
		[SerializeField] public string apiKey;
		[SerializeField] public Account[] accounts;
		[SerializeField] public bool isValid; // needed because Unity can't serialize 'null' values, so it will deserialize as an empty object with all fields reset

		public bool HasMultipleAccounts { get { return accounts != null && accounts.Length > 1; } }

		private API(string username, string apiKey)
		{
			this.username = username;
			this.apiKey = apiKey;
		}

		private bool GetAccountsFromJSON(string json)
		{
			var data = JSON.Load(json);
			if (data == null)
			{
				Log.Error("Couldn't load JSON from string:\n\"" + json + "\"");
				return false;
			}

			try
			{
				var list = new List<Account>();
				foreach (var accountJson in (ProxyArray)data)
				{
					var account = new Account(accountJson);
					list.Add(account);
				}

				if(list.Count > 0)
				{
					this.accounts = list.ToArray();
					this.isValid = true;
					return true;
				}
				else
				{
					return false;
				}

			}
			catch (Exception e)
			{
				Log.Error(string.Format("Couldn't load JSON from string:\n\"{0}\"\n\nError:\n{1}", json, e.ToString()));
			}

			return false;
		}

		#region Public Methods

		public SyncSketchItem FindItemById(int id)
		{
			foreach (var account in accounts)
			{
				if (account.id == id)
				{
					return account;
				}

				foreach (var project in account.projects)
				{
					if (project.id == id)
					{
						return project;
					}

					foreach (var review in project.reviews)
					{
						if (review.id == id)
						{
							return review;
						}
					}
				}
			}

			return null;
		}

		public Review FindReviewById(int id)
		{
			foreach (var account in accounts)
			{
				foreach (var project in account.projects)
				{
					foreach (var review in project.reviews)
					{
						if (review.id == id)
						{
							return review;
						}
					}
				}
			}

			return null;
		}

		public Project FindProjectWithReview(Review review)
		{
			foreach (var account in accounts)
			{
				foreach (var project in account.projects)
				{
					if (project.reviews.Contains(review))
					{
						return project;
					}
				}
			}

			return null;
		}

		#endregion

		#region Web Requests

		string GetJSON(string entity, params string[] getParams)
		{
			string urlParams = (getParams == null || getParams.Length == 0) ? "" : "&" + string.Join("&", getParams);
			string url = string.Format("{0}/{1}/?username={2}&api_key={3}{4}", URL_API, entity, username, apiKey, urlParams);
			Log.Message("GetJSON: " + url, Log.Level.Full);
			using (UnityWebRequest request = UnityWebRequest.Get(url))
			{
				request.SetRequestHeader("Content-Type", "application/json");
				request.SendWebRequest();

				float timeout = Time.realtimeSinceStartup + RequestTimeout;
				while (!request.isDone && Time.realtimeSinceStartup < timeout)
				{
				}

				if (Time.realtimeSinceStartup >= timeout)
				{
					Log.Error("Request Timeout");
					return null;
				}

				if (request.isNetworkError || request.isHttpError)
				{
					Log.Error("Request Error: " + request.error);
					return null;
				}
				else
				{
					Log.Message("Response: " + request.downloadHandler.text, Log.Level.Full);
					return request.downloadHandler.text;
				}
			}
		}

		string PostJSON(string entity, Dictionary<string, string> postData, params string[] getParams)
		{
			string urlParams = (getParams == null || getParams.Length == 0) ? "" : "&" + string.Join("&", getParams);
			string url = string.Format("{0}/{1}/?username={2}&api_key={3}{4}", URL_API, entity, username, apiKey, urlParams);
			string jsonPost = JSON.Dump(postData);
			byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonPost);
			Log.Message(string.Format("PostJSON: {0}\n{1}", url, jsonPost), Log.Level.Full);
			using (UnityWebRequest request = new UnityWebRequest(url, "POST"))
			{
				request.SetRequestHeader("Content-Type", "application/json");
				request.uploadHandler = new UploadHandlerRaw(jsonBytes);
				request.downloadHandler = new DownloadHandlerBuffer();
				request.SendWebRequest();

				float timeout = Time.realtimeSinceStartup + RequestTimeout;
				while (!request.isDone && Time.realtimeSinceStartup < timeout)
				{
				}

				if (Time.realtimeSinceStartup >= timeout)
				{
					Log.Error("Request Timeout");
					return null;
				}

				if (request.isNetworkError || request.isHttpError)
				{
					Log.Error("Request Error: " + request.error);
					return null;
				}
				else
				{
					Log.Message("Response: " + request.downloadHandler.text, Log.Level.Full);
					return request.downloadHandler.text;
				}
			}
		}

		struct FilePostData
		{
			public string fieldName;
			public byte[] data;
			public string fileName;
			public string mimeType;

			public FilePostData(string fieldName, byte[] data)
			{
				this.fieldName = fieldName;
				this.data = data;
				this.fileName = null;
				this.mimeType = null;
			}

			public FilePostData(string fieldName, byte[] data, string fileName, string mimeType)
			{
				this.fieldName = fieldName;
				this.data = data;
				this.fileName = fileName;
				this.mimeType = mimeType;
			}
		}

		static string PostData(string url, params FilePostData[] data)
		{
			var wwwForm = new WWWForm();
			foreach (var d in data)
			{
				if(!string.IsNullOrEmpty(d.fileName))
				{
					wwwForm.AddBinaryData(d.fieldName, d.data, d.fileName, d.mimeType);
				}
				else
				{
					wwwForm.AddBinaryData(d.fieldName, d.data);
				}
			}

			Log.Message(string.Format("PostData: {0}", url), Log.Level.Full);
			using (UnityWebRequest request = UnityWebRequest.Post(url, wwwForm))
			{
				request.SendWebRequest();

				float timeout = Time.realtimeSinceStartup + RequestTimeout;
				while (!request.isDone && Time.realtimeSinceStartup < timeout)
				{
				}

				if (Time.realtimeSinceStartup >= timeout)
				{
					Log.Error("Request Timeout");
					return null;
				}

				if (request.isNetworkError || request.isHttpError)
				{
					Log.Error("Request Error: " + request.error);
					return null;
				}
				else
				{
					Log.Message("Response: " + request.downloadHandler.text, Log.Level.Full);
					return request.downloadHandler.text;
				}
			}
		}

		#endregion

		#region Web Requests Async

		void GetJSON_Async(AsyncResult resultCallback, AsyncProgress progressCallback, string entity, params string[] getParams)
		{
			string urlParams = (getParams == null || getParams.Length == 0) ? "" : "&" + string.Join("&", getParams);
			string url = string.Format("{0}/{1}/?username={2}&api_key={3}{4}", URL_API, entity, username, apiKey, urlParams);

			Log.Message("GetJSON_Async: " + url, Log.Level.Full);
			UnityWebRequest request = UnityWebRequest.Get(url);
			request.SetRequestHeader("Content-Type", "application/json");
			request.SendWebRequest();

			asyncCalls.Add(new AsyncCall(resultCallback, progressCallback, request, Time.realtimeSinceStartup + API.RequestTimeout));
			RegisterUpdate();
		}

		void PostJSON_Async(AsyncResult resultCallback, AsyncProgress progressCallback, string entity, Dictionary<string, string> postData, params string[] getParams)
		{
			string urlParams = (getParams == null || getParams.Length == 0) ? "" : "&" + string.Join("&", getParams);
			string url = string.Format("{0}/{1}/?username={2}&api_key={3}{4}", URL_API, entity, username, apiKey, urlParams);
			string jsonPost = JSON.Dump(postData);
			byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonPost);
			Log.Message(string.Format("PostJSON_Async: {0}\n{1}", url, jsonPost), Log.Level.Full);
			UnityWebRequest request = new UnityWebRequest(url, "POST");
			request.SetRequestHeader("Content-Type", "application/json");
			request.uploadHandler = new UploadHandlerRaw(jsonBytes);
			request.downloadHandler = new DownloadHandlerBuffer();
			request.SendWebRequest();

			asyncCalls.Add(new AsyncCall(resultCallback, progressCallback, request, Time.realtimeSinceStartup + API.RequestTimeout));
			RegisterUpdate();
		}

		static void PostData_Async(AsyncResult resultCallback, AsyncProgress progressCallback, string url, params FilePostData[] data)
		{
			var wwwForm = new WWWForm();
			foreach (var d in data)
			{
				if (!string.IsNullOrEmpty(d.fileName))
				{
					wwwForm.AddBinaryData(d.fieldName, d.data, d.fileName, d.mimeType);
				}
				else
				{
					wwwForm.AddBinaryData(d.fieldName, d.data);
				}
			}

			Log.Message(string.Format("PostData_Async: {0}", url), Log.Level.Full);
			UnityWebRequest request = UnityWebRequest.Post(url, wwwForm);
			request.SendWebRequest();

			asyncCalls.Add(new AsyncCall(resultCallback, progressCallback, request, Time.realtimeSinceStartup + API.UploadTimeout));
			RegisterUpdate();
		}

		#endregion

		#region Async System

		public delegate void AsyncResult(bool isError, string message);
		public delegate void AsyncResult<T>(bool isError, T obj);
		public delegate void AsyncProgress(float progress);

		struct AsyncCall
		{
			public readonly AsyncResult resultCallback;
			public readonly AsyncProgress progressCallback;
			public readonly float timeout;
			public readonly UnityWebRequest request;
			readonly bool isUpload;

			public AsyncCall(AsyncResult resultCallback, AsyncProgress progressCallback, UnityWebRequest request, float timeout)
			{
				if (resultCallback == null || request == null)
				{
					throw new ArgumentNullException();
				}

				this.resultCallback = resultCallback;
				this.progressCallback = progressCallback;
				this.request = request;
				this.timeout = timeout;
				this.isUpload = request.uploadHandler != null && request.uploadHandler.data != null && request.uploadHandler.data.Length > 0;
			}

			public float progress
			{
				get
				{
					return isUpload ? request.uploadProgress : request.downloadProgress;
				}
			}
		}

		// EditorApplication.Update hook, to handle async requests

		static bool updateRegistered;
		static List<AsyncCall> asyncCalls = new List<AsyncCall>();
		static void OnEditorUpdate()
		{
			for (int i = asyncCalls.Count-1; i >= 0; i--)
			{
				var asyncCall = asyncCalls[i];

				// request successful
				if (asyncCall.request.isDone)
				{
					Log.Message("Response: " + asyncCall.request.downloadHandler.text, Log.Level.Full);

					// verify that the response is a JSON string, else it's an error
					var message = asyncCall.request.downloadHandler.text;
					if (!IsValidJSON(message))
					{
						asyncCall.resultCallback(true, message);
					}
					else
					{
						asyncCall.resultCallback(false, asyncCall.request.downloadHandler.text);
					}
					asyncCalls.RemoveAt(i);
				}
				// request error
				else if (asyncCall.request.isNetworkError || asyncCall.request.isHttpError)
				{
					asyncCall.resultCallback(true, "Request Error: " + asyncCall.request.error);
					asyncCalls.RemoveAt(i);
				}
				// time out
				else if (Time.realtimeSinceStartup > asyncCall.timeout)
				{
					asyncCall.resultCallback(true, "Request Timeout");
					asyncCalls.RemoveAt(i);
				}
				// request in progress
				else if (asyncCall.progressCallback != null)
				{
					asyncCall.progressCallback(asyncCall.progress);
				}
			}

			if (asyncCalls.Count == 0)
			{
				UnregisterUpdate();
			}
		}

		static void RegisterUpdate()
		{
			if (!updateRegistered)
			{
#if UNITY_EDITOR
				UnityEditor.EditorApplication.update += OnEditorUpdate;
#endif
				updateRegistered = true;
			}
		}

		static void UnregisterUpdate()
		{
			if (updateRegistered)
			{
#if UNITY_EDITOR
				UnityEditor.EditorApplication.update -= OnEditorUpdate;
#endif
				updateRegistered = false;
			}
		}

		#endregion

		//--------------------------------------------------------------------------------------------------------------------------------
		// Classes

		[Serializable]
		public abstract class SyncSketchItem
		{
			[SerializeField] public string name;
			[SerializeField] public string description;
			[SerializeField] public int id;
			// This field ensures that the object has been properly created.
			// Unity will serialize 'null' values into default(SyncSketchItem),
			// so we end up with empty items after deserialization.
			[SerializeField] public bool isValid;

			protected SyncSketchItem(Variant data)
			{
				name = data["name"] ?? "";
				description = data["description"] ?? "";
				id = data["id"];
				isValid = true;
			}

			/// <summary>
			/// Returns the URI of the SyncSketch resource, e.g. "/api/v1/account/01234"
			/// </summary>
			/// <returns></returns>
			public string GetURI()
			{
				return string.Format("/api/v{0}/{1}/{2}/", API_VERSION, TypeString(), this.id);
			}

			virtual protected string TypeString()
			{
				return null;
			}
		}

		/// <summary>
		/// Represents a SyncSketch Account.
		/// </summary>
		[Serializable]
		public class Account : SyncSketchItem
		{
			[SerializeField] List<Project> projectsList;
			ReadOnlyCollection<Project> readOnlyProjects;
			public ReadOnlyCollection<Project> projects
			{
				get
				{
					if (readOnlyProjects == null || readOnlyProjects.Count != projectsList.Count)
					{
						readOnlyProjects = new ReadOnlyCollection<Project>(projectsList);
					}
					return readOnlyProjects;
				}
			}

			internal Account(Variant data) : base(data)
			{
				this.projectsList = new List<Project>();

				var projectsJson = (ProxyArray)data["projects"];
				foreach (var projectJson in projectsJson)
				{
					var project = new Project(projectJson);
					projectsList.Add(project);
				}
			}

			protected override string TypeString()
			{
				return "account";
			}

			public void FetchProjects(API syncSketch)
			{
				string json = syncSketch.GetJSON("project", "active=1");

				if (string.IsNullOrEmpty(json))
				{
					Log.Error("Couldn't fetch projects");
					return;
				}

				ParseProjects(syncSketch, json);
			}

			public void FetchProjects_Async(API syncSketch, AsyncResult resultCallback, AsyncProgress progressCallback)
			{
				void callback(bool isError, string message)
				{
					if (isError)
					{
						Log.Error("Couldn't fetch projects");
					}
					else
					{
						ParseProjects(syncSketch, message);
					}

					resultCallback?.Invoke(isError, message);
				}
				syncSketch.GetJSON_Async(callback, progressCallback, "project", "active=1");
			}

			void ParseProjects(API syncSketch, string json)
			{
				// iterate projects
				var data = JSON.Load(json);
				var projectsJson = (ProxyArray)data["objects"];
				List<Project> list = new List<Project>();
				foreach (var project in projectsJson)
				{
					var p = new Project(project);
					list.Add(p);
				}

				list.Reverse(); // projects are fetched from most recent to oldest, so reverse them for the UI
				projectsList.Clear();
				projectsList.AddRange(list);
			}

			public Project AddProject(API syncSketch, string name, string description)
			{
				var postData = new Dictionary<string, string>
				{
					{ "account", this.GetURI() },
					{ "name", name },
					{ "description", description }
				};

				var json = syncSketch.PostJSON("project", postData);

				if (json == null)
				{
					Log.Error("Couldn't add project.");
					return null;
				}

				var data = JSON.Load(json);
				var project = new Project(data);
				this.projectsList.Add(project);

				return project;
			}
		}

		/// <summary>
		/// Represents a Project from an Account.
		/// </summary>
		[Serializable]
		public class Project : SyncSketchItem
		{
			public string ProjectURL { get { return string.Format("https://www.syncsketch.com/pro/#project/{0}", id); } }

			[SerializeField] List<Review> reviewsList;
			ReadOnlyCollection<Review> readOnlyReviews;
			public ReadOnlyCollection<Review> reviews
			{
				get
				{
					if (readOnlyReviews == null || readOnlyReviews.Count != reviewsList.Count)
					{
						readOnlyReviews = new ReadOnlyCollection<Review>(reviewsList);
					}
					return readOnlyReviews;
				}
			}

			internal Project(Variant data) : base(data)
			{
				this.reviewsList = new List<Review>();
				try
				{
					var reviewsJson = (ProxyArray)data["reviews"];
					foreach (var reviewJson in reviewsJson)
					{
						var review = new Review(reviewJson);
						reviewsList.Add(review);
					}
				}
				catch { }
			}

			protected override string TypeString()
			{
				return "project";
			}

			public void FetchReviews(API syncSketch)
			{
				var json = syncSketch.GetJSON("review", "project__id=" + this.id);
				if (string.IsNullOrEmpty(json))
				{
					Log.Error("Couldn't fetch reviews from project");
					return;
				}

				ParseReviews(syncSketch, json);
			}

			public void FetchReviewsAsync(API syncSketch, AsyncResult resultCallback, AsyncProgress progressCallback)
			{
				void callback(bool isError, string message)
				{
					if (isError)
					{
						Log.Error("Couldn't fetch reviews");
					}
					else
					{
						ParseReviews(syncSketch, message);
					}

					resultCallback?.Invoke(isError, message);
				}

				syncSketch.GetJSON_Async(callback, progressCallback, "review", "project__id=" + this.id);
			}

			void ParseReviews(API syncSketch, string json)
			{
				var data = JSON.Load(json);
				var reviewsJson = (ProxyArray)data["objects"];
				List<Review> list = new List<Review>();
				foreach (var review in reviewsJson)
				{
					var r = new Review(review);
					if (r != null)
					{
						list.Add(r);
					}
				}

				reviewsList.Clear();
				reviewsList.AddRange(list);
			}

			public Review AddReview(API syncSketch, string name, string description)
			{
				var postData = new Dictionary<string, string>
				{
					{ "project", this.GetURI() },
					{ "name", name },
					{ "description", description }
				};

				var json = syncSketch.PostJSON("review", postData);

				if (json == null)
				{
					Log.Error("Couldn't add project.");
					return null;
				}

				var data = JSON.Load(json);
				var review = new Review(data);
				this.reviewsList.Add(review);

				return review;
			}
		}

		/// <summary>
		/// Represents a Review in a Project
		/// </summary>
		[Serializable]
		public class Review : SyncSketchItem
		{
			[SerializeField] public string uuid;

			public string ReviewURL { get { return string.Format("https://www.syncsketch.com/sketch/{0}/?offlineMode=1", uuid); } }

			internal Review(Variant data) : base(data)
			{
				uuid = data["uuid"];
			}

			public static Review FromJSON(Variant jsonData)
			{
				return new Review(jsonData);
			}

			protected override string TypeString()
			{
				return "review";
			}

			/// <summary>
			/// Upload a media file synchronously.
			/// </summary>
			/// <param name="syncSketch">The SyncSketchAPI object representing the Account that will upload the file</param>
			/// <param name="mediaData">The file as a byte array</param>
			/// <param name="fileName">The file name</param>
			/// <param name="mimeType">The mime type of the file, e.g. 'video/mp4'</param>
			/// <param name="noConvertFlag">Specifies to SyncSketch to not convert the video when it is received. This assumes that the file is already in a proper format supported by SyncSketch.</param>
			/// <param name="itemParentId"></param>
			public void UploadMedia(API syncSketch, byte[] mediaData, string fileName = null, string mimeType = null, bool noConvertFlag = false, string itemParentId = null)
			{
				API.UploadMedia(syncSketch, id, mediaData, fileName, mimeType, noConvertFlag, itemParentId);
			}

			/// <summary>
			/// Upload a media file asynchronously.
			/// </summary>
			/// <param name="syncSketch">The SyncSketchAPI object representing the Account that will upload the file</param>
			/// <param name="resultCallback">Called when the process has finished</param>
			/// <param name="progressCallback">Called during the upload</param>
			/// <param name="mediaData">The file as a byte array</param>
			/// <param name="fileName">The file name</param>
			/// <param name="mimeType">The mime type of the file, e.g. 'video/mp4'</param>
			/// <param name="noConvertFlag">Specifies to SyncSketch to not convert the video when it is received. This assumes that the file is already in a proper format supported by SyncSketch.</param>
			/// <param name="itemParentId"></param>
			public void UploadMediaAsync(API syncSketch, AsyncResult resultCallback, AsyncProgress progressCallback, byte[] mediaData, string fileName = null, string mimeType = null, bool noConvertFlag = false, string itemParentId = null)
			{
				API.UploadMediaAsync(syncSketch, resultCallback, progressCallback, id, mediaData, fileName, mimeType, noConvertFlag, itemParentId);
			}
		}
	}
}