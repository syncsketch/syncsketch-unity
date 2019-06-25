using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using SyncSketch;

namespace SyncSketch
{
	namespace Timeline
	{
		[TrackColor(1f, 0.7411765f, 0.1882353f)]
		[TrackClipType(typeof(SyncSketchClip))]
		[TrackBindingType(typeof(SyncSketchRecorder))]
		public class SyncSketchTrack : TrackAsset
		{
			protected override void OnCreateClip(TimelineClip clip)
			{
				base.OnCreateClip(clip);

				// auto-fill the suffix part to be an index based on the existing clips on the track
				var syncSketchClip = (SyncSketchClip)clip.asset;
				int index = 1;
				bool nameIsUnique = true;
				do
				{
					nameIsUnique = true;
					string suffix = string.Format("_{0}", index);
					for (int i = 0; i < m_Clips.Count; i++)
					{
						if (((SyncSketchClip)m_Clips[i].asset).template.filenameSuffix == suffix)
						{
							nameIsUnique = false;
							index++;
							break;
						}
					}
				}
				while (!nameIsUnique);
				syncSketchClip.template.filenameSuffix = string.Format("_{0}", index);
			}
		}
	}
}