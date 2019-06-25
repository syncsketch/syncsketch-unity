using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace SyncSketch
{
	namespace Timeline
	{
		[CustomPropertyDrawer(typeof(SyncSketchBehaviour))]
		public class SyncSketchBehaviourDrawer : PropertyDrawer
		{
			const float spacer = 8f;

			SerializedProperty filenameSuffix;
			SerializedProperty resolutionMultiplier;
			SerializedProperty overrideOutputFile;
			SerializedProperty outputFile;
			SerializedProperty overrideResolution;
			SerializedProperty width;
			SerializedProperty height;
			SerializedProperty useGameResolution;
			SerializedProperty keepAspectRatio;

			void GetProperties(SerializedProperty property)
			{
				if (filenameSuffix != null)
				{
					return;
				}

				filenameSuffix = property.FindPropertyRelative(nameof(SyncSketchBehaviour.filenameSuffix));
				resolutionMultiplier = property.FindPropertyRelative(nameof(SyncSketchBehaviour.resolutionMultiplier));
				overrideOutputFile = property.FindPropertyRelative(nameof(SyncSketchBehaviour.overrideOutputFile));
				outputFile = property.FindPropertyRelative(nameof(SyncSketchBehaviour.outputFile));
				overrideResolution = property.FindPropertyRelative(nameof(SyncSketchBehaviour.overrideResolution));
				width = property.FindPropertyRelative(nameof(SyncSketchBehaviour.width));
				height = property.FindPropertyRelative(nameof(SyncSketchBehaviour.height));
				useGameResolution = property.FindPropertyRelative(nameof(SyncSketchBehaviour.useGameResolution));
				keepAspectRatio = property.FindPropertyRelative(nameof(SyncSketchBehaviour.keepAspectRatio));
			}

			public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
			{
				GetProperties(property);

				var rect = position;
				rect.height = EditorGUIUtility.singleLineHeight;
				var spacing = EditorGUIUtility.standardVerticalSpacing;

				EditorGUI.PropertyField(rect, filenameSuffix);
				rect.y += EditorGUI.GetPropertyHeight(filenameSuffix) + spacing;

				EditorGUI.PropertyField(rect, resolutionMultiplier);
				rect.y += EditorGUI.GetPropertyHeight(resolutionMultiplier) + spacing;

				// manual spacing, using [Space] attributes doesn't work as expected
				rect.y += spacer;

				EditorGUI.PropertyField(rect, overrideOutputFile);
				rect.y += EditorGUI.GetPropertyHeight(overrideOutputFile) + spacing;

				var indent = EditorGUI.indentLevel;

				using (GUIUtils.Enabled(overrideOutputFile.boolValue))
				{
					EditorGUI.indentLevel++;

					EditorGUI.PropertyField(rect, outputFile);
					rect.y += EditorGUI.GetPropertyHeight(outputFile, outputFile.isExpanded) + spacing;

					EditorGUI.indentLevel = indent;
				}

				// manual spacing, using [Space] attributes doesn't work as expected
				rect.y += spacer;

				EditorGUI.PropertyField(rect, overrideResolution);
				rect.y += EditorGUI.GetPropertyHeight(overrideResolution) + spacing;

				using (GUIUtils.Enabled(overrideResolution.boolValue))
				{
					EditorGUI.indentLevel++;

					EditorGUI.PropertyField(rect, useGameResolution);
					rect.y += EditorGUI.GetPropertyHeight(useGameResolution) + spacing;

					using (GUIUtils.Enabled(!useGameResolution.boolValue))
					{
						EditorGUI.PropertyField(rect, width);
						rect.y += EditorGUI.GetPropertyHeight(width) + spacing;

						using (GUIUtils.Enabled(!keepAspectRatio.boolValue))
						{
							EditorGUI.PropertyField(rect, height);
							rect.y += EditorGUI.GetPropertyHeight(height) + spacing;
						}

						EditorGUI.PropertyField(rect, keepAspectRatio);
						rect.y += EditorGUI.GetPropertyHeight(keepAspectRatio) + spacing;
					}

					EditorGUI.indentLevel = indent;
				}
			}

			public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
			{
				return EditorGUI.GetPropertyHeight(property, label, true) + 2 * spacer;
			}
		}
	}
}