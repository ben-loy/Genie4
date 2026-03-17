# Desktop Font and Color Rendering — Design Spec

**Date:** 2026-03-15
**Feature:** Add per-run font and color support to the Avalonia Desktop output window
**Scope:** Rendering only — no settings UI, no config persistence

---

## Goal

Replace the plain-text `SelectableTextBlock.Text` concatenation in `MainWindow` with an
`Inlines`-based approach so that the `color`, `bgcolor`, and `mono` parameters from
`EventPrintText` are rendered correctly, matching the WinForms `ComponentRichTextBox`
behaviour.

---

## Background

### WinForms

`ComponentRichTextBox.AddToBuffer` uses `SelectionColor`, `SelectionBackColor`, and
`SelectionFont` to apply per-run styling to a `RichTextBox`. Three fonts are supported
(main, mono, input) and two colours (fg, bg) per run. Transparent / empty colours fall
through to the control's default foreground/background. `AddToBuffer` does **not** append
an extra newline after each call — game text already carries its own newlines.

### Avalonia Desktop (current)

`MainWindow.AppendOutput` concatenates all text into `OutputText.Text` and appends a
hard-coded `"\n"` after every call, discarding `color`, `bgcolor`, and `mono`. The
`SelectableTextBlock` has a hard-coded `FontFamily="Courier New,Consolas,monospace"` and
`FontSize="13"` in XAML.

---

## Design

### Required namespace additions to `MainWindow.axaml.cs`

```csharp
using Avalonia.Controls.Documents;   // Run, LineBreak, InlineCollection
using Avalonia.Media;                 // IBrush, SolidColorBrush
```

`using Avalonia.Controls;` is already present. `using System.Drawing;` is already present.

### Control

Keep `SelectableTextBlock` (`OutputText`). It inherits `TextBlock.Inlines` from Avalonia's
`TextBlock` and adds text-selection capability.

API provenance (Avalonia 11.3.12, verified by building a standalone test project):

| API | Class | Namespace |
|-----|-------|-----------|
| `Inlines` | `TextBlock` → `SelectableTextBlock` | `Avalonia.Controls` |
| `Run.Foreground` | `TextElement` → `Inline` → `Run` | `Avalonia.Controls.Documents` |
| `Run.Background` | `TextElement` → `Inline` → `Run` | `Avalonia.Controls.Documents` |
| `Run.FontFamily` | `TextElement` → `Inline` → `Run` | `Avalonia.Controls.Documents` |
| `LineBreak` | direct | `Avalonia.Controls.Documents` |
| `InlineCollection.AddRange` | `AvaloniaList<Inline>` → `InlineCollection` | `Avalonia.Controls.Documents` |

All compiled and linked without error against Avalonia 11.3.12 / net10.0.

**XAML:** No changes. The existing `FontFamily` and `FontSize` attributes remain as
control-level defaults inherited by all runs that do not override them.

### Color conversion

```csharp
/// Returns null for transparent/empty colors (inherits control default).
private static IBrush? ToBrush(System.Drawing.Color c)
    => (c.IsEmpty || c.A == 0)
       ? null
       : new SolidColorBrush(new Avalonia.Media.Color(c.A, c.R, c.G, c.B));
```

- `System.Drawing.Color.Empty` → `IsEmpty == true` → null
- `System.Drawing.Color.Transparent` → `A == 0` → null
- Any other colour → `SolidColorBrush` with ARGB mapped 1-to-1

### AppendOutput

New signature:

```csharp
private void AppendOutput(string text,
                          System.Drawing.Color fg = default,
                          System.Drawing.Color bg = default)
```

`default` resolves to `System.Drawing.Color.Empty`. The three call sites that pass no
colors — the two status-message calls in `ConnectButton_Click` and the call in
`OnPrintError` — pass through unchanged and inherit the control foreground/background.
`OnPrintText` is a fourth call site that is **intentionally updated** in this change to
pass `color` and `bgcolor`.

**Trailing newline policy:** `AppendOutput` does **not** append a trailing `LineBreak`
after each call. This matches WinForms `AddToBuffer` behaviour — the game protocol
delivers text with embedded newlines. The current scaffolding `+ "\n"` is removed.

