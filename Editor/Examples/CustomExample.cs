using UnityEngine;
using UnityEditor;
using System.Linq;
using System.IO;

// Example to upload a movie file to a specific project/review.
// Does error checking and nice dialogs in case of an error.

namespace SyncSketch
{
	public static class CustomExample
	{
		const string username = "user@email.com";
		const string apiKey = "000000000000000000000000000000";

		static string uploadedMovie;

		public static void UploadMovieForReview(string moviePath, string reviewName, string projectName, string accountName = null)
		{
			// open the file as byte[]
			if (!File.Exists(moviePath))
			{
				Debug.LogError(string.Format("File does not exist: '{0}'", moviePath));
				return;
			}
			byte[] movieBytes = File.ReadAllBytes(moviePath);
			string filename = Path.GetFileName(moviePath);
			uploadedMovie = filename;
			string mimeType = string.Format("video/{0}", Path.GetExtension(moviePath));

			try
			{
				EditorUtility.DisplayProgressBar("SyncSketch Custom", "Logging in SyncSketch...", 0f);

				// log in SyncSketch (non persistent)
				var syncSketch = SyncSketch.API.Login(username, apiKey, false);

				// find the account
				var account = (accountName == null) ? syncSketch.accounts[0] : syncSketch.accounts.FirstOrDefault(a => a.name == accountName);

				EditorUtility.DisplayProgressBar("SyncSketch Custom", "Fetching projects...", 0f);

				// find the correct project
				var project = account.projects.FirstOrDefault(p => p.name == projectName);
				if (project == null)
				{
					Debug.LogError(string.Format("Couldn't find project named: '{0}'", projectName));
					EditorUtility.ClearProgressBar();
					return;
				}

				EditorUtility.DisplayProgressBar("SyncSketch Custom", "Fetching reviews...", 0f);

				// find the correct review
				var review = project.reviews.FirstOrDefault(r => r.name == reviewName);
				if (review == null)
				{
					Debug.LogError(string.Format("Couldn't find review named: '{0}'", reviewName));
					EditorUtility.ClearProgressBar();
					return;
				}

				// upload the file asynchronously
				review.UploadMediaAsync(syncSketch, OnUploadFinished, OnUploadProgress, movieBytes, filename, mimeType, true);
			}
			catch { }
			finally
			{
				EditorUtility.ClearProgressBar();
			}
		}

		static void OnUploadFinished(bool isError, string message)
		{
			EditorUtility.ClearProgressBar();

			if (isError)
			{
				// error
				EditorUtility.DisplayDialog("SyncSketch Error", "An error occurred during upload:\n\n" + message, "OK");
			}
			else
			{
				// success: fetch review URL
				var json = TinyJSON.JSON.Load(message);
				string reviewURL = json["reviewURL"];
				EditorUtility.DisplayDialog("SyncSketch Custom", "File uploaded successfully!\n\n" + reviewURL, "OK");

				// open review URL
				Application.OpenURL(reviewURL);

				// copy review URL to clipboard
				EditorGUIUtility.systemCopyBuffer = reviewURL;
			}
		}

		static void OnUploadProgress(float progress)
		{
			EditorUtility.DisplayProgressBar("SyncSketch Custom", string.Format("Uploading {0} for review...", uploadedMovie), progress);
		}
	}
}