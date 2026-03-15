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
through to the control's default foreground/background.

### Avalonia Desktop (current)

`MainWindow.AppendOutput` concatenates all text into `OutputText.Text`, discarding
`color`, `bgcolor`, and `mono`. The `SelectableTextBlock` has a hard-coded
`FontFamily="Courier New,Consolas,monospace"` and `FontSize="13"` in XAML.

---

## Design

### Control

Keep `SelectableTextBlock` (`OutputText`). It inherits `TextBlock.Inlines` from Avalonia's
`TextBlock` and adds text-selection capability. Verified against Avalonia 11.3.12: all
required APIs (`Inlines`, `Run.Foreground`, `Run.Background`, `Run.FontFamily`,
`LineBreak`, `InlineCollection.AddRange`) compile and link correctly.

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

`default` is `Color.Empty`, so existing call sites (`ConnectButton_Click`, `OnPrintError`)
pass through unchanged and inherit the control foreground.

Body:

1. Compute `IBrush? fgBrush = ToBrush(fg)` and `IBrush? bgBrush = ToBrush(bg)`.
2. Split `text` on `'\n'` → `string[] segments`.
3. Allocate `var inlines = new List<Inline>(segments.Length * 2)`.
4. For each segment at index `i`:
   - If `segments[i].Length > 0`: create `Run(segments[i])`, set `.Foreground` /
     `.Background` when the corresponding brush is non-null; add to `inlines`.
   - If `i < segments.Length - 1`: add a `LineBreak` to `inlines`.
5. `OutputText.Inlines.AddRange(inlines)` — single collection-change notification.
6. `OutputScroll.Offset = new Vector(OutputScroll.Offset.X, double.MaxValue)`.

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

### Buffer

No cap. All output is retained for the session lifetime (user preference).

---

## Files Changed

| File | Change |
|------|--------|
| `Desktop/MainWindow.axaml.cs` | Replace `AppendOutput(string)` with `AppendOutput(string, Color, Color)`; add `ToBrush` helper; update `OnPrintText` to pass colours |
| `Desktop/MainWindow.axaml` | No change |

---

## Out of Scope

- Font family / size configuration (follow-on sub-project)
- Settings UI
- Mono font visual distinction
- Buffer capping / pruning
- `isprompt` / `isinput` handling
- Multi-window routing (`WindowTarget`)
