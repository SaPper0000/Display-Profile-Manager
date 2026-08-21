# Build Instructions

Open `Tarkov-Gamma-Manager-v1.4.sln` in Visual Studio 2022.

Make sure the required .NET Framework 4.7.2 development components are installed.

In Solution Explorer, confirm that the `Gamma Manager` project loads correctly.

Select:

- Configuration: `Release`
- Platform: `Any CPU`

Then use **Build → Rebuild Solution**.

Keep the solution and project folder structure intact. Do not move only the `.sln` file to another location.

For the English build, `Gamma Manager/LanguageManager.cs` must contain:

```csharp
public const bool Korean = false;
```
