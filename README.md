# SyncSketch Unity Plugin

[SyncSketch](https://www.syncsketch.com/) plugin for Unity.  
Take screenshots and record videos, and send them to your SyncSketch project all within Unity!

Requires Unity **2018.3** or **2018.4**.  
_For Unity 2019.1+, use the branch [`dev_2019.1`](https://github.com/syncsketch/syncsketch-unity/tree/dev_2019.1)._

## Installation

### Using Unity Package Manager

Edit the `manifest.json` file located in the `Packages` directory at the root of your Unity project, and add this line in the _dependencies_:  
`"com.syncsketch.unity-plugin": "https://github.com/syncsketch/syncsketch-unity.git#[version]"`  
where `[version]` corresponds to the wanted release.

We follow this naming convention for releases: `[Unity Major Version]_[Plugin Version]`.

For example if you use Unity 2018.3 and want to fetch version 0.0.1 of the SyncSketch Plugin, you would use this link in your `manifest.json` file:
`"com.syncsketch.unity-plugin": "https://github.com/syncsketch/syncsketch-unity.git#2018_0.0.1"`

Unfortunately at this point, updating the plugin has to be done manually by editing the `manifest.json` to update the version number.

You can find more information about GitHub integration in the Unity Package Manager on [Unity's Documentation](https://docs.unity3d.com/Manual/upm-git.html).

### Using the .unitypackage file

A `.unitypackage` file is added and can be downloaded for each [release](https://github.com/syncsketch/syncsketch-unity/releases) on GitHub.  
Import a file in Unity through the menu:
> Assets > Import Package > Custom Package...

### Manual Installation

Clone this repository (or download a ZIP and extract it) in a dedicated folder (e.g. "SyncSketch") in your Unity Project.

Make sure to use the correct [branch](https://github.com/syncsketch/syncsketch-unity/branches) based on the Unity version you are using.

## Getting Started

Please read the [guide at SyncSketch.com](https://support.syncsketch.com/article/67-syncsketch-unity-integration) to get started!
