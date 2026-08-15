using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using UiMcp.Abstractions;

namespace UiMcp.Rendering;

/// <summary>
/// Turns a VALIDATED tree into a WPF visual tree.
///
/// SECURITY POSTURE, stated here so a later "small" change does not erode it:
///   - Element types come from THIS FILE only. Nothing is constructed from a name in the JSON.
///   - Text reaches the UI only through TextBlock.Text, which is inert - WPF does not parse markup
///     out of a string property. This is the structural equivalent of the JS renderer's
///     "textContent, never innerHTML" rule.
///   - Colours come from <see cref="Tone"/> here. The catalog's `tone` is a closed enum; the tree
///     cannot supply a brush, a style or a colour.
///   - Every data lookup goes through <see cref="PathResolver"/>, which refuses
///     __proto__/constructor/prototype and returns missing rather than throwing.
///   If you are editing this file and reach for anything that interprets a string as markup or as a
///   type name, stop: that is the whole attack surface.
///
/// All judgement lives in <see cref="RenderRules"/> and is unit-tested without a window. What
/// remains here is assembly, deliberately kept boring.
/// </summary>
public static class TreeRenderer
{
    // The LCARS-ish palette the HTML console already uses, on black. Renderer-owned by design.
    private static readonly SolidColorBrush Amber  = Frozen(0xff, 0x99, 0x00);
    private static readonly SolidColorBrush Salmon = Frozen(0xcc, 0x66, 0x66);
    private static readonly SolidColorBrush Lilac  = Frozen(0xcc, 0x99, 0xcc);
    private static readonly SolidColorBrush Sky    = Frozen(0x99, 0xcc, 0xff);
    private static readonly SolidColorBrush Green  = Frozen(0x8c, 0xcc, 0x66);
    private static readonly SolidColorBrush Grey   = Frozen(0x88, 0x88, 0x88);
    private static readonly SolidColorBrush Ink    = Frozen(0x0a, 0x0a, 0x0a);
    private static readonly SolidColorBrush Panel  = Frozen(0x14, 0x14, 0x14);

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();   // frozen brushes are shareable across the visual tree and cheaper
        return brush;
    }

    private static readonly FontFamily Mono = new("Consolas, Cascadia Mono, monospace");

    /// <summary>The ONLY place a tone becomes a colour. A tone the renderer does not know is muted.</summary>
    private static SolidColorBrush Tone(string? tone) => tone switch
    {
        "nominal" => Green,
        "attention" => Amber,
        "critical" => Salmon,
        "degraded" => Lilac,
        "info" => Sky,
        "muted" => Grey,
        _ => Amber
    };

    /// <summary>Missing values are visually distinct, not just textually. UNKNOWN must LOOK wrong.</summary>
    private static SolidColorBrush UnknownBrush => Salmon;

    public static UIElement Render(ValidatedNode node, JsonElement data, JsonElement? scope = null)
    {
        switch (node.Type)
        {
            case "StatusBanner": return StatusBanner(node);
            case "Panel": return PanelBox(node, data, scope);
            case "Row": return RowBox(node, data, scope);
            case "Metric": return Metric(node, data, scope);
            case "Field": return Field(node, data, scope);
            case "Gauge": return Gauge(node, data, scope);
            case "Repeat": return Repeat(node, data, scope);
            case "Table": return Table(node, data, scope);
            case "Note": return Note(node);

            // Unreachable: the validator refuses unknown components before anything reaches here.
            // Rendering a loud marker rather than throwing keeps a future catalog addition that
            // forgets this switch from taking down the whole display.
            default: return Text($"[no renderer for {node.Type}]", UnknownBrush, 14);
        }
    }

    // ---- components ------------------------------------------------------------------------------

    private static UIElement StatusBanner(ValidatedNode n)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(Text(Str(n, "label"), Ink, 18, bold: true));

        var detail = StrOrNull(n, "detail");
        if (detail is not null)
            stack.Children.Add(Text(detail, Ink, 14, margin: new Thickness(12, 2, 0, 0)));

        return new Border
        {
            Background = Tone(StrOrNull(n, "tone")),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 10),
            CornerRadius = new CornerRadius(3),
            Child = stack
        };
    }

    private static UIElement PanelBox(ValidatedNode n, JsonElement data, JsonElement? scope)
    {
        var body = new StackPanel();
        foreach (var c in n.Children) body.Children.Add(Render(c, data, scope));

        var head = Text(Str(n, "title"), Tone(StrOrNull(n, "tone")), 15, bold: true);
        head.Margin = new Thickness(0, 0, 0, 8);

        var inner = new StackPanel();
        inner.Children.Add(head);
        inner.Children.Add(body);

        return new Border
        {
            Background = Panel,
            BorderBrush = Tone(StrOrNull(n, "tone")),
            BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            Child = inner
        };
    }

    private static UIElement RowBox(ValidatedNode n, JsonElement data, JsonElement? scope)
    {
        var cols = n.Props.TryGetValue("cols", out var c) && c is int i ? i : 1;
        var grid = new UniformGrid { Columns = cols };
        foreach (var child in n.Children)
        {
            var cell = new Border { Padding = new Thickness(4, 0, 4, 0), Child = Render(child, data, scope) };
            grid.Children.Add(cell);
        }
        return grid;
    }

    private static UIElement Metric(ValidatedNode n, JsonElement data, JsonElement? scope)
    {
        var value = PathResolver.Resolve(data, Str(n, "valuePath"), scope);
        var unknown = RenderRules.IsUnknown(value);

        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        stack.Children.Add(Text(Str(n, "label"), Grey, 12));
        stack.Children.Add(Text(
            RenderRules.MetricText(value, StrOrNull(n, "unit")),
            unknown ? UnknownBrush : Tone(StrOrNull(n, "tone")),
            24, bold: true));

        var deltaPath = StrOrNull(n, "deltaPath");
        if (deltaPath is not null)
        {
            var delta = RenderRules.DeltaText(PathResolver.Resolve(data, deltaPath, scope));
            if (delta is not null) stack.Children.Add(Text(delta, Grey, 12));
        }

        return stack;
    }

    private static UIElement Field(ValidatedNode n, JsonElement data, JsonElement? scope)
    {
        var value = PathResolver.Resolve(data, Str(n, "valuePath"), scope);
        var unknown = RenderRules.IsUnknown(value);

        var dock = new DockPanel { Margin = new Thickness(0, 2, 0, 2), LastChildFill = false };
        var label = Text(Str(n, "label"), Grey, 13);
        DockPanel.SetDock(label, Dock.Left);

        var val = Text(PathResolver.Display(value), unknown ? UnknownBrush : Tone(StrOrNull(n, "tone")), 13);
        DockPanel.SetDock(val, Dock.Right);

        dock.Children.Add(label);
        dock.Children.Add(val);
        return dock;
    }

    private static UIElement Gauge(ValidatedNode n, JsonElement data, JsonElement? scope)
    {
        var value = PathResolver.Resolve(data, Str(n, "valuePath"), scope);
        var maxPath = StrOrNull(n, "maxPath");
        var max = maxPath is null ? null : PathResolver.Resolve(data, maxPath, scope);
        var pct = RenderRules.GaugePercent(value, max);
        var unknown = RenderRules.IsUnknown(value);

        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };

        // The label carries UNKNOWN. A 0% bar alone would read as a measured zero.
        stack.Children.Add(Text(
            $"{Str(n, "label")}  {PathResolver.Display(value)}",
            unknown ? UnknownBrush : Grey, 12));

        stack.Children.Add(new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = pct,
            Height = 10,
            Foreground = unknown ? UnknownBrush : Tone(StrOrNull(n, "tone")),
            Background = Ink,
            BorderThickness = new Thickness(0)
        });

        return stack;
    }

    private static UIElement Repeat(ValidatedNode n, JsonElement data, JsonElement? scope)
    {
        var arr = PathResolver.Resolve(data, Str(n, "fromPath"), scope);
        var stack = new StackPanel();

        if (arr is null || arr.Value.ValueKind != JsonValueKind.Array || arr.Value.GetArrayLength() == 0)
        {
            stack.Children.Add(Text(RenderRules.EmptyText(StrOrNull(n, "emptyText")), Grey, 13));
            return stack;
        }

        foreach (var item in arr.Value.EnumerateArray().Take(RenderRules.MaxRepeatItems))
        {
            var group = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            foreach (var child in n.Children) group.Children.Add(Render(child, data, item));
            stack.Children.Add(group);
        }
        return stack;
    }

    private static UIElement Table(ValidatedNode n, JsonElement data, JsonElement? scope)
    {
        var cols = n.Props.TryGetValue("columns", out var c) && c is List<ValidatedColumn> list
            ? list : new List<ValidatedColumn>();
        var arr = PathResolver.Resolve(data, Str(n, "fromPath"), scope);

        if (arr is null || arr.Value.ValueKind != JsonValueKind.Array || arr.Value.GetArrayLength() == 0)
            return Text(RenderRules.EmptyText(StrOrNull(n, "emptyText")), Grey, 13);

        var rows = arr.Value.EnumerateArray().Take(RenderRules.MaxTableRows).ToList();

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
        foreach (var _ in cols) grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var r = 0; r <= rows.Count; r++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        for (var ci = 0; ci < cols.Count; ci++)
        {
            var header = Text(cols[ci].Header, Amber, 12, bold: true);
            header.Margin = new Thickness(0, 0, 10, 4);
            Grid.SetRow(header, 0);
            Grid.SetColumn(header, ci);
            grid.Children.Add(header);
        }

        for (var ri = 0; ri < rows.Count; ri++)
        {
            for (var ci = 0; ci < cols.Count; ci++)
            {
                // Scope is the ROW, matching the proven renderer: a column path reaches the row via
                // "$item.field".
                var v = PathResolver.Resolve(data, cols[ci].ValuePath, rows[ri]);
                var cell = Text(PathResolver.Display(v), RenderRules.IsUnknown(v) ? UnknownBrush : Sky, 12);
                cell.Margin = new Thickness(0, 0, 10, 2);
                Grid.SetRow(cell, ri + 1);
                Grid.SetColumn(cell, ci);
                grid.Children.Add(cell);
            }
        }

        return grid;
    }

    private static UIElement Note(ValidatedNode n)
        => Text(Str(n, "text"), Tone(StrOrNull(n, "tone")), 13, margin: new Thickness(0, 2, 0, 6));

    // ---- primitives --------------------------------------------------------------------------------

    private static TextBlock Text(string text, Brush brush, double size, bool bold = false, Thickness? margin = null)
        => new()
        {
            Text = text,                       // inert: WPF does not parse markup from this property
            Foreground = brush,
            FontSize = size,
            FontFamily = Mono,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            Margin = margin ?? new Thickness(0)
        };

    private static string Str(ValidatedNode n, string key)
        => n.Props.TryGetValue(key, out var v) && v is string s ? s : string.Empty;

    private static string? StrOrNull(ValidatedNode n, string key)
        => n.Props.TryGetValue(key, out var v) && v is string s ? s : null;
}
