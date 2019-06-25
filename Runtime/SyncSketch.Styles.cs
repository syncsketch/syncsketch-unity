using UnityEngine;
using UnityEditor;
using System;
using Object = UnityEngine.Object;
using System.Collections.Generic;
using System.IO;

namespace SyncSketch
{
#if UNITY_EDITOR
	/// <summary>
	/// Easily find the custom editor UI icons
	/// </summary>
	public static class GUIStyles
	{
		#region Button Styles

		static GUIStyle _ContextMenuButton;
		public static GUIStyle ContextMenuButton
		{
			get
			{
				if(_ContextMenuButton == null)
				{
					_ContextMenuButton = new GUIStyle();
					_ContextMenuButton.fixedWidth = 16;
					_ContextMenuButton.fixedHeight = 16;
					GUIUtils.FindBackgrounds(() => _ContextMenuButton.normal, "icon_contextmenu.png");
				}
				return _ContextMenuButton;
			}
		}

		static GUIStyle _SyncSketchLogo;
		public static GUIStyle SyncSketchLogo
		{
			get
			{
				if (_SyncSketchLogo == null)
				{
					_SyncSketchLogo = new GUIStyle();
					_SyncSketchLogo.fixedWidth = 34;
					_SyncSketchLogo.fixedHeight = 34;
					GUIUtils.FindBackgrounds(() => _SyncSketchLogo.normal, "icon_syncsketch_logo.png");
				}
				return _SyncSketchLogo;
			}
		}

		static GUIStyle _ButtonLeftAligned;
		public static GUIStyle ButtonLeftAligned
		{
			get
			{
				if (_ButtonLeftAligned == null)
				{
					_ButtonLeftAligned = new GUIStyle("Button");
					_ButtonLeftAligned.alignment = TextAnchor.MiddleLeft;
				}
				return _ButtonLeftAligned;
			}
		}

		static GUIStyle _BoldLabelClip;
		public static GUIStyle BoldLabelClip
		{
			get
			{
				if (_BoldLabelClip == null)
				{
					_BoldLabelClip = new GUIStyle(EditorStyles.boldLabel);
					_BoldLabelClip.clipping = TextClipping.Clip;
				}
				return _BoldLabelClip;
			}
		}

		static GUIStyle _ButtonRichText;
		public static GUIStyle ButtonRichText
		{
			get
			{
				if (_ButtonRichText == null)
				{
					_ButtonRichText = new GUIStyle("Button");
					_ButtonRichText.richText = true;
				}
				return _ButtonRichText;
			}
		}

		static GUIStyle _LabelRichText;
		public static GUIStyle LabelRichText
		{
			get
			{
				if (_LabelRichText == null)
				{
					_LabelRichText = new GUIStyle(EditorStyles.label);
					_LabelRichText.richText = true;
				}
				return _LabelRichText;
			}
		}

		#endregion
	}

	public static class GUIContents
	{
		class GUIContentIcon
		{
			GUIContent guiContent;

			public GUIContentIcon(string path)
			{
				guiContent = new GUIContent(GUIUtils.FindTexture(path, Screen.dpi > 96 ? GUIUtils.FindTextureOption.HighResolution : GUIUtils.FindTextureOption.None));
			}

			public static implicit operator GUIContent(GUIContentIcon icon)
			{
				return icon.guiContent;
			}
		}

		public static GUIContent BrowseFolderIcon = new GUIContentIcon("icon_folder_open.png");
		public static GUIContent ExternalIcon = new GUIContentIcon("icon_external.png");
		public static GUIContent UploadIcon = new GUIContentIcon("icon_upload.png");
		public static GUIContent SyncIcon = new GUIContentIcon("icon_sync.png");
		public static GUIContent PlayMediaIcon = new GUIContentIcon("icon_play_media.png");
		public static GUIContent ClipboardIcon = new GUIContentIcon("icon_clipboard.png");
		public static GUIContent UrlIcon = new GUIContentIcon("icon_url.png");
		public static GUIContent ScreenshotScene = new GUIContentIcon("icon_screenshot_scene.png");
		public static GUIContent ScreenshotSnip = new GUIContentIcon("icon_screenshot_snip.png");
		public static GUIContent ScreenshotGame = new GUIContentIcon("icon_screenshot_game.png");
	}
#endif
}