**Call sites that need `"\n"` added** — the three internal status-message calls in
`ConnectButton_Click` produce bare strings with no trailing newline; add `"\n"` to each:
- `AppendOutput("[Connect] Account and password are required.")` →
  `AppendOutput("[Connect] Account and password are required.\n")`
- `AppendOutput(string.IsNullOrWhiteSpace(character) ? "[Listing characters...]" : $"[Connecting as {character}...]")` →
  append `"\n"` to each branch or the whole ternary expression
- `AppendOutput($"[Connect error: {ex.Message}]")` →
  `AppendOutput($"[Connect error: {ex.Message}]\n")`

**Call sites that must NOT have `"\n"` added** — `OnPrintError`'s `AppendOutput(text)`
call is left unchanged. Error text arrives from the game and already carries its own
newlines, exactly like `OnPrintText`.

Body:

1. Compute `IBrush? fgBrush = ToBrush(fg)` and `IBrush? bgBrush = ToBrush(bg)`.
2. Normalize line endings: `text = text.Replace("\r\n", "\n").Replace("\r", "\n")`.
   This guards against CRLF sequences that the game protocol or upstream layers may
   deliver; without it, trailing `'\r'` characters would appear in rendered runs.
3. Split normalized `text` on `'\n'` → `string[] segments`.
4. Allocate `var inlines = new List<Inline>(segments.Length * 2)`.
5. For each segment at index `i`:
   - If `segments[i].Length > 0`: create `Run(segments[i])`, set `.Foreground` /
     `.Background` when the corresponding brush is non-null; add to `inlines`.
   - If `i < segments.Length - 1`: add a `LineBreak` to `inlines`.
   - Edge case — empty `text` (`""`): `Split('\n')` produces `[""]`; the length-0 guard
     means no `Run` is created and the `i < Length - 1` condition is false, so no
     `LineBreak` is added. `AddRange` is called with an empty list — a no-op. This is
     the correct behaviour.
6. `OutputText.Inlines.AddRange(inlines)` — single collection-change notification.
7. `OutputScroll.Offset = new Avalonia.Vector(OutputScroll.Offset.X, double.MaxValue)`.
   (`Avalonia.Vector` is fully qualified to avoid ambiguity; no `using Avalonia;` is
   added.)

The existing `OutputText.Text = …` assignment is removed entirely.

### Mono font

`mono=true` is captured in `OnPrintText` but does **not** change `Run.FontFamily` in this
pass. Both mono and non-mono runs inherit the control's monospace default. A comment in
the code marks the hook for the follow-on font-configuration sub-project.

### OnPrintText

```csharp
private void OnPrintText(string text, Color color, Color bgcolor,
                         Game.WindowTarget targetwindow, string targetwindowstring,
                         bool mono, bool isprompt, bool isinput)
{
    // mono, isprompt, isinput reserved for future sub-projects
    Dispatcher.UIThread.Post(() => AppendOutput(text, color, bgcolor));
}
```

(`Color` unqualified here resolves to `System.Drawing.Color` via the existing
`using System.Drawing;` at the top of the file.)

### Buffer

No cap. All output is retained for the session lifetime (user preference).

---

## Files Changed

| File | Change |
|------|--------|
| `Desktop/MainWindow.axaml.cs` | Add `using` directives; replace `AppendOutput(string)` with `AppendOutput(string, Color, Color)`; add `ToBrush` helper; update `OnPrintText` to pass colours; add `"\n"` to three `ConnectButton_Click` status-message call sites |
| `Desktop/MainWindow.axaml` | No change |

---

## Out of Scope

- Font family / size configuration (follow-on sub-project)
- Settings UI
- **Mono font visual distinction** — non-mono runs will render in the same monospace
  font as mono runs in this pass. Both inherit the control's `FontFamily` default. This
  is an accepted, intentional limitation; a future sub-project will add a separate
  proportional font for non-mono text.
- Buffer capping / pruning
- `isprompt` / `isinput` handling
- Multi-window routing (`WindowTarget`)
