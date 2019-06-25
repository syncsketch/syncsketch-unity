using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using SyncSketch;

namespace SyncSketch
{
	namespace Timeline
	{
		[Serializable]
		public class SyncSketchClip : PlayableAsset, ITimelineClipAsset
		{
			public SyncSketchBehaviour template = new SyncSketchBehaviour();

			public ClipCaps clipCaps
			{
				get { return ClipCaps.None; }
			}

			public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
			{
				var playable = ScriptPlayable<SyncSketchBehaviour>.Create(graph, template);
				return playable;
			}
		}
	}
}
