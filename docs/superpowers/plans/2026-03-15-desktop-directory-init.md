# Desktop Directory Initialization Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add startup directory initialization to the Avalonia Desktop build so it creates the same folder structure (`Config/`, `Logs/`, `Scripts/`, etc.) that the WinForms build creates at first launch.

**Architecture:** Insert `LocalDirectory.CheckUserDirectory()` + 11 `Directory.CreateDirectory` calls at the top of `App.OnFrameworkInitializationCompleted()`, before the DI host is built. No shared code changes — Desktop-only.

**Tech Stack:** C# / .NET 10, Avalonia 11, `System.IO.Directory`, `GenieClient.LocalDirectory`

---

## Chunk 1: Directory init

### Task 1: Add directory initialization to `App.axaml.cs`

**Files:**
- Modify: `Desktop/App.axaml.cs`

**Background for implementer:**

`LocalDirectory` is a static class in `LocalDirectory.cs` at the repo root.
- `LocalDirectory.Path` — the resolved data directory path (set by `CheckUserDirectory`)
- `LocalDirectory.CheckUserDirectory()` — checks for a `Config/` subfolder next to the binary. If absent, calls `SetUserDataDirectory()` which sets `LocalDirectory.Path` to `~/.config/Genie.Desktop/` on Mac/Linux (using `Assembly.GetExecutingAssembly().GetName().Name`). This is identical to what `FormMain` does on the WinForms side.
- `Directory.CreateDirectory` — idempotent; safe to call on every launch even if the directory already exists.

`App.axaml.cs` is the Avalonia app entry point. `OnFrameworkInitializationCompleted()` builds the DI host and shows the main window. The directory init must run **before** the host is built, since `Game` and `Globals` are constructed by the host and may reference these directories.

Do NOT call `Utility.MoveLayoutFiles()` — it uses a VB.NET Windows-only API and is a one-time migration helper not needed for fresh Desktop installs.

- [ ] **Step 1: Add `using GenieClient;` to `Desktop/App.axaml.cs`**

  The file already has `using GenieClient.Genie;`. Add `using GenieClient;` before it so `LocalDirectory` is accessible without a fully qualified name. Note: the init block uses `System.IO.Directory` and `System.IO.Path` fully qualified, so no `using System.IO;` is required.

  Result — top of file should read:
  ```csharp
  using Avalonia;
  using Avalonia.Controls.ApplicationLifetimes;
  using Avalonia.Markup.Xaml;
  using Microsoft.Extensions.DependencyInjection;
  using Microsoft.Extensions.Hosting;
  using GenieClient;
  using GenieClient.Genie;
  ```

- [ ] **Step 2: Insert directory init block at the top of `OnFrameworkInitializationCompleted()`**

  Insert these lines as the very first statements of the method body, before `var builder = Host.CreateApplicationBuilder();`:

  ```csharp
  // Mirror FormMain.CreateGenieFolders() startup directory initialization
  LocalDirectory.CheckUserDirectory();
  string dataPath = LocalDirectory.Path;
  System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config"));
  System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config", "Profiles"));
  System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config", "Layout"));
  System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config", "PluginKeys"));
  // Note: Utility.MoveLayoutFiles() intentionally omitted — Windows-only migration aid
  System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Help"));
  System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Icons"));
  System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Logs"));
  System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Scripts"));
  System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Sounds"));
  System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Plugins"));
  System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Maps"));
  ```

  The complete `OnFrameworkInitializationCompleted` method should look like:

  ```csharp
  public override async void OnFrameworkInitializationCompleted()
  {
      // Mirror FormMain.CreateGenieFolders() startup directory initialization
      LocalDirectory.CheckUserDirectory();
      string dataPath = LocalDirectory.Path;
      System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config"));
      System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config", "Profiles"));
      System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config", "Layout"));
      System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Config", "PluginKeys"));
      // Note: Utility.MoveLayoutFiles() intentionally omitted — Windows-only migration aid
      System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Help"));
      System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Icons"));
      System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Logs"));
      System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Scripts"));
      System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Sounds"));
      System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Plugins"));
      System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dataPath, "Maps"));

      var builder = Host.CreateApplicationBuilder();
      builder.Services
          .AddSingleton<Globals>()
          .AddSingleton<Game>(sp =>
          {
              var globals = sp.GetRequiredService<Globals>();
              return new Game(ref globals);
          })
          .AddSingleton<MainWindow>();
      _host = builder.Build();

      if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
      {
          desktop.ShutdownRequested += async (_, _) => await _host!.StopAsync();
          await _host.StartAsync();
          desktop.MainWindow = _host.Services.GetRequiredService<MainWindow>();
      }

      base.OnFrameworkInitializationCompleted();
  }
  ```

- [ ] **Step 3: Build to verify no errors**

  From the worktree root (`worktrees/auth` inside the repo):
  ```bash
  dotnet build Desktop/Genie4.Desktop.csproj
  ```

  Expected: `0 Error(s)` (warnings are pre-existing and acceptable).

- [ ] **Step 4: Smoke test — verify directories are created**

  **Mac/Linux only** (path shown is `~/.config/Genie.Desktop/` — this applies when no `Config/` folder exists next to the binary, which is the normal case on first run).

  Run the app and immediately quit:

  ```bash
  dotnet run --project Desktop/Genie4.Desktop.csproj &
  sleep 3
  kill %1 2>/dev/null; true
  ls ~/.config/Genie.Desktop/
  ```

  Expected output includes: `Config  Help  Icons  Logs  Maps  Plugins  Scripts  Sounds`

  Also verify nested dirs:
  ```bash
  ls ~/.config/Genie.Desktop/Config/
  ```
  Expected: `Layout  PluginKeys  Profiles`

- [ ] **Step 5: Commit**

  ```bash
  git add Desktop/App.axaml.cs
  git commit -m "feat: initialize app directory structure at Desktop startup

  Mirrors FormMain.CreateGenieFolders() — calls LocalDirectory.CheckUserDirectory()
  then creates Config/, Logs/, Scripts/, and 8 other subdirs before the DI host
  starts. Utility.MoveLayoutFiles() is intentionally excluded (Windows-only API).

  Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
  ```
