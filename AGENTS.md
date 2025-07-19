# Agent Instructions

## Setup

This project is a C# mod for Dyson Sphere Program and is built using the .NET SDK.

Please ensure the environment is configured with a recent version of the .NET SDK (e.g., .NET 6 or later).

## Dependencies

All game and modding library dependencies (like `Assembly-CSharp.dll`, `UnityEngine.dll`, `0Harmony.dll`, etc.) are managed automatically through public NuGet packages. The project is already configured to use them.

There is no need to manually locate or copy DLL files.

## Build Process

Before attempting to build the solution, the agent **must** first restore the NuGet packages. This will download and link all the required dependencies.

1.  **Restore Dependencies:** Run `dotnet restore` in the root directory of the repository.
2.  **Build the Solution:** Run `dotnet build` or `msbuild` to compile the project. The solution file (`.sln`) is located in the root directory.

## Versioning and Changelog

After every feature implementation or bug fix, you **must** perform the following steps before submitting your changes:

1.  **Increment the version number** in the following files:
    * `NexusLogistics.cs`
    * `Properties/AssemblyInfo.cs`
    * `README.md`
2.  **Add a new entry to the changelog** in `README.md`. The entry should clearly and concisely describe the changes you made.
3.  **Build the solution** to ensure there are no compilation errors and the code builds successfully.