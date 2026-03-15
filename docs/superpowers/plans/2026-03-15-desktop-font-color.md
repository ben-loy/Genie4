# Desktop Font and Color Rendering Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace plain-text `SelectableTextBlock.Text` concatenation in `MainWindow` with `Inlines`-based styled runs so that the `color`, `bgcolor`, and `mono` parameters from `EventPrintText` are rendered correctly.

**Architecture:** `AppendOutput` gains fg/bg color parameters; it splits text on `'\n'`, builds `Run`+`LineBreak` inlines with per-run `Foreground`/`Background` brushes, and appends them all at once via `InlineCollection.AddRange`. A static `ToBrush` helper converts `System.Drawing.Color` → `Avalonia.Media.SolidColorBrush` (null for transparent/empty). No XAML changes are needed.

**Tech Stack:** C# / Avalonia 11.3.12 / .NET 10 — `Avalonia.Controls.Documents` (`Run`, `LineBreak`, `InlineCollection`)

---

## Chunk 1: Implement color rendering in MainWindow

### Task 1: Rewrite `AppendOutput` and `OnPrintText` in `Desktop/MainWindow.axaml.cs`

**Spec:** `docs/superpowers/specs/2026-03-15-desktop-font-color-design.md`

**Files:**
- Modify: `Desktop/MainWindow.axaml.cs` (entire file — all changes are here)

**Context for the implementer:**

`MainWindow.axaml.cs` currently has a plain `AppendOutput(string text)` that does:
```csharp
OutputText.Text = (OutputText.Text ?? string.Empty) + text + "\n";
```
This discards all color information. `OutputText` is a `SelectableTextBlock` named in
`MainWindow.axaml`. `OutputScroll` is the enclosing `ScrollViewer`.

The `EventPrintText` handler currently ignores `color`, `bgcolor`, and `mono`:
```csharp
private void OnPrintText(string text, Color color, Color bgcolor, Game.WindowTarget targetwindow, string targetwindowstring, bool mono, bool isprompt, bool isinput)
{
    Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendOutput(text));
}
```

This project has **no unit test framework**. Verification is via `dotnet build` (must
produce 0 errors) followed by a manual smoke-test (connect to the game and confirm
coloured text appears).

---

- [ ] **Step 1: Add four new `using` directives at the top of `Desktop/MainWindow.axaml.cs`**

The file currently starts with:
```csharp
using System;
using System.Drawing;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using GenieClient.Genie;
```

Add the four new directives so the top of the file reads exactly:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Media;
using GenieClient.Genie;
using Color = System.Drawing.Color;
```

**Why the alias is needed:** Adding `using Avalonia.Media;` introduces `Avalonia.Media.Color`
into scope, which conflicts with `System.Drawing.Color` (both reachable as plain `Color`).
The alias `using Color = System.Drawing.Color;` pins `Color` to `System.Drawing.Color`
throughout the file. `Avalonia.Media.Color` is still usable — fully qualified — inside `ToBrush`.

---

- [ ] **Step 2: Add the `ToBrush` static helper method**

Add this private static method anywhere in the `MainWindow` class body (before or after
`AppendOutput` is fine):

```csharp
/// <summary>
/// Converts a System.Drawing.Color to an Avalonia brush.
/// Returns null for transparent or empty colours so the Run inherits the control default.
/// </summary>
private static IBrush? ToBrush(Color c)
    => (c.IsEmpty || c.A == 0)
       ? null
       : new SolidColorBrush(new Avalonia.Media.Color(c.A, c.R, c.G, c.B));
