# What is this?
This repository contains the Unity renderer for Resonite. Resonite is a free social VR sandbox platform, which allows for socialization and collaborative in-game building.

You can get Resonite free on Steam: https://store.steampowered.com/app/2519830/Resonite/

Resonite uses a unique multi-process architecture, where the majority of Resonite is a fully custom engine running with modern .NET 10 runtime. The engine communicates with the Unity Renderer process via IPC (inter process communication) and shared memory.

This architecture allows for much higher performance and properly isolates the renderer from the rest of the engine, allowing it to be replaced in the future.

# What's the purpose of this repo?
Making this repository open source has several goals:

1) Serve as a reference for custom renderers
    - Resonite is built around community creation and welcomes tinkering and experimenting
    - Our community has already been making a number of custom renderers. Making our current official one open should help with these endeavors
2) Make it easier for the community to help us with a new official renderer
    - Our goal is to completely replace Unity as a renderer in the future with one that we have much better control over and fix a number of issues
    - Making the current one open allows the community to help us document its behavior and help with a new open source renderer
    - Read more about our goals for the new renderer here: https://github.com/Yellow-Dog-Man/Resonite-Issues/discussions/5830
3) Allow for community bugfixes & feature additions* for the renderer
    - As we're still a small team, we don't have the time to investigate all bugs and issues
    - However, if community members are willing to put time into investigating certain issues and contributing a fix, we're willing to accept PRs!
    - This way we can get some improvements to the current official renderer before abandoning it, while we as a team focus on other things.
    - **IMPORTANT:** Please read the section below on contributing!
4) Allow for custom renderer forks
    - Some changes and feature additions might not mesh with our plans and we would not merge them in
    - You can make your own fork of the renderer and use that one with Resonite
    - It's easy to replace the renderer in Resonite with a custom one
    - We want to encourage experimenting & tinkering!
    - The goal of the official renderer is long-term content compatibility

## What is this not?
- Support for custom shaders
    - Custom shaders will require us to move away from Unity completely - it's not possible to compile and load shaders dynamically with Unity in a reasonable way
    - Shader loading is not controlled by the renderer project, but by the FrooxEngine side. The renderer will only load the shaders it's been given.
- SDK for building Resonite content with Unity
    - The Unity SDK is a completely separate project available here: https://github.com/Yellow-Dog-Man/Resonite.UnitySDK

# Cloning the repo
This repo has GitLFS files and thusly, requires Git LFS to be installed alongside the Git CLI. More info about GitLFS avalible here: https://git-lfs.com/

- Git for Windows comes with Git LFS installed by default.
- Linux 
   - Arch; git-lfs is available in the extra repo:
    `pacman -S git-lfs`
   - Ubuntu/Debian:
    `apt install git-lfs`
   - Fedora/RHEL:
    `dnf install git-lfs` 
- Once Downloaded on Linux, run `git lfs install` in the terminal to finish the install.

# Building
Unity Version: Unity 2019.4f19

1) Open the project in Unity
2) Ensure `UMP_SUPPORTED` keyword is not defined if you don't have UniversalMediaPlayer in the repo
3) Go to Build -> Windows

# Reporting bugs
For reporting bugs, we recommend doing so on the main Resonite repo here: https://github.com/Yellow-Dog-Man/Resonite-Issues/issues

We monitor this repo more often for issues and some bugs can also be FrooxEngine-side, requiring a more complex approach.

# Contributing
If you'd like to contribute to the Unity official renderer, feel free! We welcome contributions that help improve the experience for everyone, provided that they:

- Do not break compatibility with existing content on Resonite
- Do not introduce other issues
- Do not introduce new behaviors that we'd have to support long-term that would go against our plans
- Do not cause regressions in performance (especially in VR)
- Comply with our platform guidelines

> [!IMPORTANT]
> We reserve the right to deny any PRs for any reason.
> We will generally strive to accept good contributions, but we do not provide a guarantee that they will always be merged. Our decision on this is final.

## Bug fixes
Generally bug fixes will be accepted! To contribute a bugfix, please:

