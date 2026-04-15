# What is this?
This repository contains the Unity renderer for Resonite. Resonite is a free social VR sandbox platform, which allows for socialization and collaborative in-game building.

You can get Resonite free on Steam: https://store.steampowered.com/app/2519830/Resonite/

Resonite uses a unique multi-process architecuture, where majority of Resonite is a fully custom engine running with modern .NET 10 runtime. The engine communicates with the Unity Renderer process via IPC (inter process communication) and shared memory.

This architecture allows for much higher performance and properly isolates the renderer from the rest of the engine, allowing it to be replaced in the future.

# What's the purpose of this repo?
Making this repository open source has several goals:

1) Serve as a reference for custom renderers
    - Resonite is built around community creation and welcomes tinkering and experimenting
    - Our community has already been making a number of custom renderers. Making our current official one open should help with these endeavors
2) Make it easier for community to help us with a new official renderer
    - Our goal is to completely replace Unity as a renderer in the future with one that we have much better control over and fix number of issues
    - Making the current one open allows community to help us document its behavior and help with a new open source renderer
    - Read more about our goals for new renderer here: https://github.com/Yellow-Dog-Man/Resonite-Issues/discussions/5830
3) Allow for community bugfixes & feature additions* for the renderer
    - As we're still small team, so we don't have time to investigate all bugs and issues
    - However if community members are willing to put time into investigating certain issues and contribute a fix, we're willing to accept PR's!
    - This way we can get some improvements to the current official renderer before abandoing it, while we as team focus on other things.
    - **IMPORTANT:** Please read section below on contributing!
4) Allow for custom renderer forks
    - Some changes and feature additions might not mesh with our plans and we would not merge them in
    - You can make your own fork of the renderer and use that one with Resonite
    - It's easy to replace renderer in Resonite with a custom one
    - We want to encourage experimenting & tinkering!
    - The goal of the official renderer is long term content compatibilty

# Building
Unity Version: Unity 2019.4f19

1) Open the project in Unity
2) Ensure `UMP_SUPPORTED` keyword is not defined if you don't have UniversalMediaPlayer in the repo
3) Go Build -> Windows

# Contributing
If you'd like to contribute to the Unity official renderer, feel free! We welcome contributions that help improve the experience for everyone, provided that they:

- Do not break compatibility with existing content on Resonite
- Do not introduce other issues
- Do not introduce new behaviors that we'd have to support long term that would go against our plans
- Do not cause regressions in performance (especially in VR)
- Comply with our platform guidelines

> [!IMPORTANT]
> We reserve right to deny any PR's for any reason.
> We will generally strive to accept good contributions, but we do not provide guarantee that they will always be merged. Our decision on this is final.

## Bug Fixes
Generally bug fixes will be accepted! To contribute a bugfix please:

1) Make sure there's an issue for it at Resonite repo: https://github.com/Yellow-Dog-Man/Resonite-Issues/issues
2) Check if someone is already assigned to the issue or not
    - If someone already has issue assigned, it's recommended to ask them first
3) Ideally communicate in the issue that you plan to work on the fix
    - This way team members and other community know not to start work on that issue themselves
4) Test your fix thoroughly before submitting PR!
    - Ensure that it doesn't break anything else
    - If it affects in-game content, test various scenarios that people would typically use
    - Expected level of testing is going to be generally proportional to the changes you made
    - Write down a quick summary of the testing you've done - this can help speed up PR merging quickly
5) Create a PR!

## New Features
Adding new features to the renderer is a bit trickier for a few reasons:

- Some can be difficult to do purely on renderer side and require FrooxEngine additions to do properly
    - In some circumstances we could collaborate on those - **communicate with us first**
- We have to consider how they affect all users of Resonite long term
- We also consider how they affect long term compatibility of the content - is the addition something we can reasonably support indefinitely?
- We also have to consider if it would add significant amount of labor for replacing the renderer in the future

If you want to contribute a new feature anyway, please follow these steps:

1) Ensure there's an issue for it at Resonite repo: https://github.com/Yellow-Dog-Man/Resonite-Issues/issues
2) Create issue in this repo proposing the implementation
   - It will help to give a rough outline on how you'd plan to approach the feature
3) Wait for us to give a green light
   - If we don't give green light, it's a lot less likely we'd accept your PR
   - If you don't wait for the green light, you might end up wasting a lot of your time - please communicate first!
4) Work on the feature
5) Submit PR

### Examples of feature additions we'd be willing to accept
To give you some idea on what we'd be likely willing to accept, here are some things we'd be generally ok with:

- Alternate video playback engines
    - Provided they do not add support for formats that we might not be able to support in the future
- Additional Antialiasing (AA) modes
    - If there's a good algorithm that might work, we'd be happy to accept this!
    - We'll help coordinate and add it to the enum on FrooxEngine side
- Additional VR controllers & headset support
    - E.g. contributing bindings & detection for HW we might not have or support properly
    - If it needs FrooxEngine side changes, please ask first!
    - The HW should generally be one that fits into the existing system however and needs to exist specifically on renderer side for handling - otherwise it would need to go to FrooxEngine!
- Compiling with IL2CPP
    - I've tried before (I being Froox) and lost my sanity
    - I'm not sure if this will actually help with performance
    - If you wanna take a stab at this, feel free!
    - More info here: https://github.com/Yellow-Dog-Man/Resonite-Issues/issues/4752
- OS X support
    - This repo alone isn't probably enough to do this yet
    - Once we open source other bits though, this could help!

> [!IMPORTANT]
> Keep in mind that this list is not exhausive! If you want to contribute something not on this list - please ask!

# Can someone just make their own Resonite with this?
No, this is not possible. Most of our engine logic is in FrooxEngine that exists outside of this repo. The renderer part is just a component that gets sent the final data for rendering output.

# What about Renderite.Shared.dll & Renderite.Unity.dll?
A big part of the renderer logic resides in the Renderite.Shared & Renderite.Unity libraries. The source code of these is not part of this repository - this is only for the Unity-side.

We plan to open source these as well as soon as possible in separate repositories - however they need to be separated from the rest of the solution first and dependencies on some parts of FrooxEngine assemblies resolved.

Our goal is to open source the renderer in incremental steps, with this repo being the first one.

# Are there any parts missing from this repo?
Yes. This repo is notably missing source for the UMP (UniversalMediaPlayer) playback engine. This is unfortunately a commercial package that we cannot re-distribute, even though it's likely abandonware at this point.

The package is still included in our internal builds, but is excluded from the public repository.

Fortunately this component is not critical - you can build the renderer completely without it - it will just not be able to play all videos.

However we do include our own code for handling the UMP playback engine - it is conditionally compiled away with `UMP_SUPPORTED` keyword.

If you have your own copy of this, you can place it in `UniversalMediaPlayer` folder in the `Assets` directory and it should work!