```

`Color` here resolves to `System.Drawing.Color` via the alias added in Step 1.
`Avalonia.Media.Color` is fully qualified to avoid any ambiguity.

---

- [ ] **Step 3: Replace `AppendOutput` with the new styled-run implementation**

Delete the existing `AppendOutput` method:
```csharp
private void AppendOutput(string text)
{
    // TODO(sub-project 2): Replace with styled run appends (RichTextBlock or custom renderer).
    // String concatenation here is O(n) per line; acceptable only for foundation scaffolding.
    OutputText.Text = (OutputText.Text ?? string.Empty) + text + "\n";
    OutputScroll.Offset = new Avalonia.Vector(OutputScroll.Offset.X, double.MaxValue);
}
```

Replace it with:
```csharp
private void AppendOutput(string text,
                           Color fg = default,
                           Color bg = default)
{
    IBrush? fgBrush = ToBrush(fg);
    IBrush? bgBrush = ToBrush(bg);

    // Normalize line endings to guard against CRLF sequences from the game protocol.
    text = text.Replace("\r\n", "\n").Replace("\r", "\n");

    // Each segment becomes a Run; LineBreaks are inserted between segments.
    // Capacity hint: worst case is one Run + one LineBreak per segment.
    string[] segments = text.Split('\n');
    var inlines = new List<Inline>(segments.Length * 2);
    for (int i = 0; i < segments.Length; i++)
    {
        if (segments[i].Length > 0)
        {
            var run = new Run(segments[i]);
            if (fgBrush != null) run.Foreground = fgBrush;
            if (bgBrush != null) run.Background = bgBrush;
            inlines.Add(run);
        }
        if (i < segments.Length - 1)
            inlines.Add(new LineBreak());
    }

    OutputText.Inlines.AddRange(inlines);
    OutputScroll.Offset = new Avalonia.Vector(OutputScroll.Offset.X, double.MaxValue);
}
```

Key points:
- `Color fg = default` → `Color.Empty` (the alias from Step 1), so the three call sites
  in `ConnectButton_Click` and `OnPrintError` that pass no colors still compile unchanged.
- `OutputText.Inlines.AddRange` triggers a single collection-change notification instead
  of one per inline.
- The `OutputText.Text = …` assignment is gone entirely. **Never set `.Text` on `OutputText`
  again** — mixing `.Text` and `.Inlines` on a `TextBlock` clears one when the other is set.

---

- [ ] **Step 4: Update `OnPrintText` to pass `color` and `bgcolor`, and add mono hook comment**

Find the existing handler:
```csharp
private void OnPrintText(string text, Color color, Color bgcolor, Game.WindowTarget targetwindow, string targetwindowstring, bool mono, bool isprompt, bool isinput)
{
    Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendOutput(text));
}
```

Replace it with:
```csharp
private void OnPrintText(string text, Color color, Color bgcolor,
                         Game.WindowTarget targetwindow, string targetwindowstring,
                         bool mono, bool isprompt, bool isinput)
{
    // mono: font switching deferred to font-configuration sub-project;
    //   both mono and non-mono runs are monospace in this pass (inherit control default).
    // isprompt, isinput: reserved for future sub-projects.
    Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendOutput(text, color, bgcolor));
}
```

---

- [ ] **Step 5: Add trailing `"\n"` to the three `ConnectButton_Click` status messages**

The three internal status messages produce bare strings with no newline. Now that
`AppendOutput` no longer appends one, each must carry its own.

Find and update these three call sites inside `ConnectButton_Click`:

**Call site 1** — validation failure message:
```csharp
// Before:
AppendOutput("[Connect] Account and password are required.");
// After:
AppendOutput("[Connect] Account and password are required.\n");
```

**Call site 2** — connecting/listing message:
```csharp
// Before:
AppendOutput(string.IsNullOrWhiteSpace(character)
    ? "[Listing characters...]"
    : $"[Connecting as {character}...]");
// After:
AppendOutput((string.IsNullOrWhiteSpace(character)
    ? "[Listing characters...]"
    : $"[Connecting as {character}...]") + "\n");
```

**Call site 3** — connection error message (inside the `catch` block):
```csharp
// Before:
AppendOutput($"[Connect error: {ex.Message}]");
// After:
AppendOutput($"[Connect error: {ex.Message}]\n");
```

`OnPrintError`'s `AppendOutput(text)` call is **not changed** — game error text already
carries its own newlines.

---

- [ ] **Step 6: Build to verify zero errors**

```bash
cd /Users/bloy/src/Genie4/worktrees/desktop-font-color
dotnet build Desktop/Genie4.Desktop.csproj -v q 2>&1 | tail -8
```

Expected output (pre-existing warnings are acceptable; errors must be zero):
```
    0 Error(s)
```

If there are compiler errors, diagnose before guessing at fixes. Common pitfalls:
- Missing `using Color = System.Drawing.Color;` alias → `Color` becomes ambiguous
- Missing `using System.Collections.Generic;` → `List<Inline>` won't resolve
- Any remaining `OutputText.Text = …` assignment → remove it; use `.Inlines` only
- `Dispatcher` used unqualified → use `Avalonia.Threading.Dispatcher` (fully qualified,
  as it already was in the original file)

---

- [ ] **Step 7: Smoke-test manually**

```bash
dotnet run --project Desktop/Genie4.Desktop.csproj
```

1. Enter account credentials and connect.
2. Confirm that game output appears in the output area.
3. Confirm that coloured text (e.g. room descriptions, combat messages) renders with
   visible foreground colour differences rather than plain white/grey.
4. Confirm that the status messages ("[Listing characters...]", "[Connecting as …]")
   each appear on their own line.
5. Confirm that text is still selectable (click and drag over output text).

If text appears blank, game text is not rendering — check that `OutputText.Inlines` is
being populated (not `OutputText.Text`).
If status messages run together, check that the three `"\n"` additions in Step 5 are in place.

---

- [ ] **Step 8: Commit**

```bash
git add Desktop/MainWindow.axaml.cs
git commit -m "feat: render per-run fg/bg colours in Desktop output window

Replace SelectableTextBlock.Text concatenation with Inlines-based
styled runs. OnPrintText now passes color/bgcolor to AppendOutput,
which converts them to SolidColorBrush and attaches them to Run
inlines. Transparent/empty colours inherit the control default.
CRLF line endings are normalised before splitting. Status-message
call sites in ConnectButton_Click updated to carry their own trailing
newlines now that AppendOutput no longer appends one."
```