1) Make sure there's an issue for it at the Resonite repo: https://github.com/Yellow-Dog-Man/Resonite-Issues/issues
2) Check if someone is already assigned to the issue or not
    - If someone already has the issue assigned, it's recommended to ask them first
3) Ideally, communicate in the issue that you plan to work on the fix
    - This way team members and other community members know not to start work on that issue themselves
4) Test your fix thoroughly before submitting a PR!
    - Ensure that it doesn't break anything else
    - If it affects in-game content, test various scenarios that people would typically use
    - The expected level of testing is going to be generally proportional to the changes you made
    - Write down a quick summary of the testing you've done - this can help speed up PR merging
5) Create a PR!

## New features
Adding new features to the renderer is a bit trickier for a few reasons:

- Some can be difficult to do purely on the renderer side and require FrooxEngine additions to do properly
    - In some circumstances we could collaborate on those - **communicate with us first**
- We have to consider how they affect all users of Resonite long term
- We also consider how they affect long term compatibility of the content - is the addition something we can reasonably support indefinitely?
- We also have to consider if it would add a significant amount of labor for replacing the renderer in the future

If you want to contribute a new feature anyway, please follow these steps:

1) Ensure there's an issue for it at the Resonite repo: https://github.com/Yellow-Dog-Man/Resonite-Issues/issues
2) Create an issue in this repo proposing the implementation
   - It will help to give a rough outline on how you'd plan to approach the feature
3) Wait for us to give a green light
   - If we don't give the green light, it's a lot less likely we'd accept your PR
   - If you don't wait for the green light, you might end up wasting a lot of your time - please communicate first!
4) Work on the feature
5) Submit a PR

### Examples of feature additions we'd be willing to accept
To give you some idea on what we'd be likely willing to accept, here are some things we'd be generally ok with:

- Alternate video playback engines
    - Provided they do not add support for formats that we might not be able to support in the future
- Additional antialiasing (AA) modes
    - If there's a good algorithm that might work, we'd be happy to accept this!
    - We'll help coordinate and add it to the enum on the FrooxEngine side
- Additional VR controllers & headset support
    - E.g. contributing bindings & detection for HW we might not have or support properly
    - If it needs FrooxEngine-side changes, please ask first!
    - The HW should generally be one that fits into the existing system and needs to exist specifically on the renderer side for handling - otherwise it would need to go to FrooxEngine!
- Compiling with IL2CPP
    - I've tried before (I being Froox) and lost my sanity
    - I'm not sure if this will actually help with performance
    - If you wanna take a stab at this, feel free!
    - More info here: https://github.com/Yellow-Dog-Man/Resonite-Issues/issues/4752
- OS X support
    - This repo alone probably isn't enough to do this yet
    - Once we open source other bits though, this could help!

> [!IMPORTANT]
> Keep in mind that this list is not exhaustive! If you want to contribute something not on this list - please ask!

# Can someone just make their own Resonite with this?
No, this is not possible. Most of our engine logic is in FrooxEngine that exists outside of this repo. The renderer part is just a component that gets sent the final data for rendering output.

# What about Renderite.Shared.dll & Renderite.Unity.dll?
A big part of the renderer logic resides in the Renderite.Shared & Renderite.Unity libraries. The source code of these is not part of this repository - this is only for the Unity-side.

You can find repository here: https://github.com/Yellow-Dog-Man/Renderite

# Are there any parts missing from this repo?
Yes. This repo is notably missing source for the UMP (UniversalMediaPlayer) playback engine. This is unfortunately a commercial package that we cannot re-distribute, even though it's likely abandonware at this point.

The package is still included in our internal builds, but is excluded from the public repository.

Fortunately this component is not critical - you can build the renderer completely without it - it will just not be able to play all videos.

However, we do include our own code for handling the UMP playback engine - it is conditionally compiled away with the `UMP_SUPPORTED` keyword.

If you have your own copy of this, you can place it in the `UniversalMediaPlayer` folder in the `Assets` directory and it should work!
